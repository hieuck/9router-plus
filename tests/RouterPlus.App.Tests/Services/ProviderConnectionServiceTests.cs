using Moq;
using RouterPlus.App.Services;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Router;

namespace RouterPlus.App.Tests.Services;

public sealed class ProviderConnectionServiceTests
{
    private static ProviderConnection MakeConnection(string id, bool active, bool exhausted)
    {
        var quotas = exhausted
            ? new[] { new ProviderQuota("session", 100m, 100m, 0m, DateTimeOffset.UtcNow.AddHours(1)) }
            : new[] { new ProviderQuota("session", 10m, 100m, 90m, DateTimeOffset.UtcNow.AddHours(1)) };

        return new ProviderConnection(
            id,
            ProviderKind.Codex,
            "Work",
            1,
            active,
            Quotas: quotas);
    }

    [Fact]
    public async Task NoExhausted_OneList_NoUpdates_EmptyResults()
    {
        var connection = MakeConnection("c1", true, false);
        var mock = new Mock<IRouterApiClient>();
        mock.Setup(m => m.ListAllConnectionsAsync(It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new[] { connection });

        var factoryCalls = 0;
        Func<Task<IRouterApiClient>> factory = () =>
        {
            factoryCalls++;
            return Task.FromResult(mock.Object);
        };

        var service = new ProviderConnectionService();
        var result = await service.RefreshConnectionsAsync(factory, CancellationToken.None);

        Assert.Single(result.Connections);
        Assert.Empty(result.DisabledConnectionIds);
        Assert.Empty(result.DisableFailures);
        mock.Verify(m => m.ListAllConnectionsAsync(It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Once);
        mock.Verify(m => m.UpdateConnectionAsync(
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>(), It.IsAny<bool?>()), Times.Never);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public async Task Exhausted_UpdateCalled_IsActiveFalse_RelistCalled_DisabledReturned()
    {
        var connection = MakeConnection("c1", true, true);
        var mock = new Mock<IRouterApiClient>();
        var listCalls = 0;
        mock.Setup(m => m.ListAllConnectionsAsync(It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(() =>
            {
                listCalls++;
                if (listCalls == 1)
                {
                    return new[] { connection };
                }

                return new[] { connection with { IsActive = false } };
            });
        mock.Setup(m => m.UpdateConnectionAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>(), It.IsAny<bool?>()))
            .Returns(Task.CompletedTask);

        var service = new ProviderConnectionService();
        var result = await service.RefreshConnectionsAsync(() => Task.FromResult(mock.Object), CancellationToken.None);

        Assert.Contains("c1", result.DisabledConnectionIds);
        Assert.Empty(result.DisableFailures);
        mock.Verify(m => m.UpdateConnectionAsync(
            "c1", null, null, null, It.IsAny<CancellationToken>(), false), Times.Once);
        Assert.Equal(2, listCalls);
    }

    [Fact]
    public async Task OneUpdateThrows_FailureRecorded_OthersContinue_ResultReturned()
    {
        var c1 = MakeConnection("c1", true, true);
        var c2 = MakeConnection("c2", true, true);
        var mock = new Mock<IRouterApiClient>();
        mock.Setup(m => m.ListAllConnectionsAsync(It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new[] { c1, c2 });
        mock.Setup(m => m.UpdateConnectionAsync(
                "c1", It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>(), It.IsAny<bool?>()))
            .ThrowsAsync(new InvalidOperationException("boom"));
        mock.Setup(m => m.UpdateConnectionAsync(
                "c2", It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>(), It.IsAny<bool?>()))
            .Returns(Task.CompletedTask);

        var service = new ProviderConnectionService();
        var result = await service.RefreshConnectionsAsync(() => Task.FromResult(mock.Object), CancellationToken.None);

        Assert.Contains("c2", result.DisabledConnectionIds);
        Assert.DoesNotContain("c1", result.DisabledConnectionIds);
        Assert.Single(result.DisableFailures);
        Assert.Equal("c1", result.DisableFailures[0].ConnectionId);
        mock.Verify(m => m.UpdateConnectionAsync(
            "c1", It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>(), It.IsAny<bool?>()), Times.Once);
        mock.Verify(m => m.UpdateConnectionAsync(
            "c2", It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>(), It.IsAny<bool?>()), Times.Once);
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        var connection = MakeConnection("c1", true, true);
        var mock = new Mock<IRouterApiClient>();
        mock.Setup(m => m.ListAllConnectionsAsync(It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new[] { connection });
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        mock.Setup(m => m.UpdateConnectionAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<string?>(),
                It.IsAny<CancellationToken>(), It.IsAny<bool?>()))
            .ThrowsAsync(new OperationCanceledException());

        var service = new ProviderConnectionService();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.RefreshConnectionsAsync(() => Task.FromResult(mock.Object), cts.Token));
    }

    [Fact]
    public async Task ApiFactory_CalledOnce()
    {
        var mock = new Mock<IRouterApiClient>();
        mock.Setup(m => m.ListAllConnectionsAsync(It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(Array.Empty<ProviderConnection>());

        var calls = 0;
        var service = new ProviderConnectionService();
        await service.RefreshConnectionsAsync(() =>
        {
            calls++;
            return Task.FromResult(mock.Object);
        }, CancellationToken.None);

        Assert.Equal(1, calls);
    }
}