using RouterPlus.Core.Chrome;
using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.Core.Tests;

public sealed class ChromeProfileDeleterTests
{
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
