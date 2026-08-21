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
        public TimeSpan Delay { get; init; } = TimeSpan.Zero;
        public bool HealthCheckResult { get; init; }
        public string? StartedExecutable { get; private set; }

        public bool IsProcessRunning(int processId) => ParentRunning;

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(Delay, cancellationToken);

        public Task<bool> LaunchAndWaitForHealthyAsync(string executablePath, string workingDirectory, TimeSpan timeout, CancellationToken cancellationToken)
        {
            StartedExecutable = executablePath;
            return Task.FromResult(HealthCheckResult);
        }
    }

    private sealed class FakeMutex : IUpdateMutex
    {
        public bool Acquired { get; init; }
        public bool TryAcquire() => Acquired;
        public void Dispose() { }
    }
}
