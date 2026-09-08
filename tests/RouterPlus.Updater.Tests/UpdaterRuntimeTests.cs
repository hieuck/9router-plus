using RouterPlus.Updater;

namespace RouterPlus.Updater.Tests;

public sealed class UpdaterRuntimeTests
{
    [Fact]
    public void IsProcessRunning_returns_false_for_a_missing_process()
    {
        var runtime = new WindowsUpdateTransactionRuntime();

        var result = runtime.IsProcessRunning(int.MaxValue);

        Assert.False(result);
    }

    [Fact]
    public async Task DelayAsync_completes_for_a_zero_delay()
    {
        var runtime = new WindowsUpdateTransactionRuntime();

        await runtime.DelayAsync(TimeSpan.Zero, CancellationToken.None);
    }

    [Fact]
    public async Task LaunchAndWaitForHealthyAsync_returns_false_when_executable_cannot_start()
    {
        var runtime = new WindowsUpdateTransactionRuntime();

        var result = await runtime.LaunchAndWaitForHealthyAsync(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.exe"),
            Path.GetTempPath(),
            TimeSpan.FromMilliseconds(10),
            CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task LaunchAndWaitForHealthyAsync_reports_success_for_a_process_that_exits_zero()
    {
        var runtime = new WindowsUpdateTransactionRuntime();

        var result = await runtime.LaunchAndWaitForHealthyAsync(
            Environment.ProcessPath!,
            AppContext.BaseDirectory,
            TimeSpan.FromMilliseconds(10),
            CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public void NamedUpdateMutex_can_acquire_and_release_the_update_lock()
    {
        using var mutex = new NamedUpdateMutex();

        Assert.True(mutex.TryAcquire());
    }
}
