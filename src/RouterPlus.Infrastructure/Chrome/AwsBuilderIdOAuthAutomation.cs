using System.Text.Json;
using RouterPlus.Infrastructure.Diagnostics;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Automation for AWS Builder ID OAuth consent flow (used by Kiro).
/// Handles: Continue with Google, account picker, TOTP, Google consent, AWS consent.
/// </summary>
public sealed class AwsBuilderIdOAuthAutomation
{
    private readonly ChromeCdpClient _client;
    private readonly string _sessionId;
    private readonly string _targetId;

    public AwsBuilderIdOAuthAutomation(ChromeCdpClient client, string sessionId, string targetId)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _sessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        _targetId = targetId ?? throw new ArgumentNullException(nameof(targetId));
    }

    /// <summary>
    /// Waits for OAuth consent flow and auto-clicks through all steps.
    /// Returns true if flow completed, false if timeout or error.
    /// </summary>
    public async Task<OAuthConsentResult> WaitAndConsentAsync(
        Uri verificationUri,
        string profileEmail,
        Func<Task<string?>>? totpGenerator,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(verificationUri);
        ArgumentNullException.ThrowIfNull(profileEmail);

        var deadline = DateTimeOffset.UtcNow + timeout;
        var clickedScreenUrls = new HashSet<string>(StringComparer.Ordinal);
        var totpAttempted = false;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var state = await ReadOAuthStateAsync(cancellationToken);

            DebugConsole.WriteLine($"[AwsBuilderIdOAuth] URL: {state.CurrentUrl}");
            DebugConsole.WriteLine($"[AwsBuilderIdOAuth] IsAwsBuilderIdPage: {state.IsAwsBuilderIdPage}");
            DebugConsole.WriteLine($"[AwsBuilderIdOAuth] IsGoogleOAuthPage: {state.IsGoogleOAuthPage}");
            DebugConsole.WriteLine($"[AwsBuilderIdOAuth] HasContinueWithGoogle: {state.HasContinueWithGoogleButton}");
            DebugConsole.WriteLine($"[AwsBuilderIdOAuth] HasAccountPicker: {state.HasAccountPicker}");
            DebugConsole.WriteLine($"[AwsBuilderIdOAuth] HasGoogleTotpInput: {state.HasGoogleTotpInput}");
            DebugConsole.WriteLine($"[AwsBuilderIdOAuth] HasGoogleConsent: {state.HasGoogleConsentButton}");
            DebugConsole.WriteLine($"[AwsBuilderIdOAuth] HasAwsConsent: {state.HasAwsConsentButton}");
            DebugConsole.WriteLine($"[AwsBuilderIdOAuth] IsCompletionPage: {state.IsCompletionPage}");

            // Check if flow completed
            if (state.IsCompletionPage)
            {
                return new OAuthConsentResult(
                    Success: true,
                    AlreadyAuthorized: false,
                    Message: "AWS Builder ID authorization completed");
            }

            // Handle "Continue with Google" button on AWS Builder ID page
            if (state.IsAwsBuilderIdPage && state.HasContinueWithGoogleButton)
            {
                var screenKey = $"aws-continue-google:{state.CurrentUrl}";
                if (!clickedScreenUrls.Add(screenKey))
                {
                    await Task.Delay(500, cancellationToken);
                    continue;
                }

                DebugConsole.WriteLine("[AwsBuilderIdOAuth] Clicking 'Continue with Google'...");
                var clicked = await TryClickContinueWithGoogleAsync(cancellationToken);
                if (clicked)
                {
                    await Task.Delay(1500, cancellationToken);
                    continue;
                }

                clickedScreenUrls.Remove(screenKey);
                return new OAuthConsentResult(
                    Success: false,
                    AlreadyAuthorized: false,
                    Message: "Could not click 'Continue with Google' button");
            }

            // Handle Google account picker
            if (state.IsGoogleOAuthPage && state.HasAccountPicker && !state.HasGoogleConsentButton)
            {
                var screenKey = $"picker:{state.CurrentUrl}";
                if (!clickedScreenUrls.Add(screenKey))
                {
                    await Task.Delay(500, cancellationToken);
                    continue;
                }

                DebugConsole.WriteLine($"[AwsBuilderIdOAuth] Clicking account matching '{profileEmail}'...");
                var accountClicked = await TryClickAccountAsync(profileEmail, cancellationToken);
                if (accountClicked)
                {
                    await Task.Delay(1500, cancellationToken);
                    continue;
                }

                clickedScreenUrls.Remove(screenKey);
                return new OAuthConsentResult(
                    Success: false,
                    AlreadyAuthorized: false,
                    Message: $"Could not select account '{profileEmail}' from picker");
            }

            // Handle Google TOTP
            if (state.IsGoogleOAuthPage && state.HasGoogleTotpInput && !totpAttempted)
            {
                totpAttempted = true;

                if (totpGenerator is not null)
                {
                    var totpCode = await totpGenerator();
                    if (!string.IsNullOrWhiteSpace(totpCode))
                    {
                        DebugConsole.WriteLine("[AwsBuilderIdOAuth] Auto-filling TOTP code...");
                        var filled = await TryFillTotpAsync(totpCode, cancellationToken);
                        if (filled)
                        {
                            await Task.Delay(2000, cancellationToken);
                            continue;
                        }
                    }
                }

                // No TOTP generator or failed to fill - wait for manual entry
                DebugConsole.WriteLine("[AwsBuilderIdOAuth] Waiting for manual TOTP entry...");
                await Task.Delay(1000, cancellationToken);
                continue;
            }

            // Handle Google consent button
            if (state.IsGoogleOAuthPage && state.HasGoogleConsentButton)
            {
                var screenKey = $"google-consent:{state.CurrentUrl}";
                if (!clickedScreenUrls.Add(screenKey))
                {
                    await Task.Delay(500, cancellationToken);
                    continue;
                }

                DebugConsole.WriteLine("[AwsBuilderIdOAuth] Clicking Google consent button...");
                var clicked = await TryClickGoogleConsentButtonAsync(cancellationToken);
                if (clicked)
                {
                    await Task.Delay(1500, cancellationToken);
                    continue;
                }

                clickedScreenUrls.Remove(screenKey);
                return new OAuthConsentResult(
                    Success: false,
                    AlreadyAuthorized: false,
                    Message: "Could not click Google consent button");
            }

            // Handle AWS Builder ID consent buttons
            if (state.IsAwsBuilderIdPage && state.HasAwsConsentButton)
            {
                var screenKey = $"aws-consent:{state.CurrentUrl}";
                if (!clickedScreenUrls.Add(screenKey))
                {
                    await Task.Delay(500, cancellationToken);
                    continue;
                }

                DebugConsole.WriteLine("[AwsBuilderIdOAuth] Clicking AWS Builder ID consent button...");
                var clicked = await TryClickAwsConsentButtonAsync(cancellationToken);
                if (clicked)
                {
                    await Task.Delay(1500, cancellationToken);
                    continue;
                }

                clickedScreenUrls.Remove(screenKey);
                return new OAuthConsentResult(
                    Success: false,
                    AlreadyAuthorized: false,
                    Message: "Could not click AWS Builder ID consent button");
            }

            await Task.Delay(500, cancellationToken);
        }

        return new OAuthConsentResult(
            Success: false,
            AlreadyAuthorized: false,
            Message: "Timeout waiting for OAuth consent flow");
    }

    private async Task<AwsBuilderIdOAuthPageState> ReadOAuthStateAsync(CancellationToken cancellationToken)
    {
        const string script = @"
(function() {
    const currentUrl = window.location.href;
    const host = window.location.host;
    const path = window.location.pathname;

    // Check if on AWS Builder ID page
    const isAwsBuilderIdPage = host.includes('view.awsapps.com') ||
                                host.includes('auth.kiro.dev') ||
                                host.includes('codewhisperer.us-east-1.amazonaws.com') ||
                                host.includes('us-east-1.signin.aws') ||
                                currentUrl.includes('aws.amazon.com/authorization');

    // Check if on Google OAuth/account page
    const isGoogleOAuthPage = host === 'accounts.google.com';

    // Check for completion page (device code activated message)
    const bodyText = document.body?.innerText?.toLowerCase() || '';
    const isCompletionPage = bodyText.includes('device is now connected') ||
                             bodyText.includes('authorization granted') ||
                             bodyText.includes('you may close') ||
                             bodyText.includes('success');

    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return el.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
    };

    // Detect 'Continue with Google' button on AWS page
    const continueWithGoogleButtons = Array.from(document.querySelectorAll('button, [role=""button""], a')).filter(btn => {
        if (!isVisible(btn)) return false;
        const text = ((btn.innerText || '') + ' ' + (btn.getAttribute('aria-label') || '')).toLowerCase();
        return text.includes('continue with google') || text.includes('sign in with google') || text.includes('google');
    });
    const hasContinueWithGoogleButton = continueWithGoogleButtons.length > 0;

    // Detect account picker
    const accountButtons = Array.from(document.querySelectorAll(
        '[data-email], [data-identifier], ul[role=""listbox""] li, button[data-email], a[data-email]'
    ));
    const hasAccountPicker = accountButtons.length > 0;

    // Detect Google TOTP input
    const totpInputs = Array.from(document.querySelectorAll('input[type=""tel""], input[name*=""otp""], input[id*=""otp""], input[name*=""totpPin""]'));
    const hasGoogleTotpInput = totpInputs.some(isVisible);

    // Detect Google consent buttons
    const googleConsentButtons = Array.from(document.querySelectorAll('button, [role=""button""]')).filter(btn => {
        if (!isVisible(btn)) return false;
        const text = ((btn.innerText || '') + ' ' + (btn.getAttribute('aria-label') || '')).toLowerCase();
        return text.includes('continue') || text.includes('tiếp tục') ||
               text.includes('allow') || text.includes('cho phép');
    });
    const hasGoogleConsentButton = googleConsentButtons.length > 0;

    // Detect AWS Builder ID consent buttons
    const awsConsentButtons = Array.from(document.querySelectorAll('button, [role=""button""]')).filter(btn => {
        if (!isVisible(btn)) return false;
        const text = ((btn.innerText || '') + ' ' + (btn.getAttribute('aria-label') || '')).toLowerCase();
        return text.includes('confirm and continue') || text.includes('confirmer et continuer') ||
               text.includes('allow access') || text.includes('autoriser') ||
               text.includes('approve') || text.includes('accept');
    });
    const hasAwsConsentButton = awsConsentButtons.length > 0;

    return {
        currentUrl: currentUrl,
        isAwsBuilderIdPage: isAwsBuilderIdPage,
        isGoogleOAuthPage: isGoogleOAuthPage,
        isCompletionPage: isCompletionPage,
        hasContinueWithGoogleButton: hasContinueWithGoogleButton,
        hasAccountPicker: hasAccountPicker,
        hasGoogleTotpInput: hasGoogleTotpInput,
        hasGoogleConsentButton: hasGoogleConsentButton,
        hasAwsConsentButton: hasAwsConsentButton
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
            return new AwsBuilderIdOAuthPageState(
                CurrentUrl: value.GetProperty("currentUrl").GetString()!,
                IsAwsBuilderIdPage: value.GetProperty("isAwsBuilderIdPage").GetBoolean(),
                IsGoogleOAuthPage: value.GetProperty("isGoogleOAuthPage").GetBoolean(),
                IsCompletionPage: value.GetProperty("isCompletionPage").GetBoolean(),
                HasContinueWithGoogleButton: value.GetProperty("hasContinueWithGoogleButton").GetBoolean(),
                HasAccountPicker: value.GetProperty("hasAccountPicker").GetBoolean(),
                HasGoogleTotpInput: value.GetProperty("hasGoogleTotpInput").GetBoolean(),
                HasGoogleConsentButton: value.GetProperty("hasGoogleConsentButton").GetBoolean(),
                HasAwsConsentButton: value.GetProperty("hasAwsConsentButton").GetBoolean());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new InvalidOperationException($"Failed to read AWS Builder ID OAuth state: {ex.Message}", ex);
        }
    }

    private async Task<bool> TryClickContinueWithGoogleAsync(CancellationToken cancellationToken)
    {
        const string script = @"
(function() {
    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return rect.width > 0 && rect.height > 0;
    };
    const buttons = Array.from(document.querySelectorAll('button, [role=""button""], a')).filter(btn => {
        if (!isVisible(btn)) return false;
        const text = ((btn.innerText || '') + ' ' + (btn.getAttribute('aria-label') || '')).toLowerCase();
        return text.includes('continue with google') || text.includes('sign in with google') || text.includes('google');
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
            var result = await _client.CallAsync("Runtime.evaluate", new
            {
                expression = script,
                returnByValue = true
            }, cancellationToken, _sessionId);

            if (result.TryGetProperty("result", out var resultProp) &&
                resultProp.TryGetProperty("value", out var clickedProp) &&
                clickedProp.GetBoolean())
            {
                DebugConsole.WriteLine("[AwsBuilderIdOAuth] 'Continue with Google' clicked");
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> TryClickAccountAsync(string targetEmail, CancellationToken cancellationToken)
    {
        var emailJson = JsonSerializer.Serialize(targetEmail);
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
            const dataEmail = (el.getAttribute('data-email') || '').toLowerCase();

            // Track 'scroll down' / 'use another' button
            if (text.includes('cuộn xuống') || text.includes('use another') || text.includes('add account')) {
                if (!scrollButton) scrollButton = el;
                continue;
            }
            if (text.includes('remove') || text.includes('xóa')) continue;
            // Skip AWS SSO identity provider button
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
            DebugConsole.WriteLine($"[AwsBuilderIdOAuth] Attempting to click account: {targetEmail}");
            var result = await _client.CallAsync("Runtime.evaluate", new
            {
                expression = script,
                returnByValue = true
            }, cancellationToken, _sessionId);

            if (result.TryGetProperty("result", out var resultProp) &&
                resultProp.TryGetProperty("value", out var valueProp))
            {
                var clicked = valueProp.GetProperty("clicked").GetBoolean();
                var found = valueProp.GetProperty("found").GetBoolean();
                var totalButtons = valueProp.GetProperty("totalButtons").GetInt32();

                DebugConsole.WriteLine($"[AwsBuilderIdOAuth] Click result - Found: {found}, Clicked: {clicked}, Total buttons: {totalButtons}");

                if (valueProp.TryGetProperty("allButtons", out var allButtonsProp) && allButtonsProp.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var buttons = allButtonsProp.EnumerateArray().Take(5).ToList();
                    DebugConsole.WriteLine($"[AwsBuilderIdOAuth] First {buttons.Count} buttons:");
                    foreach (var btn in buttons)
                    {
                        var text = btn.GetProperty("text").GetString();
                        var visible = btn.GetProperty("visible").GetBoolean();
                        DebugConsole.WriteLine($"  - Visible: {visible}, Text: {text}");
                    }
                }

                if (clicked)
                {
                    DebugConsole.WriteLine($"[AwsBuilderIdOAuth] Account '{targetEmail}' clicked successfully");
                    return true;
                }
                else
                {
                    DebugConsole.WriteLine($"[AwsBuilderIdOAuth] Account '{targetEmail}' NOT clicked - button not found or not visible");
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[AwsBuilderIdOAuth] Click error: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> TryFillTotpAsync(string totpCode, CancellationToken cancellationToken)
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
            var result = await _client.CallAsync("Runtime.evaluate", new
            {
                expression = script,
                returnByValue = true
            }, cancellationToken, _sessionId);

            if (result.TryGetProperty("result", out var resultProp) &&
                resultProp.TryGetProperty("value", out var filledProp) &&
                filledProp.GetBoolean())
            {
                DebugConsole.WriteLine("[AwsBuilderIdOAuth] TOTP filled and submitted");
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> TryClickGoogleConsentButtonAsync(CancellationToken cancellationToken)
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
            var result = await _client.CallAsync("Runtime.evaluate", new
            {
                expression = script,
                returnByValue = true
            }, cancellationToken, _sessionId);

            if (result.TryGetProperty("result", out var resultProp) &&
                resultProp.TryGetProperty("value", out var clickedProp) &&
                clickedProp.GetBoolean())
            {
                DebugConsole.WriteLine("[AwsBuilderIdOAuth] Google consent button clicked");
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> TryClickAwsConsentButtonAsync(CancellationToken cancellationToken)
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
        return text.includes('confirm and continue') || text.includes('confirmer et continuer') ||
               text.includes('allow access') || text.includes('autoriser') ||
               text.includes('approve') || text.includes('accept');
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
            var result = await _client.CallAsync("Runtime.evaluate", new
            {
                expression = script,
                returnByValue = true
            }, cancellationToken, _sessionId);

            if (result.TryGetProperty("result", out var resultProp) &&
                resultProp.TryGetProperty("value", out var clickedProp) &&
                clickedProp.GetBoolean())
            {
                DebugConsole.WriteLine("[AwsBuilderIdOAuth] AWS Builder ID consent button clicked");
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

public sealed record AwsBuilderIdOAuthPageState(
    string CurrentUrl,
    bool IsAwsBuilderIdPage,
    bool IsGoogleOAuthPage,
    bool IsCompletionPage,
    bool HasContinueWithGoogleButton,
    bool HasAccountPicker,
    bool HasGoogleTotpInput,
    bool HasGoogleConsentButton,
    bool HasAwsConsentButton);
