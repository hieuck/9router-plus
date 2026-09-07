using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using RouterPlus.Core.Providers;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Chrome;
using RouterPlus.Infrastructure.Services;

namespace RouterPlus.Infrastructure.Tests;

public sealed class OAuthAutoLoginOrchestratorTests
{
    [Fact]
    public async Task RunAsync_successful_adapter_returns_success_and_request_context()
    {
        // Arrange
        await using var cdpServer = await FakeCdpServer.StartAsync();
        await using var cdp = await cdpServer.ConnectAsync();
        var adapter = new RecordingOAuthAdapter(
            ProviderKind.GitHub,
            new ProviderOAuthResult(true, AlreadyAuthorized: true, "already authorized"));
        var registry = new ProviderOAuthAdapterRegistry(adapter);
        var authentication = new RecordingGoogleAuthenticationService();
        var session = new ChromeManagedSessionForTest();
        var orchestrator = new OAuthAutoLoginOrchestrator(
            session.Session,
            cdp,
            ProviderKind.GitHub,
            registry,
            authentication);

        // Act
        var result = await orchestrator.RunAsync(
            new Uri("https://github.com/login/oauth/authorize"),
            new Uri("https://github.com/"),
            "user@example.test",
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        // Assert
        Assert.Equal(OAuthAutoLoginOutcome.Success, result.Outcome);
        Assert.Equal("already authorized", result.Message);
        Assert.True(result.AlreadyAuthorized);
        Assert.NotNull(adapter.Request);
        Assert.Equal(ProviderKind.GitHub, adapter.Request.Provider);
        Assert.Equal("user@example.test", adapter.Request.ProfileEmail);
        Assert.Equal(new Uri("https://github.com/"), adapter.Request.TargetServiceUri);
        Assert.Same(authentication, adapter.AuthenticationService);
    }

    [Fact]
    public async Task RunAsync_failed_adapter_returns_consent_failed_and_clears_authorized_flag()
    {
        // Arrange
        await using var cdpServer = await FakeCdpServer.StartAsync();
        await using var cdp = await cdpServer.ConnectAsync();
        var adapter = new RecordingOAuthAdapter(
            ProviderKind.Codex,
            new ProviderOAuthResult(false, AlreadyAuthorized: true, "consent was not completed"));
        var orchestrator = new OAuthAutoLoginOrchestrator(
            new ChromeManagedSessionForTest().Session,
            cdp,
            ProviderKind.Codex,
            new ProviderOAuthAdapterRegistry(adapter));

        // Act
        var result = await orchestrator.RunAsync(
            new Uri("https://auth.openai.com/authorize"),
            new Uri("https://chatgpt.com/"),
            "user@example.test",
            TimeSpan.FromSeconds(5),
            CancellationToken.None);

        // Assert
        Assert.Equal(OAuthAutoLoginOutcome.ConsentFailed, result.Outcome);
        Assert.Equal("consent was not completed", result.Message);
        Assert.False(result.AlreadyAuthorized);
        Assert.Equal(ProviderKind.Codex, adapter.Request!.Provider);
    }

    private sealed class RecordingOAuthAdapter : IProviderOAuthAdapter
    {
        private readonly ProviderOAuthResult _result;

        public RecordingOAuthAdapter(ProviderKind provider, ProviderOAuthResult result)
        {
            Provider = provider;
            _result = result;
        }

        public ProviderKind Provider { get; }
        public ProviderOAuthRequest? Request { get; private set; }
        public IGoogleAuthenticationService? AuthenticationService { get; private set; }

        public Task<ProviderOAuthResult> RunAsync(
            ProviderOAuthRequest request,
            IGoogleAuthenticationService googleAuthentication,
            CancellationToken cancellationToken)
        {
            Request = request;
            AuthenticationService = googleAuthentication;
            return Task.FromResult(_result);
        }
    }

    private sealed class RecordingGoogleAuthenticationService : IGoogleAuthenticationService
    {
        public Task<GoogleLoginResult> AuthenticateAsync(
            GoogleAuthenticationRequest request,
            CancellationToken cancellationToken) =>
            Task.FromResult(GoogleLoginResult.Success());
    }

    private sealed class ChromeManagedSessionForTest
    {
        public ChromeManagedSessionForTest()
        {
            var process = System.Diagnostics.Process.GetCurrentProcess();
            Session = new ChromeManagedSession(process, new Uri("http://127.0.0.1"), "test");
        }

        public ChromeManagedSession Session { get; }
    }

    private sealed class FakeCdpServer : IAsyncDisposable
    {
        private readonly HttpListener _listener;
        private readonly CancellationTokenSource _stop = new();
        private readonly int _port;
        private Task? _serverTask;

        private FakeCdpServer(int port)
        {
            _port = port;
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        }

        public static async Task<FakeCdpServer> StartAsync()
        {
            var server = new FakeCdpServer(ChromeManagedSession.GetAvailableLoopbackPort());
            server._listener.Start();
            server._serverTask = Task.Run(() => server.RunAsync());
            await Task.Yield();
            return server;
        }

        public async Task<CdpSession> ConnectAsync()
        {
            var client = new ChromeCdpClient(new Uri($"http://127.0.0.1:{_port}"));
            await client.ConnectAsync(CancellationToken.None);
            return new CdpSession(client, "session", "target");
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
                    }
                    else
                    {
                        context.Response.StatusCode = 404;
                        context.Response.Close();
                    }
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

                    var request = JsonDocument.Parse(Encoding.UTF8.GetString(buffer, 0, received.Count)).RootElement;
                    var id = request.GetProperty("id").GetInt32();
                    var response = Encoding.UTF8.GetBytes($"{{\"id\":{id},\"result\":{{}}}}");
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
