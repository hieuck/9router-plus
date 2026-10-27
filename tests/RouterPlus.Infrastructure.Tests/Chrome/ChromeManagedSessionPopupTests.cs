using System.Collections.Generic;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using RouterPlus.Infrastructure.Chrome;
using Xunit;
using Xunit.Abstractions;

namespace RouterPlus.Infrastructure.Tests.Chrome;

public sealed class ChromeManagedSessionPopupTests
{
    private const string AgnesTargetId = "agnes-tab-target";
    private const string PopupTargetId = "popup-2"; // newer (higher ID)
    private const string OlderPopupTargetId = "popup-1"; // older (lower ID)
    private const string PreClickTargetId = "pre-click-target"; // target that exists before click

    private readonly ITestOutputHelper _output;

    public ChromeManagedSessionPopupTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static Process StartLongRunningProcess()
    {
        return Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c ping 127.0.0.1 -n 31 > nul",
            UseShellExecute = false,
            CreateNoWindow = true
        })!;
    }

    private static async Task HandleConnectionAsync(
        WebSocket webSocket,
        CancellationToken cts,
        Func<JsonElement, int, CancellationToken, string?> handler,
        ITestOutputHelper output,
        string name)
    {
        try
        {
            output.WriteLine($"[{name}] Handler started, waiting for websocket...");
            var buffer = new byte[4096];
            using var message = new MemoryStream();
            while (!cts.IsCancellationRequested && webSocket.State == WebSocketState.Open)
            {
                output.WriteLine($"[{name}] Waiting for next message...");
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cts);
                output.WriteLine($"[{name}] Received frame: MessageType={result.MessageType}, Count={result.Count}, EndOfMessage={result.EndOfMessage}");
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    output.WriteLine($"[{name}] WebSocket closed by peer");
                    break;
                }
                if (result.MessageType != WebSocketMessageType.Text)
                {
                    continue;
                }

                for (int i = 0; i < result.Count; i++)
                {
                    message.WriteByte(buffer[i]);
                }

                if (!result.EndOfMessage)
                {
                    continue;
                }

                var json = Encoding.UTF8.GetString(message.ToArray());
                message.SetLength(0);
                output.WriteLine($"[{name}] Full message: {json}");

                var request = JsonDocument.Parse(json).RootElement.Clone();
                var id = request.GetProperty("id").GetInt32();
                var method = request.GetProperty("method").GetString();
                output.WriteLine($"[{name}] Parsed: {method} (id={id})");
                try
                {
                    var responseJson = handler(request, id, cts);
                    if (responseJson != null)
                    {
                        output.WriteLine($"[{name}] Sending response for id={id}: {responseJson}");
                        var bytes = Encoding.UTF8.GetBytes(responseJson);
                        await webSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts);
                        output.WriteLine($"[{name}] Response sent for id={id}");
                    }
                    else
                    {
                        output.WriteLine($"[{name}] No response for id={id}");
                    }
                }
                catch (Exception ex)
                {
                    output.WriteLine($"[{name}] Handler exception for id={id}: {ex}");
                    throw;
                }
            }
            output.WriteLine($"[{name}] Handler finished");
        }
        catch (OperationCanceledException) { output.WriteLine($"[{name}] OperationCanceledException"); }
        catch (WebSocketException ex) { output.WriteLine($"[{name}] WebSocketException: {ex.Message}"); }
        catch (ObjectDisposedException) { output.WriteLine($"[{name}] ObjectDisposedException"); }
        catch (Exception ex) { output.WriteLine($"[{name}] Exception: {ex}"); }
    }

    [Fact]
    public async Task ConnectGooglePopupAsync_attaches_to_first_accounts_google_com_popup_excluding_agnes_tab_and_preclick_targets()
    {
        await using var server = await LoopbackCdpServer.CreateAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var attachToTargetCalled = false;
        var bringToFrontCalled = false;
        var getTargetsCallCount = 0;
        var attachTargetId = string.Empty;

        using var process = StartLongRunningProcess();
        var chromeSession = new ChromeManagedSession(
            process,
            server.BaseUri,
            "test-marker");

        // Start handler for FIRST connection (Agnes page) - it will wait for connection
        var agnesReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var agnesResponder = Task.Run(async () =>
        {
            _output.WriteLine("[Agnes] Waiting for websocket...");
            var agnesWs = await server.WaitForNextWebSocketAsync(cts.Token);
            _output.WriteLine("[Agnes] Got websocket");
            agnesReady.TrySetResult(true);
            await HandleConnectionAsync(agnesWs, cts.Token, (request, id, ct) =>
            {
                var method = request.GetProperty("method").GetString();
                if (method == "Target.getTargets")
                {
                    getTargetsCallCount++;
                    if (getTargetsCallCount == 1)
                    {
                        // First call from ConnectAnyTargetAsync - return just Agnes tab
                        return $"{{\"id\":{id},\"result\":{{\"targetInfos\":[{{\"targetId\":\"{AgnesTargetId}\",\"type\":\"page\",\"url\":\"https://platform.agnes-ai.com/settings/apiKeys\"}}]}}}}";
                    }
                }
                else if (method == "Target.attachToTarget")
                {
                    return $"{{\"id\":{id},\"result\":{{\"sessionId\":\"session-agnes-123\"}}}}";
                }
                else if (method == "Page.bringToFront")
                {
                    return $"{{\"id\":{id},\"result\":{{}}}}";
                }
                return null;
            }, _output, "Agnes");
        }, cts.Token);

        // Connect to Agnes page first - this establishes first CDP connection
        // The handler task will receive the WebSocket when ConnectAnyTargetAsync makes the connection
        _output.WriteLine("[Test] Calling ConnectAnyTargetAsync");
        var agnesBrowser = await chromeSession.ConnectAnyTargetAsync(cts.Token);
        _output.WriteLine("[Test] ConnectAnyTargetAsync completed");

        // Wait for handler to have received the WebSocket
        await agnesReady.Task.WaitAsync(cts.Token);

        // Start handler for SECOND connection (Google popup) - it will wait for connection
        var popupReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var popupResponder = Task.Run(async () =>
        {
            _output.WriteLine("[Popup] Waiting for websocket...");
            var popupWs = await server.WaitForNextWebSocketAsync(cts.Token);
            _output.WriteLine("[Popup] Got websocket");
            popupReady.TrySetResult(true);
            await HandleConnectionAsync(popupWs, cts.Token, (request, id, ct) =>
            {
                var method = request.GetProperty("method").GetString();
                if (method == "Target.getTargets")
                {
                    getTargetsCallCount++;
                    // Return Agnes tab + pre-click target + two Google popups
                    // The pre-click target should be excluded by the snapshot
                    // Deterministic: first in list order = OlderPopupTargetId
                    return $"{{\"id\":{id},\"result\":{{\"targetInfos\":[" +
                           $"{{\"targetId\":\"{AgnesTargetId}\",\"type\":\"page\",\"url\":\"https://platform.agnes-ai.com/settings/apiKeys\"}}," +
                           $"{{\"targetId\":\"{PreClickTargetId}\",\"type\":\"page\",\"url\":\"https://platform.agnes-ai.com/some-page\"}}," +
                           $"{{\"targetId\":\"{OlderPopupTargetId}\",\"type\":\"page\",\"url\":\"https://accounts.google.com/signin/identifier\"}}," +
                           $"{{\"targetId\":\"{PopupTargetId}\",\"type\":\"page\",\"url\":\"https://accounts.google.com/v3/signin/challenge\"}}" +
                           $"]}}}}";
                }
                else if (method == "Target.attachToTarget")
                {
                    attachToTargetCalled = true;
                    attachTargetId = request.GetProperty("params").GetProperty("targetId").GetString();
                    return $"{{\"id\":{id},\"result\":{{\"sessionId\":\"session-popup-123\"}}}}";
                }
                else if (method == "Page.bringToFront")
                {
                    bringToFrontCalled = true;
                    return $"{{\"id\":{id},\"result\":{{}}}}";
                }
                else if (method == "Runtime.evaluate")
                {
                    return $"{{\"id\":{id},\"result\":{{\"result\":{{\"value\":\"complete\"}}}}}}";
                }
                return null;
            }, _output, "Popup");
        }, cts.Token);

        // Create pre-click snapshot with Agnes target and a pre-existing target
        var preClickTargets = new HashSet<string> { AgnesTargetId, PreClickTargetId };

        // Now connect to Google popup - this creates a SECOND CDP connection
        // The handler task will receive the WebSocket when ConnectGooglePopupAsync makes the connection
        _output.WriteLine("[Test] Calling ConnectGooglePopupAsync with preClickTargets");
        var googleLogin = await chromeSession.ConnectGooglePopupAsync(cts.Token, preClickTargets);
        _output.WriteLine("[Test] ConnectGooglePopupAsync completed");

        // Wait for handler to have received the WebSocket
        await popupReady.Task.WaitAsync(cts.Token);

        await googleLogin.DisposeAsync();
        await agnesBrowser.DisposeAsync();
        cts.Cancel();
        try { await Task.WhenAll(agnesResponder, popupResponder); } catch { }

        // Should attach to the first Google popup in list order (OlderPopupTargetId), not the newest
        Assert.Equal(OlderPopupTargetId, attachTargetId);
        Assert.True(attachToTargetCalled);
        Assert.True(bringToFrontCalled);
        Assert.True(getTargetsCallCount >= 2);
    }

    [Fact]
    public async Task ConnectGooglePopupAsync_returns_error_result_when_no_popup_within_timeout()
    {
        // This test verifies the ConnectGooglePopupAsync method returns an error
        // (throws InvalidOperationException with clear message) when no popup appears.
        // Use a very short popup timeout (100ms) to complete very fast.
        // Use CancellationToken.None for the method so the internal deadline fires.
        await using var server = await LoopbackCdpServer.CreateAsync();
        var handlerCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        using var process = StartLongRunningProcess();
        var chromeSession = new ChromeManagedSession(
            process,
            server.BaseUri,
            "test-marker");

        // Start handler for FIRST connection (Agnes page)
        var agnesReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var agnesResponder = Task.Run(async () =>
        {
            var agnesWs = await server.WaitForNextWebSocketAsync(handlerCts.Token);
            agnesReady.TrySetResult(true);
            await HandleConnectionAsync(agnesWs, handlerCts.Token, (request, id, ct) =>
            {
                var method = request.GetProperty("method").GetString();
                if (method == "Target.getTargets")
                {
                    // Return only the Agnes tab - no Google popup
                    return $"{{\"id\":{id},\"result\":{{\"targetInfos\":[{{\"targetId\":\"{AgnesTargetId}\",\"type\":\"page\",\"url\":\"https://platform.agnes-ai.com/settings/apiKeys\"}}]}}}}";
                }
                else if (method == "Target.attachToTarget")
                {
                    return $"{{\"id\":{id},\"result\":{{\"sessionId\":\"session-agnes-123\"}}}}";
                }
                else if (method == "Page.bringToFront")
                {
                    return $"{{\"id\":{id},\"result\":{{}}}}";
                }
                return null;
            }, _output, "Agnes");
        }, handlerCts.Token);

        // Connect to Agnes page first (use no cancellation for this setup call)
        var agnesBrowser = await chromeSession.ConnectAnyTargetAsync(CancellationToken.None);

        // Wait for handler to have received the WebSocket
        await agnesReady.Task.WaitAsync(handlerCts.Token);

        // Start handler for SECOND connection (popup) - DON'T wait for it to complete
        var popupResponder = Task.Run(async () =>
        {
            var popupWs = await server.WaitForNextWebSocketAsync(handlerCts.Token);
            await HandleConnectionAsync(popupWs, handlerCts.Token, (request, id, ct) =>
            {
                var method = request.GetProperty("method").GetString();
                if (method == "Target.getTargets")
                {
                    // Return only the Agnes tab - no Google popup
                    return $"{{\"id\":{id},\"result\":{{\"targetInfos\":[{{\"targetId\":\"{AgnesTargetId}\",\"type\":\"page\",\"url\":\"https://platform.agnes-ai.com/settings/apiKeys\"}}]}}}}";
                }
                return null;
            }, _output, "Popup");
        }, handlerCts.Token);

        // Call ConnectGooglePopupAsync with a very short 1400ms popup timeout
        // This produces "1 seconds" in the error message (1.4 -> F0 = 1)
        // DON'T wait for popup handler to be ready - just call the method
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            chromeSession.ConnectGooglePopupAsync(CancellationToken.None, new HashSet<string>(), TimeSpan.FromMilliseconds(1400)));

        Assert.Contains("Google login popup", exception.Message);
        Assert.Contains("did not appear within 1 seconds", exception.Message);

        // Cancel handlers AFTER test completes
        handlerCts.Cancel();
        try { await Task.WhenAll(agnesResponder, popupResponder); } catch { }

        await agnesBrowser.DisposeAsync();
    }

    [Fact]
    public async Task ConnectGooglePopupAsync_excludes_non_page_targets()
    {
        await using var server = await LoopbackCdpServer.CreateAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var attachTargetId = string.Empty;
        var getTargetsCallCount = 0;

        using var process = StartLongRunningProcess();
        var chromeSession = new ChromeManagedSession(
            process,
            server.BaseUri,
            "test-marker");

        // Start handler for FIRST connection (Agnes page)
        var agnesReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var agnesResponder = Task.Run(async () =>
        {
            var agnesWs = await server.WaitForNextWebSocketAsync(cts.Token);
            agnesReady.TrySetResult(true);
            await HandleConnectionAsync(agnesWs, cts.Token, (request, id, ct) =>
            {
                var method = request.GetProperty("method").GetString();
                if (method == "Target.getTargets")
                {
                    getTargetsCallCount++;
                    if (getTargetsCallCount == 1)
                    {
                        // First call from ConnectAnyTargetAsync
                        return $"{{\"id\":{id},\"result\":{{\"targetInfos\":[{{\"targetId\":\"{AgnesTargetId}\",\"type\":\"page\",\"url\":\"https://platform.agnes-ai.com/settings/apiKeys\"}}]}}}}";
                    }
                }
                else if (method == "Target.attachToTarget")
                {
                    return $"{{\"id\":{id},\"result\":{{\"sessionId\":\"session-agnes-123\"}}}}";
                }
                else if (method == "Page.bringToFront")
                {
                    return $"{{\"id\":{id},\"result\":{{}}}}";
                }
                return null;
            }, _output, "Agnes");
        }, cts.Token);

        // Connect to Agnes page first
        var agnesBrowser = await chromeSession.ConnectAnyTargetAsync(cts.Token);

        // Wait for handler to have received the WebSocket
        await agnesReady.Task.WaitAsync(cts.Token);

        // Start handler for SECOND connection (Google popup)
        var popupReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var popupResponder = Task.Run(async () =>
        {
            var popupWs = await server.WaitForNextWebSocketAsync(cts.Token);
            popupReady.TrySetResult(true);
            await HandleConnectionAsync(popupWs, cts.Token, (request, id, ct) =>
            {
                var method = request.GetProperty("method").GetString();
                if (method == "Target.getTargets")
                {
                    getTargetsCallCount++;
                    // Return: Agnes page target + Google iframe (not page) + Google page target
                    // Respond to ALL getTargets calls so the polling loop continues
                    return $"{{\"id\":{id},\"result\":{{\"targetInfos\":[" +
                           $"{{\"targetId\":\"{AgnesTargetId}\",\"type\":\"page\",\"url\":\"https://platform.agnes-ai.com/settings/apiKeys\"}}," +
                           $"{{\"targetId\":\"iframe-target\",\"type\":\"iframe\",\"url\":\"https://accounts.google.com/signin/iframe\"}}," +
                           $"{{\"targetId\":\"{PopupTargetId}\",\"type\":\"page\",\"url\":\"https://accounts.google.com/signin/identifier\"}}" +
                           $"]}}}}";
                }
                else if (method == "Target.attachToTarget")
                {
                    attachTargetId = request.GetProperty("params").GetProperty("targetId").GetString();
                    return $"{{\"id\":{id},\"result\":{{\"sessionId\":\"session-xyz\"}}}}";
                }
                else if (method == "Page.bringToFront")
                {
                    return $"{{\"id\":{id},\"result\":{{}}}}";
                }
                else if (method == "Runtime.evaluate")
                {
                    return $"{{\"id\":{id},\"result\":{{\"result\":{{\"value\":\"complete\"}}}}}}";
                }
                return null;
            }, _output, "Popup");
        }, cts.Token);

        var googleLogin = await chromeSession.ConnectGooglePopupAsync(cts.Token, new HashSet<string>());

        // Wait for handler to have received the WebSocket
        await popupReady.Task.WaitAsync(cts.Token);

        await googleLogin.DisposeAsync();
        await agnesBrowser.DisposeAsync();
        cts.Cancel();
        try { await Task.WhenAll(agnesResponder, popupResponder); } catch { }

        // Should attach to the page target, not the iframe
        Assert.Equal(PopupTargetId, attachTargetId);
    }

    [Fact]
    public async Task ConnectGooglePopupAsync_throws_when_document_not_ready_within_timeout()
    {
        await using var server = await LoopbackCdpServer.CreateAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        using var process = StartLongRunningProcess();
        var chromeSession = new ChromeManagedSession(
            process,
            server.BaseUri,
            "test-marker");

        // Start handler for FIRST connection (Agnes page)
        var agnesReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var agnesResponder = Task.Run(async () =>
        {
            var agnesWs = await server.WaitForNextWebSocketAsync(cts.Token);
            agnesReady.TrySetResult(true);
            await HandleConnectionAsync(agnesWs, cts.Token, (request, id, ct) =>
            {
                var method = request.GetProperty("method").GetString();
                if (method == "Target.getTargets")
                {
                    // First call from ConnectAnyTargetAsync
                    return $"{{\"id\":{id},\"result\":{{\"targetInfos\":[{{\"targetId\":\"{AgnesTargetId}\",\"type\":\"page\",\"url\":\"https://platform.agnes-ai.com/settings/apiKeys\"}}]}}}}";
                }
                else if (method == "Target.attachToTarget")
                {
                    return $"{{\"id\":{id},\"result\":{{\"sessionId\":\"session-agnes-123\"}}}}";
                }
                else if (method == "Page.bringToFront")
                {
                    return $"{{\"id\":{id},\"result\":{{}}}}";
                }
                return null;
            }, _output, "Agnes");
        }, cts.Token);

        // Connect to Agnes page first
        var agnesBrowser = await chromeSession.ConnectAnyTargetAsync(cts.Token);

        // Wait for handler to have received the WebSocket
        await agnesReady.Task.WaitAsync(cts.Token);

        // Start handler for SECOND connection (Google popup) - document never reaches 'complete'
        var popupReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var popupResponder = Task.Run(async () =>
        {
            var popupWs = await server.WaitForNextWebSocketAsync(cts.Token);
            popupReady.TrySetResult(true);
            await HandleConnectionAsync(popupWs, cts.Token, (request, id, ct) =>
            {
                var method = request.GetProperty("method").GetString();
                if (method == "Target.getTargets")
                {
                    // Return Agnes tab + Google popup
                    return $"{{\"id\":{id},\"result\":{{\"targetInfos\":[" +
                           $"{{\"targetId\":\"{AgnesTargetId}\",\"type\":\"page\",\"url\":\"https://platform.agnes-ai.com/settings/apiKeys\"}}," +
                           $"{{\"targetId\":\"{PopupTargetId}\",\"type\":\"page\",\"url\":\"https://accounts.google.com/signin/identifier\"}}" +
                           $"]}}}}";
                }
                else if (method == "Target.attachToTarget")
                {
                    return $"{{\"id\":{id},\"result\":{{\"sessionId\":\"session-popup-123\"}}}}";
                }
                else if (method == "Page.bringToFront")
                {
                    return $"{{\"id\":{id},\"result\":{{}}}}";
                }
                else if (method == "Runtime.evaluate")
                {
                    // Return 'loading' instead of 'complete' - document never ready
                    return $"{{\"id\":{id},\"result\":{{\"result\":{{\"value\":\"loading\"}}}}}}";
                }
                return null;
            }, _output, "Popup");
        }, cts.Token);

        // Should throw InvalidOperationException with clear message about readyState
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            chromeSession.ConnectGooglePopupAsync(cts.Token, new HashSet<string>()));

        Assert.Contains("readyState", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("complete", exception.Message, StringComparison.OrdinalIgnoreCase);

        // Wait for handler to have received the WebSocket
        try { await popupReady.Task.WaitAsync(cts.Token); } catch { }

        await agnesBrowser.DisposeAsync();
        cts.Cancel();
        try { await Task.WhenAll(agnesResponder, popupResponder); } catch { }
    }

    [Fact]
    public async Task ConnectGooglePopupAsync_excludes_preclick_targets_including_existing_google_accounts_target()
    {
        // Pre-click exclusion test: include a pre-existing accounts.google.com target
        // in the pre-click snapshot AND a new Google popup; assert only the new popup is selected.
        await using var server = await LoopbackCdpServer.CreateAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var attachTargetId = string.Empty;

        using var process = StartLongRunningProcess();
        var chromeSession = new ChromeManagedSession(
            process,
            server.BaseUri,
            "test-marker");

        // Start handler for FIRST connection (Agnes page)
        var agnesReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var agnesResponder = Task.Run(async () =>
        {
            var agnesWs = await server.WaitForNextWebSocketAsync(cts.Token);
            agnesReady.TrySetResult(true);
            await HandleConnectionAsync(agnesWs, cts.Token, (request, id, ct) =>
            {
                var method = request.GetProperty("method").GetString();
                if (method == "Target.getTargets")
                {
                    // First call from ConnectAnyTargetAsync - return just Agnes tab
                    return $"{{\"id\":{id},\"result\":{{\"targetInfos\":[{{\"targetId\":\"{AgnesTargetId}\",\"type\":\"page\",\"url\":\"https://platform.agnes-ai.com/settings/apiKeys\"}}]}}}}";
                }
                else if (method == "Target.attachToTarget")
                {
                    return $"{{\"id\":{id},\"result\":{{\"sessionId\":\"session-agnes-123\"}}}}";
                }
                else if (method == "Page.bringToFront")
                {
                    return $"{{\"id\":{id},\"result\":{{}}}}";
                }
                return null;
            }, _output, "Agnes");
        }, cts.Token);

        // Connect to Agnes page first
        var agnesBrowser = await chromeSession.ConnectAnyTargetAsync(cts.Token);

        // Wait for handler to have received the WebSocket
        await agnesReady.Task.WaitAsync(cts.Token);

        // Start handler for SECOND connection (Google popup)
        var popupReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var popupResponder = Task.Run(async () =>
        {
            var popupWs = await server.WaitForNextWebSocketAsync(cts.Token);
            popupReady.TrySetResult(true);
            await HandleConnectionAsync(popupWs, cts.Token, (request, id, ct) =>
            {
                var method = request.GetProperty("method").GetString();
                if (method == "Target.getTargets")
                {
                    // Return: Agnes tab + pre-existing accounts.google.com target (pre-click) + new Google popup
                    // The pre-existing Google target should be EXCLUDED by the pre-click snapshot
                    // Only the NEW popup should be selected
                    return $"{{\"id\":{id},\"result\":{{\"targetInfos\":[" +
                           $"{{\"targetId\":\"{AgnesTargetId}\",\"type\":\"page\",\"url\":\"https://platform.agnes-ai.com/settings/apiKeys\"}}," +
                           $"{{\"targetId\":\"{PreClickTargetId}\",\"type\":\"page\",\"url\":\"https://accounts.google.com/signin/identifier\"}}," +  // PRE-EXISTING Google target
                           $"{{\"targetId\":\"{PopupTargetId}\",\"type\":\"page\",\"url\":\"https://accounts.google.com/v3/signin/challenge\"}}" +   // NEW Google popup
                           $"]}}}}";
                }
                else if (method == "Target.attachToTarget")
                {
                    attachTargetId = request.GetProperty("params").GetProperty("targetId").GetString();
                    return $"{{\"id\":{id},\"result\":{{\"sessionId\":\"session-popup-123\"}}}}";
                }
                else if (method == "Page.bringToFront")
                {
                    return $"{{\"id\":{id},\"result\":{{}}}}";
                }
                else if (method == "Runtime.evaluate")
                {
                    return $"{{\"id\":{id},\"result\":{{\"result\":{{\"value\":\"complete\"}}}}}}";
                }
                return null;
            }, _output, "Popup");
        }, cts.Token);

        // Create pre-click snapshot WITH the pre-existing Google target
        var preClickTargets = new HashSet<string> { AgnesTargetId, PreClickTargetId };

        // Connect to Google popup with pre-click snapshot
        var googleLogin = await chromeSession.ConnectGooglePopupAsync(cts.Token, preClickTargets);

        // Wait for handler to have received the WebSocket
        await popupReady.Task.WaitAsync(cts.Token);

        await googleLogin.DisposeAsync();
        await agnesBrowser.DisposeAsync();
        cts.Cancel();
        try { await Task.WhenAll(agnesResponder, popupResponder); } catch { }

        // Verify the attached target is the NEW popup (PopupTargetId), not the pre-existing one (PreClickTargetId)
        Assert.Equal(PopupTargetId, attachTargetId);
    }
}
