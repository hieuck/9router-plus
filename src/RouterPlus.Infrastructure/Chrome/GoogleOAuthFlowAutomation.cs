using System.Text.Json;
using RouterPlus.Infrastructure.Diagnostics;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Base class for Google OAuth consent flow automation.
/// Provides shared logic for account picker, TOTP, and Google consent.
/// Subclasses implement provider-specific hooks.
/// </summary>
public abstract class GoogleOAuthFlowAutomation
{
    protected readonly ChromeCdpClient _client;
    protected readonly string _sessionId;
    protected readonly string _targetId;
    protected readonly string _profileEmail;
    protected readonly Func<Task<string?>>? _totpGenerator;

    protected GoogleOAuthFlowAutomation(
        ChromeCdpClient client,
        string sessionId,
        string targetId,
        string profileEmail,
        Func<Task<string?>>? totpGenerator = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _sessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
        _targetId = targetId ?? throw new ArgumentNullException(nameof(targetId));
        _profileEmail = profileEmail ?? throw new ArgumentNullException(nameof(profileEmail));
        _totpGenerator = totpGenerator;
    }

    /// <summary>
    /// Main OAuth consent flow. Calls provider-specific hooks.
    /// </summary>
    public async Task<OAuthConsentResult> WaitAndConsentAsync(
        Uri startUri,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(startUri);

        var deadline = DateTimeOffset.UtcNow + timeout;
        var clickedScreenUrls = new HashSet<string>(StringComparer.Ordinal);
        var totpAttempted = false;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var state = await ReadPageStateAsync(cancellationToken);

            LogPageState(state);

            // Check completion (provider-specific)
            var completionCheck = CheckCompletion(state);
            if (completionCheck.IsComplete)
            {
                return completionCheck.Result!;
            }

            // Handle provider-specific initial button (e.g., "Continue with Google" on AWS)
            if (ShouldClickProviderInitialButton(state))
            {
                var screenKey = $"provider-initial:{state.CurrentUrl}";
                if (!clickedScreenUrls.Add(screenKey))
                {
                    await Task.Delay(500, cancellationToken);
                    continue;
                }

                var clicked = await TryClickProviderInitialButtonAsync(state, cancellationToken);
                if (clicked)
                {
                    await Task.Delay(1500, cancellationToken);
                    continue;
                }

                clickedScreenUrls.Remove(screenKey);
                return new OAuthConsentResult(
                    Success: false,
                    AlreadyAuthorized: false,
                    Message: "Could not click provider initial button");
            }

            // Handle Google account picker
            if (ShouldClickAccountPicker(state))
            {
                var screenKey = $"picker:{state.CurrentUrl}";
                if (!clickedScreenUrls.Add(screenKey))
                {
                    await Task.Delay(500, cancellationToken);
                    continue;
                }

                DebugConsole.WriteLine($"[GoogleOAuth] Clicking account matching '{_profileEmail}'...");
                var accountClicked = await TryClickAccountAsync(state, cancellationToken);
                if (accountClicked)
                {
                    await Task.Delay(1500, cancellationToken);
                    continue;
                }

                clickedScreenUrls.Remove(screenKey);
                return new OAuthConsentResult(
                    Success: false,
                    AlreadyAuthorized: false,
                    Message: $"Could not select account '{_profileEmail}' from picker");
            }

            // Handle Google TOTP
            if (ShouldFillTotp(state) && !totpAttempted)
            {
                totpAttempted = true;

                if (_totpGenerator is not null)
                {
                    var totpCode = await _totpGenerator();
                    if (!string.IsNullOrWhiteSpace(totpCode))
                    {
                        DebugConsole.WriteLine("[GoogleOAuth] Auto-filling TOTP code...");
                        var filled = await TryFillTotpAsync(totpCode, cancellationToken);
                        if (filled)
                        {
                            await Task.Delay(2000, cancellationToken);
                            continue;
                        }
                    }
                }

                DebugConsole.WriteLine("[GoogleOAuth] Waiting for manual TOTP entry...");
                await Task.Delay(1000, cancellationToken);
                continue;
            }

            // Handle Google consent button
            if (ShouldClickGoogleConsent(state))
            {
                var screenKey = $"google-consent:{state.CurrentUrl}";
                if (!clickedScreenUrls.Add(screenKey))
                {
                    await Task.Delay(500, cancellationToken);
                    continue;
                }

                DebugConsole.WriteLine("[GoogleOAuth] Clicking Google consent button...");
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

            // Handle provider-specific consent (e.g., AWS Builder ID consent)
            if (ShouldClickProviderConsent(state))
            {
                var screenKey = $"provider-consent:{state.CurrentUrl}";
                if (!clickedScreenUrls.Add(screenKey))
                {
                    await Task.Delay(500, cancellationToken);
                    continue;
                }

                var clicked = await TryClickProviderConsentButtonAsync(state, cancellationToken);
                if (clicked)
                {
                    await Task.Delay(1500, cancellationToken);
                    continue;
                }

                clickedScreenUrls.Remove(screenKey);
                return new OAuthConsentResult(
                    Success: false,
                    AlreadyAuthorized: false,
                    Message: "Could not click provider consent button");
            }

            await Task.Delay(500, cancellationToken);
        }

        return new OAuthConsentResult(
            Success: false,
            AlreadyAuthorized: false,
            Message: "Timeout waiting for OAuth consent flow");
    }

