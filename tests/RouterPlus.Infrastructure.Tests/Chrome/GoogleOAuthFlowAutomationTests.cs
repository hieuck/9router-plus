using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.Infrastructure.Tests.Chrome;

public sealed class GoogleOAuthFlowAutomationTests
{
    [Fact]
    public void Constructor_rejects_null_client()
    {
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new TestAutomation(null!, "session", "target", "profile@example.test"));

        Assert.Equal("client", exception.ParamName);
    }

    [Fact]
    public async Task WaitAndConsentAsync_returns_completion_result_when_provider_reports_completion()
    {
        await using var cdp = await FakeCdpServer.StartAsync(
            GoogleState(isGoogleOAuthPage: false));
        await using var client = await ConnectAsync(cdp);
        var expected = new OAuthConsentResult(true, true, "already authorized");
        var automation = new TestAutomation(client, "session", "target", "profile@example.test")
        {
            ProviderState = new TestProviderPageState { CurrentUrl = "https://provider.test/complete" },
            Completion = new GoogleOAuthFlowAutomation.CompletionCheckResult(true, expected)
        };

        var result = await automation.WaitAndConsentAsync(
            new Uri("https://provider.test/start"), TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task WaitAndConsentAsync_returns_failure_when_provider_initial_button_cannot_be_clicked()
    {
        await using var cdp = await FakeCdpServer.StartAsync(
            GoogleState(isGoogleOAuthPage: false));
        await using var client = await ConnectAsync(cdp);
        var automation = new TestAutomation(client, "session", "target", "profile@example.test")
        {
            ProviderState = new TestProviderPageState { CurrentUrl = "https://provider.test/start" },
            ClickProviderInitialButton = true,
            ProviderInitialButtonClicked = false
        };

        var result = await automation.WaitAndConsentAsync(
            new Uri("https://provider.test/start"), TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Could not click provider initial button", result.Message);
    }

    [Fact]
    public async Task WaitAndConsentAsync_returns_failure_when_provider_account_picker_cannot_be_clicked()
    {
        await using var cdp = await FakeCdpServer.StartAsync(
            GoogleState(isGoogleOAuthPage: false));
        await using var client = await ConnectAsync(cdp);
        var automation = new TestAutomation(client, "session", "target", "profile@example.test")
        {
            ProviderState = new TestProviderPageState { CurrentUrl = "https://provider.test/picker" },
            ClickProviderAccountPicker = true,
            ProviderAccountPickerClicked = false
        };

        var result = await automation.WaitAndConsentAsync(
            new Uri("https://provider.test/picker"), TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Could not select provider account from picker", result.Message);
    }

    private static async Task<ChromeCdpClient> ConnectAsync(FakeCdpServer server)
    {
        var client = new ChromeCdpClient(server.HttpUri);
        await client.ConnectAsync(CancellationToken.None);
        return client;
    }

    private static object GoogleState(
        bool isGoogleOAuthPage,
        bool hasAccountPicker = false,
        bool hasGoogleTotpInput = false,
        bool hasGoogleConsentButton = false) => new
        {
            isGoogleOAuthPage,
            currentUrl = "https://accounts.google.com/oauth",
            hasAccountPicker,
            hasGoogleTotpInput,
            hasGoogleConsentButton
        };

    private sealed class TestAutomation : GoogleOAuthFlowAutomation
    {
        public ProviderOAuthPageState? ProviderState { get; init; }
        public CompletionCheckResult Completion { get; init; } = new(false);
        public bool ClickProviderInitialButton { get; init; }
        public bool ProviderInitialButtonClicked { get; init; }
        public bool ClickProviderAccountPicker { get; init; }
        public bool ProviderAccountPickerClicked { get; init; }
        protected override bool ShouldClickProviderConsent(CombinedOAuthPageState state) => false;

        public TestAutomation(
            ChromeCdpClient client,
            string sessionId,
            string targetId,
            string profileEmail)
            : base(client, sessionId, targetId, profileEmail)
        {
        }

        protected override Task<ProviderOAuthPageState?> ReadProviderPageStateAsync(
            CancellationToken cancellationToken) => Task.FromResult(ProviderState);

        protected override CompletionCheckResult CheckCompletion(CombinedOAuthPageState state) => Completion;

        protected override void LogPageState(CombinedOAuthPageState state)
        {
        }

        protected override bool ShouldClickProviderInitialButton(CombinedOAuthPageState state) =>
            ClickProviderInitialButton;

        protected override Task<bool> TryClickProviderInitialButtonAsync(
            CombinedOAuthPageState state,
            CancellationToken cancellationToken) =>
            Task.FromResult(ProviderInitialButtonClicked);

        protected override bool ShouldClickProviderAccountPicker(CombinedOAuthPageState state) =>
            ClickProviderAccountPicker;

        protected override Task<bool> TryClickProviderAccountPickerAsync(
            CombinedOAuthPageState state,
            CancellationToken cancellationToken) =>
            Task.FromResult(ProviderAccountPickerClicked);
    }

    private sealed record TestProviderPageState : ProviderOAuthPageState;

    private sealed class FakeCdpServer : IAsyncDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _shutdown = new();
        private readonly Queue<JsonElement> _responses = new();
        private readonly object _responseLock = new();
        private JsonElement _fallbackResponse;
        private readonly Task _acceptTask;

        private FakeCdpServer(HttpListener listener, Uri httpUri)
        {
            _listener = listener;
            HttpUri = httpUri;
            _acceptTask = AcceptLoopAsync();
        }

        public Uri HttpUri { get; }

        public static async Task<FakeCdpServer> StartAsync(params object[] responses)
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            var listener = new HttpListener();
            var uri = new Uri($"http://127.0.0.1:{port}/");
            listener.Prefixes.Add(uri.ToString());
            listener.Start();

            var server = new FakeCdpServer(listener, uri);
            foreach (var response in responses)
                await server.EnqueueAsync(response);
            server.SetFallbackResponse(responses.Length == 0 ? GoogleState(isGoogleOAuthPage: false) : responses[0]);
            return server;
        }

        public async Task EnqueueAsync(object response)
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(response));
            lock (_responseLock)
                _responses.Enqueue(document.RootElement.Clone());
            await Task.CompletedTask;
        }

        private void SetFallbackResponse(object response)
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(response));
            lock (_responseLock)
                _fallbackResponse = document.RootElement.Clone();
        }

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (!_shutdown.IsCancellationRequested)
                {
                    var context = await _listener.GetContextAsync();
                    if (context.Request.Url?.AbsolutePath == "/json/version")
                    {
                        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
                        {
                            webSocketDebuggerUrl = new Uri(HttpUri, "cdp").ToString().Replace("http://", "ws://")
                        }));
                        context.Response.ContentType = "application/json";
                        context.Response.ContentLength64 = body.Length;
                        await context.Response.OutputStream.WriteAsync(body);
                        context.Response.Close();
                        continue;
                    }

                    if (context.Request.IsWebSocketRequest)
                    {
                        var socket = (await context.AcceptWebSocketAsync(null)).WebSocket;
                        _ = HandleSocketAsync(socket);
                        continue;
                    }

                    context.Response.StatusCode = 404;
                    context.Response.Close();
                }
            }
            catch (HttpListenerException) when (_shutdown.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException) when (_shutdown.IsCancellationRequested)
            {
            }
        }

        private async Task HandleSocketAsync(WebSocket socket)
        {
            var buffer = new byte[8192];
            try
            {
                while (socket.State == WebSocketState.Open && !_shutdown.IsCancellationRequested)
                {
                    var received = await socket.ReceiveAsync(buffer, _shutdown.Token);
                    if (received.MessageType == WebSocketMessageType.Close)
                        break;

                    using var request = JsonDocument.Parse(buffer.AsMemory(0, received.Count));
                    var id = request.RootElement.GetProperty("id").GetInt32();
                    JsonElement value;
                    lock (_responseLock)
                    {
                        value = _responses.Count == 0 ? _fallbackResponse : _responses.Dequeue();
                    }

                    var response = JsonSerializer.SerializeToUtf8Bytes(new { id, result = new { value } });
                    await socket.SendAsync(response, WebSocketMessageType.Text, true, _shutdown.Token);
                }
            }
            catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
            {
            }
            finally
            {
                socket.Dispose();
            }
        }

        public async ValueTask DisposeAsync()
        {
            _shutdown.Cancel();
            _listener.Stop();
            try
            {
                await _acceptTask;
            }
            catch (HttpListenerException)
            {
            }
            _listener.Close();
            _shutdown.Dispose();
        }
    }
}
