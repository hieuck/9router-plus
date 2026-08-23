using RouterPlus.App.Services;

namespace RouterPlus.Core.Tests;

public sealed class QuotaPollingServiceTests
{
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
