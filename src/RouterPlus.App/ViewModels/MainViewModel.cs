using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Providers;
using RouterPlus.Core.Security;
using RouterPlus.Core.Updates;
using RouterPlus.App;
using RouterPlus.App.Views;
using RouterPlus.Infrastructure.Chrome;
using RouterPlus.Infrastructure.Router;
using RouterPlus.Infrastructure.Security;
using RouterPlus.Infrastructure.Storage;
using RouterPlus.Infrastructure.Updates;

namespace RouterPlus.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private const int MaxLogEntries = 200;
    private static readonly TimeSpan StartupUpdateCheckCooldown = TimeSpan.FromHours(12);
    public const int MaxRecentSlots = 10;
    public const int MaxQuickLaunchResults = 8;
    private readonly ChromeLocator _chromeLocator = new();
    private readonly ChromeProfileReader _profileReader = new();
    private readonly ChromeProfileProvisioner _profileProvisioner;
    private readonly ChromeProfileDeleter _profileDeleter;
    private readonly ChromeLauncher _chromeLauncher = new();
    private readonly SettingsStore _settingsStore;
    private readonly ISecretVault _secretVault = new DpapiSecretVault();
    private readonly HttpClient _httpClient;
    private readonly IUpdateService _updateService;
    private readonly IExternalLinkLauncher _linkLauncher;
    private readonly bool _runStartupUpdateCheck;
    private bool _isUpdateChecking;
    private bool _isUpdateInstalling;
    private ReleaseCheckResult? _latestRelease;
    private UpdateState _updateState = UpdateState.Idle;
    private string _updateStatusText = "Chưa kiểm tra bản cập nhật.";
    private DateTimeOffset _lastAutomaticUpdateCheck = DateTimeOffset.MinValue;
    private ChromeInstallation? _installation;
    private CancellationTokenSource? _workflowCancellation;
    private Dictionary<string, ProviderConnection> _workflowExistingConnections = new(StringComparer.Ordinal);
    private ProviderKind? _currentWorkflowProvider;
    private bool _workflowInProgress;
    private readonly List<ManagedChromeProfile> _managedProfiles = new();
    private readonly List<RecentProfile> _recentProfiles = new();
    private ChromeProfile? _selectedProfile;
    private string _quickLaunchFilterText = string.Empty;
    private bool _isQuickLaunchOpen;
    private ChromeProfile? _selectedQuickLaunchProfile;
    private ProviderKind _selectedApiKeyProvider = ProviderKind.OpenRouter;
    private int _apiKeyLoadVersion;
    private string _dashboardBaseUrl = "http://localhost:20128";
    private string _chromeExecutablePath = string.Empty;
    private string _chromeUserDataDirectory = string.Empty;
    private string _profileSearchText = string.Empty;
    private bool _isSettingsExpanded;
    private bool _isAppearanceSectionExpanded = true;
    private bool _isDashboardSectionExpanded = true;
    private bool _isChromeSectionExpanded = true;
    private bool _isKeyboardShortcutsSectionExpanded = true;
    private bool _isProfileSidebarCollapsed;
    private double _fontScale = 1d;
    private bool _useLightTheme = true;
    private string _savedDashboardBaseUrl = "http://localhost:20128";
    private string _savedChromeExecutablePath = string.Empty;
    private string _savedChromeUserDataDirectory = string.Empty;
    private double _savedFontScale = 1d;
    private bool _savedUseLightTheme = true;
    private bool _enableKeyboardShortcuts;
    private bool _savedEnableKeyboardShortcuts;
    private readonly ShortcutBindingsViewModel _shortcutBindings = new();
    private WindowPlacement? _savedWindowPlacement;
    private string _connectionStatusText = "Chưa đồng bộ trạng thái provider.";
    private string _statusText = "Đang khởi tạo…";
    private readonly Queue<string> _logEntries = new();
    private string _logText = "Chưa có log.";
    private ToastNotification? _currentToast;

    public MainViewModel(
        SettingsStore? settingsStore = null,
        ChromeProfileProvisioner? profileProvisioner = null,
        ChromeProfileDeleter? profileDeleter = null,
        HttpClient? httpClient = null,
        IUpdateService? updateService = null,
        IExternalLinkLauncher? linkLauncher = null,
        bool runStartupUpdateCheck = false)
    {
        _settingsStore = settingsStore ?? new SettingsStore();
        _profileProvisioner = profileProvisioner ?? new ChromeProfileProvisioner();
        _profileDeleter = profileDeleter ?? new ChromeProfileDeleter();
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _updateService = updateService ?? new SelfUpdateService(_httpClient, ApplicationInfo.CurrentVersion);
        _linkLauncher = linkLauncher ?? new ShellLinkLauncher();
        _runStartupUpdateCheck = runStartupUpdateCheck;
        Profiles = new ObservableCollection<ChromeProfile>();
        FilteredProfiles = new ObservableCollection<ChromeProfile>();
        ProfileRows = new ObservableCollection<ProfileRowViewModel>();
        FilteredProfileRows = new ObservableCollection<ProfileRowViewModel>();
        Profiles.CollectionChanged += Profiles_CollectionChanged;
        Providers = ProviderCatalog.All;
        InitializeProviderFilterOptions();
        ToggleProviderCommand = new AsyncRelayCommand<ProviderKind>(kind => { ToggleProvider(kind); return Task.CompletedTask; });
        ToggleUnassignedProfilesCommand = new AsyncRelayCommand(() =>
        {
            ToggleUnassignedProfiles();
            return Task.CompletedTask;
        });
        ProviderCards = Providers.Select(definition => new ProviderCardViewModel(definition)).ToArray();
        ApiKeyProviders = Providers.Where(provider => provider.Workflow == WorkflowKind.ApiKey).ToArray();
        RefreshCommand = new AsyncRelayCommand(InitializeAsync);
        RefreshConnectionStatusesCommand = new AsyncRelayCommand(RefreshConnectionStatusesAsync);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync, CanSaveSettings);
        AddProfileCommand = new AsyncRelayCommand(AddProfileAsync, () => CanAddProfile);
        ClearProfileSearchCommand = new AsyncRelayCommand(ClearProfileSearchAsync, () => CanClearProfileSearch);
        LaunchSelectedCommand = new AsyncRelayCommand(LaunchSelectedProfileAsync, () => SelectedProfile is not null);
        LaunchProfileCommand = new AsyncRelayCommand<ChromeProfile>(LaunchProfileAsync);
        ApplyShortcutCommand = new AsyncRelayCommand<string>(ApplyShortcutAsync);
        ResetShortcutsCommand = new AsyncRelayCommand(ResetAllShortcuts);
        LaunchRecentCommand = new AsyncRelayCommand<object>(LaunchRecentAsync);
        TogglePinProfileCommand = new AsyncRelayCommand<ChromeProfile>(TogglePinProfileAsync);
        ClearRecentProfilesCommand = new AsyncRelayCommand(ClearRecentProfilesAsync, () => RecentProfileRows.Count > 0);
        OpenQuickLaunchPaletteCommand = new AsyncRelayCommand(OpenQuickLaunchPalette);
        CloseQuickLaunchPaletteCommand = new AsyncRelayCommand(CloseQuickLaunchPalette);
        ConfirmQuickLaunchSelectionCommand = new AsyncRelayCommand<ChromeProfile?>(ConfirmQuickLaunchSelectionAsync);
        MoveQuickLaunchSelectionCommand = new AsyncRelayCommand<int>(MoveQuickLaunchSelection);
        OpenProviderCommand = new AsyncRelayCommand<ProviderKind>(OpenProviderAsync);
        OpenProviderDashboardCommand = new AsyncRelayCommand<ProviderKind>(OpenProviderDashboardAsync, _ => SelectedProfile is not null);
        TestConnectionCommand = new AsyncRelayCommand<ProviderKind>(TestConnectionAsync, _ => SelectedProfile is not null);
        OpenQuickLinkCommand = new AsyncRelayCommand<ProviderKind>(OpenQuickLinkAsync);
        CancelWorkflowCommand = new AsyncRelayCommand(CancelWorkflowAsync, () => IsWorkflowInProgress);
        WaitForConnectionCommand = new AsyncRelayCommand(WaitForConnectionAsync, () => !_workflowInProgress && _currentWorkflowProvider is not null && SelectedProfile is not null);
        OpenHelpCommand = new AsyncRelayCommand(OpenHelpAsync);
        OpenSecurityCommand = new AsyncRelayCommand(OpenSecurityAsync);
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync, () => !IsUpdateChecking && !IsWorkflowInProgress);
        InstallUpdateCommand = new AsyncRelayCommand(() => InstallUpdateAsync(confirmedByUser: true), () => CanInstallUpdate);
        AutoDetectChromeCommand = new AsyncRelayCommand(AutoDetectChromeAsync);
        OpenReleasePageCommand = new AsyncRelayCommand(OpenReleasePageAsync);
        ClearDashboardUrlCommand = new AsyncRelayCommand(ClearDashboardUrl);
        ClearChromeExecutableCommand = new AsyncRelayCommand(ClearChromeExecutable);
        ClearChromeUserDataCommand = new AsyncRelayCommand(ClearChromeUserData);
        ResetSettingsCommand = new AsyncRelayCommand(ResetSettingsAsync);
        ToggleAppearanceSectionCommand = new RelayCommand(ToggleAppearanceSection);
        ToggleDashboardSectionCommand = new RelayCommand(ToggleDashboardSection);
        ToggleChromeSectionCommand = new RelayCommand(ToggleChromeSection);
        ToggleKeyboardShortcutsSectionCommand = new RelayCommand(ToggleKeyboardShortcutsSection);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ChromeProfile> Profiles { get; }

    public ObservableCollection<ChromeProfile> FilteredProfiles { get; }

    public ObservableCollection<ProfileRowViewModel> ProfileRows { get; }

    public ObservableCollection<ProfileRowViewModel> FilteredProfileRows { get; }

    public ObservableCollection<ChromeProfile> RecentProfilesList { get; } = new();

    public ObservableCollection<RecentProfileRowViewModel> RecentProfileRows { get; } = new();

    public ObservableCollection<ChromeProfile> FilteredQuickLaunchProfiles { get; } = new();

    public bool IsQuickLaunchOpen
    {
        get => _isQuickLaunchOpen;
        private set
        {
            if (_isQuickLaunchOpen == value) return;
            _isQuickLaunchOpen = value;
            OnPropertyChanged();
        }
    }

    public string QuickLaunchFilterText
    {
        get => _quickLaunchFilterText;
        set
        {
            if (string.Equals(_quickLaunchFilterText, value, StringComparison.Ordinal)) return;
            _quickLaunchFilterText = value ?? string.Empty;
            OnPropertyChanged();
            RebuildQuickLaunchProfiles();
        }
    }

    public ChromeProfile? SelectedQuickLaunchProfile
    {
        get => _selectedQuickLaunchProfile;
        set
        {
            if (Equals(_selectedQuickLaunchProfile, value)) return;
            _selectedQuickLaunchProfile = value;
            OnPropertyChanged();
        }
    }

    public AsyncRelayCommand ClearRecentProfilesCommand { get; }
    public AsyncRelayCommand OpenQuickLaunchPaletteCommand { get; }
    public AsyncRelayCommand CloseQuickLaunchPaletteCommand { get; }
    public AsyncRelayCommand<ChromeProfile?> ConfirmQuickLaunchSelectionCommand { get; }
    public AsyncRelayCommand<int> MoveQuickLaunchSelectionCommand { get; }


    public IReadOnlyList<ProviderDefinition> Providers { get; }

    public IReadOnlyList<ProviderCardViewModel> ProviderCards { get; }

    public IReadOnlyList<ProviderDefinition> ApiKeyProviders { get; }

    public string ConnectionStatusText
    {
        get => _connectionStatusText;
        private set
        {
            if (string.Equals(_connectionStatusText, value, StringComparison.Ordinal))
            {
                return;
            }

            _connectionStatusText = value;
            OnPropertyChanged();
        }
    }

    public ChromeProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (Equals(_selectedProfile, value))
            {
                return;
            }

            _selectedProfile = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedProfileRow));
            UpdateProviderCardStatuses();
            LaunchSelectedCommand.RaiseCanExecuteChanged();
            OpenProviderDashboardCommand.RaiseCanExecuteChanged();
            TestConnectionCommand.RaiseCanExecuteChanged();
            WaitForConnectionCommand.RaiseCanExecuteChanged();
            _ = LoadSelectedProfileApiKeysAsync();
        }
    }

    public void SelectProfileForContextMenu(ChromeProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        SelectedProfile = profile;
    }

    public ProfileRowViewModel? SelectedProfileRow => _selectedProfile is null

        ? null
        : ProfileRows.FirstOrDefault(row => row.Profile.Id == _selectedProfile.Id);

    public string ProfileSearchText
    {
        get => _profileSearchText;
        set
        {
            value ??= string.Empty;
            if (string.Equals(_profileSearchText, value, StringComparison.Ordinal))
            {
                return;
            }

            _profileSearchText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanAddProfile));
            OnPropertyChanged(nameof(ProfileAddButtonText));
            OnPropertyChanged(nameof(CanClearProfileSearch));
            AddProfileCommand.RaiseCanExecuteChanged();
            ClearProfileSearchCommand.RaiseCanExecuteChanged();
            ApplyProfileFilter();
        }
    }

    public bool CanAddProfile =>
        !string.IsNullOrWhiteSpace(ProfileSearchText.Trim()) &&
        !Profiles.Any(profile => string.Equals(
            profile.Name.Trim(),
            ProfileSearchText.Trim(),
            StringComparison.OrdinalIgnoreCase));

    public string ProfileAddButtonText => string.IsNullOrWhiteSpace(ProfileSearchText.Trim())
        ? "Thêm profile"
        : $"Thêm profile \"{ProfileSearchText.Trim()}\"";

    public bool CanClearProfileSearch => !string.IsNullOrEmpty(ProfileSearchText);

    public IReadOnlyList<ProfileProviderFilterOption> ProviderFilterOptions { get; private set; } = Array.Empty<ProfileProviderFilterOption>();

    private readonly Dictionary<ProviderKind, ProfileProviderFilterOption> _providerOptionByKind = new();

    private void InitializeProviderFilterOptions()
    {
        var providerOptions = ProviderCatalog.All.Select(definition => new ProfileProviderFilterOption(
            definition.Kind,
            definition.ShortDisplayName,
            definition.Glyph,
            $"Chỉ hiển profile có kết nối {definition.DisplayName}"));
        var options = providerOptions.ToArray();
        ProviderFilterOptions = options;
        foreach (var option in options)
        {
            if (option.Kind is { } kind)
            {
                _providerOptionByKind[kind] = option;
            }
        }
    }

    public HashSet<ProviderKind> SelectedProviderKinds { get; } = new();

    public AsyncRelayCommand<ProviderKind> ToggleProviderCommand { get; }

    public AsyncRelayCommand ToggleUnassignedProfilesCommand { get; }

    public bool IsUnassignedProfileFilterActive { get; private set; }

    public void ToggleProvider(ProviderKind kind)
    {
        if (!SelectedProviderKinds.Add(kind))
        {
            SelectedProviderKinds.Remove(kind);
        }

        IsUnassignedProfileFilterActive = false;
        if (_providerOptionByKind.TryGetValue(kind, out var option))
        {
            option.IsSelected = SelectedProviderKinds.Contains(kind);
        }
        NotifyProviderFilterChanged();
    }

    public void ToggleUnassignedProfiles()
    {
        IsUnassignedProfileFilterActive = !IsUnassignedProfileFilterActive;
        if (IsUnassignedProfileFilterActive)
        {
            SelectedProviderKinds.Clear();
            foreach (var option in ProviderFilterOptions)
            {
                option.IsSelected = false;
            }
        }

        NotifyProviderFilterChanged();
    }

    public void ClearProviderFilter()
    {
        if (SelectedProviderKinds.Count == 0 && !IsUnassignedProfileFilterActive)
        {
            return;
        }

        SelectedProviderKinds.Clear();
        IsUnassignedProfileFilterActive = false;
        foreach (var option in ProviderFilterOptions)
        {
            option.IsSelected = false;
        }

        NotifyProviderFilterChanged();
    }

    public bool IsProviderFilterActive => SelectedProviderKinds.Count > 0 || IsUnassignedProfileFilterActive;

    public int FilteredProfileCount => FilteredProfileRows.Count;

    public string FilteredProfileCountLabel
    {
        get
        {
            var hasFilter = IsProviderFilterActive || !string.IsNullOrWhiteSpace(ProfileSearchText);
            return hasFilter
                ? string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0} đang hiển thị", FilteredProfileCount)
                : string.Format(System.Globalization.CultureInfo.CurrentCulture, "{0} profile", FilteredProfileCount);
        }
    }

    private void NotifyProviderFilterChanged()
    {
        OnPropertyChanged(nameof(SelectedProviderKinds));
        OnPropertyChanged(nameof(IsProviderFilterActive));
        OnPropertyChanged(nameof(IsUnassignedProfileFilterActive));
        OnPropertyChanged(nameof(FilteredProfileCountLabel));
        ApplyProfileFilter();
    }

    public bool IsAppearanceSectionExpanded
    {
        get => _isAppearanceSectionExpanded;
        set
        {
            if (_isAppearanceSectionExpanded == value) return;
            _isAppearanceSectionExpanded = value;
            OnPropertyChanged();
        }
    }

    public bool IsDashboardSectionExpanded
    {
        get => _isDashboardSectionExpanded;
        set
        {
            if (_isDashboardSectionExpanded == value) return;
            _isDashboardSectionExpanded = value;
            OnPropertyChanged();
        }
    }

    public bool IsChromeSectionExpanded
    {
        get => _isChromeSectionExpanded;
        set
        {
            if (_isChromeSectionExpanded == value) return;
            _isChromeSectionExpanded = value;
            OnPropertyChanged();
        }
    }

    public bool IsKeyboardShortcutsSectionExpanded
    {
        get => _isKeyboardShortcutsSectionExpanded;
        set
        {
            if (_isKeyboardShortcutsSectionExpanded == value) return;
            _isKeyboardShortcutsSectionExpanded = value;
            OnPropertyChanged();
        }
    }

    public bool IsSettingsExpanded
    {
        get => _isSettingsExpanded;
        set
        {
            if (_isSettingsExpanded == value)
            {
                return;
            }

            _isSettingsExpanded = value;
            OnPropertyChanged();
        }
    }

    public bool IsProfileSidebarCollapsed
    {
        get => _isProfileSidebarCollapsed;
        set
        {
            if (_isProfileSidebarCollapsed == value)
            {
                return;
            }

            _isProfileSidebarCollapsed = value;
            OnPropertyChanged();
        }
    }

    public bool IsWorkflowInProgress => _workflowInProgress;

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

    public bool IsKeyboardShortcutsEnabled
    {
        get => _enableKeyboardShortcuts;
        set
        {
            if (_enableKeyboardShortcuts == value)
            {
                return;
            }

            _enableKeyboardShortcuts = value;
            OnPropertyChanged();
            NotifySettingsStateChanged();
        }
    }

    public ObservableCollection<ShortcutBindingRowViewModel> ShortcutRows => _shortcutBindings.Rows;

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
    public AsyncRelayCommand<string> ApplyShortcutCommand { get; }
    public AsyncRelayCommand ResetShortcutsCommand { get; }

    public RelayCommand ToggleAppearanceSectionCommand { get; }
    public RelayCommand ToggleDashboardSectionCommand { get; }
    public RelayCommand ToggleChromeSectionCommand { get; }
    public RelayCommand ToggleKeyboardShortcutsSectionCommand { get; }

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

    private void ToggleKeyboardShortcutsSection()
    {
        IsKeyboardShortcutsSectionExpanded = !IsKeyboardShortcutsSectionExpanded;
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

        StatusText = "Đã khôi phục cài đặt về mặc định.";
        await Task.CompletedTask;
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
            var installations = _chromeLocator.FindAll();
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

    private void ShowToast(string message, ToastType type = ToastType.Info, int durationSeconds = 3)
    {
        CurrentToast = new ToastNotification(message, type, TimeSpan.FromSeconds(durationSeconds));
        CurrentToast.Show();
    }

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand RefreshConnectionStatusesCommand { get; }

    public AsyncRelayCommand SaveSettingsCommand { get; }

    public AsyncRelayCommand AddProfileCommand { get; }

    public AsyncRelayCommand ClearProfileSearchCommand { get; }

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
                : $"Chưa có profile nào ở vị trí Ctrl+{index}.";
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
            SelectedProfile = profile;
            TrackProfileLaunch(profile);
            await LaunchUrlAsync(DashboardBaseUrl);
            StatusText = $"Đã mở 9Router bằng profile {profile.Name}.";
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }

    public AsyncRelayCommand<ProviderKind> OpenProviderCommand { get; }

    public AsyncRelayCommand<ProviderKind> OpenProviderDashboardCommand { get; }

    public AsyncRelayCommand<ProviderKind> TestConnectionCommand { get; }

    public AsyncRelayCommand<ProviderKind> OpenQuickLinkCommand { get; }

    public AsyncRelayCommand CancelWorkflowCommand { get; }

    public AsyncRelayCommand WaitForConnectionCommand { get; }

    public AsyncRelayCommand OpenHelpCommand { get; }

    public AsyncRelayCommand OpenSecurityCommand { get; }

    public AsyncRelayCommand CheckForUpdatesCommand { get; }

    public AsyncRelayCommand InstallUpdateCommand { get; }

    public AsyncRelayCommand OpenReleasePageCommand { get; }

    public async Task InitializeAsync()
    {
        try
        {
            var settings = await _settingsStore.LoadAsync();
            DashboardBaseUrl = settings.DashboardBaseUrl;
            ChromeExecutablePath = settings.ChromeExecutablePath ?? string.Empty;
            ChromeUserDataDirectory = settings.ChromeUserDataDirectory ?? string.Empty;
            FontScale = settings.FontScale;
            UseLightTheme = settings.UseLightTheme;
            _enableKeyboardShortcuts = settings.EnableKeyboardShortcuts;
        _savedEnableKeyboardShortcuts = _enableKeyboardShortcuts;
            _shortcutBindings.Load(settings.KeyboardShortcuts);
            OnPropertyChanged(nameof(IsKeyboardShortcutsEnabled));
            OnPropertyChanged(nameof(ShortcutRows));
            _savedWindowPlacement = TryCreateWindowPlacement(
                settings.WindowLeft,
                settings.WindowTop,
                settings.WindowWidth,
                settings.WindowHeight);
            _managedProfiles.Clear();
            _managedProfiles.AddRange(settings.ManagedProfiles ?? []);
            _recentProfiles.Clear();
            _recentProfiles.AddRange(settings.RecentProfiles ?? []);
            RefreshProfiles();
            MarkSettingsSaved();
            await LoadSelectedProfileApiKeysAsync();
            StatusText = Profiles.Count == 0
                ? "Chưa tìm thấy Chrome profile. Hãy kiểm tra đường dẫn rồi nhấn Làm mới."
                : $"Đã đọc {Profiles.Count} Chrome profile.";
            await RefreshConnectionStatusesAsync(showStatus: true);
            if (_runStartupUpdateCheck)
            {
                _ = RunStartupUpdateCheckAsync();
            }
        }
        catch (Exception exception)
        {
            SetError(exception);
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

    public async Task CheckForUpdatesAsync()
    {
        if (IsUpdateChecking)
        {
            return;
        }

        IsUpdateChecking = true;
        UpdateState = UpdateState.Checking;
        UpdateStatusText = "Đang kiểm tra bản cập nhật…";
        try
        {
            var release = await _updateService.CheckAsync();
            _latestRelease = release;
            OnPropertyChanged(nameof(IsUpdateAvailable));
            OnPropertyChanged(nameof(AvailableVersion));

            if (!_updateService.IsInstallSupported)
            {
                UpdateState = UpdateState.Disabled;
                UpdateStatusText = release.IsUpdateAvailable
                    ? $"Có bản {release.AvailableVersion}, nhưng tự cập nhật chỉ hỗ trợ trên Windows."
                    : "Tự cập nhật chỉ hỗ trợ trên Windows.";
            }
            else if (release.IsUpdateAvailable)
            {
                UpdateState = UpdateState.Available;
                UpdateStatusText = $"Có bản cập nhật {release.AvailableVersion}.";
            }
            else
            {
                UpdateState = UpdateState.Idle;
                UpdateStatusText = "Bạn đang dùng bản mới nhất.";
            }
        }
        catch (OperationCanceledException)
        {
            UpdateState = UpdateState.Failed;
            UpdateStatusText = "Đã hủy kiểm tra bản cập nhật.";
        }
        catch
        {
            _latestRelease = null;
            OnPropertyChanged(nameof(IsUpdateAvailable));
            OnPropertyChanged(nameof(AvailableVersion));
            UpdateState = _updateService.IsInstallSupported ? UpdateState.Failed : UpdateState.Disabled;
            UpdateStatusText = _updateService.IsInstallSupported
                ? "Không thể kiểm tra bản cập nhật lúc này."
                : "Không thể bật tự cập nhật trên hệ điều hành này.";
        }
        finally
        {
            IsUpdateChecking = false;
        }
    }

    public async Task<bool> InstallUpdateAsync(bool confirmedByUser)
    {
        if (!confirmedByUser || !CanInstallUpdate || _latestRelease is null)
        {
            return false;
        }

        _isUpdateInstalling = true;
        UpdateState = UpdateState.Downloading;
        UpdateStatusText = "Đang tải và kiểm tra bản cập nhật…";
        InstallUpdateCommand.RaiseCanExecuteChanged();
        try
        {
            var package = await _updateService.DownloadAndStageAsync(_latestRelease);
            UpdateState = UpdateState.Installing;
            UpdateStatusText = "Bản cập nhật đã được xác minh. Đang chuẩn bị khởi động lại…";
            if (!await _updateService.LaunchUpdaterAsync(package))
            {
                UpdateState = UpdateState.Failed;
                UpdateStatusText = "Không thể khởi động trình cập nhật. Bản đang chạy không bị thay đổi.";
                return false;
            }

            UpdateState = UpdateState.Completed;
            UpdateStatusText = "Đã chuẩn bị cập nhật. Ứng dụng sẽ đóng để hoàn tất thay thế an toàn.";
            return true;
        }
        catch (OperationCanceledException)
        {
            UpdateState = UpdateState.Failed;
            UpdateStatusText = "Đã hủy cập nhật. Bản đang chạy không bị thay đổi.";
            return false;
        }
        catch
        {
            UpdateState = UpdateState.Failed;
            UpdateStatusText = "Không thể xác minh hoặc cài đặt bản cập nhật. Bản đang chạy không bị thay đổi.";
            return false;
        }
        finally
        {
            _isUpdateInstalling = false;
            InstallUpdateCommand.RaiseCanExecuteChanged();
        }
    }

    public void RefreshProfiles()
    {
        var previousProfileId = SelectedProfile?.Id;
        _installation = _chromeLocator.Find(
            string.IsNullOrWhiteSpace(ChromeExecutablePath) ? null : ChromeExecutablePath,
            string.IsNullOrWhiteSpace(ChromeUserDataDirectory) ? null : ChromeUserDataDirectory);

        if (_installation is null)
        {
            return;
        }

        ChromeExecutablePath = _installation.ExecutablePath;
        ChromeUserDataDirectory = _installation.UserDataDirectory;
        var discoveredProfiles = _profileReader.Read(_installation.UserDataDirectory)
            .Where(profile => Directory.Exists(profile.ProfilePath))
            .ToArray();
        var managedProfiles = _managedProfiles
            .Where(profile => Directory.Exists(Path.Combine(profile.UserDataDirectory, profile.DirectoryName)))
            .ToArray();
        var profiles = ChromeProfileCatalog.Merge(
            discoveredProfiles,
            managedProfiles,
            _installation.UserDataDirectory);
        Profiles.Clear();
        ProfileRows.Clear();
        foreach (var profile in profiles)
        {
            Profiles.Add(profile);
            ProfileRows.Add(new ProfileRowViewModel(profile, Providers));
        }

        var restoredProfile = Profiles.FirstOrDefault(profile => profile.Id == previousProfileId)
            ?? Profiles.FirstOrDefault();
        ApplyProfileFilter();
        SelectedProfile = restoredProfile;
        OnPropertyChanged(nameof(SelectedProfileRow));
        UpdateProviderCardStatuses();
        LaunchSelectedCommand.RaiseCanExecuteChanged();
        OpenProviderDashboardCommand.RaiseCanExecuteChanged();
        TestConnectionCommand.RaiseCanExecuteChanged();
        WaitForConnectionCommand.RaiseCanExecuteChanged();
    }

    public async Task AddProfileAsync()
    {
        if (!CanAddProfile)
        {
            return;
        }

        try
        {
            _installation = _chromeLocator.Find(
                string.IsNullOrWhiteSpace(ChromeExecutablePath) ? null : ChromeExecutablePath,
                string.IsNullOrWhiteSpace(ChromeUserDataDirectory) ? null : ChromeUserDataDirectory);
            if (_installation is null)
            {
                throw new InvalidOperationException("Không tìm thấy Chrome User Data Directory. Hãy kiểm tra đường dẫn trong Cài đặt.");
            }

            var createdProfile = _profileProvisioner.Create(
                _installation.UserDataDirectory,
                ProfileSearchText,
                Profiles,
                _managedProfiles);
            _managedProfiles.Add(createdProfile);
            try
            {
                await _settingsStore.SaveAsync(BuildSettings());
            }
            catch
            {
                _managedProfiles.Remove(createdProfile);
                throw;
            }

            MarkSettingsSaved();
            RefreshProfiles();
            SelectedProfile = Profiles.FirstOrDefault(profile =>
                string.Equals(profile.Id, ChromeProfile.CreateId(
                    createdProfile.UserDataDirectory,
                    createdProfile.DirectoryName),
                    StringComparison.Ordinal));
            StatusText = $"Đã thêm profile \"{createdProfile.Name}\" ({createdProfile.DirectoryName}).";
            ShowToast($"Đã thêm profile {createdProfile.Name}", ToastType.Success);
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }

    public Task ClearProfileSearchAsync()
    {
        ProfileSearchText = string.Empty;
        return Task.CompletedTask;
    }

    public void MarkLogCopied() => StatusText = "Đã sao chép log vào clipboard.";

    public void MarkProfileNameCopied() => StatusText = "Đã sao chép tên profile vào clipboard.";

    public void MarkProfileFolderOpened()
    {
        if (SelectedProfile is not null)
        {
            StatusText = $"Đã mở thư mục profile {SelectedProfile.Name}.";
            ShowToast(StatusText, ToastType.Success);
        }
    }

    public void MarkProfileActionFailed(Exception exception, [CallerMemberName] string? operation = null) =>
        SetError(exception, operation);

    public void MarkApiKeyPasted(ProviderKind provider)
    {
        StatusText = $"{ProviderCatalog.Get(provider).DisplayName} API key đã được dán vào ô nhập.";
        ShowToast(StatusText, ToastType.Info);
    }

    public void MarkApiKeyPasteFailed(ProviderKind provider, string? details = null)
    {
        StatusText = string.IsNullOrWhiteSpace(details)
            ? $"Clipboard không có API key cho {ProviderCatalog.Get(provider).DisplayName}."
            : $"Không thể dán API key cho {ProviderCatalog.Get(provider).DisplayName}. Kiểm tra quyền truy cập clipboard.";
        ShowToast(StatusText, ToastType.Warning);
    }

    private void ApplyProfileFilter()
    {
        var selectedProfileId = SelectedProfile?.Id;
        FilteredProfiles.Clear();
        FilteredProfileRows.Clear();
        var rowsByProfileId = ProfileRows.ToDictionary(row => row.Profile.Id, StringComparer.Ordinal);
        var selectedProviders = SelectedProviderKinds;
        var hasProviderFilter = selectedProviders.Count > 0;
        var displayIndex = 1;
        foreach (var profile in ChromeProfileFilter.Filter(Profiles, ProfileSearchText))
        {
            if (!rowsByProfileId.TryGetValue(profile.Id, out var row))
            {
                continue;
            }
            if (IsUnassignedProfileFilterActive &&
                (row.ProviderStatuses.Any(status => !status.IsKnown) || row.ConnectedProviderCount > 0))
            {
                continue;
            }
            if (hasProviderFilter && !row.ProviderStatuses.Any(status => selectedProviders.Contains(status.Definition.Kind) && status.IsConnected))
            {
                continue;
            }
            FilteredProfiles.Add(profile);
            row.SetDisplayIndex(displayIndex++);
            FilteredProfileRows.Add(row);
        }

        if (selectedProfileId is not null &&
            FilteredProfiles.Any(profile => profile.Id == selectedProfileId))
        {
            SelectedProfile = Profiles.First(profile => profile.Id == selectedProfileId);
        }

        OnPropertyChanged(nameof(FilteredProfileCount));
        OnPropertyChanged(nameof(FilteredProfileCountLabel));
    }

    private void Profiles_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CanAddProfile));
        OnPropertyChanged(nameof(ProfileAddButtonText));
        AddProfileCommand.RaiseCanExecuteChanged();
        UpdateRecentProfilesList();
    }

    public async Task RefreshConnectionStatusesAsync() =>
        await RefreshConnectionStatusesAsync(showStatus: true, forceLog: true);

    private async Task LoadSelectedProfileApiKeysAsync()
    {
        var loadVersion = Interlocked.Increment(ref _apiKeyLoadVersion);
        var profile = SelectedProfile;
        foreach (var card in ProviderCards.Where(card => card.Workflow == WorkflowKind.ApiKey))
        {
            card.LoadSavedApiKey(null);
        }

        if (profile is null)
        {
            return;
        }

        try
        {
            var values = await Task.WhenAll(ApiKeyProviders.Select(async definition =>
                new
                {
                    definition.Kind,
                    Value = await _secretVault.ReadAsync(ProfileSecretKey.Create(profile, definition.Kind))
                }));

            if (loadVersion != Volatile.Read(ref _apiKeyLoadVersion) || !Equals(SelectedProfile, profile))
            {
                return;
            }

            foreach (var value in values)
            {
                GetProviderCard(value.Kind).LoadSavedApiKey(value.Value);
            }
        }
        catch (Exception exception)
        {
            if (loadVersion == Volatile.Read(ref _apiKeyLoadVersion))
            {
                AppendLog("WARN", $"Không thể đọc API key đã lưu: {SafeError(exception)}");
            }
        }
    }

    private async Task RefreshConnectionStatusesAsync(bool showStatus, bool forceLog = false)
    {
        if (ProfileRows.Count == 0)
        {
            UpdateProviderCardStatuses();
            ConnectionStatusText = "Chưa có Chrome profile để đối chiếu.";
            if (showStatus && forceLog)
            {
                SetStatusText(ConnectionStatusText, "SYNC", forceLog: true);
            }

            return;
        }

        try
        {
            var connections = await CreateApiClient().ListAllConnectionsAsync();
            foreach (var row in ProfileRows)
            {
                row.UpdateConnections(connections);
            }

            OnPropertyChanged(nameof(SelectedProfileRow));
            UpdateProviderCardStatuses();
            ApplyProfileFilter();

            var matchedProfiles = ProfileRows.Count(row => row.ConnectedProviderCount > 0);
            ConnectionStatusText =
                $"Đã đồng bộ {connections.Count} connection · {matchedProfiles}/{ProfileRows.Count} profile có provider.";
            if (showStatus)
            {
                if (forceLog)
                {
                    SetStatusText(ConnectionStatusText, "SYNC", forceLog: true);
                }
                else
                {
                    StatusText = ConnectionStatusText;
                }
            }
        }
        catch (Exception exception)
        {
            foreach (var row in ProfileRows)
            {
                row.MarkStatusUnknown();
            }
            UpdateProviderCardStatuses();
            ApplyProfileFilter();

            ConnectionStatusText = $"Chưa đồng bộ provider: {SafeError(exception)}";
            if (showStatus)
            {
                if (forceLog)
                {
                    SetStatusText(ConnectionStatusText, "SYNC", forceLog: true);
                }
                else
                {
                    StatusText = ConnectionStatusText;
                }
            }
            else
            {
                AppendLog("WARN", ConnectionStatusText);
            }
        }
    }

    public async Task SaveSettingsAsync()
    {
        try
        {
            await _settingsStore.SaveAsync(BuildSettings());
            RefreshProfiles();
            MarkSettingsSaved();
            await RefreshConnectionStatusesAsync();
            StatusText = "Đã lưu cài đặt.";
            ShowToast(StatusText, ToastType.Success);
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }

    public async Task SaveWindowPlacementAsync(double left, double top, double width, double height)
    {
        if (!IsValidWindowPlacement(left, top, width, height))
        {
            return;
        }

        var placement = new WindowPlacement(left, top, width, height);
        try
        {
            var settings = await _settingsStore.LoadAsync();
            await _settingsStore.SaveAsync(settings with
            {
                WindowLeft = placement.Left,
                WindowTop = placement.Top,
                WindowWidth = placement.Width,
                WindowHeight = placement.Height
            });
            _savedWindowPlacement = placement;
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }

    public Task<bool> AddApiKeyAsync(string apiKey) =>
        AddApiKeyAsync(SelectedApiKeyProvider, apiKey);

    public async Task<bool> AddApiKeyAsync(ProviderKind provider, string apiKey)
    {
        var definition = ProviderCatalog.Get(provider);
        if (definition.Workflow != WorkflowKind.ApiKey)
        {
            StatusText = $"{definition.DisplayName} không dùng API key.";
            ShowToast(StatusText, ToastType.Warning);
            return false;
        }

        SelectedApiKeyProvider = provider;
        var profile = SelectedProfile;
        if (profile is null)
        {
            StatusText = "Hãy chọn Chrome profile trước.";
            return false;
        }

        apiKey = apiKey.Trim();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            StatusText = "Hãy dán API key vào ô bảo mật.";
            ShowToast(StatusText, ToastType.Warning);
            return false;
        }

        try
        {
            var api = CreateApiClient();
            var existing = await api.ListConnectionsAsync(provider);
            var existingConnection = existing.FirstOrDefault(connection =>
                string.Equals(connection.Name?.Trim(), profile.Name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (existingConnection is not null)
            {
                await api.UpdateConnectionAsync(existingConnection.Id, name: profile.Name, apiKey: apiKey);
                await _secretVault.StoreAsync(ProfileSecretKey.Create(profile, provider), apiKey);
                if (Equals(SelectedProfile, profile))
                {
                    var existingCard = GetProviderCard(provider);
                    existingCard.ApiKeyValue = apiKey;
                    existingCard.MarkApiKeySaved();
                }
                var existingTestResult = await api.TestConnectionAsync(existingConnection.Id);
                await RefreshConnectionStatusesAsync(showStatus: false);
                var updateMessage = existingTestResult.Valid
                    ? $"{definition.DisplayName} key updated in 9Router for {profile.Name}; local key saved."
                    : $"{definition.DisplayName} key saved for {profile.Name}, but the connection test failed.";
                StatusText = updateMessage;
                ShowToast(updateMessage, existingTestResult.Valid ? ToastType.Success : ToastType.Warning, 5);
                return true;
            }

            var priority = PriorityCalculator.Next(existing);
            var created = await api.AddApiKeyConnectionAsync(
                provider,
                profile.Name,
                apiKey,
                priority);
            await _secretVault.StoreAsync(ProfileSecretKey.Create(profile, provider), apiKey);
            if (Equals(SelectedProfile, profile))
            {
                var card = GetProviderCard(provider);
                card.ApiKeyValue = apiKey;
                card.MarkApiKeySaved();
            }
            var createdTestResult = await api.TestConnectionAsync(created.Id);
            await RefreshConnectionStatusesAsync(showStatus: false);
            var createMessage = createdTestResult.Valid
                ? $"Đã thêm {definition.DisplayName} cho {profile.Name}, priority {created.Priority}."
                : $"Đã lưu {definition.DisplayName} cho {profile.Name}, nhưng kiểm tra kết nối thất bại.";
            StatusText = createMessage;
            ShowToast(createMessage, createdTestResult.Valid ? ToastType.Success : ToastType.Warning, 5);
            return true;
        }
        catch (Exception exception)
        {
            SetError(exception);
            return false;
        }
    }

    private ProviderCardViewModel GetProviderCard(ProviderKind provider) =>
        ProviderCards.First(card => card.Kind == provider);

    private void UpdateProviderCardStatuses()
    {
        var row = SelectedProfileRow;
        foreach (var card in ProviderCards)
        {
            var status = row?.ProviderStatuses.FirstOrDefault(item => item.Definition.Kind == card.Kind);
            card.UpdateProviderStatus(status);
        }
    }

    private async Task OpenProviderAsync(ProviderKind provider)
    {
        var definition = ProviderCatalog.Get(provider);
        if (SelectedProfile is null)
        {
            StatusText = "Hãy chọn Chrome profile trước.";
            ShowToast(StatusText, ToastType.Warning);
            return;
        }

        using var workflowCancellation = new CancellationTokenSource();
        _workflowCancellation = workflowCancellation;
        _workflowInProgress = true;
        GetProviderCard(provider).SetWorkflowInProgress(true);
        OnPropertyChanged(nameof(IsWorkflowInProgress));
        CancelWorkflowCommand.RaiseCanExecuteChanged();
        WaitForConnectionCommand.RaiseCanExecuteChanged();
        CheckForUpdatesCommand.RaiseCanExecuteChanged();
        InstallUpdateCommand.RaiseCanExecuteChanged();

        try
        {
            var cancellationToken = workflowCancellation.Token;
            WaitForConnectionCommand.RaiseCanExecuteChanged();
            var api = CreateApiClient();
            switch (definition.Workflow)
            {
                case WorkflowKind.ApiKey:
                    SelectedApiKeyProvider = provider;
                    _currentWorkflowProvider = null;
                    await LaunchUrlAsync(definition.QuickLink);
                    StatusText = $"Đã mở {definition.DisplayName}. Lấy key rồi dán vào ô bảo mật bên dưới và bấm Lưu API key.";
                    ShowToast(StatusText, ToastType.Info, 5);
                    break;
                case WorkflowKind.DeviceCode:
                    await RunDeviceCodeWorkflowAsync(api, provider, cancellationToken);
                    break;
                case WorkflowKind.OAuth:
                    await RunOAuthWorkflowAsync(api, provider, cancellationToken);
                    break;
            }
        }
        catch (OperationCanceledException) when (workflowCancellation.IsCancellationRequested)
        {
            _currentWorkflowProvider = null;
            _workflowExistingConnections.Clear();
            StatusText = $"Đã hủy thao tác thêm {definition.DisplayName}. Bạn có thể thử lại.";
            ShowToast(StatusText, ToastType.Warning, 5);
        }
        catch (Exception exception)
        {
            _currentWorkflowProvider = null;
            _workflowExistingConnections.Clear();
            SetError(exception);
        }
        finally
        {
            if (ReferenceEquals(_workflowCancellation, workflowCancellation))
            {
                _workflowCancellation = null;
            }

            _workflowInProgress = false;
            GetProviderCard(provider).SetWorkflowInProgress(false);
            OnPropertyChanged(nameof(IsWorkflowInProgress));
            CancelWorkflowCommand.RaiseCanExecuteChanged();
            WaitForConnectionCommand.RaiseCanExecuteChanged();
            CheckForUpdatesCommand.RaiseCanExecuteChanged();
            InstallUpdateCommand.RaiseCanExecuteChanged();
        }
    }

    private Task CancelWorkflowAsync()
    {
        if (_workflowCancellation is { IsCancellationRequested: false } cancellation)
        {
            StatusText = "Đang hủy thao tác thêm provider…";
            cancellation.Cancel();
        }

        return Task.CompletedTask;
    }

    private async Task RunOAuthWorkflowAsync(
        RouterApiClient api,
        ProviderKind provider,
        CancellationToken cancellationToken)
    {
        await CaptureExistingConnectionsAsync(api, provider, cancellationToken);
        var definition = ProviderCatalog.Get(provider);
        if (provider == ProviderKind.Codex)
        {
            var session = await api.StartOAuthAuthorizationAsync(
                provider,
                "http://localhost:1455/auth/callback",
                cancellationToken);
            OAuthProxyStartResult proxy;
            try
            {
                proxy = await api.StartOAuthProxyAsync(
                    provider,
                    GetDashboardPort(),
                    session,
                    cancellationToken);
            }
            catch (RouterApiException)
            {
                _currentWorkflowProvider = null;
                await LaunchUrlAsync(definition.BuildDashboardUrl(DashboardBaseUrl));
                StatusText = "9Router không bật được OAuth proxy tự động. Trong dashboard, bấm Thêm Codex rồi quay lại bấm Chờ connection thủ công.";
                return;
            }

            if (!proxy.ServerSide)
            {
                _currentWorkflowProvider = null;
                await LaunchUrlAsync(definition.BuildDashboardUrl(DashboardBaseUrl));
                StatusText = "OAuth proxy chưa sẵn sàng. Trong dashboard, bấm Thêm Codex rồi quay lại bấm Chờ connection thủ công.";
                return;
            }

            await LaunchUrlAsync(session.AuthUrl);
            StatusText = "Đã mở đăng nhập Codex. Hoàn tất chọn tài khoản; tool đang tự chờ connection mới…";
            await WaitForOAuthProxyAsync(
                api,
                provider,
                session.State,
                TimeSpan.FromMinutes(10),
                cancellationToken);
        }
        else
        {
            await using var callbackListener = await OAuthCallbackListener.StartAsync();
            var session = await api.StartOAuthAuthorizationAsync(
                provider,
                callbackListener.RedirectUri.ToString(),
                cancellationToken);
            await LaunchUrlAsync(session.AuthUrl);
            StatusText = $"Đã mở đăng nhập {definition.DisplayName}. Hoàn tất đăng nhập; tool đang chờ callback…";
            var callback = await callbackListener.WaitForCallbackAsync(
                TimeSpan.FromMinutes(10),
                cancellationToken);
            if (!string.IsNullOrWhiteSpace(callback.Error))
            {
                throw new InvalidOperationException(callback.ErrorDescription ?? callback.Error);
            }

            if (string.IsNullOrWhiteSpace(callback.Value))
            {
                throw new InvalidOperationException("OAuth callback không có code hoặc token.");
            }

            if (!callback.MatchesState(session.State))
            {
                throw new InvalidOperationException("OAuth callback state không khớp phiên đăng nhập.");
            }

            await api.ExchangeOAuthCodeAsync(
                provider,
                callback.Value,
                session.RedirectUri,
                session.CodeVerifier,
                callback.State,
                cancellationToken);
        }

        await RenameNewConnectionAsync(api, provider, cancellationToken);
    }

    private async Task RunDeviceCodeWorkflowAsync(
        RouterApiClient api,
        ProviderKind provider,
        CancellationToken cancellationToken)
    {
        await CaptureExistingConnectionsAsync(api, provider, cancellationToken);
        var definition = ProviderCatalog.Get(provider);
        var session = await api.StartDeviceCodeAsync(provider, "idc", cancellationToken);
        await LaunchUrlAsync(session.VerificationUriComplete ?? session.VerificationUri);
        StatusText = $"Đã mở AWS Builder ID cho {definition.DisplayName}. Hoàn tất xác nhận; tool đang tự chờ connection…";

        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(Math.Max(session.ExpiresIn, 60));
        var intervalSeconds = Math.Clamp(session.Interval, 1, 30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var result = await api.PollDeviceCodeAsync(provider, session, cancellationToken);
            if (result.Success)
            {
                await RenameNewConnectionAsync(api, provider, cancellationToken);
                return;
            }

            if (!string.Equals(result.Error, "authorization_pending", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(result.Error, "slow_down", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(result.ErrorDescription ?? result.Error ?? "Device-code authorization failed.");
            }

            if (string.Equals(result.Error, "slow_down", StringComparison.OrdinalIgnoreCase))
            {
                intervalSeconds = Math.Min(intervalSeconds + 5, 30);
            }

            await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), cancellationToken);
        }

        throw new TimeoutException($"Hết thời gian chờ đăng nhập {definition.DisplayName}.");
    }

    private async Task CaptureExistingConnectionsAsync(
        RouterApiClient api,
        ProviderKind provider,
        CancellationToken cancellationToken = default)
    {
        var existing = await api.ListConnectionsAsync(provider, cancellationToken);
        _workflowExistingConnections = existing.ToDictionary(connection => connection.Id, StringComparer.Ordinal);
        _currentWorkflowProvider = provider;
        WaitForConnectionCommand.RaiseCanExecuteChanged();
    }

    private async Task WaitForOAuthProxyAsync(
        RouterApiClient api,
        ProviderKind provider,
        string state,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var status = await api.GetOAuthProxyStatusAsync(provider, state, cancellationToken);
            if (string.Equals(status.Status, "done", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (string.Equals(status.Status, "error", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(status.Error ?? "OAuth authorization failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }

        throw new TimeoutException($"Hết thời gian chờ đăng nhập {ProviderCatalog.Get(provider).DisplayName}.");
    }

    private async Task RenameNewConnectionAsync(
        RouterApiClient api,
        ProviderKind provider,
        CancellationToken cancellationToken = default)
    {
        if (SelectedProfile is null)
        {
            throw new InvalidOperationException("Hãy chọn Chrome profile trước.");
        }

        var connection = await api.WaitForNewConnectionAsync(
            provider,
            _workflowExistingConnections,
            TimeSpan.FromMinutes(2),
            TimeSpan.FromSeconds(2),
            cancellationToken);
        await api.UpdateConnectionAsync(
            connection.Id,
            name: SelectedProfile.Name,
            cancellationToken: cancellationToken);
        _workflowExistingConnections[connection.Id] = connection;
        _currentWorkflowProvider = null;
        await RefreshConnectionStatusesAsync(showStatus: false);
        StatusText = $"Đã kết nối {ProviderCatalog.Get(provider).DisplayName} với profile {SelectedProfile.Name}.";
        ShowToast(StatusText, ToastType.Success, 5);
        WaitForConnectionCommand.RaiseCanExecuteChanged();
    }

    private int GetDashboardPort()
    {
        if (!Uri.TryCreate(DashboardBaseUrl, UriKind.Absolute, out var dashboardUri))
        {
            return 20128;
        }

        return dashboardUri.IsDefaultPort
            ? dashboardUri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? 443 : 80
            : dashboardUri.Port;
    }

    private async Task OpenQuickLinkAsync(ProviderKind provider)
    {
        if (SelectedProfile is null)
        {
            StatusText = "Hãy chọn Chrome profile trước.";
            ShowToast(StatusText, ToastType.Warning);
            return;
        }

        try
        {
            var definition = ProviderCatalog.Get(provider);
            if (definition.Workflow == WorkflowKind.ApiKey)
            {
                SelectedApiKeyProvider = provider;
            }
            await LaunchUrlAsync(definition.QuickLink);
            StatusText = $"Đã mở nhanh {definition.DisplayName}.";
            ShowToast(StatusText, ToastType.Success);
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }

    private async Task WaitForConnectionAsync()
    {
        if (_currentWorkflowProvider is null || SelectedProfile is null)
        {
            StatusText = "Chưa có workflow OAuth đang chờ.";
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _workflowCancellation = cancellation;
        _workflowInProgress = true;
        OnPropertyChanged(nameof(IsWorkflowInProgress));
        CancelWorkflowCommand.RaiseCanExecuteChanged();
        WaitForConnectionCommand.RaiseCanExecuteChanged();
        try
        {
            var cancellationToken = cancellation.Token;
            var provider = _currentWorkflowProvider.Value;
            var api = CreateApiClient();
            StatusText = $"Đang chờ {ProviderCatalog.Get(provider).DisplayName} báo connection mới…";
            var connection = await api.WaitForNewConnectionAsync(
                provider,
                _workflowExistingConnections,
                TimeSpan.FromMinutes(10),
                TimeSpan.FromSeconds(2),
                cancellationToken);
            await api.UpdateConnectionAsync(
                connection.Id,
                name: SelectedProfile.Name,
                cancellationToken: cancellationToken);
            _workflowExistingConnections[connection.Id] = connection;
            _currentWorkflowProvider = null;
            await RefreshConnectionStatusesAsync(showStatus: false);
            StatusText = $"Đã kết nối {ProviderCatalog.Get(provider).DisplayName} với profile {SelectedProfile.Name}.";
            ShowToast(StatusText, ToastType.Success, 5);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            _currentWorkflowProvider = null;
            _workflowExistingConnections.Clear();
            StatusText = "Đã hủy thao tác chờ connection.";
            ShowToast(StatusText, ToastType.Warning, 5);
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
        finally
        {
            if (ReferenceEquals(_workflowCancellation, cancellation))
            {
                _workflowCancellation = null;
            }

            _workflowInProgress = false;
            OnPropertyChanged(nameof(IsWorkflowInProgress));
            CancelWorkflowCommand.RaiseCanExecuteChanged();
            WaitForConnectionCommand.RaiseCanExecuteChanged();
        }
    }

    private Task LaunchUrlAsync(string url)
    {
        if (SelectedProfile is null)
        {
            throw new InvalidOperationException("Select a Chrome profile first.");
        }

        _installation ??= _chromeLocator.Find(ChromeExecutablePath, ChromeUserDataDirectory);
        if (_installation is null)
        {
            throw new InvalidOperationException("Không tìm thấy Chrome. Hãy thêm đường dẫn chrome.exe và User Data Directory.");
        }

        _chromeLauncher.Launch(_installation, SelectedProfile, url);
        return Task.CompletedTask;
    }

    private RouterApiClient CreateApiClient() => new(_httpClient, DashboardBaseUrl);

    private static bool IsManagedProfileFor(ManagedChromeProfile managedProfile, ChromeProfile profile) =>
        string.Equals(managedProfile.DirectoryName.Trim(), profile.DirectoryName.Trim(), StringComparison.OrdinalIgnoreCase)
        && PathsEqual(managedProfile.UserDataDirectory, profile.UserDataDirectory);

    private static bool PathsEqual(string left, string right)
    {
        var leftPath = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rightPath = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(leftPath, rightPath, StringComparison.OrdinalIgnoreCase);
    }

    public ICommand? ResolveShortcutCommand(string actionId) => actionId switch
    {
        "SaveSettings" => SaveSettingsCommand,
        "OpenQuickLaunch" => OpenQuickLaunchPaletteCommand,
        "ClearRecent" => ClearRecentProfilesCommand,
        "RefreshProfiles" => RefreshCommand,
        "OpenProviderCodex" => OpenProviderDashboardCommand,
        "OpenProviderKiro" => OpenProviderDashboardCommand,
        "OpenProviderOpenRouter" => OpenProviderDashboardCommand,
        "OpenProviderOllama" => OpenProviderDashboardCommand,
        "OpenProviderKimchi" => OpenProviderDashboardCommand,
        _ => null
    };

    public object? ResolveShortcutParameter(string actionId) => actionId switch
    {
        "SaveSettings" or "OpenQuickLaunch" or "ClearRecent" or "RefreshProfiles" => null,
        "OpenProviderCodex" => ProviderKind.Codex,
        "OpenProviderKiro" => ProviderKind.Kiro,
        "OpenProviderOpenRouter" => ProviderKind.OpenRouter,
        "OpenProviderOllama" => ProviderKind.Ollama,
        "OpenProviderKimchi" => ProviderKind.Kimchi,
        _ => null
    };

    private async Task ApplyShortcutAsync(string actionId)
    {
        if (string.IsNullOrWhiteSpace(actionId)) return;

        var row = _shortcutBindings.Rows.FirstOrDefault(r => r.ActionId == actionId);
        if (row is null) return;

        var error = _shortcutBindings.ValidateAndApply(actionId, row.Gesture);
        if (error is not null)
        {
            row.ErrorMessage = error;
            StatusText = error;
            return;
        }

        row.ErrorMessage = null;
        StatusText = $"Đã cập nhật phím tắt cho \"{row.DisplayName}\": {row.Gesture}.";
        OnPropertyChanged(nameof(ShortcutRows));
        await SaveSettingsAsync();
    }

    private async Task ResetAllShortcuts()
    {
        _shortcutBindings.ResetAll();
        StatusText = "Đã khôi phục phím tắt mặc định.";
        OnPropertyChanged(nameof(ShortcutRows));
        await SaveSettingsAsync();
    }

    private RouterSettings BuildSettings(WindowPlacement? windowPlacement = null)
    {
        var placement = windowPlacement ?? _savedWindowPlacement;
        return new(
        DashboardBaseUrl.Trim(),
        string.IsNullOrWhiteSpace(ChromeExecutablePath) ? null : ChromeExecutablePath.Trim(),
        string.IsNullOrWhiteSpace(ChromeUserDataDirectory) ? null : ChromeUserDataDirectory.Trim(),
        FontScale,
        UseLightTheme,
        _managedProfiles.ToArray(),
        placement?.Left,
        placement?.Top,
        placement?.Width,
        placement?.Height,
        _recentProfiles.ToArray(),
        _enableKeyboardShortcuts,
        _shortcutBindings.BuildSettingsDictionary());    }

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
        && string.Equals(ChromeExecutablePath.Trim(), _savedChromeExecutablePath, StringComparison.Ordinal)
        && string.Equals(ChromeUserDataDirectory.Trim(), _savedChromeUserDataDirectory, StringComparison.Ordinal)
        && Math.Abs(FontScale - _savedFontScale) < 0.001d
        && UseLightTheme == _savedUseLightTheme
        && IsKeyboardShortcutsEnabled == _savedEnableKeyboardShortcuts;

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
        _savedChromeExecutablePath = ChromeExecutablePath.Trim();
        _savedChromeUserDataDirectory = ChromeUserDataDirectory.Trim();
        _savedFontScale = FontScale;
        _savedUseLightTheme = UseLightTheme;
        _savedEnableKeyboardShortcuts = IsKeyboardShortcutsEnabled;
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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}























