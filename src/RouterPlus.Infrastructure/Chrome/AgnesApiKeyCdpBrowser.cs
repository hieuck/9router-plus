using System.Text.Json;
using RouterPlus.Infrastructure.Diagnostics;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// CDP adapter for platform.agnes-ai.com keys: Google sign-in (popup),
/// and create/copy a new API key.
/// </summary>
public sealed class AgnesApiKeyCdpBrowser : IAgnesApiKeyBrowser
{
    private readonly ChromeCdpClient _client;
    private readonly string _sessionId;
    private bool _disposed;

    public ChromeCdpClient Client => _client;

    public AgnesApiKeyCdpBrowser(ChromeCdpClient client, string sessionId, string targetId)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _sessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        _ = targetId;
    }

    public async Task<AgnesApiKeyPageState> ReadStateAsync(CancellationToken cancellationToken)
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

    const url = window.location.href;
    const isOnKeysPage = (() => {
        try {
            const u = new URL(url);
            return u.hostname === 'platform.agnes-ai.com' && u.pathname === '/settings/apiKeys';
        } catch {
            return false;
        }
    })();

    const hasGoogleSignIn = Array.from(document.querySelectorAll('button, [role=""button""], a')).some(el =>
        isVisible(el) && textOf(el).includes('continue with google'));

    const existingKeys = Array.from(document.querySelectorAll('[data-testid=""api-key-item""], .api-key-row, [data-key]')).filter(isVisible).length;

    const hasNewKeyButton = Array.from(document.querySelectorAll('button, [role=""button""]')).some(el =>
        isVisible(el) && textOf(el).includes('create new secret key'));

    const hasNewKeyNameInput = !!document.querySelector('input[placeholder*=""name"" i], input[placeholder*=""key"" i], dialog input[type=""text""], [role=""dialog""] input[type=""text""]');

    const createdKeyPanel = document.querySelector('input[disabled][value], input[readonly][value], [data-testid=""api-key-value""]');
    const hasCreatedKeyPanel = !!createdKeyPanel;
    let apiKey = '';
    if (hasCreatedKeyPanel) {
        apiKey = createdKeyPanel.value || createdKeyPanel.getAttribute('value') || '';
    }

    return {
        url: url,
        isOnKeysPage: isOnKeysPage,
        hasGoogleSignIn: hasGoogleSignIn,
        existingKeyCount: existingKeys,
        hasNewKeyButton: hasNewKeyButton,
        hasNewKeyNameInput: hasNewKeyNameInput,
        hasCreatedKeyPanel: hasCreatedKeyPanel,
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
            return new AgnesApiKeyPageState(
                new Uri(urlString),
                IsOnKeysPage: v.GetProperty("isOnKeysPage").GetBoolean(),
                HasGoogleSignIn: v.GetProperty("hasGoogleSignIn").GetBoolean(),
                ExistingKeyCount: v.GetProperty("existingKeyCount").GetInt32(),
                HasNewKeyButton: v.GetProperty("hasNewKeyButton").GetBoolean(),
                HasNewKeyNameInput: v.GetProperty("hasNewKeyNameInput").GetBoolean(),
                HasCreatedKeyPanel: v.GetProperty("hasCreatedKeyPanel").GetBoolean(),
                ApiKey: v.GetProperty("apiKey").GetString() ?? string.Empty);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException("Failed to parse page state from Agnes keys page.");
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
            !text.includes('add api key') && !text.includes('new key') && !text.includes('create new secret key');
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
            new[] { "create new secret key", "add api key", "generate api key", "new key", "create api key" },
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
    const allButtons = Array.from(document.querySelectorAll('button, [role=""button""]')).filter(isVisible);
    const saveBtn = allButtons.find(b => (b.innerText || '').trim().toLowerCase() === 'save');
    const fallbackBtn = allButtons.find(b => {{
        const t = (b.innerText || '').toLowerCase();
        return t.includes('create') || t.includes('generate');
    }});
    const btn = saveBtn || fallbackBtn;
    if (!btn) return false;
    btn.click();
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