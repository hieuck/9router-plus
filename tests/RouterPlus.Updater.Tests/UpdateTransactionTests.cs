using RouterPlus.Core.Updates;
using RouterPlus.Updater;

namespace RouterPlus.Updater.Tests;

public sealed class UpdateTransactionTests
{
    [Fact]
    public async Task Execute_swaps_staging_and_launches_the_new_app()
    {
        using var fixture = UpdateFixture.Create();
        await fixture.WriteAsync(fixture.TargetDirectory, "old");
        await fixture.WriteAsync(fixture.StagingDirectory, "new");
        var runtime = new FakeRuntime { HealthCheckResult = true };
        var transaction = new UpdateTransaction(runtime, new FakeMutex { Acquired = true });

        var result = await transaction.ExecuteAsync(fixture.Options);

        Assert.Equal(UpdateTransactionResult.Success, result);
        Assert.Equal("new", await fixture.ReadAsync(fixture.TargetDirectory));
        Assert.False(Directory.Exists(fixture.StagingDirectory));
        Assert.False(Directory.Exists(fixture.BackupDirectory));
        Assert.Equal(Path.Combine(fixture.TargetDirectory, "RouterPlus.exe"), runtime.StartedExecutable);
    }

    [Fact]
    public async Task Execute_rolls_back_when_health_check_fails()
    {
        using var fixture = UpdateFixture.Create();
        await fixture.WriteAsync(fixture.TargetDirectory, "old");
        await fixture.WriteAsync(fixture.StagingDirectory, "new");
        var runtime = new FakeRuntime { HealthCheckResult = false };
        var transaction = new UpdateTransaction(runtime, new FakeMutex { Acquired = true });

        var result = await transaction.ExecuteAsync(fixture.Options);

        Assert.Equal(UpdateTransactionResult.HealthCheckFailed, result);
        Assert.Equal("old", await fixture.ReadAsync(fixture.TargetDirectory));
        Assert.False(Directory.Exists(fixture.StagingDirectory));
        Assert.Equal(Path.Combine(fixture.TargetDirectory, "RouterPlus.exe"), runtime.StartedExecutable);
    }

    [Fact]
    public async Task Execute_rejects_relative_or_invalid_paths_before_touching_live_app()
    {
        using var fixture = UpdateFixture.Create();
        await fixture.WriteAsync(fixture.TargetDirectory, "old");
        var invalid = fixture.Options with { StagingDirectory = "relative-staging" };
        var transaction = new UpdateTransaction(new FakeRuntime(), new FakeMutex { Acquired = true });

        var result = await transaction.ExecuteAsync(invalid);

        Assert.Equal(UpdateTransactionResult.ValidationFailed, result);
        Assert.Equal("old", await fixture.ReadAsync(fixture.TargetDirectory));
    }

    [Theory]
    [InlineData("null-options")]
    [InlineData("null-version")]
    [InlineData("invalid-parent")]
    [InlineData("invalid-health-timeout")]
    [InlineData("relative-target")]
    [InlineData("relative-backup")]
    [InlineData("executable-outside-target")]
    [InlineData("target-equals-staging")]
    [InlineData("target-equals-backup")]
    [InlineData("staging-equals-backup")]
    [InlineData("missing-target")]
    [InlineData("missing-staging")]
    [InlineData("existing-backup")]
    public async Task Execute_rejects_each_invalid_option_without_touching_live_app(string invalidOption)
    {
        using var fixture = UpdateFixture.Create();
        await fixture.WriteAsync(fixture.TargetDirectory, "old");
        await fixture.WriteAsync(fixture.StagingDirectory, "new");
        var options = invalidOption switch
        {
            "null-options" => null,
            "null-version" => fixture.Options with { Version = null! },
            "invalid-parent" => fixture.Options with { ParentProcessId = 0 },
            "invalid-health-timeout" => fixture.Options with { HealthCheckTimeout = TimeSpan.Zero },
            "relative-target" => fixture.Options with { TargetDirectory = "relative-target" },
            "relative-backup" => fixture.Options with { BackupDirectory = "relative-backup" },
            "executable-outside-target" => fixture.Options with { ApplicationExecutablePath = Path.Combine(fixture.Root, "RouterPlus.exe") },
            "target-equals-staging" => fixture.Options with { StagingDirectory = fixture.TargetDirectory },
            "target-equals-backup" => fixture.Options with { BackupDirectory = fixture.TargetDirectory },
            "staging-equals-backup" => fixture.Options with { BackupDirectory = fixture.StagingDirectory },
            "missing-target" => fixture.Options with { TargetDirectory = Path.Combine(fixture.Root, "missing-live") },
            "missing-staging" => fixture.Options with { StagingDirectory = Path.Combine(fixture.Root, "missing-staging") },
            "existing-backup" => fixture.Options,
            _ => throw new ArgumentOutOfRangeException(nameof(invalidOption))
        };
        if (invalidOption == "existing-backup")
        {
            Directory.CreateDirectory(fixture.BackupDirectory);
        }

        var runtime = new FakeRuntime();
        var transaction = new UpdateTransaction(runtime, new FakeMutex { Acquired = true });

        var result = await transaction.ExecuteAsync(options!);

        Assert.Equal(UpdateTransactionResult.ValidationFailed, result);
        Assert.Equal("old", await fixture.ReadAsync(fixture.TargetDirectory));
        Assert.Null(runtime.StartedExecutable);
    }

