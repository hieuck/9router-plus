using RouterPlus.Core.Chrome;
using RouterPlus.Core.Security;
using RouterPlus.Core.Observability;
using RouterPlus.App.Diagnostics;
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
    private readonly IGoogleAccountVaultStore _vaultStore;
    private readonly Func<ChromeProfile, GoogleLoginCredential, CancellationToken, Task<GoogleLoginResult>> _runAutomation;

    private GoogleAccountVaultSession? _session;
    private string _email = string.Empty;
    private string _password = string.Empty;
    private string _totpSecret = string.Empty;
    private string _statusText = string.Empty;
    private bool _isVaultUnlocked;
    private bool _isBusy;
    private bool _rememberOnDevice;

    public GoogleAutoLoginViewModel(
        ChromeProfile profile,
        IGoogleAccountVaultStore vaultStore,
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
        using var perf = DebugLogger.MeasurePerformance(DiagnosticCategories.Security, "UnlockVaultAsync");
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultPassword);

        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "AutoLogin",
            "VaultUnlockAttempt",
            "User attempting to unlock vault",
            new { profile_name = _profile.Name, remember_on_device = remember });

        IsBusy = true;
        try
        {
            var vaultPaths = new GoogleAccountVaultPaths();
            var vaultPath = vaultPaths.VaultPath;

            // Try to open existing vault or create new one
            if (File.Exists(vaultPath))
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Debug,
                    "AutoLogin",
                    "VaultOpening",
                    "Opening existing vault",
                    new { vault_path = vaultPath });

                _session = await _vaultStore.OpenAsync(vaultPath, vaultPassword, cancellationToken);
            }
            else
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Info,
                    "AutoLogin",
                    "VaultCreating",
                    "Creating new vault - first time setup",
                    new { vault_path = vaultPath });

                _session = await _vaultStore.CreateAsync(vaultPath, vaultPassword, cancellationToken);
            }

            var existingCredential = _session.Vault.Find(_profile.Id);
            if (existingCredential != null)
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Info,
                    "AutoLogin",
                    "CredentialsLoaded",
                    "Existing credentials loaded from vault",
                    new { profile_id = _profile.Id, email = existingCredential.Email });

                Email = existingCredential.Email;
                Password = existingCredential.Password;
                TotpSecret = existingCredential.TotpSecret;
            }
            else
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Info,
                    "AutoLogin",
                    "NoCredentials",
                    "No existing credentials for this profile",
                    new { profile_id = _profile.Id });
            }

            IsVaultUnlocked = true;
            StatusText = "Vault unlocked successfully";

            if (remember)
            {
                await _session.RememberAsync(cancellationToken);
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Info,
                    "AutoLogin",
                    "VaultRemembered",
                    "Vault password remembered on this device",
                    null);
            }

            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "AutoLogin",
                "VaultUnlockSuccess",
                "Vault unlock completed successfully",
                new { total_credentials = _session.Vault.Records.Count });
        }
        catch (Exception ex)
        {
            ObservabilityHub.Instance.LogError(
                "AutoLogin",
                "VaultUnlockFailed",
                ex,
                new { profile_name = _profile.Name });

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
        using var perf = DebugLogger.MeasurePerformance(DiagnosticCategories.Security, "SaveInformationAsync");
        if (_session == null)
            throw new InvalidOperationException("Vault is not unlocked");

        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        // TOTP is optional for save - user might not have 2FA enabled yet
        if (string.IsNullOrWhiteSpace(totpSecret))
        {
            totpSecret = "AAAAAAAAAAAAAAAAAAAAAAAA"; // Valid Base32 placeholder
        }

        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "AutoLogin",
            "SaveCredentials",
            "Saving credentials to vault",
            new { profile_id = _profile.Id, email, has_totp = totpSecret != "AAAAAAAAAAAAAAAAAAAAAAAA" });

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

            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "AutoLogin",
                "SaveCredentialsSuccess",
                "Credentials saved successfully",
                new { profile_id = _profile.Id, email });
        }
        catch (Exception ex)
        {
            ObservabilityHub.Instance.LogError(
                "AutoLogin",
                "SaveCredentialsFailed",
                ex,
                new { profile_id = _profile.Id, email });

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
        using var perf = DebugLogger.MeasurePerformance(DiagnosticCategories.Security, "AutoLoginAsync");
        if (_session == null)
            throw new InvalidOperationException("Vault is not unlocked");

        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        // TOTP is optional - use placeholder if not provided
        if (string.IsNullOrWhiteSpace(totpSecret))
        {
            totpSecret = "AAAAAAAAAAAAAAAAAAAAAAAA";
        }

        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "AutoLogin",
            "AutoLoginStarted",
            "Starting Google auto-login automation",
            new { profile_name = _profile.Name, email, has_totp = totpSecret != "AAAAAAAAAAAAAAAAAAAAAAAA" });

        IsBusy = true;
        try
        {
            // Create credential for this login attempt
            var credential = new GoogleLoginCredential(_profile.Id, email, password, totpSecret);

            // If email changed AND there's an existing record, persist the email change only
            var existingCredential = _session.Vault.Find(_profile.Id);
            if (existingCredential != null && existingCredential.Email != email)
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Info,
                    "AutoLogin",
                    "EmailChanged",
                    "Email changed - persisting new email",
                    new { old_email = existingCredential.Email, new_email = email });

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

            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "AutoLogin",
                "AutoLoginCompleted",
                "Auto-login automation completed",
                new { profile_name = _profile.Name, result_category = result.Category.ToString(), message = result.Message });

            StatusText = MapResultToStatus(result);

            return result;
        }
        catch (Exception ex)
        {
            ObservabilityHub.Instance.LogError(
                "AutoLogin",
                "AutoLoginFailed",
                ex,
                new { profile_name = _profile.Name, email });

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
        using var perf = DebugLogger.MeasurePerformance(DiagnosticCategories.Security, "ImportVaultAsync");
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePassword);

        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "AutoLogin",
            "VaultImportStarted",
            "Starting vault import",
            new { source_path = sourcePath });

        IsBusy = true;
        try
        {
            var vaultPaths = new GoogleAccountVaultPaths();
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

            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "AutoLogin",
                "VaultImportSuccess",
                "Vault imported successfully",
                new { vault_path = vaultPath });
        }
        catch (Exception ex)
        {
            ObservabilityHub.Instance.LogError(
                "AutoLogin",
                "VaultImportFailed",
                ex,
                new { source_path = sourcePath });

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
        using var perf = DebugLogger.MeasurePerformance(DiagnosticCategories.Security, "ExportVaultAsync");
        if (_session == null)
            throw new InvalidOperationException("Vault is not unlocked");

        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(exportPassword);

        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "AutoLogin",
            "VaultExportStarted",
            "Starting vault export",
            new { destination_path = destinationPath });

        IsBusy = true;
        try
        {
            await _vaultStore.ExportAsync(_session, destinationPath, exportPassword, cancellationToken);
            StatusText = "Vault exported successfully";

            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "AutoLogin",
                "VaultExportSuccess",
                "Vault exported successfully",
                new { destination_path = destinationPath, credential_count = _session.Vault.Records.Count });
        }
        catch (Exception ex)
        {
            ObservabilityHub.Instance.LogError(
                "AutoLogin",
                "VaultExportFailed",
                ex,
                new { destination_path = destinationPath });

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
        using var perf = DebugLogger.MeasurePerformance(DiagnosticCategories.Security, "LockVaultAsync");
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
            GoogleLoginResultCategory.BrowserDisconnected => result.Message,
            GoogleLoginResultCategory.UnsupportedPage => "Unsupported page or navigation blocked",
            _ => "Unknown result"
        };
    }

    private async Task TryAutoUnlockAsync()
    {
        try
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "AutoLogin",
                "CredentialLookupStarted",
                "Starting auto-unlock credential lookup",
                new
                {
                    profile_name = _profile.Name,
                    profile_id = _profile.Id,
                    profile_directory = _profile.DirectoryName,
                    user_data_directory = _profile.UserDataDirectory
                });

            var vaultPaths = new GoogleAccountVaultPaths();
            var vaultPath = vaultPaths.VaultPath;

            ObservabilityHub.Instance.LogEvent(
                LogLevel.Debug,
                "AutoLogin",
                "VaultAccess",
                "Attempting vault access",
                new { vault_path = vaultPath, vault_exists = File.Exists(vaultPath) });

            _session = await _vaultStore.TryOpenRememberedAsync(vaultPath, CancellationToken.None);

            if (_session != null)
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Info,
                    "AutoLogin",
                    "VaultUnlocked",
                    "Vault unlocked successfully",
                    new { total_credentials = _session.Vault.Records.Count });

                if (_session.Vault.Records.Count > 0)
                {
                    var profileIds = _session.Vault.Records.Select(r => new { r.ProfileId, r.Email }).ToList();
                    ObservabilityHub.Instance.LogEvent(
                        LogLevel.Debug,
                        "AutoLogin",
                        "VaultInventory",
                        "Available credentials in vault",
                        new { credentials = profileIds });
                }

                var existingCredential = _session.Vault.Find(_profile.Id);
                if (existingCredential != null)
                {
                    ObservabilityHub.Instance.LogEvent(
                        LogLevel.Info,
                        "AutoLogin",
                        "CredentialsFound",
                        "Credentials found for profile",
                        new
                        {
                            profile_id = _profile.Id,
                            email = existingCredential.Email,
                            has_password = !string.IsNullOrEmpty(existingCredential.Password),
                            has_totp = !string.IsNullOrEmpty(existingCredential.TotpSecret)
                        });

                    Email = existingCredential.Email;
                    Password = existingCredential.Password;
                    TotpSecret = existingCredential.TotpSecret;
                }
                else
                {
                    ObservabilityHub.Instance.LogEvent(
                        LogLevel.Warning,
                        "AutoLogin",
                        "CredentialsNotFound",
                        "Credentials not found for profile - Profile ID mismatch detected",
                        new
                        {
                            lookup_profile_id = _profile.Id,
                            available_profile_ids = _session.Vault.Records.Select(r => r.ProfileId).ToList(),
                            diagnosis = "Credentials may have been saved with different User Data path or Directory Name",
                            solution = "Delete old credential in Credentials Manager and save again"
                        });
                }

                IsVaultUnlocked = true;
                StatusText = "Vault unlocked from remembered device";
            }
            else
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Info,
                    "AutoLogin",
                    "VaultLocked",
                    "Vault unlock failed - vault is locked or doesn't exist",
                    new { vault_path = vaultPath });
            }
        }
        catch (Exception ex)
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Error,
                "AutoLogin",
                "AutoUnlockFailed",
                "Auto-unlock failed with exception",
                new { error = ex.Message, error_type = ex.GetType().Name });

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
