namespace RouterPlus.App.E2E;

/// <summary>
/// Tests for profile selection and interaction.
/// </summary>
public sealed class ProfileManagementTests
{
    [Fact]
    public async Task Can_select_profile()
    {
        await using var env = await TestEnvironment.CreateAsync();
        await using var app = await AppProcess.StartAsync(env);
        var driver = new AppDriver(app);

        driver.ClickProfile("Test Profile 1");

        var selected = driver.TryFindProfile("Test Profile 1");
        Assert.NotNull(selected);
    }

    [Fact]
    public async Task Can_switch_between_profiles()
    {
        await using var env = await TestEnvironment.CreateAsync();
        await using var app = await AppProcess.StartAsync(env);
        var driver = new AppDriver(app);

        driver.ClickProfile("Test Profile 1");
        var profile1 = driver.TryFindProfile("Test Profile 1");
        Assert.NotNull(profile1);

        driver.ClickProfile("Test Profile 2");
        var profile2 = driver.TryFindProfile("Test Profile 2");
        Assert.NotNull(profile2);
    }
}
