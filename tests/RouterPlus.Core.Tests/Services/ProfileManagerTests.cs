using System.IO;
using RouterPlus.App.Services;
using RouterPlus.Core.Chrome;
using RouterPlus.Infrastructure.Chrome;
using RouterPlus.Infrastructure.Storage;

namespace RouterPlus.Core.Tests.Services;

public sealed class ProfileManagerTests
{
    [Fact]
    public void LoadManaged_AddManaged_RemoveManaged_work()
    {
        var manager = new ProfileManager();
        manager.LoadManaged(new[] { new ManagedChromeProfile("A", "P1", @"C:\ud") });
        Assert.Single(manager.ManagedProfiles);

        manager.AddManaged(new ManagedChromeProfile("B", "P2", @"C:\ud"));
        Assert.Equal(2, manager.ManagedProfiles.Count);

        var profile = new ChromeProfile(ChromeProfile.CreateId(@"C:\ud", "P1"), "A", "P1", @"C:\ud", false);
        manager.RemoveManaged(profile);
        Assert.Single(manager.ManagedProfiles);
        Assert.Equal("B", manager.ManagedProfiles[0].Name);
    }

    [Fact]
    public void LoadManaged_null_clears()
    {
        var manager = new ProfileManager();
        manager.LoadManaged(new[] { new ManagedChromeProfile("A", "P1", @"C:\ud") });
        manager.LoadManaged(null);
        Assert.Empty(manager.ManagedProfiles);
    }

    [Fact]
    public void LoadRecent_null_clears()
    {
        var manager = new ProfileManager();
        manager.LoadRecent(new[] { new RecentProfile("id", "name", @"C:\ud", DateTime.UtcNow) });
        manager.LoadRecent(null);
        Assert.Empty(manager.RecentProfiles);
    }

    [Fact]
    public void TrackLaunch_inserts_new_and_increments_existing_and_caps_at_ten()
    {
        var manager = new ProfileManager();
        var userData = @"C:\ud";
        for (var i = 0; i < 12; i++)
        {
            var profile = new ChromeProfile(ChromeProfile.CreateId(userData, $"P{i}"), $"Name{i}", $"P{i}", userData, false);
            manager.TrackLaunch(profile);
        }

        Assert.Equal(10, manager.RecentProfiles.Count);
        Assert.Equal(ChromeProfile.CreateId(userData, "P11"), manager.RecentProfiles[0].ProfileId);
        Assert.Equal(1, manager.RecentProfiles[0].LaunchCount);

        // Re-launch first (now at index 10? removed) not present; re-launch an existing one increments
        var existingId = manager.RecentProfiles[3].ProfileId;
        var existingProfile = new ChromeProfile(existingId, "X", "P", userData, false);
        // need matching directory name? TrackLaunch matches by id+userData, so ok
        // find actual profile name not required for increment matching by id
        manager.TrackLaunch(new ChromeProfile(existingId, manager.RecentProfiles.First(r => r.ProfileId == existingId).ProfileName, "P", userData, false));
        var updated = manager.RecentProfiles.First(r => r.ProfileId == existingId);
        Assert.Equal(2, updated.LaunchCount);
    }

    [Fact]
    public void PinRecent_sets_pinned_and_sorts_pinned_first()
    {
        var userData = @"C:\ud";
        var recentA = new RecentProfile(ChromeProfile.CreateId(userData, "A"), "A", userData, DateTime.UtcNow.AddMinutes(-10), 1, false);
        var recentB = new RecentProfile(ChromeProfile.CreateId(userData, "B"), "B", userData, DateTime.UtcNow, 1, false);
        var manager = new ProfileManager();
        manager.LoadRecent(new[] { recentA, recentB });

        var profileA = new ChromeProfile(recentA.ProfileId, "A", "A", userData, false);
        manager.PinRecent(profileA, true);

        Assert.True(manager.RecentProfiles[0].IsPinned);
        Assert.Equal(recentA.ProfileId, manager.RecentProfiles[0].ProfileId);
    }

    [Fact]
    public void PinRecent_unknown_profile_is_noop()
    {
        var userData = @"C:\ud";
        var manager = new ProfileManager();
        manager.LoadRecent(new[] { new RecentProfile("id", "name", userData, DateTime.UtcNow) });
        var profile = new ChromeProfile("other", "other", "other", userData, false);
        manager.PinRecent(profile, true);
        Assert.False(manager.RecentProfiles[0].IsPinned);
    }

    [Fact]
    public void ClearRecent_empties_list()
    {
        var manager = new ProfileManager();
        manager.LoadRecent(new[] { new RecentProfile("id", "name", @"C:\ud", DateTime.UtcNow) });
        manager.ClearRecent();
        Assert.Empty(manager.RecentProfiles);
    }

    [Fact]
    public void FindAllInstallations_uses_locator_seam()
    {
        var locator = new ChromeLocator(
            _ => string.Empty,
            _ => false,
            _ => false,
            _ => null,
            Array.Empty<string>());
        var manager = new ProfileManager(locator, new ChromeProfileReader());
        var installations = manager.FindAllInstallations();
        Assert.Empty(installations);
    }

    [Fact]
    public void DiscoverProfiles_returns_null_installation_when_chrome_missing()
    {
        var locator = new ChromeLocator(
            _ => string.Empty,
            _ => false,
            _ => false,
            _ => null,
            Array.Empty<string>());
        var manager = new ProfileManager(locator, new ChromeProfileReader());
        var result = manager.DiscoverProfiles(@"C:\nope\chrome.exe", @"C:\nope\ud");
        Assert.Null(result.Installation);
        Assert.Empty(result.Profiles);
    }

    [Fact]
    public void DiscoverProfiles_merges_managed_into_discovered()
    {
        using var temp = new TemporaryDirectory();
        var userData = temp.Path;
        Directory.CreateDirectory(Path.Combine(userData, "Default"));
        Directory.CreateDirectory(Path.Combine(userData, "Profile 1"));
        File.WriteAllText(Path.Combine(userData, "Local State"), """
            {
              "profile": {
                "info_cache": {
                  "Default": { "name": "Personal" },
                  "Profile 1": { "name": "Work" }
                }
              }
            }
            """);
        var chromeExe = Path.Combine(temp.Path, "chrome.exe");
        File.WriteAllText(chromeExe, string.Empty);

        var locator = new ChromeLocator(Environment.GetFolderPath, File.Exists, Directory.Exists, _ => null, new[] { "C:" });
        var reader = new ChromeProfileReader();
        var manager = new ProfileManager(locator, reader);
        manager.LoadManaged(new[] { new ManagedChromeProfile("WorkRenamed", "Profile 1", userData) });

        var result = manager.DiscoverProfiles(chromeExe, userData);

        Assert.NotNull(result.Installation);
        Assert.Equal(2, result.Profiles.Count);
        Assert.Contains(result.Profiles, p => p.Name == "WorkRenamed" && p.DirectoryName == "Profile 1");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"routerplus-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}