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
                DebugConsole.WriteLine($"[CodexOAuth] Clicking account matching '{profileEmail}'...");
                var accountClicked = await TryClickAccountAsync(profileEmail, cancellationToken);
                if (accountClicked)
                {
                    await Task.Delay(1000, cancellationToken);
                    continue;
                }
                else
                {
                    return new OAuthConsentResult(
                        Success: false,
                        AlreadyAuthorized: false,
                        Message: $"Could not select account '{profileEmail}' from picker");
                }
            }

            // Handle consent button
            if (state.HasConsentButton)
            {
                DebugConsole.WriteLine("[CodexOAuth] Clicking consent button...");
                var clicked = await TryClickConsentButtonAsync(cancellationToken);
                if (clicked)
                {
                    // Wait for redirect to target service
                    await Task.Delay(2000, cancellationToken);

                    // Verify we're on target service
                    var finalState = await ReadOAuthStateAsync(cancellationToken);
                    if (finalState.IsTargetService)
                    {
                        return new OAuthConsentResult(
                            Success: true,
                            AlreadyAuthorized: false,
                            Message: "Consent clicked successfully");
                    }
                    else
                    {
                        return new OAuthConsentResult(
                            Success: false,
                            AlreadyAuthorized: false,
                            Message: $"Consent clicked but not on target service. Current: {finalState.CurrentUrl}");
                    }
                }
                else
                {
                    return new OAuthConsentResult(
                        Success: false,
                        AlreadyAuthorized: false,
                        Message: "Could not click consent button");
                }
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
    const isTargetService = (host.includes('chatgpt.com') || host.includes('openai.com')) && host !== 'auth.openai.com';

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
        var targetEmailJson = JsonSerializer.Serialize(targetEmail);
        const string clickScript = @"
(function(targetEmail) {
    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return el.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
    };

    const emailLower = targetEmail.toLowerCase();
    const emailPrefix = emailLower.split('@')[0];

    // Strategy 1: Find element with exact data-email attribute
    const dataEmailEls = document.querySelectorAll(
        '[data-email], [data-identifier], [data-user-email], [data-value*=""@""]'
    );
    for (const el of dataEmailEls) {
        if (!isVisible(el)) continue;
        const attr = (el.getAttribute('data-email') || el.getAttribute('data-identifier') ||
                      el.getAttribute('data-user-email') || el.getAttribute('data-value') || '').toLowerCase();
        if (attr === emailLower || attr.includes(emailLower) || attr.includes(emailPrefix)) {
            el.click();
            return { clicked: true, matched: attr };
        }
    }

    // Strategy 2: Find clickable element with exact email text inside
    const clickables = Array.from(document.querySelectorAll(
        'button, [role=""button""], li, a, div[role=""option""], div[role=""link""]'
    ));
    for (const el of clickables) {
        if (!isVisible(el)) continue;
        const text = (el.innerText || el.textContent || '').toLowerCase();
        if (text === emailLower || (text.includes(emailLower) && !text.includes('remove') && !text.includes('xóa'))) {
            el.click();
            return { clicked: true, matched: text };
        }
    }

    // Strategy 3: Find parent container that wraps email text + exclude destructive actions
    const allDivs = Array.from(document.querySelectorAll('div, li, article'));
    for (const el of allDivs) {
        if (!isVisible(el)) continue;
        const text = (el.innerText || el.textContent || '').toLowerCase();
        // Must contain email and NOT contain remove/delete keywords
        if (text.includes(emailLower) &&
            !text.includes('remove') && !text.includes('xóa') &&
            !text.includes('delete') && !text.includes('sign out')) {
            // Try clicking the element itself or a button inside
            const innerBtn = el.querySelector('button, [role=""button""], a');
            const target = innerBtn || el;
            target.click();
            return { clicked: true, matched: text.substring(0, 50) };
        }
    }

    return { clicked: false, matched: null, available: document.querySelectorAll('button, li, a, div[role=""button""]').length };
})(arguments[0])
";

        try
        {
            var result = await _client.CallAsync("Runtime.evaluate", new
            {
                expression = clickScript,
                arguments = new[] { targetEmail },
                returnByValue = true,
                awaitPromise = false
            }, cancellationToken, _sessionId);

            if (result.TryGetProperty("exceptionDetails", out _))
            {
                return false;
            }

            var value = result.GetProperty("result").GetProperty("value");
            var clicked = value.GetProperty("clicked").GetBoolean();
            if (value.TryGetProperty("matched", out var matched) && matched.ValueKind != System.Text.Json.JsonValueKind.Null)
            {
                DebugConsole.WriteLine($"[CodexOAuth] Clicked account matching: {matched.GetString()}");
            }
            return clicked;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> TryClickConsentButtonAsync(CancellationToken cancellationToken)
    {
        const string clickScript = @"
(function() {
    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return el.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
    };

    // Find consent button
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
        buttons[0].click();
        return { clicked: true, buttonText: buttons[0].innerText || '' };
    }

    return { clicked: false, buttonText: '' };
})()
";

        try
        {
            var result = await _client.CallAsync("Runtime.evaluate", new
            {
                expression = clickScript,
                returnByValue = true,
                awaitPromise = false
            }, cancellationToken, _sessionId);

            if (result.TryGetProperty("exceptionDetails", out _))
            {
                return false;
            }

            var value = result.GetProperty("result").GetProperty("value");
            var clicked = value.GetProperty("clicked").GetBoolean();
            if (clicked && value.TryGetProperty("buttonText", out var btnText))
            {
                DebugConsole.WriteLine($"[CodexOAuth] Clicked button: {btnText.GetString()}");
            }
            return clicked;
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
