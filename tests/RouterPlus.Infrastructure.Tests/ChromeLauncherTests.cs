using System.Diagnostics;
using RouterPlus.Core.Chrome;
using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.Infrastructure.Tests;

public sealed class ChromeLauncherTests
{
    [Fact]
    public void Launch_builds_expected_process_start_info_without_starting_chrome()
    {
        // Arrange
        using var temp = new TestDirectory();
        var executablePath = temp.CreateFile("chrome.exe");
        var userDataDirectory = temp.CreateDirectory("User Data");
        var profile = CreateProfile(userDataDirectory, "Profile 1");
        var installation = new ChromeInstallation(executablePath, userDataDirectory);
        var startUri = "https://example.test/login?source=test";
        ProcessStartInfo? capturedStartInfo = null;
        using var fakeProcess = new Process();
        var launcher = new ChromeLauncher(
            (_, _) => Task.FromResult(string.Empty),
            startInfo =>
            {
                capturedStartInfo = startInfo;
                return fakeProcess;
            },
            (_, _, _, _, _, _) => throw new InvalidOperationException("Not used by Launch."));

        // Act
        var process = launcher.Launch(installation, profile, startUri);

        // Assert
        Assert.Same(fakeProcess, process);
        Assert.NotNull(capturedStartInfo);
        Assert.Equal(executablePath, capturedStartInfo!.FileName);
        Assert.False(capturedStartInfo.UseShellExecute);
        Assert.Equal(temp.Path, capturedStartInfo.WorkingDirectory);
        Assert.Equal(
            new[]
            {
                $"--user-data-dir={userDataDirectory}",
                "--profile-directory=Profile 1",
                startUri
            },
            capturedStartInfo.ArgumentList);
    }

    [Fact]
    public void Launch_throws_when_process_start_returns_null()
    {
        // Arrange
        using var temp = new TestDirectory();
        var installation = new ChromeInstallation(
            temp.CreateFile("chrome.exe"),
            temp.CreateDirectory("User Data"));
        var profile = CreateProfile(installation.UserDataDirectory, "Profile 1");
        var launcher = new ChromeLauncher(
            (_, _) => Task.FromResult(string.Empty),
            _ => null);

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() =>
            launcher.Launch(installation, profile, "https://example.test"));

