using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace RouterPlus.App.E2E;

/// <summary>
/// Manages RouterPlus process for live E2E tests that use real Chrome profiles and settings.
/// Does NOT enable harness mode - uses actual Chrome configuration and saved credentials.
/// </summary>
public sealed class LiveRouterPlusProcess : IAsyncDisposable
{
    private readonly Process _process;
    private readonly int _processId;
    private readonly UIA3Automation _automation;
    private readonly Application _application;
    private readonly Window _mainWindow;

    private LiveRouterPlusProcess(Process process, UIA3Automation automation, Application application, Window mainWindow)
    {
        _process = process;
        _processId = process.Id;
        _automation = automation;
        _application = application;
        _mainWindow = mainWindow;
    }

    public UIA3Automation Automation => _automation;
    public Window MainWindow => _mainWindow;

    public static async Task<LiveRouterPlusProcess> StartAsync(int timeoutSeconds = 30)
    {
        var appPath = GetApplicationPath();

        var startInfo = new ProcessStartInfo
        {
            FileName = appPath,
            UseShellExecute = false,
            // Explicitly clear harness environment variables to use real settings
            EnvironmentVariables =
            {
                ["ROUTERPLUS_HARNESS"] = string.Empty,
                ["ROUTERPLUS_HARNESS_ROOT"] = string.Empty
            }
        };

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start RouterPlus process");

        try
        {
            var automation = new UIA3Automation();
            var application = Application.Attach(process.Id);

            var timeoutAt = DateTime.UtcNow.AddSeconds(timeoutSeconds);
            Window? mainWindow = null;

            while (DateTime.UtcNow < timeoutAt)
            {
                try
                {
                    mainWindow = application.GetMainWindow(automation);
                    if (mainWindow != null && mainWindow.Title == "9Router Profile Tool")
                    {
                        break;
                    }
                }
                catch
                {
                    // Window not ready yet
                }

                await Task.Delay(100);
            }

            if (mainWindow == null)
            {
                throw new TimeoutException($"RouterPlus main window not found within {timeoutSeconds} seconds");
            }

            return new LiveRouterPlusProcess(process, automation, application, mainWindow);
        }
        catch
        {
            try { process.Kill(); } catch { /* Best effort */ }
            throw;
        }
    }

    private static string GetApplicationPath()
    {
        var projectRoot = FindProjectRoot();
        return Path.Combine(projectRoot, "src", "RouterPlus.App", "bin", "Debug", "net8.0-windows", "RouterPlus.exe");
    }

    private static string FindProjectRoot()
    {
        var directory = Directory.GetCurrentDirectory();
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory, "RouterPlus.sln")))
            {
                return directory;
            }
            directory = Directory.GetParent(directory)?.FullName;
        }
        throw new InvalidOperationException("Could not find project root (RouterPlus.sln)");
    }

    public async ValueTask DisposeAsync()
    {
        _application.Dispose();
        _automation.Dispose();

        try
        {
            var liveProcess = Process.GetProcessById(_processId);
            if (!liveProcess.HasExited)
            {
                liveProcess.Kill();
                await liveProcess.WaitForExitAsync();
            }
        }
        catch (ArgumentException)
        {
            // Process already exited
        }
    }
}
