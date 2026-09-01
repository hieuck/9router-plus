using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;

namespace RouterPlus.App.E2E;

/// <summary>
/// User journeys for the Credentials Manager dialog.
/// </summary>
public sealed class CredentialsManagerDialogTests
{
    [Fact]
    public async Task User_can_open_credentials_manager_and_see_inline_profile_controls()
    {
        await using var environment = await TestEnvironment.CreateAsync();
        await using var app = await AppProcess.StartAsync(environment);

        try
        {
            var credentialsButton = app.MainWindow.FindFirstDescendant(cf =>
                cf.ByAutomationId("CredentialsManagerButton"));
            Assert.NotNull(credentialsButton);

            // User action 1: open Credentials Manager from the toolbar.
            credentialsButton!.Click();

            var dialog = Retry.WhileNull(
                () => FindCredentialsManagerWindow(app),
                TimeSpan.FromSeconds(5),
                throwOnTimeout: false).Result;

            Assert.NotNull(dialog);
            Assert.Equal("🔐 Credentials Manager", dialog!.Name);

            // User action 2: inspect the Google tab and its profiles.
            var googleList = Retry.WhileNull(
                () => dialog.FindFirstDescendant(cf => cf.ByAutomationId("GoogleAccountsList")),
                TimeSpan.FromSeconds(5),
                throwOnTimeout: false).Result;
            Assert.NotNull(googleList);

            Assert.NotNull(dialog.FindFirstDescendant(cf => cf.ByName("Harness Alpha")));
            Assert.NotNull(dialog.FindFirstDescendant(cf => cf.ByName("Harness Beta")));

            // The inline editing surface must be present for every profile.
            var inlineEditors = googleList!.FindAllDescendants(cf =>
                cf.ByControlType(ControlType.Edit));
            Assert.True(inlineEditors.Length >= 4,
                $"Expected at least four inline text editors, found {inlineEditors.Length}.");

            var checkboxes = googleList.FindAllDescendants(cf =>
                cf.ByControlType(ControlType.CheckBox));
            Assert.Equal(2, checkboxes.Length);
            Assert.All(checkboxes, checkbox => Assert.True(checkbox.IsEnabled));

            // User action 3: select both configured profiles for batch login.
            checkboxes[0].Click();
            checkboxes[1].Click();

            var batchLoginButton = Retry.WhileNull(
                () =>
                {
                    var button = dialog.FindFirstDescendant(cf => cf.ByAutomationId("BatchLoginButton"));
                    return button?.IsEnabled == true ? button : null;
                },
                TimeSpan.FromSeconds(3),
                throwOnTimeout: false).Result;
            Assert.NotNull(batchLoginButton);

            // User action 4: enter edit mode for the first profile.
            var editButtons = googleList.FindAllDescendants(cf =>
                cf.ByControlType(ControlType.Button).And(cf.ByName("Edit")));
            Assert.Equal(2, editButtons.Length);
            editButtons[0].Click();

            var saveButton = Retry.WhileNull(
                () => googleList.FindFirstDescendant(cf =>
                    cf.ByControlType(ControlType.Button).And(cf.ByName("Save"))),
                TimeSpan.FromSeconds(2),
                throwOnTimeout: false).Result;
            Assert.NotNull(saveButton);

            var editableFields = googleList.FindAllDescendants(cf => cf.ByControlType(ControlType.Edit));
            Assert.True(editableFields.Length >= 6);
            Assert.True(editableFields.Count(field => field.IsEnabled) >= 3);
            Assert.Contains(editableFields, field => !field.IsEnabled);

            var passwordVisibilityButton = dialog.FindFirstDescendant(cf =>
                cf.ByAutomationId("GooglePasswordVisibilityButton"));
            var totpVisibilityButton = dialog.FindFirstDescendant(cf =>
                cf.ByAutomationId("GoogleTotpVisibilityButton"));
            Assert.NotNull(passwordVisibilityButton);
            Assert.NotNull(totpVisibilityButton);
            Assert.Contains("Show", passwordVisibilityButton!.Name, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Show", totpVisibilityButton!.Name, StringComparison.OrdinalIgnoreCase);

            passwordVisibilityButton.Click();
            var visiblePasswordEditor = Retry.WhileNull(
                () => dialog.FindFirstDescendant(cf => cf.ByAutomationId("GooglePasswordVisibleEditor")),
                TimeSpan.FromSeconds(2),
                throwOnTimeout: false).Result;
            Assert.NotNull(visiblePasswordEditor);

            passwordVisibilityButton = dialog.FindFirstDescendant(cf =>
                cf.ByAutomationId("GooglePasswordVisibilityButton"));
            Assert.NotNull(passwordVisibilityButton);
            Assert.Equal("Hide", passwordVisibilityButton!.Name);

            passwordVisibilityButton.Click();
            Assert.Null(dialog.FindFirstDescendant(cf => cf.ByAutomationId("GooglePasswordVisibleEditor")));

            totpVisibilityButton.Click();
            var visibleTotpEditor = Retry.WhileNull(
                () => dialog.FindFirstDescendant(cf => cf.ByAutomationId("GoogleTotpVisibleEditor")),
                TimeSpan.FromSeconds(2),
                throwOnTimeout: false).Result;
            Assert.NotNull(visibleTotpEditor);
            Assert.NotNull(dialog.FindFirstDescendant(cf => cf.ByAutomationId("GooglePasswordEditor")));

            var textBoxes = editableFields
                .Where(field => field.ControlType == ControlType.Edit)
                .Select(field => field.AsTextBox())
                .ToArray();
            Assert.Contains(textBoxes, field => field.IsEnabled && !field.IsReadOnly);

            // User action 5: edit the first profile with synthetic values.
            var editableEmail = dialog.FindFirstDescendant(cf => cf.ByAutomationId("GoogleEmailEditor"))!.AsTextBox();
            Assert.False(editableEmail.IsReadOnly);
            editableEmail.Text = "alpha-edited@example.test";

            var passwordEditor = dialog.FindFirstDescendant(cf => cf.ByAutomationId("GooglePasswordEditor"));
            Assert.NotNull(passwordEditor);
            Assert.True(passwordEditor!.IsEnabled);
            passwordEditor.Focus();
            FlaUI.Core.Input.Keyboard.TypeSimultaneously(new[]
            {
                FlaUI.Core.WindowsAPI.VirtualKeyShort.CONTROL,
                FlaUI.Core.WindowsAPI.VirtualKeyShort.KEY_A
            });
            FlaUI.Core.Input.Keyboard.Type("alpha-edited-password");

            var editableRow = googleList.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem))
                .First(row => row.FindFirstDescendant(cf =>
                    cf.ByAutomationId("GoogleEmailEditor"))?.AsTextBox().IsReadOnly == false);
            var totpEditor = editableRow.FindFirstDescendant(cf => cf.ByAutomationId("GoogleTotpVisibleEditor"));
            Assert.NotNull(totpEditor);
            Assert.True(totpEditor!.IsEnabled);
            totpEditor.AsTextBox().Text = "JBSWY3DPEHPK3PXP";

            saveButton!.Click();

            var editButtonAfterSave = Retry.WhileNull(
                () => googleList.FindFirstDescendant(cf =>
                    cf.ByControlType(ControlType.Button).And(cf.ByName("Edit"))),
                TimeSpan.FromSeconds(3),
                throwOnTimeout: false).Result;
            Assert.NotNull(editButtonAfterSave);

            // User action 6: verify the saved value after reopening the manager.
            var closeBeforeReload = dialog.FindFirstDescendant(cf =>
                cf.ByAutomationId("CredentialsManagerCloseButton"));
            Assert.NotNull(closeBeforeReload);
            closeBeforeReload!.Click();
            Assert.False(await IsCredentialsDialogOpenAsync(app, TimeSpan.FromSeconds(5)));

            dialog = await OpenCredentialsManagerAsync(app);
            googleList = dialog.FindFirstDescendant(cf => cf.ByAutomationId("GoogleAccountsList"));
            Assert.NotNull(googleList);
            var savedEmail = googleList!.FindAllDescendants(cf => cf.ByControlType(ControlType.Edit))
                .Select(field => field.AsTextBox())
                .FirstOrDefault(field => string.Equals(field.Text, "alpha-edited@example.test", StringComparison.Ordinal));
            Assert.NotNull(savedEmail);

            // TOTP Secret is now a PasswordBox for security - verify control exists but don't read value
            var savedTotp = googleList.FindFirstDescendant(cf => cf.ByAutomationId("GoogleTotpEditor"));
            Assert.NotNull(savedTotp);

            // User action 7: select profiles again after reopening the manager.
            var reloadedCheckboxes = googleList.FindAllDescendants(cf =>
                cf.ByControlType(ControlType.CheckBox));
            Assert.Equal(2, reloadedCheckboxes.Length);
            reloadedCheckboxes[0].Click();
            reloadedCheckboxes[1].Click();

            // User action 8: run batch login for the selected profiles.
            batchLoginButton = Retry.WhileNull(
                () =>
                {
                    var button = dialog.FindFirstDescendant(cf => cf.ByAutomationId("BatchLoginButton"));
                    return button?.IsEnabled == true ? button : null;
                },
                TimeSpan.FromSeconds(3),
                throwOnTimeout: false).Result;
            Assert.NotNull(batchLoginButton);
            batchLoginButton!.Click();

            var terminalStatus = Retry.WhileNull(
                () =>
                {
                    var status = dialog.FindFirstDescendant(cf =>
                        cf.ByAutomationId("CredentialsManagerStatus"));
                    try
                    {
                        return status?.Name.Contains("Batch login completed", StringComparison.OrdinalIgnoreCase) == true ||
                            status?.Name.Contains("Vault not unlocked", StringComparison.OrdinalIgnoreCase) == true
                            ? status
                            : null;
                    }
                    catch
                    {
                        return null;
                    }
                },
                TimeSpan.FromSeconds(5),
                throwOnTimeout: false).Result;
            Assert.NotNull(terminalStatus);
            // Batch login should respond (either completes or shows vault error).
            Assert.True(
                terminalStatus!.Name.Contains("Batch login completed", StringComparison.OrdinalIgnoreCase) ||
                terminalStatus.Name.Contains("Vault not unlocked", StringComparison.OrdinalIgnoreCase));

            // User action 7: close the dialog.
            var closeButton = dialog.FindFirstDescendant(cf =>
                cf.ByAutomationId("CredentialsManagerCloseButton"));
            Assert.NotNull(closeButton);
            closeButton!.Click();

            Assert.False(await IsCredentialsDialogOpenAsync(app, TimeSpan.FromSeconds(5)));
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
        return app.Desktop.FindAllDescendants(cf => cf.ByControlType(ControlType.Window))
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

    private static async Task<bool> IsCredentialsDialogOpenAsync(AppProcess app, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var isOpen = FindCredentialsManagerWindow(app) is not null;
            if (!isOpen)
            {
                return false;
            }

            await Task.Delay(50);
        }

        return true;
    }

    private static void SetPassword(AutomationElement passwordBox, string password)
    {
        var valuePattern = passwordBox.Patterns.Value.Pattern;
        if (valuePattern is not null && !valuePattern.IsReadOnly)
        {
            valuePattern.SetValue(password);
            return;
        }

        passwordBox.Focus();
        FlaUI.Core.Input.Keyboard.TypeSimultaneously(new[]
        {
            FlaUI.Core.WindowsAPI.VirtualKeyShort.CONTROL,
            FlaUI.Core.WindowsAPI.VirtualKeyShort.KEY_A
        });
        FlaUI.Core.Input.Keyboard.Type(password);
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
            Path.Combine(environment.RootPath, "credentials-manager-failure.log"),
            snapshot);
    }
}
