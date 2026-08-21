using System.Net;
using System.Text;
using RouterPlus.Core.Updates;
using RouterPlus.Infrastructure.Updates;

namespace RouterPlus.Core.Tests;

public sealed class GitHubReleaseClientTests
{
    [Fact]
    public async Task GetLatestRelease_returns_available_stable_release_with_expected_assets()
    {
        var handler = new ReleaseHandler(CreateReleaseJson("v1.3.0", prerelease: false));
        using var httpClient = new HttpClient(handler);
        var client = new GitHubReleaseClient(httpClient, ReleaseVersion.Parse("1.2.0"));

        var result = await client.GetLatestReleaseAsync();

        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("1.3.0", result.AvailableVersion!.ToString());
        Assert.Equal("notes", result.ReleaseNotes);
        Assert.Equal("RouterPlus-v1.3.0-win-x64.zip", result.Archive!.Name);
        Assert.Equal("RouterPlus-v1.3.0-win-x64.zip.sha256", result.Checksum!.Name);
        Assert.Equal("RouterPlus-v1.3.0-manifest.json", result.Manifest!.Name);
    }

    [Fact]
    public async Task GetLatestRelease_ignores_prerelease_for_stable_channel()
    {
        var handler = new ReleaseHandler(CreateReleaseJson("v1.3.0-rc.1", prerelease: true));
        using var httpClient = new HttpClient(handler);
        var client = new GitHubReleaseClient(httpClient, ReleaseVersion.Parse("1.2.0"));

        var result = await client.GetLatestReleaseAsync();

        Assert.False(result.IsUpdateAvailable);
        Assert.Null(result.AvailableVersion);
    }

    [Fact]
    public async Task GetLatestRelease_returns_no_update_for_same_version()
    {
        var handler = new ReleaseHandler(CreateReleaseJson("v1.2.0", prerelease: false));
        using var httpClient = new HttpClient(handler);
        var client = new GitHubReleaseClient(httpClient, ReleaseVersion.Parse("1.2.0"));

        var result = await client.GetLatestReleaseAsync();

        Assert.False(result.IsUpdateAvailable);
    }

    [Fact]
    public async Task GetLatestRelease_rejects_unapproved_asset_host()
    {
        var json = CreateReleaseJson("v1.3.0", prerelease: false).Replace(
            "https://github.com/hieuck/9router-plus/releases/download/v1.3.0/RouterPlus-v1.3.0-manifest.json",
            "https://evil.example/update.json",
            StringComparison.Ordinal);
        var handler = new ReleaseHandler(json);
        using var httpClient = new HttpClient(handler);
        var client = new GitHubReleaseClient(httpClient, ReleaseVersion.Parse("1.2.0"));

        await Assert.ThrowsAsync<InvalidDataException>(() => client.GetLatestReleaseAsync());
    }

    [Fact]
    public async Task GetLatestRelease_does_not_send_application_data_or_tokens()
    {
        var handler = new ReleaseHandler(CreateReleaseJson("v1.3.0", prerelease: false));
        using var httpClient = new HttpClient(handler);
        var client = new GitHubReleaseClient(httpClient, ReleaseVersion.Parse("1.2.0"));

        await client.GetLatestReleaseAsync();

        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal("api.github.com", handler.RequestUri!.Host);
        Assert.DoesNotContain("token", handler.RequestUri.Query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("profile", handler.RequestUri.Query, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("email", handler.RequestUri.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Null(handler.RequestBody);
        Assert.DoesNotContain("Authorization", handler.Headers.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetLatestRelease_rejects_negative_asset_size()
    {
        var json = CreateReleaseJson("v1.3.0", prerelease: false).Replace(
            "\"size\":123",
            "\"size\":-1",
            StringComparison.Ordinal);
        var handler = new ReleaseHandler(json);
        using var httpClient = new HttpClient(handler);
        var client = new GitHubReleaseClient(httpClient, ReleaseVersion.Parse("1.2.0"));

        await Assert.ThrowsAsync<InvalidDataException>(() => client.GetLatestReleaseAsync());
    }

    [Fact]
    public async Task GetLatestRelease_rejects_duplicate_asset_names()
    {
        var json = """
            {
              "tag_name":"v1.3.0",
              "prerelease":false,
              "assets":[
                {"name":"RouterPlus-v1.3.0-win-x64.zip","browser_download_url":"https://github.com/hieuck/9router-plus/releases/download/v1.3.0/RouterPlus-v1.3.0-win-x64.zip","size":123},
                {"name":"RouterPlus-v1.3.0-win-x64.zip.sha256","browser_download_url":"https://github.com/hieuck/9router-plus/releases/download/v1.3.0/RouterPlus-v1.3.0-win-x64.zip.sha256","size":64},
                {"name":"RouterPlus-v1.3.0-manifest.json","browser_download_url":"https://github.com/hieuck/9router-plus/releases/download/v1.3.0/RouterPlus-v1.3.0-manifest.json","size":512},
                {"name":"RouterPlus-v1.3.0-manifest.json","browser_download_url":"https://github.com/hieuck/9router-plus/releases/download/v1.3.0/RouterPlus-v1.3.0-manifest.json","size":512}
              ]
            }
            """;
        var handler = new ReleaseHandler(json);
        using var httpClient = new HttpClient(handler);
        var client = new GitHubReleaseClient(httpClient, ReleaseVersion.Parse("1.2.0"));

        await Assert.ThrowsAsync<InvalidDataException>(() => client.GetLatestReleaseAsync());
    }

    private static string CreateReleaseJson(string tag, bool prerelease) => $$"""
        {
          "tag_name":"{{tag}}",
          "prerelease":{{prerelease.ToString().ToLowerInvariant()}},
          "body":"notes",
          "assets":[
            {"name":"RouterPlus-v1.3.0-win-x64.zip","browser_download_url":"https://github.com/hieuck/9router-plus/releases/download/v1.3.0/RouterPlus-v1.3.0-win-x64.zip","size":123},
            {"name":"RouterPlus-v1.3.0-win-x64.zip.sha256","browser_download_url":"https://github.com/hieuck/9router-plus/releases/download/v1.3.0/RouterPlus-v1.3.0-win-x64.zip.sha256","size":64},
            {"name":"RouterPlus-v1.3.0-manifest.json","browser_download_url":"https://github.com/hieuck/9router-plus/releases/download/v1.3.0/RouterPlus-v1.3.0-manifest.json","size":512}
          ]
        }
        """;

    private sealed class ReleaseHandler(string responseBody) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }

        public Uri? RequestUri { get; private set; }

        public string? RequestBody { get; private set; }

        public Dictionary<string, IEnumerable<string>> Headers { get; } = new(StringComparer.OrdinalIgnoreCase);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            foreach (var header in request.Headers)
            {
                Headers[header.Key] = header.Value;
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
