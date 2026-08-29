using System.Collections.ObjectModel;
using System.ComponentModel;
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
public sealed class CredentialsManagerViewModel : INotifyPropertyChanged
{
    private readonly MainViewModel _mainViewModel;
    private readonly IGoogleAccountVaultStore _googleAccountVaultStore;
    private readonly ProviderConnectionVaultStore _providerConnectionVaultStore;
    private readonly GoogleAccountVaultPaths _vaultPaths;

    private int _selectedTabIndex;
    private string _statusMessage = string.Empty;
    private GoogleAccountRowViewModel? _selectedGoogleAccount;
    private ProviderConnectionRowViewModel? _selectedCodexConnection;
    private ProviderConnectionRowViewModel? _selectedKiroConnection;
    private ProviderConnectionRowViewModel? _selectedGitHubConnection;
    private ProviderConnectionRowViewModel? _selectedOpenRouterConnection;
    private GoogleAccountVaultSession? _vaultSession;

    public event PropertyChangedEventHandler? PropertyChanged;

    public CredentialsManagerViewModel(
        MainViewModel mainViewModel,
        IGoogleAccountVaultStore googleAccountVaultStore,
        ProviderConnectionVaultStore providerConnectionVaultStore,
        GoogleAccountVaultPaths vaultPaths)
    {
        _mainViewModel = mainViewModel ?? throw new ArgumentNullException(nameof(mainViewModel));
        _googleAccountVaultStore = googleAccountVaultStore ?? throw new ArgumentNullException(nameof(googleAccountVaultStore));
        _providerConnectionVaultStore = providerConnectionVaultStore ?? throw new ArgumentNullException(nameof(providerConnectionVaultStore));
        _vaultPaths = vaultPaths ?? throw new ArgumentNullException(nameof(vaultPaths));

        // Initialize commands
        AddGoogleAccountCommand = new AsyncRelayCommand(AddGoogleAccountAsync);
        EditGoogleAccountCommand = new AsyncRelayCommand(EditGoogleAccountAsync, () => SelectedGoogleAccount != null);
        RemoveGoogleAccountCommand = new AsyncRelayCommand(RemoveGoogleAccountAsync, () => SelectedGoogleAccount != null);
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

    public GoogleAccountRowViewModel? SelectedGoogleAccount
    {
        get => _selectedGoogleAccount;
        set
        {
            if (_selectedGoogleAccount == value) return;
            _selectedGoogleAccount = value;
            OnPropertyChanged();
            EditGoogleAccountCommand.RaiseCanExecuteChanged();
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
    public AsyncRelayCommand AddGoogleAccountCommand { get; }
    public AsyncRelayCommand EditGoogleAccountCommand { get; }
    public AsyncRelayCommand RemoveGoogleAccountCommand { get; }
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
                SetStatus("Google vault locked. Use 'Tự động đăng nhập Google' to unlock first.");
                // Load provider connections only
                await LoadProviderConnectionsAsync();
                return;
            }

            // Load Google accounts from vault
            GoogleAccounts.Clear();
            foreach (var credential in _vaultSession.Vault.Records)
            {
                GoogleAccounts.Add(new GoogleAccountRowViewModel
                {
                    Email = credential.Email,
                    HasTotpSecret = !string.IsNullOrEmpty(credential.TotpSecret)
                });
            }

            // Load provider connections
            await LoadProviderConnectionsAsync();

            SetStatus($"Loaded {GoogleAccounts.Count} Google accounts and provider connections.");
        }
        catch (Exception ex)
        {
            SetStatus($"Error loading vault: {ex.Message}");
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

    private async Task AddGoogleAccountAsync()
    {
        SetStatus("Feature coming soon: Add Google account");
        // TODO: Open GoogleAutoLoginDialog in "add new" mode
        await Task.CompletedTask;
    }

    private async Task EditGoogleAccountAsync()
    {
        if (SelectedGoogleAccount == null) return;
        SetStatus($"Feature coming soon: Edit {SelectedGoogleAccount.Email}");
        // TODO: Open GoogleAutoLoginDialog with selected account
        await Task.CompletedTask;
    }

    private async Task RemoveGoogleAccountAsync()
    {
        if (SelectedGoogleAccount == null) return;
        if (_vaultSession == null)
        {
            SetStatus("Vault not unlocked");
            return;
        }

        var email = SelectedGoogleAccount.Email;

        try
        {
            // Find credential in vault
            var credential = _vaultSession.Vault.Records
                .FirstOrDefault(c => string.Equals(c.Email, email, StringComparison.OrdinalIgnoreCase));

            if (credential == null)
            {
                SetStatus($"Account {email} not found in vault");
                return;
            }

            // Remove from vault (immutable - filter and replace)
            var currentVault = _vaultSession.Vault;
            var filteredRecords = currentVault.Records.Where(r => r.Email != email);
            var newVault = new GoogleAccountVault(filteredRecords);
            _vaultSession.Replace(newVault);
            await _googleAccountVaultStore.SaveAsync(_vaultSession, CancellationToken.None);

            // Remove from UI
            GoogleAccounts.Remove(SelectedGoogleAccount);

            SetStatus($"Removed {email} from vault");
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
    private string _email = string.Empty;
    private bool _hasTotpSecret;

    public event PropertyChangedEventHandler? PropertyChanged;

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

    public bool HasTotpSecret
    {
        get => _hasTotpSecret;
        set
        {
            if (_hasTotpSecret == value) return;
            _hasTotpSecret = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TotpIndicator));
        }
    }

    public string TotpIndicator => HasTotpSecret ? "🔐 2FA" : "";

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
