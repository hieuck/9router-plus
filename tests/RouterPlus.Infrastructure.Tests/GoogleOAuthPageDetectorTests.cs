using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.Infrastructure.Tests;

public sealed class GoogleOAuthPageDetectorTests
{
    [Theory]
    [InlineData("accounts.google.com")]
    [InlineData("ACCOUNTS.GOOGLE.COM.")]
    [InlineData("accounts.google.com.")]
    public void IsGoogleOAuthHost_accepts_accounts_google_hosts(string host)
    {
        Assert.True(GoogleOAuthPageDetector.IsGoogleOAuthHost(host));
    }

    [Theory]
    [InlineData("google.com")]
    [InlineData("accounts.google.com.evil.example")]
    [InlineData("")]
    public void IsGoogleOAuthHost_rejects_non_accounts_google_hosts(string host)
    {
        Assert.False(GoogleOAuthPageDetector.IsGoogleOAuthHost(host));
    }

    [Fact]
    public async Task TryDetectAsync_returns_null_for_non_google_page()
    {
        await using var server = await FakeCdpServer.StartAsync(
            "{\"result\":{\"value\":{\"isGoogleOAuthPage\":false}}}");
        await using var client = await CreateConnectedClientAsync(server);

        var state = await GoogleOAuthPageDetector.TryDetectAsync(
            client, "session-1", CancellationToken.None);

        Assert.Null(state);
    }

    [Fact]
    public async Task TryDetectAsync_maps_google_page_state()
    {
        await using var server = await FakeCdpServer.StartAsync(
            "{\"result\":{\"value\":{\"isGoogleOAuthPage\":true,\"currentUrl\":\"https://accounts.google.com/choose-an-account\",\"hasAccountPicker\":true,\"hasGoogleTotpInput\":true,\"hasGoogleConsentButton\":false}}}");
        await using var client = await CreateConnectedClientAsync(server);

        var state = await GoogleOAuthPageDetector.TryDetectAsync(
            client, "session-2", CancellationToken.None);

        Assert.NotNull(state);
        Assert.Equal("https://accounts.google.com/choose-an-account", state.CurrentUrl);
        Assert.True(state.HasAccountPicker);
        Assert.True(state.HasGoogleTotpInput);
        Assert.False(state.HasGoogleConsentButton);
    }

    [Fact]
    public async Task TryDetectAsync_returns_null_when_cdp_reports_an_error()
    {
        await using var server = await FakeCdpServer.StartAsync(
            "{\"error\":{\"code\":-32000,\"message\":\"synthetic failure\"}}");
        await using var client = await CreateConnectedClientAsync(server);

        var state = await GoogleOAuthPageDetector.TryDetectAsync(
            client, "session-error", CancellationToken.None);

        Assert.Null(state);
    }

    [Fact]
    public async Task TryDetectAsync_returns_null_when_cdp_state_is_malformed()
    {
        await using var server = await FakeCdpServer.StartAsync(
            "{\"result\":{\"value\":{\"isGoogleOAuthPage\":true}}}");
        await using var client = await CreateConnectedClientAsync(server);

        var state = await GoogleOAuthPageDetector.TryDetectAsync(
            client, "session-3", CancellationToken.None);

        Assert.Null(state);
    }

    [Fact]
    public async Task TryClickAccountAsync_returns_true_and_serializes_special_email()
    {
        const string profileEmail = "qa+google\\account@example.test\"";
        await using var server = await FakeCdpServer.StartAsync(
            "{\"result\":{\"value\":{\"clicked\":true}}}");
        await using var client = await CreateConnectedClientAsync(server);

        var clicked = await GoogleOAuthPageDetector.TryClickAccountAsync(
            client, "session-account", profileEmail, CancellationToken.None);

        Assert.True(clicked);
        var request = await server.GetRequestAsync();
        Assert.Contains(JsonSerializer.Serialize(profileEmail), request.GetProperty("params").GetProperty("expression").GetString());
        Assert.Equal("session-account", request.GetProperty("sessionId").GetString());
    }

    [Fact]
    public async Task TryClickAccountAsync_returns_false_when_account_is_not_clicked()
    {
        await using var server = await FakeCdpServer.StartAsync(
            "{\"result\":{\"value\":{\"clicked\":false}}}");
        await using var client = await CreateConnectedClientAsync(server);

        var clicked = await GoogleOAuthPageDetector.TryClickAccountAsync(
            client, "session-account", "account@example.test", CancellationToken.None);

        Assert.False(clicked);
    }

    [Fact]
    public async Task TryClickAccountAsync_returns_false_when_cdp_value_is_missing()
    {
        await using var server = await FakeCdpServer.StartAsync("{\"result\":{}}");
        await using var client = await CreateConnectedClientAsync(server);

        var clicked = await GoogleOAuthPageDetector.TryClickAccountAsync(
            client, "session-account", "account@example.test", CancellationToken.None);

        Assert.False(clicked);
    }

