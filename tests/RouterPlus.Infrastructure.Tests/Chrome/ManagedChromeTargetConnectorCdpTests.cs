using System.Net.WebSockets;
using RouterPlus.Infrastructure.Chrome;
using Xunit;

namespace RouterPlus.Infrastructure.Tests.Chrome;

public sealed class ManagedChromeTargetConnectorCdpTests
{
    [Fact]
    public async Task ConnectAsync_attaches_to_marked_google_page_over_real_cdp()
    {
        const string sessionMarker = "__9rp_session_test123";
        const string markedTargetId = "marked-google";
        var attachRequested = false;
        var bringToFrontRequested = false;

        await using var server = await LoopbackCdpServer.CreateAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var responder = Task.Run(async () =>
        {
            try
            {
                var webSocket = await server.WebSocketTask.WaitAsync(cts.Token);
                while (!cts.IsCancellationRequested)
                {
                    var request = await server.ReceiveJsonAsync(cts.Token);
                    var id = request.GetProperty("id").GetInt32();
                    var method = request.GetProperty("method").GetString();

                    if (method == "Target.getTargets")
                    {
                        await LoopbackCdpServer.SendJsonAsync(webSocket,
                            $"{{\"id\":{id},\"result\":{{\"targetInfos\":[{{\"targetId\":\"stale-1\",\"type\":\"page\",\"url\":\"https://example.com\"}},{{\"targetId\":\"{markedTargetId}\",\"type\":\"page\",\"url\":\"https://accounts.google.com/signin?x={sessionMarker}\"}}]}}}}",
                            cts.Token);
                    }
                    else if (method == "Target.attachToTarget")
                    {
                        attachRequested = true;
                        await LoopbackCdpServer.SendJsonAsync(webSocket,
                            $"{{\"id\":{id},\"result\":{{\"sessionId\":\"session-abc\"}}}}",
                            cts.Token);
                    }
                    else if (method == "Page.bringToFront")
                    {
                        bringToFrontRequested = true;
                        await LoopbackCdpServer.SendJsonAsync(webSocket,
                            $"{{\"id\":{id},\"result\":{{}}}}",
                            cts.Token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when the test completes.
            }
            catch (WebSocketException)
            {
                // Expected during teardown.
            }
        }, cts.Token);

        var connector = new ManagedChromeTargetConnector(server.BaseUri);
        var session = await connector.ConnectAsync(
            targets => ManagedChromeTargetConnector.SelectMarkedGooglePage(targets, sessionMarker),
            "No accounts.google.com target with session marker found in the managed session.",
            cts.Token);

        await session.DisposeAsync();
        cts.Cancel();
        try { await responder; } catch { /* ignore */ }

        Assert.Equal(markedTargetId, session.TargetId);
        Assert.Equal("session-abc", session.SessionId);
        Assert.True(attachRequested);
        Assert.True(bringToFrontRequested);
    }

    [Fact]
    public async Task ConnectAsync_throws_when_no_allowed_google_target()
    {
        await using var server = await LoopbackCdpServer.CreateAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));

        var responder = Task.Run(async () =>
        {
            try
            {
                var webSocket = await server.WebSocketTask.WaitAsync(cts.Token);
                while (!cts.IsCancellationRequested)
                {
                    var request = await server.ReceiveJsonAsync(cts.Token);
                    var id = request.GetProperty("id").GetInt32();
                    var method = request.GetProperty("method").GetString();

                    if (method == "Target.getTargets")
                    {
                        await LoopbackCdpServer.SendJsonAsync(webSocket,
                            $"{{\"id\":{id},\"result\":{{\"targetInfos\":[{{\"targetId\":\"stale-1\",\"type\":\"page\",\"url\":\"https://example.com\"}}]}}}}",
                            cts.Token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when the test completes.
            }
            catch (WebSocketException)
            {
                // Expected during teardown.
            }
        }, cts.Token);

        var connector = new ManagedChromeTargetConnector(server.BaseUri);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            connector.ConnectAsync(
                targets => ManagedChromeTargetConnector.SelectMarkedGooglePage(targets, "__9rp_session_test123"),
                "No accounts.google.com target with session marker found in the managed session.",
                cts.Token));

        cts.Cancel();
        try { await responder; } catch { /* ignore */ }

        Assert.Contains("No accounts.google.com target with session marker found", exception.Message);
    }
}