        // Assert
        Assert.Equal("Chrome did not start.", exception.Message);
    }

    [Fact]
    public async Task LaunchManagedAsync_builds_isolated_profile_and_preserves_url_fragment()
    {
        // Arrange
        using var temp = new TestDirectory();
        var executablePath = temp.CreateFile("chrome.exe");
        var userDataDirectory = temp.CreateDirectory("User Data");
        var profile = CreateProfile(userDataDirectory, "Profile 1");
        var installation = new ChromeInstallation(executablePath, userDataDirectory);
        var sourceProfileDirectory = profile.ProfilePath;
        var expectedFiles = new[]
        {
            (Path.Combine(userDataDirectory, "Local State"), "local-state"),
            (Path.Combine(sourceProfileDirectory, "Preferences"), "preferences"),
            (Path.Combine(sourceProfileDirectory, "Secure Preferences"), "secure-preferences"),
            (Path.Combine(sourceProfileDirectory, "Cookies"), "cookies"),
            (Path.Combine(sourceProfileDirectory, "Login Data"), "login-data"),
            (Path.Combine(sourceProfileDirectory, "Web Data"), "web-data"),
            (Path.Combine(sourceProfileDirectory, "Network", "Cookies"), "network-cookies")
        };
        foreach (var (path, contents) in expectedFiles)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
        }

        ProcessStartInfo? capturedStartInfo = null;
        string? capturedMarker = null;
        using var fakeProcess = new Process();
        var fakeSession = new ChromeManagedSession(
            fakeProcess,
            new Uri("http://127.0.0.1:9222"),
            "test-marker");
        var launcher = new ChromeLauncher(
            (_, _) => Task.FromResult(string.Empty),
            startInfo =>
            {
                capturedStartInfo = startInfo;
                return fakeProcess;
            },
            (process, _, marker, _, _, _) =>
            {
                capturedMarker = marker;
                return Task.FromResult(fakeSession);
            });

        // Act
        var isolatedUserDataDirectory = string.Empty;
        try
        {
            var session = await launcher.LaunchManagedAsync(
                installation,
                profile,
                new Uri("https://example.test/login?source=test#device-user-code"),
                CancellationToken.None);

            // Assert
            Assert.Same(fakeSession, session);
            Assert.NotNull(capturedStartInfo);
            Assert.NotNull(capturedMarker);
            Assert.StartsWith("__9rp_session_", capturedMarker);
            Assert.Equal(executablePath, capturedStartInfo!.FileName);
            Assert.False(capturedStartInfo.UseShellExecute);
            Assert.Contains("--profile-directory=Profile 1", capturedStartInfo.ArgumentList);
            Assert.Contains("--remote-debugging-address=127.0.0.1", capturedStartInfo.ArgumentList);
            Assert.Contains("--no-first-run", capturedStartInfo.ArgumentList);
            Assert.Contains("--new-window", capturedStartInfo.ArgumentList);

            var markedUrl = capturedStartInfo.ArgumentList[^1];
            Assert.Contains("source=test&__9rp_session=", markedUrl);
            Assert.EndsWith("#device-user-code", markedUrl);

            isolatedUserDataDirectory = capturedStartInfo.ArgumentList
                .Single(argument => argument.StartsWith("--user-data-dir=", StringComparison.Ordinal))
                .Split('=', 2)[1];
            Assert.NotEqual(userDataDirectory, isolatedUserDataDirectory);
            Assert.True(File.Exists(Path.Combine(isolatedUserDataDirectory, "Local State")));
            Assert.Equal("local-state", File.ReadAllText(Path.Combine(isolatedUserDataDirectory, "Local State")));
            Assert.Equal(
                "network-cookies",
                File.ReadAllText(Path.Combine(isolatedUserDataDirectory, "Profile 1", "Network", "Cookies")));
        }
        finally
        {
            if (Directory.Exists(isolatedUserDataDirectory))
            {
                Directory.Delete(isolatedUserDataDirectory, recursive: true);
            }
        }

        Assert.False(Directory.Exists(isolatedUserDataDirectory));
    }

    [Fact]
    public async Task LaunchManagedAsync_validates_missing_executable_before_starting_process()
    {
        // Arrange
        using var temp = new TestDirectory();
        var userDataDirectory = temp.CreateDirectory("User Data");
        var profile = CreateProfile(userDataDirectory, "Profile 1");
        var launcher = new ChromeLauncher();

        // Act
        var exception = await Assert.ThrowsAsync<FileNotFoundException>(() =>
            launcher.LaunchManagedAsync(
                new ChromeInstallation(Path.Combine(temp.Path, "missing.exe"), userDataDirectory),
                profile,
                new Uri("https://example.test"),
                CancellationToken.None));

        // Assert
        Assert.Contains("Chrome executable was not found", exception.Message);
    }

    [Fact]
    public void Launch_validates_null_and_blank_arguments()
    {
        // Arrange
        using var temp = new TestDirectory();
        var installation = new ChromeInstallation(temp.CreateFile("chrome.exe"), temp.CreateDirectory("User Data"));
        var profile = CreateProfile(installation.UserDataDirectory, "Profile 1");
        var launcher = new ChromeLauncher();

        // Act / Assert
        Assert.Throws<ArgumentNullException>(() => launcher.Launch(null!, profile, "https://example.test"));
        Assert.Throws<ArgumentNullException>(() => launcher.Launch(installation, null!, "https://example.test"));
        Assert.Throws<ArgumentException>(() => launcher.Launch(installation, profile, " "));
    }

    private static ChromeProfile CreateProfile(string userDataDirectory, string directoryName)
    {
        var profilePath = Path.Combine(userDataDirectory, directoryName);
        Directory.CreateDirectory(profilePath);
        return new ChromeProfile("profile-id", directoryName, directoryName, userDataDirectory, false);
    }

    private sealed class TestDirectory : IDisposable
    {
        public TestDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "routerplus-chrome-launcher-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string CreateDirectory(string name)
        {
            var directory = System.IO.Path.Combine(Path, name);
            Directory.CreateDirectory(directory);
            return directory;
        }

        public string CreateFile(string name)
        {
            var file = System.IO.Path.Combine(Path, name);
            File.WriteAllText(file, string.Empty);
            return file;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
