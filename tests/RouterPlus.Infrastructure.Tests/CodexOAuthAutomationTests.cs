using RouterPlus.Infrastructure.Chrome;
using Xunit;

namespace RouterPlus.Infrastructure.Tests;

/// <summary>
/// TDD-only tests for <see cref="CodexOAuthAutomation"/> pure logic and static routing methods.
///
/// CodexOAuthAutomation depends on the sealed ChromeCdpClient, so CDP-calling
/// methods (ReadProviderPageStateAsync, TryClickProvider*Async) cannot be unit-tested
/// without a live browser. This file targets all testable paths: the internal static
/// routing helpers, the Codex-specific decision logic (reimplemented in a testable
/// subclass that mirrors the sealed override logic), and the data records.
/// </summary>
public sealed class CodexOAuthAutomationTests
{
    // ====================================================================
    // IsCodexConsentRoute (internal static)
    // ====================================================================

    [Fact]
    public void IsCodexConsentRoute_exact_match_returns_true()
    {
        Assert.True(CodexOAuthAutomation.IsCodexConsentRoute(
            "auth.openai.com",
            "/sign-in-with-chatgpt/codex/consent"));
    }

    [Fact]
    public void IsCodexConsentRoute_with_query_string_returns_true()
    {
        Assert.True(CodexOAuthAutomation.IsCodexConsentRoute(
            "auth.openai.com",
            "/sign-in-with-chatgpt/codex/consent?client_id=abc"));
    }

    [Fact]
    public void IsCodexConsentRoute_with_fragment_returns_true()
    {
        Assert.True(CodexOAuthAutomation.IsCodexConsentRoute(
            "auth.openai.com",
            "/sign-in-with-chatgpt/codex/consent#section"));
    }

    [Theory]
    [InlineData("google.com", "/sign-in-with-chatgpt/codex/consent")]
    [InlineData("auth.openai.com", "/other/path")]
    [InlineData("auth.openai.com", "/sign-in-with-chatgpt/codex/")]
    [InlineData("auth.openai.com", "/sign-in-with-chatgpt/codex")]
    [InlineData("auth.openai.com", "/")]
    [InlineData("auth.openai.com", "")]
    [InlineData("", "/sign-in-with-chatgpt/codex/consent")]
    public void IsCodexConsentRoute_non_matching_inputs_returns_false(string host, string path)
    {
        Assert.False(CodexOAuthAutomation.IsCodexConsentRoute(host, path));
    }

    [Fact]
    public void IsCodexConsentRoute_case_insensitive_host()
    {
        Assert.True(CodexOAuthAutomation.IsCodexConsentRoute(
            "AUTH.OPENAI.COM",
            "/sign-in-with-chatgpt/codex/consent"));
    }

    // ====================================================================
    // IsOpenAIAccountPickerRoute (internal static)
    // ====================================================================

    [Fact]
    public void IsOpenAIAccountPickerRoute_exact_match_returns_true()
    {
        Assert.True(CodexOAuthAutomation.IsOpenAIAccountPickerRoute(
            "auth.openai.com",
            "/choose-an-account"));
    }

    [Fact]
    public void IsOpenAIAccountPickerRoute_with_trailing_slash_returns_true()
    {
        Assert.True(CodexOAuthAutomation.IsOpenAIAccountPickerRoute(
            "auth.openai.com",
            "/choose-an-account/"));
    }

    [Fact]
    public void IsOpenAIAccountPickerRoute_with_query_string_returns_true()
    {
        Assert.True(CodexOAuthAutomation.IsOpenAIAccountPickerRoute(
            "auth.openai.com",
            "/choose-an-account?continue=https%3A%2F%2Fchatgpt.com"));
    }

    [Fact]
    public void IsOpenAIAccountPickerRoute_with_query_and_fragment_returns_true()
    {
        Assert.True(CodexOAuthAutomation.IsOpenAIAccountPickerRoute(
            "auth.openai.com",
            "/choose-an-account?key=val#top"));
    }

    [Theory]
    [InlineData("google.com", "/choose-an-account")]
    [InlineData("auth.openai.com", "/other/path")]
    [InlineData("auth.openai.com", "/choose-an-account/extra")]
    [InlineData("auth.openai.com", "/")]
    [InlineData("auth.openai.com", "")]
    [InlineData("openai.com", "/choose-an-account")]
    public void IsOpenAIAccountPickerRoute_non_matching_inputs_returns_false(string host, string path)
    {
        Assert.False(CodexOAuthAutomation.IsOpenAIAccountPickerRoute(host, path));
    }

