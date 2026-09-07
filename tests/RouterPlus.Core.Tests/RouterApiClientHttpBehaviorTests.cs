using System.Net;
using System.Text;
using System.Text.Json;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Router;

namespace RouterPlus.Core.Tests;

public sealed class RouterApiClientHttpBehaviorTests
{
    [Fact]
    public async Task ListConnections_filters_provider_and_sorts_by_priority_then_name()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath.StartsWith("/api/usage/", StringComparison.Ordinal)
            ? JsonResponse(HttpStatusCode.NotFound, "{}")
            : JsonResponse(HttpStatusCode.OK, """
                {"connections":[
                  {"id":"codex-z","provider":"codex","name":"Zulu","priority":2},
                  {"id":"kiro-1","provider":"kiro","name":"Other","priority":1},
                  {"id":"codex-b","provider":"codex","name":"beta","priority":1},
                  {"id":"codex-a","provider":"codex","name":"Alpha","priority":1},
                  {"id":"invalid","provider":"unsupported","name":"Ignored","priority":0},
                  {"id":"missing-provider","name":"Ignored","priority":0}
                ]}
                """));
        using var httpClient = new HttpClient(handler);
        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        var connections = await api.ListConnectionsAsync(ProviderKind.Codex);

        Assert.Equal(["codex-a", "codex-b", "codex-z"], connections.Select(connection => connection.Id));
        Assert.All(connections, connection => Assert.Equal(ProviderKind.Codex, connection.Provider));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"connections\":{}}")]
    public async Task ListAllConnections_returns_empty_when_connections_payload_is_missing_or_not_an_array(string body)
    {
        using var httpClient = new HttpClient(new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, body)));
        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        var connections = await api.ListAllConnectionsAsync();

        Assert.Empty(connections);
    }

    [Fact]
    public async Task ListAllConnections_throws_when_response_contains_invalid_json()
    {
        using var httpClient = new HttpClient(new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, "not-json")));
        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        await Assert.ThrowsAnyAsync<JsonException>(() => api.ListAllConnectionsAsync());
    }

    [Fact]
    public async Task TestConnection_escapes_connection_id_and_reads_result()
    {
        var handler = new RecordingHandler(request => JsonResponse(HttpStatusCode.OK, """{"valid":true,"error":null}"""));
        using var httpClient = new HttpClient(handler);
        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        var result = await api.TestConnectionAsync("connection/with space");

        Assert.True(result.Valid);
        Assert.Null(result.Error);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/providers/connection%2Fwith%20space/test", new Uri(request.Uri).AbsolutePath);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task TestConnection_rejects_blank_connection_id(string connectionId)
    {
        using var httpClient = new HttpClient(new RecordingHandler(_ => throw new InvalidOperationException("No request expected.")));
        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        await Assert.ThrowsAsync<ArgumentException>(() => api.TestConnectionAsync(connectionId));
    }

    [Fact]
    public async Task TestConnection_throws_router_api_exception_for_http_error()
    {
        using var httpClient = new HttpClient(new RecordingHandler(_ => JsonResponse(HttpStatusCode.BadRequest, "{}")));
        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        var exception = await Assert.ThrowsAsync<RouterApiException>(() => api.TestConnectionAsync("codex-1"));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task AddApiKeyConnection_posts_request_and_parses_nested_connection()
    {
        var handler = new RecordingHandler(request => JsonResponse(HttpStatusCode.Created, """
            {"connection":{"id":"openrouter-1","provider":"openrouter","name":"Work","priority":3,"isActive":true}}
            """));
        using var httpClient = new HttpClient(handler);
        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        var created = await api.AddApiKeyConnectionAsync(ProviderKind.OpenRouter, "Work", "secret", 3);

        Assert.Equal("openrouter-1", created.Id);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/api/providers", new Uri(request.Uri).AbsolutePath);
        using var document = JsonDocument.Parse(request.Body!);
        Assert.Equal("openrouter", document.RootElement.GetProperty("provider").GetString());
        Assert.Equal("Work", document.RootElement.GetProperty("name").GetString());
        Assert.Equal("secret", document.RootElement.GetProperty("apiKey").GetString());
        Assert.Equal(3, document.RootElement.GetProperty("priority").GetInt32());
        Assert.Equal("unknown", document.RootElement.GetProperty("testStatus").GetString());
    }

    [Theory]
    [InlineData("", "secret", 1)]
    [InlineData("Work", "", 1)]
    [InlineData("Work", "secret", 0)]
    public async Task AddApiKeyConnection_rejects_invalid_arguments(string name, string apiKey, int priority)
    {
        using var httpClient = new HttpClient(new RecordingHandler(_ => throw new InvalidOperationException("No request expected.")));
        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            api.AddApiKeyConnectionAsync(ProviderKind.OpenRouter, name, apiKey, priority));
    }

    [Fact]
    public async Task AddApiKeyConnection_falls_back_to_listing_when_create_response_has_no_connection()
    {
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Post
            ? JsonResponse(HttpStatusCode.OK, "{}")
            : request.RequestUri!.AbsolutePath.StartsWith("/api/usage/", StringComparison.Ordinal)
                ? JsonResponse(HttpStatusCode.NotFound, "{}")
                : JsonResponse(HttpStatusCode.OK, """
                    {"connections":[
                      {"id":"kiro-1","provider":"kiro","name":"Existing","priority":1},
                      {"id":"kiro-2","provider":"kiro","name":"New Account","priority":2}
                    ]}
                    """));
        using var httpClient = new HttpClient(handler);
        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        var created = await api.AddApiKeyConnectionAsync(ProviderKind.Kiro, "New Account", "secret", 2);

        Assert.Equal("kiro-2", created.Id);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task AddApiKeyConnection_throws_when_create_response_and_fallback_listing_do_not_contain_connection()
    {
        var handler = new RecordingHandler(request => request.Method == HttpMethod.Post
            ? JsonResponse(HttpStatusCode.OK, "{}")
            : request.RequestUri!.AbsolutePath.StartsWith("/api/usage/", StringComparison.Ordinal)
                ? JsonResponse(HttpStatusCode.NotFound, "{}")
                : JsonResponse(HttpStatusCode.OK, "{\"connections\":[]}"));
        using var httpClient = new HttpClient(handler);
        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        var exception = await Assert.ThrowsAsync<RouterApiException>(() =>
            api.AddApiKeyConnectionAsync(ProviderKind.Kiro, "Missing", "secret", 1));

        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
    }

    [Fact]
    public async Task UpdateConnection_returns_without_request_when_no_changes_are_supplied()
    {
        using var httpClient = new HttpClient(new RecordingHandler(_ => throw new InvalidOperationException("No request expected.")));
        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        await api.UpdateConnectionAsync("codex-1");
    }

    [Fact]
    public async Task UpdateConnection_rejects_non_positive_priority_before_sending_request()
    {
        using var httpClient = new HttpClient(new RecordingHandler(_ => throw new InvalidOperationException("No request expected.")));
        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            api.UpdateConnectionAsync("codex-1", priority: 0));
    }

    [Fact]
    public async Task GetOAuthProxyStatus_parses_status_and_defaults_missing_status_to_pending()
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, "{\"error\":\"still waiting\"}"));
        using var httpClient = new HttpClient(handler);
        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        var status = await api.GetOAuthProxyStatusAsync(ProviderKind.Kimchi, "state with spaces");

        Assert.Equal("pending", status.Status);
        Assert.Equal("still waiting", status.Error);
        var requestUri = new Uri(Assert.Single(handler.Requests).Uri);
        Assert.Equal("/api/oauth/kimchi/poll-status", requestUri.AbsolutePath);
        Assert.Equal("state=state%20with%20spaces", requestUri.Query.TrimStart('?'));
    }

    [Fact]
    public async Task StartOAuthAuthorization_uses_redirect_and_browser_defaults_when_response_omits_optional_fields()
    {
        var handler = new RecordingHandler(_ => JsonResponse(HttpStatusCode.OK, """
            {"authUrl":"https://accounts.example/authorize","state":"state-1","codeVerifier":"verifier-1"}
            """));
        using var httpClient = new HttpClient(handler);
        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        var session = await api.StartOAuthAuthorizationAsync(ProviderKind.Codex, "http://127.0.0.1/callback");

        Assert.Equal("http://127.0.0.1/callback", session.RedirectUri);
        Assert.Equal("browser", session.FlowType);
        Assert.False(session.FixedPort);
        Assert.Null(session.CallbackPath);
    }

    [Fact]
    public async Task TestConnection_propagates_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        using var httpClient = new HttpClient(new RecordingHandler((_, token) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(token);
        }));
        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            api.TestConnectionAsync("codex-1", cancellation.Token));
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string body) => new(statusCode)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed record RequestRecord(HttpMethod Method, string Uri, string? Body);

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage>? _responder;
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? _asyncResponder;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        public RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            _asyncResponder = responder;
        }

        public List<RequestRecord> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RequestRecord(request.Method, request.RequestUri!.ToString(), body));
            return _asyncResponder is not null
                ? await _asyncResponder(request, cancellationToken)
                : _responder!(request);
        }
    }
}
