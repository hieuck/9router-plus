using RouterPlus.Core.Providers;

namespace RouterPlus.Core.Tests.Providers;

public sealed class ProviderQuotaAdditionalTests
{
    [Theory]
    [InlineData(80, true, false)]
    [InlineData(99.99, true, false)]
    [InlineData(100, true, true)]
    public void Quota_limit_flags_use_expected_percentage_boundaries(
        decimal used,
        bool expectedNearLimit,
        bool expectedOverLimit)
    {
        // Arrange
        var quota = new ProviderQuota("synthetic", used, 100m, 100m - used, null);

        // Act
        var isNearLimit = quota.IsNearLimit;
        var isOverLimit = quota.IsOverLimit;

        // Assert
        Assert.Equal(expectedNearLimit, isNearLimit);
        Assert.Equal(expectedOverLimit, isOverLimit);
    }

    [Fact]
    public void UsageText_returns_placeholder_when_only_one_value_is_present()
    {
        // Arrange
        var quota = new ProviderQuota("synthetic", 5m, null, null, null);

        // Act
        var usageText = quota.UsageText;

        // Assert
        Assert.Equal("Chưa có dữ liệu", usageText);
    }
}
