using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using RouterPlus.App.Setup;
using RouterPlus.App.Views;
using RouterPlus.Infrastructure.Storage;

namespace RouterPlus.App.Tests;

public sealed class WelcomeWizardWindowTests
{
    [Fact]
    public void Constructor_UsesDefaultDashboardUrl_AndExposesEmptyOptionalInputs()
    {
        var result = OnSta(() =>
        {
            var window = CreateWindow();

            return (window.DashboardUrl, window.DashboardPassword, window.ChromeExecutablePath, window.ChromeUserDataDirectory);
        });

        Assert.Equal("http://localhost:20128", result.DashboardUrl);
        Assert.Empty(result.DashboardPassword);
        Assert.Empty(result.ChromeExecutablePath);
        Assert.Empty(result.ChromeUserDataDirectory);
    }

    [Fact]
    public void CheckRouterAsync_ShowsDefaultNotFoundState_WhenDashboardUrlIsBlank()
    {
        var result = OnSta(async () =>
        {
            var window = CreateWindow();
            GetField<System.Windows.Controls.TextBox>(window, "DashboardUrlTextBox").Text = "  ";

            await InvokeAsync(window, "CheckRouterAsync");

            return (
                GetField<System.Windows.Controls.Border>(window, "RouterNotFoundPanel").Visibility,
                GetField<System.Windows.Controls.Border>(window, "RouterFoundPanel").Visibility,
                GetField<System.Windows.Controls.TextBlock>(window, "RouterNotFoundMessageText").Text,
                GetField<System.Windows.Controls.Button>(window, "SaveButton").IsEnabled);
        });

        Assert.Equal(Visibility.Visible, result.Item1);
        Assert.Equal(Visibility.Collapsed, result.Item2);
        Assert.Equal("9Router chưa chạy hoặc URL không đúng.", result.Item3);
        Assert.False(result.Item4);
    }

    [Fact]
    public void CheckRouterAsync_ShowsFoundState_WhenProtectedApiRespondsSuccessfully()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var result = OnSta(async () =>
        {
            var window = CreateWindow(new HttpClient(handler));

            await InvokeAsync(window, "CheckRouterAsync");

            return (
                GetField<System.Windows.Controls.Border>(window, "RouterFoundPanel").Visibility,
                GetField<System.Windows.Controls.Border>(window, "RouterNotFoundPanel").Visibility,
                GetField<System.Windows.Controls.StackPanel>(window, "RouterCheckingPanel").Visibility,
                GetField<System.Windows.Controls.Button>(window, "CheckRouterButton").IsEnabled,
                handler.Requests.Single().RequestUri!.AbsoluteUri);
        });

