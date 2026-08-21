using RouterPlus.Core.Updates;

namespace RouterPlus.Core.Tests;

public sealed class ReleaseVersionTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.4", -1)]
    [InlineData("1.2.3", "1.2.3", 0)]
    [InlineData("1.2.3", "1.2.3-rc.1", 1)]
    [InlineData("1.2.3-rc.1", "1.2.3-rc.2", -1)]
    public void Compare_uses_semver_order(string current, string candidate, int expectedSign)
    {
        var result = ReleaseVersion.Parse(current).CompareTo(ReleaseVersion.Parse(candidate));

        Assert.Equal(expectedSign, Math.Sign(result));
    }

    [Theory]
    [InlineData("v1.2.3")]
    [InlineData("1.2")]
    [InlineData("1.2.3+build")]
    [InlineData("1.2.3-rc 1")]
    public void Parse_rejects_non_release_versions(string value)
    {
        Assert.Throws<FormatException>(() => ReleaseVersion.Parse(value));
    }
}
