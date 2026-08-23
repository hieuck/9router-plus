using RouterPlus.Core.Providers;

namespace RouterPlus.App.ViewModels;

public sealed record QuotaResetSuggestion(
    string ConnectionId,
    ProviderKind Provider,
    string? Name,
    DateTimeOffset ResetAt)
{
    public string ProviderName => ProviderCatalog.Get(Provider).DisplayName;

    public string ConnectionName => string.IsNullOrWhiteSpace(Name) ? ConnectionId : Name;

    public string Message => $"Quota {ProviderName} của connection {ConnectionName} đã reset. Connection hiện vẫn đang tắt.";

    public string ResetText => $"Reset lúc {ResetAt.ToLocalTime():g}";
}
