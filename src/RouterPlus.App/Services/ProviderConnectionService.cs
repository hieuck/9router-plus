using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Router;

namespace RouterPlus.App.Services;

public sealed class ProviderConnectionService : IProviderConnectionService
{
    public async Task<ConnectionRefreshResult> RefreshConnectionsAsync(
        Func<Task<IRouterApiClient>> apiFactory,
        CancellationToken cancellationToken)
    {
        if (apiFactory is null)
        {
            throw new ArgumentNullException(nameof(apiFactory));
        }

        var api = await apiFactory();
        var connections = await api.ListAllConnectionsAsync(cancellationToken);

        var exhaustedConnections = connections
            .Where(connection =>
                connection.IsActive &&
                QuotaAutoDisablePolicy.CanAutoDisable(connection))
            .ToArray();

        var disabledConnectionIds = new List<string>();
        var disableFailures = new List<ConnectionDisableFailure>();

        foreach (var connection in exhaustedConnections)
        {
            try
            {
                await api.UpdateConnectionAsync(
                    connection.Id,
                    isActive: false,
                    cancellationToken: cancellationToken);
                disabledConnectionIds.Add(connection.Id);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                disableFailures.Add(new ConnectionDisableFailure(connection.Id, exception.Message));
            }
        }

        if (exhaustedConnections.Length > 0)
        {
            connections = await api.ListAllConnectionsAsync(cancellationToken);
        }

        return new ConnectionRefreshResult(
            connections,
            disabledConnectionIds,
            disableFailures);
    }
}