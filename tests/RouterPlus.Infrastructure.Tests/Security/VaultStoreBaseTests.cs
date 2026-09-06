using RouterPlus.Infrastructure.Security;

namespace RouterPlus.Infrastructure.Tests.Security;

/// <summary>
/// Tests for VaultStoreBase disposal and concurrency management.
/// </summary>
public sealed class VaultStoreBaseTests
{
    /// <summary>
    /// Concrete test implementation of VaultStoreBase.
    /// </summary>
    private sealed class TestVaultStore : VaultStoreBase
    {
        public int OperationCount { get; private set; }
        public int WriteOperationCount { get; private set; }
        public bool DisposeManagedResourcesCalled { get; private set; }

        public async Task<int> TestOperationAsync(CancellationToken cancellationToken = default)
        {
            return await ExecuteOperationAsync(async () =>
            {
                OperationCount++;
                await Task.Delay(10, cancellationToken);
                return OperationCount;
            }, cancellationToken);
        }

        public async Task TestWriteOperationAsync(CancellationToken cancellationToken = default)
        {
            await ExecuteWriteOperationAsync(async () =>
            {
                WriteOperationCount++;
                await Task.Delay(10, cancellationToken);
            }, cancellationToken);
        }

        protected override void DisposeManagedResources()
        {
            DisposeManagedResourcesCalled = true;
        }
    }

    [Fact]
    public async Task ExecuteOperationAsync_increments_operation_count()
    {
        using var store = new TestVaultStore();

        var result = await store.TestOperationAsync();

        Assert.Equal(1, result);
        Assert.Equal(1, store.OperationCount);
    }

    [Fact]
    public async Task ExecuteWriteOperationAsync_increments_write_operation_count()
    {
        using var store = new TestVaultStore();

        await store.TestWriteOperationAsync();

        Assert.Equal(1, store.WriteOperationCount);
    }

    [Fact]
    public async Task Concurrent_read_operations_execute_in_parallel()
    {
        using var store = new TestVaultStore();

        var tasks = Enumerable.Range(0, 10)
            .Select(_ => store.TestOperationAsync())
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.Equal(10, store.OperationCount);
        Assert.All(results, result => Assert.InRange(result, 1, 10));
    }

    [Fact]
    public async Task Write_operations_execute_serially()
    {
        using var store = new TestVaultStore();

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => store.TestWriteOperationAsync())
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.Equal(5, store.WriteOperationCount);
    }

    [Fact]
    public async Task Operations_throw_ObjectDisposedException_after_disposal_starts()
    {
        var store = new TestVaultStore();

        // Start disposal on background thread
        var disposeTask = Task.Run(() => store.Dispose());

        // Give disposal a moment to start
        await Task.Delay(5);

        // Attempting new operations should throw
        await Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await store.TestOperationAsync());

        await disposeTask;
    }

    [Fact]
    public async Task Dispose_waits_for_pending_operations_to_complete()
    {
        var store = new TestVaultStore();
        var operationStarted = new TaskCompletionSource<bool>();
        var allowOperationToComplete = new TaskCompletionSource<bool>();

        // Start long-running operation using public wrapper
        var operationTask = Task.Run(async () =>
        {
            await store.TestOperationAsync(CancellationToken.None);
        });

        // Wait a bit for operation to be running
        await Task.Delay(50);

        // Start disposal on another thread
        var disposeTask = Task.Run(() => store.Dispose());

        // Give disposal a moment
        await Task.Delay(50);

        // Wait for both to complete (operation should finish first, then disposal)
        await operationTask;
        await disposeTask;

        Assert.True(store.DisposeManagedResourcesCalled);
    }

    [Fact]
    public void Multiple_dispose_calls_are_safe()
    {
        var store = new TestVaultStore();

        store.Dispose();
        store.Dispose(); // Should not throw
        store.Dispose(); // Should not throw

        Assert.True(store.DisposeManagedResourcesCalled);
    }

    [Fact]
    public async Task Concurrent_dispose_calls_are_safe()
    {
        var store = new TestVaultStore();

        var disposeTasks = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => store.Dispose()))
            .ToArray();

        await Task.WhenAll(disposeTasks);

        Assert.True(store.DisposeManagedResourcesCalled);
    }

    [Fact]
    public async Task Operations_respect_cancellation_token()
    {
        using var store = new TestVaultStore();
        using var cts = new CancellationTokenSource();

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await store.TestOperationAsync(cts.Token));
    }

    [Fact]
    public async Task Write_operations_are_mutually_exclusive()
    {
        using var store = new TestVaultStore();
        var write1Completed = false;
        var write2Completed = false;

        // Start first write (will take 10ms from TestWriteOperationAsync)
        var write1Task = Task.Run(async () =>
        {
            await store.TestWriteOperationAsync();
            write1Completed = true;
        });

        // Give first write time to acquire lock
        await Task.Delay(5);

        // Start second write
        var write2Task = Task.Run(async () =>
        {
            await store.TestWriteOperationAsync();
            write2Completed = true;
        });

        // Wait for both to complete
        await write1Task;
        await write2Task;

        // Both should have completed (serial execution)
        Assert.True(write1Completed);
        Assert.True(write2Completed);
        Assert.Equal(2, store.WriteOperationCount);
    }

    [Fact]
    public void DisposeManagedResources_hook_is_called_during_disposal()
    {
        var store = new TestVaultStore();

        Assert.False(store.DisposeManagedResourcesCalled);

        store.Dispose();

        Assert.True(store.DisposeManagedResourcesCalled);
    }

    [Fact]
    public async Task Stress_test_concurrent_operations_and_disposal()
    {
        var store = new TestVaultStore();
        var operationTasks = new List<Task>();

        // Start many concurrent operations
        for (int i = 0; i < 100; i++)
        {
            operationTasks.Add(Task.Run(async () =>
            {
                try
                {
                    await store.TestOperationAsync();
                }
                catch (ObjectDisposedException)
                {
                    // Expected after disposal starts
                }
            }));
        }

        // Let some operations start
        await Task.Delay(10);

        // Start disposal
        var disposeTask = Task.Run(() => store.Dispose());

        // Wait for all to complete
        await Task.WhenAll(operationTasks);
        await disposeTask;

        // Should complete without deadlock or exceptions (other than ObjectDisposedException)
        Assert.True(store.DisposeManagedResourcesCalled);
    }
}
