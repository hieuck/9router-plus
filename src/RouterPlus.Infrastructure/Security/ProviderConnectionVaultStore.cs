using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RouterPlus.Core.Models;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Diagnostics;

namespace RouterPlus.Infrastructure.Security;

/// <summary>
/// Encrypted vault store for provider connection configurations.
/// Maps Chrome profile → provider → auth config (Google OAuth or Direct credentials).
///
/// Callers:
/// - AutoLoginOrchestrator: Reads connection to determine auth method
/// - MainViewModel: SaveConnectionAsync when user configures credentials
/// - ProfileRowViewModel: HasCredentialsAsync to show vault indicators
/// - Batch login: HasCredentialsAsync to filter profiles
///
/// Data Schema:
/// Dictionary&lt;profileName, Dictionary&lt;ProviderKind, ProviderConnection&gt;&gt;
///
/// File: provider-connections.vault (DPAPI encrypted)
/// User request: "dùng ProviderConnectionVaultStore" - Phase 1 Step 1.2
/// </summary>
public sealed class ProviderConnectionVaultStore : IDisposable
{
    private static readonly byte[] DpapiEntropy = Encoding.UTF8.GetBytes("9RouterPlus.ProviderConnectionVault.v1");

    private readonly string _vaultPath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _disposalLock = new();
    private int _pendingOperations;
    private bool _disposalStarted;
    private bool _disposed;

    // In-memory cache: profileName → (provider → connection)
    private Dictionary<string, Dictionary<ProviderKind, ProviderAuthConnection>>? _connections;

    public ProviderConnectionVaultStore(string vaultPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultPath);

