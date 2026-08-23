using RouterPlus.Core.Providers;

namespace RouterPlus.Core.Tests;

public sealed class QuotaAutoDisablePolicyTests
{
    [Fact]
    public void Kiro_is_not_eligible_when_only_one_of_multiple_buckets_is_exhausted()
    {
        var connection = new ProviderConnection(
            "kiro-1",
            ProviderKind.Kiro,
            "Work",
            1,
            true,
            Quotas:
            [
                new ProviderQuota("credit", 50m, 50m, 0m, DateTimeOffset.UtcNow.AddHours(1)),
                new ProviderQuota("credit_freetrial", 0m, 500m, 500m, DateTimeOffset.UtcNow.AddHours(1))
            ]);

        Assert.False(QuotaAutoDisablePolicy.CanAutoDisable(connection));
    }

    [Fact]
    public void Kiro_is_eligible_when_all_explicit_buckets_are_exhausted_with_reset_times()
    {
        var connection = new ProviderConnection(
            "kiro-1",
            ProviderKind.Kiro,
            "Work",
            1,
            true,
            Quotas:
            [
                new ProviderQuota("credit", 50m, 50m, 0m, DateTimeOffset.UtcNow.AddHours(1)),
                new ProviderQuota("credit_freetrial", 500m, 500m, 0m, DateTimeOffset.UtcNow.AddHours(1))
            ]);

        Assert.True(QuotaAutoDisablePolicy.CanAutoDisable(connection));
    }

    [Fact]
    public void Kiro_is_not_eligible_without_explicit_quota_rows()
    {
        var connection = new ProviderConnection(
            "kiro-1",
            ProviderKind.Kiro,
            "Work",
            1,
            true,
            UsageCount: 100,
            LimitCount: 100);

        Assert.False(QuotaAutoDisablePolicy.CanAutoDisable(connection));
    }
}
