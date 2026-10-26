using System.Text.Json;
using RouterPlus.Core.Observability;
using RouterPlus.Infrastructure.Diagnostics;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Shared Google OAuth page detection logic.
/// Eliminates duplicated accounts.google.com / TOTP / consent detection across provider automation classes.
/// </summary>
public static class GoogleOAuthPageDetector
{
    internal static bool IsGoogleOAuthHost(string host) =>
        string.Equals(host, "accounts.google.com", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(host, "accounts.google.com.", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Detects Google-specific page state from the current page.
    /// Returns null if not on a Google OAuth page.
    /// </summary>
    public static async Task<GoogleOAuthPageState?> TryDetectAsync(
        IChromeCdpClient client,
        string sessionId,
        CancellationToken cancellationToken)
    {
        const string script = @"
(function() {
    const currentUrl = window.location.href;
    const host = window.location.host;
    const path = window.location.pathname;

    // Check if on Google OAuth/account page
    const isGoogleOAuthPage = host === 'accounts.google.com' ||
                               host === 'accounts.google.com.';

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
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Error,
                "GoogleOAuthDetector",
                "ReadStateFailed",
                "Failed to read Google OAuth state",
                new { error = ex.Message });
            return null;
        }
    }

    /// <summary>
    /// Clicks the Google account matching the profile email.
    /// </summary>
    public static async Task<bool> TryClickAccountAsync(
        IChromeCdpClient client,
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
        // ONLY exact profile email match; no fallback to first/scroll/use-another/add-account.
        const textExact = text.trim() === emailLower;
        if (dataEmail === emailLower || dataIdentifier === emailLower || textExact) {
            matchedButton = el;
            break;
        }
    }

    if (matchedButton) {
        matchedButton.scrollIntoView({ block: 'center' });
        const rect = matchedButton.getBoundingClientRect();
        const viewportWidth = window.innerWidth || document.documentElement.clientWidth;
        const viewportHeight = window.innerHeight || document.documentElement.clientHeight;
        return { found: true, x: rect.left + rect.width / 2, y: rect.top + rect.height / 2,
                 viewportWidth: viewportWidth, viewportHeight: viewportHeight,
                 left: rect.left, top: rect.top, width: rect.width, height: rect.height,
                 totalButtons: allButtons.length, allButtons: allButtons };
    }
    return { found: false, totalButtons: allButtons.length, allButtons: allButtons };
})()
";

