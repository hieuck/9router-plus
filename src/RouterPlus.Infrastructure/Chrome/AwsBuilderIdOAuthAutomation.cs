using System.Text.Json;
using RouterPlus.Infrastructure.Diagnostics;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Automation for AWS Builder ID OAuth consent flow (used by Kiro).
/// Delegates Google-specific detection to GoogleOAuthPageDetector.
/// </summary>
public sealed class AwsBuilderIdOAuthAutomation : GoogleOAuthFlowAutomation
{
    public AwsBuilderIdOAuthAutomation(
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

    // Check if on AWS Builder ID page
    const isAwsBuilderIdPage = host.includes('view.awsapps.com') ||
                                host.includes('auth.kiro.dev') ||
                                host.includes('codewhisperer.us-east-1.amazonaws.com') ||
                                host.includes('us-east-1.signin.aws') ||
                                currentUrl.includes('aws.amazon.com/authorization');

    // Check for completion page (device code activated message)
    const bodyText = document.body?.innerText?.toLowerCase() || '';
    const isCompletionPage = bodyText.includes('device is now connected') ||
                             bodyText.includes('authorization granted') ||
                             bodyText.includes('you may close') ||
                             bodyText.includes('success');

    // Detect 'Continue with Google' button on AWS page
    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return el.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
    };
    const continueWithGoogleButtons = Array.from(document.queryselectorAll('button, [role=""button""], a')).filter(btn => {
        if (!isVisible(btn)) return false;
        const text = ((btn.innerText || '') + ' ' + (btn.getAttribute('aria-label') || '')).toLowerCase();
        return text.includes('continue with google') || text.includes('sign in with google') || text.includes('google');
    });
    const hasContinueWithGoogleButton = continueWithGoogleButtons.length > 0;

    // Detect AWS Builder ID consent buttons
    const awsConsentButtons = Array.from(document.queryselectorAll('button, [role=""button""]')).filter(btn => {
        if (!isVisible(btn)) return false;
        const text = ((btn.innerText || '') + ' ' + (btn.getAttribute('aria-label') || '')).toLowerCase();
        return text.includes('confirm and continue') || text.includes('confirmer et continuer') ||
               text.includes('allow access') || text.includes('autoriser') ||
               text.includes('approve') || text.includes('accept');
    });
    const hasAwsConsentButton = awsConsentButtons.length > 0;

    return {
        currentUrl: currentUrl,
        isAwsBuilderIdPage: isAwsBuilderIdPage,
        isCompletionPage: isCompletionPage,
        hasContinueWithGoogleButton: hasContinueWithGoogleButton,
        hasAwsConsentButton: hasAwsConsentButton
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
            return new AwsBuilderIdOAuthPageState
            {
                CurrentUrl = value.GetProperty("currentUrl").GetString()!,
                IsAwsBuilderIdPage = value.GetProperty("isAwsBuilderIdPage").GetBoolean(),
                IsCompletionPage = value.GetProperty("isCompletionPage").GetBoolean(),
                HasContinueWithGoogleButton = value.GetProperty("hasContinueWithGoogleButton").GetBoolean(),
                HasAwsConsentButton = value.GetProperty("hasAwsConsentButton").GetBoolean()
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new InvalidOperationException($"Failed to read AWS Builder ID OAuth state: {ex.Message}", ex);
        }
    }

    protected override CompletionCheckResult CheckCompletion(CombinedOAuthPageState state)
    {
        var providerState = state.ProviderState as AwsBuilderIdOAuthPageState;
        if (providerState == null)
            return new CompletionCheckResult(IsComplete: false);

        if (providerState.IsCompletionPage)
        {
            return new CompletionCheckResult(
                IsComplete: true,
                Result: new OAuthConsentResult(
                    Success: true,
                    AlreadyAuthorized: false,
                    Message: "AWS Builder ID authorization completed"));
        }

        return new CompletionCheckResult(IsComplete: false);
    }

