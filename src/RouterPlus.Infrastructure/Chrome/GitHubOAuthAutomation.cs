using System.Text.Json;
using RouterPlus.Infrastructure.Diagnostics;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Automation for GitHub OAuth consent flow.
/// Delegates Google-specific detection to GoogleOAuthPageDetector.
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

    protected override async Task<ProviderOAuthPageState?> ReadProviderPageStateAsync(CancellationToken cancellationToken)
    {
        const string script = @"
(function() {
    const currentUrl = window.location.href;
    const host = window.location.host;
    const path = window.location.pathname;

    // Check if on GitHub OAuth authorization page
    const isGitHubOAuthPage = host === 'github.com' && (
        path.includes('/login/oauth') ||
        path.includes('/oauth/authorize')
    );

    // Check if on target service (GitHub post-auth landing page)
    const isTargetService = host === 'github.com' && !path.includes('/login/oauth') && !path.includes('/oauth/authorize');

    // Detect GitHub authorization button
    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return el.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
    };
    const gitHubAuthButtons = Array.from(document.querySelectorAll('button, [role=""button""], input[type=""submit""]')).filter(btn => {
        if (!isVisible(btn)) return false;
        const text = ((btn.innerText || '') + ' ' + (btn.getAttribute('value') || '')).toLowerCase();
        return text.includes('authorize') || text.includes('grant access');
    });
    const hasGitHubAuthButton = gitHubAuthButtons.length > 0;

    return {
        currentUrl: currentUrl,
        isGitHubOAuthPage: isGitHubOAuthPage,
        isTargetService: isTargetService,
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
                IsGitHubOAuthPage = value.GetProperty("isGitHubOAuthPage").GetBoolean(),
                IsTargetService = value.GetProperty("isTargetService").GetBoolean(),
                HasGitHubAuthButton = value.GetProperty("hasGitHubAuthButton").GetBoolean()
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new InvalidOperationException($"Failed to read GitHub OAuth state: {ex.Message}", ex);
        }
    }

    protected override CompletionCheckResult CheckCompletion(CombinedOAuthPageState state)
    {
        var providerState = state.ProviderState as GitHubOAuthPageState;
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
                    Message: "Already authorized - on GitHub"));
        }

        return new CompletionCheckResult(IsComplete: false);
    }

    protected override void LogPageState(CombinedOAuthPageState state)
    {
        var providerState = state.ProviderState as GitHubOAuthPageState;
        if (providerState == null)
            return;

        DebugConsole.WriteLine($"[GitHubOAuth] URL: {state.CurrentUrl}");
        DebugConsole.WriteLine($"[GitHubOAuth] IsGoogleOAuth: {state.IsGoogleOAuthPage}");
        DebugConsole.WriteLine($"[GitHubOAuth] IsGitHubOAuth: {providerState.IsGitHubOAuthPage}");
        DebugConsole.WriteLine($"[GitHubOAuth] HasConsentButton: {state.HasGoogleConsentButton}");
        DebugConsole.WriteLine($"[GitHubOAuth] HasGitHubAuthButton: {providerState.HasGitHubAuthButton}");
        DebugConsole.WriteLine($"[GitHubOAuth] IsTargetService: {providerState.IsTargetService}");
        DebugConsole.WriteLine($"[GitHubOAuth] HasAccountPicker: {state.HasAccountPicker}");
        DebugConsole.WriteLine($"[GitHubOAuth] HasGoogleTotpInput: {state.HasGoogleTotpInput}");
    }

    // ========== Override virtual methods for GitHub-specific behavior ==========

    protected override bool ShouldClickProviderConsent(CombinedOAuthPageState state)
    {
        var providerState = state.ProviderState as GitHubOAuthPageState;
        if (providerState == null)
            return false;

        // On GitHub OAuth page with authorization button
        return providerState.IsGitHubOAuthPage && providerState.HasGitHubAuthButton;
    }

    protected override async Task<bool> TryClickProviderConsentButtonAsync(CombinedOAuthPageState state, CancellationToken cancellationToken)
    {
        var providerState = state.ProviderState as GitHubOAuthPageState;
        if (providerState == null || !providerState.IsGitHubOAuthPage || !providerState.HasGitHubAuthButton)
            return false;

        const string script = @"
(function() {
    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return el.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
    };
    const buttons = Array.from(document.querySelectorAll('button, [role=""button""], input[type=""submit""]')).filter(btn => {
        if (!isVisible(btn)) return false;
        const text = ((btn.innerText || '') + ' ' + (btn.getAttribute('value') || '')).toLowerCase();
        return text.includes('authorize') || text.includes('grant access');
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
                DebugConsole.WriteLine("[GitHubOAuth] Clicked GitHub authorization button");
                return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>
/// GitHub specific page state (provider-specific, non-Google).
/// </summary>
public sealed record GitHubOAuthPageState : ProviderOAuthPageState
{
    public required bool IsGitHubOAuthPage { get; init; }
    public required bool IsTargetService { get; init; }
    public required bool HasGitHubAuthButton { get; init; }
}
