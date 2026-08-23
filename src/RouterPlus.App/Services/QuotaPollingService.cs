namespace RouterPlus.App.Services;

public sealed record QuotaPollingOptions(
    TimeSpan NormalInterval,
    TimeSpan NearLimitInterval)
{
    public static QuotaPollingOptions Default { get; } = new(
        TimeSpan.FromMinutes(5),
        TimeSpan.FromSeconds(30));
}

public sealed class QuotaPollingService
{
    private readonly Func<CancellationToken, Task<bool>> _refreshAsync;
    private readonly QuotaPollingOptions _options;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly SemaphoreSlim _wakeSignal = new(0, 1);
    private CancellationTokenSource? _cancellation;
    private CancellationTokenSource? _delayCancellation;
    private Task? _loopTask;
    private bool _paused;
    private bool _nearLimit;

    public bool IsPaused
    {
        get
        {
            lock (_sync)
            {
                return _paused;
            }
        }
    }

    public QuotaPollingService(
        Func<CancellationToken, Task<bool>> refreshAsync,
        QuotaPollingOptions? options = null,
        Func<TimeSpan, CancellationToken, Task>? delayAsync = null)
    {
        _refreshAsync = refreshAsync ?? throw new ArgumentNullException(nameof(refreshAsync));
        _options = options ?? QuotaPollingOptions.Default;
        _delayAsync = delayAsync ?? Task.Delay;
        if (_options.NormalInterval < TimeSpan.Zero || _options.NearLimitInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }
    }

    public void Start()
    {
        lock (_sync)
        {
            if (_loopTask is not null)
            {
                return;
            }

            _paused = false;
            var cancellation = new CancellationTokenSource();
            _cancellation = cancellation;
            _loopTask = RunAsync(cancellation.Token);
        }
    }

    public void Pause()
    {
        lock (_sync)
        {
            if (_paused)
            {
                return;
            }

            _paused = true;
            _delayCancellation?.Cancel();
        }
        SignalWake();
    }

    public async Task ResumeAsync()
    {
        CancellationToken cancellationToken;
        lock (_sync)
        {
            if (_loopTask is null || !_paused)
            {
                return;
            }

            _paused = false;
            cancellationToken = _cancellation!.Token;
        }

        try
        {
            await RunRefreshAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            _nearLimit = false;
        }

        SignalWake();
    }

    private void SignalWake()
    {
        try
        {
            _wakeSignal.Release();
        }
        catch (SemaphoreFullException)
        {
        }
    }

    private async Task RunRefreshAsync(CancellationToken cancellationToken)
    {
        if (!await _refreshGate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            _nearLimit = await _refreshAsync(cancellationToken);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    public async Task StopAsync()
    {
        Task? loopTask;
        lock (_sync)
        {
            _cancellation?.Cancel();
            loopTask = _loopTask;
        }

        if (loopTask is not null)
        {
            try
            {
                await loopTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        lock (_sync)
        {
            _cancellation?.Dispose();
            _cancellation = null;
            _loopTask = null;
            _paused = false;
        }

        while (_wakeSignal.Wait(0))
        {
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Yield();
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            TimeSpan interval;
            lock (_sync)
            {
                if (_paused)
                {
                    interval = Timeout.InfiniteTimeSpan;
                }
                else
                {
                    interval = _nearLimit ? _options.NearLimitInterval : _options.NormalInterval;
                }
            }

            if (interval == Timeout.InfiniteTimeSpan)
            {
                await _wakeSignal.WaitAsync(cancellationToken);
                continue;
            }

            CancellationToken delayToken;
            lock (_sync)
            {
                _delayCancellation?.Dispose();
                _delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                delayToken = _delayCancellation.Token;
            }

            try
            {
                await _delayAsync(interval, delayToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || delayToken.IsCancellationRequested)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                continue;
            }
            finally
            {
                lock (_sync)
                {
                    _delayCancellation?.Dispose();
                    _delayCancellation = null;
                }
            }
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            lock (_sync)
            {
                if (_paused)
                {
                    continue;
                }
            }

            try
            {
                await RunRefreshAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                _nearLimit = false;
            }
        }
    }
}
