using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace RouterPlus.Infrastructure.Tests;

/// <summary>
/// Minimal loopback HTTP/WebSocket fake CDP server for integration tests.
/// Copied from the private <c>LocalCdpServer</c> helper in ChromeCdpClientTests.
/// </summary>
public sealed class LoopbackCdpServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly string _webSocketUrl;
    private WebSocket? _webSocket;

    private LoopbackCdpServer(int port, string webSocketUrl)
    {
        BaseUri = new Uri($"http://127.0.0.1:{port}/");
        _webSocketUrl = webSocketUrl;
        _listener.Prefixes.Add(BaseUri.ToString());
    }

    public Uri BaseUri { get; }
    public Task<WebSocket> WebSocketTask { get; private set; } = null!;

    public static async Task<LoopbackCdpServer> CreateAsync(string? webSocketUrl = null)
    {
        for (var attempt = 0; ; attempt++)
        {
            var port = GetFreePort();
            var server = new LoopbackCdpServer(port, webSocketUrl ?? $"ws://127.0.0.1:{port}/devtools");
            try
            {
                server._listener.Start();
                server.WebSocketTask = server.AcceptWebSocketAsync();
                await Task.Yield();
                return server;
            }
            catch (HttpListenerException) when (attempt < 5)
            {
                server._listener.Close();
            }
        }
    }

    public async Task<JsonElement> ReceiveJsonAsync(CancellationToken cancellationToken = default)
    {
        var buffer = new byte[4096];
        using var message = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await _webSocket!.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            message.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        return JsonDocument.Parse(message.ToArray()).RootElement.Clone();
    }

    public static async Task SendJsonAsync(WebSocket webSocket, string json, CancellationToken cancellationToken = default)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
    }

    private async Task<WebSocket> AcceptWebSocketAsync()
    {
        var versionContext = await _listener.GetContextAsync();
        versionContext.Response.ContentType = "application/json";
        var versionBytes = Encoding.UTF8.GetBytes($"{{\"webSocketDebuggerUrl\":\"{_webSocketUrl}\"}}");
        versionContext.Response.ContentLength64 = versionBytes.Length;
        await versionContext.Response.OutputStream.WriteAsync(versionBytes);
        versionContext.Response.Close();

        var webSocketContext = await _listener.GetContextAsync();
        var accepted = await webSocketContext.AcceptWebSocketAsync(null);
        _webSocket = accepted.WebSocket;
        return _webSocket;
    }

    public async ValueTask DisposeAsync()
    {
        _listener.Stop();
        _listener.Close();
        if (_webSocket is { State: not WebSocketState.Closed and not WebSocketState.Aborted })
        {
            _webSocket.Abort();
            _webSocket.Dispose();
        }

        try
        {
            await WebSocketTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Best effort during teardown.
        }
    }

    private static int GetFreePort()
    {
        using var tcpListener = new TcpListener(IPAddress.Loopback, 0);
        tcpListener.Start();
        return ((IPEndPoint)tcpListener.LocalEndpoint).Port;
    }
}