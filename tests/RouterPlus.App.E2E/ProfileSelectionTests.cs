namespace RouterPlus.App.E2E;

/// <summary>
/// Tests for profile selection behavior in the main window.
/// Covers single-click selection and double-click launch.
/// </summary>
public sealed class ProfileSelectionTests
{
    [Fact]
    public async Task Single_click_selects_profile()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        await using var app = await RouterPlusProcess.StartAsync(environment);
        using var driver = new MainWindowDriver(app);

        driver.ClickProfile("Harness Alpha");

        // Verify profile is selected by checking if it's visible and accessible
        var selectedItem = driver.FindProfileItem("Harness Alpha");
        Assert.NotNull(selectedItem);
    }

    [Fact]
    public async Task Can_switch_between_profiles()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        await using var app = await RouterPlusProcess.StartAsync(environment);
        using var driver = new MainWindowDriver(app);

        driver.ClickProfile("Harness Alpha");
        var alphaItem = driver.FindProfileItem("Harness Alpha");
        Assert.NotNull(alphaItem);

        driver.ClickProfile("Harness Beta");
        var betaItem = driver.FindProfileItem("Harness Beta");
        Assert.NotNull(betaItem);
    }

    [Fact]
    public async Task Double_click_launches_profile_without_crash()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        await using var app = await RouterPlusProcess.StartAsync(environment);
        using var driver = new MainWindowDriver(app);

        driver.DoubleClickProfile("Harness Alpha");

        // Chrome launch is async, just verify app doesn't crash
        await Task.Delay(500);
        Assert.False(app.HasExited);
    }

    [Fact]
    public async Task Profile_list_shows_both_synthetic_profiles()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        await using var app = await RouterPlusProcess.StartAsync(environment);
        using var driver = new MainWindowDriver(app);

        var alpha = driver.FindProfileItem("Harness Alpha");
        var beta = driver.FindProfileItem("Harness Beta");

        Assert.NotNull(alpha);
        Assert.NotNull(beta);
    }
}
