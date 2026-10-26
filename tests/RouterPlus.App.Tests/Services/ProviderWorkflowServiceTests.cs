using System.Threading;
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
        var lightweightRefreshCount = 0;
        var fullRefreshCount = 0;
        var service = new ProviderWorkflowService(
            selectedProfileGetter: () => profile,
            statusCallback: s => statuses.Add(s),
            refreshConnectionsLightweight: async ct =>
            {
                lightweightRefreshCount++;
                await Task.Delay(1, ct);
                return new[] { connection };
            },
            refreshConnections: (s, f, ct) =>
            {
                fullRefreshCount++;
                return Task.CompletedTask;
            });

        await service.CaptureExistingConnectionsAsync(mock.Object, ProviderKind.Codex, CancellationToken.None);
        await service.RenameNewConnectionAsync(mock.Object, ProviderKind.Codex, CancellationToken.None);

        Assert.Contains(statuses, s => s.Contains("Đã kết nối"));
        Assert.Equal(1, lightweightRefreshCount);
        Assert.Equal(0, fullRefreshCount);
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

    [Fact]
    public async Task RenameNewConnectionAsync_UsesLightweightRefresh_NotFullRefresh()
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

        bool lightweightRefreshCalled = false;
        var service = new ProviderWorkflowService(
            selectedProfileGetter: () => profile,
            refreshConnectionsLightweight: async ct =>
            {
                lightweightRefreshCalled = true;
                // Simulate fast lightweight refresh
                await Task.Delay(10, ct);
                return new[] { connection };
            },
            refreshConnections: (s, f, ct) =>
            {
                // This should NOT be called in the new flow
                throw new InvalidOperationException("Full refresh should not be called after rename");
            });

        await service.CaptureExistingConnectionsAsync(mock.Object, ProviderKind.Codex, CancellationToken.None);
        await service.RenameNewConnectionAsync(mock.Object, ProviderKind.Codex, CancellationToken.None);

        Assert.True(lightweightRefreshCalled);
        Assert.True(service.WorkflowExistingConnections.ContainsKey(connection.Id));
    }

    

    [Fact]
    public async Task RefreshConnectionStatusesAsync_StillUsesFullRefreshWithQuotas()
    {
        var connection = new ProviderConnection("c1", ProviderKind.Codex, "Work", 1, true);
        var mock = new Mock<IRouterApiClient>();
        bool capturedIncludeQuotas = false;
        mock.Setup(m => m.ListAllConnectionsAsync(It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .Callback<CancellationToken, bool>((_, includeQuotas) => capturedIncludeQuotas = includeQuotas)
            .ReturnsAsync(new[] { connection });

        var connectionService = new ProviderConnectionService();
        var factoryCalls = 0;
        Func<Task<IRouterApiClient>> factory = () =>
        {
            factoryCalls++;
            return Task.FromResult(mock.Object);
        };

        var result = await connectionService.RefreshConnectionsAsync(factory, CancellationToken.None);

        Assert.Single(result.Connections);
        Assert.True(capturedIncludeQuotas);
    }

    [Fact]
    public async Task RenameNewConnectionAsync_LightweightRefreshCompletesBeforeFullRefresh_WorkflowReturns()
    {
        // This test proves the user-facing behavior:
        // - Full refresh (quota fetch) would block on a TaskCompletionSource (simulating 10s quota fetch)
        // - Lightweight refresh completes immediately
        // - Workflow returns / IsInProgress becomes false while full refresh would still be blocked
        // - Only lightweight refresh ran (full refresh is NOT called)

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

        var fullRefreshTcs = new TaskCompletionSource<bool>();
        var fullRefreshStarted = false;
        var fullRefreshCompleted = false;
        var lightweightRefreshCompleted = false;

        var service = new ProviderWorkflowService(
            selectedProfileGetter: () => profile,
            refreshConnectionsLightweight: async ct =>
            {
                // Lightweight refresh completes immediately
                await Task.Delay(1, ct);
                lightweightRefreshCompleted = true;
                return new[] { connection };
            },
            refreshConnections: async (s, f, ct) =>
            {
                // Full refresh would block on TCS (simulating 10s quota fetch)
                // But this should NEVER be called in the rename workflow
                fullRefreshStarted = true;
                await fullRefreshTcs.Task.WaitAsync(ct);
                fullRefreshCompleted = true;
            });

        await service.CaptureExistingConnectionsAsync(mock.Object, ProviderKind.Codex, CancellationToken.None);

        // Start the rename workflow
        var renameTask = service.RenameNewConnectionAsync(mock.Object, ProviderKind.Codex, CancellationToken.None);

        // Wait for lightweight refresh to complete (with timeout)
        var waitTask = Task.Run(() =>
        {
            var sw = new SpinWait();
            while (!lightweightRefreshCompleted)
            {
                sw.SpinOnce();
            }
        });
        var completedTask = await Task.WhenAny(waitTask, Task.Delay(5000));
        var lightweightCompleted = completedTask == waitTask;

        Assert.True(lightweightCompleted, "Lightweight refresh did not complete in time");

        // Workflow should have returned (IsInProgress is false)
        Assert.False(service.IsInProgress, "IsInProgress should be false after lightweight refresh completes");
        Assert.True(lightweightRefreshCompleted, "Lightweight refresh should have run");
        Assert.False(fullRefreshCompleted, "Full refresh should NOT have completed");
        Assert.False(fullRefreshStarted, "Full refresh should NOT have been called at all");

        // Wait for rename to fully complete
        await renameTask;

        // Full refresh still should not have been called
        Assert.False(fullRefreshStarted, "Full refresh should never be called in rename workflow");
    }

    [Fact]
    public async Task WaitForConnectionAsync_UsesLightweightRefresh_NotFullRefresh()
    {
        // This test verifies WaitForConnectionAsync uses lightweight refresh (not full refresh)
        // by using the same pattern: full refresh would block, lightweight completes immediately

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

        // Use a TCS with a short timeout to ensure it never blocks indefinitely
        var fullRefreshTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fullRefreshStarted = false;
        var fullRefreshCompleted = false;
        var lightweightRefreshCompleted = false;

        var service = new ProviderWorkflowService(
            selectedProfileGetter: () => profile,
            createApiClient: () => Task.FromResult(mock.Object),
            refreshConnectionsLightweight: async ct =>
            {
                await Task.Delay(1, ct);
                lightweightRefreshCompleted = true;
                return new[] { connection };
            },
            refreshConnections: async (s, f, ct) =>
            {
                // Full refresh would block on TCS (simulating 10s quota fetch)
                // But this should NEVER be called in the WaitForConnectionAsync workflow
                fullRefreshStarted = true;
                try
                {
                    // This should never complete; test has a timeout on WaitForConnectionAsync
                    await fullRefreshTcs.Task.WaitAsync(ct);
                }
                catch (OperationCanceledException)
                {
                    // Expected when test cancels
                }
                fullRefreshCompleted = true;
            });

        // Set up the workflow state as if an OAuth flow just completed
        await service.CaptureExistingConnectionsAsync(mock.Object, ProviderKind.Codex, CancellationToken.None);

        // Start WaitForConnectionAsync with a bounded cancellation (5s)
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var waitTask = service.WaitForConnectionAsync();

        // Wait for lightweight refresh with a bounded timeout (not unbounded SpinWait)
        var waitForLightweight = Task.Run(async () =>
        {
            for (int i = 0; i < 500; i++) // 500 * 10ms = 5s max
            {
                if (lightweightRefreshCompleted)
                {
                    return true;
                }
                await Task.Delay(10);
            }
            return false;
        });

        var completedTask = await Task.WhenAny(waitForLightweight, Task.Delay(5500));
        var lightweightCompleted = completedTask == waitForLightweight && await waitForLightweight;

        Assert.True(lightweightCompleted, "Lightweight refresh did not complete in time");

        // Workflow should have returned (IsInProgress is false)
        Assert.False(service.IsInProgress, "IsInProgress should be false after lightweight refresh completes");
        Assert.True(lightweightRefreshCompleted, "Lightweight refresh should have run");
        Assert.False(fullRefreshCompleted, "Full refresh should NOT have completed");
        Assert.False(fullRefreshStarted, "Full refresh should NOT have been called at all");

        // Wait for wait task to fully complete (with timeout)
        var waitCompleted = await Task.WhenAny(waitTask, Task.Delay(5500));
        Assert.True(waitCompleted == waitTask, "WaitForConnectionAsync did not complete in time");

        // Full refresh still should not have been called
        Assert.False(fullRefreshStarted, "Full refresh should never be called in WaitForConnectionAsync workflow");
    }
}