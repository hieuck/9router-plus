using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using Xunit;
using RouterPlus.Core.Chrome;
using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.Infrastructure.Tests.Chrome;

public sealed class ChromeLauncherTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "RouterPlusChromeLauncherTests",
        Guid.NewGuid().ToString("N"));

    public ChromeLauncherTests()
    {
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void Launch_builds_expected_process_start_info()
    {
        var installation = CreateInstallation();
        var profile = CreateProfile();
        ProcessStartInfo? capturedStartInfo = null;
        using var fakeProcess = new Process();
        var launcher = new ChromeLauncher(
            null,
            startInfo =>
            {
                capturedStartInfo = startInfo;
                return fakeProcess;
            },
            (Func<int>)(() => 0));

        var actualProcess = launcher.Launch(installation, profile, "https://example.test/login?state=abc");

        Assert.Same(fakeProcess, actualProcess);
        Assert.NotNull(capturedStartInfo);
        Assert.Equal(installation.ExecutablePath, capturedStartInfo!.FileName);
        Assert.False(capturedStartInfo.UseShellExecute);
        Assert.Equal(Path.GetDirectoryName(installation.ExecutablePath), capturedStartInfo.WorkingDirectory);
        Assert.Equal(
            new[]
            {
                $"--user-data-dir={installation.UserDataDirectory}",
                $"--profile-directory={profile.DirectoryName}",
                "https://example.test/login?state=abc"
            },
            capturedStartInfo.ArgumentList);
    }

    [Fact]
    public void Launch_throws_when_process_launcher_returns_null()
    {
        var installation = CreateInstallation();
        var profile = CreateProfile();
        var launcher = new ChromeLauncher(null, _ => null, (Func<int>)(() => 0));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            launcher.Launch(installation, profile, "https://example.test"));

        Assert.Equal("Chrome did not start.", exception.Message);
    }

    [Fact]
    public async Task LaunchManagedAsync_isolated_profile_returns_session_and_preserves_uri_fragment()
    {
        var installation = CreateInstallation();
        var profile = CreateProfile();
        ProcessStartInfo? capturedStartInfo = null;
        var launcher = new ChromeLauncher(
            null,
            startInfo =>
            {
                capturedStartInfo = startInfo;
                return StartHarmlessCompletedProcess();
            },
            (process, port, sessionMarker, _, _, _) => Task.FromResult(
                new ChromeManagedSession(
                    process,
                    new Uri($"http://127.0.0.1:{port}"),
                    sessionMarker)));

        string temporaryUserDataDirectory;
        await using (var session = await launcher.LaunchManagedAsync(
            installation,
            profile,
            new Uri("https://example.test/login?state=abc#device-user-code"),
            CancellationToken.None))
        {
            Assert.NotNull(capturedStartInfo);
            var markedUri = new Uri(capturedStartInfo!.ArgumentList[^1]);
            Assert.Equal("abc", markedUri.Query.TrimStart('?').Split('=')[1].Split('&')[0]);
            Assert.StartsWith("?state=abc&__9rp_session=", markedUri.Query, StringComparison.Ordinal);
            Assert.Equal("#device-user-code", markedUri.Fragment);
            Assert.Equal("127.0.0.1", session.DevToolsBaseUri.Host);
            temporaryUserDataDirectory = capturedStartInfo.ArgumentList
                .Single(argument => argument.StartsWith("--user-data-dir=", StringComparison.Ordinal))
                .Substring("--user-data-dir=".Length);
        }

        Assert.False(Directory.Exists(temporaryUserDataDirectory));
    }

    [Fact]
    public async Task LaunchManagedAsync_uses_default_isolated_profile_and_copies_authentication_data()
    {
        var installation = CreateInstallation();
        var profile = CreateProfile();
        // Write dummy auth data to source directories.
        File.WriteAllText(Path.Combine(installation.UserDataDirectory, "Local State"), "local-state");
        foreach (var (name, content) in new[]
        {
            ("Preferences", "preferences"),
            ("Secure Preferences", "secure-preferences"),
            ("Cookies", "cookies"),
            ("Login Data", "login-data"),
            ("Web Data", "web-data")
        })
        {
            File.WriteAllText(Path.Combine(profile.ProfilePath, name), content);
        }
        Directory.CreateDirectory(Path.Combine(profile.ProfilePath, "Network"));
        File.WriteAllText(Path.Combine(profile.ProfilePath, "Network", "Cookies"), "network-cookies");

        ProcessStartInfo? capturedStartInfo = null;
        var launcher = new ChromeLauncher(
            null,
            startInfo =>
            {
                capturedStartInfo = startInfo;
                var tempDir = startInfo.ArgumentList.Single(arg => arg.StartsWith("--user-data-dir=")).Substring("--user-data-dir=".Length);
                // Verify that auth data were copied.
                Assert.Equal("local-state", File.ReadAllText(Path.Combine(tempDir, "Local State")));
                Assert.Equal("preferences", File.ReadAllText(Path.Combine(tempDir, profile.DirectoryName, "Preferences")));
                Assert.Equal("secure-preferences", File.ReadAllText(Path.Combine(tempDir, profile.DirectoryName, "Secure Preferences")));
                Assert.Equal("cookies", File.ReadAllText(Path.Combine(tempDir, profile.DirectoryName, "Cookies")));
                Assert.Equal("login-data", File.ReadAllText(Path.Combine(tempDir, profile.DirectoryName, "Login Data")));
                Assert.Equal("web-data", File.ReadAllText(Path.Combine(tempDir, profile.DirectoryName, "Web Data")));
                Assert.Equal("network-cookies", File.ReadAllText(Path.Combine(tempDir, profile.DirectoryName, "Network", "Cookies")));
                return null; // Simulate process start failure.
            },
            (process, port, marker, _, _, _) => Task.FromResult(new ChromeManagedSession(process, new Uri($"http://127.0.0.1:{port}"), marker)));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            launcher.LaunchManagedAsync(
                installation,
                profile,
                new Uri("https://example.test"),
                CancellationToken.None));

        Assert.Equal("Chrome did not start.", exception.Message);
        Assert.NotNull(capturedStartInfo);
        var tempDirPath = capturedStartInfo!.ArgumentList.Single(arg => arg.StartsWith("--user-data-dir=")).Substring("--user-data-dir=".Length);
        Assert.False(Directory.Exists(tempDirPath));
    }

    [Fact]
    public async Task LaunchManagedAsync_isolated_profile_copies_authentication_data_and_cleans_up_when_process_does_not_start()
    {
        var installation = CreateInstallation();
        var profile = CreateProfile();
        File.WriteAllText(Path.Combine(installation.UserDataDirectory, "Local State"), "local-state");
        foreach (var (name, content) in new[]
        {
            ("Preferences", "preferences"),
            ("Secure Preferences", "secure-preferences"),
            ("Cookies", "cookies"),
            ("Login Data", "login-data"),
            ("Web Data", "web-data")
        })
        {
            File.WriteAllText(Path.Combine(profile.ProfilePath, name), content);
        }
        Directory.CreateDirectory(Path.Combine(profile.ProfilePath, "Network"));
        File.WriteAllText(Path.Combine(profile.ProfilePath, "Network", "Cookies"), "network-cookies");
        ProcessStartInfo? capturedStartInfo = null;
        var launcher = new ChromeLauncher(
            null,
            startInfo =>
            {
                capturedStartInfo = startInfo;
                var userDataDirectory = startInfo.ArgumentList
                    .Single(argument => argument.StartsWith("--user-data-dir=", StringComparison.Ordinal))
                    .Substring("--user-data-dir=".Length);

                Assert.NotEqual(installation.UserDataDirectory, userDataDirectory);
                Assert.Equal("local-state", File.ReadAllText(Path.Combine(userDataDirectory, "Local State")));
                Assert.Equal("preferences", File.ReadAllText(Path.Combine(userDataDirectory, profile.DirectoryName, "Preferences")));
                Assert.Equal("secure-preferences", File.ReadAllText(Path.Combine(userDataDirectory, profile.DirectoryName, "Secure Preferences")));
                Assert.Equal("cookies", File.ReadAllText(Path.Combine(userDataDirectory, profile.DirectoryName, "Cookies")));
                Assert.Equal("login-data", File.ReadAllText(Path.Combine(userDataDirectory, profile.DirectoryName, "Login Data")));
                Assert.Equal("web-data", File.ReadAllText(Path.Combine(userDataDirectory, profile.DirectoryName, "Web Data")));
                Assert.Equal("network-cookies", File.ReadAllText(Path.Combine(userDataDirectory, profile.DirectoryName, "Network", "Cookies")));
                return null;
            },
            (Func<Process, int, string, TimeSpan, Func<string, CancellationToken, Task<string>>, CancellationToken, Task<ChromeManagedSession>>)(
                (_, _, _, _, _, _) => throw new InvalidOperationException("session creation failed")));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            launcher.LaunchManagedAsync(
                installation,
                profile,
                new Uri("https://example.test/login#device-user-code"),
                CancellationToken.None));

        Assert.Equal("Chrome did not start.", exception.Message);
        Assert.NotNull(capturedStartInfo);
        var temporaryUserDataDirectory = capturedStartInfo!.ArgumentList
            .Single(argument => argument.StartsWith("--user-data-dir=", StringComparison.Ordinal))
            .Substring("--user-data-dir=".Length);
        Assert.False(Directory.Exists(temporaryUserDataDirectory));
    }

    [Fact]
    public async Task LaunchManagedAsync_uses_original_profile_and_does_not_create_temp_directory()
    {
        var installation = CreateInstallation();
        var profile = CreateProfile();
        ProcessStartInfo? capturedStartInfo = null;
        var launcher = new ChromeLauncher(
            null,
            startInfo =>
            {
                capturedStartInfo = startInfo;
                return StartHarmlessCompletedProcess();
            },
            (process, port, marker, _, _, _) => Task.FromResult(new ChromeManagedSession(process, new Uri($"http://127.0.0.1:{port}"), marker)));

        await using (var session = await launcher.LaunchManagedAsync(
            installation,
            profile,
            new Uri("https://example.test"),
            CancellationToken.None,
            useOriginalProfile: true))
        {
            // Verify that the user-data-dir argument points to the original user data directory.
            Assert.NotNull(capturedStartInfo);
            var userDataArg = capturedStartInfo!.ArgumentList.Single(arg => arg.StartsWith("--user-data-dir="));
            var dir = userDataArg.Substring("--user-data-dir=".Length);
            Assert.Equal(installation.UserDataDirectory, dir);
        }
        // No temporary directory should have been created.
        var tempArgExists = capturedStartInfo!.ArgumentList.Any(arg => arg.StartsWith("--user-data-dir=") && arg.Contains("routerplus_chrome_"));
        Assert.False(tempArgExists);
    }

    private Process StartHarmlessCompletedProcess()
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c exit 0",
            UseShellExecute = false,
            CreateNoWindow = true
        });

        Assert.NotNull(process);
        process.WaitForExit();
        return process;
    }

    private ChromeInstallation CreateInstallation()
    {
        var executablePath = Path.Combine(_tempDirectory, "chrome.exe");
        File.WriteAllText(executablePath, "test executable placeholder");
        return new ChromeInstallation(executablePath, _tempDirectory);
    }

    private ChromeProfile CreateProfile()
    {
        var profileDirectory = Path.Combine(_tempDirectory, "Profile 1");
        Directory.CreateDirectory(profileDirectory);
        return new ChromeProfile(
            "profile-id",
            "Test Profile",
            "Profile 1",
            _tempDirectory,
            false);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
