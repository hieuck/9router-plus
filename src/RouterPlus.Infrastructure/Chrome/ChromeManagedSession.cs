using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using RouterPlus.Core.Observability;
using RouterPlus.Core.Security;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Wrapper that owns both the ChromeCdpClient and the GoogleLoginCdpBrowser,
/// implementing IGoogleLoginBrowser + IAsyncDisposable.
/// Ensures the CDP client is disposed when the popup browser is disposed.
/// </summary>
internal sealed class GoogleLoginPopupBrowser : IGoogleLoginBrowser, IAsyncDisposable
{
    private readonly ChromeCdpClient _client;
    private readonly GoogleLoginCdpBrowser _browser;
    private bool _disposed;

    public GoogleLoginPopupBrowser(ChromeCdpClient client, GoogleLoginCdpBrowser browser)
    {
        _client = client;
        _browser = browser;
    }

    public Task<GoogleLoginPageState> ReadStateAsync(CancellationToken cancellationToken)
        => _browser.ReadStateAsync(cancellationToken);

    public Task FillAsync(GoogleLoginField field, string value, CancellationToken cancellationToken)
        => _browser.FillAsync(field, value, cancellationToken);

    public Task SubmitAsync(GoogleLoginField field, CancellationToken cancellationToken)
        => _browser.SubmitAsync(field, cancellationToken);

    public Task<bool> TrySelectAuthenticatorMethodAsync(CancellationToken cancellationToken)
        => _browser.TrySelectAuthenticatorMethodAsync(cancellationToken);

    public Task<bool> TryClickConsentContinueAsync(CancellationToken cancellationToken)
        => _browser.TryClickConsentContinueAsync(cancellationToken);

