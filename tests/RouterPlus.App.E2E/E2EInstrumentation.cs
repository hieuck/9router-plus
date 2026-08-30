using System.Text;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace RouterPlus.App.E2E;

internal sealed class E2EInstrumentation
{
    private readonly string _rootPath;
    private readonly object _sync = new();
    private readonly List<string> _events = [];

    public E2EInstrumentation(string rootPath)
    {
        _rootPath = rootPath;
    }

    public void Record(string action, string? details = null)
    {
        var suffix = string.IsNullOrWhiteSpace(details) ? string.Empty : $" | {details}";
        lock (_sync)
        {
            _events.Add($"[{DateTimeOffset.UtcNow:O}] {action}{suffix}");
        }
    }

    public string ReadWindowTitle(Window window)
    {
        var title = window.Title;
        Record("READ_WINDOW_TITLE", title);
        return title;
    }

    public IReadOnlyList<string> ReadVisibleProfileNames(AutomationElement profileList)
    {
        var names = profileList.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem))
            .Select(item => item.FindAllDescendants(cf => cf.ByControlType(ControlType.Text))
                .Select(text => text.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name) && !int.TryParse(name, out _))
                .OrderByDescending(name => name.Length)
                .FirstOrDefault())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Cast<string>()
            .ToArray();
        Record("READ_VISIBLE_PROFILES", string.Join(",", names));
        return names;
    }

    public bool ReadSelectedState(AutomationElement element)
    {
        var target = element;
        while (target is not null && target.ControlType != ControlType.ListItem)
        {
            target = target.Parent;
        }

        if (target is null)
        {
            throw new InvalidOperationException($"List item container not found for '{element.Name}'");
        }

        var isSelected = target.Patterns.SelectionItem.Pattern?.IsSelected ?? false;
        Record("READ_SELECTED_STATE", $"{target.Name}={isSelected}");
        return isSelected;
    }

    public bool ReadToggleState(AutomationElement element)
    {
        var state = element.Patterns.Toggle.Pattern?.ToggleState;
        var isOn = state == ToggleState.On;
        Record("READ_TOGGLE_STATE", $"{element.Name}={isOn}");
        return isOn;
    }

    public string ReadText(AutomationElement element, string label)
    {
        var value = element.Name;
        Record("READ_TEXT", $"{label}={value}");
        return value;
    }

    public bool ReadEnabled(AutomationElement element, string label)
    {
        var enabled = element.IsEnabled;
        Record("READ_ENABLED", $"{label}={enabled}");
        return enabled;
    }

    public async Task SaveFailureSnapshotAsync(AppProcess app, string testName)
    {
        var safeName = string.Concat(testName.Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-'));
        var snapshotPath = Path.Combine(_rootPath, $"{safeName}-ui-tree.txt");
        var logPath = Path.Combine(_rootPath, $"{safeName}-actions.log");
        var processPath = Path.Combine(_rootPath, $"{safeName}-process.txt");

        await File.WriteAllTextAsync(snapshotPath, BuildUiSnapshot(app.Desktop));
        string[] events;
        lock (_sync)
        {
            events = _events.ToArray();
        }

        await File.WriteAllLinesAsync(logPath, events);
        var processState = $"Id={app.ProcessId}{Environment.NewLine}HasExited={app.HasExited}";
        await File.WriteAllTextAsync(processPath, processState);
    }

    private static string BuildUiSnapshot(AutomationElement root)
    {
        var builder = new StringBuilder();
        foreach (var element in root.FindAllDescendants())
        {
            try
            {
                builder.AppendLine($"{element.ControlType}: '{element.Name}' automationId='{element.AutomationId}' enabled={element.IsEnabled}");
            }
            catch
            {
                builder.AppendLine("<unavailable element>");
            }
        }

        return builder.ToString();
    }
}
