using System.Net.WebSockets;
using System.Text.Json;
using RouterPlus.Infrastructure.Chrome;
using Xunit;

namespace RouterPlus.Infrastructure.Tests.Chrome;

public sealed class AgnesApiKeyCdpBrowserTests
{
    private const string AgnesTargetId = "agnes-tab-target";
    private const string PopupTargetId = "google-popup-target";

    [Fact]
    public async Task ReadStateAsync_exact_url_match_accepts_platform_agnes_ai_com_settings_apiKeys()
    {
        await using var server = await LoopbackCdpServer.CreateAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var responder = Task.Run(async () =>
        {
            try
            {
                var webSocket = await server.WaitForNextWebSocketAsync(cts.Token);
                while (!cts.IsCancellationRequested)
                {
                    var request = await server.ReceiveJsonAsync(cts.Token);
                    var id = request.GetProperty("id").GetInt32();
                    var method = request.GetProperty("method").GetString();

                    if (method == "Runtime.evaluate")
                    {
                        // Return state with exact URL match
                        await LoopbackCdpServer.SendJsonAsync(webSocket,
                            $"{{\"id\":{id},\"result\":{{\"result\":{{\"value\":{{\"url\":\"https://platform.agnes-ai.com/settings/apiKeys\",\"isOnKeysPage\":true,\"hasGoogleSignIn\":false,\"existingKeyCount\":0,\"hasNewKeyButton\":true,\"hasNewKeyNameInput\":false,\"hasCreatedKeyPanel\":false,\"apiKey\":\"\"}}}}}}}}",
                            cts.Token);
                    }
                    else if (method == "Target.getTargets")
                    {
                        await LoopbackCdpServer.SendJsonAsync(webSocket,
                            $"{{\"id\":{id},\"result\":{{\"targetInfos\":[{{\"targetId\":\"{AgnesTargetId}\",\"type\":\"page\",\"url\":\"https://platform.agnes-ai.com/settings/apiKeys\"}}]}}}}",
                            cts.Token);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
        }, cts.Token);

        var client = new ChromeCdpClient(server.BaseUri);
        await client.ConnectAsync(cts.Token);
        var browser = new AgnesApiKeyCdpBrowser(client, "session-1", AgnesTargetId);

        var state = await browser.ReadStateAsync(cts.Token);

        await browser.DisposeAsync();
        cts.Cancel();
        try { await responder; } catch { }

        Assert.True(state.IsOnKeysPage);
        Assert.Equal("https://platform.agnes-ai.com/settings/apiKeys", state.PageUri.ToString());
    }

    [Fact]
    public async Task ReadStateAsync_exact_url_match_rejects_wrong_host()
    {
        await using var server = await LoopbackCdpServer.CreateAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var responder = Task.Run(async () =>
        {
            try
            {
                var webSocket = await server.WaitForNextWebSocketAsync(cts.Token);
                while (!cts.IsCancellationRequested)
                {
                    var request = await server.ReceiveJsonAsync(cts.Token);
                    var id = request.GetProperty("id").GetInt32();
                    var method = request.GetProperty("method").GetString();

                    if (method == "Runtime.evaluate")
                    {
                        // Return state with wrong host - should be rejected by exact match
                        await LoopbackCdpServer.SendJsonAsync(webSocket,
                            $"{{\"id\":{id},\"result\":{{\"result\":{{\"value\":{{\"url\":\"https://wrong-host.example.com/settings/apiKeys\",\"isOnKeysPage\":false,\"hasGoogleSignIn\":false,\"existingKeyCount\":0,\"hasNewKeyButton\":true,\"hasNewKeyNameInput\":false,\"hasCreatedKeyPanel\":false,\"apiKey\":\"\"}}}}}}}}",
                            cts.Token);
                    }
                    else if (method == "Target.getTargets")
                    {
                        await LoopbackCdpServer.SendJsonAsync(webSocket,
                            $"{{\"id\":{id},\"result\":{{\"targetInfos\":[{{\"targetId\":\"{AgnesTargetId}\",\"type\":\"page\",\"url\":\"https://wrong-host.example.com/settings/apiKeys\"}}]}}}}",
                            cts.Token);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
        }, cts.Token);

        var client = new ChromeCdpClient(server.BaseUri);
        await client.ConnectAsync(cts.Token);
        var browser = new AgnesApiKeyCdpBrowser(client, "session-1", AgnesTargetId);

        var state = await browser.ReadStateAsync(cts.Token);

        await browser.DisposeAsync();
        cts.Cancel();
        try { await responder; } catch { }

        Assert.False(state.IsOnKeysPage);
    }

    [Fact]
    public async Task ReadStateAsync_exact_url_match_rejects_wrong_path()
    {
        await using var server = await LoopbackCdpServer.CreateAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var responder = Task.Run(async () =>
        {
            try
            {
                var webSocket = await server.WaitForNextWebSocketAsync(cts.Token);
                while (!cts.IsCancellationRequested)
                {
                    var request = await server.ReceiveJsonAsync(cts.Token);
                    var id = request.GetProperty("id").GetInt32();
                    var method = request.GetProperty("method").GetString();

                    if (method == "Runtime.evaluate")
                    {
                        // Return state with wrong path - should be rejected by exact match
                        await LoopbackCdpServer.SendJsonAsync(webSocket,
                            $"{{\"id\":{id},\"result\":{{\"result\":{{\"value\":{{\"url\":\"https://platform.agnes-ai.com/settings/wrong-path\",\"isOnKeysPage\":false,\"hasGoogleSignIn\":false,\"existingKeyCount\":0,\"hasNewKeyButton\":true,\"hasNewKeyNameInput\":false,\"hasCreatedKeyPanel\":false,\"apiKey\":\"\"}}}}}}}}",
                            cts.Token);
                    }
                    else if (method == "Target.getTargets")
                    {
                        await LoopbackCdpServer.SendJsonAsync(webSocket,
                            $"{{\"id\":{id},\"result\":{{\"targetInfos\":[{{\"targetId\":\"{AgnesTargetId}\",\"type\":\"page\",\"url\":\"https://platform.agnes-ai.com/settings/wrong-path\"}}]}}}}",
                            cts.Token);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
        }, cts.Token);

        var client = new ChromeCdpClient(server.BaseUri);
        await client.ConnectAsync(cts.Token);
        var browser = new AgnesApiKeyCdpBrowser(client, "session-1", AgnesTargetId);

        var state = await browser.ReadStateAsync(cts.Token);

        await browser.DisposeAsync();
        cts.Cancel();
        try { await responder; } catch { }

        Assert.False(state.IsOnKeysPage);
    }

    [Fact]
    public async Task ReadStateAsync_detects_google_signin_button()
    {
        await using var server = await LoopbackCdpServer.CreateAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var responder = Task.Run(async () =>
        {
            try
            {
                var webSocket = await server.WaitForNextWebSocketAsync(cts.Token);
                while (!cts.IsCancellationRequested)
                {
                    var request = await server.ReceiveJsonAsync(cts.Token);
                    var id = request.GetProperty("id").GetInt32();
                    var method = request.GetProperty("method").GetString();

                    if (method == "Runtime.evaluate")
                    {
                        await LoopbackCdpServer.SendJsonAsync(webSocket,
                            $"{{\"id\":{id},\"result\":{{\"result\":{{\"value\":{{\"url\":\"https://platform.agnes-ai.com/settings/apiKeys\",\"isOnKeysPage\":true,\"hasGoogleSignIn\":true,\"existingKeyCount\":0,\"hasNewKeyButton\":false,\"hasNewKeyNameInput\":false,\"hasCreatedKeyPanel\":false,\"apiKey\":\"\"}}}}}}}}",
                            cts.Token);
                    }
                    else if (method == "Target.getTargets")
                    {
                        await LoopbackCdpServer.SendJsonAsync(webSocket,
                            $"{{\"id\":{id},\"result\":{{\"targetInfos\":[{{\"targetId\":\"{AgnesTargetId}\",\"type\":\"page\",\"url\":\"https://platform.agnes-ai.com/settings/apiKeys\"}}]}}}}",
                            cts.Token);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
        }, cts.Token);

        var client = new ChromeCdpClient(server.BaseUri);
        await client.ConnectAsync(cts.Token);
        var browser = new AgnesApiKeyCdpBrowser(client, "session-1", AgnesTargetId);

        var state = await browser.ReadStateAsync(cts.Token);

        await browser.DisposeAsync();
        cts.Cancel();
        try { await responder; } catch { }

        Assert.True(state.HasGoogleSignIn);
    }

    [Fact]
    public async Task ReadStateAsync_detects_new_key_button()
    {
        await using var server = await LoopbackCdpServer.CreateAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var responder = Task.Run(async () =>
        {
            try
            {
                var webSocket = await server.WaitForNextWebSocketAsync(cts.Token);
                while (!cts.IsCancellationRequested)
                {
                    var request = await server.ReceiveJsonAsync(cts.Token);
                    var id = request.GetProperty("id").GetInt32();
                    var method = request.GetProperty("method").GetString();

                    if (method == "Runtime.evaluate")
                    {
                        await LoopbackCdpServer.SendJsonAsync(webSocket,
                            $"{{\"id\":{id},\"result\":{{\"result\":{{\"value\":{{\"url\":\"https://platform.agnes-ai.com/settings/apiKeys\",\"isOnKeysPage\":true,\"hasGoogleSignIn\":false,\"existingKeyCount\":0,\"hasNewKeyButton\":true,\"hasNewKeyNameInput\":false,\"hasCreatedKeyPanel\":false,\"apiKey\":\"\"}}}}}}}}",
                            cts.Token);
                    }
                    else if (method == "Target.getTargets")
                    {
                        await LoopbackCdpServer.SendJsonAsync(webSocket,
                            $"{{\"id\":{id},\"result\":{{\"targetInfos\":[{{\"targetId\":\"{AgnesTargetId}\",\"type\":\"page\",\"url\":\"https://platform.agnes-ai.com/settings/apiKeys\"}}]}}}}",
                            cts.Token);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
        }, cts.Token);

        var client = new ChromeCdpClient(server.BaseUri);
        await client.ConnectAsync(cts.Token);
        var browser = new AgnesApiKeyCdpBrowser(client, "session-1", AgnesTargetId);

        var state = await browser.ReadStateAsync(cts.Token);

        await browser.DisposeAsync();
        cts.Cancel();
        try { await responder; } catch { }

        Assert.True(state.HasNewKeyButton);
    }

    [Fact]
    public async Task ReadStateAsync_detects_new_key_name_input()
    {
        await using var server = await LoopbackCdpServer.CreateAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var responder = Task.Run(async () =>
        {
            try
            {
                var webSocket = await server.WaitForNextWebSocketAsync(cts.Token);
                while (!cts.IsCancellationRequested)
                {
                    var request = await server.ReceiveJsonAsync(cts.Token);
                    var id = request.GetProperty("id").GetInt32();
                    var method = request.GetProperty("method").GetString();

                    if (method == "Runtime.evaluate")
                    {
                        await LoopbackCdpServer.SendJsonAsync(webSocket,
                            $"{{\"id\":{id},\"result\":{{\"result\":{{\"value\":{{\"url\":\"https://platform.agnes-ai.com/settings/apiKeys\",\"isOnKeysPage\":true,\"hasGoogleSignIn\":false,\"existingKeyCount\":0,\"hasNewKeyButton\":false,\"hasNewKeyNameInput\":true,\"hasCreatedKeyPanel\":false,\"apiKey\":\"\"}}}}}}}}",
                            cts.Token);
                    }
                    else if (method == "Target.getTargets")
                    {
                        await LoopbackCdpServer.SendJsonAsync(webSocket,
                            $"{{\"id\":{id},\"result\":{{\"targetInfos\":[{{\"targetId\":\"{AgnesTargetId}\",\"type\":\"page\",\"url\":\"https://platform.agnes-ai.com/settings/apiKeys\"}}]}}}}",
                            cts.Token);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
        }, cts.Token);

        var client = new ChromeCdpClient(server.BaseUri);
        await client.ConnectAsync(cts.Token);
        var browser = new AgnesApiKeyCdpBrowser(client, "session-1", AgnesTargetId);

        var state = await browser.ReadStateAsync(cts.Token);

        await browser.DisposeAsync();
        cts.Cancel();
        try { await responder; } catch { }

        Assert.True(state.HasNewKeyNameInput);
    }

    [Fact]
    public async Task ReadStateAsync_reads_full_key_from_disabled_readonly_modal_input()
    {
        await using var server = await LoopbackCdpServer.CreateAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        const string expectedKey = "agnes_test_key_123456789";

        var responder = Task.Run(async () =>
        {
            try
            {
                var webSocket = await server.WaitForNextWebSocketAsync(cts.Token);
                while (!cts.IsCancellationRequested)
                {
                    var request = await server.ReceiveJsonAsync(cts.Token);
                    var id = request.GetProperty("id").GetInt32();
                    var method = request.GetProperty("method").GetString();

                    if (method == "Runtime.evaluate")
                    {
                        // Return state with key in disabled/readonly input (modal)
                        await LoopbackCdpServer.SendJsonAsync(webSocket,
                            $"{{\"id\":{id},\"result\":{{\"result\":{{\"value\":{{\"url\":\"https://platform.agnes-ai.com/settings/apiKeys\",\"isOnKeysPage\":true,\"hasGoogleSignIn\":false,\"existingKeyCount\":0,\"hasNewKeyButton\":false,\"hasNewKeyNameInput\":false,\"hasCreatedKeyPanel\":true,\"apiKey\":\"{expectedKey}\"}}}}}}}}",
                            cts.Token);
                    }
                    else if (method == "Target.getTargets")
                    {
                        await LoopbackCdpServer.SendJsonAsync(webSocket,
                            $"{{\"id\":{id},\"result\":{{\"targetInfos\":[{{\"targetId\":\"{AgnesTargetId}\",\"type\":\"page\",\"url\":\"https://platform.agnes-ai.com/settings/apiKeys\"}}]}}}}",
                            cts.Token);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (WebSocketException) { }
        }, cts.Token);

        var client = new ChromeCdpClient(server.BaseUri);
        await client.ConnectAsync(cts.Token);
        var browser = new AgnesApiKeyCdpBrowser(client, "session-1", AgnesTargetId);

        var state = await browser.ReadStateAsync(cts.Token);

        await browser.DisposeAsync();
        cts.Cancel();
        try { await responder; } catch { }

        Assert.True(state.HasCreatedKeyPanel);
        Assert.Equal(expectedKey, state.ApiKey);
    }
}