        try
        {
            // Check CDP/JS exceptions at both top-level result and nested result.result.
            var result = await client.CallAsync("Runtime.evaluate", new
            {
                expression = script,
                returnByValue = true
            }, cancellationToken, sessionId);

            if (result.TryGetProperty("exceptionDetails", out _))
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Warning,
                    "GoogleOAuthDetector",
                    "ClickAccountError",
                    "Click account script exception",
                    new { email = profileEmail });
                return false;
            }

            if (!result.TryGetProperty("result", out var resultProp))
            {
                return false;
            }

            if (resultProp.TryGetProperty("exceptionDetails", out _))
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Warning,
                    "GoogleOAuthDetector",
                    "ClickAccountError",
                    "Click account script exception",
                    new { email = profileEmail });
                return false;
            }

            if (!resultProp.TryGetProperty("value", out var valueProp))
            {
                return false;
            }

            if (valueProp.ValueKind == JsonValueKind.False)
            {
                return false;
            }

            if (valueProp.TryGetProperty("found", out var foundProp) &&
                foundProp.GetBoolean() &&
                valueProp.TryGetProperty("x", out var xProp) &&
                valueProp.TryGetProperty("y", out var yProp))
            {
                if (xProp.ValueKind != JsonValueKind.Number || yProp.ValueKind != JsonValueKind.Number)
                {
                    return false;
                }

                double x = xProp.GetDouble();
                double y = yProp.GetDouble();

                // Coordinate safety: reject non-positive or out-of-viewport coords.
                double viewportWidth = valueProp.TryGetProperty("viewportWidth", out var vwProp) && vwProp.ValueKind == JsonValueKind.Number
                    ? vwProp.GetDouble() : 0;
                double viewportHeight = valueProp.TryGetProperty("viewportHeight", out var vhProp) && vhProp.ValueKind == JsonValueKind.Number
                    ? vhProp.GetDouble() : 0;

                if (x <= 0 || y <= 0)
                {
                    return false;
                }
                if (viewportWidth > 0 && (x > viewportWidth))
                {
                    return false;
                }
                if (viewportHeight > 0 && (y > viewportHeight))
                {
                    return false;
                }

                // Element bounds must be positive if provided.
                if (valueProp.TryGetProperty("width", out var wProp) && wProp.ValueKind == JsonValueKind.Number && wProp.GetDouble() <= 0)
                {
                    return false;
                }
                if (valueProp.TryGetProperty("height", out var hProp) && hProp.ValueKind == JsonValueKind.Number && hProp.GetDouble() <= 0)
                {
                    return false;
                }

                // Trusted CDP click
                await client.CallAsync("Input.dispatchMouseEvent", new
                {
                    type = "mouseMoved",
                    x,
                    y,
                    button = "none",
                    buttons = 0
                }, cancellationToken, sessionId);

                await client.CallAsync("Input.dispatchMouseEvent", new
                {
                    type = "mousePressed",
                    x,
                    y,
                    button = "left",
                    buttons = 1,
                    clickCount = 1
                }, cancellationToken, sessionId);

                await Task.Delay(50, cancellationToken);

                await client.CallAsync("Input.dispatchMouseEvent", new
                {
                    type = "mouseReleased",
                    x,
                    y,
                    button = "left",
                    buttons = 0,
                    clickCount = 1
                }, cancellationToken, sessionId);

                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Info,
                    "GoogleOAuthDetector",
                    "AccountClicked",
                    "Google account clicked successfully",
                    new { email = profileEmail, x, y, trusted = true });
                return true;
            }

            return false;
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Error,
                "GoogleOAuthDetector",
                "ClickAccountError",
                "Click account error",
                new { email = profileEmail, error = ex.Message });
            return false;
        }
    }

    /// <summary>
    /// Fills TOTP code into Google 2FA input.
    /// </summary>
    public static async Task<bool> TryFillTotpAsync(
        IChromeCdpClient client,
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
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Info,
                    "GoogleOAuthDetector",
                    "TotpFilled",
                    "TOTP filled and submitted",
                    null);
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
        IChromeCdpClient client,
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
    const isEnabled = el => {
        if (!el) return false;
        if (el.disabled) return false;
        const ariaDisabled = (el.getAttribute('aria-disabled') || '').toLowerCase();
        return ariaDisabled !== 'true';
    };
    const buttons = Array.from(document.querySelectorAll('button, [role=""button""]')).filter(btn => {
        if (!isVisible(btn) || !isEnabled(btn)) return false;
        const text = ((btn.innerText || '') + ' ' + (btn.getAttribute('aria-label') || '')).toLowerCase();
        return text.includes('continue') || text.includes('tiếp tục') ||
               text.includes('allow') || text.includes('cho phép') ||
               text.includes('accept') || text.includes('chấp nhận');
    });
    if (buttons.length > 0) {
        const btn = buttons[0];
        btn.scrollIntoView({ block: 'center' });
        const rect = btn.getBoundingClientRect();
        const viewportWidth = window.innerWidth || document.documentElement.clientWidth;
        const viewportHeight = window.innerHeight || document.documentElement.clientHeight;
        return { found: true, x: rect.left + rect.width / 2, y: rect.top + rect.height / 2,
                 viewportWidth: viewportWidth, viewportHeight: viewportHeight,
                 left: rect.left, top: rect.top, width: rect.width, height: rect.height };
    }
    return { found: false };
})()
";

        try
        {
            var result = await client.CallAsync("Runtime.evaluate", new
            {
                expression = script,
                returnByValue = true
            }, cancellationToken, sessionId);

            if (result.TryGetProperty("exceptionDetails", out _))
            {
                return false;
            }

            if (!result.TryGetProperty("result", out var resultProp))
            {
                return false;
            }

            if (resultProp.TryGetProperty("exceptionDetails", out _))
            {
                return false;
            }

            if (!resultProp.TryGetProperty("value", out var valueProp))
            {
                return false;
            }

            if (valueProp.ValueKind == JsonValueKind.False)
            {
                return false;
            }

            if (valueProp.TryGetProperty("found", out var foundProp) &&
                foundProp.GetBoolean() &&
                valueProp.TryGetProperty("x", out var xProp) &&
                valueProp.TryGetProperty("y", out var yProp))
            {
                if (xProp.ValueKind != JsonValueKind.Number || yProp.ValueKind != JsonValueKind.Number)
                {
                    return false;
                }

                double x = xProp.GetDouble();
                double y = yProp.GetDouble();

                double viewportWidth = valueProp.TryGetProperty("viewportWidth", out var vwProp) && vwProp.ValueKind == JsonValueKind.Number
                    ? vwProp.GetDouble() : 0;
                double viewportHeight = valueProp.TryGetProperty("viewportHeight", out var vhProp) && vhProp.ValueKind == JsonValueKind.Number
                    ? vhProp.GetDouble() : 0;

                if (x <= 0 || y <= 0)
                {
                    return false;
                }
                if (viewportWidth > 0 && x > viewportWidth)
                {
                    return false;
                }
                if (viewportHeight > 0 && y > viewportHeight)
                {
                    return false;
                }

                if (valueProp.TryGetProperty("width", out var wProp) && wProp.ValueKind == JsonValueKind.Number && wProp.GetDouble() <= 0)
                {
                    return false;
                }
                if (valueProp.TryGetProperty("height", out var hProp) && hProp.ValueKind == JsonValueKind.Number && hProp.GetDouble() <= 0)
                {
                    return false;
                }

                await client.CallAsync("Input.dispatchMouseEvent", new
                {
                    type = "mouseMoved",
                    x,
                    y,
                    button = "none",
                    buttons = 0
                }, cancellationToken, sessionId);

                await client.CallAsync("Input.dispatchMouseEvent", new
                {
                    type = "mousePressed",
                    x,
                    y,
                    button = "left",
                    buttons = 1,
                    clickCount = 1
                }, cancellationToken, sessionId);

                await Task.Delay(50, cancellationToken);

                await client.CallAsync("Input.dispatchMouseEvent", new
                {
                    type = "mouseReleased",
                    x,
                    y,
                    button = "left",
                    buttons = 0,
                    clickCount = 1
                }, cancellationToken, sessionId);

                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Info,
                    "GoogleOAuthDetector",
                    "ConsentButtonClicked",
                    "Google consent button clicked",
                    new { x, y, trusted = true });
                return true;
            }

            return false;
        }
        catch (Exception ex) when (!(ex is OperationCanceledException && cancellationToken.IsCancellationRequested))
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Warning,
                "GoogleOAuthDetector",
                "ConsentClickFailed",
                "Consent click failed",
                new { error = ex.Message });
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