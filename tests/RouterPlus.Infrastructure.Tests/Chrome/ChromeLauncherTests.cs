using System.Diagnostics;
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
        // Arrange
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

        // Act
        var actualProcess = launcher.Launch(installation, profile, "https://example.test/login?state=abc");

        // Assert
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
        // Arrange
        var installation = CreateInstallation();
        var profile = CreateProfile();
        var launcher = new ChromeLauncher(null, _ => null, (Func<int>)(() => 0));

        // Act
        var exception = Assert.Throws<InvalidOperationException>(() =>
            launcher.Launch(installation, profile, "https://example.test"));

        // Assert
        Assert.Equal("Chrome did not start.", exception.Message);
    }

    [Fact]
    public async Task LaunchManagedAsync_isolated_profile_returns_session_and_preserves_uri_fragment()
    {
        // Arrange
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

        // Act
        string temporaryUserDataDirectory;
        await using (var session = await launcher.LaunchManagedAsync(
            installation,
            profile,
            new Uri("https://example.test/login?state=abc#device-user-code"),
            CancellationToken.None))
        {
            // Assert
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
    public async Task LaunchManagedAsync_deletes_isolated_profile_when_session_creation_fails()
    {
        // Arrange
        var installation = CreateInstallation();
        var profile = CreateProfile();
        string? temporaryUserDataDirectory = null;
        var launcher = new ChromeLauncher(
            null,
            startInfo =>
            {
                temporaryUserDataDirectory = startInfo.ArgumentList
                    .Single(argument => argument.StartsWith("--user-data-dir=", StringComparison.Ordinal))
                    .Substring("--user-data-dir=".Length);
                return StartHarmlessCompletedProcess();
            },
            (Func<Process, int, string, TimeSpan, Func<string, CancellationToken, Task<string>>, CancellationToken, Task<ChromeManagedSession>>)(
                (_, _, _, _, _, _) => throw new InvalidOperationException("session creation failed")));

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            launcher.LaunchManagedAsync(
                installation,
                profile,
                new Uri("https://example.test"),
                CancellationToken.None));

        // Assert
        Assert.Equal("session creation failed", exception.Message);
        Assert.NotNull(temporaryUserDataDirectory);
        Assert.False(Directory.Exists(temporaryUserDataDirectory));
    }

    [Fact]
    public async Task LaunchManagedAsync_isolated_profile_copies_authentication_data_and_cleans_up_when_process_does_not_start()
    {
        // Arrange
        var installation = CreateInstallation();
        var profile = CreateProfile();
        File.WriteAllText(Path.Combine(installation.UserDataDirectory, "Local State"), "local-state");
        File.WriteAllText(Path.Combine(profile.ProfilePath, "Cookies"), "cookies");
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
                Assert.Equal("cookies", File.ReadAllText(Path.Combine(userDataDirectory, profile.DirectoryName, "Cookies")));
                Assert.Equal(
                    "network-cookies",
                    File.ReadAllText(Path.Combine(userDataDirectory, profile.DirectoryName, "Network", "Cookies")));
                return null;
            },
            (Func<Process, int, string, TimeSpan, Func<string, CancellationToken, Task<string>>, CancellationToken, Task<ChromeManagedSession>>)(
                (_, _, _, _, _, _) => throw new InvalidOperationException("session creation failed")));

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            launcher.LaunchManagedAsync(
                installation,
                profile,
                new Uri("https://example.test/login#device-user-code"),
                CancellationToken.None));

        // Assert
        Assert.Equal("Chrome did not start.", exception.Message);
        Assert.NotNull(capturedStartInfo);
        var temporaryUserDataDirectory = capturedStartInfo!.ArgumentList
            .Single(argument => argument.StartsWith("--user-data-dir=", StringComparison.Ordinal))
            .Substring("--user-data-dir=".Length);
        Assert.False(Directory.Exists(temporaryUserDataDirectory));
    }

    private static Process StartHarmlessCompletedProcess()
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
