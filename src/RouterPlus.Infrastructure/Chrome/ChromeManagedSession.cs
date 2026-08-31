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
    private readonly ManagedChromeTargetConnector _targetConnector;
    private string? _tempUserDataDirectory;
    private bool _disposed;

    internal ChromeManagedSession(Process process, Uri devToolsBaseUri, string sessionMarker)
    {
        _process = process;
        _devToolsBaseUri = devToolsBaseUri;
        _sessionMarker = sessionMarker;
        _targetConnector = new ManagedChromeTargetConnector(devToolsBaseUri);
    }

    internal void SetTempUserDataDirectory(string directory)
    {
        _tempUserDataDirectory = directory;
    }

    public Process Process => _process;
    public Uri DevToolsBaseUri => _devToolsBaseUri;

    /// <summary>
    /// Connects a raw CDP client and attaches to the first page target.
    /// Used by orchestrators that need direct page control (e.g., OAuth automation).
    /// </summary>
    public async Task<CdpSession> ConnectAnyTargetAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        return await _targetConnector.ConnectAsync(
            ManagedChromeTargetConnector.SelectFirstPage,
            "No page target found in the managed session.",
            cancellationToken);
    }

    /// <summary>
    /// Connects to the single accounts.google.com target created by the managed run.
    /// </summary>
    public async Task<IGoogleLoginBrowser> ConnectGoogleLoginAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var session = await _targetConnector.ConnectAsync(
            targets => ManagedChromeTargetConnector.SelectMarkedGooglePage(targets, _sessionMarker),
            "No accounts.google.com target with session marker found in the managed session.",
            cancellationToken);

        try
        {
            await WaitForGoogleDocumentAsync(session.Client, session.SessionId, cancellationToken);
            return new GoogleLoginCdpBrowser(session.Client, session.SessionId, session.TargetId);
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Navigates the connected page to a new URL via CDP.
    /// Caller must first call <see cref="ConnectAnyTargetAsync"/>.
    /// </summary>
    public static async Task NavigateAsync(CdpSession session, Uri url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(url);

        await session.Client.CallAsync(
            "Page.navigate",
            new { url = url.ToString() },
            cancellationToken,
            session.SessionId);

        // Give Chrome a moment to start navigation before caller polls state
        await Task.Delay(500, cancellationToken);
    }


    /// <summary>
    /// Connects to the managed OpenRouter keys page and returns a pair of adapters
    /// sharing the same CDP session: one drives the OpenRouter page (Clerk sign-in,
    /// wizard, New Key), the other drives Google sign-in once OAuth redirects there.
    /// Public threads only the interfaces, so no internal CDP type leaks out.
    /// </summary>
    public async Task<(OpenRouterOnboardingCdpBrowser Onboarding, IGoogleLoginBrowser GoogleLogin)> ConnectOpenRouterFlowAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var session = await ConnectAnyTargetAsync(cancellationToken);
        var onboarding = new OpenRouterOnboardingCdpBrowser(session.Client, session.SessionId, session.TargetId);
        var googleLogin = new GoogleLoginCdpBrowser(session.Client, session.SessionId, session.TargetId);
        return (onboarding, googleLogin);
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

        if (_tempUserDataDirectory != null)
        {
            try
            {
                if (Directory.Exists(_tempUserDataDirectory))
                {
                    Directory.Delete(_tempUserDataDirectory, recursive: true);
                }
            }
            catch
            {
                // Best effort cleanup.
            }
        }
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
                    throw new InvalidOperationException("Chrome exited immediately with code 0. The selected profile may already be open. Close all Chrome windows using this profile and retry.");
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

    private static async Task WaitForGoogleDocumentAsync(
        ChromeCdpClient client,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var result = await client.CallAsync("Runtime.evaluate", new
                {
                    expression = "window.location.href",
                    returnByValue = true,
                    awaitPromise = false
                }, cancellationToken, sessionId);

                if (result.TryGetProperty("result", out var remoteObject) &&
                    remoteObject.TryGetProperty("value", out var value) &&
                    value.ValueKind == JsonValueKind.String &&
                    Uri.TryCreate(value.GetString(), UriKind.Absolute, out var uri) &&
                    IsAllowedGoogleHost(uri.Host))
                {
                    return;
                }
            }
            catch (InvalidOperationException)
            {
                // The execution context may not exist while the first document loads.
            }
            catch (JsonException)
            {
                // Ignore incomplete CDP responses during initial navigation.
            }

            await Task.Delay(100, cancellationToken);
        }

        throw new TimeoutException("Google page did not finish navigating within the managed session timeout.");
    }

    private static bool IsAllowedGoogleHost(string host)
    {
        return host == "accounts.google.com"
            || host == "myaccount.google.com"
            || host == "www.google.com";
    }

    internal static int GetAvailableLoopbackPort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }
}

/// <summary>
/// Lightweight CDP session wrapper for direct page automation (e.g., OAuth).
/// Returned by <see cref="ChromeManagedSession.ConnectAnyTargetAsync"/>.
/// </summary>
public sealed class CdpSession : IAsyncDisposable
{
    private readonly ChromeCdpClient _client;
    private bool _disposed;

    public CdpSession(ChromeCdpClient client, string sessionId, string targetId)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        TargetId = targetId ?? throw new ArgumentNullException(nameof(targetId));
    }

    public string SessionId { get; }
    public string TargetId { get; }
    public ChromeCdpClient Client => _client;

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _client.DisposeAsync();
    }
}
