using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using Xunit;
using Xunit.Abstractions;

namespace RouterPlus.App.E2E;

[Collection("Live E2E")]
public class LiveGoogleAutoLoginTests : IDisposable
{
    private readonly ITestOutputHelper _output;

    public LiveGoogleAutoLoginTests(ITestOutputHelper output)
    {
        _output = output;
        LiveTestEnvironment.RequireLiveEnvironment();
    }

    public void Dispose()
    {
        // Cleanup
    }

    [Fact]
    public async Task Google_auto_login_completes_successfully()
    {
        var profileName = LiveTestEnvironment.GetRequiredProfileName();
        _output.WriteLine($"Testing with profile: {profileName}");

        // Start real app (not harness). The app must not close the user's browser.
        await Task.Delay(500);

        // Start real app (not harness)
        var executablePath = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "RouterPlus.App", "bin", "Debug", "net8.0-windows", "RouterPlus.exe"));

        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("Build the Debug RouterPlus app first.", executablePath);
        }

        var startInfo = new ProcessStartInfo(executablePath)
        {
            WorkingDirectory = Path.GetDirectoryName(executablePath)!,
            UseShellExecute = false
        };
        // No ROUTERPLUS_HARNESS - using real environment
        startInfo.Environment["ROUTERPLUS_LIVE_E2E"] = "1";

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("RouterPlus process could not be started.");
        var processId = process.Id;

        try
        {
            using var application = Application.Attach(process);
            application.WaitWhileMainHandleIsMissing();

            using var automation = new UIA3Automation();

            var window = application.GetMainWindow(automation);
            Assert.NotNull(window);

            await Task.Delay(2000);

            var profileList = window.FindFirstDescendant(cf =>
                cf.ByAutomationId("ProfileList"));
            Assert.NotNull(profileList);

            // Wait for provider synchronization - live mode may take longer
            _output.WriteLine("Waiting for profile synchronization...");
            await Task.Delay(10000);

            // List available profiles for diagnostics
            var allItems = profileList.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem));
            _output.WriteLine($"Found {allItems.Length} profiles:");
            foreach (var item in allItems)
            {
                _output.WriteLine($"  - ListItem.Name: {item.Name}");
                var textElements = item.FindAllDescendants(cf => cf.ByControlType(ControlType.Text));
                foreach (var text in textElements)
                {
                    _output.WriteLine($"    Text: {text.Name}");
                }
            }

            // Find the configured profile
            var profileItem = await FindProfileItemAsync(automation, profileName, timeoutSeconds: 30);
            if (profileItem == null)
            {
                _output.WriteLine($"ERROR: Could not find profile '{profileName}' after 30 seconds");
                Assert.Fail($"Profile '{profileName}' not found. Available profiles listed above.");
            }

            _output.WriteLine($"Found profile: {profileItem.Name}");

            // Ensure main window has focus
            window.Focus();
            await Task.Delay(500);

            // Click to select the profile
            profileItem.Click();
            await Task.Delay(1000);

            // Use Win32 SendInput API for hardware-level right-click
            var bounds = profileItem.BoundingRectangle;
            var centerX = (int)(bounds.Left + bounds.Width / 2);
            var centerY = (int)(bounds.Top + bounds.Height / 2);
            _output.WriteLine($"Right-clicking at ({centerX}, {centerY})");
            Win32InputHelper.RightClick(centerX, centerY);
            await Task.Delay(2500);

            // Retry right-click once if no menu opened (occasional timing flake).
            if (automation.GetDesktop().FindAllDescendants(cf => cf.ByControlType(ControlType.Menu)).Length == 0)
            {
                _output.WriteLine("No context menu opened on first RightClick; re-focusing profile and retrying.");
                profileItem = await FindProfileItemAsync(automation, profileName, timeoutSeconds: 10) ?? profileItem;
                await RightClickProfileReliablyAsync(automation, profileItem);
                await Task.Delay(2500);
            }

            var contextMenu = await WaitForContextMenuAsync(automation);
            Assert.NotNull(contextMenu);

            // Diagnostic: list every MenuItem in the context menu.
            try
            {
                var items = contextMenu.FindAllDescendants(cf => cf.ByControlType(ControlType.MenuItem));
                _output.WriteLine($"ContextMenu: {items.Length} MenuItem(s):");
                foreach (var it in items)
                {
                    try { _output.WriteLine($"  - Name='{it.Name}'"); } catch { }
                }
            }
            catch (Exception ex)
            {
                _output.WriteLine($"ContextMenu diagnostic failed: {ex.Message}");
            }

            // Find and click "Tự động đăng nhập Google"
            var autoLoginItem = contextMenu.FindFirstDescendant(cf =>
                cf.ByName("Tự động đăng nhập Google").And(cf.ByControlType(ControlType.MenuItem)));
            Assert.NotNull(autoLoginItem);

            _output.WriteLine("Clicking Google Auto Login menu item...");
            autoLoginItem.Click();
            await Task.Delay(1000);

            // Wait for dialog
            var dialog = await WaitForDialogAsync(automation, "Google", timeoutSeconds: 15);
            Assert.NotNull(dialog);
            _output.WriteLine($"Dialog opened: {dialog.Name}");

            // Check vault state
            var unlockButton = dialog.FindFirstDescendant(cf => cf.ByName("Mở khóa"));
            if (unlockButton != null && unlockButton.IsEnabled)
            {
                _output.WriteLine("Vault is locked - this test requires vault to be already unlocked with saved credentials");
                Assert.Fail("Vault must be unlocked with saved Google credentials for this test");
            }

            // Find and click Auto Login button
            var autoLoginButton = dialog.FindFirstDescendant(cf =>
                cf.ByName("Tự động đăng nhập").And(cf.ByControlType(ControlType.Button)));
            Assert.NotNull(autoLoginButton);
            Assert.True(autoLoginButton.IsEnabled, "Auto Login button should be enabled");

            _output.WriteLine("Clicking Auto Login button...");
            autoLoginButton.Click();

            // Wait for automation to complete - verify actual success
            _output.WriteLine("Waiting for Google authentication to complete...");

            bool authenticationSucceeded = false;
            bool dialogClosed = false;
            bool cdpDetected = false;
            string? authenticatedPageTitle = null;
            string? automationError = null;

            for (int i = 0; i < 90; i++) // 90 seconds timeout for full authentication flow
            {
                await Task.Delay(1000);

                // Check if dialog still exists (with error handling)
                try
                {
                    var currentDialog = automation.GetDesktop()
                        .FindAllDescendants(cf => cf.ByControlType(ControlType.Window))
                        .FirstOrDefault(w =>
                        {
                            try
                            {
                                return w.Name.Contains("Google", StringComparison.OrdinalIgnoreCase) &&
                                       w.Name.Contains("đăng nhập", StringComparison.OrdinalIgnoreCase);
                            }
                            catch
                            {
                                return false; // Window closed during iteration
                            }
                        });

                    if (currentDialog == null && !dialogClosed)
                    {
                        dialogClosed = true;
                        _output.WriteLine($"Dialog closed at {i}s - checking authentication result...");
                    }

                    // Check for error in dialog status (if dialog still open)
                    if (currentDialog != null)
                    {
                        try
                        {
                            var statusElements = currentDialog.FindAllDescendants(cf => cf.ByControlType(ControlType.Text));
                            foreach (var status in statusElements)
                            {
                                try
                                {
                                    var text = status.Name;
                                    if (!string.IsNullOrEmpty(text) &&
                                        (text.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                                         text.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
                                         text.Contains("lỗi", StringComparison.OrdinalIgnoreCase) ||
                                         text.Contains("unsupported", StringComparison.OrdinalIgnoreCase) ||
                                         text.Contains("cancelled", StringComparison.OrdinalIgnoreCase) ||
                                         text.Contains("invalid credentials", StringComparison.OrdinalIgnoreCase)))
                                    {
                                        automationError = text;
                                        _output.WriteLine($"❌ Error detected in dialog: {text}");
                                    }
                                }
                                catch
                                {
                                    // Element no longer valid
                                }
                            }
                        }
                        catch
                        {
                            // Dialog elements changed
                        }
                    }
                }
                catch
                {
                    // Desktop enumeration failed, continue
                }

                if (automationError != null)
                {
                    break;
                }

                // Verify CDP endpoint exists
                if (!cdpDetected)
                {
                    var chromeProcesses = Process.GetProcessesByName("chrome");
                    foreach (var proc in chromeProcesses)
                    {
                        try
                        {
                            using var searcher = new ManagementObjectSearcher(
                                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {proc.Id}");
                            var results = searcher.Get().Cast<ManagementObject>();
                            var commandLine = results.FirstOrDefault()?["CommandLine"]?.ToString();

                            if (!string.IsNullOrEmpty(commandLine) &&
                                commandLine.Contains("remote-debugging-port", StringComparison.OrdinalIgnoreCase))
                            {
                                cdpDetected = true;
                                _output.WriteLine($"✅ CDP endpoint detected on Chrome process {proc.Id}");
                                break;
                            }
                        }
                        catch { }
                    }
                }

                // Check for authenticated Google page
                try
                {
                    var allWindows = automation.GetDesktop().FindAllDescendants(cf => cf.ByControlType(ControlType.Window));
                    var googleWindow = allWindows.FirstOrDefault(w =>
                    {
                        try
                        {
                            return w.Name.Contains("Google", StringComparison.OrdinalIgnoreCase) &&
                                   w.Name.Contains("Chrome", StringComparison.OrdinalIgnoreCase) &&
                                   !w.Name.Contains("Sign in", StringComparison.OrdinalIgnoreCase);
                        }
                        catch
                        {
                            return false;
                        }
                    });

                    if (googleWindow != null && dialogClosed)
                    {
                        try
                        {
                            authenticatedPageTitle = googleWindow.Name;
                            // Check if it's an authenticated page (not sign-in page)
                            if (!authenticatedPageTitle.Contains("Sign in", StringComparison.OrdinalIgnoreCase) &&
                                (authenticatedPageTitle.Contains("Account", StringComparison.OrdinalIgnoreCase) ||
                                 authenticatedPageTitle.Contains("Google", StringComparison.OrdinalIgnoreCase)))
                            {
                                authenticationSucceeded = true;
                                _output.WriteLine($"✅ Authentication succeeded! Page title: '{authenticatedPageTitle}'");
                                break;
                            }
                        }
                        catch
                        {
                            // Window no longer valid
                        }
                    }
                }
                catch
                {
                    // Window enumeration failed
                }

                if (i % 10 == 0)
                {
                    _output.WriteLine($"Still waiting... ({i}s elapsed, dialog closed: {dialogClosed}, CDP: {cdpDetected})");
                }
            }

            // Final diagnostic output
            _output.WriteLine("\n=== Final Diagnostic ===");
            _output.WriteLine($"Dialog closed: {dialogClosed}");
            _output.WriteLine($"CDP detected: {cdpDetected}");
            _output.WriteLine($"Authentication succeeded: {authenticationSucceeded}");
            if (automationError != null)
            {
                Assert.Fail($"Authentication failed with error: {automationError}");
            }
            if (authenticatedPageTitle != null)
            {
                _output.WriteLine($"Authenticated page: {authenticatedPageTitle}");
            }

            // Verify all success criteria
            Assert.True(dialogClosed, "Dialog should close after automation completes");
            Assert.True(cdpDetected, "Chrome with CDP endpoint (--remote-debugging-port) must be detected");
            Assert.True(authenticationSucceeded,
                "Google authentication must succeed - authenticated Google page must be displayed");

            var appProcess = Process.GetProcessById(processId);
            Assert.False(appProcess.HasExited, "App should still be running after automation");

            _output.WriteLine("\n✅ All verification passed - Google Auto Login works end-to-end");
        }
        finally
        {
            // Cleanup
            try
            {
                var appProcess = Process.GetProcessById(processId);
                if (!appProcess.HasExited)
                {
                    appProcess.Kill(entireProcessTree: true);
                    appProcess.WaitForExit(5000);
                }
            }
            catch (ArgumentException)
            {
                // Process already exited
            }
        }
    }

    private async Task<AutomationElement?> FindProfileItemAsync(
        UIA3Automation automation,
        string profileName,
        int timeoutSeconds = 10)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

        while (DateTime.UtcNow < deadline)
        {
            var desktop = automation.GetDesktop();
            var windows = desktop.FindAllDescendants(cf => cf.ByControlType(ControlType.Window));

            foreach (var window in windows)
            {
                var profileList = window.FindFirstDescendant(cf => cf.ByAutomationId("ProfileList"));
                if (profileList != null)
                {
                    var items = profileList.FindAllDescendants(cf => cf.ByControlType(ControlType.ListItem));
                    foreach (var item in items)
                    {
                        // Look for TextBlock with the profile name inside the ListItem
                        var textElements = item.FindAllDescendants(cf => cf.ByControlType(ControlType.Text));
                        foreach (var text in textElements)
                        {
                            if (text.Name.Contains(profileName, StringComparison.OrdinalIgnoreCase))
                            {
                                return item;
                            }
                        }
                    }
                }
            }

            await Task.Delay(500);
        }

        return null;
    }

    private async Task RightClickProfileReliablyAsync(
        UIA3Automation automation,
        AutomationElement profileItem)
    {
        // Dismiss any stale context menu first
        FlaUI.Core.Input.Keyboard.Press(FlaUI.Core.WindowsAPI.VirtualKeyShort.ESC);
        await Task.Delay(300);

        // Focus the main window
        var mainWindow = automation.GetDesktop().FindAllDescendants(cf => cf.ByControlType(ControlType.Window))
            .FirstOrDefault(w => !string.IsNullOrEmpty(w.Name));
        mainWindow?.Focus();
        await Task.Delay(300);

        // Click profile to select it
        try { profileItem.Click(); } catch { }
        await Task.Delay(500);

        // Use Win32 SendInput API for hardware-level right-click
        var bounds = profileItem.BoundingRectangle;
        var centerX = (int)(bounds.Left + bounds.Width / 2);
        var centerY = (int)(bounds.Top + bounds.Height / 2);
        Win32InputHelper.RightClick(centerX, centerY);
    }

    private async Task<AutomationElement?> WaitForContextMenuAsync(UIA3Automation automation)
    {
        for (int i = 0; i < 20; i++)
        {
            var desktop = automation.GetDesktop();
            var menus = desktop.FindAllDescendants(cf => cf.ByControlType(ControlType.Menu));

            if (menus.Length > 0)
            {
                return menus[0];
            }

            await Task.Delay(100);
        }

        return null;
    }

    private async Task<AutomationElement?> WaitForDialogAsync(
        UIA3Automation automation,
        string titleContains,
        int timeoutSeconds = 10)
    {
        var desktop = automation.GetDesktop();

        for (int i = 0; i < timeoutSeconds * 2; i++)
        {
            var windows = desktop.FindAllDescendants(cf => cf.ByControlType(ControlType.Window));

            foreach (var window in windows)
            {
                if (window.Name.Contains(titleContains, StringComparison.OrdinalIgnoreCase))
                {
                    return window;
                }
            }

            await Task.Delay(500);
        }

        return null;
    }
}

internal static class Win32Helper
{
    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool ScreenToClient(IntPtr hWnd, ref System.Drawing.Point lpPoint);

    public static IntPtr MakeLParam(int x, int y)
    {
        return (IntPtr)((y << 16) | (x & 0xFFFF));
    }
}
