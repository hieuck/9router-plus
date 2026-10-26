using Moq;
using RouterPlus.App.Services;
using RouterPlus.Core.Chrome;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Router;

namespace RouterPlus.App.Tests.Services;

public sealed class ProviderWorkflowServiceTests
{
    private static ChromeProfile MakeProfile(string name = "Work") =>
        new("profile-id", name, "Default", @"C:\ud", true);

    private static ProviderConnection MakeConnection(string id, string name) =>
        new(id, ProviderKind.Codex, name, 1, true, CreatedAt: DateTimeOffset.UtcNow);

    [Fact]
    public void Cancel_LeavesInProgressUntilWorkflowCompletes()
    {
        var service = new ProviderWorkflowService();
        service.IsInProgress = true;
        service.WorkflowCancellation = new CancellationTokenSource();

        service.Cancel();

        Assert.True(service.IsInProgress);
        Assert.True(service.WorkflowCancellation!.IsCancellationRequested);
    }

    [Fact]
    public async Task CaptureAndRename_Success_SetsStatusAndRefreshes()
    {
        var profile = MakeProfile();
        var connection = MakeConnection("c-new", profile.Name);
        var mock = new Mock<IRouterApiClient>();
        mock.Setup(m => m.ListConnectionsAsync(
                ProviderKind.Codex,
                It.IsAny<CancellationToken>(),
                false))
            .ReturnsAsync(Array.Empty<ProviderConnection>());
        mock.Setup(m => m.WaitForNewConnectionAsync(
                ProviderKind.Codex,
                It.IsAny<IReadOnlyDictionary<string, ProviderConnection>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);
        mock.Setup(m => m.UpdateConnectionAsync(
                connection.Id,
                profile.Name,
                null,
                null,
                It.IsAny<CancellationToken>(),
                null))
            .Returns(Task.CompletedTask);

        var statuses = new List<string>();
        var refreshCount = 0;
        var service = new ProviderWorkflowService(
            selectedProfileGetter: () => profile,
            statusCallback: s => statuses.Add(s),
            refreshConnections: (s, f, ct) =>
            {
                refreshCount++;
                return Task.CompletedTask;
            });

        await service.CaptureExistingConnectionsAsync(mock.Object, ProviderKind.Codex, CancellationToken.None);
        await service.RenameNewConnectionAsync(mock.Object, ProviderKind.Codex, CancellationToken.None);

        Assert.Contains(statuses, s => s.Contains("Đã kết nối"));
        Assert.Equal(1, refreshCount);
        Assert.Null(service.CurrentProvider);
        Assert.True(service.WorkflowExistingConnections.ContainsKey(connection.Id));
    }

    [Fact]
    public async Task RenameWithoutCapture_ThrowsAndLeavesStateConsistent()
    {
        var profile = MakeProfile();
        var mock = new Mock<IRouterApiClient>();
        var service = new ProviderWorkflowService();
        service.IsInProgress = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RenameNewConnectionAsync(mock.Object, ProviderKind.Codex, CancellationToken.None));

        Assert.True(service.IsInProgress);
    }

    [Fact]
    public async Task ConnectOpenRouterOAuth_Success_InvokesApiKeySavedCallback()
    {
        var profile = MakeProfile();
        var connection = MakeConnection("or-1", profile.Name);
        var mockApi = new Mock<IRouterApiClient>();
        var apiKeyServiceMock = new Mock<IProviderApiKeyService>();
        apiKeyServiceMock.Setup(s => s.AddOrUpdateAsync(
                ProviderKind.OpenRouter,
                "test-key",
                profile,
                It.IsAny<Func<Task<IRouterApiClient>>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AddApiKeyResult(true, connection, new ProviderConnectionTestResult(true, null)));

        var savedKeys = new List<(ProviderKind, string)>();
        var service = new ProviderWorkflowService(
            selectedProfileGetter: () => profile,
            createApiClient: () => Task.FromResult(mockApi.Object),
            openRouterPkceFlow: _ => Task.FromResult(new OpenRouterPkceResult(true, "test-key", null)),
            apiKeyService: apiKeyServiceMock.Object,
            onApiKeySaved: (kind, key) => savedKeys.Add((kind, key)));

        var result = await service.ConnectOpenRouterOAuthAsync();

        Assert.True(result);
        Assert.Single(savedKeys);
        Assert.Equal(ProviderKind.OpenRouter, savedKeys[0].Item1);
        Assert.Equal("test-key", savedKeys[0].Item2);
    }

    [Fact]
    public async Task ThrowingStatusCallback_DoesNotBreakRenameWorkflow()
    {
        var profile = MakeProfile();
        var connection = MakeConnection("c-new", profile.Name);
        var mock = new Mock<IRouterApiClient>();
        mock.Setup(m => m.ListConnectionsAsync(
                ProviderKind.Codex,
                It.IsAny<CancellationToken>(),
                false))
            .ReturnsAsync(Array.Empty<ProviderConnection>());
        mock.Setup(m => m.WaitForNewConnectionAsync(
                ProviderKind.Codex,
                It.IsAny<IReadOnlyDictionary<string, ProviderConnection>>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);
        mock.Setup(m => m.UpdateConnectionAsync(
                connection.Id,
                profile.Name,
                null,
                null,
                It.IsAny<CancellationToken>(),
                null))
            .Returns(Task.CompletedTask);

        var service = new ProviderWorkflowService(
            selectedProfileGetter: () => profile,
            statusCallback: _ => throw new InvalidOperationException("boom"),
            stateChanged: () => throw new InvalidOperationException("boom"));

        await service.CaptureExistingConnectionsAsync(mock.Object, ProviderKind.Codex, CancellationToken.None);
        await service.RenameNewConnectionAsync(mock.Object, ProviderKind.Codex, CancellationToken.None);

        Assert.True(service.WorkflowExistingConnections.ContainsKey(connection.Id));
        Assert.Null(service.CurrentProvider);
    }

    [Fact]
    public async Task PollDeviceCode_TerminalError_ThrowsAndStops()
    {
        var profile = MakeProfile();
        var mock = new Mock<IRouterApiClient>();
        mock.Setup(m => m.PollDeviceCodeAsync(
                ProviderKind.Codex,
                It.IsAny<DeviceCodeSession>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeviceCodePollResult(false, "access_denied", "User denied authorization"));

        var service = new ProviderWorkflowService(
            selectedProfileGetter: () => profile);

        var session = new DeviceCodeSession("dev", "code", "https://example.com", null, 60, 1, null, null, null, null, null, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.PollDeviceCodeUntilSuccessAsync(mock.Object, ProviderKind.Codex, session, CancellationToken.None));
    }
}