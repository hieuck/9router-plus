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

public sealed partial class CredentialsManagerViewModel
{
    private GoogleAccountVaultSession? _vaultSession;
    private bool _isVaultLocked = true;
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

    public async Task UnlockVaultAsync(string vaultPassword, bool remember)
    {
        if (string.IsNullOrWhiteSpace(vaultPassword))
        {
            SetStatus("Vault password is required");
            return;
        }

        using var trace = TraceScope.Begin("CredentialsManager", "UnlockVault", new { remember, vault_exists = File.Exists(_vaultPaths.VaultPath) });

        try
        {
            GoogleAccountVaultSession session;
            if (File.Exists(_vaultPaths.VaultPath))
            {
                trace.LogCheckpoint("OpeningExistingVault");
                session = await _googleAccountVaultStore.OpenAsync(
                    _vaultPaths.VaultPath,
                    vaultPassword,
                    CancellationToken.None);
            }
            else
            {
                trace.LogCheckpoint("CreatingNewVault");
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
                trace.LogCheckpoint("RememberingKey");
                await _vaultSession.RememberAsync(CancellationToken.None);
            }

            IsVaultLocked = false;
            await LoadProfileRowsAsync(_vaultSession);
            await LoadProviderConnectionsAsync();

            var configuredCount = GoogleAccounts.Count(a => a.HasCredentials);
            ObservabilityHub.Instance.RecordGauge("vault.profiles.configured", configuredCount);
            ObservabilityHub.Instance.IncrementCounter("vault.unlock.success");

            SetStatus($"Vault unlocked. Loaded {configuredCount} configured profiles.");
        }
        catch (Exception ex)
        {
            ObservabilityHub.Instance.IncrementCounter("vault.unlock.failed", tags: new Dictionary<string, string> { ["error_type"] = ex.GetType().Name });
            ObservabilityHub.Instance.LogEvent(LogLevel.Error, "CredentialsManager", "VaultUnlockFailed",
                "Failed to unlock vault", new { error = ex.Message, error_type = ex.GetType().Name });
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

}
