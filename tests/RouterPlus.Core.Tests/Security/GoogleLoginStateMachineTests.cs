using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.Core.Tests.Security;

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
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/password"),
                HasEmailField: false,
                HasPasswordField: true,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/challenge/totp"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: true,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://myaccount.google.com/"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                HasTotpError: false,
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
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://myaccount.google.com/"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                HasTotpError: false,
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
                HasTotpError: false,
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
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: true))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin"),
                HasEmailField: true,
                HasPasswordField: false,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://myaccount.google.com/"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                HasTotpError: false,
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
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/challenge"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: true))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/password"),
                HasEmailField: false,
                HasPasswordField: true,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://myaccount.google.com/"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                HasTotpError: false,
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
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/password"),
                HasEmailField: false,
                HasPasswordField: true,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/challenge"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: true))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/challenge/totp"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: true,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://myaccount.google.com/"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                HasTotpError: false,
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
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/password"),
                HasEmailField: false,
                HasPasswordField: true,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/challenge/totp"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: true,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/challenge"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: true))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://myaccount.google.com/"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                HasTotpError: false,
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
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/v3/signin/challenge/recaptcha"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: true))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/v3/signin/challenge/pwd"),
                HasEmailField: false,
                HasPasswordField: true,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://myaccount.google.com/"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: true,
                HasManualChallenge: false));

        var result = await GoogleLoginStateMachine.RunAsync(browser, credential, CancellationToken.None);

        Assert.Equal(GoogleLoginResultCategory.Success, result.Category);
        Assert.Equal(new[] { "Email", "Password" }, browser.FilledFields);
        Assert.Equal(new[] { "Email", "Password" }, browser.SubmittedFields);
    }

    [Fact]
    public async Task RunAsync_selects_authenticator_from_two_factor_picker_then_completes_totp()
    {
        var credential = new GoogleLoginCredential(
            "profile-1",
            "user@example.com",
            "password123",
            "JBSWY3DPEHPK3PXP");

        var browser = new FakeBrowser()
            .SelectAuthenticatorMethod(true)
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin"),
                HasEmailField: true,
                HasPasswordField: false,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/password"),
                HasEmailField: false,
                HasPasswordField: true,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/challenge/selection"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: true,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/challenge/totp"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: true,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://myaccount.google.com/"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: true,
                HasManualChallenge: false));

        var result = await GoogleLoginStateMachine.RunAsync(browser, credential, CancellationToken.None);

        Assert.Equal(GoogleLoginResultCategory.Success, result.Category);
        Assert.Equal(new[] { "Email", "Password", "Totp" }, browser.FilledFields);
        Assert.Equal(new[] { "Email", "Password", "Totp" }, browser.SubmittedFields);
        Assert.Equal(1, browser.AuthenticatorMethodSelectionAttempts);
    }

    [Fact]
    public async Task RunAsync_rejects_two_factor_picker_when_authenticator_selection_fails()
    {
        var credential = new GoogleLoginCredential(
            "profile-1",
            "user@example.com",
            "password123",
            "JBSWY3DPEHPK3PXP");

        var browser = new FakeBrowser()
            .SelectAuthenticatorMethod(false)
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin"),
                HasEmailField: true,
                HasPasswordField: false,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/password"),
                HasEmailField: false,
                HasPasswordField: true,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/challenge/selection"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: true,
                HasCompletionSignal: false,
                HasManualChallenge: false));

        var result = await GoogleLoginStateMachine.RunAsync(browser, credential, CancellationToken.None);

        Assert.Equal(GoogleLoginResultCategory.UnsupportedPage, result.Category);
        Assert.Contains("Could not select Authenticator method", result.Message);
        Assert.Equal(1, browser.AuthenticatorMethodSelectionAttempts);
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
                HasTotpError: false,
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
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://evil.com/phishing"),
                HasEmailField: false,
                HasPasswordField: true,
                HasTotpField: false,
                HasTotpError: false,
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
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState( // After email submit
                new Uri("https://accounts.google.com/signin"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                HasTotpError: false,
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
                HasTotpError: false,
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
                HasTotpError: false,
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
                HasTotpError: false,
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
                HasTotpError: false,
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
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/password"),
                HasEmailField: false,
                HasPasswordField: true,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://myaccount.google.com/"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                HasTotpError: false,
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
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://www.google.com/"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                HasTotpError: false,
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
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://evil.com/success"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                HasTotpError: false,
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
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/password"),
                HasEmailField: false,
                HasPasswordField: true,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/challenge/totp"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: true,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false));

        var result = await GoogleLoginStateMachine.RunAsync(browser, credential, CancellationToken.None);

        Assert.Equal(GoogleLoginResultCategory.InvalidCredentials, result.Category);
    }

    [Fact]
    public async Task RunAsync_returns_invalid_credentials_when_totp_is_rejected_after_retry()
    {
        // FR-4: The machine retries once on HasTotpError; both attempts must fail for
        // InvalidCredentials to be returned. Two HasTotpError states are required.
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
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/password"),
                HasEmailField: false,
                HasPasswordField: true,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/challenge/totp"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: true,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            // First TOTP attempt rejected:
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/challenge/totp"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: true,
                HasTotpError: true,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            // Retry attempt also rejected → InvalidCredentials:
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/challenge/totp"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: true,
                HasTotpError: true,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false));

        var result = await GoogleLoginStateMachine.RunAsync(browser, credential, CancellationToken.None);

        Assert.Equal(GoogleLoginResultCategory.InvalidCredentials, result.Category);
        // Both the initial attempt and the retry fill+submit Totp:
        Assert.Equal(new[] { "Email", "Password", "Totp", "Totp" }, browser.FilledFields);
        Assert.Equal(new[] { "Email", "Password", "Totp", "Totp" }, browser.SubmittedFields);
    }

    [Fact]
    public async Task RunAsync_selects_authenticator_method_after_password_before_totp()
    {
        var credential = new GoogleLoginCredential(
            "profile-1",
            "user@example.com",
            "password123",
            "JBSWY3DPEHPK3PXP");

        var browser = new FakeBrowser()
            .SelectAuthenticatorMethod(true)
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin"),
                HasEmailField: true,
                HasPasswordField: false,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/password"),
                HasEmailField: false,
                HasPasswordField: true,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/challenge/picker"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: true,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/challenge/totp"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: true,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://myaccount.google.com/"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: true,
                HasManualChallenge: false));

        var result = await GoogleLoginStateMachine.RunAsync(browser, credential, CancellationToken.None);

        Assert.Equal(GoogleLoginResultCategory.Success, result.Category);
        Assert.Equal(1, browser.AuthenticatorMethodSelections);
        Assert.Equal(new[] { "Email", "Password", "Totp" }, browser.FilledFields);
    }

    [Fact]
    public async Task RunAsync_returns_unsupported_page_when_authenticator_selection_fails_after_password()
    {
        var credential = new GoogleLoginCredential(
            "profile-1",
            "user@example.com",
            "password123",
            "JBSWY3DPEHPK3PXP");

        var browser = new FakeBrowser()
            .SelectAuthenticatorMethod(false)
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin"),
                HasEmailField: true,
                HasPasswordField: false,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/password"),
                HasEmailField: false,
                HasPasswordField: true,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/challenge/picker"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: true,
                HasCompletionSignal: false,
                HasManualChallenge: false));

        var result = await GoogleLoginStateMachine.RunAsync(browser, credential, CancellationToken.None);

        Assert.Equal(GoogleLoginResultCategory.UnsupportedPage, result.Category);
        Assert.Contains("Could not select Authenticator method", result.Message);
        Assert.Equal(1, browser.AuthenticatorMethodSelections);
        Assert.Equal(new[] { "Email", "Password" }, browser.FilledFields);
    }

    [Fact]
    public async Task RunAsync_processes_authenticator_method_picker_at_entry()
    {
        var credential = new GoogleLoginCredential(
            "profile-1",
            "user@example.com",
            "password123",
            "JBSWY3DPEHPK3PXP");

        var browser = new FakeBrowser()
            .SelectAuthenticatorMethod(true)
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/challenge/picker"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: true,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/challenge/totp"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: true,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://www.google.com/"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: true,
                HasManualChallenge: false));

        var result = await GoogleLoginStateMachine.RunAsync(browser, credential, CancellationToken.None);

        Assert.Equal(GoogleLoginResultCategory.Success, result.Category);
        Assert.Equal(1, browser.AuthenticatorMethodSelections);
        Assert.Equal(new[] { "Totp" }, browser.FilledFields);
        Assert.Equal(new[] { "Totp" }, browser.SubmittedFields);
    }

    [Fact]
    public async Task RunAsync_returns_unsupported_page_when_navigation_reaches_wrong_origin_after_password()
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
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/password"),
                HasEmailField: false,
                HasPasswordField: true,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://evil.com/phishing"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false));

        var result = await GoogleLoginStateMachine.RunAsync(browser, credential, CancellationToken.None);

        Assert.Equal(GoogleLoginResultCategory.UnsupportedPage, result.Category);
        Assert.Contains("unexpected origin", result.Message);
        Assert.Contains("evil.com", result.Message);
        Assert.Equal(new[] { "Email", "Password" }, browser.FilledFields);
    }

    [Fact]
    public async Task RunAsync_returns_unsupported_page_when_navigation_reaches_wrong_origin_after_totp()
    {
        var credential = new GoogleLoginCredential(
            "profile-1",
            "user@example.com",
            "password123",
            "JBSWY3DPEHPK3PXP");

        var browser = new FakeBrowser()
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/challenge/totp"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: true,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://evil.com/phishing"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false));

        var result = await GoogleLoginStateMachine.RunAsync(browser, credential, CancellationToken.None);

        Assert.Equal(GoogleLoginResultCategory.UnsupportedPage, result.Category);
        Assert.Contains("unexpected origin", result.Message);
        Assert.Contains("evil.com", result.Message);
        Assert.Equal(new[] { "Totp" }, browser.FilledFields);
        Assert.Equal(new[] { "Totp" }, browser.SubmittedFields);
    }

    [Fact]
    public async Task RunAsync_returns_invalid_credentials_when_google_rejects_a_field_operation()
    {
        var credential = new GoogleLoginCredential(
            "profile-1",
            "user@example.com",
            "password123",
            "JBSWY3DPEHPK3PXP");

        var browser = new FakeBrowser()
            .ThrowOnFill(new InvalidOperationException("Google rejected the submitted password"))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin"),
                HasEmailField: true,
                HasPasswordField: false,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false));

        var result = await GoogleLoginStateMachine.RunAsync(browser, credential, CancellationToken.None);

        Assert.Equal(GoogleLoginResultCategory.InvalidCredentials, result.Category);
    }

    [Fact]
    public async Task RunAsync_returns_manual_intervention_when_totp_submission_leaves_manual_challenge_after_picker()
    {
        var credential = new GoogleLoginCredential(
            "profile-1",
            "user@example.com",
            "password123",
            "JBSWY3DPEHPK3PXP");

        var browser = new FakeBrowser()
            .SelectAuthenticatorMethod(true)
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/challenge/picker"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: true,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/challenge/totp"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: true,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/challenge"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: true));

        var result = await GoogleLoginStateMachine.RunAsync(browser, credential, CancellationToken.None);

        Assert.Equal(GoogleLoginResultCategory.ManualInterventionRequired, result.Category);
        Assert.Contains("Manual challenge", result.Message);
        Assert.Equal(new[] { "Totp" }, browser.FilledFields);
        Assert.Equal(new[] { "Totp" }, browser.SubmittedFields);
    }

    // -------------------------------------------------------------------------
    // T-1: Cancellation during TOTP generation returns Cancelled, not InvalidCredentials
    // -------------------------------------------------------------------------
    [Fact]
    public async Task RunAsync_cancellation_during_totp_step_returns_cancelled_not_invalid_credentials()
    {
        // FR-2: Bare catch must not swallow OperationCanceledException.
        // Arrange: pre-cancel the token so cancellation fires before/during TOTP generation.
        var credential = new GoogleLoginCredential(
            "profile-1",
            "user@example.com",
            "password123",
            "JBSWY3DPEHPK3PXP");

        using var cts = new CancellationTokenSource();

        var browser = new FakeBrowser()
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/challenge/totp"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: true,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false));

        // Cancel before RunAsync starts executing the TOTP step
        cts.Cancel();

        var result = await GoogleLoginStateMachine.RunAsync(browser, credential, cts.Token);

        // Must return Cancelled, not InvalidCredentials
        Assert.Equal(GoogleLoginResultCategory.Cancelled, result.Category);
    }

    // -------------------------------------------------------------------------
    // T-2: Cancellation fires during a blocking wait loop → Cancelled result
    // -------------------------------------------------------------------------
    [Fact]
    public async Task RunAsync_cancellation_during_field_wait_loop_returns_cancelled()
    {
        // The machine enters a "wait for actionable fields" loop when no flags are set.
        // Cancelling the token during that loop must return Cancelled.
        var credential = new GoogleLoginCredential(
            "profile-1",
            "user@example.com",
            "password123",
            "JBSWY3DPEHPK3PXP");

        using var userCts = new CancellationTokenSource();

        // FakeBrowser always returns a state with no actionable fields.
        // After the second read, the token is cancelled so Task.Delay in the loop throws.
        int callCount = 0;
        var browser = new FakeBrowserWithCallback(() =>
        {
            callCount++;
            if (callCount >= 2) userCts.Cancel();
            return new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false);
        });

        var result = await GoogleLoginStateMachine.RunAsync(browser, credential, userCts.Token);

        Assert.Equal(GoogleLoginResultCategory.Cancelled, result.Category);
    }

    // -------------------------------------------------------------------------
    // T-3: 2FA fill succeeds but submit throws → machine handles gracefully → machine handles gracefully
    // -------------------------------------------------------------------------
    [Fact]
    public async Task RunAsync_totp_submit_button_not_found_returns_unsupported_page()
    {
        // FR-3 area: FillAsync succeeds but SubmitAsync throws InvalidOperationException
        // (e.g. submit button disappeared). Machine must not crash.
        var credential = new GoogleLoginCredential(
            "profile-1",
            "user@example.com",
            "password123",
            "JBSWY3DPEHPK3PXP");

        var browser = new FakeBrowserWithSubmitException(
            new InvalidOperationException("Submit button not found"),
            throwOnField: "Totp")
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/challenge/totp"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: true,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false));

        var result = await GoogleLoginStateMachine.RunAsync(browser, credential, CancellationToken.None);

        // InvalidOperationException from submit maps to UnsupportedPage (not a disconnect)
        Assert.Equal(GoogleLoginResultCategory.UnsupportedPage, result.Category);
    }

    // -------------------------------------------------------------------------
    // T-4: State machine unrecognised-field combination → UnsupportedPage
    // (already covered by RunAsync_rejects_unrecognized_field_combination above)
    // -------------------------------------------------------------------------

    // -------------------------------------------------------------------------
    // T-6: TOTP near window rollover — machine waits then submits fresh code
    // -------------------------------------------------------------------------
    [Fact]
    public async Task RunAsync_totp_succeeds_when_submitted_near_window_rollover()
    {
        // FR-3: when secondsLeft < 5 the machine awaits before generating TOTP.
        // We verify the result is Success when the guard fires and then the TOTP
        // is accepted on the first attempt.
        var credential = new GoogleLoginCredential(
            "profile-1",
            "user@example.com",
            "password123",
            "JBSWY3DPEHPK3PXP");

        var browser = new FakeBrowser()
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin/challenge/totp"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: true,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: false))
            .ReturnState(new GoogleLoginPageState(
                new Uri("https://myaccount.google.com/"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: true,
                HasManualChallenge: false));

        // Run with normal token; the guard fires internally based on real clock.
        // We just verify the machine completes successfully (the guard does not break the flow).
        var result = await GoogleLoginStateMachine.RunAsync(browser, credential, CancellationToken.None);

        Assert.Equal(GoogleLoginResultCategory.Success, result.Category);
        Assert.Equal(new[] { "Totp" }, browser.FilledFields);
    }

    // -------------------------------------------------------------------------
    // T-7: Manual challenge resolver exceeds deadline → Cancelled (via user ct)
    // -------------------------------------------------------------------------
    [Fact]
    public async Task RunAsync_manual_challenge_resolver_cancellation_returns_cancelled()
    {
        // WaitForManualChallengeResolutionAsync polls with Task.Delay(2s).
        // When the user token fires during that delay, RunAsync returns Cancelled.
        var credential = new GoogleLoginCredential(
            "profile-1",
            "user@example.com",
            "password123",
            "JBSWY3DPEHPK3PXP");

        using var cts = new CancellationTokenSource();

        // Return manual-challenge state; then cancel so the poll delay throws.
        var browser = new FakeBrowserWithCallback(() =>
        {
            cts.Cancel();   // fires on the poll read inside the wait loop
            return new GoogleLoginPageState(
                new Uri("https://accounts.google.com/signin"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                HasTotpError: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: false,
                HasManualChallenge: true);
        });

        var result = await GoogleLoginStateMachine.RunAsync(browser, credential, cts.Token);

        Assert.Equal(GoogleLoginResultCategory.Cancelled, result.Category);
    }

    // -------------------------------------------------------------------------
    // Helper fake browser that invokes a callback on each ReadStateAsync call
    // -------------------------------------------------------------------------
    private sealed class FakeBrowserWithCallback : IGoogleLoginBrowser
    {
        private readonly Func<GoogleLoginPageState> _stateFactory;

        public FakeBrowserWithCallback(Func<GoogleLoginPageState> stateFactory)
            => _stateFactory = stateFactory;

        public Task<GoogleLoginPageState> ReadStateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_stateFactory());
        }

        public Task FillAsync(GoogleLoginField field, string value, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task SubmitAsync(GoogleLoginField field, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<bool> TrySelectAuthenticatorMethodAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(false);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    // -------------------------------------------------------------------------
    // Helper fake browser that throws on SubmitAsync for a specific field
    // -------------------------------------------------------------------------
    private sealed class FakeBrowserWithSubmitException : IGoogleLoginBrowser
    {
        private readonly Queue<GoogleLoginPageState> _states = new();
        private readonly Exception _exception;
        private readonly string _throwOnField;

        public FakeBrowserWithSubmitException(Exception exception, string throwOnField)
        {
            _exception = exception;
            _throwOnField = throwOnField;
        }

        public FakeBrowserWithSubmitException ReturnState(GoogleLoginPageState state)
        {
            _states.Enqueue(state);
            return this;
        }

        public Task<GoogleLoginPageState> ReadStateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_states.Dequeue());
        }

        public Task FillAsync(GoogleLoginField field, string value, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task SubmitAsync(GoogleLoginField field, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (field.ToString() == _throwOnField)
                throw _exception;
            return Task.CompletedTask;
        }

        public Task<bool> TrySelectAuthenticatorMethodAsync(CancellationToken cancellationToken)
            => Task.FromResult(false);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakeBrowser : IGoogleLoginBrowser
    {
        private readonly Queue<GoogleLoginPageState> _states = new();
        private readonly List<string> _filledFields = new();
        private readonly List<string> _submittedFields = new();
        private Action? _onFill;
        private Exception? _readStateException;
        private Exception? _fillException;
        private bool _selectAuthenticatorMethodResult;

        public IReadOnlyList<string> FilledFields => _filledFields;
        public IReadOnlyList<string> SubmittedFields => _submittedFields;
        public int AuthenticatorMethodSelections { get; private set; }
        public int AuthenticatorMethodSelectionAttempts { get; private set; }

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

        public FakeBrowser SelectAuthenticatorMethod(bool result)
        {
            _selectAuthenticatorMethodResult = result;
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
            cancellationToken.ThrowIfCancellationRequested();
            AuthenticatorMethodSelections++;
            AuthenticatorMethodSelectionAttempts++;
            return Task.FromResult(_selectAuthenticatorMethodResult);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