        Assert.Equal(Visibility.Visible, result.Item1);
        Assert.Equal(Visibility.Collapsed, result.Item2);
        Assert.Equal(Visibility.Collapsed, result.Item3);
        Assert.True(result.Item4);
        Assert.Equal("http://localhost:20128/api/providers", result.Item5);
    }

    [Fact]
    public void CheckRouterAsync_ExplainsAuthenticationRequirement_WhenProtectedApiReturnsUnauthorized()
    {
        var handler = new RecordingHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var result = OnSta(async () =>
        {
            var window = CreateWindow(new HttpClient(handler));

            await InvokeAsync(window, "CheckRouterAsync");

            return (
                GetField<System.Windows.Controls.Border>(window, "RouterNotFoundPanel").Visibility,
                GetField<System.Windows.Controls.TextBlock>(window, "RouterNotFoundMessageText").Text,
                GetField<System.Windows.Controls.Button>(window, "CheckRouterButton").IsEnabled);
        });

        Assert.Equal(Visibility.Visible, result.Item1);
        Assert.Equal("9Router yêu cầu mật khẩu. Vui lòng nhập mật khẩu và thử lại.", result.Item2);
        Assert.True(result.Item3);
    }

    [Fact]
    public void RefreshSetupStatusAsync_MapsDetectedAvailabilityToActionVisibility()
    {
        var runner = new RecordingSetupProcessRunner(new Dictionary<string, SetupProcessResult>
        {
            ["node --version"] = SetupProcessResult.Failure("missing"),
            ["npm --version"] = SetupProcessResult.Success("10.0.0"),
            ["9router --version"] = SetupProcessResult.Success("1.0.0")
        });
        var setupService = new NodeRouterSetupService(runner, new NoOpSetupLinkLauncher());

        var result = OnSta(async () =>
        {
            var window = CreateWindow(nodeRouterSetupService: setupService);

            await InvokeAsync(window, "RefreshSetupStatusAsync");

            return (
                GetField<System.Windows.Controls.TextBlock>(window, "SetupStatusText").Text,
                GetField<System.Windows.Controls.StackPanel>(window, "NodeSetupActionsPanel").Visibility,
                GetField<System.Windows.Controls.Button>(window, "InstallNodeButton").Visibility,
                GetField<System.Windows.Controls.Button>(window, "InstallRouterButton").Visibility,
                GetField<System.Windows.Controls.Button>(window, "LaunchRouterButton").Visibility,
                runner.Commands.ToArray());
        });

        Assert.Equal("Node.js: ✗    npm: ✓    9Router: ✓", result.Item1);
        Assert.Equal(Visibility.Visible, result.Item2);
        Assert.Equal(Visibility.Visible, result.Item3);
        Assert.Equal(Visibility.Collapsed, result.Item4);
        Assert.Equal(Visibility.Visible, result.Item5);
        Assert.Equal(new[] { "node --version", "npm --version", "9router --version" }, result.Item6);
    }

    [Fact]
    public void ValidateChromeConfig_ReportsBothPathsValid_WhenFilesAndDirectoryExist()
    {
        var executablePath = Path.GetTempFileName();
        var userDataPath = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var result = OnSta(() =>
            {
                var window = CreateWindow();
                GetField<System.Windows.Controls.TextBox>(window, "ChromeExeTextBox").Text = executablePath;
                GetField<System.Windows.Controls.TextBox>(window, "ChromeUserDataTextBox").Text = userDataPath;

                Invoke(window, "ValidateChromeConfig");

                return (
                    GetField<System.Windows.Controls.TextBlock>(window, "ChromeExeStatus").Text,
                    GetField<System.Windows.Controls.TextBlock>(window, "ChromeUserDataStatus").Text,
                    GetField<System.Windows.Controls.Border>(window, "ChromeValidPanel").Visibility);
            });

            Assert.Equal("✓ Hợp lệ", result.Item1);
            Assert.Equal("✓ Hợp lệ", result.Item2);
            Assert.Equal(Visibility.Visible, result.Item3);
        }
        finally
        {
            File.Delete(executablePath);
            Directory.Delete(userDataPath);
        }
    }

    private static WelcomeWizardWindow CreateWindow(
        HttpClient? httpClient = null,
        NodeRouterSetupService? nodeRouterSetupService = null)
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"router-plus-wizard-{Guid.NewGuid():N}.json");
        return new WelcomeWizardWindow(
            new SettingsStore(settingsPath),
            httpClient,
            nodeRouterSetupService ?? new NodeRouterSetupService(new NoOpSetupProcessRunner(), new NoOpSetupLinkLauncher()));
    }

    private static T GetField<T>(WelcomeWizardWindow window, string fieldName)
    {
        return (T)typeof(WelcomeWizardWindow)
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window)!;
    }

    private static void Invoke(WelcomeWizardWindow window, string methodName)
    {
        typeof(WelcomeWizardWindow)
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, null);
    }

    private static Task InvokeAsync(WelcomeWizardWindow window, string methodName)
    {
        return (Task)typeof(WelcomeWizardWindow)
            .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, null)!;
    }

    private static T OnSta<T>(Func<T> action)
    {
        T? result = default;
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception caught)
            {
                exception = caught;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
        {
            throw new AggregateException(exception);
        }

        return result!;
    }

    private static T OnSta<T>(Func<Task<T>> action)
    {
        T? result = default;
        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                var dispatcher = Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                var task = action();
                if (!task.IsCompleted)
                {
                    var frame = new DispatcherFrame();
                    _ = task.ContinueWith(
                        _ => dispatcher.BeginInvoke(
                            DispatcherPriority.Send,
                            new Action(() => frame.Continue = false)),
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                    Dispatcher.PushFrame(frame);
                }

                result = task.GetAwaiter().GetResult();
            }
            catch (Exception caught)
            {
                exception = caught;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception is not null)
        {
            throw new AggregateException(exception);
        }

        return result!;
    }

    private sealed class RecordingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class RecordingSetupProcessRunner(IReadOnlyDictionary<string, SetupProcessResult> results)
        : ISetupProcessRunner
    {
        public List<string> Commands { get; } = new();

        public Task<SetupProcessResult> RunAsync(string fileName, string arguments, CancellationToken cancellationToken = default)
        {
            var command = string.IsNullOrWhiteSpace(arguments) ? fileName : $"{fileName} {arguments}";
            Commands.Add(command);
            return Task.FromResult(results[command]);
        }
    }

    private sealed class NoOpSetupProcessRunner : ISetupProcessRunner
    {
        public Task<SetupProcessResult> RunAsync(string fileName, string arguments, CancellationToken cancellationToken = default) =>
            Task.FromResult(SetupProcessResult.Failure("not configured"));
    }

    private sealed class NoOpSetupLinkLauncher : ISetupLinkLauncher
    {
        public void Open(Uri uri)
        {
        }
    }
}
