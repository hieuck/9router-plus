using System.Text.Json;
using RouterPlus.Core.Chrome;
using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.Core.Tests;

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

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Delete_rejects_blank_user_data_directory(string userDataDirectory)
    {
        var profile = CreateProfile(Path.GetTempPath(), "Profile 1");

        Assert.Throws<ArgumentException>(() => new ChromeProfileDeleter().Delete(profile, userDataDirectory));
    }

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
    public void Delete_skips_browser_close_when_executable_path_is_blank()
    {
        var userDataDirectory = CreateTempDirectory();
        var profileDirectory = Path.Combine(userDataDirectory, "Profile 1");
        Directory.CreateDirectory(profileDirectory);
        var closeCalled = false;

        try
        {
            var profile = CreateProfile(userDataDirectory, "Profile 1");
            var deleter = new ChromeProfileDeleter((_, _) => closeCalled = true);

            deleter.Delete(profile, userDataDirectory, "   ");

            Assert.False(closeCalled);
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
    public void Delete_rejects_profile_nested_below_user_data_directory()
    {
        var userDataDirectory = CreateTempDirectory();
        var nestedDirectory = Path.Combine(userDataDirectory, "Profiles", "Profile 1");
        Directory.CreateDirectory(nestedDirectory);

        try
        {
            var profile = CreateProfile(userDataDirectory, Path.Combine("Profiles", "Profile 1"));
            var exception = Assert.Throws<InvalidOperationException>(() => new ChromeProfileDeleter().Delete(profile, userDataDirectory));

            Assert.Contains("immediate child", exception.Message);
            Assert.True(Directory.Exists(nestedDirectory));
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
        File.WriteAllText(profilePath, "not a directory");

        try
        {
            var profile = CreateProfile(userDataDirectory, "Profile 1");
            var exception = Assert.Throws<InvalidOperationException>(() => new ChromeProfileDeleter().Delete(profile, userDataDirectory));

            Assert.Contains("not a directory", exception.Message);
            Assert.True(File.Exists(profilePath));
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
    public void Delete_rejects_profile_nested_below_user_data_directory()
    {
        var userDataDirectory = CreateTempDirectory();
        var nestedDirectory = Path.Combine(userDataDirectory, "Profiles", "Profile 1");
        Directory.CreateDirectory(nestedDirectory);

        try
        {
            var profile = CreateProfile(userDataDirectory, Path.Combine("Profiles", "Profile 1"));
            var exception = Assert.Throws<InvalidOperationException>(() => new ChromeProfileDeleter().Delete(profile, userDataDirectory));

            Assert.Contains("immediate child", exception.Message);
            Assert.True(Directory.Exists(nestedDirectory));
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
        File.WriteAllText(profilePath, "not a directory");

        try
        {
            var profile = CreateProfile(userDataDirectory, "Profile 1");
            var exception = Assert.Throws<InvalidOperationException>(() => new ChromeProfileDeleter().Delete(profile, userDataDirectory));

            Assert.Contains("not a directory", exception.Message);
            Assert.True(File.Exists(profilePath));
        }
        finally
        {
            DeleteTempDirectory(userDataDirectory);
        }
    }

    [Fact]
    public void Delete_handles_local_state_without_profile_metadata_without_rewriting_it()
    {
        var userDataDirectory = CreateTempDirectory();
        var profileDirectory = Path.Combine(userDataDirectory, "Profile 1");
        Directory.CreateDirectory(profileDirectory);
        var localStatePath = Path.Combine(userDataDirectory, "Local State");
        var localStateJson = "{\"profile\": {\"info_cache\": {\"Default\": {\"name\": \"Default\"}}}}";
        File.WriteAllText(localStatePath, localStateJson);

        try
        {
            var profile = CreateProfile(userDataDirectory, "Profile 1");

            new ChromeProfileDeleter().Delete(profile, userDataDirectory);

            Assert.Equal(localStateJson, File.ReadAllText(localStatePath));
            Assert.False(Directory.Exists(profileDirectory));
        }
        finally
        {
            DeleteTempDirectory(userDataDirectory);
        }
    }

    [Fact]
    public void Delete_handles_null_local_state_json_without_rewriting_it()
    {
        var userDataDirectory = CreateTempDirectory();
        var profileDirectory = Path.Combine(userDataDirectory, "Profile 1");
        Directory.CreateDirectory(profileDirectory);
        var localStatePath = Path.Combine(userDataDirectory, "Local State");
        File.WriteAllText(localStatePath, "null");

        try
        {
            var profile = CreateProfile(userDataDirectory, "Profile 1");

            new ChromeProfileDeleter().Delete(profile, userDataDirectory);

            Assert.Equal("null", File.ReadAllText(localStatePath));
            Assert.False(Directory.Exists(profileDirectory));
        }
        finally
        {
            DeleteTempDirectory(userDataDirectory);
        }
    }

    [Fact]
    public void Delete_handles_profile_metadata_with_non_array_order_properties_without_rewriting_it()
    {
        var userDataDirectory = CreateTempDirectory();
        var profileDirectory = Path.Combine(userDataDirectory, "Profile 1");
        Directory.CreateDirectory(profileDirectory);
        var localStatePath = Path.Combine(userDataDirectory, "Local State");
        var localStateJson = "{\"profile\": {\"info_cache\": {\"Default\": {\"name\": \"Default\"}}, \"profiles_order\": \"Profile 1\"}}";
        File.WriteAllText(localStatePath, localStateJson);

        try
        {
            var profile = CreateProfile(userDataDirectory, "Profile 1");

            new ChromeProfileDeleter().Delete(profile, userDataDirectory);

            Assert.Equal(localStateJson, File.ReadAllText(localStatePath));
            Assert.False(Directory.Exists(profileDirectory));
        }
        finally
        {
            DeleteTempDirectory(userDataDirectory);
        }
    }

    [Fact]
    public void Delete_removes_all_case_insensitive_matches_from_local_state_arrays()
    {
        var userDataDirectory = CreateTempDirectory();
        var profileDirectory = Path.Combine(userDataDirectory, "Profile 1");
        Directory.CreateDirectory(profileDirectory);
        var localStatePath = Path.Combine(userDataDirectory, "Local State");
        File.WriteAllText(localStatePath, "{\"profile\": {\"profiles_order\": [\"profile 1\", \"Default\", \"PROFILE 1\"], \"last_active_profiles\": [\"Profile 1\"]}}" );

        try
        {
            var profile = CreateProfile(userDataDirectory, "Profile 1");

            new ChromeProfileDeleter().Delete(profile, userDataDirectory);

            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(localStatePath));
            var profileMetadata = document.RootElement.GetProperty("profile");
            Assert.Equal(new[] { "Default" }, profileMetadata.GetProperty("profiles_order").EnumerateArray().Select(item => item.GetString()));
            Assert.Empty(profileMetadata.GetProperty("last_active_profiles").EnumerateArray());
        }
        finally
        {
            DeleteTempDirectory(userDataDirectory);
        }
    }

    [Fact]
    public void Delete_preserves_non_string_local_state_array_values()
    {
        var userDataDirectory = CreateTempDirectory();
        var profileDirectory = Path.Combine(userDataDirectory, "Profile 1");
        Directory.CreateDirectory(profileDirectory);
        var localStatePath = Path.Combine(userDataDirectory, "Local State");
        File.WriteAllText(localStatePath, "{\"profile\": {\"profiles_order\": [\"Profile 1\", 7, null]}}" );

        try
        {
            var profile = CreateProfile(userDataDirectory, "Profile 1");

            new ChromeProfileDeleter().Delete(profile, userDataDirectory);

            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(localStatePath));
            var values = document.RootElement.GetProperty("profile").GetProperty("profiles_order").EnumerateArray().ToArray();
            Assert.Equal(2, values.Length);
            Assert.Equal(JsonValueKind.Number, values[0].ValueKind);
            Assert.Equal(JsonValueKind.Null, values[1].ValueKind);
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
    public void Delete_handles_null_local_state_json_without_rewriting_it()
    {
        var userDataDirectory = CreateTempDirectory();
        var profileDirectory = Path.Combine(userDataDirectory, "Profile 1");
        Directory.CreateDirectory(profileDirectory);
        var localStatePath = Path.Combine(userDataDirectory, "Local State");
        File.WriteAllText(localStatePath, "null");

        try
        {
            var profile = CreateProfile(userDataDirectory, "Profile 1");

            new ChromeProfileDeleter().Delete(profile, userDataDirectory);

            Assert.Equal("null", File.ReadAllText(localStatePath));
            Assert.False(Directory.Exists(profileDirectory));
        }
        finally
        {
            DeleteTempDirectory(userDataDirectory);
        }
    }

    [Fact]
    public void Delete_removes_case_insensitive_duplicate_array_entries_and_ignores_non_strings()
    {
        var userDataDirectory = CreateTempDirectory();
        var profileDirectory = Path.Combine(userDataDirectory, "Profile 1");
        Directory.CreateDirectory(profileDirectory);
        var localStatePath = Path.Combine(userDataDirectory, "Local State");
        File.WriteAllText(localStatePath, """
        {
          "profile": {
            "info_cache": {},
            "profiles_order": ["profile 1", 7, "Profile 1", "Default"],
            "last_active_profiles": ["PROFILE 1", "Default"]
          }
        }
        """);

        try
        {
            var profile = CreateProfile(userDataDirectory, "Profile 1");

            new ChromeProfileDeleter().Delete(profile, userDataDirectory);

            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(localStatePath));
            var profileMetadata = document.RootElement.GetProperty("profile");
            var profilesOrder = profileMetadata.GetProperty("profiles_order").EnumerateArray().ToArray();
            Assert.Equal(System.Text.Json.JsonValueKind.Number, profilesOrder[0].ValueKind);
            Assert.Equal(7, profilesOrder[0].GetInt32());
            Assert.Equal("Default", profilesOrder[1].GetString());
            Assert.Equal(new[] { "Default" }, profileMetadata.GetProperty("last_active_profiles").EnumerateArray().Select(value => value.GetString()).ToArray());
        }
        finally
        {
            DeleteTempDirectory(userDataDirectory);
        }
    }

    [Fact]
    public void Delete_leaves_local_state_unchanged_when_profile_has_no_matching_metadata()
    {
        var userDataDirectory = CreateTempDirectory();
        var profileDirectory = Path.Combine(userDataDirectory, "Profile 1");
        Directory.CreateDirectory(profileDirectory);
        var localStatePath = Path.Combine(userDataDirectory, "Local State");
        var localStateJson = "{\"profile\": {\"info_cache\": {\"Default\": {\"name\": \"Default\"}}, \"profiles_order\": \"Default\"}}";
        File.WriteAllText(localStatePath, localStateJson);

        try
        {
            var profile = CreateProfile(userDataDirectory, "Profile 1");

            new ChromeProfileDeleter().Delete(profile, userDataDirectory);

            Assert.Equal(localStateJson, File.ReadAllText(localStatePath));
            Assert.False(Directory.Exists(profileDirectory));
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
