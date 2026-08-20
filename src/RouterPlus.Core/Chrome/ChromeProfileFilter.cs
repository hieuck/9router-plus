namespace RouterPlus.Core.Chrome;

public static class ChromeProfileFilter
{
    public static IReadOnlyList<ChromeProfile> Filter(
        IEnumerable<ChromeProfile> profiles,
        string? query)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        var normalizedQuery = query?.Trim();
        if (string.IsNullOrEmpty(normalizedQuery))
        {
            return profiles.ToArray();
        }

        return profiles
            .Where(profile =>
                profile.Name.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
                profile.DirectoryName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }
}
