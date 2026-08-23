using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Router;
using Xunit;

namespace RouterPlus.Core.Tests;

public sealed class RouterApiDeleteConnectionTests
{
    [Fact]
    public async Task DeleteConnectionAsync_ShouldSendDeleteRequest()
    {
        // Arrange
        using var httpClient = new HttpClient(new TestHttpMessageHandler(request =>
        {
            Assert.Equal(HttpMethod.Delete, request.Method);
            Assert.EndsWith("/api/providers/test-connection-id", request.RequestUri?.ToString());
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        }));

        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        // Act
        await api.DeleteConnectionAsync("test-connection-id");

        // Assert - handled by TestHttpMessageHandler
    }

    [Fact]
    public async Task DeleteConnectionAsync_WithNullId_ShouldThrow()
    {
        // Arrange
        using var httpClient = new HttpClient();
        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(async () =>
            await api.DeleteConnectionAsync(null!));
    }

    [Fact]
    public async Task DeleteConnectionAsync_WithEmptyId_ShouldThrow()
    {
        // Arrange
        using var httpClient = new HttpClient();
        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await api.DeleteConnectionAsync(""));
    }

    [Fact]
    public async Task DeleteConnectionAsync_WithWhitespaceId_ShouldThrow()
    {
        // Arrange
        using var httpClient = new HttpClient();
        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await api.DeleteConnectionAsync("   "));
    }

    [Fact]
    public async Task DeleteConnectionAsync_WithFailedResponse_ShouldThrow()
    {
        // Arrange
        using var httpClient = new HttpClient(new TestHttpMessageHandler(_ =>
            new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
            {
                Content = new StringContent("Connection not found")
            }));

        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        // Act & Assert
        var exception = await Assert.ThrowsAsync<RouterApiException>(async () =>
            await api.DeleteConnectionAsync("non-existent-id"));

        Assert.Contains("404", exception.Message);
    }

    private sealed class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }
}
