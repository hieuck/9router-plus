using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.Infrastructure.Tests;

public sealed class GoogleOAuthFlowAutomationTests
{
    private static readonly Uri ProviderUri = new("https://provider.example/oauth");
    private static readonly Uri GoogleUri = new("https://accounts.google.com/v3/signin/identifier");
    private static readonly Uri SyntheticStartUri = new("https://provider.invalid/oauth/start");

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

    [Fact]
    public async Task Google_page_owns_account_picker_and_does_not_read_provider_state()
    {
        var automation = new FakeGoogleOAuthAutomation(
            googleStates: [GoogleState(accountPicker: true)]);

        var result = await automation.WaitAndConsentAsync(
            SyntheticStartUri,
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Could not select account", result.Message);
        Assert.Equal(0, automation.ProviderReadCount);
        Assert.Equal(["ClickAccountPicker"], automation.Actions);
    }

    [Fact]
    public async Task Zero_timeout_returns_timeout_without_touching_browser()
    {
        var automation = new FakeGoogleOAuthAutomation();

        var result = await automation.WaitAndConsentAsync(
            SyntheticStartUri,
            TimeSpan.Zero,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Timeout waiting for OAuth consent flow", result.Message);
        Assert.Equal(0, automation.GoogleReadCount);
        Assert.Equal(0, automation.ProviderReadCount);
    }

    [Fact]
    public async Task Cancellation_is_observed_before_browser_access()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var automation = new FakeGoogleOAuthAutomation();

        await Assert.ThrowsAsync<OperationCanceledException>(() => automation.WaitAndConsentAsync(
            SyntheticStartUri,
            TimeSpan.FromSeconds(1),
            cancellation.Token));

        Assert.Equal(0, automation.GoogleReadCount);
        Assert.Equal(0, automation.ProviderReadCount);
    }

    [Fact]
    public async Task Totp_branch_generates_and_fills_code_then_completes()
    {
        var totpGeneratorCallCount = 0;
        var automation = new FakeGoogleOAuthAutomation(
            googleStates: [GoogleState(totp: true), GoogleState(totp: true), null],
            providerStates: [FakeProviderState.Completed],
            totpGenerator: () =>
            {
                totpGeneratorCallCount++;
                return Task.FromResult<string?>("123456");
            });

        var result = await automation.WaitAndConsentAsync(
            SyntheticStartUri,
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(["FillTotp"], automation.Actions);
        Assert.Equal(1, totpGeneratorCallCount);
    }

    [Fact]
    public async Task Consent_branch_clicks_google_consent_then_completes()
    {
        var automation = new FakeGoogleOAuthAutomation(
            googleStates: [GoogleState(consent: true), null],
            providerStates: [FakeProviderState.Completed]);

        var result = await automation.WaitAndConsentAsync(
            SyntheticStartUri,
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(["ClickGoogleConsent"], automation.Actions);
    }

    [Fact]
    public async Task Consent_click_is_idempotent_for_the_same_screen()
    {
        var automation = new FakeGoogleOAuthAutomation(
            googleStates: [GoogleState(consent: true), GoogleState(consent: true), null],
            providerStates: [FakeProviderState.Completed]);

        var result = await automation.WaitAndConsentAsync(
            SyntheticStartUri,
            TimeSpan.FromSeconds(1),
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(["ClickGoogleConsent"], automation.Actions);
    }

    private static TestAutomation CreateAutomation() =>
        new(new ChromeCdpClient(new Uri("http://127.0.0.1:9222")), "session", "target", "user@example.com");

    private static GoogleOAuthPageState GoogleState(
        bool accountPicker = false,
        bool totp = false,
        bool consent = false) => new()
        {
            CurrentUrl = "https://accounts.google.com/oauth/authorize",
            HasAccountPicker = accountPicker,
            HasGoogleTotpInput = totp,
            HasGoogleConsentButton = consent
        };

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

    private sealed class FakeGoogleOAuthAutomation : GoogleOAuthFlowAutomation
    {
        private readonly Queue<GoogleOAuthPageState?> _googleStates;
        private readonly Queue<ProviderOAuthPageState?> _providerStates;

        public FakeGoogleOAuthAutomation(
            IEnumerable<GoogleOAuthPageState?>? googleStates = null,
            IEnumerable<ProviderOAuthPageState?>? providerStates = null,
            Func<Task<string?>>? totpGenerator = null)
            : base(
                new ChromeCdpClient(new Uri("http://127.0.0.1:9222")),
                "synthetic-session",
                "synthetic-target",
                "synthetic-user@example.invalid",
                totpGenerator)
        {
            _googleStates = new Queue<GoogleOAuthPageState?>(googleStates ?? []);
            _providerStates = new Queue<ProviderOAuthPageState?>(providerStates ?? []);
        }

        public int GoogleReadCount { get; private set; }
        public int ProviderReadCount { get; private set; }
        public List<string> Actions { get; } = [];

        protected override Task<GoogleOAuthPageState?> ReadGooglePageStateAsync(
            CancellationToken cancellationToken)
        {
            GoogleReadCount++;
            return Task.FromResult(_googleStates.Count > 0 ? _googleStates.Dequeue() : null);
        }

        protected override Task<ProviderOAuthPageState?> ReadProviderPageStateAsync(
            CancellationToken cancellationToken)
        {
            ProviderReadCount++;
            return Task.FromResult(_providerStates.Count > 0 ? _providerStates.Dequeue() : null);
        }

        protected override CompletionCheckResult CheckCompletion(CombinedOAuthPageState state) =>
            state.ProviderState is FakeProviderState { IsComplete: true }
                ? new CompletionCheckResult(
                    IsComplete: true,
                    Result: new OAuthConsentResult(true, false, "Synthetic flow completed"))
                : new CompletionCheckResult(false);

        protected override void LogPageState(CombinedOAuthPageState state)
        {
        }

        protected override Task<bool> TryClickAccountPickerAsync(
            CombinedOAuthPageState state,
            CancellationToken cancellationToken)
        {
            Actions.Add("ClickAccountPicker");
            return Task.FromResult(false);
        }

        protected override Task<bool> TryFillGoogleTotpAsync(
            string totpCode,
            CancellationToken cancellationToken)
        {
            Actions.Add("FillTotp");
            return Task.FromResult(true);
        }

        protected override Task<bool> TryClickGoogleConsentAsync(
            CancellationToken cancellationToken)
        {
            Actions.Add("ClickGoogleConsent");
            return Task.FromResult(true);
        }

        protected override Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed record TestProviderState : ProviderOAuthPageState;

    private sealed record FakeProviderState : ProviderOAuthPageState
    {
        public static FakeProviderState Completed { get; } = new()
        {
            CurrentUrl = "https://provider.invalid/oauth/callback",
            IsComplete = true
        };

        public bool IsComplete { get; init; }
    }
}
