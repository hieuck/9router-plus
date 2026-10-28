using System.Text.Json;
using RouterPlus.Core.Observability;
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

    // Throttling state for ReadStateAsync logging
    private int _readStateFailureCount;
    private bool _readStateFirstFailureLogged;

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
    
    // Content-based detection for keys page: has 'Create new secret key' button OR keys table
    const newKeyKeywords = ['create new secret key', 'create new key', 'new secret key', 'new key', 'create key', 'add key', 'create api key', '+ create', 'add api key', 'generate api key'];
    const hasNewKeyButton = Array.from(document.querySelectorAll('button, [role=""button""]')).some(el =>
        isVisible(el) && newKeyKeywords.some(k => textOf(el).includes(k)));
    
    const hasKeysTable = Array.from(document.querySelectorAll('[data-testid=""api-key-item""], .api-key-row, [data-key], table tbody tr')).filter(isVisible).length > 0;
    
    const isOnKeysPage = hasNewKeyButton || hasKeysTable;

    // Content-based detection for login page: sign in/forgot password/password input
    const hasSignInText = Array.from(document.querySelectorAll('button, [role=""button""], a, h1, h2, h3, p, span, label')).some(el =>
        isVisible(el) && (textOf(el).includes('sign in') || textOf(el).includes('log in') || textOf(el).includes('login')));
    const hasForgotPassword = Array.from(document.querySelectorAll('a, button, [role=""button""]')).some(el =>
        isVisible(el) && textOf(el).includes('forgot password'));
    const hasPasswordInput = !!document.querySelector('input[type=""password""]');
    const isOnLoginPage = hasSignInText || hasForgotPassword || hasPasswordInput;

    // Enhanced Google sign-in detection
    const googleKeywords = ['continue with google', 'sign in with google', 'log in with google', 'tiếp tục với google', 'đăng nhập bằng google'];
    const hasGoogleSignIn = Array.from(document.querySelectorAll('button, [role=""button""], a, [aria-label*=""google"" i], [aria-label*=""Google"" i]')).some(el =>
        isVisible(el) && (
            googleKeywords.some(k => textOf(el).includes(k)) ||
            (el.getAttribute('aria-label') || '').toLowerCase().includes('google')
        ));

    const existingKeys = Array.from(document.querySelectorAll('[data-testid=""api-key-item""], .api-key-row, [data-key]')).filter(isVisible).length;

    // Collect up to 15 visible button/a/[role=button] candidates for debugging
    const candidates = Array.from(document.querySelectorAll('button, a, [role=""button""]')).filter(isVisible);
    const debugCandidates = candidates.slice(0, 15).map(el => {
        const t = textOf(el);
        const role = el.getAttribute('role') || (el.tagName === 'A' ? 'link' : 'button');
        const tag = el.tagName.toLowerCase();
        const ariaLabel = el.getAttribute('aria-label') || '';
        const className = el.className || '';
        return { role, tag, text: t.slice(0, 120), ariaLabel: ariaLabel.slice(0, 120), className: className.slice(0, 120) };
    });

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
        isOnLoginPage: isOnLoginPage,
        hasGoogleSignIn: hasGoogleSignIn,
        existingKeyCount: existingKeys,
        hasNewKeyButton: hasNewKeyButton,
        hasNewKeyNameInput: hasNewKeyNameInput,
        hasCreatedKeyPanel: hasCreatedKeyPanel,
        apiKey: apiKey,
        candidates: debugCandidates
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
            
            // Parse candidates
            IReadOnlyList<ButtonCandidate>? candidates = null;
            if (v.TryGetProperty("candidates", out var candidatesProp) && candidatesProp.ValueKind == JsonValueKind.Array)
            {
                var list = new List<ButtonCandidate>();
                foreach (var c in candidatesProp.EnumerateArray())
                {
                    list.Add(new ButtonCandidate(
                        c.TryGetProperty("role", out var r) ? r.GetString() ?? "" : "",
                        c.TryGetProperty("tag", out var t) ? t.GetString() ?? "" : "",
                        c.TryGetProperty("text", out var txt) ? txt.GetString() ?? "" : "",
                        c.TryGetProperty("ariaLabel", out var al) ? al.GetString() ?? "" : "",
                        c.TryGetProperty("className", out var cn) ? cn.GetString() ?? "" : ""));
                }
                candidates = list;
            }

            var state = new AgnesApiKeyPageState(
                new Uri(urlString),
                IsOnKeysPage: v.GetProperty("isOnKeysPage").GetBoolean(),
                IsOnLoginPage: v.GetProperty("isOnLoginPage").GetBoolean(),
                HasGoogleSignIn: v.GetProperty("hasGoogleSignIn").GetBoolean(),
                ExistingKeyCount: v.GetProperty("existingKeyCount").GetInt32(),
                HasNewKeyButton: v.GetProperty("hasNewKeyButton").GetBoolean(),
                HasNewKeyNameInput: v.GetProperty("hasNewKeyNameInput").GetBoolean(),
                HasCreatedKeyPanel: v.GetProperty("hasCreatedKeyPanel").GetBoolean(),
                ApiKey: v.GetProperty("apiKey").GetString() ?? string.Empty,
                Candidates: candidates);

            // Throttled logging when hasNewKeyButton is false
            if (!state.HasNewKeyButton)
            {
                _readStateFailureCount++;
                bool shouldLog = !_readStateFirstFailureLogged || (_readStateFailureCount % 5 == 0);
                if (shouldLog)
                {
                    _readStateFirstFailureLogged = true;
                    var context = new
                    {
                        url = state.PageUri.ToString(),
                        isOnKeysPage = state.IsOnKeysPage,
                        candidates = state.Candidates?.Select(c => new { c.Role, c.Tag, c.Text, c.AriaLabel, c.ClassName })
                    };
                    ObservabilityHub.Instance.LogEvent(LogLevel.Debug, "AgnesApiKey", "NewKeyButtonNotDetected",
                        "Create new secret key button not detected", context);
                }
            }
            else
            {
                // Reset throttle on success
                _readStateFailureCount = 0;
                _readStateFirstFailureLogged = false;
            }

            return state;
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

    public async Task<bool> WaitForKeysPageAsync(CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(45));
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

    public async Task<bool> TryClickSignInWithGoogleAsync(CancellationToken cancellationToken, TimeSpan? timeout = null)
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
            cancellationToken,
            timeout ?? TimeSpan.FromSeconds(10));
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
            new[]
            {
                "create new secret key",
                "create new key",
                "new secret key",
                "new key",
                "create key",
                "add key",
                "create api key",
                "+ create",
                "add api key",
                "generate api key"
            },
            cancellationToken,
            TimeSpan.FromSeconds(10));
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

    private async Task<bool> ClickByKeywordsAsync(IReadOnlyList<string> keywords, CancellationToken cancellationToken, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        var lastCandidates = new List<ButtonCandidate>();
        bool isFirstAttempt = true;

        // Read initial state once for logging context
        var initialState = await ReadStateAsync(cancellationToken);
        var initialUrl = initialState.PageUri.ToString();
        var initialIsOnKeysPage = initialState.IsOnKeysPage;
        var initialHasNewKeyButton = initialState.HasNewKeyButton;

        while (DateTimeOffset.UtcNow < deadline)
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
    const candidates = Array.from(document.querySelectorAll('button, a, [role=""button""]')).filter(isVisible);
    const matches = candidates.filter(el => keywords.some(k => textOf(el).includes(k)));
    
    // Collect debug info for candidates (up to 15)
    const debugCandidates = candidates.slice(0, 15).map(el => {{
        const t = textOf(el);
        const role = el.getAttribute('role') || (el.tagName === 'A' ? 'link' : 'button');
        const tag = el.tagName.toLowerCase();
        const ariaLabel = el.getAttribute('aria-label') || '';
        const className = el.className || '';
        return {{role, tag, text: t.slice(0, 120), ariaLabel: ariaLabel.slice(0, 120), className: className.slice(0, 120)}};
    }});
    
    // Also return URL and page detection for logging
    const url = window.location.href;
    const isOnKeysPage = (() => {{
        try {{
            const u = new URL(url);
            return u.hostname === 'platform.agnes-ai.com' && u.pathname === '/settings/apiKeys';
        }} catch {{
            return false;
        }}
    }})();
    
    const newKeyKeywords = ['create new secret key', 'create new key', 'new secret key', 'new key', 'create key', 'add key', 'create api key', '+ create', 'add api key', 'generate api key'];
    const hasNewKeyButton = Array.from(document.querySelectorAll('button, [role=""button""]')).some(el =>
        isVisible(el) && newKeyKeywords.some(k => textOf(el).includes(k)));
    
    if (matches.length > 0) {{
        matches[0].scrollIntoView({{block: 'center', inline: 'center'}});
        matches[0].click();
        return {{clicked: true, candidates: debugCandidates, url, isOnKeysPage, hasNewKeyButton}};
    }}
    return {{clicked: false, candidates: debugCandidates, url, isOnKeysPage, hasNewKeyButton}};
}})()
";
            var result = await _client.CallAsync("Runtime.evaluate", new
            {
                expression = script,
                returnByValue = true,
                awaitPromise = false
            }, cancellationToken, _sessionId);

            if (result.TryGetProperty("result", out var remote) && remote.TryGetProperty("value", out var value))
            {
                var clicked = value.GetProperty("clicked").GetBoolean();
                
                // Parse structured candidates
                var candidates = new List<ButtonCandidate>();
                if (value.TryGetProperty("candidates", out var candidatesProp) && candidatesProp.ValueKind == JsonValueKind.Array)
                {
                    foreach (var c in candidatesProp.EnumerateArray())
                    {
                        candidates.Add(new ButtonCandidate(
                            c.TryGetProperty("role", out var r) ? r.GetString() ?? "" : "",
                            c.TryGetProperty("tag", out var t) ? t.GetString() ?? "" : "",
                            c.TryGetProperty("text", out var txt) ? txt.GetString() ?? "" : "",
                            c.TryGetProperty("ariaLabel", out var al) ? al.GetString() ?? "" : "",
                            c.TryGetProperty("className", out var cn) ? cn.GetString() ?? "" : ""));
                    }
                }
                lastCandidates = candidates;

                // Get URL and page info from script result (or fallback to initial)
                var currentUrl = value.TryGetProperty("url", out var urlProp) ? urlProp.GetString() ?? initialUrl : initialUrl;
                var isOnKeysPage = value.TryGetProperty("isOnKeysPage", out var pageProp) ? pageProp.GetBoolean() : initialIsOnKeysPage;
                var hasNewKeyButton = value.TryGetProperty("hasNewKeyButton", out var btnProp) ? btnProp.GetBoolean() : initialHasNewKeyButton;

                // Log on first attempt if no button found
                if (isFirstAttempt && !clicked)
                {
                    isFirstAttempt = false;
                    var context = new
                    {
                        currentUrl = currentUrl,
                        isOnKeysPage = isOnKeysPage,
                        hasNewKeyButton = hasNewKeyButton,
                        candidates = candidates.Select(c => new { c.Role, c.Tag, c.Text, c.AriaLabel, c.ClassName })
                    };
                    ObservabilityHub.Instance.LogEvent(LogLevel.Debug, "AgnesApiKey", "NewKeyButtonNotFound",
                        "Could not find 'Create new secret key' button on first attempt", context);
                }

                if (clicked)
                {
                    return true;
                }
            }

            await Task.Delay(500, cancellationToken);
        }

        // Log debug event with candidates on final failure
        var contextFinal = new
        {
            currentUrl = initialUrl,
            isOnKeysPage = initialIsOnKeysPage,
            hasNewKeyButton = initialHasNewKeyButton,
            candidates = lastCandidates.Select(c => new { c.Role, c.Tag, c.Text, c.AriaLabel, c.ClassName })
        };
        ObservabilityHub.Instance.LogEvent(LogLevel.Debug, "AgnesApiKey", "NewKeyButtonNotFound",
            "Could not find 'Create new secret key' button after all retries", contextFinal);

        // Keep the existing Debug.WriteLine for backwards compatibility
        var candidatesLog = string.Join("; ", lastCandidates.Select(c => $"{c.Role}:{c.Tag}:{c.Text}"));
        System.Diagnostics.Debug.WriteLine($"[AgnesApiKeyCdpBrowser] ClickByKeywordsAsync failed after retries. Keywords: {string.Join(", ", keywords)}. Visible candidates: {candidatesLog}");
        return false;
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

    public async Task<bool> NavigateToKeysPageAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        var script = @"
(function() {
    window.location.href = 'https://platform.agnes-ai.com/settings/apiKeys';
    return true;
})()
";
        return await EvaluateBoolAsync(script, cancellationToken);
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