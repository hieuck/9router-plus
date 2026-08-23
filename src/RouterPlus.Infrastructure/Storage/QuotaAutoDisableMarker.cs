using RouterPlus.Core.Providers;

namespace RouterPlus.Infrastructure.Storage;

public sealed record QuotaAutoDisableMarker(
    string ConnectionId,
    ProviderKind Provider,
    string? Name,
    DateTimeOffset? ResetAt);
