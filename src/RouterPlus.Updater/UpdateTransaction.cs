using System.Diagnostics;
using RouterPlus.Core.Updates;

namespace RouterPlus.Updater;

public enum UpdateTransactionResult
{
    Success = 0,
    ValidationFailed = 10,
    AlreadyRunning = 11,
    ParentStillRunning = 12,
    SwapFailed = 20,
    HealthCheckFailed = 21,
    RollbackFailed = 22
}

public sealed record UpdateTransactionOptions(
    string TargetDirectory,
    string StagingDirectory,
    string BackupDirectory,
    string ApplicationExecutablePath,
    int ParentProcessId,
    ReleaseVersion Version,
    TimeSpan ParentWaitTimeout,
    TimeSpan HealthCheckTimeout);

public interface IUpdateTransactionRuntime
{
    bool IsProcessRunning(int processId);

    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);

    Task<bool> LaunchAndWaitForHealthyAsync(
        string executablePath,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}

public interface IUpdateMutex : IDisposable
{
    bool TryAcquire();
}

public sealed class UpdateTransaction
{
    private static readonly TimeSpan ParentPollInterval = TimeSpan.FromMilliseconds(100);
    private readonly IUpdateTransactionRuntime _runtime;
    private readonly IUpdateMutex _mutex;

    public UpdateTransaction(
        IUpdateTransactionRuntime? runtime = null,
        IUpdateMutex? mutex = null)
    {
        _runtime = runtime ?? new WindowsUpdateTransactionRuntime();
        _mutex = mutex ?? new NamedUpdateMutex();
    }

    public async Task<UpdateTransactionResult> ExecuteAsync(
        UpdateTransactionOptions options,
        CancellationToken cancellationToken = default)
    {
        if (!IsValid(options))
        {
            return UpdateTransactionResult.ValidationFailed;
        }

        using (_mutex)
        {
            if (!_mutex.TryAcquire())
            {
                return UpdateTransactionResult.AlreadyRunning;
            }

            if (!await WaitForParentExitAsync(options, cancellationToken))
            {
                return UpdateTransactionResult.ParentStillRunning;
            }

            var targetMovedToBackup = false;
            try
            {
                Directory.Move(options.TargetDirectory, options.BackupDirectory);
                targetMovedToBackup = true;
                Directory.Move(options.StagingDirectory, options.TargetDirectory);

                var liveExecutablePath = Path.Combine(
                    options.TargetDirectory,
                    Path.GetFileName(options.ApplicationExecutablePath));
                var healthy = await _runtime.LaunchAndWaitForHealthyAsync(
                    liveExecutablePath,
                    options.TargetDirectory,
                    options.HealthCheckTimeout,
                    cancellationToken);
                if (!healthy)
                {
                    return await RollbackAsync(options, UpdateTransactionResult.HealthCheckFailed, cancellationToken);
                }

                TryDeleteDirectory(options.BackupDirectory);
                return UpdateTransactionResult.Success;
            }
            catch (OperationCanceledException)
            {
                if (!targetMovedToBackup)
                {
                    return UpdateTransactionResult.SwapFailed;
                }

                return await RollbackAsync(options, UpdateTransactionResult.SwapFailed, CancellationToken.None);
            }
            catch
            {
                if (!targetMovedToBackup)
                {
                    return UpdateTransactionResult.SwapFailed;
                }

                return await RollbackAsync(options, UpdateTransactionResult.SwapFailed, CancellationToken.None);
            }
        }
    }

    private Task<UpdateTransactionResult> RollbackAsync(
        UpdateTransactionOptions options,
        UpdateTransactionResult failure,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(options.TargetDirectory))
            {
                Directory.Delete(options.TargetDirectory, recursive: true);
            }

            if (!Directory.Exists(options.BackupDirectory))
            {
                return Task.FromResult(UpdateTransactionResult.RollbackFailed);
            }

