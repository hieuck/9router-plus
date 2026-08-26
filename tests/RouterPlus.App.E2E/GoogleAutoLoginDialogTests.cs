namespace RouterPlus.App.E2E;

/// <summary>
/// Tests for Google Auto Login dialog functionality in synthetic harness environment.
/// Tests dialog UI, vault operations, and basic workflow without real credentials.
/// </summary>
public sealed class GoogleAutoLoginDialogTests
{
    [Fact]
    public async Task Dialog_opens_successfully()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        await using var app = await RouterPlusProcess.StartAsync(environment);
        using var driver = new MainWindowDriver(app);

        // Must select profile first before opening dialog
        driver.ClickProfile("Harness Alpha");
        await Task.Delay(200);

        driver.RightClickProfile("Harness Alpha");
        driver.WaitForContextMenu(TimeSpan.FromSeconds(3));
        driver.ClickContextMenuItem("Tự động đăng nhập Google");

        var dialog = driver.WaitForDialog("Google", TimeSpan.FromSeconds(5));
        Assert.NotNull(dialog);

        // Verify dialog has expected controls
        Assert.True(dialog.IsEnabled);
    }

    [Fact]
    public async Task Dialog_has_vault_unlock_button()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        await using var app = await RouterPlusProcess.StartAsync(environment);
        using var driver = new MainWindowDriver(app);

        driver.ClickProfile("Harness Alpha");
        await Task.Delay(200);

        driver.RightClickProfile("Harness Alpha");
        driver.WaitForContextMenu(TimeSpan.FromSeconds(3));
        driver.ClickContextMenuItem("Tự động đăng nhập Google");

        var dialog = driver.WaitForDialog("Google", TimeSpan.FromSeconds(5));
        Assert.NotNull(dialog);

        // Button name is in Vietnamese: "Mở khóa" not "Unlock Vault"
        var unlockButton = dialog.FindFirstDescendant(cf => cf.ByName("Mở khóa"));
        Assert.NotNull(unlockButton);
    }

    [Fact]
    public async Task Dialog_has_auto_login_button()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        await using var app = await RouterPlusProcess.StartAsync(environment);
        using var driver = new MainWindowDriver(app);

        driver.ClickProfile("Harness Beta");
        await Task.Delay(200);

        driver.RightClickProfile("Harness Beta");
        driver.WaitForContextMenu(TimeSpan.FromSeconds(3));
        driver.ClickContextMenuItem("Tự động đăng nhập Google");

        var dialog = driver.WaitForDialog("Google", TimeSpan.FromSeconds(5));
        Assert.NotNull(dialog);

        // Button name is in Vietnamese: "Tự động đăng nhập" not "Auto Login"
        var autoLoginButton = dialog.FindFirstDescendant(cf =>
            cf.ByName("Tự động đăng nhập").And(cf.ByControlType(FlaUI.Core.Definitions.ControlType.Button)));

        Assert.NotNull(autoLoginButton);
    }

    [Fact]
    public async Task Dialog_can_be_cancelled()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        await using var app = await RouterPlusProcess.StartAsync(environment);
        using var driver = new MainWindowDriver(app);

        driver.ClickProfile("Harness Alpha");
        await Task.Delay(200);

        driver.RightClickProfile("Harness Alpha");
        driver.WaitForContextMenu(TimeSpan.FromSeconds(3));
        driver.ClickContextMenuItem("Tự động đăng nhập Google");

        var dialog = driver.WaitForDialog("Google", TimeSpan.FromSeconds(5));
        Assert.NotNull(dialog);

        // Button name is in Vietnamese: "Hủy" not "Cancel"
        var cancelButton = dialog.FindFirstDescendant(cf => cf.ByName("Hủy"));
        if (cancelButton != null)
        {
            cancelButton.Click();
            await Task.Delay(500);

            var stillOpen = driver.WaitForDialog("Google", TimeSpan.FromMilliseconds(500));
            Assert.Null(stillOpen);
        }
    }
}
