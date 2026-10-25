using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Router;

namespace RouterPlus.App.Services;

public sealed record ConnectionDisableFailure(string ConnectionId, string Error);

public sealed record ConnectionRefreshResult(
    IReadOnlyList<ProviderConnection> Connections,
    IReadOnlyList<string> DisabledConnectionIds,
    IReadOnlyList<ConnectionDisableFailure> DisableFailures);

public interface IProviderConnectionService
{
    Task<ConnectionRefreshResult> RefreshConnectionsAsync(
        Func<Task<IRouterApiClient>> apiFactory,
        CancellationToken cancellationToken);
}