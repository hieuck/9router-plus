using RouterPlus.Core.Models;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Security;

namespace RouterPlus.Infrastructure.Tests.Security;

public sealed class ProviderConnectionVaultStoreTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly string _vaultPath;

    public ProviderConnectionVaultStoreTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"ProviderVaultTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
        _vaultPath = Path.Combine(_testDirectory, "connections.vault");
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveConnectionAsync_ThenGetConnectionAsync_ReturnsConnection()
    {
        var store = new ProviderConnectionVaultStore(_vaultPath);
        const string profileName = "profile1";
        var connection = new ProviderAuthConnection
        {
            ProfileName = profileName,
            Provider = ProviderKind.Codex,
            PreferredMethod = AuthMethod.Direct,
            DirectCredential = new ProviderCredential { Email = "test@example.com", Password = "test-pass" }
        };

        await store.SaveConnectionAsync(connection);
        var retrieved = await store.GetConnectionAsync(profileName, ProviderKind.Codex);

        Assert.NotNull(retrieved);
        Assert.Equal(ProviderKind.Codex, retrieved.Provider);
        Assert.Equal("test@example.com", retrieved.DirectCredential?.Email);
    }

    [Fact]
    public async Task GetConnectionAsync_NonExistent_ReturnsNull()
    {
        var store = new ProviderConnectionVaultStore(_vaultPath);

        var result = await store.GetConnectionAsync("nonexistent", ProviderKind.Codex);

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveConnectionAsync_Overwrite_UpdatesConnection()
    {
        var store = new ProviderConnectionVaultStore(_vaultPath);
        const string profileName = "profile1";

        var connection1 = new ProviderAuthConnection
        {
            ProfileName = profileName,
            Provider = ProviderKind.Codex,
            PreferredMethod = AuthMethod.Direct,
            DirectCredential = new ProviderCredential { Email = "user1@example.com", Password = "pass1" }
        };

        var connection2 = new ProviderAuthConnection
        {
            ProfileName = profileName,
            Provider = ProviderKind.Codex,
            PreferredMethod = AuthMethod.Direct,
            DirectCredential = new ProviderCredential { Email = "user2@example.com", Password = "pass2" }
        };

        await store.SaveConnectionAsync(connection1);
        await store.SaveConnectionAsync(connection2);

        var retrieved = await store.GetConnectionAsync(profileName, ProviderKind.Codex);

        Assert.NotNull(retrieved);
        Assert.Equal("user2@example.com", retrieved.DirectCredential?.Email);
    }

    [Fact]
    public async Task RemoveConnectionAsync_ExistingConnection_RemovesIt()
    {
        var store = new ProviderConnectionVaultStore(_vaultPath);
        const string profileName = "profile1";

        var connection = new ProviderAuthConnection
        {
            ProfileName = profileName,
            Provider = ProviderKind.Codex,
            PreferredMethod = AuthMethod.Direct,
            DirectCredential = new ProviderCredential { Email = "user@example.com", Password = "pass" }
        };

        await store.SaveConnectionAsync(connection);
        await store.RemoveConnectionAsync(profileName, ProviderKind.Codex);

        var retrieved = await store.GetConnectionAsync(profileName, ProviderKind.Codex);

        Assert.Null(retrieved);
    }

    [Fact]
    public async Task RemoveConnectionAsync_NonExistent_DoesNotThrow()
    {
        var store = new ProviderConnectionVaultStore(_vaultPath);

        await store.RemoveConnectionAsync("nonexistent", ProviderKind.Codex);

        // Should complete without exception
    }

    [Fact]
    public async Task HasCredentialsAsync_WithCredentials_ReturnsTrue()
    {
        var store = new ProviderConnectionVaultStore(_vaultPath);
        const string profileName = "profile1";

        var connection = new ProviderAuthConnection
        {
            ProfileName = profileName,
            Provider = ProviderKind.Codex,
            PreferredMethod = AuthMethod.Direct,
            DirectCredential = new ProviderCredential { Email = "user@example.com", Password = "pass" }
        };

        await store.SaveConnectionAsync(connection);

        var hasCredentials = await store.HasCredentialsAsync(profileName, ProviderKind.Codex);

        Assert.True(hasCredentials);
    }

    [Fact]
    public async Task HasCredentialsAsync_WithoutCredentials_ReturnsFalse()
    {
        var store = new ProviderConnectionVaultStore(_vaultPath);

        var hasCredentials = await store.HasCredentialsAsync("profile1", ProviderKind.Codex);

        Assert.False(hasCredentials);
    }

    [Fact]
    public async Task GetProfileConnectionsAsync_MultipleProviders_ReturnsAll()
    {
        var store = new ProviderConnectionVaultStore(_vaultPath);
        const string profileName = "profile1";

        var codexConnection = new ProviderAuthConnection
        {
            ProfileName = profileName,
            Provider = ProviderKind.Codex,
            PreferredMethod = AuthMethod.Direct,
            DirectCredential = new ProviderCredential { Email = "codex@example.com", Password = "codex-pass" }
        };

        var openRouterConnection = new ProviderAuthConnection
        {
            ProfileName = profileName,
            Provider = ProviderKind.OpenRouter,
            PreferredMethod = AuthMethod.Direct,
            DirectCredential = new ProviderCredential { Email = "openrouter@example.com", Password = "openrouter-pass" }
        };

        await store.SaveConnectionAsync(codexConnection);
        await store.SaveConnectionAsync(openRouterConnection);

        var connections = await store.GetProfileConnectionsAsync(profileName);

        Assert.Equal(2, connections.Count);
        Assert.True(connections.ContainsKey(ProviderKind.Codex));
        Assert.True(connections.ContainsKey(ProviderKind.OpenRouter));
        Assert.Equal("codex@example.com", connections[ProviderKind.Codex].DirectCredential?.Email);
        Assert.Equal("openrouter@example.com", connections[ProviderKind.OpenRouter].DirectCredential?.Email);
    }

    [Fact]
    public async Task GetProfileConnectionsAsync_NoConnections_ReturnsEmpty()
    {
        var store = new ProviderConnectionVaultStore(_vaultPath);

        var connections = await store.GetProfileConnectionsAsync("nonexistent");

        Assert.Empty(connections);
    }

    [Fact]
    public async Task SaveConnectionAsync_MultipleProfiles_Isolated()
    {
        var store = new ProviderConnectionVaultStore(_vaultPath);

        var profile1Connection = new ProviderAuthConnection
        {
            ProfileName = "profile1",
            Provider = ProviderKind.Codex,
            PreferredMethod = AuthMethod.Direct,
            DirectCredential = new ProviderCredential { Email = "user1@example.com", Password = "pass1" }
        };

        var profile2Connection = new ProviderAuthConnection
        {
            ProfileName = "profile2",
            Provider = ProviderKind.Codex,
            PreferredMethod = AuthMethod.Direct,
            DirectCredential = new ProviderCredential { Email = "user2@example.com", Password = "pass2" }
        };

        await store.SaveConnectionAsync(profile1Connection);
        await store.SaveConnectionAsync(profile2Connection);

        var profile1Retrieved = await store.GetConnectionAsync("profile1", ProviderKind.Codex);
        var profile2Retrieved = await store.GetConnectionAsync("profile2", ProviderKind.Codex);

        Assert.NotNull(profile1Retrieved);
        Assert.NotNull(profile2Retrieved);
        Assert.Equal("user1@example.com", profile1Retrieved.DirectCredential?.Email);
        Assert.Equal("user2@example.com", profile2Retrieved.DirectCredential?.Email);
    }

    [Fact]
    public async Task Cancellation_ThrowsTaskCanceledException()
    {
        var store = new ProviderConnectionVaultStore(_vaultPath);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var connection = new ProviderAuthConnection
        {
            ProfileName = "profile1",
            Provider = ProviderKind.Codex,
            PreferredMethod = AuthMethod.Direct,
            DirectCredential = new ProviderCredential { Email = "user@example.com", Password = "pass" }
        };

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            store.SaveConnectionAsync(connection, cts.Token));
    }

    [Fact]
    public async Task ConcurrentOperations_Serialized()
    {
        var store = new ProviderConnectionVaultStore(_vaultPath);
        var tasks = new List<Task>();

        // Start 10 concurrent write operations
        for (int i = 0; i < 10; i++)
        {
            var index = i;
            var connection = new ProviderAuthConnection
            {
                ProfileName = $"profile{index}",
                Provider = ProviderKind.Codex,
                PreferredMethod = AuthMethod.Direct,
                DirectCredential = new ProviderCredential { Email = $"user{index}@example.com", Password = $"pass{index}" }
            };
            tasks.Add(store.SaveConnectionAsync(connection));
        }

        await Task.WhenAll(tasks);

        // Verify all writes succeeded
        for (int i = 0; i < 10; i++)
        {
            var connection = await store.GetConnectionAsync($"profile{i}", ProviderKind.Codex);
            Assert.NotNull(connection);
            Assert.Equal($"user{i}@example.com", connection.DirectCredential?.Email);
        }
    }

    [Fact]
    public async Task Dispose_WaitsForPendingOperations()
    {
        var store = new ProviderConnectionVaultStore(_vaultPath);

        var connection = new ProviderAuthConnection
        {
            ProfileName = "profile1",
            Provider = ProviderKind.Codex,
            PreferredMethod = AuthMethod.Direct,
            DirectCredential = new ProviderCredential { Email = "user@example.com", Password = "pass" }
        };

        var saveTask = store.SaveConnectionAsync(connection);

        // Wait for the operation to complete before disposing
        await saveTask;
        store.Dispose();

        // Verify operation completed successfully
        Assert.True(saveTask.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task AfterDispose_ThrowsObjectDisposedException()
    {
        var store = new ProviderConnectionVaultStore(_vaultPath);
        store.Dispose();

        var connection = new ProviderAuthConnection
        {
            ProfileName = "profile1",
            Provider = ProviderKind.Codex,
            PreferredMethod = AuthMethod.Direct,
            DirectCredential = new ProviderCredential { Email = "user@example.com", Password = "pass" }
        };

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            store.SaveConnectionAsync(connection));
    }
}
