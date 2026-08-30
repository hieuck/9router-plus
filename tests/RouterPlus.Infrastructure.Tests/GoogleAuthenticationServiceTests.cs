using RouterPlus.Core.Security;
using RouterPlus.Infrastructure.Chrome;
using RouterPlus.Infrastructure.Services;

namespace RouterPlus.Infrastructure.Tests;

public sealed class GoogleAuthenticationServiceTests
{
    [Fact]
    public async Task AuthenticateAsync_AlreadyAuthenticatedBrowser_ReturnsSuccess()
    {
        var browser = new FakeGoogleLoginBrowser
        {
            State = new GoogleLoginPageState(
                new Uri("https://accounts.google.com/"),
                HasEmailField: false,
                HasPasswordField: false,
                HasTotpField: false,
                Has2FAMethodPicker: false,
                HasCompletionSignal: true,
                HasManualChallenge: false)
        };
        var credential = new GoogleLoginCredential(
            "profile-1", "user@example.com", "synthetic-password", "JBSWY3DPEHPK3PXP");

        var result = await new GoogleAuthenticationService().AuthenticateAsync(
            new GoogleAuthenticationRequest(credential, browser),
            CancellationToken.None);

        Assert.Equal(GoogleLoginResultCategory.Success, result.Category);
        Assert.Equal(1, browser.ReadCount);
    }

    [Fact]
    public async Task AuthenticateAsync_NullRequest_ThrowsArgumentNullException()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            new GoogleAuthenticationService().AuthenticateAsync(null!, CancellationToken.None));
    }

    private sealed class FakeGoogleLoginBrowser : IGoogleLoginBrowser
    {
        public GoogleLoginPageState State { get; set; } = null!;
        public int ReadCount { get; private set; }

        public Task<GoogleLoginPageState> ReadStateAsync(CancellationToken cancellationToken)
        {
            ReadCount++;
            return Task.FromResult(State);
        }

        public Task FillAsync(
            GoogleLoginField field,
            string value,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task SubmitAsync(
            GoogleLoginField field,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<bool> TrySelectAuthenticatorMethodAsync(
            CancellationToken cancellationToken) => Task.FromResult(true);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
