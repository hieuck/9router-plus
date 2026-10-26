using System.Collections.Generic;
using System.Linq;
using RouterPlus.Core.Chrome;

namespace RouterPlus.Core.Providers;

public static class ProfileConnectionMatcher
{
    /// <summary>
    /// Counts connections by provider that match the given profile by name (case-insensitive, trimmed).
    /// </summary>
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

    /// <summary>
    /// Determines if a profile name looks like an email address (contains '@').
    /// Uses normalized (trimmed) name.
    /// </summary>
    private static bool IsEmailLike(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Contains('@');

    /// <summary>
    /// Normalizes a name for matching (trim, case-insensitive comparison).
    /// </summary>
    public static string NormalizeName(string? name) =>
        name?.Trim() ?? string.Empty;

    /// <summary>
    /// Finds matching connections for a specific profile and provider kind.
    /// Returns empty list if profile name is ambiguous (shared by multiple profiles in the all-profiles list).
    /// </summary>
    public static IReadOnlyList<ProviderConnection> MatchConnections(
        ChromeProfile profile,
        ProviderKind providerKind,
        IEnumerable<ProviderConnection> connections,
        IEnumerable<ChromeProfile> allProfiles)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(allProfiles);

        var normalizedProfileName = NormalizeName(profile.Name);

        // Check for ambiguity: multiple profiles with same normalized name (case-insensitive)
        var ambiguousNames = allProfiles
            .GroupBy(p => NormalizeName(p.Name), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (ambiguousNames.Contains(normalizedProfileName))
        {
            return Array.Empty<ProviderConnection>();
        }

        var matches = connections
            .Where(c => c.Provider == providerKind)
            .Where(c => string.Equals(NormalizeName(c.Name), normalizedProfileName, StringComparison.OrdinalIgnoreCase))
            .Where(c =>
            {
                // If connection has email AND profile name looks like email, require email match
                // Use normalized profile name for consistency (trim/case)
                if (!string.IsNullOrWhiteSpace(c.Email) && IsEmailLike(normalizedProfileName))
                {
                    return string.Equals(c.Email, normalizedProfileName, StringComparison.OrdinalIgnoreCase);
                }
                // If profile name is not email-like, skip email check
                return true;
            })
            .OrderBy(c => c.Priority)
            .ThenBy(c => c.CreatedAt ?? DateTimeOffset.MaxValue)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .ToArray();

        return matches;
    }

    /// <summary>
    /// Gets all matching connections grouped by provider kind for a profile.
    /// Returns empty collections for ambiguous profile names.
    /// </summary>
    public static IReadOnlyDictionary<ProviderKind, IReadOnlyList<ProviderConnection>> MatchAllProviders(
        ChromeProfile profile,
        IEnumerable<ProviderConnection> connections,
        IEnumerable<ChromeProfile> allProfiles)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(allProfiles);

        var result = new Dictionary<ProviderKind, IReadOnlyList<ProviderConnection>>();
        var connectionList = connections.ToArray();

        foreach (var definition in ProviderCatalog.All)
        {
            result[definition.Kind] = MatchConnections(profile, definition.Kind, connectionList, allProfiles);
        }

        return result;
    }

    /// <summary>
    /// Identifies profile names that are ambiguous (shared by multiple profiles).
    /// Returns a set of normalized names that appear more than once (case-insensitive).
    /// </summary>
    public static IReadOnlySet<string> GetAmbiguousProfileNames(IEnumerable<ChromeProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        return profiles
            .GroupBy(p => NormalizeName(p.Name), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }
}