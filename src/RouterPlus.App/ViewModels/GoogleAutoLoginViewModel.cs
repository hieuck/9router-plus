using RouterPlus.Core.Chrome;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Security;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.IO;

namespace RouterPlus.App.ViewModels;

/// <summary>
/// View model for the Google auto-login dialog.
/// </summary>
public sealed class GoogleAutoLoginViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly ChromeProfile _profile;
    private readonly IGoogleLoginVaultStore _vaultStore;
    private readonly Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<GoogleLoginResult>> _runAutomation;

    private GoogleLoginVaultSession? _session;
    private string _email = string.Empty;
    private string _password = string.Empty;
    private string _totpSecret = string.Empty;
    private string _statusText = string.Empty;
    private bool _isVaultUnlocked;
    private bool _isBusy;
    private bool _rememberOnDevice;

    public GoogleAutoLoginViewModel(
        ChromeProfile profile,
        IGoogleLoginVaultStore vaultStore,
        Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<GoogleLoginResult>> runAutomation)
    {
        _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        _vaultStore = vaultStore ?? throw new ArgumentNullException(nameof(vaultStore));
        _runAutomation = runAutomation ?? throw new ArgumentNullException(nameof(runAutomation));

        _email = string.Empty; // Don't default to profile name

        // Try to open remembered vault automatically
        _ = TryAutoUnlockAsync();
    }

    public string ProfileName => _profile.DirectoryName;

    public string Email
    {
        get => _email;
        set
        {
            if (_email != value)
            {
                _email = value;
                OnPropertyChanged();
            }
        }
    }

    public string Password
    {
        get => _password;
        set
        {
            if (_password != value)
            {
                _password = value;
                OnPropertyChanged();
            }
        }
    }

    public string TotpSecret
    {
        get => _totpSecret;
        set
        {
            if (_totpSecret != value)
            {
                _totpSecret = value;
                OnPropertyChanged();
            }
        }
    }

    public string VaultPasswordStatus => IsVaultUnlocked ? "Unlocked" : "Locked";

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText != value)
            {
                _statusText = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsVaultUnlocked
    {
        get => _isVaultUnlocked;
        private set
        {
            if (_isVaultUnlocked != value)
            {
                _isVaultUnlocked = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(VaultPasswordStatus));
                OnPropertyChanged(nameof(CanAutoLogin));
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy != value)
            {
                _isBusy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanAutoLogin));
            }
        }
    }

    public bool RememberOnDevice
    {
        get => _rememberOnDevice;
        set
        {
            if (_rememberOnDevice != value)
            {
                _rememberOnDevice = value;
                OnPropertyChanged();
            }
        }
    }

    public bool CanAutoLogin => IsVaultUnlocked && !IsBusy;

    public async Task UnlockVaultAsync(string vaultPassword, bool remember, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultPassword);

        IsBusy = true;
        try
        {
            var vaultPaths = new GoogleLoginVaultPaths();
            var vaultPath = vaultPaths.VaultPath;

            // Try to open existing vault or create new one
            if (File.Exists(vaultPath))
            {
                _session = await _vaultStore.OpenAsync(vaultPath, vaultPassword, cancellationToken);
            }
            else
            {
                _session = await _vaultStore.CreateAsync(vaultPath, vaultPassword, cancellationToken);
            }

            var existingCredential = _session.Vault.Find(_profile.Id);
            if (existingCredential != null)
            {
                Email = existingCredential.Email;
                Password = existingCredential.Password;
                TotpSecret = existingCredential.TotpSecret;
            }

            IsVaultUnlocked = true;
            StatusText = "Vault unlocked successfully";

            if (remember)
            {
                await _session.RememberAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to unlock vault: {GetSafeErrorMessage(ex)}";
            IsVaultUnlocked = false;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SaveInformationAsync(string email, string password, string totpSecret, CancellationToken cancellationToken)
    {
        if (_session == null)
            throw new InvalidOperationException("Vault is not unlocked");

        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        // TOTP is optional for save - user might not have 2FA enabled yet
        if (string.IsNullOrWhiteSpace(totpSecret))
        {
            totpSecret = "AAAAAAAAAAAAAAAAAAAAAAAA"; // Valid Base32 placeholder
        }

        IsBusy = true;
        try
        {
            var credential = new GoogleLoginCredential(_profile.Id, email, password, totpSecret);
            var updatedVault = _session.Vault.Upsert(credential);
            _session.Replace(updatedVault);

            await _vaultStore.SaveAsync(_session, cancellationToken);

            Email = email;
            Password = password;
            TotpSecret = totpSecret;
            StatusText = "Information saved successfully";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to save: {GetSafeErrorMessage(ex)}";
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<GoogleLoginResult> AutoLoginAsync(string email, string password, string totpSecret, CancellationToken cancellationToken)
    {
        if (_session == null)
            throw new InvalidOperationException("Vault is not unlocked");

        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        ArgumentException.ThrowIfNullOrWhiteSpace(totpSecret);

        IsBusy = true;
        try
        {
            // Create credential for this login attempt
            var credential = new GoogleLoginCredential(_profile.Id, email, password, totpSecret);

            // If email changed AND there's an existing record, persist the email change only
            var existingCredential = _session.Vault.Find(_profile.Id);
            if (existingCredential != null && existingCredential.Email != email)
            {
                // Persist email change only - keep existing password/TOTP
                var updatedCredential = new GoogleLoginCredential(
                    _profile.Id,
                    email,
                    existingCredential.Password,
                    existingCredential.TotpSecret);

                var updatedVault = _session.Vault.Upsert(updatedCredential);
                _session.Replace(updatedVault);
                await _vaultStore.SaveAsync(_session, cancellationToken);

                Email = email;
            }
            // For new profiles: do NOT persist password/TOTP, only update display email
            else if (existingCredential == null)
            {
                Email = email;
            }

            StatusText = "Starting auto-login...";

            // Run automation with current fields
            var result = await _runAutomation(_profile, credential, cancellationToken);

            StatusText = MapResultToStatus(result);

            return result;
        }
        catch (Exception ex)
        {
            var errorMessage = GetSafeErrorMessage(ex);
            StatusText = $"Auto-login failed: {errorMessage}";
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ImportAsync(string sourcePath, string sourcePassword, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePassword);

        IsBusy = true;
        try
        {
            var vaultPaths = new GoogleLoginVaultPaths();
            var vaultPath = vaultPaths.VaultPath;
            await _vaultStore.ImportAsync(vaultPath, sourcePath, sourcePassword, cancellationToken);

            // Reload the session after import
            if (_session != null)
            {
                await _session.DisposeAsync();
                _session = null;
            }

            IsVaultUnlocked = false;
            StatusText = "Vault imported successfully. Please unlock the new vault.";
        }
        catch (Exception ex)
        {
            StatusText = $"Import failed: {GetSafeErrorMessage(ex)}";
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ExportAsync(string destinationPath, string exportPassword, CancellationToken cancellationToken)
    {
        if (_session == null)
            throw new InvalidOperationException("Vault is not unlocked");

        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(exportPassword);

        IsBusy = true;
        try
        {
            await _vaultStore.ExportAsync(_session, destinationPath, exportPassword, cancellationToken);
            StatusText = "Vault exported successfully";
        }
        catch (Exception ex)
        {
            StatusText = $"Export failed: {GetSafeErrorMessage(ex)}";
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task LockVaultAsync(CancellationToken cancellationToken)
    {
        if (_session != null)
        {
            await _session.DisposeAsync();
            _session = null;
        }

        IsVaultUnlocked = false;
        Email = _profile.Name;
        StatusText = "Vault locked";
    }

    public async Task RemoveRememberedUnlockAsync(CancellationToken cancellationToken)
    {
        if (_session != null)
        {
            await _session.RemoveRememberedAsync(cancellationToken);
            StatusText = "Remembered unlock removed";
        }
    }

    private static string MapResultToStatus(GoogleLoginResult result)
    {
        return result.Category switch
        {
            GoogleLoginResultCategory.Success => "Login completed successfully",
            GoogleLoginResultCategory.ManualInterventionRequired => "Manual intervention required. Chrome is open for you to continue.",
            GoogleLoginResultCategory.InvalidCredentials => "Invalid credentials",
            GoogleLoginResultCategory.Timeout => "Login timed out",
            GoogleLoginResultCategory.Cancelled => "Login cancelled",
            GoogleLoginResultCategory.BrowserDisconnected => "Browser disconnected",
            GoogleLoginResultCategory.UnsupportedPage => "Unsupported page or navigation blocked",
            _ => "Unknown result"
        };
    }

    private async Task TryAutoUnlockAsync()
    {
        try
        {
            var vaultPaths = new GoogleLoginVaultPaths();
            _session = await _vaultStore.TryOpenRememberedAsync(vaultPaths.VaultPath, CancellationToken.None);

            if (_session != null)
            {
                var existingCredential = _session.Vault.Find(_profile.Id);
                if (existingCredential != null)
                {
                    Email = existingCredential.Email;
                    Password = existingCredential.Password;
                    TotpSecret = existingCredential.TotpSecret;
                }

                IsVaultUnlocked = true;
                StatusText = "Vault unlocked from remembered device";
            }
        }
        catch
        {
            // Silently ignore remembered unlock failures
            _session = null;
            IsVaultUnlocked = false;
        }
    }

    private static string GetSafeErrorMessage(Exception ex)
    {
        // Return only safe error categories, never expose secrets
        return ex switch
        {
            ArgumentException => "Invalid input",
            FormatException => "Invalid format",
            System.Security.Cryptography.CryptographicException => "Cryptographic operation failed",
            InvalidOperationException => ex.Message,
            _ => "An error occurred"
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public async ValueTask DisposeAsync()
    {
        if (_session != null)
        {
            await _session.DisposeAsync();
            _session = null;
        }

        // Clear sensitive state
        _email = string.Empty;
        _password = string.Empty;
        _totpSecret = string.Empty;
        _statusText = string.Empty;
    }
}
