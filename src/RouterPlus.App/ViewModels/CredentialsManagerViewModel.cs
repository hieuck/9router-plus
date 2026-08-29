using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using RouterPlus.Core.Models;
using RouterPlus.Core.Providers;
using RouterPlus.Core.Chrome;
using RouterPlus.Infrastructure.Storage;

namespace RouterPlus.App.ViewModels;

/// <summary>
/// ViewModel for unified credentials manager dialog.
/// Manages Google accounts and provider connections.
/// </summary>
public sealed class CredentialsManagerViewModel : INotifyPropertyChanged
{
    private int _selectedTabIndex;
    private string _statusMessage = string.Empty;
    private GoogleAccountRowViewModel? _selectedGoogleAccount;
    private ProviderConnectionRowViewModel? _selectedCodexConnection;
    private ProviderConnectionRowViewModel? _selectedKiroConnection;
    private ProviderConnectionRowViewModel? _selectedGitHubConnection;
    private ProviderConnectionRowViewModel? _selectedOpenRouterConnection;

    public event PropertyChangedEventHandler? PropertyChanged;

    public CredentialsManagerViewModel(MainViewModel mainViewModel)
    {
        ArgumentNullException.ThrowIfNull(mainViewModel);
        LoadData(mainViewModel);
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

    private void LoadData(MainViewModel mainViewModel)
    {
        // TODO: Load Google accounts from GoogleAccountVaultStore
        // For now, show placeholder
        GoogleAccounts.Add(new GoogleAccountRowViewModel
        {
            Email = "example@gmail.com (placeholder)",
            HasTotpSecret = true
        });

        SetStatus("Credentials Manager: UI skeleton ready. Vault integration pending.");

        // TODO: Load provider connections from vault stores
        // For now, create placeholders for each profile
        foreach (var profile in mainViewModel.FilteredProfiles)
        {
            // Add placeholder for each provider
            var providers = new[]
            {
                (ProviderKind.Codex, CodexConnections),
                (ProviderKind.Kiro, KiroConnections),
                (ProviderKind.GitHub, GitHubConnections),
                (ProviderKind.OpenRouter, OpenRouterConnections)
            };

            foreach (var (kind, collection) in providers)
            {
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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public void SetStatus(string message)
    {
        StatusMessage = $"[{DateTime.Now:HH:mm:ss}] {message}";
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
