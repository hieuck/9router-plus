using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.Infrastructure.Tests;

public sealed class ChromeCdpClientTests
{
    [Fact]
    public async Task CallAsync_round_trips_response_and_preserves_request_fields()
    {
        await using var server = await FakeCdpServer.StartAsync();
        await using var client = new ChromeCdpClient(server.BaseUri);

        await client.ConnectAsync(CancellationToken.None);

        var requestTask = server.ReceiveRequestAsync();
        var callTask = client.CallAsync(
            "Runtime.evaluate",
            new { expression = "1 + 1", returnByValue = true },
            CancellationToken.None,
            sessionId: "session-1");

        var request = await requestTask;
        Assert.Equal("Runtime.evaluate", request.GetProperty("method").GetString());
        Assert.Equal("session-1", request.GetProperty("sessionId").GetString());
        Assert.Equal("1 + 1", request.GetProperty("params").GetProperty("expression").GetString());
        Assert.True(request.GetProperty("params").GetProperty("returnByValue").GetBoolean());

        await server.SendResponseAsync(request.GetProperty("id").GetInt32(), "{\"value\":2}");

        var result = await callTask;
        Assert.Equal(2, result.GetProperty("value").GetInt32());
    }

    [Fact]
    public async Task CallAsync_reassembles_fragmented_response()
    {
        await using var server = await FakeCdpServer.StartAsync();
        await using var client = new ChromeCdpClient(server.BaseUri);

        await client.ConnectAsync(CancellationToken.None);

        var requestTask = server.ReceiveRequestAsync();
        var callTask = client.CallAsync("Runtime.evaluate", null, CancellationToken.None);
        var request = await requestTask;

        await server.SendFragmentedResponseAsync(
            request.GetProperty("id").GetInt32(),
            "{\"value\":\"fragmented response\"}");

        var result = await callTask;
        Assert.Equal("fragmented response", result.GetProperty("value").GetString());
    }

