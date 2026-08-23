using System.Diagnostics;

namespace RouterPlus.App.Setup;

public sealed class WindowsSetupProcessRunner : ISetupProcessRunner
{
    private readonly ISetupCommandExecutor _executor;

    public WindowsSetupProcessRunner(ISetupCommandExecutor? executor = null)
    {
        _executor = executor ?? new SetupCommandExecutor();
    }

    public Task<SetupProcessResult> RunAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken = default)
    {
        var launchRouter = string.Equals(fileName, "9router", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(arguments);
        if (OperatingSystem.IsWindows()
            && (fileName.Equals("npm", StringComparison.OrdinalIgnoreCase)
                || fileName.Equals("9router", StringComparison.OrdinalIgnoreCase)))
        {
            var command = string.IsNullOrWhiteSpace(arguments)
                ? $"{fileName}.cmd"
                : $"{fileName}.cmd {arguments}";
            return _executor.ExecuteAsync(
                "cmd.exe",
                $"/d /s /c \"{command}\"",
                useShellExecute: launchRouter,
                captureOutput: !launchRouter,
                cancellationToken);
        }

        return _executor.ExecuteAsync(
            fileName,
            arguments,
            useShellExecute: launchRouter,
            captureOutput: !launchRouter,
            cancellationToken);
    }
}

public interface ISetupCommandExecutor
{
    Task<SetupProcessResult> ExecuteAsync(
        string fileName,
        string arguments,
        bool useShellExecute,
        bool captureOutput,
        CancellationToken cancellationToken = default);
}

internal sealed class SetupCommandExecutor : ISetupCommandExecutor
{
    public async Task<SetupProcessResult> ExecuteAsync(
        string fileName,
        string arguments,
        bool useShellExecute,
        bool captureOutput,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = useShellExecute,
                    CreateNoWindow = !useShellExecute,
                    RedirectStandardOutput = captureOutput,
                    RedirectStandardError = captureOutput
                }
            };
            process.Start();

            if (!captureOutput)
            {
                return SetupProcessResult.Started();
            }

            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = await outputTask;
            var error = await errorTask;
            return process.ExitCode == 0
                ? SetupProcessResult.Success(output.Trim())
                : SetupProcessResult.Failure(error.Trim());
        }
        catch (Exception exception)
        {
            return SetupProcessResult.Failure(exception.Message);
        }
    }
}
