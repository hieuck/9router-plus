using System.Net;
using System.Net.WebSockets;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.Infrastructure.Tests.Chrome;

public sealed class DirectLoginAutomationTests
{
    public static TheoryData<string> Providers => new()
    {
        "github",
        "codex",
        "kiro",
        "openrouter"
    };

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task RunAsync_CompletesAfterFillingCredentialsWithoutTotp(string provider)
    {
        // Arrange
        await using var cdp = new FakeCdpServer((expression, _) =>
        {
            if (expression.Contains("querySelectorAll", StringComparison.Ordinal))
            {
                return !IsTotpSelector(expression);
            }

            if (expression.Contains("window.location.host", StringComparison.Ordinal))
            {
                return true;
            }

            return true;
        });
        await cdp.StartAsync();
        await using var client = new ChromeCdpClient(cdp.BaseUri);
        await client.ConnectAsync(CancellationToken.None);
        var automation = CreateAutomation(provider, client);

        // Act
        var result = await automation.RunAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("Login completed", result.Message);
        Assert.Equal(7, cdp.RuntimeEvaluateCount);
    }

    [Fact]
    public async Task RunAsync_FillsAndSubmitsTotpOnce_WhenChallengeIsVisible()
    {
        // Arrange
        await using var cdp = new FakeCdpServer((expression, _) =>
        {
            if (expression.Contains("querySelectorAll", StringComparison.Ordinal))
            {
                return true;
            }

            return true;
        });
        await cdp.StartAsync();
        await using var client = new ChromeCdpClient(cdp.BaseUri);
        await client.ConnectAsync(CancellationToken.None);
        var totpCalls = 0;
        var automation = new GitHubDirectLoginAutomation(
            client,
            "session",
            "target",
            "user@example.test",
            "synthetic-password",
            () =>
            {
                totpCalls++;
                return Task.FromResult<string?>("123456");
            });

        // Act
        var result = await automation.RunAsync(TimeSpan.FromSeconds(15), CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, totpCalls);
        Assert.Equal(9, cdp.RuntimeEvaluateCount);
    }

    [Fact]
    public async Task RunAsync_ClicksLoginButtonBeforeWaitingForEmail()
    {
        // Arrange
        await using var cdp = new FakeCdpServer((expression, evaluationNumber) =>
        {
            if (expression.Contains("loginButton", StringComparison.Ordinal))
            {
                return true;
            }

            if (expression.Contains("querySelectorAll", StringComparison.Ordinal))
            {
                return evaluationNumber > 2 && !IsTotpSelector(expression);
            }

            return true;
        });
        await cdp.StartAsync();
        await using var client = new ChromeCdpClient(cdp.BaseUri);
        await client.ConnectAsync(CancellationToken.None);
        var automation = new GitHubDirectLoginAutomation(
            client,
            "session",
            "target",
            "user@example.test",
            "synthetic-password");

        // Act
        var result = await automation.RunAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.True(cdp.RuntimeEvaluateCount >= 8);
    }

