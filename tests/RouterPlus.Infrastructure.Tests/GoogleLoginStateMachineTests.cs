using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Chrome;

namespace RouterPlus.Infrastructure.Tests;

public sealed class GoogleLoginStateMachineTests
{
    [Fact]
    public async Task RunAsync_password_picker_selects_authenticator_and_completes_totp()
    {
        // Arrange
        var browser = new FakeGoogleLoginBrowser()
            .ReturnState(State("/signin", email: true))
            .ReturnState(State("/signin/password", password: true))
            .ReturnState(State("/signin/challenge", methodPicker: true))
            .ReturnState(State("/signin/challenge/totp", totp: true))
            .ReturnState(State("/", completion: true));

        // Act
        var result = await GoogleLoginStateMachine.RunAsync(browser, Credential(), CancellationToken.None);

        // Assert
        Assert.Equal(GoogleLoginResultCategory.Success, result.Category);
        Assert.Equal(1, browser.AuthenticatorSelectionCount);
        Assert.Equal(
            new[] { GoogleLoginField.Email, GoogleLoginField.Password, GoogleLoginField.Totp },
            browser.FilledFields);
        Assert.Equal(
            new[] { GoogleLoginField.Email, GoogleLoginField.Password, GoogleLoginField.Totp },
            browser.SubmittedFields);
    }

    [Fact]
    public async Task RunAsync_password_picker_returns_unsupported_page_when_authenticator_selection_fails()
    {
        // Arrange
        var browser = new FakeGoogleLoginBrowser
        {
            AuthenticatorSelectionResult = false
        }
        .ReturnState(State("/signin", email: true))
        .ReturnState(State("/signin/password", password: true))
        .ReturnState(State("/signin/challenge", methodPicker: true));

        // Act
        var result = await GoogleLoginStateMachine.RunAsync(browser, Credential(), CancellationToken.None);

        // Assert
        Assert.Equal(GoogleLoginResultCategory.UnsupportedPage, result.Category);
        Assert.Contains("Could not select Authenticator method", result.Message);
        Assert.Equal(1, browser.AuthenticatorSelectionCount);
        Assert.Equal(new[] { GoogleLoginField.Email, GoogleLoginField.Password }, browser.FilledFields);
    }

    [Fact]
    public async Task RunAsync_method_picker_at_entry_selects_authenticator_and_completes_totp()
    {
        // Arrange
        var browser = new FakeGoogleLoginBrowser()
            .ReturnState(State("/signin/challenge", methodPicker: true))
            .ReturnState(State("/signin/challenge/totp", totp: true))
            .ReturnState(State("/", completion: true));

        // Act
        var result = await GoogleLoginStateMachine.RunAsync(browser, Credential(), CancellationToken.None);

        // Assert
        Assert.Equal(GoogleLoginResultCategory.Success, result.Category);
        Assert.Equal(1, browser.AuthenticatorSelectionCount);
        Assert.Equal(new[] { GoogleLoginField.Totp }, browser.FilledFields);
        Assert.Equal(new[] { GoogleLoginField.Totp }, browser.SubmittedFields);
    }

    [Fact]
    public async Task RunAsync_method_picker_at_entry_returns_unsupported_page_when_selection_fails()
    {
        // Arrange
        var browser = new FakeGoogleLoginBrowser
        {
            AuthenticatorSelectionResult = false
        }
        .ReturnState(State("/signin/challenge", methodPicker: true));

        // Act
        var result = await GoogleLoginStateMachine.RunAsync(browser, Credential(), CancellationToken.None);

        // Assert
        Assert.Equal(GoogleLoginResultCategory.UnsupportedPage, result.Category);
        Assert.Contains("Could not select Authenticator method", result.Message);
        Assert.Equal(1, browser.AuthenticatorSelectionCount);
        Assert.Empty(browser.FilledFields);
    }

