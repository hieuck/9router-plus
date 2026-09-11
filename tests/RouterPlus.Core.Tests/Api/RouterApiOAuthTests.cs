using System.Net;
using System.Net.Http.Json;
using System.Text;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Router;

namespace RouterPlus.Core.Tests.Api;

public sealed class RouterApiOAuthTests
{
    [Fact]
    public async Task StartOAuthAuthorization_parses_authorization_session()
    {
        using var httpClient = new HttpClient(new JsonHandler(_ => """{"authUrl":"https://accounts.example/authorize","state":"state-1","codeVerifier":"verifier-1","redirectUri":"http://127.0.0.1:38579/callback","flowType":"browser","callbackPath":"/callback"}"""));
        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        var session = await api.StartOAuthAuthorizationAsync(
            ProviderKind.Kimchi,
            "http://127.0.0.1:38579/callback");

        Assert.Equal("https://accounts.example/authorize", session.AuthUrl);
        Assert.Equal("state-1", session.State);
        Assert.Equal("verifier-1", session.CodeVerifier);
        Assert.Equal("browser", session.FlowType);
    }

    [Fact]
    public async Task StartDeviceCode_parses_kiro_session_metadata()
    {
        using var httpClient = new HttpClient(new JsonHandler(_ => """{"device_code":"device-1","user_code":"ABCD-EFGH","verification_uri":"https://aws.example/device","verification_uri_complete":"https://aws.example/device?user_code=ABCD-EFGH","expires_in":600,"interval":1,"_clientId":"client-1","_clientSecret":"secret-1","_region":"us-east-1","_authMethod":"idc","_startUrl":"https://view.awsapps.com/start","codeVerifier":"verifier-1"}"""));
        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        var session = await api.StartDeviceCodeAsync(ProviderKind.Kiro, "idc");

        Assert.Equal("device-1", session.DeviceCode);
        Assert.Equal("ABCD-EFGH", session.UserCode);
        Assert.Equal("https://aws.example/device?user_code=ABCD-EFGH", session.VerificationUriComplete);
        Assert.Equal("client-1", session.ClientId);
        Assert.Equal("us-east-1", session.Region);
    }

    [Fact]
    public async Task StartOAuthAuthorization_uses_defaults_for_optional_response_fields()
    {
        using var httpClient = new HttpClient(new JsonHandler(_ => """{"authUrl":"https://accounts.example/authorize","state":"state-1","codeVerifier":"verifier-1"}"""));
        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        var session = await api.StartOAuthAuthorizationAsync(ProviderKind.Codex, "http://127.0.0.1/callback");

        Assert.Equal("http://127.0.0.1/callback", session.RedirectUri);
        Assert.Equal("browser", session.FlowType);
        Assert.False(session.FixedPort);
        Assert.Null(session.CallbackPath);
    }