    [Fact]
    public async Task CallAsync_maps_cdp_error_response_to_invalid_operation()
    {
        await using var server = await FakeCdpServer.StartAsync();
        await using var client = new ChromeCdpClient(server.BaseUri);

        await client.ConnectAsync(CancellationToken.None);

        var requestTask = server.ReceiveRequestAsync();
        var callTask = client.CallAsync("Runtime.evaluate", null, CancellationToken.None);
        var request = await requestTask;

        await server.SendErrorResponseAsync(
            request.GetProperty("id").GetInt32(),
            -32000,
            "synthetic failure");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => callTask);
        Assert.Contains("Runtime.evaluate", exception.Message);
        Assert.Contains("synthetic failure", exception.Message);
        Assert.Contains("-32000", exception.Message);
    }

    private sealed class FakeCdpServer : IAsyncDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _disposalCts = new();
        private readonly object _requestGate = new();
        private readonly Queue<JsonElement> _requests = new();
        private readonly Queue<TaskCompletionSource<JsonElement>> _requestWaiters = new();
        private readonly TaskCompletionSource<WebSocket> _webSocketReady =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task _acceptTask;
        private Task? _receiveTask;
        private WebSocket? _webSocket;

        private FakeCdpServer(HttpListener listener, Uri baseUri)
        {
            _listener = listener;
            BaseUri = baseUri;
            _acceptTask = AcceptLoopAsync();
        }

        public Uri BaseUri { get; }

        public static Task<FakeCdpServer> StartAsync()
        {
            var port = GetFreePort();
            var listener = new HttpListener();
            var baseUri = new Uri($"http://127.0.0.1:{port}/");
            listener.Prefixes.Add(baseUri.ToString());
            listener.Start();
            return Task.FromResult(new FakeCdpServer(listener, baseUri));
        }

        public Task<JsonElement> ReceiveRequestAsync()
        {
            lock (_requestGate)
            {
                if (_requests.Count > 0)
                {
                    return Task.FromResult(_requests.Dequeue());
                }

                var waiter = new TaskCompletionSource<JsonElement>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _requestWaiters.Enqueue(waiter);
                return waiter.Task;
            }
        }

        public async Task SendResponseAsync(int requestId, string resultJson)
        {
            var response = $"{{\"id\":{requestId},\"result\":{resultJson}}}";
            await SendTextAsync(response, endOfMessage: true);
        }

        public async Task SendFragmentedResponseAsync(int requestId, string resultJson)
        {
            var response = Encoding.UTF8.GetBytes(
                $"{{\"id\":{requestId},\"result\":{resultJson}}}");
            var split = response.Length / 2;
            var webSocket = await _webSocketReady.Task;
            await webSocket.SendAsync(
                new ArraySegment<byte>(response, 0, split),
                WebSocketMessageType.Text,
                endOfMessage: false,
                CancellationToken.None);
            await webSocket.SendAsync(
                new ArraySegment<byte>(response, split, response.Length - split),
                WebSocketMessageType.Text,
                endOfMessage: true,
                CancellationToken.None);
        }

        public Task SendErrorResponseAsync(int requestId, int code, string message)
        {
            var escapedMessage = JsonSerializer.Serialize(message);
            return SendTextAsync(
                $"{{\"id\":{requestId},\"error\":{{\"code\":{code},\"message\":{escapedMessage}}}}}",
                endOfMessage: true);
        }


        private async Task SendTextAsync(string response, bool endOfMessage)
        {
            var webSocket = await _webSocketReady.Task;
            var bytes = Encoding.UTF8.GetBytes(response);
            await webSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                endOfMessage,
                CancellationToken.None);
        }

        private async Task AcceptLoopAsync()
        {
            try
            {
                while (!_disposalCts.IsCancellationRequested)
                {
                    var context = await _listener.GetContextAsync();
                    if (context.Request.Url?.AbsolutePath == "/json/version")
                    {
                        var body = Encoding.UTF8.GetBytes(
                            $"{{\"webSocketDebuggerUrl\":\"ws://127.0.0.1:{BaseUri.Port}/devtools/browser/fake\"}}");
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

                    var webSocketContext = await context.AcceptWebSocketAsync(null);
                    _webSocket = webSocketContext.WebSocket;
                    _webSocketReady.TrySetResult(_webSocket);
                    _receiveTask = ReceiveLoopAsync(_webSocket);
                    return;
                }
            }
            catch (HttpListenerException) when (_disposalCts.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException) when (_disposalCts.IsCancellationRequested)
            {
            }
        }

        private async Task ReceiveLoopAsync(WebSocket webSocket)
        {
            var buffer = new byte[4096];
            var message = new List<byte>();

            try
            {
                while (!_disposalCts.IsCancellationRequested && webSocket.State == WebSocketState.Open)
                {
                    var result = await webSocket.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        _disposalCts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    if (result.MessageType != WebSocketMessageType.Text)
                    {
                        continue;
                    }

                    message.AddRange(buffer.AsSpan(0, result.Count).ToArray());
                    if (!result.EndOfMessage)
                    {
                        continue;
                    }

                    using var document = JsonDocument.Parse(message.ToArray());
                    var request = document.RootElement.Clone();
                    message.Clear();
                    TaskCompletionSource<JsonElement>? waiter = null;
                    lock (_requestGate)
                    {
                        if (_requestWaiters.Count > 0)
                        {
                            waiter = _requestWaiters.Dequeue();
                        }
                        else
                        {
                            _requests.Enqueue(request);
                        }
                    }

                    waiter?.TrySetResult(request);
                }
            }
            catch (OperationCanceledException) when (_disposalCts.IsCancellationRequested)
            {
            }
            catch (WebSocketException)
            {
                // The client may close without completing the server-side handshake.
            }
        }

        public async ValueTask DisposeAsync()
        {
            _disposalCts.Cancel();
            _listener.Stop();
            _listener.Close();

            if (_webSocket is { State: WebSocketState.Open })
            {
                try
                {
                    await _webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "test complete",
                        CancellationToken.None);
                }
                catch (WebSocketException)
                {
                }
            }

            try
            {
                await _acceptTask;
            }
            catch (HttpListenerException)
            {
            }

            if (_receiveTask is not null)
            {
                try
                {
                    await _receiveTask;
                }
                catch (OperationCanceledException)
                {
                }
            }

            _disposalCts.Dispose();
        }

        private static int GetFreePort()
        {
            using var socket = new TcpListener(IPAddress.Loopback, 0);
            socket.Start();
            return ((IPEndPoint)socket.LocalEndpoint).Port;
        }
    }
}
