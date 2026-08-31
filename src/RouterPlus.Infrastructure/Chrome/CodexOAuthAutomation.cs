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
        path.includes('/signin') ||
        path.includes('/choose-an-account') ||
        path.includes('/consent')
    );

    // Check if on target service (Codex post-auth landing page)
    // Must NOT be auth.openai.com - that's still the OAuth flow, not the target
    const isTargetService = (host.includes('chatgpt.com') || host.includes('openai.com')) && host !== 'auth.openai.com'
        || (host.startsWith('localhost') && path.includes('/auth/callback') && currentUrl.includes('code='));

    return {
        currentUrl: currentUrl,
        isOpenAIOAuthPage: isOpenAIOAuthPage,
        isTargetService: isTargetService
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
                IsTargetService = value.GetProperty("isTargetService").GetBoolean()
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

        // On OpenAI OAuth page - click "Continue with Google" if present
        // (The base flow handles Google account picker and consent)
        return providerState.IsOpenAIOAuthPage;
    }

    protected override async Task<bool> TryClickProviderInitialButtonAsync(CombinedOAuthPageState state, CancellationToken cancellationToken)
    {
        var providerState = state.ProviderState as CodexOAuthPageState;
        if (providerState == null || !providerState.IsOpenAIOAuthPage)
            return false;

        const string script = @"
(function() {
    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return rect.width > 0 && rect.height > 0;
    };
    const candidates = Array.from(document.queryselectorAll('button, a, [role=""button""]')).filter(btn => {
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
}

/// <summary>
/// Codex/OpenAI specific page state (provider-specific, non-Google).
/// </summary>
public sealed record CodexOAuthPageState : ProviderOAuthPageState
{
    public required bool IsOpenAIOAuthPage { get; init; }
    public required bool IsTargetService { get; init; }
}