    public Task<bool> TrySelectAccountOnChooserAsync(string? email, CancellationToken cancellationToken)
        => _browser.TrySelectAccountOnChooserAsync(email, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Dispose the inner browser first
        await _browser.DisposeAsync();

        // Then dispose the client we own
        await _client.DisposeAsync();
    }
}

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
    private string? _agnesTabTargetId;
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

    /// <summary>
    /// Connects to the managed Ollama keys page and returns a pair of adapters
    /// sharing the same CDP session: one drives the Ollama page (WorkOS Google sign-in,
    /// revoke/create key), the other drives Google sign-in once OAuth redirects there.
    /// Public threads only the interfaces, so no internal CDP type leaks out.
    /// </summary>
    public async Task<(OllamaApiKeyCdpBrowser OllamaPage, IGoogleLoginBrowser GoogleLogin)> ConnectOllamaFlowAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var session = await ConnectAnyTargetAsync(cancellationToken);
        var ollamaPage = new OllamaApiKeyCdpBrowser(session.Client, session.SessionId, session.TargetId);
        var googleLogin = new GoogleLoginCdpBrowser(session.Client, session.SessionId, session.TargetId);
        return (ollamaPage, googleLogin);
    }

    /// <summary>
    /// Connects to the managed Agnes AI keys page and returns the Agnes page plus
    /// a factory for connecting to the Google login popup (separate CDP target).
    /// The Agnes page and Google popup are distinct targets; they do NOT share a session.
    /// </summary>
    public async Task<(AgnesApiKeyCdpBrowser AgnesPage, Func<CancellationToken, Task<IGoogleLoginBrowser>> ConnectGooglePopupAsync)> ConnectAgnesFlowAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Capture the set of target IDs BEFORE the Google button is clicked.
        // This snapshot is used by the popup factory to exclude pre-existing targets.
        var preClickTargets = await GetPageTargetIdsAsync(cancellationToken);

        // Connect to the marked Agnes tab (platform.agnes-ai.com with session marker)
        var agnesSession = await _targetConnector.ConnectAsync(
            targets => ManagedChromeTargetConnector.SelectMarkedAgnesPage(targets, _sessionMarker),
            "No platform.agnes-ai.com target with session marker found in the managed session.",
            cancellationToken);

        var agnesPage = new AgnesApiKeyCdpBrowser(agnesSession.Client, agnesSession.SessionId, agnesSession.TargetId);
        _agnesTabTargetId = agnesSession.TargetId;

        Func<CancellationToken, Task<IGoogleLoginBrowser>> connectGooglePopup = async (ct) =>
        {
            return await ConnectGooglePopupAsync(ct, preClickTargets);
        };

        return (agnesPage, connectGooglePopup);
    }

    /// <summary>
    /// Gets all current page target IDs from the managed Chrome session.
    /// Used to capture a snapshot before clicking the Google sign-in button.
    /// </summary>
    internal async Task<HashSet<string>> GetPageTargetIdsAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var client = new ChromeCdpClient(_devToolsBaseUri);
        try
        {
            await client.ConnectAsync(cancellationToken);
            var response = await client.CallAsync("Target.getTargets", null, cancellationToken);
            var targets = ManagedChromeTargetConnector.ReadTargets(response);

            var pageTargetIds = new HashSet<string>();
            foreach (var t in targets)
            {
                if (t.Type == "page")
                {
                    pageTargetIds.Add(t.TargetId);
                }
            }
            return pageTargetIds;
        }
        finally
        {
            await client.DisposeAsync();
        }
    }

    /// <summary>
    /// Polls Target.getTargets for a page target whose URL host is accounts.google.com,
    /// excluding the specified Agnes tab target and any targets that existed before the click.
    /// Attaches to the first new Google popup (deterministic: first in Target.getTargets list order),
    /// brings it to front, waits for document readyState=complete, and returns IGoogleLoginBrowser.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="preClickTargetIds">Snapshot of target IDs captured BEFORE the Google button click.
    /// Any target in this set will be excluded from popup selection.</param>
    /// <param name="popupTimeout">Optional override for the 30-second popup detection timeout (for testing).</param>
    /// <exception cref="InvalidOperationException">No Google popup found within the timeout.</exception>
    /// <exception cref="InvalidOperationException">Document readyState did not reach 'complete' within 10 seconds.</exception>
    /// <exception cref="OperationCanceledException">The operation was cancelled.</exception>
    public async Task<IGoogleLoginBrowser> ConnectGooglePopupAsync(
        CancellationToken cancellationToken,
        HashSet<string>? preClickTargetIds = null,
        TimeSpan? popupTimeout = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var client = new ChromeCdpClient(_devToolsBaseUri);

        try
        {
            await client.ConnectAsync(cancellationToken);

            var deadline = DateTimeOffset.UtcNow + (popupTimeout ?? TimeSpan.FromSeconds(30));
            string? popupTargetId = null;
            int lastTargetsSeen = 0;

            while (DateTimeOffset.UtcNow < deadline && popupTargetId is null)
            {
                var response = await client.CallAsync("Target.getTargets", null, cancellationToken);
                var targets = ManagedChromeTargetConnector.ReadTargets(response);
                lastTargetsSeen = targets.Count;

                // Find accounts.google.com page targets, excluding the Agnes tab and pre-click targets
                var googlePopups = new List<ManagedChromeTarget>();
                foreach (var t in targets)
                {
                    if (t.Type != "page")
                    {
                        continue;
                    }
                    if (t.TargetId == _agnesTabTargetId)
                    {
                        continue;
                    }
                    // Exclude any target that existed before the click
                    if (preClickTargetIds != null && preClickTargetIds.Contains(t.TargetId))
                    {
                        continue;
                    }
                    if (!Uri.TryCreate(t.Url, UriKind.Absolute, out var uri))
                    {
                        continue;
                    }
                    if (uri.Host == "platform.agnes-ai.com")
                    {
                        continue; // exclude Agnes tab
                    }
                    if (uri.Host != "accounts.google.com")
                    {
                        continue;
                    }
                    googlePopups.Add(t);
                }
                // Deterministic: first in Target.getTargets list order (no TargetId ordering)
                // List order from Target.getTargets is deterministic and stable

                if (googlePopups.Count > 0)
                {
                    popupTargetId = googlePopups[0].TargetId;
                    ObservabilityHub.Instance.LogEvent(
                        LogLevel.Info,
                        "ChromeManagedSession",
                        "GooglePopupFound",
                        "Google popup target found",
                        new { targetId = popupTargetId, url = googlePopups[0].Url });
                    break;
                }

                await Task.Delay(100, cancellationToken);
            }

            if (popupTargetId is null)
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Warning,
                    "ChromeManagedSession",
                    "GooglePopupTimeout",
                    "Google popup not found within timeout",
                    new { targetsSeen = lastTargetsSeen, targets_seen = lastTargetsSeen });

                var timeoutSeconds = (popupTimeout ?? TimeSpan.FromSeconds(30)).TotalSeconds;
                throw new InvalidOperationException($"Google login popup (accounts.google.com) did not appear within {timeoutSeconds:F0} seconds.");
            }

            // Attach to the popup
            var attachResponse = await client.CallAsync(
                "Target.attachToTarget",
                new { targetId = popupTargetId, flatten = true },
                cancellationToken);
            var sessionId = attachResponse.GetProperty("sessionId").GetString();
            if (string.IsNullOrWhiteSpace(sessionId))
            {
                throw new InvalidOperationException("CDP attach response did not contain a session identifier for Google popup.");
            }

            await client.CallAsync("Page.bringToFront", null, cancellationToken, sessionId);

            // Wait for document to be ready (readyState = complete)
            var docDeadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
            bool documentReady = false;
            while (DateTimeOffset.UtcNow < docDeadline)
            {
                try
                {
                    var result = await client.CallAsync("Runtime.evaluate", new
                    {
                        expression = "document.readyState",
                        returnByValue = true,
                        awaitPromise = false
                    }, cancellationToken, sessionId);

                    if (result.TryGetProperty("result", out var remoteObject)
                        && remoteObject.TryGetProperty("value", out var value)
                        && value.GetString() == "complete")
                    {
                        documentReady = true;
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw; // Do not swallow cancellation
                }
                catch
                {
                    // Context may not be ready yet
                }
                await Task.Delay(100, cancellationToken);
            }

            if (!documentReady)
            {
                throw new InvalidOperationException("Google login popup document did not reach readyState 'complete' within 10 seconds.");
            }

            var cdpBrowser = new GoogleLoginCdpBrowser(client, sessionId, popupTargetId);
            return new GoogleLoginPopupBrowser(client, cdpBrowser);
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
