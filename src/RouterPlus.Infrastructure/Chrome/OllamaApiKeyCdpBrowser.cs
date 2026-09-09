using System.Text.Json;
using RouterPlus.Infrastructure.Diagnostics;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// CDP adapter for ollama.com keys: WorkOS Google sign-in, revoke existing keys,
/// and create/copy a new API key.
/// </summary>
public sealed class OllamaApiKeyCdpBrowser : IOllamaApiKeyBrowser
{
    private readonly ChromeCdpClient _client;
    private readonly string _sessionId;
    private bool _disposed;

    public OllamaApiKeyCdpBrowser(ChromeCdpClient client, string sessionId, string targetId)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _sessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        _ = targetId;
    }

    public async Task<OllamaApiKeyPageState> ReadStateAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        const string script = @"
(function() {
    const url = window.location.href;
    const host = window.location.host;
    const path = window.location.pathname;
    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return el.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
    };
    const textOf = el => ((el.innerText || '') + ' ' + (el.getAttribute('aria-label') || '') + ' ' + (el.getAttribute('title') || '') + ' ' + (el.getAttribute('href') || '')).toLowerCase();

    const isKeys = (host === 'ollama.com' || host.endsWith('.ollama.com')) && path.includes('/settings/keys');
    const clickables = Array.from(document.querySelectorAll('button, a, [role=""button""]')).filter(isVisible);
    const hasGoogleLoginButton = clickables.some(el => {
        const text = textOf(el);
        return text.includes('continue with google') ||
            text.includes('sign in with google') ||
            text.includes('log in with google') ||
            text.includes('tiếp tục với google') ||
            text.includes('đăng nhập bằng google');
    });

    const hasNewKeyButton = clickables.some(el => {
        const text = textOf(el);
        return text.includes('add api key') || text.includes('new key') || text.includes('create api key') || text.includes('generate api key');
    });
    const nameInput = !!document.querySelector('input[placeholder*=""name"" i], input[placeholder*=""key"" i], dialog input[type=""text""], [role=""dialog""] input[type=""text""]');
    const deleteButtons = clickables.filter(el => {
        const text = textOf(el);
        return (text.includes('delete') || text.includes('revoke') || text.includes('remove') || text.includes('xóa')) &&
            !text.includes('add api key') && !text.includes('new key');
    });

    const bodyText = document.body ? (document.body.innerText || '') : '';
    let apiKey = '';
    const keyMatch = bodyText.match(/ol_[A-Za-z0-9_-]{16,}/);
    if (keyMatch) apiKey = keyMatch[0];

    return {
        url: url,
        isOnKeysPage: isKeys,
        hasGoogleLoginButton: hasGoogleLoginButton,
        existingKeyCount: deleteButtons.length,
        hasNewKeyButton: hasNewKeyButton,
        hasNewKeyNameInput: nameInput,
        hasCreatedKeyPanel: apiKey !== '',
        apiKey: apiKey
    };
})()
";

        try
        {
            var result = await _client.CallAsync("Runtime.evaluate", new
            {
                expression = script,
                returnByValue = true,
                awaitPromise = false
            }, cancellationToken, _sessionId);

            var v = result.GetProperty("result").GetProperty("value");
            var urlString = v.GetProperty("url").GetString()!;
            return new OllamaApiKeyPageState(
                new Uri(urlString),
                IsOnKeysPage: v.GetProperty("isOnKeysPage").GetBoolean(),
                HasGoogleSignIn: v.GetProperty("hasGoogleLoginButton").GetBoolean(),
                ExistingKeyCount: v.GetProperty("existingKeyCount").GetInt32(),
                HasNewKeyButton: v.GetProperty("hasNewKeyButton").GetBoolean(),
                HasNewKeyNameInput: v.GetProperty("hasNewKeyNameInput").GetBoolean(),
                HasCreatedKeyPanel: v.GetProperty("hasCreatedKeyPanel").GetBoolean(),
                ApiKey: v.GetProperty("apiKey").GetString() ?? string.Empty);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or UriFormatException)
        {
            throw new InvalidOperationException($"Failed to read Ollama keys page state: {ex.Message}", ex);
        }
    }

    public Task<string> GetCurrentUrlAsync(CancellationToken cancellationToken) =>
        ReadStateAsync(cancellationToken).ContinueWith(
            t => t.Result.PageUri.ToString(),
            cancellationToken,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    public async Task<bool> WaitForGoogleSignInAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(30);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var state = await ReadStateAsync(cancellationToken);
            if (state.PageUri.Host == "accounts.google.com")
            {
                return true;
            }

            await Task.Delay(500, cancellationToken);
        }

        return false;
    }

    public async Task<bool> WaitForKeysPageAsync(CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(45);
        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                var state = await ReadStateAsync(cancellationToken);
                if (state.IsOnKeysPage)
                {
                    return true;
                }
            }
            catch (InvalidOperationException)
            {
                // Execution context may be recreated during OAuth redirect.
            }

            await Task.Delay(1000, cancellationToken);
        }

        return false;
    }

    public async Task<bool> TryClickSignInWithGoogleAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return await ClickByKeywordsAsync(
            new[]
            {
                "continue with google",
                "sign in with google",
                "log in with google",
                "tiếp tục với google",
                "đăng nhập bằng google"
            },
            cancellationToken);
    }

    public async Task<bool> TryDeleteOneExistingKeyAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        const string script = @"
