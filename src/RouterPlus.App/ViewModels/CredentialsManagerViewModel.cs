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
