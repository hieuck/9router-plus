using System.Text.Json;
using RouterPlus.Core.Chrome;
using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.Infrastructure.Tests.Chrome;

public sealed class ChromeProfileDeleterTests
{
    [Fact]
    public void Delete_rejects_null_profile()
    {
        var userDataDirectory = CreateTempDirectory();

        try
        {
            Assert.Throws<ArgumentNullException>(() => new ChromeProfileDeleter().Delete(null!, userDataDirectory));
        }
        finally
        {
            DeleteTempDirectory(userDataDirectory);
        }
    }

    [Fact]
    public void Delete_rejects_whitespace_user_data_directory()
    {
        var userDataDirectory = CreateTempDirectory();
        var profile = CreateProfile(userDataDirectory, "Profile 1");

        try
        {
            Assert.Throws<ArgumentException>(() => new ChromeProfileDeleter().Delete(profile, " "));
        }
        finally
        {
            DeleteTempDirectory(userDataDirectory);
        }
    }

    [Fact]
    public void Delete_rejects_profile_with_different_user_data_directory()
    {
        var userDataDirectory = CreateTempDirectory();
        var profileUserDataDirectory = CreateTempDirectory();
        var profileDirectory = Path.Combine(profileUserDataDirectory, "Profile 1");
        Directory.CreateDirectory(profileDirectory);

        try
        {
            var profile = CreateProfile(profileUserDataDirectory, "Profile 1");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new ChromeProfileDeleter().Delete(profile, userDataDirectory));

            Assert.Equal("The Chrome profile is outside the configured User Data directory.", exception.Message);
            Assert.True(Directory.Exists(profileDirectory));
        }
        finally
        {
            DeleteTempDirectory(userDataDirectory);
            DeleteTempDirectory(profileUserDataDirectory);
        }
    }

    [Fact]
    public void Delete_rejects_profile_that_is_not_an_immediate_child()
    {
        var userDataDirectory = CreateTempDirectory();
        var profileDirectory = Path.Combine(userDataDirectory, "Profiles", "Profile 1");
        Directory.CreateDirectory(profileDirectory);

        try
        {
            var profile = CreateProfile(userDataDirectory, Path.Combine("Profiles", "Profile 1"));

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new ChromeProfileDeleter().Delete(profile, userDataDirectory));

            Assert.Equal("The Chrome profile directory must be an immediate child of User Data.", exception.Message);
            Assert.True(Directory.Exists(profileDirectory));
        }
        finally
        {
            DeleteTempDirectory(userDataDirectory);
        }
    }

    [Fact]
    public void Delete_rejects_profile_path_that_is_a_file()
    {
        var userDataDirectory = CreateTempDirectory();
        var profilePath = Path.Combine(userDataDirectory, "Profile 1");
        File.WriteAllText(profilePath, string.Empty);

        try
        {
            var profile = CreateProfile(userDataDirectory, "Profile 1");

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new ChromeProfileDeleter().Delete(profile, userDataDirectory));

            Assert.Equal("The Chrome profile path is not a directory.", exception.Message);
            Assert.True(File.Exists(profilePath));
        }
        finally
        {
            DeleteTempDirectory(userDataDirectory);
        }
    }

    [Fact]
    public void Delete_handles_null_local_state_document()
    {
        var userDataDirectory = CreateTempDirectory();
        var profileDirectory = Path.Combine(userDataDirectory, "Profile 1");
        var localStatePath = Path.Combine(userDataDirectory, "Local State");
        Directory.CreateDirectory(profileDirectory);
        File.WriteAllText(localStatePath, "null");

        try
        {
            new ChromeProfileDeleter().Delete(CreateProfile(userDataDirectory, "Profile 1"), userDataDirectory);

            Assert.False(Directory.Exists(profileDirectory));
            Assert.Equal("null", File.ReadAllText(localStatePath));
        }
        finally
        {
            DeleteTempDirectory(userDataDirectory);
        }
    }

    [Fact]
    public void Delete_preserves_local_state_when_profile_metadata_is_missing()
    {
        var userDataDirectory = CreateTempDirectory();
        var profileDirectory = Path.Combine(userDataDirectory, "Profile 1");
        var localStatePath = Path.Combine(userDataDirectory, "Local State");
        var localStateJson = "{\"profile\":{\"info_cache\":{},\"profiles_order\":[],\"last_active_profiles\":[]}}";
        Directory.CreateDirectory(profileDirectory);
        File.WriteAllText(localStatePath, localStateJson);

        try
        {
            new ChromeProfileDeleter().Delete(CreateProfile(userDataDirectory, "Profile 1"), userDataDirectory);

            Assert.False(Directory.Exists(profileDirectory));
            Assert.Equal(localStateJson, File.ReadAllText(localStatePath));
        }
        finally
        {
            DeleteTempDirectory(userDataDirectory);
        }
    }

    [Fact]
    public void Delete_removes_case_insensitive_duplicate_profile_entries_and_ignores_non_string_values()
    {
        var userDataDirectory = CreateTempDirectory();
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
            new ChromeProfileDeleter().Delete(CreateProfile(userDataDirectory, "Profile 1"), userDataDirectory);

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
            DeleteTempDirectory(userDataDirectory);
        }
    }

    [Fact]
    public void Delete_preserves_local_state_when_profile_entries_do_not_match()
    {
        var userDataDirectory = CreateTempDirectory();
        var profileDirectory = Path.Combine(userDataDirectory, "Profile 1");
        var localStatePath = Path.Combine(userDataDirectory, "Local State");
        var localStateJson = "{\"profile\":{\"info_cache\":{\"Default\":{}},\"profiles_order\":[\"Default\"],\"last_active_profiles\":[1]}}";
        Directory.CreateDirectory(profileDirectory);
        File.WriteAllText(localStatePath, localStateJson);

        try
        {
            new ChromeProfileDeleter().Delete(CreateProfile(userDataDirectory, "Profile 1"), userDataDirectory);

            Assert.Equal(localStateJson, File.ReadAllText(localStatePath));
        }
        finally
        {
            DeleteTempDirectory(userDataDirectory);
        }
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

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "RouterPlusInfrastructureTests", Guid.NewGuid().ToString("N"));
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
