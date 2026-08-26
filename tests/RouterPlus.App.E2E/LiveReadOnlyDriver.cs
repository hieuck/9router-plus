using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace RouterPlus.App.E2E;

public sealed class LiveReadOnlyDriver : IDisposable
{
    public static readonly string[] SafeMenuItems =
    {
        "Đăng nhập Google bằng Chrome",
        "Tự động đăng nhập Google",
        "Mở thư mục profile",
        "Sao chép tên profile"
    };

    private readonly LiveRouterPlusProcess _process;

    public LiveReadOnlyDriver(LiveRouterPlusProcess process)
    {
        _process = process;
    }

    public AutomationElement FindProfileList() =>
        _process.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("ProfileList"))
        ?? throw new InvalidOperationException("ProfileList was not found.");

    public AutomationElement[] FindProfileItems(string profileName)
    {
        var list = FindProfileList();
        return list.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem))
            .Where(item => item.Name.Contains(profileName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public void ClickProfile(string profileName)
    {
        var item = FindRequiredProfileItem(profileName);
        item.Click();
    }

    public void RightClickProfile(string profileName)
    {
        var item = FindRequiredProfileItem(profileName);
        var bounds = item.BoundingRectangle;
        if (bounds.IsEmpty)
        {
            throw new InvalidOperationException($"Profile item '{profileName}' has no visible bounds.");
        }

        _process.MainWindow.Focus();
        Mouse.MoveTo(new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2));
        Mouse.Click(MouseButton.Right);
    }

    public AutomationElement WaitForContextMenu() =>
        WaitFor(() => TryFindContextMenu(), TimeSpan.FromSeconds(5))
        ?? throw new TimeoutException("The live profile context menu did not appear.");

    public AutomationElement? TryFindContextMenu() =>
        _process.Automation.GetDesktop().FindFirstDescendant(cf => cf.Menu());

    public void DismissContextMenu()
    {
        Keyboard.Press(VirtualKeyShort.ESC);
        WaitFor(() => TryFindContextMenu() is null, TimeSpan.FromSeconds(2));
    }

    public void ClickAllowedContextMenuItem(string menuItemText)
    {
        if (!LiveActionPolicy.IsAllowed(menuItemText))
        {
            throw new InvalidOperationException($"Live harness action is not allowed: '{menuItemText}'.");
        }

        var menu = WaitForContextMenu();
        var menuItem = menu.FindFirstDescendant(cf =>
            cf.ByName(menuItemText).And(cf.ByControlType(ControlType.MenuItem)));
        if (menuItem is null)
        {
            throw new InvalidOperationException($"Context menu item '{menuItemText}' was not found.");
        }

        menuItem.Click();
    }

    public AutomationElement? WaitForDialog(
        string titleContains,
        TimeSpan? timeout = null,
        bool throwOnTimeout = true)
    {
        var result = WaitFor(
            () => _process.Automation.GetDesktop()
                .FindAllDescendants(cf => cf.ByControlType(ControlType.Window))
                .FirstOrDefault(window =>
                {
                    try
                    {
                        return window.Name.Contains(titleContains, StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        return false;
                    }
                }),
            timeout ?? TimeSpan.FromSeconds(10));

        if (result is null && throwOnTimeout)
        {
            throw new TimeoutException($"Dialog containing '{titleContains}' was not found.");
        }

        return result;
    }

    public void Dispose()
    {
        DismissContextMenuIfPresent();
    }

    private AutomationElement FindRequiredProfileItem(string profileName) =>
        FindProfileItems(profileName).SingleOrDefault()
        ?? throw new InvalidOperationException(
            $"Expected exactly one live profile item containing '{profileName}'.");

    private void DismissContextMenuIfPresent()
    {
        if (TryFindContextMenu() is not null)
        {
            DismissContextMenu();
        }
    }

    private static T? WaitFor<T>(Func<T?> operation, TimeSpan timeout)
        where T : class
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var result = operation();
            if (result is not null)
            {
                return result;
            }

            Thread.Sleep(100);
        }

        return null;
    }

    private static bool WaitFor(Func<bool> operation, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (operation())
            {
                return true;
            }

            Thread.Sleep(100);
        }

        return operation();
    }
}
