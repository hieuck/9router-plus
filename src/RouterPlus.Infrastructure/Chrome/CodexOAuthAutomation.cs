using System.Text.Json;
using RouterPlus.Infrastructure.Diagnostics;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Automation for Codex OAuth consent flow.
/// Detects and clicks Continue/Allow buttons on Google OAuth screen.
/// </summary>
public sealed class CodexOAuthAutomation
{
    private readonly ChromeCdpClient _client;
    private readonly string _sessionId;
    private readonly string _targetId;

    public CodexOAuthAutomation(ChromeCdpClient client, string sessionId, string targetId)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _sessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        _targetId = targetId ?? throw new ArgumentNullException(nameof(targetId));
    }

    /// <summary>
    /// Waits for OAuth consent screen and auto-clicks consent button.
    /// Returns true if consent was clicked, false if already authorized or timeout.
    /// </summary>
    public async Task<OAuthConsentResult> WaitAndConsentAsync(
        Uri targetServiceUri,
        string profileEmail,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targetServiceUri);
        ArgumentNullException.ThrowIfNull(profileEmail);

        var deadline = DateTimeOffset.UtcNow + timeout;
        var clickedScreenUrls = new HashSet<string>(StringComparer.Ordinal);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var state = await ReadOAuthStateAsync(cancellationToken);

            DebugConsole.WriteLine($"[CodexOAuth] URL: {state.CurrentUrl}");
            DebugConsole.WriteLine($"[CodexOAuth] IsGoogleOAuth: {state.IsGoogleOAuthPage}");
            DebugConsole.WriteLine($"[CodexOAuth] HasConsentButton: {state.HasConsentButton}");
            DebugConsole.WriteLine($"[CodexOAuth] IsTargetService: {state.IsTargetService}");
            DebugConsole.WriteLine($"[CodexOAuth] HasAccountPicker: {state.HasAccountPicker}");

            // Check if already on target service (OAuth completed)
            if (state.IsTargetService)
            {
                return new OAuthConsentResult(
                    Success: true,
                    AlreadyAuthorized: true,
                    Message: "Already authorized - on target service");
            }

            // Not on Google OAuth page - might be loading or redirecting
            if (!state.IsGoogleOAuthPage && !state.IsAnyOAuthPage)
            {
                await Task.Delay(500, cancellationToken);
                continue;
            }

            // Handle account picker - click matching profile email
            if (state.HasAccountPicker && !state.HasConsentButton)
            {
                // Do not click the same picker twice while its navigation is pending.
                if (!clickedScreenUrls.Add($"picker:{state.CurrentUrl}"))
                {
                    await Task.Delay(500, cancellationToken);
                    continue;
                }

                DebugConsole.WriteLine($"[CodexOAuth] Clicking account matching '{profileEmail}'...");
                var accountClicked = await TryClickAccountAsync(profileEmail, cancellationToken);
                if (accountClicked)
                {
                    await Task.Delay(1500, cancellationToken);
                    continue;
                }

                clickedScreenUrls.Remove($"picker:{state.CurrentUrl}");
                return new OAuthConsentResult(
                    Success: false,
                    AlreadyAuthorized: false,
                    Message: $"Could not select account '{profileEmail}' from picker");
            }

            // Handle consent button. The callback URL is the only success signal;
            // never report success while the consent page is still displayed.
            if (state.HasConsentButton)
            {
                var screenKey = $"consent:{state.CurrentUrl}";
                if (!clickedScreenUrls.Add(screenKey))
                {
                    await Task.Delay(500, cancellationToken);
                    continue;
                }

                DebugConsole.WriteLine("[CodexOAuth] Clicking consent button...");
                var clicked = await TryClickConsentButtonAsync(cancellationToken);
                if (clicked)
                {
                    await Task.Delay(500, cancellationToken);
                    continue;
                }

                clickedScreenUrls.Remove(screenKey);
                return new OAuthConsentResult(
                    Success: false,
                    AlreadyAuthorized: false,
                    Message: "Could not click consent button");
            }

            await Task.Delay(500, cancellationToken);
        }

        return new OAuthConsentResult(
            Success: false,
            AlreadyAuthorized: false,
            Message: "Timeout waiting for OAuth consent screen");
    }

    private async Task<OAuthPageState> ReadOAuthStateAsync(CancellationToken cancellationToken)
    {
        const string script = @"
(function() {
    const currentUrl = window.location.href;
    const host = window.location.host;
    const path = window.location.pathname;

    // Check if on Google OAuth/account page
    const isGoogleOAuthPage = host === 'accounts.google.com' && (
        path.includes('/signin/oauth') ||
        path.includes('/o/oauth2') ||
        path.includes('/ServiceLogin') ||
        path.includes('/AccountChooser') ||
        path.includes('/signin/v2')
    );

    // Check if on OpenAI OAuth page (Codex uses auth.openai.com)
    const isOpenAIOAuthPage = host === 'auth.openai.com' && (
        path.includes('/oauth') ||
        path.includes('/authorize') ||
        path.includes('/login') ||
        path.includes('/signin')
    );

    // Treat any known OAuth provider page as an OAuth screen that may need interaction
    const isAnyOAuthPage = isGoogleOAuthPage || isOpenAIOAuthPage;

    // Check if on target service (Codex post-auth landing page)
    // Must NOT be auth.openai.com - that's still the OAuth flow, not the target
    const isTargetService = (host.includes('chatgpt.com') || host.includes('openai.com')) && host !== 'auth.openai.com'
        || (host.startsWith('localhost') && path.includes('/auth/callback') && currentUrl.includes('code='));

    // Detect account picker (Google uses data-*, OpenAI uses choose-an-account page)
    const isChooseAccountPage = path.includes('/choose-an-account') || path.includes('/account-chooser');
    const accountButtons = Array.from(document.querySelectorAll(
        '[data-email], [data-identifier], ul[role=""listbox""] li, button[data-email], a[data-email], ' +
        'button[aria-label*=""Continue with""], button[aria-label*=""Tiếp tục với""]'
    ));
    const hasAccountPicker = isChooseAccountPage || accountButtons.length > 0;

    // Detect 'Continue with Google' button on OpenAI login page
    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return el.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
    };

    const consentButtonCandidates = Array.from(document.querySelectorAll('button, [role=""button""]')).filter(btn => {
        if (!isVisible(btn)) return false;
        const text = ((btn.innerText || '') + ' ' + (btn.getAttribute('aria-label') || '')).toLowerCase();
        return text.includes('continue with google') ||
               text.includes('continue') ||
               text.includes('tiếp tục') ||
               text.includes('allow') ||
               text.includes('cho phép') ||
               text.includes('accept') ||
               text.includes('chấp nhận');
    });

    const hasConsentButton = consentButtonCandidates.length > 0;

    return {
        currentUrl: currentUrl,
        isGoogleOAuthPage: isGoogleOAuthPage,
        isOpenAIOAuthPage: isOpenAIOAuthPage,
        isAnyOAuthPage: isAnyOAuthPage,
        isTargetService: isTargetService,
        hasAccountPicker: hasAccountPicker,
        hasConsentButton: hasConsentButton,
        isChooseAccountPage: isChooseAccountPage
    };
})()
";

        var result = await _client.CallAsync("Runtime.evaluate", new
        {
            expression = script,
            returnByValue = true,
            awaitPromise = false
        }, cancellationToken, _sessionId);

        try
        {
            var value = result.GetProperty("result").GetProperty("value");
            return new OAuthPageState(
                CurrentUrl: value.GetProperty("currentUrl").GetString()!,
                IsGoogleOAuthPage: value.GetProperty("isGoogleOAuthPage").GetBoolean(),
                IsTargetService: value.GetProperty("isTargetService").GetBoolean(),
                HasAccountPicker: value.GetProperty("hasAccountPicker").GetBoolean(),
                HasConsentButton: value.GetProperty("hasConsentButton").GetBoolean());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new InvalidOperationException($"Failed to read OAuth state: {ex.Message}", ex);
        }
    }

    private async Task<bool> TryClickAccountAsync(string targetEmail, CancellationToken cancellationToken)
    {
        // Find button coordinates via JavaScript, then use CDP mouse events for proper click
        var emailJson = System.Text.Json.JsonSerializer.Serialize(targetEmail);
        var findScript = @"
(function() {
    const targetEmail = " + emailJson + @";
    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return rect.width > 0 && rect.height > 0;
    };
    const emailLower = targetEmail.toLowerCase();
    const buttons = Array.from(document.querySelectorAll('button, [role=""button""]'));
    for (const el of buttons) {
        if (!isVisible(el)) continue;
        const text = ((el.innerText || el.textContent || '') + ' ' + (el.getAttribute('aria-label') || '')).toLowerCase();
        if (text.includes('xóa') || text.includes('remove') || text.includes('delete') || text.includes('sign out')) continue;
        if (text.includes(emailLower)) {
            const rect = el.getBoundingClientRect();
            return { found: true, x: rect.x + rect.width / 2, y: rect.y + rect.height / 2, text: text.substring(0, 60) };
        }
    }
    return { found: false };
})()
";
        try
        {
            var result = await _client.CallAsync("Runtime.evaluate", new { expression = findScript, returnByValue = true }, cancellationToken, _sessionId);
            if (result.TryGetProperty("exceptionDetails", out _)) return false;
            var value = result.GetProperty("result").GetProperty("value");
            if (!value.TryGetProperty("found", out var found) || !found.GetBoolean()) return false;
            var x = value.GetProperty("x").GetDouble();
            var y = value.GetProperty("y").GetDouble();
            var text = value.GetProperty("text").GetString();
            DebugConsole.WriteLine($"[CodexOAuth] Found button at ({x:F1}, {y:F1}): {text}");

            // Try direct element.click() first for more reliable click
            var clickScript = @"
(function() {
    const targetEmail = " + emailJson + @";
    const emailLower = targetEmail.toLowerCase();
    const buttons = Array.from(document.querySelectorAll('button, [role=""button""]'));
    for (const el of buttons) {
        const rect = el.getBoundingClientRect();
        if (rect.width === 0 || rect.height === 0) continue;
        const text = ((el.innerText || el.textContent || '') + ' ' + (el.getAttribute('aria-label') || '')).toLowerCase();
        if (text.includes('xóa') || text.includes('remove') || text.includes('delete') || text.includes('sign out')) continue;
        if (text.includes(emailLower)) {
            el.click();
            return true;
        }
    }
    return false;
})()
";
            var clickResult = await _client.CallAsync("Runtime.evaluate", new { expression = clickScript, returnByValue = true }, cancellationToken, _sessionId);
            if (clickResult.TryGetProperty("result", out var resultProp) && resultProp.TryGetProperty("value", out var clickedProp) && clickedProp.GetBoolean())
            {
                DebugConsole.WriteLine($"[CodexOAuth] Element.click() dispatched");
                return true;
            }

            DebugConsole.WriteLine($"[CodexOAuth] Element.click() failed, trying CDP mouse events");
            await _client.CallAsync("Input.dispatchMouseEvent", new { type = "mousePressed", x, y, button = "left", clickCount = 1 }, cancellationToken, _sessionId);
            await Task.Delay(50, cancellationToken);
            await _client.CallAsync("Input.dispatchMouseEvent", new { type = "mouseReleased", x, y, button = "left", clickCount = 1 }, cancellationToken, _sessionId);
            DebugConsole.WriteLine($"[CodexOAuth] CDP mouse click dispatched");
            return true;
        }
        catch { return false; }
    }


    private async Task<bool> TryClickConsentButtonAsync(CancellationToken cancellationToken)
    {
        const string findScript = @"
(function() {
    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return rect.width > 0 && rect.height > 0;
    };
    const buttons = Array.from(document.querySelectorAll('button, [role=""button""]')).filter(btn => {
        if (!isVisible(btn)) return false;
        const text = ((btn.innerText || '') + ' ' + (btn.getAttribute('aria-label') || '')).toLowerCase();
        return text.includes('continue') ||
               text.includes('tiếp tục') ||
               text.includes('allow') ||
               text.includes('cho phép') ||
               text.includes('accept') ||
               text.includes('chấp nhận');
    });
    if (buttons.length > 0) {
        const rect = buttons[0].getBoundingClientRect();
        return { found: true, x: rect.x + rect.width / 2, y: rect.y + rect.height / 2, text: buttons[0].innerText || '' };
    }
    return { found: false };
})()
";

        try
        {
            var result = await _client.CallAsync("Runtime.evaluate", new
            {
                expression = findScript,
                returnByValue = true,
                awaitPromise = false
            }, cancellationToken, _sessionId);

            if (result.TryGetProperty("exceptionDetails", out _))
            {
                return false;
            }

            var value = result.GetProperty("result").GetProperty("value");
            if (!value.TryGetProperty("found", out var found) || !found.GetBoolean())
            {
                return false;
            }

            var x = value.GetProperty("x").GetDouble();
            var y = value.GetProperty("y").GetDouble();
            var text = value.GetProperty("text").GetString();
            DebugConsole.WriteLine($"[CodexOAuth] Found consent button at ({x:F1}, {y:F1}): {text}");

            await _client.CallAsync("Input.dispatchMouseEvent", new
            {
                type = "mousePressed",
                x,
                y,
                button = "left",
                clickCount = 1
            }, cancellationToken, _sessionId);
            await Task.Delay(50, cancellationToken);
            await _client.CallAsync("Input.dispatchMouseEvent", new
            {
                type = "mouseReleased",
                x,
                y,
                button = "left",
                clickCount = 1
            }, cancellationToken, _sessionId);

            DebugConsole.WriteLine($"[CodexOAuth] CDP mouse click dispatched for consent button");
            return true;
        }
        catch
        {
            return false;
        }
    }
}

public sealed record OAuthPageState(
    string CurrentUrl,
    bool IsGoogleOAuthPage,
    bool IsTargetService,
    bool HasAccountPicker,
    bool HasConsentButton)
{
    // Treat any known OAuth provider page (Google or OpenAI) as needing interaction
    public bool IsAnyOAuthPage => IsGoogleOAuthPage || CurrentUrl.Contains("auth.openai.com", StringComparison.OrdinalIgnoreCase);
}

public sealed record OAuthConsentResult(
    bool Success,
    bool AlreadyAuthorized,
    string Message);
