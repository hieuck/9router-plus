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
    DateTimeOffset? LastErrorAt = null)
{
    public bool HasError =>
        !string.IsNullOrWhiteSpace(ErrorCode) ||
        !string.IsNullOrWhiteSpace(LastError) ||
        string.Equals(TestStatus, "error", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(TestStatus, "expired", StringComparison.OrdinalIgnoreCase);
}
