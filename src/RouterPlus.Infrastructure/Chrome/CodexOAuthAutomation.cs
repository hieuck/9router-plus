using System.Text.Json;
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

        DebugConsole.WriteLine($"[CodexOAuth] URL: {state.CurrentUrl}");
        DebugConsole.WriteLine($"[CodexOAuth] IsGoogleOAuth: {state.IsGoogleOAuthPage}");
        DebugConsole.WriteLine($"[CodexOAuth] IsOpenAIOAuth: {providerState.IsOpenAIOAuthPage}");
        DebugConsole.WriteLine($"[CodexOAuth] IsTargetService: {providerState.IsTargetService}");
        DebugConsole.WriteLine($"[CodexOAuth] HasGoogleLoginButton: {providerState.HasGoogleLoginButton}");
        DebugConsole.WriteLine($"[CodexOAuth] HasOpenAIAccountPicker: {providerState.HasOpenAIAccountPicker}");
        DebugConsole.WriteLine($"[CodexOAuth] HasCodexConsentButton: {providerState.HasCodexConsentButton}");
        DebugConsole.WriteLine($"[CodexOAuth] HasAccountPicker: {state.HasAccountPicker}");
        DebugConsole.WriteLine($"[CodexOAuth] HasGoogleTotpInput: {state.HasGoogleTotpInput}");
        DebugConsole.WriteLine($"[CodexOAuth] HasGoogleConsentButton: {state.HasGoogleConsentButton}");
    }

    // ========== Override virtual methods for Codex-specific behavior ==========

    protected override bool ShouldClickProviderInitialButton(CombinedOAuthPageState state)
    {
        var providerState = state.ProviderState as CodexOAuthPageState;
        if (providerState == null)
            return false;

        // Click "Log in" button on authorize page, OR "Continue with Google" button on login page
        return !state.IsGoogleOAuthPage &&
               providerState.IsOpenAIOAuthPage &&
               !providerState.HasOpenAIAccountPicker &&
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
                DebugConsole.WriteLine($"[CodexOAuth] OpenAI account '{_profileEmail}' clicked successfully");
                return true;
            }
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[CodexOAuth] Click OpenAI account error: {ex.Message}");
        }

        return false;
    }

    protected override async Task<bool> TryClickProviderInitialButtonAsync(CombinedOAuthPageState state, CancellationToken cancellationToken)
    {
        var providerState = state.ProviderState as CodexOAuthPageState;
        if (state.IsGoogleOAuthPage || providerState == null ||
            !providerState.IsOpenAIOAuthPage || providerState.HasOpenAIAccountPicker)
            return false;

        const string script = @"
(function() {
    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return rect.width > 0 && rect.height > 0;
    };
    const buttons = Array.from(document.querySelectorAll('button, a, [role=""button""]')).filter(isVisible);

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
                resultProp.TryGetProperty("value", out var valueProp))
            {
                if (valueProp.ValueKind == JsonValueKind.String)
                {
                    var clickedType = valueProp.GetString();
                    if (clickedType == "google")
                    {
                        DebugConsole.WriteLine("[CodexOAuth] Clicked 'Continue with Google' button on OpenAI page");
                        return true;
                    }
                    if (clickedType == "login")
                    {
                        DebugConsole.WriteLine("[CodexOAuth] Clicked 'Log in' button on authorize page");
                        return true;
                    }
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[CodexOAuth] Click initial button error: {ex.Message}");
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
                DebugConsole.WriteLine("[CodexOAuth] Clicked Codex consent Continue button");
                return true;
            }
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[CodexOAuth] Click consent button error: {ex.Message}");
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
