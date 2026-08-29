using System.Net;
using System.Text;
using RouterPlus.Core.Updates;
using RouterPlus.Infrastructure.Updates;

namespace RouterPlus.Core.Tests;

public sealed class GitHubReleaseClientHttpErrorTests
{
    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task GetLatestRelease_throws_http_exception_with_response_status(HttpStatusCode statusCode)
    {
        using var httpClient = new HttpClient(new StatusHandler(statusCode));
        var client = new GitHubReleaseClient(httpClient, ReleaseVersion.Parse("0.2.0"));

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetLatestReleaseAsync());

        Assert.Equal(statusCode, exception.StatusCode);
    }

    private sealed class StatusHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent("{\"message\":\"synthetic failure\"}", Encoding.UTF8, "application/json")
            });
    }
}
