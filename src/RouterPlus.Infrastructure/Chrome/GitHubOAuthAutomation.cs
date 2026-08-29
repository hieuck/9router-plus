using System.Text.Json;
using RouterPlus.Infrastructure.Diagnostics;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Automation for GitHub OAuth consent flow.
/// Extends GoogleOAuthFlowAutomation with GitHub-specific logic.
/// </summary>
public sealed class GitHubOAuthAutomation : GoogleOAuthFlowAutomation
{
    public GitHubOAuthAutomation(
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

    // Check if on GitHub OAuth authorization page
    const isGitHubOAuthPage = host === 'github.com' && (
        path.includes('/login/oauth') ||
        path.includes('/oauth/authorize')
    );

    // Treat any known OAuth provider page as an OAuth screen that may need interaction
    const isAnyOAuthPage = isGoogleOAuthPage || isGitHubOAuthPage;

    // Check if on target service (GitHub post-auth landing page)
    const isTargetService = host === 'github.com' && !path.includes('/login/oauth') && !path.includes('/oauth/authorize');

    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return el.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
    };

    // Detect account picker (Google uses data-*, GitHub may redirect through Google)
    const isChooseAccountPage = path.includes('/choose-an-account') || path.includes('/account-chooser');
    const accountButtons = Array.from(document.querySelectorAll(
        '[data-email], [data-identifier], ul[role=""listbox""] li, button[data-email], a[data-email], ' +
        'button[aria-label*=""Continue with""], button[aria-label*=""Tiếp tục với""]'
    ));
    const hasAccountPicker = isChooseAccountPage || accountButtons.length > 0;

    // Detect Google TOTP input
    const totpInputs = Array.from(document.querySelectorAll('input[type=""tel""], input[name*=""otp""], input[id*=""otp""], input[name*=""totpPin""]'));
    const hasGoogleTotpInput = totpInputs.some(isVisible);

    // Detect consent buttons (Continue with Google / Authorize)
    const consentButtonCandidates = Array.from(document.querySelectorAll('button, [role=""button""], input[type=""submit""]')).filter(btn => {
        if (!isVisible(btn)) return false;
        const text = ((btn.innerText || '') + ' ' + (btn.getAttribute('aria-label') || '') + ' ' + (btn.getAttribute('value') || '')).toLowerCase();
        return text.includes('continue with google') ||
               text.includes('continue') ||
               text.includes('tiếp tục') ||
               text.includes('authorize') ||
               text.includes('allow') ||
               text.includes('cho phép');
    });
    const hasConsentButton = consentButtonCandidates.length > 0;

    // Detect GitHub authorization button
    const gitHubAuthButtons = Array.from(document.querySelectorAll('button, [role=""button""], input[type=""submit""]')).filter(btn => {
        if (!isVisible(btn)) return false;
        const text = ((btn.innerText || '') + ' ' + (btn.getAttribute('value') || '')).toLowerCase();
        return text.includes('authorize') || text.includes('grant access');
    });
    const hasGitHubAuthButton = gitHubAuthButtons.length > 0;

    return {
        currentUrl: currentUrl,
        isGoogleOAuthPage: isGoogleOAuthPage,
        isAnyOAuthPage: isAnyOAuthPage,
        isTargetService: isTargetService,
        hasAccountPicker: hasAccountPicker,
        hasGoogleTotpInput: hasGoogleTotpInput,
        hasGoogleConsentButton: hasConsentButton,
        isGitHubOAuthPage: isGitHubOAuthPage,
        hasGitHubAuthButton: hasGitHubAuthButton
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
            return new GitHubOAuthPageState
            {
                CurrentUrl = value.GetProperty("currentUrl").GetString()!,
                IsGoogleOAuthPage = value.GetProperty("isGoogleOAuthPage").GetBoolean(),
                HasAccountPicker = value.GetProperty("hasAccountPicker").GetBoolean(),
                HasGoogleTotpInput = value.GetProperty("hasGoogleTotpInput").GetBoolean(),
                HasGoogleConsentButton = value.GetProperty("hasGoogleConsentButton").GetBoolean(),
                IsAnyOAuthPage = value.GetProperty("isAnyOAuthPage").GetBoolean(),
                IsTargetService = value.GetProperty("isTargetService").GetBoolean(),
                IsGitHubOAuthPage = value.GetProperty("isGitHubOAuthPage").GetBoolean(),
                HasGitHubAuthButton = value.GetProperty("hasGitHubAuthButton").GetBoolean()
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new InvalidOperationException($"Failed to read GitHub OAuth state: {ex.Message}", ex);
        }
    }

    protected override CompletionCheckResult CheckCompletion(GoogleOAuthPageState state)
    {
        if (state is not GitHubOAuthPageState gitHubState)
            return new CompletionCheckResult(IsComplete: false);

        // Check if already on target service (OAuth completed)
        if (gitHubState.IsTargetService)
        {
            return new CompletionCheckResult(
                IsComplete: true,
                Result: new OAuthConsentResult(
                    Success: true,
                    AlreadyAuthorized: true,
                    Message: "Already authorized - on GitHub"));
        }

        return new CompletionCheckResult(IsComplete: false);
    }

    protected override void LogPageState(GoogleOAuthPageState state)
    {
        if (state is not GitHubOAuthPageState gitHubState)
            return;

        DebugConsole.WriteLine($"[GitHubOAuth] URL: {gitHubState.CurrentUrl}");
        DebugConsole.WriteLine($"[GitHubOAuth] IsGoogleOAuth: {gitHubState.IsGoogleOAuthPage}");
        DebugConsole.WriteLine($"[GitHubOAuth] IsGitHubOAuth: {gitHubState.IsGitHubOAuthPage}");
        DebugConsole.WriteLine($"[GitHubOAuth] HasConsentButton: {gitHubState.HasGoogleConsentButton}");
        DebugConsole.WriteLine($"[GitHubOAuth] HasGitHubAuthButton: {gitHubState.HasGitHubAuthButton}");
        DebugConsole.WriteLine($"[GitHubOAuth] IsTargetService: {gitHubState.IsTargetService}");
    }

    // ========== Override virtual methods for GitHub-specific behavior ==========

    protected override bool ShouldClickAccountPicker(GoogleOAuthPageState state)
    {
        if (state is not GitHubOAuthPageState gitHubState)
            return false;

        // Not on Google OAuth page - might be on GitHub auth page
        if (!gitHubState.IsGoogleOAuthPage)
            return false;

        // Click account picker if present and no consent button yet
        return gitHubState.HasAccountPicker && !gitHubState.HasGoogleConsentButton;
    }

    protected override bool ShouldClickGoogleConsent(GoogleOAuthPageState state)
    {
        if (state is not GitHubOAuthPageState gitHubState)
            return false;

        // On Google OAuth page with consent button
        if (gitHubState.IsGoogleOAuthPage && gitHubState.HasGoogleConsentButton)
            return true;

        // On GitHub OAuth page with authorization button
        if (gitHubState.IsGitHubOAuthPage && gitHubState.HasGitHubAuthButton)
            return true;

        return false;
    }
}

/// <summary>
/// GitHub specific page state.
/// </summary>
public sealed record GitHubOAuthPageState : GoogleOAuthPageState
{
    public required bool IsAnyOAuthPage { get; init; }
    public required bool IsTargetService { get; init; }
    public required bool IsGitHubOAuthPage { get; init; }
    public required bool HasGitHubAuthButton { get; init; }
}
