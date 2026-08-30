using System.Security.Cryptography;
using System.Text.Json;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Security;
using Xunit;

namespace RouterPlus.Core.Tests;

public sealed class GoogleAccountVaultStoreTests
{
    private const string VaultPassword = "vault-password";
    private const string MarkerEmail = "integrity@example.test";
    private const string MarkerPassword = "integrity-synthetic-password";
    private const string MarkerTotp = "HXQWIIQFDUIJZA3Q";

    [Fact]
    public async Task Save_and_open_round_trip_the_profile_record()
    {
        using var root = new TemporaryDirectory();
        var paths = new GoogleAccountVaultPaths(root.Path);
        using var store = new GoogleAccountVaultStore(paths);
        await using var session = await store.CreateAsync(paths.VaultPath, "vault-password");
        session.Replace(new GoogleAccountVault().Upsert(
            new GoogleLoginCredential("profile-1", "user@example.com", "password", "JBSWY3DPEHPK3PXP")));
        await store.SaveAsync(session);

        await using var reopened = await store.OpenAsync(paths.VaultPath, "vault-password");

        var record = reopened.Vault.Find("profile-1");
        Assert.NotNull(record);
        Assert.Equal("user@example.com", record.Email);
        Assert.Equal("password", record.Password);
        Assert.Equal("JBSWY3DPEHPK3PXP", record.TotpSecret);
    }

