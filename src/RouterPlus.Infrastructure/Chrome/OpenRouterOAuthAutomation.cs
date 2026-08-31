using System.Text.Json;
using RouterPlus.Infrastructure.Diagnostics;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Automation for OpenRouter OAuth consent flow.
/// Delegates Google-specific detection to GoogleOAuthPageDetector.
/// </summary>
public sealed class OpenRouterOAuthAutomation : GoogleOAuthFlowAutomation
{
    public OpenRouterOAuthAutomation(
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

    // Check if on OpenRouter OAuth page
    const isOpenRouterOAuthPage = host === 'openrouter.ai' && (
        path.includes('/auth') ||
        path.includes('/oauth') ||
        path.includes('/login')
    );

    // Check if on target service (OpenRouter post-auth landing page)
    const isTargetService = host === 'openrouter.ai' &&
        !path.includes('/auth') &&
        !path.includes('/oauth') &&
        !path.includes('/login');

    // Detect OpenRouter sign-in-with-Google. Clerk renders it as an icon-only button
    // (no innerText) whose inner span carries the Sign-in-with-Google aria label.
    const clerkGoogleSpan = document.querySelector('[aria-label=""Sign in with Google""]');
    const visibleAndVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return el.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
    };
    const loginButtonCandidates = Array.from(document.querySelectorAll('button, a, [role=""button""]')).filter(btn => {
        if (!visibleAndVisible(btn)) return false;
        const text = ((btn.innerText || '') + ' ' + (btn.getAttribute('aria-label') || '') + ' ' + (btn.getAttribute('href') || '')).toLowerCase();
        return text.includes('continue with google') ||
               text.includes('sign in with google') ||
               text.includes('log in with google') ||
               text.includes('tiếp tục với google') ||
               text.includes('đăng nhập bằng google');
    });
    const hasGoogleLoginButton = !!clerkGoogleSpan || loginButtonCandidates.length > 0;

