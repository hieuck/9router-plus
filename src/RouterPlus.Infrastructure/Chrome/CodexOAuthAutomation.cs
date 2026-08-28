using System.Text.Json;
using RouterPlus.Infrastructure.Diagnostics;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Automation for Codex OAuth consent flow.
/// Extends GoogleOAuthFlowAutomation with Codex/OpenAI-specific logic.
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

    protected override async Task<GoogleOAuthPageState> ReadPageStateAsync(CancellationToken cancellationToken)
    {
        const string script = @"
(function() {
    const currentUrl = window.location.href;
    const host = window.location.host;
    const path = window.location.pathname;

    // Check if on Google OAuth/account page
    const isGoogleOAuthPage = host === 'accounts.google.com' && (
        path.includes('/signin/oauth') ||
        path.includes('/o/oauth2') ||
        path.includes('/ServiceLogin') ||
        path.includes('/AccountChooser') ||
        path.includes('/signin/v2')
    );

    // Check if on OpenAI OAuth page (Codex uses auth.openai.com)
    const isOpenAIOAuthPage = host === 'auth.openai.com' && (
        path.includes('/oauth') ||
        path.includes('/authorize') ||
        path.includes('/login') ||
        path.includes('/signin')
    );

    // Treat any known OAuth provider page as an OAuth screen that may need interaction
    const isAnyOAuthPage = isGoogleOAuthPage || isOpenAIOAuthPage;

    // Check if on target service (Codex post-auth landing page)
    // Must NOT be auth.openai.com - that's still the OAuth flow, not the target
    const isTargetService = (host.includes('chatgpt.com') || host.includes('openai.com')) && host !== 'auth.openai.com'
        || (host.startsWith('localhost') && path.includes('/auth/callback') && currentUrl.includes('code='));

    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return el.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
    };

    // Detect account picker (Google uses data-*, OpenAI uses choose-an-account page)
    const isChooseAccountPage = path.includes('/choose-an-account') || path.includes('/account-chooser');
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
    const hasConsentButton = consentButtonCandidates.length > 0;

    return {
        currentUrl: currentUrl,
        isGoogleOAuthPage: isGoogleOAuthPage,
        isAnyOAuthPage: isAnyOAuthPage,
        isTargetService: isTargetService,
        hasAccountPicker: hasAccountPicker,
        hasGoogleTotpInput: hasGoogleTotpInput,
        hasGoogleConsentButton: hasConsentButton
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
                IsGoogleOAuthPage = value.GetProperty("isGoogleOAuthPage").GetBoolean(),
                HasAccountPicker = value.GetProperty("hasAccountPicker").GetBoolean(),
                HasGoogleTotpInput = value.GetProperty("hasGoogleTotpInput").GetBoolean(),
                HasGoogleConsentButton = value.GetProperty("hasGoogleConsentButton").GetBoolean(),
                IsAnyOAuthPage = value.GetProperty("isAnyOAuthPage").GetBoolean(),
                IsTargetService = value.GetProperty("isTargetService").GetBoolean()
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new InvalidOperationException($"Failed to read Codex OAuth state: {ex.Message}", ex);
        }
    }

    protected override CompletionCheckResult CheckCompletion(GoogleOAuthPageState state)
    {
        if (state is not CodexOAuthPageState codexState)
            return new CompletionCheckResult(IsComplete: false);

        // Check if already on target service (OAuth completed)
        if (codexState.IsTargetService)
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

    protected override void LogPageState(GoogleOAuthPageState state)
    {
        if (state is not CodexOAuthPageState codexState)
            return;

        DebugConsole.WriteLine($"[CodexOAuth] URL: {codexState.CurrentUrl}");
        DebugConsole.WriteLine($"[CodexOAuth] IsGoogleOAuth: {codexState.IsGoogleOAuthPage}");
        DebugConsole.WriteLine($"[CodexOAuth] HasConsentButton: {codexState.HasGoogleConsentButton}");
        DebugConsole.WriteLine($"[CodexOAuth] IsTargetService: {codexState.IsTargetService}");
        DebugConsole.WriteLine($"[CodexOAuth] HasAccountPicker: {codexState.HasAccountPicker}");
    }

    // ========== Override virtual methods for Codex-specific behavior ==========

    protected override bool ShouldClickAccountPicker(GoogleOAuthPageState state)
    {
        if (state is not CodexOAuthPageState codexState)
            return false;

        // Not on Google OAuth page - might be loading or redirecting
        if (!codexState.IsGoogleOAuthPage && !codexState.IsAnyOAuthPage)
            return false;

        // Click account picker if present and no consent button yet
        return codexState.HasAccountPicker && !codexState.HasGoogleConsentButton;
    }
}

/// <summary>
/// Codex/OpenAI specific page state.
/// </summary>
public sealed record CodexOAuthPageState : GoogleOAuthPageState
{
    public required bool IsAnyOAuthPage { get; init; }
    public required bool IsTargetService { get; init; }
}
