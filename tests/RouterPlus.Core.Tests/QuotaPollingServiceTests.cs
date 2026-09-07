using RouterPlus.App.Services;

namespace RouterPlus.Core.Tests;

public sealed class QuotaPollingServiceTests
{
    [Fact]
    public void Constructor_rejects_null_refresh_delegate()
    {
        Assert.Throws<ArgumentNullException>(() => new QuotaPollingService(null!));
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void Constructor_rejects_negative_intervals(double normalSeconds, double nearLimitSeconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new QuotaPollingService(
            _ => Task.FromResult(false),
            new QuotaPollingOptions(TimeSpan.FromSeconds(normalSeconds), TimeSpan.FromSeconds(nearLimitSeconds))));
    }

    [Fact]
    public async Task Start_is_idempotent_and_pause_is_idempotent()
    {
        var refreshes = 0;
        var delayEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new QuotaPollingService(
            _ =>
            {
                Interlocked.Increment(ref refreshes);
                return Task.FromResult(false);
            },
            new QuotaPollingOptions(TimeSpan.FromHours(1), TimeSpan.FromHours(1)),
            (_, cancellationToken) =>
            {
                delayEntered.TrySetResult(true);
                return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            });

        service.Start();
        service.Start();
        await delayEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        service.Pause();
        service.Pause();

        Assert.True(service.IsPaused);
        await service.StopAsync();
        Assert.False(service.IsPaused);
        Assert.Equal(0, refreshes);
    }

    [Fact]
    public async Task ResumeAsync_is_noop_before_start_and_when_not_paused()
    {
        var refreshes = 0;
        var service = new QuotaPollingService(
            _ =>
            {
                Interlocked.Increment(ref refreshes);
                return Task.FromResult(false);
            },
            new QuotaPollingOptions(TimeSpan.FromHours(1), TimeSpan.FromHours(1)),
            (_, cancellationToken) => Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken));

        await service.ResumeAsync();
        service.Start();
        await service.ResumeAsync();
        await service.StopAsync();

        Assert.Equal(0, refreshes);
    }

    [Fact]
    public async Task Refresh_failure_falls_back_to_normal_interval()
    {
        var delays = new List<TimeSpan>();
        var refreshes = 0;
        var refreshFailed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new QuotaPollingService(
            _ =>
            {
                refreshes++;
                if (refreshes == 1)
                {
                    refreshFailed.TrySetResult(true);
                    throw new InvalidOperationException("refresh failed");
                }

                return Task.FromResult(false);
            },
            new QuotaPollingOptions(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30)),
            (delay, cancellationToken) =>
            {
                if (delays.Count >= 2)
                {
                    return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }

                delays.Add(delay);
                return Task.CompletedTask;
            });

        service.Start();
        await refreshFailed.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await service.StopAsync();

        Assert.Equal([TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5)], delays);
    }

    [Fact]
    public async Task ResumeAsync_refresh_failure_clears_near_limit_state()
    {
        var delays = new List<TimeSpan>();
        var refreshes = 0;
        var refreshStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new QuotaPollingService(
            _ =>
            {
                refreshes++;
                if (refreshes == 1)
                {
                    return Task.FromResult(true);
                }

                refreshStarted.TrySetResult(true);
                throw new InvalidOperationException("resume refresh failed");
            },
            new QuotaPollingOptions(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30)),
            (delay, cancellationToken) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            });

        service.Start();
        await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        service.Pause();
        await service.ResumeAsync();
        await service.StopAsync();

        Assert.Contains(TimeSpan.FromSeconds(30), delays);
        Assert.Contains(TimeSpan.FromMinutes(5), delays);
    }

    [Fact]
    public async Task ResumeAsync_ignores_cancellation_when_stop_cancels_refresh()
    {
        var delayEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new QuotaPollingService(
            cancellationToken =>
            {
                refreshStarted.TrySetResult(true);
                return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)
                    .ContinueWith(_ => false, TaskScheduler.Default);
            },
            new QuotaPollingOptions(TimeSpan.FromHours(1), TimeSpan.FromHours(1)),
            (_, cancellationToken) =>
            {
                delayEntered.TrySetResult(true);
                return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            });

        service.Start();
        await delayEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        service.Pause();
        var resumeTask = service.ResumeAsync();
        await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await service.StopAsync();

        await resumeTask;
    }

    [Fact]
    public async Task Polling_uses_near_limit_interval_after_refresh_reports_near_limit()
    {
        var delays = new List<TimeSpan>();
        var refreshes = 0;
        var firstRefresh = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRefresh = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new QuotaPollingService(
            _ =>
            {
                refreshes++;
                if (refreshes == 1) firstRefresh.TrySetResult(true);
                if (refreshes == 2) secondRefresh.TrySetResult(true);
                return Task.FromResult(refreshes == 1);
            },
            new QuotaPollingOptions(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30)),
            (delay, cancellationToken) =>
            {
                if (delays.Count >= 2)
                {
                    return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }

                delays.Add(delay);
                return Task.CompletedTask;
            });

        service.Start();
        await firstRefresh.Task;
        await secondRefresh.Task;
        await service.StopAsync();

        Assert.Equal(
            [TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30)],
            delays);
    }

    [Fact]
    public async Task Paused_polling_does_not_refresh_until_resumed()
    {
        var refreshes = 0;
        var delayEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new QuotaPollingService(
            _ =>
            {
                Interlocked.Increment(ref refreshes);
                return Task.FromResult(false);
            },
            new QuotaPollingOptions(TimeSpan.FromHours(1), TimeSpan.FromHours(1)),
            (_, cancellationToken) =>
            {
                delayEntered.TrySetResult(true);
                return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            });

        service.Start();
        await delayEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        service.Pause();
        await service.ResumeAsync();
        await service.StopAsync();

        Assert.Equal(1, refreshes);
    }

    [Fact]
    public async Task Stop_cancels_pending_delay()
    {
        var delayEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new QuotaPollingService(
            _ => Task.FromResult(false),
            new QuotaPollingOptions(TimeSpan.FromHours(1), TimeSpan.FromHours(1)),
            (_, cancellationToken) =>
            {
                delayEntered.TrySetResult(true);
                cancellationToken.Register(() => delayCancelled.TrySetResult(true));
                return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            });

        service.Start();
        await delayEntered.Task.WaitAsync(TimeSpan.FromSeconds(1));
        await service.StopAsync();

        Assert.True(await delayCancelled.Task.WaitAsync(TimeSpan.FromSeconds(1)));
    }
}