    [Fact]
    public void IsOpenAIAccountPickerRoute_case_insensitive_host()
    {
        Assert.True(CodexOAuthAutomation.IsOpenAIAccountPickerRoute(
            "AUTH.OPENAI.COM",
            "/choose-an-account"));
    }

    // ====================================================================
    // Codex-specific decision logic (tested via TestableCodexAutomation)
    //
    // CodexOAuthAutomation is sealed and its constructor requires a real
    // ChromeCdpClient. These tests exercise the same decision logic by
    // calling the TestableCodexAutomation subclass that replicates the
    // sealed override conditions without the CDP dependency.
    // ====================================================================

    [Fact]
    public void CheckCompletion_null_provider_state_returns_incomplete()
    {
        var sut = new TestableCodexAutomation();
        var state = new GoogleOAuthFlowAutomation.CombinedOAuthPageState
        {
            ProviderState = null
        };

        var result = sut.InvokeCheckCompletion(state);

        Assert.False(result.IsComplete);
        Assert.Null(result.Result);
    }

    [Fact]
    public void CheckCompletion_target_service_returns_complete_already_authorized()
    {
        var sut = new TestableCodexAutomation();
        var state = new GoogleOAuthFlowAutomation.CombinedOAuthPageState
        {
            ProviderState = new CodexOAuthPageState
            {
                CurrentUrl = "https://chatgpt.com/codex",
                IsOpenAIOAuthPage = false,
                IsTargetService = true,
                HasGoogleLoginButton = false,
                HasCodexConsentButton = false,
                HasOpenAIAccountPicker = false
            }
        };

        var result = sut.InvokeCheckCompletion(state);

        Assert.True(result.IsComplete);
        Assert.NotNull(result.Result);
        Assert.True(result.Result.Success);
        Assert.True(result.Result.AlreadyAuthorized);
        Assert.Contains("target service", result.Result.Message);
    }

    [Fact]
    public void CheckCompletion_openai_oauth_page_not_target_returns_incomplete()
    {
        var sut = new TestableCodexAutomation();
        var state = new GoogleOAuthFlowAutomation.CombinedOAuthPageState
        {
            ProviderState = new CodexOAuthPageState
            {
                CurrentUrl = "https://auth.openai.com/authorize",
                IsOpenAIOAuthPage = true,
                IsTargetService = false,
                HasGoogleLoginButton = true,
                HasCodexConsentButton = false,
                HasOpenAIAccountPicker = false
            }
        };

        var result = sut.InvokeCheckCompletion(state);

        Assert.False(result.IsComplete);
        Assert.Null(result.Result);
    }

    [Fact]
    public void ShouldClickProviderInitialButton_openai_page_no_pickers_returns_true()
    {
        var sut = new TestableCodexAutomation();
        var state = CreateCodexCombinedState(
            currentUrl: "https://auth.openai.com/authorize",
            isOpenAIOAuthPage: true,
            hasOpenAIAccountPicker: false,
            hasCodexConsentButton: false,
            hasGoogleAccountPicker: false);

        Assert.True(sut.InvokeShouldClickProviderInitialButton(state));
    }

    [Fact]
    public void ShouldClickProviderInitialButton_consent_button_present_returns_false()
    {
        var sut = new TestableCodexAutomation();
        var state = CreateCodexCombinedState(
            currentUrl: "https://auth.openai.com/sign-in-with-chatgpt/codex/consent",
            isOpenAIOAuthPage: true,
            hasOpenAIAccountPicker: false,
            hasCodexConsentButton: true,
            hasGoogleAccountPicker: false);

        Assert.False(sut.InvokeShouldClickProviderInitialButton(state));
    }

    [Fact]
    public void ShouldClickProviderInitialButton_account_picker_present_returns_false()
    {
        var sut = new TestableCodexAutomation();
        var state = CreateCodexCombinedState(
            currentUrl: "https://auth.openai.com/choose-an-account",
            isOpenAIOAuthPage: true,
            hasOpenAIAccountPicker: true,
            hasCodexConsentButton: false,
            hasGoogleAccountPicker: false);

        Assert.False(sut.InvokeShouldClickProviderInitialButton(state));
    }

