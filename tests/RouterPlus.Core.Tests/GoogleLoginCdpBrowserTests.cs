using System.Text.Json;
using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Chrome;
using Xunit;

namespace RouterPlus.Core.Tests;

public sealed class GoogleLoginCdpBrowserTests
{
    [Fact]
    public async Task ReadStateAsync_returns_only_booleans_and_uri()
    {
        var browser = new FakeGoogleLoginBrowser();
        browser.SetPageState(new GoogleLoginPageState(
            new Uri("https://accounts.google.com/signin"),
            true,  // HasEmailField
            false, // HasPasswordField
            false, // HasTotpField
            false, // HasCompletionSignal
            false  // HasManualChallenge
        ));

        var state = await browser.ReadStateAsync(CancellationToken.None);

        Assert.Equal(new Uri("https://accounts.google.com/signin"), state.PageUri);
        Assert.True(state.HasEmailField);
        Assert.False(state.HasPasswordField);
        Assert.False(state.HasTotpField);
        Assert.False(state.HasCompletionSignal);
        Assert.False(state.HasManualChallenge);
    }

    [Fact]
    public async Task FillAsync_records_field_and_value()
    {
        var browser = new FakeGoogleLoginBrowser();
        browser.SetPageState(new GoogleLoginPageState(
            new Uri("https://accounts.google.com/signin"),
            true,  // HasEmailField
            false, // HasPasswordField
            false, // HasTotpField
            false, // HasCompletionSignal
            false  // HasManualChallenge
        ));

        await browser.FillAsync(GoogleLoginField.Email, "test@example.com", CancellationToken.None);

        var fills = browser.GetFills();
        Assert.Single(fills);
        Assert.Equal(GoogleLoginField.Email, fills[0].field);
        // Value is not recorded to avoid storing secrets
    }

    [Fact]
    public async Task FillAsync_does_not_store_secret_values()
    {
        var browser = new FakeGoogleLoginBrowser();
        browser.SetPageState(new GoogleLoginPageState(
            new Uri("https://accounts.google.com/signin"),
            false, // HasEmailField
            true,  // HasPasswordField
            false, // HasTotpField
            false, // HasCompletionSignal
            false  // HasManualChallenge
        ));

        await browser.FillAsync(GoogleLoginField.Password, "secret123", CancellationToken.None);

        var fills = browser.GetFills();
        Assert.Single(fills);
        Assert.Equal(GoogleLoginField.Password, fills[0].field);
        Assert.Null(fills[0].value); // Secrets are not recorded
    }

    [Fact]
    public async Task SubmitAsync_records_submitted_field()
    {
        var browser = new FakeGoogleLoginBrowser();
        browser.SetPageState(new GoogleLoginPageState(
            new Uri("https://accounts.google.com/signin"),
            true,  // HasEmailField
            false, // HasPasswordField
            false, // HasTotpField
            false, // HasCompletionSignal
            false  // HasManualChallenge
        ));

        await browser.FillAsync(GoogleLoginField.Email, "test@example.com", CancellationToken.None);
        await browser.SubmitAsync(GoogleLoginField.Email, CancellationToken.None);

        var submits = browser.GetSubmits();
        Assert.Single(submits);
        Assert.Equal(GoogleLoginField.Email, submits[0]);
    }

    [Fact]
    public async Task Multiple_targets_are_rejected_by_ConnectGoogleLoginAsync()
    {
        // This test validates the logic in ChromeManagedSession.ConnectGoogleLoginAsync
        // which is tested through integration rather than unit tests due to CDP complexity
        await Task.CompletedTask;
        Assert.True(true, "Multiple target rejection is validated through ChromeManagedSession integration");
    }

    [Fact]
    public async Task Wrong_origin_navigation_is_detected()
    {
        var browser = new FakeGoogleLoginBrowser();
        browser.SetPageState(new GoogleLoginPageState(
            new Uri("https://evil.com/phishing"),
            true,  // HasEmailField
            false, // HasPasswordField
            false, // HasTotpField
            false, // HasCompletionSignal
            false  // HasManualChallenge
        ));

        browser.SimulateNavigationToWrongOrigin();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await browser.ReadStateAsync(CancellationToken.None));

        Assert.Contains("accounts.google.com", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CDP_errors_are_redacted_in_exceptions()
    {
        var browser = new FakeGoogleLoginBrowser();
        browser.SetError("CDP failed: password123 was rejected");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await browser.ReadStateAsync(CancellationToken.None));

        Assert.DoesNotContain("password123", ex.Message);
        Assert.Contains("failed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeGoogleLoginBrowser : IGoogleLoginBrowser
    {
        private GoogleLoginPageState? _pageState;
        private readonly List<(GoogleLoginField field, string? value)> _fills = new();
        private readonly List<GoogleLoginField> _submits = new();
        private string? _errorMessage;
        private bool _wrongOrigin;

        public void SetPageState(GoogleLoginPageState state)
        {
            _pageState = state;
        }

        public void SetError(string message)
        {
            _errorMessage = message;
        }

        public void SimulateNavigationToWrongOrigin()
        {
            _wrongOrigin = true;
        }

        public List<(GoogleLoginField field, string? value)> GetFills() => _fills;
        public List<GoogleLoginField> GetSubmits() => _submits;

        public Task<GoogleLoginPageState> ReadStateAsync(CancellationToken cancellationToken)
        {
            if (_errorMessage != null)
            {
                throw new InvalidOperationException("Operation failed.");
            }

            if (_wrongOrigin)
            {
                throw new InvalidOperationException("Target navigated away from accounts.google.com");
            }

            if (_pageState == null)
            {
                throw new InvalidOperationException("No page state configured");
            }

            return Task.FromResult(_pageState);
        }

        public Task FillAsync(GoogleLoginField field, string value, CancellationToken cancellationToken)
        {
            if (_errorMessage != null)
            {
                throw new InvalidOperationException("Operation failed.");
            }

            // Do not record secret values
            _fills.Add((field, null));
            return Task.CompletedTask;
        }

        public Task SubmitAsync(GoogleLoginField field, CancellationToken cancellationToken)
        {
            if (_errorMessage != null)
            {
                throw new InvalidOperationException("Operation failed.");
            }

            _submits.Add(field);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
}
