using System.Text.Json;
using RouterPlus.Core.Observability;
using RouterPlus.Infrastructure.Diagnostics;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Automation for Codex/OpenAI OAuth consent flow.
/// Delegates Google-specific detection to GoogleOAuthPageDetector.
/// </summary>
public sealed class CodexOAuthAutomation : GoogleOAuthFlowAutomation
{
    internal static bool IsCodexConsentRoute(string host, string path) =>
        string.Equals(host, "auth.openai.com", StringComparison.OrdinalIgnoreCase) &&
        path.StartsWith("/sign-in-with-chatgpt/codex/consent", StringComparison.Ordinal);

    internal static bool IsOpenAIAccountPickerRoute(string host, string path)
    {
        if (!string.Equals(host, "auth.openai.com", StringComparison.OrdinalIgnoreCase))
            return false;

        var route = path.Split('?', '#')[0].TrimEnd('/');
        return string.Equals(route, "/choose-an-account", StringComparison.Ordinal);
    }

    public CodexOAuthAutomation(
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
    const path = window.location.pathname;

    // Check if on OpenAI OAuth page (Codex uses auth.openai.com)
    const isOpenAIOAuthPage = host === 'auth.openai.com' && (
        path.includes('/oauth') ||
        path.includes('/authorize') ||
        path.includes('/login') ||
        path.includes('/log-in') ||
        path.includes('/signin') ||
        path.includes('/choose-an-account') ||
        path.includes('/consent')
    );

    const isCodexAddPhonePage = host === 'auth.openai.com' &&
        path.replace(/\/+$/, '') === '/sign-in-with-chatgpt/codex/add-phone';
    const isCodexConsentPage = host === 'auth.openai.com' &&
        path.replace(/\/+$/, '') === '/sign-in-with-chatgpt/codex/consent';
    const isOpenAIAccountPicker = host === 'auth.openai.com' &&
        path.replace(/\/+$/, '') === '/choose-an-account';

    // Check if on target service (Codex post-auth landing page).
    // The add-phone and consent routes are still part of OAuth on auth.openai.com.
    const isTargetService = ((host.includes('chatgpt.com') || host.includes('openai.com')) &&
        host !== 'auth.openai.com' && !path.includes('/auth/') && !isCodexAddPhonePage && !isCodexConsentPage) ||
        (host.startsWith('localhost') && path.includes('/auth/callback') && currentUrl.includes('code='));

    let hasGoogleLoginButton = false;
    let hasCodexConsentButton = false;
    let hasOpenAIAccountPicker = false;
    if (isOpenAIOAuthPage || isCodexConsentPage) {
        const isVisible = el => {
            if (!el) return false;
            const rect = el.getBoundingClientRect();
            return el.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
        };
        const buttons = Array.from(document.querySelectorAll('button, a, [role=""button""]')).filter(isVisible);
        const buttonText = btn => ((btn.innerText || '') + ' ' +
            (btn.getAttribute('aria-label') || '') + ' ' +
            (btn.getAttribute('href') || '')).toLowerCase();
        hasGoogleLoginButton = buttons.some(btn => {
            const text = buttonText(btn);
            return text.includes('continue with google') ||
                   text.includes('sign in with google') ||
                   text.includes('log in with google') ||
                   text.includes('tiếp tục với google') ||
                   text.includes('đăng nhập bằng google');
        });
        hasCodexConsentButton = isCodexConsentPage && buttons.some(btn => {
            const text = buttonText(btn);
            return text.includes('continue') || text.includes('tiếp tục');
        });

        if (isOpenAIAccountPicker) {
            const accountCandidates = Array.from(document.querySelectorAll(
                '[data-email], [data-identifier], [data-user-email], [data-value*=""@""], ' +
                '[role=""option""], [role=""link""], li, button, a, [role=""button""]'
            )).filter(isVisible);
            hasOpenAIAccountPicker = accountCandidates.some(el => {
                const text = buttonText(el);
                return !text.includes('use another') &&
                       !text.includes('remove') &&
                       !text.includes('delete') &&
                       !text.includes('sign out');
            });
        }
    }

    return {
        currentUrl: currentUrl,
        isOpenAIOAuthPage: isOpenAIOAuthPage,
        isTargetService: isTargetService,
        hasGoogleLoginButton: hasGoogleLoginButton,
        hasCodexConsentButton: hasCodexConsentButton,
        hasOpenAIAccountPicker: hasOpenAIAccountPicker
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
            return new CodexOAuthPageState
            {
                CurrentUrl = value.GetProperty("currentUrl").GetString()!,
                IsOpenAIOAuthPage = value.GetProperty("isOpenAIOAuthPage").GetBoolean(),
                IsTargetService = value.GetProperty("isTargetService").GetBoolean(),
                HasGoogleLoginButton = value.GetProperty("hasGoogleLoginButton").GetBoolean(),
                HasCodexConsentButton = value.GetProperty("hasCodexConsentButton").GetBoolean(),
                HasOpenAIAccountPicker = value.GetProperty("hasOpenAIAccountPicker").GetBoolean()
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new InvalidOperationException($"Failed to read Codex OAuth state: {ex.Message}", ex);
        }
    }

    protected override CompletionCheckResult CheckCompletion(CombinedOAuthPageState state)
    {
        var providerState = state.ProviderState as CodexOAuthPageState;
        if (providerState == null)
            return new CompletionCheckResult(IsComplete: false);

        // Check if already on target service (OAuth completed)
        if (providerState.IsTargetService)
        {
            return new CompletionCheckResult(
                IsComplete: true,
                Result: new OAuthConsentResult(
                    Success: true,
                    AlreadyAuthorized: true,
                    Message: "Already authorized - on target service"));
        }

        return new CompletionCheckResult(IsComplete: false);
    }

    protected override void LogPageState(CombinedOAuthPageState state)
    {
        var providerState = state.ProviderState as CodexOAuthPageState;
        if (providerState == null)
            return;

        ObservabilityHub.Instance.LogEvent(
            LogLevel.Debug,
            "CodexOAuth",
            "PageStateDetailed",
            "Detailed Codex OAuth page state",
            new {
                url = state.CurrentUrl,
                is_google_oauth = state.IsGoogleOAuthPage,
                is_openai_oauth = providerState.IsOpenAIOAuthPage,
                is_target_service = providerState.IsTargetService,
                has_google_login_button = providerState.HasGoogleLoginButton,
                has_openai_account_picker = providerState.HasOpenAIAccountPicker,
                has_codex_consent_button = providerState.HasCodexConsentButton,
                has_google_account_picker = state.HasAccountPicker,
                has_google_totp = state.HasGoogleTotpInput,
                has_google_consent = state.HasGoogleConsentButton
            });
    }

    // ========== Override virtual methods for Codex-specific behavior ==========

    protected override bool ShouldClickProviderInitialButton(CombinedOAuthPageState state)
    {
        var providerState = state.ProviderState as CodexOAuthPageState;
        if (providerState == null)
            return false;

        // Click "Log in" button on authorize page, OR "Continue with Google" button on login page
        // BUT NOT on consent page (consent button is handled separately)
        return !state.IsGoogleOAuthPage &&
               providerState.IsOpenAIOAuthPage &&
               !providerState.HasOpenAIAccountPicker &&
               !providerState.HasCodexConsentButton &&
               !state.HasAccountPicker; // Try clicking if no account pickers visible
    }

    protected override bool ShouldClickProviderAccountPicker(CombinedOAuthPageState state)
    {
        var providerState = state.ProviderState as CodexOAuthPageState;
        return !state.IsGoogleOAuthPage &&
               providerState?.IsOpenAIOAuthPage == true &&
               providerState.HasOpenAIAccountPicker;
    }

    protected override async Task<bool> TryClickProviderAccountPickerAsync(
        CombinedOAuthPageState state,
        CancellationToken cancellationToken)
    {
        var providerState = state.ProviderState as CodexOAuthPageState;
        if (state.IsGoogleOAuthPage || providerState?.IsOpenAIOAuthPage != true ||
            !providerState.HasOpenAIAccountPicker)
        {
            return false;
        }

        var emailJson = JsonSerializer.Serialize(_profileEmail);
        var script = @"
(function() {
    const targetEmail = " + emailJson + @".toLowerCase();
    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return el.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
    };
    const candidates = Array.from(document.querySelectorAll(
        '[data-email], [data-identifier], [role=""option""], button, a, [role=""button""]'
    )).filter(isVisible);
    for (const candidate of candidates) {
        const text = ((candidate.innerText || candidate.textContent || '') + ' ' +
            (candidate.getAttribute('aria-label') || '') + ' ' +
            (candidate.getAttribute('data-email') || '') + ' ' +
            (candidate.getAttribute('data-identifier') || '')).toLowerCase();
        if (text.includes('use another') || text.includes('remove') ||
            text.includes('delete') || text.includes('sign out')) continue;
        if (!text.includes(targetEmail)) continue;
        const clickable = candidate.closest('button, a, [role=""button""], [role=""option""]') || candidate;
        clickable.click();
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
                resultProp.TryGetProperty("value", out var valueProp) &&
                valueProp.ValueKind == JsonValueKind.True)
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Info,
                    "CodexOAuth",
                    "OpenAIAccountClicked",
                    "OpenAI account clicked successfully",
                    new { email = _profileEmail });
                return true;
            }
        }
        catch (Exception ex)
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Error,
                "CodexOAuth",
                "ClickAccountError",
                "Click OpenAI account error",
                new { email = _profileEmail, error = ex.Message });
        }

        return false;
    }

    protected override async Task<bool> TryClickProviderInitialButtonAsync(CombinedOAuthPageState state, CancellationToken cancellationToken)
    {
        var providerState = state.ProviderState as CodexOAuthPageState;
        if (state.IsGoogleOAuthPage || providerState == null ||
            !providerState.IsOpenAIOAuthPage || providerState.HasOpenAIAccountPicker)
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Debug,
                "OAuth",
                "CodexButtonSkipped",
                "TryClickProviderInitialButton skipped",
                new {
                    is_google_oauth = state.IsGoogleOAuthPage,
                    is_openai_oauth = providerState?.IsOpenAIOAuthPage,
                    has_openai_picker = providerState?.HasOpenAIAccountPicker
                });
            return false;
        }

        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "OAuth",
            "CodexButtonAttempt",
            "TryClickProviderInitialButton executing",
            new { url = state.CurrentUrl });

        const string script = @"
