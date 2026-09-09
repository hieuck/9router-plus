using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Chrome;
using RouterPlus.Infrastructure.Services;

namespace RouterPlus.Infrastructure.Tests;

public sealed class OllamaKeyFlowOrchestratorTests
{
    private static readonly Uri KeysPageUri = new("https://ollama.com/settings/keys");
    private static readonly GoogleLoginCredential Credential =
        new(profileId: "p1", email: "user@example.com", password: "pw", totpSecret: "JBSWY3DPEHPK3PXP");

    [Fact]
    public async Task RunAsync_when_already_on_keys_page_deletes_old_keys_then_creates_without_google()
    {
        var browser = new FakeOllamaBrowser
        {
            State = OnKeysPage(hasNewKeyButton: true, existingKeyCount: 1)
        };
        var google = new FakeGoogleLoginBrowser();

        var result = await OllamaKeyFlowOrchestrator.RunAsync(
            browser,
            Credential,
            google,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("ol_createdkeyvalue123456", result.ApiKey);
        Assert.False(browser.ClickedSignInWithGoogle);
        Assert.Equal(0, google.GoogleLoginRuns);
        Assert.True(browser.DeletedExistingKeys);
        Assert.True(browser.CreatedKey);
    }

    [Fact]
    public async Task RunAsync_clicks_google_then_autologins_and_creates_key()
    {
        var events = new List<string>();
        var browser = new FakeOllamaBrowser
        {
            State = OnSignInPage(),
            Events = events
        };
        var google = new FakeGoogleLoginBrowser();
        var authentication = new RecordingGoogleAuthenticationService { Events = events };

        var result = await OllamaKeyFlowOrchestrator.RunAsync(
            browser,
            Credential,
            google,
            CancellationToken.None,
            authentication);

        Assert.True(result.Success);
        Assert.Equal("ol_createdkeyvalue123456", result.ApiKey);
        Assert.True(browser.ClickedSignInWithGoogle);
        Assert.Equal(1, authentication.CallCount);
        Assert.Equal(new[] { "google", "create" }, events);
    }

    [Fact]
    public async Task RunAsync_returns_error_when_google_login_fails()
    {
        var browser = new FakeOllamaBrowser { State = OnSignInPage() };
        var google = new FakeGoogleLoginBrowser { Fail = true };
        var authentication = new RecordingGoogleAuthenticationService
        {
            Result = GoogleLoginResult.InvalidCredentials()
        };

        var result = await OllamaKeyFlowOrchestrator.RunAsync(
            browser,
            Credential,
            google,
            CancellationToken.None,
            authentication);

        Assert.False(result.Success);
        Assert.Contains("sign-in", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(browser.CreatedKey);
    }

    private static OllamaApiKeyPageState OnSignInPage() =>
        new(
            new Uri("https://ollama.com/sign-in"),
            IsOnKeysPage: false,
            HasGoogleSignIn: true,
            ExistingKeyCount: 0,
            HasNewKeyButton: false,
            HasNewKeyNameInput: false,
            HasCreatedKeyPanel: false,
            ApiKey: string.Empty);

    private static OllamaApiKeyPageState OnKeysPage(
        bool hasNewKeyButton = false,
        bool hasNewKeyNameInput = false,
        bool hasCreatedKeyPanel = false,
        string apiKey = "",
        int existingKeyCount = 0) =>
        new(
            KeysPageUri,
            IsOnKeysPage: true,
            HasGoogleSignIn: false,
            ExistingKeyCount: existingKeyCount,
            HasNewKeyButton: hasNewKeyButton,
            HasNewKeyNameInput: hasNewKeyNameInput,
            HasCreatedKeyPanel: hasCreatedKeyPanel,
            ApiKey: apiKey);

    private sealed class RecordingGoogleAuthenticationService : IGoogleAuthenticationService
    {
        public GoogleLoginResult Result { get; set; } = GoogleLoginResult.Success();
        public int CallCount { get; private set; }
        public List<string>? Events { get; init; }

        public Task<GoogleLoginResult> AuthenticateAsync(
            GoogleAuthenticationRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Events?.Add("google");
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeOllamaBrowser : IOllamaApiKeyBrowser
    {
        public OllamaApiKeyPageState State { get; set; } = null!;
        public bool ClickedSignInWithGoogle { get; private set; }
        public bool DeletedExistingKeys { get; private set; }
        public bool CreatedKey { get; private set; }
        public List<string>? Events { get; init; }

        public Task<OllamaApiKeyPageState> ReadStateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(State);

        public Task<bool> TryClickSignInWithGoogleAsync(CancellationToken cancellationToken)
        {
            if (!State.HasGoogleSignIn)
            {
                return Task.FromResult(false);
            }

            ClickedSignInWithGoogle = true;
            return Task.FromResult(true);
        }

        public Task<bool> WaitForGoogleSignInAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> WaitForKeysPageAsync(CancellationToken cancellationToken)
        {
            State = OnKeysPage(hasNewKeyButton: true);
            return Task.FromResult(true);
        }

        public Task<bool> TryDeleteOneExistingKeyAsync(CancellationToken cancellationToken)
        {
            if (State.ExistingKeyCount <= 0)
            {
                return Task.FromResult(false);
            }

            DeletedExistingKeys = true;
            State = OnKeysPage(hasNewKeyButton: true, existingKeyCount: State.ExistingKeyCount - 1);
            return Task.FromResult(true);
        }

        public Task<bool> TryClickNewKeyAsync(CancellationToken cancellationToken)
        {
            State = OnKeysPage(hasNewKeyNameInput: true);
            return Task.FromResult(true);
        }

        public Task<bool> TryCreateKeyAsync(string name, CancellationToken cancellationToken)
        {
            CreatedKey = true;
            Events?.Add("create");
            State = OnKeysPage(hasCreatedKeyPanel: true, apiKey: "ol_createdkeyvalue123456");
            return Task.FromResult(true);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeGoogleLoginBrowser : IGoogleLoginBrowser
    {
        public bool Fail { get; set; }
        public int GoogleLoginRuns { get; private set; }

        public Task<GoogleLoginPageState> ReadStateAsync(CancellationToken cancellationToken)
        {
            GoogleLoginRuns++;
            return Task.FromResult(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/v3/signin/identifier"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: !Fail,
                HasManualChallenge: Fail));
        }

        public Task FillAsync(GoogleLoginField field, string value, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task SubmitAsync(GoogleLoginField field, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<bool> TrySelectAuthenticatorMethodAsync(CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
