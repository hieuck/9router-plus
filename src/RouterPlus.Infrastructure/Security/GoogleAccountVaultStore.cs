using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RouterPlus.Core.Security;

namespace RouterPlus.Infrastructure.Security;

/// <summary>
/// Encrypted vault store using AES-256-GCM, PBKDF2-HMAC-SHA256, and DPAPI remembered unlock.
/// </summary>
public sealed class GoogleAccountVaultStore : IGoogleAccountVaultStore, IDisposable
{
    private const int CurrentVersion = 1;
    private const string KdfAlgorithmName = "PBKDF2-HMAC-SHA256";
    private const int KdfIterations = 600000;
    private const int SaltBytes = 16;
    private const int PayloadKeyBytes = 32;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private static readonly byte[] DpapiEntropy = Encoding.UTF8.GetBytes("9RouterPlus.GoogleAccountVault.v1");

    private readonly GoogleAccountVaultPaths _paths;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _disposalLock = new();
    private int _pendingOperations;
    private bool _disposalStarted;
    private bool _disposed;

    public GoogleAccountVaultStore(GoogleAccountVaultPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _paths = paths;
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

    private void EnterOperation()
    {
        lock (_disposalLock)
        {
            ThrowIfDisposalStarted();
            _pendingOperations++;
        }
    }

    private async Task EnterGateAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            lock (_disposalLock)
            {
                ThrowIfDisposalStarted();
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private void ThrowIfDisposalStarted()
    {
        if (_disposalStarted)
        {
            throw new ObjectDisposedException(GetType().Name);
        }
    }

    private void ExitOperation()
    {
        lock (_disposalLock)
        {
            _pendingOperations--;
            Monitor.PulseAll(_disposalLock);
        }
    }

    public async Task<GoogleAccountVaultSession> CreateAsync(
        string path,
        string vaultPassword,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, nameof(path));
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultPassword, nameof(vaultPassword));

        return await ExecuteOperationAsync(async () =>
        {
            var vaultId = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
            var payloadKey = RandomNumberGenerator.GetBytes(PayloadKeyBytes);

            var kdfSalt = RandomNumberGenerator.GetBytes(SaltBytes);
            var kdfKey = DeriveKey(vaultPassword, kdfSalt);

            var keyWrapNonce = RandomNumberGenerator.GetBytes(NonceBytes);
            var keyWrapTag = new byte[TagBytes];
            var wrappedPayloadKey = new byte[payloadKey.Length];

            var keyWrapAad = Encoding.UTF8.GetBytes($"{CurrentVersion}|{vaultId}|{KdfAlgorithmName}|KeyWrap");

            using (var aes = new AesGcm(kdfKey, TagBytes))
            {
                aes.Encrypt(keyWrapNonce, payloadKey, wrappedPayloadKey, keyWrapTag, keyWrapAad);
            }

            var keyWrapMetadata = new KeyWrapEnvelope
            {
                Version = CurrentVersion,
                VaultId = vaultId,
                KdfAlgorithm = KdfAlgorithmName,
                KdfIterations = KdfIterations,
                KdfSalt = Convert.ToBase64String(kdfSalt),
                KeyWrapNonce = Convert.ToBase64String(keyWrapNonce),
                KeyWrapTag = Convert.ToBase64String(keyWrapTag),
                WrappedPayloadKey = Convert.ToBase64String(wrappedPayloadKey)
            };

            var session = new VaultSession(path, vaultId, payloadKey, new GoogleAccountVault(), _paths, keyWrapMetadata);
            await SaveAsync(session, cancellationToken);

            return session;
        }, cancellationToken);
    }

