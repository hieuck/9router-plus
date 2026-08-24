using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RouterPlus.Infrastructure.Security;

public sealed class DpapiSecretVault : ISecretVault
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("9RouterPlus.SecretVault.v1");
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ProcessWideGates = new(StringComparer.OrdinalIgnoreCase);

    private readonly string _filePath;
    private readonly SemaphoreSlim _processWideGate;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public DpapiSecretVault(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "9RouterPlus",
            "secrets.json");

        var normalizedPath = Path.GetFullPath(_filePath);
        _processWideGate = ProcessWideGates.GetOrAdd(normalizedPath, _ => new SemaphoreSlim(1, 1));
    }

    public async Task<string?> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await _processWideGate.WaitAsync(cancellationToken);
        try
        {
            var values = await ReadStoreAsync(cancellationToken);
            if (!values.TryGetValue(key, out var encoded))
            {
                return null;
            }

            var protectedBytes = Convert.FromBase64String(encoded);
            var plainBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        finally
        {
            _processWideGate.Release();
        }
    }

    public async Task StoreAsync(string key, string secret, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        await _processWideGate.WaitAsync(cancellationToken);
        try
        {
            var values = await ReadStoreAsync(cancellationToken);
            var plainBytes = Encoding.UTF8.GetBytes(secret);
            var protectedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
            values[key] = Convert.ToBase64String(protectedBytes);
            await WriteStoreAsync(values, cancellationToken);
        }
        finally
        {
            _processWideGate.Release();
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        await _processWideGate.WaitAsync(cancellationToken);
        try
        {
            var values = await ReadStoreAsync(cancellationToken);
            if (values.Remove(key))
            {
                await WriteStoreAsync(values, cancellationToken);
            }
        }
        finally
        {
            _processWideGate.Release();
        }
    }

    private async Task<Dictionary<string, string>> ReadStoreAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        await using var stream = File.OpenRead(_filePath);
        var values = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(stream, _jsonOptions, cancellationToken);
        return values is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(values, StringComparer.Ordinal);
    }

    private async Task WriteStoreAsync(Dictionary<string, string> values, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(_filePath) ?? string.Empty,
            $"{Path.GetFileName(_filePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, values, _jsonOptions, cancellationToken);
            }

            File.Move(temporaryPath, _filePath, true);
        }
        catch
        {
            // Clean up temp file on failure
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch
            {
                // Best effort cleanup
            }
            throw;
        }
    }
}
