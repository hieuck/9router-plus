using System.Text.Json;
using RouterPlus.Infrastructure.Diagnostics;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Automation for Codex OAuth consent flow.
/// Delegates Google-specific detection to GoogleOAuthPageDetector.
/// </summary>
public sealed class CodexOAuthAutomation : GoogleOAuthFlowAutomation
{
    public CodexOAuthAutomation(
        ChromeCdpClient client,
        string sessionId,
        string targetId,
        string profileEmail)
        : base(client, sessionId, targetId, profileEmail, totpGenerator: null)
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

    const isCodexAddPhonePage = path.includes('/sign-in-with-chatgpt/codex/add-phone');
    const isCodexConsentPage = path.includes('/sign-in-with-chatgpt/codex/consent');

    // Check if on target service (Codex post-auth landing page).
    // The add-phone and consent routes are still part of OAuth, even on chatgpt.com.
    const isTargetService = ((host.includes('chatgpt.com') || host.includes('openai.com')) &&
        host !== 'auth.openai.com' && !isCodexAddPhonePage && !isCodexConsentPage) ||
        (host.startsWith('localhost') && path.includes('/auth/callback') && currentUrl.includes('code='));

    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return el.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
    };
    const buttons = Array.from(document.querySelectorAll('button, a, [role=""button""]')).filter(isVisible);
    const buttonText = btn => ((btn.innerText || '') + ' ' +
        (btn.getAttribute('aria-label') || '') + ' ' +
        (btn.getAttribute('href') || '')).toLowerCase();
    const hasGoogleLoginButton = buttons.some(btn => {
        const text = buttonText(btn);
        return text.includes('continue with google') ||
               text.includes('sign in with google') ||
               text.includes('log in with google') ||
               text.includes('tiếp tục với google') ||
               text.includes('đăng nhập bằng google');
    });
    const hasCodexConsentButton = isCodexConsentPage && buttons.some(btn => {
        const text = buttonText(btn);
        return text.includes('continue') || text.includes('tiếp tục');
    });

    return {
        currentUrl: currentUrl,
        isOpenAIOAuthPage: isOpenAIOAuthPage,
        isTargetService: isTargetService,
        hasGoogleLoginButton: hasGoogleLoginButton,
        hasCodexConsentButton: hasCodexConsentButton
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
                HasCodexConsentButton = value.GetProperty("hasCodexConsentButton").GetBoolean()
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

        // Only click the initial CTA while it is actually present.
        // Add-phone and Codex consent pages are passive/provider-consent states.
        return providerState.IsOpenAIOAuthPage && providerState.HasGoogleLoginButton;
    }

    protected override async Task<bool> TryClickProviderInitialButtonAsync(CombinedOAuthPageState state, CancellationToken cancellationToken)
    {
        var providerState = state.ProviderState as CodexOAuthPageState;
        if (providerState == null || !providerState.IsOpenAIOAuthPage || !providerState.HasGoogleLoginButton)
            return false;

        const string script = @"
(function() {
    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return rect.width > 0 && rect.height > 0;
    };
    const candidates = Array.from(document.querySelectorAll('button, a, [role=""button""]')).filter(btn => {
        if (!isVisible(btn)) return false;
        const text = ((btn.innerText || '') + ' ' + (btn.getAttribute('aria-label') || '') + ' ' + (btn.getAttribute('href') || '')).toLowerCase();
        return text.includes('continue with google') ||
               text.includes('sign in with google') ||
               text.includes('log in with google') ||
               text.includes('tiếp tục với google') ||
               text.includes('đăng nhập bằng google');
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
                resultProp.TryGetProperty("value", out var valueProp))
            {
                var clicked = valueProp.ValueKind == JsonValueKind.True;
                if (clicked)
                {
                    DebugConsole.WriteLine("[CodexOAuth] Clicked 'Continue with Google' button on OpenAI page");
                }
                return clicked;
            }

            return false;
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[CodexOAuth] Click Google login button error: {ex.Message}");
            return false;
        }
    }

    protected override bool ShouldClickProviderConsent(CombinedOAuthPageState state)
    {
        var providerState = state.ProviderState as CodexOAuthPageState;
        return providerState?.HasCodexConsentButton == true;
    }

    protected override async Task<bool> TryClickProviderConsentButtonAsync(
        CombinedOAuthPageState state,
        CancellationToken cancellationToken)
    {
        var providerState = state.ProviderState as CodexOAuthPageState;
        if (providerState?.HasCodexConsentButton != true)
            return false;

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
}
