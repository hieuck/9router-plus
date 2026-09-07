using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.Infrastructure.Tests;

public sealed class GoogleOAuthPageDetectorTests
{
    [Theory]
    [InlineData("accounts.google.com", true)]
    [InlineData("ACCOUNTS.GOOGLE.COM", true)]
    [InlineData("accounts.google.com.", true)]
    [InlineData("accounts.google.com.evil.test", false)]
    [InlineData("google.com", false)]
    [InlineData("auth.openai.com", false)]
    public void IsGoogleOAuthHost_matches_only_google_accounts_origins(string host, bool expected)
    {
        Assert.Equal(expected, GoogleOAuthPageDetector.IsGoogleOAuthHost(host));
    }
}

public sealed class GoogleOAuthCombinedStateTests
{
    [Fact]
    public void Combined_state_exposes_google_flags_and_url()
    {
        var state = new GoogleOAuthFlowAutomation.CombinedOAuthPageState
        {
            GoogleState = new GoogleOAuthPageState
            {
                CurrentUrl = "https://accounts.google.com/signin/challenge/totp",
                HasAccountPicker = false,
                HasGoogleTotpInput = true,
                HasGoogleConsentButton = true
            }
        };

        Assert.True(state.IsGoogleOAuthPage);
        Assert.Equal(state.GoogleState.CurrentUrl, state.CurrentUrl);
        Assert.True(state.HasGoogleTotpInput);
        Assert.True(state.HasGoogleConsentButton);
        Assert.False(state.HasAccountPicker);
        Assert.False(GoogleOAuthFlowAutomation.IsProviderOwnedPage(state));
    }

    [Fact]
    public void Combined_state_uses_provider_url_and_is_provider_owned_without_google_state()
    {
        var state = new GoogleOAuthFlowAutomation.CombinedOAuthPageState
        {
            ProviderState = new CodexOAuthPageState
            {
                CurrentUrl = "https://auth.openai.com/authorize",
                IsOpenAIOAuthPage = true,
                IsTargetService = true,
                HasGoogleLoginButton = false,
                HasCodexConsentButton = false,
                HasOpenAIAccountPicker = false
            }
        };

        Assert.False(state.IsGoogleOAuthPage);
        Assert.Equal("https://auth.openai.com/authorize", state.CurrentUrl);
        Assert.False(state.HasGoogleTotpInput);
        Assert.True(GoogleOAuthFlowAutomation.IsProviderOwnedPage(state));
    }
}