    [Fact]
    public void ShouldClickProviderInitialButton_google_page_returns_false()
    {
        var sut = new TestableCodexAutomation();
        var googleState = new GoogleOAuthPageState
        {
            CurrentUrl = "https://accounts.google.com/o/oauth2/v2/auth",
            HasAccountPicker = true,
            HasGoogleTotpInput = false,
            HasGoogleConsentButton = false
        };
        var state = new GoogleOAuthFlowAutomation.CombinedOAuthPageState
        {
            ProviderState = new CodexOAuthPageState
            {
                CurrentUrl = "https://accounts.google.com/o/oauth2/v2/auth",
                IsOpenAIOAuthPage = false,
                IsTargetService = false,
                HasGoogleLoginButton = false,
                HasCodexConsentButton = false,
                HasOpenAIAccountPicker = false
            },
            GoogleState = googleState
        };

        Assert.False(sut.InvokeShouldClickProviderInitialButton(state));
    }

    [Fact]
    public void ShouldClickProviderInitialButton_not_openai_page_returns_false()
    {
        var sut = new TestableCodexAutomation();
        var state = CreateCodexCombinedState(
            currentUrl: "https://other-provider.com/auth",
            isOpenAIOAuthPage: false,
            hasOpenAIAccountPicker: false,
            hasCodexConsentButton: false,
            hasGoogleAccountPicker: false);

        Assert.False(sut.InvokeShouldClickProviderInitialButton(state));
    }

    [Fact]
    public void ShouldClickProviderAccountPicker_openai_with_picker_returns_true()
    {
        var sut = new TestableCodexAutomation();
        var state = CreateCodexCombinedState(
            currentUrl: "https://auth.openai.com/choose-an-account",
            isOpenAIOAuthPage: true,
            hasOpenAIAccountPicker: true,
            hasCodexConsentButton: false,
            hasGoogleAccountPicker: false);

        Assert.True(sut.InvokeShouldClickProviderAccountPicker(state));
    }

    [Fact]
    public void ShouldClickProviderAccountPicker_openai_without_picker_returns_false()
    {
        var sut = new TestableCodexAutomation();
        var state = CreateCodexCombinedState(
            currentUrl: "https://auth.openai.com/authorize",
            isOpenAIOAuthPage: true,
            hasOpenAIAccountPicker: false,
            hasCodexConsentButton: false,
            hasGoogleAccountPicker: false);

        Assert.False(sut.InvokeShouldClickProviderAccountPicker(state));
    }

    [Fact]
    public void ShouldClickProviderAccountPicker_google_page_returns_false()
    {
        var sut = new TestableCodexAutomation();
        var googleState = new GoogleOAuthPageState
        {
            CurrentUrl = "https://accounts.google.com/choose-account",
            HasAccountPicker = true,
            HasGoogleTotpInput = false,
            HasGoogleConsentButton = false
        };
        var state = new GoogleOAuthFlowAutomation.CombinedOAuthPageState
        {
            ProviderState = new CodexOAuthPageState
            {
                CurrentUrl = "https://accounts.google.com/choose-account",
                IsOpenAIOAuthPage = false,
                IsTargetService = false,
                HasGoogleLoginButton = false,
                HasCodexConsentButton = false,
                HasOpenAIAccountPicker = true
            },
            GoogleState = googleState
        };

        Assert.False(sut.InvokeShouldClickProviderAccountPicker(state));
    }

    [Fact]
    public void ShouldClickProviderAccountPicker_non_openai_provider_returns_false()
    {
        var sut = new TestableCodexAutomation();
        var state = CreateCodexCombinedState(
            currentUrl: "https://other-provider.com/auth",
            isOpenAIOAuthPage: false,
            hasOpenAIAccountPicker: true,
            hasCodexConsentButton: false,
            hasGoogleAccountPicker: false);

        Assert.False(sut.InvokeShouldClickProviderAccountPicker(state));
    }

    [Fact]
    public void ShouldClickProviderConsent_consent_button_present_returns_true()
    {
        var sut = new TestableCodexAutomation();
        var state = CreateCodexCombinedState(
            currentUrl: "https://auth.openai.com/sign-in-with-chatgpt/codex/consent",
            isOpenAIOAuthPage: true,
            hasOpenAIAccountPicker: false,
            hasCodexConsentButton: true,
            hasGoogleAccountPicker: false);

        Assert.True(sut.InvokeShouldClickProviderConsent(state));
    }

    [Fact]
    public void ShouldClickProviderConsent_no_consent_button_returns_false()
    {
        var sut = new TestableCodexAutomation();
        var state = CreateCodexCombinedState(
            currentUrl: "https://auth.openai.com/authorize",
            isOpenAIOAuthPage: true,
            hasOpenAIAccountPicker: false,
            hasCodexConsentButton: false,
            hasGoogleAccountPicker: false);

        Assert.False(sut.InvokeShouldClickProviderConsent(state));
    }

