using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using RouterPlus.Core.Models;
using RouterPlus.Core.Providers;

namespace RouterPlus.App.ViewModels;

/// <summary>
/// ViewModel for unified credentials manager dialog.
/// Manages Google accounts and provider connections.
/// </summary>
public sealed class CredentialsManagerViewModel : INotifyPropertyChanged
{
    private string _selectedTabName = "Google";
    private string _statusMessage = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

    // Selected tab
    public string SelectedTabName
    {
        get => _selectedTabName;
        set
        {
            if (_selectedTabName == value) return;
            _selectedTabName = value;
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

    // Provider connections per provider
    public ObservableCollection<ProviderConnectionRowViewModel> CodexConnections { get; } = new();
    public ObservableCollection<ProviderConnectionRowViewModel> KiroConnections { get; } = new();
    public ObservableCollection<ProviderConnectionRowViewModel> GitHubConnections { get; } = new();
    public ObservableCollection<ProviderConnectionRowViewModel> OpenRouterConnections { get; } = new();

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
