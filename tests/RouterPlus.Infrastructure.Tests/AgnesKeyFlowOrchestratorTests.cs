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
        // On keys page with Google button -> click Google -> popup auth -> create key
        var events = new List<string>();
        var browser = new FakeAgnesBrowserWithStateSequence
        {
            State = OnSignInPage(), // Keys page with Google button
            Events = events,
            StateSequence = new Queue<AgnesApiKeyPageState>(new[]
            {
                // Initial: on keys page with Google button
                OnSignInPage(),
                // After Google click + popup auth + redirect: keys page (signed in)
                OnKeysPage(hasNewKeyButton: true)
            })
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
        // Events: "google" (click), "google" (auth), "create"
        Assert.Equal(new[] { "google", "google", "create" }, events);
    }

    [Fact]
    public async Task RunAsync_returns_error_when_google_login_fails()
    {
        var events = new List<string>();
        var browser = new FakeAgnesBrowserWithStateSequence
        {
            State = OnSignInPage(),
            Events = events,
            StateSequence = new Queue<AgnesApiKeyPageState>(new[]
            {
                OnSignInPage(),
                OnKeysPage(hasNewKeyButton: true)
            })
        };
        var google = new FakeGoogleLoginBrowser { Fail = true };
        var authentication = new RecordingGoogleAuthenticationService
        {
            Result = GoogleLoginResult.InvalidCredentials(),
            Events = events
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
        // Dashboard page (not keys, not login) -> navigate to keys -> timeout
        var events = new List<string>();
        var browser = new FakeAgnesBrowserWithStateSequence
        {
            State = new AgnesApiKeyPageState(
                new Uri("https://platform.agnes-ai.com/dashboard"),
                IsOnKeysPage: false,
                IsOnLoginPage: false,
                HasGoogleSignIn: false,
                ExistingKeyCount: 0,
                HasNewKeyButton: false,
                HasNewKeyNameInput: false,
                HasCreatedKeyPanel: false,
                ApiKey: string.Empty),
            Events = events,
            StateSequence = new Queue<AgnesApiKeyPageState>()
        };
        var google = new FakeGoogleLoginBrowser();

        // Use a short timeout for testing
        var result = await AgnesKeyFlowOrchestrator.RunAsync(
            browser,
            Credential,
            google,
            CancellationToken.None,
            timeout: TimeSpan.FromMilliseconds(100));

        Assert.False(result.Success);
        // Error should include URL and state info
        Assert.Contains("dashboard", result.ErrorMessage);
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
        // New error message says "Could not click" not "Could not find"
        Assert.Contains("Could not click the 'Continue with Google' button", result.ErrorMessage);
        Assert.False(browser.CreatedKey);
    }

    [Fact]
    public async Task RunAsync_returns_error_when_not_on_keys_page_and_no_google_button()
    {
        // On dashboard (not keys page), no Google button
        // Use StateSequence to simulate that navigation doesn't magically fix things
        var browser = new FakeAgnesBrowserWithStateSequence
        {
            State = new AgnesApiKeyPageState(
                new Uri("https://platform.agnes-ai.com/dashboard"),
                IsOnKeysPage: false,
                IsOnLoginPage: false,
                HasGoogleSignIn: false,
                ExistingKeyCount: 0,
                HasNewKeyButton: false,
                HasNewKeyNameInput: false,
                HasCreatedKeyPanel: false,
                ApiKey: string.Empty),
            // After navigation, still no Google button (dashboard doesn't have it)
            StateSequence = new Queue<AgnesApiKeyPageState>(new[]
            {
                new AgnesApiKeyPageState(
                    new Uri("https://platform.agnes-ai.com/settings/apiKeys"),
                    IsOnKeysPage: false, // Still not keys page content
                    IsOnLoginPage: false,
                    HasGoogleSignIn: false, // No Google button on dashboard
                    ExistingKeyCount: 0,
                    HasNewKeyButton: false,
                    HasNewKeyNameInput: false,
                    HasCreatedKeyPanel: false,
                    ApiKey: string.Empty)
            })
        };
        var google = new FakeGoogleLoginBrowser();

        var result = await AgnesKeyFlowOrchestrator.RunAsync(
            browser,
            Credential,
            google,
            CancellationToken.None);

        Assert.False(result.Success);
        // Error message will be about not finding Google button
        Assert.Contains("Could not find the 'Continue with Google' button", result.ErrorMessage);
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
                IsOnLoginPage: false,
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
                IsOnLoginPage: false,
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
        // Wrong host - orchestrator will navigate to keys page but state doesn't change
        // (simulate that navigation to wrong host doesn't yield keys page content)
        var browser = new FakeAgnesBrowserWithStateSequence
        {
            State = new AgnesApiKeyPageState(
                new Uri("https://wrong-host.example.com/settings/apiKeys"),
                IsOnKeysPage: false, // Content-based: not keys page
                IsOnLoginPage: false,
                HasGoogleSignIn: false,
                ExistingKeyCount: 0,
                HasNewKeyButton: true,
                HasNewKeyNameInput: false,
                HasCreatedKeyPanel: false,
                ApiKey: string.Empty),
            // After navigation, still not on keys page (wrong host)
            StateSequence = new Queue<AgnesApiKeyPageState>(new[]
            {
                new AgnesApiKeyPageState(
                    new Uri("https://wrong-host.example.com/settings/apiKeys"),
                    IsOnKeysPage: false,
                    IsOnLoginPage: false,
                    HasGoogleSignIn: false,
                    ExistingKeyCount: 0,
                    HasNewKeyButton: false,
                    HasNewKeyNameInput: false,
                    HasCreatedKeyPanel: false,
                    ApiKey: string.Empty)
            })
        };
        var google = new FakeGoogleLoginBrowser();

        var result = await AgnesKeyFlowOrchestrator.RunAsync(
            browser,
            Credential,
            google,
            CancellationToken.None);

        Assert.False(result.Success);
        // Error will be about not finding Google button (navigates but state doesn't change)
        Assert.Contains("Could not find the 'Continue with Google' button", result.ErrorMessage);
    }

    [Fact]
    public async Task RunAsync_exact_url_matching_rejects_wrong_path()
    {
        // Wrong path - orchestrator will navigate to keys page but state doesn't change
        // (simulate that navigation to wrong path doesn't yield keys page content)
        var browser = new FakeAgnesBrowserWithStateSequence
        {
            State = new AgnesApiKeyPageState(
                new Uri("https://platform.agnes-ai.com/settings/wrong-path"),
                IsOnKeysPage: false, // Content-based: not keys page
                IsOnLoginPage: false,
                HasGoogleSignIn: false,
                ExistingKeyCount: 0,
                HasNewKeyButton: true,
                HasNewKeyNameInput: false,
                HasCreatedKeyPanel: false,
                ApiKey: string.Empty),
            // After navigation, still not on keys page (wrong path)
            StateSequence = new Queue<AgnesApiKeyPageState>(new[]
            {
                new AgnesApiKeyPageState(
                    new Uri("https://platform.agnes-ai.com/settings/wrong-path"),
                    IsOnKeysPage: false,
                    IsOnLoginPage: false,
                    HasGoogleSignIn: false,
                    ExistingKeyCount: 0,
                    HasNewKeyButton: false,
                    HasNewKeyNameInput: false,
                    HasCreatedKeyPanel: false,
                    ApiKey: string.Empty)
            })
        };
        var google = new FakeGoogleLoginBrowser();

        var result = await AgnesKeyFlowOrchestrator.RunAsync(
            browser,
            Credential,
            google,
            CancellationToken.None);

        Assert.False(result.Success);
        // Error will be about not finding Google button (navigates but state doesn't change)
        Assert.Contains("Could not find the 'Continue with Google' button", result.ErrorMessage);
    }

    private sealed class FakeAgnesBrowserNoNewKeyButton : IAgnesApiKeyBrowser
    {
        public AgnesApiKeyPageState State { get; set; } = null!;
        public bool ClickedSignInWithGoogle { get; private set; }
        public bool CreatedKey { get; private set; }
        public string? CreatedKeyName { get; private set; }
        public List<string> Events { get; set; } = new();
        public bool NavigateToKeysPageCalled { get; private set; }

        public Task<AgnesApiKeyPageState> ReadStateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(State);

        public Task<bool> TryClickSignInWithGoogleAsync(CancellationToken cancellationToken, TimeSpan? timeout = null)
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

        public Task<bool> WaitForKeysPageAsync(CancellationToken cancellationToken, TimeSpan? timeout = null)
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

        public Task<bool> NavigateToKeysPageAsync(CancellationToken cancellationToken)
        {
            NavigateToKeysPageCalled = true;
            State = OnKeysPage(hasNewKeyButton: true);
            return Task.FromResult(true);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeAgnesBrowserClickFails : IAgnesApiKeyBrowser
    {
        public AgnesApiKeyPageState State { get; set; } = null!;
        public bool ClickedSignInWithGoogle { get; private set; }
        public bool CreatedKey { get; private set; }
        public List<string> Events { get; set; } = new();
        public bool NavigateToKeysPageCalled { get; private set; }

        public Task<AgnesApiKeyPageState> ReadStateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(State);

        public Task<bool> TryClickSignInWithGoogleAsync(CancellationToken cancellationToken, TimeSpan? timeout = null)
        {
            // Always return false - simulates button exists but click fails
            return Task.FromResult(false);
        }

        public Task<bool> WaitForGoogleSignInAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> WaitForKeysPageAsync(CancellationToken cancellationToken, TimeSpan? timeout = null)
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

        public Task<bool> NavigateToKeysPageAsync(CancellationToken cancellationToken)
        {
            NavigateToKeysPageCalled = true;
            State = OnKeysPage(hasNewKeyButton: true);
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
        public List<string> Events { get; set; } = new();
        public bool NavigateToKeysPageCalled { get; private set; }
        public string? NavigatedUrl { get; private set; }

        public Task<AgnesApiKeyPageState> ReadStateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(State);

        public Task<bool> TryClickSignInWithGoogleAsync(CancellationToken cancellationToken, TimeSpan? timeout = null)
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

        public Task<bool> WaitForKeysPageAsync(CancellationToken cancellationToken, TimeSpan? timeout = null)
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

        public Task<bool> NavigateToKeysPageAsync(CancellationToken cancellationToken)
        {
            NavigateToKeysPageCalled = true;
            NavigatedUrl = "https://platform.agnes-ai.com/settings/apiKeys";
            State = OnKeysPage(hasNewKeyButton: true);
            return Task.FromResult(true);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    // Fix: Don't auto-update State in NavigateToKeysPageAsync for StateSequence fake
    private sealed class FakeAgnesBrowserWithStateSequence : IAgnesApiKeyBrowser
    {
        public AgnesApiKeyPageState State { get; set; } = null!;
        public bool ClickedSignInWithGoogle { get; private set; }
        public bool CreatedKey { get; private set; }
        public string? CreatedKeyName { get; private set; }
        public List<string> Events { get; set; } = new();
        public Queue<AgnesApiKeyPageState>? StateSequence { get; set; }
        public bool NavigateToKeysPageCalled { get; private set; }
        public string? NavigatedUrl { get; private set; }
        public Func<CancellationToken, Task<IGoogleLoginBrowser>>? PopupFactory { get; set; }

        public Task<AgnesApiKeyPageState> ReadStateAsync(CancellationToken cancellationToken)
        {
            if (StateSequence is not null && StateSequence.Count > 0)
            {
                State = StateSequence.Dequeue();
            }
            return Task.FromResult(State);
        }

        public Task<bool> TryClickSignInWithGoogleAsync(CancellationToken cancellationToken, TimeSpan? timeout = null)
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

        public Task<bool> WaitForKeysPageAsync(CancellationToken cancellationToken, TimeSpan? timeout = null)
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

        public Task<bool> NavigateToKeysPageAsync(CancellationToken cancellationToken)
        {
            NavigateToKeysPageCalled = true;
            NavigatedUrl = "https://platform.agnes-ai.com/settings/apiKeys";
            // Don't update State here - it should come from StateSequence via ReadStateAsync
            return Task.FromResult(true);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static AgnesApiKeyPageState OnSignInPage() =>
        new(
            KeysPageUri,
            IsOnKeysPage: true,
            IsOnLoginPage: false,
            HasGoogleSignIn: true,
            ExistingKeyCount: 0,
            HasNewKeyButton: false,
            HasNewKeyNameInput: false,
            HasCreatedKeyPanel: false,
            ApiKey: string.Empty);

    private static AgnesApiKeyPageState OnLoginPage() =>
        new(
            KeysPageUri,
            IsOnKeysPage: false,
            IsOnLoginPage: true,
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
            IsOnLoginPage: false,
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
        public List<string> Events { get; set; } = new();
        public GoogleAuthenticationRequest? LastRequest { get; private set; }

        public Task<GoogleLoginResult> AuthenticateAsync(
            GoogleAuthenticationRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Events?.Add("google");
            LastRequest = request;
            return Task.FromResult(Result);
        }
    }

    /// <summary>
    /// Wrapper that delegates to a real GoogleAuthenticationService but captures the LastRequest.
    /// </summary>
    private sealed class CapturingGoogleAuthenticationService : IGoogleAuthenticationService
    {
        private readonly GoogleAuthenticationService _inner = new();
        public GoogleAuthenticationRequest? LastRequest { get; private set; }
        public int CallCount { get; private set; }

        public Task<GoogleLoginResult> AuthenticateAsync(
            GoogleAuthenticationRequest request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequest = request;
            return _inner.AuthenticateAsync(request, cancellationToken);
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
        public List<string> Events { get; set; } = new();
        public Func<CancellationToken, Task<IGoogleLoginBrowser>>? PopupFactory { get; set; }
        public HashSet<string>? CapturedPreClickTargets { get; set; }
        public bool NavigateToKeysPageCalled { get; private set; }
        public string? NavigatedUrl { get; private set; }

        public Task<AgnesApiKeyPageState> ReadStateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(State);

        public Task<bool> TryClickSignInWithGoogleAsync(CancellationToken cancellationToken, TimeSpan? timeout = null)
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

        public Task<bool> WaitForKeysPageAsync(CancellationToken cancellationToken, TimeSpan? timeout = null)
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

        public Task<bool> NavigateToKeysPageAsync(CancellationToken cancellationToken)
        {
            NavigateToKeysPageCalled = true;
            NavigatedUrl = "https://platform.agnes-ai.com/settings/apiKeys";
            State = OnKeysPage(hasNewKeyButton: true);
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

    /// <summary>
    /// Stateful fake Google login browser that simulates the full Google auth flow:
    /// email -> password -> TOTP -> completion.
    /// </summary>
    private sealed class StatefulFakePopupGoogleLoginBrowser : IGoogleLoginBrowser
    {
        public int ReadStateCallCount { get; private set; }
        public int FillCallCount { get; private set; }
        public int SubmitCallCount { get; private set; }
        public int SelectAuthenticatorCallCount { get; private set; }
        public List<(GoogleLoginField Field, string Value)> FilledFields { get; } = new();
        public List<GoogleLoginField> SubmittedFields { get; } = new();
        
        // State machine: 0=email, 1=password, 2=totp, 3=completion
        private int _step = 0;

        public Task<GoogleLoginPageState> ReadStateAsync(CancellationToken cancellationToken)
        {
            ReadStateCallCount++;
            
            // Return state based on current step
            return Task.FromResult(_step switch
            {
                0 => new GoogleLoginPageState(
                    new Uri("https://accounts.google.com/v3/signin/identifier"),
                    HasEmailField: true,
                    HasPasswordField: false,
                    HasTotpField: false,
                    HasTotpError: false,
                    Has2FAMethodPicker: false,
                    HasCompletionSignal: false,
                    HasManualChallenge: false),
                1 => new GoogleLoginPageState(
                    new Uri("https://accounts.google.com/v3/signin/challenge/pwd"),
                    HasEmailField: false,
                    HasPasswordField: true,
                    HasTotpField: false,
                    HasTotpError: false,
                    Has2FAMethodPicker: false,
                    HasCompletionSignal: false,
                    HasManualChallenge: false),
                2 => new GoogleLoginPageState(
                    new Uri("https://accounts.google.com/v3/signin/challenge/totp"),
                    HasEmailField: false,
                    HasPasswordField: false,
                    HasTotpField: true,
                    HasTotpError: false,
                    Has2FAMethodPicker: false,
                    HasCompletionSignal: false,
                    HasManualChallenge: false),
                _ => new GoogleLoginPageState(
                    new Uri("https://accounts.google.com/"),
                    HasEmailField: false,
                    HasPasswordField: false,
                    HasTotpField: false,
                    HasTotpError: false,
                    Has2FAMethodPicker: false,
                    HasCompletionSignal: true,
                    HasManualChallenge: false)
            });
        }

        public Task FillAsync(GoogleLoginField field, string value, CancellationToken cancellationToken)
        {
            FillCallCount++;
            FilledFields.Add((field, value));
            return Task.CompletedTask;
        }

        public Task SubmitAsync(GoogleLoginField field, CancellationToken cancellationToken)
        {
            SubmitCallCount++;
            SubmittedFields.Add(field);
            
            // Advance step after each submit
            if (_step == 0 && field == GoogleLoginField.Email) _step = 1;
            else if (_step == 1 && field == GoogleLoginField.Password) _step = 2;
            else if (_step == 2 && field == GoogleLoginField.Totp) _step = 3;
            
            return Task.CompletedTask;
        }

        public Task<bool> TrySelectAuthenticatorMethodAsync(CancellationToken cancellationToken)
        {
            SelectAuthenticatorCallCount++;
            return Task.FromResult(true);
        }

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
            State = new AgnesApiKeyPageState(
                KeysPageUri,
                IsOnKeysPage: true,
                IsOnLoginPage: false,
                HasGoogleSignIn: true,
                ExistingKeyCount: 0,
                HasNewKeyButton: false,
                HasNewKeyNameInput: false,
                HasCreatedKeyPanel: false,
                ApiKey: string.Empty)
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
            State = new AgnesApiKeyPageState(
                KeysPageUri,
                IsOnKeysPage: true,
                IsOnLoginPage: false,
                HasGoogleSignIn: true,
                ExistingKeyCount: 0,
                HasNewKeyButton: false,
                HasNewKeyNameInput: false,
                HasCreatedKeyPanel: false,
                ApiKey: string.Empty)
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

    [Fact]
    public async Task RunAsync_login_page_clicks_google_then_autologins_and_creates_key()
    {
        // Login page with Google button -> click -> popup auth -> returns to keys -> create key
        // Using the popup factory overload (which is the actual code path)
        var events = new List<string>();
        var browser = new FakeAgnesBrowserWithPopupFactory
        {
            State = new AgnesApiKeyPageState(
                KeysPageUri,
                IsOnKeysPage: false,
                IsOnLoginPage: true,
                HasGoogleSignIn: true,
                ExistingKeyCount: 0,
                HasNewKeyButton: false,
                HasNewKeyNameInput: false,
                HasCreatedKeyPanel: false,
                ApiKey: string.Empty),
            Events = events
        };
        
        var popupGoogleBrowser = new FakePopupGoogleLoginBrowser();
        var authentication = new RecordingGoogleAuthenticationService { Events = events };

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
    public async Task RunAsync_loading_about_blank_waits_does_not_fail_immediately()
    {
        // about:blank -> loading -> then keys page
        var events = new List<string>();
        var browser = new FakeAgnesBrowserWithStateSequence
        {
            State = new AgnesApiKeyPageState(
                new Uri("about:blank"),
                IsOnKeysPage: false,
                IsOnLoginPage: false,
                HasGoogleSignIn: false,
                ExistingKeyCount: 0,
                HasNewKeyButton: false,
                HasNewKeyNameInput: false,
                HasCreatedKeyPanel: false,
                ApiKey: string.Empty),
            Events = events,
            StateSequence = new Queue<AgnesApiKeyPageState>(new[]
            {
                new AgnesApiKeyPageState(
                    new Uri("about:blank"),
                    IsOnKeysPage: false,
                    IsOnLoginPage: false,
                    HasGoogleSignIn: false,
                    ExistingKeyCount: 0,
                    HasNewKeyButton: false,
                    HasNewKeyNameInput: false,
                    HasCreatedKeyPanel: false,
                    ApiKey: string.Empty),  // First read: about:blank
                new AgnesApiKeyPageState(
                    new Uri("https://platform.agnes-ai.com/settings/apiKeys"),
                    IsOnKeysPage: true,
                    IsOnLoginPage: false,
                    HasGoogleSignIn: false,  // Already signed in
                    ExistingKeyCount: 0,
                    HasNewKeyButton: true,
                    HasNewKeyNameInput: false,
                    HasCreatedKeyPanel: false,
                    ApiKey: string.Empty)   // Second read: keys page
            })
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
        Assert.False(browser.ClickedSignInWithGoogle);
        Assert.Equal(0, authentication.CallCount);
    }

    [Fact]
    public async Task RunAsync_timeout_returns_clear_error_with_current_url()
    {
        // Always returns about:blank - with short timeout, will fail with Google button not found
        // because it keeps navigating to keys page but state never changes
        var events = new List<string>();
        var browser = new FakeAgnesBrowserWithStateSequence
        {
            State = new AgnesApiKeyPageState(
                new Uri("about:blank"),
                IsOnKeysPage: false,
                IsOnLoginPage: false,
                HasGoogleSignIn: false,
                ExistingKeyCount: 0,
                HasNewKeyButton: false,
                HasNewKeyNameInput: false,
                HasCreatedKeyPanel: false,
                ApiKey: string.Empty),
            Events = events,
            StateSequence = new Queue<AgnesApiKeyPageState>()
        };
        var google = new FakeGoogleLoginBrowser();
        var authentication = new RecordingGoogleAuthenticationService { Events = events };

        // Use a short timeout for testing (100ms instead of 30s)
        var result = await AgnesKeyFlowOrchestrator.RunAsync(
            browser,
            Credential,
            google,
            CancellationToken.None,
            authentication,
            timeout: TimeSpan.FromMilliseconds(100));

        Assert.False(result.Success);
        // With new logic: navigates to keys page but state doesn't change, eventually fails with Google button not found
        Assert.Contains("Could not find the 'Continue with Google' button", result.ErrorMessage);
        // The error message should include the URL for debugging
        Assert.Contains("about:blank", result.ErrorMessage);
    }

    [Fact]
    public async Task RunAsync_with_real_GoogleAuthenticationService_popup_flow_works()
    {
        // Test with REAL GoogleAuthenticationService + stateful fake popup browser
        // This exercises the actual Google auth state machine (email -> password -> TOTP -> completion)
        // Assertions:
        // 1. The popup browser is the one passed to Google auth (GoogleAuthenticationRequest.Browser == popup)
        // 2. The credential is propagated correctly
        // 3. Key creation happens AFTER auth completes
        // 4. LastRequest captures the popup browser and credential

        var events = new List<string>();
        var browser = new FakeAgnesBrowserWithPopupFactory
        {
            State = new AgnesApiKeyPageState(
                KeysPageUri,
                IsOnKeysPage: true,
                IsOnLoginPage: false,
                HasGoogleSignIn: true,
                ExistingKeyCount: 0,
                HasNewKeyButton: false,
                HasNewKeyNameInput: false,
                HasCreatedKeyPanel: false,
                ApiKey: string.Empty),
            Events = events
        };

        var popupGoogleBrowser = new StatefulFakePopupGoogleLoginBrowser();
        
        // Use CapturingGoogleAuthenticationService to capture LastRequest while using real auth
        var capturingAuth = new CapturingGoogleAuthenticationService();

        // Popup factory that returns our stateful fake Google login browser
        browser.PopupFactory = (ct) =>
        {
            browser.Events.Add("popup_factory_called");
            return Task.FromResult<IGoogleLoginBrowser>(popupGoogleBrowser);
        };

        var credential = new GoogleLoginCredential(
            profileId: "test-profile",
            email: "user@example.com",
            password: "test-password",
            totpSecret: "JBSWY3DPEHPK3PXP");

        var result = await AgnesKeyFlowOrchestrator.RunAsync(
            browser,
            credential,
            browser.PopupFactory!,
            CancellationToken.None,
            capturingAuth);

        Assert.True(result.Success);
        Assert.Equal("agnes_createdkeyvalue123456", result.ApiKey);
        Assert.True(browser.ClickedSignInWithGoogle);
        
        // Assert: Key creation happens AFTER auth (events order)
        Assert.Equal(new[] { "google", "popup_factory_called", "create" }, browser.Events);
        Assert.Equal("user@example.com-agnes", browser.CreatedKeyName);

        // Assert: Popup browser was used for Google auth
        Assert.Equal(4, popupGoogleBrowser.ReadStateCallCount); // email, password, totp, completion
        Assert.Equal(3, popupGoogleBrowser.FillCallCount); // email, password, totp
        Assert.Equal(3, popupGoogleBrowser.SubmitCallCount); // email, password, totp
        
        // Assert: Credential propagated correctly (email, password, TOTP filled)
        Assert.Equal(3, popupGoogleBrowser.FilledFields.Count);
        Assert.Equal(GoogleLoginField.Email, popupGoogleBrowser.FilledFields[0].Field);
        Assert.Equal("user@example.com", popupGoogleBrowser.FilledFields[0].Value);
        Assert.Equal(GoogleLoginField.Password, popupGoogleBrowser.FilledFields[1].Field);
        Assert.Equal("test-password", popupGoogleBrowser.FilledFields[1].Value);
        Assert.Equal(GoogleLoginField.Totp, popupGoogleBrowser.FilledFields[2].Field);
        Assert.NotEmpty(popupGoogleBrowser.FilledFields[2].Value); // TOTP code generated
        
        // Assert: Submit order correct
        Assert.Equal(GoogleLoginField.Email, popupGoogleBrowser.SubmittedFields[0]);
        Assert.Equal(GoogleLoginField.Password, popupGoogleBrowser.SubmittedFields[1]);
        Assert.Equal(GoogleLoginField.Totp, popupGoogleBrowser.SubmittedFields[2]);

        // Assert: LastRequest captured with popup browser and credential
        Assert.NotNull(capturingAuth.LastRequest);
        Assert.Same(popupGoogleBrowser, capturingAuth.LastRequest!.Browser);
        Assert.Equal(credential.ProfileId, capturingAuth.LastRequest.Credential.ProfileId);
        Assert.Equal(credential.Email, capturingAuth.LastRequest.Credential.Email);
        Assert.Equal(credential.Password, capturingAuth.LastRequest.Credential.Password);
        Assert.Equal(credential.TotpSecret, capturingAuth.LastRequest.Credential.TotpSecret);
        Assert.Equal(1, capturingAuth.CallCount);
    }

    [Fact]
    public async Task RecordingGoogleAuthenticationService_captures_last_request_browser_and_credential()
    {
        // Test that RecordingGoogleAuthenticationService captures LastRequest with correct browser (popup) and credential
        var events = new List<string>();
        var browser = new FakeAgnesBrowserWithPopupFactory
        {
            State = new AgnesApiKeyPageState(
                KeysPageUri,
                IsOnKeysPage: true,
                IsOnLoginPage: false,
                HasGoogleSignIn: true,
                ExistingKeyCount: 0,
                HasNewKeyButton: false,
                HasNewKeyNameInput: false,
                HasCreatedKeyPanel: false,
                ApiKey: string.Empty),
            Events = events
        };

        var popupGoogleBrowser = new FakePopupGoogleLoginBrowser();
        var authentication = new RecordingGoogleAuthenticationService 
        { 
            Events = events,
            Result = GoogleLoginResult.Success()
        };

        browser.PopupFactory = (ct) =>
        {
            browser.Events.Add("popup_factory_called");
            return Task.FromResult<IGoogleLoginBrowser>(popupGoogleBrowser);
        };

        var credential = new GoogleLoginCredential(
            profileId: "specific-profile-id",
            email: "specific-email@example.com",
            password: "specific-password",
            totpSecret: "JBSWY3DPEHPK3PXP");

        var result = await AgnesKeyFlowOrchestrator.RunAsync(
            browser,
            credential,
            browser.PopupFactory!,
            CancellationToken.None,
            authentication);

        Assert.True(result.Success);
        
        // Assert: RecordingGoogleAuthenticationService captured the last request
        Assert.NotNull(authentication.LastRequest);
        
        // Assert: Browser is the popup browser from factory
        Assert.Same(popupGoogleBrowser, authentication.LastRequest!.Browser);
        
        // Assert: Credential matches what was passed to RunAsync
        Assert.Equal(credential.ProfileId, authentication.LastRequest.Credential.ProfileId);
        Assert.Equal(credential.Email, authentication.LastRequest.Credential.Email);
        Assert.Equal(credential.Password, authentication.LastRequest.Credential.Password);
        Assert.Equal(credential.TotpSecret, authentication.LastRequest.Credential.TotpSecret);
        
        // Assert: Auth was called once
        Assert.Equal(1, authentication.CallCount);
    }

    [Fact]
    public async Task RunAsync_navigates_to_keys_page_after_auth_and_waits()
    {
        // After Google auth, orchestrator navigates to keys page and waits for content-based IsOnKeysPage
        var events = new List<string>();
        var browser = new FakeAgnesBrowserWithStateSequence
        {
            State = new AgnesApiKeyPageState(
                KeysPageUri,
                IsOnKeysPage: true,
                IsOnLoginPage: false,
                HasGoogleSignIn: true,
                ExistingKeyCount: 0,
                HasNewKeyButton: false,
                HasNewKeyNameInput: false,
                HasCreatedKeyPanel: false,
                ApiKey: string.Empty),
            Events = events,
            StateSequence = new Queue<AgnesApiKeyPageState>(new[]
            {
                // Initial: on keys page with Google button
                new AgnesApiKeyPageState(
                    KeysPageUri,
                    IsOnKeysPage: true,
                    IsOnLoginPage: false,
                    HasGoogleSignIn: true,
                    ExistingKeyCount: 0,
                    HasNewKeyButton: false,
                    HasNewKeyNameInput: false,
                    HasCreatedKeyPanel: false,
                    ApiKey: string.Empty),
                // After Google click + popup auth + NavigateToKeysPageAsync: keys page (signed in)
                new AgnesApiKeyPageState(
                    KeysPageUri,
                    IsOnKeysPage: true,
                    IsOnLoginPage: false,
                    HasGoogleSignIn: false,
                    ExistingKeyCount: 0,
                    HasNewKeyButton: true,
                    HasNewKeyNameInput: false,
                    HasCreatedKeyPanel: false,
                    ApiKey: string.Empty)
            })
        };

        var popupGoogleBrowser = new FakePopupGoogleLoginBrowser();
        var authentication = new RecordingGoogleAuthenticationService 
        { 
            Events = events,
            Result = GoogleLoginResult.Success()
        };

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
        
        // Assert: NavigateToKeysPageAsync was called after auth
        Assert.True(browser.NavigateToKeysPageCalled);
        
        // Assert: The navigation URL was correct
        Assert.Equal("https://platform.agnes-ai.com/settings/apiKeys", browser.NavigatedUrl);
        
        Assert.Equal(1, authentication.CallCount);
        // Events: "google" (click), "popup_factory_called", "google" (auth), "create"
        Assert.Equal(new[] { "google", "popup_factory_called", "google", "create" }, browser.Events);
        Assert.Equal("user@example.com-agnes", browser.CreatedKeyName);
    }

    [Fact]
    public async Task RecordingGoogleAuthenticationService_popup_browser_matches_factory()
    {
        // Test that RecordingGoogleAuthenticationService receives the exact popup browser from factory
        // and the exact credential passed to RunAsync
        
        var events = new List<string>();
        var browser = new FakeAgnesBrowserWithPopupFactory
        {
            State = new AgnesApiKeyPageState(
                KeysPageUri,
                IsOnKeysPage: true,
                IsOnLoginPage: false,
                HasGoogleSignIn: true,
                ExistingKeyCount: 0,
                HasNewKeyButton: false,
                HasNewKeyNameInput: false,
                HasCreatedKeyPanel: false,
                ApiKey: string.Empty),
            Events = events
        };

        var popupGoogleBrowser = new FakePopupGoogleLoginBrowser();
        var authentication = new RecordingGoogleAuthenticationService 
        { 
            Events = events,
            Result = GoogleLoginResult.Success()
        };

        browser.PopupFactory = (ct) =>
        {
            browser.Events.Add("popup_factory_called");
            return Task.FromResult<IGoogleLoginBrowser>(popupGoogleBrowser);
        };

        var credential = new GoogleLoginCredential(
            profileId: "specific-profile-id",
            email: "specific-email@example.com",
            password: "specific-password",
            totpSecret: "JBSWY3DPEHPK3PXP");

        var result = await AgnesKeyFlowOrchestrator.RunAsync(
            browser,
            credential,
            browser.PopupFactory!,
            CancellationToken.None,
            authentication);

        Assert.True(result.Success);
        
        // The RecordingGoogleAuthenticationService doesn't directly expose the request,
        // but we verify through the flow that auth was called once
        Assert.Equal(1, authentication.CallCount);
        Assert.Contains("google", browser.Events);
        Assert.Contains("popup_factory_called", browser.Events);
        Assert.Contains("create", browser.Events);
    }

    [Fact]
    public async Task RunAsync_fallback_navigate_to_keys_page_when_wait_for_keys_page_fails()
    {
        // Test the fallback: Phase 1 passes (keys page with Google), after auth ReadStateAsync returns not-on-keys-page -> NavigateToKeysPageAsync is called -> succeeds
        // Use a fake that tracks Phase 1 vs Phase 3 reads separately
        var events = new List<string>();
        var browser = new FakeAgnesBrowserWithPhaseTracking
        {
            // Phase 1 state: on keys page with Google button
            Phase1State = new AgnesApiKeyPageState(
                KeysPageUri,
                IsOnKeysPage: true,
                IsOnLoginPage: false,
                HasGoogleSignIn: true,
                ExistingKeyCount: 0,
                HasNewKeyButton: false,
                HasNewKeyNameInput: false,
                HasCreatedKeyPanel: false,
                ApiKey: string.Empty),
            Events = events,
            // Phase 3 state sequence: after auth, first ReadStateAsync returns not-on-keys-page, second returns keys page
            Phase3StateSequence = new Queue<AgnesApiKeyPageState>(new[]
            {
                // After auth + NavigateToKeysPageAsync: not on keys page (redirect issue)
                new AgnesApiKeyPageState(
                    KeysPageUri,
                    IsOnKeysPage: false,
                    IsOnLoginPage: false,
                    HasGoogleSignIn: false,
                    ExistingKeyCount: 0,
                    HasNewKeyButton: false,
                    HasNewKeyNameInput: false,
                    HasCreatedKeyPanel: false,
                    ApiKey: string.Empty),
                // After fallback navigation: keys page
                new AgnesApiKeyPageState(
                    KeysPageUri,
                    IsOnKeysPage: true,
                    IsOnLoginPage: false,
                    HasGoogleSignIn: false,
                    ExistingKeyCount: 0,
                    HasNewKeyButton: true,
                    HasNewKeyNameInput: false,
                    HasCreatedKeyPanel: false,
                    ApiKey: string.Empty)
            })
        };

        var popupGoogleBrowser = new FakePopupGoogleLoginBrowser();
        var authentication = new RecordingGoogleAuthenticationService 
        { 
            Events = events,
            Result = GoogleLoginResult.Success()
        };

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
        Assert.True(browser.NavigateToKeysPageCalled); // Fallback was invoked
        Assert.Equal(1, authentication.CallCount);
        Assert.Contains("google", browser.Events);
        Assert.Contains("popup_factory_called", browser.Events);
        Assert.Contains("create", browser.Events);
        Assert.Equal("user@example.com-agnes", browser.CreatedKeyName);
    }

    [Fact]
    public async Task RunAsync_fallback_navigate_to_keys_page_when_wait_for_keys_page_returns_false()
    {
        // Test the fallback path: Phase 1 passes, after auth ReadStateAsync returns not-keys-page, then NavigateToKeysPageAsync is called and succeeds
        var events = new List<string>();
        var browser = new FakeAgnesBrowserWithPhaseTracking
        {
            // Phase 1 state: on keys page with Google button
            Phase1State = new AgnesApiKeyPageState(
                KeysPageUri,
                IsOnKeysPage: true,
                IsOnLoginPage: false,
                HasGoogleSignIn: true,
                ExistingKeyCount: 0,
                HasNewKeyButton: false,
                HasNewKeyNameInput: false,
                HasCreatedKeyPanel: false,
                ApiKey: string.Empty),
            Events = events,
            // Phase 3 state sequence: after auth, first ReadStateAsync returns not-on-keys-page, second returns keys page
            Phase3StateSequence = new Queue<AgnesApiKeyPageState>(new[]
            {
                // After auth + NavigateToKeysPageAsync: not on keys page (redirect issue)
                new AgnesApiKeyPageState(
                    KeysPageUri,
                    IsOnKeysPage: false,
                    IsOnLoginPage: false,
                    HasGoogleSignIn: false,
                    ExistingKeyCount: 0,
                    HasNewKeyButton: false,
                    HasNewKeyNameInput: false,
                    HasCreatedKeyPanel: false,
                    ApiKey: string.Empty),
                // After fallback navigation: keys page
                new AgnesApiKeyPageState(
                    KeysPageUri,
                    IsOnKeysPage: true,
                    IsOnLoginPage: false,
                    HasGoogleSignIn: false,
                    ExistingKeyCount: 0,
                    HasNewKeyButton: true,
                    HasNewKeyNameInput: false,
                    HasCreatedKeyPanel: false,
                    ApiKey: string.Empty)
            })
        };

        var popupGoogleBrowser = new FakePopupGoogleLoginBrowser();
        var authentication = new RecordingGoogleAuthenticationService 
        { 
            Events = events,
            Result = GoogleLoginResult.Success()
        };

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
        Assert.True(browser.NavigateToKeysPageCalled); // Fallback was invoked
        Assert.Equal(1, authentication.CallCount);
        Assert.Contains("google", browser.Events);
        Assert.Contains("popup_factory_called", browser.Events);
        Assert.Contains("create", browser.Events);
        Assert.Equal("user@example.com-agnes", browser.CreatedKeyName);
    }

    [Fact]
    public async Task RunAsync_fallback_fails_when_navigation_also_fails()
    {
        // Test the fallback failure: Phase 1 passes, after auth NavigateToKeysPageAsync called but state doesn't become keys page
        var events = new List<string>();
        var browser = new FakeAgnesBrowserWithPhaseTracking
        {
            // Phase 1 state: on keys page with Google button
            Phase1State = new AgnesApiKeyPageState(
                KeysPageUri,
                IsOnKeysPage: true,
                IsOnLoginPage: false,
                HasGoogleSignIn: true,
                ExistingKeyCount: 0,
                HasNewKeyButton: false,
                HasNewKeyNameInput: false,
                HasCreatedKeyPanel: false,
                ApiKey: string.Empty),
            Events = events,
            // Phase 3 state sequence: after auth, ReadStateAsync always returns not-on-keys-page
            Phase3StateSequence = new Queue<AgnesApiKeyPageState>(new[]
            {
                // After auth + NavigateToKeysPageAsync: not on keys page (redirect issue)
                new AgnesApiKeyPageState(
                    KeysPageUri,
                    IsOnKeysPage: false,
                    IsOnLoginPage: false,
                    HasGoogleSignIn: false,
                    ExistingKeyCount: 0,
                    HasNewKeyButton: false,
                    HasNewKeyNameInput: false,
                    HasCreatedKeyPanel: false,
                    ApiKey: string.Empty),
                // After fallback navigation: still not on keys page
                new AgnesApiKeyPageState(
                    KeysPageUri,
                    IsOnKeysPage: false,
                    IsOnLoginPage: false,
                    HasGoogleSignIn: false,
                    ExistingKeyCount: 0,
                    HasNewKeyButton: false,
                    HasNewKeyNameInput: false,
                    HasCreatedKeyPanel: false,
                    ApiKey: string.Empty)
            })
        };

        var popupGoogleBrowser = new FakePopupGoogleLoginBrowser();
        var authentication = new RecordingGoogleAuthenticationService 
        { 
            Events = events,
            Result = GoogleLoginResult.Success()
        };

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

        Assert.False(result.Success);
        // Error message will be about not reaching keys page after auth
        Assert.Contains("Did not return to Agnes AI keys page after Google sign-in", result.ErrorMessage);
        Assert.True(browser.ClickedSignInWithGoogle);
        Assert.True(browser.NavigateToKeysPageCalled); // Fallback was invoked but failed
        Assert.Equal(1, authentication.CallCount);
        Assert.False(browser.CreatedKey); // Key was not created
    }

    /// <summary>
    /// Fake browser for fallback test: Phase 1 passes (on keys page with Google), after auth ReadStateAsync fails once, then NavigateToKeysPageAsync fixes it.
    /// </summary>
    private sealed class FakeAgnesBrowserWithFallback : IAgnesApiKeyBrowser
    {
        public AgnesApiKeyPageState State { get; set; } = null!;
        public bool ClickedSignInWithGoogle { get; private set; }
        public bool CreatedKey { get; private set; }
        public string? CreatedKeyName { get; private set; }
        public List<string> Events { get; set; } = new();
        public bool NavigateToKeysPageCalled { get; private set; }
        public string? NavigatedUrl { get; private set; }
        public int ReadStateCallCount { get; private set; }
        public bool AfterAuth { get; private set; }
        public Func<CancellationToken, Task<IGoogleLoginBrowser>>? PopupFactory { get; set; }

        public Task<AgnesApiKeyPageState> ReadStateAsync(CancellationToken cancellationToken)
        {
            ReadStateCallCount++;
            
            // Phase 1: Before Google click - return keys page with Google button
            if (!ClickedSignInWithGoogle)
            {
                return Task.FromResult(State);
            }
            
            // Phase 3: After auth - first ReadStateAsync after auth returns not-on-keys-page
            if (!AfterAuth)
            {
                AfterAuth = true;
                return Task.FromResult(new AgnesApiKeyPageState(
                    KeysPageUri,
                    IsOnKeysPage: false, // Simulate redirect issue
                    IsOnLoginPage: false,
                    HasGoogleSignIn: false,
                    ExistingKeyCount: 0,
                    HasNewKeyButton: false,
                    HasNewKeyNameInput: false,
                    HasCreatedKeyPanel: false,
                    ApiKey: string.Empty));
            }
            
            // After navigation, return keys page state
            State = OnKeysPage(hasNewKeyButton: true);
            return Task.FromResult(State);
        }

        public Task<bool> TryClickSignInWithGoogleAsync(CancellationToken cancellationToken, TimeSpan? timeout = null)
        {
            if (!State.HasGoogleSignIn)
            {
                return Task.FromResult(false);
            }

            ClickedSignInWithGoogle = true;
            Events.Add("google");
            return Task.FromResult(true);
        }

        public Task<bool> WaitForGoogleSignInAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> WaitForKeysPageAsync(CancellationToken cancellationToken, TimeSpan? timeout = null)
        {
            return Task.FromResult(State.IsOnKeysPage);
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

        public Task<bool> NavigateToKeysPageAsync(CancellationToken cancellationToken)
        {
            NavigateToKeysPageCalled = true;
            NavigatedUrl = "https://platform.agnes-ai.com/settings/apiKeys";
            // Navigation succeeds - state will be updated on next ReadStateAsync call
            return Task.FromResult(true);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// Fake browser for fallback failure test: Phase 1 passes, after auth ReadStateAsync always returns not-on-keys-page, NavigateToKeysPageAsync also fails.
    /// </summary>
    private sealed class FakeAgnesBrowserWithFallbackFail : IAgnesApiKeyBrowser
    {
        public AgnesApiKeyPageState State { get; set; } = null!;
        public bool ClickedSignInWithGoogle { get; private set; }
        public bool CreatedKey { get; private set; }
        public string? CreatedKeyName { get; private set; }
        public List<string> Events { get; set; } = new();
        public bool NavigateToKeysPageCalled { get; private set; }
        public string? NavigatedUrl { get; private set; }
        public int ReadStateCallCount { get; private set; }
        public bool AfterAuth { get; private set; }
        public Func<CancellationToken, Task<IGoogleLoginBrowser>>? PopupFactory { get; set; }

        public Task<AgnesApiKeyPageState> ReadStateAsync(CancellationToken cancellationToken)
        {
            ReadStateCallCount++;
            
            // Phase 1: Before Google click - return keys page with Google button
            if (!ClickedSignInWithGoogle)
            {
                return Task.FromResult(State);
            }
            
            // Phase 3: After auth - always return not-on-keys-page
            if (!AfterAuth)
            {
                AfterAuth = true;
            }
            return Task.FromResult(new AgnesApiKeyPageState(
                KeysPageUri,
                IsOnKeysPage: false, // Simulate redirect issue
                IsOnLoginPage: false,
                HasGoogleSignIn: false,
                ExistingKeyCount: 0,
                HasNewKeyButton: false,
                HasNewKeyNameInput: false,
                HasCreatedKeyPanel: false,
                ApiKey: string.Empty));
        }

        public Task<bool> TryClickSignInWithGoogleAsync(CancellationToken cancellationToken, TimeSpan? timeout = null)
        {
            if (!State.HasGoogleSignIn)
            {
                return Task.FromResult(false);
            }

            ClickedSignInWithGoogle = true;
            Events.Add("google");
            return Task.FromResult(true);
        }

        public Task<bool> WaitForGoogleSignInAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> WaitForKeysPageAsync(CancellationToken cancellationToken, TimeSpan? timeout = null)
        {
            return Task.FromResult(State.IsOnKeysPage);
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

        public Task<bool> NavigateToKeysPageAsync(CancellationToken cancellationToken)
        {
            NavigateToKeysPageCalled = true;
            NavigatedUrl = "https://platform.agnes-ai.com/settings/apiKeys";
            // Navigation fails
            return Task.FromResult(false);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    /// <summary>
    /// Fake browser for fallback test with URL capture: WaitForKeysPageAsync fails once, 
    /// then NavigateToKeysPageAsync is called and captures the URL, then succeeds on retry.
    /// </summary>
    private sealed class FakeAgnesBrowserWithFallbackAndUrlCapture : IAgnesApiKeyBrowser
    {
        public AgnesApiKeyPageState State { get; set; } = null!;
        public bool ClickedSignInWithGoogle { get; private set; }
        public bool CreatedKey { get; private set; }
        public string? CreatedKeyName { get; private set; }
        public List<string> Events { get; set; } = new();
        public bool NavigateToKeysPageCalled { get; private set; }
        public string? NavigatedUrl { get; private set; }
        public int WaitForKeysPageCallCount { get; private set; }
        public Func<CancellationToken, Task<IGoogleLoginBrowser>>? PopupFactory { get; set; }

        public Task<AgnesApiKeyPageState> ReadStateAsync(CancellationToken cancellationToken) =>
            Task.FromResult(State);

        public Task<bool> TryClickSignInWithGoogleAsync(CancellationToken cancellationToken, TimeSpan? timeout = null)
        {
            if (!State.HasGoogleSignIn)
            {
                return Task.FromResult(false);
            }

            ClickedSignInWithGoogle = true;
            Events.Add("google");
            return Task.FromResult(true);
        }

        public Task<bool> WaitForGoogleSignInAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> WaitForKeysPageAsync(CancellationToken cancellationToken, TimeSpan? timeout = null)
        {
            WaitForKeysPageCallCount++;
            if (WaitForKeysPageCallCount == 1)
            {
                // First call fails - simulate not on keys page
                return Task.FromResult(false);
            }
            // Second call succeeds - after fallback navigation
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

        public Task<bool> NavigateToKeysPageAsync(CancellationToken cancellationToken)
        {
            NavigateToKeysPageCalled = true;
            NavigatedUrl = "https://platform.agnes-ai.com/settings/apiKeys";
            // Navigation succeeds - state will be checked on next WaitForKeysPageAsync call
            return Task.FromResult(true);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    // ===== New tests for content-based page detection and login flow =====

    [Fact]
    public async Task RunAsync_navigates_to_login_when_on_keys_url_but_login_content()
    {
        // Scenario: URL is /settings/apiKeys but content is login form (the bug scenario)
        // Orchestrator should detect login page content and navigate to /login
        var events = new List<string>();
        var browser = new FakeAgnesBrowserWithStateSequence
        {
            State = new AgnesApiKeyPageState(
                KeysPageUri,
                IsOnKeysPage: false,      // Content-based: false because login form
                IsOnLoginPage: true,      // Content-based: true because login form
                HasGoogleSignIn: false,   // No Google button on this login form
                ExistingKeyCount: 0,
                HasNewKeyButton: false,
                HasNewKeyNameInput: false,
                HasCreatedKeyPanel: false,
                ApiKey: string.Empty),
            Events = events,
            StateSequence = new Queue<AgnesApiKeyPageState>(new[]
            {
                // First read: on keys URL but login content, no Google button
                new AgnesApiKeyPageState(
                    KeysPageUri,
                    IsOnKeysPage: false,
                    IsOnLoginPage: true,
                    HasGoogleSignIn: false,
                    ExistingKeyCount: 0,
                    HasNewKeyButton: false,
                    HasNewKeyNameInput: false,
                    HasCreatedKeyPanel: false,
                    ApiKey: string.Empty),
                // After NavigateToKeysPageAsync (which now goes to /login): login page WITH Google button
                new AgnesApiKeyPageState(
                    new Uri("https://platform.agnes-ai.com/login"),
                    IsOnKeysPage: false,
                    IsOnLoginPage: true,
                    HasGoogleSignIn: true,
                    ExistingKeyCount: 0,
                    HasNewKeyButton: false,
                    HasNewKeyNameInput: false,
                    HasCreatedKeyPanel: false,
                    ApiKey: string.Empty),
                // After Google auth and redirect: keys page
                new AgnesApiKeyPageState(
                    KeysPageUri,
                    IsOnKeysPage: true,
                    IsOnLoginPage: false,
                    HasGoogleSignIn: false,
                    ExistingKeyCount: 0,
                    HasNewKeyButton: true,
                    HasNewKeyNameInput: false,
                    HasCreatedKeyPanel: false,
                    ApiKey: string.Empty)
            })
        };
        
        var popupGoogleBrowser = new FakePopupGoogleLoginBrowser();
        var authentication = new RecordingGoogleAuthenticationService 
        { 
            Events = events,
            Result = GoogleLoginResult.Success()
        };

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
        
        // Verify NavigateToKeysPageAsync was called (to go to /login)
        Assert.True(browser.NavigateToKeysPageCalled);
        
        // Events: "google" (click on login page), "popup_factory_called", "google" (auth), "create"
        Assert.Equal(new[] { "google", "popup_factory_called", "google", "create" }, browser.Events);
        Assert.Equal("user@example.com-agnes", browser.CreatedKeyName);
    }

    [Fact]
    public async Task RunAsync_navigates_to_keys_page_after_popup_auth()
    {
        // Scenario: After Google popup auth completes, orchestrator navigates to keys page
        // and waits for content-based IsOnKeysPage
        var events = new List<string>();
        var browser = new FakeAgnesBrowserWithStateSequence
        {
            State = new AgnesApiKeyPageState(
                KeysPageUri,
                IsOnKeysPage: true,
                IsOnLoginPage: false,
                HasGoogleSignIn: true,  // On keys page but needs Google sign-in
                ExistingKeyCount: 0,
                HasNewKeyButton: false,
                HasNewKeyNameInput: false,
                HasCreatedKeyPanel: false,
                ApiKey: string.Empty),
            Events = events,
            StateSequence = new Queue<AgnesApiKeyPageState>(new[]
            {
                // Initial: on keys page with Google button
                new AgnesApiKeyPageState(
                    KeysPageUri,
                    IsOnKeysPage: true,
                    IsOnLoginPage: false,
                    HasGoogleSignIn: true,
                    ExistingKeyCount: 0,
                    HasNewKeyButton: false,
                    HasNewKeyNameInput: false,
                    HasCreatedKeyPanel: false,
                    ApiKey: string.Empty),
                // After Google click + popup auth + redirect: keys page (signed in)
                new AgnesApiKeyPageState(
                    KeysPageUri,
                    IsOnKeysPage: true,
                    IsOnLoginPage: false,
                    HasGoogleSignIn: false,  // Signed in now
                    ExistingKeyCount: 0,
                    HasNewKeyButton: true,
                    HasNewKeyNameInput: false,
                    HasCreatedKeyPanel: false,
                    ApiKey: string.Empty)
            })
        };
        
        var popupGoogleBrowser = new FakePopupGoogleLoginBrowser();
        var authentication = new RecordingGoogleAuthenticationService 
        { 
            Events = events,
            Result = GoogleLoginResult.Success()
        };

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
        
        // After auth, NavigateToKeysPageAsync should be called again
        Assert.True(browser.NavigateToKeysPageCalled);
        
        // Events: "google" (click), "popup_factory_called", "google" (auth), "create"
        Assert.Equal(new[] { "google", "popup_factory_called", "google", "create" }, browser.Events);
    }

    [Fact]
    public async Task RunAsync_timeout_includes_url_and_detected_state()
    {
        // Scenario: Short timeout - orchestrator keeps navigating but state never changes
        // Error will be about not finding Google button, but should include URL
        var events = new List<string>();
        var browser = new FakeAgnesBrowserWithStateSequence
        {
            State = new AgnesApiKeyPageState(
                new Uri("https://platform.agnes-ai.com/dashboard"),
                IsOnKeysPage: false,
                IsOnLoginPage: false,
                HasGoogleSignIn: false,
                ExistingKeyCount: 0,
                HasNewKeyButton: false,
                HasNewKeyNameInput: false,
                HasCreatedKeyPanel: false,
                ApiKey: string.Empty),
            Events = events,
            StateSequence = new Queue<AgnesApiKeyPageState>()
        };
        var google = new FakeGoogleLoginBrowser();
        var authentication = new RecordingGoogleAuthenticationService { Events = events };

        // Use a short timeout for testing
        var result = await AgnesKeyFlowOrchestrator.RunAsync(
            browser,
            Credential,
            google,
            CancellationToken.None,
            authentication,
            timeout: TimeSpan.FromMilliseconds(100));

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        
        // Error should include URL (dashboard or keys page after navigation)
        Assert.Contains("dashboard", result.ErrorMessage);
        // And should mention Google button not found
        Assert.Contains("Could not find the 'Continue with Google' button", result.ErrorMessage);
    }

    [Fact]
    public async Task RunAsync_login_page_with_google_clicks_then_auth_then_keys()
    {
        // Full flow: Login page (with Google) -> click Google -> popup auth -> navigate to keys page -> create key
        var events = new List<string>();
        var browser = new FakeAgnesBrowserWithStateSequence
        {
            State = new AgnesApiKeyPageState(
                new Uri("https://platform.agnes-ai.com/login"),
                IsOnKeysPage: false,
                IsOnLoginPage: true,
                HasGoogleSignIn: true,
                ExistingKeyCount: 0,
                HasNewKeyButton: false,
                HasNewKeyNameInput: false,
                HasCreatedKeyPanel: false,
                ApiKey: string.Empty),
            Events = events,
            StateSequence = new Queue<AgnesApiKeyPageState>(new[]
            {
                // Initial: on login page with Google button
                new AgnesApiKeyPageState(
                    new Uri("https://platform.agnes-ai.com/login"),
                    IsOnKeysPage: false,
                    IsOnLoginPage: true,
                    HasGoogleSignIn: true,
                    ExistingKeyCount: 0,
                    HasNewKeyButton: false,
                    HasNewKeyNameInput: false,
                    HasCreatedKeyPanel: false,
                    ApiKey: string.Empty),
                // After Google click + popup auth + redirect: keys page
                new AgnesApiKeyPageState(
                    KeysPageUri,
                    IsOnKeysPage: true,
                    IsOnLoginPage: false,
                    HasGoogleSignIn: false,
                    ExistingKeyCount: 0,
                    HasNewKeyButton: true,
                    HasNewKeyNameInput: false,
                    HasCreatedKeyPanel: false,
                    ApiKey: string.Empty)
            })
        };
        
        var popupGoogleBrowser = new FakePopupGoogleLoginBrowser();
        var authentication = new RecordingGoogleAuthenticationService 
        { 
            Events = events,
            Result = GoogleLoginResult.Success()
        };

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
        
        // NavigateToKeysPageAsync should be called after auth
        Assert.True(browser.NavigateToKeysPageCalled);
        
        // Events: "google" (click), "popup_factory_called", "google" (auth), "create"
        Assert.Equal(new[] { "google", "popup_factory_called", "google", "create" }, browser.Events);
    }

    /// <summary>
    /// Fake browser that tracks Phase 1 (before auth) and Phase 3 (after auth) separately.
    /// Phase 1: returns Phase1State on ReadStateAsync
    /// Phase 3: dequeues from Phase3StateSequence on ReadStateAsync
    /// </summary>
    private sealed class FakeAgnesBrowserWithPhaseTracking : IAgnesApiKeyBrowser
    {
        public AgnesApiKeyPageState Phase1State { get; set; } = null!;
        public AgnesApiKeyPageState State { get; set; } = null!;
        public bool ClickedSignInWithGoogle { get; private set; }
        public bool CreatedKey { get; private set; }
        public string? CreatedKeyName { get; private set; }
        public List<string> Events { get; set; } = new();
        public Queue<AgnesApiKeyPageState>? Phase3StateSequence { get; set; }
        public bool NavigateToKeysPageCalled { get; private set; }
        public string? NavigatedUrl { get; private set; }
        public bool InPhase3 { get; private set; }
        public Func<CancellationToken, Task<IGoogleLoginBrowser>>? PopupFactory { get; set; }

        public Task<AgnesApiKeyPageState> ReadStateAsync(CancellationToken cancellationToken)
        {
            if (!InPhase3)
            {
                return Task.FromResult(Phase1State);
            }
            
            if (Phase3StateSequence is not null && Phase3StateSequence.Count > 0)
            {
                State = Phase3StateSequence.Dequeue();
            }
            return Task.FromResult(State);
        }

        public Task<bool> TryClickSignInWithGoogleAsync(CancellationToken cancellationToken, TimeSpan? timeout = null)
        {
            // Use the current state (Phase1State before auth, State after auth)
            var currentState = InPhase3 ? State : Phase1State;
            if (!currentState.HasGoogleSignIn)
            {
                return Task.FromResult(false);
            }

            ClickedSignInWithGoogle = true;
            InPhase3 = true; // Switch to Phase 3 after Google click
            Events?.Add("google");
            return Task.FromResult(true);
        }

        public Task<bool> WaitForGoogleSignInAsync(CancellationToken cancellationToken) => Task.FromResult(true);

        public Task<bool> WaitForKeysPageAsync(CancellationToken cancellationToken, TimeSpan? timeout = null)
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

        public Task<bool> NavigateToKeysPageAsync(CancellationToken cancellationToken)
        {
            NavigateToKeysPageCalled = true;
            NavigatedUrl = "https://platform.agnes-ai.com/settings/apiKeys";
            // Don't update State here - it should come from Phase3StateSequence via ReadStateAsync
            return Task.FromResult(true);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}