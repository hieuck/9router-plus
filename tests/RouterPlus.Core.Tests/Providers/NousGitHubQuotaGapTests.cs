using RouterPlus.Core.Providers;
using RouterPlus.Core.Tests.TestHelpers;
using Xunit;

namespace RouterPlus.Core.Tests.Providers;

/// <summary>
/// Pins the explicit quota gaps for Nous/GitHub: no usage inference and no
/// quota-based auto-disable; recovery falls back to !IsOverLimit.
/// </summary>
public sealed class NousGitHubQuotaGapTests
{
    [Theory]
    [InlineData(ProviderKind.Nous)]
    [InlineData(ProviderKind.GitHub)]
    public void InferUsageFromError_ReturnsNull_ForProvidersWithoutUsageApi(ProviderKind provider)
    {
        // Arrange - limit-style errors must not be turned into fabricated numbers.
        var messages = new[]
        {
            "[429]: Rate limit exceeded",
            "[402]: usage limit reached",
            "You have reached the limit",
            "quota exhausted"
        };

        foreach (var message in messages)
        {
            // Act
            var result = UsageInferenceService.InferUsageFromError(
                provider,
                "429",
                message,
                new DateTimeOffset(2026, 9, 15, 10, 30, 0, TimeSpan.Zero));

            // Assert - no 100/100 or 5000/5000 style fabricated usage.
            Assert.Null(result);
        }
    }

    [Theory]
    [InlineData(ProviderKind.Nous)]
    [InlineData(ProviderKind.GitHub)]
    public void CanAutoDisable_IsFalse_EvenWhenOverLimit(ProviderKind provider)
    {
        // Arrange - IsOverLimit is true via 100/100 usage.
        var connection = TestData.CreateConnection(provider, quotas: null, usageCount: 100, limitCount: 100);

        // Act
        var result = QuotaAutoDisablePolicy.CanAutoDisable(connection);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData(ProviderKind.Nous)]
    [InlineData(ProviderKind.GitHub)]
    public void HasRecovered_UsesLegacyFallback(ProviderKind provider)
    {
        // Arrange - below limit => recovered; over limit => not recovered.
        var belowLimit = TestData.CreateConnection(provider, quotas: null, usageCount: 99, limitCount: 100);
        var overLimit = TestData.CreateConnection(provider, quotas: null, usageCount: 100, limitCount: 100);

        // Act & Assert
        Assert.True(QuotaAutoDisablePolicy.HasRecovered(belowLimit));
        Assert.False(QuotaAutoDisablePolicy.HasRecovered(overLimit));
    }
}
