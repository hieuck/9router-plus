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
    private readonly ISecretVault _secretVault;
    private readonly IGoogleAccountVaultStore _googleLoginVaultStore;
    private readonly GoogleAccountVaultPaths _googleLoginVaultPaths;
    private readonly IProviderConnectionVaultStore _providerConnectionVaultStore;
    private ProfileHealthService _profileHealthService;
    private GoogleAccountVault? _googleAccountVault;

    // Internal access for CredentialsManagerViewModel
    internal IGoogleAccountVaultStore GoogleAccountVaultStore => _googleLoginVaultStore;
    internal GoogleAccountVaultPaths GoogleAccountVaultPaths => _googleLoginVaultPaths;
    internal IProviderConnectionVaultStore ProviderConnectionVaultStore => _providerConnectionVaultStore;
    internal Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<GoogleLoginResult>> GoogleLoginAutomation => _googleLoginAutomation;
    internal Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<GoogleLoginResult>> GoogleLoginHealthCheckAutomation => _googleLoginHealthCheckAutomation;
    internal async Task<IProviderLoginChromeSession> LaunchManagedChromeForProviderAsync(ChromeProfile profile, Uri uri, CancellationToken cancellationToken)
    {
        var installation = _installation ?? throw new InvalidOperationException("Chrome installation not configured.");
        var session = await _launchManagedChrome(installation, profile, uri, cancellationToken);
        return new ProviderLoginChromeSession(session);
    }
    internal Func<ChromeProfile, CodexLoginCredential, CancellationToken, Task<CodexLoginResult>> CodexLoginAutomation => _codexLoginAutomation;

    private readonly Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<GoogleLoginResult>> _googleLoginAutomation;
    private readonly Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<GoogleLoginResult>> _googleLoginHealthCheckAutomation;
    private readonly Func<ChromeProfile, CodexLoginCredential, CancellationToken, Task<CodexLoginResult>> _codexLoginAutomation;
    private readonly IGoogleAuthenticationService _googleAuthenticationService;
    private readonly HttpClient _httpClient;
    private readonly IUpdateService _updateService;
    private readonly IExternalLinkLauncher _linkLauncher;
    private readonly Func<ChromeProfile, string, Task> _launchUrl;
    private readonly Func<ChromeInstallation, ChromeProfile, Uri, CancellationToken, Task<ChromeManagedSession>> _launchManagedChrome;
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
    // Workflow state now owned by _providerWorkflowService.
    private readonly IProfileManager _profileManager;
    private ChromeProfile? _selectedProfile;
    // Auto-Get-Key seam (mirrors _googleLoginAutomation): runs the Chrome-based
    // key flow for a profile + vault credential. Testable standalone.
    private Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<OpenRouterKeyFlowOrchestrator.OpenRouterKeyFlowResult>> _openRouterKeyFlow = null!;
    private Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<OllamaKeyFlowOrchestrator.OllamaKeyFlowResult>> _ollamaKeyFlow = null!;
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
    private bool _useOriginalProfileForAutoLogin;
    private string _savedDashboardBaseUrl = "http://localhost:20128";
    private string? _dashboardAuthPassword;
    private string? _savedDashboardAuthPassword;
    private string _savedChromeExecutablePath = string.Empty;
    private string _savedChromeUserDataDirectory = string.Empty;
    private double _savedFontScale = 1d;
    private bool _savedUseLightTheme = true;
    private bool _savedUseOriginalProfileForAutoLogin;
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
    private readonly IProviderConnectionService _connectionService;
    private readonly IProviderApiKeyService _providerApiKeyService;
    private readonly IProviderConnectionRenameService _renameService;
    private IProviderWorkflowService _providerWorkflowService = null!;
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
        ISecretVault? secretVault = null,
        IGoogleAccountVaultStore? googleLoginVaultStore = null,
        GoogleAccountVaultPaths? googleLoginVaultPaths = null,
        Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<GoogleLoginResult>>? googleLoginAutomation = null,
        IReadOnlyList<ChromeProfile>? harnessProfiles = null,
        IGoogleAuthenticationService? googleAuthenticationService = null,
        ProfileHealthService? profileHealthService = null,
        Func<ChromeProfile, string, Task>? launchUrl = null,
        Func<ChromeInstallation, ChromeProfile, Uri, CancellationToken, Task<ChromeManagedSession>>? launchManagedChrome = null,
        IProfileManager? profileManager = null,
        IProviderConnectionService? connectionService = null,
        IProviderApiKeyService? providerApiKeyService = null,
        IProviderConnectionRenameService? renameService = null,
        IProviderWorkflowService? providerWorkflowService = null)
    {
        _settingsStore = settingsStore ?? new SettingsStore();
        _secretVault = secretVault ?? new DpapiSecretVault();
        _profileProvisioner = profileProvisioner ?? new ChromeProfileProvisioner();
        _launchUrl = launchUrl ?? ((profile, url) =>
        {
            _installation ??= _chromeLocator.Find(ChromeExecutablePath, ChromeUserDataDirectory)
                ?? throw new InvalidOperationException("Không tìm thấy Chrome. Hãy thêm đường dẫn chrome.exe và User Data Directory.");
            _chromeLauncher.Launch(_installation, profile, url);
            return Task.CompletedTask;
        });
        _launchManagedChrome = launchManagedChrome ?? ((installation, profile, uri, cancellationToken) =>
            _chromeLauncher.LaunchManagedAsync(
                installation,
                profile,
                uri,
                cancellationToken,
                useOriginalProfile: true));
        _profileDeleter = profileDeleter ?? new ChromeProfileDeleter();
        _profileManager = profileManager ?? new ProfileManager(_chromeLocator, _profileReader);
        _connectionService = connectionService ?? new ProviderConnectionService();
        _providerApiKeyService = providerApiKeyService ?? new ProviderApiKeyService(_secretVault);
        _renameService = renameService ?? new ProviderConnectionRenameService();
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
        _profileHealthService = profileHealthService!; // Deferred initialization in LoadGoogleAccountVaultAsync
        _googleLoginAutomation = googleLoginAutomation ?? CreateDefaultGoogleLoginAutomation();
        _googleLoginHealthCheckAutomation = CreateDefaultGoogleLoginAutomation(minimized: true);
        _codexLoginAutomation = CreateDefaultCodexLoginAutomation();
        _openRouterKeyFlow = CreateDefaultOpenRouterKeyFlow();
        _ollamaKeyFlow = CreateDefaultOllamaKeyFlow();
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
        CloseBatchProgressCommand = new AsyncRelayCommand(() => { CloseBatchProgress(); return Task.CompletedTask; }, () => !IsBatchLoginRunning);
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
        AutoGetKeyCommand = new AsyncRelayCommand<ProviderKind>(
            provider => AutoGetKeyAsync(provider),
            _ => SelectedProfile is not null);
        ConnectOpenRouterOAuthCommand = new AsyncRelayCommand(
            () => ConnectOpenRouterOAuthAsync(),
            () => SelectedProfile is not null && !_providerWorkflowService.IsInProgress);
        CancelWorkflowCommand = new AsyncRelayCommand(CancelWorkflowAsync, () => IsWorkflowInProgress);
        WaitForConnectionCommand = new AsyncRelayCommand(WaitForConnectionAsync, () => !_providerWorkflowService.IsInProgress && _providerWorkflowService.CurrentProvider is not null && SelectedProfile is not null);
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

        if (providerWorkflowService is not null)
        {
            _providerWorkflowService = providerWorkflowService;
        }
        else
        {
            _providerWorkflowService = new ProviderWorkflowService(
                renameService: _renameService,
                apiKeyService: _providerApiKeyService,
                selectedProfileGetter: () => SelectedProfile,
                installationGetter: () => _installation,
                launchManagedChrome: _launchManagedChrome,
                launchUrl: _launchUrl,
                launchUrlAsync: url => LaunchUrlAsync(url),
                createApiClient: () => CreateApiClientAsync(),
                tryAutoGoogleLogin: TryAutoGoogleLoginAsync,
                pauseQuotaPolling: () => { PauseQuotaPolling(); return Task.CompletedTask; },
                resumeQuotaPolling: ResumeQuotaPollingAsync,
                refreshConnections: (showStatus, forceLog, ct) => RefreshConnectionStatusesAsync(showStatus, forceLog, ct),
                statusCallback: value => StatusText = value,
                showToast: (msg, type, sec) => ShowToast(msg, type, sec),
                setCardWorkflowInProgress: (kind, value) => GetProviderCard(kind).SetWorkflowInProgress(value),
                setSelectedApiKeyProvider: value => SelectedApiKeyProvider = value,
                onApiKeySaved: (kind, key) =>
                {
                    var card = GetProviderCard(kind);
                    card.ApiKeyValue = key;
                    card.MarkApiKeySaved();
                },
                openRouterPkceFlow: ct => _openRouterPkceFlow(ct),
                getDashboardPort: GetDashboardPort,
                getDashboardBaseUrl: () => DashboardBaseUrl,
                getTotpGenerator: (profile, ct) => CreateTotpGeneratorAsync(profile, ct),
                setError: ex => SetError(ex),
                stateChanged: () =>
                {
                    OnPropertyChanged(nameof(IsWorkflowInProgress));
                    CancelWorkflowCommand.RaiseCanExecuteChanged();
                    WaitForConnectionCommand.RaiseCanExecuteChanged();
                    CheckForUpdatesCommand.RaiseCanExecuteChanged();
                    InstallUpdateCommand.RaiseCanExecuteChanged();
                    ConnectOpenRouterOAuthCommand.RaiseCanExecuteChanged();
                });
        }
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

    public int ProviderCount => ProviderCards.Count;

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
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Debug,
                    "ViewModel",
                    "SelectedProfileUnchanged",
                    "SelectedProfile unchanged, skipping",
                    new { });
                return;
            }

            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "ViewModel",
                "SelectedProfileChanging",
                "SelectedProfile changing",
                new { profile_name = value?.Name ?? "null" });
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

        ObservabilityHub.Instance.IncrementCounter("profile.selected", tags: new Dictionary<string, string>
        {
            ["source"] = "context_menu"
        });
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
            StartBatchAutoLoginCommand.RaiseCanExecuteChanged();

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
                // Find credentials for this profile
                var credential = session.Vault.Find(row.Profile.Id);
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
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "Updates",
                "UpdateCheckCompleted",
                "Update check completed",
                new { is_update_available = release.IsUpdateAvailable, available_version = release.AvailableVersion });
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
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "Updates",
                "DownloadingUpdate",
                "Downloading and staging update package",
                new { version = _latestRelease.AvailableVersion });
            var package = await _updateService.DownloadAndStageAsync(_latestRelease);
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "Updates",
                "UpdateStaged",
                "Update package staged; launching updater",
                new { archive_path = package.ArchivePath, staging_path = package.StagingPath });
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

        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "Profiles",
            "ProfileRefreshStarted",
            "Starting profile discovery",
            new { harness_mode = _harnessMode, chrome_path = ChromeExecutablePath, user_data_dir = ChromeUserDataDirectory });

        if (_harnessMode)
        {
            RefreshHarnessProfiles(previousProfileId);
            return;
        }
        var discovery = _profileManager.DiscoverProfiles(
            string.IsNullOrWhiteSpace(ChromeExecutablePath) ? null : ChromeExecutablePath,
            string.IsNullOrWhiteSpace(ChromeUserDataDirectory) ? null : ChromeUserDataDirectory);
        _installation = discovery.Installation;

        if (_installation is null)
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Warning,
                "Profiles",
                "ChromeInstallationNotFound",
                "Profile refresh could not find Chrome installation",
                new { chrome_path = ChromeExecutablePath, user_data_dir = ChromeUserDataDirectory });
            return;
        }

        ChromeExecutablePath = _installation.ExecutablePath;
        ChromeUserDataDirectory = _installation.UserDataDirectory;
        var managedProfiles = _profileManager.ManagedProfiles
            .Where(profile => Directory.Exists(Path.Combine(profile.UserDataDirectory, profile.DirectoryName)))
            .ToArray();
        var profiles = discovery.Profiles;

        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "Profiles",
            "ProfileRefreshCompleted",
            "Profile discovery completed",
            new
            {
                total_profiles = profiles.Count,
                managed_profiles = managedProfiles.Length,
                user_data_dir = _installation.UserDataDirectory
            });

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
        CheckAllProfilesHealthCommand.RaiseCanExecuteChanged();
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
        CheckAllProfilesHealthCommand.RaiseCanExecuteChanged();
        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "Chrome",
            "HarnessProfilesLoaded",
            "Harness profile refresh loaded synthetic profiles",
            new { profile_count = profiles.Count });
    }

    public async Task AddProfileAsync()
    {
        if (!CanAddProfile)
        {
            return;
        }

        try
        {
            _installation = _profileManager.FindInstallation(
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
                _profileManager.ManagedProfiles);
            _profileManager.AddManaged(createdProfile);
            try
            {
                await _settingsStore.SaveAsync(BuildSettings());
            }
            catch
            {
                _profileManager.RemoveManaged(createdProfile);
                try
                {
                    var createdPath = Path.Combine(createdProfile.UserDataDirectory, createdProfile.DirectoryName);
                    if (Directory.Exists(createdPath))
                    {
                        Directory.Delete(createdPath, recursive: true);
                    }
                }
                catch
                {
                    // Best-effort cleanup; managed state is already restored.
                }
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
        StartBatchAutoLoginCommand.RaiseCanExecuteChanged();
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
            ObservabilityHub.Instance.LogError(
                "ViewModel",
                "VaultCredentialsCheckFailed",
                ex,
                new { profile_name = profile.Name });
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
        CloseBatchProgressCommand.RaiseCanExecuteChanged();
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
                    ObservabilityHub.Instance.LogError(
                        "ViewModel",
                        "BatchLoginFailed",
                        ex,
                        new { profile_name = profile.Name });
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
            ObservabilityHub.Instance.LogError(
                "ViewModel",
                "BatchAutoLoginFailed",
                ex,
                new { });
        }
        finally
        {
            IsBatchLoginRunning = false;
            _batchLoginCts?.Dispose();
            _batchLoginCts = null;
            StartBatchAutoLoginCommand.RaiseCanExecuteChanged();
            StopBatchLoginCommand.RaiseCanExecuteChanged();
            CloseBatchProgressCommand.RaiseCanExecuteChanged();
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
                ObservabilityHub.Instance.LogError(
                    "ViewModel",
                    "ProviderLoginFailed",
                    ex,
                    new { provider_kind = kind.ToString(), profile_name = profile.Name });
            }
        }

        return anySuccess;
    }

    /// <summary>
    /// Get the login URL for a provider.
    /// </summary>
    private static Uri GetProviderLoginUri(ProviderKind provider)
    {
        return ProviderCatalog.GetDirectLoginStartUri(provider) ?? new Uri("https://chatgpt.com/");
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
        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "Providers",
            "ConnectionRefreshRequested",
            "Provider sync requested",
            new { profile_count = ProfileRows.Count, forceLog });

        // Coalesce duplicate background refreshes: skip non-forced if another refresh is running
        if (!forceLog)
        {
            if (!await _connectionRefreshGate.WaitAsync(0, cancellationToken))
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Debug,
                    "Providers",
                    "ConnectionRefreshCoalesced",
                    "Skipped duplicate background refresh (another refresh in progress)",
                    new { profile_count = ProfileRows.Count });
                return;
            }
        }
        else
        {
            await _connectionRefreshGate.WaitAsync(cancellationToken);
        }

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
        var loadVersion = Interlocked.Increment(ref _apiKeyLoadVersion);
        var profile = SelectedProfile;
        ObservabilityHub.Instance.LogEvent(
            LogLevel.Debug,
            "Security",
            "LoadingProfileCredentials",
            "Loading saved provider credentials for profile",
            new { has_profile = profile is not null });
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

            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "Security",
                "ProfileCredentialsLoaded",
                "Saved provider credentials loaded",
                new { provider_count = values.Length });
        }
        catch (Exception exception)
        {
            ObservabilityHub.Instance.LogError(
                "Security",
                "ProfileCredentialsLoadFailed",
                exception,
                new { profile_name = profile.Name });
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
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "Providers",
                "LoadingConnections",
                "Loading provider connections",
                new { });

            await LoadQuotaAutoDisableMarkersAsync();

            var refreshResult = await _connectionService.RefreshConnectionsAsync(
                async () => (IRouterApiClient)await CreateApiClientAsync(),
                cancellationToken);
            var connections = refreshResult.Connections;
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "Providers",
                "ConnectionsLoaded",
                "Provider connections loaded",
                new { connection_count = connections.Count });
            _lastReliableNearLimit = refreshResult.NearLimit;

            var disableFailures = refreshResult.DisableFailures.Count;
            foreach (var disabledConnection in refreshResult.DisabledConnections)
            {
                UpsertQuotaAutoDisableMarker(disabledConnection);
            }
            if (refreshResult.DisabledConnections.Count > 0)
            {
                await SaveQuotaAutoDisableMarkersAsync(cancellationToken);
            }

            foreach (var failure in refreshResult.DisableFailures)
            {
                AppendLog("WARN", $"Không thể tự tắt connection {failure.ConnectionId}: {SafeError(failure.Exception)}");
            }

            if (refreshResult.FollowUpRefreshException is not null)
            {
                throw refreshResult.FollowUpRefreshException;
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
            var lostRows = ProfileRows.Where(row => row.ProviderStatuses.Any(status => status.HasLostConnection)).ToList();
            ConnectionStatusText = disableFailures > 0
                ? $"Đã đồng bộ {connections.Count} connection · tắt tự động thất bại {disableFailures} connection."
                : $"Đã đồng bộ {connections.Count} connection · {matchedProfiles}/{ProfileRows.Count} profile có provider.";
            if (lostRows.Count > 0)
            {
                var lostNames = string.Join(", ", lostRows.Select(row => row.Name));
                ConnectionStatusText += $" · Mất connection: {lostNames} — thêm lại";
                AppendLog("WARN", $"Mất connection: {lostNames} — thêm lại");
            }
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
    /// Injectable seam for the Chrome-based Ollama key flow, so tests can drive the flow
    /// without launching a browser.
    /// </summary>
    public Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<OllamaKeyFlowOrchestrator.OllamaKeyFlowResult>> OllamaKeyFlow
    {
        get => _ollamaKeyFlow;
        set => _ollamaKeyFlow = value ?? throw new ArgumentNullException(nameof(value));
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
    /// Auto-get an API key for the given provider (OpenRouter or Ollama):
    /// opens Chrome, signs in with Google from the vault, deletes old keys, creates
    /// a new one, then saves it into 9Router for the selected profile.
    /// </summary>
    public async Task<bool> AutoGetKeyAsync(ProviderKind provider, CancellationToken cancellationToken = default)
    {
        SelectedApiKeyProvider = provider;

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

        if (provider == ProviderKind.Ollama)
        {
            var ollamaResult = await _ollamaKeyFlow(profile, credential, cancellationToken);
            if (!ollamaResult.Success)
            {
                StatusText = $"Không lấy được Ollama key: {ollamaResult.ErrorMessage}";
                ShowToast(StatusText, ToastType.Error);
                return false;
            }

            return await AddApiKeyAsync(ProviderKind.Ollama, ollamaResult.ApiKey!);
        }
        else
        {
            var flowResult = await _openRouterKeyFlow(profile, credential, cancellationToken);
            if (!flowResult.Success)
            {
                StatusText = $"Không lấy được OpenRouter key: {flowResult.ErrorMessage}";
                ShowToast(StatusText, ToastType.Error);
                return false;
            }

            return await AddApiKeyAsync(ProviderKind.OpenRouter, flowResult.ApiKey!);
        }
    }

    public Task<bool> ConnectOpenRouterOAuthAsync(CancellationToken cancellationToken = default) =>
        _providerWorkflowService.ConnectOpenRouterOAuthAsync(cancellationToken);

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
            async Task<IRouterApiClient> CreateApiClientWrapper() => await CreateApiClientAsync();
            var serviceResult = await _providerApiKeyService.AddOrUpdateAsync(
                provider,
                apiKey,
                profile,
                CreateApiClientWrapper,
                CancellationToken.None);

            if (Equals(SelectedProfile, profile))
            {
                var card = GetProviderCard(provider);
                card.ApiKeyValue = apiKey;
                card.MarkApiKeySaved();
            }

            var connection = serviceResult.Connection;
            var testResult = serviceResult.TestResult;
            string message;
            if (serviceResult.Created)
            {
                message = testResult.Valid
                    ? $"Đã thêm {definition.DisplayName} cho {profile.Name}, priority {connection.Priority}."
                    : $"Đã lưu {definition.DisplayName} cho {profile.Name}, nhưng kiểm tra kết nối thất bại.";
            }
            else
            {
                message = testResult.Valid
                    ? $"{definition.DisplayName} key updated in 9Router for {profile.Name}; local key saved."
                    : $"{definition.DisplayName} key saved for {profile.Name}, but the connection test failed.";
            }
            StatusText = message;
            ShowToast(message, testResult.Valid ? ToastType.Success : ToastType.Warning, 5);
            // Background refresh after UI feedback
            await RefreshConnectionStatusesAsync(showStatus: false, forceLog: true);
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

    internal Task OpenProviderForTestAsync(ProviderKind provider) => _providerWorkflowService.OpenProviderForTestAsync(provider);

    private Task OpenProviderAsync(ProviderKind provider) => _providerWorkflowService.OpenProviderAsync(provider);
    private Task CancelWorkflowAsync()
    {
        if (_providerWorkflowService.WorkflowCancellation is { IsCancellationRequested: false })
        {
            StatusText = "Đang hủy thao tác thêm provider…";
            _providerWorkflowService.Cancel();
        }

        return Task.CompletedTask;
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

    private async Task<Func<Task<string?>>?> CreateTotpGeneratorAsync(ChromeProfile profile, CancellationToken cancellationToken)
    {
        Func<Task<string?>>? totpGenerator = null;
        var vaultSession = await _googleLoginVaultStore.TryOpenRememberedAsync(_googleLoginVaultPaths.VaultPath, cancellationToken);
        if (vaultSession is not null)
        {
            await using (vaultSession)
            {
                var credential = vaultSession.Vault.Records.FirstOrDefault(c =>
                    string.Equals(c.Email, profile.Name, StringComparison.OrdinalIgnoreCase));
                if (credential is not null && !string.IsNullOrWhiteSpace(credential.TotpSecret))
                {
                    var totpSecret = credential.TotpSecret;
                    totpGenerator = () => Task.FromResult<string?>(GoogleTotpGenerator.Generate(totpSecret, DateTimeOffset.UtcNow));
                }
            }
        }
        return totpGenerator;
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

    private Task WaitForConnectionAsync() => _providerWorkflowService.WaitForConnectionAsync();
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
                // Find credential for this profile: stable Id first, fallback to legacy display name
                var credential = session.Vault.Find(SelectedProfile.Id);
                if (credential == null)
                {
                    // Legacy compatibility: try profile name if it's unique
                    var profilesWithSameName = Profiles.Count(p => p.Name == SelectedProfile.Name);
                    if (profilesWithSameName == 1)
                    {
                        credential = session.Vault.Find(SelectedProfile.Name);
                    }
                }

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
                string.Equals(c.ProfileId, profile.Id, StringComparison.OrdinalIgnoreCase))
                ?? vaultSession.Vault.Records.FirstOrDefault(c =>
                    string.Equals(c.Email, profile.Name, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Attempts to auto-login Google for the given profile before the provider OAuth flow.
    /// Runs once before the Nous Chrome session; Google cookies persist in the real profile.
    /// Never throws; if vault is locked/no credential/unsupported page, just continues.
    /// </summary>
    private async Task TryAutoGoogleLoginAsync(ChromeProfile profile, CancellationToken ct)
    {
        if (_installation is null) return;
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
            return;
        }
        if (credential is null || string.IsNullOrWhiteSpace(credential.Email) || string.IsNullOrWhiteSpace(credential.Password)) return;

        ChromeManagedSession? session = null;
        try
        {
            StatusText = $"Đang đăng nhập Google cho {profile.Name}…";
            session = await _chromeLauncher.LaunchManagedAsync(_installation, profile,
                new Uri("https://accounts.google.com/"), ct, useOriginalProfile: true);
            var browser = await session.ConnectGoogleLoginAsync(ct);
            await using (browser)
            {
                var state = await browser.ReadStateAsync(ct);
                if (state.HasCompletionSignal && state.PageUri.Host == "myaccount.google.com")
                {
                    ObservabilityHub.Instance.LogEvent(
                        LogLevel.Info,
                        "Providers",
                        "AutoGoogleLoginAlreadySignedIn",
                        "Already signed in via session cookies",
                        new { profile = profile.Name });
                    return;
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
                }
                else
                {
                    ObservabilityHub.Instance.LogEvent(
                        LogLevel.Warning,
                        "Providers",
                        "AutoGoogleLoginIncomplete",
                        "Auto Google login did not complete",
                        new { profile = profile.Name, category = result.Category.ToString(), message = result.Message });
                }
            }
        }
        catch (Exception ex)
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Warning,
                "Providers",
                "AutoGoogleLoginFailed",
                "Auto Google login failed, continuing with provider flow",
                new { profile = profile.Name, error = ex.Message });
        }
        finally
        {
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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}