    [Fact]
    public async Task Wrong_password_is_rejected_without_secret_material_in_message()
    {
        using var root = new TemporaryDirectory();
        var paths = new GoogleAccountVaultPaths(root.Path);
        using var store = new GoogleAccountVaultStore(paths);
        await using var session = await store.CreateAsync(paths.VaultPath, "vault-password");
        session.Replace(new GoogleAccountVault().Upsert(
            new GoogleLoginCredential("profile-1", "user@example.com", "synthetic-password", "JBSWY3DPEHPK3PXP")));
        await store.SaveAsync(session);

        var exception = await Assert.ThrowsAsync<CryptographicException>(() =>
            store.OpenAsync(paths.VaultPath, "wrong-password"));

        Assert.DoesNotContain("synthetic-password", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("JBSWY3DPEHPK3PXP", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("vault-password", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tampered_ciphertext_is_rejected()
    {
        using var root = new TemporaryDirectory();
        var paths = new GoogleAccountVaultPaths(root.Path);
        using var store = new GoogleAccountVaultStore(paths);
        await using var session = await store.CreateAsync(paths.VaultPath, "vault-password");
        session.Replace(new GoogleAccountVault().Upsert(
            new GoogleLoginCredential("profile-1", "user@example.com", "password", "JBSWY3DPEHPK3PXP")));
        await store.SaveAsync(session);

        var envelope = JsonDocument.Parse(await File.ReadAllTextAsync(paths.VaultPath));
        var payload = Convert.FromBase64String(envelope.RootElement.GetProperty("PayloadCiphertext").GetString()!);
        payload[0] ^= 0xFF;
        var json = JsonSerializer.Deserialize<Dictionary<string, object>>(await File.ReadAllTextAsync(paths.VaultPath))!;
        json["PayloadCiphertext"] = Convert.ToBase64String(payload);
        await File.WriteAllTextAsync(paths.VaultPath, JsonSerializer.Serialize(json));

        var exception = await Assert.ThrowsAsync<CryptographicException>(() =>
            store.OpenAsync(paths.VaultPath, "vault-password"));

        Assert.DoesNotContain("password", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("JBSWY3DPEHPK3PXP", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tampered_payload_nonce_is_rejected()
    {
        using var root = new TemporaryDirectory();
        var paths = new GoogleAccountVaultPaths(root.Path);
        using var store = new GoogleAccountVaultStore(paths);
        await using var session = await store.CreateAsync(paths.VaultPath, VaultPassword);
        session.Replace(new GoogleAccountVault().Upsert(
            new GoogleLoginCredential("profile-1", MarkerEmail, MarkerPassword, MarkerTotp)));
        await store.SaveAsync(session);

        var json = JsonSerializer.Deserialize<Dictionary<string, object>>(await File.ReadAllTextAsync(paths.VaultPath))!;
        var nonce = Convert.FromBase64String(json["PayloadNonce"]!.ToString()!);
        nonce[0] ^= 0xFF;
        json["PayloadNonce"] = Convert.ToBase64String(nonce);
        await File.WriteAllTextAsync(paths.VaultPath, JsonSerializer.Serialize(json));

        var exception = await Assert.ThrowsAsync<CryptographicException>(() =>
            store.OpenAsync(paths.VaultPath, VaultPassword));

        Assert.Equal("Vault integrity check failed.", exception.Message);
    }

    [Fact]
    public async Task Tampered_payload_tag_is_rejected()
    {
        using var root = new TemporaryDirectory();
        var paths = new GoogleAccountVaultPaths(root.Path);
        using var store = new GoogleAccountVaultStore(paths);
        await using var session = await store.CreateAsync(paths.VaultPath, VaultPassword);
        session.Replace(new GoogleAccountVault().Upsert(
            new GoogleLoginCredential("profile-1", MarkerEmail, MarkerPassword, MarkerTotp)));
        await store.SaveAsync(session);

        var json = JsonSerializer.Deserialize<Dictionary<string, object>>(await File.ReadAllTextAsync(paths.VaultPath))!;
        var tag = Convert.FromBase64String(json["PayloadTag"]!.ToString()!);
        tag[^1] ^= 0xFF;
        json["PayloadTag"] = Convert.ToBase64String(tag);
        await File.WriteAllTextAsync(paths.VaultPath, JsonSerializer.Serialize(json));

        var exception = await Assert.ThrowsAsync<CryptographicException>(() =>
            store.OpenAsync(paths.VaultPath, VaultPassword));

        Assert.Equal("Vault integrity check failed.", exception.Message);
    }

    [Fact]
    public async Task Malformed_non_json_vault_file_is_rejected_as_invalid_format()
    {
        using var root = new TemporaryDirectory();
        var paths = new GoogleAccountVaultPaths(root.Path);
        using var store = new GoogleAccountVaultStore(paths);
        await File.WriteAllTextAsync(paths.VaultPath, "<html>not a vault</html>");

        var exception = await Assert.ThrowsAsync<CryptographicException>(() =>
            store.OpenAsync(paths.VaultPath, VaultPassword));

        Assert.Equal("Invalid vault format.", exception.Message);
        Assert.DoesNotContain(MarkerEmail, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(MarkerPassword, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(MarkerTotp, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Envelope_missing_required_properties_is_rejected()
    {
        using var root = new TemporaryDirectory();
        var paths = new GoogleAccountVaultPaths(root.Path);
        using var store = new GoogleAccountVaultStore(paths);
        await File.WriteAllTextAsync(paths.VaultPath, """{ }""");

        var exception = await Assert.ThrowsAsync<CryptographicException>(() =>
            store.OpenAsync(paths.VaultPath, VaultPassword));

        Assert.Equal("Unsupported vault version: 0", exception.Message);
        Assert.DoesNotContain(VaultPassword, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unsupported_envelope_version_is_rejected()
    {
        using var root = new TemporaryDirectory();
        var paths = new GoogleAccountVaultPaths(root.Path);
        using var store = new GoogleAccountVaultStore(paths);
        await using var session = await store.CreateAsync(paths.VaultPath, VaultPassword);
        await store.SaveAsync(session);

        var json = JsonSerializer.Deserialize<Dictionary<string, object>>(await File.ReadAllTextAsync(paths.VaultPath))!;
        json["Version"] = 99;
        await File.WriteAllTextAsync(paths.VaultPath, JsonSerializer.Serialize(json));

        var exception = await Assert.ThrowsAsync<CryptographicException>(() =>
            store.OpenAsync(paths.VaultPath, VaultPassword));

        Assert.Equal("Unsupported vault version: 99", exception.Message);
    }

    [Fact]
    public async Task Unsupported_kdf_algorithm_is_rejected()
    {
        using var root = new TemporaryDirectory();
        var paths = new GoogleAccountVaultPaths(root.Path);
        using var store = new GoogleAccountVaultStore(paths);
        await using var session = await store.CreateAsync(paths.VaultPath, VaultPassword);
        await store.SaveAsync(session);

        var json = JsonSerializer.Deserialize<Dictionary<string, object>>(await File.ReadAllTextAsync(paths.VaultPath))!;
        json["KdfAlgorithm"] = "PBKDF2-HMAC-SHA1";
        await File.WriteAllTextAsync(paths.VaultPath, JsonSerializer.Serialize(json));

        var exception = await Assert.ThrowsAsync<CryptographicException>(() =>
            store.OpenAsync(paths.VaultPath, VaultPassword));

        Assert.Equal("Unsupported KDF algorithm: PBKDF2-HMAC-SHA1", exception.Message);
    }

    [Fact]
    public async Task Unsupported_kdf_iterations_are_rejected()
    {
        using var root = new TemporaryDirectory();
        var paths = new GoogleAccountVaultPaths(root.Path);
        using var store = new GoogleAccountVaultStore(paths);
        await using var session = await store.CreateAsync(paths.VaultPath, VaultPassword);
        await store.SaveAsync(session);

        var json = JsonSerializer.Deserialize<Dictionary<string, object>>(await File.ReadAllTextAsync(paths.VaultPath))!;
        json["KdfIterations"] = 1000;
        await File.WriteAllTextAsync(paths.VaultPath, JsonSerializer.Serialize(json));

        var exception = await Assert.ThrowsAsync<CryptographicException>(() =>
            store.OpenAsync(paths.VaultPath, VaultPassword));

        Assert.Equal("Invalid KDF iterations: 1000", exception.Message);
    }

    [Fact]
    public async Task Invalid_payload_object_is_rejected_without_exposing_partial_state()
    {
        using var root = new TemporaryDirectory();
        var paths = new GoogleAccountVaultPaths(root.Path);
        using var store = new GoogleAccountVaultStore(paths);
        await using var session = await store.CreateAsync(paths.VaultPath, VaultPassword);
        await store.SaveAsync(session);

        // Re-wrap the payload key under the vault password, then encrypt a non-array JSON payload.
        var payloadKey = UnwrapPayloadKeyWithPassword(paths, VaultPassword);
        var json = JsonSerializer.Deserialize<Dictionary<string, object>>(await File.ReadAllTextAsync(paths.VaultPath))!;
        ReplacePayload(json, payloadKey, session.VaultId, """{ "not": "a credential array" }""");
        await File.WriteAllTextAsync(paths.VaultPath, JsonSerializer.Serialize(json));

        var exception = await Assert.ThrowsAsync<CryptographicException>(() =>
            store.OpenAsync(paths.VaultPath, VaultPassword));

        Assert.Equal("Invalid vault format.", exception.Message);
        // The crafted payload is never stored in plaintext on disk.
        Assert.DoesNotContain("{ \"not\": \"a credential array\" }", await File.ReadAllTextAsync(paths.VaultPath), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Credential_with_invalid_email_field_is_rejected_and_mapped_to_invalid_format()
    {
        using var root = new TemporaryDirectory();
        var paths = new GoogleAccountVaultPaths(root.Path);
        using var store = new GoogleAccountVaultStore(paths);
        await using var session = await store.CreateAsync(paths.VaultPath, VaultPassword);
        await store.SaveAsync(session);

        // A blob-valid payload whose credential has a malformed email -> FormatException -> generic message.
        CorruptPayloadCredential(paths, vaultKey: session.VaultId, email: "not-an-email", password: "irrelevant", totp: "irrelevant");

        var exception = await Assert.ThrowsAsync<CryptographicException>(() =>
            store.OpenAsync(paths.VaultPath, VaultPassword));

        Assert.Equal("Invalid vault format.", exception.Message);
        Assert.DoesNotContain("irrelevant", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Save_failure_keeps_previous_vault_bytes_and_leaves_no_temp_file()
    {
        using var root = new TemporaryDirectory();
        var paths = new GoogleAccountVaultPaths(root.Path);
        using var store = new GoogleAccountVaultStore(paths);
        await using var session = await store.CreateAsync(paths.VaultPath, VaultPassword);
        session.Replace(new GoogleAccountVault().Upsert(
            new GoogleLoginCredential("profile-1", MarkerEmail, MarkerPassword, MarkerTotp)));
        await store.SaveAsync(session);
        var original = await File.ReadAllTextAsync(paths.VaultPath);

        var nextPassword = new string('x', 50000);
        session.Replace(new GoogleAccountVault().Upsert(
            new GoogleLoginCredential("profile-1", MarkerEmail, nextPassword, MarkerTotp)));
        File.SetAttributes(paths.VaultPath, FileAttributes.ReadOnly);

        try
        {
            var saveException = await CaptureSaveFailureAsync(() => store.SaveAsync(session));
            Assert.True(
                saveException is IOException or UnauthorizedAccessException,
                $"Expected an IOException/UnauthorizedAccessException, got: {saveException.GetType().Name} ({saveException.Message})");
            Assert.Equal(original, await File.ReadAllTextAsync(paths.VaultPath));
            var leftover = Directory.EnumerateFiles(root.Path, "google-accounts.vault.tmp.*").ToArray();
            Assert.Empty(leftover);
            Assert.False(File.Exists(paths.VaultPath + ".bak"));
            Assert.False(File.Exists(paths.RememberedKeyPath));
        }
        finally
        {
            File.SetAttributes(paths.VaultPath, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task Failed_save_leaves_no_secret_markers_in_error_or_remaining_files()
    {
        using var root = new TemporaryDirectory();
        var paths = new GoogleAccountVaultPaths(root.Path);
        using var store = new GoogleAccountVaultStore(paths);
        await using var session = await store.CreateAsync(paths.VaultPath, VaultPassword);
        session.Replace(new GoogleAccountVault().Upsert(
            new GoogleLoginCredential("profile-1", MarkerEmail, MarkerPassword, MarkerTotp)));
        await store.SaveAsync(session);

        File.SetAttributes(paths.VaultPath, FileAttributes.ReadOnly);
        var remaining = new List<string>();
        try
        {
            var exception = await CaptureSaveFailureAsync(() => store.SaveAsync(session));
            Assert.DoesNotContain(MarkerEmail, exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(MarkerPassword, exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(MarkerTotp, exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(VaultPassword, exception.Message, StringComparison.Ordinal);

            var onDisk = await File.ReadAllTextAsync(paths.VaultPath);
            Assert.DoesNotContain(MarkerPassword, onDisk, StringComparison.Ordinal);
            Assert.DoesNotContain(MarkerTotp, onDisk, StringComparison.Ordinal);
        }
        finally
        {
            File.SetAttributes(paths.VaultPath, FileAttributes.Normal);
            remaining.AddRange(Directory.EnumerateFiles(root.Path, "google-accounts.vault.tmp.*"));
        }

        Assert.Empty(remaining);
    }

    [Fact]
    public async Task Remembered_key_mismatch_is_rejected_and_remembered_file_removed()
    {
        using var root = new TemporaryDirectory();
        var paths = new GoogleAccountVaultPaths(root.Path);
        using var store = new GoogleAccountVaultStore(paths);
        await using var session = await store.CreateAsync(paths.VaultPath, VaultPassword);
        session.Replace(new GoogleAccountVault().Upsert(
            new GoogleLoginCredential("profile-1", MarkerEmail, MarkerPassword, MarkerTotp)));
        await store.SaveAsync(session);
        await session.RememberAsync();

        var json = JsonSerializer.Deserialize<Dictionary<string, object>>(await File.ReadAllTextAsync(paths.VaultPath))!;
        json["VaultId"] = "AAAAAAAAAAAAAAAAAAAAAA==";
        await File.WriteAllTextAsync(paths.VaultPath, JsonSerializer.Serialize(json));

        Assert.Null(await store.TryOpenRememberedAsync(paths.VaultPath));
        Assert.False(File.Exists(paths.RememberedKeyPath));
    }

    [Fact]
    public async Task Remembered_file_with_wrong_version_is_rejected_and_removed()
    {
        using var root = new TemporaryDirectory();
        var paths = new GoogleAccountVaultPaths(root.Path);
        using var store = new GoogleAccountVaultStore(paths);
        await using var session = await store.CreateAsync(paths.VaultPath, VaultPassword);
        await store.SaveAsync(session);
        await session.RememberAsync();

        var json = JsonSerializer.Deserialize<Dictionary<string, object>>(await File.ReadAllTextAsync(paths.RememberedKeyPath))!;
        json["Version"] = 99;
        await File.WriteAllTextAsync(paths.RememberedKeyPath, JsonSerializer.Serialize(json));

        Assert.Null(await store.TryOpenRememberedAsync(paths.VaultPath));
        Assert.False(File.Exists(paths.RememberedKeyPath));
    }

    [Fact]
    public async Task Remembered_file_secrets_are_dpapi_protected_not_plaintext()
    {
        using var root = new TemporaryDirectory();
        var paths = new GoogleAccountVaultPaths(root.Path);
        using var store = new GoogleAccountVaultStore(paths);
        await using var session = await store.CreateAsync(paths.VaultPath, VaultPassword);
        session.Replace(new GoogleAccountVault().Upsert(
            new GoogleLoginCredential("profile-1", MarkerEmail, MarkerPassword, MarkerTotp)));
        await store.SaveAsync(session);
        await session.RememberAsync();

        var rememberedJson = await File.ReadAllTextAsync(paths.RememberedKeyPath);

        Assert.Contains("ProtectedPayloadKey", rememberedJson, StringComparison.Ordinal);
        Assert.DoesNotContain(MarkerEmail, rememberedJson, StringComparison.Ordinal);
        Assert.DoesNotContain(MarkerPassword, rememberedJson, StringComparison.Ordinal);
        Assert.DoesNotContain(MarkerTotp, rememberedJson, StringComparison.Ordinal);
        Assert.DoesNotContain(VaultPassword, rememberedJson, StringComparison.Ordinal);

        var protectedKey = Convert.FromBase64String(
            JsonDocument.Parse(rememberedJson).RootElement.GetProperty("ProtectedPayloadKey").GetString()!);

        // The stored blob must be DPAPI-protected: same entropy as the store uses.
        var entropy = System.Text.Encoding.UTF8.GetBytes("9RouterPlus.GoogleAccountVault.v1")
            .Concat(System.Text.Encoding.UTF8.GetBytes(session.VaultId))
            .ToArray();
        var unprotected = ProtectedData.Unprotect(protectedKey, entropy, DataProtectionScope.CurrentUser);
        Assert.Equal(32, unprotected.Length);
        Assert.NotEqual(Convert.ToBase64String(unprotected),
            JsonDocument.Parse(rememberedJson).RootElement.GetProperty("ProtectedPayloadKey").GetString());
    }

    [Fact]
    public async Task Open_failure_messages_never_contain_synthetic_secrets()
    {
        using var root = new TemporaryDirectory();
        var paths = new GoogleAccountVaultPaths(root.Path);
        using var store = new GoogleAccountVaultStore(paths);
        await using var session = await store.CreateAsync(paths.VaultPath, VaultPassword);
        session.Replace(new GoogleAccountVault().Upsert(
            new GoogleLoginCredential("profile-1", MarkerEmail, MarkerPassword, MarkerTotp)));
        await store.SaveAsync(session);
        var writeTv = new Func<string, Task>(async (text) => await File.WriteAllTextAsync(paths.VaultPath, text));

        // Capture the message each failure mode produces, then assert none leak markers.
        var messages = new List<string>();
        try
        {
            _ = await store.OpenAsync(paths.VaultPath, "wrong-password");
        }
        catch (CryptographicException ex)
        {
            messages.Add(ex.Message);
        }

        var tampered = JsonSerializer.Deserialize<Dictionary<string, object>>(await File.ReadAllTextAsync(paths.VaultPath))!;
        var tamperedCiphertext = Convert.FromBase64String(tampered["PayloadCiphertext"]!.ToString()!);
        tamperedCiphertext[0] ^= 0xFF;
        tampered["PayloadCiphertext"] = Convert.ToBase64String(tamperedCiphertext);
        await writeTv(JsonSerializer.Serialize(tampered));
        try
        {
            _ = await store.OpenAsync(paths.VaultPath, VaultPassword);
        }
        catch (CryptographicException ex)
        {
            messages.Add(ex.Message);
        }

        await writeTv("{ not valid json !");
        try
        {
            _ = await store.OpenAsync(paths.VaultPath, VaultPassword);
        }
        catch (CryptographicException ex)
        {
            messages.Add(ex.Message);
        }

        await writeTv("""{ "Version": 42 }""");
        try
        {
            _ = await store.OpenAsync(paths.VaultPath, VaultPassword);
        }
        catch (CryptographicException ex)
        {
            messages.Add(ex.Message);
        }

        Assert.NotEmpty(messages);
        foreach (var message in messages)
        {
            Assert.DoesNotContain(MarkerEmail, message, StringComparison.Ordinal);
            Assert.DoesNotContain(MarkerPassword, message, StringComparison.Ordinal);
            Assert.DoesNotContain(MarkerTotp, message, StringComparison.Ordinal);
            Assert.DoesNotContain(VaultPassword, message, StringComparison.Ordinal);
            Assert.True(message.Length < 256, $"Message too long/informative: {message}");
        }
    }

    [Fact]
    public async Task Vault_file_contains_only_metadata_and_ciphertext_outside_payload()
    {
        using var root = new TemporaryDirectory();
        var paths = new GoogleAccountVaultPaths(root.Path);
        using var store = new GoogleAccountVaultStore(paths);
        await using var session = await store.CreateAsync(paths.VaultPath, "vault-password");
        session.Replace(new GoogleAccountVault().Upsert(
            new GoogleLoginCredential("profile-1", "user@example.com", "synthetic-password", "JBSWY3DPEHPK3PXP")));
        await store.SaveAsync(session);

        var json = await File.ReadAllTextAsync(paths.VaultPath);

        Assert.DoesNotContain("user@example.com", json, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-password", json, StringComparison.Ordinal);
        Assert.DoesNotContain("JBSWY3DPEHPK3PXP", json, StringComparison.Ordinal);
        Assert.Contains("PayloadCiphertext", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Export_and_import_round_trip_replaces_current_vault_and_creates_backup()
    {
        using var root = new TemporaryDirectory();
        using var exportRoot = new TemporaryDirectory();
        var paths = new GoogleAccountVaultPaths(root.Path);
        using var store = new GoogleAccountVaultStore(paths);
        await using var session = await store.CreateAsync(paths.VaultPath, "current-password");
        session.Replace(new GoogleAccountVault().Upsert(
            new GoogleLoginCredential("source-profile", "source@example.com", "source-password", "JBSWY3DPEHPK3PXP")));
        await store.SaveAsync(session);

        var exportPath = Path.Combine(exportRoot.Path, "export.gvault");
        await store.ExportAsync(session, exportPath, "export-password");

        session.Replace(new GoogleAccountVault().Upsert(
            new GoogleLoginCredential("current-profile", "current@example.com", "current-password", "JBSWY3DPEHPK3PXP")));
        await store.SaveAsync(session);
        await store.ImportAsync(paths.VaultPath, exportPath, "export-password");

        await using var imported = await store.OpenAsync(paths.VaultPath, "export-password");
        Assert.NotNull(imported.Vault.Find("source-profile"));
        Assert.Null(imported.Vault.Find("current-profile"));
        Assert.True(File.Exists(paths.VaultPath + ".bak"));
    }

    [Fact]
    public async Task Failed_import_leaves_current_vault_and_backup_unchanged()
    {
        using var root = new TemporaryDirectory();
        using var sourceRoot = new TemporaryDirectory();
        var paths = new GoogleAccountVaultPaths(root.Path);
        var sourcePaths = new GoogleAccountVaultPaths(sourceRoot.Path);
        using var store = new GoogleAccountVaultStore(paths);
        using var sourceStore = new GoogleAccountVaultStore(sourcePaths);
        await using var session = await store.CreateAsync(paths.VaultPath, "current-password");
        session.Replace(new GoogleAccountVault().Upsert(
            new GoogleLoginCredential("current-profile", "current@example.com", "current-password", "JBSWY3DPEHPK3PXP")));
        await store.SaveAsync(session);
        var original = await File.ReadAllTextAsync(paths.VaultPath);

        await using var sourceSession = await sourceStore.CreateAsync(sourcePaths.VaultPath, "source-password");
        sourceSession.Replace(new GoogleAccountVault().Upsert(
            new GoogleLoginCredential("source-profile", "source@example.com", "source-password", "JBSWY3DPEHPK3PXP")));
        await sourceStore.SaveAsync(sourceSession);
        var sourceText = await File.ReadAllTextAsync(sourcePaths.VaultPath);
        await File.WriteAllTextAsync(sourcePaths.VaultPath, sourceText[..^2] + "xx");

        await Assert.ThrowsAsync<CryptographicException>(() =>
            store.ImportAsync(paths.VaultPath, sourcePaths.VaultPath, "source-password"));

        Assert.Equal(original, await File.ReadAllTextAsync(paths.VaultPath));
        Assert.False(File.Exists(paths.VaultPath + ".bak"));
    }

    [Fact]
    public async Task Failed_import_leaves_remembered_unlock_intact()
    {
        using var root = new TemporaryDirectory();
        using var sourceRoot = new TemporaryDirectory();
        var paths = new GoogleAccountVaultPaths(root.Path);
        var sourcePaths = new GoogleAccountVaultPaths(sourceRoot.Path);
        using var store = new GoogleAccountVaultStore(paths);
        using var sourceStore = new GoogleAccountVaultStore(sourcePaths);
        await using var session = await store.CreateAsync(paths.VaultPath, "current-password");
        session.Replace(new GoogleAccountVault().Upsert(
            new GoogleLoginCredential("current-profile", "current@example.com", "current-password", "JBSWY3DPEHPK3PXP")));
        await store.SaveAsync(session);
        await session.RememberAsync();

        await using var source = await sourceStore.CreateAsync(sourcePaths.VaultPath, "source-password");
        source.Replace(new GoogleAccountVault().Upsert(
            new GoogleLoginCredential("source-profile", "source@example.com", "source-password", "JBSWY3DPEHPK3PXP")));
        await sourceStore.SaveAsync(source);
        var sourceText = await File.ReadAllTextAsync(sourcePaths.VaultPath);
        await File.WriteAllTextAsync(sourcePaths.VaultPath, sourceText[..^2] + "zz");

        await Assert.ThrowsAsync<CryptographicException>(() =>
            store.ImportAsync(paths.VaultPath, sourcePaths.VaultPath, "source-password"));

        Assert.True(File.Exists(paths.RememberedKeyPath));
        await using var remembered = await store.TryOpenRememberedAsync(paths.VaultPath);
        Assert.NotNull(remembered);
        Assert.NotNull(remembered.Vault.Find("current-profile"));
    }

    [Fact]
    public async Task Import_replaces_backup_file_and_keeps_previous_backup_intact_on_failure()
    {
        using var root = new TemporaryDirectory();
        using var sourceRoot = new TemporaryDirectory();
        var paths = new GoogleAccountVaultPaths(root.Path);
        var sourcePaths = new GoogleAccountVaultPaths(sourceRoot.Path);
        using var store = new GoogleAccountVaultStore(paths);
        using var sourceStore = new GoogleAccountVaultStore(sourcePaths);
        await using var session = await store.CreateAsync(paths.VaultPath, "current-password");
        session.Replace(new GoogleAccountVault().Upsert(
            new GoogleLoginCredential("current-profile", "current@example.com", "current-password", "JBSWY3DPEHPK3PXP")));
        await store.SaveAsync(session);
        var firstBackup = await CreateFirstBackupAsync(paths);

        await using var source = await sourceStore.CreateAsync(sourcePaths.VaultPath, "source-password");
        source.Replace(new GoogleAccountVault().Upsert(
            new GoogleLoginCredential("source-profile", "source@example.com", "source-password", "JBSWY3DPEHPK3PXP")));
        await sourceStore.SaveAsync(source);

        // First import: the stale fake backup is replaced with the pre-import current vault.
        await store.ImportAsync(paths.VaultPath, sourcePaths.VaultPath, "source-password");

        var backupText = await File.ReadAllTextAsync(paths.VaultPath + ".bak");
        Assert.DoesNotContain("stale-sentinel", backupText, StringComparison.Ordinal); // overwritten
        await using (var backup = await store.OpenAsync(paths.VaultPath + ".bak", "current-password"))
        {
            Assert.NotNull(backup.Vault.Find("current-profile"));
            Assert.Null(backup.Vault.Find("source-profile"));
        }

        // Second import fails mid-flight (bad source version): backup must be untouched and
        // the current vault still opens with the imported password.
        var importSource = await File.ReadAllTextAsync(sourcePaths.VaultPath);
        var brokenSource = JsonSerializer.Deserialize<Dictionary<string, object>>(importSource)!;
        brokenSource["Version"] = 999;
        await File.WriteAllTextAsync(sourcePaths.VaultPath, JsonSerializer.Serialize(brokenSource));
        await Assert.ThrowsAsync<CryptographicException>(() =>
            store.ImportAsync(paths.VaultPath, sourcePaths.VaultPath, "source-password"));

        Assert.Equal(backupText, await File.ReadAllTextAsync(paths.VaultPath + ".bak"));
        await using var current = await store.OpenAsync(paths.VaultPath, "source-password");
        Assert.NotNull(current.Vault.Find("source-profile"));
    }

    [Fact]
    public async Task Session_disposal_blocks_further_saves()
    {
        using var root = new TemporaryDirectory();
        var paths = new GoogleAccountVaultPaths(root.Path);
        using var store = new GoogleAccountVaultStore(paths);
        var session = await store.CreateAsync(paths.VaultPath, VaultPassword);
        session.Replace(new GoogleAccountVault().Upsert(
            new GoogleLoginCredential("profile-1", MarkerEmail, MarkerPassword, MarkerTotp)));
        await store.SaveAsync(session);

        await session.DisposeAsync();

        var exception = await Assert.ThrowsAsync<ObjectDisposedException>(() => store.SaveAsync(session));
        Assert.Contains("VaultSession", exception.ObjectName, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Session_disposal_blocks_remember_and_remove_remembered()
    {
        using var root = new TemporaryDirectory();
        var paths = new GoogleAccountVaultPaths(root.Path);
        using var store = new GoogleAccountVaultStore(paths);
        var session = await store.CreateAsync(paths.VaultPath, VaultPassword);
        await session.RememberAsync();
        Assert.True(File.Exists(paths.RememberedKeyPath));

        await session.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.RememberAsync());
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.RemoveRememberedAsync());
        Assert.True(File.Exists(paths.RememberedKeyPath));
    }

    [Fact]
    public async Task Remembered_unlock_reopens_on_same_user_and_remove_disables_it()
    {
        using var root = new TemporaryDirectory();
        var paths = new GoogleAccountVaultPaths(root.Path);
        using var store = new GoogleAccountVaultStore(paths);
        await using var session = await store.CreateAsync(paths.VaultPath, "vault-password");
        session.Replace(new GoogleAccountVault().Upsert(
            new GoogleLoginCredential("profile-1", "user@example.com", "password", "JBSWY3DPEHPK3PXP")));
        await store.SaveAsync(session);
        await session.RememberAsync();

        await using var remembered = await store.TryOpenRememberedAsync(paths.VaultPath);
        Assert.NotNull(remembered);
        Assert.Equal("user@example.com", remembered.Vault.Find("profile-1")!.Email);

        await remembered.RemoveRememberedAsync();
        Assert.Null(await store.TryOpenRememberedAsync(paths.VaultPath));
    }

    [Fact]
    public async Task Import_invalidates_previous_remembered_unlock()
    {
        using var root = new TemporaryDirectory();
        using var sourceRoot = new TemporaryDirectory();
        var paths = new GoogleAccountVaultPaths(root.Path);
        var sourcePaths = new GoogleAccountVaultPaths(sourceRoot.Path);
        using var store = new GoogleAccountVaultStore(paths);
        using var sourceStore = new GoogleAccountVaultStore(sourcePaths);
        await using var session = await store.CreateAsync(paths.VaultPath, "current-password");
        session.Replace(new GoogleAccountVault().Upsert(
            new GoogleLoginCredential("current-profile", "current@example.com", "current-password", "JBSWY3DPEHPK3PXP")));
        await store.SaveAsync(session);
        await session.RememberAsync();

        await using var source = await sourceStore.CreateAsync(sourcePaths.VaultPath, "source-password");
        source.Replace(new GoogleAccountVault().Upsert(
            new GoogleLoginCredential("source-profile", "source@example.com", "source-password", "JBSWY3DPEHPK3PXP")));
        await sourceStore.SaveAsync(source);
        await store.ImportAsync(paths.VaultPath, sourcePaths.VaultPath, "source-password");

        Assert.Null(await store.TryOpenRememberedAsync(paths.VaultPath));
    }

    [Fact]
    public async Task Remembered_session_can_save_and_original_password_still_opens_vault()
    {
        using var root = new TemporaryDirectory();
        var paths = new GoogleAccountVaultPaths(root.Path);
        using var store = new GoogleAccountVaultStore(paths);
        await using var session = await store.CreateAsync(paths.VaultPath, "original-password");
        session.Replace(new GoogleAccountVault().Upsert(
            new GoogleLoginCredential("profile-1", "user@example.com", "password", "JBSWY3DPEHPK3PXP")));
        await store.SaveAsync(session);
        await session.RememberAsync();

        await using var remembered = await store.TryOpenRememberedAsync(paths.VaultPath);
        Assert.NotNull(remembered);
        remembered.Replace(new GoogleAccountVault().Upsert(
            new GoogleLoginCredential("profile-2", "user2@example.com", "password2", "JBSWY3DPEHPK3PXQ")));
        await store.SaveAsync(remembered);

        await using var reopened = await store.OpenAsync(paths.VaultPath, "original-password");
        Assert.NotNull(reopened.Vault.Find("profile-2"));
        Assert.Equal("user2@example.com", reopened.Vault.Find("profile-2")!.Email);
    }

    [Fact]
    public async Task Disposed_store_rejects_operations_with_ObjectDisposedException()
    {
        using var root = new TemporaryDirectory();
        var paths = new GoogleAccountVaultPaths(root.Path);
        var store = new GoogleAccountVaultStore(paths);
        await using var session = await store.CreateAsync(paths.VaultPath, "password");
        await store.SaveAsync(session);

        store.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.OpenAsync(paths.VaultPath, "password"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.CreateAsync(paths.VaultPath + ".new", "password"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.SaveAsync(session));
    }

    [Fact]
    public async Task Concurrent_dispose_calls_are_safe()
    {
        using var root = new TemporaryDirectory();
        var paths = new GoogleAccountVaultPaths(root.Path);
        var store = new GoogleAccountVaultStore(paths);
        await using var session = await store.CreateAsync(paths.VaultPath, "password");
        await store.SaveAsync(session);

        var tasks = Enumerable.Range(0, 10).Select(_ => Task.Run(() => store.Dispose())).ToArray();
        await Task.WhenAll(tasks);

        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.OpenAsync(paths.VaultPath, "password"));
    }

    [Fact]
    public async Task Dispose_blocks_new_operations_after_gate_acquired()
    {
        using var root = new TemporaryDirectory();
        var paths = new GoogleAccountVaultPaths(root.Path);
        var store = new GoogleAccountVaultStore(paths);
        await using var session = await store.CreateAsync(paths.VaultPath, "password");
        await store.SaveAsync(session);

        store.Dispose();

        // Operations after dispose should throw ObjectDisposedException, not hang
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.OpenAsync(paths.VaultPath, "password"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.SaveAsync(session));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.CreateAsync(paths.VaultPath + ".new", "password"));
    }

    [Fact]
    public async Task Dispose_can_be_called_multiple_times_safely()
    {
        using var root = new TemporaryDirectory();
        var paths = new GoogleAccountVaultPaths(root.Path);
        var store = new GoogleAccountVaultStore(paths);
        await using var session = await store.CreateAsync(paths.VaultPath, "password");
        await store.SaveAsync(session);

        // Multiple dispose calls should not throw or deadlock
        store.Dispose();
        store.Dispose();
        store.Dispose();

        // Operations should still be rejected
        await Assert.ThrowsAsync<ObjectDisposedException>(() => store.OpenAsync(paths.VaultPath, "password"));
    }

    private static byte[] UnwrapPayloadKeyWithPassword(GoogleAccountVaultPaths paths, string password)
    {
        var envelope = JsonDocument.Parse(File.ReadAllText(paths.VaultPath)).RootElement;
        var salt = Convert.FromBase64String(envelope.GetProperty("KdfSalt").GetString()!);
        var kdfKey = System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(
            System.Text.Encoding.UTF8.GetBytes(password),
            salt,
            600000,
            HashAlgorithmName.SHA256,
            32);
        var nonce = Convert.FromBase64String(envelope.GetProperty("KeyWrapNonce").GetString()!);
        var tag = Convert.FromBase64String(envelope.GetProperty("KeyWrapTag").GetString()!);
        var ciphertext = Convert.FromBase64String(envelope.GetProperty("WrappedPayloadKey").GetString()!);
        var aad = System.Text.Encoding.UTF8.GetBytes($"1|{envelope.GetProperty("VaultId").GetString()}|PBKDF2-HMAC-SHA256|KeyWrap");
        var payloadKey = new byte[ciphertext.Length];
        using var aes = new AesGcm(kdfKey, 16);
        aes.Decrypt(nonce, ciphertext, tag, payloadKey, aad);
        return payloadKey;
    }

    private static async Task<Exception> CaptureSaveFailureAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ex;
        }

        throw new Xunit.Sdk.XunitException("Expected SaveAsync to fail with an IO error.");
    }

    private static void ReplacePayload(
        Dictionary<string, object> json,
        byte[] payloadKey,
        string vaultId,
        string payloadJson)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var plaintext = System.Text.Encoding.UTF8.GetBytes(payloadJson);
        var ciphertext = new byte[plaintext.Length];
        var aad = System.Text.Encoding.UTF8.GetBytes($"1|{vaultId}|PBKDF2-HMAC-SHA256|Payload");
        using (var aes = new AesGcm(payloadKey, 16))
        {
            aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);
        }

        json["PayloadNonce"] = Convert.ToBase64String(nonce);
        json["PayloadTag"] = Convert.ToBase64String(tag);
        json["PayloadCiphertext"] = Convert.ToBase64String(ciphertext);
    }

    private static void CorruptPayloadCredential(
        GoogleAccountVaultPaths paths,
        string vaultKey,
        string email,
        string password,
        string totp)
    {
        var payloadKey = UnwrapPayloadKeyWithPassword(paths, "vault-password");
        var payloadJson = JsonSerializer.Serialize(new[]
        {
            new { ProfileId = "profile-1", Email = email, Password = password, TotpSecret = totp }
        });
        var json = JsonSerializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(paths.VaultPath))!;
        ReplacePayload(json, payloadKey, vaultKey, payloadJson);
        File.WriteAllText(paths.VaultPath, JsonSerializer.Serialize(json));
    }

    private static async Task<string> CreateFirstBackupAsync(GoogleAccountVaultPaths paths)
    {
        // A stale sentinel placeholder the import must overwrite.
        await File.WriteAllTextAsync(paths.VaultPath + ".bak", "stale-sentinel");
        return await File.ReadAllTextAsync(paths.VaultPath + ".bak");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "RouterPlusTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
            }
        }
    }
}