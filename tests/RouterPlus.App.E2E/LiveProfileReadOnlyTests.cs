using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;

namespace RouterPlus.App.E2E;

[Collection("Live E2E")]
public sealed class LiveProfileReadOnlyTests
{
    public LiveProfileReadOnlyTests()
    {
        LiveTestEnvironment.RequireLiveEnvironment();
    }

    [Fact]
    public async Task Live_startup_shows_configured_profile()
    {
        var profileName = LiveTestEnvironment.GetRequiredProfileName();
        await using var app = await LiveRouterPlusProcess.StartAsync();
        using var driver = new LiveReadOnlyDriver(app);

        var profileList = driver.FindProfileList();
        Assert.False(IsOffscreen(profileList));
        Assert.Single(driver.FindProfileItems(profileName));
        Assert.False(IsOffscreen(app.MainWindow));
    }

    [Fact]
    public async Task Live_profile_selection_and_menu_dismissal_keep_app_stable()
    {
        var profileName = LiveTestEnvironment.GetRequiredProfileName();
        await using var app = await LiveRouterPlusProcess.StartAsync();
        using var driver = new LiveReadOnlyDriver(app);

        driver.ClickProfile(profileName);
        driver.RightClickProfile(profileName);
        var menu = driver.WaitForContextMenu();

        foreach (var menuItem in LiveReadOnlyDriver.SafeMenuItems)
        {
            Assert.True(menu.FindFirstDescendant(cf => cf.ByName(menuItem)) is not null, menuItem);
            Assert.True(LiveActionPolicy.IsAllowed(menuItem), menuItem);
        }

        Assert.False(LiveActionPolicy.IsAllowed("Xóa profile…"));
        driver.DismissContextMenu();
        Assert.Null(driver.TryFindContextMenu());
        Assert.False(IsOffscreen(app.MainWindow));
    }

    [Fact]
    public async Task Live_google_auto_login_dialog_can_be_cancelled_without_starting_login()
    {
        var profileName = LiveTestEnvironment.GetRequiredProfileName();
        await using var app = await LiveRouterPlusProcess.StartAsync();
        using var driver = new LiveReadOnlyDriver(app);

        driver.ClickProfile(profileName);
        driver.RightClickProfile(profileName);
        driver.WaitForContextMenu();
        driver.ClickAllowedContextMenuItem("Tự động đăng nhập Google");

        var dialog = driver.WaitForDialog("Google");
        Assert.NotNull(dialog);
        Assert.NotNull(dialog.FindFirstDescendant(cf => cf.ByName("Mở khóa")));
        Assert.NotNull(dialog.FindFirstDescendant(cf =>
            cf.ByName("Tự động đăng nhập").And(cf.ByControlType(ControlType.Button))));

        var cancelButton = dialog.FindFirstDescendant(cf =>
            cf.ByName("Hủy").And(cf.ByControlType(ControlType.Button)));
        Assert.NotNull(cancelButton);
        cancelButton!.Click();

        Assert.Null(driver.WaitForDialog("Google", TimeSpan.FromSeconds(2), throwOnTimeout: false));
        Assert.False(IsOffscreen(app.MainWindow));
    }

    private static bool IsOffscreen(AutomationElement element)
    {
        if (element.Properties is null)
        {
            return false;
        }

        return element.Properties.IsOffscreen == true;
    }
}
