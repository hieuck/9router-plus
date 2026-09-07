using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.Infrastructure.Tests;

public sealed class GoogleOAuthFlowAutomationTests
{
    private static readonly Uri ProviderUri = new("https://provider.example/oauth");
    private static readonly Uri GoogleUri = new("https://accounts.google.com/v3/signin/identifier");

    [Fact]
    public void Combined_state_prefers_provider_url_and_exposes_google_flags()
    {
        var state = new GoogleOAuthFlowAutomation.CombinedOAuthPageState
        {
            ProviderState = new TestProviderState { CurrentUrl = ProviderUri.ToString() },
            GoogleState = new GoogleOAuthPageState
            {
                CurrentUrl = GoogleUri.ToString(),
                HasAccountPicker = true,
                HasGoogleTotpInput = true,
                HasGoogleConsentButton = true
            }
        };

        Assert.True(state.IsGoogleOAuthPage);
        Assert.Equal(ProviderUri.ToString(), state.CurrentUrl);
        Assert.True(state.HasAccountPicker);
        Assert.True(state.HasGoogleTotpInput);
        Assert.True(state.HasGoogleConsentButton);
        Assert.False(GoogleOAuthFlowAutomation.IsProviderOwnedPage(state));
    }

    [Fact]
    public void Combined_state_uses_provider_url_when_google_state_is_absent()
    {
        var state = new GoogleOAuthFlowAutomation.CombinedOAuthPageState
        {
            ProviderState = new TestProviderState { CurrentUrl = ProviderUri.ToString() }
        };

        Assert.False(state.IsGoogleOAuthPage);
        Assert.Equal(ProviderUri.ToString(), state.CurrentUrl);
        Assert.False(state.HasAccountPicker);
        Assert.False(state.HasGoogleTotpInput);
        Assert.False(state.HasGoogleConsentButton);
        Assert.True(GoogleOAuthFlowAutomation.IsProviderOwnedPage(state));
    }

    [Fact]
    public void Combined_state_without_page_state_has_empty_url()
    {
        var state = new GoogleOAuthFlowAutomation.CombinedOAuthPageState();

        Assert.Equal(string.Empty, state.CurrentUrl);
        Assert.False(state.IsGoogleOAuthPage);
        Assert.True(GoogleOAuthFlowAutomation.IsProviderOwnedPage(state));
    }

    [Fact]
    public void Default_google_action_predicates_follow_detected_flags()
    {
        var automation = CreateAutomation();
        var state = new GoogleOAuthFlowAutomation.CombinedOAuthPageState
        {
            GoogleState = new GoogleOAuthPageState
            {
                CurrentUrl = GoogleUri.ToString(),
                HasAccountPicker = true,
                HasGoogleTotpInput = true,
                HasGoogleConsentButton = true
            }
        };

        Assert.True(automation.ExposeShouldClickAccountPicker(state));
        Assert.True(automation.ExposeShouldFillTotp(state));
        Assert.True(automation.ExposeShouldClickGoogleConsent(state));
        Assert.False(automation.ExposeShouldClickProviderInitialButton(state));
        Assert.False(automation.ExposeShouldClickProviderAccountPicker(state));
        Assert.False(automation.ExposeShouldClickProviderConsent(state));
    }

    [Fact]
    public void Default_google_action_predicates_are_false_without_google_state()
    {
        var automation = CreateAutomation();
        var state = new GoogleOAuthFlowAutomation.CombinedOAuthPageState
        {
            ProviderState = new TestProviderState { CurrentUrl = ProviderUri.ToString() }
        };

        Assert.False(automation.ExposeShouldClickAccountPicker(state));
        Assert.False(automation.ExposeShouldFillTotp(state));
        Assert.False(automation.ExposeShouldClickGoogleConsent(state));
        Assert.False(automation.ExposeShouldClickProviderInitialButton(state));
        Assert.False(automation.ExposeShouldClickProviderAccountPicker(state));
        Assert.False(automation.ExposeShouldClickProviderConsent(state));
    }

    [Fact]
    public void Constructor_rejects_missing_required_flow_context()
    {
        var client = new ChromeCdpClient(new Uri("http://127.0.0.1:9222"));

        Assert.Throws<ArgumentNullException>(() => new TestAutomation(null!, "session", "target", "user@example.com"));
        Assert.Throws<ArgumentNullException>(() => new TestAutomation(client, null!, "target", "user@example.com"));
        Assert.Throws<ArgumentNullException>(() => new TestAutomation(client, "session", null!, "user@example.com"));
        Assert.Throws<ArgumentNullException>(() => new TestAutomation(client, "session", "target", null!));
    }

    private static TestAutomation CreateAutomation() =>
        new(new ChromeCdpClient(new Uri("http://127.0.0.1:9222")), "session", "target", "user@example.com");

    private sealed class TestAutomation : GoogleOAuthFlowAutomation
    {
        public TestAutomation(
            ChromeCdpClient client,
            string sessionId,
            string targetId,
            string profileEmail)
            : base(client, sessionId, targetId, profileEmail)
        {
        }

        public bool ExposeShouldClickAccountPicker(CombinedOAuthPageState state) => ShouldClickAccountPicker(state);
        public bool ExposeShouldFillTotp(CombinedOAuthPageState state) => ShouldFillTotp(state);
        public bool ExposeShouldClickGoogleConsent(CombinedOAuthPageState state) => ShouldClickGoogleConsent(state);
        public bool ExposeShouldClickProviderInitialButton(CombinedOAuthPageState state) => ShouldClickProviderInitialButton(state);
        public bool ExposeShouldClickProviderAccountPicker(CombinedOAuthPageState state) => ShouldClickProviderAccountPicker(state);
        public bool ExposeShouldClickProviderConsent(CombinedOAuthPageState state) => ShouldClickProviderConsent(state);

        protected override Task<ProviderOAuthPageState?> ReadProviderPageStateAsync(CancellationToken cancellationToken) =>
            Task.FromResult<ProviderOAuthPageState?>(null);

        protected override CompletionCheckResult CheckCompletion(CombinedOAuthPageState state) =>
            new(IsComplete: false);

        protected override void LogPageState(CombinedOAuthPageState state)
        {
        }
    }

    private sealed record TestProviderState : ProviderOAuthPageState;
}
