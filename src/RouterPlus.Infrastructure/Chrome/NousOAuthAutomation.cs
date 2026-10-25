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
               text.includes('connect as') ||
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

        const string finderScript = @"
    // Find the connect button (same filters as ReadProviderPageStateAsync)
    const buttons = Array.from(document.querySelectorAll('button, a, [role=""button""]')).filter(btn => {
        if (!isVisible(btn)) return false;
        if (btn.tagName === 'H1' || btn.tagName === 'H2' || btn.tagName === 'H3') return false;
        const text = (btn.innerText || '').trim().toLowerCase();
        return text.includes('connect hermes') ||
               text.includes('connect nous') ||
               text.includes('connect as') ||
               text === 'connect' ||
               text.includes('authorize');
    });
    if (buttons.length > 0) {
        const btn = buttons[0];
        btn.scrollIntoView({ block: 'center' });
        const rect = btn.getBoundingClientRect();
        return { found: true, x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 };
    }
";

        return await TryTrustedClickAsync(
            finderScript,
            "ConnectButtonClicked",
            "Connect button clicked on Nous portal (trusted click)",
            cancellationToken);
    }

    /// <summary>
    /// Override to only click account picker on actual accountchooser pages.
    /// Prevents clicking "Developer info" or other links on consent pages.
    /// </summary>
    protected override bool ShouldClickAccountPicker(CombinedOAuthPageState state) =>
        state.CurrentUrl.Contains("accountchooser", StringComparison.OrdinalIgnoreCase)
        && base.ShouldClickAccountPicker(state);

    /// <summary>
    /// Checks if the exact account row for the profile email exists and is visible.
    /// </summary>
    private async Task<bool> HasExactAccountRowAsync(string email, CancellationToken ct)
    {
        var emailJson = System.Text.Json.JsonSerializer.Serialize(email.ToLowerInvariant());
        var script = $@"
(function() {{
    const targetEmail = {emailJson};
    const rows = Array.from(document.querySelectorAll('[data-identifier], [data-email]'));
    for (const row of rows) {{
        const identifier = (row.getAttribute('data-identifier') || '').toLowerCase();
        const emailAttr = (row.getAttribute('data-email') || '').toLowerCase();
        if (identifier === targetEmail || emailAttr === targetEmail) {{
            const rect = row.getBoundingClientRect();
            if (rect.width > 0 && rect.height > 0) return true;
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
            }, ct, _sessionId);

            if (result.TryGetProperty("result", out var resultProp) &&
                resultProp.TryGetProperty("value", out var valueProp) &&
                valueProp.ValueKind == System.Text.Json.JsonValueKind.True)
            {
                return true;
            }
        }
        catch
        {
            // Ignore errors, treat as not found
        }
        return false;
    }

    /// <summary>
    /// Override account picker for Nous: dedicated retry that polls for the account row to appear,
    /// using the shared Google detector (base.TryClickAccountPickerAsync) with JS click and exact data-identifier.
    /// </summary>
    protected override async Task<bool> TryClickAccountPickerAsync(
        CombinedOAuthPageState state,
        CancellationToken cancellationToken)
    {
        var email = _profileEmail;

        const int maxAttempts = 120;
        const int delayMs = 500;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Log only every 10th attempt (1, 10, 20, ..., 120) to avoid log spam
            if (attempt == 1 || attempt % 10 == 0)
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Debug,
                    "NousOAuth",
                    "AccountPickerWait",
                    $"Waiting for account picker row (attempt {attempt}/{maxAttempts})",
                    new { email, attempt, maxAttempts });
            }

            // Pre-check: exact account row must exist and be visible before calling base detector.
            // This prevents the shared detector's fallback from clicking "Developer info" or other links.
            var hasExactRow = await HasExactAccountRowAsync(email, cancellationToken);
            if (!hasExactRow)
            {
                await Task.Delay(delayMs, cancellationToken);
                continue;
            }

            var clicked = await base.TryClickAccountPickerAsync(state, cancellationToken);
            if (clicked)
            {
                // Wait briefly and verify navigation away from accountchooser
                await Task.Delay(1000, cancellationToken);
                var pathname = await GetPathnameAsync(cancellationToken);

                if (pathname.Contains("accountchooser", StringComparison.OrdinalIgnoreCase))
                {
                    ObservabilityHub.Instance.LogEvent(
                        LogLevel.Debug,
                        "NousOAuth",
                        "AccountPickerClickDidNotNavigate",
                        "Account picker click did not navigate away from accountchooser, continuing retry",
                        new { email, attempt, pathname });
                }
                else
                {
                    ObservabilityHub.Instance.LogEvent(
                        LogLevel.Info,
                        "NousOAuth",
                        "AccountPickerClicked",
                        "Account picker row clicked and navigated",
                        new { email, attempt, pathname });
                    return true;
                }
            }

            await Task.Delay(delayMs, cancellationToken);
        }

        ObservabilityHub.Instance.LogEvent(
            LogLevel.Warning,
            "NousOAuth",
            "AccountPickerManualFallback",
            "Account picker row not found after all attempts; user will complete manually, keeping automation alive",
            new { email, maxAttempts });

        return true;
    }

    /// <summary>
    /// Gets the current page pathname via CDP Runtime.evaluate.
    /// </summary>
    private async Task<string> GetPathnameAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _client.CallAsync("Runtime.evaluate", new
            {
                expression = "location.pathname",
                returnByValue = true
            }, cancellationToken, _sessionId);

            if (result.TryGetProperty("result", out var rp) &&
                rp.TryGetProperty("value", out var vp) &&
                vp.ValueKind == System.Text.Json.JsonValueKind.String)
            {
                return vp.GetString() ?? "";
            }
        }
        catch
        {
            // Ignore errors, return empty
        }
        return "";
    }

    /// <summary>
    /// Trusted click via CDP Input.dispatchMouseEvent at element center.
    /// Returns true if element found and click dispatched.
    /// </summary>
    private async Task<bool> TryTrustedClickAsync(
        string jsFinderScript,
        string eventName,
        string logMessage,
        CancellationToken cancellationToken)
    {
        // Step 1: Find element and get its center coordinates
        string findScript = @"
(function() {
    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return rect.width > 0 && rect.height > 0;
    };
" + jsFinderScript + @"
    return false;
})()
";

        try
        {
            var result = await _client.CallAsync("Runtime.evaluate", new
            {
                expression = findScript,
                returnByValue = true
            }, cancellationToken, _sessionId);

            if (!result.TryGetProperty("result", out var resultProp) ||
                !resultProp.TryGetProperty("value", out var valueProp))
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Debug,
                    "NousOAuth",
                    "TrustedClickNotFound",
                    "Trusted click element not found",
                    new { eventName, valueKind = "null" });
                return false;
            }

            // Check for exceptionDetails
            if (resultProp.TryGetProperty("exceptionDetails", out var exceptionDetails))
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Warning,
                    "NousOAuth",
                    "TrustedClickScriptFailed",
                    "Trusted click script failed with exception",
                    new { eventName, exception = exceptionDetails.ToString() });
                return false;
            }

            // Expected return: { found: true, x: number, y: number } or false
            if (valueProp.ValueKind == System.Text.Json.JsonValueKind.False)
            {
                return false;
            }

            if (valueProp.TryGetProperty("found", out var foundProp) && foundProp.GetBoolean() &&
                valueProp.TryGetProperty("x", out var xProp) &&
                valueProp.TryGetProperty("y", out var yProp))
            {
                double x = xProp.GetDouble();
                double y = yProp.GetDouble();

                // Step 2: Dispatch trusted mouse events via CDP with proper button states
                // mouseMoved: buttons = 0
                await _client.CallAsync("Input.dispatchMouseEvent", new
                {
                    type = "mouseMoved",
                    x,
                    y,
                    button = "none",
                    buttons = 0
                }, cancellationToken, _sessionId);

                // mousePressed: buttons = 1
                await _client.CallAsync("Input.dispatchMouseEvent", new
                {
                    type = "mousePressed",
                    x,
                    y,
                    button = "left",
                    buttons = 1,
                    clickCount = 1
                }, cancellationToken, _sessionId);

                // mouseReleased: buttons = 0
                await _client.CallAsync("Input.dispatchMouseEvent", new
                {
                    type = "mouseReleased",
                    x,
                    y,
                    button = "left",
                    buttons = 0,
                    clickCount = 1
                }, cancellationToken, _sessionId);

                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Info,
                    "NousOAuth",
                    eventName,
                    logMessage,
                    new { x, y });

                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Warning,
                "NousOAuth",
                "TrustedClickFailed",
                "CDP trusted click failed",
                new { eventName, error = ex.Message });
            return false;
        }
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