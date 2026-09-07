using RouterPlus.Core.Providers;

namespace RouterPlus.Core.Tests;

public sealed class QuotaAutoDisablePolicyTests
{
    private static readonly DateTimeOffset ResetAt = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(ProviderKind.Codex)]
    [InlineData(ProviderKind.Ollama)]
    public void CanAutoDisable_returns_connection_limit_state_for_supported_simple_providers(ProviderKind provider)
    {
        // Arrange
        var overLimit = CreateConnection(provider, quotas: null, usageCount: 100, limitCount: 100);
        var belowLimit = CreateConnection(provider, quotas: null, usageCount: 99, limitCount: 100);

        // Act
        var overLimitResult = QuotaAutoDisablePolicy.CanAutoDisable(overLimit);
        var belowLimitResult = QuotaAutoDisablePolicy.CanAutoDisable(belowLimit);

        // Assert
        Assert.True(overLimitResult);
        Assert.False(belowLimitResult);
    }

    [Theory]
    [InlineData(ProviderKind.OpenRouter)]
    [InlineData(ProviderKind.GitHub)]
    [InlineData(ProviderKind.Kimchi)]
    public void CanAutoDisable_returns_false_for_providers_without_auto_disable_policy(ProviderKind provider)
    {
        // Arrange
        var connection = CreateConnection(provider, quotas: null, usageCount: 100, limitCount: 100);

        // Act
        var result = QuotaAutoDisablePolicy.CanAutoDisable(connection);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CanAutoDisable_returns_false_for_kiro_without_explicit_quota_rows()
    {
        // Arrange
        var connection = CreateConnection(ProviderKind.Kiro, quotas: null, usageCount: 100, limitCount: 100);

        // Act
        var result = QuotaAutoDisablePolicy.CanAutoDisable(connection);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CanAutoDisable_returns_true_for_kiro_when_all_buckets_are_exhausted_and_resettable()
    {
        // Arrange
        var connection = CreateConnection(
            ProviderKind.Kiro,
            [
                new ProviderQuota("credit", 50m, 50m, 0m, ResetAt),
                new ProviderQuota("credit_freetrial", 500m, 500m, 0m, ResetAt)
            ]);

        // Act
        var result = QuotaAutoDisablePolicy.CanAutoDisable(connection);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CanAutoDisable_returns_false_for_kiro_when_any_bucket_is_not_exhausted()
    {
        // Arrange
        var connection = CreateConnection(
            ProviderKind.Kiro,
            [
                new ProviderQuota("credit", 50m, 50m, 0m, ResetAt),
                new ProviderQuota("credit_freetrial", 0m, 500m, 500m, ResetAt)
            ]);

        // Act
        var result = QuotaAutoDisablePolicy.CanAutoDisable(connection);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CanAutoDisable_returns_false_for_kiro_when_bucket_has_no_reset_time()
    {
        // Arrange
        var connection = CreateConnection(
            ProviderKind.Kiro,
            [new ProviderQuota("credit", 50m, 50m, 0m, null)]);

        // Act
        var result = QuotaAutoDisablePolicy.CanAutoDisable(connection);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CanAutoDisable_returns_false_for_kiro_when_bucket_total_is_not_positive()
    {
        // Arrange
        var connection = CreateConnection(
            ProviderKind.Kiro,
            [new ProviderQuota("credit", 0m, 0m, 0m, ResetAt)]);

        // Act
        var result = QuotaAutoDisablePolicy.CanAutoDisable(connection);

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData(ProviderKind.Codex)]
    [InlineData(ProviderKind.OpenRouter)]
    [InlineData(ProviderKind.GitHub)]
    [InlineData(ProviderKind.Ollama)]
    [InlineData(ProviderKind.Kimchi)]
    public void HasRecovered_returns_true_for_non_kiro_connections_below_limit(ProviderKind provider)
    {
        // Arrange
        var connection = CreateConnection(provider, quotas: null, usageCount: 99, limitCount: 100);

        // Act
        var result = QuotaAutoDisablePolicy.HasRecovered(connection);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasRecovered_returns_false_for_non_kiro_connections_still_over_limit()
    {
        // Arrange
        var connection = CreateConnection(ProviderKind.Codex, quotas: null, usageCount: 100, limitCount: 100);

        // Act
        var result = QuotaAutoDisablePolicy.HasRecovered(connection);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasRecovered_returns_true_for_kiro_when_all_positive_buckets_are_below_limit()
    {
        // Arrange
        var connection = CreateConnection(
            ProviderKind.Kiro,
            [
                new ProviderQuota("credit", 10m, 50m, 40m, ResetAt),
                new ProviderQuota("credit_freetrial", 100m, 500m, 400m, ResetAt)
            ]);

        // Act
        var result = QuotaAutoDisablePolicy.HasRecovered(connection);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void HasRecovered_returns_false_for_kiro_without_explicit_quota_rows()
    {
        // Arrange
        var connection = CreateConnection(ProviderKind.Kiro, quotas: null, usageCount: 0, limitCount: 100);

        // Act
        var result = QuotaAutoDisablePolicy.HasRecovered(connection);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasRecovered_returns_false_for_kiro_when_any_bucket_is_over_limit()
    {
        // Arrange
        var connection = CreateConnection(
            ProviderKind.Kiro,
            [
                new ProviderQuota("credit", 10m, 50m, 40m, ResetAt),
                new ProviderQuota("credit_freetrial", 500m, 500m, 0m, ResetAt)
            ]);

        // Act
        var result = QuotaAutoDisablePolicy.HasRecovered(connection);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HasRecovered_returns_false_for_kiro_when_bucket_total_is_not_positive()
    {
        // Arrange
        var connection = CreateConnection(
            ProviderKind.Kiro,
            [new ProviderQuota("credit", 0m, 0m, 0m, ResetAt)]);

        // Act
        var result = QuotaAutoDisablePolicy.HasRecovered(connection);

        // Assert
        Assert.False(result);
    }

    private static ProviderConnection CreateConnection(
        ProviderKind provider,
        IReadOnlyList<ProviderQuota>? quotas,
        long? usageCount = null,
        long? limitCount = null) =>
        new(
            "synthetic-connection",
            provider,
            "Synthetic",
            1,
            true,
            UsageCount: usageCount,
            LimitCount: limitCount,
            Quotas: quotas);
}
