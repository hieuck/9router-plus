using System.Text.Json;
using RouterPlus.Infrastructure.Diagnostics;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Automation for OpenRouter OAuth consent flow.
/// Extends GoogleOAuthFlowAutomation with OpenRouter-specific logic.
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

    // Check if on OpenRouter OAuth page
    const isOpenRouterOAuthPage = host === 'openrouter.ai' && (
        path.includes('/auth') ||
        path.includes('/oauth') ||
        path.includes('/login')
    );

    // Treat any known OAuth provider page as an OAuth screen that may need interaction
    const isAnyOAuthPage = isGoogleOAuthPage || isOpenRouterOAuthPage;

    // Check if on target service (OpenRouter post-auth landing page)
    const isTargetService = host === 'openrouter.ai' &&
        !path.includes('/auth') &&
        !path.includes('/oauth') &&
        !path.includes('/login');

    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return el.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
    };

    // Detect account picker (Google uses data-*)
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
               text.includes('chấp nhận') ||
               text.includes('authorize');
    });
    const hasConsentButton = consentButtonCandidates.length > 0;

    return {
        currentUrl: currentUrl,
        isGoogleOAuthPage: isGoogleOAuthPage,
        isAnyOAuthPage: isAnyOAuthPage,
        isTargetService: isTargetService,
        hasAccountPicker: hasAccountPicker,
        hasGoogleTotpInput: hasGoogleTotpInput,
        hasGoogleConsentButton: hasConsentButton,
        isOpenRouterOAuthPage: isOpenRouterOAuthPage
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
                IsGoogleOAuthPage = value.GetProperty("isGoogleOAuthPage").GetBoolean(),
                HasAccountPicker = value.GetProperty("hasAccountPicker").GetBoolean(),
                HasGoogleTotpInput = value.GetProperty("hasGoogleTotpInput").GetBoolean(),
                HasGoogleConsentButton = value.GetProperty("hasGoogleConsentButton").GetBoolean(),
                IsAnyOAuthPage = value.GetProperty("isAnyOAuthPage").GetBoolean(),
                IsTargetService = value.GetProperty("isTargetService").GetBoolean(),
                IsOpenRouterOAuthPage = value.GetProperty("isOpenRouterOAuthPage").GetBoolean()
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new InvalidOperationException($"Failed to read OpenRouter OAuth state: {ex.Message}", ex);
        }
    }

    protected override CompletionCheckResult CheckCompletion(GoogleOAuthPageState state)
    {
        if (state is not OpenRouterOAuthPageState openRouterState)
            return new CompletionCheckResult(IsComplete: false);

        // Check if already on target service (OAuth completed)
        if (openRouterState.IsTargetService)
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

    protected override void LogPageState(GoogleOAuthPageState state)
    {
        if (state is not OpenRouterOAuthPageState openRouterState)
            return;

        DebugConsole.WriteLine($"[OpenRouterOAuth] URL: {openRouterState.CurrentUrl}");
        DebugConsole.WriteLine($"[OpenRouterOAuth] IsGoogleOAuth: {openRouterState.IsGoogleOAuthPage}");
        DebugConsole.WriteLine($"[OpenRouterOAuth] IsOpenRouterOAuth: {openRouterState.IsOpenRouterOAuthPage}");
        DebugConsole.WriteLine($"[OpenRouterOAuth] HasConsentButton: {openRouterState.HasGoogleConsentButton}");
        DebugConsole.WriteLine($"[OpenRouterOAuth] IsTargetService: {openRouterState.IsTargetService}");
        DebugConsole.WriteLine($"[OpenRouterOAuth] HasAccountPicker: {openRouterState.HasAccountPicker}");
    }

    // ========== Override virtual methods for OpenRouter-specific behavior ==========

    protected override bool ShouldClickAccountPicker(GoogleOAuthPageState state)
    {
        if (state is not OpenRouterOAuthPageState openRouterState)
            return false;

        // Not on Google OAuth page - might be loading or on OpenRouter page
        if (!openRouterState.IsGoogleOAuthPage && !openRouterState.IsAnyOAuthPage)
            return false;

        // Click account picker if present and no consent button yet
        return openRouterState.HasAccountPicker && !openRouterState.HasGoogleConsentButton;
    }
}

/// <summary>
/// OpenRouter specific page state.
/// </summary>
public sealed record OpenRouterOAuthPageState : GoogleOAuthPageState
{
    public required bool IsAnyOAuthPage { get; init; }
    public required bool IsTargetService { get; init; }
    public required bool IsOpenRouterOAuthPage { get; init; }
}
