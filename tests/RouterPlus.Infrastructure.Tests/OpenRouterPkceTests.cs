using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RouterPlus.Infrastructure.Router;

namespace RouterPlus.Infrastructure.Tests;

public sealed class OpenRouterPkceTests
{
    [Fact]
    public void CreateS256Pair_challenge_is_base64url_sha256_of_verifier()
    {
        var pair = OpenRouterPkce.CreateS256Pair();

        Assert.False(string.IsNullOrWhiteSpace(pair.Verifier));
        Assert.Equal(
            OpenRouterPkce.ToBase64Url(SHA256.HashData(Encoding.ASCII.GetBytes(pair.Verifier))),
            pair.Challenge);
        Assert.DoesNotContain('+', pair.Challenge);
        Assert.DoesNotContain('/', pair.Challenge);
        Assert.DoesNotContain('=', pair.Challenge);
    }

    [Fact]
    public void BuildAuthorizationUrl_includes_callback_and_s256_challenge()
    {
        var callback = new Uri("http://127.0.0.1:51423/callback");
        var url = OpenRouterPkce.BuildAuthorizationUrl(callback, "abc_challenge");

        Assert.Equal("openrouter.ai", url.Host);
        Assert.Equal("/auth", url.AbsolutePath);
        Assert.Contains("callback_url=http%3A%2F%2F127.0.0.1%3A51423%2Fcallback", url.Query);
        Assert.Contains("code_challenge=abc_challenge", url.Query);
        Assert.Contains("code_challenge_method=S256", url.Query);
    }

    [Fact]
    public void TryGetAuthorizationCode_returns_code_or_error()
    {
        Assert.True(OpenRouterPkce.TryGetAuthorizationCode(
            new OAuthCallbackData("or-code", null, null, null, null),
            out var code,
            out var error));
        Assert.Equal("or-code", code);
        Assert.Null(error);

        Assert.False(OpenRouterPkce.TryGetAuthorizationCode(
            new OAuthCallbackData(null, null, null, "access_denied", "User denied"),
            out var deniedCode,
            out var deniedError));
        Assert.Null(deniedCode);
        Assert.Equal("User denied", deniedError);
    }

    [Fact]
    public async Task ExchangeCodeForApiKeyAsync_posts_pkce_body_and_returns_key()
    {
        var handler = new RecordingHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"key":"sk-or-v1-oauth"}""", Encoding.UTF8, "application/json")
            }
        };
        using var http = new HttpClient(handler);

        var result = await OpenRouterPkce.ExchangeCodeForApiKeyAsync(
            http,
            "auth-code",
            "verifier-value",
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("sk-or-v1-oauth", result.ApiKey);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal(OpenRouterPkce.KeyExchangeEndpoint, handler.Request.RequestUri!.AbsoluteUri);

        using var body = JsonDocument.Parse(handler.RequestBody);
        Assert.Equal("auth-code", body.RootElement.GetProperty("code").GetString());
        Assert.Equal("verifier-value", body.RootElement.GetProperty("code_verifier").GetString());
        Assert.Equal("S256", body.RootElement.GetProperty("code_challenge_method").GetString());
    }

    [Fact]
    public async Task ExchangeCodeForApiKeyAsync_maps_expired_code()
    {
        var handler = new RecordingHandler
        {
            Response = new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = new StringContent("Authorization code expired", Encoding.UTF8, "text/plain")
            }
        };
        using var http = new HttpClient(handler);

        var result = await OpenRouterPkce.ExchangeCodeForApiKeyAsync(
            http,
            "stale-code",
            "verifier-value",
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("expired", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string RequestBody { get; private set; } = string.Empty;

        public required HttpResponseMessage Response { get; init; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            RequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return Response;
        }
    }
}
