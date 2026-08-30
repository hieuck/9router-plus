using System.Text.Json;
using RouterPlus.Infrastructure.Diagnostics;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// CDP adapter that implements <see cref="IOpenRouterOnboardingBrowser"/> for the
/// OpenRouter keys/onboarding page. Detects the Clerk "Sign in with Google" icon-only
/// button, the welcome wizard, and the New Key popup, all via Runtime.evaluate.
/// </summary>
public sealed class OpenRouterOnboardingCdpBrowser : IOpenRouterOnboardingBrowser
{
    private readonly ChromeCdpClient _client;
    private readonly string _sessionId;
    private readonly string _targetId;
    private bool _disposed;

    private static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

    public OpenRouterOnboardingCdpBrowser(ChromeCdpClient client, string sessionId, string targetId)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _sessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        _targetId = targetId ?? throw new ArgumentNullException(nameof(targetId));
    }

    public async Task<OpenRouterOnboardingPageState> ReadStateAsync(CancellationToken cancellationToken)
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

    // Keys page = /settings/keys, /workspaces/*/keys, or /keys. Logged-in
    // landing after OAuth; /settings/keys is what users navigate to directly.
    const isKeys = host === 'openrouter.ai' &&
        (path.startsWith('/settings/keys')
            || path.includes('/workspaces/default/keys')
            || path.startsWith('/keys'));

    // Clerk sign-in page = /sign-in (also /sign-up). Keys page redirects here when logged out.
    const isSignIn = host === 'openrouter.ai' && (path.includes('/sign-in') || path.includes('/login'));

    // Clerk renders Google as icon-only button whose span has the aria label.
    const googleSpan = document.querySelector('[aria-label=""Sign in with Google""]');
    const hasGoogleLoginButton = (!!googleSpan && isVisible(googleSpan)) || isSignIn;

    // Welcome wizard: a centered overlay dialog. Detect by copy-panel / continue buttons
    // rather than URL, since the wizard renders over the keys page.
    const hasCopyPanel = Array.from(document.querySelectorAll('button')).some(b =>
        isVisible(b) && (b.innerText || '').toLowerCase().includes('copy your api key'));
    const hasWizard = !!document.querySelector('[role=""dialog""], [data-testid=""welcome""], [aria-modal=""true""]');
    const hasNext = Array.from(document.querySelectorAll('button')).some(b =>
        isVisible(b) && ['next', 'tiếp', 'continue'].some(k => (b.innerText || '').toLowerCase().includes(k)));
    const hasContinue = Array.from(document.querySelectorAll('button')).some(b =>
        isVisible(b) && (b.innerText || '').toLowerCase().includes('continue'));
    const hasWizardContinue = hasContinue;
    const hasDoLater = Array.from(document.querySelectorAll('button')).some(b =>
        isVisible(b) && ['do this later', 'i will do this later', 'i\'ll do'].some(k => (b.innerText || '').toLowerCase().includes(k)));
    const hasNotSure = Array.from(document.querySelectorAll('button')).some(b =>
        isVisible(b) && ['not sure', 'other', 'i don\'t know'].some(k => (b.innerText || '').toLowerCase().includes(k)));

    // New Key popup: a modal with a name input + Create button.
    const newKeyButton = Array.from(document.querySelectorAll('button, a[href], [role=""button""]')).some(b =>
        isVisible(b) && (b.innerText || '').toLowerCase().includes('new key'));
    const nameInput = !!document.querySelector('input[placeholder*=""key name"" i], input[placeholder*=""tên key"" i]');
    const hasNewKeyNameInput = nameInput;

    // Read a visible API key from the page if one is shown (sk-or-v1-...).
    const bodyText = document.body ? (document.body.innerText || '') : '';
    let apiKey = '';
    const keyMatch = bodyText.match(/sk-or-v1-[A-Za-z0-9]{8,}/);
    if (keyMatch) apiKey = keyMatch[0];

    return {
        url: url,
        host: host,
        path: path,
        isOnKeysPage: isKeys,
        hasWelcomeWizard: hasWizard,
        hasWelcomeNext: hasNext,
        hasKeyCopyPanel: hasCopyPanel,
        hasWelcomeContinue: hasWizardContinue,
        hasDoLaterOption: hasDoLater,
        hasNotSureOption: hasNotSure,
        hasNewKeyButton: newKeyButton,
        hasNewKeyNameInput: hasNewKeyNameInput,
        hasCreatedKeyPanel: apiKey !== '' && !hasCopyPanel,
        apiKey: apiKey,
        hasGoogleLoginButton: hasGoogleLoginButton
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
            return new OpenRouterOnboardingPageState(
                new Uri(urlString),
                IsOnKeysPage: v.GetProperty("isOnKeysPage").GetBoolean(),
                HasWelcomeWizard: v.GetProperty("hasWelcomeWizard").GetBoolean(),
                HasWelcomeNext: v.GetProperty("hasWelcomeNext").GetBoolean(),
                HasKeyCopyPanel: v.GetProperty("hasKeyCopyPanel").GetBoolean(),
                HasWelcomeContinue: v.GetProperty("hasWelcomeContinue").GetBoolean(),
                HasDoLaterOption: v.GetProperty("hasDoLaterOption").GetBoolean(),
                HasNotSureOption: v.GetProperty("hasNotSureOption").GetBoolean(),
                HasNewKeyButton: v.GetProperty("hasNewKeyButton").GetBoolean(),
                HasNewKeyNameInput: v.GetProperty("hasNewKeyNameInput").GetBoolean(),
                HasCreatedKeyPanel: v.GetProperty("hasCreatedKeyPanel").GetBoolean(),
                ApiKey: v.GetProperty("apiKey").GetString() ?? string.Empty);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or UriFormatException)
        {
            throw new InvalidOperationException($"Failed to read OpenRouter onboarding state: {ex.Message}", ex);
        }
    }

    public async Task ReloadAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _client.CallAsync("Runtime.evaluate", new
        {
            expression = "window.location.reload(true)",
            awaitPromise = false
        }, cancellationToken, _sessionId);
    }

    public async Task<bool> NavigateToOpenRouterSignInAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        try
        {
            // Page.navigate is safe even when the current execution context is gone
            // (e.g. the target is mid-navigation), unlike a Runtime.evaluate location change.
            await _client.CallAsync("Page.navigate", new
            {
                url = "https://openrouter.ai/auth/sign-in"
            }, cancellationToken, _sessionId);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or TimeoutException or System.Net.Http.HttpRequestException)
        {
            return false;
        }
    }

    public Task<string> GetCurrentUrlAsync(CancellationToken ct)
        => ReadStateAsync(ct).ContinueWith(
            t => t.Result.PageUri.ToString(), ct, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

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

    public async Task<bool> WaitForOpenRouterKeysAsync(CancellationToken cancellationToken)
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

        const string script = @"
(function() {
    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return el.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
    };
    const span = document.querySelector('[aria-label=""Sign in with Google""]');
    if (span) {
        const btn = span.closest('button') || span;
        if (isVisible(btn)) { btn.click(); return true; }
    }
    return false;
})()
";

        var result = await _client.CallAsync("Runtime.evaluate", new
        {
            expression = script,
            returnByValue = true,
            awaitPromise = false
        }, cancellationToken, _sessionId);

        return result.TryGetProperty("result", out var r) && r.TryGetProperty("value", out var val)
            && val.ValueKind == JsonValueKind.True;
    }

    public async Task<bool> TryClickNextAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return await ClickButtonByKeywordAsync(new[] { "next", "tiếp" }, ct);
    }

    public async Task<bool> TryClickContinueAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return await ClickButtonByKeywordAsync(new[] { "continue", "tiếp tục" }, ct);
    }

    public async Task<bool> TryClickDoLaterAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return await ClickButtonByKeywordAsync(new[] { "do this later", "i will do this later", "i'll do" }, ct);
    }

    public async Task<bool> TryClickNotSureAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return await ClickButtonByKeywordAsync(new[] { "not sure", "other", "i don't know" }, ct);
    }

    public async Task<bool> TryClickNewKeyAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return await ClickButtonByKeywordAsync(new[] { "new key" }, ct);
    }

    public async Task<bool> TryCreateKeyAsync(string name, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var nameJson = JsonSerializer.Serialize(name);
        var script = $@"
(function() {{
    const name = {nameJson};
    const input = document.querySelector(
        'input[placeholder*=""key name"" i], input[placeholder*=""tên key"" i], input[type=""text""]');
    if (!input) return false;
    const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
    setter.call(input, name);
    input.dispatchEvent(new Event('input', {{ bubbles: true }}));
    input.dispatchEvent(new Event('change', {{ bubbles: true }}));
    const buttons = Array.from(document.querySelectorAll('button')).filter(b => {{
        const r = b.getBoundingClientRect();
        return b.getClientRects().length > 0 && r.width > 0 && r.height > 0 &&
            (b.innerText || '').toLowerCase().includes('create');
    }});
    if (buttons.length === 0) return false;
    buttons[buttons.length - 1].click();
    return true;
}})()
";

        var result = await _client.CallAsync("Runtime.evaluate", new
        {
            expression = script,
            returnByValue = true,
            awaitPromise = false
        }, ct, _sessionId);

        return result.TryGetProperty("result", out var r) && r.TryGetProperty("value", out var val)
            && val.ValueKind == JsonValueKind.True;
    }

    private async Task<bool> ClickButtonByKeywordAsync(IEnumerable<string> keywords, CancellationToken cancellationToken)
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
    const buttons = Array.from(document.querySelectorAll('button, [role=""button""]')).filter(b => {{
        if (!isVisible(b)) return false;
        const text = (b.innerText || '').toLowerCase();
        return keywords.some(k => text.includes(k));
    }});
    if (buttons.length === 0) return false;
    buttons[buttons.length - 1].click();
    return true;
}})()
";

        var result = await _client.CallAsync("Runtime.evaluate", new
        {
            expression = script,
            returnByValue = true,
            awaitPromise = false
        }, cancellationToken, _sessionId);

        return result.TryGetProperty("result", out var r) && r.TryGetProperty("value", out var val)
            && val.ValueKind == JsonValueKind.True;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        // The client is owned by the caller (CdpSession) and disposed separately.
        await ValueTask.CompletedTask;
    }
}