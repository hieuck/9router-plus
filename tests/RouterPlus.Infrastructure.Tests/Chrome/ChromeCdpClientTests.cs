using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.Infrastructure.Tests.Chrome;

public sealed class ChromeCdpClientTests
{
    [Fact]
    public async Task CallAsync_CdpErrorWithoutOptionalDetails_UsesFallbacks()
    {
        using var listener = StartListener(out var port);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = ServeErrorResponseAsync(listener, port, cancellation.Token);

        try
        {
            await using var client = new ChromeCdpClient(new Uri($"http://127.0.0.1:{port}"));

            await client.ConnectAsync(cancellation.Token);
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                client.CallAsync("Runtime.evaluate", new { expression = "1" }, cancellation.Token));

            Assert.Equal("CDP method 'Runtime.evaluate' failed: Unknown error (code 0)", exception.Message);
            await serverTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            cancellation.Cancel();
            listener.Close();
            await StopServerAsync(serverTask);
        }
    }

    private static HttpListener StartListener(out int port)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            port = GetAvailablePort();
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");

            try
            {
                listener.Start();
                return listener;
            }
            catch (HttpListenerException)
            {
                listener.Close();
            }
        }

        throw new InvalidOperationException("Could not bind a loopback HTTP listener.");
    }

    private static int GetAvailablePort()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        return ((IPEndPoint)socket.LocalEndpoint).Port;
    }

    private static async Task ServeErrorResponseAsync(
        HttpListener listener,
        int port,
        CancellationToken cancellationToken)
    {
        var versionContext = await listener.GetContextAsync().WaitAsync(cancellationToken);
        var versionJson = Encoding.UTF8.GetBytes(
            $"{{\"webSocketDebuggerUrl\":\"ws://127.0.0.1:{port}/devtools/browser\"}}");
        versionContext.Response.ContentType = "application/json";
        versionContext.Response.ContentLength64 = versionJson.Length;
        await versionContext.Response.OutputStream.WriteAsync(versionJson, cancellationToken);
        versionContext.Response.Close();

        var webSocketContext = await listener.GetContextAsync().WaitAsync(cancellationToken);
        using var webSocket = (await webSocketContext.AcceptWebSocketAsync(null)).WebSocket;
        var requestBytes = await ReceiveMessageAsync(webSocket, cancellationToken);
        using var requestDocument = JsonDocument.Parse(requestBytes);
        var requestId = requestDocument.RootElement.GetProperty("id").GetInt32();

        var response = Encoding.UTF8.GetBytes($"{{\"id\":{requestId},\"error\":{{}}}}");
        await webSocket.SendAsync(response, WebSocketMessageType.Text, true, cancellationToken);

        // One-way close (no handshake wait): the close frame is ordered after
        // the error frame on the wire, unlike disposing the socket immediately
        // after SendAsync, whose TCP RST can discard the buffered error frame
        // on a loaded machine ("CDP connection closed."). Waiting for the
        // peer's close response here would deadlock: the test only disposes
        // the client after this server task completes.
        await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None);
        using var graceCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await ReceiveMessageAsync(webSocket, graceCts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
        }
    }

    private static async Task<byte[]> ReceiveMessageAsync(
        WebSocket webSocket,
        CancellationToken cancellationToken)
    {
        using var message = new MemoryStream();
        var buffer = new byte[4096];
        WebSocketReceiveResult result;

        do
        {
            result = await webSocket.ReceiveAsync(buffer, cancellationToken);
            await message.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken);
        }
        while (!result.EndOfMessage);

        return message.ToArray();
    }

    private static async Task StopServerAsync(Task serverTask)
    {
        try
        {
            await serverTask.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (HttpListenerException)
        {
        }
        catch (WebSocketException)
        {
        }
        catch (TimeoutException)
        {
        }
    }
}