    [Fact]
    public async Task Execute_propagates_cancellation_while_waiting_for_parent_exit()
    {
        using var fixture = UpdateFixture.Create();
        await fixture.WriteAsync(fixture.TargetDirectory, "old");
        await fixture.WriteAsync(fixture.StagingDirectory, "new");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var runtime = new FakeRuntime { ParentRunning = true, Delay = TimeSpan.FromMilliseconds(1) };
        var transaction = new UpdateTransaction(runtime, new FakeMutex { Acquired = true });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transaction.ExecuteAsync(fixture.Options, cancellation.Token));

        Assert.Equal("old", await fixture.ReadAsync(fixture.TargetDirectory));
        Assert.False(Directory.Exists(fixture.BackupDirectory));
    }

    [Fact]
    public async Task Execute_rolls_back_and_returns_swap_failure_when_health_check_is_cancelled()
    {
        using var fixture = UpdateFixture.Create();
        await fixture.WriteAsync(fixture.TargetDirectory, "old");
        await fixture.WriteAsync(fixture.StagingDirectory, "new");
        var runtime = new FakeRuntime { HealthCheckException = new OperationCanceledException() };
        var transaction = new UpdateTransaction(runtime, new FakeMutex { Acquired = true });

        var result = await transaction.ExecuteAsync(fixture.Options);

        Assert.Equal(UpdateTransactionResult.SwapFailed, result);
        Assert.Equal("old", await fixture.ReadAsync(fixture.TargetDirectory));
        Assert.False(Directory.Exists(fixture.StagingDirectory));
        Assert.False(Directory.Exists(fixture.BackupDirectory));
    }

    [Fact]
    public async Task Execute_returns_rollback_failed_when_health_check_removes_the_backup()
    {
        using var fixture = UpdateFixture.Create();
        await fixture.WriteAsync(fixture.TargetDirectory, "old");
        await fixture.WriteAsync(fixture.StagingDirectory, "new");
        var runtime = new FakeRuntime
        {
            HealthCheckResult = false,
            BeforeHealthCheck = () => Directory.Delete(fixture.BackupDirectory, recursive: true)
        };
        var transaction = new UpdateTransaction(runtime, new FakeMutex { Acquired = true });

        var result = await transaction.ExecuteAsync(fixture.Options);

        Assert.Equal(UpdateTransactionResult.RollbackFailed, result);
        Assert.False(Directory.Exists(fixture.TargetDirectory));
    }

    [Fact]
    public async Task Execute_returns_rollback_failed_when_cancellation_prevents_health_rollback()
    {
        using var fixture = UpdateFixture.Create();
        await fixture.WriteAsync(fixture.TargetDirectory, "old");
        await fixture.WriteAsync(fixture.StagingDirectory, "new");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var runtime = new FakeRuntime { HealthCheckResult = false };
        var transaction = new UpdateTransaction(runtime, new FakeMutex { Acquired = true });

        var result = await transaction.ExecuteAsync(fixture.Options, cancellation.Token);

        Assert.Equal(UpdateTransactionResult.RollbackFailed, result);
        Assert.Equal("new", await fixture.ReadAsync(fixture.TargetDirectory));
        Assert.True(Directory.Exists(fixture.BackupDirectory));
    }

    [Fact]
    public async Task Execute_uses_default_parent_timeout_when_configured_timeout_is_zero()
    {
        using var fixture = UpdateFixture.Create(parentWaitTimeout: TimeSpan.Zero);
        await fixture.WriteAsync(fixture.TargetDirectory, "old");
        await fixture.WriteAsync(fixture.StagingDirectory, "new");
        var runtime = new FakeRuntime { HealthCheckResult = true };
        var transaction = new UpdateTransaction(runtime, new FakeMutex { Acquired = true });

        var result = await transaction.ExecuteAsync(fixture.Options);

        Assert.Equal(UpdateTransactionResult.Success, result);
        Assert.Equal("new", await fixture.ReadAsync(fixture.TargetDirectory));
    }

    [Fact]
    public void WindowsRuntime_reports_missing_process_as_not_running()
    {
        var runtime = new WindowsUpdateTransactionRuntime();

        Assert.False(runtime.IsProcessRunning(int.MaxValue));
    }

    [Fact]
    public async Task WindowsRuntime_returns_false_when_executable_cannot_be_started()
    {
        using var fixture = UpdateFixture.Create();
        var runtime = new WindowsUpdateTransactionRuntime();

        var healthy = await runtime.LaunchAndWaitForHealthyAsync(
            Path.Combine(fixture.Root, "missing.exe"),
            fixture.Root,
            TimeSpan.FromMilliseconds(10),
            CancellationToken.None);

        Assert.False(healthy);
    }

    [Fact]
    public async Task WindowsRuntime_delay_honors_cancellation()
    {
        var runtime = new WindowsUpdateTransactionRuntime();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            runtime.DelayAsync(TimeSpan.FromSeconds(1), cancellation.Token));
    }

    [Fact]
    public async Task Execute_refuses_to_run_when_another_updater_holds_the_mutex()
    {
        using var fixture = UpdateFixture.Create();
        await fixture.WriteAsync(fixture.TargetDirectory, "old");
        await fixture.WriteAsync(fixture.StagingDirectory, "new");
        var mutex = new FakeMutex { Acquired = false };
        var transaction = new UpdateTransaction(new FakeRuntime(), mutex);

        var result = await transaction.ExecuteAsync(fixture.Options);

        Assert.Equal(UpdateTransactionResult.AlreadyRunning, result);
        Assert.Equal("old", await fixture.ReadAsync(fixture.TargetDirectory));
    }

    [Fact]
    public async Task Execute_returns_swap_failed_when_target_move_fails_before_backup_is_created()
    {
        using var fixture = UpdateFixture.Create();
        await fixture.WriteAsync(fixture.TargetDirectory, "old");
        await fixture.WriteAsync(fixture.StagingDirectory, "new");
        var invalidSwap = fixture.Options with
        {
            BackupDirectory = Path.Combine(fixture.TargetDirectory, "backup")
        };
        var transaction = new UpdateTransaction(new FakeRuntime(), new FakeMutex { Acquired = true });

        var result = await transaction.ExecuteAsync(invalidSwap);

        Assert.Equal(UpdateTransactionResult.SwapFailed, result);
        Assert.Equal("old", await fixture.ReadAsync(fixture.TargetDirectory));
        Assert.Equal("new", await fixture.ReadAsync(fixture.StagingDirectory));
    }

    [Fact]
    public async Task Execute_rolls_back_when_staging_is_nested_under_target_and_swap_fails()
    {
        using var fixture = UpdateFixture.Create();
        var nestedStaging = Path.Combine(fixture.TargetDirectory, "staging");
        await fixture.WriteAsync(fixture.TargetDirectory, "old");
        await fixture.WriteAsync(nestedStaging, "new");
        var options = fixture.Options with { StagingDirectory = nestedStaging };
        var transaction = new UpdateTransaction(new FakeRuntime(), new FakeMutex { Acquired = true });

        var result = await transaction.ExecuteAsync(options);

        Assert.Equal(UpdateTransactionResult.SwapFailed, result);
        Assert.Equal("old", await fixture.ReadAsync(fixture.TargetDirectory));
        Assert.False(Directory.Exists(nestedStaging));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Execute_rolls_back_when_health_check_launch_throws(bool cancellation)
    {
        using var fixture = UpdateFixture.Create();
        await fixture.WriteAsync(fixture.TargetDirectory, "old");
        await fixture.WriteAsync(fixture.StagingDirectory, "new");
        var runtime = new FakeRuntime
        {
            LaunchException = cancellation
                ? new OperationCanceledException()
                : new InvalidOperationException()
        };
        var transaction = new UpdateTransaction(runtime, new FakeMutex { Acquired = true });

        var result = await transaction.ExecuteAsync(fixture.Options);

        Assert.Equal(UpdateTransactionResult.SwapFailed, result);
        Assert.Equal("old", await fixture.ReadAsync(fixture.TargetDirectory));
        Assert.False(Directory.Exists(fixture.StagingDirectory));
    }

    [Fact]
    public async Task Execute_returns_rollback_failed_when_health_check_removes_backup()
    {
        using var fixture = UpdateFixture.Create();
        await fixture.WriteAsync(fixture.TargetDirectory, "old");
        await fixture.WriteAsync(fixture.StagingDirectory, "new");
        var runtime = new FakeRuntime
        {
            HealthCheckResult = false,
            BeforeHealthCheck = () => Directory.Delete(fixture.BackupDirectory, recursive: true)
        };
        var transaction = new UpdateTransaction(runtime, new FakeMutex { Acquired = true });

        var result = await transaction.ExecuteAsync(fixture.Options);

        Assert.Equal(UpdateTransactionResult.RollbackFailed, result);
        Assert.False(Directory.Exists(fixture.TargetDirectory));
    }

    [Fact]
    public async Task Execute_returns_rollback_failed_when_cancellation_is_requested_during_rollback()
    {
        using var fixture = UpdateFixture.Create();
        await fixture.WriteAsync(fixture.TargetDirectory, "old");
        await fixture.WriteAsync(fixture.StagingDirectory, "new");
        var runtime = new FakeRuntime { HealthCheckResult = false };
        var transaction = new UpdateTransaction(runtime, new FakeMutex { Acquired = true });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var result = await transaction.ExecuteAsync(fixture.Options, cancellation.Token);

        Assert.Equal(UpdateTransactionResult.RollbackFailed, result);
        Assert.Equal("new", await fixture.ReadAsync(fixture.TargetDirectory));
    }

    [Fact]
    public async Task Execute_uses_default_parent_timeout_when_configured_timeout_is_nonpositive()
    {
        using var fixture = UpdateFixture.Create();
        await fixture.WriteAsync(fixture.TargetDirectory, "old");
        await fixture.WriteAsync(fixture.StagingDirectory, "new");
        var runtime = new FakeRuntime { HealthCheckResult = true };
        var transaction = new UpdateTransaction(runtime, new FakeMutex { Acquired = true });
        var options = fixture.Options with { ParentWaitTimeout = TimeSpan.Zero };

        var result = await transaction.ExecuteAsync(options);

        Assert.Equal(UpdateTransactionResult.Success, result);
    }

    [Fact]
    public async Task Execute_rejects_an_executable_that_is_not_under_the_target_directory()
    {
        using var fixture = UpdateFixture.Create();
        await fixture.WriteAsync(fixture.TargetDirectory, "old");
        var options = fixture.Options with
        {
            ApplicationExecutablePath = Path.Combine(fixture.Root, "RouterPlus.exe")
        };
        var transaction = new UpdateTransaction(new FakeRuntime(), new FakeMutex { Acquired = true });

        var result = await transaction.ExecuteAsync(options);

        Assert.Equal(UpdateTransactionResult.ValidationFailed, result);
        Assert.Equal("old", await fixture.ReadAsync(fixture.TargetDirectory));
    }

    [Fact]
    public async Task Execute_waits_for_parent_then_returns_distinct_timeout_result()
    {
        using var fixture = UpdateFixture.Create(parentWaitTimeout: TimeSpan.FromMilliseconds(40));
        await fixture.WriteAsync(fixture.TargetDirectory, "old");
        await fixture.WriteAsync(fixture.StagingDirectory, "new");
        var runtime = new FakeRuntime { ParentRunning = true, Delay = TimeSpan.FromMilliseconds(10) };
        var transaction = new UpdateTransaction(runtime, new FakeMutex { Acquired = true });

        var result = await transaction.ExecuteAsync(fixture.Options);

        Assert.Equal(UpdateTransactionResult.ParentStillRunning, result);
        Assert.Equal("old", await fixture.ReadAsync(fixture.TargetDirectory));
    }

    [Fact]
    public async Task Execute_swaps_after_parent_exits()
    {
        using var fixture = UpdateFixture.Create();
        await fixture.WriteAsync(fixture.TargetDirectory, "old");
        await fixture.WriteAsync(fixture.StagingDirectory, "new");
        var checks = 0;
        var runtime = new FakeRuntime
        {
            ProcessRunning = () => Interlocked.Increment(ref checks) == 1,
            HealthCheckResult = true
        };
        var transaction = new UpdateTransaction(runtime, new FakeMutex { Acquired = true });

        var result = await transaction.ExecuteAsync(fixture.Options);

        Assert.Equal(UpdateTransactionResult.Success, result);
        Assert.Equal("new", await fixture.ReadAsync(fixture.TargetDirectory));
        Assert.Equal(2, checks);
    }

    [Fact]
    public async Task Execute_returns_swap_failed_when_target_disappears_before_swap()
    {
        using var fixture = UpdateFixture.Create();
        await fixture.WriteAsync(fixture.TargetDirectory, "old");
        await fixture.WriteAsync(fixture.StagingDirectory, "new");
        var mutex = new FakeMutex
        {
            Acquired = true,
            OnAcquire = () => Directory.Delete(fixture.TargetDirectory, recursive: true)
        };
        var transaction = new UpdateTransaction(new FakeRuntime(), mutex);

        var result = await transaction.ExecuteAsync(fixture.Options);

        Assert.Equal(UpdateTransactionResult.SwapFailed, result);
        Assert.False(Directory.Exists(fixture.TargetDirectory));
        Assert.Equal("new", await fixture.ReadAsync(fixture.StagingDirectory));
    }

    [Fact]
    public async Task Execute_rolls_back_when_staging_disappears_during_swap()
    {
        using var fixture = UpdateFixture.Create();
        await fixture.WriteAsync(fixture.TargetDirectory, "old");
        await fixture.WriteAsync(fixture.StagingDirectory, "new");
        var mutex = new FakeMutex
        {
            Acquired = true,
            OnAcquire = () => Directory.Delete(fixture.StagingDirectory, recursive: true)
        };
        var runtime = new FakeRuntime
        {
            HealthCheckResult = true
        };
        var transaction = new UpdateTransaction(runtime, mutex);

        var result = await transaction.ExecuteAsync(fixture.Options);

        Assert.Equal(UpdateTransactionResult.SwapFailed, result);
        Assert.Equal("old", await fixture.ReadAsync(fixture.TargetDirectory));
        Assert.False(Directory.Exists(fixture.StagingDirectory));
        Assert.False(Directory.Exists(fixture.BackupDirectory));
    }

    [Fact]
    public async Task Execute_returns_swap_failed_and_restores_target_when_health_check_is_cancelled()
    {
        using var fixture = UpdateFixture.Create();
        await fixture.WriteAsync(fixture.TargetDirectory, "old");
        await fixture.WriteAsync(fixture.StagingDirectory, "new");
        var runtime = new FakeRuntime { ThrowOperationCanceled = true };
        var transaction = new UpdateTransaction(runtime, new FakeMutex { Acquired = true });

        var result = await transaction.ExecuteAsync(fixture.Options);

        Assert.Equal(UpdateTransactionResult.SwapFailed, result);
        Assert.Equal("old", await fixture.ReadAsync(fixture.TargetDirectory));
        Assert.False(Directory.Exists(fixture.BackupDirectory));
    }

    [Fact]
    public async Task Execute_returns_rollback_failed_when_backup_disappears_before_rollback()
    {
        using var fixture = UpdateFixture.Create();
        await fixture.WriteAsync(fixture.TargetDirectory, "old");
        await fixture.WriteAsync(fixture.StagingDirectory, "new");
        var runtime = new FakeRuntime
        {
            HealthCheckResult = false,
            BeforeHealthCheck = () => Directory.Delete(fixture.BackupDirectory, recursive: true)
        };
        var transaction = new UpdateTransaction(runtime, new FakeMutex { Acquired = true });

        var result = await transaction.ExecuteAsync(fixture.Options);

        Assert.Equal(UpdateTransactionResult.RollbackFailed, result);
        Assert.False(Directory.Exists(fixture.TargetDirectory));
    }

    [Fact]
    public async Task Execute_propagates_cancellation_while_waiting_for_parent()
    {
        using var fixture = UpdateFixture.Create(parentWaitTimeout: TimeSpan.FromSeconds(1));
        await fixture.WriteAsync(fixture.TargetDirectory, "old");
        await fixture.WriteAsync(fixture.StagingDirectory, "new");
        using var cancellation = new CancellationTokenSource();
        var runtime = new FakeRuntime
        {
            ParentRunning = true,
            Delay = TimeSpan.Zero,
            CancelDuringDelay = cancellation
        };
        var transaction = new UpdateTransaction(runtime, new FakeMutex { Acquired = true });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => transaction.ExecuteAsync(fixture.Options, cancellation.Token));

        Assert.Equal("old", await fixture.ReadAsync(fixture.TargetDirectory));
        Assert.Equal("new", await fixture.ReadAsync(fixture.StagingDirectory));
    }

    private sealed class UpdateFixture : IDisposable
    {
        private UpdateFixture(string root, TimeSpan parentWaitTimeout)
        {
            Root = root;
            TargetDirectory = Path.Combine(root, "live");
            StagingDirectory = Path.Combine(root, "staging");
            BackupDirectory = Path.Combine(root, "backup");
            Options = new UpdateTransactionOptions(
                TargetDirectory,
                StagingDirectory,
                BackupDirectory,
                Path.Combine(TargetDirectory, "RouterPlus.exe"),
                123,
                ReleaseVersion.Parse("1.1.0"),
                parentWaitTimeout,
                TimeSpan.FromMilliseconds(40));
        }

        public string Root { get; }
        public string TargetDirectory { get; }
        public string StagingDirectory { get; }
        public string BackupDirectory { get; }
        public UpdateTransactionOptions Options { get; }

        public static UpdateFixture Create(TimeSpan? parentWaitTimeout = null)
        {
            var root = Path.Combine(Path.GetTempPath(), "RouterPlusUpdaterTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new UpdateFixture(root, parentWaitTimeout ?? TimeSpan.FromMilliseconds(100));
        }

        public async Task WriteAsync(string directory, string content)
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllTextAsync(Path.Combine(directory, "RouterPlus.exe"), content);
        }

        public Task<string> ReadAsync(string directory) => File.ReadAllTextAsync(Path.Combine(directory, "RouterPlus.exe"));

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    private sealed class FakeRuntime : IUpdateTransactionRuntime
    {
        public bool ParentRunning { get; init; }
        public Func<bool>? ProcessRunning { get; init; }
        public TimeSpan Delay { get; init; } = TimeSpan.Zero;
        public bool HealthCheckResult { get; init; }
        public Exception? HealthCheckException { get; init; }
        public bool ThrowOperationCanceled { get; init; }
        public Exception? LaunchException { get; init; }
        public Action? BeforeHealthCheck { get; init; }
        public CancellationTokenSource? CancelDuringDelay { get; init; }
        public string? StartedExecutable { get; private set; }

        public bool IsProcessRunning(int processId) => ProcessRunning?.Invoke() ?? ParentRunning;

        public async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            CancelDuringDelay?.Cancel();
            await Task.Delay(Delay, cancellationToken);
        }

        public Task<bool> LaunchAndWaitForHealthyAsync(string executablePath, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
        {
            StartedExecutable = executablePath;
            BeforeHealthCheck?.Invoke();
<<<<<<< HEAD
            if (HealthCheckException is not null)
            {
                throw HealthCheckException;
            }

            if (ThrowOperationCanceled)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (LaunchException is not null)
            {
                return Task.FromException<bool>(LaunchException);
            }

            return Task.FromResult(HealthCheckResult);
        }
    }

    private sealed class FakeMutex : IUpdateMutex
    {
        public bool Acquired { get; init; }
        public Action? OnAcquire { get; init; }
        public bool TryAcquire()
        {
            OnAcquire?.Invoke();
            return Acquired;
        }

        public void Dispose() { }
    }
}
