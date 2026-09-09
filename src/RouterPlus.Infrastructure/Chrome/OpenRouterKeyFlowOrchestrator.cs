using RouterPlus.Core.Observability;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Diagnostics;
using RouterPlus.Infrastructure.Services;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Orchestrates the full OpenRouter API-key flow:
/// Google sign-in when needed, delete existing keys, then create and copy a new key.
/// </summary>
public static class OpenRouterKeyFlowOrchestrator
{
    public sealed record OpenRouterKeyFlowResult(
        bool Success,
        string? ApiKey,
        string? ErrorMessage);

    public static async Task<OpenRouterKeyFlowResult> RunAsync(
        IOpenRouterOnboardingBrowser onboarding,
        Uri? startUri,
        GoogleLoginCredential credential,
        IGoogleLoginBrowser googleLogin,
        CancellationToken cancellationToken,
        IGoogleAuthenticationService? googleAuthenticationService = null)
    {
        ArgumentNullException.ThrowIfNull(onboarding);
        ArgumentNullException.ThrowIfNull(googleLogin);
        ArgumentNullException.ThrowIfNull(credential);
        _ = startUri;

        var signedIn = await EnsureSignedInAsync(
            onboarding,
            googleLogin,
            credential,
            cancellationToken,
            googleAuthenticationService);
        if (signedIn is not null)
        {
            return signedIn;
        }

        await DeleteExistingKeysAsync(onboarding, cancellationToken);

        var onboardingResult = await OpenRouterOnboardingAutomation.RunAsync(
            onboarding,
            credential.ProfileId,
            cancellationToken);
        if (!onboardingResult.Success)
        {
            return new OpenRouterKeyFlowResult(false, null, onboardingResult.ErrorMessage);
        }

        return new OpenRouterKeyFlowResult(true, onboardingResult.ApiKey, null);
    }

    private static async Task<OpenRouterKeyFlowResult?> EnsureSignedInAsync(
        IOpenRouterOnboardingBrowser onboarding,
        IGoogleLoginBrowser googleLogin,
        GoogleLoginCredential credential,
        CancellationToken cancellationToken,
        IGoogleAuthenticationService? googleAuthenticationService)
    {
        var state = await onboarding.ReadStateAsync(cancellationToken);
        if (state.IsOnKeysPage && !state.HasGoogleSignIn)
        {
            return null;
        }

        var clicked = await onboarding.TryClickSignInWithGoogleAsync(cancellationToken);
        if (!clicked)
        {
            return new OpenRouterKeyFlowResult(false, null, "No 'Sign in with Google' button was found.");
        }

        if (!await onboarding.WaitForGoogleSignInAsync(cancellationToken))
        {
            return new OpenRouterKeyFlowResult(false, null, "Timed out waiting for Google sign-in page.");
        }

        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "OpenRouterKeyFlow",
            "GoogleAutoLoginStarted",
            "Running Google autologin",
            new { email = credential.Email });
        var googleAuthentication = googleAuthenticationService ?? new GoogleAuthenticationService();
        var loginResult = await googleAuthentication.AuthenticateAsync(
            new GoogleAuthenticationRequest(credential, googleLogin),
            cancellationToken);
        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "OpenRouterKeyFlow",
            "GoogleAutoLoginCompleted",
            "Google login completed",
            new { category = loginResult.Category.ToString(), message = loginResult.Message });
        if (loginResult.Category != GoogleLoginResultCategory.Success)
        {
            return new OpenRouterKeyFlowResult(false, null, $"Google sign-in failed: {loginResult.Message}");
        }

        if (!await onboarding.WaitForOpenRouterKeysAsync(cancellationToken))
        {
            return new OpenRouterKeyFlowResult(false, null, "Timed out waiting to return to OpenRouter.");
        }

        return null;
    }

    private static async Task DeleteExistingKeysAsync(
        IOpenRouterOnboardingBrowser onboarding,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var state = await onboarding.ReadStateAsync(cancellationToken);
            if (state.ExistingKeyCount <= 0)
            {
                return;
            }

            if (!await onboarding.TryDeleteOneExistingKeyAsync(cancellationToken))
            {
                return;
            }
        }
    }
}
