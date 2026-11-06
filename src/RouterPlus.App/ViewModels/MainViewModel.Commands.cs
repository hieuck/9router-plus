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
    public bool IsWorkflowInProgress => _providerWorkflowService.IsInProgress;

    public bool IsUpdateChecking
    {
        get => _isUpdateChecking;
        private set
        {
            if (_isUpdateChecking == value)
            {
                return;
            }

            _isUpdateChecking = value;
            OnPropertyChanged();
            CheckForUpdatesCommand?.RaiseCanExecuteChanged();
            InstallUpdateCommand?.RaiseCanExecuteChanged();
        }
    }

    public bool IsUpdateAvailable => _latestRelease?.IsUpdateAvailable == true;

    public ReleaseVersion? AvailableVersion => _latestRelease?.AvailableVersion;

    public UpdateState UpdateState
    {
        get => _updateState;
        private set
        {
            if (_updateState == value)
            {
                return;
            }

            _updateState = value;
            OnPropertyChanged();
            InstallUpdateCommand?.RaiseCanExecuteChanged();
        }
    }

    public string UpdateStatusText
    {
        get => _updateStatusText;
        private set
        {
            if (string.Equals(_updateStatusText, value, StringComparison.Ordinal))
            {
                return;
            }

            _updateStatusText = value;
            OnPropertyChanged();
        }
    }

    public bool CanInstallUpdate =>
        IsUpdateAvailable
        && !_isUpdateChecking
        && !_isUpdateInstalling
        && !IsWorkflowInProgress
        && !HasUnsavedSettings
        && !HasSettingsValidationError
        && _updateService.IsInstallSupported;

    public IReadOnlyList<FontScaleOption> FontScaleOptions { get; } =
    [
        new(0.9d, "90%"),
        new(1d, "100%"),
        new(1.1d, "110%"),
        new(1.25d, "125%"),
        new(1.4d, "140%")
    ];

    public IReadOnlyList<ThemeOption> ThemeOptions { get; } =
    [
        new(false, "Tối"),
        new(true, "Sáng")
    ];

    public double FontScale
    {
        get => _fontScale;
        set
        {
            var next = Math.Clamp(value, 0.9d, 1.4d);
            if (Math.Abs(_fontScale - next) < 0.001d)
            {
                return;
            }

            _fontScale = next;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FontScaleLabel));
            NotifySettingsStateChanged();
        }
    }

    public string FontScaleLabel => $"{Math.Round(FontScale * 100):0}%";

    public bool UseLightTheme
    {
        get => _useLightTheme;
        set
        {
            if (_useLightTheme == value)
            {
                return;
            }

            _useLightTheme = value;
            ThemeManager.Apply(value);
            OnPropertyChanged();
            NotifySettingsStateChanged();
        }
    }

    public bool UseOriginalProfileForAutoLogin
    {
        get => _useOriginalProfileForAutoLogin;
        set
        {
            if (_useOriginalProfileForAutoLogin == value)
            {
                return;
            }

            _useOriginalProfileForAutoLogin = value;
            OnPropertyChanged();
            NotifySettingsStateChanged();
        }
    }

    public sealed record FontScaleOption(double Value, string Label)
    {
        public override string ToString() => Label;
    }

    public sealed record ThemeOption(bool Value, string Label)
    {
        public override string ToString() => Label;
    }

    public sealed record WindowPlacement(double Left, double Top, double Width, double Height);

    public WindowPlacement? SavedWindowPlacement => _savedWindowPlacement;

    public void LoadWindowPlacementSync()
    {
        try
        {
            var settings = _settingsStore.Load();
            _savedWindowPlacement = TryCreateWindowPlacement(
                settings.WindowLeft,
                settings.WindowTop,
                settings.WindowWidth,
                settings.WindowHeight);
        }
        catch
        {
            // Silently ignore errors during sync load
        }
    }

    public ProviderKind SelectedApiKeyProvider
    {
        get => _selectedApiKeyProvider;
        set
        {
            if (_selectedApiKeyProvider == value)
            {
                return;
            }

            _selectedApiKeyProvider = value;
            OnPropertyChanged();
        }
    }

    public string DashboardBaseUrl
    {
        get => _dashboardBaseUrl;
        set
        {
            if (string.Equals(_dashboardBaseUrl, value, StringComparison.Ordinal))
            {
                return;
            }

            _dashboardBaseUrl = value;
            OnPropertyChanged();
            NotifySettingsStateChanged();
        }
    }

    public string? DashboardAuthPassword
    {
        get => _dashboardAuthPassword;
        set
        {
            if (string.Equals(_dashboardAuthPassword, value, StringComparison.Ordinal))
            {
                return;
            }

            _dashboardAuthPassword = value;
            OnPropertyChanged();
            NotifySettingsStateChanged();
        }
    }

    public string ChromeExecutablePath
    {
        get => _chromeExecutablePath;
        set
        {
            if (string.Equals(_chromeExecutablePath, value, StringComparison.Ordinal))
            {
                return;
            }

            _chromeExecutablePath = value;
            OnPropertyChanged();
            NotifySettingsStateChanged();
        }
    }

    public string ChromeUserDataDirectory
    {
        get => _chromeUserDataDirectory;
        set
        {
            if (string.Equals(_chromeUserDataDirectory, value, StringComparison.Ordinal))
            {
                return;
            }

            _chromeUserDataDirectory = value;
            OnPropertyChanged();
            NotifySettingsStateChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetStatusText(value, "INFO", forceLog: false);
    }

    public bool IsDashboardUrlValid
    {
        get
        {
            var url = DashboardBaseUrl.Trim();
            return Uri.TryCreate(url, UriKind.Absolute, out var uri)
                && uri != null
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                && !string.IsNullOrWhiteSpace(uri.Host);
        }
    }

    public bool IsChromeExecutableValid
    {
        get
        {
            var path = ChromeExecutablePath.Trim();
            return string.IsNullOrWhiteSpace(path) || File.Exists(path);
        }
    }

    public bool IsChromeUserDataValid
    {
        get
        {
            var path = ChromeUserDataDirectory.Trim();
            return string.IsNullOrWhiteSpace(path) || Directory.Exists(path);
        }
    }

    public AsyncRelayCommand AutoDetectChromeCommand { get; }

    public AsyncRelayCommand ClearDashboardUrlCommand { get; }
    public AsyncRelayCommand ClearChromeExecutableCommand { get; }
    public AsyncRelayCommand ClearChromeUserDataCommand { get; }

    public AsyncRelayCommand ResetSettingsCommand { get; }

    public RelayCommand ToggleAppearanceSectionCommand { get; }
    public RelayCommand ToggleDashboardSectionCommand { get; }
    public RelayCommand ToggleChromeSectionCommand { get; }

    private void ToggleAppearanceSection()
    {
        IsAppearanceSectionExpanded = !IsAppearanceSectionExpanded;
    }

    private void ToggleDashboardSection()
    {
        IsDashboardSectionExpanded = !IsDashboardSectionExpanded;
    }

    private void ToggleChromeSection()
    {
        IsChromeSectionExpanded = !IsChromeSectionExpanded;
    }

    private async Task ResetSettingsAsync()
    {
        var result = System.Windows.MessageBox.Show(
            "Bạn có chắc muốn khôi phục tất cả cài đặt về giá trị mặc định?\n\nDanh sách profile được quản lý sẽ được giữ lại.",
            "Khôi phục cài đặt gốc",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question,
            System.Windows.MessageBoxResult.No);

        if (result != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        DashboardBaseUrl = "http://localhost:20128";
        ChromeExecutablePath = string.Empty;
        ChromeUserDataDirectory = string.Empty;
        FontScale = 1.0d;
        UseLightTheme = true;

        // Save to file so wizard appears on next app start
        await SaveSettingsAsync();

        StatusText = "Đã khôi phục cài đặt về mặc định.";
    }

    private async Task ClearDashboardUrl(){await Task.CompletedTask;
        DashboardBaseUrl = "http://localhost:20128";
    }

    private async Task ClearChromeExecutable(){await Task.CompletedTask;
        ChromeExecutablePath = string.Empty;
    }

    private async Task ClearChromeUserData(){await Task.CompletedTask;
        ChromeUserDataDirectory = string.Empty;
    }

    private async Task AutoDetectChromeAsync()
    {
        try
        {
            AppendLog("INFO", "Auto-detect: searching for Chrome installations...");
            var installations = _profileManager.FindAllInstallations();
            AppendLog("INFO", $"Auto-detect: found {installations.Count} installation(s).");

            foreach (var inst in installations)
            {
                AppendLog("INFO", $"  - {inst.ExecutablePath}");
                AppendLog("INFO", $"    User Data: {inst.UserDataDirectory}");
            }

            if (installations.Count == 0)
            {
                StatusText = "Không tìm thấy Chrome. Vui lòng chọn thủ công.";
                return;
            }

            if (installations.Count == 1)
            {
                var installation = installations[0];
                ChromeExecutablePath = installation.ExecutablePath;
                ChromeUserDataDirectory = installation.UserDataDirectory;
                StatusText = "Đã tự động phát hiện Chrome.";
                RefreshProfiles();
                return;
            }

            AppendLog("INFO", $"Auto-detect: showing selection dialog for {installations.Count} installations...");
            var dialog = new ChromeSelectionDialog(installations)
            {
                Owner = System.Windows.Application.Current.MainWindow
            };

            var dialogResult = dialog.ShowDialog();
            AppendLog("INFO", $"Auto-detect: dialog result = {dialogResult}");

            if (dialogResult == true && dialog.Result != null)
            {
                ChromeExecutablePath = dialog.Result.ExecutablePath;
                ChromeUserDataDirectory = dialog.Result.UserDataDirectory;
                StatusText = $"Đã chọn Chrome: {dialog.Result.ExecutablePath}";
                RefreshProfiles();
            }
            else
            {
                StatusText = "Đã hủy chọn Chrome.";
            }
        }
        catch (Exception ex)
        {
            AppendLog("ERROR", $"Auto-detect error: {SafeError(ex)}");
            StatusText = $"Lỗi auto-detect: {SafeError(ex)}";
        }
        await Task.CompletedTask;
    }

    public bool HasUnsavedSettings => !SettingsMatchSavedValues();

    public string SettingsValidationMessage => GetSettingsValidationMessage();

    public bool HasSettingsValidationError => !string.IsNullOrWhiteSpace(SettingsValidationMessage);

    public string SettingsStatusText => HasSettingsValidationError
        ? SettingsValidationMessage
        : HasUnsavedSettings
            ? "Có thay đổi chưa lưu"
            : "Đã lưu";

    public string LogText
    {
        get => _logText;
        private set
        {
            if (string.Equals(_logText, value, StringComparison.Ordinal))
            {
                return;
            }

            _logText = value;
            OnPropertyChanged();
        }
    }

    public ToastNotification? CurrentToast
    {
        get => _currentToast;
        private set
        {
            if (_currentToast == value) return;
            _currentToast?.Hide();
            _currentToast = value;
            OnPropertyChanged();
        }
    }

    public IReadOnlyList<QuotaResetSuggestion> QuotaResetSuggestions => _quotaResetSuggestions;

    internal async Task InitializeQuotaAutoDisableMarkersForTestAsync()
    {
        await LoadQuotaAutoDisableMarkersAsync();
    }

    public async Task<bool> ReenableQuotaConnectionAsync(
        string connectionId,
        bool confirmedByUser,
        CancellationToken cancellationToken = default)
    {
        if (!confirmedByUser || string.IsNullOrWhiteSpace(connectionId))
        {
            return false;
        }

        var marker = _quotaAutoDisableMarkers.FirstOrDefault(item =>
            string.Equals(item.ConnectionId, connectionId, StringComparison.Ordinal));
        if (marker is null)
        {
            return false;
        }

        try
        {
            var api = await CreateApiClientAsync();
            var current = (await api.ListAllConnectionsAsync(cancellationToken, includeQuotas: true))
                .FirstOrDefault(connection => string.Equals(connection.Id, connectionId, StringComparison.Ordinal));
            if (current is null || current.Provider != marker.Provider || current.IsActive)
            {
                RemoveQuotaResetSuggestion(connectionId);
                return false;
            }

            var resetAt = current.QuotaRows.FirstOrDefault(quota => quota.ResetAt.HasValue)?.ResetAt;
            if (marker.ResetAt is not { } markerResetAt
                || resetAt is not { } currentResetAt
                || markerResetAt > DateTimeOffset.UtcNow
                || currentResetAt < markerResetAt
                || !QuotaAutoDisablePolicy.HasRecovered(current))
            {
                StatusText = $"Connection {marker.Name ?? connectionId} chưa đủ điều kiện bật lại; quota có thể đã hết lại hoặc chưa có reset time đáng tin cậy.";
                ShowToast(StatusText, ToastType.Warning, 5);
                return false;
            }

            await api.UpdateConnectionAsync(
                connectionId,
                isActive: true,
                cancellationToken: cancellationToken);
            _quotaAutoDisableMarkers = _quotaAutoDisableMarkers
                .Where(item => !string.Equals(item.ConnectionId, connectionId, StringComparison.Ordinal))
                .ToArray();
            await SaveQuotaAutoDisableMarkersAsync(cancellationToken);
            RemoveQuotaResetSuggestion(connectionId);
            await RefreshConnectionStatusesAsync(showStatus: false, forceLog: true, cancellationToken: cancellationToken);
            StatusText = $"Đã bật lại connection {marker.Name ?? connectionId}.";
            ShowToast(StatusText, ToastType.Success);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            SetError(exception);
            return false;
        }
    }

    private async Task LoadQuotaAutoDisableMarkersAsync()
    {
        if (_quotaMarkersLoaded)
        {
            return;
        }

        try
        {
            var settings = await _settingsStore.LoadAsync();
            _quotaAutoDisableMarkers = settings.QuotaAutoDisableMarkers ?? [];
        }
        catch (Exception exception)
        {
            AppendLog("WARN", $"Không thể đọc marker quota: {SafeError(exception)}");
            _quotaAutoDisableMarkers = [];
        }

        _quotaMarkersLoaded = true;
        OnPropertyChanged(nameof(QuotaResetSuggestions));
    }

    private async Task SaveQuotaAutoDisableMarkersAsync(CancellationToken cancellationToken = default)
    {
        await _settingsStore.UpdateQuotaAutoDisableMarkersAsync(_quotaAutoDisableMarkers, cancellationToken);
    }

    private void RemoveQuotaResetSuggestion(string connectionId)
    {
        var suggestion = _quotaResetSuggestions.FirstOrDefault(item =>
            string.Equals(item.ConnectionId, connectionId, StringComparison.Ordinal));
        if (suggestion is not null)
        {
            _quotaResetSuggestions.Remove(suggestion);
        }
    }

    private void UpsertQuotaAutoDisableMarker(ProviderConnection connection)
    {
        var marker = new QuotaAutoDisableMarker(
            connection.Id,
            connection.Provider,
            connection.Name,
            connection.QuotaRows.FirstOrDefault(quota => quota.ResetAt.HasValue)?.ResetAt);
        _quotaAutoDisableMarkers = _quotaAutoDisableMarkers
            .Where(item => !string.Equals(item.ConnectionId, connection.Id, StringComparison.Ordinal))
            .Append(marker)
            .ToArray();
    }

    private async Task UpdateQuotaResetSuggestionsAsync(
        IReadOnlyList<ProviderConnection> connections,
        CancellationToken cancellationToken)
    {
        var byId = connections.ToDictionary(connection => connection.Id, StringComparer.Ordinal);
        HashSet<string> previousSuggestionIds;
        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            previousSuggestionIds = dispatcher.Invoke(() => _quotaResetSuggestions
                .Select(suggestion => suggestion.ConnectionId)
                .ToHashSet(StringComparer.Ordinal));
        }
        else
        {
            previousSuggestionIds = _quotaResetSuggestions
                .Select(suggestion => suggestion.ConnectionId)
                .ToHashSet(StringComparer.Ordinal);
        }
        var now = DateTimeOffset.UtcNow;
        var nextSuggestions = new List<QuotaResetSuggestion>();
        var nextMarkers = new List<QuotaAutoDisableMarker>();
        foreach (var marker in _quotaAutoDisableMarkers)
        {
            if (!byId.TryGetValue(marker.ConnectionId, out var connection))
            {
                nextMarkers.Add(marker);
                continue;
            }

            if (connection.IsActive || connection.Provider != marker.Provider)
            {
                continue;
            }

            nextMarkers.Add(marker);
            if (marker.ResetAt is { } resetAt && resetAt <= now && QuotaAutoDisablePolicy.HasRecovered(connection))
            {
                nextSuggestions.Add(new QuotaResetSuggestion(
                    marker.ConnectionId,
                    marker.Provider,
                    connection.Name ?? marker.Name,
                    resetAt));
            }
        }

        var markersChanged = !_quotaAutoDisableMarkers.SequenceEqual(nextMarkers);
        _quotaAutoDisableMarkers = nextMarkers;
        var distinctSuggestions = nextSuggestions
            .GroupBy(item => item.ConnectionId, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
        if (System.Windows.Application.Current?.Dispatcher is { } suggestionDispatcher)
        {
            suggestionDispatcher.Invoke(() =>
            {
                _quotaResetSuggestions.Clear();
                foreach (var suggestion in distinctSuggestions)
                {
                    _quotaResetSuggestions.Add(suggestion);
                }
            });
        }
        else
        {
            _quotaResetSuggestions.Clear();
            foreach (var suggestion in distinctSuggestions)
            {
                _quotaResetSuggestions.Add(suggestion);
            }
        }

        if (markersChanged)
        {
            await SaveQuotaAutoDisableMarkersAsync(cancellationToken);
        }

        OnPropertyChanged(nameof(QuotaResetSuggestions));
        var newSuggestion = distinctSuggestions.FirstOrDefault(suggestion =>
            !previousSuggestionIds.Contains(suggestion.ConnectionId));
        if (newSuggestion is not null)
        {
            ShowToast(newSuggestion.Message, ToastType.Info, 6);
        }
    }

    private void ShowToast(string message, ToastType type = ToastType.Info, int durationSeconds = 3)
    {
        void Show()
        {
            var toast = new ToastNotification(message, type, TimeSpan.FromSeconds(durationSeconds));
            CurrentToast = toast;
            toast.Show();
        }

        if (System.Windows.Application.Current?.Dispatcher is not { } dispatcher || dispatcher.CheckAccess())
        {
            Show();
        }
        else
        {
            dispatcher.Invoke(Show);
        }
    }

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand RefreshConnectionStatusesCommand { get; }

    public AsyncRelayCommand SaveSettingsCommand { get; }

    public AsyncRelayCommand AddProfileCommand { get; }

    public AsyncRelayCommand ClearProfileSearchCommand { get; }

    public RelayCommand ToggleMultiSelectModeCommand { get; }
    public RelayCommand ClearSelectionCommand { get; }
    public RelayCommand ToggleSelectAllCommand { get; }
    public AsyncRelayCommand SelectProfilesWithVaultCommand { get; }
    public AsyncRelayCommand<ProviderKind> AutoGetKeyCommand { get; }
    public AsyncRelayCommand ConnectOpenRouterOAuthCommand { get; }
    public AsyncRelayCommand StartBatchAutoLoginCommand { get; }
    public RelayCommand StopBatchLoginCommand { get; }
    public AsyncRelayCommand CloseBatchProgressCommand { get; }

    public AsyncRelayCommand LaunchSelectedCommand { get; }
    public AsyncRelayCommand<ChromeProfile> LaunchProfileCommand { get; }
    public AsyncRelayCommand<object> LaunchRecentCommand { get; }

    private async Task LaunchRecentAsync(object? parameter)
    {
        if (parameter is not int index || index < 0 || index >= MaxRecentSlots)
        {
            return;
        }

        if (index >= RecentProfileRows.Count)
        {
            StatusText = index == 0
                ? "Chưa có profile nào trong Quick Launch."
                : $"Chưa có profile nào ở vị trí {index + 1}.";
            return;
        }

        await LaunchProfileAsync(RecentProfileRows[index].Profile);
    }

    private async Task LaunchProfileAsync(ChromeProfile? profile)
    {
        if (profile is null)
        {
            StatusText = "Profile không hợp lệ.";
            return;
        }

        try
        {
            // Set as selected and track
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "UI",
                "ProfileLaunchRequested",
                "User launching profile",
                new { profile_name = profile.Name, profile_id = profile.Id, directory = profile.DirectoryName });

            SelectedProfile = profile;
            TrackProfileLaunch(profile);
            await LaunchUrlAsync(DashboardBaseUrl);

            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "UI",
                "ProfileLaunchCompleted",
                "Profile launch completed successfully",
                new { profile_name = profile.Name });

            StatusText = $"Đã mở 9Router bằng profile {profile.Name}.";
        }
        catch (Exception exception)
        {
            ObservabilityHub.Instance.LogError(
                "UI",
                "ProfileLaunchFailed",
                exception,
                new { profile_name = profile.Name });

            SetError(exception);
        }
    }

    public AsyncRelayCommand<ProviderKind> OpenProviderCommand { get; }

    public AsyncRelayCommand<ProviderKind> OpenProviderDashboardCommand { get; }

    public AsyncRelayCommand<ProviderKind> TestConnectionCommand { get; }

    public AsyncRelayCommand<ProviderKind> DeleteConnectionCommand { get; }

    public AsyncRelayCommand<ProviderKind> OpenQuickLinkCommand { get; }

    public AsyncRelayCommand CancelWorkflowCommand { get; }

    public AsyncRelayCommand WaitForConnectionCommand { get; }

    public AsyncRelayCommand OpenHelpCommand { get; }

    public AsyncRelayCommand OpenSecurityCommand { get; }

    public AsyncRelayCommand CheckForUpdatesCommand { get; }

    public AsyncRelayCommand InstallUpdateCommand { get; }

    public AsyncRelayCommand OpenReleasePageCommand { get; }

    /// <summary>
    /// Command to check health status for all profiles.
    /// </summary>
    public AsyncRelayCommand CheckAllProfilesHealthCommand { get; }

    /// <summary>
    /// Command to check health status for a single profile.
    /// </summary>
    public AsyncRelayCommand<ProfileRowViewModel> CheckProfileHealthCommand { get; }

    internal Task InitializationTask => _initializationTask ?? _initializationCompletion.Task;

    internal bool IsInitialized => _isInitialized;
}