(function() {
    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return rect.width > 0 && rect.height > 0;
    };
    const buttons = Array.from(document.querySelectorAll('button, a, [role=""button""]')).filter(isVisible);

    const result = {
        buttonCount: buttons.length,
        buttonTexts: []
    };

    let googleButton = buttons.find(btn => {
        const text = ((btn.innerText || '') + ' ' + (btn.getAttribute('aria-label') || '') + ' ' + (btn.getAttribute('href') || '')).toLowerCase();
        return text.includes('continue with google') || text.includes('sign in with google') || text.includes('log in with google') || text.includes('tiếp tục với google') || text.includes('đăng nhập bằng google');
    });
    if (googleButton) {
        googleButton.click();
        return 'google';
    }

    let loginButton = buttons.find(btn => {
        const text = ((btn.innerText || '') + ' ' + (btn.getAttribute('aria-label') || '')).toLowerCase();
        return text.includes('log in') || text.includes('sign in') || text.includes('đăng nhập');
    });
    if (loginButton) {
        loginButton.click();
        return 'login';
    }

    // Collect button texts for debugging
    buttons.forEach(btn => {
        const text = ((btn.innerText || '') + ' ' + (btn.getAttribute('aria-label') || '')).trim();
        if (text) result.buttonTexts.push(text);
    });
    result.clicked = false;
    return result;
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
                if (valueProp.ValueKind == JsonValueKind.String)
                {
                    var clickedType = valueProp.GetString();
                    if (clickedType == "google")
                    {
                        ObservabilityHub.Instance.LogEvent(
                            LogLevel.Info,
                            "OAuth",
                            "CodexGoogleButtonClicked",
                            "Clicked 'Continue with Google' button on OpenAI page",
                            new { url = state.CurrentUrl });
                        return true;
                    }
                    if (clickedType == "login")
                    {
                        ObservabilityHub.Instance.LogEvent(
                            LogLevel.Info,
                            "OAuth",
                            "CodexLoginButtonClicked",
                            "Clicked 'Log in' button on authorize page",
                            new { url = state.CurrentUrl });
                        return true;
                    }
                }
                else if (valueProp.ValueKind == JsonValueKind.Object)
                {
                    // Script returned debug object with button list
                    var buttonCount = valueProp.TryGetProperty("buttonCount", out var countProp) ? countProp.GetInt32() : 0;
                    var buttonTexts = new List<string>();
                    if (valueProp.TryGetProperty("buttonTexts", out var textsProp) && textsProp.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var textEl in textsProp.EnumerateArray())
                        {
                            if (textEl.ValueKind == JsonValueKind.String)
                                buttonTexts.Add(textEl.GetString() ?? "");
                        }
                    }

                    ObservabilityHub.Instance.LogEvent(
                        LogLevel.Warning,
                        "OAuth",
                        "CodexButtonNotFound",
                        "No matching button found",
                        new {
                            url = state.CurrentUrl,
                            button_count = buttonCount,
                            button_texts = buttonTexts
                        });
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Error,
                "OAuth",
                "CodexButtonError",
                "Click initial button error",
                new { url = state.CurrentUrl, error = ex.Message });
            return false;
        }
    }

    protected override bool ShouldClickProviderConsent(CombinedOAuthPageState state)
    {
        var providerState = state.ProviderState as CodexOAuthPageState;
        return !state.IsGoogleOAuthPage &&
               providerState?.IsOpenAIOAuthPage == true &&
               providerState.HasCodexConsentButton;
    }

    protected override async Task<bool> TryClickProviderConsentButtonAsync(
        CombinedOAuthPageState state,
        CancellationToken cancellationToken)
    {
        var providerState = state.ProviderState as CodexOAuthPageState;
        if (state.IsGoogleOAuthPage || providerState?.IsOpenAIOAuthPage != true ||
            !providerState.HasCodexConsentButton)
        {
            return false;
        }


        const string script = @"
(function() {
    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return el.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
    };
    const candidates = Array.from(document.querySelectorAll('button, a, [role=""button""]')).filter(btn => {
        if (!isVisible(btn)) return false;
        const text = ((btn.innerText || '') + ' ' + (btn.getAttribute('aria-label') || '')).toLowerCase();
        return text.includes('continue') || text.includes('tiếp tục');
    });
    if (candidates.length === 0) return false;
    candidates[0].click();
    return true;
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
                resultProp.TryGetProperty("value", out var valueProp) &&
                valueProp.ValueKind == JsonValueKind.True)
            {
                ObservabilityHub.Instance.LogEvent(
                    LogLevel.Info,
                    "CodexOAuth",
                    "ConsentButtonClicked",
                    "Clicked Codex consent Continue button",
                    new { url = state.CurrentUrl });
                return true;
            }
        }
        catch (Exception ex)
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Error,
                "CodexOAuth",
                "ConsentButtonError",
                "Click consent button error",
                new { url = state.CurrentUrl, error = ex.Message });
        }

        return false;
    }
}

/// <summary>
/// Codex/OpenAI specific page state (provider-specific, non-Google).
/// </summary>
public sealed record CodexOAuthPageState : ProviderOAuthPageState
{
    public required bool IsOpenAIOAuthPage { get; init; }
    public required bool IsTargetService { get; init; }
    public required bool HasGoogleLoginButton { get; init; }
    public required bool HasCodexConsentButton { get; init; }
    public required bool HasOpenAIAccountPicker { get; init; }
}
