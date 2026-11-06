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
