using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.Infrastructure.Tests.Chrome;

public sealed class DirectLoginAutomationPredicateTests
{
    [Theory]
    [InlineData(typeof(GitHubDirectLoginAutomation), true)]
    [InlineData(typeof(GitHubDirectLoginAutomation), false)]
    [InlineData(typeof(OpenRouterDirectLoginAutomation), true)]
    [InlineData(typeof(OpenRouterDirectLoginAutomation), false)]
    [InlineData(typeof(KiroDirectLoginAutomation), true)]
    [InlineData(typeof(KiroDirectLoginAutomation), false)]
    [InlineData(typeof(CodexDirectLoginAutomation), true)]
    [InlineData(typeof(CodexDirectLoginAutomation), false)]
    public async Task IsLoginCompleteAsync_returns_loopback_cdp_boolean(Type automationType, bool expected)
    {
        await using var cdp = await FakeCdpServer.StartAsync(FakeCdpResponse.Boolean(expected));
        await using var client = new ChromeCdpClient(cdp.HttpUri);
        await client.ConnectAsync(CancellationToken.None);
        var automation = CreateAutomation(automationType, client);

        var result = await InvokeIsLoginCompleteAsync(automation);

        Assert.Equal(expected, result);
        Assert.Single(cdp.EvaluatedExpressions);
        Assert.Contains("Runtime.evaluate", cdp.Methods);
    }

    [Theory]
    [InlineData(typeof(GitHubDirectLoginAutomation))]
    [InlineData(typeof(OpenRouterDirectLoginAutomation))]
    [InlineData(typeof(KiroDirectLoginAutomation))]
    [InlineData(typeof(CodexDirectLoginAutomation))]
    public async Task IsLoginCompleteAsync_returns_false_when_cdp_has_no_value(Type automationType)
    {
        await using var cdp = await FakeCdpServer.StartAsync(FakeCdpResponse.NoValue());
        await using var client = new ChromeCdpClient(cdp.HttpUri);
        await client.ConnectAsync(CancellationToken.None);
        var automation = CreateAutomation(automationType, client);

        var result = await InvokeIsLoginCompleteAsync(automation);

        Assert.False(result);
    }

    [Theory]
    [InlineData(typeof(GitHubDirectLoginAutomation))]
    [InlineData(typeof(OpenRouterDirectLoginAutomation))]
    [InlineData(typeof(KiroDirectLoginAutomation))]
    [InlineData(typeof(CodexDirectLoginAutomation))]
    public async Task IsLoginCompleteAsync_returns_false_when_cdp_fails(Type automationType)
    {
        await using var cdp = await FakeCdpServer.StartAsync(FakeCdpResponse.Error());
        await using var client = new ChromeCdpClient(cdp.HttpUri);
        await client.ConnectAsync(CancellationToken.None);
        var automation = CreateAutomation(automationType, client);

        var result = await InvokeIsLoginCompleteAsync(automation);

        Assert.False(result);
    }

    private static DirectLoginAutomation CreateAutomation(Type automationType, ChromeCdpClient client) =>
        automationType == typeof(GitHubDirectLoginAutomation)
            ? new GitHubDirectLoginAutomation(client, "session-under-test", "target-under-test", "test@example.invalid", "not-a-real-password")
            : automationType == typeof(OpenRouterDirectLoginAutomation)
                ? new OpenRouterDirectLoginAutomation(client, "session-under-test", "target-under-test", "test@example.invalid", "not-a-real-password")
                : automationType == typeof(KiroDirectLoginAutomation)
                    ? new KiroDirectLoginAutomation(client, "session-under-test", "target-under-test", "test@example.invalid", "not-a-real-password")
                    : new CodexDirectLoginAutomation(client, "session-under-test", "target-under-test", "test@example.invalid", "not-a-real-password");