    [Fact]
    public async Task TryClickAccountAsync_returns_false_for_cdp_error()
    {
        await using var server = await FakeCdpServer.StartAsync(
            "{\"error\":{\"code\":-32000,\"message\":\"synthetic failure\"}}");
        await using var client = await CreateConnectedClientAsync(server);

        var clicked = await GoogleOAuthPageDetector.TryClickAccountAsync(
            client, "session-account", "account@example.test", CancellationToken.None);

        Assert.False(clicked);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TryFillTotpAsync_returns_cdp_boolean(bool filled)
    {
        await using var server = await FakeCdpServer.StartAsync(
            $"{{\"result\":{{\"value\":{filled.ToString().ToLowerInvariant()}}}}}");
        await using var client = await CreateConnectedClientAsync(server);

        var result = await GoogleOAuthPageDetector.TryFillTotpAsync(
            client, "session-totp", "123456", CancellationToken.None);

        Assert.Equal(filled, result);
    }

    [Fact]
    public async Task TryFillTotpAsync_returns_false_when_cdp_value_is_missing()
    {
        await using var server = await FakeCdpServer.StartAsync("{\"result\":{}}");
        await using var client = await CreateConnectedClientAsync(server);

        var result = await GoogleOAuthPageDetector.TryFillTotpAsync(
            client, "session-totp", "123456", CancellationToken.None);

        Assert.False(result);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TryClickGoogleConsentButtonAsync_returns_cdp_boolean(bool clicked)
    {
        await using var server = await FakeCdpServer.StartAsync(
            $"{{\"result\":{{\"value\":{clicked.ToString().ToLowerInvariant()}}}}}");
        await using var client = await CreateConnectedClientAsync(server);

        var result = await GoogleOAuthPageDetector.TryClickGoogleConsentButtonAsync(
            client, "session-consent", CancellationToken.None);

        Assert.Equal(clicked, result);
    }

    [Fact]
    public async Task TryClickGoogleConsentButtonAsync_returns_false_when_cdp_value_is_missing()
    {
        await using var server = await FakeCdpServer.StartAsync("{\"result\":{}}");
        await using var client = await CreateConnectedClientAsync(server);

        var result = await GoogleOAuthPageDetector.TryClickGoogleConsentButtonAsync(
            client, "session-consent", CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task TryClickGoogleConsentButtonAsync_returns_false_for_cdp_error()
    {
        await using var server = await FakeCdpServer.StartAsync(
            "{\"error\":{\"code\":-32000,\"message\":\"synthetic failure\"}}");
        await using var client = await CreateConnectedClientAsync(server);

        var result = await GoogleOAuthPageDetector.TryClickGoogleConsentButtonAsync(
            client, "session-consent", CancellationToken.None);

        Assert.False(result);
    }

    private static async Task<ChromeCdpClient> CreateConnectedClientAsync(FakeCdpServer server)
    {
        var client = new ChromeCdpClient(server.HttpUri);
        await client.ConnectAsync(CancellationToken.None);
        return client;
    }

    private sealed class FakeCdpServer : IAsyncDisposable
    {
        private readonly HttpListener _listener;
        private readonly string _responseJson;
        private readonly TaskCompletionSource<JsonElement> _request = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource _shutdown = new();
        private readonly Task _serverTask;

        private FakeCdpServer(int port, string responseJson)
        {
            HttpUri = new Uri($"http://127.0.0.1:{port}/");
            _responseJson = responseJson;
            _listener = new HttpListener();
            _listener.Prefixes.Add(HttpUri.ToString());
            _listener.Start();
            _serverTask = Task.Run(ServeAsync);
        }

        public Uri HttpUri { get; }

        public static Task<FakeCdpServer> StartAsync(string responseJson)
        {
            var probe = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            probe.Start();
            var port = ((System.Net.IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return Task.FromResult(new FakeCdpServer(port, responseJson));
        }

        public async Task<JsonElement> GetRequestAsync()
        {
            return await _request.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        private async Task ServeAsync()
        {
            try
            {
                while (!_shutdown.IsCancellationRequested)
                {
                    var context = await _listener.GetContextAsync();
                    if (context.Request.Url?.AbsolutePath == "/json/version")
                    {
                        var body = Encoding.UTF8.GetBytes(
                            $"{{\"webSocketDebuggerUrl\":\"ws://127.0.0.1:{HttpUri.Port}/devtools/page/synthetic\"}}");
                        context.Response.ContentType = "application/json";
                        context.Response.ContentLength64 = body.Length;
                        await context.Response.OutputStream.WriteAsync(body);
                        context.Response.Close();
                        continue;
                    }

                    if (!context.Request.IsWebSocketRequest)
                    {
                        context.Response.StatusCode = 404;
                        context.Response.Close();
                        continue;
                    }

                    var socket = (await context.AcceptWebSocketAsync(null)).WebSocket;
                    var buffer = new byte[8192];
                    var received = await socket.ReceiveAsync(buffer, CancellationToken.None);
                    var request = JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, received.Count)).RootElement.Clone();
                    _request.TrySetResult(request);
                    var id = request.GetProperty("id").GetInt32();
                    var response = Encoding.UTF8.GetBytes(
                        $"{{\"id\":{id},\"result\":{_responseJson}}}");
                    await socket.SendAsync(response, WebSocketMessageType.Text, true, CancellationToken.None);
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "synthetic", CancellationToken.None);
                    socket.Dispose();
                    return;
                }
            }
            catch (HttpListenerException) when (_shutdown.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException) when (_shutdown.IsCancellationRequested)
            {
            }
            catch (WebSocketException)
            {
                // The client may close immediately after receiving the synthetic response.
            }
        }

        public async ValueTask DisposeAsync()
        {
            _shutdown.Cancel();
            _listener.Stop();
            try
            {
                await _serverTask;
            }
            catch (HttpListenerException)
            {
            }
        }
    }
}
