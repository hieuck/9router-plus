using System.Net;
using System.Text;
using System.Text.Json;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Router;

namespace RouterPlus.Infrastructure.Tests;

public sealed class RouterApiClientBranchTests
{
    [Fact]
    public async Task StartOAuthProxyAsync_sends_escaped_parameters_and_parses_success()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json("{\"success\":true,\"serverSide\":true}")
        });
        using var httpClient = new HttpClient(handler);
        var client = new RouterApiClient(httpClient, "https://dashboard.example.test/");
        var session = new OAuthAuthorizationSession(
            "https://provider.example.test/authorize",
            "state value",
            "verifier/value",
            "http://127.0.0.1:4321/callback?next=1",
            "browser",
            false,
            "/callback");

        var result = await client.StartOAuthProxyAsync(ProviderKind.OpenRouter, 4321, session);

        Assert.True(result.Success);
        Assert.True(result.ServerSide);
        Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal("/api/oauth/openrouter/start-proxy", handler.RequestUri!.AbsolutePath);
        var query = handler.RequestUri.Query;
        Assert.Contains("app_port=4321", query, StringComparison.Ordinal);
        Assert.Contains("state=state%20value", query, StringComparison.Ordinal);
        Assert.Contains("code_verifier=verifier%2Fvalue", query, StringComparison.Ordinal);
        Assert.Contains("redirect_uri=http%3A%2F%2F127.0.0.1%3A4321%2Fcallback%3Fnext%3D1", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartOAuthProxyAsync_throws_router_api_exception_with_reason_and_status()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json("{\"success\":false,\"reason\":\"proxy unavailable\"}")
        });
        using var httpClient = new HttpClient(handler);
        var client = new RouterApiClient(httpClient, "https://dashboard.example.test");
        var session = CreateSession();

        var exception = await Assert.ThrowsAsync<RouterApiException>(() =>
            client.StartOAuthProxyAsync(ProviderKind.Codex, 4321, session));

        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        Assert.Equal("proxy unavailable", exception.Message);
    }

    [Fact]
    public async Task StartOAuthProxyAsync_validates_port_and_session_before_http_request()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler);
        var client = new RouterApiClient(httpClient, "https://dashboard.example.test");

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            client.StartOAuthProxyAsync(ProviderKind.Codex, 0, CreateSession()));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            client.StartOAuthProxyAsync(ProviderKind.Codex, 65536, CreateSession()));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            client.StartOAuthProxyAsync(ProviderKind.Codex, 4321, null!));

        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task ExchangeOAuthCodeAsync_posts_all_code_fields()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var httpClient = new HttpClient(handler);
        var client = new RouterApiClient(httpClient, "https://dashboard.example.test");

        await client.ExchangeOAuthCodeAsync(
            ProviderKind.Kimchi,
            "synthetic-code",
            "http://127.0.0.1:4321/callback",
            "synthetic-verifier",
            "synthetic-state");

        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/api/oauth/kimchi/exchange", handler.RequestUri!.AbsolutePath);
        using var body = JsonDocument.Parse(handler.Body);
        Assert.Equal("synthetic-code", body.RootElement.GetProperty("code").GetString());
        Assert.Equal("http://127.0.0.1:4321/callback", body.RootElement.GetProperty("redirectUri").GetString());
        Assert.Equal("synthetic-verifier", body.RootElement.GetProperty("codeVerifier").GetString());
        Assert.Equal("synthetic-state", body.RootElement.GetProperty("state").GetString());
    }

    [Fact]
    public async Task ExchangeOAuthCodeAsync_maps_non_success_response_to_router_api_exception()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest));
        using var httpClient = new HttpClient(handler);
        var client = new RouterApiClient(httpClient, "https://dashboard.example.test");

        var exception = await Assert.ThrowsAsync<RouterApiException>(() =>
            client.ExchangeOAuthCodeAsync(ProviderKind.Codex, "synthetic-code", "http://127.0.0.1/callback", null, null));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
    }

    [Fact]
    public async Task GetOAuthProxyStatusAsync_defaults_missing_status_and_preserves_error()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json("{\"error\":\"still waiting\"}")
        });
        using var httpClient = new HttpClient(handler);
        var client = new RouterApiClient(httpClient, "https://dashboard.example.test");

        var result = await client.GetOAuthProxyStatusAsync(ProviderKind.Kiro, "state with spaces");

        Assert.Equal("pending", result.Status);
        Assert.Equal("still waiting", result.Error);
        Assert.Equal("/api/oauth/kiro/poll-status", handler.RequestUri!.AbsolutePath);
        Assert.Contains("state=state%20with%20spaces", handler.RequestUri.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartDeviceCodeAsync_omits_auth_method_query_and_uses_numeric_defaults()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json("{\"device_code\":\"synthetic-device-code\",\"verification_uri\":\"https://provider.example.test/device\"}")
        });
        using var httpClient = new HttpClient(handler);
        var client = new RouterApiClient(httpClient, "https://dashboard.example.test");

        var session = await client.StartDeviceCodeAsync(ProviderKind.Codex);

        Assert.Equal("synthetic-device-code", session.DeviceCode);
        Assert.Equal("https://provider.example.test/device", session.VerificationUri);
        Assert.Equal(600, session.ExpiresIn);
        Assert.Equal(5, session.Interval);
        Assert.Equal("/api/oauth/codex/device-code", handler.RequestUri!.AbsolutePath);
        Assert.Empty(handler.RequestUri.Query);
    }

    [Fact]
    public async Task PollDeviceCodeAsync_posts_device_code_and_extra_data()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json("{\"success\":false,\"error\":\"authorization_pending\",\"errorDescription\":\"Approve the device\"}")
        });
        using var httpClient = new HttpClient(handler);
        var client = new RouterApiClient(httpClient, "https://dashboard.example.test");
        var session = new DeviceCodeSession(
            "synthetic-device-code",
            "ABCD-EFGH",
            "https://provider.example.test/device",
            null,
            600,
            5,
            "synthetic-client-id",
            "synthetic-client-secret",
            "synthetic-region",
            "synthetic-method",
            "https://provider.example.test/start",
            "synthetic-verifier");

        var result = await client.PollDeviceCodeAsync(ProviderKind.Kiro, session);

        Assert.False(result.Success);
        Assert.Equal("authorization_pending", result.Error);
        Assert.Equal("Approve the device", result.ErrorDescription);
        Assert.Equal("/api/oauth/kiro/poll", handler.RequestUri!.AbsolutePath);
        using var body = JsonDocument.Parse(handler.Body);
        Assert.Equal("synthetic-device-code", body.RootElement.GetProperty("deviceCode").GetString());
        Assert.Equal("synthetic-verifier", body.RootElement.GetProperty("codeVerifier").GetString());
        var extraData = body.RootElement.GetProperty("extraData");
        Assert.Equal("synthetic-client-id", extraData.GetProperty("_clientId").GetString());
        Assert.Equal("synthetic-client-secret", extraData.GetProperty("_clientSecret").GetString());
        Assert.Equal("synthetic-region", extraData.GetProperty("_region").GetString());
        Assert.Equal("synthetic-method", extraData.GetProperty("_authMethod").GetString());
        Assert.Equal("https://provider.example.test/start", extraData.GetProperty("_startUrl").GetString());
    }

    [Fact]
    public async Task AddApiKeyConnectionAsync_posts_synthetic_key_and_parses_direct_connection()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json("{\"id\":\"conn-direct\",\"provider\":\"openrouter\",\"name\":\"Synthetic\",\"priority\":3}")
        });
        using var httpClient = new HttpClient(handler);
        var client = new RouterApiClient(httpClient, "https://dashboard.example.test");

        var connection = await client.AddApiKeyConnectionAsync(
            ProviderKind.OpenRouter,
            "Synthetic",
            "sk-synthetic-key",
            3);

        Assert.Equal("conn-direct", connection.Id);
        Assert.Equal(ProviderKind.OpenRouter, connection.Provider);
        Assert.Equal(HttpMethod.Post, handler.Method);
        Assert.Equal("/api/providers", handler.RequestUri!.AbsolutePath);
        using var body = JsonDocument.Parse(handler.Body);
        Assert.Equal("openrouter", body.RootElement.GetProperty("provider").GetString());
        Assert.Equal("sk-synthetic-key", body.RootElement.GetProperty("apiKey").GetString());
        Assert.Equal("Synthetic", body.RootElement.GetProperty("name").GetString());
        Assert.Equal(3, body.RootElement.GetProperty("priority").GetInt32());
        Assert.Equal("unknown", body.RootElement.GetProperty("testStatus").GetString());
    }

    [Fact]
    public async Task AddApiKeyConnectionAsync_falls_back_to_matching_connection_when_post_has_no_connection()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/api/providers" when request.Method == HttpMethod.Post => Response(HttpStatusCode.OK, "{}"),
            "/api/providers" => Response(HttpStatusCode.OK, "{\"connections\":[{\"id\":\"conn-fallback\",\"provider\":\"codex\",\"name\":\"SYNTHETIC\",\"priority\":4}]}"),
            "/api/usage/conn-fallback" => Response(HttpStatusCode.NotFound, "{}"),
            _ => throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}")
        });
        using var httpClient = new HttpClient(handler);
        var client = new RouterApiClient(httpClient, "https://dashboard.example.test");

        var connection = await client.AddApiKeyConnectionAsync(ProviderKind.Codex, "Synthetic", "synthetic-key", 4);

        Assert.Equal("conn-fallback", connection.Id);
        Assert.Equal(ProviderKind.Codex, connection.Provider);
        Assert.Equal(3, handler.RequestCount);
    }

    [Fact]
    public async Task UpdateConnectionAsync_returns_without_request_when_no_fields_change()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var httpClient = new HttpClient(handler);
        var client = new RouterApiClient(httpClient, "https://dashboard.example.test");

        await client.UpdateConnectionAsync("conn-1");

        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task UpdateConnectionAsync_throws_router_api_exception_for_failed_update()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict));
        using var httpClient = new HttpClient(handler);
        var client = new RouterApiClient(httpClient, "https://dashboard.example.test");

        var exception = await Assert.ThrowsAsync<RouterApiException>(() =>
            client.UpdateConnectionAsync("conn/with slash", name: "Synthetic"));

        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
        Assert.Equal("/api/providers/conn%2Fwith%20slash", handler.RequestUri!.AbsolutePath);
    }

    private static OAuthAuthorizationSession CreateSession() => new(
        "https://provider.example.test/authorize",
        "synthetic-state",
        "synthetic-verifier",
        "http://127.0.0.1:4321/callback",
        "browser",
        false,
        "/callback");

    private static StringContent Json(string content) =>
        new(content, Encoding.UTF8, "application/json");

    private static HttpResponseMessage Response(HttpStatusCode statusCode, string content) => new(statusCode)
    {
        Content = Json(content)
    };

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }

        public Uri? RequestUri { get; private set; }

        public string Body { get; private set; } = string.Empty;

        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            Method = request.Method;
            RequestUri = request.RequestUri;
            Body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return responseFactory(request);
        }
    }
}
