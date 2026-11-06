using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Observability;
using RouterPlus.Core.Providers;
using RouterPlus.Core.Security;
using RouterPlus.Core.Updates;
using RouterPlus.App;
using RouterPlus.App.Views;
using RouterPlus.Infrastructure.Chrome;
using RouterPlus.Infrastructure.Router;
using RouterPlus.Infrastructure.Diagnostics;
using RouterPlus.Infrastructure.Security;
using RouterPlus.Infrastructure.Services;
using RouterPlus.Infrastructure.Storage;
using RouterPlus.Infrastructure.Updates;
using RouterPlus.App.Services;

namespace RouterPlus.App.ViewModels;

public sealed partial class MainViewModel
{
    public async Task OpenSelectedGoogleLoginAsync()
    {
        var profile = SelectedProfile;
        if (profile is null)
        {
            StatusText = "Hãy chọn Chrome profile trước.";
            return;
        }

        try
        {
            await LaunchUrlAsync("https://accounts.google.com/");
            StatusText = $"Đã mở đăng nhập Google bằng profile {profile.Name}.";
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }

    public GoogleAutoLoginViewModel? CreateGoogleAutoLoginViewModel()
    {
        if (SelectedProfile is null)
        {
            StatusText = "Hãy chọn Chrome profile trước.";
            return null;
        }

        return new GoogleAutoLoginViewModel(SelectedProfile, _googleLoginVaultStore, _googleLoginAutomation, Profiles);
    }

    /// <summary>
    /// Run Google auto-login directly without showing dialog.
    /// Credentials are loaded from vault (single source of truth).
    /// </summary>
    public async Task RunGoogleAutoLoginDirectAsync()
    {
        if (SelectedProfile is null)
        {
            StatusText = "Hãy chọn Chrome profile trước.";
            return;
        }

        using var trace = TraceScope.Begin("MainViewModel", "GoogleAutoLoginDirect",
            new { profile_id = SelectedProfile.Id, profile_name = SelectedProfile.Name });

        try
        {
            StatusText = $"Đang tải thông tin đăng nhập cho {SelectedProfile.Name}...";

            // Get vault path
            var vaultPaths = new GoogleAccountVaultPaths();
            var vaultPath = vaultPaths.VaultPath;

            // Try to open vault with remembered key
            GoogleAccountVaultSession? session = null;
            try
            {
                session = await _googleLoginVaultStore.TryOpenRememberedAsync(vaultPath, CancellationToken.None);
            }
            catch
            {
                // Remembered key not available
            }

            if (session == null)
            {
                StatusText = $"❌ Vault chưa được mở khóa. Vui lòng mở Credentials Manager để thiết lập.";
                ObservabilityHub.Instance.LogEvent(LogLevel.Warning, "MainViewModel", "AutoLoginVaultLocked",
                    "Cannot auto-login: vault is locked", new { profile_id = SelectedProfile.Id });
                return;
            }

            await using (session)
            {
                // Find credential for this profile: stable Id first,
                // legacy name only when unambiguous.
                var credential = GoogleAccountCredentialResolver.Resolve(session.Vault, SelectedProfile, Profiles);

                if (credential == null)
                {
                    StatusText = $"❌ Không tìm thấy thông tin đăng nhập cho {SelectedProfile.Name}. Vui lòng thêm trong Credentials Manager.";
                    ObservabilityHub.Instance.LogEvent(LogLevel.Warning, "MainViewModel", "AutoLoginNoCredentials",
                        "Cannot auto-login: no credentials found", new { profile_id = SelectedProfile.Id, profile_name = SelectedProfile.Name });
                    return;
                }

                trace.LogCheckpoint("CredentialsLoaded", new { email = credential.Email, has_totp = !string.IsNullOrWhiteSpace(credential.TotpSecret) });

                StatusText = $"🚀 Đang đăng nhập Google với {credential.Email}...";

                ObservabilityHub.Instance.LogEvent(LogLevel.Info, "MainViewModel", "AutoLoginStarted",
                    "Starting direct auto-login from vault credentials",
                    new { profile_id = SelectedProfile.Id, email = credential.Email });

                // Run automation
                var result = await _googleLoginAutomation(SelectedProfile, credential, CancellationToken.None);

                ObservabilityHub.Instance.IncrementCounter("autologin.direct",
                    tags: new Dictionary<string, string> { ["result"] = result.Category.ToString() });

                // Update status based on result
                if (result.Category == GoogleLoginResultCategory.Success)
                {
                    StatusText = $"✓ Đăng nhập Google thành công cho {SelectedProfile.Name}";
                }
                else if (result.Category == GoogleLoginResultCategory.ManualInterventionRequired)
                {
                    StatusText = $"⚠ {SelectedProfile.Name}: Cần can thiệp thủ công - {result.Message}";
                }
                else
                {
                    StatusText = $"❌ {SelectedProfile.Name}: Đăng nhập thất bại - {result.Message ?? result.Category.ToString()}";
                }
            }
        }
        catch (Exception ex)
        {
            ObservabilityHub.Instance.LogError("MainViewModel", "AutoLoginDirectFailed", ex,
                new { profile_id = SelectedProfile.Id });
            StatusText = $"❌ Lỗi auto-login: {ex.Message}";
        }
    }

    private Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<GoogleLoginResult>> CreateDefaultGoogleLoginAutomation(bool minimized = false)
    {
        return async (profile, credential, cancellationToken) =>
        {
            var installation = _installation ?? throw new InvalidOperationException("Chrome installation not configured.");

            ChromeManagedSession? session = null;
            IGoogleLoginBrowser? browser = null;
            try
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Info,
                    "Security",
                    "GoogleAutoLoginStarted",
                    "Google auto-login started",
                    new { profile_directory = profile.DirectoryName });
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Info,
                    "Chrome",
                    "GoogleAutoLoginChromeLaunchRequested",
                    "Google auto-login Chrome launch requested",
                    new { });

                var settings = await _settingsStore.LoadAsync(cancellationToken);

                session = await _chromeLauncher.LaunchManagedAsync(
                    installation,
                    profile,
                    new Uri("https://accounts.google.com/"),
                    cancellationToken,
                    settings.UseOriginalProfileForAutoLogin,
                    minimized);

                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Info,
                    "Chrome",
                    "GoogleAutoLoginChromeLaunched",
                    "Google auto-login Chrome launched and CDP endpoint is available",
                    new { });

