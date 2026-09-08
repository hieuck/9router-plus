using System.Runtime.InteropServices;
using System.Security.Cryptography;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Security;

namespace RouterPlus.Infrastructure.Tests.Security;

public sealed class GoogleAccountVaultStoreTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly GoogleAccountVaultPaths _paths;

    public GoogleAccountVaultStoreTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"GoogleVaultTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
        _paths = new GoogleAccountVaultPaths(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task OpenAsync_MalformedEnvelope_ThrowsInvalidVaultFormat()
    {
        var store = new GoogleAccountVaultStore(_paths);
        var vaultPath = Path.Combine(_testDirectory, "malformed.vault");
        await File.WriteAllTextAsync(vaultPath, "{ not valid json");

        var exception = await Assert.ThrowsAsync<CryptographicException>(() =>
            store.OpenAsync(vaultPath, "synthetic-password"));

        Assert.Equal("Invalid vault format.", exception.Message);
    }

    [Fact]
    public async Task OpenAsync_EnvelopeWithUnsupportedVersion_ThrowsSpecificError()
    {
        var store = new GoogleAccountVaultStore(_paths);
        var vaultPath = Path.Combine(_testDirectory, "unsupported-version.vault");
        await File.WriteAllTextAsync(vaultPath, "{\"Version\":99}");

        var exception = await Assert.ThrowsAsync<CryptographicException>(() =>
            store.OpenAsync(vaultPath, "synthetic-password"));

        Assert.Equal("Unsupported vault version: 99", exception.Message);
    }

    [Fact]
    public async Task ExportAsync_ThenImportAsync_ReplacesCurrentVaultAndCreatesBackup()
    {
        var store = new GoogleAccountVaultStore(_paths);
        var currentPath = Path.Combine(_testDirectory, "current.vault");
        var exportPath = Path.Combine(_testDirectory, "export.vault");

        await using var current = await store.CreateAsync(currentPath, "current-password");
        current.Replace(new GoogleAccountVault().Upsert(
            new GoogleLoginCredential("source-profile", "source@example.test", "source-password", "SYNTHETIC-TOTP")));
        await store.SaveAsync(current);
        await store.ExportAsync(current, exportPath, "export-password");

        current.Replace(new GoogleAccountVault().Upsert(
            new GoogleLoginCredential("current-profile", "current@example.test", "current-password", "SYNTHETIC-TOTP")));
        await store.SaveAsync(current);
        await store.ImportAsync(currentPath, exportPath, "export-password");

        await using var imported = await store.OpenAsync(currentPath, "export-password");
        Assert.NotNull(imported.Vault.Find("source-profile"));
        Assert.Null(imported.Vault.Find("current-profile"));
        Assert.True(File.Exists(currentPath + ".bak"));
    }

    [Fact]
    public async Task ImportAsync_InvalidSource_LeavesCurrentVaultAndRememberedUnlockIntact()
    {
        var store = new GoogleAccountVaultStore(_paths);
        var currentPath = Path.Combine(_testDirectory, "current.vault");
        var sourcePath = Path.Combine(_testDirectory, "source.vault");

        await using var current = await store.CreateAsync(currentPath, "current-password");
        current.Replace(new GoogleAccountVault().Upsert(
            new GoogleLoginCredential("current-profile", "current@example.test", "current-password", "SYNTHETIC-TOTP")));
        await store.SaveAsync(current);
        await current.RememberAsync();
        var original = await File.ReadAllTextAsync(currentPath);

        await using var source = await store.CreateAsync(sourcePath, "source-password");
        await store.SaveAsync(source);
        var sourceText = await File.ReadAllTextAsync(sourcePath);
        await File.WriteAllTextAsync(sourcePath, sourceText[..^2] + "xx");

        await Assert.ThrowsAsync<CryptographicException>(() =>
            store.ImportAsync(currentPath, sourcePath, "source-password"));

        Assert.Equal(original, await File.ReadAllTextAsync(currentPath));
        Assert.False(File.Exists(currentPath + ".bak"));
        Assert.True(File.Exists(_paths.RememberedKeyPath));
        await using var remembered = await store.TryOpenRememberedAsync(currentPath);
        Assert.NotNull(remembered);
        Assert.NotNull(remembered.Vault.Find("current-profile"));
    }

    [Fact]
    public async Task ImportAsync_Success_InvalidatesRememberedUnlock()
    {
        var store = new GoogleAccountVaultStore(_paths);
        var currentPath = Path.Combine(_testDirectory, "current.vault");
        var sourcePath = Path.Combine(_testDirectory, "source.vault");

        await using var current = await store.CreateAsync(currentPath, "current-password");
        await store.SaveAsync(current);
        await current.RememberAsync();
        Assert.True(File.Exists(_paths.RememberedKeyPath));

        await using var source = await store.CreateAsync(sourcePath, "source-password");
        await store.SaveAsync(source);
        await store.ImportAsync(currentPath, sourcePath, "source-password");

        Assert.False(File.Exists(_paths.RememberedKeyPath));
        Assert.Null(await store.TryOpenRememberedAsync(currentPath));
    }

    [Fact]
    public async Task TryOpenRememberedAsync_MalformedRememberedEnvelope_RemovesRememberedFile()
    {
        var store = new GoogleAccountVaultStore(_paths);
        var vaultPath = Path.Combine(_testDirectory, "remembered.vault");
        await using var session = await store.CreateAsync(vaultPath, "synthetic-password");
        await store.SaveAsync(session);
        await File.WriteAllTextAsync(_paths.RememberedKeyPath, "{ not valid json");

        var result = await store.TryOpenRememberedAsync(vaultPath);

        Assert.Null(result);
        Assert.False(File.Exists(_paths.RememberedKeyPath));
    }

    [Fact]
    public async Task CreateAsync_ThenOpenAsync_ReturnsValidSession()
    {
        var store = new GoogleAccountVaultStore(_paths);
        var vaultPath = Path.Combine(_testDirectory, "test.vault");
        const string password = "test-password-123";

        var createSession = await store.CreateAsync(vaultPath, password);
        Assert.NotNull(createSession);
        Assert.NotNull(createSession.VaultId);
        Assert.True(File.Exists(vaultPath));

        var openSession = await store.OpenAsync(vaultPath, password);
        Assert.NotNull(openSession);
        Assert.Equal(createSession.VaultId, openSession.VaultId);
    }

    [Fact]
    public async Task OpenAsync_WrongPassword_ThrowsCryptographicException()
    {
        var store = new GoogleAccountVaultStore(_paths);
        var vaultPath = Path.Combine(_testDirectory, "test.vault");

        await store.CreateAsync(vaultPath, "correct-password");

        await Assert.ThrowsAsync<System.Security.Cryptography.CryptographicException>(() =>
            store.OpenAsync(vaultPath, "wrong-password"));
    }

    [Fact]
    public async Task OpenAsync_NonExistentVault_ThrowsFileNotFoundException()
    {
        var store = new GoogleAccountVaultStore(_paths);
        var nonExistentPath = Path.Combine(_testDirectory, "nonexistent.vault");

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            store.OpenAsync(nonExistentPath, "password"));
    }

    [Fact]
    public async Task CreateAsync_InvalidPath_ThrowsArgumentException()
    {
        var store = new GoogleAccountVaultStore(_paths);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.CreateAsync("", "password"));
    }

    [Fact]
    public async Task CreateAsync_InvalidPassword_ThrowsArgumentException()
    {
        var store = new GoogleAccountVaultStore(_paths);
        var vaultPath = Path.Combine(_testDirectory, "test.vault");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.CreateAsync(vaultPath, ""));
    }

    [Fact]
    public async Task SaveAsync_UpdatesVaultContent()
    {
        var store = new GoogleAccountVaultStore(_paths);
        var vaultPath = Path.Combine(_testDirectory, "test.vault");
        const string password = "test-password";

        var session = await store.CreateAsync(vaultPath, password);

        // Add credential to vault (immutable pattern)
        var credential = new GoogleLoginCredential("profile1", "test@example.com", "password123", "TOTPSECRET");
        var updatedVault = session.Vault.Upsert(credential);
        session.Replace(updatedVault);

        await store.SaveAsync(session);

        // Reopen and verify
        var reopened = await store.OpenAsync(vaultPath, password);
        Assert.Single(reopened.Vault.Records);
        Assert.Equal("test@example.com", reopened.Vault.Records[0].Email);
        Assert.Equal("profile1", reopened.Vault.Records[0].ProfileId);
    }

    [Fact]
    public async Task RememberAsync_ThenTryOpenRememberedAsync_SucceedsOnWindows()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // DPAPI remember feature only works on Windows
            return;
        }

        var store = new GoogleAccountVaultStore(_paths);
        var vaultPath = Path.Combine(_testDirectory, "test.vault");
        const string password = "test-password";

        var session = await store.CreateAsync(vaultPath, password);
        await session.RememberAsync();

        var remembered = await store.TryOpenRememberedAsync(vaultPath);
        Assert.NotNull(remembered);
        Assert.Equal(session.VaultId, remembered.VaultId);
    }

    [Fact]
    public async Task TryOpenRememberedAsync_NoRemembered_ReturnsNull()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var store = new GoogleAccountVaultStore(_paths);
        var vaultPath = Path.Combine(_testDirectory, "test.vault");

        await store.CreateAsync(vaultPath, "password");
        // Don't call RememberAsync

        var result = await store.TryOpenRememberedAsync(vaultPath);
        Assert.Null(result);
    }

    [Fact]
    public async Task RememberAsync_ThenRemoveRememberedAsync_ClearsRemembered()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var store = new GoogleAccountVaultStore(_paths);
        var vaultPath = Path.Combine(_testDirectory, "test.vault");

        var session = await store.CreateAsync(vaultPath, "password");
        await session.RememberAsync();

        var remembered = await store.TryOpenRememberedAsync(vaultPath);
        Assert.NotNull(remembered);

        await remembered.RemoveRememberedAsync();

        var afterRemove = await store.TryOpenRememberedAsync(vaultPath);
        Assert.Null(afterRemove);
    }

    [Fact]
    public async Task CreateAsync_GeneratesUniqueVaultIds()
    {
        var store = new GoogleAccountVaultStore(_paths);
        var path1 = Path.Combine(_testDirectory, "vault1.vault");
        var path2 = Path.Combine(_testDirectory, "vault2.vault");

        var session1 = await store.CreateAsync(path1, "password1");
        var session2 = await store.CreateAsync(path2, "password2");

        Assert.NotEqual(session1.VaultId, session2.VaultId);
    }

    [Fact]
    public async Task SaveAsync_MultipleCredentials_AllPreserved()
    {
        var store = new GoogleAccountVaultStore(_paths);
        var vaultPath = Path.Combine(_testDirectory, "test.vault");

        var session = await store.CreateAsync(vaultPath, "password");

        var credential1 = new GoogleLoginCredential("profile1", "user1@example.com", "pass1", "TOTP1");
        var credential2 = new GoogleLoginCredential("profile2", "user2@example.com", "pass2", "TOTP2");

        var vault = session.Vault.Upsert(credential1).Upsert(credential2);
        session.Replace(vault);

        await store.SaveAsync(session);

        var reopened = await store.OpenAsync(vaultPath, "password");
        Assert.Equal(2, reopened.Vault.Records.Count);
        Assert.Contains(reopened.Vault.Records, r => r.Email == "user1@example.com");
        Assert.Contains(reopened.Vault.Records, r => r.Email == "user2@example.com");
    }

    [Fact]
    public async Task Cancellation_ThrowsTaskCanceledException()
    {
        var store = new GoogleAccountVaultStore(_paths);
        var vaultPath = Path.Combine(_testDirectory, "test.vault");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            store.CreateAsync(vaultPath, "password", cts.Token));
    }

    [Fact]
    public async Task Dispose_WaitsForPendingOperations()
    {
        var store = new GoogleAccountVaultStore(_paths);
        var vaultPath = Path.Combine(_testDirectory, "test.vault");

        var createTask = store.CreateAsync(vaultPath, "password");
        store.Dispose();

        // Should not throw - dispose waits for pending operations
        var session = await createTask;
        Assert.NotNull(session);
    }

    [Fact]
    public async Task AfterDispose_ThrowsObjectDisposedException()
    {
        var store = new GoogleAccountVaultStore(_paths);
        var vaultPath = Path.Combine(_testDirectory, "test.vault");

        store.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            store.CreateAsync(vaultPath, "password"));
    }
}
