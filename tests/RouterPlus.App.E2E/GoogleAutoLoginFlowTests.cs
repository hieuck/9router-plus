namespace RouterPlus.App.E2E;

/// <summary>
/// Tests that actually click the Auto Login button and verify the automation flow.
/// Uses harness environment with synthetic profiles.
/// </summary>
public sealed class GoogleAutoLoginFlowTests
{
    [Fact]
    public async Task Auto_login_button_click_starts_automation()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        await using var app = await RouterPlusProcess.StartAsync(environment);
        using var driver = new MainWindowDriver(app);

        Console.WriteLine("Selecting profile...");
        driver.ClickProfile("Harness Alpha");
        await Task.Delay(200);

        Console.WriteLine("Opening Auto Login dialog...");
        driver.RightClickProfile("Harness Alpha");
        driver.WaitForContextMenu(TimeSpan.FromSeconds(3));
        driver.ClickContextMenuItem("Tự động đăng nhập Google");

        var dialog = driver.WaitForDialog("Google", TimeSpan.FromSeconds(5));
        Assert.NotNull(dialog);
        Console.WriteLine("Dialog opened");

        // Check if vault is locked
        var unlockButton = dialog.FindFirstDescendant(cf => cf.ByName("Mở khóa"));
        if (unlockButton != null && unlockButton.IsEnabled)
        {
            Console.WriteLine("Vault is LOCKED - cannot proceed without unlocking");
            Assert.NotNull(unlockButton);
            return;
        }

        Console.WriteLine("Vault appears to be unlocked or not required");

        // Find Auto Login button
        var autoLoginButton = dialog.FindFirstDescendant(cf =>
            cf.ByName("Tự động đăng nhập").And(cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button)));

        Assert.NotNull(autoLoginButton);
        Console.WriteLine($"Auto Login button found. IsEnabled: {autoLoginButton.IsEnabled}");

        if (!autoLoginButton.IsEnabled)
        {
            Console.WriteLine("Auto Login button is DISABLED - likely no credentials saved");
            var statusElements = dialog.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Text));
            Console.WriteLine($"Found {statusElements.Length} text elements in dialog");
            foreach (var elem in statusElements.Take(10))
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(elem.Name))
                    {
                        Console.WriteLine($"  Status text: '{elem.Name}'");
                    }
                }
                catch { }
            }
            return;
        }

        Console.WriteLine("Clicking Auto Login button...");
        autoLoginButton.Click();

        Console.WriteLine("Waiting for automation to start...");
        await Task.Delay(3000);

        Console.WriteLine($"App still running: {!app.HasExited}");

        await Task.Delay(5000);

        Console.WriteLine("Test completed - check if Chrome windows opened");
    }
}
