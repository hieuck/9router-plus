using RouterPlus.Core.Updates;

namespace RouterPlus.Core.Tests;

public sealed class ReleaseVersionTests
{
    [Theory]
    [InlineData("1.2.3", "1.2.4", -1)]
    [InlineData("1.2.3", "2.0.0", -1)]
    [InlineData("2.0.0", "1.2.3", 1)]
    [InlineData("1.2.3", "1.3.0", -1)]
    [InlineData("1.3.0", "1.2.3", 1)]
    [InlineData("1.2.4", "1.2.3", 1)]
    [InlineData("1.2.3", "1.2.3", 0)]
    [InlineData("1.2.3-rc.1", "1.2.3-rc.2", -1)]
    [InlineData("1.2.3", "1.2.3-rc.1", 1)]
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
    [InlineData("1.2.3-01")]
    public void Parse_rejects_non_release_versions(string value)
    {
        Assert.Throws<FormatException>(() => ReleaseVersion.Parse(value));
    }

    [Theory]
    [InlineData("1.2.3-alpha.1", "1.2.3-alpha.beta", -1)]
    [InlineData("1.2.3-alpha.1", "1.2.3-alpha.1.1", -1)]
    [InlineData("1.2.3-alpha.2", "1.2.3-alpha.10", -1)]
    [InlineData("1.2.3-alpha", "1.2.3-alpha.1", -1)]
    public void Compare_orders_prerelease_identifiers(
        string current, string candidate, int expectedSign)
    {
        var result = ReleaseVersion.Parse(current).CompareTo(ReleaseVersion.Parse(candidate));

        Assert.Equal(expectedSign, Math.Sign(result));
    }

    [Fact]
    public void Compare_orders_release_after_prerelease()
    {
        var release = ReleaseVersion.Parse("1.2.3");
        var prerelease = ReleaseVersion.Parse("1.2.3-rc.1");

        Assert.True(release.CompareTo(prerelease) > 0);
        Assert.True(prerelease.CompareTo(release) < 0);
    }

    [Fact]
    public void CompareTo_null_returns_positive()
    {
        Assert.True(ReleaseVersion.Parse("1.2.3").CompareTo(null) > 0);
    }

    [Fact]
    public void Equal_versions_have_equal_hash_codes()
    {
        var first = ReleaseVersion.Parse("1.2.3-rc.1");
        var second = ReleaseVersion.Parse("1.2.3-rc.1");

        Assert.True(first.Equals(second));
        Assert.True(first.Equals((object)second));
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.False(first.Equals(null));
        Assert.False(first.Equals("1.2.3-rc.1"));
    }
}
