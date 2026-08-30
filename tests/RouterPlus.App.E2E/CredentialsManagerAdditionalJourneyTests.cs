using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;

namespace RouterPlus.App.E2E;

/// <summary>
/// Additional user journeys for Credentials Manager provider navigation and removal.
/// </summary>
public sealed class CredentialsManagerAdditionalJourneyTests
{
    [Fact]
    public async Task User_can_switch_through_provider_tabs_and_see_profile_rows()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        await using var app = await AppProcess.StartAsync(environment);

        try
        {
            var dialog = await OpenCredentialsManagerAsync(app);

            var tabs = new[]
            {
                ("GoogleAccountsTab", "GoogleAccountsList"),
                ("CodexTab", "CodexConnectionsList"),
                ("KiroTab", "KiroConnectionsList"),
                ("GitHubTab", "GitHubConnectionsList"),
                ("OpenRouterTab", "OpenRouterConnectionsList")
            };

            foreach (var (tabAutomationId, listAutomationId) in tabs)
            {
                var tab = dialog.FindFirstDescendant(cf => cf.ByAutomationId(tabAutomationId));
                Assert.NotNull(tab);
                tab!.Click();

                var list = Retry.WhileNull(
                    () => dialog.FindFirstDescendant(cf => cf.ByAutomationId(listAutomationId)),
                    TimeSpan.FromSeconds(3),
                    throwOnTimeout: false).Result;
                Assert.NotNull(list);
                Assert.NotNull(list!.FindFirstDescendant(cf => cf.ByName("Harness Alpha")));
                Assert.NotNull(list.FindFirstDescendant(cf => cf.ByName("Harness Beta")));
            }

            await CloseCredentialsManagerAsync(app, dialog);
        }
        catch
        {
            await CaptureFailureAsync(environment, app);
            throw;
        }
    }

    [Fact]
    public async Task User_can_cancel_google_credential_removal()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        await using var app = await AppProcess.StartAsync(environment);

        try
        {
            var dialog = await OpenCredentialsManagerAsync(app);
            var googleList = dialog.FindFirstDescendant(cf => cf.ByAutomationId("GoogleAccountsList"));
            Assert.NotNull(googleList);

            var profile = googleList!.FindFirstDescendant(cf => cf.ByName("Harness Alpha"));
            Assert.NotNull(profile);
            profile!.Click();

            var removeButton = dialog.FindFirstDescendant(cf => cf.ByAutomationId("RemoveGoogleAccountButton"));
            Assert.NotNull(removeButton);
            removeButton!.Click();

            var confirmation = await FindWindowAsync(
                app,
                window => window.Name.Contains("Remove Google Account", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(confirmation);

            var noButton = confirmation!.FindFirstDescendant(cf =>
                cf.ByControlType(ControlType.Button).And(cf.ByName("No")));
            Assert.NotNull(noButton);
            noButton!.Click();

            var editButton = googleList.FindFirstDescendant(cf =>
                cf.ByControlType(ControlType.Button).And(cf.ByName("Edit")));
            Assert.NotNull(editButton);
            await CloseCredentialsManagerAsync(app, dialog);
        }
        catch
        {
            await CaptureFailureAsync(environment, app);
            throw;
        }
    }

    [Fact]
    public async Task User_can_remove_a_google_credential_from_credentials_manager()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        await using var app = await AppProcess.StartAsync(environment);

        try
        {
            var dialog = await OpenCredentialsManagerAsync(app);
            var googleList = dialog.FindFirstDescendant(cf => cf.ByAutomationId("GoogleAccountsList"));
            Assert.NotNull(googleList);

            var profile = googleList!.FindFirstDescendant(cf => cf.ByName("Harness Alpha"));
            Assert.NotNull(profile);
            profile!.Click();

            var removeButton = dialog.FindFirstDescendant(cf => cf.ByAutomationId("RemoveGoogleAccountButton"));
            Assert.NotNull(removeButton);
            Assert.True(removeButton!.IsEnabled);
            removeButton.Click();

            var confirmation = Retry.WhileNull(
                () => app.Desktop.FindAllDescendants(cf => cf.ByControlType(ControlType.Window))
                    .Concat(app.MainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Window)))
                    .FirstOrDefault(window =>
                    {
                        try
                        {
                            return window.Name.Contains("Remove Google Account", StringComparison.OrdinalIgnoreCase)
                                || window.Name.Contains("Remove Google account", StringComparison.OrdinalIgnoreCase);
                        }
                        catch
                        {
                            return false;
                        }
                    }),
                TimeSpan.FromSeconds(3),
                throwOnTimeout: false).Result;
            Assert.NotNull(confirmation);

            var yesButton = confirmation!.FindFirstDescendant(cf =>
                cf.ByControlType(ControlType.Button).And(cf.ByName("Yes")));
            Assert.NotNull(yesButton);
            yesButton!.Click();

            var status = Retry.WhileNull(
                () =>
                {
                    var element = dialog.FindFirstDescendant(cf => cf.ByAutomationId("CredentialsManagerStatus"));
                    try
                    {
                        return element?.Name.Contains("Removed Google account", StringComparison.OrdinalIgnoreCase) == true
                            ? element
                            : null;
                    }
                    catch
                    {
                        return null;
                    }
                },
                TimeSpan.FromSeconds(5),
                throwOnTimeout: false).Result;
            Assert.NotNull(status);

            var editButton = googleList.FindFirstDescendant(cf =>
                cf.ByControlType(ControlType.Button).And(cf.ByName("Edit")));
            Assert.NotNull(editButton);

            await CloseCredentialsManagerAsync(app, dialog);
        }
        catch
        {
            await CaptureFailureAsync(environment, app);
            throw;
        }
    }

    private static Task<AutomationElement> OpenCredentialsManagerAsync(AppProcess app)
    {
        var button = app.MainWindow.FindFirstDescendant(cf => cf.ByAutomationId("CredentialsManagerButton"));
        Assert.NotNull(button);
        button!.Click();

        var dialog = Retry.WhileNull(
            () => app.Desktop.FindAllDescendants(cf => cf.ByControlType(ControlType.Window))
                .FirstOrDefault(window =>
                {
                    try
                    {
                        return window.Name.Contains("Credentials Manager", StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        return false;
                    }
                }),
            TimeSpan.FromSeconds(5),
            throwOnTimeout: false).Result;
        Assert.NotNull(dialog);
        return Task.FromResult(dialog!);
    }

    private static Task<AutomationElement?> FindWindowAsync(
        AppProcess app,
        Func<AutomationElement, bool> predicate)
    {
        var window = Retry.WhileNull(
            () => app.Desktop.FindAllDescendants(cf => cf.ByControlType(ControlType.Window))
                .FirstOrDefault(candidate =>
                {
                    try
                    {
                        return predicate(candidate);
                    }
                    catch
                    {
                        return false;
                    }
                }),
            TimeSpan.FromSeconds(3),
            throwOnTimeout: false).Result;
        return Task.FromResult(window);
    }

    private static async Task CloseCredentialsManagerAsync(AppProcess app, AutomationElement dialog)
    {
        var closeButton = dialog.FindFirstDescendant(cf => cf.ByAutomationId("CredentialsManagerCloseButton"));
        Assert.NotNull(closeButton);
        closeButton!.Click();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var open = app.Desktop.FindAllDescendants(cf => cf.ByControlType(ControlType.Window))
                .Any(window =>
                {
                    try
                    {
                        return window.Name.Contains("Credentials Manager", StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        return false;
                    }
                });
            if (!open)
            {
                return;
            }

            await Task.Delay(50);
        }

        Assert.Fail("Credentials Manager dialog did not close within five seconds.");
    }

    private static async Task CaptureFailureAsync(TestEnvironment environment, AppProcess app)
    {
        var windows = app.Desktop.FindAllDescendants(cf => cf.ByControlType(ControlType.Window));
        var snapshot = string.Join(
            Environment.NewLine,
            windows.Select(window =>
            {
                try
                {
                    var controls = window.FindAllDescendants(cf => cf.ByControlType(ControlType.Button))
                        .Select(button =>
                        {
                            try
                            {
                                return $"  Button: '{button.Name}'";
                            }
                            catch
                            {
                                return "  Button: <unavailable>";
                            }
                        });
                    return $"Window: '{window.Name}'{Environment.NewLine}{string.Join(Environment.NewLine, controls)}";
                }
                catch
                {
                    return "Window: <unavailable>";
                }
            }));
        await File.WriteAllTextAsync(
            Path.Combine(environment.RootPath, "credentials-manager-additional-failure.log"),
            snapshot);
    }
}