    // Detect OpenRouter terms-of-service consent button (post-OAuth Agree & Continue step).
    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return el.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
    };
    const termsCandidates = Array.from(document.querySelectorAll('button, [role=""button""]')).filter(btn => {
        if (!isVisible(btn)) return false;
        const text = ((btn.innerText || '') + ' ' + (btn.getAttribute('aria-label') || '')).toLowerCase();
        return text.includes('agree & continue') ||
               text.includes('agree and continue') ||
               text.includes('accept terms') ||
               text.includes('i agree to the terms') ||
               text.includes('đồng ý & tiếp tục') ||
               text.includes('chấp nhận điều khoản');
    });
    const hasTermsConsentButton = termsCandidates.length > 0;

    return {
        currentUrl: currentUrl,
        isOpenRouterOAuthPage: isOpenRouterOAuthPage,
        isTargetService: isTargetService,
        hasGoogleLoginButton: hasGoogleLoginButton,
        hasTermsConsentButton: hasTermsConsentButton
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
            return new OpenRouterOAuthPageState
            {
                CurrentUrl = value.GetProperty("currentUrl").GetString()!,
                IsOpenRouterOAuthPage = value.GetProperty("isOpenRouterOAuthPage").GetBoolean(),
                IsTargetService = value.GetProperty("isTargetService").GetBoolean(),
                HasGoogleLoginButton = value.GetProperty("hasGoogleLoginButton").GetBoolean(),
                HasTermsConsentButton = value.GetProperty("hasTermsConsentButton").GetBoolean()
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new InvalidOperationException($"Failed to read OpenRouter OAuth state: {ex.Message}", ex);
        }
    }

    protected override CompletionCheckResult CheckCompletion(CombinedOAuthPageState state)
    {
        var providerState = state.ProviderState as OpenRouterOAuthPageState;
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
                    Message: "Already authorized - on OpenRouter"));
        }

        return new CompletionCheckResult(IsComplete: false);
    }

    protected override void LogPageState(CombinedOAuthPageState state)
    {
        var providerState = state.ProviderState as OpenRouterOAuthPageState;
        if (providerState == null)
            return;

        DebugConsole.WriteLine($"[OpenRouterOAuth] URL: {state.CurrentUrl}");
        DebugConsole.WriteLine($"[OpenRouterOAuth] IsGoogleOAuth: {state.IsGoogleOAuthPage}");
        DebugConsole.WriteLine($"[OpenRouterOAuth] IsOpenRouterOAuth: {providerState.IsOpenRouterOAuthPage}");
        DebugConsole.WriteLine($"[OpenRouterOAuth] HasConsentButton: {state.HasGoogleConsentButton}");
        DebugConsole.WriteLine($"[OpenRouterOAuth] IsTargetService: {providerState.IsTargetService}");
        DebugConsole.WriteLine($"[OpenRouterOAuth] HasAccountPicker: {state.HasAccountPicker}");
        DebugConsole.WriteLine($"[OpenRouterOAuth] HasGoogleTotpInput: {state.HasGoogleTotpInput}");
        DebugConsole.WriteLine($"[OpenRouterOAuth] HasGoogleLoginButton: {providerState.HasGoogleLoginButton}");
        DebugConsole.WriteLine($"[OpenRouterOAuth] HasTermsConsentButton: {providerState.HasTermsConsentButton}");
    }

    // ========== OpenRouter-specific consent steps ==========

    protected override bool ShouldClickProviderInitialButton(CombinedOAuthPageState state)
    {
        var providerState = state.ProviderState as OpenRouterOAuthPageState;
        if (providerState == null)
            return false;

        // Click the "Continue with Google" CTA on the OpenRouter landing page to start OAuth.
        // Ignore Google's own account/consent pages (the base flow handles those).
        return !state.IsGoogleOAuthPage && providerState.HasGoogleLoginButton;
    }

    protected override async Task<bool> TryClickProviderInitialButtonAsync(
        CombinedOAuthPageState state,
        CancellationToken cancellationToken)
    {
        var providerState = state.ProviderState as OpenRouterOAuthPageState;
        if (providerState == null || !providerState.HasGoogleLoginButton)
            return false;

        const string script = @"
(function() {
    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return el.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
    };
    // Clerk renders the Google sign-in as an icon-only button whose span carries
    // the Sign-in-with-Google aria label (button has an empty innerText).
    const clerkSpan = document.querySelector('[aria-label=""Sign in with Google""]');
    if (clerkSpan) {
        const btn = clerkSpan.closest('button') || clerkSpan;
        if (isVisible(btn)) {
            btn.click();
            return true;
        }
    }
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
                    DebugConsole.WriteLine("[OpenRouterOAuth] Clicked 'Continue with Google' button");
                }
                return clicked;
            }

            return false;
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[OpenRouterOAuth] Click Google login button error: {ex.Message}");
            return false;
        }
    }

    protected override bool ShouldClickProviderConsent(CombinedOAuthPageState state)
    {
        var providerState = state.ProviderState as OpenRouterOAuthPageState;
        if (providerState == null)
            return false;

        // On the OpenRouter page with a terms-of-service "Agree & Continue" button.
        return !state.IsGoogleOAuthPage && providerState.HasTermsConsentButton;
    }

    protected override async Task<bool> TryClickProviderConsentButtonAsync(
        CombinedOAuthPageState state,
        CancellationToken cancellationToken)
    {
        var providerState = state.ProviderState as OpenRouterOAuthPageState;
        if (providerState == null || !providerState.HasTermsConsentButton)
            return false;

        const string script = @"
(function() {
    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return el.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
    };
    const candidates = Array.from(document.queryselectorAll('button, [role=""button""]')).filter(btn => {
        if (!isVisible(btn)) return false;
        const text = ((btn.innerText || '') + ' ' + (btn.getAttribute('aria-label') || '')).toLowerCase();
        return text.includes('agree & continue') ||
               text.includes('agree and continue') ||
               text.includes('accept terms') ||
               text.includes('i agree to the terms') ||
               text.includes('đồng ý & tiếp tục') ||
               text.includes('chấp nhận điều khoản');
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
                    DebugConsole.WriteLine("[OpenRouterOAuth] Clicked 'Agree & Continue' terms button");
                }
                return clicked;
            }

            return false;
        }
        catch (Exception ex)
        {
            DebugConsole.WriteLine($"[OpenRouterOAuth] Click terms consent error: {ex.Message}");
            return false;
        }
    }
}

/// <summary>
/// OpenRouter specific page state (provider-specific, non-Google).
/// </summary>
public sealed record OpenRouterOAuthPageState : ProviderOAuthPageState
{
    public required bool IsOpenRouterOAuthPage { get; init; }
    public required bool IsTargetService { get; init; }
    public required bool HasGoogleLoginButton { get; init; }
    public required bool HasTermsConsentButton { get; init; }
}