using RouterPlus.Core.Providers;

namespace RouterPlus.Core.Chrome;

public static class ChromeProfileFilter
{
    public static IReadOnlyList<ChromeProfile> Filter(
        IEnumerable<ChromeProfile> profiles,
        string? query)
    {
        return Filter(profiles, query, null);
    }

    public static IReadOnlyList<ChromeProfile> Filter(
        IEnumerable<ChromeProfile> profiles,
        string? query,
        IReadOnlyDictionary<string, ProviderKind>? providerByProfileId)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        var normalizedQuery = query?.Trim();
        IEnumerable<ChromeProfile> result = profiles;

        if (!string.IsNullOrEmpty(normalizedQuery))
        {
            result = result.Where(profile =>
                profile.Name.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
                profile.DirectoryName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase));
        }

        if (providerByProfileId is { Count: > 0 })
        {
            result = result.Where(profile => providerByProfileId.ContainsKey(profile.Id));
        }

        return result.ToArray();
    }
}
