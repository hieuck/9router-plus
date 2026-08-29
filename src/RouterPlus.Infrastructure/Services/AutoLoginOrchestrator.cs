using System.Text.Json;
using RouterPlus.Core.Models;
using RouterPlus.Core.Providers;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Chrome;
using RouterPlus.Infrastructure.Diagnostics;
using RouterPlus.Infrastructure.Security;

namespace RouterPlus.Infrastructure.Services;

/// <summary>
/// Unified auto-login orchestrator with fallback support.
/// Routes between Google OAuth and Direct login based on provider connection config.
/// </summary>
public sealed class AutoLoginOrchestrator
{
    private readonly GoogleAccountVaultStore _googleAccountVault;
    private readonly ProviderConnectionVaultStore _connectionVault;
    private readonly IChromeLauncher _chromeLauncher;

    public AutoLoginOrchestrator(
        GoogleAccountVaultStore googleAccountVault,
        ProviderConnectionVaultStore connectionVault,
        IChromeLauncher chromeLauncher)
    {
        _googleAccountVault = googleAccountVault ?? throw new ArgumentNullException(nameof(googleAccountVault));
        _connectionVault = connectionVault ?? throw new ArgumentNullException(nameof(connectionVault));
        _chromeLauncher = chromeLauncher ?? throw new ArgumentNullException(nameof(chromeLauncher));
    }

    /// <summary>
    /// Attempt auto-login for a profile + provider.
    /// Tries preferred method first, falls back to alternative if available.
    /// </summary>
    public async Task<AutoLoginResult> LoginAsync(
        string profileName,
        ProviderKind provider,
        Uri startUri,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(startUri);

        // Get connection config
        var connection = await _connectionVault.GetConnectionAsync(profileName, provider, cancellationToken);
        if (connection == null)
        {
            return new AutoLoginResult(
                Success: false,
                Method: null,
                ErrorMessage: "No credentials configured for this profile and provider");
        }

        // Determine which method to try first
        var primaryMethod = connection.PreferredMethod;
        var fallbackMethod = primaryMethod == AuthMethod.GoogleOAuth ? AuthMethod.Direct : AuthMethod.GoogleOAuth;

        // Try primary method
        DebugConsole.WriteLine($"[AutoLogin] Trying primary method: {primaryMethod}");
        var primaryResult = await TryMethodAsync(connection, primaryMethod, provider, startUri, timeout, cancellationToken);
        if (primaryResult.Success)
        {
            return primaryResult;
        }

        DebugConsole.WriteLine($"[AutoLogin] Primary method failed: {primaryResult.ErrorMessage}");

        // Try fallback if alternative credentials exist
        var fallbackAvailable = primaryMethod == AuthMethod.GoogleOAuth
            ? connection.DirectCredential != null
            : !string.IsNullOrEmpty(connection.LinkedGoogleAccount);

        if (!fallbackAvailable)
        {
            return primaryResult;
        }

        DebugConsole.WriteLine($"[AutoLogin] Attempting fallback: {fallbackMethod}");
        var fallbackResult = await TryMethodAsync(connection, fallbackMethod, provider, startUri, timeout, cancellationToken);
        return fallbackResult.Success ? fallbackResult : primaryResult;
    }

    private async Task<AutoLoginResult> TryMethodAsync(
        ProviderAuthConnection connection,
        AuthMethod method,
        ProviderKind provider,
        Uri startUri,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        return method switch
        {
            AuthMethod.GoogleOAuth => await TryGoogleOAuthAsync(connection, provider, startUri, timeout, cancellationToken),
            AuthMethod.Direct => await TryDirectLoginAsync(connection, provider, startUri, timeout, cancellationToken),
            _ => new AutoLoginResult(false, null, $"Unknown auth method: {method}")
        };
    }

    private async Task<AutoLoginResult> TryGoogleOAuthAsync(
        ProviderAuthConnection connection,
        ProviderKind provider,
        Uri startUri,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(connection.LinkedGoogleAccount))
        {
            return new AutoLoginResult(false, AuthMethod.GoogleOAuth, "No Google account linked");
        }

