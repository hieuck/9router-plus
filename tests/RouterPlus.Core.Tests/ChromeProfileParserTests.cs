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
}
