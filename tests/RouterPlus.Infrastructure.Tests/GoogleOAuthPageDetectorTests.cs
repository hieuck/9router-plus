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
        // Arrange

        // Act
        var result = GoogleOAuthPageDetector.IsGoogleOAuthHost(host);

        // Assert
        Assert.True(result);
    }

    [Theory]
    [InlineData("google.com")]
    [InlineData("accounts.google.com.evil.example")]
    [InlineData("")]
    public void IsGoogleOAuthHost_rejects_non_accounts_google_hosts(string host)
    {
        // Arrange

        // Act
        var result = GoogleOAuthPageDetector.IsGoogleOAuthHost(host);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task TryDetectAsync_returns_null_when_page_is_not_google_oauth()
    {
        // Arrange
        await using var server = await FakeCdpServer.StartAsync(
            "{\"isGoogleOAuthPage\":false}");
        await using var client = await server.ConnectClientAsync();

        // Act
        var result = await GoogleOAuthPageDetector.TryDetectAsync(
            client,
            "synthetic-session",
            CancellationToken.None);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task TryDetectAsync_returns_detected_google_page_state()
    {
        // Arrange
        await using var server = await FakeCdpServer.StartAsync(
            "{\"isGoogleOAuthPage\":true,\"currentUrl\":\"https://accounts.google.com/signin\",\"hasAccountPicker\":true,\"hasGoogleTotpInput\":true,\"hasGoogleConsentButton\":false}");
        await using var client = await server.ConnectClientAsync();

        // Act
        var result = await GoogleOAuthPageDetector.TryDetectAsync(
            client,
            "synthetic-session",
            CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("https://accounts.google.com/signin", result.CurrentUrl);
        Assert.True(result.HasAccountPicker);
        Assert.True(result.HasGoogleTotpInput);
        Assert.False(result.HasGoogleConsentButton);
    }

    [Fact]
    public async Task TryDetectAsync_returns_null_when_cdp_response_is_malformed()
    {
        // Arrange
        await using var server = await FakeCdpServer.StartAsync("{\"isGoogleOAuthPage\":true}");
        await using var client = await server.ConnectClientAsync();

        // Act
        var result = await GoogleOAuthPageDetector.TryDetectAsync(
            client,
            "synthetic-session",
            CancellationToken.None);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task TryClickAccountAsync_returns_true_when_cdp_reports_click()
    {
        // Arrange
        await using var server = await FakeCdpServer.StartAsync("{\"clicked\":true,\"found\":true}");
        await using var client = await server.ConnectClientAsync();

        // Act
        var result = await GoogleOAuthPageDetector.TryClickAccountAsync(
            client,
            "synthetic-session",
            "user@example.com",
            CancellationToken.None);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task TryClickAccountAsync_returns_false_when_cdp_reports_no_click()
    {
        // Arrange
        await using var server = await FakeCdpServer.StartAsync("{\"clicked\":false,\"found\":false}");
        await using var client = await server.ConnectClientAsync();

        // Act
        var result = await GoogleOAuthPageDetector.TryClickAccountAsync(
            client,
            "synthetic-session",
            "user@example.com",
            CancellationToken.None);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task TryFillTotpAsync_returns_true_when_cdp_reports_filled()
    {
        // Arrange
        await using var server = await FakeCdpServer.StartAsync("true");
        await using var client = await server.ConnectClientAsync();

        // Act
        var result = await GoogleOAuthPageDetector.TryFillTotpAsync(
            client,
            "synthetic-session",
            "123456",
            CancellationToken.None);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task TryFillTotpAsync_returns_false_when_cdp_reports_not_filled()
    {
        // Arrange
        await using var server = await FakeCdpServer.StartAsync("false");
        await using var client = await server.ConnectClientAsync();

        // Act
        var result = await GoogleOAuthPageDetector.TryFillTotpAsync(
            client,
            "synthetic-session",
            "123456",
            CancellationToken.None);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task TryClickGoogleConsentButtonAsync_returns_true_when_cdp_reports_click()
    {
        // Arrange
        await using var server = await FakeCdpServer.StartAsync("true");
        await using var client = await server.ConnectClientAsync();

        // Act
        var result = await GoogleOAuthPageDetector.TryClickGoogleConsentButtonAsync(
            client,
            "synthetic-session",
            CancellationToken.None);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task TryClickGoogleConsentButtonAsync_returns_false_when_cdp_reports_no_click()
    {
        // Arrange
        await using var server = await FakeCdpServer.StartAsync("false");
        await using var client = await server.ConnectClientAsync();

        // Act
        var result = await GoogleOAuthPageDetector.TryClickGoogleConsentButtonAsync(
            client,
            "synthetic-session",
            CancellationToken.None);

        // Assert
        Assert.False(result);
    }

    private sealed class FakeCdpServer : IAsyncDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _stop = new();
        private readonly int _port;
        private readonly string _value;
        private Task? _serverTask;

        private FakeCdpServer(int port, string value)
        {
            _port = port;
            _value = value;
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        }

        public static async Task<FakeCdpServer> StartAsync(string value)
        {
            var server = new FakeCdpServer(ChromeManagedSession.GetAvailableLoopbackPort(), value);
            server._listener.Start();
            server._serverTask = Task.Run(server.RunAsync);
            await Task.Yield();
            return server;
        }

        public async Task<ChromeCdpClient> ConnectClientAsync()
        {
            var client = new ChromeCdpClient(new Uri($"http://127.0.0.1:{_port}"));
            await client.ConnectAsync(CancellationToken.None);
            return client;
        }

        private async Task RunAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    var context = await _listener.GetContextAsync();
                    if (context.Request.Url?.AbsolutePath == "/json/version")
                    {
                        var response = Encoding.UTF8.GetBytes(
                            $"{{\"webSocketDebuggerUrl\":\"ws://127.0.0.1:{_port}/devtools/browser/test\"}}");
                        context.Response.ContentType = "application/json";
                        context.Response.ContentLength64 = response.Length;
                        await context.Response.OutputStream.WriteAsync(response);
                        context.Response.Close();
                        continue;
                    }

                    if (context.Request.IsWebSocketRequest)
                    {
                        var socket = (await context.AcceptWebSocketAsync(null)).WebSocket;
                        await RunWebSocketAsync(socket);
                        continue;
                    }

                    context.Response.StatusCode = 404;
                    context.Response.Close();
                }
            }
            catch (HttpListenerException) when (_stop.IsCancellationRequested)
            {
            }
            catch (ObjectDisposedException) when (_stop.IsCancellationRequested)
            {
            }
        }

        private async Task RunWebSocketAsync(WebSocket socket)
        {
            var buffer = new byte[4096];
            try
            {
                while (socket.State == WebSocketState.Open && !_stop.IsCancellationRequested)
                {
                    var received = await socket.ReceiveAsync(buffer, _stop.Token);
                    if (received.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    using var request = JsonDocument.Parse(buffer.AsMemory(0, received.Count));
                    var id = request.RootElement.GetProperty("id").GetInt32();
                    var response = Encoding.UTF8.GetBytes(
                        $"{{\"id\":{id},\"result\":{{\"result\":{{\"value\":{_value}}}}}}}");
                    await socket.SendAsync(response, WebSocketMessageType.Text, true, _stop.Token);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
            }
            catch (WebSocketException)
            {
            }
            finally
            {
                socket.Dispose();
            }
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Stop();
            _listener.Close();
            if (_serverTask is not null)
            {
                await _serverTask;
            }

            _stop.Dispose();
        }
    }
}
