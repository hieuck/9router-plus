namespace RouterPlus.App.E2E;

/// <summary>
/// Tests for profile selection and interaction.
/// </summary>
[Trait("Category", "E2E")]
public sealed class ProfileManagementTests
{
    [E2EFact]
    public async Task Can_select_profile()
    {
        await using var env = await TestEnvironment.CreateAsync();
        await using var app = await AppProcess.StartAsync(env);
        var driver = new AppDriver(app);

        driver.ClickProfile("Harness Alpha");

        var selected = driver.TryFindProfile("Harness Alpha");
        Assert.NotNull(selected);
    }

    [E2EFact]
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

