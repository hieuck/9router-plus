using System.Net;
using System.Text;
using RouterPlus.Core.Updates;
using RouterPlus.Infrastructure.Updates;

namespace RouterPlus.Infrastructure.Tests.Updates;

public sealed class GitHubReleaseClientTests
{
    [Fact]
    public async Task GetLatestRelease_selects_highest_stable_release_and_sends_expected_headers()
    {
        // Arrange
        var response = "[" + CreateReleaseJson("v1.2.0", prerelease: false) + "," + CreateReleaseJson("v1.4.0", prerelease: false) + "]";
        var handler = new ReleaseHandler(response);
        using var httpClient = new HttpClient(handler);
        var client = new GitHubReleaseClient(httpClient, ReleaseVersion.Parse("1.0.0"));

        // Act
        var result = await client.GetLatestReleaseAsync();

        // Assert
        Assert.True(result.IsUpdateAvailable);
        Assert.Equal("1.4.0", result.AvailableVersion!.ToString());
        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal("RouterPlus/1.0.0", handler.UserAgent);
        Assert.Equal("application/vnd.github+json", handler.Accept);
    }

    [Fact]
    public async Task GetLatestRelease_rejects_redirected_metadata_endpoint()
    {
        // Arrange
        var handler = new ReleaseHandler(CreateReleaseJson("v1.1.0", prerelease: false),
            new Uri("https://evil.example/releases"));
        using var httpClient = new HttpClient(handler);
        var client = new GitHubReleaseClient(httpClient, ReleaseVersion.Parse("1.0.0"));

        // Act
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => client.GetLatestReleaseAsync());

        // Assert
        Assert.Contains("redirect is not allowed", exception.Message);
    }

    [Fact]
    public async Task GetLatestRelease_accepts_single_release_object_response()
    {
        // Arrange
        var handler = new ReleaseHandler(CreateReleaseJson("v1.1.0", prerelease: false));
        using var httpClient = new HttpClient(handler);
        var client = new GitHubReleaseClient(httpClient, ReleaseVersion.Parse("1.0.0"));

        // Act
        var result = await client.GetLatestReleaseAsync();

        // Assert
        Assert.Equal("1.1.0", result.AvailableVersion!.ToString());
    }

    private static string CreateReleaseJson(string tag, bool prerelease)
    {
        var archiveName = $"RouterPlus-{tag}-win-x64.zip";
        var checksumName = $"{archiveName}.sha256";
        var releaseBaseUri = $"https://github.com/hieuck/9router-plus/releases/download/{tag}";
        return $$"""
            {
              "tag_name":"{{tag}}",
              "prerelease":{{prerelease.ToString().ToLowerInvariant()}},
              "body":"notes",
              "assets":[
                {"name":"{{archiveName}}","browser_download_url":"{{releaseBaseUri}}/{{archiveName}}","size":123},
                {"name":"{{checksumName}}","browser_download_url":"{{releaseBaseUri}}/{{checksumName}}","size":64}
              ]
            }
            """;
    }

    private sealed class ReleaseHandler(string responseBody, Uri? responseUri = null) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? UserAgent { get; private set; }
        public string? Accept { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            UserAgent = request.Headers.UserAgent.ToString();
            Accept = request.Headers.Accept.ToString();
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
            response.RequestMessage = new HttpRequestMessage(request.Method, responseUri ?? request.RequestUri);
            return Task.FromResult(response);
        }
    }
}