    // ========== Abstract methods (must override) ==========

    /// <summary>
    /// Read current page state (provider-specific detection).
    /// </summary>
    protected abstract Task<GoogleOAuthPageState> ReadPageStateAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Check if OAuth flow completed.
    /// </summary>
    protected abstract CompletionCheckResult CheckCompletion(GoogleOAuthPageState state);

    /// <summary>
    /// Log page state for debugging.
    /// </summary>
    protected abstract void LogPageState(GoogleOAuthPageState state);

    // ========== Virtual methods (can override) ==========

    /// <summary>
    /// Should click provider-specific initial button (e.g., "Continue with Google" on AWS)?
    /// </summary>
    protected virtual bool ShouldClickProviderInitialButton(GoogleOAuthPageState state) => false;

    /// <summary>
    /// Click provider-specific initial button.
    /// </summary>
    protected virtual Task<bool> TryClickProviderInitialButtonAsync(GoogleOAuthPageState state, CancellationToken cancellationToken)
        => Task.FromResult(false);

    /// <summary>
    /// Should click account picker?
    /// </summary>
    protected virtual bool ShouldClickAccountPicker(GoogleOAuthPageState state)
        => state.IsGoogleOAuthPage && state.HasAccountPicker && !state.HasGoogleConsentButton;

    /// <summary>
    /// Should fill TOTP?
    /// </summary>
    protected virtual bool ShouldFillTotp(GoogleOAuthPageState state)
        => state.IsGoogleOAuthPage && state.HasGoogleTotpInput;

    /// <summary>
    /// Should click Google consent button?
    /// </summary>
    protected virtual bool ShouldClickGoogleConsent(GoogleOAuthPageState state)
        => state.IsGoogleOAuthPage && state.HasGoogleConsentButton;

    /// <summary>
    /// Should click provider-specific consent?
    /// </summary>
    protected virtual bool ShouldClickProviderConsent(GoogleOAuthPageState state) => false;

    /// <summary>
    /// Click provider-specific consent button.
    /// </summary>
    protected virtual Task<bool> TryClickProviderConsentButtonAsync(GoogleOAuthPageState state, CancellationToken cancellationToken)
        => Task.FromResult(false);

    // ========== Shared implementations ==========

    /// <summary>
    /// Click Google account matching profileEmail.
    /// Subclasses can override for custom account picker logic.
    /// </summary>
    protected virtual async Task<bool> TryClickAccountAsync(GoogleOAuthPageState state, CancellationToken cancellationToken)
    {
        var emailJson = JsonSerializer.Serialize(_profileEmail);
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
            var result = await _client.CallAsync("Runtime.evaluate", new
            {
                expression = script,
                returnByValue = true
            }, cancellationToken, _sessionId);

            if (result.TryGetProperty("result", out var resultProp) &&
                resultProp.TryGetProperty("value", out var valueProp))
            {
                var clicked = valueProp.GetProperty("clicked").GetBoolean();
                if (clicked)
                {
                    DebugConsole.WriteLine($"[GoogleOAuth] Account '{_profileEmail}' clicked successfully");
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[GoogleOAuth] Click account error: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Fill TOTP code into Google 2FA input.
    /// </summary>
    protected virtual async Task<bool> TryFillTotpAsync(string totpCode, CancellationToken cancellationToken)
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
                DebugConsole.WriteLine("[GoogleOAuth] TOTP filled and submitted");
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
    /// Click Google consent button (Continue/Allow).
    /// </summary>
    protected virtual async Task<bool> TryClickGoogleConsentButtonAsync(CancellationToken cancellationToken)
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
                DebugConsole.WriteLine("[GoogleOAuth] Google consent button clicked");
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
/// Base state for Google OAuth page detection.
/// Subclasses extend with provider-specific fields.
/// </summary>
public abstract record GoogleOAuthPageState
{
    public required string CurrentUrl { get; init; }
    public required bool IsGoogleOAuthPage { get; init; }
    public required bool HasAccountPicker { get; init; }
    public required bool HasGoogleTotpInput { get; init; }
    public required bool HasGoogleConsentButton { get; init; }
}

/// <summary>
/// Result of completion check.
/// </summary>
public record CompletionCheckResult(
    bool IsComplete,
    OAuthConsentResult? Result = null);