    [Fact]
    public async Task StartOAuthProxy_returns_server_side_result_and_escapes_session_parameters()
    {
        var handler = new RecordingJsonHandler("""{"success":true,"serverSide":true}""");
        using var httpClient = new HttpClient(handler);
        var api = new RouterApiClient(httpClient, "http://localhost:20128");
        var session = new OAuthAuthorizationSession(
            "https://accounts.example/authorize", "state with spaces", "verifier/value",
            "http://127.0.0.1/callback?mode=oauth", "browser", false, "/callback");

        var result = await api.StartOAuthProxyAsync(ProviderKind.Kimchi, 38579, session);

        Assert.True(result.Success);
        Assert.True(result.ServerSide);
        Assert.Equal("GET", handler.Method);
        Assert.Contains("/api/oauth/kimchi/start-proxy?app_port=38579", handler.RequestUri, StringComparison.Ordinal);
        var query = Uri.UnescapeDataString(new Uri(handler.RequestUri!).Query);
        Assert.Contains("state=state with spaces", query, StringComparison.Ordinal);
        Assert.Contains("code_verifier=verifier/value", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartOAuthProxy_throws_router_exception_when_backend_rejects_proxy()
    {
        var handler = new RecordingJsonHandler("""{"success":false,"reason":"callback port unavailable"}""");
        using var httpClient = new HttpClient(handler);
        var api = new RouterApiClient(httpClient, "http://localhost:20128");
        var session = new OAuthAuthorizationSession("https://accounts.example/authorize", "state-1", "verifier-1", "http://127.0.0.1/callback", "browser", false, null);

        var exception = await Assert.ThrowsAsync<RouterApiException>(() =>
            api.StartOAuthProxyAsync(ProviderKind.Codex, 3000, session));

        Assert.Equal("callback port unavailable", exception.Message);
        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
    }

    [Fact]
    public async Task GetOAuthProxyStatus_parses_status_and_error()
    {
        using var httpClient = new HttpClient(new JsonHandler(_ => """{"status":"error","error":"access denied"}"""));
        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        var status = await api.GetOAuthProxyStatusAsync(ProviderKind.Codex, "state/1");

        Assert.Equal("error", status.Status);
        Assert.Equal("access denied", status.Error);
    }

    [Fact]
    public async Task ExchangeOAuthCode_posts_code_verifier_and_state()
    {
        var handler = new RecordingJsonHandler("{}");
        using var httpClient = new HttpClient(handler);
        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        await api.ExchangeOAuthCodeAsync(ProviderKind.OpenRouter, "auth-code", "http://127.0.0.1/callback", "verifier-1", "state-1");

        Assert.Equal("POST", handler.Method);
        Assert.EndsWith("/api/oauth/openrouter/exchange", handler.RequestUri, StringComparison.Ordinal);
        using var body = System.Text.Json.JsonDocument.Parse(handler.RequestBody);
        Assert.Equal("auth-code", body.RootElement.GetProperty("code").GetString());
        Assert.Equal("http://127.0.0.1/callback", body.RootElement.GetProperty("redirectUri").GetString());
        Assert.Equal("verifier-1", body.RootElement.GetProperty("codeVerifier").GetString());
        Assert.Equal("state-1", body.RootElement.GetProperty("state").GetString());
    }

    [Fact]
    public async Task PollDeviceCode_posts_session_metadata_and_parses_failure()
    {
        var handler = new RecordingJsonHandler("""{"success":false,"error":"authorization_pending","errorDescription":"Approve the device first"}""");
        using var httpClient = new HttpClient(handler);
        var api = new RouterApiClient(httpClient, "http://localhost:20128");
        var session = new DeviceCodeSession("device-1", "ABCD", "https://aws.example/device", null, 600, 5, "client-1", "secret-1", "us-east-1", "idc", "https://view.awsapps.com/start", "verifier-1");

        var result = await api.PollDeviceCodeAsync(ProviderKind.Kiro, session);

        Assert.False(result.Success);
        Assert.Equal("authorization_pending", result.Error);
        Assert.Equal("Approve the device first", result.ErrorDescription);
        using var body = System.Text.Json.JsonDocument.Parse(handler.RequestBody);
        Assert.Equal("device-1", body.RootElement.GetProperty("deviceCode").GetString());
        Assert.Equal("verifier-1", body.RootElement.GetProperty("codeVerifier").GetString());
        var extraData = body.RootElement.GetProperty("extraData");
        Assert.Equal("client-1", extraData.GetProperty("_clientId").GetString());
        Assert.Equal("us-east-1", extraData.GetProperty("_region").GetString());
        Assert.Equal("idc", extraData.GetProperty("_authMethod").GetString());
    }

    [Fact]
    public async Task StartDeviceCode_uses_default_expiration_and_interval()
    {
        using var httpClient = new HttpClient(new JsonHandler(_ => """{"device_code":"device-1","verification_uri":"https://aws.example/device"}"""));
        var api = new RouterApiClient(httpClient, "http://localhost:20128");

        var session = await api.StartDeviceCodeAsync(ProviderKind.Kiro);

        Assert.Equal(600, session.ExpiresIn);
        Assert.Equal(5, session.Interval);
        Assert.Null(session.UserCode);
        Assert.Null(session.VerificationUriComplete);
    }

    [Fact]
    public async Task StartOAuthProxy_rejects_invalid_app_port_before_sending_request()
    {
        var handler = new RecordingJsonHandler("{}");
        using var httpClient = new HttpClient(handler);
        var api = new RouterApiClient(httpClient, "http://localhost:20128");
        var session = new OAuthAuthorizationSession("https://accounts.example/authorize", "state-1", "verifier-1", "http://127.0.0.1/callback", "browser", false, null);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            api.StartOAuthProxyAsync(ProviderKind.Codex, 0, session));
        Assert.Null(handler.RequestUri);
    }

    private sealed class RecordingJsonHandler(string responseBody) : HttpMessageHandler
    {
        public string? Method { get; private set; }

        public string? RequestUri { get; private set; }

        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method.Method;
            RequestUri = request.RequestUri?.ToString();
            RequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class JsonHandler(Func<HttpRequestMessage, string> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseFactory(request), Encoding.UTF8, "application/json")
            });
        }
    }
}
