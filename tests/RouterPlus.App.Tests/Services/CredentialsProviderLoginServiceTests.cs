using RouterPlus.Core.Chrome;
using RouterPlus.Core.Models;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Chrome;
using RouterPlus.Infrastructure.Services;
using RouterPlus.App.Services;

namespace RouterPlus.App.Tests.Services;

public sealed class CredentialsProviderLoginServiceTests
{
    private static ChromeProfile Profile => new(
        ChromeProfile.CreateId(Path.GetTempPath(), "Default"),
        "Test Profile",
        "Default",
        Path.GetTempPath(),
        true);

    private static ProviderCredential Credential => new()
    {
        Email = "user@example.test",
        Password = "synthetic-password",
        TotpSecret = "synthetic-totp"
    };

    [Fact]
    public async Task LoginAsync_returns_success_when_automation_succeeds()
    {
        var session = new FakeSession();
        var factory = new FakeFactory((_, _, _, _) =>
            new FakeAutomation(new DirectLoginResult(true, "done")));
        var service = new CredentialsProviderLoginService(
            (_, _, _) => Task.FromResult<IProviderLoginChromeSession>(session),
            factory);

        var result = await service.LoginAsync(ProviderKind.GitHub, Profile, Credential, null, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("Login completed successfully.", result.Message);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task LoginAsync_returns_failure_for_unsupported_provider()
    {
        var session = new FakeSession();
        var factory = new FakeFactory((_, _, _, _) =>
            throw new NotSupportedException("Direct login not supported for provider X"));
        var service = new CredentialsProviderLoginService(
            (_, _, _) => Task.FromResult<IProviderLoginChromeSession>(session),
            factory);

        var result = await service.LoginAsync(ProviderKind.GitHub, Profile, Credential, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("not supported", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task LoginAsync_returns_failure_when_automation_throws()
    {
        var session = new FakeSession();
        var factory = new FakeFactory((_, _, _, _) =>
            new ThrowingAutomation(new InvalidOperationException("boom")));
        var service = new CredentialsProviderLoginService(
            (_, _, _) => Task.FromResult<IProviderLoginChromeSession>(session),
            factory);

        var result = await service.LoginAsync(ProviderKind.GitHub, Profile, Credential, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("boom", result.Message);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task LoginAsync_returns_failure_when_ConnectAnyTarget_throws()
    {
        var session = new FakeSession { ThrowOnConnect = new InvalidOperationException("connect boom") };
        var factory = new FakeFactory((_, _, _, _) =>
            new FakeAutomation(new DirectLoginResult(true, "done")));
        var service = new CredentialsProviderLoginService(
            (_, _, _) => Task.FromResult<IProviderLoginChromeSession>(session),
            factory);

        var result = await service.LoginAsync(ProviderKind.GitHub, Profile, Credential, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("connect boom", result.Message);
        Assert.True(session.Disposed);
    }

    [Fact]
    public async Task LoginAsync_returns_failure_when_launch_returns_null()
    {
        var factory = new FakeFactory((_, _, _, _) =>
            new FakeAutomation(new DirectLoginResult(true, "done")));
        var service = new CredentialsProviderLoginService(
            (_, _, _) => Task.FromResult<IProviderLoginChromeSession>(null!),
            factory);

        var result = await service.LoginAsync(ProviderKind.GitHub, Profile, Credential, null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("launch browser", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LoginAsync_cancels_and_disposes_session()
    {
        var session = new FakeSession();
        var factory = new FakeFactory((_, _, _, _) =>
            new ThrowingAutomation(new OperationCanceledException()));
        var service = new CredentialsProviderLoginService(
            (_, _, _) => Task.FromResult<IProviderLoginChromeSession>(session),
            factory);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var result = await service.LoginAsync(ProviderKind.GitHub, Profile, Credential, null, cts.Token);

        Assert.False(result.Success);
        Assert.Contains("cancelled", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(session.Disposed);
    }

    private sealed class FakeFactory(Func<ProviderKind, CdpSession, ProviderCredential, Func<Task<string?>>?, IDirectLoginAutomation> handler) : IDirectLoginAutomationFactory
    {
        public IDirectLoginAutomation Create(ProviderKind provider, CdpSession cdpSession, ProviderCredential credential, Func<Task<string?>>? totpGenerator) =>
            handler(provider, cdpSession, credential, totpGenerator);
    }

    private sealed class FakeAutomation(DirectLoginResult result) : IDirectLoginAutomation
    {
        public Task<DirectLoginResult> RunAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    private sealed class ThrowingAutomation(Exception exception) : IDirectLoginAutomation
    {
        public Task<DirectLoginResult> RunAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.FromException<DirectLoginResult>(exception);
    }

    private sealed class FakeSession : IProviderLoginChromeSession
    {
        public bool Disposed { get; private set; }
        public Exception? ThrowOnConnect { get; set; }
        public CdpSession Synthetic { get; } = new CdpSession(new ChromeCdpClient(new Uri("http://127.0.0.1:9222")), "s", "t");

        public Task<CdpSession> ConnectAnyTargetAsync(CancellationToken cancellationToken)
        {
            if (ThrowOnConnect is not null)
            {
                throw ThrowOnConnect;
            }
            return Task.FromResult(Synthetic);
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}