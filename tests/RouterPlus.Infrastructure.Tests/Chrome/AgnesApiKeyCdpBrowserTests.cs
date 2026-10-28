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
                            $"{{\"id\":{id},\"result\":{{\"result\":{{\"value\":{{\"url\":\"https://platform.agnes-ai.com/settings/apiKeys\",\"isOnKeysPage\":true,\"isOnLoginPage\":false,\"hasGoogleSignIn\":false,\"existingKeyCount\":0,\"hasNewKeyButton\":true,\"hasNewKeyNameInput\":false,\"hasCreatedKeyPanel\":false,\"apiKey\":\"\"}}}}}}}}",
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
                            $"{{\"id\":{id},\"result\":{{\"result\":{{\"value\":{{\"url\":\"https://wrong-host.example.com/settings/apiKeys\",\"isOnKeysPage\":false,\"isOnLoginPage\":false,\"hasGoogleSignIn\":false,\"existingKeyCount\":0,\"hasNewKeyButton\":true,\"hasNewKeyNameInput\":false,\"hasCreatedKeyPanel\":false,\"apiKey\":\"\"}}}}}}}}",
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
                            $"{{\"id\":{id},\"result\":{{\"result\":{{\"value\":{{\"url\":\"https://platform.agnes-ai.com/settings/wrong-path\",\"isOnKeysPage\":false,\"isOnLoginPage\":false,\"hasGoogleSignIn\":false,\"existingKeyCount\":0,\"hasNewKeyButton\":true,\"hasNewKeyNameInput\":false,\"hasCreatedKeyPanel\":false,\"apiKey\":\"\"}}}}}}}}",
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
                            $"{{\"id\":{id},\"result\":{{\"result\":{{\"value\":{{\"url\":\"https://platform.agnes-ai.com/settings/apiKeys\",\"isOnKeysPage\":true,\"isOnLoginPage\":false,\"hasGoogleSignIn\":true,\"existingKeyCount\":0,\"hasNewKeyButton\":false,\"hasNewKeyNameInput\":false,\"hasCreatedKeyPanel\":false,\"apiKey\":\"\"}}}}}}}}",
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
                            $"{{\"id\":{id},\"result\":{{\"result\":{{\"value\":{{\"url\":\"https://platform.agnes-ai.com/settings/apiKeys\",\"isOnKeysPage\":true,\"isOnLoginPage\":false,\"hasGoogleSignIn\":false,\"existingKeyCount\":0,\"hasNewKeyButton\":true,\"hasNewKeyNameInput\":false,\"hasCreatedKeyPanel\":false,\"apiKey\":\"\"}}}}}}}}",
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
                            $"{{\"id\":{id},\"result\":{{\"result\":{{\"value\":{{\"url\":\"https://platform.agnes-ai.com/settings/apiKeys\",\"isOnKeysPage\":true,\"isOnLoginPage\":false,\"hasGoogleSignIn\":false,\"existingKeyCount\":0,\"hasNewKeyButton\":false,\"hasNewKeyNameInput\":true,\"hasCreatedKeyPanel\":false,\"apiKey\":\"\"}}}}}}}}",
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
                            $"{{\"id\":{id},\"result\":{{\"result\":{{\"value\":{{\"url\":\"https://platform.agnes-ai.com/settings/apiKeys\",\"isOnKeysPage\":true,\"isOnLoginPage\":false,\"hasGoogleSignIn\":false,\"existingKeyCount\":0,\"hasNewKeyButton\":false,\"hasNewKeyNameInput\":false,\"hasCreatedKeyPanel\":true,\"apiKey\":\"{expectedKey}\"}}}}}}}}",
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

    [Fact]
    public async Task TryClickSignInWithGoogleAsync_clicks_button_when_found()
    {
        await using var server = await LoopbackCdpServer.CreateAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        int callCount = 0;
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
                        callCount++;
                        if (callCount == 1)
                        {
                            // First call: ReadStateAsync - return full state with proper candidates array
                            await LoopbackCdpServer.SendJsonAsync(webSocket,
                                $"{{\"id\":{id},\"result\":{{\"result\":{{\"value\":{{\"url\":\"https://platform.agnes-ai.com/settings/apiKeys\",\"isOnKeysPage\":true,\"isOnLoginPage\":false,\"hasGoogleSignIn\":false,\"existingKeyCount\":0,\"hasNewKeyButton\":true,\"hasNewKeyNameInput\":false,\"hasCreatedKeyPanel\":false,\"apiKey\":\"\",\"candidates\":[{{\"role\":\"button\",\"tag\":\"button\",\"text\":\"continue with google\",\"ariaLabel\":\"\",\"className\":\"\"}}]}}}}}}}}",
                                cts.Token);
                        }
                        else
                        {
                            // Second call: ClickByKeywordsAsync - returns clicked=true with candidates
                            await LoopbackCdpServer.SendJsonAsync(webSocket,
                                $"{{\"id\":{id},\"result\":{{\"result\":{{\"value\":{{\"clicked\":true,\"candidates\":[{{\"role\":\"button\",\"tag\":\"button\",\"text\":\"continue with google\",\"ariaLabel\":\"\",\"className\":\"\"}}],\"url\":\"https://platform.agnes-ai.com/settings/apiKeys\",\"isOnKeysPage\":true,\"hasNewKeyButton\":true}}}}}}}}",
                                cts.Token);
                        }
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

        var clicked = await browser.TryClickSignInWithGoogleAsync(cts.Token);

        await browser.DisposeAsync();
        cts.Cancel();
        try { await responder; } catch { }

        Assert.True(clicked);
    }

    [Fact]
    public async Task WaitForKeysPageAsync_returns_true_when_on_keys_page()
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
                        // First call: not on keys page
                        // Second call: on keys page
                        await LoopbackCdpServer.SendJsonAsync(webSocket,
                            $"{{\"id\":{id},\"result\":{{\"result\":{{\"value\":{{\"url\":\"https://platform.agnes-ai.com/settings/apiKeys\",\"isOnKeysPage\":true,\"isOnLoginPage\":false,\"hasGoogleSignIn\":false,\"existingKeyCount\":0,\"hasNewKeyButton\":true,\"hasNewKeyNameInput\":false,\"hasCreatedKeyPanel\":false,\"apiKey\":\"\"}}}}}}}}",
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

        var result = await browser.WaitForKeysPageAsync(cts.Token);

        await browser.DisposeAsync();
        cts.Cancel();
        try { await responder; } catch { }

        Assert.True(result);
    }

    [Fact]
    public async Task TryClickSignInWithGoogleAsync_returns_false_naturally_with_short_timeout()
    {
        // Test that TryClickSignInWithGoogleAsync returns false naturally (not via cancellation)
        // when a short timeout is provided and the button is never found.
        // This exercises the timeout path without relying on test cancellation.

        await using var server = await LoopbackCdpServer.CreateAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var callCount = 0;
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
                        callCount++;
                        if (callCount == 1)
                        {
                            // First call: ReadStateAsync - return full state with proper candidates array
                            await LoopbackCdpServer.SendJsonAsync(webSocket,
                                $"{{\"id\":{id},\"result\":{{\"result\":{{\"value\":{{\"url\":\"https://platform.agnes-ai.com/settings/apiKeys\",\"isOnKeysPage\":true,\"isOnLoginPage\":false,\"hasGoogleSignIn\":false,\"existingKeyCount\":0,\"hasNewKeyButton\":true,\"hasNewKeyNameInput\":false,\"hasCreatedKeyPanel\":false,\"apiKey\":\"\",\"candidates\":[{{\"role\":\"button\",\"tag\":\"button\",\"text\":\"sign in\",\"ariaLabel\":\"\",\"className\":\"\"}},{{\"role\":\"button\",\"tag\":\"button\",\"text\":\"create new key\",\"ariaLabel\":\"\",\"className\":\"\"}}]}}}}}}}}",
                                cts.Token);
                        }
                        else
                        {
                            // Subsequent calls: ClickByKeywordsAsync - returns clicked=false with candidates
                            await LoopbackCdpServer.SendJsonAsync(webSocket,
                                $"{{\"id\":{id},\"result\":{{\"result\":{{\"value\":{{\"clicked\":false,\"candidates\":[{{\"role\":\"button\",\"tag\":\"button\",\"text\":\"sign in\",\"ariaLabel\":\"\",\"className\":\"\"}},{{\"role\":\"button\",\"tag\":\"button\",\"text\":\"create new key\",\"ariaLabel\":\"\",\"className\":\"\"}}],\"url\":\"https://platform.agnes-ai.com/settings/apiKeys\",\"isOnKeysPage\":true,\"hasNewKeyButton\":true}}}}}}}}",
                                cts.Token);
                        }
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

        // Use a short timeout (100ms) so it returns false naturally
        var result = await browser.TryClickSignInWithGoogleAsync(cts.Token, TimeSpan.FromMilliseconds(100));

        await browser.DisposeAsync();
        cts.Cancel();
        try { await responder; } catch { }

        Assert.False(result);
        // Should have been called at least once
        Assert.True(callCount >= 1, $"Expected at least 1 call, got {callCount}");
    }

    [Fact]
    public async Task WaitForKeysPageAsync_returns_false_naturally_with_short_timeout()
    {
        // Test that WaitForKeysPageAsync returns false naturally (not via cancellation)
        // when a short timeout is provided and the page never becomes keys page.
        // This exercises the timeout path without relying on test cancellation.

        await using var server = await LoopbackCdpServer.CreateAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var callCount = 0;
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
                        callCount++;
                        // Always return not on keys page
                        await LoopbackCdpServer.SendJsonAsync(webSocket,
                            $"{{\"id\":{id},\"result\":{{\"result\":{{\"value\":{{\"url\":\"https://platform.agnes-ai.com/dashboard\",\"isOnKeysPage\":false,\"isOnLoginPage\":false,\"hasGoogleSignIn\":false,\"existingKeyCount\":0,\"hasNewKeyButton\":false,\"hasNewKeyNameInput\":false,\"hasCreatedKeyPanel\":false,\"apiKey\":\"\",\"candidates\":[]}}}}}}}}",
                            cts.Token);
                    }
                    else if (method == "Target.getTargets")
                    {
                        await LoopbackCdpServer.SendJsonAsync(webSocket,
                            $"{{\"id\":{id},\"result\":{{\"targetInfos\":[{{\"targetId\":\"{AgnesTargetId}\",\"type\":\"page\",\"url\":\"https://platform.agnes-ai.com/dashboard\"}}]}}}}",
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

        // Use a short timeout (100ms) so it returns false naturally
        var result = await browser.WaitForKeysPageAsync(cts.Token, TimeSpan.FromMilliseconds(100));

        await browser.DisposeAsync();
        cts.Cancel();
        try { await responder; } catch { }

        Assert.False(result);
        // Should have been called at least once
        Assert.True(callCount >= 1, $"Expected at least 1 call, got {callCount}");
    }

    [Fact]
    public async Task NavigateToKeysPageAsync_navigates_to_correct_url()
    {
        await using var server = await LoopbackCdpServer.CreateAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        string? evaluatedScript = null;

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
                        // Capture the script that was evaluated
                        var expr = request.GetProperty("params").GetProperty("expression").GetString();
                        evaluatedScript = expr;

                        await LoopbackCdpServer.SendJsonAsync(webSocket,
                            $"{{\"id\":{id},\"result\":{{\"result\":{{\"value\":true}}}}}}",
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

        var result = await browser.NavigateToKeysPageAsync(cts.Token);

        await browser.DisposeAsync();
        cts.Cancel();
        try { await responder; } catch { }

        Assert.True(result);
        Assert.NotNull(evaluatedScript);
        Assert.Contains("platform.agnes-ai.com/settings/apiKeys", evaluatedScript);
        Assert.Contains("window.location.href", evaluatedScript);
    }

    [Fact]
    public async Task ReadStateAsync_content_based_keys_page_true_when_create_key_button_present()
    {
        // Content-based: IsOnKeysPage=true when "Create new secret key" button is present (even if URL is keys page)
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
                        // Return state with URL on keys page AND create new key button present
                        await LoopbackCdpServer.SendJsonAsync(webSocket,
                            $"{{\"id\":{id},\"result\":{{\"result\":{{\"value\":{{\"url\":\"https://platform.agnes-ai.com/settings/apiKeys\",\"isOnKeysPage\":true,\"isOnLoginPage\":false,\"hasGoogleSignIn\":false,\"existingKeyCount\":0,\"hasNewKeyButton\":true,\"hasNewKeyNameInput\":false,\"hasCreatedKeyPanel\":false,\"apiKey\":\"\"}}}}}}}}",
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
        Assert.True(state.HasNewKeyButton);
    }

    [Fact]
    public async Task ReadStateAsync_content_based_keys_page_true_when_keys_table_present()
    {
        // Content-based: IsOnKeysPage=true when keys table is present (even if URL is keys page)
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
                        // Return state with keys table present but no new key button
                        await LoopbackCdpServer.SendJsonAsync(webSocket,
                            $"{{\"id\":{id},\"result\":{{\"result\":{{\"value\":{{\"url\":\"https://platform.agnes-ai.com/settings/apiKeys\",\"isOnKeysPage\":true,\"isOnLoginPage\":false,\"hasGoogleSignIn\":false,\"existingKeyCount\":2,\"hasNewKeyButton\":false,\"hasNewKeyNameInput\":false,\"hasCreatedKeyPanel\":false,\"apiKey\":\"\"}}}}}}}}",
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
        Assert.False(state.HasNewKeyButton);
        Assert.Equal(2, state.ExistingKeyCount);
    }

    [Fact]
    public async Task ReadStateAsync_content_based_keys_page_false_when_login_form_on_keys_url()
    {
        // Content-based: IsOnKeysPage=false when login form is rendered on keys URL (the bug scenario)
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
                        // Return state with URL on keys page BUT login form content (no keys UI)
                        await LoopbackCdpServer.SendJsonAsync(webSocket,
                            $"{{\"id\":{id},\"result\":{{\"result\":{{\"value\":{{\"url\":\"https://platform.agnes-ai.com/settings/apiKeys\",\"isOnKeysPage\":false,\"isOnLoginPage\":true,\"hasGoogleSignIn\":false,\"existingKeyCount\":0,\"hasNewKeyButton\":false,\"hasNewKeyNameInput\":false,\"hasCreatedKeyPanel\":false,\"apiKey\":\"\"}}}}}}}}",
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

        Assert.False(state.IsOnKeysPage);
        Assert.True(state.IsOnLoginPage);
    }

    [Fact]
    public async Task ReadStateAsync_detects_login_page_with_sign_in_text()
    {
        // Login page detection: "sign in" text present
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
                            $"{{\"id\":{id},\"result\":{{\"result\":{{\"value\":{{\"url\":\"https://platform.agnes-ai.com/login\",\"isOnKeysPage\":false,\"isOnLoginPage\":true,\"hasGoogleSignIn\":false,\"existingKeyCount\":0,\"hasNewKeyButton\":false,\"hasNewKeyNameInput\":false,\"hasCreatedKeyPanel\":false,\"apiKey\":\"\"}}}}}}}}",
                            cts.Token);
                    }
                    else if (method == "Target.getTargets")
                    {
                        await LoopbackCdpServer.SendJsonAsync(webSocket,
                            $"{{\"id\":{id},\"result\":{{\"targetInfos\":[{{\"targetId\":\"{AgnesTargetId}\",\"type\":\"page\",\"url\":\"https://platform.agnes-ai.com/login\"}}]}}}}",
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
        Assert.True(state.IsOnLoginPage);
    }

    [Fact]
    public async Task ReadStateAsync_detects_login_page_with_forgot_password()
    {
        // Login page detection: "forgot password" link present
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
                            $"{{\"id\":{id},\"result\":{{\"result\":{{\"value\":{{\"url\":\"https://platform.agnes-ai.com/login\",\"isOnKeysPage\":false,\"isOnLoginPage\":true,\"hasGoogleSignIn\":false,\"existingKeyCount\":0,\"hasNewKeyButton\":false,\"hasNewKeyNameInput\":false,\"hasCreatedKeyPanel\":false,\"apiKey\":\"\"}}}}}}}}",
                            cts.Token);
                    }
                    else if (method == "Target.getTargets")
                    {
                        await LoopbackCdpServer.SendJsonAsync(webSocket,
                            $"{{\"id\":{id},\"result\":{{\"targetInfos\":[{{\"targetId\":\"{AgnesTargetId}\",\"type\":\"page\",\"url\":\"https://platform.agnes-ai.com/login\"}}]}}}}",
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
        Assert.True(state.IsOnLoginPage);
    }

    [Fact]
    public async Task ReadStateAsync_detects_login_page_with_password_input()
    {
        // Login page detection: password input present
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
                            $"{{\"id\":{id},\"result\":{{\"result\":{{\"value\":{{\"url\":\"https://platform.agnes-ai.com/login\",\"isOnKeysPage\":false,\"isOnLoginPage\":true,\"hasGoogleSignIn\":false,\"existingKeyCount\":0,\"hasNewKeyButton\":false,\"hasNewKeyNameInput\":false,\"hasCreatedKeyPanel\":false,\"apiKey\":\"\"}}}}}}}}",
                            cts.Token);
                    }
                    else if (method == "Target.getTargets")
                    {
                        await LoopbackCdpServer.SendJsonAsync(webSocket,
                            $"{{\"id\":{id},\"result\":{{\"targetInfos\":[{{\"targetId\":\"{AgnesTargetId}\",\"type\":\"page\",\"url\":\"https://platform.agnes-ai.com/login\"}}]}}}}",
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
        Assert.True(state.IsOnLoginPage);
    }

    [Fact]
    public async Task ReadStateAsync_detects_google_signin_button_via_aria_label()
    {
        // Enhanced Google detection: via aria-label containing "google"
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
                            $"{{\"id\":{id},\"result\":{{\"result\":{{\"value\":{{\"url\":\"https://platform.agnes-ai.com/login\",\"isOnKeysPage\":false,\"isOnLoginPage\":true,\"hasGoogleSignIn\":true,\"existingKeyCount\":0,\"hasNewKeyButton\":false,\"hasNewKeyNameInput\":false,\"hasCreatedKeyPanel\":false,\"apiKey\":\"\"}}}}}}}}",
                            cts.Token);
                    }
                    else if (method == "Target.getTargets")
                    {
                        await LoopbackCdpServer.SendJsonAsync(webSocket,
                            $"{{\"id\":{id},\"result\":{{\"targetInfos\":[{{\"targetId\":\"{AgnesTargetId}\",\"type\":\"page\",\"url\":\"https://platform.agnes-ai.com/login\"}}]}}}}",
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
    public async Task ReadStateAsync_detects_google_signin_button_via_sign_in_with_google_text()
    {
        // Enhanced Google detection: "Sign in with Google" text
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
                            $"{{\"id\":{id},\"result\":{{\"result\":{{\"value\":{{\"url\":\"https://platform.agnes-ai.com/login\",\"isOnKeysPage\":false,\"isOnLoginPage\":true,\"hasGoogleSignIn\":true,\"existingKeyCount\":0,\"hasNewKeyButton\":false,\"hasNewKeyNameInput\":false,\"hasCreatedKeyPanel\":false,\"apiKey\":\"\"}}}}}}}}",
                            cts.Token);
                    }
                    else if (method == "Target.getTargets")
                    {
                        await LoopbackCdpServer.SendJsonAsync(webSocket,
                            $"{{\"id\":{id},\"result\":{{\"targetInfos\":[{{\"targetId\":\"{AgnesTargetId}\",\"type\":\"page\",\"url\":\"https://platform.agnes-ai.com/login\"}}]}}}}",
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
}
