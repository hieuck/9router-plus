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
        var initialConnections = await api.ListAllConnectionsAsync(cancellationToken, includeQuotas: true);

        var nearLimit = initialConnections.Any(connection =>
            connection.QuotaRows.Count > 0 && connection.IsNearLimit);

        var exhaustedConnections = initialConnections
            .Where(connection =>
                connection.IsActive &&
                QuotaAutoDisablePolicy.CanAutoDisable(connection))
            .ToArray();

        var disabledConnections = new List<ProviderConnection>();
        var disableFailures = new List<ConnectionDisableFailure>();

        foreach (var connection in exhaustedConnections)
        {
            try
            {
                await api.UpdateConnectionAsync(
                    connection.Id,
                    isActive: false,
                    cancellationToken: cancellationToken);
                disabledConnections.Add(connection);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                disableFailures.Add(new ConnectionDisableFailure(connection.Id, exception));
            }
        }

        var connections = initialConnections;
        Exception? followUpRefreshException = null;

        if (exhaustedConnections.Length > 0)
        {
            try
            {
                connections = await api.ListAllConnectionsAsync(cancellationToken, includeQuotas: true);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                followUpRefreshException = exception;
            }
        }

        return new ConnectionRefreshResult(
            connections,
            disabledConnections,
            disableFailures,
            nearLimit,
            followUpRefreshException);
    }
}
