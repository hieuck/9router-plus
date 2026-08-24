using System.Diagnostics;
using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;

namespace RouterPlus.App.E2E;

public sealed class MainWindowDriver : IDisposable
{
    private readonly RouterPlusProcess _process;

    public MainWindowDriver(RouterPlusProcess process)
    {
        _process = process;
    }

    public AutomationElement FindProfileList()
    {
        return Retry.WhileNull(
            () => _process.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("ProfileList")),
            TimeSpan.FromSeconds(5)).Result
            ?? throw new InvalidOperationException("ProfileList was not found.");
    }

    public AutomationElement FindProfileItem(string profileName)
    {
        var list = FindProfileList();
        return Retry.WhileNull(
            () => list.FindFirstDescendant(cf => cf.ByName(profileName)),
            TimeSpan.FromSeconds(5)).Result
            ?? throw new InvalidOperationException($"Profile item '{profileName}' was not found.");
    }

    public void RightClickProfile(string profileName)
    {
        DismissContextMenu();
        var item = FindProfileItem(profileName);
        var bounds = item.BoundingRectangle;
        if (bounds.IsEmpty)
        {
            throw new InvalidOperationException($"Profile item '{profileName}' has no visible bounds.");
        }

        _process.MainWindow.Focus();
        Mouse.MoveTo(new Point((int)bounds.Center().X, (int)bounds.Center().Y));
        Mouse.Click(MouseButton.Right);
    }

    public TimeSpan WaitForContextMenu(TimeSpan timeout)
    {
        var started = Stopwatch.StartNew();
        var result = Retry.WhileNull(
            () => FindDesktopElement("Mở thư mục profile"),
            timeout,
            throwOnTimeout: false);
        if (result.Result is null)
        {
            CaptureFailure("context-menu-timeout");
            throw new TimeoutException("The profile context menu did not appear.");
        }

        return started.Elapsed;
    }

    public bool ContextMenuContains(string header) => FindDesktopElement(header) is not null;

    public void DismissContextMenu()
    {
        Keyboard.Press(VirtualKeyShort.ESC);
        Retry.WhileTrue(
            () => FindDesktopElement("Mở thư mục profile") is not null,
            TimeSpan.FromSeconds(2),
            throwOnTimeout: false);
    }

    public void CaptureFailure(string label)
    {
        Directory.CreateDirectory(_process.Environment.ArtifactPath);
        var screenshotPath = Path.Combine(_process.Environment.ArtifactPath, $"{label}.png");
        _process.MainWindow.Capture().Save(screenshotPath);
        File.WriteAllText(
            Path.Combine(_process.Environment.ArtifactPath, $"{label}.tree.txt"),
            _process.Automation.GetDesktop().AsTree().ToString());
    }

    public void Dispose()
    {
        DismissContextMenu();
    }

    private AutomationElement? FindDesktopElement(string name)
    {
        var menu = _process.Automation.GetDesktop().FindFirstDescendant(cf => cf.Menu());
        return menu?.FindFirstDescendant(cf => cf.ByName(name));
    }
}
