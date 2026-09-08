using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Moq;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Chrome;
using RouterPlus.Infrastructure.Services;

namespace RouterPlus.Infrastructure.Tests;

public sealed class OAuthProviderAdapterBranchTests
{
    public static TheoryData<Type, ProviderKind> SupportedAdapters => new()
    {
        { typeof(CodexOAuthAdapter), ProviderKind.Codex },
        { typeof(GitHubOAuthAdapter), ProviderKind.GitHub },
        { typeof(OpenRouterOAuthAdapter), ProviderKind.OpenRouter },
        { typeof(AwsBuilderIdOAuthAdapter), ProviderKind.Kiro }
    };

    [Theory]
    [MemberData(nameof(SupportedAdapters))]
    public async Task RunAsync_rejects_null_request_before_using_cdp(
        Type adapterType,
        ProviderKind expectedProvider)
    {
        var adapter = CreateAdapter(adapterType);
        var googleAuthentication = new Mock<IGoogleAuthenticationService>().Object;

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            adapter.RunAsync(null!, googleAuthentication, CancellationToken.None));

        Assert.Equal("request", exception.ParamName);
        Assert.Equal(expectedProvider, adapter.Provider);
    }

    [Theory]
    [MemberData(nameof(SupportedAdapters))]
    public async Task RunAsync_rejects_null_google_authentication_before_using_cdp(
        Type adapterType,
        ProviderKind expectedProvider)
    {
        var adapter = CreateAdapter(adapterType);
        await using var cdpSession = CreateSyntheticCdpSession();
        var request = CreateRequest(expectedProvider, cdpSession);

        var exception = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            adapter.RunAsync(request, null!, CancellationToken.None));

        Assert.Equal("googleAuthentication", exception.ParamName);
    }

    [Theory]
    [MemberData(nameof(SupportedAdapters))]
    public async Task RunAsync_rejects_request_for_another_provider_before_using_cdp(
        Type adapterType,
        ProviderKind expectedProvider)
    {
        var adapter = CreateAdapter(adapterType);
        await using var cdpSession = CreateSyntheticCdpSession();
        var wrongProvider = expectedProvider == ProviderKind.Codex
            ? ProviderKind.GitHub
            : ProviderKind.Codex;
        var request = CreateRequest(wrongProvider, cdpSession);
        var googleAuthentication = new Mock<IGoogleAuthenticationService>().Object;

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            adapter.RunAsync(request, googleAuthentication, CancellationToken.None));

        Assert.Equal("request", exception.ParamName);
        Assert.Contains(expectedProvider.ToString(), exception.Message);
    }

    [Theory]
    [MemberData(nameof(SupportedAdapters))]
    public async Task RunAsync_maps_provider_completion_to_success_result(
        Type adapterType,
        ProviderKind provider)
    {
        await using var cdp = await SyntheticCdpServer.StartAsync(provider);
        await cdp.Client.ConnectAsync(CancellationToken.None);
        await using var cdpSession = new CdpSession(
            cdp.Client,
            cdp.SessionId,
            "synthetic-target");
        var request = CreateRequest(provider, cdpSession);
        var googleAuthentication = new Mock<IGoogleAuthenticationService>().Object;

        var result = await CreateAdapter(adapterType).RunAsync(
            request,
            googleAuthentication,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(provider != ProviderKind.Kiro, result.AlreadyAuthorized);
        Assert.Contains(provider switch
        {
            ProviderKind.Codex => "target service",
            ProviderKind.GitHub => "GitHub",
            ProviderKind.OpenRouter => "OpenRouter",
            ProviderKind.Kiro => "AWS Builder ID",
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        }, result.Message);
    }

    [Fact]
    public void Combined_page_state_uses_provider_url_before_google_url()
    {
        var state = new GoogleOAuthFlowAutomation.CombinedOAuthPageState
        {
            ProviderState = new GitHubOAuthPageState
            {
                CurrentUrl = "https://github.com/login/oauth/authorize",
                IsGitHubOAuthPage = true,
                IsTargetService = false,
                HasGitHubAuthButton = true
            },
            GoogleState = new GoogleOAuthPageState
            {
                CurrentUrl = "https://accounts.google.com/signin",
                HasAccountPicker = false,
                HasGoogleTotpInput = false,
                HasGoogleConsentButton = false
            }
        };

        Assert.Equal("https://github.com/login/oauth/authorize", state.CurrentUrl);
        Assert.True(state.IsGoogleOAuthPage);
    }

    [Fact]
    public void Combined_page_state_falls_back_to_google_url_then_empty()
    {
        var googleState = new GoogleOAuthFlowAutomation.CombinedOAuthPageState
        {
            GoogleState = new GoogleOAuthPageState
            {
                CurrentUrl = "https://accounts.google.com/signin",
                HasAccountPicker = true,
                HasGoogleTotpInput = true,
                HasGoogleConsentButton = true
            }
        };
        var emptyState = new GoogleOAuthFlowAutomation.CombinedOAuthPageState();

        Assert.Equal("https://accounts.google.com/signin", googleState.CurrentUrl);
        Assert.Equal(string.Empty, emptyState.CurrentUrl);
        Assert.True(googleState.HasAccountPicker);
        Assert.True(googleState.HasGoogleTotpInput);
        Assert.True(googleState.HasGoogleConsentButton);
        Assert.False(emptyState.IsGoogleOAuthPage);
        Assert.False(emptyState.HasAccountPicker);
        Assert.False(emptyState.HasGoogleTotpInput);
        Assert.False(emptyState.HasGoogleConsentButton);
    }

    private static IProviderOAuthAdapter CreateAdapter(Type adapterType) =>
        (IProviderOAuthAdapter)Activator.CreateInstance(adapterType)!;

    private static ProviderOAuthRequest CreateRequest(
        ProviderKind provider,
        CdpSession cdpSession) =>
        new(
            provider,
            new Uri("https://synthetic.example.test/oauth"),
            new Uri("https://synthetic.example.test/complete"),
            "synthetic@example.test",
            TimeSpan.FromSeconds(1),
            cdpSession);

    private static CdpSession CreateSyntheticCdpSession() =>
        new(
            new ChromeCdpClient(new Uri("http://127.0.0.1:9222")),
            "synthetic-session",
            "synthetic-target");

    private sealed class SyntheticCdpServer : IAsyncDisposable
    {
        private readonly HttpListener _listener;
        private readonly Task _serverTask;
        private readonly int _port;

        private SyntheticCdpServer(HttpListener listener, Task serverTask, int port, ChromeCdpClient client, string sessionId)
        {
            _listener = listener;
            _serverTask = serverTask;
            _port = port;
            Client = client;
            SessionId = sessionId;
        }

        public ChromeCdpClient Client { get; }
        public string SessionId { get; }

        public static Task<SyntheticCdpServer> StartAsync(ProviderKind provider)
        {
            using var portProbe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            portProbe.Start();
            var port = ((System.Net.IPEndPoint)portProbe.LocalEndpoint).Port;
            portProbe.Stop();

            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            var serverTask = ServeAsync(listener, port, provider);
            var client = new ChromeCdpClient(new Uri($"http://127.0.0.1:{port}"));
            return Task.FromResult(new SyntheticCdpServer(listener, serverTask, port, client, "synthetic-session"));
        }

        private static async Task ServeAsync(HttpListener listener, int port, ProviderKind provider)
        {
            var versionContext = await listener.GetContextAsync();
            var versionJson = "{\"webSocketDebuggerUrl\":\"ws://127.0.0.1:" +
                              port +
                              "/devtools/page/synthetic\"}";
            var versionBytes = Encoding.UTF8.GetBytes(versionJson);
            versionContext.Response.ContentType = "application/json";
            versionContext.Response.ContentLength64 = versionBytes.Length;
            await versionContext.Response.OutputStream.WriteAsync(versionBytes);
            versionContext.Response.Close();

            var socketContext = await listener.GetContextAsync();
            using var webSocket = (await socketContext.AcceptWebSocketAsync(null)).WebSocket;
            var buffer = new byte[8192];
            while (webSocket.State == WebSocketState.Open)
            {
                var received = await webSocket.ReceiveAsync(buffer, CancellationToken.None);
                if (received.MessageType == WebSocketMessageType.Close)
                    break;

                using var request = JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, received.Count));
                var root = request.RootElement;
                var id = root.GetProperty("id").GetInt32();
                var expression = root.GetProperty("params").GetProperty("expression").GetString()!;
                var value = expression.Contains("isGoogleOAuthPage", StringComparison.Ordinal)
                    ? new { isGoogleOAuthPage = false }
                    : BuildProviderState(provider);
                var response = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    id,
                    result = new { result = new { value } }
                });
                await webSocket.SendAsync(response, WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }

        private static object BuildProviderState(ProviderKind provider) => provider switch
        {
            ProviderKind.Codex => new
            {
                currentUrl = "https://synthetic.example.test/complete",
                isOpenAIOAuthPage = false,
                isTargetService = true,
                hasGoogleLoginButton = false,
                hasCodexConsentButton = false,
                hasOpenAIAccountPicker = false
            },
            ProviderKind.GitHub => new
            {
                currentUrl = "https://synthetic.example.test/complete",
                isGitHubOAuthPage = false,
                isTargetService = true,
                hasGitHubAuthButton = false
            },
            ProviderKind.OpenRouter => new
            {
                currentUrl = "https://synthetic.example.test/complete",
                isOpenRouterOAuthPage = false,
                isTargetService = true,
                hasGoogleLoginButton = false,
                hasTermsConsentButton = false
            },
            ProviderKind.Kiro => new
            {
                currentUrl = "https://synthetic.example.test/complete",
                isAwsBuilderIdPage = true,
                isCompletionPage = true,
                hasContinueWithGoogleButton = false,
                hasAwsConsentButton = false
            },
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };

        public async ValueTask DisposeAsync()
        {
            _listener.Stop();
            _listener.Close();
            try
            {
                await _serverTask;
            }
            catch (Exception ex) when (ex is HttpListenerException or WebSocketException)
            {
            }
        }
    }
}
