namespace RouterPlus.Infrastructure.Security;

/// <summary>
/// Base class for vault stores providing thread-safe disposal and concurrent operation management.
/// Ensures that all pending operations complete before disposal and prevents new operations
/// from starting after disposal begins.
/// </summary>
public abstract class VaultStoreBase : IDisposable
{
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _disposalLock = new();
    private int _pendingOperations;
    private bool _disposalStarted;
    private bool _disposed;

    /// <summary>
    /// Execute an operation with disposal protection and concurrency control.
    /// </summary>
    protected async Task<T> ExecuteOperationAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        EnterOperation();
        try
        {
            await EnterGateAsync(cancellationToken);
            return await operation();
        }
        finally
        {
            ExitOperation();
        }
    }

    /// <summary>
    /// Execute an operation with disposal protection and concurrency control.
    /// </summary>
    protected async Task ExecuteOperationAsync(Func<Task> operation, CancellationToken cancellationToken)
    {
        EnterOperation();
        try
        {
            await EnterGateAsync(cancellationToken);
            await operation();
        }
        finally
        {
            ExitOperation();
        }
    }

    /// <summary>
    /// Execute a write operation with exclusive write lock, disposal protection, and concurrency control.
    /// </summary>
    protected async Task<T> ExecuteWriteOperationAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            return await ExecuteOperationAsync(operation, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Execute a write operation with exclusive write lock, disposal protection, and concurrency control.
    /// </summary>
    protected async Task ExecuteWriteOperationAsync(Func<Task> operation, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            await ExecuteOperationAsync(operation, cancellationToken);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void EnterOperation()
    {
        lock (_disposalLock)
        {
            ThrowIfDisposalStarted();
            _pendingOperations++;
        }
    }

    private async Task EnterGateAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            lock (_disposalLock)
            {
                ThrowIfDisposalStarted();
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private void ThrowIfDisposalStarted()
    {
        if (_disposalStarted)
        {
            throw new ObjectDisposedException(GetType().Name);
        }
    }

    private void ExitOperation()
    {
        lock (_disposalLock)
        {
            _pendingOperations--;
            Monitor.PulseAll(_disposalLock);
        }
    }

    /// <summary>
    /// Dispose the vault store. Waits for all pending operations to complete before disposing resources.
    /// Safe to call multiple times and from multiple threads.
    /// </summary>
    public void Dispose()
    {
        lock (_disposalLock)
        {
            if (_disposed)
            {
                return;
            }

            if (_disposalStarted)
            {
                // Another thread is already disposing, wait for it to finish
                while (!_disposed)
                {
                    Monitor.Wait(_disposalLock);
                }

                return;
            }

            _disposalStarted = true;

            // Wait for all pending operations to complete
            while (_pendingOperations > 0)
            {
                Monitor.Wait(_disposalLock);
            }
        }

        // Dispose resources outside the lock
        _writeLock.Dispose();
        _operationGate.Dispose();

        // Additional cleanup hook for subclasses
        DisposeManagedResources();

        lock (_disposalLock)
        {
            _disposed = true;
            Monitor.PulseAll(_disposalLock);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Override this method to dispose additional managed resources in subclasses.
    /// Called after locks are disposed but before _disposed flag is set.
    /// </summary>
    protected virtual void DisposeManagedResources()
    {
        // Subclasses can override to dispose their own resources
    }
}
