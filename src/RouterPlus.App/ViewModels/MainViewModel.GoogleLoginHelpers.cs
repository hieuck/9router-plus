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

    private async Task<GoogleLoginCredential?> LoadGoogleCredentialForProfileAsync(
        ChromeProfile profile,
        CancellationToken cancellationToken)
    {
        if (_googleLoginVaultStore is null)
        {
            return null;
        }

        var vaultSession = await _googleLoginVaultStore.TryOpenRememberedAsync(
            _googleLoginVaultPaths.VaultPath,
            cancellationToken);
        if (vaultSession is null)
        {
            return null;
        }

        await using (vaultSession)
        {
            return GoogleAccountCredentialResolver.Resolve(vaultSession.Vault, profile, Profiles);
        }
    }

    /// <summary>
    /// Attempts to auto-login Google for the given profile before the provider OAuth flow.
    /// Runs once before the Nous Chrome session; Google cookies persist in the real profile.
    /// Never throws; if vault is locked/no credential/unsupported page, just continues.
    /// </summary>
    private async Task<GooglePreLoginResult> TryAutoGoogleLoginAsync(ChromeProfile profile, CancellationToken ct)
    {
        if (_installation is null)
        {
            return GooglePreLoginResult.Skipped;
        }

        GoogleLoginCredential? credential = null;
        try
        {
            credential = await LoadGoogleCredentialForProfileAsync(profile, ct);
        }
        catch (Exception ex)
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Warning,
                "Providers",
                "AutoGoogleLoginCredentialLoadFailed",
                "Failed to load Google credential for auto-login",
                new { profile = profile.Name, error = ex.Message });
            return GooglePreLoginResult.Failed;
        }
        if (credential is null || string.IsNullOrWhiteSpace(credential.Email) || string.IsNullOrWhiteSpace(credential.Password))
        {
            return GooglePreLoginResult.Skipped;
        }

        ChromeManagedSession? session = null;
        IGoogleLoginBrowser? browser = null;
        try
        {
            StatusText = $"Đang đăng nhập Google cho {profile.Name}…";
            session = await _chromeLauncher.LaunchManagedAsync(_installation, profile,
                new Uri("https://accounts.google.com/"), ct, useOriginalProfile: true);
            browser = await session.ConnectGoogleLoginAsync(ct);
            var state = await browser.ReadStateAsync(ct);
            if (state.HasCompletionSignal && state.PageUri.Host == "myaccount.google.com")
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Info,
                    "Providers",
                    "AutoGoogleLoginAlreadySignedIn",
                    "Already signed in via session cookies",
                    new { profile = profile.Name });
                await browser.DisposeAsync();
                browser = null;
                await session.DisposeAsync();
                session = null;
                return GooglePreLoginResult.Success;
            }
            var service = new GoogleAuthenticationService();
            var result = await service.AuthenticateAsync(new GoogleAuthenticationRequest(credential, browser), ct);
            if (result.Category == GoogleLoginResultCategory.Success)
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Info,
                    "Providers",
                    "AutoGoogleLoginSuccess",
                    "Auto Google login completed",
                    new { profile = profile.Name });
                await browser.DisposeAsync();
                browser = null;
                await session.DisposeAsync();
                session = null;
                return GooglePreLoginResult.Success;
            }

            if (result.Category == GoogleLoginResultCategory.ManualInterventionRequired)
            {
                // Keep the original profile open so the user can complete Google's verification.
                _googleLoginSessions.Add((session, browser));
                browser = null;
                session = null;
                return GooglePreLoginResult.ManualInterventionRequired;
            }

            ObservabilityHub.Instance.LogEvent(
                LogLevel.Warning,
                "Providers",
                "AutoGoogleLoginIncomplete",
                "Auto Google login did not complete",
                new { profile = profile.Name, category = result.Category.ToString(), message = result.Message });
            await browser.DisposeAsync();
            browser = null;
            await session.DisposeAsync();
            session = null;
            return GooglePreLoginResult.Failed;
        }
        catch (Exception ex)
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Warning,
                "Providers",
                "AutoGoogleLoginFailed",
                "Auto Google login failed, continuing with provider flow",
                new { profile = profile.Name, error = ex.Message });
            return GooglePreLoginResult.Failed;
        }
        finally
        {
            if (browser is not null)
            {
                try { await browser.DisposeAsync(); } catch { /* best effort */ }
            }
            if (session is not null)
            {
                try { await session.DisposeAsync(); } catch { /* best effort */ }
            }
        }
    }

    private Func<CancellationToken, Task<OpenRouterPkceResult>> CreateDefaultOpenRouterPkceFlow()
    {
        return async cancellationToken =>
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            await using var listener = await OAuthCallbackListener.StartAsync();
            var pkce = OpenRouterPkce.CreateS256Pair();
            var authUrl = OpenRouterPkce.BuildAuthorizationUrl(listener.RedirectUri, pkce.Challenge);
            await LaunchUrlAsync(authUrl.AbsoluteUri);
            var callback = await listener.WaitForCallbackAsync(TimeSpan.FromMinutes(10), cancellationToken);
            if (!OpenRouterPkce.TryGetAuthorizationCode(callback, out var code, out var errorMessage))
            {
                return OpenRouterPkceResult.Failed(errorMessage!);
            }

            return await OpenRouterPkce.ExchangeCodeForApiKeyAsync(
                http,
                code!,
                pkce.Verifier,
                cancellationToken);
        };
    }

    private Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<OpenRouterKeyFlowOrchestrator.OpenRouterKeyFlowResult>> CreateDefaultOpenRouterKeyFlow()
    {
        return async (profile, credential, cancellationToken) =>
        {
            var installation = _installation ?? throw new InvalidOperationException("Chrome installation not configured.");

            ChromeManagedSession? session = null;
            try
            {
                var settings = await _settingsStore.LoadAsync(cancellationToken);
                session = await _chromeLauncher.LaunchManagedAsync(
                    installation,
                    profile,
                    new Uri("https://openrouter.ai/settings/keys"),
                    cancellationToken,
                    settings.UseOriginalProfileForAutoLogin);

                // The session connects both adapters (OpenRouter page + Google sign-in)
                // from one CDP session behind the public interface pair.
                var (onboarding, googleLogin) = await session.ConnectOpenRouterFlowAsync(cancellationToken);
                return await OpenRouterKeyFlowOrchestrator.RunAsync(
                    onboarding,
                    null,
                    credential,
                    googleLogin,
                    cancellationToken,
                    _googleAuthenticationService);
            }
            catch (OperationCanceledException)
            {
                return new OpenRouterKeyFlowOrchestrator.OpenRouterKeyFlowResult(false, null, "Đã hủy.");
            }
            catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
            {
                return new OpenRouterKeyFlowOrchestrator.OpenRouterKeyFlowResult(false, null, ex.Message);
            }
            finally
            {
                if (session is not null)
                {
                    await session.DisposeAsync();
                }
            }
        };
    }

    private Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<OllamaKeyFlowOrchestrator.OllamaKeyFlowResult>> CreateDefaultOllamaKeyFlow()
    {
        return async (profile, credential, cancellationToken) =>
        {
            var installation = _installation ?? throw new InvalidOperationException("Chrome installation not configured.");

            ChromeManagedSession? session = null;
            try
            {
                var settings = await _settingsStore.LoadAsync(cancellationToken);
                session = await _chromeLauncher.LaunchManagedAsync(
                    installation,
                    profile,
                    new Uri("https://ollama.com/settings/keys"),
                    cancellationToken,
                    settings.UseOriginalProfileForAutoLogin);

                var (ollamaPage, googleLogin) = await session.ConnectOllamaFlowAsync(cancellationToken);
                return await OllamaKeyFlowOrchestrator.RunAsync(
                    ollamaPage,
                    credential,
                    googleLogin,
                    cancellationToken,
                    _googleAuthenticationService);
            }
            catch (OperationCanceledException)
            {
                return new OllamaKeyFlowOrchestrator.OllamaKeyFlowResult(false, null, "Đã hủy.");
            }
            catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
            {
                return new OllamaKeyFlowOrchestrator.OllamaKeyFlowResult(false, null, ex.Message);
            }
            finally
            {
                if (session is not null)
                {
                    await session.DisposeAsync();
                }
            }
        };
    }

    private Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<AgnesKeyFlowOrchestrator.AgnesKeyFlowResult>> CreateDefaultAgnesKeyFlow()
    {
        return async (profile, credential, cancellationToken) =>
        {
            var installation = _installation ?? throw new InvalidOperationException("Chrome installation not configured.");

            ChromeManagedSession? session = null;
            try
            {
                var settings = await _settingsStore.LoadAsync(cancellationToken);
                session = await _chromeLauncher.LaunchManagedAsync(
                    installation,
                    profile,
                    new Uri("https://platform.agnes-ai.com/settings/apiKeys"),
                    cancellationToken,
                    settings.UseOriginalProfileForAutoLogin);

                var (agnesPage, connectGooglePopup) = await session.ConnectAgnesFlowAsync(cancellationToken);
                return await AgnesKeyFlowOrchestrator.RunAsync(
                    agnesPage,
                    credential,
                    connectGooglePopup,
                    cancellationToken,
                    _googleAuthenticationService);
            }
            catch (OperationCanceledException)
            {
                return new AgnesKeyFlowOrchestrator.AgnesKeyFlowResult(false, null, "Đã hủy.");
            }
            catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
            {
                return new AgnesKeyFlowOrchestrator.AgnesKeyFlowResult(false, null, ex.Message);
            }
            finally
            {
                if (session is not null)
                {
                    await session.DisposeAsync();
                }
            }
        };
    }

    private Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<PoolsideKeyFlowOrchestrator.PoolsideKeyFlowResult>> CreateDefaultPoolsideKeyFlow()
    {
        return async (profile, credential, cancellationToken) =>
        {
            var installation = _installation ?? throw new InvalidOperationException("Chrome installation not configured.");

            ChromeManagedSession? session = null;
            try
            {
                var settings = await _settingsStore.LoadAsync(cancellationToken);
                session = await _chromeLauncher.LaunchManagedAsync(
                    installation,
                    profile,
                    new Uri("https://platform.poolside.ai/api-keys"),
                    cancellationToken,
                    settings.UseOriginalProfileForAutoLogin);

                var (poolsidePage, googleLogin) = await session.ConnectPoolsideFlowAsync(cancellationToken);
                return await PoolsideKeyFlowOrchestrator.RunAsync(
                    poolsidePage,
                    credential,
                    googleLogin,
                    cancellationToken,
                    _googleAuthenticationService);
            }
            catch (OperationCanceledException)
            {
                return new PoolsideKeyFlowOrchestrator.PoolsideKeyFlowResult(false, null, "Đã hủy.");
            }
            catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
            {
                return new PoolsideKeyFlowOrchestrator.PoolsideKeyFlowResult(false, null, ex.Message);
            }
            finally
            {
                if (session is not null)
                {
                    await session.DisposeAsync();
                }
            }
        };
    }

    private Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<BazaarlinkKeyFlowOrchestrator.BazaarlinkKeyFlowResult>> CreateDefaultBazaarlinkKeyFlow()
    {
        return async (profile, credential, cancellationToken) =>
        {
            var installation = _installation ?? throw new InvalidOperationException("Chrome installation not configured.");

            ChromeManagedSession? session = null;
            try
            {
                var settings = await _settingsStore.LoadAsync(cancellationToken);
                session = await _chromeLauncher.LaunchManagedAsync(
                    installation,
                    profile,
                    new Uri("https://bazaarlink.ai/keys"),
                    cancellationToken,
                    settings.UseOriginalProfileForAutoLogin);

                var (bazaarlinkPage, googleLogin) = await session.ConnectBazaarlinkFlowAsync(cancellationToken);
                return await BazaarlinkKeyFlowOrchestrator.RunAsync(
                    bazaarlinkPage,
                    credential,
                    googleLogin,
                    cancellationToken,
                    _googleAuthenticationService);
            }
            catch (OperationCanceledException)
            {
                return new BazaarlinkKeyFlowOrchestrator.BazaarlinkKeyFlowResult(false, null, "Đã hủy.");
            }
            catch (Exception ex) when (ex is InvalidOperationException or TimeoutException)
            {
                return new BazaarlinkKeyFlowOrchestrator.BazaarlinkKeyFlowResult(false, null, ex.Message);
            }
            finally
            {
                if (session is not null)
                {
                    await session.DisposeAsync();
                }
            }
        };
    }

}