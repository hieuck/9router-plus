using System.Net;
using System.Text;
using System.Text.Json;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Router;

namespace RouterPlus.Infrastructure.Tests;

public sealed class RouterApiOAuthSurfaceTests
{
    private const string SyntheticDeviceCode = "synthetic-device-code";
    private const string SyntheticVerifier = "synthetic-code-verifier";
    private const string SyntheticClientSecret = "synthetic-client-secret";

    [Fact]
    public async Task StartOAuthProxyAsync_maps_backend_reason_when_http_request_succeeds()
    {
        using var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json("{\"success\":false,\"reason\":\"synthetic proxy rejection\"}")
        });
        var client = CreateClient(handler);
        var session = new OAuthAuthorizationSession(
            "https://auth.example.test/authorize",
            "synthetic-state",
            SyntheticVerifier,
            "http://127.0.0.1:43123/callback",
            "browser",
            false,
            "/callback");

        var exception = await Assert.ThrowsAsync<RouterApiException>(() =>
            client.StartOAuthProxyAsync(ProviderKind.Codex, 43123, session));

        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        Assert.Equal("synthetic proxy rejection", exception.Message);
        Assert.Equal("GET", handler.Request!.Method.Method);
        Assert.Contains("api/oauth/codex/start-proxy", handler.Request.RequestUri!.AbsolutePath);
        Assert.Contains("state=synthetic-state", handler.Request.RequestUri.Query);
        Assert.Contains("code_verifier=synthetic-code-verifier", handler.Request.RequestUri.Query);
    }

    [Fact]
    public async Task StartDeviceCodeAsync_preserves_auth_method_and_optional_backend_metadata()
    {
        using var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json("""
                {
                  "device_code": "synthetic-device-code",
                  "user_code": "synthetic-user-code",
                  "verification_uri": "https://device.example.test",
                  "verification_uri_complete": "https://device.example.test/complete",
                  "expires_in": 300,
                  "interval": 7,
                  "_clientId": "synthetic-client-id",
                  "_clientSecret": "synthetic-client-secret",
                  "_region": "synthetic-region",
                  "_authMethod": "synthetic-auth-method",
                  "_startUrl": "https://start.example.test",
                  "codeVerifier": "synthetic-code-verifier"
                }
                """)
        });
        var client = CreateClient(handler);

        var session = await client.StartDeviceCodeAsync(ProviderKind.Kiro, "auth method/with spaces");

        Assert.Equal(SyntheticDeviceCode, session.DeviceCode);
        Assert.Equal("synthetic-user-code", session.UserCode);
        Assert.Equal(300, session.ExpiresIn);
        Assert.Equal(7, session.Interval);
        Assert.Equal(SyntheticClientSecret, session.ClientSecret);
        Assert.Equal("synthetic-code-verifier", session.CodeVerifier);
        Assert.Equal("/api/oauth/kiro/device-code", handler.Request!.RequestUri!.AbsolutePath);
        Assert.Contains("auth_method=auth%20method%2Fwith%20spaces", handler.Request.RequestUri.Query);
    }

    [Fact]
    public async Task PollDeviceCodeAsync_forwards_synthetic_backend_credentials_and_parses_failure()
    {
        using var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = Json("{\"success\":false,\"error\":\"authorization_pending\",\"errorDescription\":\"synthetic pending\"}")
        });
        var client = CreateClient(handler);
        var session = new DeviceCodeSession(
            SyntheticDeviceCode,
            "synthetic-user-code",
            "https://device.example.test",
            null,
            600,
            5,
            "synthetic-client-id",
            SyntheticClientSecret,
            "synthetic-region",
            "synthetic-auth-method",
            "https://start.example.test",
            SyntheticVerifier);

        var result = await client.PollDeviceCodeAsync(ProviderKind.Kiro, session);

        Assert.False(result.Success);
        Assert.Equal("authorization_pending", result.Error);
        Assert.Equal("synthetic pending", result.ErrorDescription);
        Assert.Equal("POST", handler.Request!.Method.Method);
        Assert.Equal("/api/oauth/kiro/poll", handler.Request.RequestUri!.AbsolutePath);

        using var body = JsonDocument.Parse(handler.RequestBody);
        var root = body.RootElement;
        Assert.Equal(SyntheticDeviceCode, root.GetProperty("deviceCode").GetString());
        Assert.Equal(SyntheticVerifier, root.GetProperty("codeVerifier").GetString());
        Assert.Equal(SyntheticClientSecret, root.GetProperty("extraData").GetProperty("_clientSecret").GetString());
        Assert.Equal("synthetic-region", root.GetProperty("extraData").GetProperty("_region").GetString());
    }

    [Fact]
    public async Task ExchangeOAuthCodeAsync_escapes_provider_route_and_maps_http_failure()
    {
        using var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = Json("{\"error\":\"synthetic failure\"}")
        });
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<RouterApiException>(() =>
            client.ExchangeOAuthCodeAsync(
                ProviderKind.OpenRouter,
                "synthetic authorization code",
                "http://127.0.0.1:43123/callback?x=synthetic",
                SyntheticVerifier,
                "synthetic-state"));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
        Assert.Contains("/api/oauth/openrouter/exchange", handler.Request!.RequestUri!.AbsolutePath);
        using var body = JsonDocument.Parse(handler.RequestBody);
        Assert.Equal("synthetic authorization code", body.RootElement.GetProperty("code").GetString());
        Assert.Equal(SyntheticVerifier, body.RootElement.GetProperty("codeVerifier").GetString());
        Assert.Equal("synthetic-state", body.RootElement.GetProperty("state").GetString());
    }

    private static RouterApiClient CreateClient(RecordingHandler handler) =>
        new(new HttpClient(handler), "https://api.example.test");

    private static StringContent Json(string content) =>
        new(content, Encoding.UTF8, "application/json");

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public RecordingHandler(HttpResponseMessage response) => _response = response;

        public HttpRequestMessage? Request { get; private set; }

        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return _response;
        }
    }
}
