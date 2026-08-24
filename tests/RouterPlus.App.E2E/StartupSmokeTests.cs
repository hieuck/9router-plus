namespace RouterPlus.App.E2E;

public sealed class StartupSmokeTests
{
    [Fact]
    public async Task Harness_starts_routerplus_with_synthetic_environment()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        await using var app = await RouterPlusProcess.StartAsync(environment);

        Assert.Equal("9Router Profile Tool", app.MainWindow.Title);
    }
}
