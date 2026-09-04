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
using RouterPlus.App.Diagnostics;
using RouterPlus.Infrastructure.Chrome;
using RouterPlus.Infrastructure.Router;
using RouterPlus.Infrastructure.Diagnostics;
using RouterPlus.Infrastructure.Security;
using RouterPlus.Infrastructure.Services;
using RouterPlus.Infrastructure.Storage;
using RouterPlus.Infrastructure.Updates;
using RouterPlus.App.Services;

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
    private readonly IGoogleAccountVaultStore _googleLoginVaultStore;
    private readonly GoogleAccountVaultPaths _googleLoginVaultPaths;
    private readonly ProviderConnectionVaultStore _providerConnectionVaultStore;
    private readonly ProfileHealthService _profileHealthService;

    // Internal access for CredentialsManagerViewModel
    internal IGoogleAccountVaultStore GoogleAccountVaultStore => _googleLoginVaultStore;
    internal GoogleAccountVaultPaths GoogleAccountVaultPaths => _googleLoginVaultPaths;
    internal ProviderConnectionVaultStore ProviderConnectionVaultStore => _providerConnectionVaultStore;
    internal Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<GoogleLoginResult>> GoogleLoginAutomation => _googleLoginAutomation;
    internal Func<ChromeProfile, CodexLoginCredential, CancellationToken, Task<CodexLoginResult>> CodexLoginAutomation => _codexLoginAutomation;

    private readonly Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<GoogleLoginResult>> _googleLoginAutomation;
    private readonly Func<ChromeProfile, CodexLoginCredential, CancellationToken, Task<CodexLoginResult>> _codexLoginAutomation;
    private readonly IGoogleAuthenticationService _googleAuthenticationService;
    private readonly HttpClient _httpClient;
    private readonly IUpdateService _updateService;
    private readonly IExternalLinkLauncher _linkLauncher;
    private readonly bool _runStartupUpdateCheck;
    private readonly IReadOnlyList<ChromeProfile>? _harnessProfiles;
    private readonly bool _harnessMode;
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
    // Auto-Get-Key seam (mirrors _googleLoginAutomation): runs the Chrome-based
    // OpenRouter key flow for a profile + vault credential. Testable standalone.
    private Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<OpenRouterKeyFlowOrchestrator.OpenRouterKeyFlowResult>> _openRouterKeyFlow = null!;
    private Func<ChromeProfile, CancellationToken, Task<GoogleLoginCredential?>> _autoGetKeyCredentials = null!;
    private Func<CancellationToken, Task<OpenRouterPkceResult>> _openRouterPkceFlow = null!;
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
    private bool _isProfileSidebarCollapsed;
    private double _fontScale = 1d;
    private bool _useLightTheme = true;
    private bool _useOriginalProfileForAutoLogin = false;
    private string _savedDashboardBaseUrl = "http://localhost:20128";
    private string _savedChromeExecutablePath = string.Empty;
    private string _savedChromeUserDataDirectory = string.Empty;
    private double _savedFontScale = 1d;
    private bool _savedUseLightTheme = true;
    private bool _savedUseOriginalProfileForAutoLogin = false;
    private WindowPlacement? _savedWindowPlacement;
    private string _connectionStatusText = "Chưa đồng bộ trạng thái provider.";
    private string _statusText = "Đang khởi tạo…";
    private readonly Queue<string> _logEntries = new();
    private string _logText = "Chưa có log.";
    private ToastNotification? _currentToast;
    private IReadOnlyList<QuotaAutoDisableMarker> _quotaAutoDisableMarkers = Array.Empty<QuotaAutoDisableMarker>();
    private readonly ObservableCollection<QuotaResetSuggestion> _quotaResetSuggestions = new();
    private bool _quotaMarkersLoaded;
    private readonly SemaphoreSlim _connectionRefreshGate = new(1, 1);
    private readonly QuotaPollingService _quotaPollingService;
    private readonly List<(ChromeManagedSession Session, IGoogleLoginBrowser Browser)> _googleLoginSessions = new();
    private bool _isMultiSelectMode;
    private CancellationTokenSource? _batchLoginCts;
    private readonly object _initializationLock = new();
    private Task? _initializationTask;
    private bool _isInitialized;
    private readonly TaskCompletionSource<bool> _initializationCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);


    public MainViewModel(
        SettingsStore? settingsStore = null,
        ChromeProfileProvisioner? profileProvisioner = null,
        ChromeProfileDeleter? profileDeleter = null,
        HttpClient? httpClient = null,
        IUpdateService? updateService = null,
        IExternalLinkLauncher? linkLauncher = null,
        bool runStartupUpdateCheck = false,
        IGoogleAccountVaultStore? googleLoginVaultStore = null,
        GoogleAccountVaultPaths? googleLoginVaultPaths = null,
        Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<GoogleLoginResult>>? googleLoginAutomation = null,
        IReadOnlyList<ChromeProfile>? harnessProfiles = null,
        IGoogleAuthenticationService? googleAuthenticationService = null,
        ProfileHealthService? profileHealthService = null)
    {
        _settingsStore = settingsStore ?? new SettingsStore();
        _profileProvisioner = profileProvisioner ?? new ChromeProfileProvisioner();
        _profileDeleter = profileDeleter ?? new ChromeProfileDeleter();
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _updateService = updateService ?? new SelfUpdateService(_httpClient, ApplicationInfo.CurrentVersion);
        _linkLauncher = linkLauncher ?? new ShellLinkLauncher();
        _runStartupUpdateCheck = runStartupUpdateCheck;
        _harnessProfiles = harnessProfiles;
        _harnessMode = harnessProfiles is not null;
        _googleLoginVaultPaths = googleLoginVaultPaths ?? new GoogleAccountVaultPaths();
        _googleLoginVaultStore = googleLoginVaultStore ?? new GoogleAccountVaultStore(_googleLoginVaultPaths);

        // Provider connection vault for new auth config system
        var providerConnectionPath = Path.Combine(
            Path.GetDirectoryName(_googleLoginVaultPaths.VaultPath) ?? string.Empty,
            "provider-connections.vault");
        _providerConnectionVaultStore = new ProviderConnectionVaultStore(providerConnectionPath);
        _googleAuthenticationService = googleAuthenticationService ?? new GoogleAuthenticationService();
        _profileHealthService = profileHealthService ?? new ProfileHealthService();
        _googleLoginAutomation = googleLoginAutomation ?? CreateDefaultGoogleLoginAutomation();
        _codexLoginAutomation = CreateDefaultCodexLoginAutomation();
        _openRouterKeyFlow = CreateDefaultOpenRouterKeyFlow();
        _autoGetKeyCredentials = CreateDefaultAutoGetKeyCredentials();
        _openRouterPkceFlow = CreateDefaultOpenRouterPkceFlow();
        _quotaPollingService = new QuotaPollingService(
            RefreshForQuotaPollingAsync,
            QuotaPollingOptions.Default);
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
        RefreshConnectionStatusesCommand = new AsyncRelayCommand(() => RefreshConnectionStatusesAsync());
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync, CanSaveSettings);
        AddProfileCommand = new AsyncRelayCommand(AddProfileAsync, () => CanAddProfile);
        ClearProfileSearchCommand = new AsyncRelayCommand(ClearProfileSearchAsync, () => CanClearProfileSearch);
        ToggleMultiSelectModeCommand = new RelayCommand(ToggleMultiSelectMode);
        ClearSelectionCommand = new RelayCommand(ClearSelection);
        ToggleSelectAllCommand = new RelayCommand(ToggleSelectAll);
        SelectProfilesWithVaultCommand = new AsyncRelayCommand(() => SelectProfilesWithVaultCredentialsAsync());
        StartBatchAutoLoginCommand = new AsyncRelayCommand(StartBatchAutoLoginAsync, () => HasSelectedProfiles && !IsBatchLoginRunning);
        StopBatchLoginCommand = new RelayCommand(StopBatchLogin, () => IsBatchLoginRunning);
        CloseBatchProgressCommand = new RelayCommand(CloseBatchProgress, () => !IsBatchLoginRunning);
        LaunchSelectedCommand = new AsyncRelayCommand(LaunchSelectedProfileAsync, () => SelectedProfile is not null);
        LaunchProfileCommand = new AsyncRelayCommand<ChromeProfile>(LaunchProfileAsync);
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
        DeleteConnectionCommand = new AsyncRelayCommand<ProviderKind>(DeleteConnectionAsync, _ => SelectedProfile is not null);
        OpenQuickLinkCommand = new AsyncRelayCommand<ProviderKind>(OpenQuickLinkAsync);
        AutoGetKeyCommand = new AsyncRelayCommand(() => AutoGetKeyAsync(), () => SelectedProfile is not null);
        ConnectOpenRouterOAuthCommand = new AsyncRelayCommand(
            () => ConnectOpenRouterOAuthAsync(),
            () => SelectedProfile is not null && !_workflowInProgress);
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
        CheckAllProfilesHealthCommand = new AsyncRelayCommand(
            CheckAllProfilesHealthAsync,
            () => ProfileRows.Any());
        CheckProfileHealthCommand = new AsyncRelayCommand<ProfileRowViewModel>(
            CheckProfileHealthAsync,
            row => row != null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ChromeProfile> Profiles { get; }

    public ObservableCollection<ChromeProfile> FilteredProfiles { get; }

    public ObservableCollection<ProfileRowViewModel> ProfileRows { get; }

    public ObservableCollection<ProfileRowViewModel> FilteredProfileRows { get; }

    public ObservableCollection<ChromeProfile> RecentProfilesList { get; } = new();

    public ObservableCollection<RecentProfileRowViewModel> RecentProfileRows { get; } = new();

    public ObservableCollection<ChromeProfile> FilteredQuickLaunchProfiles { get; } = new();

    public bool IsQuickLaunchFeatureEnabled => false;

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
            using var perf = DebugLogger.MeasurePerformance(DiagnosticCategories.ViewModel, "SelectedProfile.set");

            if (Equals(_selectedProfile, value))
            {
                DebugLogger.Log(DiagnosticCategories.ViewModel, "SelectedProfile unchanged, skipping");
                return;
            }

            DebugLogger.Log(DiagnosticCategories.ViewModel, $"SelectedProfile changing to: {value?.Name ?? "null"}");
            _selectedProfile = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedProfileRow));
            UpdateProviderCardStatuses();
            LaunchSelectedCommand.RaiseCanExecuteChanged();
            OpenProviderDashboardCommand.RaiseCanExecuteChanged();
            TestConnectionCommand.RaiseCanExecuteChanged();
            DeleteConnectionCommand.RaiseCanExecuteChanged();
            WaitForConnectionCommand.RaiseCanExecuteChanged();
            AutoGetKeyCommand.RaiseCanExecuteChanged();
            ConnectOpenRouterOAuthCommand.RaiseCanExecuteChanged();
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

    public bool IsMultiSelectMode
    {
        get => _isMultiSelectMode;
        set
        {
            if (_isMultiSelectMode == value)
            {
                return;
            }

            _isMultiSelectMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedProfiles));

            // Clear selections when exiting multi-select mode
            if (!value)
            {
                foreach (var row in ProfileRows)
                {
                    row.IsSelected = false;
                }
            }
        }
    }

    public IEnumerable<ProfileRowViewModel> SelectedProfileRows =>
        ProfileRows.Where(row => row.IsSelected);

    public bool HasSelectedProfiles => _isMultiSelectMode && SelectedProfileRows.Any();

    public string SelectedProfilesText
    {
        get
        {
            var count = SelectedProfileRows.Count();
            return count == 1 ? "1 profile đã chọn" : $"{count} profiles đã chọn";
        }
    }

    // Batch Phase 3: Progress tracking
    public ObservableCollection<BatchLoginProgressRow> BatchProgressRows { get; } = new();

    private bool _isBatchLoginRunning;
    public bool IsBatchLoginRunning
    {
        get => _isBatchLoginRunning;
        set
        {
            if (_isBatchLoginRunning == value) return;
            _isBatchLoginRunning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BatchProgressSummary));
        }
    }

    public string BatchProgressSummary
    {
        get
        {
            var total = BatchProgressRows.Count;
            var completed = BatchProgressRows.Count(r =>
                r.State == BatchLoginState.Success ||
                r.State == BatchLoginState.Failed ||
                r.State == BatchLoginState.Skipped);
            var success = BatchProgressRows.Count(r => r.State == BatchLoginState.Success);
            var failed = BatchProgressRows.Count(r => r.State == BatchLoginState.Failed);
            return $"{completed}/{total} · {success} thành công · {failed} lỗi";
        }
    }

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

    public Dictionary<ProviderKind, ProviderFilterState> ProviderFilterStates { get; } = new();

    public AsyncRelayCommand<ProviderKind> ToggleProviderCommand { get; }

    public AsyncRelayCommand ToggleUnassignedProfilesCommand { get; }

    public bool IsUnassignedProfileFilterActive { get; private set; }

    public void ToggleProvider(ProviderKind kind)
    {
        if (!_providerOptionByKind.TryGetValue(kind, out var option))
        {
            return;
        }

        // Cycle through 3 states: Off -> Has -> NotHas -> Off
        option.CycleFilterState();

        // Update dictionaries based on new state
        var newState = option.FilterState;
        if (newState == ProviderFilterState.Off)
        {
            SelectedProviderKinds.Remove(kind);
            ProviderFilterStates.Remove(kind);
        }
        else
        {
            SelectedProviderKinds.Add(kind);
            ProviderFilterStates[kind] = newState;
        }

        IsUnassignedProfileFilterActive = false;
        NotifyProviderFilterChanged();
    }

    public void ToggleUnassignedProfiles()
    {
        IsUnassignedProfileFilterActive = !IsUnassignedProfileFilterActive;
        if (IsUnassignedProfileFilterActive)
        {
            SelectedProviderKinds.Clear();
            ProviderFilterStates.Clear();
            foreach (var option in ProviderFilterOptions)
            {
                option.FilterState = ProviderFilterState.Off;
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
        ProviderFilterStates.Clear();
        IsUnassignedProfileFilterActive = false;
        foreach (var option in ProviderFilterOptions)
        {
            option.FilterState = ProviderFilterState.Off;
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

    private void UpdateProviderFilterCounts()
    {
        var rowsByProfileId = ProfileRows.ToDictionary(row => row.Profile.Id, StringComparer.Ordinal);
        foreach (var option in ProviderFilterOptions)
        {
            if (option.Kind is not { } kind)
            {
                continue;
            }

            var hasCount = ProfileRows.Count(row =>
                row.ProviderStatuses.Any(status =>
                    status.Definition.Kind == kind && status.IsConnected));
            option.SetProfileCounts(hasCount, ProfileRows.Count - hasCount);
            option.SetProfileCount(hasCount);
        }
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
            var api = CreateApiClient();
            var current = (await api.ListAllConnectionsAsync(cancellationToken))
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
            await RefreshConnectionStatusesAsync(showStatus: false, cancellationToken: cancellationToken);
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
    public AsyncRelayCommand AutoGetKeyCommand { get; }
    public AsyncRelayCommand ConnectOpenRouterOAuthCommand { get; }
    public AsyncRelayCommand StartBatchAutoLoginCommand { get; }
    public RelayCommand StopBatchLoginCommand { get; }
    public RelayCommand CloseBatchProgressCommand { get; }

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
        using var perf = DebugLogger.MeasurePerformance(DiagnosticCategories.Chrome, "LaunchProfile");
        if (profile is null)
        {
            StatusText = "Profile không hợp lệ.";
            return;
        }

        try
        {
            // Set as selected and track
            DebugLogger.Log(DiagnosticCategories.Chrome, $"Launching profile: {profile.Name}");
            SelectedProfile = profile;
            TrackProfileLaunch(profile);
            await LaunchUrlAsync(DashboardBaseUrl);
            DebugLogger.Log(DiagnosticCategories.Chrome, $"Profile launch completed: {profile.Name}");
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
        using var perf = DebugLogger.MeasurePerformance(DiagnosticCategories.Startup, "MainViewModel.InitializeAsync");
        DebugLogger.Log(DiagnosticCategories.Startup, "Loading application settings and profiles");
        try
        {
            var settings = await _settingsStore.LoadAsync();
            _quotaAutoDisableMarkers = settings.QuotaAutoDisableMarkers ?? [];
            _quotaMarkersLoaded = true;
            DashboardBaseUrl = settings.DashboardBaseUrl;
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
            _managedProfiles.Clear();
            _managedProfiles.AddRange(settings.ManagedProfiles ?? []);
            _recentProfiles.Clear();
            _recentProfiles.AddRange(settings.RecentProfiles ?? []);
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
                await RefreshConnectionStatusesAsync(showStatus: true);
            }
            _isInitialized = true;
            _initializationCompletion.TrySetResult(true);
            DebugLogger.Log(DiagnosticCategories.Startup, $"Initialization completed: {Profiles.Count} profiles");
            if (_runStartupUpdateCheck)
            {
                _ = RunStartupUpdateCheckAsync();
            }
        }
        catch (Exception exception)
        {
            DebugLogger.LogError(DiagnosticCategories.Startup, "Initialization failed", exception);
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

    /// <summary>
    /// Check health status for all profiles.
    /// </summary>
    private async Task CheckAllProfilesHealthAsync()
    {
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
    /// Check health status for a single profile.
    /// </summary>
    private async Task CheckProfileHealthAsync(ProfileRowViewModel? row)
    {
        if (row == null) return;

        var status = await _profileHealthService.GetHealthStatusAsync(
            row.Profile,
            forceRefresh: true);
        row.HealthStatus = status;
    }

    public async Task CheckForUpdatesAsync()
    {
        using var perf = DebugLogger.MeasurePerformance(DiagnosticCategories.Updates, "CheckForUpdatesAsync");
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
            DebugLogger.Log(DiagnosticCategories.Updates, $"Update check completed; available: {release.IsUpdateAvailable}");
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
        using var perf = DebugLogger.MeasurePerformance(DiagnosticCategories.Updates, "InstallUpdateAsync");
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
            DebugLogger.Log(DiagnosticCategories.Updates, "Downloading and staging update package");
            var package = await _updateService.DownloadAndStageAsync(_latestRelease);
            DebugLogger.Log(DiagnosticCategories.Updates, "Update package staged; launching updater");
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
        using var perf = DebugLogger.MeasurePerformance(DiagnosticCategories.Chrome, "RefreshProfiles");
        var previousProfileId = SelectedProfile?.Id;

        if (_harnessMode)
        {
            RefreshHarnessProfiles(previousProfileId);
            return;
        }
        _installation = _chromeLocator.Find(
            string.IsNullOrWhiteSpace(ChromeExecutablePath) ? null : ChromeExecutablePath,
            string.IsNullOrWhiteSpace(ChromeUserDataDirectory) ? null : ChromeUserDataDirectory);

        if (_installation is null)
        {
            DebugLogger.LogWarning(DiagnosticCategories.Chrome, "Profile refresh could not find a Chrome installation");
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
        DebugLogger.Log(DiagnosticCategories.Chrome, $"Profile refresh discovered {profiles.Count} profiles");
        Profiles.Clear();
        ProfileRows.Clear();
        foreach (var profile in profiles)
        {
            Profiles.Add(profile);
            var row = new ProfileRowViewModel(profile, Providers);
            row.SelectionChanged += OnProfileSelectionChanged;
            ProfileRows.Add(row);
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

    private void RefreshHarnessProfiles(string? previousProfileId)
    {
        var profiles = _harnessProfiles ?? Array.Empty<ChromeProfile>();
        Profiles.Clear();
        ProfileRows.Clear();
        foreach (var profile in profiles)
        {
            Profiles.Add(profile);
            var row = new ProfileRowViewModel(profile, Providers);
            row.SelectionChanged += OnProfileSelectionChanged;
            ProfileRows.Add(row);
        }

        ApplyProfileFilter();
        SelectedProfile = Profiles.FirstOrDefault(profile => profile.Id == previousProfileId)
            ?? Profiles.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedProfileRow));
        UpdateProviderCardStatuses();
        LaunchSelectedCommand.RaiseCanExecuteChanged();
        OpenProviderDashboardCommand.RaiseCanExecuteChanged();
        TestConnectionCommand.RaiseCanExecuteChanged();
        WaitForConnectionCommand.RaiseCanExecuteChanged();
        DebugLogger.Log(DiagnosticCategories.Chrome, $"Harness profile refresh loaded {profiles.Count} synthetic profiles");
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
        var hasProviderFilter = ProviderFilterStates.Count > 0;
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
            if (hasProviderFilter)
            {
                // Check all provider filters - ALL must match (AND logic)
                var passesAllFilters = true;
                foreach (var (kind, state) in ProviderFilterStates)
                {
                    var hasConnection = row.ProviderStatuses.Any(status =>
                        status.Definition.Kind == kind && status.IsConnected);

                    if (state == ProviderFilterState.Has && !hasConnection)
                    {
                        passesAllFilters = false;
                        break;
                    }
                    if (state == ProviderFilterState.NotHas && hasConnection)
                    {
                        passesAllFilters = false;
                        break;
                    }
                }

                if (!passesAllFilters)
                {
                    continue;
                }
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

    private void ToggleMultiSelectMode()
    {
        IsMultiSelectMode = !IsMultiSelectMode;
    }

    private void ClearSelection()
    {
        foreach (var row in ProfileRows)
        {
            row.IsSelected = false;
        }
        OnPropertyChanged(nameof(HasSelectedProfiles));
        OnPropertyChanged(nameof(SelectedProfilesText));
    }

    private void OnProfileSelectionChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(HasSelectedProfiles));
        OnPropertyChanged(nameof(SelectedProfilesText));
        OnPropertyChanged(nameof(AreAllProfilesSelected));
        OnPropertyChanged(nameof(SelectAllButtonText));
    }

    /// <summary>
    /// Toggle select/deselect all profiles.
    /// If any profile is unselected → select all.
    /// If all profiles are selected → deselect all.
    /// </summary>
    private void ToggleSelectAll()
    {
        // Auto-enable multi-select mode
        if (!_isMultiSelectMode)
        {
            IsMultiSelectMode = true;
        }

        // Check if all are currently selected
        bool allSelected = ProfileRows.All(row => row.IsSelected);

        // Toggle: if all selected, deselect all; otherwise select all
        bool newState = !allSelected;
        foreach (var row in ProfileRows)
        {
            row.IsSelected = newState;
        }

        OnPropertyChanged(nameof(HasSelectedProfiles));
        OnPropertyChanged(nameof(SelectedProfilesText));
    }

    public bool AreAllProfilesSelected => ProfileRows.Any() && ProfileRows.All(row => row.IsSelected);

    public string SelectAllButtonText => AreAllProfilesSelected ? "☐  Bỏ chọn tất cả" : "☑  Chọn tất cả";

    /// <summary>
    /// Batch Phase 2: Check if a profile has vault credentials for any provider.
    /// Used to filter profiles eligible for batch auto-login.
    /// </summary>
    public async Task<bool> HasVaultCredentialsAsync(
        ChromeProfile profile,
        CancellationToken cancellationToken = default)
    {
        if (profile is null)
        {
            return false;
        }

        try
        {
            // Check all providers - returns true if any provider has credentials
            foreach (ProviderKind kind in Enum.GetValues(typeof(ProviderKind)))
            {
                if (kind == ProviderKind.Ollama || kind == ProviderKind.Kimchi)
                {
                    // Skip providers without auto-login support
                    continue;
                }

                var hasCreds = await _providerConnectionVaultStore.HasCredentialsAsync(
                    profile.Name,
                    kind,
                    cancellationToken);

                if (hasCreds)
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            DebugLogger.LogError(
                DiagnosticCategories.ViewModel,
                $"Failed to check vault credentials for {profile.Name}: {ex.Message}",
                ex);
            return false;
        }
    }

    /// <summary>
    /// Batch Phase 2: Select only profiles that have vault credentials configured.
    /// Useful for quick batch auto-login setup.
    /// </summary>
    public async Task SelectProfilesWithVaultCredentialsAsync(
        CancellationToken cancellationToken = default)
    {
        if (!_isMultiSelectMode)
        {
            IsMultiSelectMode = true;
        }

        int selectedCount = 0;
        foreach (var row in ProfileRows)
        {
            var hasCreds = await HasVaultCredentialsAsync(row.Profile, cancellationToken);
            row.IsSelected = hasCreds;
            if (hasCreds)
            {
                selectedCount++;
            }
        }

        OnPropertyChanged(nameof(HasSelectedProfiles));
        OnPropertyChanged(nameof(SelectedProfilesText));
        StatusText = selectedCount > 0
            ? $"Đã chọn {selectedCount} profile có vault credentials"
            : "Không có profile nào có vault credentials";
    }

    /// <summary>
    /// Batch Phase 4: Start batch auto-login for all selected profiles.
    /// Sequential execution with auto-skip, continue-on-failure, and 2s delays.
    /// </summary>
    private async Task StartBatchAutoLoginAsync()
    {
        var profiles = SelectedProfileRows.Select(r => r.Profile).ToArray();
        if (profiles.Length == 0)
        {
            return;
        }

        // Setup
        IsBatchLoginRunning = true;
        BatchProgressRows.Clear();
        _batchLoginCts = new CancellationTokenSource();
        var ct = _batchLoginCts.Token;

        try
        {
            foreach (var profile in profiles)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                var row = new BatchLoginProgressRow(profile)
                {
                    State = BatchLoginState.InProgress,
                    StatusMessage = "Đang kiểm tra vault..."
                };
                BatchProgressRows.Add(row);
                OnPropertyChanged(nameof(BatchProgressSummary));

                var sw = System.Diagnostics.Stopwatch.StartNew();

                try
                {
                    // Check vault credentials first
                    var hasCreds = await HasVaultCredentialsAsync(profile, ct);
                    if (!hasCreds)
                    {
                        row.State = BatchLoginState.Skipped;
                        row.StatusMessage = "Không có vault credentials";
                        row.Duration = sw.Elapsed;
                        OnPropertyChanged(nameof(BatchProgressSummary));
                        continue;
                    }

                    row.StatusMessage = "Đang đăng nhập...";

                    // Try login with each provider that has credentials
                    var anySuccess = await TryLoginProfileAllProvidersAsync(profile, row, ct);
                    row.Duration = sw.Elapsed;

                    if (anySuccess)
                    {
                        row.State = BatchLoginState.Success;
                    }
                    else if (row.State == BatchLoginState.InProgress)
                    {
                        row.State = BatchLoginState.Failed;
                        if (string.IsNullOrEmpty(row.StatusMessage) || row.StatusMessage == "Đang đăng nhập...")
                        {
                            row.StatusMessage = "Không thể đăng nhập";
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    row.State = BatchLoginState.Failed;
                    row.StatusMessage = "Đã hủy";
                    row.Duration = sw.Elapsed;
                    throw;
                }
                catch (Exception ex)
                {
                    row.State = BatchLoginState.Failed;
                    row.StatusMessage = ex.Message;
                    row.Duration = sw.Elapsed;
                    DebugLogger.LogError(
                        DiagnosticCategories.ViewModel,
                        $"Batch login failed for {profile.Name}: {ex.Message}",
                        ex);
                }
                finally
                {
                    OnPropertyChanged(nameof(BatchProgressSummary));
                }

                // 2s delay between profiles (skip if cancelled)
                if (!ct.IsCancellationRequested && profile != profiles[^1])
                {
                    await Task.Delay(2000, ct);
                }
            }

            StatusText = ct.IsCancellationRequested
                ? "Đã hủy batch auto-login"
                : $"Hoàn thành batch: {BatchProgressSummary}";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Batch auto-login đã bị hủy";
        }
        catch (Exception ex)
        {
            StatusText = $"Lỗi batch: {ex.Message}";
            DebugLogger.LogError(
                DiagnosticCategories.ViewModel,
                $"Batch auto-login failed: {ex.Message}",
                ex);
        }
        finally
        {
            IsBatchLoginRunning = false;
            _batchLoginCts?.Dispose();
            _batchLoginCts = null;
            StartBatchAutoLoginCommand.RaiseCanExecuteChanged();
            StopBatchLoginCommand.RaiseCanExecuteChanged();
        }
    }

    private void StopBatchLogin()
    {
        _batchLoginCts?.Cancel();
    }

    private void CloseBatchProgress()
    {
        // Exit multi-select mode after batch completes
        IsMultiSelectMode = false;
        BatchProgressRows.Clear();
        OnPropertyChanged(nameof(BatchProgressSummary));
    }

    /// <summary>
    /// Try auto-login for a profile across all providers that have credentials.
    /// Returns true if any provider login succeeds.
    /// </summary>
    private async Task<bool> TryLoginProfileAllProvidersAsync(
        ChromeProfile profile,
        BatchLoginProgressRow row,
        CancellationToken ct)
    {
        if (_installation is null)
        {
            row.StatusMessage = "Chrome chưa được cấu hình";
            return false;
        }

        bool anySuccess = false;

        foreach (ProviderKind kind in Enum.GetValues(typeof(ProviderKind)))
        {
            if (ct.IsCancellationRequested)
            {
                return anySuccess;
            }

            // Skip providers without auto-login support
            if (kind == ProviderKind.Ollama || kind == ProviderKind.Kimchi)
            {
                continue;
            }

            bool hasCreds = false;
            try
            {
                hasCreds = await _providerConnectionVaultStore.HasCredentialsAsync(
                    profile.Name, kind, ct);
            }
            catch
            {
                continue;
            }

            if (!hasCreds)
            {
                continue;
            }

            row.StatusMessage = $"Đang đăng nhập {kind}...";

            try
            {
                var startUri = GetProviderLoginUri(kind);
                var result = await RunAutoLoginWithOrchestratorAsync(profile, kind, startUri, ct);

                if (result.Success)
                {
                    row.StatusMessage = $"Thành công ({kind})";
                    anySuccess = true;
                    // Stop trying other providers once one succeeds
                    break;
                }
                else
                {
                    row.StatusMessage = $"{kind}: {result.ErrorMessage}";
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                row.StatusMessage = $"{kind}: {ex.Message}";
                DebugLogger.LogError(
                    DiagnosticCategories.ViewModel,
                    $"Provider {kind} login failed for {profile.Name}: {ex.Message}",
                    ex);
            }
        }

        return anySuccess;
    }

    /// <summary>
    /// Get the login URL for a provider.
    /// </summary>
    private static Uri GetProviderLoginUri(ProviderKind provider)
    {
        return provider switch
        {
            ProviderKind.Codex => new Uri("https://chatgpt.com/"),
            ProviderKind.Kiro => new Uri("https://view.awsapps.com/"),
            ProviderKind.GitHub => new Uri("https://github.com/login"),
            ProviderKind.OpenRouter => new Uri("https://openrouter.ai/"),
            _ => new Uri("https://chatgpt.com/")
        };
    }

    private void Profiles_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CanAddProfile));
        OnPropertyChanged(nameof(ProfileAddButtonText));
        AddProfileCommand.RaiseCanExecuteChanged();
        UpdateRecentProfilesList();
    }

    public Task RefreshConnectionStatusesAsync(CancellationToken cancellationToken = default) =>
        RefreshConnectionStatusesAsync(showStatus: true, forceLog: true, cancellationToken);

    public void StartQuotaPolling() => _quotaPollingService.Start();

    public void PauseQuotaPolling() => _quotaPollingService.Pause();

    public Task ResumeQuotaPollingAsync() => _quotaPollingService.ResumeAsync();

    public Task StopQuotaPollingAsync() => _quotaPollingService.StopAsync();

    public async Task DisposeGoogleLoginSessionsAsync()
    {
        foreach (var (session, browser) in _googleLoginSessions.ToArray())
        {
            try
            {
                await browser.DisposeAsync();
            }
            catch
            {
                // Best effort cleanup.
            }

            try
            {
                await session.DisposeAsync();
            }
            catch
            {
                // Best effort cleanup.
            }
        }

        _googleLoginSessions.Clear();
    }

    private async Task<bool> RefreshForQuotaPollingAsync(CancellationToken cancellationToken)
    {
        await RefreshConnectionStatusesAsync(showStatus: false, forceLog: false, cancellationToken);
        return _lastReliableNearLimit;
    }

    private bool _lastReliableNearLimit;

    private async Task RefreshConnectionStatusesAsync(
        bool showStatus,
        bool forceLog = false,
        CancellationToken cancellationToken = default)
    {
        using var perf = DebugLogger.MeasurePerformance(DiagnosticCategories.Providers, "RefreshConnectionStatusesAsync");
        DebugLogger.Log(DiagnosticCategories.Providers, $"Provider sync requested; profiles: {ProfileRows.Count}");
        await _connectionRefreshGate.WaitAsync(cancellationToken);
        try
        {
            await RefreshConnectionStatusesCoreAsync(showStatus, forceLog, cancellationToken);
        }
        finally
        {
            _connectionRefreshGate.Release();
        }
    }

    private async Task LoadSelectedProfileApiKeysAsync()
    {
        using var perf = DebugLogger.MeasurePerformance(DiagnosticCategories.Security, "LoadSelectedProfileApiKeysAsync");
        var loadVersion = Interlocked.Increment(ref _apiKeyLoadVersion);
        var profile = SelectedProfile;
        DebugLogger.Log(DiagnosticCategories.Security, $"Loading saved provider credentials for profile selected: {profile is not null}");
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

            DebugLogger.Log(DiagnosticCategories.Security, $"Saved provider credentials loaded for {values.Length} providers");
        }
        catch (Exception exception)
        {
            DebugLogger.LogError(DiagnosticCategories.Security, "Saved provider credentials could not be loaded", exception);
            if (loadVersion == Volatile.Read(ref _apiKeyLoadVersion))
            {
                AppendLog("WARN", $"Không thể đọc API key đã lưu: {SafeError(exception)}");
            }
        }
    }

    private async Task RefreshConnectionStatusesCoreAsync(
        bool showStatus,
        bool forceLog = false,
        CancellationToken cancellationToken = default)
    {
        if (ProfileRows.Count == 0)
        {
            _lastReliableNearLimit = false;
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
            var api = CreateApiClient();
            DebugLogger.Log(DiagnosticCategories.Providers, "Loading provider connections");
            var connections = await api.ListAllConnectionsAsync(cancellationToken);
            DebugLogger.Log(DiagnosticCategories.Providers, $"Provider connections loaded: {connections.Count}");
            var exhaustedConnections = connections
                .Where(connection =>
                    connection.IsActive &&
                    QuotaAutoDisablePolicy.CanAutoDisable(connection))
                .ToArray();
            _lastReliableNearLimit = connections.Any(connection =>
                connection.QuotaRows.Count > 0 && connection.IsNearLimit);

            await LoadQuotaAutoDisableMarkersAsync();
            var disableFailures = 0;
            foreach (var connection in exhaustedConnections)
            {
                try
                {
                    await api.UpdateConnectionAsync(
                        connection.Id,
                        isActive: false,
                        cancellationToken: cancellationToken);
                    UpsertQuotaAutoDisableMarker(connection);
                    await SaveQuotaAutoDisableMarkersAsync(cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    disableFailures++;
                    AppendLog("WARN", $"Không thể tự tắt connection {connection.Id}: {SafeError(exception)}");
                }
            }

            if (exhaustedConnections.Length > 0)
            {
                connections = await api.ListAllConnectionsAsync(cancellationToken);
                await SaveQuotaAutoDisableMarkersAsync(cancellationToken);
            }

            await UpdateQuotaResetSuggestionsAsync(connections, cancellationToken);

            foreach (var row in ProfileRows)
            {
                row.UpdateConnections(connections);
            }

            OnPropertyChanged(nameof(SelectedProfileRow));
            UpdateProviderCardStatuses();
            UpdateProviderFilterCounts();
            ApplyProfileFilter();

            var matchedProfiles = ProfileRows.Count(row => row.ConnectedProviderCount > 0);
            ConnectionStatusText = disableFailures > 0
                ? $"Đã đồng bộ {connections.Count} connection · tắt tự động thất bại {disableFailures} connection."
                : $"Đã đồng bộ {connections.Count} connection · {matchedProfiles}/{ProfileRows.Count} profile có provider.";
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _lastReliableNearLimit = false;
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

    /// <summary>
    /// Injectable seam for the Chrome-based OpenRouter key flow (mirrors
    /// <see cref="GoogleLoginAutomation"/>), so tests can drive the flow
    /// without launching a browser.
    /// </summary>
    public Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<OpenRouterKeyFlowOrchestrator.OpenRouterKeyFlowResult>> OpenRouterKeyFlow
    {
        get => _openRouterKeyFlow;
        set => _openRouterKeyFlow = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Injectable seam for loading the Google login credential (from the
    /// Google account vault) used by <see cref="AutoGetKeyAsync"/>.
    /// </summary>
    public Func<ChromeProfile, CancellationToken, Task<GoogleLoginCredential?>> AutoGetKeyCredentials
    {
        get => _autoGetKeyCredentials;
        set => _autoGetKeyCredentials = value ?? throw new ArgumentNullException(nameof(value));
    }

    public Func<CancellationToken, Task<OpenRouterPkceResult>> OpenRouterPkceFlow
    {
        get => _openRouterPkceFlow;
        set => _openRouterPkceFlow = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>
    /// Auto-get an OpenRouter API key: run the key flow (opening Chrome, Google
    /// sign-in from the vault, onboarding), then automatically save the returned
    /// key into 9Router for the selected profile.
    /// </summary>
    public async Task<bool> AutoGetKeyAsync(CancellationToken cancellationToken = default)
    {
        var profile = SelectedProfile;
        if (profile is null)
        {
            StatusText = "Hãy chọn Chrome profile trước.";
            ShowToast(StatusText, ToastType.Warning);
            return false;
        }

        var credential = await _autoGetKeyCredentials(profile, cancellationToken);
        if (credential is null)
        {
            StatusText = "Không có thông tin Google trong Vault cho profile này.";
            ShowToast(StatusText, ToastType.Warning);
            return false;
        }

        var flowResult = await _openRouterKeyFlow(profile, credential, cancellationToken);
        if (!flowResult.Success)
        {
            StatusText = $"Không lấy được OpenRouter key: {flowResult.ErrorMessage}";
            ShowToast(StatusText, ToastType.Error);
            return false;
        }

        SelectedApiKeyProvider = ProviderKind.OpenRouter;
        return await AddApiKeyAsync(SelectedApiKeyProvider, flowResult.ApiKey!);
    }

    public async Task<bool> ConnectOpenRouterOAuthAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedProfile is null)
        {
            StatusText = "Hãy chọn Chrome profile trước.";
            ShowToast(StatusText, ToastType.Warning);
            return false;
        }

        if (_workflowInProgress)
        {
            return false;
        }

        using var workflowCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _workflowCancellation = workflowCancellation;
        _workflowInProgress = true;
        var card = GetProviderCard(ProviderKind.OpenRouter);
        card.SetWorkflowInProgress(true);
        OnPropertyChanged(nameof(IsWorkflowInProgress));
        CancelWorkflowCommand.RaiseCanExecuteChanged();
        ConnectOpenRouterOAuthCommand.RaiseCanExecuteChanged();

        try
        {
            StatusText = "Đang mở OpenRouter OAuth…";
            OpenRouterPkceResult flowResult;
            try
            {
                flowResult = await _openRouterPkceFlow(workflowCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                StatusText = "Đã hủy OpenRouter OAuth.";
                ShowToast(StatusText, ToastType.Warning);
                return false;
            }
            catch (TimeoutException)
            {
                StatusText = "Hết thời gian chờ OpenRouter OAuth. Thử lại.";
                ShowToast(StatusText, ToastType.Error);
                return false;
            }
            catch (Exception exception)
            {
                StatusText = $"OpenRouter OAuth thất bại: {exception.Message}";
                ShowToast(StatusText, ToastType.Error);
                return false;
            }

            if (!flowResult.Success)
            {
                StatusText = $"OpenRouter OAuth thất bại: {flowResult.ErrorMessage}";
                ShowToast(StatusText, ToastType.Error);
                return false;
            }

            SelectedApiKeyProvider = ProviderKind.OpenRouter;
            return await AddApiKeyAsync(ProviderKind.OpenRouter, flowResult.ApiKey!);
        }
        finally
        {
            if (ReferenceEquals(_workflowCancellation, workflowCancellation))
            {
                _workflowCancellation = null;
            }

            _workflowInProgress = false;
            card.SetWorkflowInProgress(false);
            OnPropertyChanged(nameof(IsWorkflowInProgress));
            CancelWorkflowCommand.RaiseCanExecuteChanged();
            ConnectOpenRouterOAuthCommand.RaiseCanExecuteChanged();
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
        using var perf = DebugLogger.MeasurePerformance(DiagnosticCategories.ViewModel, "UpdateProviderCardStatuses");
        var row = SelectedProfileRow;
        foreach (var card in ProviderCards)
        {
            var status = row?.ProviderStatuses.FirstOrDefault(item => item.Definition.Kind == card.Kind);
            card.UpdateProviderStatus(status);
        }
    }

    private async Task OpenProviderAsync(ProviderKind provider)
    {
        // Prevent race condition from double-click
        if (_workflowInProgress)
        {
            return;
        }

        using var perf = DebugLogger.MeasurePerformance(DiagnosticCategories.Providers, "OpenProviderAsync");
        var definition = ProviderCatalog.Get(provider);
        DebugLogger.Log(DiagnosticCategories.Providers, $"Provider workflow started: {provider} ({definition.Workflow})");
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
            DebugLogger.Log(DiagnosticCategories.Providers, $"Provider workflow finished: {provider}");
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

            StatusText = "Đang mở đăng nhập Codex và tự động xác nhận OAuth…";
            await RunOAuthAutoLoginAsync(provider, session.AuthUrl, new Uri("https://chatgpt.com"), cancellationToken);
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
            StatusText = $"Đang mở đăng nhập {definition.DisplayName} và tự động xác nhận OAuth…";
            var targetUri = definition.QuickLink is { } ql ? new Uri(ql) : null;
            if (targetUri is not null)
            {
                await RunOAuthAutoLoginAsync(provider, session.AuthUrl, targetUri, cancellationToken);
            }
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

        // Try automation for Kiro
        if (provider == ProviderKind.Kiro)
        {
            StatusText = $"Đang mở Chrome và tự động xác nhận {definition.DisplayName}…";
            await RunDeviceCodeWithAutomationAsync(
                api,
                provider,
                session.VerificationUriComplete ?? session.VerificationUri,
                session,
                cancellationToken);
        }
        else
        {
            // Fallback for other providers
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
    }

    private async Task RunDeviceCodeWithAutomationAsync(
        RouterApiClient api,
        ProviderKind provider,
        string verificationUri,
        DeviceCodeSession session,
        CancellationToken cancellationToken)
    {
        if (SelectedProfile is null || _installation is null)
        {
            // Fallback to manual flow
            await LaunchUrlAsync(verificationUri);
            StatusText = "Không thể tự động hóa. Vui lòng hoàn tất thủ công.";
            await PollDeviceCodeUntilSuccessAsync(api, provider, session, cancellationToken);
            return;
        }

        ChromeManagedSession? chromeSession = null;
        try
        {
            DebugLogger.Log(
                DiagnosticCategories.Providers,
                $"Device code automation start for profile {SelectedProfile.Name}");

            chromeSession = await _chromeLauncher.LaunchManagedAsync(
                _installation,
                SelectedProfile,
                new Uri(verificationUri),
                cancellationToken,
                useOriginalProfile: true);

            StatusText = $"Đã mở Chrome với profile {SelectedProfile.Name}. Đang tự động xác nhận…";

            var cdp = await chromeSession.ConnectAnyTargetAsync(cancellationToken);

            // Create TOTP generator from vault if available
            Func<Task<string?>>? totpGenerator = null;
            var vaultSession = await _googleLoginVaultStore.TryOpenRememberedAsync(
                _googleLoginVaultPaths.VaultPath,
                cancellationToken);

            if (vaultSession is not null)
            {
                await using (vaultSession)
                {
                    var credential = vaultSession.Vault.Records.FirstOrDefault(c =>
                        string.Equals(c.Email, SelectedProfile.Name, StringComparison.OrdinalIgnoreCase));

                    if (credential is not null && !string.IsNullOrWhiteSpace(credential.TotpSecret))
                    {
                        var totpSecret = credential.TotpSecret;
                        totpGenerator = () => Task.FromResult<string?>(
                            GoogleTotpGenerator.Generate(totpSecret, DateTimeOffset.UtcNow));
                        DebugLogger.Log(DiagnosticCategories.Providers, "TOTP generator created from vault");
                    }
                }
            }

            var automation = new AwsBuilderIdOAuthAutomation(cdp.Client, cdp.SessionId, cdp.TargetId, SelectedProfile.Name, totpGenerator);

            // Run automation in background (don't wait for it)
            var automationTask = automation.WaitAndConsentAsync(
                new Uri(verificationUri),
                TimeSpan.FromMinutes(10), // Longer timeout
                cancellationToken);

            // Polling is the source of truth - wait for connection to be detected
            StatusText = "Đang chờ xác nhận từ AWS…";
            await PollDeviceCodeUntilSuccessAsync(api, provider, session, cancellationToken);

            // Connection detected! Now we can cleanup
            StatusText = "Đã nhận connection. Đang lưu…";
            DebugLogger.Log(DiagnosticCategories.Providers, "Device code polling succeeded");

            // Try to get automation result (may still be running)
            OAuthConsentResult automationResult;
            if (automationTask.IsCompleted)
            {
                automationResult = await automationTask;
            }
            else
            {
                automationResult = new OAuthConsentResult(false, false, "Automation still running");
            }

            if (automationResult.Success)
            {
                DebugLogger.Log(DiagnosticCategories.Providers, $"Device code automation success: {automationResult.Message}");
            }
            else
            {
                DebugLogger.Log(DiagnosticCategories.Providers, $"Device code automation incomplete: {automationResult.Message}");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            DebugLogger.LogError(DiagnosticCategories.Providers, $"Device code automation failed: {ex.Message}", ex);
            StatusText = $"Tự động hóa lỗi: {ex.Message}. Đang chờ hoàn tất thủ công…";

            // Continue polling even if automation fails
            await PollDeviceCodeUntilSuccessAsync(api, provider, session, cancellationToken);
        }
        finally
        {
            if (chromeSession is not null)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);
                    await chromeSession.DisposeAsync();
                }
                catch
                {
                    // Best effort
                }
            }
        }
    }

    private async Task PollDeviceCodeUntilSuccessAsync(
        RouterApiClient api,
        ProviderKind provider,
        DeviceCodeSession session,
        CancellationToken cancellationToken)
    {
        var definition = ProviderCatalog.Get(provider);
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
        var profile = SelectedProfile ?? throw new InvalidOperationException("Hãy chọn Chrome profile trước.");

        var connection = await api.WaitForNewConnectionAsync(
            provider,
            _workflowExistingConnections,
            TimeSpan.FromMinutes(2),
            TimeSpan.FromSeconds(2),
            cancellationToken);
        await api.UpdateConnectionAsync(
            connection.Id,
            name: profile.Name,
            cancellationToken: cancellationToken);
        _workflowExistingConnections[connection.Id] = connection;
        _currentWorkflowProvider = null;
        await RefreshConnectionStatusesAsync(showStatus: false);
        StatusText = $"Đã kết nối {ProviderCatalog.Get(provider).DisplayName} với profile {profile.Name}.";
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
            ShowToast(StatusText, ToastType.Warning);
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
            await RefreshConnectionStatusesAsync(showStatus: false, cancellationToken: cancellationToken);
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

        return new GoogleAutoLoginViewModel(SelectedProfile, _googleLoginVaultStore, _googleLoginAutomation);
    }

    private Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<GoogleLoginResult>> CreateDefaultGoogleLoginAutomation()
    {
        return async (profile, credential, cancellationToken) =>
        {
            var installation = _installation ?? throw new InvalidOperationException("Chrome installation not configured.");

            ChromeManagedSession? session = null;
            IGoogleLoginBrowser? browser = null;
            try
            {
                DebugLogger.Log(DiagnosticCategories.Security, $"Google auto-login started for profile: {profile.DirectoryName}");
                DebugLogger.Log(DiagnosticCategories.Chrome, "Google auto-login Chrome launch requested");

                var settings = await _settingsStore.LoadAsync();

                session = await _chromeLauncher.LaunchManagedAsync(
                    installation,
                    profile,
                    new Uri("https://accounts.google.com/"),
                    cancellationToken,
                    settings.UseOriginalProfileForAutoLogin);

                DebugLogger.Log(DiagnosticCategories.Chrome, "Google auto-login Chrome launched and CDP endpoint is available");

                browser = await session.ConnectGoogleLoginAsync(cancellationToken);

                DebugLogger.Log(DiagnosticCategories.Security, "Google auto-login CDP connected; checking session cookies");

                // Check if already logged in via session cookies
                var initialState = await browser.ReadStateAsync(cancellationToken);
                GoogleLoginResult result;

                if (initialState.HasCompletionSignal)
                {
                    result = GoogleLoginResult.Success();
                    DebugLogger.Log(DiagnosticCategories.Security, "Google auto-login: session cookies authenticated successfully");
                }
                else
                {
                    DebugLogger.Log(DiagnosticCategories.Security, "Google auto-login: session cookies did not authenticate, starting state machine");
                    result = await _googleAuthenticationService.AuthenticateAsync(
                        new GoogleAuthenticationRequest(credential, browser),
                        cancellationToken);
                }

                DebugLogger.Log(DiagnosticCategories.Security, $"Google auto-login state machine completed: {result.Category}");

                // Keep the managed browser open after success so the authenticated
                // Google page remains visible. Manual intervention also transfers
                // ownership to the main view model for the same reason.
                if (result.Category is GoogleLoginResultCategory.Success
                    or GoogleLoginResultCategory.ManualInterventionRequired)
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
                DebugLogger.Log(DiagnosticCategories.Security, "Google auto-login cancelled");
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
                DebugLogger.LogError(DiagnosticCategories.Security, "Google auto-login failed", ex);
                DebugConsole.WriteLine($"[MainViewModel] Google auto-login exception: {ex.GetType().Name}: {ex.Message}");
                DebugConsole.WriteLine($"[MainViewModel] StackTrace: {ex.StackTrace}");

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
                DebugLogger.Log(DiagnosticCategories.Security, $"Codex login started for profile: {profile.DirectoryName}, method: {credential.Method}");

                if (credential.Method == Core.Models.AuthMethod.GoogleOAuth)
                {
                    // Google OAuth: Launch Chrome and run OAuth automation
                    DebugLogger.Log(DiagnosticCategories.Security, "Launching Chrome for Codex OAuth");
                    var settings = await _settingsStore.LoadAsync();

                    var codexOAuthUrl = new Uri("https://auth.openai.com/authorize?client_id=chatgpt-web&scope=openid%20profile%20email&response_type=code&redirect_uri=https%3A%2F%2Fchatgpt.com%2Fcodex");

                    session = await _chromeLauncher.LaunchManagedAsync(
                        installation,
                        profile,
                        codexOAuthUrl,
                        cancellationToken,
                        settings.UseOriginalProfileForAutoLogin);

                    cdp = await session.ConnectAnyTargetAsync(cancellationToken);

                    // Run OAuth automation
                    var automation = new CodexOAuthAutomation(
                        cdp.Client,
                        cdp.SessionId,
                        cdp.TargetId,
                        credential.LinkedGoogleEmail ?? string.Empty);

                    DebugLogger.Log(DiagnosticCategories.Security, "Starting Codex OAuth automation");
                    var consentResult = await automation.WaitAndConsentAsync(
                        codexOAuthUrl,
                        timeout: TimeSpan.FromMinutes(3),
                        cancellationToken);

                    if (consentResult.Success)
                    {
                        DebugLogger.Log(DiagnosticCategories.Security, "Codex OAuth completed successfully");
                        return CodexLoginResult.Success();
                    }

                    DebugLogger.Log(DiagnosticCategories.Security, $"Codex OAuth failed: {consentResult.Message}");
                    return CodexLoginResult.Failed(consentResult.Message);
                }
                else // Direct login
                {
                    // TODO: Implement Direct login automation
                    // 1. Navigate to OpenAI login page
                    // 2. Fill email/password
                    // 3. Handle TOTP if provided
                    // 4. Detect success

                    DebugLogger.Log(DiagnosticCategories.Security, "Codex Direct login not implemented yet");
                    return CodexLoginResult.Failed("Codex Direct login not implemented yet");
                }
            }
            catch (OperationCanceledException)
            {
                DebugLogger.Log(DiagnosticCategories.Security, "Codex login cancelled");
                return CodexLoginResult.Cancelled();
            }
            catch (Exception ex)
            {
                DebugLogger.LogError(DiagnosticCategories.Security, "Codex login failed", ex);
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
            return vaultSession.Vault.Records.FirstOrDefault(c =>
                string.Equals(c.ProfileId, profile.Id, StringComparison.OrdinalIgnoreCase));
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
                var settings = await _settingsStore.LoadAsync();
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

    public async Task DeleteSelectedProfileAsync()
    {
        var profile = SelectedProfile;
        if (profile is null)
        {
            StatusText = "Hãy chọn Chrome profile trước.";
            return;
        }

        var removedManagedProfiles = _managedProfiles
            .Where(managedProfile => IsManagedProfileFor(managedProfile, profile))
            .ToArray();

        try
        {
            _profileDeleter.Delete(profile, ChromeUserDataDirectory, ChromeExecutablePath);
            _managedProfiles.RemoveAll(managedProfile => IsManagedProfileFor(managedProfile, profile));
            try
            {
                await _settingsStore.SaveAsync(BuildSettings());
            }
            catch
            {
                _managedProfiles.AddRange(removedManagedProfiles);
                throw;
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
        var existing = _recentProfiles.FirstOrDefault(r =>
            r.ProfileId == profile.Id &&
            r.UserDataDirectory.Equals(profile.UserDataDirectory, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            _recentProfiles.Remove(existing);
            _recentProfiles.Insert(0, existing with
            {
                LastUsedUtc = DateTime.UtcNow,
                LaunchCount = existing.LaunchCount + 1
            });
        }
        else
        {
            _recentProfiles.Insert(0, new RecentProfile(
                profile.Id,
                profile.Name,
                profile.UserDataDirectory,
                DateTime.UtcNow,
                1,
                false));
        }

        // Keep only top MaxRecentSlots recent profiles
        while (_recentProfiles.Count > MaxRecentSlots)
        {
            _recentProfiles.RemoveAt(_recentProfiles.Count - 1);
        }

        UpdateRecentProfilesList();
    }

    public AsyncRelayCommand<ChromeProfile> TogglePinProfileCommand { get; }

    private async Task TogglePinProfileAsync(ChromeProfile? profile)
    {
        if (profile is null) return;

        var recent = _recentProfiles.FirstOrDefault(r =>
            r.ProfileId == profile.Id &&
            r.UserDataDirectory.Equals(profile.UserDataDirectory, StringComparison.OrdinalIgnoreCase));

        if (recent != null)
        {
            var index = _recentProfiles.IndexOf(recent);
            _recentProfiles[index] = recent with { IsPinned = !recent.IsPinned };

            // Sort: pinned first, then by last used
            var sorted = _recentProfiles
                .OrderByDescending(r => r.IsPinned)
                .ThenByDescending(r => r.LastUsedUtc)
                .ToList();
            _recentProfiles.Clear();
            _recentProfiles.AddRange(sorted);

            UpdateRecentProfilesList();
            await SaveSettingsAsync();
        }
    }

    private void UpdateRecentProfilesList()
    {
        RecentProfileRows.Clear();
        var slot = 0;
        foreach (var recent in _recentProfiles.Take(MaxRecentSlots))
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
        if (_recentProfiles.Count == 0) return;
        _recentProfiles.Clear();
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
            var api = CreateApiClient();
            var matchingConnections = (await api.ListConnectionsAsync(provider))
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
            var api = CreateApiClient();
            var matchingConnections = (await api.ListConnectionsAsync(provider))
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

            await RefreshConnectionStatusesAsync(showStatus: false);
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

        _installation ??= _chromeLocator.Find(ChromeExecutablePath, ChromeUserDataDirectory);
        if (_installation is null)
        {
            throw new InvalidOperationException("Không tìm thấy Chrome. Hãy thêm đường dẫn chrome.exe và User Data Directory.");
        }

        _chromeLauncher.Launch(_installation, SelectedProfile, url);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Best-effort OAuth auto-login: launches Chrome with the selected profile, navigates
    /// to the auth URL, and clicks Google account picker / consent automatically.
    /// Never throws — failures fall back to the user-completed flow already in progress.
    /// </summary>
    private async Task RunOAuthAutoLoginAsync(
        ProviderKind provider,
        string authUrl,
        Uri targetServiceUri,
        CancellationToken cancellationToken)
    {
        if (SelectedProfile is null || _installation is null)
        {
            return;
        }

        ChromeManagedSession? chromeSession = null;
        try
        {
            DebugLogger.Log(
                DiagnosticCategories.Providers,
                $"OAuth auto-login start for profile {SelectedProfile.Name}");

            chromeSession = await _chromeLauncher.LaunchManagedAsync(
                _installation,
                SelectedProfile,
                new Uri(authUrl),
                cancellationToken,
                useOriginalProfile: true);

            StatusText = $"Đã mở Chrome với profile {SelectedProfile.Name}. Đang chờ Google account picker…";

            var cdp = await chromeSession.ConnectAnyTargetAsync(cancellationToken);
            await using var orchestrator = new OAuthAutoLoginOrchestrator(
                chromeSession,
                cdp,
                provider);

            var result = await orchestrator.RunAsync(
                new Uri(authUrl),
                targetServiceUri,
                SelectedProfile.Name, // Profile email to match exactly
                provider == ProviderKind.Codex ? TimeSpan.FromMinutes(10) : TimeSpan.FromMinutes(2),
                cancellationToken);

            switch (result.Outcome)
            {
                case OAuthAutoLoginOutcome.Success:
                    StatusText = result.AlreadyAuthorized
                        ? "Đã đăng nhập sẵn. Đang lưu connection…"
                        : "Đã hoàn tất OAuth consent. Đang lưu connection…";
                    DebugLogger.Log(DiagnosticCategories.Providers, $"OAuth auto-login success: {result.Message}");
                    break;
                default:
                    StatusText = "Auto-login chưa hoàn tất. Vui lòng click cho phép trong cửa sổ Chrome…";
                    DebugLogger.Log(DiagnosticCategories.Providers, $"OAuth auto-login fallback: {result.Message}");
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Workflow cancelled; outer handler will surface the message.
        }
        catch (Exception ex)
        {
            DebugLogger.LogError(DiagnosticCategories.Providers, $"OAuth auto-login failed: {ex.Message}", ex);
            StatusText = $"Auto-login lỗi: {ex.Message}. Vui lòng hoàn tất thủ công.";
            // Do not rethrow — manual flow / OAuth proxy continues.
        }
        finally
        {
            if (chromeSession is not null)
            {
                try
                {
                    // Leave Chrome open briefly so the user sees the result if automation
                    // failed and they need to complete manually.
                    await Task.Delay(TimeSpan.FromSeconds(2), CancellationToken.None);
                    await chromeSession.DisposeAsync();
                }
                catch
                {
                    // Best effort
                }
            }
        }
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

    private RouterSettings BuildSettings(WindowPlacement? windowPlacement = null)
    {
        var placement = windowPlacement ?? _savedWindowPlacement;
        return new(
        DashboardBaseUrl.Trim(),
        string.IsNullOrWhiteSpace(ChromeExecutablePath) ? null : ChromeExecutablePath.Trim(),
        string.IsNullOrWhiteSpace(ChromeUserDataDirectory) ? null : ChromeUserDataDirectory.Trim(),
        FontScale,
        UseLightTheme,
        UseOriginalProfileForAutoLogin,
        _managedProfiles.ToArray(),
        placement?.Left,
        placement?.Top,
        placement?.Width,
        placement?.Height,
        _recentProfiles.ToArray(),
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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}























