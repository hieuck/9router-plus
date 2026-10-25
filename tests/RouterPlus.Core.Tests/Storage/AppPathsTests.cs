using System;
using System.IO;
using RouterPlus.Core.Storage;
using Xunit;

namespace RouterPlus.Core.Tests.Storage;

public class AppPathsTests
{
    [Fact]
    public void UsesEnvironmentOverride()
    {
        var original = Environment.GetEnvironmentVariable("ROUTERPLUS_TEST_ROOT");
        try
        {
            var temp = Path.Combine(Path.GetTempPath(), "routerplus-test-root-" + Guid.NewGuid().ToString("N"));
            Environment.SetEnvironmentVariable("ROUTERPLUS_TEST_ROOT", temp);

            Assert.Equal(temp, AppPaths.LocalAppDataRoot);
            Assert.Equal(Path.Combine(temp, "settings.json"), AppPaths.SettingsPath);
            Assert.Equal(Path.Combine(temp, "secrets.json"), AppPaths.SecretsPath);
            Assert.Equal(Path.Combine(temp, "google-accounts.vault"), AppPaths.GoogleAccountsVaultPath);
            Assert.Equal(Path.Combine(temp, "provider-connections.vault"), AppPaths.ProviderConnectionsVaultPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ROUTERPLUS_TEST_ROOT", original);
        }
    }

    [Fact]
    public void DefaultWhenUnset()
    {
        var original = Environment.GetEnvironmentVariable("ROUTERPLUS_TEST_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("ROUTERPLUS_TEST_ROOT", null);
            var expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "9RouterPlus");

            Assert.Equal(expected, AppPaths.LocalAppDataRoot);
            Assert.Equal(Path.Combine(expected, "settings.json"), AppPaths.SettingsPath);
            Assert.Equal(Path.Combine(expected, "secrets.json"), AppPaths.SecretsPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ROUTERPLUS_TEST_ROOT", original);
        }
    }
}