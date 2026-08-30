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
        _app.Instrumentation.Record("CLICK_PROFILE", profileName);
        profile.Click();
    }

    public string ReadSelectedProfileName()
    {
        var element = _app.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("SelectedProfileName"))
            ?? throw new InvalidOperationException("Selected profile label not found");
        return _app.Instrumentation.ReadText(element, "selected-profile");
    }

    public bool ReadProfileSelectionState(string profileName)
    {
        var profile = TryFindProfile(profileName)
            ?? throw new InvalidOperationException($"Profile '{profileName}' not found");
        return _app.Instrumentation.ReadSelectedState(profile);
    }

    public IReadOnlyList<string> ReadVisibleProfileNames() =>
        _app.Instrumentation.ReadVisibleProfileNames(FindProfileList());

    public void SetProfileSearchText(string value)
    {
        var searchBox = _app.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("ProfileSearchTextBox"))
            ?? throw new InvalidOperationException("Profile search box not found");
        _app.Instrumentation.Record("SET_PROFILE_SEARCH", $"length={value.Length}");
        searchBox.AsTextBox().Text = value;
    }

    public void EnableMultiSelectMode()
    {
        var button = _app.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("ToggleMultiSelectButton"))
            ?? throw new InvalidOperationException("Multi-select button not found");
        _app.Instrumentation.Record("CLICK_MULTI_SELECT");
        button.Click();
    }

    public void ClickSelectAll()
    {
        var button = _app.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("SelectAllButton"))
            ?? throw new InvalidOperationException("Select-all button not found");
        _app.Instrumentation.Record("CLICK_SELECT_ALL");
        button.Click();
    }

    public IReadOnlyList<bool> ReadProfileCheckboxStates()
    {
        var states = FindProfileList()
            .FindAllDescendants(cf => cf.ByControlType(ControlType.CheckBox))
            .Select(_app.Instrumentation.ReadToggleState)
            .ToArray();
        return states;
    }

    public void OpenSettings()
    {
        var button = _app.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("SettingsToggleButton"))
            ?? throw new InvalidOperationException("Settings toggle not found");
        _app.Instrumentation.Record("OPEN_SETTINGS");
        if (button.Patterns.Toggle.Pattern?.ToggleState != FlaUI.Core.Definitions.ToggleState.On)
        {
            button.Click();
        }
    }

    public void SetDashboardUrl(string value)
    {
        var textBox = _app.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("DashboardUrlTextBox"))
            ?? throw new InvalidOperationException("Dashboard URL text box not found");
        _app.Instrumentation.Record("SET_DASHBOARD_URL", $"length={value.Length}");
        textBox.AsTextBox().Text = value;
    }

    public string ReadDashboardUrl()
    {
        var textBox = _app.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("DashboardUrlTextBox"))
            ?? throw new InvalidOperationException("Dashboard URL text box not found");
        return _app.Instrumentation.ReadText(textBox, "dashboard-url");
    }

    public string ReadSettingsStatus()
    {
        var status = _app.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("SettingsStatusText"))
            ?? throw new InvalidOperationException("Settings status not found");
        return _app.Instrumentation.ReadText(status, "settings-status");
    }

    public bool IsSaveSettingsEnabled()
    {
        var button = _app.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("SaveSettingsButton"))
            ?? throw new InvalidOperationException("Save settings button not found");
        return _app.Instrumentation.ReadEnabled(button, "save-settings");
    }

    public void SaveSettings()
    {
        var button = _app.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("SaveSettingsButton"))
            ?? throw new InvalidOperationException("Save settings button not found");
        _app.Instrumentation.Record("CLICK_SAVE_SETTINGS");
        button.Click();
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