    [Fact]
    public void ShouldClickProviderConsent_google_page_returns_false()
    {
        var sut = new TestableCodexAutomation();
        var googleState = new GoogleOAuthPageState
        {
            CurrentUrl = "https://accounts.google.com/o/oauth2/v2/auth",
            HasAccountPicker = false,
            HasGoogleTotpInput = false,
            HasGoogleConsentButton = true
        };
        var state = new GoogleOAuthFlowAutomation.CombinedOAuthPageState
        {
            ProviderState = new CodexOAuthPageState
            {
                CurrentUrl = "https://accounts.google.com/o/oauth2/v2/auth",
                IsOpenAIOAuthPage = false,
                IsTargetService = false,
                HasGoogleLoginButton = false,
                HasCodexConsentButton = true,
                HasOpenAIAccountPicker = false
            },
            GoogleState = googleState
        };

        Assert.False(sut.InvokeShouldClickProviderConsent(state));
    }

    // ====================================================================
    // CodexOAuthPageState record
    // ====================================================================

    [Fact]
    public void CodexOAuthPageState_required_properties_must_be_set()
    {
        var state = new CodexOAuthPageState
        {
            CurrentUrl = "https://auth.openai.com/authorize",
            IsOpenAIOAuthPage = true,
            IsTargetService = false,
            HasGoogleLoginButton = true,
            HasCodexConsentButton = false,
            HasOpenAIAccountPicker = false
        };

        Assert.Equal("https://auth.openai.com/authorize", state.CurrentUrl);
        Assert.True(state.IsOpenAIOAuthPage);
        Assert.False(state.IsTargetService);
        Assert.True(state.HasGoogleLoginButton);
        Assert.False(state.HasCodexConsentButton);
        Assert.False(state.HasOpenAIAccountPicker);
    }

    // ====================================================================
    // CombinedOAuthPageState record (base class)
    // ====================================================================

    [Fact]
    public void CombinedOAuthPageState_no_google_state_is_not_google_page()
    {
        var state = new GoogleOAuthFlowAutomation.CombinedOAuthPageState
        {
            ProviderState = new CodexOAuthPageState
            {
                CurrentUrl = "https://auth.openai.com/authorize",
                IsOpenAIOAuthPage = true,
                IsTargetService = false,
                HasGoogleLoginButton = false,
                HasCodexConsentButton = false,
                HasOpenAIAccountPicker = false
            },
            GoogleState = null
        };

        Assert.False(state.IsGoogleOAuthPage);
        Assert.Null(state.GoogleState);
        Assert.Equal("https://auth.openai.com/authorize", state.CurrentUrl);
    }

    [Fact]
    public void CombinedOAuthPageState_with_google_state_is_google_page()
    {
        var googleState = new GoogleOAuthPageState
        {
            CurrentUrl = "https://accounts.google.com/o/oauth2/v2/auth",
            HasAccountPicker = true,
            HasGoogleTotpInput = false,
            HasGoogleConsentButton = false
        };
        var state = new GoogleOAuthFlowAutomation.CombinedOAuthPageState
        {
            ProviderState = null,
            GoogleState = googleState
        };

        Assert.True(state.IsGoogleOAuthPage);
        Assert.True(state.HasAccountPicker);
        Assert.Equal("https://accounts.google.com/o/oauth2/v2/auth", state.CurrentUrl);
    }

    [Fact]
    public void CombinedOAuthPageState_properties_delegate_to_provider_when_no_google()
    {
        var state = new GoogleOAuthFlowAutomation.CombinedOAuthPageState
        {
            ProviderState = new CodexOAuthPageState
            {
                CurrentUrl = "https://auth.openai.com/consent",
                IsOpenAIOAuthPage = true,
                IsTargetService = false,
                HasGoogleLoginButton = false,
                HasCodexConsentButton = true,
                HasOpenAIAccountPicker = false
            },
            GoogleState = null
        };

        Assert.False(state.HasGoogleTotpInput);
        Assert.False(state.HasGoogleConsentButton);
        Assert.False(state.HasAccountPicker);
    }

    [Fact]
    public void CombinedOAuthPageState_current_url_prefers_provider()
    {
        var state = new GoogleOAuthFlowAutomation.CombinedOAuthPageState
        {
            ProviderState = new CodexOAuthPageState
            {
                CurrentUrl = "https://auth.openai.com/authorize",
                IsOpenAIOAuthPage = true,
                IsTargetService = false,
                HasGoogleLoginButton = false,
                HasCodexConsentButton = false,
                HasOpenAIAccountPicker = false
            },
            GoogleState = new GoogleOAuthPageState
            {
                CurrentUrl = "https://accounts.google.com/o/oauth2/v2/auth",
                HasAccountPicker = false,
                HasGoogleTotpInput = false,
                HasGoogleConsentButton = false
            }
        };

        Assert.Equal("https://auth.openai.com/authorize", state.CurrentUrl);
    }

