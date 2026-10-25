using RouterPlus.Core.Chrome;
using RouterPlus.Core.Models;
using RouterPlus.Core.Providers;

namespace RouterPlus.App.Services;

public sealed record ProviderConnectionRowItem(
    ProviderKind Kind,
    ProviderAuthConnection? Connection);

public sealed record ProviderConnectionRowData(
    ChromeProfile Profile,
    ProviderAuthConnection? CodexConnection,
    IReadOnlyList<ProviderConnectionRowItem> ProviderConnections);

public interface IProviderConnectionRowService
{
    Task<ProviderConnectionRowData> LoadAsync(
        ChromeProfile profile,
        CancellationToken cancellationToken = default);
}
