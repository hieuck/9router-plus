using System.Net;
using System.Text;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Router;

namespace RouterPlus.Core.Tests;

public sealed class RouterApiClientLifecycleTests
{
    [Fact]
    public async Task Client_can_be_created_again_after_a_request_using_shared_http_client()
    {
        using var httpClient = new HttpClient(new StubHandler());
        var firstClient = new RouterApiClient(httpClient, "http://localhost:20128");

        await firstClient.ListConnectionsAsync(ProviderKind.Codex);

        var secondClient = new RouterApiClient(httpClient, "http://localhost:20128");
        var connections = await secondClient.ListConnectionsAsync(ProviderKind.Codex);

        Assert.Empty(connections);
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"connections\":[]}", Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
