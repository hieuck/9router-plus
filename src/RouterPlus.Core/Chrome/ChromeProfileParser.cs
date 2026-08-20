using System.Text.Json;

namespace RouterPlus.Core.Chrome;

public static class ChromeProfileParser
{
    public static IReadOnlyList<ChromeProfile> Parse(string userDataDirectory, string localStateJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userDataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(localStateJson);

        using var document = JsonDocument.Parse(localStateJson);
        if (!document.RootElement.TryGetProperty("profile", out var profile) ||
            !profile.TryGetProperty("info_cache", out var cache) ||
            cache.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<ChromeProfile>();
        }

        return cache.EnumerateObject()
            .Select(entry =>
            {
                var name = entry.Value.TryGetProperty("name", out var nameElement)
                    ? nameElement.GetString()
                    : null;
                var directoryName = entry.Name;
                var displayName = string.IsNullOrWhiteSpace(name) ? directoryName : name.Trim();
                return new ChromeProfile(
                    ChromeProfile.CreateId(userDataDirectory, directoryName),
                    displayName,
                    directoryName,
                    userDataDirectory,
                    string.Equals(directoryName, "Default", StringComparison.OrdinalIgnoreCase));
            })
            .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(profile => profile.DirectoryName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