    public async Task<GoogleAccountVaultSession> OpenAsync(
        string path,
        string vaultPassword,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, nameof(path));
        ArgumentException.ThrowIfNullOrWhiteSpace(vaultPassword, nameof(vaultPassword));

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Vault file not found.", path);
        }

        return await ExecuteOperationAsync(async () =>
        {
            try
            {
                var json = await File.ReadAllTextAsync(path, cancellationToken);
                var envelope = JsonSerializer.Deserialize<VaultEnvelope>(json)
                    ?? throw new CryptographicException("Invalid vault format.");

                ValidateEnvelope(envelope);

                var kdfSalt = Convert.FromBase64String(envelope.KdfSalt);
                var kdfKey = DeriveKey(vaultPassword, kdfSalt);

                var wrappedKeyNonce = Convert.FromBase64String(envelope.KeyWrapNonce);
                var wrappedKeyTag = Convert.FromBase64String(envelope.KeyWrapTag);
                var wrappedKeyCiphertext = Convert.FromBase64String(envelope.WrappedPayloadKey);

                var payloadKey = DecryptPayloadKey(kdfKey, wrappedKeyNonce, wrappedKeyTag, wrappedKeyCiphertext, envelope);

                var payloadNonce = Convert.FromBase64String(envelope.PayloadNonce);
                var payloadTag = Convert.FromBase64String(envelope.PayloadTag);
                var payloadCiphertext = Convert.FromBase64String(envelope.PayloadCiphertext);

                var payloadJson = DecryptPayload(payloadKey, payloadNonce, payloadTag, payloadCiphertext, envelope);
                var records = JsonSerializer.Deserialize<List<CredentialDto>>(payloadJson)
                    ?? throw new CryptographicException("Invalid payload format.");

                var vault = new GoogleAccountVault(records.Select(dto => new GoogleLoginCredential(
                    dto.ProfileId,
                    dto.Email,
                    dto.Password,
                    dto.TotpSecret)));

                return new VaultSession(path, envelope.VaultId, payloadKey, vault, _paths, new KeyWrapEnvelope
                {
                    Version = envelope.Version,
                    VaultId = envelope.VaultId,
                    KdfAlgorithm = envelope.KdfAlgorithm,
                    KdfIterations = envelope.KdfIterations,
                    KdfSalt = envelope.KdfSalt,
                    KeyWrapNonce = envelope.KeyWrapNonce,
                    KeyWrapTag = envelope.KeyWrapTag,
                    WrappedPayloadKey = envelope.WrappedPayloadKey
                });
            }
            catch (JsonException)
            {
                throw new CryptographicException("Invalid vault format.");
            }
            catch (FormatException)
            {
                throw new CryptographicException("Invalid vault format.");
            }
        }, cancellationToken);
    }

    public async Task<GoogleAccountVaultSession?> TryOpenRememberedAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path, nameof(path));

        var rememberedPath = _paths.RememberedKeyPath;
        if (!File.Exists(rememberedPath) || !File.Exists(path))
        {
            return null;
        }

        return await ExecuteOperationAsync(async () =>
        {
            try
            {
                var rememberedJson = await File.ReadAllTextAsync(rememberedPath, cancellationToken);
                var remembered = JsonSerializer.Deserialize<RememberedKey>(rememberedJson);

                if (remembered is null || remembered.Version != CurrentVersion)
                {
                    await RemoveRememberedFileAsync(cancellationToken);
                    return null;
                }

                var vaultJson = await File.ReadAllTextAsync(path, cancellationToken);
                var envelope = JsonSerializer.Deserialize<VaultEnvelope>(vaultJson);

                if (envelope is null || envelope.VaultId != remembered.VaultId)
                {
                    await RemoveRememberedFileAsync(cancellationToken);
                    return null;
                }

                var protectedKey = Convert.FromBase64String(remembered.ProtectedPayloadKey);
                var entropy = DeriveEntropy(remembered.VaultId);
                var payloadKey = ProtectedData.Unprotect(protectedKey, entropy, DataProtectionScope.CurrentUser);

                var payloadNonce = Convert.FromBase64String(envelope.PayloadNonce);
                var payloadTag = Convert.FromBase64String(envelope.PayloadTag);
                var payloadCiphertext = Convert.FromBase64String(envelope.PayloadCiphertext);

                var payloadJson = DecryptPayload(payloadKey, payloadNonce, payloadTag, payloadCiphertext, envelope);
                var records = JsonSerializer.Deserialize<List<CredentialDto>>(payloadJson)
                    ?? throw new CryptographicException("Invalid payload format.");

                var vault = new GoogleAccountVault(records.Select(dto => new GoogleLoginCredential(
                    dto.ProfileId,
                    dto.Email,
                    dto.Password,
                    dto.TotpSecret)));

                return new VaultSession(path, envelope.VaultId, payloadKey, vault, _paths, new KeyWrapEnvelope
                {
                    Version = envelope.Version,
                    VaultId = envelope.VaultId,
                    KdfAlgorithm = envelope.KdfAlgorithm,
                    KdfIterations = envelope.KdfIterations,
                    KdfSalt = envelope.KdfSalt,
                    KeyWrapNonce = envelope.KeyWrapNonce,
                    KeyWrapTag = envelope.KeyWrapTag,
                    WrappedPayloadKey = envelope.WrappedPayloadKey
                });
            }
            catch (JsonException)
            {
                await RemoveRememberedFileAsync(cancellationToken);
                return null;
            }
            catch (FormatException)
            {
                await RemoveRememberedFileAsync(cancellationToken);
                return null;
            }
            catch (CryptographicException)
            {
                await RemoveRememberedFileAsync(cancellationToken);
                return null;
            }
            catch (IOException)
            {
                await RemoveRememberedFileAsync(cancellationToken);
                return null;
            }
        }, cancellationToken);
    }

    public async Task SaveAsync(
        GoogleAccountVaultSession session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (session is not VaultSession vaultSession)
        {
            throw new ArgumentException("Invalid session type.", nameof(session));
        }

        await ExecuteOperationAsync(async () =>
        {
            var payloadKey = vaultSession.GetPayloadKey();
            var records = vaultSession.Vault.Records.Select(c => new CredentialDto
            {
                ProfileId = c.ProfileId,
                Email = c.Email,
                Password = c.Password,
                TotpSecret = c.TotpSecret
            }).ToList();

            var payloadJson = JsonSerializer.Serialize(records);
            var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);

            var payloadNonce = RandomNumberGenerator.GetBytes(NonceBytes);
            var payloadTag = new byte[TagBytes];
            var payloadCiphertext = new byte[payloadBytes.Length];

            var payloadAad = Encoding.UTF8.GetBytes($"{CurrentVersion}|{vaultSession.VaultId}|{KdfAlgorithmName}|Payload");

            using (var aes = new AesGcm(payloadKey, TagBytes))
            {
                aes.Encrypt(payloadNonce, payloadBytes, payloadCiphertext, payloadTag, payloadAad);
            }

            var keyWrapMetadata = vaultSession.GetKeyWrapMetadata();

            var envelope = new VaultEnvelope
            {
                Version = keyWrapMetadata.Version,
                VaultId = keyWrapMetadata.VaultId,
                KdfAlgorithm = keyWrapMetadata.KdfAlgorithm,
                KdfIterations = keyWrapMetadata.KdfIterations,
                KdfSalt = keyWrapMetadata.KdfSalt,
                KeyWrapNonce = keyWrapMetadata.KeyWrapNonce,
                KeyWrapTag = keyWrapMetadata.KeyWrapTag,
                WrappedPayloadKey = keyWrapMetadata.WrappedPayloadKey,
                PayloadNonce = Convert.ToBase64String(payloadNonce),
                PayloadTag = Convert.ToBase64String(payloadTag),
                PayloadCiphertext = Convert.ToBase64String(payloadCiphertext)
            };

            var json = JsonSerializer.Serialize(envelope, new JsonSerializerOptions { WriteIndented = true });
            await WriteAtomicAsync(vaultSession.Path, json, cancellationToken);
        }, cancellationToken);
    }

    public async Task ExportAsync(
        GoogleAccountVaultSession session,
        string destinationPath,
        string exportPassword,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath, nameof(destinationPath));
        ArgumentException.ThrowIfNullOrWhiteSpace(exportPassword, nameof(exportPassword));

        if (session is not VaultSession vaultSession)
        {
            throw new ArgumentException("Invalid session type.", nameof(session));
        }

        await ExecuteOperationAsync(async () =>
        {
            var payloadKey = vaultSession.GetPayloadKey();
            var vaultId = vaultSession.VaultId;

            var kdfSalt = RandomNumberGenerator.GetBytes(SaltBytes);
            var kdfKey = DeriveKey(exportPassword, kdfSalt);

            var keyWrapNonce = RandomNumberGenerator.GetBytes(NonceBytes);
            var keyWrapTag = new byte[TagBytes];
            var wrappedPayloadKey = new byte[payloadKey.Length];

            var keyWrapAad = Encoding.UTF8.GetBytes($"{CurrentVersion}|{vaultId}|{KdfAlgorithmName}|KeyWrap");

            using (var aes = new AesGcm(kdfKey, TagBytes))
            {
                aes.Encrypt(keyWrapNonce, payloadKey, wrappedPayloadKey, keyWrapTag, keyWrapAad);
            }

            var keyWrapMetadata = new KeyWrapEnvelope
            {
                Version = CurrentVersion,
                VaultId = vaultId,
                KdfAlgorithm = KdfAlgorithmName,
                KdfIterations = KdfIterations,
                KdfSalt = Convert.ToBase64String(kdfSalt),
                KeyWrapNonce = Convert.ToBase64String(keyWrapNonce),
                KeyWrapTag = Convert.ToBase64String(keyWrapTag),
                WrappedPayloadKey = Convert.ToBase64String(wrappedPayloadKey)
            };

            var tempSession = new VaultSession(destinationPath, vaultId, payloadKey, vaultSession.Vault, _paths, keyWrapMetadata);
            await SaveAsync(tempSession, cancellationToken);
        }, cancellationToken);
    }

    public async Task ImportAsync(
        string currentPath,
        string sourcePath,
        string sourcePassword,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currentPath, nameof(currentPath));
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath, nameof(sourcePath));
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePassword, nameof(sourcePassword));

        await ExecuteOperationAsync(async () =>
        {
            var sourceContent = await File.ReadAllTextAsync(sourcePath, cancellationToken);
            var importedSession = await OpenAsync(sourcePath, sourcePassword, cancellationToken);
            await using (importedSession)
            {
                if (File.Exists(currentPath))
                {
                    var backupPath = currentPath + ".bak";
                    File.Copy(currentPath, backupPath, overwrite: true);
                }

                await WriteAtomicAsync(currentPath, sourceContent, cancellationToken);

                await RemoveRememberedFileAsync(cancellationToken);
            }
        }, cancellationToken);
    }

    private static void ValidateEnvelope(VaultEnvelope envelope)
    {
        if (envelope.Version != CurrentVersion)
        {
            throw new CryptographicException($"Unsupported vault version: {envelope.Version}");
        }

        if (envelope.KdfAlgorithm != KdfAlgorithmName)
        {
            throw new CryptographicException($"Unsupported KDF algorithm: {envelope.KdfAlgorithm}");
        }

        if (envelope.KdfIterations != KdfIterations)
        {
            throw new CryptographicException($"Invalid KDF iterations: {envelope.KdfIterations}");
        }
    }

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(password),
            salt,
            KdfIterations,
            HashAlgorithmName.SHA256,
            PayloadKeyBytes);
    }

    private static byte[] DecryptPayloadKey(
        byte[] kdfKey,
        byte[] nonce,
        byte[] tag,
        byte[] ciphertext,
        VaultEnvelope envelope)
    {
        var plaintext = new byte[ciphertext.Length];
        var associatedData = Encoding.UTF8.GetBytes($"{envelope.Version}|{envelope.VaultId}|{envelope.KdfAlgorithm}|KeyWrap");

        try
        {
            using var aes = new AesGcm(kdfKey, TagBytes);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
            return plaintext;
        }
        catch (CryptographicException)
        {
            throw new CryptographicException("Invalid vault password or corrupted vault.");
        }
    }

    private static string DecryptPayload(
        byte[] payloadKey,
        byte[] nonce,
        byte[] tag,
        byte[] ciphertext,
        VaultEnvelope envelope)
    {
        var plaintext = new byte[ciphertext.Length];
        var associatedData = Encoding.UTF8.GetBytes($"{envelope.Version}|{envelope.VaultId}|{envelope.KdfAlgorithm}|Payload");

        try
        {
            using var aes = new AesGcm(payloadKey, TagBytes);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (CryptographicException)
        {
            throw new CryptographicException("Vault integrity check failed.");
        }
    }

    private static byte[] DeriveEntropy(string vaultId)
    {
        var vaultIdBytes = Encoding.UTF8.GetBytes(vaultId);
        var entropy = new byte[DpapiEntropy.Length + vaultIdBytes.Length];
        Buffer.BlockCopy(DpapiEntropy, 0, entropy, 0, DpapiEntropy.Length);
        Buffer.BlockCopy(vaultIdBytes, 0, entropy, DpapiEntropy.Length, vaultIdBytes.Length);
        return entropy;
    }

    private async Task WriteAtomicAsync(string path, string content, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = path + $".tmp.{Guid.NewGuid():N}";
            await File.WriteAllTextAsync(tempPath, content, cancellationToken);
            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task RemoveRememberedFileAsync(CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            if (File.Exists(_paths.RememberedKeyPath))
            {
                File.Delete(_paths.RememberedKeyPath);
            }
        }, cancellationToken);
    }

    private sealed class VaultSession : GoogleAccountVaultSession
    {
        private readonly byte[] _payloadKey;
        private readonly KeyWrapEnvelope _keyWrapMetadata;
        private bool _disposed;

        public VaultSession(string path, string vaultId, byte[] payloadKey, GoogleAccountVault vault, GoogleAccountVaultPaths paths, KeyWrapEnvelope keyWrapMetadata)
        {
            Path = path;
            VaultId = vaultId;
            _payloadKey = payloadKey;
            Vault = vault;
            Paths = paths;
            _keyWrapMetadata = keyWrapMetadata;
        }

        public string Path { get; }
        public string VaultId { get; }
        public GoogleAccountVault Vault { get; private set; }
        public GoogleAccountVaultPaths Paths { get; }

        public void Replace(GoogleAccountVault vault)
        {
            ArgumentNullException.ThrowIfNull(vault);
            Vault = vault;
        }

        public byte[] GetPayloadKey()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _payloadKey;
        }

        public KeyWrapEnvelope GetKeyWrapMetadata()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _keyWrapMetadata;
        }

        public async Task RememberAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var entropy = DeriveEntropy(VaultId);
            var protectedKey = ProtectedData.Protect(_payloadKey, entropy, DataProtectionScope.CurrentUser);

            var remembered = new RememberedKey
            {
                Version = CurrentVersion,
                VaultId = VaultId,
                ProtectedPayloadKey = Convert.ToBase64String(protectedKey)
            };

            var json = JsonSerializer.Serialize(remembered, new JsonSerializerOptions { WriteIndented = true });
            var directory = System.IO.Path.GetDirectoryName(Paths.RememberedKeyPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllTextAsync(Paths.RememberedKeyPath, json, cancellationToken);
        }

        public async Task RemoveRememberedAsync(CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                if (File.Exists(Paths.RememberedKeyPath))
                {
                    File.Delete(Paths.RememberedKeyPath);
                }
            }, cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            Array.Clear(_payloadKey);
            _disposed = true;
            await Task.CompletedTask;
        }
    }

    private sealed class VaultEnvelope
    {
        public int Version { get; set; }
        public string VaultId { get; set; } = string.Empty;
        public string KdfAlgorithm { get; set; } = string.Empty;
        public int KdfIterations { get; set; }
        public string KdfSalt { get; set; } = string.Empty;
        public string KeyWrapNonce { get; set; } = string.Empty;
        public string KeyWrapTag { get; set; } = string.Empty;
        public string WrappedPayloadKey { get; set; } = string.Empty;
        public string PayloadNonce { get; set; } = string.Empty;
        public string PayloadTag { get; set; } = string.Empty;
        public string PayloadCiphertext { get; set; } = string.Empty;
    }

    private sealed class RememberedKey
    {
        public int Version { get; set; }
        public string VaultId { get; set; } = string.Empty;
        public string ProtectedPayloadKey { get; set; } = string.Empty;
    }

    private sealed class KeyWrapEnvelope
    {
        public int Version { get; set; }
        public string VaultId { get; set; } = string.Empty;
        public string KdfAlgorithm { get; set; } = string.Empty;
        public int KdfIterations { get; set; }
        public string KdfSalt { get; set; } = string.Empty;
        public string KeyWrapNonce { get; set; } = string.Empty;
        public string KeyWrapTag { get; set; } = string.Empty;
        public string WrappedPayloadKey { get; set; } = string.Empty;
    }

    private sealed class CredentialDto
    {
        public string ProfileId { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string TotpSecret { get; set; } = string.Empty;
    }

    public void Dispose()
    {
        lock (_disposalLock)
        {
            if (_disposed)
            {
                return;
            }

            if (_disposalStarted)
            {
                while (!_disposed)
                {
                    Monitor.Wait(_disposalLock);
                }

                return;
            }

            _disposalStarted = true;
            while (_pendingOperations > 0)
            {
                Monitor.Wait(_disposalLock);
            }
        }

        _writeLock.Dispose();
        _operationGate.Dispose();

        lock (_disposalLock)
        {
            _disposed = true;
            Monitor.PulseAll(_disposalLock);
        }
    }
}
