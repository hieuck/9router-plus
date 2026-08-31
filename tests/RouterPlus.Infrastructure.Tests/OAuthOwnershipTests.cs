using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.Infrastructure.Tests;

public sealed class OAuthOwnershipTests
{
    [Theory]
    [InlineData("accounts.google.com")]
    [InlineData("accounts.google.com.")]
    public void Google_detector_owns_every_accounts_google_origin(string host)
    {
        Assert.True(GoogleOAuthPageDetector.IsGoogleOAuthHost(host));
    }

    [Fact]
    public void Google_detector_does_not_own_provider_origins()
    {
        Assert.False(GoogleOAuthPageDetector.IsGoogleOAuthHost("auth.openai.com"));
    }

    [Fact]
    public void Provider_actions_are_disabled_when_shared_google_state_is_present()
    {
        var state = new GoogleOAuthFlowAutomation.CombinedOAuthPageState
        {
            GoogleState = new GoogleOAuthPageState
            {
                CurrentUrl = "https://accounts.google.com/v3/signin/accountchooser",
                HasAccountPicker = true,
                HasGoogleTotpInput = false,
                HasGoogleConsentButton = false
            }
        };

        Assert.False(GoogleOAuthFlowAutomation.IsProviderOwnedPage(state));
    }

    [Fact]
    public void Codex_consent_route_requires_the_OpenAI_auth_origin()
    {
        Assert.True(CodexOAuthAutomation.IsCodexConsentRoute(
            "auth.openai.com", "/sign-in-with-chatgpt/codex/consent"));
        Assert.False(CodexOAuthAutomation.IsCodexConsentRoute(
            "accounts.google.com", "/sign-in-with-chatgpt/codex/consent"));
    }

    [Fact]
    public void Codex_picker_state_is_provider_owned_but_not_google_owned()
    {
        var state = new GoogleOAuthFlowAutomation.CombinedOAuthPageState
        {
            ProviderState = new CodexOAuthPageState
            {
                CurrentUrl = "https://auth.openai.com/choose-an-account",
                IsOpenAIOAuthPage = true,
                IsTargetService = false,
                HasGoogleLoginButton = false,
                HasCodexConsentButton = false,
                HasOpenAIAccountPicker = true
            }
        };

        Assert.True(GoogleOAuthFlowAutomation.IsProviderOwnedPage(state));
        Assert.False(state.IsGoogleOAuthPage);
    }

    [Theory]
    [InlineData("auth.openai.com", "/choose-an-account")]
    [InlineData("auth.openai.com", "/choose-an-account/")]
    [InlineData("auth.openai.com", "/choose-an-account?state=oauth")]
    public void Codex_account_picker_requires_the_exact_OpenAI_route(
        string host,
        string path)
    {
        Assert.True(CodexOAuthAutomation.IsOpenAIAccountPickerRoute(host, path));
    }

    [Theory]
    [InlineData("accounts.google.com", "/choose-an-account")]
    [InlineData("evil-auth.openai.com", "/choose-an-account")]
    [InlineData("auth.openai.com", "/authorize/choose-an-account")]
    [InlineData("auth.openai.com", "/choose-an-account/extra")]
    public void Codex_account_picker_rejects_non_owned_routes(
        string host,
        string path)
    {
        Assert.False(CodexOAuthAutomation.IsOpenAIAccountPickerRoute(host, path));
    }
}
