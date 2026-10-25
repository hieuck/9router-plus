using System.Text.Json;
using RouterPlus.Core.Observability;
using RouterPlus.Infrastructure.Diagnostics;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Automation for Nous OAuth consent flow via Google login.
/// Delegates Google-specific detection to GoogleOAuthPageDetector.
/// </summary>
public sealed class NousOAuthAutomation : GoogleOAuthFlowAutomation
{
    public NousOAuthAutomation(
        ChromeCdpClient client,
        string sessionId,
        string targetId,
        string profileEmail,
        Func<Task<string?>>? totpGenerator = null)
        : base(client, sessionId, targetId, profileEmail, totpGenerator)
    {
    }

    // ========== Override abstract methods ==========

    protected override async Task<ProviderOAuthPageState?> ReadProviderPageStateAsync(CancellationToken cancellationToken)
    {
        const string script = @"
(function() {
    const currentUrl = window.location.href;
    const host = window.location.host;
    const bodyText = document.body?.innerText?.toLowerCase() || '';
    const bodySnippet = (document.body?.innerText || '').substring(0, 300);

    // Check if on Nous portal page
    const isNousPage = host.includes('portal.nousresearch.com');

    // Check for verification/humanity check page (Kasada bot check)
    const isVerificationPage = bodyText.includes('verifying humanity') ||
                               bodyText.includes('failed bot check');

    // Detect visible Google login link/button
    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return el.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
    };

    const googleLoginLinks = Array.from(document.querySelectorAll('a, button, [role=""button""]')).filter(el => {
        if (!isVisible(el)) return false;
        const href = (el.getAttribute('href') || '').toLowerCase();
        const text = (el.innerText || '').trim().toLowerCase();
        return href.includes('provider=googleoauth') || text === 'google';
    });
    const hasGoogleLoginLink = googleLoginLinks.length > 0;

    // Detect connect buttons (interactive elements only, not headings)
    const connectButtons = Array.from(document.querySelectorAll('button, a, [role=""button""]')).filter(btn => {
        if (!isVisible(btn)) return false;
        // Exclude headings
        if (btn.tagName === 'H1' || btn.tagName === 'H2' || btn.tagName === 'H3') return false;
        const text = (btn.innerText || '').trim().toLowerCase();
        return text.includes('connect hermes') ||
               text.includes('connect nous') ||
               text === 'connect' ||
               text.includes('authorize');
    });
    const hasConnectButton = connectButtons.length > 0;

    // Check for completion page
    const isCompletionPage = bodyText.includes('successfully connected') ||
                             bodyText.includes('is now connected') ||
                             bodyText.includes('device is now connected') ||
                             bodyText.includes('connection successful') ||
                             bodyText.includes('hermes is connected') ||
                             bodyText.includes('you may close');

    return {
        currentUrl: currentUrl,
        isNousPage: isNousPage,
        isVerificationPage: isVerificationPage,
        hasGoogleLoginLink: hasGoogleLoginLink,
        hasConnectButton: hasConnectButton,
        isCompletionPage: isCompletionPage,
        bodySnippet: bodySnippet
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
            return new NousOAuthPageState
            {
                CurrentUrl = value.GetProperty("currentUrl").GetString()!,
                IsNousPage = value.GetProperty("isNousPage").GetBoolean(),
                IsVerificationPage = value.GetProperty("isVerificationPage").GetBoolean(),
                HasGoogleLoginLink = value.GetProperty("hasGoogleLoginLink").GetBoolean(),
                HasConnectButton = value.GetProperty("hasConnectButton").GetBoolean(),
                IsCompletionPage = value.GetProperty("isCompletionPage").GetBoolean()
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new InvalidOperationException($"Failed to read Nous OAuth state: {ex.Message}", ex);
        }
    }

    protected override CompletionCheckResult CheckCompletion(CombinedOAuthPageState state)
    {
        var providerState = state.ProviderState as NousOAuthPageState;
        if (providerState == null)
            return new CompletionCheckResult(IsComplete: false);

        if (providerState.IsCompletionPage)
        {
            return new CompletionCheckResult(
                IsComplete: true,
                Result: new OAuthConsentResult(
                    Success: true,
                    AlreadyAuthorized: false,
                    Message: "Nous authorization completed"));
        }

        return new CompletionCheckResult(IsComplete: false);
    }

    protected override void LogPageState(CombinedOAuthPageState state)
    {
        var providerState = state.ProviderState as NousOAuthPageState;
        if (providerState == null)
            return;

        ObservabilityHub.Instance.LogEvent(
            LogLevel.Debug,
            "NousOAuth",
            "PageStateDetailed",
            "Detailed Nous OAuth page state",
            new {
                url = state.CurrentUrl,
                is_nous_page = providerState.IsNousPage,
                is_verification_page = providerState.IsVerificationPage,
                has_google_login_link = providerState.HasGoogleLoginLink,
                has_connect_button = providerState.HasConnectButton,
                is_completion_page = providerState.IsCompletionPage
            });
    }

    // ========== Override virtual methods for Nous-specific behavior ==========

    protected override bool ShouldClickProviderInitialButton(CombinedOAuthPageState state)
    {
        var providerState = state.ProviderState as NousOAuthPageState;
        if (providerState == null)
            return false;

        // Ignore Google's own pages (base flow handles those)
        return !state.IsGoogleOAuthPage &&
               providerState.IsNousPage &&
               providerState.HasGoogleLoginLink &&
               !providerState.HasConnectButton &&
               !providerState.IsVerificationPage;
    }

    protected override async Task<bool> TryClickProviderInitialButtonAsync(CombinedOAuthPageState state, CancellationToken cancellationToken)
    {
        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "NousOAuth",
            "ClickingGoogleLoginLink",
            "Clicking Google login link on Nous portal",
            new { url = state.CurrentUrl });

        const string script = @"
(function() {
    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return rect.width > 0 && rect.height > 0;
    };
    // First try: href contains provider=GoogleOAuth
    let buttons = Array.from(document.querySelectorAll('a, button, [role=""button""]')).filter(btn => {
        if (!isVisible(btn)) return false;
        const href = (btn.getAttribute('href') || '').toLowerCase();
        return href.includes('provider=googleoauth');
    });
    if (buttons.length > 0) {
        buttons[0].click();
        return true;
    }
    // Fallback: text exactly 'google' (case-insensitive)
    buttons = Array.from(document.querySelectorAll('a, button, [role=""button""]')).filter(btn => {
        if (!isVisible(btn)) return false;
        const text = (btn.innerText || '').trim().toLowerCase();
        return text === 'google';
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
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Info,
                    "NousOAuth",
                    "GoogleLoginLinkClicked",
                    "Google login link clicked on Nous portal",
                    new { url = state.CurrentUrl });
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    protected override bool ShouldClickProviderConsent(CombinedOAuthPageState state)
    {
        var providerState = state.ProviderState as NousOAuthPageState;
        if (providerState == null)
            return false;

        // Ignore Google's own pages (base flow handles those)
        return !state.IsGoogleOAuthPage &&
               providerState.IsNousPage &&
               providerState.HasConnectButton;
    }

    protected override async Task<bool> TryClickProviderConsentButtonAsync(CombinedOAuthPageState state, CancellationToken cancellationToken)
    {
        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "NousOAuth",
            "ClickingConnectButton",
            "Clicking Connect button on Nous portal",
            new { url = state.CurrentUrl });

        const string script = @"
(function() {
    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return rect.width > 0 && rect.height > 0;
    };
    const buttons = Array.from(document.querySelectorAll('button, a, [role=""button""]')).filter(btn => {
        if (!isVisible(btn)) return false;
        // Exclude headings
        if (btn.tagName === 'H1' || btn.tagName === 'H2' || btn.tagName === 'H3') return false;
        const text = (btn.innerText || '').trim().toLowerCase();
        return text.includes('connect hermes') ||
               text.includes('connect nous') ||
               text === 'connect' ||
               text.includes('authorize');
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
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Info,
                    "NousOAuth",
                    "ConnectButtonClicked",
                    "Connect button clicked on Nous portal",
                    new { url = state.CurrentUrl });
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
    /// Override account picker for Nous: dedicated retry that polls for the account row to appear,
    /// without the base fallback that can click "use another account".
    /// </summary>
    protected override async Task<bool> TryClickAccountPickerAsync(
        CombinedOAuthPageState state,
        CancellationToken cancellationToken)
    {
        var email = _profileEmail;
        var emailLower = email.ToLowerInvariant();
        var emailJson = JsonSerializer.Serialize(email);
        var emailLowerJson = JsonSerializer.Serialize(emailLower);

        const int maxAttempts = 20;
        const int delayMs = 500;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ObservabilityHub.Instance.LogEvent(
                LogLevel.Debug,
                "NousOAuth",
                "AccountPickerWait",
                $"Waiting for account picker row (attempt {attempt}/{maxAttempts})",
                new { email, attempt, maxAttempts });

            var script = $@"
(function() {{
    const targetEmail = {emailJson};
    const targetEmailLower = {emailLowerJson};
    const isVisible = el => {{
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return rect.width > 0 && rect.height > 0;
    }};

    // 1) Prefer elements with data-identifier or data-email matching the email
    let candidates = Array.from(document.querySelectorAll('[data-identifier], [data-email]'));
    for (const el of candidates) {{
        if (!isVisible(el)) continue;
        const id = (el.getAttribute('data-identifier') || '').toLowerCase();
        const em = (el.getAttribute('data-email') || '').toLowerCase();
        if (id === targetEmailLower || em === targetEmailLower) {{
            // Click a clickable descendant if present, else the element itself
            const clickable = el.querySelector('a, button, [role=\""link\""], [role=\""button\""]) || el;
            clickable.click();
            return true;
        }}
    }}

    // 2) Fallback: role=link or role=button whose innerText includes the email
    candidates = Array.from(document.querySelectorAll('[role=\""link\""], [role=\""button\""]'));
    for (const el of candidates) {{
        if (!isVisible(el)) continue;
        const text = (el.innerText || '').toLowerCase();
        if (text.includes(targetEmailLower)) {{
            el.click();
            return true;
        }}
    }}

    return false;
}})()
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
                    ObservabilityHub.Instance.LogEvent(
                        LogLevel.Info,
                        "NousOAuth",
                        "AccountPickerClicked",
                        "Account picker row clicked",
                        new { email, attempt });
                    return true;
                }
            }
            catch
            {
                // Ignore JS errors, continue polling
            }

            await Task.Delay(delayMs, cancellationToken);
        }

        ObservabilityHub.Instance.LogEvent(
            LogLevel.Warning,
            "NousOAuth",
            "AccountPickerNotFound",
            "Account picker row not found after all attempts",
            new { email, maxAttempts });

        return false;
    }
}

/// <summary>
/// Nous specific page state (provider-specific, non-Google).
/// </summary>
public sealed record NousOAuthPageState : ProviderOAuthPageState
{
    public required bool IsNousPage { get; init; }
    public required bool IsVerificationPage { get; init; }
    public required bool HasGoogleLoginLink { get; init; }
    public required bool HasConnectButton { get; init; }
    public required bool IsCompletionPage { get; init; }
}