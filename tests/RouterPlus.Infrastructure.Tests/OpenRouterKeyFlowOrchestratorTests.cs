using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Chrome;
using RouterPlus.Infrastructure.Services;

namespace RouterPlus.Infrastructure.Tests;

/// <summary>
/// Standalone tests for <see cref="OpenRouterKeyFlowOrchestrator"/> —
/// click Clerk "Sign in with Google" → Google autologin via Vault → OpenRouter onboarding.
/// Kept separate from the shared E2E suite so the flow is verifiable without launching a browser.
/// </summary>
public sealed class OpenRouterKeyFlowOrchestratorTests
{
    private static readonly Uri KeysPageUri = new("https://openrouter.ai/workspaces/default/keys");
    private static readonly GoogleLoginCredential Credential =
        new(profileId: "p1", email: "user@example.com", password: "pw", totpSecret: "JBSWY3DPEHPK3PXP");

    [Fact]
    public async Task RunAsync_when_key_visible_on_keys_page_returns_it_without_login()
    {
        var browser = new FakeKeyFlowBrowser();
        browser.OnboardingState = OnKeysPage(apiKey: "sk-or-v1-existing");
        var google = new FakeGoogleLoginBrowser();

        var result = await OpenRouterKeyFlowOrchestrator.RunAsync(
            browser,
            null,
            Credential,
            googleLogin: google,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("sk-or-v1-existing", result.ApiKey);
        Assert.False(browser.ClickedSignInWithGoogle);
        Assert.Equal(0, google.GoogleLoginRuns);
    }

    [Fact]
    public async Task RunAsync_clicks_google_then_autologins_with_vault_and_returns_created_key()
    {
        var events = new List<string>();
        var browser = new FakeKeyFlowBrowser { Events = events };
        browser.OnboardingState = OnKeysPage(); // no key yet -> start sign-in flow
        var google = new FakeGoogleLoginBrowser(); // completes successfully
        var authentication = new RecordingGoogleAuthenticationService { Events = events };

        var result = await OpenRouterKeyFlowOrchestrator.RunAsync(
            browser,
            null,
            Credential,
            googleLogin: google,
            CancellationToken.None,
            authentication);

        Assert.True(result.Success);
        Assert.Equal("sk-or-v1-created", result.ApiKey);
        Assert.True(browser.ClickedSignInWithGoogle);
        Assert.Equal(1, authentication.CallCount);
        Assert.Same(google, authentication.LastRequest!.Browser);
        Assert.Equal(Credential, authentication.LastRequest.Credential);
        Assert.Equal(new[] { "google", "onboarding" }, events);
    }

    [Fact]
    public async Task RunAsync_returns_error_when_google_login_fails()
    {
        var browser = new FakeKeyFlowBrowser();
        browser.OnboardingState = OnKeysPage();
        var google = new FakeGoogleLoginBrowser { Fail = true };
        var authentication = new RecordingGoogleAuthenticationService
        {
            Result = GoogleLoginResult.InvalidCredentials()
        };

        var result = await OpenRouterKeyFlowOrchestrator.RunAsync(
            browser,
            null,
            Credential,
            googleLogin: google,
            CancellationToken.None,
            authentication);

        Assert.False(result.Success);
        Assert.Contains("sign-in", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, authentication.CallCount);
        Assert.False(browser.OnboardingStarted);
    }

    [Fact]
    public async Task RunAsync_returns_error_when_onboarding_fails()
    {
        var browser = new FakeKeyFlowBrowser();
        browser.OnboardingState = OnKeysPage();
        browser.FailOnboarding = true;
        var google = new FakeGoogleLoginBrowser();

        var result = await OpenRouterKeyFlowOrchestrator.RunAsync(
            browser,
            null,
            Credential,
            googleLogin: google,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("key", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ==================== Helpers ====================

    private sealed class RecordingGoogleAuthenticationService : IGoogleAuthenticationService
    {
        public GoogleLoginResult Result { get; set; } = GoogleLoginResult.Success();
        public int CallCount { get; private set; }
        public GoogleAuthenticationRequest? LastRequest { get; private set; }
        public List<string>? Events { get; init; }

        public Task<GoogleLoginResult> AuthenticateAsync(
            GoogleAuthenticationRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            Events?.Add("google");
            return Task.FromResult(Result);
        }
    }

    private static OpenRouterOnboardingPageState OnKeysPage(
        bool hasNewKeyButton = false,
        bool hasNewKeyNameInput = false,
        bool hasCreatedKeyPanel = false,
        string apiKey = "")
    {
        return new OpenRouterOnboardingPageState(
            KeysPageUri,
            IsOnKeysPage: true,
            HasWelcomeWizard: false,
            HasWelcomeNext: false,
            HasKeyCopyPanel: !string.IsNullOrEmpty(apiKey),
            HasWelcomeContinue: false,
            HasDoLaterOption: false,
            HasNotSureOption: false,
            HasNewKeyButton: hasNewKeyButton,
            HasNewKeyNameInput: hasNewKeyNameInput,
            HasCreatedKeyPanel: hasCreatedKeyPanel,
            ApiKey: apiKey);
    }

    private sealed class FakeKeyFlowBrowser : IOpenRouterOnboardingBrowser
    {
        public OpenRouterOnboardingPageState OnboardingState { get; set; } = null!;
        public bool FailOnboarding { get; set; }
        public bool ClickedSignInWithGoogle { get; private set; }
        public bool OnboardingStarted { get; private set; }
        public List<string>? Events { get; init; }

        public Task<OpenRouterOnboardingPageState> ReadStateAsync(CancellationToken ct) =>
            Task.FromResult(OnboardingState);

        public Task ReloadAsync(CancellationToken ct) => Task.CompletedTask;

        public Task<bool> TryClickNextAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<bool> TryClickContinueAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<bool> TryClickDoLaterAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<bool> TryClickNotSureAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<bool> TryClickNewKeyAsync(CancellationToken ct)
        {
            OnboardingStarted = true;
            Events?.Add("onboarding");
            OnboardingState = OnKeysPage(hasNewKeyNameInput: true);
            return Task.FromResult(true);
        }

        public Task<bool> TryCreateKeyAsync(string name, CancellationToken ct)
        {
            OnboardingState = FailOnboarding
                ? OnKeysPage(hasCreatedKeyPanel: false)
                : OnKeysPage(hasCreatedKeyPanel: true, apiKey: "sk-or-v1-created");
            return Task.FromResult(!FailOnboarding);
        }

        public Task<bool> TryClickSignInWithGoogleAsync(CancellationToken ct)
        {
            ClickedSignInWithGoogle = true;
            return Task.FromResult(true);
        }

        public Task<string> GetCurrentUrlAsync(CancellationToken ct) =>
            Task.FromResult(OnboardingState.PageUri.ToString());

        public Task<bool> NavigateToOpenRouterSignInAsync(CancellationToken ct) => Task.FromResult(true);

        public Task<bool> WaitForGoogleSignInAsync(CancellationToken ct) => Task.FromResult(true);

        public Task<bool> WaitForOpenRouterKeysAsync(CancellationToken ct)
        {
            // After Google auth, the keys page becomes reachable and shows a "New Key" button.
            OnboardingState = OnKeysPage(hasNewKeyButton: true);
            return Task.FromResult(true);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeGoogleLoginBrowser : IGoogleLoginBrowser
    {
        public bool Fail { get; set; }
        public int GoogleLoginRuns { get; private set; }

        public Task<GoogleLoginPageState> ReadStateAsync(CancellationToken ct)
        {
            GoogleLoginRuns++;
            return Task.FromResult(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/v3/signin/identifier"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: !Fail,
                HasManualChallenge: Fail));
        }

        public Task FillAsync(GoogleLoginField field, string value, CancellationToken ct) => Task.CompletedTask;
        public Task SubmitAsync(GoogleLoginField field, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> TrySelectAuthenticatorMethodAsync(CancellationToken ct) => Task.FromResult(true);
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}