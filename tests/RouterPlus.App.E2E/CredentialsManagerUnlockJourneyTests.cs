using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;

namespace RouterPlus.App.E2E;

/// <summary>
/// User journeys for unlocking the credentials vault and logging in from a single row.
/// </summary>
public sealed class CredentialsManagerUnlockJourneyTests
{
    [Fact]
    public async Task User_can_unlock_vault_and_see_credentials_loaded()
    {
        await using var environment = await TestEnvironment.CreateAsync(rememberVault: false);
        await using var app = await AppProcess.StartAsync(environment);

        try
        {
            var dialog = await OpenCredentialsManagerAsync(app);
            var unlockButton = WaitForDescendant(
                dialog,
                () => dialog.FindFirstDescendant(cf => cf.ByAutomationId("UnlockVaultButton")),
                TimeSpan.FromSeconds(5));
            Assert.NotNull(unlockButton);
            Assert.True(unlockButton!.IsEnabled);

            var googleList = WaitForDescendant(
                dialog,
                () => dialog.FindFirstDescendant(cf => cf.ByAutomationId("GoogleAccountsList")),
                TimeSpan.FromSeconds(5));
            Assert.NotNull(googleList);
            var rows = googleList!.FindAllChildren(cf =>
                cf.ByControlType(ControlType.ListItem));
            Assert.Equal(2, rows.Length);
            var lockedLoginButtons = rows
                .Select(row => row.FindFirstDescendant(cf =>
                    cf.ByAutomationId("GoogleLoginRowButton")))
                .ToArray();
            Assert.All(lockedLoginButtons, button =>
            {
                Assert.NotNull(button);
                Assert.False(button!.IsEnabled);
            });

            unlockButton.Click();
            var passwordDialog = await FindWindowAsync(
                app,
                window => window.Name.Contains("Unlock Google Vault", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(passwordDialog);

            var passwordBox = passwordDialog!.FindFirstDescendant(cf => cf.ByAutomationId("VaultPasswordBox"));
            Assert.NotNull(passwordBox);
            passwordDialog.Focus();
            passwordDialog.SetForeground();
            SetPassword(passwordBox!, "harness-vault-password");

            var unlockDialogButton = passwordDialog.FindFirstDescendant(cf =>
                cf.ByControlType(ControlType.Button).And(cf.ByName("Unlock")));
            Assert.NotNull(unlockDialogButton);
            unlockDialogButton!.Click();

            var status = await WaitForStatusAsync(
                dialog,
                text => text.Contains("Vault unlocked", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("Unable to unlock vault", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("Vault unlocked", status, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("2 configured profiles", status, StringComparison.OrdinalIgnoreCase);

            rows = googleList.FindAllChildren(cf =>
                cf.ByControlType(ControlType.ListItem));
            Assert.Equal(2, rows.Length);
            var loadedLoginButtons = rows
                .Select(row => row.FindFirstDescendant(cf =>
                    cf.ByAutomationId("GoogleLoginRowButton")))
                .ToArray();
            Assert.All(loadedLoginButtons, button =>
            {
                Assert.NotNull(button);
                Assert.True(button!.IsEnabled);
            });

            await CloseCredentialsManagerAsync(app, dialog);
        }
        catch
        {
            await CaptureFailureAsync(environment, app);
            throw;
        }
    }

    [Fact]
    public async Task User_can_remember_vault_unlock_on_device()
    {
        await using var environment = await TestEnvironment.CreateAsync(rememberVault: false);
        await using var app = await AppProcess.StartAsync(environment);

        try
        {
            var dialog = await OpenCredentialsManagerAsync(app);
            var unlockButton = WaitForDescendant(
                dialog,
                () => dialog.FindFirstDescendant(cf => cf.ByAutomationId("UnlockVaultButton")),
                TimeSpan.FromSeconds(5));
            Assert.NotNull(unlockButton);
            unlockButton!.Click();

            var passwordDialog = await FindWindowAsync(
                app,
                window => window.Name.Contains("Unlock Google Vault", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(passwordDialog);

            var passwordBox = passwordDialog!.FindFirstDescendant(cf => cf.ByAutomationId("VaultPasswordBox"));
            var rememberCheckBox = passwordDialog.FindFirstDescendant(cf =>
                cf.ByAutomationId("RememberVaultCheckBox"));
            Assert.NotNull(passwordBox);
            Assert.NotNull(rememberCheckBox);
            Assert.NotNull(rememberCheckBox!.Patterns.Toggle);

            passwordDialog.Focus();
            passwordDialog.SetForeground();
            SetPassword(passwordBox!, "harness-vault-password");
            rememberCheckBox.Click();
            await Task.Delay(100);

            var unlockDialogButton = passwordDialog.FindFirstDescendant(cf =>
                cf.ByControlType(ControlType.Button).And(cf.ByName("Unlock")));
            Assert.NotNull(unlockDialogButton);
            unlockDialogButton!.Click();

            await WaitForStatusAsync(
                dialog,
                text => text.Contains("Vault unlocked", StringComparison.OrdinalIgnoreCase));
            Assert.True(File.Exists(Path.Combine(environment.RootPath, "Vault", "google-accounts.remembered")));

            await CloseCredentialsManagerAsync(app, dialog);
        }
        catch
        {
            await CaptureFailureAsync(environment, app);
            throw;
        }
    }

    [Fact]
    public async Task User_can_login_one_profile_from_its_row()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        await using var app = await AppProcess.StartAsync(environment);

        try
        {
            var dialog = await OpenCredentialsManagerAsync(app);
            var googleList = dialog.FindFirstDescendant(cf => cf.ByAutomationId("GoogleAccountsList"));
            Assert.NotNull(googleList);

            var rows = googleList!.FindAllChildren(cf =>
                cf.ByControlType(ControlType.ListItem));
            Assert.Equal(2, rows.Length);

            var alphaRow = rows.Single(row =>
                row.FindFirstDescendant(cf => cf.ByName("Harness Alpha")) is not null);
            var loginButton = alphaRow.FindFirstDescendant(cf =>
                cf.ByAutomationId("GoogleLoginRowButton"));
            Assert.NotNull(loginButton);
            Assert.True(loginButton!.IsEnabled);

            loginButton.Click();
            var status = await WaitForStatusAsync(
                dialog,
                text => text.Contains("Harness Alpha", StringComparison.OrdinalIgnoreCase)
                    && text.Contains("Login successful", StringComparison.OrdinalIgnoreCase));
            Assert.Contains("Harness Alpha", status, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Login successful", status, StringComparison.OrdinalIgnoreCase);

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
            () => FindCredentialsManagerWindow(app),
            TimeSpan.FromSeconds(5),
            throwOnTimeout: false).Result;
        Assert.NotNull(dialog);
        return Task.FromResult(dialog!);
    }

    private static AutomationElement? FindCredentialsManagerWindow(AppProcess app)
    {
        return app.Desktop.FindAllChildren(cf => cf.ByControlType(ControlType.Window))
            .Concat(app.Desktop.FindAllDescendants(cf => cf.ByControlType(ControlType.Window)))
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
            });
    }


    private static AutomationElement? WaitForDescendant(
        AutomationElement container,
        Func<AutomationElement?> finder,
        TimeSpan timeout)
    {
        return Retry.WhileNull(finder, timeout, throwOnTimeout: false).Result;
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
            TimeSpan.FromSeconds(5),
            throwOnTimeout: false).Result;
        return Task.FromResult(window);
    }

    private static void SetPassword(AutomationElement passwordBox, string password)
    {
        var valuePattern = passwordBox.Patterns.Value.Pattern;
        if (valuePattern is not null && !valuePattern.IsReadOnly)
        {
            valuePattern.SetValue(password);
            return;
        }

        passwordBox.SetForeground();
        passwordBox.Focus();
        Keyboard.TypeSimultaneously(new[]
        {
            VirtualKeyShort.CONTROL,
            VirtualKeyShort.KEY_A
        });
        Keyboard.Type(password);
        Keyboard.Press(VirtualKeyShort.TAB);
    }


    private static async Task<string> WaitForStatusAsync(
        AutomationElement dialog,
        Func<string, bool> predicate)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var status = dialog.FindFirstDescendant(cf => cf.ByAutomationId("CredentialsManagerStatus"));
            try
            {
                if (status is not null && predicate(status.Name))
                {
                    return status.Name;
                }
            }
            catch
            {
                // The dialog may be refreshing its visual tree.
            }

            await Task.Delay(50);
        }

        var finalStatus = dialog.FindFirstDescendant(cf => cf.ByAutomationId("CredentialsManagerStatus"));
        Assert.True(finalStatus is not null && predicate(finalStatus.Name),
            "Expected credentials manager status was not reached within ten seconds.");
        return finalStatus!.Name;
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
        var snapshot = string.Join(
            Environment.NewLine,
            app.Desktop.FindAllDescendants(cf => cf.ByControlType(ControlType.Window))
                .Select(window =>
                {
                    try
                    {
                        return $"Window: '{window.Name}'";
                    }
                    catch
                    {
                        return "Window: <unavailable>";
                    }
                }));

        await File.WriteAllTextAsync(
            Path.Combine(environment.RootPath, "credentials-manager-unlock-failure.log"),
            snapshot);
    }
}
