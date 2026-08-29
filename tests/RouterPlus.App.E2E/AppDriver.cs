using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;

namespace RouterPlus.App.E2E;

/// <summary>
/// Minimal UI automation helpers for RouterPlus main window.
/// </summary>
public sealed class AppDriver
{
    private readonly AppProcess _app;

    public AppDriver(AppProcess app)
    {
        _app = app;
    }

    public AutomationElement FindProfileList()
    {
        return Retry.WhileNull(
            () => _app.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("ProfileList")),
            TimeSpan.FromSeconds(5)).Result
            ?? throw new InvalidOperationException("ProfileList not found");
    }

    public AutomationElement? TryFindProfile(string profileName)
    {
        var list = FindProfileList();
        return Retry.WhileNull(
            () => list.FindFirstDescendant(cf => cf.ByName(profileName)),
            TimeSpan.FromSeconds(2),
            throwOnTimeout: false).Result;
    }

    public void ClickProfile(string profileName)
    {
        var profile = TryFindProfile(profileName)
            ?? throw new InvalidOperationException($"Profile '{profileName}' not found");
        profile.Click();
    }

    public AutomationElement? TryFindDialog(string titleSubstring, TimeSpan timeout)
    {
        return Retry.WhileNull(
            () =>
            {
                var windows = _app.MainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Window));
                return windows.FirstOrDefault(w =>
                {
                    try
                    {
                        return w.Name.Contains(titleSubstring, StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        return false;
                    }
                });
            },
            timeout,
            throwOnTimeout: false).Result;
    }

    public AutomationElement? TryFindButton(AutomationElement container, string buttonText)
    {
        return Retry.WhileNull(
            () => container.FindFirstDescendant(cf =>
                cf.ByControlType(ControlType.Button).And(cf.ByName(buttonText))),
            TimeSpan.FromSeconds(2),
            throwOnTimeout: false).Result;
    }
}
