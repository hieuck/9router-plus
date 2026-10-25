using Moq;
using RouterPlus.App.Services;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Router;

namespace RouterPlus.App.Tests.Services;

public sealed class ProviderConnectionRenameServiceTests
{
    private static ChromeProfile MakeProfile(string name = "WorkProfile") =>
        new("prof-1", name, "Default", @"C:\ud", false);

    private static ProviderConnection MakeConnection(string id, string? name, DateTimeOffset? createdAt = null, bool existing = false) =>
        new(id, ProviderKind.Codex, name, 1, true, CreatedAt: createdAt ?? DateTimeOffset.UtcNow);

    [Fact]
    public async Task WaitSuccess_UpdateCalled_FlagsFalse()
    {
        var profile = MakeProfile();
        var connection = MakeConnection("c-new", profile.Name);
        var existing = new Dictionary<string, ProviderConnection>(StringComparer.Ordinal);
        var mock = new Mock<IRouterApiClient>();
        mock.Setup(m => m.WaitForNewConnectionAsync(
                It.IsAny<ProviderKind>(),
                It.IsAny<IReadOnlyDictionary<string, ProviderConnection>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        var service = new ProviderConnectionRenameService();
        var result = await service.RenameAsync(
            mock.Object, ProviderKind.Codex, profile, existing, TimeSpan.FromMinutes(5), DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.Equal(connection, result.Connection);
        Assert.False(result.FoundAfterTimeout);
        Assert.False(result.ReusedExistingId);
        mock.Verify(m => m.UpdateConnectionAsync(
            connection.Id, profile.Name, null, null, It.IsAny<CancellationToken>(), null), Times.Once);
        mock.Verify(m => m.ListConnectionsAsync(
            It.IsAny<ProviderKind>(), It.IsAny<CancellationToken>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task Timeout_FallbackNewNameMatch_FoundAfterTimeoutTrue_Update_CorrectConnection()
    {
        var profile = MakeProfile();
        var existingConnection = MakeConnection("c-old", "Other");
        var existing = new Dictionary<string, ProviderConnection>(StringComparer.Ordinal)
        {
            [existingConnection.Id] = existingConnection
        };
        var newConnection = MakeConnection("c-new", profile.Name, DateTimeOffset.UtcNow.AddMinutes(1));
        var all = new[] { newConnection, MakeConnection("c-old", profile.Name, DateTimeOffset.UtcNow.AddMinutes(-1)) };
        // c-old is in existing, filtered out in first pass; second pass includes it but newConnection preferred by CreatedAt
        var mock = new Mock<IRouterApiClient>();
        mock.Setup(m => m.WaitForNewConnectionAsync(
                It.IsAny<ProviderKind>(),
                It.IsAny<IReadOnlyDictionary<string, ProviderConnection>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("timeout"));
        mock.Setup(m => m.ListConnectionsAsync(
                It.IsAny<ProviderKind>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .ReturnsAsync(all);

        var service = new ProviderConnectionRenameService();
        var result = await service.RenameAsync(
            mock.Object, ProviderKind.Codex, profile, existing, TimeSpan.FromMinutes(5), DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.True(result.FoundAfterTimeout);
        Assert.False(result.ReusedExistingId);
        Assert.Equal(newConnection.Id, result.Connection.Id);
        mock.Verify(m => m.UpdateConnectionAsync(
            newConnection.Id, profile.Name, null, null, It.IsAny<CancellationToken>(), null), Times.Once);
        mock.Verify(m => m.ListConnectionsAsync(ProviderKind.Codex, It.IsAny<CancellationToken>(), false), Times.Once);
    }

    [Fact]
    public async Task Timeout_OnlyReusedProfileNameMatch_ReusedExistingIdTrue()
    {
        var profile = MakeProfile();
        var existingConnection = MakeConnection("c-existing", profile.Name);
        var existing = new Dictionary<string, ProviderConnection>(StringComparer.Ordinal)
        {
            [existingConnection.Id] = existingConnection
        };
        // Only profile-named connection has an id already in existing (reused)
        var all = new[] { existingConnection };
        var mock = new Mock<IRouterApiClient>();
        mock.Setup(m => m.WaitForNewConnectionAsync(
                It.IsAny<ProviderKind>(),
                It.IsAny<IReadOnlyDictionary<string, ProviderConnection>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("timeout"));
        mock.Setup(m => m.ListConnectionsAsync(
                It.IsAny<ProviderKind>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .ReturnsAsync(all);

        var service = new ProviderConnectionRenameService();
        var result = await service.RenameAsync(
            mock.Object, ProviderKind.Codex, profile, existing, TimeSpan.FromMinutes(5), DateTimeOffset.UtcNow, CancellationToken.None);

        Assert.True(result.FoundAfterTimeout);
        Assert.True(result.ReusedExistingId);
        Assert.Equal(existingConnection.Id, result.Connection.Id);
    }

    [Fact]
    public async Task Timeout_PrefersNewAccountConnectionOverOldProfileNameMatch()
    {
        var profile = MakeProfile();
        var start = DateTimeOffset.UtcNow;
        var existingConnection = MakeConnection("c-old", profile.Name, start.AddMinutes(-5));
        var existing = new Dictionary<string, ProviderConnection>(StringComparer.Ordinal)
        {
            [existingConnection.Id] = existingConnection
        };
        var oldProfileNameConnection = MakeConnection("c-old2", profile.Name, start.AddMinutes(-1));
        var newAccountConnection = MakeConnection("c-new", "Account 19", start.AddMinutes(1));
        var all = new[] { oldProfileNameConnection, newAccountConnection };
        var mock = new Mock<IRouterApiClient>();
        mock.Setup(m => m.WaitForNewConnectionAsync(
                It.IsAny<ProviderKind>(),
                It.IsAny<IReadOnlyDictionary<string, ProviderConnection>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("timeout"));
        mock.Setup(m => m.ListConnectionsAsync(
                It.IsAny<ProviderKind>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .ReturnsAsync(all);

        var service = new ProviderConnectionRenameService();
        var result = await service.RenameAsync(
            mock.Object, ProviderKind.Codex, profile, existing, TimeSpan.FromMinutes(5), start, CancellationToken.None);

        Assert.True(result.FoundAfterTimeout);
        Assert.Equal(newAccountConnection.Id, result.Connection.Id);
        mock.Verify(m => m.UpdateConnectionAsync(
            newAccountConnection.Id, profile.Name, null, null, It.IsAny<CancellationToken>(), null), Times.Once);
    }

    [Fact]
    public async Task Timeout_NoMatch_ThrowsTimeoutException()
    {
        var profile = MakeProfile();
        var existing = new Dictionary<string, ProviderConnection>(StringComparer.Ordinal);
        var mock = new Mock<IRouterApiClient>();
        mock.Setup(m => m.WaitForNewConnectionAsync(
                It.IsAny<ProviderKind>(),
                It.IsAny<IReadOnlyDictionary<string, ProviderConnection>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("timeout"));
        mock.Setup(m => m.ListConnectionsAsync(
                It.IsAny<ProviderKind>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<bool>()))
            .ReturnsAsync(Array.Empty<ProviderConnection>());

        var service = new ProviderConnectionRenameService();
        await Assert.ThrowsAsync<TimeoutException>(() =>
            service.RenameAsync(mock.Object, ProviderKind.Codex, profile, existing, TimeSpan.FromMinutes(5), DateTimeOffset.UtcNow, CancellationToken.None));
    }

    [Fact]
    public async Task Cancellation_Propagates()
    {
        var profile = MakeProfile();
        var existing = new Dictionary<string, ProviderConnection>(StringComparer.Ordinal);
        var mock = new Mock<IRouterApiClient>();
        mock.Setup(m => m.WaitForNewConnectionAsync(
                It.IsAny<ProviderKind>(),
                It.IsAny<IReadOnlyDictionary<string, ProviderConnection>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var service = new ProviderConnectionRenameService();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.RenameAsync(mock.Object, ProviderKind.Codex, profile, existing, TimeSpan.FromMinutes(5), DateTimeOffset.UtcNow, CancellationToken.None));
    }

    [Fact]
    public async Task Update_UsesExactName()
    {
        var profile = MakeProfile("Exact Name");
        var connection = MakeConnection("c-new", "old");
        var existing = new Dictionary<string, ProviderConnection>(StringComparer.Ordinal);
        var mock = new Mock<IRouterApiClient>();
        mock.Setup(m => m.WaitForNewConnectionAsync(
                It.IsAny<ProviderKind>(),
                It.IsAny<IReadOnlyDictionary<string, ProviderConnection>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        var service = new ProviderConnectionRenameService();
        await service.RenameAsync(mock.Object, ProviderKind.Codex, profile, existing, TimeSpan.FromMinutes(5), DateTimeOffset.UtcNow, CancellationToken.None);

        mock.Verify(m => m.UpdateConnectionAsync(
            connection.Id, "Exact Name", null, null, It.IsAny<CancellationToken>(), null), Times.Once);
    }
}
