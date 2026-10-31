using RouterPlus.Core.Observability;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Diagnostics;
using RouterPlus.Infrastructure.Services;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Orchestrates Bazaarlink Cloud API-key acquisition:
/// Google sign-in when needed, delete existing keys, then create and copy a new key.
/// </summary>
public static class BazaarlinkKeyFlowOrchestrator
{
    public sealed record BazaarlinkKeyFlowResult(
        bool Success,
        string? ApiKey,
        string? ErrorMessage);

    public static async Task<BazaarlinkKeyFlowResult> RunAsync(
        IBazaarlinkApiKeyBrowser page,
        GoogleLoginCredential credential,
        IGoogleLoginBrowser googleLogin,
        CancellationToken cancellationToken,
        IGoogleAuthenticationService? googleAuthenticationService = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(googleLogin);

        var signedIn = await EnsureSignedInAsync(
            page,
            googleLogin,
            credential,
            cancellationToken,
            googleAuthenticationService);
        if (signedIn is not null)
        {
            return signedIn;
        }

        await DeleteExistingKeysAsync(page, cancellationToken);

        return await CreateKeyAsync(page, credential.ProfileId, cancellationToken);
    }

    private static async Task<BazaarlinkKeyFlowResult?> EnsureSignedInAsync(
        IBazaarlinkApiKeyBrowser page,
        IGoogleLoginBrowser googleLogin,
        GoogleLoginCredential credential,
        CancellationToken cancellationToken,
        IGoogleAuthenticationService? googleAuthenticationService)
    {
        var state = await page.ReadStateAsync(cancellationToken);
        if (state.IsOnKeysPage && !state.HasGoogleSignIn)
        {
            return null;
        }

        var clicked = await page.TryClickSignInWithGoogleAsync(cancellationToken);
        if (!clicked)
        {
            return new BazaarlinkKeyFlowResult(false, null, "No 'Continue with Google' button was found.");
        }

        if (!await page.WaitForGoogleSignInAsync(cancellationToken))
        {
            return new BazaarlinkKeyFlowResult(false, null, "Timed out waiting for Google sign-in page.");
        }

        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "BazaarlinkKeyFlow",
            "GoogleAutoLoginStarted",
            "Running Google autologin",
            new { email = credential.Email });
        var googleAuthentication = googleAuthenticationService ?? new GoogleAuthenticationService();
        var loginResult = await googleAuthentication.AuthenticateAsync(
            new GoogleAuthenticationRequest(credential, googleLogin),
            cancellationToken);
        ObservabilityHub.Instance.LogEvent(
            LogLevel.Info,
            "BazaarlinkKeyFlow",
            "GoogleAutoLoginCompleted",
            "Google login completed",
            new { category = loginResult.Category.ToString(), message = loginResult.Message });
        if (loginResult.Category != GoogleLoginResultCategory.Success)
        {
            return new BazaarlinkKeyFlowResult(false, null, $"Google sign-in failed: {loginResult.Message}");
        }

        if (!await page.WaitForKeysPageAsync(cancellationToken))
        {
            return new BazaarlinkKeyFlowResult(false, null, "Timed out waiting to return to Bazaarlink keys.");
        }

        return null;
    }

    private static async Task DeleteExistingKeysAsync(
        IBazaarlinkApiKeyBrowser page,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var state = await page.ReadStateAsync(cancellationToken);
            if (state.ExistingKeyCount <= 0)
            {
                return;
            }

            if (!await page.TryDeleteOneExistingKeyAsync(cancellationToken))
            {
                return;
            }
        }
    }

    private static async Task<BazaarlinkKeyFlowResult> CreateKeyAsync(
        IBazaarlinkApiKeyBrowser page,
        string keyName,
        CancellationToken cancellationToken)
    {
        var state = await page.ReadStateAsync(cancellationToken);
        if (!state.HasNewKeyButton)
        {
            return new BazaarlinkKeyFlowResult(false, null, "The \"Add API Key\" button was not found on the keys page.");
        }

        if (!await page.TryClickNewKeyAsync(cancellationToken))
        {
            return new BazaarlinkKeyFlowResult(false, null, "Could not click the \"Add API Key\" button.");
        }

        state = await page.ReadStateAsync(cancellationToken);
        if (!state.HasNewKeyNameInput)
        {
            return new BazaarlinkKeyFlowResult(false, null, "The new API key form did not appear.");
        }

        if (!await page.TryCreateKeyAsync(keyName, cancellationToken))
        {
            return new BazaarlinkKeyFlowResult(false, null, "Failed to create the Bazaarlink API key.");
        }

        state = await page.ReadStateAsync(cancellationToken);
        if (!state.HasCreatedKeyPanel || string.IsNullOrEmpty(state.ApiKey))
        {
            return new BazaarlinkKeyFlowResult(false, null, "The key was created but its value could not be read from the page.");
        }

        return new BazaarlinkKeyFlowResult(true, state.ApiKey, null);
    }
}

