using System.Text.Json;
using RouterPlus.Infrastructure.Diagnostics;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Automation for AWS Builder ID OAuth consent flow (used by Kiro).
/// Extends GoogleOAuthFlowAutomation with AWS-specific logic.
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

    protected override async Task<GoogleOAuthPageState> ReadPageStateAsync(CancellationToken cancellationToken)
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

    // Check if on Google OAuth/account page
    const isGoogleOAuthPage = host === 'accounts.google.com';

    // Check for completion page (device code activated message)
    const bodyText = document.body?.innerText?.toLowerCase() || '';
    const isCompletionPage = bodyText.includes('device is now connected') ||
                             bodyText.includes('authorization granted') ||
                             bodyText.includes('you may close') ||
                             bodyText.includes('success');

    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return el.getClientRects().length > 0 && rect.width > 0 && rect.height > 0;
    };

    // Detect 'Continue with Google' button on AWS page
    const continueWithGoogleButtons = Array.from(document.querySelectorAll('button, [role=""button""], a')).filter(btn => {
        if (!isVisible(btn)) return false;
        const text = ((btn.innerText || '') + ' ' + (btn.getAttribute('aria-label') || '')).toLowerCase();
        return text.includes('continue with google') || text.includes('sign in with google') || text.includes('google');
    });
    const hasContinueWithGoogleButton = continueWithGoogleButtons.length > 0;

    // Detect account picker
    const accountButtons = Array.from(document.querySelectorAll(
        '[data-email], [data-identifier], ul[role=""listbox""] li, button[data-email], a[data-email]'
    ));
    const hasAccountPicker = accountButtons.length > 0;

    // Detect Google TOTP input
    const totpInputs = Array.from(document.querySelectorAll('input[type=""tel""], input[name*=""otp""], input[id*=""otp""], input[name*=""totpPin""]'));
    const hasGoogleTotpInput = totpInputs.some(isVisible);

    // Detect Google consent buttons
    const googleConsentButtons = Array.from(document.querySelectorAll('button, [role=""button""]')).filter(btn => {
        if (!isVisible(btn)) return false;
        const text = ((btn.innerText || '') + ' ' + (btn.getAttribute('aria-label') || '')).toLowerCase();
        return text.includes('continue') || text.includes('tiếp tục') ||
               text.includes('allow') || text.includes('cho phép');
    });
    const hasGoogleConsentButton = googleConsentButtons.length > 0;

    // Detect AWS Builder ID consent buttons
    const awsConsentButtons = Array.from(document.querySelectorAll('button, [role=""button""]')).filter(btn => {
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
        isGoogleOAuthPage: isGoogleOAuthPage,
        isCompletionPage: isCompletionPage,
        hasContinueWithGoogleButton: hasContinueWithGoogleButton,
        hasAccountPicker: hasAccountPicker,
        hasGoogleTotpInput: hasGoogleTotpInput,
        hasGoogleConsentButton: hasGoogleConsentButton,
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
                IsGoogleOAuthPage = value.GetProperty("isGoogleOAuthPage").GetBoolean(),
                HasAccountPicker = value.GetProperty("hasAccountPicker").GetBoolean(),
                HasGoogleTotpInput = value.GetProperty("hasGoogleTotpInput").GetBoolean(),
                HasGoogleConsentButton = value.GetProperty("hasGoogleConsentButton").GetBoolean(),
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

    protected override CompletionCheckResult CheckCompletion(GoogleOAuthPageState state)
    {
        if (state is not AwsBuilderIdOAuthPageState awsState)
            return new CompletionCheckResult(IsComplete: false);

        if (awsState.IsCompletionPage)
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

    protected override void LogPageState(GoogleOAuthPageState state)
    {
        if (state is not AwsBuilderIdOAuthPageState awsState)
            return;

        DebugConsole.WriteLine($"[AwsBuilderIdOAuth] URL: {awsState.CurrentUrl}");
        DebugConsole.WriteLine($"[AwsBuilderIdOAuth] IsAwsBuilderIdPage: {awsState.IsAwsBuilderIdPage}");
        DebugConsole.WriteLine($"[AwsBuilderIdOAuth] IsGoogleOAuthPage: {awsState.IsGoogleOAuthPage}");
        DebugConsole.WriteLine($"[AwsBuilderIdOAuth] HasContinueWithGoogle: {awsState.HasContinueWithGoogleButton}");
        DebugConsole.WriteLine($"[AwsBuilderIdOAuth] HasAccountPicker: {awsState.HasAccountPicker}");
        DebugConsole.WriteLine($"[AwsBuilderIdOAuth] HasGoogleTotpInput: {awsState.HasGoogleTotpInput}");
        DebugConsole.WriteLine($"[AwsBuilderIdOAuth] HasGoogleConsent: {awsState.HasGoogleConsentButton}");
        DebugConsole.WriteLine($"[AwsBuilderIdOAuth] HasAwsConsent: {awsState.HasAwsConsentButton}");
        DebugConsole.WriteLine($"[AwsBuilderIdOAuth] IsCompletionPage: {awsState.IsCompletionPage}");
    }

    // ========== Override virtual methods for AWS-specific behavior ==========

    protected override bool ShouldClickProviderInitialButton(GoogleOAuthPageState state)
    {
        if (state is not AwsBuilderIdOAuthPageState awsState)
            return false;

        return awsState.IsAwsBuilderIdPage && awsState.HasContinueWithGoogleButton;
    }

    protected override async Task<bool> TryClickProviderInitialButtonAsync(GoogleOAuthPageState state, CancellationToken cancellationToken)
    {
        DebugConsole.WriteLine("[AwsBuilderIdOAuth] Clicking 'Continue with Google'...");

        const string script = @"
(function() {
    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return rect.width > 0 && rect.height > 0;
    };
    const buttons = Array.from(document.querySelectorAll('button, [role=""button""], a')).filter(btn => {
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

    protected override bool ShouldClickProviderConsent(GoogleOAuthPageState state)
    {
        if (state is not AwsBuilderIdOAuthPageState awsState)
            return false;

        return awsState.IsAwsBuilderIdPage && awsState.HasAwsConsentButton;
    }

    protected override async Task<bool> TryClickProviderConsentButtonAsync(GoogleOAuthPageState state, CancellationToken cancellationToken)
    {
        DebugConsole.WriteLine("[AwsBuilderIdOAuth] Clicking AWS Builder ID consent button...");

        const string script = @"
(function() {
    const isVisible = el => {
        if (!el) return false;
        const rect = el.getBoundingClientRect();
        return rect.width > 0 && rect.height > 0;
    };
    const buttons = Array.from(document.querySelectorAll('button, [role=""button""]')).filter(btn => {
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
/// AWS Builder ID specific page state.
/// </summary>
public sealed record AwsBuilderIdOAuthPageState : GoogleOAuthPageState
{
    public required bool IsAwsBuilderIdPage { get; init; }
    public required bool IsCompletionPage { get; init; }
    public required bool HasContinueWithGoogleButton { get; init; }
    public required bool HasAwsConsentButton { get; init; }
}
