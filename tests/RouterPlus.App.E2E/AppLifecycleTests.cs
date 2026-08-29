namespace RouterPlus.App.E2E;

/// <summary>
/// Tests for application startup and shutdown.
/// </summary>
public sealed class AppLifecycleTests
{
    [Fact]
    public async Task App_starts_with_harness_environment()
    {
        await using var env = await TestEnvironment.CreateAsync();
        await using var app = await AppProcess.StartAsync(env);

        Assert.Equal("9Router Profile Tool", app.MainWindow.Title);
    }

    [Fact]
    public async Task App_loads_synthetic_profiles()
    {
        await using var env = await TestEnvironment.CreateAsync();
        await using var app = await AppProcess.StartAsync(env);
        var driver = new AppDriver(app);

        var profile1 = driver.TryFindProfile("Harness Alpha");
        var profile2 = driver.TryFindProfile("Harness Beta");

        Assert.NotNull(profile1);
        Assert.NotNull(profile2);
    }
}
