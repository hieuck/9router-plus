using System;
using System.IO;
using System.Security.Cryptography;
using RouterPlus.Core.Storage;
using RouterPlus.Infrastructure.Security;
using RouterPlus.Infrastructure.Storage;
using Xunit;

namespace RouterPlus.Infrastructure.Tests.Storage;

public class DefaultAppDataIsolationTests
{
    [Fact]
    public async Task DefaultSettingsStore_Save_LandsUnderTestRoot()
    {
        var realPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "9RouterPlus", "settings.json");

        var before = ComputeHash(realPath);

        var store = new SettingsStore();
        await store.SaveAsync(new RouterSettings(DashboardBaseUrl: "http://localhost:20129"));

        Assert.True(File.Exists(AppPaths.SettingsPath));
        var root = Environment.GetEnvironmentVariable("ROUTERPLUS_TEST_ROOT");
        Assert.False(string.IsNullOrWhiteSpace(root));
        Assert.StartsWith(root!, AppPaths.SettingsPath, StringComparison.OrdinalIgnoreCase);

        var after = ComputeHash(realPath);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task DefaultDpapiSecretVault_Save_LandsUnderTestRoot()
    {
        var realPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "9RouterPlus", "secrets.json");

        var before = ComputeHash(realPath);

        var vault = new DpapiSecretVault();
        await vault.StoreAsync("test-key", "test-secret");

        Assert.True(File.Exists(AppPaths.SecretsPath));
        var root = Environment.GetEnvironmentVariable("ROUTERPLUS_TEST_ROOT");
        Assert.False(string.IsNullOrWhiteSpace(root));
        Assert.StartsWith(root!, AppPaths.SecretsPath, StringComparison.OrdinalIgnoreCase);

        var after = ComputeHash(realPath);
        Assert.Equal(before, after);
    }

    private static string? ComputeHash(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexString(hash);
    }
}