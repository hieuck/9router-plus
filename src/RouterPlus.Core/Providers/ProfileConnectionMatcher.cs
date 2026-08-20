using RouterPlus.Core.Chrome;

namespace RouterPlus.Core.Providers;

public static class ProfileConnectionMatcher
{
    public static IReadOnlyDictionary<ProviderKind, int> CountByProvider(
        ChromeProfile profile,
        IEnumerable<ProviderConnection> connections)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(connections);

        var profileName = profile.Name.Trim();
        return ProviderCatalog.All.ToDictionary(
            definition => definition.Kind,
            definition => connections.Count(connection =>
                connection.Provider == definition.Kind &&
                string.Equals(connection.Name?.Trim(), profileName, StringComparison.OrdinalIgnoreCase)));
    }
}
