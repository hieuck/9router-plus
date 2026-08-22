namespace RouterPlus.Core.Providers;

public sealed record ProviderConnection(
    string Id,
    ProviderKind Provider,
    string? Name,
    int Priority,
    bool IsActive,
    string? Email = null,
    DateTimeOffset? CreatedAt = null,
    string? TestStatus = null,
    string? ErrorCode = null,
    string? LastError = null,
    DateTimeOffset? LastErrorAt = null,
    long? UsageCount = null,
    long? LimitCount = null,
    DateTimeOffset? UsageResetAt = null,
    DateTimeOffset? ExpiresAt = null,
    int? ExpiresIn = null,
    DateTimeOffset? LastRefreshAt = null,
    IReadOnlyList<ProviderQuota>? Quotas = null)
{
    public IReadOnlyList<ProviderQuota> QuotaRows => Quotas ?? Array.Empty<ProviderQuota>();

    public bool IsDisabled => !IsActive;

    public bool HasUsageData => QuotaRows.Count > 0 || UsageCount.HasValue || LimitCount.HasValue;

    public double? UsagePercentage => QuotaRows.FirstOrDefault()?.UsagePercentage is { } quotaPercentage
        ? (double)quotaPercentage
        : LimitCount.HasValue && LimitCount.Value > 0
            ? (UsageCount ?? 0) * 100.0 / LimitCount.Value
            : null;

    public bool IsNearLimit => UsagePercentage.HasValue && UsagePercentage.Value >= 80;

    public bool IsOverLimit => UsagePercentage.HasValue && UsagePercentage.Value >= 100;

    public bool HasSuccessfulTestStatus => TestStatus is not null &&
        TestStatus.Trim().ToLowerInvariant() is "active" or "ok" or "healthy" or "available" or "ready" or "success" or "connected";

    public bool HasUnknownTestStatus => string.IsNullOrWhiteSpace(TestStatus)
        || (!HasSuccessfulTestStatus && !HasError);

    public bool HasError => !HasSuccessfulTestStatus &&
        !string.IsNullOrWhiteSpace(ErrorCode) ||
        !HasSuccessfulTestStatus &&
        (!string.IsNullOrWhiteSpace(LastError) ||
         string.Equals(TestStatus, "error", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(TestStatus, "expired", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(TestStatus, "unavailable", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(TestStatus, "invalid", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(TestStatus, "failed", StringComparison.OrdinalIgnoreCase));
}

public sealed record ProviderQuota(
    string Name,
    decimal? Used,
    decimal? Total,
    decimal? Remaining,
    DateTimeOffset? ResetAt)
{
    public decimal? UsagePercentage => Total is > 0
        ? (Used ?? 0m) * 100m / Total.Value
        : null;

    public string UsageText => Used.HasValue && Total.HasValue
        ? $"{FormatValue(Used.Value)} / {FormatValue(Total.Value)}"
        : "Chưa có dữ liệu";

    public string PercentageText => UsagePercentage.HasValue
        ? $"{UsagePercentage.Value:0.##}%"
        : "—";

    public string ResetText => ResetAt.HasValue
        ? ResetAt.Value.ToLocalTime().ToString("g", System.Globalization.CultureInfo.CurrentCulture)
        : "N/A";

    public bool IsNearLimit => UsagePercentage >= 80m;

    public bool IsOverLimit => UsagePercentage >= 100m;

    private static string FormatValue(decimal value) =>
        value.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture);
}
