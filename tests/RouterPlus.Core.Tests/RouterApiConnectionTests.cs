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

        Assert.True(handler.RequestCount >= 1, $"Expected at least 1 request, got {handler.RequestCount}");
        Assert.Equal(2, connections.Count);
        Assert.Contains(connections, connection => connection.Provider == ProviderKind.Codex);
        Assert.Contains(connections, connection => connection.Provider == ProviderKind.Ollama && !connection.IsActive);
    }

    [Fact]
    public void RouterApiClient_does_not_retain_a_local_usage_database_reader()
    {
        var readerFields = typeof(RouterApiClient)
            .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Where(field => field.FieldType.Name == "UsageDatabaseReader");

        Assert.Empty(readerFields);
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
    public async Task WaitForNewConnection_accepts_existing_connection_refreshed_after_snapshot()
    {
        var snapshot = new ProviderConnection(
            "codex-1",
            ProviderKind.Codex,
            "old-account@example.com",
            1,
            true,
            "old-account@example.com",
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-1),
            LastRefreshAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        var refreshedAt = DateTimeOffset.UtcNow;
        var handler = new JsonHandler($$"""
            {"connections":[
              {"id":"codex-1","provider":"codex","name":"new-account@example.com","email":"new-account@example.com","priority":1,"isActive":true,"testStatus":"active","lastRefreshAt":"{{refreshedAt:O}}"}
            ]}
            """);
        using var httpClient = new HttpClient(handler);
        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        var connection = await api.WaitForNewConnectionAsync(
            ProviderKind.Codex,
            new Dictionary<string, ProviderConnection> { [snapshot.Id] = snapshot },
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(1));

        Assert.Equal(snapshot.Id, connection.Id);
        Assert.Equal("new-account@example.com", connection.Email);
    }

    [Fact]
    public async Task WaitForNewConnection_accepts_refresh_when_previous_timestamp_is_missing()
    {
        var snapshot = new ProviderConnection(
            "codex-1",
            ProviderKind.Codex,
            "old-account@example.com",
            1,
            true,
            "old-account@example.com",
            TestStatus: "active");
        var refreshedAt = DateTimeOffset.UtcNow;
        var handler = new JsonHandler($$"""
            {"connections":[
              {"id":"codex-1","provider":"codex","name":"old-account@example.com","email":"old-account@example.com","priority":1,"isActive":true,"testStatus":"active","lastRefreshAt":"{{refreshedAt:O}}"}
            ]}
            """);
        using var httpClient = new HttpClient(handler);
        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        var connection = await api.WaitForNewConnectionAsync(
            ProviderKind.Codex,
            new Dictionary<string, ProviderConnection> { [snapshot.Id] = snapshot },
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(1));

        Assert.Equal(snapshot.Id, connection.Id);
    }

    [Fact]
    public async Task WaitForNewConnection_ignores_unrelated_health_status_changes()
    {
        var snapshot = new ProviderConnection(
            "codex-1",
            ProviderKind.Codex,
            "account@example.com",
            1,
            true,
            "account@example.com",
            TestStatus: "active");
        var handler = new JsonHandler("""
            {"connections":[
              {"id":"codex-1","provider":"codex","name":"account@example.com","email":"account@example.com","priority":1,"isActive":true,"testStatus":"unavailable"}
            ]}
            """);
        using var httpClient = new HttpClient(handler);
        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        await Assert.ThrowsAsync<TimeoutException>(() => api.WaitForNewConnectionAsync(
            ProviderKind.Codex,
            new Dictionary<string, ProviderConnection> { [snapshot.Id] = snapshot },
            TimeSpan.FromMilliseconds(20),
            TimeSpan.FromMilliseconds(1)));
    }

    [Fact]
    public async Task UpdateConnection_includes_api_key_in_the_9router_request()
    {
        var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        await api.UpdateConnectionAsync("openrouter-1", "Work", 7, "new-key", CancellationToken.None);

        Assert.Equal(HttpMethod.Put, handler.Method);
        Assert.EndsWith("/api/providers/openrouter-1", handler.RequestUri, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(handler.Body);
        Assert.Equal("Work", document.RootElement.GetProperty("name").GetString());
        Assert.Equal(7, document.RootElement.GetProperty("priority").GetInt32());
        Assert.Equal("new-key", document.RootElement.GetProperty("apiKey").GetString());
    }

    [Fact]
    public async Task UpdateConnection_can_disable_a_connection()
    {
        var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        await api.UpdateConnectionAsync("codex-1", isActive: false);

        Assert.Equal(HttpMethod.Put, handler.Method);
        using var document = JsonDocument.Parse(handler.Body);
        Assert.False(document.RootElement.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task ListAllConnections_propagates_quota_fetch_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        using var httpClient = new HttpClient(new CancellationHandler(cancellation));
        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => api.ListAllConnectionsAsync(cancellation.Token));
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

    private sealed class CancellationHandler(CancellationTokenSource cancellation) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath == "/api/providers")
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"connections\":[{\"id\":\"codex-1\",\"provider\":\"codex\",\"name\":\"Work\",\"isActive\":true}]}",
                        Encoding.UTF8,
                        "application/json")
                };
            }

            cancellation.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Cancellation was not propagated.");
        }
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