    protected override void LogPageState(CombinedOAuthPageState state)
    {
        var providerState = state.ProviderState as AwsBuilderIdOAuthPageState;
        if (providerState == null)
            return;

        DebugConsole.WriteLine($"[AwsBuilderIdOAuth] URL: {state.CurrentUrl}");
        DebugConsole.WriteLine($"[AwsBuilderIdOAuth] IsAwsBuilderIdPage: {providerState.IsAwsBuilderIdPage}");
        DebugConsole.WriteLine($"[AwsBuilderIdOAuth] IsGoogleOAuthPage: {state.IsGoogleOAuthPage}");
        DebugConsole.WriteLine($"[AwsBuilderIdOAuth] HasContinueWithGoogle: {providerState.HasContinueWithGoogleButton}");
        DebugConsole.WriteLine($"[AwsBuilderIdOauth] HasAccountPicker: {state.HasAccountPicker}");
        DebugConsole.WriteLine($"[AwsBuilderIdOAuth] HasGoogleTotpInput: {state.HasGoogleTotpInput}");
        DebugConsole.WriteLine($"[AwsBuilderIdOAuth] HasGoogleConsent: {state.HasGoogleConsentButton}");
        DebugConsole.WriteLine($"[AwsBuilderIdOAuth] HasAwsConsent: {providerState.HasAwsConsentButton}");
        DebugConsole.WriteLine($"[AwsBuilderIdOAuth] IsCompletionPage: {providerState.IsCompletionPage}");
    }

    // ========== Override virtual methods for AWS-specific behavior ==========

    protected override bool ShouldClickProviderInitialButton(CombinedOAuthPageState state)
    {
        var providerState = state.ProviderState as AwsBuilderIdOAuthPageState;
        if (providerState == null)
            return false;

        return providerState.IsAwsBuilderIdPage && providerState.HasContinueWithGoogleButton;
    }

    protected override async Task<bool> TryClickProviderInitialButtonAsync(CombinedOAuthPageState state, CancellationToken cancellationToken)
    {
        DebugConsole.WriteLine("[AwsBuilderIdOAuth] Clicking 'Continue with Google'...");

        const string script = @"
(function() {
    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return rect.width > 0 && rect.height > 0;
    };
    const buttons = Array.from(document.queryselectorAll('button, [role=""button""], a')).filter(btn => {
        if (!isVisible(btn)) return false;
        const text = ((btn.innerText || '') + ' ' + (btn.getAttribute('aria-label') || '')).toLowerCase();
        return text.includes('continue with google') || text.includes('sign in with google') || text.includes('google');
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
                DebugConsole.WriteLine("[AwsBuilderIdOAuth] 'Continue with Google' clicked");
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
        var providerState = state.ProviderState as AwsBuilderIdOAuthPageState;
        if (providerState == null)
            return false;

        return providerState.IsAwsBuilderIdPage && providerState.HasAwsConsentButton;
    }

    protected override async Task<bool> TryClickProviderConsentButtonAsync(CombinedOAuthPageState state, CancellationToken cancellationToken)
    {
        DebugConsole.WriteLine("[AwsBuilderIdOAuth] Clicking AWS Builder ID consent button...");

        const string script = @"
(function() {
    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return rect.width > 0 && rect.height > 0;
    };
    const buttons = Array.from(document.queryselectorAll('button, [role=""button""]')).filter(btn => {
        if (!isVisible(btn)) return false;
        const text = ((btn.innerText || '') + ' ' + (btn.getAttribute('aria-label') || '')).toLowerCase();
        return text.includes('confirm and continue') || text.includes('confirmer et continuer') ||
               text.includes('allow access') || text.includes('autoriser') ||
               text.includes('approve') || text.includes('accept');
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
                DebugConsole.WriteLine("[AwsBuilderIdOAuth] AWS Builder ID consent button clicked");
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
/// AWS Builder ID specific page state (provider-specific, non-Google).
/// </summary>
public sealed record AwsBuilderIdOAuthPageState : ProviderOAuthPageState
{
    public required bool IsAwsBuilderIdPage { get; init; }
    public required bool IsCompletionPage { get; init; }
    public required bool HasContinueWithGoogleButton { get; init; }
    public required bool HasAwsConsentButton { get; init; }
}