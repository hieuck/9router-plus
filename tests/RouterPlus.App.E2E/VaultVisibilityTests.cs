using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;

namespace RouterPlus.App.E2E;

/// <summary>
/// Tests for vault unlock button visibility and state transitions.
/// </summary>
public sealed class VaultVisibilityTests
{
    [Fact]
    public async Task Vault_unlock_button_visible_when_locked()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        await using var app = await AppProcess.StartAsync(environment);

        var credentialsButton = app.MainWindow.FindFirstDescendant(cf =>
            cf.ByAutomationId("CredentialsManagerButton"));
        Assert.NotNull(credentialsButton);
        credentialsButton!.Click();

        var dialog = Retry.WhileNull(
            () => FindCredentialsManagerWindow(app),
            TimeSpan.FromSeconds(5),
            throwOnTimeout: false).Result;

        Assert.NotNull(dialog);

        // Vault should be locked initially
        var unlockButton = dialog!.FindFirstDescendant(cf =>
            cf.ByAutomationId("UnlockVaultButton"));

        Assert.NotNull(unlockButton);
        Assert.True(unlockButton.IsEnabled);
        Assert.Contains("Unlock", unlockButton.Name, StringComparison.OrdinalIgnoreCase);

        var status = dialog.FindFirstDescendant(cf =>
            cf.ByAutomationId("CredentialsManagerStatus"))?.AsLabel();

        Assert.NotNull(status);
        Assert.Contains("locked", status!.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Vault_unlock_button_hidden_after_successful_unlock()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        await using var app = await AppProcess.StartAsync(environment);

        var credentialsButton = app.MainWindow.FindFirstDescendant(cf =>
            cf.ByAutomationId("CredentialsManagerButton"));
        Assert.NotNull(credentialsButton);
        credentialsButton!.Click();

        var dialog = Retry.WhileNull(
            () => FindCredentialsManagerWindow(app),
            TimeSpan.FromSeconds(5),
            throwOnTimeout: false).Result;

        Assert.NotNull(dialog);

        var unlockButton = dialog!.FindFirstDescendant(cf =>
            cf.ByAutomationId("UnlockVaultButton"));
        Assert.NotNull(unlockButton);

        // Click unlock
        unlockButton!.Click();
        await Task.Delay(1500);

        // Password dialog should appear
        var passwordDialog = Retry.WhileNull(
            () => app.Desktop.FindAllChildren(cf => cf.ByControlType(ControlType.Window))
                .Concat(app.Desktop.FindAllDescendants(cf => cf.ByControlType(ControlType.Window)))
                .FirstOrDefault(w =>
                {
                    try
                    {
                        return w.Name.Contains("Unlock Google Vault", StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        return false;
                    }
                }),
            TimeSpan.FromSeconds(3),
            throwOnTimeout: false).Result;

        Assert.NotNull(passwordDialog);

        var passwordBox = passwordDialog!.FindFirstDescendant(cf =>
            cf.ByAutomationId("VaultPasswordBox"));
        Assert.NotNull(passwordBox);

        // Enter password and unlock
        passwordBox!.AsTextBox().Text = "test-password";

        var unlockDialogButton = passwordDialog.FindFirstDescendant(cf =>
            cf.ByName("Unlock"));
        Assert.NotNull(unlockDialogButton);
        unlockDialogButton!.Click();

        await Task.Delay(2000);

        // Unlock button should now be hidden (collapsed elements return null in FlaUI)
        var unlockButtonAfter = dialog.FindFirstDescendant(cf =>
            cf.ByAutomationId("UnlockVaultButton"));

        Assert.Null(unlockButtonAfter);

        var status = dialog.FindFirstDescendant(cf =>
            cf.ByAutomationId("CredentialsManagerStatus"))?.AsLabel();

        Assert.NotNull(status);
        Assert.Contains("unlocked", status!.Text, StringComparison.OrdinalIgnoreCase);
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
}
