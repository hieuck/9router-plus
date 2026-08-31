using System.Text.Json;
using RouterPlus.Infrastructure.Diagnostics;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Shared Google OAuth page detection logic.
/// Eliminates duplicated accounts.google.com / TOTP / consent detection across provider automation classes.
/// </summary>
public static class GoogleOAuthPageDetector
{
    /// <summary>
    /// Detects Google-specific page state from the current page.
    /// Returns null if not on a Google OAuth page.
    /// </summary>
    public static async Task<GoogleOAuthPageState?> TryDetectAsync(
        ChromeCdpClient client,
        string sessionId,
        CancellationToken cancellationToken)
    {
        const string script = @"
(function() {
    const currentUrl = window.location.href;
    const host = window.location.host;
    const path = window.location.pathname;

    // Check if on Google OAuth/account page
    const isGoogleOAuthPage = host === 'accounts.google.com' && (
        path.includes('/signin/oauth') ||
        path.includes('/v3/signin') ||
        path.includes('/o/oauth2') ||
        path.includes('/ServiceLogin') ||
        path.includes('/AccountChooser') ||
        path.includes('/signin/v2')
    );

    if (!isGoogleOAuthPage) {
        return { isGoogleOAuthPage: false };
    }

    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return el.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
    };

    // Detect account picker (Google uses data-*)
    const isChooseAccountPage = path.includes('/choose-an-account') ||
                                 path.includes('/account-chooser') ||
                                 path.includes('/accountchooser');
    const accountButtons = Array.from(document.querySelectorAll(
        '[data-email], [data-identifier], ul[role=""listbox""] li, button[data-email], a[data-email], ' +
        'button[aria-label*=""Continue with""], button[aria-label*=""Tiếp tục với""]'
    ));
    const hasAccountPicker = isChooseAccountPage || accountButtons.length > 0;

    // Detect Google TOTP input
    const totpInputs = Array.from(document.querySelectorAll('input[type=""tel""], input[name*=""otp""], input[id*=""otp""], input[name*=""totpPin""]'));
    const hasGoogleTotpInput = totpInputs.some(isVisible);

    // Detect consent buttons (Continue with Google / Continue / Allow)
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
    const hasGoogleConsentButton = consentButtonCandidates.length > 0;

    return {
        isGoogleOAuthPage: true,
        currentUrl: currentUrl,
        hasAccountPicker: hasAccountPicker,
        hasGoogleTotpInput: hasGoogleTotpInput,
        hasGoogleConsentButton: hasGoogleConsentButton
    };
})()
";

