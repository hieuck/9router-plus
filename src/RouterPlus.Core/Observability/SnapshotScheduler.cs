using System;
using System.Threading;
using System.Threading.Tasks;

namespace RouterPlus.Core.Observability;

/// <summary>
/// Manages periodic state snapshots for ViewModels.
/// </summary>
public sealed class SnapshotScheduler : IDisposable
{
    private readonly ObservabilityHub _hub;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _schedulerTask;
    private Func<(string component, Dictionary<string, object?> state)>? _snapshotProvider;
    private Dictionary<string, object?>? _lastState;

    public SnapshotScheduler(ObservabilityHub hub, TimeSpan interval)
    {
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));
        _interval = interval;
        _schedulerTask = Task.Run(ScheduleLoopAsync);
    }

    /// <summary>
    /// Registers a callback that provides component name and state for periodic snapshots.
    /// </summary>
    public void RegisterProvider(Func<(string component, Dictionary<string, object?> state)> provider)
    {
        _snapshotProvider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    private async Task ScheduleLoopAsync()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_interval, _cts.Token);

                if (_snapshotProvider != null)
                {
                    var (component, state) = _snapshotProvider();

                    // Only capture if state changed
                    if (HasStateChanged(state))
                    {
                        _hub.CaptureSnapshot(component, state, SnapshotTrigger.Periodic);
                        _lastState = state;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Continue on errors
            }
        }
    }

    private bool HasStateChanged(Dictionary<string, object?> newState)
    {
        if (_lastState == null) return true;
        if (_lastState.Count != newState.Count) return true;

        foreach (var kvp in newState)
        {
            if (!_lastState.TryGetValue(kvp.Key, out var oldValue))
                return true;

            if (!Equals(oldValue, kvp.Value))
                return true;
        }

        return false;
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _schedulerTask.Wait(TimeSpan.FromSeconds(5));
        }
        catch
        {
            // Best effort
        }
        finally
        {
            _cts.Dispose();
        }
    }
}
