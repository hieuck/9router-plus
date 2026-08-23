using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Manages a Chrome process with loopback-only CDP endpoint.
/// </summary>
public sealed class ChromeManagedSession : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Uri _devToolsBaseUri;
    private readonly string _sessionMarker;
    private bool _disposed;

    internal ChromeManagedSession(Process process, Uri devToolsBaseUri, string sessionMarker)
    {
        _process = process;
        _devToolsBaseUri = devToolsBaseUri;
        _sessionMarker = sessionMarker;
    }

    public Process Process => _process;
    public Uri DevToolsBaseUri => _devToolsBaseUri;

    /// <summary>
    /// Connects to the single accounts.google.com target created by the managed run.
    /// </summary>
    public async Task<IGoogleLoginBrowser> ConnectGoogleLoginAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var client = new ChromeCdpClient(_devToolsBaseUri);
        await client.ConnectAsync(cancellationToken);

        try
        {
            // Poll for the managed target, identified by session marker in URL
            var endTime = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
            string? targetId = null;

            while (DateTimeOffset.UtcNow < endTime && targetId == null)
            {
                var response = await client.CallAsync("Target.getTargets", null, cancellationToken);
                var targetInfos = response.GetProperty("targetInfos");

                foreach (var target in targetInfos.EnumerateArray())
                {
                    var type = target.GetProperty("type").GetString();
                    var url = target.GetProperty("url").GetString();

                    // Look for our session marker in the URL (before or after redirect)
                    if (type == "page" && url != null && url.Contains(_sessionMarker))
                    {
                        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Host == "accounts.google.com")
                        {
                            if (targetId != null)
                            {
                                throw new InvalidOperationException("Multiple accounts.google.com targets with session marker found; exactly one is required.");
                            }
                            targetId = target.GetProperty("targetId").GetString();
                        }
                    }
                }

                if (targetId == null)
                {
                    await Task.Delay(100, cancellationToken);
                }
            }

            if (targetId == null)
            {
                throw new InvalidOperationException("No accounts.google.com target with session marker found in the managed session.");
            }

            var attachResponse = await client.CallAsync("Target.attachToTarget", new { targetId, flatten = true }, cancellationToken);
            var sessionId = attachResponse.GetProperty("sessionId").GetString()!;

            await client.CallAsync("Page.bringToFront", null, cancellationToken, sessionId);

            return new GoogleLoginCdpBrowser(client, sessionId, targetId);
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (!_process.HasExited)
        {
            try
            {
                _process.Kill();
                await _process.WaitForExitAsync();
            }
            catch
            {
                // Best effort
            }
        }

        _process.Dispose();
    }

    internal static async Task<ChromeManagedSession> CreateAsync(
        Process process,
        int port,
        string sessionMarker,
        TimeSpan pollTimeout,
        Func<string, CancellationToken, Task<string>> httpGetAsync,
        CancellationToken cancellationToken)
    {
        var endTime = DateTimeOffset.UtcNow + pollTimeout;

        while (DateTimeOffset.UtcNow < endTime)
        {
            if (process.HasExited)
            {
                var exitCode = process.ExitCode;
                if (exitCode == 0)
                {
                    throw new InvalidOperationException("Chrome exited immediately with code 0. The profile may be in use by another Chrome instance. Close the existing Chrome and retry.");
                }
                throw new InvalidOperationException($"Chrome exited with code {exitCode} before the CDP endpoint became available.");
            }

            try
            {
                var versionUrl = $"http://127.0.0.1:{port}/json/version";
                var json = await httpGetAsync(versionUrl, cancellationToken);
                var doc = JsonDocument.Parse(json);
                var webSocketUrl = doc.RootElement.GetProperty("webSocketDebuggerUrl").GetString()!;

                var wsUri = new Uri(webSocketUrl);
                if (wsUri.Host != "127.0.0.1" && wsUri.Host != "localhost")
                {
                    throw new InvalidOperationException($"CDP endpoint returned non-loopback WebSocket URL: {wsUri.Host}");
                }

                var baseUri = new Uri($"http://127.0.0.1:{port}");
                return new ChromeManagedSession(process, baseUri, sessionMarker);
            }
            catch (HttpRequestException)
            {
                // Endpoint not ready
            }
            catch (JsonException)
            {
                // Malformed response
            }

            await Task.Delay(500, cancellationToken);
        }

        throw new TimeoutException($"Chrome CDP endpoint did not become available within {pollTimeout.TotalSeconds} seconds.");
    }

    internal static int GetAvailableLoopbackPort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }
}
