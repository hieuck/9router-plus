using System.Diagnostics;
using FlaUI.Core.Definitions;
using Xunit;
using Xunit.Abstractions;

namespace RouterPlus.App.E2E;

public class ProfileContextMenuPerformanceTests
{
    private readonly ITestOutputHelper _output;

    public ProfileContextMenuPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Profile_right_click_should_be_fast()
    {
        // Arrange
        await using var env = await TestEnvironment.CreateAsync();
        await using var app = await AppProcess.StartAsync(env);
        var driver = new AppDriver(app);

        var profileList = driver.FindProfileList();
        var firstProfile = profileList.FindFirstDescendant(cf => cf.ByControlType(ControlType.ListItem));

        Assert.NotNull(firstProfile);

        // Act - measure right-click to context menu appearance
        var stopwatch = Stopwatch.StartNew();
        firstProfile.RightClick();

        // Poll for context menu with timeout - try multiple search strategies
        var automation = app.MainWindow.Automation;
        var contextMenu = null as FlaUI.Core.AutomationElements.AutomationElement;
        var timeout = TimeSpan.FromSeconds(6);
        var endTime = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < endTime && contextMenu == null)
        {
            // Try AutomationId first
            contextMenu = automation.GetDesktop().FindFirstDescendant(cf => cf.ByAutomationId("ProfileContextMenu"));

            // Fallback to ControlType.Menu
            if (contextMenu == null)
            {
                contextMenu = automation.GetDesktop().FindFirstChild(cf => cf.ByControlType(ControlType.Menu));
            }

            if (contextMenu == null)
            {
                await Task.Delay(50);
            }
        }

        stopwatch.Stop();

        // Assert
        _output.WriteLine($"Right-click latency: {stopwatch.ElapsedMilliseconds}ms");

        Assert.NotNull(contextMenu);
        Assert.True(stopwatch.ElapsedMilliseconds < 500,
            $"Right-click took {stopwatch.ElapsedMilliseconds}ms, expected < 500ms");

        // Cleanup
        app.MainWindow.Focus();
        await Task.Delay(100);
    }
}
