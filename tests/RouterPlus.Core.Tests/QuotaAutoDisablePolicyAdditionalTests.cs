using RouterPlus.Core.Providers;

namespace RouterPlus.Core.Tests;

public sealed class QuotaAutoDisablePolicyAdditionalTests
{
    [Theory]
    [InlineData(ProviderKind.Codex)]
    [InlineData(ProviderKind.Ollama)]
    public void CanAutoDisable_uses_over_limit_for_supported_legacy_providers(ProviderKind provider)
    {
        // Arrange
        var connection = Connection(provider, isOverLimit: true);

        // Act
        var canAutoDisable = QuotaAutoDisablePolicy.CanAutoDisable(connection);

        // Assert
        Assert.True(canAutoDisable);
    }

    [Fact]
    public void CanAutoDisable_returns_false_for_unsupported_provider()
    {
        // Arrange
        var connection = Connection(ProviderKind.OpenRouter, isOverLimit: true);

        // Act
        var canAutoDisable = QuotaAutoDisablePolicy.CanAutoDisable(connection);

        // Assert
        Assert.False(canAutoDisable);
    }

    [Fact]
    public void HasRecovered_returns_true_for_non_kiro_connection_below_limit()
    {
        // Arrange
        var connection = Connection(ProviderKind.Codex, isOverLimit: false);

        // Act
        var hasRecovered = QuotaAutoDisablePolicy.HasRecovered(connection);

        // Assert
        Assert.True(hasRecovered);
    }

    [Fact]
    public void HasRecovered_returns_false_for_kiro_without_explicit_quota_rows()
    {
        // Arrange
        var connection = new ProviderConnection(
            "synthetic-kiro",
            ProviderKind.Kiro,
            "Synthetic",
            1,
            false);

        // Act
        var hasRecovered = QuotaAutoDisablePolicy.HasRecovered(connection);

        // Assert
        Assert.False(hasRecovered);
    }

    [Fact]
    public void HasRecovered_returns_true_for_kiro_when_all_buckets_are_below_limit()
    {
        // Arrange
        var connection = new ProviderConnection(
            "synthetic-kiro",
            ProviderKind.Kiro,
            "Synthetic",
            1,
            false,
            Quotas:
            [
                new ProviderQuota("credit", 20m, 100m, 80m, null),
                new ProviderQuota("trial", 5m, 10m, 5m, null)
            ]);

        // Act
        var hasRecovered = QuotaAutoDisablePolicy.HasRecovered(connection);

        // Assert
        Assert.True(hasRecovered);
    }

    private static ProviderConnection Connection(ProviderKind provider, bool isOverLimit) =>
        new(
            "synthetic-connection",
            provider,
            "Synthetic",
            1,
            true,
            UsageCount: isOverLimit ? 100 : 20,
            LimitCount: 100);
}
