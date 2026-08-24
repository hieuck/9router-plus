using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace RouterPlus.App.E2E;

public sealed class RouterPlusProcess : IAsyncDisposable
{
    private readonly UIA3Automation _automation;

    private RouterPlusProcess(
        Application application,
        UIA3Automation automation,
        Window mainWindow,
        TestEnvironment environment)
    {
        Application = application;
        _automation = automation;
        MainWindow = mainWindow;
        Environment = environment;
    }

    public Application Application { get; }
    public UIA3Automation Automation => _automation;
    public Window MainWindow { get; private set; }
    public TestEnvironment Environment { get; }

    public static async Task<RouterPlusProcess> StartAsync(TestEnvironment environment)
    {
        var executablePath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "RouterPlus.App", "bin", "Debug", "net8.0-windows", "RouterPlus.exe"));
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("Build the Debug RouterPlus app before starting the harness.", executablePath);
        }

        var startInfo = new ProcessStartInfo(executablePath)
        {
            WorkingDirectory = Path.GetDirectoryName(executablePath)!,
            UseShellExecute = false
        };
        startInfo.Environment["ROUTERPLUS_HARNESS"] = "1";
        startInfo.Environment["ROUTERPLUS_HARNESS_ROOT"] = environment.RootPath;
        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("RouterPlus process could not be started.");
        var application = Application.Attach(process);
        application.WaitWhileMainHandleIsMissing();

        var automation = new UIA3Automation();
        try
        {
            var mainWindow = application.GetMainWindow(automation)
                ?? throw new InvalidOperationException("RouterPlus main window was not found.");
            await WaitForTitleAsync(mainWindow, "9Router Profile Tool", TimeSpan.FromSeconds(10));
            return new RouterPlusProcess(application, automation, mainWindow, environment);
        }
        catch
        {
            automation.Dispose();
            application.Close();
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            Application.Close();
        }
        finally
        {
            _automation.Dispose();
        }
        return ValueTask.CompletedTask;
    }

    private static async Task WaitForTitleAsync(Window window, string title, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (string.Equals(window.Title, title, StringComparison.Ordinal))
            {
                return;
            }
            await Task.Delay(50);
        }

        throw new TimeoutException($"Window title did not become '{title}'. Actual: '{window.Title}'.");
    }
}
