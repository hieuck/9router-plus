using System;
using System.IO;
using RouterPlus.Core.Storage;
using Xunit;

namespace RouterPlus.Core.Tests.Storage;

[Collection("AppPathsEnv")]
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
            Assert.Equal(Path.Combine(temp, "Observability"), AppPaths.ObservabilityRoot);
            Assert.Equal(Path.Combine(temp, "google-login-diag.log"), AppPaths.DiagnosticLogPath);
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

            var expectedObs = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RouterPlus",
                "Observability");
            Assert.Equal(expectedObs, AppPaths.ObservabilityRoot);

            var expectedDiag = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RouterPlus",
                "google-login-diag.log");
            Assert.Equal(expectedDiag, AppPaths.DiagnosticLogPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ROUTERPLUS_TEST_ROOT", original);
        }
    }

    [Theory]
    [InlineData("relative/path")]
    [InlineData("..\\escape")]
    [InlineData("C:\\temp\\..\\escape")]
    public void InvalidOverrideIsIgnored(string value)
    {
        var original = Environment.GetEnvironmentVariable("ROUTERPLUS_TEST_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("ROUTERPLUS_TEST_ROOT", value);

            var expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "9RouterPlus");
            Assert.Equal(expected, AppPaths.LocalAppDataRoot);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ROUTERPLUS_TEST_ROOT", original);
        }
    }

    [Fact]
    public void AbsoluteOverrideIsNormalized()
    {
        var original = Environment.GetEnvironmentVariable("ROUTERPLUS_TEST_ROOT");
        try
        {
            var temp = Path.Combine(Path.GetTempPath(), "routerplus-test-root-" + Guid.NewGuid().ToString("N"));
            var nested = Path.Combine(temp, "sub", "..", "sub");
            Environment.SetEnvironmentVariable("ROUTERPLUS_TEST_ROOT", nested);

            // nested contains '..' segment, so it must be rejected.
            var expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "9RouterPlus");
            Assert.Equal(expected, AppPaths.LocalAppDataRoot);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ROUTERPLUS_TEST_ROOT", original);
        }
    }

    [Fact]
    public void AbsoluteNormalizedOverrideAccepted()
    {
        var original = Environment.GetEnvironmentVariable("ROUTERPLUS_TEST_ROOT");
        var temp = Path.Combine(Path.GetTempPath(), "routerplus-test-root-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);
            Environment.SetEnvironmentVariable("ROUTERPLUS_TEST_ROOT", temp + "\\");

            Assert.Equal(Path.GetFullPath(temp + "\\"), AppPaths.LocalAppDataRoot);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ROUTERPLUS_TEST_ROOT", original);
            try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }
}

[CollectionDefinition("AppPathsEnv", DisableParallelization = true)]
public class AppPathsEnvCollection { }