            Directory.Move(options.BackupDirectory, options.TargetDirectory);
            TryDeleteDirectory(options.StagingDirectory);
            return Task.FromResult(failure);
        }
        catch
        {
            return Task.FromResult(UpdateTransactionResult.RollbackFailed);
        }
    }

    private async Task<bool> WaitForParentExitAsync(
        UpdateTransactionOptions options,
        CancellationToken cancellationToken)
    {
        var timeout = options.ParentWaitTimeout <= TimeSpan.Zero
            ? TimeSpan.FromSeconds(30)
            : options.ParentWaitTimeout;
        var deadline = DateTime.UtcNow + timeout;
        while (_runtime.IsProcessRunning(options.ParentProcessId))
        {
            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            await _runtime.DelayAsync(ParentPollInterval, cancellationToken);
        }

        return true;
    }

    private static bool IsValid(UpdateTransactionOptions options)
    {
        if (options is null
            || options.Version is null
            || options.ParentProcessId <= 0
            || options.HealthCheckTimeout <= TimeSpan.Zero
            || !IsAbsolute(options.TargetDirectory)
            || !IsAbsolute(options.StagingDirectory)
            || !IsAbsolute(options.BackupDirectory)
            || !IsAbsolute(options.ApplicationExecutablePath))
        {
            return false;
        }

        try
        {
            var target = Path.GetFullPath(options.TargetDirectory);
            var staging = Path.GetFullPath(options.StagingDirectory);
            var backup = Path.GetFullPath(options.BackupDirectory);
            var executable = Path.GetFullPath(options.ApplicationExecutablePath);
            return !PathsEqual(target, staging)
                && !PathsEqual(target, backup)
                && !PathsEqual(staging, backup)
                && IsUnderDirectory(executable, target)
                && Directory.Exists(target)
                && Directory.Exists(staging)
                && !Directory.Exists(backup);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsAbsolute(string path) =>
        !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path);

    private static bool IsUnderDirectory(string candidate, string root)
    {
        var canonicalRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(canonicalRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(left.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), right.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }
}

public sealed class WindowsUpdateTransactionRuntime : IUpdateTransactionRuntime
{
    private static readonly TimeSpan HealthPollInterval = TimeSpan.FromMilliseconds(100);

    public bool IsProcessRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.Delay(delay, cancellationToken);

    public async Task<bool> LaunchAndWaitForHealthyAsync(
        string executablePath,
        string workingDirectory,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        Process? process = null;
        var healthy = false;
        try
        {
            process = Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null)
            {
                return false;
            }

            var healthWindow = timeout <= TimeSpan.Zero
                ? TimeSpan.FromSeconds(5)
                : timeout;
            var deadline = DateTime.UtcNow + healthWindow;
            while (true)
            {
                if (process.HasExited)
                {
                    healthy = process.ExitCode == 0;
                    return healthy;
                }

                var remaining = deadline - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero)
                {
                    healthy = true;
                    return true;
                }

                await Task.Delay(
                    remaining < HealthPollInterval ? remaining : HealthPollInterval,
                    cancellationToken);
            }
        }
        catch
        {
            return false;
        }
        finally
        {
            if (process is not null)
            {
                try
                {
                    if (!healthy && !process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit();
                    }
                }
                catch
                {
                }

                process.Dispose();
            }
        }
    }
}

public sealed class NamedUpdateMutex : IUpdateMutex
{
    private readonly Mutex _mutex;
    private bool _acquired;

    public NamedUpdateMutex()
    {
        _mutex = new Mutex(false, "Global\\9RouterPlus.Updater");
    }

    public bool TryAcquire()
    {
        try
        {
            _acquired = _mutex.WaitOne(TimeSpan.Zero);
            return _acquired;
        }
        catch (AbandonedMutexException)
        {
            _acquired = true;
            return true;
        }
    }

    public void Dispose()
    {
        if (_acquired)
        {
            _mutex.ReleaseMutex();
            _acquired = false;
        }

        _mutex.Dispose();
    }
}