        // Get vault path
        var vaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "9RouterPlus", "google-accounts.vault");

        // Try to open with remembered key first
        GoogleAccountVaultSession? session = null;
        try
        {
            session = await _googleAccountVault.TryOpenRememberedAsync(vaultPath, cancellationToken);
        }
        catch
        {
            // Remembered key not available, will try password below
        }

        if (session == null)
        {
            return new AutoLoginResult(false, AuthMethod.GoogleOAuth, "Google vault not unlocked. Open Google Auto Login first to unlock.");
        }

        await using (session)
        {
            var googleAccount = session.Vault.Records
                .FirstOrDefault(c => string.Equals(c.Email, connection.LinkedGoogleAccount, StringComparison.OrdinalIgnoreCase));

            if (googleAccount == null)
            {
                return new AutoLoginResult(false, AuthMethod.GoogleOAuth, $"Google account '{connection.LinkedGoogleAccount}' not found in vault");
            }

            return await RunGoogleOAuthAsync(connection, provider, googleAccount, startUri, timeout, cancellationToken);
        }
    }

    private async Task<AutoLoginResult> RunGoogleOAuthAsync(
        ProviderAuthConnection connection,
        ProviderKind provider,
        GoogleLoginCredential googleAccount,
        Uri startUri,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        // Create TOTP generator if secret exists
        Func<Task<string?>>? totpGenerator = null;
        if (!string.IsNullOrEmpty(googleAccount.TotpSecret))
        {
            var totpSecret = googleAccount.TotpSecret;
            totpGenerator = () => Task.FromResult<string?>(GoogleTotpGenerator.Generate(totpSecret, DateTimeOffset.UtcNow));
        }

        // Launch browser
        var loginUrl = GetProviderLoginUrl(provider, startUri);
        var cdp = await _chromeLauncher.LaunchAsync(connection.ProfileName, loginUrl, cancellationToken);
        if (cdp == null)
        {
            return new AutoLoginResult(false, AuthMethod.GoogleOAuth, "Failed to launch browser");
        }

        try
        {
            // Create provider-specific OAuth automation
            GoogleOAuthFlowAutomation automation = provider switch
            {
                ProviderKind.Kiro => new AwsBuilderIdOAuthAutomation(
                    cdp.Client, cdp.SessionId, cdp.TargetId, connection.ProfileName, totpGenerator),
                ProviderKind.Codex => new CodexOAuthAutomation(
                    cdp.Client, cdp.SessionId, cdp.TargetId, connection.ProfileName),
                ProviderKind.GitHub => new GitHubOAuthAutomation(
                    cdp.Client, cdp.SessionId, cdp.TargetId, connection.ProfileName),
                ProviderKind.OpenRouter => new OpenRouterOAuthAutomation(
                    cdp.Client, cdp.SessionId, cdp.TargetId, connection.ProfileName),
                _ => throw new NotSupportedException($"Google OAuth not supported for provider {provider}")
            };

            var result = await automation.WaitAndConsentAsync(loginUrl, timeout, cancellationToken);
            return new AutoLoginResult(
                Success: result.Success,
                Method: AuthMethod.GoogleOAuth,
                ErrorMessage: result.Success ? null : result.Message);
        }
        finally
        {
            await cdp.DisposeAsync();
        }
    }

    private async Task<AutoLoginResult> TryDirectLoginAsync(
        ProviderAuthConnection connection,
        ProviderKind provider,
        Uri startUri,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        if (connection.DirectCredential == null)
        {
            return new AutoLoginResult(false, AuthMethod.Direct, "No direct credentials");
        }

        var creds = connection.DirectCredential;

        // Create TOTP generator if secret exists
        Func<Task<string?>>? totpGenerator = null;
        if (!string.IsNullOrEmpty(creds.TotpSecret))
        {
            var totpSecret = creds.TotpSecret;
            totpGenerator = () => Task.FromResult<string?>(GoogleTotpGenerator.Generate(totpSecret, DateTimeOffset.UtcNow));
        }

        // Launch browser
        var loginUrl = GetProviderLoginUrl(provider, startUri);
        var cdp = await _chromeLauncher.LaunchAsync(connection.ProfileName, loginUrl, cancellationToken);
        if (cdp == null)
        {
            return new AutoLoginResult(false, AuthMethod.Direct, "Failed to launch browser");
        }

        try
        {
            // Create provider-specific direct login automation
            DirectLoginAutomation automation = provider switch
            {
                ProviderKind.GitHub => new GitHubDirectLoginAutomation(
                    cdp.Client, cdp.SessionId, cdp.TargetId, creds.Email, creds.Password, totpGenerator),
                ProviderKind.OpenRouter => new OpenRouterDirectLoginAutomation(
                    cdp.Client, cdp.SessionId, cdp.TargetId, creds.Email, creds.Password, totpGenerator),
                ProviderKind.Codex => new CodexDirectLoginAutomation(
                    cdp.Client, cdp.SessionId, cdp.TargetId, creds.Email, creds.Password, totpGenerator),
                ProviderKind.Kiro => new KiroDirectLoginAutomation(
                    cdp.Client, cdp.SessionId, cdp.TargetId, creds.Email, creds.Password, totpGenerator),
                _ => throw new NotSupportedException($"Direct login not supported for provider {provider}")
            };

            var result = await automation.RunAsync(timeout, cancellationToken);
            return new AutoLoginResult(
                Success: result.Success,
                Method: AuthMethod.Direct,
                ErrorMessage: result.Success ? null : result.Message);
        }
        finally
        {
            await cdp.DisposeAsync();
        }
    }

    private Uri GetProviderLoginUrl(ProviderKind provider, Uri fallback)
    {
        return provider switch
        {
            ProviderKind.Codex => new Uri("https://chatgpt.com/"),
            ProviderKind.Kiro => new Uri("https://kiro.dev/"),
            ProviderKind.GitHub => new Uri("https://github.com/login"),
            ProviderKind.OpenRouter => new Uri("https://openrouter.ai/"),
            _ => fallback
        };
    }
}

/// <summary>
/// Result of auto-login attempt.
/// </summary>
public sealed record AutoLoginResult(
    bool Success,
    AuthMethod? Method,
    string? ErrorMessage);

/// <summary>
/// Interface for Chrome launcher (decoupled from concrete implementation).
/// </summary>
public interface IChromeLauncher
{
    Task<CdpSession?> LaunchAsync(string profileName, Uri loginUrl, CancellationToken cancellationToken);
}
