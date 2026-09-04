using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using RouterPlus.Core.Models;
using RouterPlus.Core.Providers;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Storage;
using RouterPlus.Infrastructure.Security;

namespace RouterPlus.App.ViewModels;

/// <summary>
/// ViewModel for unified credentials manager dialog.
/// Manages Google accounts and provider connections.
/// Phase 5 Step 5.2 - Complete vault integration
/// </summary>
public sealed class CredentialsManagerViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly MainViewModel _mainViewModel;
    private readonly IGoogleAccountVaultStore _googleAccountVaultStore;
    private readonly ProviderConnectionVaultStore _providerConnectionVaultStore;
    private readonly GoogleAccountVaultPaths _vaultPaths;
    // Compatibility seam: MainViewModel composes this runner from the shared
    // IGoogleAuthenticationService and owns the Chrome/browser lifetime.
    private readonly Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<GoogleLoginResult>> _runGoogleAuthentication;
    private readonly Func<ChromeProfile, CodexLoginCredential, CancellationToken, Task<CodexLoginResult>> _runCodexAuthentication;

    private int _selectedTabIndex;
    private string _statusMessage = string.Empty;
    private GoogleAccountRowViewModel? _selectedGoogleAccount;
    private CodexConnectionRowViewModel? _selectedCodexConnection;
    private ProviderConnectionRowViewModel? _selectedKiroConnection;
    private ProviderConnectionRowViewModel? _selectedGitHubConnection;
    private ProviderConnectionRowViewModel? _selectedOpenRouterConnection;
    private GoogleAccountVaultSession? _vaultSession;
    private bool _isBatchLoginRunning;
    private bool _isVaultLocked = true;
    private CancellationTokenSource? _batchLoginCts;
    private Task? _batchLoginTask;
    private readonly object _initializationLock = new();
    private Task? _initializationTask;

    public event PropertyChangedEventHandler? PropertyChanged;

    public CredentialsManagerViewModel(
        MainViewModel mainViewModel,
        IGoogleAccountVaultStore googleAccountVaultStore,
        ProviderConnectionVaultStore providerConnectionVaultStore,
        GoogleAccountVaultPaths vaultPaths,
        Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<GoogleLoginResult>> googleAuthenticationRunner,
        Func<ChromeProfile, CodexLoginCredential, CancellationToken, Task<CodexLoginResult>> codexAuthenticationRunner)
    {
        _mainViewModel = mainViewModel ?? throw new ArgumentNullException(nameof(mainViewModel));
        _googleAccountVaultStore = googleAccountVaultStore ?? throw new ArgumentNullException(nameof(googleAccountVaultStore));
        _providerConnectionVaultStore = providerConnectionVaultStore ?? throw new ArgumentNullException(nameof(providerConnectionVaultStore));
        _vaultPaths = vaultPaths ?? throw new ArgumentNullException(nameof(vaultPaths));
        _runGoogleAuthentication = googleAuthenticationRunner ?? throw new ArgumentNullException(nameof(googleAuthenticationRunner));
        _runCodexAuthentication = codexAuthenticationRunner ?? throw new ArgumentNullException(nameof(codexAuthenticationRunner));

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
        BatchLoginCommand = new AsyncRelayCommand(StartBatchLoginAsync, () => GoogleAccounts.Any(a => a.IsSelected && a.HasCredentials) && !IsBatchLoginRunning);

        StopBatchLoginCommand = new RelayCommand(StopBatchLogin, () => IsBatchLoginRunning);
        RefreshCommand = new AsyncRelayCommand(RefreshDataAsync, () => !IsBatchLoginRunning);
        CheckAllHealthCommand = new AsyncRelayCommand(CheckAllHealthAsync, () => GoogleAccounts.Any(a => a.HasCredentials) && !IsBatchLoginRunning);

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
    public ObservableCollection<GoogleAccountRowViewModel> GoogleAccounts { get; } = new();

    /// <summary>
    /// Filtered view of Google accounts that have credentials configured.
    /// Used in ComboBox dropdowns to avoid showing empty entries.
    /// </summary>
    public IEnumerable<GoogleAccountRowViewModel> ConfiguredGoogleAccounts =>
        GoogleAccounts.Where(account => account.HasCredentials &&
                                       !string.IsNullOrWhiteSpace(account.Email));

    public int SelectedCount => GoogleAccounts.Count(a => a.IsSelected && a.HasCredentials);

    public int CodexSelectedCount => CodexConnections.Count(c => c.IsSelected && c.HasCredentials);

    public bool IsBatchLoginRunning
    {
        get => _isBatchLoginRunning;
        private set
        {
            if (_isBatchLoginRunning == value) return;
            _isBatchLoginRunning = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanModifyCredentials));
            OnPropertyChanged(nameof(CanRemoveGoogleAccount));
            OnPropertyChanged(nameof(CanRemoveCodexConnection));
            OnPropertyChanged(nameof(CanRemoveKiroConnection));
            OnPropertyChanged(nameof(CanRemoveGitHubConnection));
            OnPropertyChanged(nameof(CanRemoveOpenRouterConnection));
            BatchLoginCommand.RaiseCanExecuteChanged();
            StopBatchLoginCommand.RaiseCanExecuteChanged();
            RefreshCommand.RaiseCanExecuteChanged();
            CheckAllHealthCommand.RaiseCanExecuteChanged();
            SaveRowCommand.RaiseCanExecuteChanged();
            LoginRowCommand.RaiseCanExecuteChanged();
            CheckHealthRowCommand.RaiseCanExecuteChanged();
            RemoveGoogleAccountCommand.RaiseCanExecuteChanged();
            SaveCodexRowCommand.RaiseCanExecuteChanged();
            LoginCodexRowCommand.RaiseCanExecuteChanged();
            RemoveCodexConnectionCommand.RaiseCanExecuteChanged();
            SaveProviderRowCommand.RaiseCanExecuteChanged();
            LoginKiroRowCommand.RaiseCanExecuteChanged();
            LoginGitHubRowCommand.RaiseCanExecuteChanged();
            LoginOpenRouterRowCommand.RaiseCanExecuteChanged();
            RemoveKiroConnectionCommand.RaiseCanExecuteChanged();
            RemoveGitHubConnectionCommand.RaiseCanExecuteChanged();
            RemoveOpenRouterConnectionCommand.RaiseCanExecuteChanged();
        }
    }

    public bool CanModifyCredentials => !IsBatchLoginRunning;

    public bool IsVaultLocked
    {
        get => _isVaultLocked;
        private set
        {
            if (_isVaultLocked == value) return;
            _isVaultLocked = value;
            OnPropertyChanged();
        }
    }

    public GoogleAccountRowViewModel? SelectedGoogleAccount
    {
        get => _selectedGoogleAccount;
        set
        {
            if (_selectedGoogleAccount == value) return;
            _selectedGoogleAccount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRemoveGoogleAccount));
            RemoveGoogleAccountCommand.RaiseCanExecuteChanged();
        }
    }

    public bool CanRemoveGoogleAccount =>
        CanModifyCredentials && SelectedGoogleAccount?.HasCredentials == true;

    // Provider connections per provider
    public ObservableCollection<CodexConnectionRowViewModel> CodexConnections { get; } = new();
    public ObservableCollection<ProviderConnectionRowViewModel> KiroConnections { get; } = new();
    public ObservableCollection<ProviderConnectionRowViewModel> GitHubConnections { get; } = new();
    public ObservableCollection<ProviderConnectionRowViewModel> OpenRouterConnections { get; } = new();

    public CodexConnectionRowViewModel? SelectedCodexConnection
    {
        get => _selectedCodexConnection;
        set
        {
            if (_selectedCodexConnection == value) return;
            _selectedCodexConnection = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRemoveCodexConnection));
            RemoveCodexConnectionCommand?.RaiseCanExecuteChanged();
        }
    }

    public ProviderConnectionRowViewModel? SelectedKiroConnection
    {
        get => _selectedKiroConnection;
        set
        {
            if (_selectedKiroConnection == value) return;
            _selectedKiroConnection = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRemoveKiroConnection));
            RemoveKiroConnectionCommand.RaiseCanExecuteChanged();
        }
    }

    public ProviderConnectionRowViewModel? SelectedGitHubConnection
    {
        get => _selectedGitHubConnection;
        set
        {
            if (_selectedGitHubConnection == value) return;
            _selectedGitHubConnection = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRemoveGitHubConnection));
            RemoveGitHubConnectionCommand.RaiseCanExecuteChanged();
        }
    }

    public ProviderConnectionRowViewModel? SelectedOpenRouterConnection
    {
        get => _selectedOpenRouterConnection;
        set
        {
            if (_selectedOpenRouterConnection == value) return;
            _selectedOpenRouterConnection = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRemoveOpenRouterConnection));
            RemoveOpenRouterConnectionCommand.RaiseCanExecuteChanged();
        }
    }

    // Commands
    public AsyncRelayCommand<GoogleAccountRowViewModel> SaveRowCommand { get; }
    public AsyncRelayCommand<GoogleAccountRowViewModel> LoginRowCommand { get; }
    public AsyncRelayCommand<GoogleAccountRowViewModel> CheckHealthRowCommand { get; }
    public AsyncRelayCommand RemoveGoogleAccountCommand { get; }
    public AsyncRelayCommand BatchLoginCommand { get; }
    public RelayCommand StopBatchLoginCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand CheckAllHealthCommand { get; }

    // Codex commands
    public AsyncRelayCommand<CodexConnectionRowViewModel> SaveCodexRowCommand { get; }
    public AsyncRelayCommand<CodexConnectionRowViewModel> LoginCodexRowCommand { get; }
    public AsyncRelayCommand RemoveCodexConnectionCommand { get; }

    // Provider commands (Kiro, GitHub, OpenRouter)
    public AsyncRelayCommand<ProviderConnectionRowViewModel> SaveProviderRowCommand { get; }
    public AsyncRelayCommand<ProviderConnectionRowViewModel> LoginKiroRowCommand { get; }
    public AsyncRelayCommand<ProviderConnectionRowViewModel> LoginGitHubRowCommand { get; }
    public AsyncRelayCommand<ProviderConnectionRowViewModel> LoginOpenRouterRowCommand { get; }
    public AsyncRelayCommand RemoveKiroConnectionCommand { get; }
    public AsyncRelayCommand RemoveGitHubConnectionCommand { get; }
    public AsyncRelayCommand RemoveOpenRouterConnectionCommand { get; }

    public bool CanRemoveCodexConnection =>
        CanModifyCredentials && SelectedCodexConnection?.HasCredentials == true;

    public bool CanRemoveKiroConnection =>
        CanModifyCredentials && SelectedKiroConnection?.HasCredentials == true;

    public bool CanRemoveGitHubConnection =>
        CanModifyCredentials && SelectedGitHubConnection?.HasCredentials == true;

    public bool CanRemoveOpenRouterConnection =>
        CanModifyCredentials && SelectedOpenRouterConnection?.HasCredentials == true;

    public Task? BatchLoginTask => _batchLoginTask;

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

    private async Task LoadProfileRowsAsync(GoogleAccountVaultSession? session)
    {
        await Task.Yield();

        GoogleAccounts.Clear();

        // Create row for each profile
        foreach (var profile in _mainViewModel.FilteredProfiles)
        {
            var credential = ResolveCredentialForProfile(session?.Vault, profile);

            var row = new GoogleAccountRowViewModel
            {
                ProfileName = profile.Name,
                ProfileId = profile.Id,
                Email = credential?.Email ?? string.Empty,
                Password = credential?.Password ?? string.Empty,
                TotpSecret = credential?.TotpSecret ?? string.Empty,
                HasCredentials = credential != null,
                IsEditing = false,
                IsSelected = false,
                IsVaultUnlocked = session != null
            };

            // Subscribe to property changes to update SelectedCount
            row.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName is nameof(GoogleAccountRowViewModel.IsSelected)
                    or nameof(GoogleAccountRowViewModel.HasCredentials))
                {
                    OnPropertyChanged(nameof(SelectedCount));
                    OnPropertyChanged(nameof(ConfiguredGoogleAccounts));
                    BatchLoginCommand.RaiseCanExecuteChanged();
                }
                else if (e.PropertyName is nameof(GoogleAccountRowViewModel.Email))
                {
                    OnPropertyChanged(nameof(ConfiguredGoogleAccounts));
                }
            };

            GoogleAccounts.Add(row);
        }

        OnPropertyChanged(nameof(ConfiguredGoogleAccounts));
    }

    /// <summary>
    /// Resolves the credential for a profile using the stable profile Id first,
    /// then falls back to the legacy display-name key ONLY when the name maps
    /// unambiguously to a single current profile (no silent merge of shared names).
    /// A load never writes to the vault: legacy name-keyed records stay on disk
    /// until an explicit save migrates them to the stable Id.
    /// </summary>
    private GoogleLoginCredential? ResolveCredentialForProfile(GoogleAccountVault? vault, ChromeProfile profile)
    {
        if (vault is null)
        {
            return null;
        }

        var byId = vault.Find(profile.Id);
        if (byId is not null)
        {
            return byId;
        }

        // Legacy compatibility: adopt a name-keyed record only when exactly one
        // current profile carries that display name.
        return HasUniqueProfileName(profile.Name) ? vault.Find(profile.Name) : null;
    }

    private bool HasUniqueProfileName(string profileName)
    {
        return _mainViewModel.Profiles.Count(p => p.Name == profileName) == 1;
    }

    /// <summary>
    /// Resolves the ChromeProfile backing a row by stable Id first, falling
    /// back to the display name for legacy rows created before Id resolution.
    /// </summary>
    private ChromeProfile? ResolveRowProfile(GoogleAccountRowViewModel row)
    {
        if (!string.IsNullOrEmpty(row.ProfileId))
        {
            return _mainViewModel.Profiles.FirstOrDefault(p => p.Id == row.ProfileId);
        }

        return HasUniqueProfileName(row.ProfileName)
            ? _mainViewModel.Profiles.FirstOrDefault(p => p.Name == row.ProfileName)
            : null;
    }

    private async Task LoadProviderConnectionsAsync()
    {
        CodexConnections.Clear();
        KiroConnections.Clear();
        GitHubConnections.Clear();
        OpenRouterConnections.Clear();

        foreach (var profile in _mainViewModel.FilteredProfiles)
        {
            // Load Codex with full inline editing support
            var codexConnection = await _providerConnectionVaultStore.GetConnectionAsync(
                profile.Name,
                ProviderKind.Codex,
                CancellationToken.None);

            var codexRow = new CodexConnectionRowViewModel
            {
                ProfileId = profile.Id,
                ProfileName = profile.Name,
                AuthMethod = codexConnection?.PreferredMethod ?? AuthMethod.GoogleOAuth,
                LinkedGoogleAccount = codexConnection?.LinkedGoogleAccount ?? string.Empty,
                Email = codexConnection?.DirectCredential?.Email ?? string.Empty,
                Password = codexConnection?.DirectCredential?.Password ?? string.Empty,
                TotpSecret = codexConnection?.DirectCredential?.TotpSecret ?? string.Empty,
                HasCredentials = codexConnection != null &&
                    (!string.IsNullOrEmpty(codexConnection.LinkedGoogleAccount) ||
                     codexConnection.DirectCredential != null),
                IsEditing = false,
                IsSelected = false
            };

            codexRow.SetConfiguredGoogleAccounts(ConfiguredGoogleAccounts);

            // Subscribe to property changes to update selection count
            codexRow.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName is nameof(CodexConnectionRowViewModel.IsSelected)
                    or nameof(CodexConnectionRowViewModel.HasCredentials))
                {
                    OnPropertyChanged(nameof(CodexSelectedCount));
                }
            };

            CodexConnections.Add(codexRow);

            // Load other providers with full inline editing support
            var providers = new[]
            {
                (ProviderKind.Kiro, KiroConnections),
                (ProviderKind.GitHub, GitHubConnections),
                (ProviderKind.OpenRouter, OpenRouterConnections)
            };

            foreach (var (kind, collection) in providers)
            {
                var connection = await _providerConnectionVaultStore.GetConnectionAsync(
                    profile.Name,
                    kind,
                    CancellationToken.None);

                var row = new ProviderConnectionRowViewModel
                {
                    ProfileId = profile.Id,
                    ProfileName = profile.Name,
                    AuthMethod = connection?.PreferredMethod ?? AuthMethod.GoogleOAuth,
                    LinkedGoogleAccount = connection?.LinkedGoogleAccount ?? string.Empty,
                    Email = connection?.DirectCredential?.Email ?? string.Empty,
                    Password = connection?.DirectCredential?.Password ?? string.Empty,
                    TotpSecret = connection?.DirectCredential?.TotpSecret ?? string.Empty,
                    HasCredentials = connection != null &&
                        (!string.IsNullOrEmpty(connection.LinkedGoogleAccount) ||
                         connection.DirectCredential != null),
                    IsEditing = false,
                    IsSelected = false
                };

                collection.Add(row);
            }
        }
    }

    private async Task RefreshDataAsync()
    {
        await LoadDataAsync();
    }

    public async Task UnlockVaultAsync(string vaultPassword, bool remember)
    {
        if (string.IsNullOrWhiteSpace(vaultPassword))
        {
            SetStatus("Vault password is required");
            return;
        }

        try
        {
            GoogleAccountVaultSession session;
            if (File.Exists(_vaultPaths.VaultPath))
            {
                session = await _googleAccountVaultStore.OpenAsync(
                    _vaultPaths.VaultPath,
                    vaultPassword,
                    CancellationToken.None);
            }
            else
            {
                session = await _googleAccountVaultStore.CreateAsync(
                    _vaultPaths.VaultPath,
                    vaultPassword,
                    CancellationToken.None);
            }

            if (_vaultSession != null)
            {
                await _vaultSession.DisposeAsync();
            }

            _vaultSession = session;
            if (remember)
            {
                await _vaultSession.RememberAsync(CancellationToken.None);
            }

            IsVaultLocked = false;
            await LoadProfileRowsAsync(_vaultSession);
            await LoadProviderConnectionsAsync();
            SetStatus($"Vault unlocked. Loaded {GoogleAccounts.Count(a => a.HasCredentials)} configured profiles.");
        }
        catch (Exception ex)
        {
            SetStatus($"Unable to unlock vault: {GetSafeVaultErrorMessage(ex)}");
        }
    }

    private static string GetSafeVaultErrorMessage(Exception ex)
    {
        return ex switch
        {
            ArgumentException => "Invalid vault password",
            UnauthorizedAccessException => "Access denied",
            System.Security.Cryptography.CryptographicException => "Invalid vault password",
            InvalidOperationException => ex.Message,
            _ => "Vault could not be opened"
        };
    }

    private async Task SaveRowAsync(GoogleAccountRowViewModel? row)
    {
        if (row == null) return;

        if (IsBatchLoginRunning)
        {
            SetStatus("Batch login is already running");
            return;
        }

        // Existing rows enter edit mode before the command saves changes.
        if (row.HasCredentials && !row.IsEditing)
        {
            row.IsEditing = true;
            SetStatus($"Editing credentials for {row.ProfileName}");
            return;
        }

        if (_vaultSession == null)
        {
            SetStatus("Vault not unlocked");
            return;
        }

        // Validate
        if (string.IsNullOrWhiteSpace(row.Email))
        {
            SetStatus("Email is required");
            return;
        }
        if (string.IsNullOrWhiteSpace(row.Password))
        {
            SetStatus("Password is required");
            return;
        }

        try
        {
            // Resolve the underlying profile: by stable Id first, then by display
            // name so legacy rows created before Id resolution still work. Write
            // the resolved Id back onto the row for future operations.
            var profile = ResolveRowProfile(row);
            if (profile is null)
            {
                SetStatus($"Profile not found for {row.ProfileName}");
                return;
            }

            row.ProfileId = profile.Id;

            // Create credential keyed by the stable profile Id (TOTP required - use placeholder if empty).
            var credential = new GoogleLoginCredential(
                row.ProfileId,
                row.Email.Trim(),
                row.Password,
                string.IsNullOrWhiteSpace(row.TotpSecret) ? "NONE" : row.TotpSecret.Trim());

            // Upsert into vault (immutable pattern). Remove any prior record keyed
            // by the stable Id AND any legacy record keyed by the display name,
            // but only remove the name-keyed record when the name maps
            // unambiguously to this profile (never merge shared names).
            var currentVault = _vaultSession.Vault;
            var filtered = currentVault.Records
                .Where(r => r.ProfileId != row.ProfileId)
                .Where(r => !HasUniqueProfileName(row.ProfileName) || r.ProfileId != row.ProfileName);
            var updated = filtered.Append(credential);
            var newVault = new GoogleAccountVault(updated);
            _vaultSession.Replace(newVault);
            await _googleAccountVaultStore.SaveAsync(_vaultSession, CancellationToken.None);

            // Update UI state
            row.HasCredentials = true;
            row.IsEditing = false;

            OnPropertyChanged(nameof(ConfiguredGoogleAccounts));
            SetStatus($"Saved credentials for {row.ProfileName}");
        }
        catch (Exception ex)
        {
            SetStatus($"Error saving: {ex.Message}");
        }
    }

    private async Task LoginRowAsync(GoogleAccountRowViewModel? row)
    {
        if (IsBatchLoginRunning)
        {
            SetStatus("Batch login is already running");
            return;
        }

        if (row == null || !row.HasCredentials)
        {
            SetStatus("No credentials to login with");
            return;
        }

        if (_vaultSession == null)
        {
            SetStatus("Vault not unlocked. Please unlock the vault first.");
            return;
        }

        if (IsBatchLoginRunning)
        {
            SetStatus("Batch login is already running");
            return;
        }

        // Find matching profile: by stable Id first, then by display name for
        // legacy rows that predate Id resolution.
        var profile = ResolveRowProfile(row);
        if (profile == null)
        {
            SetStatus($"❌ {row.ProfileName}: Profile not found");
            return;
        }

        row.ProfileId = profile.Id;

        // Create credential from row, keyed by the stable profile Id
        var credential = new GoogleLoginCredential(
            row.ProfileId,
            row.Email,
            row.Password,
            string.IsNullOrWhiteSpace(row.TotpSecret) ? "NONE" : row.TotpSecret.Trim());

        SetStatus($"🚀 Logging in {row.ProfileName}...");

        try
        {
            var result = await _runGoogleAuthentication(profile, credential, CancellationToken.None);

            if (result.Category == GoogleLoginResultCategory.Success)
            {
                SetStatus($"✓ {row.ProfileName}: Login successful");
            }
            else if (result.Category == GoogleLoginResultCategory.ManualInterventionRequired)
            {
                SetStatus($"⚠ {row.ProfileName}: Manual intervention required");
            }
            else
            {
                SetStatus($"❌ {row.ProfileName}: {result.Message ?? result.Category.ToString()}");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"❌ {row.ProfileName}: {ex.Message}");
        }
    }

    private async Task CheckHealthRowAsync(GoogleAccountRowViewModel? row)
    {
        if (row == null || !row.HasCredentials)
        {
            SetStatus("No credentials to check");
            return;
        }

        if (_vaultSession == null)
        {
            SetStatus("Vault not unlocked. Please unlock the vault first.");
            return;
        }

        if (IsBatchLoginRunning)
        {
            SetStatus("Batch login is already running");
            return;
        }

        var profile = ResolveRowProfile(row);
        if (profile == null)
        {
            row.UpdateHealthStatus(CredentialHealthCheckResult.Error($"Profile not found"));
            SetStatus($"❌ {row.ProfileName}: Profile not found");
            return;
        }

        row.ProfileId = profile.Id;

        var credential = new GoogleLoginCredential(
            row.ProfileId,
            row.Email,
            row.Password,
            string.IsNullOrWhiteSpace(row.TotpSecret) ? "NONE" : row.TotpSecret.Trim());

        row.UpdateHealthStatus(CredentialHealthCheckResult.Checking());
        SetStatus($"⟳ Checking health for {row.ProfileName}...");

        try
        {
            var result = await _runGoogleAuthentication(profile, credential, CancellationToken.None);

            var healthResult = MapLoginResultToHealthCheck(result);
            row.UpdateHealthStatus(healthResult);

            SetStatus($"{healthResult.Status.ToEmoji()} {row.ProfileName}: {healthResult.Message}");
        }
        catch (Exception ex)
        {
            var healthResult = CredentialHealthCheckResult.Error($"Health check failed: {ex.Message}", ex);
            row.UpdateHealthStatus(healthResult);
            SetStatus($"❌ {row.ProfileName}: {ex.Message}");
        }
    }

    private async Task CheckAllHealthAsync()
    {
        var accountsWithCredentials = GoogleAccounts.Where(a => a.HasCredentials).ToList();
        if (!accountsWithCredentials.Any())
        {
            SetStatus("No configured accounts to check");
            return;
        }

        SetStatus($"Checking health for {accountsWithCredentials.Count} account(s)...");

        var healthyCount = 0;
        var unhealthyCount = 0;

        foreach (var account in accountsWithCredentials)
        {
            await CheckHealthRowAsync(account);

            if (account.HealthStatus?.Status.IsHealthy() == true)
            {
                healthyCount++;
            }
            else if (account.HealthStatus?.Status.NeedsAttention() == true)
            {
                unhealthyCount++;
            }

            // Small delay between checks to avoid overwhelming the system
            await Task.Delay(500);
        }

        SetStatus($"Health check completed: {healthyCount} healthy, {unhealthyCount} need attention");
    }

    private static CredentialHealthCheckResult MapLoginResultToHealthCheck(GoogleLoginResult loginResult)
    {
        return loginResult.Category switch
        {
            GoogleLoginResultCategory.Success =>
                CredentialHealthCheckResult.Healthy("Credentials are valid"),

            GoogleLoginResultCategory.InvalidCredentials =>
                CredentialHealthCheckResult.Invalid("Invalid email, password, or TOTP code"),

            GoogleLoginResultCategory.ManualInterventionRequired =>
                CredentialHealthCheckResult.RequiresAction(loginResult.Message),

            GoogleLoginResultCategory.Timeout =>
                CredentialHealthCheckResult.Error("Health check timed out"),

            GoogleLoginResultCategory.Cancelled =>
                CredentialHealthCheckResult.Error("Health check was cancelled"),

            GoogleLoginResultCategory.BrowserDisconnected =>
                CredentialHealthCheckResult.Error("Browser connection lost"),

            GoogleLoginResultCategory.UnsupportedPage =>
                CredentialHealthCheckResult.RequiresAction(loginResult.Message),

            _ => CredentialHealthCheckResult.Unknown($"Unexpected result: {loginResult.Category}")
        };
    }

    private Task StartBatchLoginAsync()
    {
        var selectedRows = GoogleAccounts
            .Where(a => a.IsSelected && a.HasCredentials)
            .ToList();
        var batchCts = new CancellationTokenSource();
        _batchLoginCts = batchCts;
        IsBatchLoginRunning = true;
        _batchLoginTask = BatchLoginAsync(batchCts, selectedRows);
        OnPropertyChanged(nameof(BatchLoginTask));
        return _batchLoginTask;
    }

    private void StopBatchLogin()
    {
        _batchLoginCts?.Cancel();
    }

    private async Task BatchLoginAsync(
        CancellationTokenSource batchCts,
        IReadOnlyList<GoogleAccountRowViewModel> selectedRows)
    {
        // Yield so StartBatchLoginAsync can publish the task before preflight
        // exits through the cleanup path.
        await Task.Yield();

        var successCount = 0;
        var failCount = 0;
        var cancelled = false;

        try
        {
            if (!selectedRows.Any())
            {
                SetStatus("No profiles selected");
                return;
            }

            if (_vaultSession == null)
            {
                SetStatus("Vault not unlocked. Please unlock the vault first.");
                return;
            }

            SetStatus($"Starting batch login for {selectedRows.Count} profile(s)...");

            foreach (var row in selectedRows)
            {
                // Find matching profile: by stable Id first, then by display name
                // for legacy rows that predate Id resolution.
                batchCts.Token.ThrowIfCancellationRequested();

                var profile = ResolveRowProfile(row);
                if (profile == null)
                {
                    SetStatus($"❌ {row.ProfileName}: Profile not found");
                    failCount++;
                    continue;
                }

                row.ProfileId = profile.Id;

                try
                {
                    // Create credential from row, keyed by the stable profile Id.
                    // Invalid row data must fail only this profile, not the batch.
                    var credential = new GoogleLoginCredential(
                        row.ProfileId,
                        row.Email,
                        row.Password,
                        string.IsNullOrWhiteSpace(row.TotpSecret) ? "NONE" : row.TotpSecret.Trim());

                    SetStatus($"🚀 Logging in {row.ProfileName}...");

                    var result = await _runGoogleAuthentication(profile, credential, batchCts.Token);

                    if (batchCts.IsCancellationRequested)
                    {
                        cancelled = true;
                        break;
                    }

                    if (result.Category == GoogleLoginResultCategory.Success)
                    {
                        SetStatus($"✓ {row.ProfileName}: Login successful");
                        successCount++;
                    }
                    else if (result.Category == GoogleLoginResultCategory.Cancelled)
                    {
                        SetStatus($"⏹ {row.ProfileName}: Login cancelled");
                        cancelled = true;
                        break;
                    }
                    else if (result.Category == GoogleLoginResultCategory.ManualInterventionRequired)
                    {
                        SetStatus($"⚠ {row.ProfileName}: Manual intervention required");
                        failCount++;
                    }
                    else
                    {
                        SetStatus($"❌ {row.ProfileName}: {result.Message}");
                        failCount++;
                    }
                }
                catch (OperationCanceledException) when (batchCts.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }
                catch (Exception ex)
                {
                    SetStatus($"❌ {row.ProfileName}: {ex.Message}");
                    failCount++;
                }
            }

            SetStatus(cancelled
                ? $"Batch login cancelled: {successCount} succeeded, {failCount} failed"
                : $"Batch login completed: {successCount} succeeded, {failCount} failed");
        }
        catch (OperationCanceledException) when (batchCts.IsCancellationRequested)
        {
            SetStatus($"Batch login cancelled: {successCount} succeeded, {failCount} failed");
        }
        finally
        {
            if (ReferenceEquals(_batchLoginCts, batchCts))
            {
                _batchLoginCts = null;
            }

            batchCts.Dispose();
            IsBatchLoginRunning = false;
            _batchLoginTask = null;
            OnPropertyChanged(nameof(BatchLoginTask));
        }
    }

    private Task RemoveGoogleAccountAsync()
    {
        return SelectedGoogleAccount is { } row
            ? RemoveGoogleAccountAsync(row)
            : Task.CompletedTask;
    }

    public async Task RemoveGoogleAccountAsync(GoogleAccountRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        if (_vaultSession == null)
        {
            SetStatus("Vault not unlocked");
            return;
        }

        var profileName = row.ProfileName;

        try
        {
            // Remove from vault (immutable - filter and replace). Key by the
            // stable profile Id, and ALSO drop a legacy name-keyed record only
            // when the display name maps unambiguously to this profile.
            var currentVault = _vaultSession.Vault;
            var filteredRecords = currentVault.Records
                .Where(r => r.ProfileId != row.ProfileId)
                .Where(r => !HasUniqueProfileName(row.ProfileName) || r.ProfileId != row.ProfileName);
            var newVault = new GoogleAccountVault(filteredRecords);
            _vaultSession.Replace(newVault);
            await _googleAccountVaultStore.SaveAsync(_vaultSession, CancellationToken.None);

            // Update UI - clear credentials but keep row.
            row.Email = string.Empty;
            row.Password = string.Empty;
            row.TotpSecret = string.Empty;
            row.HasCredentials = false;
            row.IsEditing = false;
            OnPropertyChanged(nameof(CanRemoveGoogleAccount));
            OnPropertyChanged(nameof(ConfiguredGoogleAccounts));
            RemoveGoogleAccountCommand.RaiseCanExecuteChanged();

            SetStatus($"Removed credentials for {profileName}");
        }
        catch (Exception ex)
        {
            SetStatus($"Error removing account: {ex.Message}");
        }
    }

    public async Task RemoveGoogleAccountAsync(string profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            SetStatus("Profile is required");
            return;
        }

        var matchingRows = GoogleAccounts
            .Where(account => account.ProfileName == profileName)
            .ToList();
        if (matchingRows.Count != 1)
        {
            SetStatus(matchingRows.Count == 0
                ? $"Profile not found for {profileName}"
                : $"Profile name is ambiguous: {profileName}");
            return;
        }

        await RemoveGoogleAccountAsync(matchingRows[0]);
    }

    private async Task SaveCodexRowAsync(CodexConnectionRowViewModel? row)
    {
        if (row == null) return;

        if (IsBatchLoginRunning)
        {
            SetStatus("Batch login is already running");
            return;
        }

        // Existing rows enter edit mode before the command saves changes
        if (row.HasCredentials && !row.IsEditing)
        {
            row.IsEditing = true;
            SetStatus($"Editing Codex credentials for {row.ProfileName}");
            return;
        }

        // Validate based on auth method
        if (row.AuthMethod == AuthMethod.GoogleOAuth)
        {
            if (string.IsNullOrWhiteSpace(row.LinkedGoogleAccount))
            {
                SetStatus("Google account is required for OAuth method");
                return;
            }

            // Verify the linked Google account exists
            var googleAccount = GoogleAccounts.FirstOrDefault(a =>
                a.Email.Equals(row.LinkedGoogleAccount, StringComparison.OrdinalIgnoreCase));
            if (googleAccount == null)
            {
                SetStatus($"Google account '{row.LinkedGoogleAccount}' not found in vault");
                return;
            }
        }
        else // Direct
        {
            if (string.IsNullOrWhiteSpace(row.Email))
            {
                SetStatus("Email is required for Direct login");
                return;
            }
            if (string.IsNullOrWhiteSpace(row.Password))
            {
                SetStatus("Password is required for Direct login");
                return;
            }
        }

        try
        {
            // Build connection
            var connection = new ProviderAuthConnection
            {
                ProfileName = row.ProfileName,
                Provider = ProviderKind.Codex,
                PreferredMethod = row.AuthMethod,
                LinkedGoogleAccount = row.AuthMethod == AuthMethod.GoogleOAuth
                    ? row.LinkedGoogleAccount
                    : null,
                DirectCredential = row.AuthMethod == AuthMethod.Direct
                    ? new ProviderCredential
                    {
                        Email = row.Email.Trim(),
                        Password = row.Password,
                        TotpSecret = string.IsNullOrWhiteSpace(row.TotpSecret)
                            ? null
                            : row.TotpSecret.Trim()
                    }
                    : null
            };

            await _providerConnectionVaultStore.SaveConnectionAsync(connection, CancellationToken.None);

            // Update UI state
            row.HasCredentials = true;
            row.IsEditing = false;

            SetStatus($"Saved Codex credentials for {row.ProfileName}");
        }
        catch (Exception ex)
        {
            SetStatus($"Error saving Codex credentials: {ex.Message}");
        }
    }

    private Task RemoveCodexConnectionAsync()
    {
        return SelectedCodexConnection is { } row
            ? RemoveCodexConnectionAsync(row)
            : Task.CompletedTask;
    }

    private async Task RemoveCodexConnectionAsync(CodexConnectionRowViewModel row)
    {
        ArgumentNullException.ThrowIfNull(row);

        var profileName = row.ProfileName;

        try
        {
            await _providerConnectionVaultStore.RemoveConnectionAsync(
                profileName,
                ProviderKind.Codex,
                CancellationToken.None);

            // Update UI - clear credentials but keep row
            row.AuthMethod = AuthMethod.GoogleOAuth;
            row.LinkedGoogleAccount = string.Empty;
            row.Email = string.Empty;
            row.Password = string.Empty;
            row.TotpSecret = string.Empty;
            row.HasCredentials = false;
            row.IsEditing = false;
            OnPropertyChanged(nameof(CanRemoveCodexConnection));
            RemoveCodexConnectionCommand.RaiseCanExecuteChanged();

            SetStatus($"Removed Codex credentials for {profileName}");
        }
        catch (Exception ex)
        {
            SetStatus($"Error removing Codex credentials: {ex.Message}");
        }
    }

    private async Task LoginCodexRowAsync(CodexConnectionRowViewModel? row)
    {
        if (row == null || !row.HasCredentials)
        {
            SetStatus("No Codex credentials to login with");
            return;
        }

        if (IsBatchLoginRunning)
        {
            SetStatus("Batch login is already running");
            return;
        }

        if (string.IsNullOrWhiteSpace(row.ProfileId))
        {
            SetStatus($"Cannot login {row.ProfileName}: Profile ID not resolved");
            return;
        }

        var profile = _mainViewModel.Profiles.FirstOrDefault(p => p.Id == row.ProfileId);
        if (profile == null)
        {
            SetStatus($"❌ {row.ProfileName}: Profile not found");
            return;
        }

        SetStatus($"🚀 Logging in Codex for {row.ProfileName}...");

        try
        {
            CodexLoginCredential credential;

            if (row.AuthMethod == AuthMethod.GoogleOAuth)
            {
                // Google OAuth flow: Login Google first, then auto-consent Codex
                if (string.IsNullOrWhiteSpace(row.LinkedGoogleAccount))
                {
                    SetStatus($"❌ {row.ProfileName}: No linked Google account");
                    return;
                }

                var googleAccount = GoogleAccounts.FirstOrDefault(a =>
                    a.Email.Equals(row.LinkedGoogleAccount, StringComparison.OrdinalIgnoreCase));

                if (googleAccount == null || !googleAccount.HasCredentials)
                {
                    SetStatus($"❌ {row.ProfileName}: Google account '{row.LinkedGoogleAccount}' not found or has no credentials");
                    return;
                }

                // Step 1: Login Google account first
                SetStatus($"🚀 {row.ProfileName}: Logging in Google account {row.LinkedGoogleAccount}...");

                var googleCredential = new GoogleLoginCredential(
                    googleAccount.ProfileId,
                    googleAccount.Email,
                    googleAccount.Password,
                    string.IsNullOrWhiteSpace(googleAccount.TotpSecret) ? "NONE" : googleAccount.TotpSecret.Trim());

                var googleResult = await _runGoogleAuthentication(profile, googleCredential, CancellationToken.None);

                if (googleResult.Category != GoogleLoginResultCategory.Success)
                {
                    SetStatus($"❌ {row.ProfileName}: Google login failed - {googleResult.Category}");
                    return;
                }

                SetStatus($"✓ {row.ProfileName}: Google logged in, starting Codex OAuth...");

                credential = CodexLoginCredential.FromGoogleOAuth(row.ProfileId, row.LinkedGoogleAccount);
            }
            else // Direct method
            {
                // Direct login: auto-fill OpenAI login form
                if (string.IsNullOrWhiteSpace(row.Email) || string.IsNullOrWhiteSpace(row.Password))
                {
                    SetStatus($"❌ {row.ProfileName}: Email and password required for Direct login");
                    return;
                }

                credential = CodexLoginCredential.FromDirect(
                    row.ProfileId,
                    row.Email,
                    row.Password,
                    string.IsNullOrWhiteSpace(row.TotpSecret) ? null : row.TotpSecret.Trim());
            }

            // Step 2: Run Codex login automation
            var result = await _runCodexAuthentication(profile, credential, CancellationToken.None);

            if (result.Category == CodexLoginResultCategory.Success)
            {
                SetStatus($"✓ {row.ProfileName}: Codex login successful");
            }
            else if (result.Category == CodexLoginResultCategory.ManualInterventionRequired)
            {
                SetStatus($"⚠ {row.ProfileName}: Manual intervention required - {result.Message}");
            }
            else
            {
                SetStatus($"❌ {row.ProfileName}: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            SetStatus($"❌ {row.ProfileName}: {ex.Message}");
        }
    }

    private async Task SaveProviderRowAsync(ProviderConnectionRowViewModel? row)
    {
        if (row == null) return;

        if (IsBatchLoginRunning)
        {
            SetStatus("Batch login is already running");
            return;
        }

        // Existing rows enter edit mode before the command saves changes
        if (row.HasCredentials && !row.IsEditing)
        {
            row.IsEditing = true;
            SetStatus($"Editing provider credentials for {row.ProfileName}");
            return;
        }

        // Determine provider kind based on which collection contains this row
        ProviderKind provider;
        if (KiroConnections.Contains(row))
            provider = ProviderKind.Kiro;
        else if (GitHubConnections.Contains(row))
            provider = ProviderKind.GitHub;
        else if (OpenRouterConnections.Contains(row))
            provider = ProviderKind.OpenRouter;
        else
        {
            SetStatus("Cannot determine provider for this row");
            return;
        }

        // Validate based on auth method
        if (row.AuthMethod == AuthMethod.GoogleOAuth)
        {
            if (string.IsNullOrWhiteSpace(row.LinkedGoogleAccount))
            {
                SetStatus("Google account is required for OAuth method");
                return;
            }

            // Verify the linked Google account exists
            var googleAccount = GoogleAccounts.FirstOrDefault(a =>
                a.Email.Equals(row.LinkedGoogleAccount, StringComparison.OrdinalIgnoreCase));
            if (googleAccount == null)
            {
                SetStatus($"Google account '{row.LinkedGoogleAccount}' not found in vault");
                return;
            }
        }
        else // Direct
        {
            if (string.IsNullOrWhiteSpace(row.Email))
            {
                SetStatus("Email is required for Direct login");
                return;
            }
            if (string.IsNullOrWhiteSpace(row.Password))
            {
                SetStatus("Password is required for Direct login");
                return;
            }
        }

        try
        {
            // Build connection
            var connection = new ProviderAuthConnection
            {
                ProfileName = row.ProfileName,
                Provider = provider,
                PreferredMethod = row.AuthMethod,
                LinkedGoogleAccount = row.AuthMethod == AuthMethod.GoogleOAuth
                    ? row.LinkedGoogleAccount
                    : null,
                DirectCredential = row.AuthMethod == AuthMethod.Direct
                    ? new ProviderCredential
                    {
                        Email = row.Email.Trim(),
                        Password = row.Password,
                        TotpSecret = string.IsNullOrWhiteSpace(row.TotpSecret)
                            ? null
                            : row.TotpSecret.Trim()
                    }
                    : null
            };

            await _providerConnectionVaultStore.SaveConnectionAsync(connection, CancellationToken.None);

            // Update UI state
            row.HasCredentials = true;
            row.IsEditing = false;

            SetStatus($"Saved {provider} credentials for {row.ProfileName}");
        }
        catch (Exception ex)
        {
            SetStatus($"Error saving {provider} credentials: {ex.Message}");
        }
    }

    private Task RemoveKiroConnectionAsync()
    {
        return SelectedKiroConnection is { } row
            ? RemoveProviderConnectionAsync(row, ProviderKind.Kiro)
            : Task.CompletedTask;
    }

    private Task RemoveGitHubConnectionAsync()
    {
        return SelectedGitHubConnection is { } row
            ? RemoveProviderConnectionAsync(row, ProviderKind.GitHub)
            : Task.CompletedTask;
    }

    private Task RemoveOpenRouterConnectionAsync()
    {
        return SelectedOpenRouterConnection is { } row
            ? RemoveProviderConnectionAsync(row, ProviderKind.OpenRouter)
            : Task.CompletedTask;
    }

    private async Task LoginKiroRowAsync(ProviderConnectionRowViewModel? row)
    {
        await LoginProviderRowAsync(row, ProviderKind.Kiro);
    }

    private async Task LoginGitHubRowAsync(ProviderConnectionRowViewModel? row)
    {
        await LoginProviderRowAsync(row, ProviderKind.GitHub);
    }

    private async Task LoginOpenRouterRowAsync(ProviderConnectionRowViewModel? row)
    {
        await LoginProviderRowAsync(row, ProviderKind.OpenRouter);
    }

    private async Task LoginProviderRowAsync(ProviderConnectionRowViewModel? row, ProviderKind provider)
    {
        if (row == null || !row.HasCredentials)
        {
            SetStatus($"No {provider} credentials to login with");
            return;
        }

        if (IsBatchLoginRunning)
        {
            SetStatus("Batch login is already running");
            return;
        }

        if (string.IsNullOrWhiteSpace(row.ProfileId))
        {
            SetStatus($"Cannot login {row.ProfileName}: Profile ID not resolved");
            return;
        }

        var profile = _mainViewModel.Profiles.FirstOrDefault(p => p.Id == row.ProfileId);
        if (profile == null)
        {
            SetStatus($"❌ {row.ProfileName}: Profile not found");
            return;
        }

        SetStatus($"🚀 Logging in {provider} for {row.ProfileName}...");

        try
        {
            // TODO: Implement provider direct login automation
            // For now, just show not implemented message
            SetStatus($"⚠ {provider} login not implemented yet for {row.ProfileName}");
            await Task.Delay(100); // Remove warning
        }
        catch (Exception ex)
        {
            SetStatus($"Error connecting {provider}: {ex.Message}");
        }
    }

    private async Task RemoveProviderConnectionAsync(ProviderConnectionRowViewModel row, ProviderKind provider)
    {
        ArgumentNullException.ThrowIfNull(row);

        var profileName = row.ProfileName;

        try
        {
            await _providerConnectionVaultStore.RemoveConnectionAsync(
                profileName,
                provider,
                CancellationToken.None);

            // Update UI - clear credentials but keep row
            row.AuthMethod = AuthMethod.GoogleOAuth;
            row.LinkedGoogleAccount = string.Empty;
            row.Email = string.Empty;
            row.Password = string.Empty;
            row.TotpSecret = string.Empty;
            row.HasCredentials = false;
            row.IsEditing = false;

            // Update CanRemove properties
            OnPropertyChanged(nameof(CanRemoveKiroConnection));
            OnPropertyChanged(nameof(CanRemoveGitHubConnection));
            OnPropertyChanged(nameof(CanRemoveOpenRouterConnection));
            RemoveKiroConnectionCommand.RaiseCanExecuteChanged();
            RemoveGitHubConnectionCommand.RaiseCanExecuteChanged();
            RemoveOpenRouterConnectionCommand.RaiseCanExecuteChanged();

            SetStatus($"Removed {provider} credentials for {profileName}");
        }
        catch (Exception ex)
        {
            SetStatus($"Error removing {provider} credentials: {ex.Message}");
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

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

        if (_vaultSession != null)
        {
            await _vaultSession.DisposeAsync();
            _vaultSession = null;
        }
    }
}

/// <summary>
/// Row for Google account in the list.
/// </summary>
public sealed class GoogleAccountRowViewModel : INotifyPropertyChanged
{
    private string _profileId = string.Empty;
    private string _profileName = string.Empty;
    private string _email = string.Empty;
    private string _password = string.Empty;
    private string _totpSecret = string.Empty;
    private bool _isSelected;
    private bool _isEditing;
    private bool _hasCredentials;
    private bool _isPasswordVisible;
    private bool _isTotpSecretVisible;
    private bool _isVaultUnlocked;
    private CredentialHealthCheckResult? _healthStatus;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Stable ChromeProfile.Id that keys Google vault records.
    /// </summary>
    public string ProfileId
    {
        get => _profileId;
        set
        {
            if (_profileId == value) return;
            _profileId = value;
            OnPropertyChanged();
        }
    }

    public string ProfileName
    {
        get => _profileName;
        set
        {
            if (_profileName == value) return;
            _profileName = value;
            OnPropertyChanged();
        }
    }

    public string Email
    {
        get => _email;
        set
        {
            if (_email == value) return;
            _email = value;
            OnPropertyChanged();
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            if (_password == value) return;
            _password = value;
            OnPropertyChanged();
        }
    }

    public string TotpSecret
    {
        get => _totpSecret;
        set
        {
            if (_totpSecret == value) return;
            _totpSecret = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TotpIndicator));
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (_isEditing == value) return;
            _isEditing = value;
            if (!value)
            {
                ResetSensitiveVisibility();
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEditable));
            OnPropertyChanged(nameof(ActionButtonText));
        }
    }

    public bool HasCredentials
    {
        get => _hasCredentials;
        set
        {
            if (_hasCredentials == value) return;
            _hasCredentials = value;
            if (!value)
            {
                ResetSensitiveVisibility();
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEditable));
        }
    }

    public bool IsVaultUnlocked
    {
        get => _isVaultUnlocked;
        set
        {
            if (_isVaultUnlocked == value) return;
            _isVaultUnlocked = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEditable));
        }
    }

    public bool IsEditable => IsVaultUnlocked && (!HasCredentials || IsEditing);

    public bool IsPasswordVisible
    {
        get => _isPasswordVisible;
        internal set
        {
            if (_isPasswordVisible == value) return;
            _isPasswordVisible = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PasswordVisibilityButtonText));
            OnPropertyChanged(nameof(PasswordVisibilityToolTip));
        }
    }

    public bool IsTotpSecretVisible
    {
        get => _isTotpSecretVisible;
        internal set
        {
            if (_isTotpSecretVisible == value) return;
            _isTotpSecretVisible = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TotpVisibilityButtonText));
            OnPropertyChanged(nameof(TotpVisibilityToolTip));
        }
    }

    public string PasswordVisibilityButtonText => IsPasswordVisible ? "Hide" : "Show";

    public string PasswordVisibilityToolTip => IsPasswordVisible ? "Hide password" : "Show password";

    public string TotpVisibilityButtonText => IsTotpSecretVisible ? "Hide" : "Show";

    public string TotpVisibilityToolTip => IsTotpSecretVisible ? "Hide TOTP Secret" : "Show TOTP Secret";

    public string ActionButtonText => HasCredentials && !IsEditing ? "Edit" : "Save";

    public string TotpIndicator => !string.IsNullOrEmpty(TotpSecret) ? "✓" : string.Empty;

    public CredentialHealthCheckResult? HealthStatus
    {
        get => _healthStatus;
        private set
        {
            if (_healthStatus == value) return;
            _healthStatus = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HealthStatusDisplay));
            OnPropertyChanged(nameof(HealthStatusEmoji));
        }
    }

    public string HealthStatusDisplay => HealthStatus?.Status.ToDisplayText() ?? string.Empty;

    public string HealthStatusEmoji => HealthStatus?.Status.ToEmoji() ?? string.Empty;

    public void UpdateHealthStatus(CredentialHealthCheckResult result)
    {
        HealthStatus = result;
    }

    public void TogglePasswordVisibility()
    {
        if (IsEditable)
        {
            IsPasswordVisible = !IsPasswordVisible;
        }
    }

    public void ToggleTotpSecretVisibility()
    {
        if (IsEditable)
        {
            IsTotpSecretVisible = !IsTotpSecretVisible;
        }
    }

    public void ResetSensitiveVisibility()
    {
        IsPasswordVisible = false;
        IsTotpSecretVisible = false;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Row for provider connection (per profile).
/// Used by Kiro/GitHub/OpenRouter tabs with inline editing support.
/// </summary>
public sealed class ProviderConnectionRowViewModel : INotifyPropertyChanged
{
    private string _profileId = string.Empty;
    private string _profileName = string.Empty;
    private AuthMethod _authMethod = AuthMethod.GoogleOAuth;
    private string _linkedGoogleAccount = string.Empty;
    private string _email = string.Empty;
    private string _password = string.Empty;
    private string _totpSecret = string.Empty;
    private bool _isSelected;
    private bool _isEditing;
    private bool _hasCredentials;
    private bool _isPasswordVisible;
    private bool _isTotpSecretVisible;
    private CredentialHealthCheckResult? _healthStatus;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string ProfileId
    {
        get => _profileId;
        set
        {
            if (_profileId == value) return;
            _profileId = value;
            OnPropertyChanged();
        }
    }

    public string ProfileName
    {
        get => _profileName;
        set
        {
            if (_profileName == value) return;
            _profileName = value;
            OnPropertyChanged();
        }
    }

    public AuthMethod AuthMethod
    {
        get => _authMethod;
        set
        {
            if (_authMethod == value) return;
            _authMethod = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsGoogleOAuth));
            OnPropertyChanged(nameof(IsDirect));
            OnPropertyChanged(nameof(PreferredMethodText));
        }
    }

    public string LinkedGoogleAccount
    {
        get => _linkedGoogleAccount;
        set
        {
            if (_linkedGoogleAccount == value) return;
            _linkedGoogleAccount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasGoogleOAuth));
        }
    }

    public string Email
    {
        get => _email;
        set
        {
            if (_email == value) return;
            _email = value;
            OnPropertyChanged();
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            if (_password == value) return;
            _password = value;
            OnPropertyChanged();
        }
    }

    public string TotpSecret
    {
        get => _totpSecret;
        set
        {
            if (_totpSecret == value) return;
            _totpSecret = value;
            OnPropertyChanged();
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (_isEditing == value) return;
            _isEditing = value;
            if (!value)
            {
                ResetSensitiveVisibility();
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActionButtonText));
            OnPropertyChanged(nameof(IsEditable));
        }
    }

    public bool HasCredentials
    {
        get => _hasCredentials;
        set
        {
            if (_hasCredentials == value) return;
            _hasCredentials = value;
            OnPropertyChanged();
        }
    }

    public bool IsPasswordVisible
    {
        get => _isPasswordVisible;
        set
        {
            if (_isPasswordVisible == value) return;
            _isPasswordVisible = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PasswordVisibilityButtonText));
            OnPropertyChanged(nameof(PasswordVisibilityToolTip));
        }
    }

    public bool IsTotpSecretVisible
    {
        get => _isTotpSecretVisible;
        set
        {
            if (_isTotpSecretVisible == value) return;
            _isTotpSecretVisible = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TotpVisibilityButtonText));
            OnPropertyChanged(nameof(TotpVisibilityToolTip));
        }
    }

    public bool IsGoogleOAuth => AuthMethod == AuthMethod.GoogleOAuth;
    public bool IsDirect => AuthMethod == AuthMethod.Direct;
    public bool IsEditable => !HasCredentials || IsEditing;
    public bool HasGoogleOAuth => !string.IsNullOrEmpty(LinkedGoogleAccount);
    public string ActionButtonText => IsEditing ? "💾 Save" : (HasCredentials ? "✏ Edit" : "💾 Save");
    public string PasswordVisibilityButtonText => IsPasswordVisible ? "👁" : "👁";
    public string PasswordVisibilityToolTip => IsPasswordVisible ? "Hide password" : "Show password";
    public string TotpVisibilityButtonText => IsTotpSecretVisible ? "👁" : "👁";
    public string TotpVisibilityToolTip => IsTotpSecretVisible ? "Hide TOTP" : "Show TOTP";

    public CredentialHealthCheckResult? HealthStatus
    {
        get => _healthStatus;
        private set
        {
            if (_healthStatus == value) return;
            _healthStatus = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HealthStatusDisplay));
            OnPropertyChanged(nameof(HealthStatusEmoji));
        }
    }

    public string HealthStatusDisplay => HealthStatus?.Status.ToDisplayText() ?? string.Empty;

    public string HealthStatusEmoji => HealthStatus?.Status.ToEmoji() ?? string.Empty;

    public void UpdateHealthStatus(CredentialHealthCheckResult result)
    {
        HealthStatus = result;
    }

    public string AuthMethodDisplay => AuthMethod switch
    {
        AuthMethod.GoogleOAuth => "Google",
        AuthMethod.Direct => "Direct",
        _ => "Unknown"
    };

    public string PreferredMethodText => AuthMethod switch
    {
        AuthMethod.GoogleOAuth => "Google OAuth",
        AuthMethod.Direct => "Direct Login",
        _ => "Unknown"
    };

    private void ResetSensitiveVisibility()
    {
        IsPasswordVisible = false;
        IsTotpSecretVisible = false;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Row ViewModel for Codex connection with inline editing support.
/// Similar structure to GoogleAccountRowViewModel but for Codex provider.
/// Supports both Google OAuth and Direct Login methods.
/// </summary>
public sealed class CodexConnectionRowViewModel : INotifyPropertyChanged
{
    private string _profileId = string.Empty;
    private string _profileName = string.Empty;
    private AuthMethod _authMethod = AuthMethod.GoogleOAuth;
    private string _linkedGoogleAccount = string.Empty;
    private string _email = string.Empty;
    private string _password = string.Empty;
    private string _totpSecret = string.Empty;
    private bool _isSelected;
    private bool _isEditing;
    private bool _hasCredentials;
    private bool _isPasswordVisible;
    private bool _isTotpSecretVisible;
    private IEnumerable<GoogleAccountRowViewModel>? _configuredGoogleAccounts;
    private CredentialHealthCheckResult? _healthStatus;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Smart-suggested Google accounts for this profile.
    /// Shows matching account first if profile name is an exact email match.
    /// </summary>
    public IEnumerable<GoogleAccountItem> SuggestedGoogleAccounts
    {
        get
        {
            if (_configuredGoogleAccounts == null)
                return Enumerable.Empty<GoogleAccountItem>();

            return SmartGoogleAccountSuggestion.GetSuggestedAccounts(
                ProfileName,
                _configuredGoogleAccounts);
        }
    }

    /// <summary>
    /// Sets the configured Google accounts for smart suggestion.
    /// Should be called by parent ViewModel when creating rows.
    /// </summary>
    public void SetConfiguredGoogleAccounts(IEnumerable<GoogleAccountRowViewModel> accounts)
    {
        _configuredGoogleAccounts = accounts;
        OnPropertyChanged(nameof(SuggestedGoogleAccounts));
    }

    public string ProfileId
    {
        get => _profileId;
        set
        {
            if (_profileId == value) return;
            _profileId = value;
            OnPropertyChanged();
        }
    }

    public string ProfileName
    {
        get => _profileName;
        set
        {
            if (_profileName == value) return;
            _profileName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SuggestedGoogleAccounts));
        }
    }

    public AuthMethod AuthMethod
    {
        get => _authMethod;
        set
        {
            if (_authMethod == value) return;
            _authMethod = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsGoogleOAuth));
            OnPropertyChanged(nameof(IsDirect));
        }
    }

    public string LinkedGoogleAccount
    {
        get => _linkedGoogleAccount;
        set
        {
            if (_linkedGoogleAccount == value) return;
            _linkedGoogleAccount = value;
            OnPropertyChanged();
        }
    }

    public string Email
    {
        get => _email;
        set
        {
            if (_email == value) return;
            _email = value;
            OnPropertyChanged();
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            if (_password == value) return;
            _password = value;
            OnPropertyChanged();
        }
    }

    public string TotpSecret
    {
        get => _totpSecret;
        set
        {
            if (_totpSecret == value) return;
            _totpSecret = value;
            OnPropertyChanged();
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public bool IsEditing
    {
        get => _isEditing;
        set
        {
            if (_isEditing == value) return;
            _isEditing = value;
            if (!value)
            {
                ResetSensitiveVisibility();
            }
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActionButtonText));
            OnPropertyChanged(nameof(IsEditable));
        }
    }

    public bool HasCredentials
    {
        get => _hasCredentials;
        set
        {
            if (_hasCredentials == value) return;
            _hasCredentials = value;
            OnPropertyChanged();
        }
    }

    public bool IsPasswordVisible
    {
        get => _isPasswordVisible;
        set
        {
            if (_isPasswordVisible == value) return;
            _isPasswordVisible = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PasswordVisibilityButtonText));
            OnPropertyChanged(nameof(PasswordVisibilityToolTip));
        }
    }

    public bool IsTotpSecretVisible
    {
        get => _isTotpSecretVisible;
        set
        {
            if (_isTotpSecretVisible == value) return;
            _isTotpSecretVisible = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TotpVisibilityButtonText));
            OnPropertyChanged(nameof(TotpVisibilityToolTip));
        }
    }

    public bool IsGoogleOAuth => AuthMethod == AuthMethod.GoogleOAuth;
    public bool IsDirect => AuthMethod == AuthMethod.Direct;
    public bool IsEditable => !HasCredentials || IsEditing;
    public string ActionButtonText => IsEditing ? "💾 Save" : (HasCredentials ? "✏ Edit" : "💾 Save");
    public string PasswordVisibilityButtonText => IsPasswordVisible ? "👁" : "👁";
    public string PasswordVisibilityToolTip => IsPasswordVisible ? "Hide password" : "Show password";
    public string TotpVisibilityButtonText => IsTotpSecretVisible ? "👁" : "👁";
    public string TotpVisibilityToolTip => IsTotpSecretVisible ? "Hide TOTP" : "Show TOTP";

    public CredentialHealthCheckResult? HealthStatus
    {
        get => _healthStatus;
        private set
        {
            if (_healthStatus == value) return;
            _healthStatus = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HealthStatusDisplay));
            OnPropertyChanged(nameof(HealthStatusEmoji));
        }
    }

    public string HealthStatusDisplay => HealthStatus?.Status.ToDisplayText() ?? string.Empty;

    public string HealthStatusEmoji => HealthStatus?.Status.ToEmoji() ?? string.Empty;

    public void UpdateHealthStatus(CredentialHealthCheckResult result)
    {
        HealthStatus = result;
    }

    public string AuthMethodDisplay => AuthMethod switch
    {
        AuthMethod.GoogleOAuth => "Google",
        AuthMethod.Direct => "Direct",
        _ => "Unknown"
    };

    private void ResetSensitiveVisibility()
    {
        IsPasswordVisible = false;
        IsTotpSecretVisible = false;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
