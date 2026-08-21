namespace RouterPlus.Core.Providers;

public enum ProviderHealthState
{
    Unknown,
    Missing,
    Healthy,
    Disabled,
    Error
}

public static class ProviderHealthStateResolver
{
    public static ProviderHealthState Resolve(
        bool isKnown,
        IReadOnlyCollection<ProviderConnection> connections)
    {
        ArgumentNullException.ThrowIfNull(connections);

        if (!isKnown)
        {
            return ProviderHealthState.Unknown;
        }

        if (connections.Count == 0)
        {
            return ProviderHealthState.Missing;
        }

        var activeConnections = connections
            .Where(connection => connection.IsActive)
            .ToArray();

        if (activeConnections.Length == 0)
        {
            return ProviderHealthState.Disabled;
        }

        if (activeConnections.Any(connection => connection.HasError))
        {
            return ProviderHealthState.Error;
        }

        if (activeConnections.Any(connection => connection.HasUnknownTestStatus))
        {
            return ProviderHealthState.Unknown;
        }

        return ProviderHealthState.Healthy;
    }
}