    [Fact]
    public async Task RunAsync_returns_invalid_credentials_when_totp_is_rejected()
    {
        // Arrange
        var browser = new FakeGoogleLoginBrowser()
            .ReturnState(State("/signin/challenge/totp", totp: true))
            .ReturnState(State("/signin/challenge/totp", totpError: true));

        // Act
        var result = await GoogleLoginStateMachine.RunAsync(browser, Credential(), CancellationToken.None);

        // Assert
        Assert.Equal(GoogleLoginResultCategory.InvalidCredentials, result.Category);
        Assert.Equal(new[] { GoogleLoginField.Totp }, browser.FilledFields);
        Assert.Equal(new[] { GoogleLoginField.Totp }, browser.SubmittedFields);
    }

    [Fact]
    public async Task RunAsync_returns_manual_intervention_required_for_challenge_after_totp()
    {
        // Arrange
        var browser = new FakeGoogleLoginBrowser()
            .ReturnState(State("/signin/challenge", methodPicker: true))
            .ReturnState(State("/signin/challenge/totp", totp: true))
            .ReturnState(State("/signin/challenge", manualChallenge: true));

        // Act
        var result = await GoogleLoginStateMachine.RunAsync(browser, Credential(), CancellationToken.None);

        // Assert
        Assert.Equal(GoogleLoginResultCategory.ManualInterventionRequired, result.Category);
        Assert.Contains("Manual challenge detected after TOTP submission", result.Message);
        Assert.Equal(new[] { GoogleLoginField.Totp }, browser.FilledFields);
    }

    [Fact]
    public async Task RunAsync_returns_invalid_credentials_when_google_rejects_a_field()
    {
        // Arrange
        var browser = new FakeGoogleLoginBrowser
        {
            FillException = new InvalidOperationException("Google rejected the credentials")
        }
        .ReturnState(State("/signin", email: true));

        // Act
        var result = await GoogleLoginStateMachine.RunAsync(browser, Credential(), CancellationToken.None);

        // Assert
        Assert.Equal(GoogleLoginResultCategory.InvalidCredentials, result.Category);
        Assert.Empty(browser.SubmittedFields);
    }

    private static GoogleLoginCredential Credential()
        => new("profile-1", "synthetic@example.com", "synthetic-password", "JBSWY3DPEHPK3PXP");

    private static GoogleLoginPageState State(
        string path,
        bool email = false,
        bool password = false,
        bool totp = false,
        bool totpError = false,
        bool methodPicker = false,
        bool completion = false,
        bool manualChallenge = false)
        => new(
            new Uri($"https://accounts.google.com{path}"),
            HasEmailField: email,
            HasPasswordField: password,
            HasTotpField: totp,
            HasTotpError: totpError,
            Has2FAMethodPicker: methodPicker,
            HasCompletionSignal: completion,
            HasManualChallenge: manualChallenge);

    private sealed class FakeGoogleLoginBrowser : IGoogleLoginBrowser
    {
        private readonly Queue<GoogleLoginPageState> _states = new();

        public List<GoogleLoginField> FilledFields { get; } = new();
        public List<GoogleLoginField> SubmittedFields { get; } = new();
        public int AuthenticatorSelectionCount { get; private set; }
        public bool AuthenticatorSelectionResult { get; init; } = true;
        public Exception? FillException { get; init; }

        public FakeGoogleLoginBrowser ReturnState(GoogleLoginPageState state)
        {
            _states.Enqueue(state);
            return this;
        }

        public Task<GoogleLoginPageState> ReadStateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_states.Dequeue());
        }

        public Task FillAsync(
            GoogleLoginField field,
            string value,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FillException is not null)
            {
                throw FillException;
            }

            FilledFields.Add(field);
            return Task.CompletedTask;
        }

        public Task SubmitAsync(GoogleLoginField field, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SubmittedFields.Add(field);
            return Task.CompletedTask;
        }

        public Task<bool> TrySelectAuthenticatorMethodAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AuthenticatorSelectionCount++;
            return Task.FromResult(AuthenticatorSelectionResult);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
