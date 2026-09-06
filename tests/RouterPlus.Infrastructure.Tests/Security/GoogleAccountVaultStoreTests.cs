using System.Runtime.InteropServices;
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
        _paths = new GoogleAccountVaultPaths(Path.Combine(_testDirectory, "remembered.dat"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
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
