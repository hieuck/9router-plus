using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace RouterPlus.Infrastructure.Tests;

/// <summary>
/// Minimal loopback HTTP/WebSocket fake CDP server for integration tests.
/// Supports multiple concurrent WebSocket connections (simulating multiple CDP clients).
/// </summary>
public sealed class LoopbackCdpServer : IAsyncDisposable
{
    private readonly HttpListener _listener = new();
    private readonly string _webSocketUrl;
    private readonly Channel<WebSocket> _wsChannel = Channel.CreateUnbounded<WebSocket>();
    private readonly ConcurrentDictionary<WebSocket, HttpListenerContext> _wsContexts = new();
    private bool _disposed;
    private int _connectionCount = 0;
    private WebSocket? _currentWebSocket;

    private LoopbackCdpServer(int port, string webSocketUrl)
    {
        BaseUri = new Uri($"http://127.0.0.1:{port}/");
        _webSocketUrl = webSocketUrl;
        _listener.Prefixes.Add(BaseUri.ToString());
    }

    public Uri BaseUri { get; }
    public Task<WebSocket> WebSocketTask => WaitForNextWebSocketAsync(CancellationToken.None);

    public static async Task<LoopbackCdpServer> CreateAsync(string? webSocketUrl = null)
    {
        for (var attempt = 0; ; attempt++)
        {
            var port = GetFreePort();
            var server = new LoopbackCdpServer(port, webSocketUrl ?? $"ws://127.0.0.1:{port}/devtools");
            try
            {
                server._listener.Start();
                // Start accepting connections in background
                _ = server.AcceptConnectionsAsync();
                await Task.Yield();
                return server;
            }
            catch (HttpListenerException) when (attempt < 5)
            {
                server._listener.Close();
            }
        }
    }

    /// <summary>
    /// Gets the next connected WebSocket in FIFO order, waiting for a new connection if none available.
    /// </summary>
    public async Task<WebSocket> WaitForNextWebSocketAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var ws = await _wsChannel.Reader.ReadAsync(cancellationToken);
            _currentWebSocket = ws;
            return ws;
        }
        catch (ChannelClosedException)
        {
            throw new ObjectDisposedException(nameof(LoopbackCdpServer));
        }
    }

    public async Task<JsonElement> ReceiveJsonAsync(CancellationToken cancellationToken = default)
    {
        if (_currentWebSocket is null)
        {
            _currentWebSocket = await WaitForNextWebSocketAsync(cancellationToken);
        }
        return await ReceiveJsonAsync(_currentWebSocket, cancellationToken);
    }

    public async Task<JsonElement> ReceiveJsonAsync(WebSocket webSocket, CancellationToken cancellationToken = default)
    {
        var buffer = new byte[4096];
        using var message = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
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

    private async Task AcceptConnectionsAsync()
    {
        try
        {
            while (!_disposed && _listener.IsListening)
            {
                HttpListenerContext? context = null;
                try
                {
                    context = await _listener.GetContextAsync();
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }

                if (context == null)
                    continue;

                var connNum = Interlocked.Increment(ref _connectionCount);
                Console.WriteLine($"[Server] Connection #{connNum}: {context.Request.HttpMethod} {context.Request.Url?.PathAndQuery}");
                
                if (context.Request.IsWebSocketRequest)
                {
                    Console.WriteLine($"[Server] Connection #{connNum}: WebSocket upgrade");
                    // WebSocket upgrade request - accept the WebSocket
                    var webSocketContext = await context.AcceptWebSocketAsync(null);
                    var ws = webSocketContext.WebSocket;
                    Console.WriteLine($"[Server] Connection #{connNum}: WebSocket accepted, writing to channel");
                    // Keep the ORIGINAL HttpListenerContext alive for the lifetime of the WebSocket
                    // to prevent GC from disposing it and closing the underlying TCP connection.
                    // HttpListenerWebSocketContext keeps a reference to the original context internally,
                    // but we explicitly store the context to be absolutely sure.
                    _wsContexts.TryAdd(ws, context);
                    await _wsChannel.Writer.WriteAsync(ws);
                    Console.WriteLine($"[Server] Connection #{connNum}: WebSocket written to channel");
                    // Do NOT write to HTTP response after AcceptWebSocketAsync - it's already sent the upgrade response
                }
                else
                {
                    Console.WriteLine($"[Server] Connection #{connNum}: HTTP request");
                    // HTTP /json/version request
                    context.Response.ContentType = "application/json";
                    var versionBytes = Encoding.UTF8.GetBytes($"{{\"webSocketDebuggerUrl\":\"{_webSocketUrl}\"}}");
                    context.Response.ContentLength64 = versionBytes.Length;
                    await context.Response.OutputStream.WriteAsync(versionBytes);
                    context.Response.OutputStream.Close();
                    Console.WriteLine($"[Server] Connection #{connNum}: HTTP response sent");
                }
            }
        }
        catch (HttpListenerException)
        {
            // Listener stopped
        }
        catch (ObjectDisposedException)
        {
            // Listener disposed
        }
    }

    /// <summary>
    /// Call when a WebSocket is no longer needed to release its HttpListenerContext.
    /// </summary>
    internal void ReleaseWebSocketContext(WebSocket ws)
    {
        if (_wsContexts.TryRemove(ws, out var context))
        {
            // The context will be GC'd; closing the response stream ensures clean teardown
            try { context.Response.OutputStream?.Close(); } catch { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        _listener.Stop();
        _listener.Close();
        _wsChannel.Writer.Complete();

        while (_wsChannel.Reader.TryRead(out var ws))
        {
            if (ws.State != WebSocketState.Closed && ws.State != WebSocketState.Aborted)
            {
                try { ws.Abort(); } catch { }
                ws.Dispose();
            }
            // Release associated context
            _wsContexts.TryRemove(ws, out _);
        }

        _wsContexts.Clear();

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