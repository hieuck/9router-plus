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
