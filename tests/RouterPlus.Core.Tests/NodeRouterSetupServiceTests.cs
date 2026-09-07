using RouterPlus.App.Setup;

namespace RouterPlus.Core.Tests;

public sealed class NodeRouterSetupServiceTests
{
    [Fact]
    public async Task DetectAsync_reports_node_npm_and_router_availability()
    {
        var runner = new FakeSetupProcessRunner(
            new Dictionary<string, SetupProcessResult>(StringComparer.OrdinalIgnoreCase)
            {
                ["node --version"] = SetupProcessResult.Success("v22.1.0"),
                ["npm --version"] = SetupProcessResult.Success("10.7.0"),
                ["9router --version"] = SetupProcessResult.Success("1.0.0")
            });
        var service = new NodeRouterSetupService(runner, new FakeSetupLinkLauncher());

        var status = await service.DetectAsync();

        Assert.True(status.NodeAvailable);
        Assert.True(status.NpmAvailable);
        Assert.True(status.RouterAvailable);
        Assert.Equal(new[] { "node --version", "npm --version", "9router --version" }, runner.Commands);
    }

    [Fact]
    public async Task DetectAsync_preserves_command_output_and_reports_unavailable_tools()
    {
        var runner = new FakeSetupProcessRunner(
            new Dictionary<string, SetupProcessResult>(StringComparer.OrdinalIgnoreCase)
            {
                ["node --version"] = SetupProcessResult.Success("v22.1.0"),
                ["npm --version"] = SetupProcessResult.Failure("npm missing"),
                ["9router --version"] = SetupProcessResult.Failure("router missing")
            });
        var service = new NodeRouterSetupService(runner, new FakeSetupLinkLauncher());

        var status = await service.DetectAsync();

        Assert.True(status.NodeAvailable);
        Assert.False(status.NpmAvailable);
        Assert.False(status.RouterAvailable);
        Assert.Equal("v22.1.0", status.Output);
        Assert.Equal(string.Empty, status.NpmOutput);
        Assert.Equal(string.Empty, status.RouterOutput);
    }

    [Fact]
    public async Task EnsureNodeAsync_returns_completed_without_installing_when_node_is_available()
    {
        var runner = new FakeSetupProcessRunner(
            new Dictionary<string, SetupProcessResult>(StringComparer.OrdinalIgnoreCase)
            {
                ["node --version"] = SetupProcessResult.Success("v22.1.0")
            });
        var service = new NodeRouterSetupService(runner, new FakeSetupLinkLauncher());

        var result = await service.EnsureNodeAsync();

        Assert.Equal(SetupActionStatus.Completed, result.Status);
        Assert.Single(runner.Commands);
        Assert.DoesNotContain("winget", runner.Commands);
    }

    [Fact]
    public async Task EnsureNodeAsync_returns_failed_when_winget_install_fails()
    {
        var runner = new FakeSetupProcessRunner(
            new Dictionary<string, SetupProcessResult>(StringComparer.OrdinalIgnoreCase)
            {
                ["node --version"] = SetupProcessResult.Failure("not found"),
                ["winget --version"] = SetupProcessResult.Success("v1.8.0"),
                ["winget install --id OpenJS.NodeJS.LTS --exact"] = SetupProcessResult.Failure("access denied")
            });
        var service = new NodeRouterSetupService(runner, new FakeSetupLinkLauncher());

        var result = await service.EnsureNodeAsync();

        Assert.Equal(SetupActionStatus.Failed, result.Status);
        Assert.Contains("Không thể cài Node.js", result.Message);
    }

    [Fact]
    public async Task EnsureNodeAsync_returns_failed_when_download_page_cannot_be_opened()
    {
        var runner = new FakeSetupProcessRunner(
            new Dictionary<string, SetupProcessResult>(StringComparer.OrdinalIgnoreCase)
            {
                ["node --version"] = SetupProcessResult.Failure("not found"),
                ["winget --version"] = SetupProcessResult.Failure("not found")
            });
        var service = new NodeRouterSetupService(runner, new ThrowingSetupLinkLauncher());

        var result = await service.EnsureNodeAsync();

        Assert.Equal(SetupActionStatus.Failed, result.Status);
        Assert.Contains("Không thể mở trang tải Node.js", result.Message);
    }