    [Fact]
    public async Task RunAsync_ReturnsTimeoutWithoutCallingCdp_WhenDeadlineAlreadyPassed()
    {
        // Arrange
        await using var cdp = new FakeCdpServer((_, _) => true);
        await cdp.StartAsync();
        await using var client = new ChromeCdpClient(cdp.BaseUri);
        await client.ConnectAsync(CancellationToken.None);
        var automation = new GitHubDirectLoginAutomation(
            client,
            "session",
            "target",
            "user@example.test",
            "synthetic-password");

        // Act
        var result = await automation.RunAsync(TimeSpan.Zero, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Timeout waiting for login completion", result.Message);
        Assert.Equal(0, cdp.RuntimeEvaluateCount);
    }

    private static DirectLoginAutomation CreateAutomation(string provider, ChromeCdpClient client)
    {
        return provider switch
        {
            "github" => new GitHubDirectLoginAutomation(client, "session", "target", "user@example.test", "synthetic-password"),
            "codex" => new CodexDirectLoginAutomation(client, "session", "target", "user@example.test", "synthetic-password"),
            "kiro" => new KiroDirectLoginAutomation(client, "session", "target", "user@example.test", "synthetic-password"),
            "openrouter" => new OpenRouterDirectLoginAutomation(client, "session", "target", "user@example.test", "synthetic-password"),
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
        };
    }

    private static bool IsTotpSelector(string expression)
    {
        return expression.Contains("otp", StringComparison.OrdinalIgnoreCase)
            || expression.Contains("mfacode", StringComparison.OrdinalIgnoreCase)
            || expression.Contains("one-time-code", StringComparison.OrdinalIgnoreCase)
            || expression.Contains("verification", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeCdpServer : IAsyncDisposable
    {
        private readonly System.Net.Sockets.TcpListener _listener;
        private readonly Func<string, int, bool> _evaluate;
        private Task? _serverTask;
        private int _runtimeEvaluateCount;
        private int _disposed;

        public FakeCdpServer(Func<string, int, bool> evaluate)
        {
            _evaluate = evaluate;
            var port = GetFreePort();
            BaseUri = new Uri($"http://127.0.0.1:{port}");
            _listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, port);
        }

        public Uri BaseUri { get; }
        public int RuntimeEvaluateCount => Volatile.Read(ref _runtimeEvaluateCount);

        public Task StartAsync()
        {
            _listener.Start();
            _serverTask = Task.Run(ServeAsync);
            return Task.CompletedTask;
        }

        private async Task ServeAsync()
        {
            try
            {
                while (Volatile.Read(ref _disposed) == 0)
                {
                    var tcp = await _listener.AcceptTcpClientAsync();
                    _ = Task.Run(() => ServeConnectionAsync(tcp));
                }
            }
            catch (ObjectDisposedException) when (Volatile.Read(ref _disposed) != 0)
            {
            }
            catch (System.Net.Sockets.SocketException) when (Volatile.Read(ref _disposed) != 0)
            {
            }
        }

        private async Task ServeConnectionAsync(System.Net.Sockets.TcpClient tcp)
        {
            using var client = tcp;
            using var stream = client.GetStream();
            using var requestBuffer = new MemoryStream();
            var one = new byte[1];
            while (requestBuffer.Length < 16_384)
            {
                var read = await stream.ReadAsync(one);
                if (read == 0) return;
                requestBuffer.WriteByte(one[0]);
                if (requestBuffer.Length >= 4)
                {
                    var bytes = requestBuffer.ToArray();
                    if (bytes[^4] == '\r' && bytes[^3] == '\n' && bytes[^2] == '\r' && bytes[^1] == '\n') break;
                }
            }

            var headers = Encoding.ASCII.GetString(requestBuffer.ToArray());
            if (headers.StartsWith("GET /json/version", StringComparison.Ordinal))
            {
                var payload = JsonSerializer.Serialize(new { webSocketDebuggerUrl = $"ws://127.0.0.1:{BaseUri.Port}/devtools/page/test" });
                var body = Encoding.UTF8.GetBytes(payload);
                var response = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(response);
                await stream.WriteAsync(body);
                return;
            }

            var key = headers.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)
                .First(line => line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                .Split(':', 2)[1].Trim();
            var accept = Convert.ToBase64String(System.Security.Cryptography.SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            var handshake = Encoding.ASCII.GetBytes($"HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: {accept}\r\n\r\n");
            await stream.WriteAsync(handshake);
            while (Volatile.Read(ref _disposed) == 0)
            {
                var frame = await ReadWebSocketFrameAsync(stream);
                if (frame is null) return;
                using var request = JsonDocument.Parse(frame);
                var root = request.RootElement;
                var id = root.GetProperty("id").GetInt32();
                var method = root.GetProperty("method").GetString();
                var response = method == "Runtime.evaluate" ? Evaluate(root, id) : new { id, result = new { } };
                await WriteWebSocketFrameAsync(stream, JsonSerializer.SerializeToUtf8Bytes(response));
            }
        }

        private static async Task<byte[]?> ReadWebSocketFrameAsync(NetworkStream stream)
        {
            var header = new byte[2];
            if (await stream.ReadAsync(header) != 2) return null;
            var length = header[1] & 0x7f;
            if (length == 126)
            {
                var extended = new byte[2];
                await stream.ReadExactlyAsync(extended);
                length = (extended[0] << 8) | extended[1];
            }
            var mask = new byte[4];
            await stream.ReadExactlyAsync(mask);
            var payload = new byte[length];
            await stream.ReadExactlyAsync(payload);
            for (var i = 0; i < payload.Length; i++) payload[i] ^= mask[i % 4];
            return payload;
        }

        private static async Task WriteWebSocketFrameAsync(NetworkStream stream, byte[] payload)
        {
            var header = payload.Length < 126 ? new[] { (byte)0x81, (byte)payload.Length } : new[] { (byte)0x81, (byte)126, (byte)(payload.Length >> 8), (byte)payload.Length };
            await stream.WriteAsync(header);
            await stream.WriteAsync(payload);
        }

        private object Evaluate(JsonElement root, int id)
        {
            var expression = root.GetProperty("params").GetProperty("expression").GetString() ?? string.Empty;
            var count = Interlocked.Increment(ref _runtimeEvaluateCount);
            return new
            {
                id,
                result = new
                {
                    result = new
                    {
                        type = "boolean",
                        value = _evaluate(expression, count)
                    }
                }
            };
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                _listener.Stop();
            }
            catch (ObjectDisposedException)
            {
            }

            if (_serverTask is not null)
            {
                await _serverTask;
            }
        }

        private static int GetFreePort()
        {
            using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
