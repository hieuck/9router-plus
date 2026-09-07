using System.Diagnostics;
using RouterPlus.Core.Chrome;
using RouterPlus.Infrastructure.Chrome;
using Xunit;

namespace RouterPlus.Infrastructure.Tests.Chrome;

public sealed class ChromeManagedSessionTests
{
    [Fact]
    public async Task CreateAsync_returns_session_when_endpoint_becomes_available()
    {
        // Arrange
        using var process = StartLongRunningProcess();
        var requestedUrl = string.Empty;

        // Act
        await using var session = await ChromeManagedSession.CreateAsync(
            process,
            9222,
            "marker",
            TimeSpan.FromSeconds(1),
            (url, _) =>
            {
                requestedUrl = url;
                return Task.FromResult("{\"webSocketDebuggerUrl\":\"ws://127.0.0.1:9222/devtools/browser/test\"}");
            },
            CancellationToken.None);

        // Assert
        Assert.Equal("http://127.0.0.1:9222/json/version", requestedUrl);
        Assert.Equal(new Uri("http://127.0.0.1:9222"), session.DevToolsBaseUri);
        Assert.Same(process, session.Process);
    }

    [Fact]
    public async Task CreateAsync_retries_transient_http_and_json_failures()
    {
        // Arrange
        using var process = StartLongRunningProcess();
        var attempts = 0;

        // Act
        await using var session = await ChromeManagedSession.CreateAsync(
            process,
            9222,
            "marker",
            TimeSpan.FromSeconds(2),
            (_, _) =>
            {
                attempts++;
                return attempts switch
                {
                    1 => Task.FromException<string>(new HttpRequestException("not ready")),
                    2 => Task.FromResult("not json"),
                    _ => Task.FromResult("{\"webSocketDebuggerUrl\":\"ws://localhost:9222/devtools/browser/test\"}")
                };
            },
            CancellationToken.None);

        // Assert
        Assert.Equal(3, attempts);
        Assert.Equal(new Uri("http://127.0.0.1:9222"), session.DevToolsBaseUri);
    }

    [Fact]
    public async Task CreateAsync_rejects_non_loopback_websocket_url()
    {
        // Arrange
        using var process = StartLongRunningProcess();

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ChromeManagedSession.CreateAsync(
                process,
                9222,
                "marker",
                TimeSpan.FromSeconds(1),
                (_, _) => Task.FromResult("{\"webSocketDebuggerUrl\":\"ws://192.0.2.1:9222/devtools/browser/test\"}"),
                CancellationToken.None));

