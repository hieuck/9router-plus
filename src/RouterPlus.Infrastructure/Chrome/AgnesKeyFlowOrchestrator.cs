using RouterPlus.Core.Observability;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Diagnostics;
using RouterPlus.Infrastructure.Services;

namespace RouterPlus.Infrastructure.Chrome;

/// <summary>
/// Orchestrates Agnes AI API-key acquisition:
/// Google sign-in when needed, then create and copy a new key.
/// </summary>
public static class AgnesKeyFlowOrchestrator
{
    public sealed record AgnesKeyFlowResult(
        bool Success,
        string? ApiKey,
        string? ErrorMessage);

    public static async Task<AgnesKeyFlowResult> RunAsync(
        IAgnesApiKeyBrowser page,
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
            googleAuthenticationService,
            popupFactory: null);

        if (signedIn is not null)
        {
            return signedIn;
        }

        return await CreateKeyAsync(page, credential.Email, cancellationToken);
    }

    public static async Task<AgnesKeyFlowResult> RunAsync(
        IAgnesApiKeyBrowser page,
        GoogleLoginCredential credential,
        Func<CancellationToken, Task<IGoogleLoginBrowser>> connectGooglePopup,
        CancellationToken cancellationToken,
        IGoogleAuthenticationService? googleAuthenticationService = null)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(credential);
        ArgumentNullException.ThrowIfNull(connectGooglePopup);

        var signedIn = await EnsureSignedInAsync(
            page,
            googleLogin: null,
            credential,
            cancellationToken,
            googleAuthenticationService,
            popupFactory: connectGooglePopup);

        if (signedIn is not null)
        {
            return signedIn;
        }

        return await CreateKeyAsync(page, credential.Email, cancellationToken);
    }

    private static async Task<AgnesKeyFlowResult?> EnsureSignedInAsync(
        IAgnesApiKeyBrowser page,
        IGoogleLoginBrowser? googleLogin,
        GoogleLoginCredential credential,
        CancellationToken cancellationToken,
        IGoogleAuthenticationService? googleAuthenticationService,
        Func<CancellationToken, Task<IGoogleLoginBrowser>>? popupFactory)
    {
        var state = await page.ReadStateAsync(cancellationToken);
        if (!state.IsOnKeysPage)
        {
            return new AgnesKeyFlowResult(
                false,
                null,
                "Not on the Agnes AI keys page. Navigate to https://platform.agnes-ai.com/settings/apiKeys");
        }

        if (!state.HasGoogleSignIn)
        {
            // Already signed in (on keys page, no Google button visible)
            return null;
        }

        var clicked = await page.TryClickSignInWithGoogleAsync(cancellationToken);
        if (!clicked)
        {
            return new AgnesKeyFlowResult(
                false,
                null,
                "Could not find the 'Continue with Google' button on the Agnes AI sign-in page.");
        }

        // Get the Google login popup (separate CDP target) after click
        IGoogleLoginBrowser? popup = googleLogin;
        bool ownsPopup = false;
        if (popup is null && popupFactory is not null)
        {
            try
            {
                popup = await popupFactory(cancellationToken);
                ownsPopup = true;
            }
            catch (Exception ex)
            {
                return new AgnesKeyFlowResult(
                    false,
                    null,
                    $"Google login popup did not appear: {ex.Message}");
            }
        }

        if (popup is null)
        {
            return new AgnesKeyFlowResult(
                false,
                null,
                "Google login popup did not appear: no popup connector provided.");
        }

        try
        {
            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "AgnesKeyFlow",
                "GoogleAutoLoginStarted",
                "Running Google autologin",
                new { email = credential.Email });

            var googleAuthentication = googleAuthenticationService ?? new GoogleAuthenticationService();
            var loginResult = await googleAuthentication.AuthenticateAsync(
                new GoogleAuthenticationRequest(credential, popup),
                cancellationToken);

            ObservabilityHub.Instance.LogEvent(
                LogLevel.Info,
                "AgnesKeyFlow",
                "GoogleAutoLoginCompleted",
                "Google login completed",
                new { category = loginResult.Category.ToString(), message = loginResult.Message });

            if (loginResult.Category != GoogleLoginResultCategory.Success)
            {
                return new AgnesKeyFlowResult(
                    false,
                    null,
                    $"Google sign-in failed: {loginResult.Message}");
            }
        }
        finally
        {
            if (ownsPopup)
            {
                await popup.DisposeAsync();
            }
        }

        // Wait for redirect back to keys page
        if (!await page.WaitForKeysPageAsync(cancellationToken))
        {
            return new AgnesKeyFlowResult(
                false,
                null,
                "Did not return to Agnes AI keys page after Google sign-in.");
        }

        return null;
    }

    private static async Task<AgnesKeyFlowResult> CreateKeyAsync(
        IAgnesApiKeyBrowser page,
        string profileEmail,
        CancellationToken cancellationToken)
    {
        // Click "Create new secret key"
        var clickedNewKey = await page.TryClickNewKeyAsync(cancellationToken);
        if (!clickedNewKey)
        {
            return new AgnesKeyFlowResult(
                false,
                null,
                "Could not find 'Create new secret key' button.");
        }

        // Wait for the name input modal
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15);
        bool hasNameInput = false;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var state = await page.ReadStateAsync(cancellationToken);
            if (state.HasNewKeyNameInput)
            {
                hasNameInput = true;
                break;
            }
            await Task.Delay(500, cancellationToken);
        }

        if (!hasNameInput)
        {
            return new AgnesKeyFlowResult(
                false,
                null,
                "Key name input modal did not appear.");
        }

        // Fill key name and create
        var keyName = $"{profileEmail}-agnes";
        var created = await page.TryCreateKeyAsync(keyName, cancellationToken);
        if (!created)
        {
            return new AgnesKeyFlowResult(
                false,
                null,
                "Failed to create the API key (modal submit failed).");
        }

        // Wait for the key to appear in the disabled input
        deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(15);
        string? apiKey = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var state = await page.ReadStateAsync(cancellationToken);
            if (state.HasCreatedKeyPanel && !string.IsNullOrWhiteSpace(state.ApiKey))
            {
                apiKey = state.ApiKey;
                break;
            }
            await Task.Delay(500, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AgnesKeyFlowResult(
                false,
                null,
                "API key was created but could not be read from the modal before it closed.");
        }

        return new AgnesKeyFlowResult(true, apiKey, null);
    }
}