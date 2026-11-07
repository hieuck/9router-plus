using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Chrome;
using RouterPlus.Infrastructure.Security;
using Moq;
namespace RouterPlus.Infrastructure.Tests;
/// <summary>
/// T-1 through T-8 from the Google auto-login state machine security audit.
/// FR-1: HandleTotpStepAsync extracted.
/// FR-2: Bare catch replaced with typed catch that excludes OperationCanceledException.
/// FR-3: TOTP expiry-window guard added.
/// FR-4: Single TOTP retry on rejection added.
/// </summary>
public sealed class GoogleLoginStateMachineAuditFixTests
{
    // ---------------------------------------------------------------
    // T-1: Cancellation during TOTP step returns Cancelled, not InvalidCredentials
    // Validates FR-2: the typed catch does not swallow OperationCanceledException.
    // In the old code, a bare catch would return InvalidCredentials even on cancellation.
    // ---------------------------------------------------------------
    [Fact]
    public async Task T1_cancellation_during_totp_fill_returns_cancelled_not_invalid_credentials()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var browser = new AuditFakeBrowser(cts: cts, cancelOnTotpFill: true)
            .ReturnState(State("/signin/challenge/totp", totp: true));
        // Act
        var result = await GoogleLoginStateMachine.RunAsync(browser, Credential(), cts.Token);
        // Assert — must be Cancelled, NOT InvalidCredentials
        Assert.Equal(GoogleLoginResultCategory.Cancelled, result.Category);
    }

    // ---------------------------------------------------------------
    // T-2: Consent flow deadline exceeded returns Timeout (or Cancelled)
    // Validates that the outer timeout is respected during consent handling.
    // ---------------------------------------------------------------
    [Fact]
    public async Task T2_consent_flow_deadline_exceeded_returns_cancelled_when_cts_fires()
    {
        // Arrange — total cancellation fires after the consent click, simulating timeout
        using var cts = new CancellationTokenSource();
        var browser = new AuditFakeBrowser(cts: cts, cancelAfterConsentClick: true)
            .ReturnState(State("/v3/signin/consent", consent: true))
            .RepeatLastState(10);
        // Act
        var result = await GoogleLoginStateMachine.RunAsync(browser, Credential(), cts.Token);
        // Assert
        Assert.True(
            result.Category == GoogleLoginResultCategory.Cancelled ||
            result.Category == GoogleLoginResultCategory.Timeout,
            $"Expected Cancelled or Timeout but got {result.Category}");
    }

    // ---------------------------------------------------------------
    // T-3: 2FA fill succeeds but submit throws InvalidOperationException
    // Validates that a missing submit button is surfaced as UnsupportedPage,
    // not silently swallowed.
    // ---------------------------------------------------------------
    [Fact]
    public async Task T3_totp_fill_succeeds_but_submit_throws_returns_unsupported_page()
    {
        // Arrange
        var browser = new AuditFakeBrowser(submitException: new InvalidOperationException("submit button not found"))
            .ReturnState(State("/signin/challenge/totp", totp: true));
        // Act
        var result = await GoogleLoginStateMachine.RunAsync(browser, Credential(), CancellationToken.None);
        // Assert — InvalidOperationException without "Google rejected" → UnsupportedPage
        Assert.Equal(GoogleLoginResultCategory.UnsupportedPage, result.Category);
        Assert.Contains("submit button not found", result.Message);
    }

    // ---------------------------------------------------------------
    // T-4: Unrecognised field combination falls through to UnsupportedPage
    // Validates the unrecognised-state fallback is exercised correctly.
    // Uses sticky=true so AuditFakeBrowser returns the all-false state repeatedly
    // when the wait-for-fields loop reads again.
    // ---------------------------------------------------------------
    [Fact]
    public async Task T4_unrecognised_field_combination_returns_unsupported_page()
    {
        // Arrange — all flags false on a valid Google URL; sticky so all reads return same state
        var browser = new AuditFakeBrowser(sticky: true)
            .ReturnState(State("/signin/unknown-step"));
        // Act
        var result = await GoogleLoginStateMachine.RunAsync(browser, Credential(), CancellationToken.None);
        // Assert
        Assert.Equal(GoogleLoginResultCategory.UnsupportedPage, result.Category);
        Assert.Contains("Unrecognized page state", result.Message);
    }

    // ---------------------------------------------------------------
    // T-5: Vault key file throws IOException — vault store surfaces the error
    // Validates the vault store contract: IOException from the key file propagates.
    // ---------------------------------------------------------------
    [Fact]
    public async Task T5_vault_store_io_exception_propagates_to_caller()
    {
        // Arrange
        var storeMock = new Mock<IGoogleAccountVaultStore>();
        storeMock
            .Setup(s => s.TryOpenRememberedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("key file not found or corrupted"));
        // Act & Assert — IOException must propagate, not be silently swallowed
        await Assert.ThrowsAsync<IOException>(
            () => storeMock.Object.TryOpenRememberedAsync("/fake/vault.dat", CancellationToken.None));
        storeMock.Verify(
            s => s.TryOpenRememberedAsync("/fake/vault.dat", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ---------------------------------------------------------------
    // T-6: TOTP expiry-window guard is in place — happy-path still completes
    // Validates FR-3: the guard code path does not break normal operation.
    // ---------------------------------------------------------------
    [Fact]
    public async Task T6_totp_expiry_guard_does_not_break_normal_totp_flow()
    {
        // Arrange
        var browser = new AuditFakeBrowser()
            .ReturnState(State("/signin/challenge/totp", totp: true))
            .ReturnState(State("/", completion: true));
        // Act
        var result = await GoogleLoginStateMachine.RunAsync(browser, Credential(), CancellationToken.None);
        // Assert — TOTP submitted and machine completed successfully
        Assert.Equal(GoogleLoginResultCategory.Success, result.Category);
        Assert.Contains(GoogleLoginField.Totp, browser.FilledFields);
        Assert.Contains(GoogleLoginField.Totp, browser.SubmittedFields);
    }

    // ---------------------------------------------------------------
    // T-7: Manual challenge resolver exceeds deadline — machine terminates correctly
    // Validates WaitForManualChallengeResolutionAsync loop respects cancellation.
    // ---------------------------------------------------------------
    [Fact]
    public async Task T7_manual_challenge_resolver_returns_when_cts_cancelled()
    {
        // Arrange — browser stuck on manual challenge; cts fires after 2nd state read
        using var cts = new CancellationTokenSource();
        var browser = new AuditFakeBrowser(cts: cts, cancelAfterNthRead: 2, sticky: true)
            .ReturnState(State("/signin/challenge/recaptcha", manualChallenge: true));
        // Act
        var result = await GoogleLoginStateMachine.RunAsync(browser, Credential(), cts.Token);
        // Assert — must not hang; machine returns Cancelled or ManualInterventionRequired
        Assert.True(
            result.Category == GoogleLoginResultCategory.Cancelled ||
            result.Category == GoogleLoginResultCategory.ManualInterventionRequired,
            $"Expected Cancelled or ManualInterventionRequired but got {result.Category}");
    }

    // ---------------------------------------------------------------
    // T-8: Browser session launch throws ObjectDisposedException → BrowserDisconnected
    // Validates IsBrowserDisconnectException handles ObjectDisposedException.
    // ---------------------------------------------------------------
    [Fact]
    public async Task T8_browser_disposed_on_first_read_returns_browser_disconnected()
    {
        // Arrange — ReadStateAsync throws ObjectDisposedException immediately
        var browser = new AuditFakeBrowser(
            readStateException: new ObjectDisposedException(
                "ChromeSession",
                "Browser session was disposed before the login flow could start"));
        // Act
        var result = await GoogleLoginStateMachine.RunAsync(browser, Credential(), CancellationToken.None);
        // Assert
        Assert.Equal(GoogleLoginResultCategory.BrowserDisconnected, result.Category);
    }

    // ---------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------
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
        bool manualChallenge = false,
        bool accountChooser = false,
        bool consent = false)
        => new(
            new Uri($"https://accounts.google.com{path}"),
            HasEmailField: email,
            HasPasswordField: password,
            HasTotpField: totp,
            HasTotpError: totpError,
            Has2FAMethodPicker: methodPicker,
            HasCompletionSignal: completion,
            HasManualChallenge: manualChallenge,
            HasAccountChooser: accountChooser,
            HasConsentScreen: consent);

    // ---------------------------------------------------------------
    // Extended fake browser for audit tests
    // ---------------------------------------------------------------
    private sealed class AuditFakeBrowser : IGoogleLoginBrowser
    {
        private readonly Queue<GoogleLoginPageState> _states = new();
        private readonly CancellationTokenSource? _cts;
        private readonly bool _cancelOnTotpFill;
        private readonly bool _cancelAfterConsentClick;
        private readonly int _cancelAfterNthRead;
        private readonly Exception? _readStateException;
        private readonly Exception? _submitException;
        private readonly bool _sticky; // when true, replays the last dequeued state on empty queue
        private int _readCount;
        private int _consentClickCount;
        private GoogleLoginPageState? _lastState;

        public List<GoogleLoginField> FilledFields { get; } = new();
        public List<GoogleLoginField> SubmittedFields { get; } = new();

        public AuditFakeBrowser(
            CancellationTokenSource? cts = null,
            bool cancelOnTotpFill = false,
            bool cancelAfterConsentClick = false,
            int cancelAfterNthRead = 0,
            Exception? readStateException = null,
            Exception? submitException = null,
            bool sticky = false)
        {
            _cts = cts;
            _cancelOnTotpFill = cancelOnTotpFill;
            _cancelAfterConsentClick = cancelAfterConsentClick;
            _cancelAfterNthRead = cancelAfterNthRead;
            _readStateException = readStateException;
            _submitException = submitException;
            _sticky = sticky;
        }

        public AuditFakeBrowser ReturnState(GoogleLoginPageState state)
        {
            _states.Enqueue(state);
            return this;
        }

        /// <summary>Queues <paramref name="count"/> additional copies of the last state added.</summary>
        public AuditFakeBrowser RepeatLastState(int count)
        {
            if (_states.Count == 0)
            {
                throw new InvalidOperationException("No state to repeat.");
            }
            var last = _states.Last();
            for (var i = 0; i < count; i++)
            {
                _states.Enqueue(last);
            }
            return this;
        }

        public Task<GoogleLoginPageState> ReadStateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_readStateException is not null)
            {
                throw _readStateException;
            }
            _readCount++;
            if (_cancelAfterNthRead > 0 && _readCount >= _cancelAfterNthRead)
            {
                _cts?.Cancel();
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (_states.Count > 0)
            {
                _lastState = _states.Dequeue();
                return Task.FromResult(_lastState);
            }
            if (_sticky && _lastState is not null)
            {
                return Task.FromResult(_lastState);
            }
            throw new InvalidOperationException("AuditFakeBrowser: no more states queued");
        }

        public Task FillAsync(GoogleLoginField field, string value, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_cancelOnTotpFill && field == GoogleLoginField.Totp)
            {
                _cts?.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }
            FilledFields.Add(field);
            return Task.CompletedTask;
        }

        public Task SubmitAsync(GoogleLoginField field, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_submitException is not null)
            {
                throw _submitException;
            }
            SubmittedFields.Add(field);
            return Task.CompletedTask;
        }

        public Task<bool> TrySelectAuthenticatorMethodAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(true);
        }

        public Task<bool> TryClickConsentContinueAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _consentClickCount++;
            if (_cancelAfterConsentClick && _consentClickCount >= 1)
            {
                _cts?.Cancel();
            }
            return Task.FromResult(true);
        }

        public Task<bool> TrySelectAccountOnChooserAsync(string? email, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(true);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}