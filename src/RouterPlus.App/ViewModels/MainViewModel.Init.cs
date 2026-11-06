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
    public async Task InitializeAsync()
    {
        Task initializationTask;
        lock (_initializationLock)
        {
            initializationTask = _initializationTask = InitializeCoreAsync();
        }

        await initializationTask;
    }

    private async Task InitializeCoreAsync()
    {
        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "Startup",
            "MainViewModelInitStarted",
            "MainViewModel initialization started",
            null);

        try
        {
            var settings = await _settingsStore.LoadAsync();
            _quotaAutoDisableMarkers = settings.QuotaAutoDisableMarkers ?? [];
            _quotaMarkersLoaded = true;
            DashboardBaseUrl = settings.DashboardBaseUrl;
            DashboardAuthPassword = settings.DashboardAuthPassword;
            try
            {
                await _settingsStore.MigrateDashboardPasswordAsync();
            }
            catch (Exception migrationException)
            {
                AppendLog("WARN", $"Dashboard password migration failed: {SafeError(migrationException)}");
            }
            ChromeExecutablePath = settings.ChromeExecutablePath ?? string.Empty;
            ChromeUserDataDirectory = settings.ChromeUserDataDirectory ?? string.Empty;
            FontScale = settings.FontScale;
            UseLightTheme = settings.UseLightTheme;
            UseOriginalProfileForAutoLogin = settings.UseOriginalProfileForAutoLogin;
            _savedWindowPlacement = TryCreateWindowPlacement(
                settings.WindowLeft,
                settings.WindowTop,
                settings.WindowWidth,
                settings.WindowHeight);
            _profileManager.LoadManaged(settings.ManagedProfiles);
            _profileManager.LoadRecent(settings.RecentProfiles);

            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "Startup",
                "SettingsLoaded",
                "Application settings loaded",
                new
                {
                    chrome_path = ChromeExecutablePath,
                    user_data_dir = ChromeUserDataDirectory,
                    managed_profiles = _profileManager.ManagedProfiles.Count,
                    recent_profiles = _profileManager.RecentProfiles.Count,
                    font_scale = FontScale,
                    light_theme = UseLightTheme
                });

            // Load Google account vault for health checks
            await LoadGoogleAccountVaultAsync();

            RefreshProfiles();
            MarkSettingsSaved();
            if (!_harnessMode)
            {
                await LoadSelectedProfileApiKeysAsync();
            }
            StatusText = Profiles.Count == 0
                ? "Chưa tìm thấy Chrome profile. Hãy kiểm tra đường dẫn rồi nhấn Làm mới."
                : $"Đã đọc {Profiles.Count} Chrome profile.";
            if (_harnessMode)
            {
                ConnectionStatusText = "Harness mode: provider sync disabled.";
            }
            else
            {
                await RefreshConnectionStatusesAsync(showStatus: true, forceLog: true);
            }
            _isInitialized = true;
            _initializationCompletion.TrySetResult(true);

            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "Startup",
                "MainViewModelInitCompleted",
                "MainViewModel initialization completed",
                new
                {
                    profile_count = Profiles.Count,
                    harness_mode = _harnessMode
                });

            if (_runStartupUpdateCheck)
            {
                _ = RunStartupUpdateCheckAsync();
            }
        }
        catch (Exception exception)
        {
            ObservabilityHub.Instance.LogError(
                "Startup",
                "MainViewModelInitFailed",
                exception,
                new { chrome_path = ChromeExecutablePath, user_data_dir = ChromeUserDataDirectory });

            SetError(exception);
        }
    }

    private async Task LoadGoogleAccountVaultAsync()
    {
        try
        {
            var vaultSession = await _googleLoginVaultStore.TryOpenRememberedAsync(
                _googleLoginVaultPaths.VaultPath,
                CancellationToken.None);

            if (vaultSession is not null)
            {
                await using (vaultSession)
                {
                    _googleAccountVault = vaultSession.Vault;
                }
            }
        }
        catch (Exception ex)
        {
            // Vault loading is optional - don't fail initialization
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Warning,
                "Startup",
                "VaultLoadFailed",
                "Could not load Google account vault for health checks",
                new { error_message = ex.Message });
        }
        finally
        {
            // Always initialize ProfileHealthService with loaded vault (or null)
            _profileHealthService = new ProfileHealthService(_googleAccountVault);
        }
    }

    private async Task RunStartupUpdateCheckAsync()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastAutomaticUpdateCheck < StartupUpdateCheckCooldown)
        {
            return;
        }

        _lastAutomaticUpdateCheck = now;
        await CheckForUpdatesAsync();
    }

    public Task OpenHelpAsync()
    {
        _linkLauncher.Open(ApplicationLinks.HelpUri);
        return Task.CompletedTask;
    }

    public Task OpenSecurityAsync()
    {
        _linkLauncher.Open(ApplicationLinks.SecurityUri);
        return Task.CompletedTask;
    }

    public Task OpenReleasePageAsync()
    {
        _linkLauncher.Open(ApplicationLinks.ReleaseUri);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Check health status for all profiles.
    /// </summary>
    private async Task CheckAllProfilesHealthAsync()
    {
        if (_profileHealthService == null) return;

        foreach (var row in ProfileRows)
        {
            var status = await _profileHealthService.GetHealthStatusAsync(
                row.Profile,
                forceRefresh: true);
            row.HealthStatus = status;

            // Small delay to avoid overwhelming UI
            await Task.Delay(50);
        }
    }

    /// <summary>
    /// Check health status for a single profile by testing actual Google login.
    /// </summary>
    private async Task CheckProfileHealthAsync(ProfileRowViewModel? row)
    {
        if (row == null) return;

        try
        {
            row.IsCheckingHealth = true;
            StatusText = $"⟳ Checking health for {row.Name}...";

            using var trace = TraceScope.Begin("MainViewModel", "CheckHealth",
                new { profile = row.Name, profile_id = row.Profile.Id });

            // Open vault to get credentials (same as auto-login)
            GoogleAccountVaultSession? session = null;
            try
            {
                session = await _googleLoginVaultStore.TryOpenRememberedAsync(
                    _googleLoginVaultPaths.VaultPath,
                    CancellationToken.None);
            }
            catch
            {
                // Remembered key not available
            }

            if (session == null)
            {
                StatusText = $"❌ {row.Name}: Vault not unlocked. Open Credentials Manager first.";
                ShowToast(StatusText, ToastType.Error);
                return;
            }

            await using (session)
            {
                // Find credentials for this profile (stable Id, unique legacy name only)
                var credential = GoogleAccountCredentialResolver.Resolve(session.Vault, row.Profile, Profiles);
                if (credential == null)
                {
                    ObservabilityHub.Instance.LogEvent(LogLevel.Warning, "MainViewModel", "CheckHealthNoCredentials",
                        "No credentials found for profile in health check",
                        new { profile = row.Name, profile_id = row.Profile.Id });

                    ObservabilityHub.Instance.IncrementCounter("profile.health_check.no_credentials",
                        tags: new Dictionary<string, string> { ["profile_id"] = row.Profile.Id });

                    var status = ProfileHealthStatus.FromIssues(new[]
                    {
                        HealthIssue.Warning(HealthCategory.Credentials,
                            "No Google credentials saved for this profile",
                            "Add credentials in Credentials Manager")
                    });
                    row.HealthStatus = status;
                    StatusText = $"⚠ {row.Name}: No credentials configured";
                    ShowToast(StatusText, ToastType.Warning);
                    return;
                }

                // Run actual Google login to verify credentials
                var result = await _googleLoginAutomation(row.Profile, credential, CancellationToken.None);

                // Map login result to health status
                var healthResult = MapLoginResultToHealthStatus(result);
                row.HealthStatus = healthResult;

                // Log metrics
                ObservabilityHub.Instance.IncrementCounter("profile.health_check.completed",
                    tags: new Dictionary<string, string>
                    {
                        ["status"] = healthResult.Level.ToString(),
                        ["result"] = result.Category.ToString()
                    });

                // Show result
                ShowHealthCheckResult(row, healthResult);
            }
        }
        catch (Exception ex)
        {
            ObservabilityHub.Instance.LogError(
                "Chrome",
                "HealthCheckFailed",
                ex,
                new { profile_name = row.Name });
            var status = ProfileHealthStatus.FromIssues(new[]
            {
                HealthIssue.Error(HealthCategory.Credentials, ex.Message, null)
            });
            row.HealthStatus = status;
            StatusText = $"❌ {row.Name}: Health check failed - {ex.Message}";
            ShowToast(StatusText, ToastType.Error);

            ObservabilityHub.Instance.IncrementCounter("profile.health_check.exception",
                tags: new Dictionary<string, string> { ["error_type"] = ex.GetType().Name });
        }
        finally
        {
            row.IsCheckingHealth = false;
        }
    }

    /// <summary>
    /// Map Google login result to profile health status.
    /// </summary>
    private static ProfileHealthStatus MapLoginResultToHealthStatus(GoogleLoginResult loginResult)
    {
        return loginResult.Category switch
        {
            GoogleLoginResultCategory.Success =>
                ProfileHealthStatus.Healthy("Credentials are valid and login successful"),

            GoogleLoginResultCategory.InvalidCredentials =>
                ProfileHealthStatus.FromIssues(new[]
                {
                    HealthIssue.Error(HealthCategory.Credentials,
                        "Invalid email, password, or TOTP code",
                        "Update credentials in Credentials Manager")
                }),

            GoogleLoginResultCategory.ManualInterventionRequired =>
                ProfileHealthStatus.FromIssues(new[]
                {
                    HealthIssue.Warning(HealthCategory.Credentials,
                        loginResult.Message,
                        "Manual intervention may be required")
                }),

            GoogleLoginResultCategory.Timeout =>
                ProfileHealthStatus.FromIssues(new[]
                {
                    HealthIssue.Error(HealthCategory.Credentials,
                        "Login attempt timed out",
                        "Check network connection or try again")
                }),

            GoogleLoginResultCategory.BrowserDisconnected =>
                ProfileHealthStatus.FromIssues(new[]
                {
                    HealthIssue.Error(HealthCategory.Credentials,
                        "Browser disconnected during health check",
                        null)
                }),

            GoogleLoginResultCategory.UnsupportedPage =>
                ProfileHealthStatus.FromIssues(new[]
                {
                    HealthIssue.Warning(HealthCategory.Credentials,
                        loginResult.Message,
                        "Google may have changed their login flow")
                }),

            _ =>
                ProfileHealthStatus.FromIssues(new[]
                {
                    HealthIssue.Error(HealthCategory.Credentials,
                        $"Unknown login result: {loginResult.Category}",
                        null)
                })
        };
    }

    /// <summary>
    /// Show health check result message to user.
    /// </summary>
    private void ShowHealthCheckResult(ProfileRowViewModel row, ProfileHealthStatus status)
    {
        var icon = status.Level switch
        {
            HealthLevel.Healthy => "✓",
            HealthLevel.Warning => "⚠",
            HealthLevel.Error => "✗",
            _ => "?"
        };

        var toastType = status.Level switch
        {
            HealthLevel.Healthy => ToastType.Success,
            HealthLevel.Warning => ToastType.Warning,
            HealthLevel.Error => ToastType.Error,
            _ => ToastType.Info
        };

        StatusText = status.Level switch
        {
            HealthLevel.Healthy => $"{icon} {row.Name}: {status.Message}",
            HealthLevel.Warning => $"{icon} {row.Name}: {status.Issues.Count} warning(s) found",
            HealthLevel.Error => $"{icon} {row.Name}: {status.Issues.Count} error(s) found",
            _ => $"{icon} {row.Name}: Health status unknown"
        };

        ShowToast(StatusText, toastType);
    }

}