    private static async Task<bool> InvokeIsLoginCompleteAsync(DirectLoginAutomation automation)
    {
        var method = automation.GetType().GetMethod(
            "IsLoginCompleteAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var task = (Task<bool>)method.Invoke(automation, [CancellationToken.None])!;
        return await task;
    }

    private sealed record FakeCdpResponse(string Kind, bool? Value = null)
    {
        public static FakeCdpResponse Boolean(bool value) => new("boolean", value);
        public static FakeCdpResponse NoValue() => new("no-value");
        public static FakeCdpResponse Error() => new("error");
    }

    private sealed class FakeCdpServer : IAsyncDisposable
    {
        private readonly HttpListener _listener;
        private readonly FakeCdpResponse _response;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _serveTask;
        private int _disposed;

        private FakeCdpServer(HttpListener listener, FakeCdpResponse response, int port)
        {
            _listener = listener;
            _response = response;
            HttpUri = new Uri($"http://127.0.0.1:{port}/");
            _serveTask = Task.Run(ServeAsync);
        }

        public Uri HttpUri { get; }
        public List<string> Methods { get; } = [];
        public List<string> EvaluatedExpressions { get; } = [];

        public static async Task<FakeCdpServer> StartAsync(FakeCdpResponse response)
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            var server = new FakeCdpServer(listener, response, port);
            await Task.Yield();
            return server;
        }

        private async Task ServeAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (HttpListenerException) when (_stop.IsCancellationRequested)
                {
                    return;
                }
                catch (ObjectDisposedException) when (_stop.IsCancellationRequested)
                {
                    return;
                }

                if (context.Request.Url?.AbsolutePath == "/json/version")
                {
                    var webSocketUri = new Uri(HttpUri, "devtools/page/fake");
                    var body = JsonSerializer.Serialize(new
                    {
                        webSocketDebuggerUrl = $"ws://{webSocketUri.Host}:{webSocketUri.Port}{webSocketUri.AbsolutePath}"
                    });
                    var bytes = Encoding.UTF8.GetBytes(body);
                    context.Response.ContentType = "application/json";
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes);
                    context.Response.Close();
                    continue;
                }

                if (!context.Request.IsWebSocketRequest)
                {
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    continue;
                }

                var webSocketContext = await context.AcceptWebSocketAsync(null);
                await HandleWebSocketAsync(webSocketContext.WebSocket);
                return;
            }
        }

        private async Task HandleWebSocketAsync(WebSocket webSocket)
        {
            var buffer = new byte[16 * 1024];
            try
            {
                while (webSocket.State == WebSocketState.Open && !_stop.IsCancellationRequested)
                {
                    using var message = new MemoryStream();
                    WebSocketReceiveResult receiveResult;
                    do
                    {
                        receiveResult = await webSocket.ReceiveAsync(buffer, _stop.Token);
                        if (receiveResult.MessageType == WebSocketMessageType.Close)
                        {
                            await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                            return;
                        }
                        message.Write(buffer, 0, receiveResult.Count);
                    }
                    while (!receiveResult.EndOfMessage);

                    using var document = JsonDocument.Parse(message.ToArray());
                    var root = document.RootElement;
                    var id = root.GetProperty("id").GetInt32();
                    var method = root.GetProperty("method").GetString()!;
                    Methods.Add(method);
                    if (method == "Runtime.evaluate")
                    {
                        EvaluatedExpressions.Add(root.GetProperty("params").GetProperty("expression").GetString()!);
                    }

                    var response = CreateResponse(id);
                    var bytes = Encoding.UTF8.GetBytes(response);
                    await webSocket.SendAsync(bytes, WebSocketMessageType.Text, true, _stop.Token);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
        }

        private string CreateResponse(int id) => _response.Kind switch
        {
            "boolean" => JsonSerializer.Serialize(new
            {
                id,
                result = new { result = new { type = "boolean", value = _response.Value!.Value } }
            }),
            "no-value" => JsonSerializer.Serialize(new { id, result = new { result = new { } } }),
            "error" => JsonSerializer.Serialize(new
            {
                id,
                error = new { code = -32000, message = "synthetic CDP failure" }
            }),
            _ => throw new InvalidOperationException("Unknown fake CDP response kind.")
        };

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            _stop.Cancel();
            _listener.Stop();
            try
            {
                await _serveTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                // The listener may be blocked in an in-flight WebSocket receive.
            }
            catch (WebSocketException)
            {
                // The client may close without a full handshake during disposal.
            }
            _listener.Close();
            _stop.Dispose();
        }
    }
}
