namespace RouterPlus.Core.Providers;

public static class PriorityCalculator
{
    public static int Next(IEnumerable<ProviderConnection> connections)
    {
        ArgumentNullException.ThrowIfNull(connections);

        var maximum = connections.Select(connection => connection.Priority).DefaultIfEmpty(0).Max();
        return Math.Max(0, maximum) + 1;
    }
}