    [Fact]
    public async Task InstallRouterAsync_returns_failed_without_installing_when_npm_is_missing()
    {
        var runner = new FakeSetupProcessRunner(
            new Dictionary<string, SetupProcessResult>(StringComparer.OrdinalIgnoreCase)
            {
                ["npm --version"] = SetupProcessResult.Failure("not found")
            });
        var service = new NodeRouterSetupService(runner, new FakeSetupLinkLauncher());

        var result = await service.InstallRouterAsync();

        Assert.Equal(SetupActionStatus.Failed, result.Status);
        Assert.Single(runner.Commands);
    }

    [Fact]
    public async Task InstallRouterAsync_returns_failed_when_npm_install_fails()
    {
        var runner = new FakeSetupProcessRunner(
            new Dictionary<string, SetupProcessResult>(StringComparer.OrdinalIgnoreCase)
            {
                ["npm --version"] = SetupProcessResult.Success("10.7.0"),
                ["npm install --global 9router"] = SetupProcessResult.Failure("permission denied")
            });
        var service = new NodeRouterSetupService(runner, new FakeSetupLinkLauncher());

        var result = await service.InstallRouterAsync();

        Assert.Equal(SetupActionStatus.Failed, result.Status);
        Assert.Contains("Không thể cài 9Router", result.Message);
    }

    [Fact]
    public async Task LaunchRouterAsync_returns_failed_when_router_process_cannot_start()
    {
        var runner = new FakeSetupProcessRunner(
            new Dictionary<string, SetupProcessResult>(StringComparer.OrdinalIgnoreCase)
            {
                ["9router"] = SetupProcessResult.Failure("not found")
            });
        var service = new NodeRouterSetupService(runner, new FakeSetupLinkLauncher());

        var result = await service.LaunchRouterAsync();

        Assert.Equal(SetupActionStatus.Failed, result.Status);
        Assert.Equal("9router", runner.Commands.Single());
    }

    [Fact]
    public async Task EnsureNodeAsync_forwards_cancellation_token_to_process_runner()
    {
        var cancellation = new CancellationTokenSource();
        var runner = new CapturingSetupProcessRunner();
        var service = new NodeRouterSetupService(runner, new FakeSetupLinkLauncher());

        await service.EnsureNodeAsync(cancellation.Token);

        Assert.Equal(2, runner.Tokens.Count);
        Assert.All(runner.Tokens, token => Assert.Equal(cancellation.Token, token));
    }

    [Fact]
    public async Task EnsureNodeAsync_uses_winget_for_node_lts_when_node_is_missing()
    {
        var runner = new FakeSetupProcessRunner(
            new Dictionary<string, SetupProcessResult>(StringComparer.OrdinalIgnoreCase)
            {
                ["node --version"] = SetupProcessResult.Failure("not found"),
                ["winget --version"] = SetupProcessResult.Success("v1.8.0"),
                ["winget install --id OpenJS.NodeJS.LTS --exact"] = SetupProcessResult.Success("installed")
            });
        var service = new NodeRouterSetupService(runner, new FakeSetupLinkLauncher());

        var result = await service.EnsureNodeAsync();

        Assert.Equal(SetupActionStatus.Completed, result.Status);
        Assert.Contains("OpenJS.NodeJS.LTS", runner.Commands.Last());
    }

    [Fact]
    public async Task EnsureNodeAsync_opens_official_node_page_when_winget_is_missing()
    {
        var runner = new FakeSetupProcessRunner(
            new Dictionary<string, SetupProcessResult>(StringComparer.OrdinalIgnoreCase)
            {
                ["node --version"] = SetupProcessResult.Failure("not found"),
                ["winget --version"] = SetupProcessResult.Failure("not found")
            });
        var launcher = new FakeSetupLinkLauncher();
        var service = new NodeRouterSetupService(runner, launcher);

        var result = await service.EnsureNodeAsync();

        Assert.Equal(SetupActionStatus.ManualActionRequired, result.Status);
        Assert.Single(launcher.OpenedUris);
        Assert.Equal("nodejs.org", launcher.OpenedUris[0].Host);
    }

