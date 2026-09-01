using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.Core.Tests;

public class GoogleLoginStateMachineTests
{
    [Fact]
    public async Task RunAsync_completes_full_email_password_totp_flow()
    {
        var credential = new GoogleLoginCredential(
            "profile-1",
            "user@example.com",
            "password123",
            "JBSWY3DPEHPK3PXP");

        var browser = new FakeBrowser()
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin"),
                HasEmailField: true,
                HasPasswordField: false,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/password"),
                HasEmailField: false,
                HasPasswordField: true,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/challenge/totp"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: true,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://myaccount.google.com/"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: true,
                HasManualChallenge: false));

        var result = await GoogleLoginStateMachine.RunAsync(browser, credential, CancellationToken.None);

        Assert.Equal(GoogleLoginResultCategory.Success, result.Category);
        Assert.Equal(new[] { "Email", "Password", "Totp" }, browser.FilledFields);
        Assert.Equal(new[] { "Email", "Password", "Totp" }, browser.SubmittedFields);
    }

    [Fact]
    public async Task RunAsync_succeeds_when_already_partially_authenticated()
    {
        var credential = new GoogleLoginCredential(
            "profile-1",
            "user@example.com",
            "password123",
            "JBSWY3DPEHPK3PXP");

        var browser = new FakeBrowser()
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin"),
                HasEmailField: true,
                HasPasswordField: false,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://myaccount.google.com/"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: true,
                HasManualChallenge: false));

        var result = await GoogleLoginStateMachine.RunAsync(browser, credential, CancellationToken.None);

        Assert.Equal(GoogleLoginResultCategory.Success, result.Category);
        Assert.Equal(new[] { "Email" }, browser.FilledFields);
        Assert.Equal(new[] { "Email" }, browser.SubmittedFields);
    }

    [Fact]
    public async Task RunAsync_succeeds_when_completion_signal_present_initially()
    {
        var credential = new GoogleLoginCredential(
            "profile-1",
            "user@example.com",
            "password123",
            "JBSWY3DPEHPK3PXP");

        var browser = new FakeBrowser()
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: true,
                HasManualChallenge: false));

        var result = await GoogleLoginStateMachine.RunAsync(browser, credential, CancellationToken.None);

        Assert.Equal(GoogleLoginResultCategory.Success, result.Category);
        Assert.Empty(browser.FilledFields);
        Assert.Empty(browser.SubmittedFields);
    }

    [Fact]
    public async Task RunAsync_waits_for_manual_challenge_resolution_at_entry()
    {
        var credential = new GoogleLoginCredential(
            "profile-1",
            "user@example.com",
            "password123",
            "JBSWY3DPEHPK3PXP");

        var browser = new FakeBrowser()
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin"),
                HasEmailField: true,
                HasPasswordField: false,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: true))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin"),
                HasEmailField: true,
                HasPasswordField: false,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://myaccount.google.com/"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: true,
                HasManualChallenge: false));

        var result = await GoogleLoginStateMachine.RunAsync(browser, credential, CancellationToken.None);

        Assert.Equal(GoogleLoginResultCategory.Success, result.Category);
        Assert.Equal(new[] { "Email" }, browser.FilledFields);
    }

    [Fact]
    public async Task RunAsync_waits_for_manual_challenge_resolution_after_email()
    {
        var credential = new GoogleLoginCredential(
            "profile-1",
            "user@example.com",
            "password123",
            "JBSWY3DPEHPK3PXP");

        var browser = new FakeBrowser()
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin"),
                HasEmailField: true,
                HasPasswordField: false,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/challenge"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: true))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/password"),
                HasEmailField: false,
                HasPasswordField: true,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://myaccount.google.com/"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: true,
                HasManualChallenge: false));

        var result = await GoogleLoginStateMachine.RunAsync(browser, credential, CancellationToken.None);

        Assert.Equal(GoogleLoginResultCategory.Success, result.Category);
        Assert.Equal(new[] { "Email", "Password" }, browser.FilledFields);
    }

    [Fact]
    public async Task RunAsync_waits_for_manual_challenge_resolution_after_password()
    {
        var credential = new GoogleLoginCredential(
            "profile-1",
            "user@example.com",
            "password123",
            "JBSWY3DPEHPK3PXP");

        var browser = new FakeBrowser()
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin"),
                HasEmailField: true,
                HasPasswordField: false,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/password"),
                HasEmailField: false,
                HasPasswordField: true,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/challenge"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: true))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/challenge/totp"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: true,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://myaccount.google.com/"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: true,
                HasManualChallenge: false));

        var result = await GoogleLoginStateMachine.RunAsync(browser, credential, CancellationToken.None);

        Assert.Equal(GoogleLoginResultCategory.Success, result.Category);
        Assert.Equal(new[] { "Email", "Password", "Totp" }, browser.FilledFields);
    }

    [Fact]
    public async Task RunAsync_waits_for_manual_challenge_resolution_after_totp()
    {
        var credential = new GoogleLoginCredential(
            "profile-1",
            "user@example.com",
            "password123",
            "JBSWY3DPEHPK3PXP");

        var browser = new FakeBrowser()
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin"),
                HasEmailField: true,
                HasPasswordField: false,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/password"),
                HasEmailField: false,
                HasPasswordField: true,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/challenge/totp"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: true,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/challenge"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: true))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://myaccount.google.com/"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: true,
                HasManualChallenge: false));

        var result = await GoogleLoginStateMachine.RunAsync(browser, credential, CancellationToken.None);

        Assert.Equal(GoogleLoginResultCategory.Success, result.Category);
        Assert.Equal(new[] { "Email", "Password", "Totp" }, browser.FilledFields);
    }

    [Fact]
    public async Task RunAsync_resumes_password_step_after_manual_challenge_resolution()
    {
        var credential = new GoogleLoginCredential(
            "profile-1",
            "user@example.com",
            "password123",
            "JBSWY3DPEHPK3PXP");

        var browser = new FakeBrowser()
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin"),
                HasEmailField: true,
                HasPasswordField: false,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/v3/signin/challenge/recaptcha"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: true))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/v3/signin/challenge/pwd"),
                HasEmailField: false,
                HasPasswordField: true,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://myaccount.google.com/"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: true,
                HasManualChallenge: false));

        var result = await GoogleLoginStateMachine.RunAsync(browser, credential, CancellationToken.None);

        Assert.Equal(GoogleLoginResultCategory.Success, result.Category);
        Assert.Equal(new[] { "Email", "Password" }, browser.FilledFields);
        Assert.Equal(new[] { "Email", "Password" }, browser.SubmittedFields);
    }

    [Fact]
    public async Task RunAsync_rejects_wrong_entry_origin()
    {
        var credential = new GoogleLoginCredential(
            "profile-1",
            "user@example.com",
            "password123",
            "JBSWY3DPEHPK3PXP");

        var browser = new FakeBrowser()
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://evil.com/phishing"),
                HasEmailField: true,
                HasPasswordField: false,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false));

        var result = await GoogleLoginStateMachine.RunAsync(browser, credential, CancellationToken.None);

        Assert.Equal(GoogleLoginResultCategory.UnsupportedPage, result.Category);
        Assert.Contains("Wrong origin", result.Message);
        Assert.Contains("evil.com", result.Message);
        Assert.Empty(browser.FilledFields);
    }

    [Fact]
    public async Task RunAsync_rejects_navigation_to_wrong_origin_after_email()
    {
        var credential = new GoogleLoginCredential(
            "profile-1",
            "user@example.com",
            "password123",
            "JBSWY3DPEHPK3PXP");

        var browser = new FakeBrowser()
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin"),
                HasEmailField: true,
                HasPasswordField: false,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://evil.com/phishing"),
                HasEmailField: false,
                HasPasswordField: true,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false));

        var result = await GoogleLoginStateMachine.RunAsync(browser, credential, CancellationToken.None);

        Assert.Equal(GoogleLoginResultCategory.UnsupportedPage, result.Category);
        Assert.Contains("unexpected origin", result.Message);
        Assert.Contains("evil.com", result.Message);
        Assert.Equal(new[] { "Email" }, browser.FilledFields);
    }

    [Fact]
    public async Task RunAsync_rejects_unrecognized_field_combination()
    {
        var credential = new GoogleLoginCredential(
            "profile-1",
            "user@example.com",
            "password123",
            "JBSWY3DPEHPK3PXP");

        var browser = new FakeBrowser()
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin"),
                HasEmailField: true, // Set to true to skip wait loop
                HasPasswordField: false,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState( // After email submit
                new Uri("https://accounts.google.com/signin"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false));

        var result = await GoogleLoginStateMachine.RunAsync(browser, credential, CancellationToken.None);

        Assert.Equal(GoogleLoginResultCategory.UnsupportedPage, result.Category);
        Assert.Contains("Unrecognized page state at", result.Message);
    }

    [Fact]
    public async Task RunAsync_returns_cancelled_when_cancellation_requested_before_start()
    {
        var credential = new GoogleLoginCredential(
            "profile-1",
            "user@example.com",
            "password123",
            "JBSWY3DPEHPK3PXP");

        var browser = new FakeBrowser()
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin"),
                HasEmailField: true,
                HasPasswordField: false,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false));

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await GoogleLoginStateMachine.RunAsync(browser, credential, cts.Token);

        Assert.Equal(GoogleLoginResultCategory.Cancelled, result.Category);
    }

    [Fact]
    public async Task RunAsync_returns_cancelled_when_cancelled_during_fill()
    {
        var credential = new GoogleLoginCredential(
            "profile-1",
            "user@example.com",
            "password123",
            "JBSWY3DPEHPK3PXP");

        var cts = new CancellationTokenSource();
        var browser = new FakeBrowser()
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin"),
                HasEmailField: true,
                HasPasswordField: false,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .OnFill(() => cts.Cancel());

        var result = await GoogleLoginStateMachine.RunAsync(browser, credential, cts.Token);

        Assert.Equal(GoogleLoginResultCategory.Cancelled, result.Category);
    }

    [Fact]
    public async Task RunAsync_returns_browser_disconnected_when_browser_disposed()
    {
        var credential = new GoogleLoginCredential(
            "profile-1",
            "user@example.com",
            "password123",
            "JBSWY3DPEHPK3PXP");

        var browser = new FakeBrowser()
            .ThrowOnReadState(new ObjectDisposedException("Browser"));

        var result = await GoogleLoginStateMachine.RunAsync(browser, credential, CancellationToken.None);

        Assert.Equal(GoogleLoginResultCategory.BrowserDisconnected, result.Category);
    }

    [Fact]
    public async Task RunAsync_returns_browser_disconnected_on_disconnect_exception()
    {
        var credential = new GoogleLoginCredential(
            "profile-1",
            "user@example.com",
            "password123",
            "JBSWY3DPEHPK3PXP");

        var browser = new FakeBrowser()
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin"),
                HasEmailField: true,
                HasPasswordField: false,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ThrowOnFill(new InvalidOperationException("Browser connection closed"));

        var result = await GoogleLoginStateMachine.RunAsync(browser, credential, CancellationToken.None);

        Assert.Equal(GoogleLoginResultCategory.BrowserDisconnected, result.Category);
    }

    [Fact]
    public async Task RunAsync_returns_unsupported_page_for_control_failure()
    {
        var credential = new GoogleLoginCredential(
            "profile-1",
            "user@example.com",
            "password123",
            "JBSWY3DPEHPK3PXP");

        var browser = new FakeBrowser()
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin"),
                HasEmailField: true,
                HasPasswordField: false,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ThrowOnFill(new InvalidOperationException("Field not found"));

        var result = await GoogleLoginStateMachine.RunAsync(browser, credential, CancellationToken.None);

        Assert.Equal(GoogleLoginResultCategory.UnsupportedPage, result.Category);
        Assert.DoesNotContain("Browser disconnected", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_does_not_record_secret_values_in_fake_browser()
    {
        var credential = new GoogleLoginCredential(
            "profile-1",
            "user@example.com",
            "secret-password-123",
            "JBSWY3DPEHPK3PXP");

        var browser = new FakeBrowser()
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin"),
                HasEmailField: true,
                HasPasswordField: false,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/password"),
                HasEmailField: false,
                HasPasswordField: true,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://myaccount.google.com/"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: true,
                HasManualChallenge: false));

        await GoogleLoginStateMachine.RunAsync(browser, credential, CancellationToken.None);

        // Verify fake browser only recorded field names, not values
        Assert.Equal(new[] { "Email", "Password" }, browser.FilledFields);
        Assert.DoesNotContain("secret-password-123", string.Join(",", browser.FilledFields));
    }

    [Fact]
    public async Task RunAsync_accepts_www_google_com_as_completion_host()
    {
        var credential = new GoogleLoginCredential(
            "profile-1",
            "user@example.com",
            "password123",
            "JBSWY3DPEHPK3PXP");

        var browser = new FakeBrowser()
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin"),
                HasEmailField: true,
                HasPasswordField: false,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://www.google.com/"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: true,
                HasManualChallenge: false));

        var result = await GoogleLoginStateMachine.RunAsync(browser, credential, CancellationToken.None);

        Assert.Equal(GoogleLoginResultCategory.Success, result.Category);
    }

    [Fact]
    public async Task RunAsync_rejects_completion_on_wrong_host()
    {
        var credential = new GoogleLoginCredential(
            "profile-1",
            "user@example.com",
            "password123",
            "JBSWY3DPEHPK3PXP");

        var browser = new FakeBrowser()
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin"),
                HasEmailField: true,
                HasPasswordField: false,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://evil.com/success"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: true,
                HasManualChallenge: false));

        var result = await GoogleLoginStateMachine.RunAsync(browser, credential, CancellationToken.None);

        Assert.Equal(GoogleLoginResultCategory.UnsupportedPage, result.Category);
    }

    [Fact]
    public async Task RunAsync_returns_invalid_credentials_on_bad_totp_secret()
    {
        var credential = new GoogleLoginCredential(
            "profile-1",
            "user@example.com",
            "password123",
            "INVALID!!!"); // Invalid Base32

        var browser = new FakeBrowser()
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin"),
                HasEmailField: true,
                HasPasswordField: false,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/password"),
                HasEmailField: false,
                HasPasswordField: true,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/challenge/totp"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: true,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false));

        var result = await GoogleLoginStateMachine.RunAsync(browser, credential, CancellationToken.None);

        Assert.Equal(GoogleLoginResultCategory.InvalidCredentials, result.Category);
    }

    private sealed class FakeBrowser : IGoogleLoginBrowser
    {
        private readonly Queue<GoogleLoginPageState> _states = new();
        private readonly List<string> _filledFields = new();
        private readonly List<string> _submittedFields = new();
        private Action? _onFill;
        private Exception? _readStateException;
        private Exception? _fillException;

        public IReadOnlyList<string> FilledFields => _filledFields;
        public IReadOnlyList<string> SubmittedFields => _submittedFields;

        public FakeBrowser ReturnState(GoogleLoginPageState state)
        {
            _states.Enqueue(state);
            return this;
        }

        public FakeBrowser OnFill(Action action)
        {
            _onFill = action;
            return this;
        }

        public FakeBrowser ThrowOnReadState(Exception exception)
        {
            _readStateException = exception;
            return this;
        }

        public FakeBrowser ThrowOnFill(Exception exception)
        {
            _fillException = exception;
            return this;
        }

        public Task<GoogleLoginPageState> ReadStateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_readStateException != null)
            {
                throw _readStateException;
            }

            if (_states.Count == 0)
            {
                throw new InvalidOperationException("No more states configured in fake browser");
            }

            return Task.FromResult(_states.Dequeue());
        }

        public Task FillAsync(GoogleLoginField field, string value, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_fillException != null)
            {
                throw _fillException;
            }

            _onFill?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();

            // Record only field name, never the value
            _filledFields.Add(field.ToString());
            return Task.CompletedTask;
        }

        public Task SubmitAsync(GoogleLoginField field, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _submittedFields.Add(field.ToString());
            return Task.CompletedTask;
        }

        public Task<bool> TrySelectAuthenticatorMethodAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(false);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
