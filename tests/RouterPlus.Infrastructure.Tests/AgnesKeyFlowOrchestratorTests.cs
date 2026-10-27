using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Chrome;
using RouterPlus.Infrastructure.Services;

namespace RouterPlus.Infrastructure.Tests;

public sealed class AgnesKeyFlowOrchestratorTests
{
    private static readonly Uri KeysPageUri = new("https://platform.agnes-ai.com/settings/apiKeys");
    private static readonly GoogleLoginCredential Credential =
        new(profileId: "p1", email: "user@example.com", password: "pw", totpSecret: "JBSWY3DPEHPK3PXP");

    [Fact]
    public async Task RunAsync_when_already_on_keys_page_creates_key_without_google()
    {
        var browser = new FakeAgnesBrowser
        {
            State = OnKeysPage(hasNewKeyButton: true)
        };
        var google = new FakeGoogleLoginBrowser();

        var result = await AgnesKeyFlowOrchestrator.RunAsync(
            browser,
            Credential,
            google,
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("agnes_createdkeyvalue123456", result.ApiKey);
        Assert.False(browser.ClickedSignInWithGoogle);
        Assert.Equal(0, google.GoogleLoginRuns);
        Assert.True(browser.CreatedKey);
    }

    [Fact]
    public async Task RunAsync_clicks_google_then_autologins_and_creates_key()
    {
        var events = new List<string>();
        var browser = new FakeAgnesBrowser
        {
            State = OnSignInPage(),
            Events = events
        };
        var google = new FakeGoogleLoginBrowser();
        var authentication = new RecordingGoogleAuthenticationService { Events = events };

        var result = await AgnesKeyFlowOrchestrator.RunAsync(
            browser,
            Credential,
            google,
            CancellationToken.None,
            authentication);

        Assert.True(result.Success);
        Assert.Equal("agnes_createdkeyvalue123456", result.ApiKey);
        Assert.True(browser.ClickedSignInWithGoogle);
        Assert.Equal(1, authentication.CallCount);
        Assert.Equal(new[] { "google", "google", "create" }, events);
    }

    [Fact]
    public async Task RunAsync_returns_error_when_google_login_fails()
    {
        var browser = new FakeAgnesBrowser { State = OnSignInPage() };
        var google = new FakeGoogleLoginBrowser { Fail = true };
        var authentication = new RecordingGoogleAuthenticationService
        {
            Result = GoogleLoginResult.InvalidCredentials()
        };

        var result = await AgnesKeyFlowOrchestrator.RunAsync(
            browser,
            Credential,
            google,
            CancellationToken.None,
            authentication);

        Assert.False(result.Success);
        Assert.Contains("sign-in", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(browser.CreatedKey);
    }

    [Fact]
    public async Task RunAsync_returns_error_when_not_on_keys_page()
    {
        var browser = new FakeAgnesBrowser
        {
            State = new AgnesApiKeyPageState(
                new Uri("https://platform.agnes-ai.com/dashboard"),
                IsOnKeysPage: false,
                HasGoogleSignIn: false,
                ExistingKeyCount: 0,
                HasNewKeyButton: false,
                HasNewKeyNameInput: false,
                HasCreatedKeyPanel: false,
                ApiKey: string.Empty)
        };
        var google = new FakeGoogleLoginBrowser();

        var result = await AgnesKeyFlowOrchestrator.RunAsync(
            browser,
            Credential,
            google,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("keys page", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_returns_error_when_google_button_cannot_be_clicked()
    {
        // On sign-in page with Google button visible, but clicking it fails (click=false)
        var browser = new FakeAgnesBrowserClickFails
        {
            State = OnSignInPage()
        };
        var google = new FakeGoogleLoginBrowser();

        var result = await AgnesKeyFlowOrchestrator.RunAsync(
            browser,
            Credential,
            google,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Could not find the 'Continue with Google' button", result.ErrorMessage);
        Assert.False(browser.CreatedKey);
    }

    [Fact]
    public async Task RunAsync_returns_error_when_not_on_keys_page_and_no_google_button()
    {
        // On dashboard (not keys page), no Google button
        var browser = new FakeAgnesBrowser
        {
            State = new AgnesApiKeyPageState(
                new Uri("https://platform.agnes-ai.com/dashboard"),
                IsOnKeysPage: false,
                HasGoogleSignIn: false,
                ExistingKeyCount: 0,
                HasNewKeyButton: false,
                HasNewKeyNameInput: false,
                HasCreatedKeyPanel: false,
                ApiKey: string.Empty)
        };
        var google = new FakeGoogleLoginBrowser();

        var result = await AgnesKeyFlowOrchestrator.RunAsync(
            browser,
            Credential,
            google,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("keys page", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_returns_error_when_signed_in_but_no_new_key_button()
    {
        // On keys page, no Google button (signed in), but no new key button either (edge case)
        var browser = new FakeAgnesBrowserNoNewKeyButton
        {
            State = new AgnesApiKeyPageState(
                KeysPageUri,
                IsOnKeysPage: true,
                HasGoogleSignIn: false, // Signed in
                ExistingKeyCount: 0,
                HasNewKeyButton: false, // But no new key button - weird state
                HasNewKeyNameInput: false,
                HasCreatedKeyPanel: false,
                ApiKey: string.Empty)
        };
        var google = new FakeGoogleLoginBrowser();

        var result = await AgnesKeyFlowOrchestrator.RunAsync(
            browser,
            Credential,
            google,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Create new secret key", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_uses_email_for_key_name_not_profile_id()
    {
        var events = new List<string>();
        var browser = new FakeAgnesBrowser
        {
            State = OnSignInPage(),
            Events = events
        };
        var google = new FakeGoogleLoginBrowser();
        var authentication = new RecordingGoogleAuthenticationService { Events = events };

        // Create credential with different ProfileId and Email
        var credential = new GoogleLoginCredential(
            profileId: "different-profile-id",
            email: "actual-email@example.com",
            password: "pw",
            totpSecret: "JBSWY3DPEHPK3PXP");

        var result = await AgnesKeyFlowOrchestrator.RunAsync(
            browser,
            credential,
            google,
            CancellationToken.None,
            authentication);

        Assert.True(result.Success);
        Assert.Equal("actual-email@example.com-agnes", browser.CreatedKeyName);
    }

    [Fact]
    public async Task RunAsync_exact_url_matching_accepts_exact_host_and_path()
    {
        var browser = new FakeAgnesBrowser
        {
            State = new AgnesApiKeyPageState(
                KeysPageUri,
                IsOnKeysPage: true,
                HasGoogleSignIn: false,
                ExistingKeyCount: 0,
                HasNewKeyButton: true,
                HasNewKeyNameInput: false,
                HasCreatedKeyPanel: false,
                ApiKey: string.Empty)
        };
        var google = new FakeGoogleLoginBrowser();

        var result = await AgnesKeyFlowOrchestrator.RunAsync(
            browser,
            Credential,
            google,
            CancellationToken.None);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task RunAsync_exact_url_matching_rejects_wrong_host()
    {
        var browser = new FakeAgnesBrowser
        {
            State = new AgnesApiKeyPageState(
                new Uri("https://wrong-host.example.com/settings/apiKeys"),
                IsOnKeysPage: false, // Exact URL match will reject this
                HasGoogleSignIn: false,
                ExistingKeyCount: 0,
                HasNewKeyButton: true,
                HasNewKeyNameInput: false,
                HasCreatedKeyPanel: false,
                ApiKey: string.Empty)
        };
        var google = new FakeGoogleLoginBrowser();

        var result = await AgnesKeyFlowOrchestrator.RunAsync(
            browser,
            Credential,
            google,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("keys page", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_exact_url_matching_rejects_wrong_path()
    {
        var browser = new FakeAgnesBrowser
        {
            State = new AgnesApiKeyPageState(
                new Uri("https://platform.agnes-ai.com/settings/wrong-path"),
                IsOnKeysPage: false, // Exact URL match will reject this
                HasGoogleSignIn: false,
                ExistingKeyCount: 0,
                HasNewKeyButton: true,
                HasNewKeyNameInput: false,
                HasCreatedKeyPanel: false,
                ApiKey: string.Empty)
        };
        var google = new FakeGoogleLoginBrowser();

        var result = await AgnesKeyFlowOrchestrator.RunAsync(
            browser,
            Credential,
            google,
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("keys page", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeAgnesBrowserNoNewKeyButton : IAgnesApiKeyBrowser
    {
        public AgnesApiKeyPageState State { get; set; } = null!;
        public bool ClickedSignInWithGoogle { get; private set; }
        public bool CreatedKey { get; private set; }
        public string? CreatedKeyName { get; private set; }
        public List<string>? Events { get; init; }

        public Task<AgnesApiKeyPageState> ReadStateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(State);

        public Task<bool> TryClickSignInWithGoogleAsync(CancellationToken cancellationToken)
        {
            if (!State.HasGoogleSignIn)
            {
                return Task.FromResult(false);
            }

            ClickedSignInWithGoogle = true;
            Events?.Add("google");
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

            State = OnKeysPage(hasNewKeyButton: true, existingKeyCount: State.ExistingKeyCount - 1);
            return Task.FromResult(true);
        }

        public Task<bool> TryClickNewKeyAsync(CancellationToken cancellationToken)
        {
            if (!State.HasNewKeyButton)
            {
                return Task.FromResult(false);
            }
            State = OnKeysPage(hasNewKeyNameInput: true);
            return Task.FromResult(true);
        }

        public Task<bool> TryCreateKeyAsync(string name, CancellationToken cancellationToken)
        {
            CreatedKey = true;
            CreatedKeyName = name;
            Events?.Add("create");
            State = OnKeysPage(hasCreatedKeyPanel: true, apiKey: "agnes_createdkeyvalue123456");
            return Task.FromResult(true);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeAgnesBrowserClickFails : IAgnesApiKeyBrowser
    {
        public AgnesApiKeyPageState State { get; set; } = null!;
        public bool ClickedSignInWithGoogle { get; private set; }
        public bool CreatedKey { get; private set; }
        public List<string>? Events { get; init; }

        public Task<AgnesApiKeyPageState> ReadStateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(State);

        public Task<bool> TryClickSignInWithGoogleAsync(CancellationToken cancellationToken)
        {
            // Always return false - simulates button exists but click fails
            return Task.FromResult(false);
        }

        public Task<bool> WaitForGoogleSignInAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> WaitForKeysPageAsync(CancellationToken cancellationToken)
        {
            State = OnKeysPage(hasNewKeyButton: true);
            return Task.FromResult(true);
        }

        public Task<bool> TryDeleteOneExistingKeyAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(false);
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
            State = OnKeysPage(hasCreatedKeyPanel: true, apiKey: "agnes_createdkeyvalue123456");
            return Task.FromResult(true);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeAgnesBrowser : IAgnesApiKeyBrowser
    {
        public AgnesApiKeyPageState State { get; set; } = null!;
        public bool ClickedSignInWithGoogle { get; private set; }
        public bool CreatedKey { get; private set; }
        public string? CreatedKeyName { get; private set; }
        public List<string>? Events { get; init; }

        public Task<AgnesApiKeyPageState> ReadStateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(State);

        public Task<bool> TryClickSignInWithGoogleAsync(CancellationToken cancellationToken)
        {
            if (!State.HasGoogleSignIn)
            {
                return Task.FromResult(false);
            }

            ClickedSignInWithGoogle = true;
            Events?.Add("google");
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
            CreatedKeyName = name;
            Events?.Add("create");
            State = OnKeysPage(hasCreatedKeyPanel: true, apiKey: "agnes_createdkeyvalue123456");
            return Task.FromResult(true);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static AgnesApiKeyPageState OnSignInPage() =>
        new(
            KeysPageUri,
            IsOnKeysPage: true,
            HasGoogleSignIn: true,
            ExistingKeyCount: 0,
            HasNewKeyButton: false,
            HasNewKeyNameInput: false,
            HasCreatedKeyPanel: false,
            ApiKey: string.Empty);

    private static AgnesApiKeyPageState OnKeysPage(
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

    // ===== Integration tests for popup factory overload =====

    /// <summary>
    /// Fake browser that simulates the ConnectAgnesFlowAsync -> popup factory flow.
    /// Captures the pre-click snapshot and uses it in the popup factory.
    /// </summary>
    private sealed class FakeAgnesBrowserWithPopupFactory : IAgnesApiKeyBrowser
    {
        public AgnesApiKeyPageState State { get; set; } = null!;
        public bool ClickedSignInWithGoogle { get; private set; }
        public bool CreatedKey { get; private set; }
        public string? CreatedKeyName { get; private set; }
        public List<string> Events { get; } = new();
        public Func<CancellationToken, Task<IGoogleLoginBrowser>>? PopupFactory { get; set; }
        public HashSet<string>? CapturedPreClickTargets { get; set; }

        public Task<AgnesApiKeyPageState> ReadStateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(State);

        public Task<bool> TryClickSignInWithGoogleAsync(CancellationToken cancellationToken)
        {
            if (!State.HasGoogleSignIn)
            {
                return Task.FromResult(false);
            }

            ClickedSignInWithGoogle = true;
            Events.Add("google");

            // Simulate: after click, capture pre-click snapshot (in real code this is done BEFORE click)
            // For this test, we'll just return true and let the factory handle it
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

            State = OnKeysPage(hasNewKeyButton: true, existingKeyCount: State.ExistingKeyCount - 1);
            return Task.FromResult(true);
        }

        public Task<bool> TryClickNewKeyAsync(CancellationToken cancellationToken)
        {
            if (!State.HasNewKeyButton)
            {
                return Task.FromResult(false);
            }
            State = OnKeysPage(hasNewKeyNameInput: true);
            return Task.FromResult(true);
        }

        public Task<bool> TryCreateKeyAsync(string name, CancellationToken cancellationToken)
        {
            CreatedKey = true;
            CreatedKeyName = name;
            Events.Add("create");
            State = OnKeysPage(hasCreatedKeyPanel: true, apiKey: "agnes_createdkeyvalue123456");
            return Task.FromResult(true);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// Fake Google login browser returned by the popup factory for integration testing.
    /// </summary>
    private sealed class FakePopupGoogleLoginBrowser : IGoogleLoginBrowser
    {
        public int ReadStateCallCount { get; private set; }
        public bool Fail { get; set; }

        public Task<GoogleLoginPageState> ReadStateAsync(CancellationToken cancellationToken)
        {
            ReadStateCallCount++;
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

    [Fact]
    public async Task RunAsync_with_popup_factory_clicks_google_then_autologins_and_creates_key()
    {
        // This test exercises the popup factory overload (RunAsync with Func<CancellationToken, Task<IGoogleLoginBrowser>>)
        // Orchestrator clicks Google -> factory returns a fake popup browser -> Google auth -> returns to Agnes -> key created
        // Assert exact key name {email}-agnes

        var browser = new FakeAgnesBrowserWithPopupFactory
        {
            State = OnSignInPage()
        };
        
        var popupGoogleBrowser = new FakePopupGoogleLoginBrowser();
        var authentication = new RecordingGoogleAuthenticationService { Events = browser.Events };

        // Popup factory that returns our fake Google login browser
        browser.PopupFactory = (ct) =>
        {
            browser.Events.Add("popup_factory_called");
            return Task.FromResult<IGoogleLoginBrowser>(popupGoogleBrowser);
        };

        var credential = new GoogleLoginCredential(
            profileId: "test-profile",
            email: "user@example.com",
            password: "pw",
            totpSecret: "JBSWY3DPEHPK3PXP");

        var result = await AgnesKeyFlowOrchestrator.RunAsync(
            browser,
            credential,
            browser.PopupFactory!,
            CancellationToken.None,
            authentication);

        Assert.True(result.Success);
        Assert.Equal("agnes_createdkeyvalue123456", result.ApiKey);
        Assert.True(browser.ClickedSignInWithGoogle);
        Assert.Equal(1, authentication.CallCount);
        // Events: "google" (click), "popup_factory_called", "google" (auth), "create"
        Assert.Equal(new[] { "google", "popup_factory_called", "google", "create" }, browser.Events);
        Assert.Equal("user@example.com-agnes", browser.CreatedKeyName);
    }

    [Fact]
    public async Task RunAsync_with_popup_factory_returns_error_when_no_popup_appears()
    {
        // No-popup failure: assert error result with clear message, no key
        var browser = new FakeAgnesBrowserWithPopupFactory
        {
            State = OnSignInPage()
        };
        
        var authentication = new RecordingGoogleAuthenticationService { Events = browser.Events };

        // Popup factory that throws (simulating popup not appearing)
        browser.PopupFactory = (ct) =>
        {
            browser.Events.Add("popup_factory_called");
            throw new InvalidOperationException("Google login popup (accounts.google.com) did not appear within 30 seconds.");
        };

        var credential = new GoogleLoginCredential(
            profileId: "test-profile",
            email: "user@example.com",
            password: "pw",
            totpSecret: "JBSWY3DPEHPK3PXP");

        var result = await AgnesKeyFlowOrchestrator.RunAsync(
            browser,
            credential,
            browser.PopupFactory!,
            CancellationToken.None,
            authentication);

        Assert.False(result.Success);
        Assert.Contains("Google login popup", result.ErrorMessage);
        Assert.False(browser.CreatedKey);
        Assert.Equal(new[] { "google", "popup_factory_called" }, browser.Events);
    }
}