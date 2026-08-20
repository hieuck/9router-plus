using RouterPlus.Core.Chrome;

namespace RouterPlus.Core.Tests;

public sealed class ChromeProfileFilterTests
{
    private static readonly ChromeProfile[] Profiles =
    [
        new("1", "Personal", "Default", "C:\\Chrome\\User Data", true),
        new("2", "Automation", "Profile 2", "C:\\Chrome\\User Data", false),
        new("3", "Work", "Profile 3", "C:\\Chrome\\User Data", false)
    ];

    [Fact]
    public void Filter_matches_name_or_directory_without_case_sensitivity()
    {
        Assert.Equal("Automation", Assert.Single(ChromeProfileFilter.Filter(Profiles, "auto")).Name);
        Assert.Equal("Work", Assert.Single(ChromeProfileFilter.Filter(Profiles, "PROFILE 3")).Name);
    }

    [Fact]
    public void Filter_returns_all_profiles_for_a_blank_query()
    {
        Assert.Equal(Profiles, ChromeProfileFilter.Filter(Profiles, "  ").ToArray());
    }
}
