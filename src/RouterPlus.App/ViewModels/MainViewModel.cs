using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Providers;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Chrome;
using RouterPlus.Infrastructure.Router;
using RouterPlus.Infrastructure.Security;
using RouterPlus.Infrastructure.Storage;

namespace RouterPlus.App.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private const int MaxLogEntries = 200;
    private readonly ChromeLocator _chromeLocator = new();
    private readonly ChromeProfileReader _profileReader = new();
    private readonly ChromeProfileProvisioner _profileProvisioner;
    private readonly ChromeProfileDeleter _profileDeleter;
    private readonly ChromeLauncher _chromeLauncher = new();
    private readonly SettingsStore _settingsStore;
    private readonly ISecretVault _secretVault = new DpapiSecretVault();
    private readonly HttpClient _httpClient;
    private ChromeInstallation? _installation;
    private CancellationTokenSource? _workflowCancellation;
    private HashSet<string> _workflowExistingIds = new(StringComparer.Ordinal);
    private ProviderKind? _currentWorkflowProvider;
    private bool _workflowInProgress;
    private readonly List<ManagedChromeProfile> _managedProfiles = new();
    private ChromeProfile? _selectedProfile;
    private ProviderKind _selectedApiKeyProvider = ProviderKind.OpenRouter;
    private int _apiKeyLoadVersion;
    private string _dashboardBaseUrl = "http://localhost:20128";
    private string _chromeExecutablePath = string.Empty;
    private string _chromeUserDataDirectory = string.Empty;
    private string _profileSearchText = string.Empty;
    private bool _isSettingsExpanded;
    private bool _isProfileSidebarCollapsed;
    private double _fontScale = 1d;
    private bool _useLightTheme;
    private string _savedDashboardBaseUrl = "http://localhost:20128";
    private string _savedChromeExecutablePath = string.Empty;
    private string _savedChromeUserDataDirectory = string.Empty;
    private double _savedFontScale = 1d;
    private bool _savedUseLightTheme;
    private string _connectionStatusText = "Chưa đồng bộ trạng thái provider.";
    private string _statusText = "Đang khởi tạo…";
    private readonly Queue<string> _logEntries = new();
    private string _logText = "Chưa có log.";

    public MainViewModel(
        SettingsStore? settingsStore = null,
        ChromeProfileProvisioner? profileProvisioner = null,
        ChromeProfileDeleter? profileDeleter = null,
        HttpClient? httpClient = null)
    {
        _settingsStore = settingsStore ?? new SettingsStore();
        _profileProvisioner = profileProvisioner ?? new ChromeProfileProvisioner();
        _profileDeleter = profileDeleter ?? new ChromeProfileDeleter();
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        Profiles = new ObservableCollection<ChromeProfile>();
        FilteredProfiles = new ObservableCollection<ChromeProfile>();
        ProfileRows = new ObservableCollection<ProfileRowViewModel>();
        FilteredProfileRows = new ObservableCollection<ProfileRowViewModel>();
        Profiles.CollectionChanged += Profiles_CollectionChanged;
        Providers = ProviderCatalog.All;
        ProviderCards = Providers.Select(definition => new ProviderCardViewModel(definition)).ToArray();
        ApiKeyProviders = Providers.Where(provider => provider.Workflow == WorkflowKind.ApiKey).ToArray();
        RefreshCommand = new AsyncRelayCommand(InitializeAsync);
        RefreshConnectionStatusesCommand = new AsyncRelayCommand(RefreshConnectionStatusesAsync);
        SaveSettingsCommand = new AsyncRelayCommand(SaveSettingsAsync, CanSaveSettings);
        AddProfileCommand = new AsyncRelayCommand(AddProfileAsync, () => CanAddProfile);
        ClearProfileSearchCommand = new AsyncRelayCommand(ClearProfileSearchAsync, () => CanClearProfileSearch);
        LaunchSelectedCommand = new AsyncRelayCommand(LaunchSelectedProfileAsync, () => SelectedProfile is not null);
        OpenProviderCommand = new AsyncRelayCommand<ProviderKind>(OpenProviderAsync);
        OpenProviderDashboardCommand = new AsyncRelayCommand<ProviderKind>(OpenProviderDashboardAsync, _ => SelectedProfile is not null);
        TestConnectionCommand = new AsyncRelayCommand<ProviderKind>(TestConnectionAsync, _ => SelectedProfile is not null);
        OpenQuickLinkCommand = new AsyncRelayCommand<ProviderKind>(OpenQuickLinkAsync);
        CancelWorkflowCommand = new AsyncRelayCommand(CancelWorkflowAsync, () => IsWorkflowInProgress);
        WaitForConnectionCommand = new AsyncRelayCommand(WaitForConnectionAsync, () => !_workflowInProgress && _currentWorkflowProvider is not null && SelectedProfile is not null);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<ChromeProfile> Profiles { get; }

    public ObservableCollection<ChromeProfile> FilteredProfiles { get; }

    public ObservableCollection<ProfileRowViewModel> ProfileRows { get; }

    public ObservableCollection<ProfileRowViewModel> FilteredProfileRows { get; }

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

    public sealed record FontScaleOption(double Value, string Label)
    {
        public override string ToString() => Label;
    }

    public sealed record ThemeOption(bool Value, string Label)
    {
        public override string ToString() => Label;
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

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand RefreshConnectionStatusesCommand { get; }

    public AsyncRelayCommand SaveSettingsCommand { get; }

    public AsyncRelayCommand AddProfileCommand { get; }

    public AsyncRelayCommand ClearProfileSearchCommand { get; }

    public AsyncRelayCommand LaunchSelectedCommand { get; }

    public AsyncRelayCommand<ProviderKind> OpenProviderCommand { get; }

    public AsyncRelayCommand<ProviderKind> OpenProviderDashboardCommand { get; }

    public AsyncRelayCommand<ProviderKind> TestConnectionCommand { get; }

    public AsyncRelayCommand<ProviderKind> OpenQuickLinkCommand { get; }

    public AsyncRelayCommand CancelWorkflowCommand { get; }

    public AsyncRelayCommand WaitForConnectionCommand { get; }

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
            _managedProfiles.Clear();
            _managedProfiles.AddRange(settings.ManagedProfiles ?? []);
            RefreshProfiles();
            MarkSettingsSaved();
            await LoadSelectedProfileApiKeysAsync();
            StatusText = Profiles.Count == 0
                ? "Chưa tìm thấy Chrome profile. Hãy kiểm tra đường dẫn rồi nhấn Làm mới."
                : $"Đã đọc {Profiles.Count} Chrome profile.";
            await RefreshConnectionStatusesAsync(showStatus: true);
        }
        catch (Exception exception)
        {
            SetError(exception);
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
        }
    }

    public void MarkProfileActionFailed(Exception exception, [CallerMemberName] string? operation = null) =>
        SetError(exception, operation);

    public void MarkApiKeyPasted(ProviderKind provider) =>
        StatusText = $"{ProviderCatalog.Get(provider).DisplayName} API key đã được dán vào ô nhập.";

    public void MarkApiKeyPasteFailed(ProviderKind provider, string? details = null) =>
        StatusText = string.IsNullOrWhiteSpace(details)
            ? $"Clipboard không có API key cho {ProviderCatalog.Get(provider).DisplayName}."
            : $"Không thể dán API key: {details}";

    private void ApplyProfileFilter()
    {
        FilteredProfiles.Clear();
        FilteredProfileRows.Clear();
        var rowsByProfileId = ProfileRows.ToDictionary(row => row.Profile.Id, StringComparer.Ordinal);
        var displayIndex = 1;
        foreach (var profile in ChromeProfileFilter.Filter(Profiles, ProfileSearchText))
        {
            FilteredProfiles.Add(profile);
            if (rowsByProfileId.TryGetValue(profile.Id, out var row))
            {
                row.SetDisplayIndex(displayIndex++);
                FilteredProfileRows.Add(row);
            }
        }
    }

    private void Profiles_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CanAddProfile));
        OnPropertyChanged(nameof(ProfileAddButtonText));
        AddProfileCommand.RaiseCanExecuteChanged();
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
                await RefreshConnectionStatusesAsync(showStatus: false);
                StatusText = $"{definition.DisplayName} key updated in 9Router for {profile.Name}; local key saved.";
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
            await RefreshConnectionStatusesAsync(showStatus: false);
            StatusText = $"Đã thêm {definition.DisplayName} cho {profile.Name}, priority {created.Priority}.";
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
            return;
        }

        using var workflowCancellation = new CancellationTokenSource();
        _workflowCancellation = workflowCancellation;
        _workflowInProgress = true;
        OnPropertyChanged(nameof(IsWorkflowInProgress));
        CancelWorkflowCommand.RaiseCanExecuteChanged();
        WaitForConnectionCommand.RaiseCanExecuteChanged();

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
            _workflowExistingIds.Clear();
            StatusText = $"Đã hủy thao tác thêm {definition.DisplayName}. Bạn có thể thử lại.";
        }
        catch (Exception exception)
        {
            _currentWorkflowProvider = null;
            _workflowExistingIds.Clear();
            SetError(exception);
        }
        finally
        {
            if (ReferenceEquals(_workflowCancellation, workflowCancellation))
            {
                _workflowCancellation = null;
            }

            _workflowInProgress = false;
            OnPropertyChanged(nameof(IsWorkflowInProgress));
            CancelWorkflowCommand.RaiseCanExecuteChanged();
            WaitForConnectionCommand.RaiseCanExecuteChanged();
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

            await api.ExchangeOAuthCodeAsync(
                provider,
                callback.Value,
                session.RedirectUri,
                session.CodeVerifier,
                callback.State ?? session.State,
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
        _workflowExistingIds = existing.Select(connection => connection.Id).ToHashSet(StringComparer.Ordinal);
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
            _workflowExistingIds,
            TimeSpan.FromMinutes(2),
            TimeSpan.FromSeconds(2),
            cancellationToken);
        await api.UpdateConnectionAsync(
            connection.Id,
            name: SelectedProfile.Name,
            cancellationToken: cancellationToken);
        _workflowExistingIds.Add(connection.Id);
        _currentWorkflowProvider = null;
        await RefreshConnectionStatusesAsync(showStatus: false);
        StatusText = $"Đã kết nối {ProviderCatalog.Get(provider).DisplayName} với profile {SelectedProfile.Name}.";
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

        try
        {
            var provider = _currentWorkflowProvider.Value;
            var api = CreateApiClient();
            StatusText = $"Đang chờ {ProviderCatalog.Get(provider).DisplayName} báo connection mới…";
            var connection = await api.WaitForNewConnectionAsync(
                provider,
                _workflowExistingIds,
                TimeSpan.FromMinutes(10),
                TimeSpan.FromSeconds(2));
            await api.UpdateConnectionAsync(connection.Id, name: SelectedProfile.Name);
            _workflowExistingIds.Add(connection.Id);
            _currentWorkflowProvider = null;
            await RefreshConnectionStatusesAsync(showStatus: false);
            StatusText = $"Đã kết nối {ProviderCatalog.Get(provider).DisplayName} với profile {SelectedProfile.Name}.";
            WaitForConnectionCommand.RaiseCanExecuteChanged();
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }

    public async Task OpenSelectedGoogleLoginAsync()
    {
        if (SelectedProfile is null)
        {
            StatusText = "Hãy chọn Chrome profile trước.";
            return;
        }

        try
        {
            await LaunchUrlAsync("https://accounts.google.com/");
            StatusText = $"Đã mở đăng nhập Google bằng profile {SelectedProfile.Name}.";
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
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
            _profileDeleter.Delete(profile, ChromeUserDataDirectory);
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

    private async Task LaunchSelectedProfileAsync()
    {
        if (SelectedProfile is null)
        {
            StatusText = "Hãy chọn Chrome profile trước.";
            return;
        }

        try
        {
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
                return;
            }

            var errors = new List<string>();
            foreach (var connection in matchingConnections)
            {
                var result = await api.TestConnectionAsync(connection.Id);
                if (!result.Valid)
                {
                    errors.Add(string.IsNullOrWhiteSpace(result.Error) ? "invalid" : result.Error.Trim());
                }
            }

            if (errors.Count == 0)
            {
                StatusText = $"Test connection succeeded for {definition.DisplayName} on profile {profile.Name}.";
            }
            else
            {
                StatusText = $"Test connection failed for {definition.DisplayName}: {string.Join("; ", errors)}";
            }
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

    private RouterSettings BuildSettings() => new(
        DashboardBaseUrl.Trim(),
        string.IsNullOrWhiteSpace(ChromeExecutablePath) ? null : ChromeExecutablePath.Trim(),
        string.IsNullOrWhiteSpace(ChromeUserDataDirectory) ? null : ChromeUserDataDirectory.Trim(),
        FontScale,
        UseLightTheme,
        _managedProfiles.ToArray());

    private void SetError(Exception exception, [CallerMemberName] string? operation = null)
    {
        StatusText = SafeError(exception);
        var details = string.IsNullOrWhiteSpace(operation)
            ? exception.ToString()
            : $"{operation}: {exception}";
        AppendLog("ERROR", details);
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
        && UseLightTheme == _savedUseLightTheme;

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
        NotifySettingsStateChanged();
    }

    private void NotifySettingsStateChanged()
    {
        OnPropertyChanged(nameof(HasUnsavedSettings));
        OnPropertyChanged(nameof(SettingsValidationMessage));
        OnPropertyChanged(nameof(HasSettingsValidationError));
        OnPropertyChanged(nameof(SettingsStatusText));
        SaveSettingsCommand?.RaiseCanExecuteChanged();
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
        RouterApiException apiException => apiException.Message,
        TimeoutException timeoutException => timeoutException.Message,
        FileNotFoundException => "Không tìm thấy file cần thiết. Kiểm tra lại đường dẫn Chrome.",
        DirectoryNotFoundException => "Không tìm thấy thư mục Chrome profile.",
        _ => $"Thao tác thất bại: {exception.Message}"
    };

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