(function() {
    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return el.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
    };
    const textOf = el => ((el.innerText || '') + ' ' + (el.getAttribute('aria-label') || '') + ' ' + (el.getAttribute('title') || '')).toLowerCase();
    const candidates = Array.from(document.querySelectorAll('button, [role=""button""], a')).filter(isVisible);
    const confirm = candidates.find(el => {
        const text = textOf(el);
        return text.includes('confirm') || text.includes('delete key') || text.includes('revoke') || text.includes('yes, delete') || text.includes('xác nhận');
    });
    if (confirm) {
        confirm.click();
        return true;
    }
    const del = candidates.find(el => {
        const text = textOf(el);
        return (text.includes('delete') || text.includes('revoke') || text.includes('remove') || text.includes('xóa')) &&
            !text.includes('add api key') && !text.includes('new key');
    });
    if (del) {
        del.click();
        return true;
    }
    return false;
})()
";
        return await EvaluateBoolAsync(script, cancellationToken);
    }

    public async Task<bool> TryClickNewKeyAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return await ClickByKeywordsAsync(
            new[] { "add api key", "generate api key", "new key", "create api key" },
            cancellationToken);
    }

    public async Task<bool> TryCreateKeyAsync(string name, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var nameJson = JsonSerializer.Serialize(name);
        var script = $@"
(function() {{
    const name = {nameJson};
    const input = document.querySelector(
        'input[placeholder*=""name"" i], input[placeholder*=""key"" i], dialog input[type=""text""], [role=""dialog""] input[type=""text""], input[type=""text""]');
    if (input) {{
        const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
        setter.call(input, name);
        input.dispatchEvent(new Event('input', {{ bubbles: true }}));
        input.dispatchEvent(new Event('change', {{ bubbles: true }}));
    }}
    const isVisible = el => {{
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return el.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
    }};
    const buttons = Array.from(document.querySelectorAll('button, [role=""button""]')).filter(b => {{
        if (!isVisible(b)) return false;
        const text = (b.innerText || '').toLowerCase();
        return text.includes('generate api key') || text.includes('create') || text.includes('generate') || text.includes('add');
    }});
    if (buttons.length === 0) return false;
    buttons[buttons.length - 1].click();
    return true;
}})()
";
        return await EvaluateBoolAsync(script, cancellationToken);
    }

    private async Task<bool> ClickByKeywordsAsync(IReadOnlyList<string> keywords, CancellationToken cancellationToken)
    {
        var keywordsJson = JsonSerializer.Serialize(keywords);
        var script = $@"
(function() {{
    const keywords = {keywordsJson};
    const isVisible = el => {{
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return el.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
    }};
    const textOf = el => ((el.innerText || '') + ' ' + (el.getAttribute('aria-label') || '') + ' ' + (el.getAttribute('href') || '')).toLowerCase();
    const matches = Array.from(document.querySelectorAll('button, a, [role=""button""]')).filter(el => {{
        if (!isVisible(el)) return false;
        const text = textOf(el);
        return keywords.some(k => text.includes(k));
    }});
    if (matches.length === 0) return false;
    matches[0].click();
    return true;
}})()
";
        return await EvaluateBoolAsync(script, cancellationToken);
    }

    private async Task<bool> EvaluateBoolAsync(string script, CancellationToken cancellationToken)
    {
        var result = await _client.CallAsync("Runtime.evaluate", new
        {
            expression = script,
            returnByValue = true,
            awaitPromise = false
        }, cancellationToken, _sessionId);

        return result.TryGetProperty("result", out var remote) && remote.TryGetProperty("value", out var value)
            && value.ValueKind == JsonValueKind.True;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await ValueTask.CompletedTask;
    }
}
