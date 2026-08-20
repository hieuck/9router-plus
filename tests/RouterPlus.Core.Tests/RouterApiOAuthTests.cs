using System.Net;
using System.Net.Http.Json;
using System.Text;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Router;

namespace RouterPlus.Core.Tests;

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