        _vaultPath = vaultPath;
    }

    /// <summary>
    /// Get provider connection for a specific profile and provider.
    /// </summary>
    public async Task<ProviderAuthConnection?> GetConnectionAsync(
        string profileName,
        ProviderKind provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);

        await EnsureLoadedAsync(cancellationToken);

        if (_connections!.TryGetValue(profileName, out var profileConnections) &&
            profileConnections.TryGetValue(provider, out var connection))
        {
            return connection;
        }

        return null;
    }

    /// <summary>
    /// Save or update provider connection.
    /// </summary>
    public async Task SaveConnectionAsync(
        ProviderAuthConnection connection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(connection.ProfileName);

        await EnsureLoadedAsync(cancellationToken);

        if (!_connections!.ContainsKey(connection.ProfileName))
        {
            _connections[connection.ProfileName] = new Dictionary<ProviderKind, ProviderAuthConnection>();
        }

        _connections[connection.ProfileName][connection.Provider] = connection;

        await SaveAsync(cancellationToken);

        DebugConsole.WriteLine(
            $"[ProviderConnectionVault] Saved connection: profile={connection.ProfileName}, provider={connection.Provider}, method={connection.PreferredMethod}");
    }

    /// <summary>
    /// Remove provider connection.
    /// </summary>
    public async Task RemoveConnectionAsync(
        string profileName,
        ProviderKind provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);

        await EnsureLoadedAsync(cancellationToken);

        if (_connections!.TryGetValue(profileName, out var profileConnections))
        {
            if (profileConnections.Remove(provider))
            {
                if (profileConnections.Count == 0)
                {
                    _connections.Remove(profileName);
                }

                await SaveAsync(cancellationToken);

                DebugConsole.WriteLine(
                    $"[ProviderConnectionVault] Removed connection: profile={profileName}, provider={provider}");
            }
        }
    }

    /// <summary>
    /// Check if profile has credentials configured for a provider.
    /// </summary>
    public async Task<bool> HasCredentialsAsync(
        string profileName,
        ProviderKind provider,
        CancellationToken cancellationToken = default)
    {
        var connection = await GetConnectionAsync(profileName, provider, cancellationToken);

        if (connection == null)
            return false;

        // Has credentials if either Google OAuth or Direct is configured
        return !string.IsNullOrEmpty(connection.LinkedGoogleAccount) ||
               connection.DirectCredential != null;
    }

    /// <summary>
    /// Get all connections for a profile.
    /// </summary>
    public async Task<IReadOnlyDictionary<ProviderKind, ProviderAuthConnection>> GetProfileConnectionsAsync(
        string profileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileName);

        await EnsureLoadedAsync(cancellationToken);

        if (_connections!.TryGetValue(profileName, out var profileConnections))
        {
            return profileConnections;
        }

        return new Dictionary<ProviderKind, ProviderAuthConnection>();
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_connections != null)
            return;

        await LoadAsync(cancellationToken);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        await ExecuteOperationAsync(async () =>
        {
            if (!File.Exists(_vaultPath))
            {
                DebugConsole.WriteLine("[ProviderConnectionVault] Vault file does not exist, initializing empty vault");
                _connections = new Dictionary<string, Dictionary<ProviderKind, ProviderAuthConnection>>(
                    StringComparer.Ordinal);
                return;
            }

            try
            {
                var encryptedBytes = await File.ReadAllBytesAsync(_vaultPath, cancellationToken);
                var json = DecryptPayload(encryptedBytes);

                var deserialized = JsonSerializer.Deserialize<Dictionary<string, Dictionary<ProviderKind, ProviderAuthConnection>>>(
                    json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                _connections = deserialized ?? new Dictionary<string, Dictionary<ProviderKind, ProviderAuthConnection>>(
                    StringComparer.Ordinal);

                DebugConsole.WriteLine(
                    $"[ProviderConnectionVault] Loaded vault: {_connections.Count} profiles, {_connections.Values.Sum(p => p.Count)} connections");
            }
            catch (Exception ex)
            {
                DebugConsole.WriteLine($"[ProviderConnectionVault] ERROR loading vault: {ex.Message}, initializing empty");
                _connections = new Dictionary<string, Dictionary<ProviderKind, ProviderAuthConnection>>(
                    StringComparer.Ordinal);
            }
        }, cancellationToken);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        await ExecuteOperationAsync(async () =>
        {
            await _writeLock.WaitAsync(cancellationToken);
            try
            {
                var json = JsonSerializer.Serialize(_connections, new JsonSerializerOptions
                {
                    WriteIndented = false
                });

                var encryptedBytes = EncryptPayload(json);

                // Ensure directory exists
                var directory = Path.GetDirectoryName(_vaultPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllBytesAsync(_vaultPath, encryptedBytes, cancellationToken);

                DebugConsole.WriteLine(
                    $"[ProviderConnectionVault] Saved vault: {_connections!.Count} profiles, {_connections.Values.Sum(p => p.Count)} connections");
            }
            finally
            {
                _writeLock.Release();
            }
        }, cancellationToken);
    }

    private string DecryptPayload(byte[] encryptedBytes)
    {
        // Simple DPAPI decryption (matching GoogleAccountVaultStore pattern)
        var decryptedBytes = ProtectedData.Unprotect(encryptedBytes, DpapiEntropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(decryptedBytes);
    }

    private byte[] EncryptPayload(string json)
    {
        // Simple DPAPI encryption (matching GoogleAccountVaultStore pattern)
        var plaintextBytes = Encoding.UTF8.GetBytes(json);
        return ProtectedData.Protect(plaintextBytes, DpapiEntropy, DataProtectionScope.CurrentUser);
    }

    private async Task<T> ExecuteOperationAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        EnterOperation();
        try
        {
            await EnterGateAsync(cancellationToken);
            return await operation();
        }
        finally
        {
            ExitOperation();
        }
    }

    private async Task ExecuteOperationAsync(Func<Task> operation, CancellationToken cancellationToken)
    {
        EnterOperation();
        try
        {
            await EnterGateAsync(cancellationToken);
            await operation();
        }
        finally
        {
            ExitOperation();
        }
    }

    private async Task EnterGateAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken);
        _operationGate.Release();
    }

    private void EnterOperation()
    {
        lock (_disposalLock)
        {
            if (_disposalStarted)
            {
                throw new ObjectDisposedException(nameof(ProviderConnectionVaultStore));
            }
            _pendingOperations++;
        }
    }

    private void ExitOperation()
    {
        lock (_disposalLock)
        {
            _pendingOperations--;
            if (_pendingOperations == 0 && _disposalStarted)
            {
                Monitor.PulseAll(_disposalLock);
            }
        }
    }

    public void Dispose()
    {
        lock (_disposalLock)
        {
            if (_disposed)
                return;

            _disposalStarted = true;

            while (_pendingOperations > 0)
            {
                Monitor.Wait(_disposalLock);
            }

            _disposed = true;
        }

        _writeLock.Dispose();
        _operationGate.Dispose();
    }
}
