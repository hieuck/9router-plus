namespace RouterPlus.App.E2E;

/// <summary>
/// Diagnostic test to debug Google Auto Login dialog opening issues.
/// </summary>
public sealed class GoogleAutoLoginDiagnosticTests
{
    [Fact]
    public async Task Debug_what_happens_when_auto_login_clicked()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        await using var app = await RouterPlusProcess.StartAsync(environment);
        using var driver = new MainWindowDriver(app);

        Console.WriteLine("Opening context menu...");
        driver.RightClickProfile("Harness Alpha");
        driver.WaitForContextMenu(TimeSpan.FromSeconds(3));

        Console.WriteLine("Clicking Auto Login menu item...");
        driver.ClickContextMenuItem("Tự động đăng nhập Google");

        Console.WriteLine("Waiting for dialog...");
        await Task.Delay(3000);

        // Check all windows on desktop
        var desktop = app.Automation.GetDesktop();
        var allWindows = desktop.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Window));

        Console.WriteLine($"Found {allWindows.Length} windows on desktop:");
        foreach (var window in allWindows)
        {
            try
            {
                Console.WriteLine($"  - '{window.Name}' (AutomationId: {window.AutomationId})");
            }
            catch
            {
                Console.WriteLine($"  - [Unable to read window info]");
            }
        }

        // Check if app crashed
        Console.WriteLine($"App HasExited: {app.HasExited}");

        // Try to find dialog with different approaches
        Console.WriteLine("\nSearching for dialog with different methods:");

        var byName = desktop.FindFirstDescendant(cf => cf.ByName("Google Auto Login"));
        Console.WriteLine($"By name 'Google Auto Login': {(byName != null ? "FOUND" : "NOT FOUND")}");

        var byPartialName = desktop.FindFirstDescendant(cf => cf.ByName("Google"));
        Console.WriteLine($"By partial name 'Google': {(byPartialName != null ? $"FOUND: '{byPartialName.Name}'" : "NOT FOUND")}");
    }
}
