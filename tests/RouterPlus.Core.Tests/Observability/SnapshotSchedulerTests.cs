using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using RouterPlus.Core.Observability;
using Xunit;

namespace RouterPlus.Core.Tests.Observability;

[Collection("Observability")]
public sealed class SnapshotSchedulerTests
{
    [Fact]
    public void Constructor_and_register_provider_reject_null_arguments()
    {
        var hub = ObservabilityHub.Instance;
        Assert.Throws<ArgumentNullException>(() => new SnapshotScheduler(null!, TimeSpan.FromHours(1)));
        using var scheduler = new SnapshotScheduler(hub, TimeSpan.FromHours(1));
        Assert.Throws<ArgumentNullException>(() => scheduler.RegisterProvider(null!));
    }

    [Fact]
    public void HasStateChanged_returns_true_for_initial_and_different_states()
    {
        using var scheduler = new SnapshotScheduler(ObservabilityHub.Instance, TimeSpan.FromHours(1));
        var method = GetHasStateChangedMethod();
        var initial = new Dictionary<string, object?> { ["status"] = "ready" };
        Assert.True(InvokeHasStateChanged(method, scheduler, initial));
        SetLastState(scheduler, initial);
        Assert.True(InvokeHasStateChanged(method, scheduler, new Dictionary<string, object?> { ["status"] = "busy" }));
        Assert.True(InvokeHasStateChanged(method, scheduler, new Dictionary<string, object?> { ["status"] = "ready", ["attempt"] = 1 }));
        Assert.True(InvokeHasStateChanged(method, scheduler, new Dictionary<string, object?> { ["other"] = "ready" }));
    }

    [Fact]
    public void HasStateChanged_returns_false_for_equal_states()
    {
        using var scheduler = new SnapshotScheduler(ObservabilityHub.Instance, TimeSpan.FromHours(1));
        var method = GetHasStateChangedMethod();
        SetLastState(scheduler, new Dictionary<string, object?> { ["status"] = "ready", ["count"] = 2 });
        Assert.False(InvokeHasStateChanged(method, scheduler,
            new Dictionary<string, object?> { ["status"] = "ready", ["count"] = 2 }));
    }

    [Fact]
    public async Task Scheduler_captures_first_state_and_only_changed_subsequent_states()
    {
        var delay = new ControlledDelay();
        var snapshots = new List<StateSnapshot>();
        var calls = 0;
        using var scheduler = new SnapshotScheduler(ObservabilityHub.Instance, TimeSpan.FromMinutes(7), delay.DelayAsync, Capture(snapshots));
        scheduler.RegisterProvider(() =>
        {
            var call = Interlocked.Increment(ref calls);
            return call switch
            {
                1 => ("SyntheticComponent", new Dictionary<string, object?> { ["Count"] = 1, ["Status"] = "Ready" }),
                2 => ("SyntheticComponent", new Dictionary<string, object?> { ["Status"] = "Ready", ["Count"] = 1 }),
                _ => ("SyntheticComponent", new Dictionary<string, object?> { ["Count"] = 2, ["Status"] = "Ready" })
            };
        });

        await delay.WaitForCallAsync();
        delay.ReleaseNext();
        await WaitForConditionAsync(() => Volatile.Read(ref calls) == 1);
        await delay.WaitForCallAsync();
        delay.ReleaseNext();
        await WaitForConditionAsync(() => Volatile.Read(ref calls) == 2);
        await delay.WaitForCallAsync();
        delay.ReleaseNext();
        await WaitForConditionAsync(() => Volatile.Read(ref calls) == 3);

        Assert.Equal(TimeSpan.FromMinutes(7), delay.LastInterval);
        Assert.Equal(2, snapshots.Count);
        Assert.Equal(1, snapshots[0].State["Count"]);
        Assert.Equal(2, snapshots[1].State["Count"]);
    }

    [Fact]
    public async Task Scheduler_continues_after_provider_failure()
    {
        var delay = new ControlledDelay();
        var snapshots = new List<StateSnapshot>();
        var secondSnapshot = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var scheduler = new SnapshotScheduler(ObservabilityHub.Instance, TimeSpan.FromSeconds(3), delay.DelayAsync,
            (component, state, trigger) => { Capture(snapshots)(component, state, trigger); secondSnapshot.TrySetResult(true); });
        scheduler.RegisterProvider(() =>
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 1) throw new InvalidOperationException("synthetic provider failure");
            return ("SyntheticComponent", new Dictionary<string, object?> { ["Attempt"] = call });
        });


        await delay.WaitForCallAsync();
        delay.ReleaseNext();
        await WaitForConditionAsync(() => Volatile.Read(ref calls) == 1);
        await delay.WaitForCallAsync();
        delay.ReleaseNext();
        await secondSnapshot.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Single(snapshots);
        Assert.Equal(2, snapshots[0].State["Attempt"]);
    }

    private static MethodInfo GetHasStateChangedMethod() => typeof(SnapshotScheduler).GetMethod("HasStateChanged", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static bool InvokeHasStateChanged(MethodInfo method, SnapshotScheduler scheduler, Dictionary<string, object?> state) =>
        (bool)method.Invoke(scheduler, new object[] { state })!;

    private static void SetLastState(SnapshotScheduler scheduler, Dictionary<string, object?> state) =>
        typeof(SnapshotScheduler).GetField("_lastState", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(scheduler, state);

    private static Action<string, Dictionary<string, object?>, SnapshotTrigger> Capture(List<StateSnapshot> snapshots) =>
        (component, state, trigger) => snapshots.Add(new StateSnapshot { Component = component, State = new Dictionary<string, object?>(state), Timestamp = DateTime.UtcNow, Trigger = trigger });

    private static async Task WaitForConditionAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition()) return;
            await Task.Delay(10);
        }
        Assert.True(condition(), "The scheduler did not complete the expected callback.");
    }

    private sealed class ControlledDelay
    {
        private readonly SemaphoreSlim _started = new(0);
        private readonly Queue<TaskCompletionSource<bool>> _gates = new();
        private readonly object _sync = new();
        private TimeSpan _lastInterval;
        public TimeSpan LastInterval
        {
            get
            {
                lock (_sync) return _lastInterval;
            }
        }

        public Task DelayAsync(TimeSpan interval, CancellationToken cancellationToken)
        {
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_sync)
            {
                _lastInterval = interval;
                _gates.Enqueue(gate);
            }
            _started.Release();
            return gate.Task.WaitAsync(cancellationToken);
        }

        public Task WaitForCallAsync() => _started.WaitAsync(TimeSpan.FromSeconds(1));

        public void ReleaseNext()
        {
            lock (_sync)
            {
                Assert.True(_gates.TryDequeue(out var gate));
                gate.TrySetResult(true);
            }
        }
    }
}