    [Fact]
    public async Task InstallRouterAsync_runs_npm_global_install_only_after_npm_is_available()
    {
        var runner = new FakeSetupProcessRunner(
            new Dictionary<string, SetupProcessResult>(StringComparer.OrdinalIgnoreCase)
            {
                ["npm --version"] = SetupProcessResult.Success("10.7.0"),
                ["npm install --global 9router"] = SetupProcessResult.Success("added 9router")
            });
        var service = new NodeRouterSetupService(runner, new FakeSetupLinkLauncher());

        var result = await service.InstallRouterAsync();

        Assert.Equal(SetupActionStatus.Completed, result.Status);
        Assert.Equal("npm install --global 9router", runner.Commands.Last());
    }

    [Fact]
    public async Task LaunchRouterAsync_runs_the_9router_command()
    {
        var runner = new FakeSetupProcessRunner(
            new Dictionary<string, SetupProcessResult>(StringComparer.OrdinalIgnoreCase)
            {
                ["9router"] = SetupProcessResult.Started()
            });
        var service = new NodeRouterSetupService(runner, new FakeSetupLinkLauncher());

        var result = await service.LaunchRouterAsync();

        Assert.Equal(SetupActionStatus.Completed, result.Status);
        Assert.Equal("9router", runner.Commands.Last());
    }

    [Fact]
    public async Task Windows_runner_uses_cmd_shims_for_npm_and_9router()
    {
        var executor = new FakeCommandExecutor();
        var runner = new WindowsSetupProcessRunner(executor);

        await runner.RunAsync("npm", "--version");
        await runner.RunAsync("9router", "--version");

        Assert.Equal(new[] { "cmd.exe", "cmd.exe" }, executor.ExecutedFileNames);
        Assert.Contains("npm.cmd", executor.Arguments[0]);
        Assert.Contains("9router.cmd", executor.Arguments[1]);
        Assert.All(executor.CaptureOutput, Assert.True);
    }

    [Fact]
    public async Task Windows_runner_does_not_capture_output_only_when_launching_router()
    {
        var executor = new FakeCommandExecutor();
        var runner = new WindowsSetupProcessRunner(executor);

        await runner.RunAsync("9router", "--version");
        await runner.RunAsync("9router", string.Empty);

        Assert.Equal(new[] { true, false }, executor.CaptureOutput);
    }

    private sealed class FakeCommandExecutor : ISetupCommandExecutor
    {
        public List<string> ExecutedFileNames { get; } = new();
        public List<string> Arguments { get; } = new();
        public List<bool> CaptureOutput { get; } = new();

        public Task<SetupProcessResult> ExecuteAsync(
            string fileName,
            string arguments,
            bool useShellExecute,
            bool captureOutput,
            CancellationToken cancellationToken = default)
        {
            ExecutedFileNames.Add(fileName);
            Arguments.Add(arguments);
            CaptureOutput.Add(captureOutput);
            return Task.FromResult(SetupProcessResult.Success("ok"));
        }
    }

    private sealed class FakeSetupProcessRunner : ISetupProcessRunner
    {
        private readonly IReadOnlyDictionary<string, SetupProcessResult> _results;

        public FakeSetupProcessRunner(IReadOnlyDictionary<string, SetupProcessResult> results)
        {
            _results = results;
        }

        public List<string> Commands { get; } = new();

        public Task<SetupProcessResult> RunAsync(string fileName, string arguments, CancellationToken cancellationToken = default)
        {
            var command = string.IsNullOrWhiteSpace(arguments) ? fileName : $"{fileName} {arguments}";
            Commands.Add(command);
            return Task.FromResult(_results.TryGetValue(command, out var result)
                ? result
                : SetupProcessResult.Failure($"No fake result for {command}"));
        }
    }

    private sealed class FakeSetupLinkLauncher : ISetupLinkLauncher
    {
        public List<Uri> OpenedUris { get; } = new();

        public void Open(Uri uri) => OpenedUris.Add(uri);
    }

    private sealed class ThrowingSetupLinkLauncher : ISetupLinkLauncher
    {
        public void Open(Uri uri) => throw new InvalidOperationException("browser unavailable");
    }

    private sealed class CapturingSetupProcessRunner : ISetupProcessRunner
    {
        public List<CancellationToken> Tokens { get; } = new();

        public Task<SetupProcessResult> RunAsync(
            string fileName,
            string arguments,
            CancellationToken cancellationToken = default)
        {
            Tokens.Add(cancellationToken);
            return Task.FromResult(SetupProcessResult.Failure("not available"));
        }
    }
}
