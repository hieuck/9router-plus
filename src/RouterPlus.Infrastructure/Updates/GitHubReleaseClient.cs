using System.Net.Http.Headers;
using System.Text.Json;
using RouterPlus.Core.Updates;

namespace RouterPlus.Infrastructure.Updates;

public sealed class GitHubReleaseClient
{
    private static readonly Uri LatestReleaseUri = new("https://api.github.com/repos/hieuck/9router-plus/releases/latest");
    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "github.com",
        "api.github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com",
        "github-releases.githubusercontent.com"
    };

    private readonly HttpClient _httpClient;
    private readonly ReleaseVersion _currentVersion;

    public GitHubReleaseClient(HttpClient httpClient, ReleaseVersion currentVersion)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _currentVersion = currentVersion ?? throw new ArgumentNullException(nameof(currentVersion));
    }

    public async Task<ReleaseCheckResult> GetLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUri);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("RouterPlus", _currentVersion.ToString()));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        var finalUri = response.RequestMessage?.RequestUri ?? request.RequestUri;
        if (finalUri is null
            || !string.Equals(finalUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(finalUri.Host, "api.github.com", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(finalUri.AbsolutePath, LatestReleaseUri.AbsolutePath, StringComparison.Ordinal))
        {
            throw new InvalidDataException("GitHub release metadata redirect is not allowed.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"GitHub release metadata request failed with {(int)response.StatusCode}.",
                null,
                response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var tag = GetRequiredString(root, "tag_name");
        if (!tag.StartsWith('v'))
        {
            throw new InvalidDataException("GitHub release tag must use the vMAJOR.MINOR.PATCH format.");
        }

        var version = ReleaseVersion.Parse(tag[1..]);
        var prerelease = GetRequiredBoolean(root, "prerelease");
        if (prerelease || version.CompareTo(_currentVersion) <= 0)
        {
            return new ReleaseCheckResult(_currentVersion, null, null, null, null, null);
        }

        var assets = ParseAssets(root);
        var archive = RequireAsset(assets, $"RouterPlus-{tag}-win-x64.zip");
        var checksum = RequireAsset(assets, $"RouterPlus-{tag}-win-x64.zip.sha256");
        var manifest = RequireAsset(assets, $"RouterPlus-{tag}-manifest.json");
        ValidateAssetUri(archive.DownloadUri);
        ValidateAssetUri(checksum.DownloadUri);
        ValidateAssetUri(manifest.DownloadUri);

        return new ReleaseCheckResult(
            _currentVersion,
            version,
            GetString(root, "body"),
            archive,
            checksum,
            manifest);
    }

    private static IReadOnlyDictionary<string, ReleaseAsset> ParseAssets(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assetsElement) ||
            assetsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("GitHub release metadata does not contain an assets array.");
        }

        var assets = new Dictionary<string, ReleaseAsset>(StringComparer.Ordinal);
        foreach (var assetElement in assetsElement.EnumerateArray())
        {
            var name = GetRequiredString(assetElement, "name");
            var downloadUrl = GetRequiredUri(assetElement, "browser_download_url");
            var length = assetElement.TryGetProperty("size", out var sizeElement) &&
                         sizeElement.ValueKind == JsonValueKind.Number &&
                         sizeElement.TryGetInt64(out var parsedLength)
                ? parsedLength
                : 0;
            if (length < 0)
            {
                throw new InvalidDataException("GitHub release asset size cannot be negative.");
            }

            ValidateAssetUri(downloadUrl);
            if (!assets.TryAdd(name, new ReleaseAsset(name, downloadUrl, length, null, true)))
            {
                throw new InvalidDataException($"GitHub release contains duplicate asset '{name}'.");
            }
        }

        return assets;
    }

    private static ReleaseAsset RequireAsset(IReadOnlyDictionary<string, ReleaseAsset> assets, string name) =>
        assets.TryGetValue(name, out var asset)
            ? asset
            : throw new InvalidDataException($"GitHub release is missing required asset '{name}'.");

    public static bool IsAllowedAssetHost(string host) => AllowedHosts.Contains(host);

    public static void ValidateAssetUri(Uri uri)
    {
        if (!uri.IsAbsoluteUri || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Release assets must use HTTPS URLs.");
        }

        if (!IsAllowedAssetHost(uri.Host))
        {
            throw new InvalidDataException($"Release asset host is not allowed: {uri.Host}");
        }
    }

    private static string GetRequiredString(JsonElement root, string propertyName)
    {
        var value = GetString(root, propertyName);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidDataException($"GitHub release metadata is missing '{propertyName}'.")
            : value;
    }

    private static string? GetString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool GetRequiredBoolean(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.True ||
        root.TryGetProperty(propertyName, out value) && value.ValueKind == JsonValueKind.False
            ? value.GetBoolean()
            : throw new InvalidDataException($"GitHub release metadata is missing '{propertyName}'.");

    private static Uri GetRequiredUri(JsonElement root, string propertyName)
    {
        var value = GetRequiredString(root, propertyName);
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            ? uri
            : throw new InvalidDataException($"GitHub release metadata contains an invalid URI in '{propertyName}'.");
    }
}
