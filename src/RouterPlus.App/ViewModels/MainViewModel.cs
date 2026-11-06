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

public sealed partial class MainViewModel : INotifyPropertyChanged
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
    private Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<AgnesKeyFlowOrchestrator.AgnesKeyFlowResult>> _agnesKeyFlow = null!;
    private Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<PoolsideKeyFlowOrchestrator.PoolsideKeyFlowResult>> _poolsideKeyFlow = null!;
    private Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<BazaarlinkKeyFlowOrchestrator.BazaarlinkKeyFlowResult>> _bazaarlinkKeyFlow = null!;
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
        _agnesKeyFlow = CreateDefaultAgnesKeyFlow();
        _poolsideKeyFlow = CreateDefaultPoolsideKeyFlow();
        _bazaarlinkKeyFlow = CreateDefaultBazaarlinkKeyFlow();
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
                refreshConnectionsLightweight: ct => RefreshConnectionsLightweightAsync(ct),
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
                getGoogleCredential: (profile, ct) => LoadGoogleCredentialForProfileAsync(profile, ct),
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


    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

