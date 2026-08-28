using RouterPlus.Core.Chrome;
using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.Core.Tests;

public sealed class ChromeProfileDeleterTests
{
    [Fact]
    public void Delete_closes_configured_browser_before_removing_profile()
    {
        var userDataDirectory = CreateTempDirectory();
        var profileDirectory = Path.Combine(userDataDirectory, "Profile 1");
        Directory.CreateDirectory(profileDirectory);
        var executablePath = Path.Combine(userDataDirectory, "chrome.exe");
        File.WriteAllText(executablePath, string.Empty);
        string? closedExecutablePath = null;
        string? closedUserDataDirectory = null;

        try
        {
            var profile = CreateProfile(userDataDirectory, "Profile 1");
            var deleter = new ChromeProfileDeleter((executable, userData) =>
            {
                closedExecutablePath = executable;
                closedUserDataDirectory = userData;
            });

            deleter.Delete(profile, userDataDirectory, executablePath);

            Assert.Equal(executablePath, closedExecutablePath);
            Assert.Equal(userDataDirectory, closedUserDataDirectory);
            Assert.False(Directory.Exists(profileDirectory));
        }
        finally
        {
            DeleteTempDirectory(userDataDirectory);
        }
    }

    [Fact]
    public void Delete_removes_profile_directory_and_preserves_user_data_root()
    {
        var userDataDirectory = CreateTempDirectory();
        var profileDirectory = Path.Combine(userDataDirectory, "Profile 1");
        Directory.CreateDirectory(Path.Combine(profileDirectory, "Default"));
        File.WriteAllText(Path.Combine(profileDirectory, "Preferences"), "profile");

        try
        {
            var profile = CreateProfile(userDataDirectory, "Profile 1");

            new ChromeProfileDeleter().Delete(profile, userDataDirectory);

            Assert.False(Directory.Exists(profileDirectory));
            Assert.True(Directory.Exists(userDataDirectory));
        }
        finally
        {
            DeleteTempDirectory(userDataDirectory);
        }
    }

    [Fact]
    public void Delete_rejects_profile_outside_configured_user_data_directory()
    {
        var userDataDirectory = CreateTempDirectory();
        var outsideDirectory = CreateTempDirectory();
        var profileDirectory = Path.Combine(outsideDirectory, "Profile 1");
        Directory.CreateDirectory(profileDirectory);

        try
        {
            var profile = CreateProfile(outsideDirectory, "Profile 1");

            Assert.Throws<InvalidOperationException>(() => new ChromeProfileDeleter().Delete(profile, userDataDirectory));
            Assert.True(Directory.Exists(profileDirectory));
        }
        finally
        {
            DeleteTempDirectory(userDataDirectory);
            DeleteTempDirectory(outsideDirectory);
        }
    }

    [Fact]
    public void Delete_rejects_target_equal_to_configured_user_data_directory()
    {
        var userDataDirectory = CreateTempDirectory();

        try
        {
            var profile = CreateProfile(userDataDirectory, ".");

            Assert.Throws<InvalidOperationException>(() => new ChromeProfileDeleter().Delete(profile, userDataDirectory));
            Assert.True(Directory.Exists(userDataDirectory));
        }
        finally
        {
            DeleteTempDirectory(userDataDirectory);
        }
    }

    [Fact]
    public void Delete_allows_missing_profile_directory()
    {
        var userDataDirectory = CreateTempDirectory();

        try
        {
            var profile = CreateProfile(userDataDirectory, "Profile 1");

            new ChromeProfileDeleter().Delete(profile, userDataDirectory);

            Assert.True(Directory.Exists(userDataDirectory));
        }
        finally
        {
            DeleteTempDirectory(userDataDirectory);
        }
    }

    [Fact]
    public void Delete_removes_profile_from_local_state()
    {
        var userDataDirectory = CreateTempDirectory();
        var profileDirectory = Path.Combine(userDataDirectory, "Profile 1");
        Directory.CreateDirectory(profileDirectory);

        var localStatePath = Path.Combine(userDataDirectory, "Local State");
        var localStateJson = """
        {
          "profile": {
            "info_cache": {
              "Default": {
                "name": "Cá nhân 1"
              },
              "Profile 1": {
                "name": "Cá nhân 2"
              }
            },
            "profiles_order": ["Profile 1", "Default"],
            "last_active_profiles": ["Profile 1"]
          }
        }
        """;
        File.WriteAllText(localStatePath, localStateJson);

        try
        {
            var profile = CreateProfile(userDataDirectory, "Profile 1");

            new ChromeProfileDeleter().Delete(profile, userDataDirectory);

            Assert.False(Directory.Exists(profileDirectory));
            var updatedJson = File.ReadAllText(localStatePath);
            using var document = System.Text.Json.JsonDocument.Parse(updatedJson);
            var profileMetadata = document.RootElement.GetProperty("profile");
            Assert.True(profileMetadata.GetProperty("info_cache").TryGetProperty("Default", out _));
            Assert.False(profileMetadata.GetProperty("info_cache").TryGetProperty("Profile 1", out _));
            Assert.DoesNotContain("Profile 1", profileMetadata.GetProperty("profiles_order").EnumerateArray().Select(item => item.GetString()));
            Assert.Empty(profileMetadata.GetProperty("last_active_profiles").EnumerateArray());
            Assert.DoesNotContain("Cá nhân 2", updatedJson);
        }
        finally
        {
            DeleteTempDirectory(userDataDirectory);
        }
    }

    [Fact]
    public void Delete_handles_missing_local_state_gracefully()
    {
        var userDataDirectory = CreateTempDirectory();
        var profileDirectory = Path.Combine(userDataDirectory, "Profile 1");
        Directory.CreateDirectory(profileDirectory);

        try
        {
            var profile = CreateProfile(userDataDirectory, "Profile 1");

            new ChromeProfileDeleter().Delete(profile, userDataDirectory);

            Assert.False(Directory.Exists(profileDirectory));
        }
        finally
        {
            DeleteTempDirectory(userDataDirectory);
        }
    }

    [Fact]
    public void Delete_handles_malformed_local_state_gracefully()
    {
        var userDataDirectory = CreateTempDirectory();
        var profileDirectory = Path.Combine(userDataDirectory, "Profile 1");
        Directory.CreateDirectory(profileDirectory);

        var localStatePath = Path.Combine(userDataDirectory, "Local State");
        File.WriteAllText(localStatePath, "{}");

        try
        {
            var profile = CreateProfile(userDataDirectory, "Profile 1");

            new ChromeProfileDeleter().Delete(profile, userDataDirectory);

            Assert.False(Directory.Exists(profileDirectory));
        }
        finally
        {
            DeleteTempDirectory(userDataDirectory);
        }
    }

    private static ChromeProfile CreateProfile(string userDataDirectory, string directoryName) =>
        new(
            ChromeProfile.CreateId(userDataDirectory, directoryName),
            "Test profile",
            directoryName,
            userDataDirectory,
            string.Equals(directoryName, "Default", StringComparison.OrdinalIgnoreCase));

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "RouterPlusTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
