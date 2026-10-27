using System.Diagnostics;
using System.Text.Json;
using RouterPlus.Core.Chrome;
using RouterPlus.Infrastructure.Chrome;
using RouterPlus.TestHelpers;

namespace RouterPlus.Infrastructure.Tests.Chrome;

public sealed class ChromeProfileDeleterTests
{
    [Fact]
    public async Task Delete_rejects_null_profile()
    {
        var userDataDirectory = TestDirectory.Create();

        try
        {
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await new ChromeProfileDeleter().DeleteAsync(null!, userDataDirectory));
        }
        finally
        {
            TestDirectory.Delete(userDataDirectory);
        }
    }

    [Fact]
    public async Task Delete_rejects_whitespace_user_data_directory()
    {
        var userDataDirectory = TestDirectory.Create();
        var profile = CreateProfile(userDataDirectory, "Profile 1");

        try
        {
            await Assert.ThrowsAsync<ArgumentException>(async () => await new ChromeProfileDeleter().DeleteAsync(profile, " "));
        }
        finally
        {
            TestDirectory.Delete(userDataDirectory);
        }
    }

    [Fact]
    public async Task Delete_rejects_profile_with_different_user_data_directory()
    {
        var userDataDirectory = TestDirectory.Create();
        var profileUserDataDirectory = TestDirectory.Create();
        var profileDirectory = Path.Combine(profileUserDataDirectory, "Profile 1");
        Directory.CreateDirectory(profileDirectory);

        try
        {
            var profile = CreateProfile(profileUserDataDirectory, "Profile 1");

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await
                new ChromeProfileDeleter().DeleteAsync(profile, userDataDirectory));

            Assert.Equal("The Chrome profile is outside the configured User Data directory.", exception.Message);
            Assert.True(Directory.Exists(profileDirectory));
        }
        finally
        {
            TestDirectory.Delete(userDataDirectory);
            TestDirectory.Delete(profileUserDataDirectory);
        }
    }

    [Fact]
    public async Task Delete_rejects_profile_that_is_not_an_immediate_child()
    {
        var userDataDirectory = TestDirectory.Create();
        var profileDirectory = Path.Combine(userDataDirectory, "Profiles", "Profile 1");
        Directory.CreateDirectory(profileDirectory);

        try
        {
            var profile = CreateProfile(userDataDirectory, Path.Combine("Profiles", "Profile 1"));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await
                new ChromeProfileDeleter().DeleteAsync(profile, userDataDirectory));

            Assert.Equal("The Chrome profile directory must be an immediate child of User Data.", exception.Message);
            Assert.True(Directory.Exists(profileDirectory));
        }
        finally
        {
            TestDirectory.Delete(userDataDirectory);
        }
    }

    [Fact]
    public async Task Delete_rejects_profile_path_that_is_a_file()
    {
        var userDataDirectory = TestDirectory.Create();
        var profilePath = Path.Combine(userDataDirectory, "Profile 1");
        File.WriteAllText(profilePath, string.Empty);

        try
        {
            var profile = CreateProfile(userDataDirectory, "Profile 1");

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () => await
                new ChromeProfileDeleter().DeleteAsync(profile, userDataDirectory));

            Assert.Equal("The Chrome profile path is not a directory.", exception.Message);
            Assert.True(File.Exists(profilePath));
        }
        finally
        {
            TestDirectory.Delete(userDataDirectory);
        }
    }

    [Fact]
    public async Task Delete_handles_null_local_state_document()
    {
        var userDataDirectory = TestDirectory.Create();
        var profileDirectory = Path.Combine(userDataDirectory, "Profile 1");
        var localStatePath = Path.Combine(userDataDirectory, "Local State");
        Directory.CreateDirectory(profileDirectory);
        File.WriteAllText(localStatePath, "null");

        try
        {
            await new ChromeProfileDeleter().DeleteAsync(CreateProfile(userDataDirectory, "Profile 1"), userDataDirectory);

            Assert.False(Directory.Exists(profileDirectory));
            Assert.Equal("null", File.ReadAllText(localStatePath));
        }
        finally
        {
            TestDirectory.Delete(userDataDirectory);
        }
    }

    [Fact]
    public async Task Delete_preserves_local_state_when_profile_metadata_is_missing()
    {
        var userDataDirectory = TestDirectory.Create();
        var profileDirectory = Path.Combine(userDataDirectory, "Profile 1");
        var localStatePath = Path.Combine(userDataDirectory, "Local State");
        var localStateJson = "{\"profile\":{\"info_cache\":{},\"profiles_order\":[],\"last_active_profiles\":[]}}";
        Directory.CreateDirectory(profileDirectory);
        File.WriteAllText(localStatePath, localStateJson);

        try
        {
            await new ChromeProfileDeleter().DeleteAsync(CreateProfile(userDataDirectory, "Profile 1"), userDataDirectory);

            Assert.False(Directory.Exists(profileDirectory));
            Assert.Equal(localStateJson, File.ReadAllText(localStatePath));
        }
        finally
        {
            TestDirectory.Delete(userDataDirectory);
        }
    }

    [Fact]
    public async Task Delete_removes_case_insensitive_duplicate_profile_entries_and_ignores_non_string_values()
    {
        var userDataDirectory = TestDirectory.Create();
        var profileDirectory = Path.Combine(userDataDirectory, "Profile 1");
        var localStatePath = Path.Combine(userDataDirectory, "Local State");
        Directory.CreateDirectory(profileDirectory);
        File.WriteAllText(localStatePath, """
        {
          "profile": {
            "info_cache": {
              "Profile 1": { "name": "duplicate" },
              "Default": { "name": "keep" }
            },
            "profiles_order": ["Profile 1", 7, "profile 1", "Default"],
            "last_active_profiles": ["PROFILE 1", false, "Default"]
          }
        }
        """);

        try
        {
            await new ChromeProfileDeleter().DeleteAsync(CreateProfile(userDataDirectory, "Profile 1"), userDataDirectory);

            using var document = JsonDocument.Parse(File.ReadAllText(localStatePath));
            var profile = document.RootElement.GetProperty("profile");
            Assert.False(profile.GetProperty("info_cache").TryGetProperty("PROFILE 1", out _));
            Assert.True(profile.GetProperty("info_cache").TryGetProperty("Default", out _));
            Assert.Equal(new[] { "Default" }, profile.GetProperty("profiles_order").EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()));
            Assert.Equal(new[] { "Default" }, profile.GetProperty("last_active_profiles").EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()));
            Assert.Contains("7", File.ReadAllText(localStatePath));
            Assert.Contains("false", File.ReadAllText(localStatePath));
        }
        finally
        {
            TestDirectory.Delete(userDataDirectory);
        }
    }

    [Fact]
    public async Task Delete_preserves_local_state_when_profile_entries_do_not_match()
    {
        var userDataDirectory = TestDirectory.Create();
        var profileDirectory = Path.Combine(userDataDirectory, "Profile 1");
        var localStatePath = Path.Combine(userDataDirectory, "Local State");
        var localStateJson = "{\"profile\":{\"info_cache\":{\"Default\":{}},\"profiles_order\":[\"Default\"],\"last_active_profiles\":[1]}}";
        Directory.CreateDirectory(profileDirectory);
        File.WriteAllText(localStatePath, localStateJson);

        try
        {
            await new ChromeProfileDeleter().DeleteAsync(CreateProfile(userDataDirectory, "Profile 1"), userDataDirectory);

            Assert.Equal(localStateJson, File.ReadAllText(localStatePath));
        }
        finally
        {
            TestDirectory.Delete(userDataDirectory);
        }
    }

    [Fact]
    public async Task CloseBrowserProcesses_no_matching_processes_does_not_delay_or_kill()
    {
        var processes = Array.Empty<Process>();
        var delayCalls = 0;
        var killed = new List<Process>();

        await ChromeProfileDeleter.CloseBrowserProcesses(
            "chrome.exe",
            "C:\\data",
            CancellationToken.None,
            getProcesses: (_, _) => processes,
            hasExited: _ => false,
            killProcess: p => killed.Add(p),
            clock: () => DateTimeOffset.UtcNow,
            delay: _ => { delayCalls++; return Task.CompletedTask; });

        Assert.Equal(0, delayCalls);
        Assert.Empty(killed);
    }

    [Fact]
    public async Task CloseBrowserProcesses_processes_exit_during_wait_do_not_force_kill()
    {
        var process = new Process();
        var processes = new[] { process };
        var start = DateTimeOffset.UtcNow;
        var now = start;
        var killed = new List<Process>();
        var delayCalls = 0;

        Func<Process, bool> hasExited = _ => now >= start.AddMilliseconds(100);

        await ChromeProfileDeleter.CloseBrowserProcesses(
            "chrome.exe",
            "C:\\data",
            CancellationToken.None,
            getProcesses: (_, _) => processes,
            hasExited: hasExited,
            killProcess: p => killed.Add(p),
            clock: () => now,
            delay: _ =>
            {
                delayCalls++;
                now = now.AddMilliseconds(100);
                return Task.CompletedTask;
            });

        Assert.True(delayCalls >= 1);
        Assert.Empty(killed);
    }

    [Fact]
    public async Task CloseBrowserProcesses_processes_remain_are_force_killed_after_bounded_wait()
    {
        var process = new Process();
        var processes = new[] { process };
        var start = DateTimeOffset.UtcNow;
        var now = start;
        var killed = new List<Process>();
        var delayCalls = 0;

        await ChromeProfileDeleter.CloseBrowserProcesses(
            "chrome.exe",
            "C:\\data",
            CancellationToken.None,
            getProcesses: (_, _) => processes,
            hasExited: _ => false,
            killProcess: p => killed.Add(p),
            clock: () => now,
            delay: _ =>
            {
                delayCalls++;
                now = now.AddMilliseconds(100);
                return Task.CompletedTask;
            });

        Assert.Equal(15, delayCalls);
        Assert.Single(killed);
        Assert.Same(process, killed[0]);
    }

    [Fact]
    public async Task CloseBrowserProcesses_honors_cancellation_during_wait()
    {
        var process = new Process();
        var processes = new[] { process };
        var now = DateTimeOffset.UtcNow;
        using var cts = new CancellationTokenSource();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await ChromeProfileDeleter.CloseBrowserProcesses(
                "chrome.exe",
                "C:\\data",
                cts.Token,
                getProcesses: (_, _) => processes,
                hasExited: _ => false,
                killProcess: _ => { },
                clock: () => now,
                delay: token =>
                {
                    cts.Cancel();
                    token.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                });
        });
    }

    private static ChromeProfile CreateProfile(string userDataDirectory, string directoryName)
    {
        return new ChromeProfile(
            ChromeProfile.CreateId(userDataDirectory, directoryName),
            "Test profile",
            directoryName,
            userDataDirectory,
            string.Equals(directoryName, "Default", StringComparison.OrdinalIgnoreCase));
    }

}
