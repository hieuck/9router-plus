using System.Net;
using System.Text;
using System.Text.Json;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Router;

namespace RouterPlus.Infrastructure.Tests;

public sealed class RouterApiClientTests
{
    [Fact]
    public async Task ListAllConnectionsAsync_parses_filters_and_orders_connections()
    {
        // Arrange
        var handler = new RecordingHandler((request, _) => request.RequestUri!.AbsolutePath switch
        {
            "/api/providers" => JsonResponse("""
                {
                  "connections": [
                    {"id":"openrouter-1","provider":"openrouter","name":"Router","priority":1},
                    {"id":"codex-2","provider":"codex","name":"Later","priority":2},
                    {"id":"codex-1","provider":"codex","name":"First","priority":1},
                    {"id":"ignored","provider":"unsupported","name":"Ignored","priority":1}
                  ]
                }
                """),
            _ when request.RequestUri.AbsolutePath.StartsWith("/api/usage/", StringComparison.Ordinal)
                => JsonResponse("{\"message\":\"no quota\"}"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        using var httpClient = new HttpClient(handler);
        var client = new RouterApiClient(httpClient, "https://dashboard.example.test/");

        // Act
        var connections = await client.ListAllConnectionsAsync();

        // Assert
        Assert.Equal(["codex-1", "codex-2", "openrouter-1"], connections.Select(connection => connection.Id));
        Assert.DoesNotContain(connections, connection => connection.Id == "ignored");
        Assert.Equal(5, handler.Requests.Count);
    }

    [Fact]
    public async Task StartOAuthAuthorizationAsync_uses_provider_alias_and_escaped_redirect_uri()
    {
        // Arrange
        var handler = new RecordingHandler((_, _) => JsonResponse("""
            {"authUrl":"https://accounts.example/authorize","state":"state-1","codeVerifier":"verifier-1"}
            """));
        using var httpClient = new HttpClient(handler);
        var client = new RouterApiClient(httpClient, "https://dashboard.example.test");
        const string redirectUri = "http://127.0.0.1:1234/callback?value=a b";

        // Act
        var session = await client.StartOAuthAuthorizationAsync(ProviderKind.Kimchi, redirectUri);

        // Assert
        Assert.Equal("https://accounts.example/authorize", session.AuthUrl);
        Assert.Equal(redirectUri, session.RedirectUri);
        Assert.Equal("browser", session.FlowType);
        var requestUri = handler.Requests.Single().RequestUri!;
        Assert.Equal("kimchi", requestUri.Segments[3].TrimEnd('/'));
        Assert.Equal(redirectUri, Uri.UnescapeDataString(requestUri.Query["?redirect_uri=".Length..].Replace('+', ' ')));
    }

    [Fact]
    public async Task StartOAuthProxyAsync_returns_server_side_result_for_successful_response()
    {
        // Arrange
        var handler = new RecordingHandler((_, _) => JsonResponse("{\"success\":true,\"serverSide\":true}"));
        using var httpClient = new HttpClient(handler);
        var client = new RouterApiClient(httpClient, "https://dashboard.example.test");
        var session = new OAuthAuthorizationSession(
            "https://accounts.example/authorize",
            "state value",
            "verifier/value",
            "http://127.0.0.1:1234/callback",
            "browser",
            false,
            "/callback");

        // Act
        var result = await client.StartOAuthProxyAsync(ProviderKind.Codex, 4321, session);

        // Assert
        Assert.Equal(new OAuthProxyStartResult(true, true), result);
        var requestUri = handler.Requests.Single().RequestUri!.ToString();
        Assert.Contains("/api/oauth/codex/start-proxy?app_port=4321", requestUri, StringComparison.Ordinal);
        var decodedUri = Uri.UnescapeDataString(requestUri);
        Assert.Contains("state=state value", decodedUri, StringComparison.Ordinal);
        Assert.Contains("code_verifier=verifier/value", decodedUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartOAuthProxyAsync_throws_router_exception_when_proxy_start_fails()
    {
        // Arrange
        var handler = new RecordingHandler((_, _) => JsonResponse("{\"success\":false,\"reason\":\"port unavailable\"}"));
        using var httpClient = new HttpClient(handler);
        var client = new RouterApiClient(httpClient, "https://dashboard.example.test");
        var session = new OAuthAuthorizationSession("auth", "state", "verifier", "redirect", "browser", false, null);

        // Act
        var exception = await Assert.ThrowsAsync<RouterApiException>(
            () => client.StartOAuthProxyAsync(ProviderKind.Codex, 4321, session));

        // Assert
        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        Assert.Equal("port unavailable", exception.Message);
    }

    [Fact]
    public async Task ExchangeOAuthCodeAsync_posts_expected_json_payload()
    {
        // Arrange
        var handler = new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var httpClient = new HttpClient(handler);
        var client = new RouterApiClient(httpClient, "https://dashboard.example.test");

        // Act
        await client.ExchangeOAuthCodeAsync(
            ProviderKind.Codex,
            "code value",
            "http://127.0.0.1/callback",
            "verifier",
            "state");

        // Assert
        var request = handler.Requests.Single();
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://dashboard.example.test/api/oauth/codex/exchange", request.RequestUri!.ToString());
        using var body = JsonDocument.Parse(handler.Bodies.Single());
        Assert.Equal("code value", body.RootElement.GetProperty("code").GetString());
        Assert.Equal("http://127.0.0.1/callback", body.RootElement.GetProperty("redirectUri").GetString());
        Assert.Equal("verifier", body.RootElement.GetProperty("codeVerifier").GetString());
        Assert.Equal("state", body.RootElement.GetProperty("state").GetString());
    }

    [Fact]
    public async Task StartDeviceCodeAsync_includes_auth_method_and_applies_defaults()
    {
        // Arrange
        var handler = new RecordingHandler((_, _) => JsonResponse("""
            {"device_code":"device-1","verification_uri":"https://example.test/device"}
            """));
        using var httpClient = new HttpClient(handler);
        var client = new RouterApiClient(httpClient, "https://dashboard.example.test");

        // Act
        var session = await client.StartDeviceCodeAsync(ProviderKind.Kiro, "idc");

        // Assert
        Assert.Equal("device-1", session.DeviceCode);
        Assert.Equal(600, session.ExpiresIn);
        Assert.Equal(5, session.Interval);
        Assert.Equal("https://dashboard.example.test/api/oauth/kiro/device-code?auth_method=idc", handler.Requests.Single().RequestUri!.ToString());
    }

    [Fact]
    public async Task PollDeviceCodeAsync_posts_device_code_and_extra_data()
    {
        // Arrange
        var handler = new RecordingHandler((_, _) => JsonResponse("{\"success\":false,\"error\":\"authorization_pending\",\"errorDescription\":\"Wait\"}"));
        using var httpClient = new HttpClient(handler);
        var client = new RouterApiClient(httpClient, "https://dashboard.example.test");
        var session = new DeviceCodeSession(
            "device-1", "ABCD", "https://example.test/device", null, 300, 2,
            "client-1", "secret-1", "us-east-1", "idc", "https://example.test/start", "verifier");

        // Act
        var result = await client.PollDeviceCodeAsync(ProviderKind.Kiro, session);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("authorization_pending", result.Error);
        Assert.Equal("Wait", result.ErrorDescription);
        using var body = JsonDocument.Parse(handler.Bodies.Single());
        Assert.Equal("device-1", body.RootElement.GetProperty("deviceCode").GetString());
        Assert.Equal("verifier", body.RootElement.GetProperty("codeVerifier").GetString());
        Assert.Equal("client-1", body.RootElement.GetProperty("extraData").GetProperty("_clientId").GetString());
        Assert.Equal("us-east-1", body.RootElement.GetProperty("extraData").GetProperty("_region").GetString());
    }

    [Fact]
    public async Task AddApiKeyConnectionAsync_posts_trimmed_input_and_parses_created_connection()
    {
        // Arrange
        var handler = new RecordingHandler((_, _) => JsonResponse("""
            {"connection":{"id":"openrouter-1","provider":"openrouter","name":"Synthetic","priority":3,"isActive":true}}
            """));
        using var httpClient = new HttpClient(handler);
        var client = new RouterApiClient(httpClient, "https://dashboard.example.test");

        // Act
        var connection = await client.AddApiKeyConnectionAsync(ProviderKind.OpenRouter, "Synthetic", "synthetic-key", 3);

        // Assert
        Assert.Equal("openrouter-1", connection.Id);
        Assert.Equal(HttpMethod.Post, handler.Requests.Single().Method);
        using var body = JsonDocument.Parse(handler.Bodies.Single());
        Assert.Equal("openrouter", body.RootElement.GetProperty("provider").GetString());
        Assert.Equal("synthetic-key", body.RootElement.GetProperty("apiKey").GetString());
        Assert.Equal("Synthetic", body.RootElement.GetProperty("name").GetString());
        Assert.Equal(3, body.RootElement.GetProperty("priority").GetInt32());
        Assert.Equal("unknown", body.RootElement.GetProperty("testStatus").GetString());
    }

    [Fact]
    public async Task UpdateConnectionAsync_sends_only_supplied_trimmed_fields()
    {
        // Arrange
        var handler = new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var httpClient = new HttpClient(handler);
        var client = new RouterApiClient(httpClient, "https://dashboard.example.test");

        // Act
        await client.UpdateConnectionAsync("connection/1", "  New name  ", 2, "  new-key  ", isActive: false);

        // Assert
        Assert.Equal("https://dashboard.example.test/api/providers/connection%2F1", handler.Requests.Single().RequestUri!.ToString());
        using var body = JsonDocument.Parse(handler.Bodies.Single());
        Assert.Equal("New name", body.RootElement.GetProperty("name").GetString());
        Assert.Equal(2, body.RootElement.GetProperty("priority").GetInt32());
        Assert.Equal("new-key", body.RootElement.GetProperty("apiKey").GetString());
        Assert.False(body.RootElement.GetProperty("isActive").GetBoolean());
        Assert.Equal(4, body.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public async Task UpdateConnectionAsync_does_not_request_when_no_update_is_supplied()
    {
        // Arrange
        var handler = new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.NotImplemented));
        using var httpClient = new HttpClient(handler);
        var client = new RouterApiClient(httpClient, "https://dashboard.example.test");

        // Act
        await client.UpdateConnectionAsync("connection-1");

        // Assert
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task DeleteConnectionAsync_throws_with_status_code_for_failed_response()
    {
        // Arrange
        var handler = new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Conflict));
        using var httpClient = new HttpClient(handler);
        var client = new RouterApiClient(httpClient, "https://dashboard.example.test");

        // Act
        var exception = await Assert.ThrowsAsync<RouterApiException>(
            () => client.DeleteConnectionAsync("connection/1"));

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
        Assert.Equal("https://dashboard.example.test/api/providers/connection%2F1", handler.Requests.Single().RequestUri!.ToString());
    }

    [Fact]
    public async Task Methods_validate_required_arguments_before_sending_requests()
    {
        // Arrange
        using var httpClient = new HttpClient(new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)));
        var client = new RouterApiClient(httpClient, "https://dashboard.example.test");

        // Act and Assert
        await Assert.ThrowsAsync<ArgumentException>(() => client.TestConnectionAsync(" "));
        await Assert.ThrowsAsync<ArgumentException>(() => client.StartOAuthAuthorizationAsync(ProviderKind.Codex, " "));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.StartOAuthProxyAsync(
            ProviderKind.Codex,
            0,
            new OAuthAuthorizationSession("auth", "state", "verifier", "redirect", "browser", false, null)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => client.AddApiKeyConnectionAsync(
            ProviderKind.OpenRouter, "name", "key", 0));
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, string, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (Requests)
            {
                Requests.Add(request);
                Bodies.Add(body);
            }

            return responseFactory(request, body);
        }
    }
}
