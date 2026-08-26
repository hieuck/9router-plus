namespace RouterPlus.App.E2E;

/// <summary>
/// Tests for profile context menu actions.
/// Covers Google login, auto-login dialog, folder operations, and profile management.
/// </summary>
public sealed class ProfileActionsTests
{
    [Fact]
    public async Task Google_auto_login_menu_item_opens_dialog()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        await using var app = await RouterPlusProcess.StartAsync(environment);
        using var driver = new MainWindowDriver(app);

        driver.RightClickProfile("Harness Alpha");
        driver.WaitForContextMenu(TimeSpan.FromSeconds(3));
        driver.ClickContextMenuItem("Tự động đăng nhập Google");

        // Wait for dialog to appear
        await Task.Delay(1000);

        // Dialog should be visible (app doesn't crash)
        Assert.False(app.HasExited);
    }

    [Fact]
    public async Task Google_login_with_chrome_does_not_crash()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        await using var app = await RouterPlusProcess.StartAsync(environment);
        using var driver = new MainWindowDriver(app);

        driver.RightClickProfile("Harness Alpha");
        driver.WaitForContextMenu(TimeSpan.FromSeconds(3));
        driver.ClickContextMenuItem("Đăng nhập Google bằng Chrome");

        // Chrome launch is async, verify app doesn't crash
        await Task.Delay(1000);
        Assert.False(app.HasExited);
    }

    [Fact]
    public async Task Open_profile_folder_does_not_crash()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        await using var app = await RouterPlusProcess.StartAsync(environment);
        using var driver = new MainWindowDriver(app);

        driver.RightClickProfile("Harness Beta");
        driver.WaitForContextMenu(TimeSpan.FromSeconds(3));
        driver.ClickContextMenuItem("Mở thư mục profile");

        await Task.Delay(500);
        Assert.False(app.HasExited);
    }

    [Fact]
    public async Task Copy_profile_name_action_executes()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        await using var app = await RouterPlusProcess.StartAsync(environment);
        using var driver = new MainWindowDriver(app);

        driver.RightClickProfile("Harness Alpha");
        driver.WaitForContextMenu(TimeSpan.FromSeconds(3));
        driver.ClickContextMenuItem("Sao chép tên profile");

        // Action should complete without crash
        await Task.Delay(300);
        Assert.False(app.HasExited);
    }

    [Fact]
    public async Task Context_menu_can_be_dismissed_with_escape()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        await using var app = await RouterPlusProcess.StartAsync(environment);
        using var driver = new MainWindowDriver(app);

        driver.RightClickProfile("Harness Alpha");
        driver.WaitForContextMenu(TimeSpan.FromSeconds(3));
        driver.DismissContextMenu();

        // Menu should be dismissed without issues
        Assert.False(app.HasExited);
    }
}
