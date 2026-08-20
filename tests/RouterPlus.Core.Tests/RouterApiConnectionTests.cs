using System.Net;
using System.Text;
using System.Text.Json;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Router;

namespace RouterPlus.Core.Tests;

public sealed class RouterApiConnectionTests
{
    [Fact]
    public async Task ListAllConnections_parses_known_connections_in_one_request()
    {
        var handler = new JsonHandler("""
            {"connections":[
              {"id":"codex-1","provider":"codex","name":"Work","priority":1,"isActive":true},
              {"id":"ollama-1","provider":"ollama","name":"Work","priority":2,"isActive":false},
              {"id":"unknown-1","provider":"not-supported","name":"Ignored","priority":1,"isActive":true}
            ]}
            """);
        using var httpClient = new HttpClient(handler);
        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        var connections = await api.ListAllConnectionsAsync();

        Assert.Equal(1, handler.RequestCount);
        Assert.Equal(2, connections.Count);
        Assert.Contains(connections, connection => connection.Provider == ProviderKind.Codex);
        Assert.Contains(connections, connection => connection.Provider == ProviderKind.Ollama && !connection.IsActive);
    }

    [Fact]
    public async Task ListAllConnections_marks_connections_with_provider_errors()
    {
        var handler = new JsonHandler("""
            {"connections":[
              {"id":"codex-1","provider":"codex","name":"Work","priority":1,"isActive":true,
               "testStatus":"unavailable","errorCode":"401","lastError":"Usage API temporarily unavailable (401)"}
            ]}
            """);
        using var httpClient = new HttpClient(handler);
        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        var connection = Assert.Single(await api.ListAllConnectionsAsync());

        Assert.True(connection.HasError);
        Assert.Equal("401", connection.ErrorCode);
        Assert.Contains("Usage API", connection.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListAllConnections_uses_unavailable_test_status_as_provider_error()
    {
        var handler = new JsonHandler("""
            {"connections":[
              {"id":"ollama-1","provider":"ollama","name":"Work","priority":1,"isActive":true,"testStatus":"unavailable"}
            ]}
            """);
        using var httpClient = new HttpClient(handler);
        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        var connection = Assert.Single(await api.ListAllConnectionsAsync());

        Assert.Equal("unavailable", connection.TestStatus);
        Assert.True(connection.HasError);
    }

    [Fact]
    public async Task ListAllConnections_tolerates_null_optional_connection_fields()
    {
        var handler = new JsonHandler("""
            {"connections":[
              {"id":"openrouter-1","provider":"openrouter","name":null,"email":null,
               "priority":null,"isActive":null,"testStatus":null,"errorCode":null,
               "lastError":null,"createdAt":null,"lastErrorAt":null}
            ]}
            """);
        using var httpClient = new HttpClient(handler);
        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        var connection = Assert.Single(await api.ListAllConnectionsAsync());

        Assert.Equal("openrouter-1", connection.Id);
        Assert.Equal(ProviderKind.OpenRouter, connection.Provider);
        Assert.Null(connection.Name);
        Assert.True(connection.IsActive);
        Assert.False(connection.HasError);
    }

    [Fact]
    public async Task UpdateConnection_includes_api_key_in_the_9router_request()
    {
        var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        await api.UpdateConnectionAsync("openrouter-1", "Work", 7, "new-key");

        Assert.Equal(HttpMethod.Put, handler.Method);
        Assert.EndsWith("/api/providers/openrouter-1", handler.RequestUri, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(handler.Body);
        Assert.Equal("Work", document.RootElement.GetProperty("name").GetString());
        Assert.Equal(7, document.RootElement.GetProperty("priority").GetInt32());
        Assert.Equal("new-key", document.RootElement.GetProperty("apiKey").GetString());
    }

    [Fact]
    public async Task TestConnection_posts_to_connection_test_endpoint_and_reads_validation_result()
    {
        var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        var result = await api.TestConnectionAsync("codex-1");

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.EndsWith("/api/providers/codex-1/test", handler.RequestUri, StringComparison.Ordinal);
        Assert.False(result.Valid);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task TestConnection_reads_error_message_from_invalid_response()
    {
        var handler = new RecordingHandler
        {
            ResponseBody = """{"valid":false,"error":"Invalid token"}"""
        };
        using var httpClient = new HttpClient(handler);
        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        var result = await api.TestConnectionAsync("codex-1");

        Assert.False(result.Valid);
        Assert.Equal("Invalid token", result.Error);
    }

    private sealed class JsonHandler(string json) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }

        public string? RequestUri { get; private set; }

        public string Body { get; private set; } = string.Empty;

        public string ResponseBody { get; init; } = "{}";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri?.ToString();
            Body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
