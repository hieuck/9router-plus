using FlaUI.Core.Tools;

namespace RouterPlus.App.E2E;

public sealed class ProfileStateJourneyTests
{
    [Fact]
    public async Task Selecting_profile_updates_visible_selected_state()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        await using var app = await AppProcess.StartAsync(environment);
        var driver = new AppDriver(app);

        try
        {
            driver.ClickProfile("Harness Beta");

            Assert.Equal("Harness Beta", driver.ReadSelectedProfileName());
            Assert.True(driver.ReadProfileSelectionState("Harness Beta"));
            Assert.False(driver.ReadProfileSelectionState("Harness Alpha"));
        }
        catch
        {
            await app.Instrumentation.SaveFailureSnapshotAsync(app, nameof(Selecting_profile_updates_visible_selected_state));
            throw;
        }
    }

    [Fact]
    public async Task Searching_profile_updates_visible_profile_list()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        await using var app = await AppProcess.StartAsync(environment);
        var driver = new AppDriver(app);

        try
        {
            driver.SetProfileSearchText("Harness Alpha");
            Retry.WhileFalse(
                () => driver.ReadVisibleProfileNames().SequenceEqual(new[] { "Harness Alpha" }),
                TimeSpan.FromSeconds(3));

            Assert.Equal(new[] { "Harness Alpha" }, driver.ReadVisibleProfileNames());

            driver.SetProfileSearchText(string.Empty);
            Retry.WhileFalse(
                () => driver.ReadVisibleProfileNames().Count == 2,
                TimeSpan.FromSeconds(3));
            Assert.Equal(
                new[] { "Harness Alpha", "Harness Beta" },
                driver.ReadVisibleProfileNames());
        }
        catch
        {
            await app.Instrumentation.SaveFailureSnapshotAsync(app, nameof(Searching_profile_updates_visible_profile_list));
            throw;
        }
    }

    [Fact]
    public async Task Select_all_button_toggles_profile_checkbox_state()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        await using var app = await AppProcess.StartAsync(environment);
        var driver = new AppDriver(app);

        try
        {
            driver.EnableMultiSelectMode();
            driver.ClickSelectAll();
            Assert.All(driver.ReadProfileCheckboxStates(), Assert.True);

            driver.ClickSelectAll();
            Assert.All(driver.ReadProfileCheckboxStates(), Assert.False);
        }
        catch
        {
            await app.Instrumentation.SaveFailureSnapshotAsync(app, nameof(Select_all_button_toggles_profile_checkbox_state));
            throw;
        }
    }
}
