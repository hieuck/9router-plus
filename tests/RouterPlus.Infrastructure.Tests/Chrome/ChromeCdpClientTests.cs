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
        var port = GetAvailablePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();

        var serverTask = ServeErrorResponseAsync(listener, port);
        await using var client = new ChromeCdpClient(new Uri($"http://127.0.0.1:{port}"));

        await client.ConnectAsync(CancellationToken.None);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.CallAsync("Runtime.evaluate", new { expression = "1" }, CancellationToken.None));

        Assert.Equal("CDP method 'Runtime.evaluate' failed: Unknown error (code 0)", exception.Message);
        await serverTask;
    }

    private static int GetAvailablePort()
    {
        using var socket = new TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        return ((IPEndPoint)socket.LocalEndpoint).Port;
    }

    private static async Task ServeErrorResponseAsync(HttpListener listener, int port)
    {
        var versionContext = await listener.GetContextAsync();
        var versionJson = Encoding.UTF8.GetBytes(
            $"{{\"webSocketDebuggerUrl\":\"ws://127.0.0.1:{port}/devtools/browser\"}}");
        versionContext.Response.ContentType = "application/json";
        versionContext.Response.ContentLength64 = versionJson.Length;
        await versionContext.Response.OutputStream.WriteAsync(versionJson);
        versionContext.Response.Close();

        var webSocketContext = await listener.GetContextAsync();
        using var webSocket = (await webSocketContext.AcceptWebSocketAsync(null)).WebSocket;
        var requestBuffer = new byte[4096];
        var request = await webSocket.ReceiveAsync(requestBuffer, CancellationToken.None);
        using var requestDocument = JsonDocument.Parse(Encoding.UTF8.GetString(requestBuffer, 0, request.Count));
        var requestId = requestDocument.RootElement.GetProperty("id").GetInt32();

        var response = Encoding.UTF8.GetBytes($"{{\"id\":{requestId},\"error\":{{}}}}");
        await webSocket.SendAsync(response, WebSocketMessageType.Text, true, CancellationToken.None);
    }
}
