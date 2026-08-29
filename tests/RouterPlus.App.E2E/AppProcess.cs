using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace RouterPlus.App.E2E;

/// <summary>
/// Manages RouterPlus.exe process lifecycle for E2E tests.
/// </summary>
public sealed class AppProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Application _application;
    private readonly UIA3Automation _automation;

    private AppProcess(Process process, Application application, UIA3Automation automation, Window mainWindow)
    {
        _process = process;
        _application = application;
        _automation = automation;
        MainWindow = mainWindow;
    }

    public Window MainWindow { get; }

    public AutomationElement Desktop => _automation.GetDesktop();

    public static async Task<AppProcess> StartAsync(TestEnvironment environment)
    {
        var exePath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "RouterPlus.App", "bin", "Debug", "net8.0-windows", "RouterPlus.exe"));

        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException($"RouterPlus.exe not found. Build the app first: {exePath}");
        }

        var startInfo = new ProcessStartInfo(exePath)
        {
            WorkingDirectory = Path.GetDirectoryName(exePath)!,
            UseShellExecute = false
        };
        startInfo.Environment["ROUTERPLUS_HARNESS"] = "1";
        startInfo.Environment["ROUTERPLUS_HARNESS_ROOT"] = environment.RootPath;

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start RouterPlus.exe");

        var application = Application.Attach(process);
        application.WaitWhileMainHandleIsMissing(TimeSpan.FromSeconds(10));

        var automation = new UIA3Automation();
        try
        {
            var mainWindow = application.GetMainWindow(automation)
                ?? throw new InvalidOperationException("Main window not found");

            await WaitForWindowTitleAsync(mainWindow, "9Router Profile Tool", TimeSpan.FromSeconds(10));

            return new AppProcess(process, application, automation, mainWindow);
        }
        catch
        {
            automation.Dispose();
            application.Close();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Task.Yield();

        try
        {
            _application.Close();
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(3000);
            }
        }
        catch (InvalidOperationException)
        {
            // Process already exited
        }
        finally
        {
            _automation.Dispose();
            _process.Dispose();
        }
    }

    private static async Task WaitForWindowTitleAsync(Window window, string expectedTitle, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (string.Equals(window.Title, expectedTitle, StringComparison.Ordinal))
            {
                return;
            }
            await Task.Delay(50);
        }

        throw new TimeoutException($"Window title did not become '{expectedTitle}'. Actual: '{window.Title}'");
    }
}
