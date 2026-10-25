using Moq;
using RouterPlus.App.Services;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Models;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Security;

namespace RouterPlus.App.Tests.Services;

public sealed class ProviderConnectionRowServiceTests
{
    private static ChromeProfile MakeProfile(string name = "Test Profile") =>
        new("prof-1", name, "Default", @"C:\ud", true);

    private static ProviderAuthConnection MakeConnection(string profileName, ProviderKind kind, string email = "a@b.com") =>
        new()
        {
            ProfileName = profileName,
            Provider = kind,
            PreferredMethod = AuthMethod.Direct,
            DirectCredential = new ProviderCredential { Email = email, Password = "p" }
        };

    [Fact]
    public async Task LoadAsync_NullProfile_ThrowsArgumentNullException()
    {
        var store = new Mock<IProviderConnectionVaultStore>();
        var service = new ProviderConnectionRowService(store.Object);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            service.LoadAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task LoadAsync_EmptyProfile_ReturnsNullConnectionsInOrder()
    {
        var profile = MakeProfile();
        var store = new Mock<IProviderConnectionVaultStore>();
        store.Setup(s => s.GetConnectionAsync(profile.Name, It.IsAny<ProviderKind>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ProviderAuthConnection?)null);

        var service = new ProviderConnectionRowService(store.Object);
        var result = await service.LoadAsync(profile, CancellationToken.None);

        Assert.Equal(profile, result.Profile);
        Assert.Null(result.CodexConnection);
        Assert.Equal(3, result.ProviderConnections.Count);
        Assert.Equal(ProviderKind.Kiro, result.ProviderConnections[0].Kind);
        Assert.Equal(ProviderKind.GitHub, result.ProviderConnections[1].Kind);
        Assert.Equal(ProviderKind.OpenRouter, result.ProviderConnections[2].Kind);
        Assert.All(result.ProviderConnections, item => Assert.Null(item.Connection));
    }

    [Fact]
    public async Task LoadAsync_AllProviders_ReturnsConnections()
    {
        var profile = MakeProfile();
        var codex = MakeConnection(profile.Name, ProviderKind.Codex, "codex@x.com");
        var kiro = MakeConnection(profile.Name, ProviderKind.Kiro, "kiro@x.com");
        var github = MakeConnection(profile.Name, ProviderKind.GitHub, "gh@x.com");
        var openRouter = MakeConnection(profile.Name, ProviderKind.OpenRouter, "or@x.com");

        var store = new Mock<IProviderConnectionVaultStore>();
        store.Setup(s => s.GetConnectionAsync(profile.Name, ProviderKind.Codex, It.IsAny<CancellationToken>()))
            .ReturnsAsync(codex);
        store.Setup(s => s.GetConnectionAsync(profile.Name, ProviderKind.Kiro, It.IsAny<CancellationToken>()))
            .ReturnsAsync(kiro);
        store.Setup(s => s.GetConnectionAsync(profile.Name, ProviderKind.GitHub, It.IsAny<CancellationToken>()))
            .ReturnsAsync(github);
        store.Setup(s => s.GetConnectionAsync(profile.Name, ProviderKind.OpenRouter, It.IsAny<CancellationToken>()))
            .ReturnsAsync(openRouter);

        var service = new ProviderConnectionRowService(store.Object);
        var result = await service.LoadAsync(profile, CancellationToken.None);

        Assert.Same(codex, result.CodexConnection);
        Assert.Same(kiro, result.ProviderConnections[0].Connection);
        Assert.Same(github, result.ProviderConnections[1].Connection);
        Assert.Same(openRouter, result.ProviderConnections[2].Connection);
    }

    [Fact]
    public async Task LoadAsync_ExceptionPropagates()
    {
        var profile = MakeProfile();
        var store = new Mock<IProviderConnectionVaultStore>();
        store.Setup(s => s.GetConnectionAsync(profile.Name, ProviderKind.Codex, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var service = new ProviderConnectionRowService(store.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.LoadAsync(profile, CancellationToken.None));
    }

    [Fact]
    public async Task LoadAsync_CallsStoreInProviderOrder()
    {
        var profile = MakeProfile();
        var calls = new List<ProviderKind>();
        var store = new Mock<IProviderConnectionVaultStore>();
        store.Setup(s => s.GetConnectionAsync(profile.Name, It.IsAny<ProviderKind>(), It.IsAny<CancellationToken>()))
            .Callback<string, ProviderKind, CancellationToken>((_, kind, _) => calls.Add(kind))
            .ReturnsAsync((ProviderAuthConnection?)null);

        var service = new ProviderConnectionRowService(store.Object);
        await service.LoadAsync(profile, CancellationToken.None);

        Assert.Equal(
            new[] { ProviderKind.Codex, ProviderKind.Kiro, ProviderKind.GitHub, ProviderKind.OpenRouter },
            calls);
    }

    [Fact]
    public async Task LoadAsync_PassesCancellationTokenToStore()
    {
        var profile = MakeProfile();
        var cts = new CancellationTokenSource();
        CancellationToken captured = default;
        var store = new Mock<IProviderConnectionVaultStore>();
        store.Setup(s => s.GetConnectionAsync(profile.Name, It.IsAny<ProviderKind>(), It.IsAny<CancellationToken>()))
            .Callback<string, ProviderKind, CancellationToken>((_, _, token) => captured = token)
            .ReturnsAsync((ProviderAuthConnection?)null);

        var service = new ProviderConnectionRowService(store.Object);
        await service.LoadAsync(profile, cts.Token);

        Assert.Equal(cts.Token, captured);
    }

    [Fact]
    public async Task LoadAsync_PropagatesCancellation()
    {
        var profile = MakeProfile();
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var store = new Mock<IProviderConnectionVaultStore>();
        store.Setup(s => s.GetConnectionAsync(profile.Name, It.IsAny<ProviderKind>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var service = new ProviderConnectionRowService(store.Object);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.LoadAsync(profile, cts.Token));
    }

    [Fact]
    public async Task LoadAsync_DoesNotContinueLookupsAfterCancellation()
    {
        var profile = MakeProfile();
        var cts = new CancellationTokenSource();
        var calls = new List<ProviderKind>();
        var store = new Mock<IProviderConnectionVaultStore>();
        store.Setup(s => s.GetConnectionAsync(profile.Name, It.IsAny<ProviderKind>(), It.IsAny<CancellationToken>()))
            .Callback<string, ProviderKind, CancellationToken>((_, kind, _) => calls.Add(kind))
            .Returns<string, ProviderKind, CancellationToken>((_, kind, token) =>
            {
                if (kind == ProviderKind.Codex)
                {
                    cts.Cancel();
                }

                if (token.IsCancellationRequested)
                {
                    throw new OperationCanceledException(token);
                }

                return Task.FromResult<ProviderAuthConnection?>(null);
            });

        var service = new ProviderConnectionRowService(store.Object);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.LoadAsync(profile, cts.Token));

        Assert.Equal(new[] { ProviderKind.Codex }, calls);
    }
}