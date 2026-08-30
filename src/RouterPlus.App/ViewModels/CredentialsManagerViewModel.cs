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
    private readonly Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<GoogleLoginResult>> _googleLoginAutomation;

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

    public event PropertyChangedEventHandler? PropertyChanged;

    public CredentialsManagerViewModel(
        MainViewModel mainViewModel,
        IGoogleAccountVaultStore googleAccountVaultStore,
        ProviderConnectionVaultStore providerConnectionVaultStore,
        GoogleAccountVaultPaths vaultPaths,
        Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<GoogleLoginResult>> googleLoginAutomation)
    {
        _mainViewModel = mainViewModel ?? throw new ArgumentNullException(nameof(mainViewModel));
        _googleAccountVaultStore = googleAccountVaultStore ?? throw new ArgumentNullException(nameof(googleAccountVaultStore));
        _providerConnectionVaultStore = providerConnectionVaultStore ?? throw new ArgumentNullException(nameof(providerConnectionVaultStore));
        _vaultPaths = vaultPaths ?? throw new ArgumentNullException(nameof(vaultPaths));
        _googleLoginAutomation = googleLoginAutomation ?? throw new ArgumentNullException(nameof(googleLoginAutomation));

        // Initialize commands
        SaveRowCommand = new AsyncRelayCommand<GoogleAccountRowViewModel>(SaveRowAsync);
        LoginRowCommand = new AsyncRelayCommand<GoogleAccountRowViewModel>(LoginRowAsync);
        RemoveGoogleAccountCommand = new AsyncRelayCommand(RemoveGoogleAccountAsync, () => SelectedGoogleAccount != null);
        BatchLoginCommand = new AsyncRelayCommand(BatchLoginAsync, () => GoogleAccounts.Any(a => a.IsSelected && a.HasCredentials) && !IsBatchLoginRunning);
        RefreshCommand = new AsyncRelayCommand(RefreshDataAsync);

        _ = LoadDataAsync();
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
            BatchLoginCommand.RaiseCanExecuteChanged();
        }
    }

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
            RemoveGoogleAccountCommand.RaiseCanExecuteChanged();
        }
    }

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
    public AsyncRelayCommand RefreshCommand { get; }

    private async Task LoadDataAsync()
    {
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
            var credential = session?.Vault.Records.FirstOrDefault(r => r.ProfileId == profile.Name);

            var row = new GoogleAccountRowViewModel
            {
                ProfileName = profile.Name,
                Email = credential?.Email ?? string.Empty,
                Password = credential?.Password ?? string.Empty,
                TotpSecret = credential?.TotpSecret ?? string.Empty,
                HasCredentials = credential != null,
                IsEditing = false,
                IsSelected = false
            };

            // Subscribe to property changes to update SelectedCount
            row.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(GoogleAccountRowViewModel.IsSelected))
                {
                    OnPropertyChanged(nameof(SelectedCount));
                    BatchLoginCommand.RaiseCanExecuteChanged();
                }
            };

            GoogleAccounts.Add(row);
        }
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
            // Create credential (TOTP required - use placeholder if empty)
            var credential = new GoogleLoginCredential(
                row.ProfileName,
                row.Email.Trim(),
                row.Password,
                string.IsNullOrWhiteSpace(row.TotpSecret) ? "NONE" : row.TotpSecret.Trim());

            // Upsert into vault (immutable pattern)
            var currentVault = _vaultSession.Vault;
            var filtered = currentVault.Records.Where(r => r.ProfileId != row.ProfileName);
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

        // Find matching profile
        var profile = _mainViewModel.FilteredProfiles.FirstOrDefault(p => p.Name == row.ProfileName);
        if (profile == null)
        {
            SetStatus($"❌ {row.ProfileName}: Profile not found");
            return;
        }

        // Create credential from row
        var credential = new GoogleLoginCredential(
            row.ProfileName,
            row.Email,
            row.Password,
            string.IsNullOrWhiteSpace(row.TotpSecret) ? "NONE" : row.TotpSecret.Trim());

        SetStatus($"🚀 Logging in {row.ProfileName}...");

        try
        {
            var result = await _googleLoginAutomation(profile, credential, CancellationToken.None);

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

    private async Task BatchLoginAsync()
    {
        var selectedRows = GoogleAccounts.Where(a => a.IsSelected && a.HasCredentials).ToList();
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

        IsBatchLoginRunning = true;
        var successCount = 0;
        var failCount = 0;

        try
        {
            SetStatus($"Starting batch login for {selectedRows.Count} profile(s)...");

            foreach (var row in selectedRows)
            {
                // Find matching profile
                var profile = _mainViewModel.FilteredProfiles.FirstOrDefault(p => p.Name == row.ProfileName);
                if (profile == null)
                {
                    SetStatus($"❌ {row.ProfileName}: Profile not found");
                    failCount++;
                    await Task.Delay(1000);
                    continue;
                }

                // Create credential from row
                var credential = new GoogleLoginCredential(
                    row.ProfileName,
                    row.Email,
                    row.Password,
                    string.IsNullOrWhiteSpace(row.TotpSecret) ? "NONE" : row.TotpSecret.Trim());

                SetStatus($"🚀 Logging in {row.ProfileName}...");

                try
                {
                    var result = await _googleLoginAutomation(profile, credential, CancellationToken.None);

                    if (result.Category == GoogleLoginResultCategory.Success)
                    {
                        SetStatus($"✓ {row.ProfileName}: Login successful");
                        successCount++;
                    }
                    else if (result.Category == GoogleLoginResultCategory.ManualInterventionRequired)
                    {
                        SetStatus($"⚠ {row.ProfileName}: Manual intervention required");
                        failCount++;
                    }
                    else
                    {
                        SetStatus($"❌ {row.ProfileName}: {result.Message ?? result.Category.ToString()}");
                        failCount++;
                    }
                }
                catch (Exception ex)
                {
                    SetStatus($"❌ {row.ProfileName}: {ex.Message}");
                    failCount++;
                }

                await Task.Delay(1500);
            }

            SetStatus($"Batch login completed: {successCount} succeeded, {failCount} failed");
        }
        finally
        {
            IsBatchLoginRunning = false;
        }
    }

    private async Task RemoveGoogleAccountAsync()
    {
        if (SelectedGoogleAccount == null) return;
        if (_vaultSession == null)
        {
            SetStatus("Vault not unlocked");
            return;
        }

        var profileName = SelectedGoogleAccount.ProfileName;

        try
        {
            // Remove from vault (immutable - filter and replace)
            var currentVault = _vaultSession.Vault;
            var filteredRecords = currentVault.Records.Where(r => r.ProfileId != profileName);
            var newVault = new GoogleAccountVault(filteredRecords);
            _vaultSession.Replace(newVault);
            await _googleAccountVaultStore.SaveAsync(_vaultSession, CancellationToken.None);

            // Update UI - clear credentials but keep row
            SelectedGoogleAccount.Email = string.Empty;
            SelectedGoogleAccount.Password = string.Empty;
            SelectedGoogleAccount.TotpSecret = string.Empty;
            SelectedGoogleAccount.HasCredentials = false;
            SelectedGoogleAccount.IsEditing = false;

            SetStatus($"Removed credentials for {profileName}");
        }
        catch (Exception ex)
        {
            SetStatus($"Error removing account: {ex.Message}");
        }
    }

    public async Task RemoveGoogleAccountAsync(string profileName)
    {
        if (_vaultSession == null)
        {
            SetStatus("Vault not unlocked");
            return;
        }

        if (string.IsNullOrWhiteSpace(profileName))
        {
            SetStatus("Profile is required");
            return;
        }

        try
        {
            // Remove from vault by profile ID, which is the stable credential key.
            var currentVault = _vaultSession.Vault;
            var filteredRecords = currentVault.Records.Where(r => r.ProfileId != profileName);
            var newVault = new GoogleAccountVault(filteredRecords);
            _vaultSession.Replace(newVault);
            await _googleAccountVaultStore.SaveAsync(_vaultSession, CancellationToken.None);

            // Update the existing row without reloading from the remembered-key path.
            var row = GoogleAccounts.FirstOrDefault(account => account.ProfileName == profileName);
            if (row is not null)
            {
                row.Email = string.Empty;
                row.Password = string.Empty;
                row.TotpSecret = string.Empty;
                row.HasCredentials = false;
                row.IsEditing = false;
            }

            SetStatus($"Removed Google account for {profileName}");
        }
        catch (Exception ex)
        {
            SetStatus($"Error removing account: {ex.Message}");
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

    public async ValueTask DisposeAsync()
    {
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
    private string _profileName = string.Empty;
    private string _email = string.Empty;
    private string _password = string.Empty;
    private string _totpSecret = string.Empty;
    private bool _isSelected;
    private bool _isEditing;
    private bool _hasCredentials;

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
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEditable));
        }
    }

    public bool IsEditable => !HasCredentials || IsEditing;

    public string ActionButtonText => HasCredentials && !IsEditing ? "Edit" : "Save";

    public string TotpIndicator => !string.IsNullOrEmpty(TotpSecret) ? "✓" : string.Empty;

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
