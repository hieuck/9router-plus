namespace RouterPlus.App.E2E;

/// <summary>
/// Tests for profile sidebar collapse/expand functionality.
/// </summary>
public sealed class ProfileSidebarTests
{
    [Fact]
    public async Task Sidebar_is_visible_on_startup()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        await using var app = await RouterPlusProcess.StartAsync(environment);
        using var driver = new MainWindowDriver(app);

        // Profile list should be visible
        var profileList = driver.FindProfileList();
        Assert.NotNull(profileList);
        Assert.False(profileList.IsOffscreen);
    }

    [Fact]
    public async Task Both_profiles_visible_in_sidebar()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        await using var app = await RouterPlusProcess.StartAsync(environment);
        using var driver = new MainWindowDriver(app);

        var alpha = driver.FindProfileItem("Harness Alpha");
        var beta = driver.FindProfileItem("Harness Beta");

        Assert.False(alpha.IsOffscreen);
        Assert.False(beta.IsOffscreen);
    }

    [Fact]
    public async Task Profile_items_are_clickable()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        await using var app = await RouterPlusProcess.StartAsync(environment);
        using var driver = new MainWindowDriver(app);

        var alpha = driver.FindProfileItem("Harness Alpha");
        Assert.True(alpha.IsEnabled);

        driver.ClickProfile("Harness Alpha");
        Assert.False(app.HasExited);
    }
}
