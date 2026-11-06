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
    public async Task DeleteSelectedProfileAsync()
    {
        var profile = SelectedProfile;
        if (profile is null)
        {
            StatusText = "Hãy chọn Chrome profile trước.";
            return;
        }

        var removedManagedProfiles = _profileManager.ManagedProfiles
            .Where(managedProfile => IsManagedProfileFor(managedProfile, profile))
            .ToArray();

        // Managed-only guard: delete is only offered for managed profiles in the UI.
        // Non-managed profiles are intentionally rejected here to avoid deleting
        // Chrome directories that the app did not provision.
        if (removedManagedProfiles.Length == 0)
        {
            StatusText = "Profile này không phải profile được quản lý.";
            return;
        }

        try
        {
            // Use the discovered installation root so a stale VM setting cannot
            // redirect deletion outside the real Chrome User Data directory.
            var deletionRoot = _installation?.UserDataDirectory ?? ChromeUserDataDirectory;
            _profileDeleter.Validate(profile, deletionRoot);

            var previousManaged = _profileManager.ManagedProfiles.ToArray();
            _profileManager.RemoveManaged(profile);
            try
            {
                await _settingsStore.SaveAsync(BuildSettings());
            }
            catch
            {
                _profileManager.LoadManaged(previousManaged);
                throw;
            }

            try
            {
                await _profileDeleter.DeleteAsync(profile, deletionRoot, ChromeExecutablePath);
            }
            catch (Exception deleteException)
            {
                // Restore managed state and best-effort persist it again so the
                // profile can be retried; the orphan directory remains on disk.
                _profileManager.LoadManaged(previousManaged);
                try
                {
                    await _settingsStore.SaveAsync(BuildSettings());
                }
                catch
                {
                    // Best-effort; in-memory state is already restored.
                }
                AppendLog("ERROR", $"Profile deletion failed: {SafeError(deleteException)}");
                StatusText = $"Xóa profile thất bại: {deleteException.Message}";
                RefreshProfiles();
                return;
            }

            RefreshProfiles();
            StatusText = $"Đã xóa profile {profile.Name}.";
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }

    private void TrackProfileLaunch(ChromeProfile profile)
    {
        _profileManager.TrackLaunch(profile);
        UpdateRecentProfilesList();
    }

    public AsyncRelayCommand<ChromeProfile> TogglePinProfileCommand { get; }

    private async Task TogglePinProfileAsync(ChromeProfile? profile)
    {
        if (profile is null) return;

        var recent = _profileManager.RecentProfiles.FirstOrDefault(r =>
            r.ProfileId == profile.Id &&
            r.UserDataDirectory.Equals(profile.UserDataDirectory, StringComparison.OrdinalIgnoreCase));

        if (recent != null)
        {
            _profileManager.PinRecent(profile, !recent.IsPinned);
            UpdateRecentProfilesList();
            await SaveSettingsAsync();
        }
    }

    private void UpdateRecentProfilesList()
    {
        RecentProfileRows.Clear();
        var slot = 0;
        foreach (var recent in _profileManager.RecentProfiles.Take(MaxRecentSlots))
        {
            var profile = Profiles.FirstOrDefault(p =>
                p.Id == recent.ProfileId &&
                p.UserDataDirectory.Equals(recent.UserDataDirectory, StringComparison.OrdinalIgnoreCase));
            if (profile is null)
            {
                continue;
            }
            RecentProfileRows.Add(new RecentProfileRowViewModel(recent, profile, slot));
            slot++;
        }
        ClearRecentProfilesCommand.RaiseCanExecuteChanged();
        RebuildQuickLaunchProfiles();
    }

    internal async Task ClearRecentProfilesAsync()
    {
        if (_profileManager.RecentProfiles.Count == 0) return;
        _profileManager.ClearRecent();
        UpdateRecentProfilesList();
        StatusText = "Đã xoá danh sách profile dùng gần đây.";
        await SaveSettingsAsync();
    }

    internal Task OpenQuickLaunchPalette()
    {
        if (!IsQuickLaunchFeatureEnabled)
        {
            return Task.CompletedTask;
        }

        if (Profiles.Count == 0)
        {
            StatusText = "Chưa có Chrome profile để hiển thị Quick Launch.";
            return Task.CompletedTask;
        }
        QuickLaunchFilterText = string.Empty;
        SelectedQuickLaunchProfile = FilteredQuickLaunchProfiles.FirstOrDefault();
        IsQuickLaunchOpen = true;
        QuickLaunchVisibilityRequested?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    internal Task CloseQuickLaunchPalette()
    {
        IsQuickLaunchOpen = false;
        QuickLaunchFilterText = string.Empty;
        return Task.CompletedTask;
    }

    public event EventHandler? QuickLaunchVisibilityRequested;

    private void RebuildQuickLaunchProfiles()
    {
        FilteredQuickLaunchProfiles.Clear();
        var filter = QuickLaunchFilterText?.Trim() ?? string.Empty;
        IEnumerable<ChromeProfile> source = Profiles;
        if (!string.IsNullOrEmpty(filter))
        {
            source = source.Where(p => p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }
        foreach (var profile in source.Take(MaxQuickLaunchResults))
        {
            FilteredQuickLaunchProfiles.Add(profile);
        }
        SelectedQuickLaunchProfile = FilteredQuickLaunchProfiles.FirstOrDefault();
    }

    internal Task MoveQuickLaunchSelection(int delta)
    {
        if (FilteredQuickLaunchProfiles.Count == 0)
        {
            SelectedQuickLaunchProfile = null;
            return Task.CompletedTask;
        }
        var currentIndex = SelectedQuickLaunchProfile is null
            ? -1
            : FilteredQuickLaunchProfiles.IndexOf(SelectedQuickLaunchProfile);
        var count = FilteredQuickLaunchProfiles.Count;
        var nextIndex = ((currentIndex + delta) % count + count) % count;
        SelectedQuickLaunchProfile = FilteredQuickLaunchProfiles[nextIndex];
        return Task.CompletedTask;
    }

    private async Task ConfirmQuickLaunchSelectionAsync(ChromeProfile? profile)
    {
        var target = profile ?? SelectedQuickLaunchProfile;
        if (target is null)
        {
            await CloseQuickLaunchPalette();
            return;
        }
        await LaunchProfileAsync(target);
        await CloseQuickLaunchPalette();
    }

    private async Task LaunchSelectedProfileAsync()
    {
        if (SelectedProfile is null)
        {
            StatusText = "Hãy chọn Chrome profile trước.";
            return;
        }

        try
        {
            TrackProfileLaunch(SelectedProfile);
            await LaunchUrlAsync(DashboardBaseUrl);
            StatusText = $"Đã mở 9Router bằng profile {SelectedProfile.Name}.";
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }

    private async Task OpenProviderDashboardAsync(ProviderKind provider)
    {
        if (SelectedProfile is null)
        {
            StatusText = "Hãy chọn Chrome profile trước.";
            return;
        }

        try
        {
            var definition = ProviderCatalog.Get(provider);
            await LaunchUrlAsync(definition.BuildDashboardUrl(DashboardBaseUrl));
            StatusText = $"Đã mở dashboard {definition.DisplayName} cho profile {SelectedProfile.Name}.";
            ShowToast(StatusText, ToastType.Success);
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }

    private async Task TestConnectionAsync(ProviderKind provider)
    {
        var profile = SelectedProfile;
        if (profile is null)
        {
            StatusText = "Select a Chrome profile first.";
            return;
        }

        try
        {
            var definition = ProviderCatalog.Get(provider);
            var api = await CreateApiClientAsync();
            var matchingConnections = (await api.ListConnectionsAsync(provider, CancellationToken.None, includeQuotas: false))
                .Where(connection => string.Equals(
                    connection.Name?.Trim(),
                    profile.Name.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (matchingConnections.Length == 0)
            {
                StatusText = $"No {definition.DisplayName} connection found for profile {profile.Name}.";
                ShowToast(StatusText, ToastType.Warning, 5);
                return;
            }

            var failedConnectionCount = 0;
            foreach (var connection in matchingConnections)
            {
                var result = await api.TestConnectionAsync(connection.Id);
                if (!result.Valid)
                {
                    failedConnectionCount++;
                }
            }

            if (failedConnectionCount == 0)
            {
                StatusText = $"Test connection succeeded for {definition.DisplayName} on profile {profile.Name}.";
                ShowToast(StatusText, ToastType.Success);
            }
            else
            {
                StatusText = $"Test connection failed for {definition.DisplayName} ({failedConnectionCount} connection(s)).";
                ShowToast(StatusText, ToastType.Warning, 5);
            }
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }

    private async Task DeleteConnectionAsync(ProviderKind provider)
    {
        var profile = SelectedProfile;
        if (profile is null)
        {
            StatusText = "Hãy chọn Chrome profile trước.";
            ShowToast(StatusText, ToastType.Warning);
            return;
        }

        try
        {
            var definition = ProviderCatalog.Get(provider);
            var api = await CreateApiClientAsync();
            var matchingConnections = (await api.ListConnectionsAsync(provider, CancellationToken.None, includeQuotas: false))
                .Where(connection => string.Equals(
                    connection.Name?.Trim(),
                    profile.Name.Trim(),
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (matchingConnections.Length == 0)
            {
                StatusText = $"Không tìm thấy connection {definition.DisplayName} cho profile {profile.Name}.";
                ShowToast(StatusText, ToastType.Warning, 5);
                return;
            }

            var connectionList = matchingConnections.Length == 1
                ? $"connection {matchingConnections[0].Email ?? matchingConnections[0].Id}"
                : $"{matchingConnections.Length} connections";

            var result = System.Windows.MessageBox.Show(
                $"Bạn có chắc muốn xóa {connectionList} của {definition.DisplayName} cho profile {profile.Name}?\n\n" +
                "Thao tác này không thể hoàn tác.",
                "Xác nhận xóa connection",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning,
                System.Windows.MessageBoxResult.No);

            if (result != System.Windows.MessageBoxResult.Yes)
            {
                StatusText = "Đã hủy xóa connection.";
                return;
            }

            var deletedCount = 0;
            foreach (var connection in matchingConnections)
            {
                await api.DeleteConnectionAsync(connection.Id);
                deletedCount++;

                // Delete saved API key if exists
                if (definition.Workflow == WorkflowKind.ApiKey)
                {
                    try
                    {
                        await _secretVault.RemoveAsync(ProfileSecretKey.Create(profile, provider));
                    }
                    catch
                    {
                        // Ignore vault deletion errors
                    }
                }
            }

            await RefreshConnectionStatusesAsync(showStatus: false, forceLog: true);
            StatusText = $"Đã xóa {deletedCount} connection {definition.DisplayName} cho profile {profile.Name}.";
            ShowToast(StatusText, ToastType.Success, 5);
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }

    private Task LaunchUrlAsync(string url)
    {
        if (SelectedProfile is null)
        {
            throw new InvalidOperationException("Select a Chrome profile first.");
        }

        return _launchUrl(SelectedProfile, url);
    }
    /// <summary>
    /// Phase 6 Step 6.2: Helper method demonstrating AutoLoginOrchestrator usage.
    /// This method shows how to integrate the orchestrator for unified auto-login with fallback support.
    ///
    /// Future batch login implementation will call this method for each profile.
    /// </summary>
    private async Task<AutoLoginResult> RunAutoLoginWithOrchestratorAsync(
        ChromeProfile profile,
        ProviderKind provider,
        Uri startUri,
        CancellationToken cancellationToken)
    {
        if (_installation is null)
        {
            throw new InvalidOperationException("Chrome installation not configured");
        }

        ChromeLauncherAdapter? adapter = null;
        try
        {
            // Create adapter for this profile
            adapter = new ChromeLauncherAdapter(_chromeLauncher, _installation, profile);

            // Create orchestrator
            var orchestrator = new AutoLoginOrchestrator(
                (GoogleAccountVaultStore)_googleLoginVaultStore,
                _providerConnectionVaultStore,
                adapter);

            // Run auto-login with fallback support
            var result = await orchestrator.LoginAsync(
                profile.Name,
                provider,
                startUri,
                TimeSpan.FromMinutes(2),
                cancellationToken);

            return result;
        }
        finally
        {
            // Cleanup Chrome session
            if (adapter != null)
            {
                await adapter.CleanupAsync();
            }
        }
    }

    private async Task<IRouterApiClient> CreateApiClientAsync()
    {
        // 9Router uses JWT cookie authentication, not Basic Auth
        // If password is provided, we need to login to get the JWT token cookie
        if (!string.IsNullOrWhiteSpace(DashboardAuthPassword))
        {
            var handler = new HttpClientHandler
            {
                CookieContainer = new System.Net.CookieContainer(),
                UseCookies = true
            };
            var authClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };

            // Login to 9Router to get JWT token in cookie
            await LoginTo9RouterAsync(authClient, DashboardAuthPassword.Trim());

            return new RouterApiClient(authClient, DashboardBaseUrl);
        }

        return new RouterApiClient(_httpClient, DashboardBaseUrl);
    }

    private async Task LoginTo9RouterAsync(HttpClient client, string password)
    {
        try
        {
            var loginPayload = new { password };
            var loginUri = new Uri(new Uri(DashboardBaseUrl), "api/auth/login");
            var response = await client.PostAsync(
                loginUri,
                JsonContent.Create(loginPayload),
                CancellationToken.None);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Warning,
                    "Providers",
                    "NineRouterLoginFailed",
                    "9Router login failed",
                    new { error });
            }
        }
        catch (Exception ex)
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Warning,
                "Providers",
                "NineRouterLoginError",
                "9Router login error",
                new { error_message = ex.Message });
        }
    }

    private static bool IsManagedProfileFor(ManagedChromeProfile managedProfile, ChromeProfile profile) =>
        string.Equals(managedProfile.DirectoryName.Trim(), profile.DirectoryName.Trim(), StringComparison.OrdinalIgnoreCase)
        && PathsEqual(managedProfile.UserDataDirectory, profile.UserDataDirectory);

    private static bool PathsEqual(string left, string right)
    {
        var leftPath = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rightPath = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(leftPath, rightPath, StringComparison.OrdinalIgnoreCase);
    }

    private RouterSettings BuildSettings(WindowPlacement? windowPlacement = null)
    {
        var placement = windowPlacement ?? _savedWindowPlacement;
        return new(
        DashboardBaseUrl.Trim(),
        string.IsNullOrWhiteSpace(DashboardAuthPassword) ? null : DashboardAuthPassword.Trim(),
        string.IsNullOrWhiteSpace(ChromeExecutablePath) ? null : ChromeExecutablePath.Trim(),
        string.IsNullOrWhiteSpace(ChromeUserDataDirectory) ? null : ChromeUserDataDirectory.Trim(),
        FontScale,
        UseLightTheme,
        UseOriginalProfileForAutoLogin,
        _profileManager.ManagedProfiles.ToArray(),
        placement?.Left,
        placement?.Top,
        placement?.Width,
        placement?.Height,
        _profileManager.RecentProfiles.ToArray(),
        _quotaAutoDisableMarkers);    }

    private static WindowPlacement? TryCreateWindowPlacement(
        double? left,
        double? top,
        double? width,
        double? height) =>
        left is { } leftValue
        && top is { } topValue
        && width is { } widthValue
        && height is { } heightValue
        && IsValidWindowPlacement(leftValue, topValue, widthValue, heightValue)
            ? new WindowPlacement(leftValue, topValue, widthValue, heightValue)
            : null;

    private static bool IsValidWindowPlacement(double left, double top, double width, double height) =>
        double.IsFinite(left)
        && double.IsFinite(top)
        && double.IsFinite(width)
        && double.IsFinite(height)
        && width > 0d
        && height > 0d;

    private void SetError(Exception exception, [CallerMemberName] string? operation = null)
    {
        StatusText = SafeError(exception);
        var operationName = string.IsNullOrWhiteSpace(operation) ? "Thao tác" : operation;
        AppendLog("ERROR", $"{operationName}: {SafeErrorLog(exception)}");
        ShowToast(SafeError(exception), ToastType.Error, 5);
    }

    private void SetStatusText(string value, string level, bool forceLog)
    {
        if (!forceLog && string.Equals(_statusText, value, StringComparison.Ordinal))
        {
            return;
        }

        _statusText = value;
        AppendLog(level, value);
        OnPropertyChanged(nameof(StatusText));
    }

    private bool CanSaveSettings() => !HasSettingsValidationError;

    private bool SettingsMatchSavedValues() =>
        string.Equals(DashboardBaseUrl.Trim(), _savedDashboardBaseUrl, StringComparison.Ordinal)
        && string.Equals(DashboardAuthPassword?.Trim(), _savedDashboardAuthPassword, StringComparison.Ordinal)
        && string.Equals(ChromeExecutablePath.Trim(), _savedChromeExecutablePath, StringComparison.Ordinal)
        && string.Equals(ChromeUserDataDirectory.Trim(), _savedChromeUserDataDirectory, StringComparison.Ordinal)
        && Math.Abs(FontScale - _savedFontScale) < 0.001d
        && UseLightTheme == _savedUseLightTheme
        && UseOriginalProfileForAutoLogin == _savedUseOriginalProfileForAutoLogin;

    private string GetSettingsValidationMessage()
    {
        var dashboardBaseUrl = DashboardBaseUrl.Trim();
        if (!Uri.TryCreate(dashboardBaseUrl, UriKind.Absolute, out var dashboardUri)
            || dashboardUri is null
            || (dashboardUri.Scheme != Uri.UriSchemeHttp && dashboardUri.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(dashboardUri.Host))
        {
            return "Nhập URL dashboard hợp lệ.";
        }

        var chromeExecutablePath = ChromeExecutablePath.Trim();
        if (!string.IsNullOrWhiteSpace(chromeExecutablePath) && !File.Exists(chromeExecutablePath))
        {
            return "Không tìm thấy file Chrome đã chọn.";
        }

        var chromeUserDataDirectory = ChromeUserDataDirectory.Trim();
        if (!string.IsNullOrWhiteSpace(chromeUserDataDirectory) && !Directory.Exists(chromeUserDataDirectory))
        {
            return "Không tìm thấy thư mục dữ liệu Chrome đã chọn.";
        }

        return string.Empty;
    }

    private void MarkSettingsSaved()
    {
        _savedDashboardBaseUrl = DashboardBaseUrl.Trim();
        _savedDashboardAuthPassword = DashboardAuthPassword?.Trim();
        _savedChromeExecutablePath = ChromeExecutablePath.Trim();
        _savedChromeUserDataDirectory = ChromeUserDataDirectory.Trim();
        _savedFontScale = FontScale;
        _savedUseLightTheme = UseLightTheme;
        _savedUseOriginalProfileForAutoLogin = UseOriginalProfileForAutoLogin;
        NotifySettingsStateChanged();
    }

    private void NotifySettingsStateChanged()
    {
        OnPropertyChanged(nameof(HasUnsavedSettings));
        OnPropertyChanged(nameof(SettingsValidationMessage));
        OnPropertyChanged(nameof(HasSettingsValidationError));
        OnPropertyChanged(nameof(SettingsStatusText));
        OnPropertyChanged(nameof(IsDashboardUrlValid));
        OnPropertyChanged(nameof(IsChromeExecutableValid));
        OnPropertyChanged(nameof(IsChromeUserDataValid));
        SaveSettingsCommand?.RaiseCanExecuteChanged();
        InstallUpdateCommand?.RaiseCanExecuteChanged();
    }

    private void AppendLog(string level, string message)
    {
        _logEntries.Enqueue($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}");
        while (_logEntries.Count > MaxLogEntries)
        {
            _logEntries.Dequeue();
        }

        LogText = string.Join(Environment.NewLine, _logEntries);
    }

    private static string SafeError(Exception exception) => exception switch
    {
        RouterApiException apiException => $"9Router từ chối yêu cầu (HTTP {(int)apiException.StatusCode}). Kiểm tra dashboard URL và trạng thái 9Router.",
        TimeoutException => "Thao tác hết thời gian chờ. Hãy thử lại.",
        FileNotFoundException => "Không tìm thấy file cần thiết. Kiểm tra lại đường dẫn Chrome.",
        DirectoryNotFoundException => "Không tìm thấy thư mục Chrome profile.",
        _ => "Thao tác thất bại. Kiểm tra cài đặt rồi thử lại."
    };

    private static string SafeErrorLog(Exception exception) => exception switch
    {
        RouterApiException apiException => $"yêu cầu 9Router thất bại (HTTP {(int)apiException.StatusCode}).",
        TimeoutException => "thao tác hết thời gian chờ.",
        FileNotFoundException => "không tìm thấy file cần thiết.",
        DirectoryNotFoundException => "không tìm thấy thư mục Chrome profile.",
        _ => "thao tác thất bại."
    };
}