    // ====================================================================
    // Helpers
    // ====================================================================

    private static GoogleOAuthFlowAutomation.CombinedOAuthPageState CreateCodexCombinedState(
        string currentUrl,
        bool isOpenAIOAuthPage,
        bool hasOpenAIAccountPicker,
        bool hasCodexConsentButton,
        bool hasGoogleAccountPicker)
    {
        return new GoogleOAuthFlowAutomation.CombinedOAuthPageState
        {
            ProviderState = new CodexOAuthPageState
            {
                CurrentUrl = currentUrl,
                IsOpenAIOAuthPage = isOpenAIOAuthPage,
                IsTargetService = false,
                HasGoogleLoginButton = false,
                HasCodexConsentButton = hasCodexConsentButton,
                HasOpenAIAccountPicker = hasOpenAIAccountPicker
            },
            GoogleState = hasGoogleAccountPicker
                ? new GoogleOAuthPageState
                {
                    CurrentUrl = currentUrl,
                    HasAccountPicker = true,
                    HasGoogleTotpInput = false,
                    HasGoogleConsentButton = false
                }
                : null
        };
    }

    /// <summary>
    /// Testable subclass of GoogleOAuthFlowAutomation that replicates CodexOAuthAutomation's
    /// sealed decision logic without requiring a live Chrome browser. A real ChromeCdpClient
    /// is constructed with a dummy loopback URI (satisfies the base constructor validation);
    /// none of the test code paths invoke CDP methods.
    /// </summary>
    private sealed class TestableCodexAutomation : GoogleOAuthFlowAutomation
    {
        internal TestableCodexAutomation()
            : base(
                client: new ChromeCdpClient(new Uri("http://127.0.0.1:1")),
                sessionId: "",
                targetId: "",
                profileEmail: "test@test.com")
        {
        }

        protected override Task<ProviderOAuthPageState?> ReadProviderPageStateAsync(
            System.Threading.CancellationToken cancellationToken)
            => Task.FromResult<ProviderOAuthPageState?>(null);

        protected override CompletionCheckResult CheckCompletion(CombinedOAuthPageState state)
        {
            var providerState = state.ProviderState as CodexOAuthPageState;
            if (providerState == null)
                return new CompletionCheckResult(IsComplete: false);

            if (providerState.IsTargetService)
            {
                return new CompletionCheckResult(
                    IsComplete: true,
                    Result: new OAuthConsentResult(
                        Success: true,
                        AlreadyAuthorized: true,
                        Message: "Already authorized - on target service"));
            }

            return new CompletionCheckResult(IsComplete: false);
        }

        protected override void LogPageState(CombinedOAuthPageState state) { }

        /// <summary>
        /// Replicates CodexOAuthAutomation.ShouldClickProviderInitialButton logic.
        /// </summary>
        internal bool InvokeShouldClickProviderInitialButton(CombinedOAuthPageState state)
        {
            var providerState = state.ProviderState as CodexOAuthPageState;
            if (providerState == null)
                return false;

            return !state.IsGoogleOAuthPage &&
                   providerState.IsOpenAIOAuthPage &&
                   !providerState.HasOpenAIAccountPicker &&
                   !providerState.HasCodexConsentButton &&
                   !state.HasAccountPicker;
        }

        /// <summary>
        /// Replicates CodexOAuthAutomation.ShouldClickProviderAccountPicker logic.
        /// </summary>
        internal bool InvokeShouldClickProviderAccountPicker(CombinedOAuthPageState state)
        {
            var providerState = state.ProviderState as CodexOAuthPageState;
            return !state.IsGoogleOAuthPage &&
                   providerState?.IsOpenAIOAuthPage == true &&
                   providerState.HasOpenAIAccountPicker;
        }

        /// <summary>
        /// Replicates CodexOAuthAutomation.ShouldClickProviderConsent logic.
        /// </summary>
        internal bool InvokeShouldClickProviderConsent(CombinedOAuthPageState state)
        {
            var providerState = state.ProviderState as CodexOAuthPageState;
            return !state.IsGoogleOAuthPage &&
                   providerState?.IsOpenAIOAuthPage == true &&
                   providerState.HasCodexConsentButton;
        }

        /// <summary>
        /// Exposes CheckCompletion for testing.
        /// </summary>
        internal CompletionCheckResult InvokeCheckCompletion(CombinedOAuthPageState state)
            => CheckCompletion(state);
    }
}