        // Assert
        Assert.Contains("non-loopback", exception.Message);
    }

    [Fact]
    public async Task CreateAsync_reports_exit_code_zero_as_profile_lock()
    {
        // Arrange
        using var process = StartExitedProcess(0);

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ChromeManagedSession.CreateAsync(
                process,
                9222,
                "marker",
                TimeSpan.FromSeconds(1),
                (_, _) => throw new Xunit.Sdk.XunitException("HTTP should not be called"),
                CancellationToken.None));

        // Assert
        Assert.Contains("profile may already be open", exception.Message);
    }

    [Fact]
    public async Task CreateAsync_reports_nonzero_exit_code()
    {
        // Arrange
        using var process = StartExitedProcess(7);

        // Act
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ChromeManagedSession.CreateAsync(
                process,
                9222,
                "marker",
                TimeSpan.FromSeconds(1),
                (_, _) => throw new Xunit.Sdk.XunitException("HTTP should not be called"),
                CancellationToken.None));

        // Assert
        Assert.Contains("code 7", exception.Message);
    }

    [Fact]
    public async Task CreateAsync_times_out_when_endpoint_never_becomes_available()
    {
        // Arrange
        using var process = StartLongRunningProcess();

        // Act
        var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            ChromeManagedSession.CreateAsync(
                process,
                9222,
                "marker",
                TimeSpan.FromMilliseconds(1),
                (_, _) => Task.FromException<string>(new HttpRequestException("not ready")),
                CancellationToken.None));

        // Assert
        Assert.Contains("did not become available", exception.Message);
    }

    [Fact]
    public async Task DisposeAsync_kills_process_deletes_temp_directory_and_is_idempotent()
    {
        // Arrange
        using var process = StartLongRunningProcess();
        var tempDirectory = Directory.CreateTempSubdirectory("routerplus-managed-session-");
        var session = new ChromeManagedSession(process, new Uri("http://127.0.0.1:9222"), "marker");
        session.SetTempUserDataDirectory(tempDirectory.FullName);

        // Act
        await session.DisposeAsync();
        await session.DisposeAsync();

        // Assert
        Assert.False(Directory.Exists(tempDirectory.FullName));
    }

    [Fact]
    public void AppendSessionMarker_preserves_query_and_fragment()
    {
        // Arrange
        var original = new Uri("https://example.test/path?existing=1#device-code");

        // Act
        var marked = ChromeLauncher.AppendSessionMarker(original, "marker value");

        // Assert
        Assert.Equal("https://example.test/path?existing=1&__9rp_session=marker value#device-code", marked.ToString());
        Assert.Contains("__9rp_session=marker%20value", marked.Query);
    }

    [Fact]
    public async Task LaunchManagedAsync_copies_authentication_data_into_isolated_profile()
    {
        // Arrange
        var root = Directory.CreateTempSubdirectory("routerplus-chrome-launcher-");
        try
        {
            var executablePath = Path.Combine(root.FullName, "chrome.exe");
            var userDataDirectory = Path.Combine(root.FullName, "User Data");
            var profileDirectory = Path.Combine(userDataDirectory, "Profile 1");
            var networkDirectory = Path.Combine(profileDirectory, "Network");
            Directory.CreateDirectory(networkDirectory);
            File.WriteAllText(executablePath, "fake chrome");
            File.WriteAllText(Path.Combine(userDataDirectory, "Local State"), "local-state");
            File.WriteAllText(Path.Combine(profileDirectory, "Preferences"), "preferences");
            File.WriteAllText(Path.Combine(profileDirectory, "Cookies"), "cookies");
            File.WriteAllText(Path.Combine(networkDirectory, "Cookies"), "network-cookies");

            var profile = new ChromeProfile("id", "Profile 1", "Profile 1", userDataDirectory, false);
            var capturedArguments = new List<string>();
            using var process = StartLongRunningProcess();
            var launcher = new ChromeLauncher(
                (_, _) => Task.FromResult("{\"webSocketDebuggerUrl\":\"ws://127.0.0.1:9222/devtools/browser/test\"}"),
                startInfo =>
                {
                    capturedArguments.AddRange(startInfo.ArgumentList);
                    return process;
                },
                () => 9222);

            // Act
            await using var session = await launcher.LaunchManagedAsync(
                new ChromeInstallation(executablePath, userDataDirectory),
                profile,
                new Uri("https://accounts.google.com/signin#fragment"),
                CancellationToken.None);

            // Assert
            var isolatedUserData = capturedArguments
                .Single(argument => argument.StartsWith("--user-data-dir=", StringComparison.Ordinal))
                .Split('=', 2)[1];
            Assert.NotEqual(userDataDirectory, isolatedUserData);
            Assert.Equal("local-state", File.ReadAllText(Path.Combine(isolatedUserData, "Local State")));
            Assert.Equal("preferences", File.ReadAllText(Path.Combine(isolatedUserData, "Profile 1", "Preferences")));
            Assert.Equal("cookies", File.ReadAllText(Path.Combine(isolatedUserData, "Profile 1", "Cookies")));
            Assert.Equal("network-cookies", File.ReadAllText(Path.Combine(isolatedUserData, "Profile 1", "Network", "Cookies")));
            Assert.Contains("__9rp_session=", capturedArguments.Single(argument => argument.StartsWith("https://", StringComparison.Ordinal)));

            await session.DisposeAsync();
            Assert.False(Directory.Exists(isolatedUserData));
        }
        finally
        {
            if (Directory.Exists(root.FullName))
            {
                Directory.Delete(root.FullName, recursive: true);
            }
        }
    }

    private static Process StartLongRunningProcess()
    {
        return Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c ping 127.0.0.1 -n 31 > nul",
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
    }

    private static Process StartExitedProcess(int exitCode)
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c exit {exitCode}",
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
        process.WaitForExit();
        return process;
    }
}
