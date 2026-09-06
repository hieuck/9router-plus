using System.Runtime.InteropServices;
using RouterPlus.Infrastructure.Security;

namespace RouterPlus.Infrastructure.Tests.Security;

public sealed class DpapiSecretVaultTests : IDisposable
{
    private readonly string _testDirectory;
    private readonly string _testFilePath;

    public DpapiSecretVaultTests()
    {
        _testDirectory = Path.Combine(Path.GetTempPath(), $"DpapiVaultTests_{Guid.NewGuid():N}");
        _testFilePath = Path.Combine(_testDirectory, "secrets.json");
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task StoreAsync_ThenReadAsync_ReturnsOriginalValue()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // DPAPI only works on Windows
            return;
        }

        var vault = new DpapiSecretVault(_testFilePath);
        const string key = "test-key";
        const string secret = "my-secret-value";

        await vault.StoreAsync(key, secret);
        var result = await vault.ReadAsync(key);

        Assert.Equal(secret, result);
    }

    [Fact]
    public async Task ReadAsync_NonExistentKey_ReturnsNull()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var vault = new DpapiSecretVault(_testFilePath);

        var result = await vault.ReadAsync("nonexistent");

        Assert.Null(result);
    }

    [Fact]
    public async Task RemoveAsync_ExistingKey_RemovesValue()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var vault = new DpapiSecretVault(_testFilePath);
        const string key = "remove-test";
        await vault.StoreAsync(key, "secret");

        await vault.RemoveAsync(key);
        var result = await vault.ReadAsync(key);

        Assert.Null(result);
    }

    [Fact]
    public async Task RemoveAsync_NonExistentKey_DoesNotThrow()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var vault = new DpapiSecretVault(_testFilePath);

        await vault.RemoveAsync("nonexistent");

        // Should complete without exception
    }

    [Fact]
    public async Task StoreAsync_OverwritesExistingValue()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var vault = new DpapiSecretVault(_testFilePath);
        const string key = "overwrite-key";

        await vault.StoreAsync(key, "first-value");
        await vault.StoreAsync(key, "second-value");
        var result = await vault.ReadAsync(key);

        Assert.Equal("second-value", result);
    }

    [Fact]
    public async Task StoreAsync_MultipleKeys_AllAccessible()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var vault = new DpapiSecretVault(_testFilePath);

        await vault.StoreAsync("key1", "value1");
        await vault.StoreAsync("key2", "value2");
        await vault.StoreAsync("key3", "value3");

        Assert.Equal("value1", await vault.ReadAsync("key1"));
        Assert.Equal("value2", await vault.ReadAsync("key2"));
        Assert.Equal("value3", await vault.ReadAsync("key3"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ReadAsync_InvalidKey_ThrowsArgumentException(string invalidKey)
    {
        var vault = new DpapiSecretVault(_testFilePath);

        await Assert.ThrowsAsync<ArgumentException>(() => vault.ReadAsync(invalidKey));
    }

    [Fact]
    public async Task ReadAsync_NullKey_ThrowsArgumentNullException()
    {
        var vault = new DpapiSecretVault(_testFilePath);

        await Assert.ThrowsAsync<ArgumentNullException>(() => vault.ReadAsync(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task StoreAsync_InvalidKey_ThrowsArgumentException(string invalidKey)
    {
        var vault = new DpapiSecretVault(_testFilePath);

        await Assert.ThrowsAsync<ArgumentException>(() => vault.StoreAsync(invalidKey, "secret"));
    }

    [Fact]
    public async Task StoreAsync_NullKey_ThrowsArgumentNullException()
    {
        var vault = new DpapiSecretVault(_testFilePath);

        await Assert.ThrowsAsync<ArgumentNullException>(() => vault.StoreAsync(null!, "secret"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task StoreAsync_InvalidSecret_ThrowsArgumentException(string invalidSecret)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var vault = new DpapiSecretVault(_testFilePath);

        await Assert.ThrowsAsync<ArgumentException>(() => vault.StoreAsync("key", invalidSecret));
    }

    [Fact]
    public async Task StoreAsync_NullSecret_ThrowsArgumentNullException()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var vault = new DpapiSecretVault(_testFilePath);

        await Assert.ThrowsAsync<ArgumentNullException>(() => vault.StoreAsync("key", null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RemoveAsync_InvalidKey_ThrowsArgumentException(string invalidKey)
    {
        var vault = new DpapiSecretVault(_testFilePath);

        await Assert.ThrowsAsync<ArgumentException>(() => vault.RemoveAsync(invalidKey));
    }

    [Fact]
    public async Task RemoveAsync_NullKey_ThrowsArgumentNullException()
    {
        var vault = new DpapiSecretVault(_testFilePath);

        await Assert.ThrowsAsync<ArgumentNullException>(() => vault.RemoveAsync(null!));
    }

    [Fact]
    public async Task StoreAsync_CreatesDirectoryIfNotExists()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var nestedPath = Path.Combine(_testDirectory, "nested", "deep", "secrets.json");
        var vault = new DpapiSecretVault(nestedPath);

        await vault.StoreAsync("key", "value");

        Assert.True(File.Exists(nestedPath));
    }

    [Fact]
    public async Task ConcurrentOperations_Serialized()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var vault = new DpapiSecretVault(_testFilePath);
        var tasks = new List<Task>();

        // Start 10 concurrent write operations
        for (int i = 0; i < 10; i++)
        {
            var index = i;
            tasks.Add(vault.StoreAsync($"key{index}", $"value{index}"));
        }

        await Task.WhenAll(tasks);

        // Verify all writes succeeded
        for (int i = 0; i < 10; i++)
        {
            var result = await vault.ReadAsync($"key{i}");
            Assert.Equal($"value{i}", result);
        }
    }

    [Fact]
    public async Task Cancellation_ThrowsTaskCanceledException()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var vault = new DpapiSecretVault(_testFilePath);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            vault.StoreAsync("key", "value", cts.Token));
    }

    [Fact]
    public async Task MultipleVaults_SamePath_ShareProcessWideGate()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var vault1 = new DpapiSecretVault(_testFilePath);
        var vault2 = new DpapiSecretVault(_testFilePath);

        await vault1.StoreAsync("key1", "value1");
        await vault2.StoreAsync("key2", "value2");

        // Both vaults should see both keys (they share the same file)
        Assert.Equal("value1", await vault1.ReadAsync("key1"));
        Assert.Equal("value2", await vault1.ReadAsync("key2"));
        Assert.Equal("value1", await vault2.ReadAsync("key1"));
        Assert.Equal("value2", await vault2.ReadAsync("key2"));
    }

    [Fact]
    public async Task StoreAsync_SpecialCharacters_HandledCorrectly()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var vault = new DpapiSecretVault(_testFilePath);
        const string key = "special-key";
        const string secret = "Secret with special chars: 你好 🔐 \r\n\t\\\"'";

        await vault.StoreAsync(key, secret);
        var result = await vault.ReadAsync(key);

        Assert.Equal(secret, result);
    }

    [Fact]
    public async Task StoreAsync_LargeValue_HandledCorrectly()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        var vault = new DpapiSecretVault(_testFilePath);
        const string key = "large-key";
        var largeSecret = new string('x', 10000); // 10KB secret

        await vault.StoreAsync(key, largeSecret);
        var result = await vault.ReadAsync(key);

        Assert.Equal(largeSecret, result);
    }
}
