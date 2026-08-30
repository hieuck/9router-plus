using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.Infrastructure.Tests;

public sealed class OpenRouterOnboardingAutomationTests
{
    private static readonly Uri KeysPage = new("https://openrouter.ai/workspaces/default/keys");

    // ==================== Wizard path ====================

    [Fact]
    public async Task RunAsync_follows_welcome_wizard_to_key()
    {
        // Wizard appears (Welcome + Next) and walks the user through:
        // Next -> Continue -> I'll do this later -> Other/Not Sure -> Continue.
        var browser = new FakeOnboardingBrowser()
            .ReturnState(OnKeysPage(wizard: false))               // initial keys page, wizard not yet shown
            .ReturnState(OnKeysPage(wizard: true, next: true))     // after reload: Welcome -> Next
            .ReturnState(OnKeysPage(wizard: true, wizardContinue: true, keyCopyPanel: true, apiKey: "sk-or-v1-abc")) // after Next -> Continue + copy panel with key
            .ReturnState(OnKeysPage(wizard: true, doLater: true))  // after Continue
            .ReturnState(OnKeysPage(wizard: true, notSure: true))  // after DoLater
            .ReturnState(OnKeysPage(wizard: true, wizardContinue: true)) // after NotSure
            .ReturnState(OnKeysPage(wizard: false));               // after final Continue -> wizard done

        var result = await OpenRouterOnboardingAutomation.RunAsync(browser, "Main", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("sk-or-v1-abc", result.ApiKey);
        Assert.Equal(new[] { "Next", "Continue", "DoLater", "NotSure", "Continue" }, browser.ClickedActions);
        Assert.Equal(1, browser.ReloadCount);
    }

    [Fact]
    public async Task RunAsync_when_wizard_never_appears_uses_new_key_fallback()
    {
        // keys page stays without wizard across N reloads -> New Key fallback.
        var browser = new FakeOnboardingBrowser()
            .ReturnState(OnKeysPage(wizard: false))      // initial
            .ReturnState(OnKeysPage(wizard: false))      // reload 1
            .ReturnState(OnKeysPage(wizard: false))      // reload 2
            .ReturnState(OnKeysPage(wizard: false))      // reload 3 -> wizard never appears
            .ReturnState(OnKeysPage(newKeyButton: true)) // New Key clickable
            .ReturnState(OnKeysPage(newKeyNameInput: true)) // popup open
            .ReturnState(OnKeysPage(createdKeyPanel: true, apiKey: "sk-or-v1-new"));

        var result = await OpenRouterOnboardingAutomation.RunAsync(browser, "Main", CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("sk-or-v1-new", result.ApiKey);
        Assert.Equal(new[] { "NewKey", "CreateKey" }, browser.ClickedActions);
        Assert.Equal(3, browser.ReloadCount); // initial load + 3 reloads, then we give up on the wizard
    }

    [Fact]
    public async Task RunAsync_returns_error_when_wizard_never_appears_and_new_key_unreachable()
    {
        var browser = new FakeOnboardingBrowser()
            .ReturnState(OnKeysPage(wizard: false))      // initial
            .ReturnState(OnKeysPage(wizard: false))      // reload 1
            .ReturnState(OnKeysPage(wizard: false))      // reload 2
            .ReturnState(OnKeysPage(wizard: false))      // reload 3 -> wizard never appears
            .ReturnState(OnKeysPage(newKeyButton: true)) // New Key clickable
            .ReturnState(OnKeysPage(newKeyNameInput: true)) // popup open
            .ReturnState(OnKeysPage(createdKeyPanel: true, apiKey: "sk-or-v1"));

        // TryCreateKey returns false -> create fails
        browser.FailCreateKey = true;

        var result = await OpenRouterOnboardingAutomation.RunAsync(browser, "Main", CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("create", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_returns_error_on_timeout_when_keys_never_render()
    {
        var browser = new FakeOnboardingBrowser()
            .ReturnState(new OpenRouterOnboardingPageState(
                new Uri("https://openrouter.ai/login"),
                IsOnKeysPage: false,
                HasWelcomeWizard: false,
                HasWelcomeNext: false,
                HasKeyCopyPanel: false,
                HasWelcomeContinue: false,
                HasDoLaterOption: false,
                HasNotSureOption: false,
                HasNewKeyButton: false,
                HasNewKeyNameInput: false,
                HasCreatedKeyPanel: false,
                ApiKey: string.Empty));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        var result = await OpenRouterOnboardingAutomation.RunAsync(browser, "Main", cts.Token);

        Assert.False(result.Success);
        Assert.Contains("timeout", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ==================== Helpers ====================

    private static OpenRouterOnboardingPageState OnKeysPage(
        bool wizard = false,
        bool next = false,
        bool keyCopyPanel = false,
        bool wizardContinue = false,
        bool doLater = false,
        bool notSure = false,
        bool newKeyButton = false,
        bool newKeyNameInput = false,
        bool createdKeyPanel = false,
        string apiKey = "")
    {
        return new OpenRouterOnboardingPageState(
            KeysPage,
            IsOnKeysPage: true,
            HasWelcomeWizard: wizard,
            HasWelcomeNext: next,
            HasKeyCopyPanel: keyCopyPanel,
            HasWelcomeContinue: wizardContinue,
            HasDoLaterOption: doLater,
            HasNotSureOption: notSure,
            HasNewKeyButton: newKeyButton,
            HasNewKeyNameInput: newKeyNameInput,
            HasCreatedKeyPanel: createdKeyPanel,
            ApiKey: apiKey);
    }

    private sealed class FakeOnboardingBrowser : IOpenRouterOnboardingBrowser
    {
        private readonly Queue<OpenRouterOnboardingPageState> _states = new();
        private readonly List<string> _clickedActions = new();
        private OpenRouterOnboardingPageState? _lastState;

        public IReadOnlyList<string> ClickedActions => _clickedActions;
        public int ReloadCount { get; private set; }
        public bool FailCreateKey { get; set; }

        public FakeOnboardingBrowser ReturnState(OpenRouterOnboardingPageState state)
        {
            _states.Enqueue(state);
            return this;
        }

        public Task<OpenRouterOnboardingPageState> ReadStateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_states.Count > 0)
            {
                _lastState = _states.Dequeue();
                return Task.FromResult(_lastState);
            }

            // Replay the last observed state (the page keeps showing what it was showing).
            return Task.FromResult(_lastState ?? OnKeysPage());
        }

        public Task ReloadAsync(CancellationToken cancellationToken)
        {
            ReloadCount++;
            return Task.CompletedTask;
        }

        public Task<bool> TryClickNextAsync(CancellationToken cancellationToken)
        {
            _clickedActions.Add("Next");
            return Task.FromResult(true);
        }

        public Task<bool> TryClickContinueAsync(CancellationToken cancellationToken)
        {
            _clickedActions.Add("Continue");
            return Task.FromResult(true);
        }

        public Task<bool> TryClickDoLaterAsync(CancellationToken cancellationToken)
        {
            _clickedActions.Add("DoLater");
            return Task.FromResult(true);
        }

        public Task<bool> TryClickNotSureAsync(CancellationToken cancellationToken)
        {
            _clickedActions.Add("NotSure");
            return Task.FromResult(true);
        }

        public Task<bool> TryClickNewKeyAsync(CancellationToken cancellationToken)
        {
            _clickedActions.Add("NewKey");
            return Task.FromResult(true);
        }

        public Task<bool> TryCreateKeyAsync(string name, CancellationToken cancellationToken)
        {
            _clickedActions.Add("CreateKey");
            return Task.FromResult(!FailCreateKey);
        }

        public Task<bool> TryClickSignInWithGoogleAsync(CancellationToken cancellationToken)
        {
            _clickedActions.Add("SignInWithGoogle");
            return Task.FromResult(true);
        }

        public Task<string> GetCurrentUrlAsync(CancellationToken cancellationToken) =>
            Task.FromResult(KeysPage.ToString());

        public Task<bool> WaitForGoogleSignInAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> WaitForOpenRouterKeysAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> NavigateToOpenRouterSignInAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}