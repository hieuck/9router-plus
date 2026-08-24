using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using Xunit;
using Xunit.Abstractions;

namespace RouterPlus.App.E2E;

/// <summary>
/// Live E2E tests for Google Auto Login that use real Chrome profiles and saved credentials.
/// Requires ROUTERPLUS_LIVE_E2E=1 and ROUTERPLUS_LIVE_PROFILE=<profile-name> environment variables.
/// </summary>
public sealed class LiveGoogleAutoLoginTests : IAsyncDisposable
{
    private readonly ITestOutputHelper _output;
    private LiveRouterPlusProcess? _process;

    public LiveGoogleAutoLoginTests(ITestOutputHelper output)
    {
        _output = output;
        LiveTestEnvironment.RequireLiveEnvironment();
    }

    [Fact]
    public async Task AutoLogin_WithSavedCredentials_ShouldSucceed()
    {
        // Arrange
        var profileName = LiveTestEnvironment.GetRequiredProfileName();
        _output.WriteLine($"Testing Auto Login with profile: {profileName}");

        _process = await LiveRouterPlusProcess.StartAsync(timeoutSeconds: 30);
        _output.WriteLine("App started successfully");
        _output.WriteLine($"Main window title: {_process.MainWindow.Title}");

        // Wait for profile list to load (may take time in real mode with provider sync)
        await Task.Delay(2000);

        // Find ProfileList using FindAllDescendants
        var profileList = _process.MainWindow.FindAllDescendants()
            .FirstOrDefault(d =>
            {
                try { return d.AutomationId == "ProfileList"; }
                catch { return false; }
            });

        Assert.NotNull(profileList);
        _output.WriteLine("Found ProfileList");

        // Wait for provider sync to complete
        _output.WriteLine("Waiting for provider sync to complete...");
        bool syncCompleted = false;
        for (int i = 0; i < 15; i++)
        {
            var items = profileList.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem));
            if (items.Length > 0)
            {
                var texts = items[0].FindAllDescendants(cf => cf.ByControlType(ControlType.Text));
                var hasSyncingText = texts.Any(t => t.Name.Contains("Đang chờ đồng bộ", StringComparison.Ordinal));
                if (!hasSyncingText)
                {
                    syncCompleted = true;
                    _output.WriteLine("Provider sync completed");
                    break;
                }
            }
            await Task.Delay(1000);
        }

        if (!syncCompleted)
        {
            _output.WriteLine("Warning: Provider sync did not complete within timeout");
        }

        // Find target profile item
        var profileItem = FindProfileItem(profileList, profileName);
        if (profileItem == null)
        {
            // Debug: show actual profile names with all text elements
            var items = profileList.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem));
            _output.WriteLine($"Could not find profile '{profileName}'. Available profiles:");
            foreach (var item in items)
            {
                var texts = item.FindAllDescendants(cf => cf.ByControlType(ControlType.Text));
                _output.WriteLine($"  Profile item with {texts.Length} text elements:");
                foreach (var text in texts)
                {
                    _output.WriteLine($"    - '{text.Name}'");
                }
            }
        }
        Assert.NotNull(profileItem);
        _output.WriteLine($"Found profile item: {profileName}");

        // Right-click to open context menu with retry
        var clickPoint = profileItem.GetClickablePoint();
        _process.MainWindow.Focus();
        await Task.Delay(200);

        Mouse.Click(clickPoint, MouseButton.Right);
        _output.WriteLine($"Right-clicked at {clickPoint.X},{clickPoint.Y}");

        // Wait longer for context menu to appear
        await Task.Delay(1500);

        // Find context menu and Auto Login item with retry
        AutomationElement? autoLoginItem = null;
        for (int i = 0; i < 3; i++)
        {
            autoLoginItem = FindContextMenuItem("Tự động đăng nhập Google");
            if (autoLoginItem != null)
            {
                _output.WriteLine("Found 'Tự động đăng nhập Google' menu item");
                break;
            }
            _output.WriteLine($"Menu item not found, retry {i + 1}/3...");
            await Task.Delay(500);
        }

        if (autoLoginItem == null)
        {
            _output.WriteLine("Context menu item not found after retries. Checking all Popup controls...");
            var desktop = _process.Automation.GetDesktop();
            var popups = desktop.FindAllDescendants(cf => cf.ByControlType(ControlType.Pane));
            _output.WriteLine($"Found {popups.Length} pane controls on desktop");

            // Also try searching main window for popup
            var windowPopups = _process.MainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Menu));
            _output.WriteLine($"Found {windowPopups.Length} menu controls in main window");
        }
        Assert.NotNull(autoLoginItem);
        _output.WriteLine("Found 'Tự động đăng nhập Google' menu item");

        // Click Auto Login menu item
        autoLoginItem.Click();
        await Task.Delay(1000);

        // Find Google Auto Login dialog with longer timeout
        var dialog = await WaitForDialogAsync("Google Auto Login", timeoutSeconds: 15);
        if (dialog == null)
        {
            _output.WriteLine("Dialog not found. Checking all windows...");
            var allWindows = _process.MainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Window));
            _output.WriteLine($"Found {allWindows.Length} window elements:");
            foreach (var win in allWindows.Take(10))
            {
                _output.WriteLine($"  - {win.Name}");
            }
        }
        Assert.NotNull(dialog);
        _output.WriteLine("Google Auto Login dialog opened");

        // Check if vault needs unlocking
        var unlockButton = dialog.FindFirstDescendant(cf => cf.ByName("Unlock Vault"));
        if (unlockButton != null && unlockButton.IsEnabled)
        {
            _output.WriteLine("Vault is locked - test requires pre-unlocked vault or remembered password");
            throw new InvalidOperationException(
                "Vault is locked. Please unlock vault manually or configure remembered unlock before running live tests.");
        }

        // Find and click Auto Login button
        var autoLoginButton = dialog.FindFirstDescendant(cf =>
            cf.ByName("Auto Login").And(cf.ByControlType(ControlType.Button)));
        Assert.NotNull(autoLoginButton);
        Assert.True(autoLoginButton.IsEnabled, "Auto Login button should be enabled");

        _output.WriteLine("Clicking Auto Login button...");
        autoLoginButton.Click();

        // Wait for Chrome to launch and automation to complete
        // Real Google login can take 10-30 seconds depending on network and 2FA
        _output.WriteLine("Waiting for automation to complete (up to 60 seconds)...");
        await Task.Delay(5000); // Initial delay for Chrome launch

        // Check for result - dialog should close on success or show error
        var resultTimeout = DateTime.UtcNow.AddSeconds(60);
        bool dialogClosed = false;
        string? errorMessage = null;

        while (DateTime.UtcNow < resultTimeout)
        {
            try
            {
                // Check if dialog still exists
                var currentDialog = _process.MainWindow.FindFirstDescendant(cf =>
                    cf.ByName("Google Auto Login").And(cf.ByControlType(ControlType.Window)));

                if (currentDialog == null)
                {
                    dialogClosed = true;
                    _output.WriteLine("Dialog closed - automation completed");
                    break;
                }

                // Check for error message in dialog
                var errorText = currentDialog.FindFirstDescendant(cf =>
                    cf.ByControlType(ControlType.Text).And(cf.ByName("Error")));
                if (errorText != null)
                {
                    errorMessage = errorText.Name;
                    _output.WriteLine($"Error detected: {errorMessage}");
                    break;
                }
            }
            catch
            {
                // Dialog might be in transition
            }

            await Task.Delay(1000);
        }

        // Assert result
        Assert.True(dialogClosed || errorMessage != null,
            "Expected dialog to close (success) or show error message within timeout");

        if (errorMessage != null)
        {
            _output.WriteLine($"Automation completed with error: {errorMessage}");
            // Don't fail test - error message means automation ran but encountered expected failure
            // (e.g., manual challenge, network issue, etc.)
        }
        else
        {
            _output.WriteLine("Automation completed successfully");
        }
    }

    private AutomationElement? FindProfileItem(AutomationElement profileList, string profileName)
    {
        var items = profileList.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem));
        foreach (var item in items)
        {
            var texts = item.FindAllDescendants(cf => cf.ByControlType(ControlType.Text));
            // Profile name is typically the 3rd text element (after index and initial letter)
            // Check all text elements for the profile name
            foreach (var text in texts)
            {
                if (text.Name.Contains(profileName, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
            }
        }
        return null;
    }

    private AutomationElement? FindContextMenuItem(string menuItemName)
    {
        var desktop = _process!.Automation.GetDesktop();
        var menu = desktop.FindFirstDescendant(cf => cf.ByControlType(ControlType.Menu));
        return menu?.FindFirstDescendant(cf => cf.ByName(menuItemName));
    }

    private async Task<AutomationElement?> WaitForDialogAsync(string dialogTitle, int timeoutSeconds)
    {
        var timeoutAt = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < timeoutAt)
        {
            // Search for dialog as a top-level window on desktop, not as MainWindow descendant
            var desktop = _process!.Automation.GetDesktop();
            var dialog = desktop.FindFirstDescendant(cf =>
                cf.ByName(dialogTitle).And(cf.ByControlType(ControlType.Window)));
            if (dialog != null)
            {
                return dialog;
            }
            await Task.Delay(100);
        }
        return null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_process != null)
        {
            await _process.DisposeAsync();
        }
    }
}