                browser = await session.ConnectGoogleLoginAsync(cancellationToken);

                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Info,
                    "Security",
                    "GoogleAutoLoginCdpConnected",
                    "Google auto-login CDP connected; checking session cookies",
                    new { });

                // Check if already logged in via session cookies
                var initialState = await browser.ReadStateAsync(cancellationToken);
                GoogleLoginResult result;

                if (initialState.HasCompletionSignal)
                {
                    result = GoogleLoginResult.Success();
                    ObservabilityHub.Instance.LogEvent(
                        LogLevel.Info,
                        "Security",
                        "GoogleAutoLoginSessionCookiesSuccess",
                        "Google auto-login: session cookies authenticated successfully",
                        new { });
                }
                else
                {
                    ObservabilityHub.Instance.LogEvent(
                        LogLevel.Info,
                        "Security",
                        "GoogleAutoLoginStateMachineStarting",
                        "Google auto-login: session cookies did not authenticate, starting state machine",
                        new { });
                    result = await _googleAuthenticationService.AuthenticateAsync(
                        new GoogleAuthenticationRequest(credential, browser),
                        cancellationToken);
                }

                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Info,
                    "Security",
                    "GoogleAutoLoginCompleted",
                    "Google auto-login state machine completed",
                    new { result_category = result.Category.ToString() });

                // Dispose session to close the profile after successful login
                if (result.Category is GoogleLoginResultCategory.Success)
                {
                    if (browser is not null)
                    {
                        await browser.DisposeAsync();
                        browser = null;
                    }
                    if (session is not null)
                    {
                        await session.DisposeAsync();
                        session = null;
                    }
                    return result;
                }

                // Keep the managed browser open for manual intervention
                if (result.Category is GoogleLoginResultCategory.ManualInterventionRequired)
                {
                    if (session is not null && browser is not null)
                    {
                        _googleLoginSessions.Add((session, browser));
                        session = null;
                        browser = null;
                    }
                    return result;
                }

                if (browser is not null)
                {
                    await browser.DisposeAsync();
                    browser = null;
                }
                if (session is not null)
                {
                    await session.DisposeAsync();
                    session = null;
                }

                return result;
            }
            catch (OperationCanceledException)
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Info,
                    "Security",
                    "GoogleAutoLoginCancelled",
                    "Google auto-login cancelled",
                    new { });
                if (browser is not null)
                {
                    await browser.DisposeAsync();
                }
                if (session is not null)
                {
                    await session.DisposeAsync();
                }
                return GoogleLoginResult.Cancelled();
            }
            catch (Exception ex)
            {
                ObservabilityHub.Instance.LogError(
                    "Security",
                    "GoogleAutoLoginFailed",
                    ex,
                    new { profile_directory = profile.DirectoryName });
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Error,
                    "MainViewModel",
                    "GoogleAutoLoginException",
                    "Google auto-login exception",
                    new {
                        exception_type = ex.GetType().Name,
                        message = ex.Message,
                        stack_trace = ex.StackTrace
                    });

                if (browser is not null)
                {
                    await browser.DisposeAsync();
                }
                if (session is not null)
                {
                    await session.DisposeAsync();
                }

                // Check for specific known errors
                if (ex is InvalidOperationException &&
                    ex.Message.Contains("selected profile may already be open", StringComparison.OrdinalIgnoreCase))
                {
                    return GoogleLoginResult.BrowserDisconnected(
                        "The selected Chrome profile is already open. Close all Chrome windows using this profile and retry.");
                }

                if (ex is TimeoutException)
                {
                    return GoogleLoginResult.Timeout();
                }

                return GoogleLoginResult.BrowserDisconnected($"{ex.GetType().Name}: {ex.Message}");
            }
        };
    }

    /// <summary>
    /// Creates Codex login automation for Credentials Manager.
    /// Supports Google OAuth (auto-consent) and Direct login.
    /// </summary>
    private Func<ChromeProfile, CodexLoginCredential, CancellationToken, Task<CodexLoginResult>> CreateDefaultCodexLoginAutomation()
    {
        return async (profile, credential, cancellationToken) =>
        {
            var installation = _installation ?? throw new InvalidOperationException("Chrome installation not configured.");

            ChromeManagedSession? session = null;
            CdpSession? cdp = null;

            try
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Info,
                    "Security",
                    "CodexLoginStarted",
                    "Codex login started",
                    new { profile_directory = profile.DirectoryName, method = credential.Method.ToString() });

                if (credential.Method == Core.Models.AuthMethod.GoogleOAuth)
                {
                    // Google OAuth: Launch Chrome and run OAuth automation
                    ObservabilityHub.Instance.LogEvent(
                        LogLevel.Info,
                        "Security",
                        "CodexOAuthLaunchingChrome",
                        "Launching Chrome for Codex OAuth",
                        new { });
                    var settings = await _settingsStore.LoadAsync(cancellationToken);

                    var codexOAuthUrl = new Uri("https://auth.openai.com/authorize?client_id=chatgpt-web&scope=openid%20profile%20email&response_type=code&redirect_uri=https%3A%2F%2Fchatgpt.com%2Fcodex");

                    session = await _chromeLauncher.LaunchManagedAsync(
                        installation,
                        profile,
                        codexOAuthUrl,
                        cancellationToken,
                        settings.UseOriginalProfileForAutoLogin);

                    cdp = await session.ConnectAnyTargetAsync(cancellationToken);

                    // Create Codex TOTP generator if secret provided
                    Func<Task<string?>>? codexTotpGenerator = null;
                    if (!string.IsNullOrWhiteSpace(credential.TotpSecret))
                    {
                        codexTotpGenerator = async () =>
                        {
                            var secret = credential.TotpSecret!;
                            var totp = GoogleTotpGenerator.Generate(secret, DateTimeOffset.UtcNow);
                            ObservabilityHub.Instance.LogEvent(
                                LogLevel.Debug,
                                "CodexLogin",
                                "CodexTotpGenerated",
                                "Codex TOTP code generated for OAuth flow",
                                new { });
                            return await Task.FromResult(totp);
                        };
                    }

                    // Run OAuth automation
                    var automation = new CodexOAuthAutomation(
                        cdp.Client,
                        cdp.SessionId,
                        cdp.TargetId,
                        credential.LinkedGoogleEmail ?? string.Empty,
                        codexTotpGenerator);

                    ObservabilityHub.Instance.LogEvent(
                        LogLevel.Info,
                        "Security",
                        "CodexOAuthAutomationStarting",
                        "Starting Codex OAuth automation",
                        new { });
                    var consentResult = await automation.WaitAndConsentAsync(
                        codexOAuthUrl,
                        timeout: TimeSpan.FromMinutes(3),
                        cancellationToken);

                    if (consentResult.Success)
                    {
                        ObservabilityHub.Instance.LogEvent(
                            LogLevel.Info,
                            "Security",
                            "CodexOAuthCompleted",
                            "Codex OAuth completed successfully",
                            new { });
                        return CodexLoginResult.Success();
                    }

                    ObservabilityHub.Instance.LogEvent(
                        LogLevel.Warning,
                        "Security",
                        "CodexOAuthFailed",
                        "Codex OAuth failed",
                        new { message = consentResult.Message });
                    return CodexLoginResult.Failed(consentResult.Message);
                }
                else // Direct login
                {
                    ObservabilityHub.Instance.LogEvent(
                        LogLevel.Info,
                        "CodexLogin",
                        "DirectLoginStarting",
                        "Starting Codex direct login with email/password",
                        new { email = credential.Email });

                    var settings = await _settingsStore.LoadAsync(cancellationToken);

                    // Navigate to OpenAI login page (not OAuth authorize)
                    var loginUrl = new Uri("https://chatgpt.com/auth/login");

                    session = await _chromeLauncher.LaunchManagedAsync(
                        installation,
                        profile,
                        loginUrl,
                        cancellationToken,
                        settings.UseOriginalProfileForAutoLogin);

                    cdp = await session.ConnectAnyTargetAsync(cancellationToken);

                    ObservabilityHub.Instance.LogEvent(
                        LogLevel.Debug,
                        "CodexLogin",
                        "ChromeLaunched",
                        "Chrome launched for direct login");

                    // Create TOTP generator if secret provided
                    Func<Task<string?>>? totpGenerator = null;
                    if (!string.IsNullOrWhiteSpace(credential.TotpSecret))
                    {
                        totpGenerator = async () =>
                        {
                            var secret = credential.TotpSecret!;
                            var totp = GoogleTotpGenerator.Generate(secret, DateTimeOffset.UtcNow);
                            ObservabilityHub.Instance.LogEvent(
                                LogLevel.Debug,
                                "CodexLogin",
                                "TotpGenerated",
                                "TOTP code generated for Codex login");
                            return await Task.FromResult(totp);
                        };
                    }

                    // Run direct login automation
                    var automation = new CodexDirectLoginAutomation(
                        cdp.Client,
                        cdp.SessionId,
                        cdp.TargetId,
                        credential.Email!,
                        credential.Password!,
                        totpGenerator);

                    ObservabilityHub.Instance.LogEvent(
                        LogLevel.Info,
                        "CodexLogin",
                        "DirectLoginAutomationStarted",
                        "Running direct login automation");

                    var directResult = await automation.RunAsync(
                        timeout: TimeSpan.FromMinutes(3),
                        cancellationToken);

                    if (directResult.Success)
                    {
                        ObservabilityHub.Instance.LogEvent(
                            LogLevel.Info,
                            "CodexLogin",
                            "DirectLoginSuccess",
                            "Codex direct login completed successfully");
                        return CodexLoginResult.Success();
                    }

                    ObservabilityHub.Instance.LogEvent(
                        LogLevel.Warning,
                        "CodexLogin",
                        "DirectLoginFailed",
                        "Codex direct login failed",
                        new { message = directResult.Message });
                    return CodexLoginResult.Failed(directResult.Message);
                }
            }
            catch (OperationCanceledException)
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Info,
                    "Security",
                    "CodexLoginCancelled",
                    "Codex login cancelled",
                    new { });
                return CodexLoginResult.Cancelled();
            }
            catch (Exception ex)
            {
                ObservabilityHub.Instance.LogError(
                    "Security",
                    "CodexLoginFailed",
                    ex,
                    new { method = credential.Method.ToString() });
                return CodexLoginResult.Failed($"{ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                if (cdp != null)
                {
                    await cdp.DisposeAsync();
                }
                if (session != null)
                {
                    await session.DisposeAsync();
                }
            }
        };
    }

    private Func<ChromeProfile, CancellationToken, Task<GoogleLoginCredential?>> CreateDefaultAutoGetKeyCredentials()
    {
        return (profile, cancellationToken) => LoadGoogleCredentialForProfileAsync(profile, cancellationToken);
    }
}