        try
        {
            var result = await client.CallAsync("Runtime.evaluate", new
            {
                expression = script,
                returnByValue = true,
                awaitPromise = false
            }, cancellationToken, sessionId);

            var value = result.GetProperty("result").GetProperty("value");

            if (!value.GetProperty("isGoogleOAuthPage").GetBoolean())
            {
                return null;
            }

            return new GoogleOAuthPageState
            {
                CurrentUrl = value.GetProperty("currentUrl").GetString()!,
                HasAccountPicker = value.GetProperty("hasAccountPicker").GetBoolean(),
                HasGoogleTotpInput = value.GetProperty("hasGoogleTotpInput").GetBoolean(),
                HasGoogleConsentButton = value.GetProperty("hasGoogleConsentButton").GetBoolean()
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            DebugConsole.WriteLine($"[GoogleOAuthDetector] Failed to read Google OAuth state: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Clicks the Google account matching the profile email.
    /// </summary>
    public static async Task<bool> TryClickAccountAsync(
        ChromeCdpClient client,
        string sessionId,
        string profileEmail,
        CancellationToken cancellationToken)
    {
        var emailJson = JsonSerializer.Serialize(profileEmail);
        var script = @"
(function() {
    const targetEmail = " + emailJson + @";
    const emailLower = targetEmail.toLowerCase();
    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return rect.width > 0 && rect.height > 0;
    };
    // Search broader - include div/li items
    const buttons = Array.from(document.querySelectorAll('button, [role=""button""], li[data-email], li[data-identifier], div[data-email], div[data-identifier], [data-email], [data-identifier]'));
    let matchedButton = null;
    let allButtons = [];

    for (const el of buttons) {
        const visible = isVisible(el);
        const text = ((el.innerText || el.textContent || '') + ' ' + (el.getAttribute('aria-label') || '')).toLowerCase();
        const dataEmail = (el.getAttribute('data-email') || '').toLowerCase();
        const dataIdentifier = (el.getAttribute('data-identifier') || '').toLowerCase();
        allButtons.push({text: text.substring(0, 100), visible, dataEmail, dataIdentifier, tag: el.tagName});

        if (!visible) continue;
        if (text.includes('xóa') || text.includes('remove') || text.includes('delete') || text.includes('sign out') || text.includes('cuộn xuống') || text.includes('use another')) continue;
        if (text.includes(emailLower) || dataEmail === emailLower || dataIdentifier === emailLower) {
            matchedButton = el;
            break;
        }
    }

    // Fallback: If no email match, look for actual Google account (not AWS SSO identity)
    let scrollButton = null;
    if (!matchedButton) {
        for (const el of buttons) {
            if (!isVisible(el)) continue;
            const text = ((el.innerText || el.textContent || '') + ' ' + (el.getAttribute('aria-label') || '')).toLowerCase();

            // Track 'scroll down' / 'use another' button
            if (text.includes('cuộn xuống') || text.includes('use another') || text.includes('add account')) {
                if (!scrollButton) scrollButton = el;
                continue;
            }
            if (text.includes('remove') || text.includes('xóa')) continue;
            // Skip AWS SSO identity provider button (provider-agnostic fallback)
            if (text.includes('amazon web services') || text.includes('aws sso')) continue;
            // Found a real account button
            matchedButton = el;
            break;
        }
    }

    // If no real account button found, click 'scroll down' to reveal more accounts
    if (!matchedButton && scrollButton) {
        matchedButton = scrollButton;
    }

    if (matchedButton) {
        matchedButton.click();
        return {clicked: true, found: true, totalButtons: allButtons.length, allButtons};
    }
    return {clicked: false, found: false, totalButtons: allButtons.length, allButtons};
})()
";

        try
        {
            var result = await client.CallAsync("Runtime.evaluate", new
            {
                expression = script,
                returnByValue = true
            }, cancellationToken, sessionId);

            if (result.TryGetProperty("result", out var resultProp) &&
                resultProp.TryGetProperty("value", out var valueProp))
            {
                var clicked = valueProp.GetProperty("clicked").GetBoolean();
                if (clicked)
                {
                    DebugConsole.WriteLine($"[GoogleOAuthDetector] Account '{profileEmail}' clicked successfully");
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[GoogleOAuthDetector] Click account error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Fills TOTP code into Google 2FA input.
    /// </summary>
    public static async Task<bool> TryFillTotpAsync(
        ChromeCdpClient client,
        string sessionId,
        string totpCode,
        CancellationToken cancellationToken)
    {
        var codeJson = JsonSerializer.Serialize(totpCode);
        var script = @"
(function() {
    const code = " + codeJson + @";
    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return rect.width > 0 && rect.height > 0;
    };
    const inputs = Array.from(document.querySelectorAll('input[type=""tel""], input[name*=""otp""], input[id*=""otp""], input[name*=""totpPin""]'));
    for (const input of inputs) {
        if (isVisible(input)) {
            input.value = code;
            input.dispatchEvent(new Event('input', { bubbles: true }));
            input.dispatchEvent(new Event('change', { bubbles: true }));

            // Find and click submit button
            const form = input.closest('form');
            if (form) {
                const submitBtn = form.querySelector('button[type=""submit""]') ||
                                  Array.from(form.querySelectorAll('button')).find(b =>
                                      (b.innerText || '').toLowerCase().includes('next') ||
                                      (b.innerText || '').toLowerCase().includes('tiếp') ||
                                      (b.innerText || '').toLowerCase().includes('verify')
                                  );
                if (submitBtn) {
                    submitBtn.click();
                    return true;
                }
            }
            return true;
        }
    }
    return false;
})()
";

        try
        {
            var result = await client.CallAsync("Runtime.evaluate", new
            {
                expression = script,
                returnByValue = true
            }, cancellationToken, sessionId);

            if (result.TryGetProperty("result", out var resultProp) &&
                resultProp.TryGetProperty("value", out var filledProp) &&
                filledProp.GetBoolean())
            {
                DebugConsole.WriteLine("[GoogleOAuthDetector] TOTP filled and submitted");
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Clicks Google consent button (Continue/Allow).
    /// </summary>
    public static async Task<bool> TryClickGoogleConsentButtonAsync(
        ChromeCdpClient client,
        string sessionId,
        CancellationToken cancellationToken)
    {
        const string script = @"
(function() {
    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return rect.width > 0 && rect.height > 0;
    };
    const buttons = Array.from(document.querySelectorAll('button, [role=""button""]')).filter(btn => {
        if (!isVisible(btn)) return false;
        const text = ((btn.innerText || '') + ' ' + (btn.getAttribute('aria-label') || '')).toLowerCase();
        return text.includes('continue') || text.includes('tiếp tục') ||
               text.includes('allow') || text.includes('cho phép');
    });
    if (buttons.length > 0) {
        buttons[0].click();
        return true;
    }
    return false;
})()
";

        try
        {
            var result = await client.CallAsync("Runtime.evaluate", new
            {
                expression = script,
                returnByValue = true
            }, cancellationToken, sessionId);

            if (result.TryGetProperty("result", out var resultProp) &&
                resultProp.TryGetProperty("value", out var clickedProp) &&
                clickedProp.GetBoolean())
            {
                DebugConsole.WriteLine("[GoogleOAuthDetector] Google consent button clicked");
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// Detected state for Google OAuth pages.
/// </summary>
public sealed record GoogleOAuthPageState
{
    public required string CurrentUrl { get; init; }
    public required bool HasAccountPicker { get; init; }
    public required bool HasGoogleTotpInput { get; init; }
    public required bool HasGoogleConsentButton { get; init; }
}