using RouterPlus.Core.Chrome;

namespace RouterPlus.Core.Tests;

public sealed class ChromeProfileParserTests
{
    [Fact]
    public void Parse_reads_profile_names_and_stable_metadata()
    {
        const string json = """
        {
          "profile": {
            "info_cache": {
              "Default": { "name": "Personal", "is_using_default_name": false },
              "Profile 2": { "name": "Automation", "is_using_default_name": false }
            }
          }
        }
        """;

        var profiles = ChromeProfileParser.Parse("C:\\Chrome\\User Data", json);

        Assert.Equal(2, profiles.Count);
        Assert.Equal("Automation", profiles[0].Name);
        Assert.Equal("Profile 2", profiles[0].DirectoryName);
        Assert.False(string.IsNullOrWhiteSpace(profiles[0].Id));
        Assert.Equal("Personal", profiles[1].Name);
        Assert.True(profiles[1].IsDefault);
    }

    [Fact]
    public void Parse_uses_directory_name_when_profile_name_is_missing()
    {
        const string json = """
        {
          "profile": {
            "info_cache": {
              "Profile 1": {}
            }
          }
        }
        """;

        var profiles = ChromeProfileParser.Parse("C:\\Chrome\\User Data", json);

        var profile = Assert.Single(profiles);
        Assert.Equal("Profile 1", profile.Name);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{ \"profile\": {} }")]
    [InlineData("{ \"profile\": { \"info_cache\": [] } }")]
    public void Parse_without_object_info_cache_returns_empty(string json)
    {
        // Arrange
        const string userDataDirectory = "C:\\Chrome\\User Data";

        // Act
        var profiles = ChromeProfileParser.Parse(userDataDirectory, json);

        // Assert
        Assert.Empty(profiles);
    }

    [Fact]
    public void Parse_trims_names_sorts_case_insensitively_and_recognizes_default_directory()
    {
        // Arrange
        const string json = """
        {
          "profile": {
            "info_cache": {
              "Profile 2": { "name": "  zed  " },
              "default": { "name": "Alpha" }
            }
          }
        }
        """;

        // Act
        var profiles = ChromeProfileParser.Parse("C:\\Chrome\\User Data", json);

        // Assert
        Assert.Equal(2, profiles.Count);
        Assert.Equal("Alpha", profiles[0].Name);
        Assert.Equal("default", profiles[0].DirectoryName);
        Assert.True(profiles[0].IsDefault);
        Assert.Equal("zed", profiles[1].Name);
    }

    [Fact]
    public void Parse_rejects_blank_user_data_directory()
    {
        // Arrange
        const string json = "{ \"profile\": { \"info_cache\": {} } }";

        // Act
        var exception = Assert.Throws<ArgumentException>(() => ChromeProfileParser.Parse(" ", json));

        // Assert
        Assert.Equal("userDataDirectory", exception.ParamName);
    }

    [Fact]
    public void Parse_rejects_blank_local_state_json()
    {
        // Arrange
        const string userDataDirectory = "C:\\Chrome\\User Data";

        // Act
        var exception = Assert.Throws<ArgumentException>(() => ChromeProfileParser.Parse(userDataDirectory, " "));

        // Assert
        Assert.Equal("localStateJson", exception.ParamName);
    }

    [Fact]
    public void Parse_rejects_malformed_json()
    {
        // Arrange
        const string userDataDirectory = "C:\\Chrome\\User Data";

        // Act
        var exception = Assert.ThrowsAny<System.Text.Json.JsonException>(() =>
            ChromeProfileParser.Parse(userDataDirectory, "{ not-json"));

        // Assert
        Assert.NotNull(exception);
    }

    [Fact]
    public void Parse_uses_directory_name_for_null_or_whitespace_names()
    {
        // Arrange
        const string json = """
        {
          "profile": {
            "info_cache": {
              "Profile 1": { "name": "   " },
              "Profile 2": { "name": null }
            }
          }
        }
        """;

        // Act
        var profiles = ChromeProfileParser.Parse("C:\\Chrome\\User Data", json);

        // Assert
        Assert.Equal("Profile 1", profiles[0].Name);
        Assert.Equal("Profile 2", profiles[1].Name);
    }
}
