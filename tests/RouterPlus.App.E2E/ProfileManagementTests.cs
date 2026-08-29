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

        driver.ClickProfile("Harness Alpha");

        var selected = driver.TryFindProfile("Harness Alpha");
        Assert.NotNull(selected);
    }

    [Fact]
    public async Task Can_switch_between_profiles()
    {
        await using var env = await TestEnvironment.CreateAsync();
        await using var app = await AppProcess.StartAsync(env);
        var driver = new AppDriver(app);

        driver.ClickProfile("Harness Alpha");
        var profile1 = driver.TryFindProfile("Harness Alpha");
        Assert.NotNull(profile1);

        driver.ClickProfile("Harness Beta");
        var profile2 = driver.TryFindProfile("Harness Beta");
        Assert.NotNull(profile2);
    }
}
