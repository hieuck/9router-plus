using RouterPlus.Core.Chrome;
using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.Core.Tests;

public sealed class ChromeProfileProvisionerTests
{
    [Fact]
    public void Create_trims_name_uses_name_for_directory_and_creates_it()
    {
        var userDataDirectory = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(userDataDirectory, "Profile 1"));
            File.WriteAllText(Path.Combine(userDataDirectory, "Profile 2"), string.Empty);
            var discovered = new[]
            {
                new ChromeProfile(
                    ChromeProfile.CreateId(userDataDirectory, "Profile 3"),
                    "Existing",
                    "Profile 3",
                    userDataDirectory,
                    false)
            };
            var managed = new[]
            {
                new ManagedChromeProfile("Managed", "Profile 4", userDataDirectory)
            };

            var created = new ChromeProfileProvisioner().Create(
                userDataDirectory,
                "  New profile  ",
                discovered,
                managed);

            Assert.Equal("New profile", created.Name);
            Assert.Equal("Profile New profile", created.DirectoryName);
            Assert.Equal(Path.GetFullPath(userDataDirectory), created.UserDataDirectory);
            Assert.True(Directory.Exists(Path.Combine(userDataDirectory, "Profile New profile")));
        }
        finally
        {
            DeleteTempDirectory(userDataDirectory);
        }
    }

    [Theory]
    [InlineData("abc", "Profile abc")]
    [InlineData("abc@example.com", "Profile abc@example.com")]
    public void Create_preserves_email_and_special_characters_in_directory_name(string name, string expectedDirectoryName)
    {
        var userDataDirectory = CreateTempDirectory();
        try
        {
            var created = new ChromeProfileProvisioner().Create(
                userDataDirectory,
                name,
                Array.Empty<ChromeProfile>(),
                Array.Empty<ManagedChromeProfile>());

            Assert.Equal(expectedDirectoryName, created.DirectoryName);
            Assert.True(Directory.Exists(Path.Combine(userDataDirectory, expectedDirectoryName)));
        }
        finally
        {
            DeleteTempDirectory(userDataDirectory);
        }
    }

    [Fact]
    public void Create_rejects_existing_directory_for_name()
    {
        var userDataDirectory = CreateTempDirectory();
        try
        {
            Directory.CreateDirectory(Path.Combine(userDataDirectory, "Profile abc"));

            var exception = Assert.Throws<InvalidOperationException>(() => new ChromeProfileProvisioner().Create(
                userDataDirectory,
                "abc",
                Array.Empty<ChromeProfile>(),
                Array.Empty<ManagedChromeProfile>()));

            Assert.Contains("Profile abc", exception.Message);
        }
        finally
        {
            DeleteTempDirectory(userDataDirectory);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_blank_names(string name)
    {
        var userDataDirectory = CreateTempDirectory();
        try
        {
            Assert.Throws<ArgumentException>(() => new ChromeProfileProvisioner().Create(
                userDataDirectory,
                name,
                Array.Empty<ChromeProfile>(),
                Array.Empty<ManagedChromeProfile>()));
        }
        finally
        {
            DeleteTempDirectory(userDataDirectory);
        }
    }

    [Fact]
    public void Create_rejects_case_insensitive_duplicate_names()
    {
        var userDataDirectory = CreateTempDirectory();
        try
        {
            var discovered = new[]
            {
                new ChromeProfile(
                    ChromeProfile.CreateId(userDataDirectory, "Default"),
                    "Personal",
                    "Default",
                    userDataDirectory,
                    true)
            };

            Assert.Throws<InvalidOperationException>(() => new ChromeProfileProvisioner().Create(
                userDataDirectory,
                " personal ",
                discovered,
                Array.Empty<ManagedChromeProfile>()));
        }
        finally
        {
            DeleteTempDirectory(userDataDirectory);
        }
    }

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
