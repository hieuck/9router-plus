using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Router;

namespace RouterPlus.App.Services;

public sealed record ConnectionDisableFailure(string ConnectionId, Exception Exception);

public sealed record ConnectionRefreshResult(
    IReadOnlyList<ProviderConnection> Connections,
    IReadOnlyList<ProviderConnection> DisabledConnections,
    IReadOnlyList<ConnectionDisableFailure> DisableFailures,
    bool NearLimit,
    Exception? FollowUpRefreshException);

public interface IProviderConnectionService
{
    Task<ConnectionRefreshResult> RefreshConnectionsAsync(
        Func<Task<IRouterApiClient>> apiFactory,
        CancellationToken cancellationToken);
}
