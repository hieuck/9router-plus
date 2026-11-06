using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using RouterPlus.Core.Models;
using RouterPlus.Core.Providers;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Security;
using RouterPlus.Core.Observability;
using RouterPlus.App.Services;
using RouterPlus.Infrastructure.Storage;
using RouterPlus.Infrastructure.Security;
using RouterPlus.Infrastructure.Chrome;
using RouterPlus.Infrastructure.Services;

namespace RouterPlus.App.ViewModels;

/// <summary>
/// ViewModel for unified credentials manager dialog.
/// Manages Google accounts and provider connections.
/// Phase 5 Step 5.2 - Complete vault integration
/// </summary>
public sealed partial class CredentialsManagerViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly MainViewModel _mainViewModel;
    private readonly IGoogleAccountVaultStore _googleAccountVaultStore;
    private readonly IProviderConnectionVaultStore _providerConnectionVaultStore;
    private readonly IProviderConnectionRowService _providerConnectionRowService;
    private readonly GoogleAccountVaultPaths _vaultPaths;
    // Compatibility seam: MainViewModel composes this runner from the shared
    // IGoogleAuthenticationService and owns the Chrome/browser lifetime.
    private readonly Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<GoogleLoginResult>> _runGoogleAuthentication;
    private readonly Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<GoogleLoginResult>> _runGoogleHealthCheck;
    private readonly Func<ChromeProfile, CodexLoginCredential, CancellationToken, Task<CodexLoginResult>> _runCodexAuthentication;
    private readonly ICredentialsProviderLoginService _credentialsProviderLoginService;
    private int _selectedTabIndex;
    private string _statusMessage = string.Empty;
    private readonly object _initializationLock = new();
    private Task? _initializationTask;

    public event PropertyChangedEventHandler? PropertyChanged;

    public CredentialsManagerViewModel(
        MainViewModel mainViewModel,
        IGoogleAccountVaultStore googleAccountVaultStore,
        IProviderConnectionVaultStore providerConnectionVaultStore,
        GoogleAccountVaultPaths vaultPaths,
        Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<GoogleLoginResult>> googleAuthenticationRunner,
        Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<GoogleLoginResult>> googleHealthCheckRunner,
        Func<ChromeProfile, CodexLoginCredential, CancellationToken, Task<CodexLoginResult>> codexAuthenticationRunner,
        IProviderConnectionRowService? providerConnectionRowService = null,
        ICredentialsProviderLoginService? credentialsProviderLoginService = null)
    {
        _mainViewModel = mainViewModel ?? throw new ArgumentNullException(nameof(mainViewModel));
        _googleAccountVaultStore = googleAccountVaultStore ?? throw new ArgumentNullException(nameof(googleAccountVaultStore));
        _providerConnectionVaultStore = providerConnectionVaultStore ?? throw new ArgumentNullException(nameof(providerConnectionVaultStore));
        _providerConnectionRowService = providerConnectionRowService ?? new ProviderConnectionRowService(_providerConnectionVaultStore);
        _vaultPaths = vaultPaths ?? throw new ArgumentNullException(nameof(vaultPaths));
        _runGoogleAuthentication = googleAuthenticationRunner ?? throw new ArgumentNullException(nameof(googleAuthenticationRunner));
        _runGoogleHealthCheck = googleHealthCheckRunner ?? throw new ArgumentNullException(nameof(googleHealthCheckRunner));
        _runCodexAuthentication = codexAuthenticationRunner ?? throw new ArgumentNullException(nameof(codexAuthenticationRunner));
        _credentialsProviderLoginService = credentialsProviderLoginService ?? new CredentialsProviderLoginService(
            (profile, uri, ct) => _mainViewModel.LaunchManagedChromeForProviderAsync(profile, uri, ct));
        // Initialize commands
        SaveRowCommand = new AsyncRelayCommand<GoogleAccountRowViewModel>(
            SaveRowAsync,
            _ => !IsBatchLoginRunning);
        LoginRowCommand = new AsyncRelayCommand<GoogleAccountRowViewModel>(
            LoginRowAsync,
            row => !IsBatchLoginRunning && row.HasCredentials);
        CheckHealthRowCommand = new AsyncRelayCommand<GoogleAccountRowViewModel>(
            CheckHealthRowAsync,
            row => !IsBatchLoginRunning && row.HasCredentials);
        RemoveGoogleAccountCommand = new AsyncRelayCommand(
            RemoveGoogleAccountAsync,
            () => CanRemoveGoogleAccount);
        BatchLoginCommand = new AsyncRelayCommand(StartBatchLoginAsync, () =>
            (GoogleAccounts.Any(a => a.IsSelected && a.HasCredentials) ||
             CodexConnections.Any(c => c.IsSelected && c.HasCredentials)) &&
            !IsBatchLoginRunning);

        StopBatchLoginCommand = new RelayCommand(StopBatchLogin, () => IsBatchLoginRunning);
        RefreshCommand = new AsyncRelayCommand(RefreshDataAsync, () => !IsBatchLoginRunning);
        CheckAllHealthCommand = new AsyncRelayCommand(CheckAllHealthAsync, () => GoogleAccounts.Any(a => a.HasCredentials) && !IsBatchLoginRunning);
        ToggleSelectAllGoogleCommand = new RelayCommand(ToggleSelectAllGoogle, () => GoogleAccounts.Any(a => a.HasCredentials));
        ToggleSelectAllCodexCommand = new RelayCommand(ToggleSelectAllCodex, () => CodexConnections.Any(c => c.HasCredentials));

        // Codex commands
        SaveCodexRowCommand = new AsyncRelayCommand<CodexConnectionRowViewModel>(
            SaveCodexRowAsync,
            _ => !IsBatchLoginRunning);
        LoginCodexRowCommand = new AsyncRelayCommand<CodexConnectionRowViewModel>(
            LoginCodexRowAsync,
            row => !IsBatchLoginRunning && row?.HasCredentials == true);
        RemoveCodexConnectionCommand = new AsyncRelayCommand(
            RemoveCodexConnectionAsync,
            () => CanRemoveCodexConnection);

        // Provider commands
        SaveProviderRowCommand = new AsyncRelayCommand<ProviderConnectionRowViewModel>(
            SaveProviderRowAsync,
            _ => !IsBatchLoginRunning);
        LoginKiroRowCommand = new AsyncRelayCommand<ProviderConnectionRowViewModel>(
            LoginKiroRowAsync,
            row => !IsBatchLoginRunning && row?.HasCredentials == true);
        LoginGitHubRowCommand = new AsyncRelayCommand<ProviderConnectionRowViewModel>(
            LoginGitHubRowAsync,
            row => !IsBatchLoginRunning && row?.HasCredentials == true);
        LoginOpenRouterRowCommand = new AsyncRelayCommand<ProviderConnectionRowViewModel>(
            LoginOpenRouterRowAsync,
            row => !IsBatchLoginRunning && row?.HasCredentials == true);
        RemoveKiroConnectionCommand = new AsyncRelayCommand(
            RemoveKiroConnectionAsync,
            () => CanRemoveKiroConnection);
        RemoveGitHubConnectionCommand = new AsyncRelayCommand(
            RemoveGitHubConnectionAsync,
            () => CanRemoveGitHubConnection);
        RemoveOpenRouterConnectionCommand = new AsyncRelayCommand(
            RemoveOpenRouterConnectionAsync,
            () => CanRemoveOpenRouterConnection);

        _ = InitializeAsync();
    }

    public Task InitializationTask => _initializationTask ?? InitializeAsync();

    public Task InitializeAsync()
    {
        lock (_initializationLock)
        {
            _initializationTask ??= LoadDataAsync();
            return _initializationTask;
        }
    }

    // Selected tab index
    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set
        {
            if (_selectedTabIndex == value) return;
            _selectedTabIndex = value;
            OnPropertyChanged();
        }
    }

    // Status message
    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (_statusMessage == value) return;
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    // Google Accounts section
    public bool CanModifyCredentials => !IsBatchLoginRunning;

    public AsyncRelayCommand RefreshCommand { get; }
    private async Task LoadDataAsync()
    {
        await _mainViewModel.InitializationTask;

        try
        {
            SetStatus("Loading vault data...");

            // Try to open Google vault with remembered key
            _vaultSession = await _googleAccountVaultStore.TryOpenRememberedAsync(
                _vaultPaths.VaultPath,
                CancellationToken.None);

            if (_vaultSession == null)
            {
                SetStatus("Google vault locked. Unlock to manage credentials.");
                IsVaultLocked = true;
                // Still show all profiles but with empty credentials
                await LoadProfileRowsAsync(null);
                await LoadProviderConnectionsAsync();
                return;
            }

            IsVaultLocked = false;
            // Load all profiles with their credentials
            await LoadProfileRowsAsync(_vaultSession);

            // Load provider connections
            await LoadProviderConnectionsAsync();

            var credentialCount = GoogleAccounts.Count(a => a.HasCredentials);
            SetStatus($"Loaded {credentialCount} configured profiles from {GoogleAccounts.Count} total.");
        }
        catch (Exception ex)
        {
            SetStatus($"Error loading vault: {ex.Message}");
        }
    }

    private async Task RefreshDataAsync()
    {
        await LoadDataAsync();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // Launching is now delegated to MainViewModel; no local helper needed.

    public void SetStatus(string message)
    {
        StatusMessage = $"[{DateTime.Now:HH:mm:ss}] {message}";
    }

    public async Task CancelBatchLoginAsync()
    {
        _batchLoginCts?.Cancel();
        var batchTask = _batchLoginTask;
        if (batchTask != null)
        {
            await batchTask;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CancelBatchLoginAsync();
        _providerLoginCts?.Cancel();
        _providerLoginCts?.Dispose();
        _providerLoginCts = null;

        if (_vaultSession != null)
        {
            await _vaultSession.DisposeAsync();
            _vaultSession = null;
        }
    }
}
