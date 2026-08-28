using System.Security.Cryptography;
using System.Text.Json;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Security;
using Xunit;

namespace RouterPlus.Core.Tests;

public sealed class GoogleAccountVaultStoreTests
{
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
