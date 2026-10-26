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
    public async Task TryClickAccountAsync_returns_true_and_dispatches_trusted_click_when_coords_found()
    {
        const string profileEmail = "qa+google\\account@example.test\"";
        var client = new FakeCdpClient
        {
            Responses =
            [
                ResultCoords(42, 84),
                EmptyResult(),
                EmptyResult(),
                EmptyResult()
            ]
        };

        var clicked = await GoogleOAuthPageDetector.TryClickAccountAsync(
            client, "session-account", profileEmail, CancellationToken.None);

        Assert.True(clicked);
        Assert.Equal(4, client.Calls.Count);
        Assert.Equal("Runtime.evaluate", client.Calls[0].Method);
        Assert.Equal(3, client.Calls.Count(c => c.Method == "Input.dispatchMouseEvent"));
        Assert.DoesNotContain(".click()", client.Calls[0].Expression);
        Assert.Equal("session-account", client.Calls[0].SessionId);
        Assert.Contains(JsonSerializer.Serialize(profileEmail), client.Calls[0].Expression);
    }

    [Fact]
    public async Task TryClickAccountAsync_returns_false_when_no_coords()
    {
        var client = new FakeCdpClient
        {
            Responses = [ResultNoCoords()]
        };

        var clicked = await GoogleOAuthPageDetector.TryClickAccountAsync(
            client, "session-account", "account@example.test", CancellationToken.None);

        Assert.False(clicked);
        Assert.Single(client.Calls);
        Assert.DoesNotContain(client.Calls, c => c.Method == "Input.dispatchMouseEvent");
    }

    [Fact]
    public async Task TryClickAccountAsync_returns_false_when_cdp_value_is_missing()
    {
        var client = new FakeCdpClient
        {
            Responses = [EmptyResult()]
        };

        var clicked = await GoogleOAuthPageDetector.TryClickAccountAsync(
            client, "session-account", "account@example.test", CancellationToken.None);

        Assert.False(clicked);
    }

    [Fact]
    public async Task TryClickAccountAsync_returns_false_for_cdp_error()
    {
        var client = new FakeCdpClient
        {
            Exceptions = [new InvalidOperationException("synthetic failure")]
        };

        var clicked = await GoogleOAuthPageDetector.TryClickAccountAsync(
            client, "session-account", "account@example.test", CancellationToken.None);

        Assert.False(clicked);
    }

    [Fact]
    public async Task TryClickAccountAsync_dispatches_mouse_events_with_expected_parameters()
    {
        var client = new FakeCdpClient
        {
            Responses =
            [
                ResultCoords(10, 20),
                EmptyResult(),
                EmptyResult(),
                EmptyResult()
            ]
        };

        var clicked = await GoogleOAuthPageDetector.TryClickAccountAsync(
            client, "session-account", "account@example.test", CancellationToken.None);

        Assert.True(clicked);
        var moved = client.Calls[1];
        var pressed = client.Calls[2];
        var released = client.Calls[3];
        Assert.Equal("mouseMoved", GetProperty(moved.Parameters, "type"));
        Assert.Equal("none", GetProperty(moved.Parameters, "button"));
        Assert.Equal("0", GetProperty(moved.Parameters, "buttons"));
        Assert.Equal("mousePressed", GetProperty(pressed.Parameters, "type"));
        Assert.Equal("left", GetProperty(pressed.Parameters, "button"));
        Assert.Equal("1", GetProperty(pressed.Parameters, "buttons"));
        Assert.Equal("1", GetProperty(pressed.Parameters, "clickCount"));
        Assert.Equal("mouseReleased", GetProperty(released.Parameters, "type"));
        Assert.Equal("left", GetProperty(released.Parameters, "button"));
        Assert.Equal("0", GetProperty(released.Parameters, "buttons"));
        Assert.Equal("1", GetProperty(released.Parameters, "clickCount"));
    }

    [Fact]
    public async Task TryFillTotpAsync_returns_true_and_dispatches_trusted_click_when_coords_found()
    {
        var client = new FakeCdpClient
        {
            Responses =
            [
                ResultTotpCoords(42, 84),
                EmptyResult(),
                EmptyResult(),
                EmptyResult()
            ]
        };

        var result = await GoogleOAuthPageDetector.TryFillTotpAsync(
            client, "session-totp", "123456", CancellationToken.None);

        Assert.True(result);
        Assert.Equal(4, client.Calls.Count);
        Assert.Equal("Runtime.evaluate", client.Calls[0].Method);
        Assert.Equal(3, client.Calls.Count(c => c.Method == "Input.dispatchMouseEvent"));
        var moved = client.Calls[1];
        var pressed = client.Calls[2];
        var released = client.Calls[3];
        Assert.Equal("mouseMoved", GetProperty(moved.Parameters, "type"));
        Assert.Equal("0", GetProperty(moved.Parameters, "buttons"));
        Assert.Equal("left", GetProperty(pressed.Parameters, "button"));
        Assert.Equal("1", GetProperty(pressed.Parameters, "buttons"));
        Assert.Equal("1", GetProperty(pressed.Parameters, "clickCount"));
        Assert.Equal("mouseReleased", GetProperty(released.Parameters, "type"));
        Assert.Equal("left", GetProperty(released.Parameters, "button"));
        Assert.Equal("0", GetProperty(released.Parameters, "buttons"));
        Assert.Equal("1", GetProperty(released.Parameters, "clickCount"));
    }

    [Fact]
    public async Task TryFillTotpAsync_returns_false_when_filled_but_no_button()
    {
        var client = new FakeCdpClient
        {
            Responses = [ResultTotpFilledNoFound()]
        };

        var result = await GoogleOAuthPageDetector.TryFillTotpAsync(
            client, "session-totp", "123456", CancellationToken.None);

        Assert.False(result);
        Assert.Single(client.Calls);
        Assert.DoesNotContain(client.Calls, c => c.Method == "Input.dispatchMouseEvent");
    }

    [Fact]
    public async Task TryFillTotpAsync_returns_false_when_script_returns_false()
    {
        var client = new FakeCdpClient
        {
            Responses = [JsonSerializer.SerializeToDocument(new { result = new { value = false } }).RootElement.Clone()]
        };

        var result = await GoogleOAuthPageDetector.TryFillTotpAsync(
            client, "session-totp", "123456", CancellationToken.None);

        Assert.False(result);
        Assert.Single(client.Calls);
    }

    [Fact]
    public async Task TryFillTotpAsync_returns_false_for_cdp_error()
    {
        var client = new FakeCdpClient
        {
            Exceptions = [new InvalidOperationException("synthetic failure")]
        };

        var result = await GoogleOAuthPageDetector.TryFillTotpAsync(
            client, "session-totp", "123456", CancellationToken.None);

        Assert.False(result);
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

    [Fact]
    public async Task TryClickGoogleConsentButtonAsync_returns_true_and_dispatches_trusted_click_when_coords_found()
    {
        var client = new FakeCdpClient
        {
            Responses =
            [
                ResultCoords(10, 20),
                EmptyResult(),
                EmptyResult(),
                EmptyResult()
            ]
        };

        var result = await GoogleOAuthPageDetector.TryClickGoogleConsentButtonAsync(
            client, "session-consent", CancellationToken.None);

        Assert.True(result);
        Assert.Equal(4, client.Calls.Count);
        Assert.Equal(3, client.Calls.Count(c => c.Method == "Input.dispatchMouseEvent"));
        Assert.DoesNotContain(".click()", client.Calls[0].Expression);
    }

    [Fact]
    public async Task TryClickGoogleConsentButtonAsync_returns_false_when_no_coords()
    {
        var client = new FakeCdpClient
        {
            Responses = [ResultNoCoords()]
        };

        var result = await GoogleOAuthPageDetector.TryClickGoogleConsentButtonAsync(
            client, "session-consent", CancellationToken.None);

        Assert.False(result);
        Assert.Single(client.Calls);
        Assert.DoesNotContain(client.Calls, c => c.Method == "Input.dispatchMouseEvent");
    }

    [Fact]
    public async Task TryClickGoogleConsentButtonAsync_returns_false_when_cdp_value_is_missing()
    {
        var client = new FakeCdpClient
        {
            Responses = [EmptyResult()]
        };

        var result = await GoogleOAuthPageDetector.TryClickGoogleConsentButtonAsync(
            client, "session-consent", CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task TryClickGoogleConsentButtonAsync_returns_false_for_cdp_error()
    {
        var client = new FakeCdpClient
        {
            Exceptions = [new InvalidOperationException("synthetic failure")]
        };

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

    [Fact]
    public async Task TryClickAccountAsync_returns_false_when_no_exact_email_match()
    {
        var client = new FakeCdpClient
        {
            Responses = [ResultNoCoords()]
        };

        var clicked = await GoogleOAuthPageDetector.TryClickAccountAsync(
            client, "session-account", "exact@example.test", CancellationToken.None);

        Assert.False(clicked);
        Assert.Single(client.Calls);
        Assert.DoesNotContain(client.Calls, c => c.Method == "Input.dispatchMouseEvent");
    }

    [Fact]
    public async Task TryClickAccountAsync_returns_false_for_offscreen_coords()
    {
        var client = new FakeCdpClient
        {
            Responses = [ResultCoords(150, 20, 100, 100)]
        };

        var clicked = await GoogleOAuthPageDetector.TryClickAccountAsync(
            client, "session-account", "exact@example.test", CancellationToken.None);

        Assert.False(clicked);
        Assert.Single(client.Calls);
        Assert.DoesNotContain(client.Calls, c => c.Method == "Input.dispatchMouseEvent");
    }

    [Fact]
    public async Task TryClickAccountAsync_returns_false_for_non_positive_coords()
    {
        var client = new FakeCdpClient
        {
            Responses = [ResultCoords(0, 20, 100, 100)]
        };

        var clicked = await GoogleOAuthPageDetector.TryClickAccountAsync(
            client, "session-account", "exact@example.test", CancellationToken.None);

        Assert.False(clicked);
        Assert.Single(client.Calls);
    }

    [Fact]
    public async Task TryClickAccountAsync_returns_false_on_exceptionDetails_top_level()
    {
        var client = new FakeCdpClient
        {
            Responses = [ResultExceptionTopLevel()]
        };

        var clicked = await GoogleOAuthPageDetector.TryClickAccountAsync(
            client, "session-account", "exact@example.test", CancellationToken.None);

        Assert.False(clicked);
        Assert.Single(client.Calls);
        Assert.DoesNotContain(client.Calls, c => c.Method == "Input.dispatchMouseEvent");
    }

    [Fact]
    public async Task TryClickAccountAsync_returns_false_on_exceptionDetails_nested()
    {
        var client = new FakeCdpClient
        {
            Responses = [ResultExceptionNested()]
        };

        var clicked = await GoogleOAuthPageDetector.TryClickAccountAsync(
            client, "session-account", "exact@example.test", CancellationToken.None);

        Assert.False(clicked);
        Assert.Single(client.Calls);
        Assert.DoesNotContain(client.Calls, c => c.Method == "Input.dispatchMouseEvent");
    }

    [Fact]
    public async Task TryClickGoogleConsentButtonAsync_skips_disabled_and_returns_false_when_none()
    {
        var client = new FakeCdpClient
        {
            Responses = [ResultNoCoords()]
        };

        var result = await GoogleOAuthPageDetector.TryClickGoogleConsentButtonAsync(
            client, "session-consent", CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task TryClickGoogleConsentButtonAsync_returns_false_on_exceptionDetails()
    {
        var client = new FakeCdpClient
        {
            Responses = [ResultExceptionNested()]
        };

        var result = await GoogleOAuthPageDetector.TryClickGoogleConsentButtonAsync(
            client, "session-consent", CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task TryClickGoogleConsentButtonAsync_returns_false_for_offscreen_coords()
    {
        var client = new FakeCdpClient
        {
            Responses = [ResultCoords(150, 20, 100, 100)]
        };

        var result = await GoogleOAuthPageDetector.TryClickGoogleConsentButtonAsync(
            client, "session-consent", CancellationToken.None);

        Assert.False(result);
        Assert.Single(client.Calls);
    }


    [Fact]
    public async Task TryFillTotpAsync_returns_false_for_offscreen_coords()
    {
        var client = new FakeCdpClient
        {
            Responses = [ResultTotpCoords(150, 20, 100, 100)]
        };

        var result = await GoogleOAuthPageDetector.TryFillTotpAsync(
            client, "session-totp", "123456", CancellationToken.None);

        Assert.False(result);
        Assert.Single(client.Calls);
        Assert.DoesNotContain(client.Calls, c => c.Method == "Input.dispatchMouseEvent");
    }

    [Fact]
    public async Task TryFillTotpAsync_returns_false_for_negative_coords()
    {
        var client = new FakeCdpClient
        {
            Responses = [ResultTotpCoords(-5, -5, 100, 100)]
        };

        var result = await GoogleOAuthPageDetector.TryFillTotpAsync(
            client, "session-totp", "123456", CancellationToken.None);

        Assert.False(result);
        Assert.Single(client.Calls);
        Assert.DoesNotContain(client.Calls, c => c.Method == "Input.dispatchMouseEvent");
    }

    [Fact]
    public async Task TryFillTotpAsync_returns_false_on_exceptionDetails_top_level()
    {
        var client = new FakeCdpClient
        {
            Responses = [ResultExceptionTopLevel()]
        };

        var result = await GoogleOAuthPageDetector.TryFillTotpAsync(
            client, "session-totp", "123456", CancellationToken.None);

        Assert.False(result);
        Assert.Single(client.Calls);
        Assert.DoesNotContain(client.Calls, c => c.Method == "Input.dispatchMouseEvent");
    }

    [Fact]
    public async Task TryFillTotpAsync_returns_false_on_exceptionDetails_nested()
    {
        var client = new FakeCdpClient
        {
            Responses = [ResultExceptionNested()]
        };

        var result = await GoogleOAuthPageDetector.TryFillTotpAsync(
            client, "session-totp", "123456", CancellationToken.None);

        Assert.False(result);
        Assert.Single(client.Calls);
        Assert.DoesNotContain(client.Calls, c => c.Method == "Input.dispatchMouseEvent");
    }

    [Fact]
    public async Task TryFillTotpAsync_returns_false_when_result_value_is_missing()
    {
        var client = new FakeCdpClient
        {
            Responses = [JsonSerializer.SerializeToDocument(new { result = new { } }).RootElement.Clone()]
        };

        var result = await GoogleOAuthPageDetector.TryFillTotpAsync(
            client, "session-totp", "123456", CancellationToken.None);

        Assert.False(result);
        Assert.Single(client.Calls);
    }

    private static JsonElement ResultTotpCoords(double x, double y, double viewportWidth = 100, double viewportHeight = 100, double width = 10, double height = 10) =>
        JsonSerializer.SerializeToDocument(new { result = new { value = new { filled = true, found = true, x, y, viewportWidth, viewportHeight, width, height } } }).RootElement.Clone();

    private static JsonElement ResultTotpFilledNoFound() =>
        JsonSerializer.SerializeToDocument(new { result = new { value = new { filled = true, found = false } } }).RootElement.Clone();

    private static JsonElement ResultCoords(double x, double y, double viewportWidth = 100, double viewportHeight = 100, double width = 10, double height = 10) =>
        JsonSerializer.SerializeToDocument(new { result = new { value = new { found = true, x, y, viewportWidth, viewportHeight, width, height } } }).RootElement.Clone();

    private static JsonElement ResultNoCoords() =>
        JsonSerializer.SerializeToDocument(new { result = new { value = new { found = false } } }).RootElement.Clone();

    private static JsonElement ResultExceptionTopLevel() =>
        JsonSerializer.SerializeToDocument(new { exceptionDetails = new { text = "synthetic error" } }).RootElement.Clone();

    private static JsonElement ResultExceptionNested() =>
        JsonSerializer.SerializeToDocument(new { result = new { exceptionDetails = new { text = "synthetic error" } } }).RootElement.Clone();

    private static JsonElement EmptyResult() =>
        JsonSerializer.SerializeToDocument(new { }).RootElement.Clone();

    private static string GetProperty(object? parameters, string name)
    {
        var prop = parameters?.GetType().GetProperty(name);
        var value = prop?.GetValue(parameters);
        return value?.ToString() ?? string.Empty;
    }

    private sealed record CdpCall(string Method, object? Parameters, string? SessionId, string Expression);

    private sealed class FakeCdpClient : IChromeCdpClient
    {
        public List<JsonElement> Responses { get; init; } = [];
        public List<Exception> Exceptions { get; init; } = [];
        public List<CdpCall> Calls { get; } = [];

        public Task<JsonElement> CallAsync(string method, object? parameters, CancellationToken cancellationToken, string? sessionId = null)
        {
            var expression = parameters?.GetType().GetProperty("expression")?.GetValue(parameters)?.ToString() ?? string.Empty;
            Calls.Add(new CdpCall(method, parameters, sessionId, expression));

            if (Exceptions.Count > 0)
            {
                return Task.FromException<JsonElement>(Exceptions[0]);
            }
            if (Responses.Count == 0)
            {
                throw new InvalidOperationException("No fake CDP response configured.");
            }

            var response = Responses[0];
            Responses.RemoveAt(0);
            return Task.FromResult(response);
        }
    }
}
