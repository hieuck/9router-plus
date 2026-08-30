using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Diagnostics;
using RouterPlus.Infrastructure.Services;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Orchestrates the full OpenRouter API-key flow:
/// click Clerk "Sign in with Google" -> Google autologin via Vault -> OpenRouter onboarding.
/// </summary>
public static class OpenRouterKeyFlowOrchestrator
{
    /// <summary>
    /// Result of the full OpenRouter key-acquisition flow.
    /// </summary>
    public sealed record OpenRouterKeyFlowResult(
        bool Success,
        string? ApiKey,
        string? ErrorMessage);

    /// <summary>
    /// Runs the flow.
    /// </summary>
    /// <param name="onboarding">Adapter for the OpenRouter page (Clerk sign-in + onboarding).</param>
    /// <param name="startUri">Keys-page URI. Null when the onboarding adapter already navigated.</param>
    /// <param name="credential">Google credential from the Vault used for autologin.</param>
    /// <param name="googleLogin">Adapter for Google sign-in automation.</param>
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

        // The managed Chrome window points at the keys page, so it must first be
        // navigated to the OpenRouter sign-in (this also happens against an open
        // target whose execution context is gone). Fail fast with a clear message.
        await onboarding.NavigateToOpenRouterSignInAsync(cancellationToken);

        // Phase 1: if the keys page already exposes a key, return it directly.
        var state = await onboarding.ReadStateAsync(cancellationToken);
        if (!string.IsNullOrEmpty(state.ApiKey))
        {
            return new OpenRouterKeyFlowResult(true, state.ApiKey, null);
        }

        // Phase 2: click the Clerk "Sign in with Google" button to start Google OAuth.
        var clicked = await onboarding.TryClickSignInWithGoogleAsync(cancellationToken);
        if (!clicked)
        {
            return new OpenRouterKeyFlowResult(false, null, "No 'Sign in with Google' button was found.");
        }

        // Wait for the redirect to reach accounts.google.com.
        if (!await onboarding.WaitForGoogleSignInAsync(cancellationToken))
        {
            return new OpenRouterKeyFlowResult(false, null, "Timed out waiting for Google sign-in page.");
        }

        // Phase 3: Google autologin using the vault credential.
        DebugConsole.WriteLine($"[OpenRouterKeyFlow] Running Google autologin for {credential.Email}...");
        var googleAuthentication = googleAuthenticationService ?? new GoogleAuthenticationService();
        var loginResult = await googleAuthentication.AuthenticateAsync(
            new GoogleAuthenticationRequest(credential, googleLogin),
            cancellationToken);
        DebugConsole.WriteLine($"[OpenRouterKeyFlow] Google login category: {loginResult.Category}, message: {loginResult.Message}");
        if (loginResult.Category != GoogleLoginResultCategory.Success)
        {
            return new OpenRouterKeyFlowResult(false, null, $"Google sign-in failed: {loginResult.Message}");
        }

        // Phase 4: wait for the OAuth callback to return to the OpenRouter keys page.
        if (!await onboarding.WaitForOpenRouterKeysAsync(cancellationToken))
        {
            return new OpenRouterKeyFlowResult(false, null, "Timed out waiting to return to OpenRouter.");
        }

        // Phase 5: run the OpenRouter onboarding (wizard / New Key) and capture the key.
        var onboardingResult = await OpenRouterOnboardingAutomation.RunAsync(onboarding, credential.ProfileId, cancellationToken);
        if (!onboardingResult.Success)
        {
            return new OpenRouterKeyFlowResult(false, null, onboardingResult.ErrorMessage);
        }

        return new OpenRouterKeyFlowResult(true, onboardingResult.ApiKey, null);
    }
}