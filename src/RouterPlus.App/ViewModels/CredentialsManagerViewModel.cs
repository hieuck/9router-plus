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

    private int _selectedTabIndex;
    private string _statusMessage = string.Empty;
    private GoogleAccountRowViewModel? _selectedGoogleAccount;
    private ProviderConnectionRowViewModel? _selectedCodexConnection;
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
        Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<GoogleLoginResult>> googleAuthenticationRunner)
    {
        _mainViewModel = mainViewModel ?? throw new ArgumentNullException(nameof(mainViewModel));
        _googleAccountVaultStore = googleAccountVaultStore ?? throw new ArgumentNullException(nameof(googleAccountVaultStore));
        _providerConnectionVaultStore = providerConnectionVaultStore ?? throw new ArgumentNullException(nameof(providerConnectionVaultStore));
        _vaultPaths = vaultPaths ?? throw new ArgumentNullException(nameof(vaultPaths));
        _runGoogleAuthentication = googleAuthenticationRunner ?? throw new ArgumentNullException(nameof(googleAuthenticationRunner));

        // Initialize commands
        SaveRowCommand = new AsyncRelayCommand<GoogleAccountRowViewModel>(
            SaveRowAsync,
            _ => !IsBatchLoginRunning);
        LoginRowCommand = new AsyncRelayCommand<GoogleAccountRowViewModel>(
            LoginRowAsync,
            row => !IsBatchLoginRunning && row.HasCredentials);
        RemoveGoogleAccountCommand = new AsyncRelayCommand(
            RemoveGoogleAccountAsync,
            () => CanRemoveGoogleAccount);
        BatchLoginCommand = new AsyncRelayCommand(StartBatchLoginAsync, () => GoogleAccounts.Any(a => a.IsSelected && a.HasCredentials) && !IsBatchLoginRunning);

        StopBatchLoginCommand = new RelayCommand(StopBatchLogin, () => IsBatchLoginRunning);
        RefreshCommand = new AsyncRelayCommand(RefreshDataAsync, () => !IsBatchLoginRunning);

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

    public int SelectedCount => GoogleAccounts.Count(a => a.IsSelected && a.HasCredentials);

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
            BatchLoginCommand.RaiseCanExecuteChanged();
            StopBatchLoginCommand.RaiseCanExecuteChanged();
            RefreshCommand.RaiseCanExecuteChanged();
            SaveRowCommand.RaiseCanExecuteChanged();
            LoginRowCommand.RaiseCanExecuteChanged();
            RemoveGoogleAccountCommand.RaiseCanExecuteChanged();
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
    public ObservableCollection<ProviderConnectionRowViewModel> CodexConnections { get; } = new();
    public ObservableCollection<ProviderConnectionRowViewModel> KiroConnections { get; } = new();
    public ObservableCollection<ProviderConnectionRowViewModel> GitHubConnections { get; } = new();
    public ObservableCollection<ProviderConnectionRowViewModel> OpenRouterConnections { get; } = new();

    public ProviderConnectionRowViewModel? SelectedCodexConnection
    {
        get => _selectedCodexConnection;
        set
        {
            if (_selectedCodexConnection == value) return;
            _selectedCodexConnection = value;
            OnPropertyChanged();
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
        }
    }

    // Commands
    public AsyncRelayCommand<GoogleAccountRowViewModel> SaveRowCommand { get; }
    public AsyncRelayCommand<GoogleAccountRowViewModel> LoginRowCommand { get; }
    public AsyncRelayCommand RemoveGoogleAccountCommand { get; }
    public AsyncRelayCommand BatchLoginCommand { get; }
    public RelayCommand StopBatchLoginCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }

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
                    BatchLoginCommand.RaiseCanExecuteChanged();
                }
            };

            GoogleAccounts.Add(row);
        }
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
            // Load connections for each provider
            var providers = new[]
            {
                (ProviderKind.Codex, CodexConnections),
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

                if (connection != null)
                {
                    collection.Add(new ProviderConnectionRowViewModel
                    {
                        ProfileName = connection.ProfileName,
                        PreferredMethod = connection.PreferredMethod,
                        LinkedGoogleAccount = connection.LinkedGoogleAccount ?? "Not linked",
                        HasDirectCredentials = connection.DirectCredential != null
                    });
                }
                else
                {
                    // Show unconfigured row
                    collection.Add(new ProviderConnectionRowViewModel
                    {
                        ProfileName = profile.Name,
                        PreferredMethod = AuthMethod.GoogleOAuth,
                        LinkedGoogleAccount = "Not configured",
                        HasDirectCredentials = false
                    });
                }
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
/// </summary>
public sealed class ProviderConnectionRowViewModel : INotifyPropertyChanged
{
    private string _profileName = string.Empty;
    private AuthMethod _preferredMethod = AuthMethod.GoogleOAuth;
    private string? _linkedGoogleAccount;
    private bool _hasDirectCredentials;

    public event PropertyChangedEventHandler? PropertyChanged;

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

    public AuthMethod PreferredMethod
    {
        get => _preferredMethod;
        set
        {
            if (_preferredMethod == value) return;
            _preferredMethod = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PreferredMethodText));
        }
    }

    public string? LinkedGoogleAccount
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

    public bool HasDirectCredentials
    {
        get => _hasDirectCredentials;
        set
        {
            if (_hasDirectCredentials == value) return;
            _hasDirectCredentials = value;
            OnPropertyChanged();
        }
    }

    public bool HasGoogleOAuth => !string.IsNullOrEmpty(LinkedGoogleAccount);

    public string PreferredMethodText => PreferredMethod switch
    {
        AuthMethod.GoogleOAuth => "Google OAuth",
        AuthMethod.Direct => "Direct Login",
        _ => "Unknown"
    };

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
