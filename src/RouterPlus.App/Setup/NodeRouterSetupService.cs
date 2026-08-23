using System.Diagnostics;

namespace RouterPlus.App.Setup;

public sealed class NodeRouterSetupService
{
    public static readonly Uri NodeDownloadUri = new("https://nodejs.org/en/download");

    private readonly ISetupProcessRunner _processRunner;
    private readonly ISetupLinkLauncher _linkLauncher;

    public NodeRouterSetupService(
        ISetupProcessRunner? processRunner = null,
        ISetupLinkLauncher? linkLauncher = null)
    {
        _processRunner = processRunner ?? new WindowsSetupProcessRunner();
        _linkLauncher = linkLauncher ?? new SetupLinkLauncher();
    }

    public async Task<NodeRouterSetupStatus> DetectAsync(CancellationToken cancellationToken = default)
    {
        var node = await _processRunner.RunAsync("node", "--version", cancellationToken);
        var npm = await _processRunner.RunAsync("npm", "--version", cancellationToken);
        var router = await _processRunner.RunAsync("9router", "--version", cancellationToken);

        return new NodeRouterSetupStatus(
            node.IsSuccess,
            npm.IsSuccess,
            router.IsSuccess,
            node.Output,
            npm.Output,
            router.Output);
    }

    public async Task<SetupActionResult> EnsureNodeAsync(CancellationToken cancellationToken = default)
    {
        var node = await _processRunner.RunAsync("node", "--version", cancellationToken);
        if (node.IsSuccess)
        {
            return SetupActionResult.Completed("Node.js đã được cài đặt.");
        }

        var winget = await _processRunner.RunAsync("winget", "--version", cancellationToken);
        if (!winget.IsSuccess)
        {
            try
            {
                _linkLauncher.Open(NodeDownloadUri);
                return SetupActionResult.ManualActionRequired(
                    "Máy chưa có Node.js hoặc WinGet. Đã mở trang tải Node.js chính thức.");
            }
            catch (Exception exception)
            {
                return SetupActionResult.Failed($"Không thể mở trang tải Node.js: {exception.Message}");
            }
        }

        var install = await _processRunner.RunAsync(
            "winget",
            "install --id OpenJS.NodeJS.LTS --exact",
            cancellationToken);
        return install.IsSuccess
            ? SetupActionResult.Completed("Đã cài Node.js LTS bằng WinGet. Hãy kiểm tra lại Node.js nếu PATH vừa được cập nhật.")
            : SetupActionResult.Failed("Không thể cài Node.js LTS bằng WinGet.");
    }

    public async Task<SetupActionResult> InstallRouterAsync(CancellationToken cancellationToken = default)
    {
        var npm = await _processRunner.RunAsync("npm", "--version", cancellationToken);
        if (!npm.IsSuccess)
        {
            return SetupActionResult.Failed("Chưa tìm thấy npm. Hãy cài Node.js trước rồi thử lại.");
        }

        var install = await _processRunner.RunAsync(
            "npm",
            "install --global 9router",
            cancellationToken);
        return install.IsSuccess
            ? SetupActionResult.Completed("Đã cài 9Router.")
            : SetupActionResult.Failed("Không thể cài 9Router bằng npm.");
    }

    public async Task<SetupActionResult> LaunchRouterAsync(CancellationToken cancellationToken = default)
    {
        var result = await _processRunner.RunAsync("9router", string.Empty, cancellationToken);
        return result.IsSuccess
            ? SetupActionResult.Completed("Đã gửi lệnh khởi chạy 9Router. Hãy nhấn kiểm tra lại để xác nhận dashboard đã sẵn sàng.")
            : SetupActionResult.Failed("Không thể khởi chạy 9Router.");
    }
}

public sealed record NodeRouterSetupStatus(
    bool NodeAvailable,
    bool NpmAvailable,
    bool RouterAvailable,
    string Output,
    string NpmOutput,
    string RouterOutput);

public enum SetupActionStatus
{
    Completed,
    ManualActionRequired,
    Failed
}

public sealed record SetupActionResult(SetupActionStatus Status, string Message)
{
    public static SetupActionResult Completed(string message) => new(SetupActionStatus.Completed, message);

    public static SetupActionResult ManualActionRequired(string message) =>
        new(SetupActionStatus.ManualActionRequired, message);

    public static SetupActionResult Failed(string message) => new(SetupActionStatus.Failed, message);
}

public sealed record SetupProcessResult(
    bool IsSuccess,
    string Output,
    string Error,
    bool ProcessStarted = false)
{
    public static SetupProcessResult Success(string output) => new(true, output, string.Empty);

    public static SetupProcessResult Started() => new(true, string.Empty, string.Empty, true);

    public static SetupProcessResult Failure(string error) => new(false, string.Empty, error);
}

public interface ISetupProcessRunner
{
    Task<SetupProcessResult> RunAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken = default);
}

public interface ISetupLinkLauncher
{
    void Open(Uri uri);
}

internal sealed class SetupProcessRunner : ISetupProcessRunner
{
    public async Task<SetupProcessResult> RunAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken = default)
    {
        if (string.Equals(fileName, "9router", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = true,
                    CreateNoWindow = false
                });
                return process is null
                    ? SetupProcessResult.Failure("Không thể tạo process 9Router.")
                    : SetupProcessResult.Started();
            }
            catch (Exception exception)
            {
                return SetupProcessResult.Failure(exception.Message);
            }
        }

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            process.Start();
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

internal sealed class SetupLinkLauncher : ISetupLinkLauncher
{
    public void Open(Uri uri)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = uri.AbsoluteUri,
            UseShellExecute = true
        });
    }
}
