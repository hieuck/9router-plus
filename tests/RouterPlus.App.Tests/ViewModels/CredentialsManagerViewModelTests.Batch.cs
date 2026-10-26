using RouterPlus.Core.Chrome;
using RouterPlus.Core.Models;
using RouterPlus.Core.Security;
using RouterPlus.Core.Providers;
using RouterPlus.Infrastructure.Security;
using RouterPlus.Infrastructure.Storage;
using RouterPlus.App.ViewModels;
using RouterPlus.App.Services;

namespace RouterPlus.App.Tests.ViewModels;

public sealed partial class CredentialsManagerViewModelTests
{
    [Fact]
    public async Task BatchLoginCommand_uses_the_shared_google_authentication_runner_for_each_selected_row()
    {
        var receivedProfiles = new List<ChromeProfile>();
        GoogleLoginCredential? receivedCredential = null;
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile",
            "user@example.test",
            "synthetic-login-password",
            "NONE"));
        var viewModel = CreateViewModel((profile, credential, _) =>
        {
            receivedProfiles.Add(profile);
            receivedCredential = credential;
            return Task.FromResult(GoogleLoginResult.Success());
        });

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.GoogleAccounts);
        row.IsSelected = true;

        viewModel.BatchLoginCommand.Execute(null);
        await WaitForAsync(() => viewModel.StatusMessage.Contains("Batch login completed", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(new[] { _profile }, receivedProfiles);
        Assert.NotNull(receivedCredential);
        Assert.Equal(_profile.Id, receivedCredential!.ProfileId);
        Assert.Equal("user@example.test", receivedCredential.Email);
        Assert.Equal("synthetic-login-password", receivedCredential.Password);
        Assert.Equal("NONE", receivedCredential.TotpSecret);
        Assert.False(viewModel.IsBatchLoginRunning);
        Assert.Contains("1 succeeded", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task BatchLoginCommand_runs_selected_rows_sequentially_and_continues_after_failure()
    {
        var secondProfile = new ChromeProfile(
            ChromeProfile.CreateId(_rootDirectory, "Profile 2"),
            "Test Profile 2",
            "Profile 2",
            _rootDirectory,
            true);
        _mainViewModel.Profiles.Add(secondProfile);
        _mainViewModel.FilteredProfiles.Add(secondProfile);

        var receivedProfiles = new List<ChromeProfile>();
        var receivedEmails = new List<string>();
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            _profile.Id,
            "first@example.test",
            "synthetic-login-password",
            "NONE"));
        var viewModel = CreateViewModel((profile, credential, _) =>
        {
            receivedProfiles.Add(profile);
            receivedEmails.Add(credential.Email);
            return Task.FromResult(
                credential.Email.StartsWith("first", StringComparison.Ordinal)
                    ? GoogleLoginResult.InvalidCredentials()
                    : GoogleLoginResult.Success());
        });

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 2);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var firstRow = viewModel.GoogleAccounts.Single(row => row.ProfileId == _profile.Id);
        firstRow.IsSelected = true;
        var secondRow = viewModel.GoogleAccounts.Single(row => row.ProfileId == secondProfile.Id);
        secondRow.Email = "second@example.test";
        secondRow.Password = "synthetic-login-password";
        secondRow.TotpSecret = "NONE";
        secondRow.HasCredentials = true;
        secondRow.IsSelected = true;

        viewModel.BatchLoginCommand.Execute(null);
        await WaitForAsync(() => viewModel.StatusMessage.Contains(
            "Batch login completed", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(new[] { _profile, secondProfile }, receivedProfiles);
        Assert.Equal(
            new[] { "first@example.test", "second@example.test" },
            receivedEmails);
        Assert.Contains("1 succeeded", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1 failed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task BatchLoginCommand_reports_locked_vault_without_invoking_automation()
    {
        // Arrange
        var invoked = false;
        var viewModel = CreateViewModel((_, _, _) =>
        {
            invoked = true;
            return Task.FromResult(GoogleLoginResult.Success());
        });
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        var row = Assert.Single(viewModel.GoogleAccounts);
        row.Email = "locked@example.test";
        row.Password = "synthetic-password";
        row.HasCredentials = true;
        row.IsSelected = true;

        // Act
        viewModel.BatchLoginCommand.Execute(null);
        await WaitForAsync(() => viewModel.StatusMessage.Contains(
            "Vault not unlocked", StringComparison.OrdinalIgnoreCase));

        // Assert
        Assert.False(invoked);
        Assert.Contains("Vault not unlocked", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.IsBatchLoginRunning);
    }
    [Fact]
    public async Task BatchLoginCommand_reports_missing_profile_and_completes_cleanup()
    {
        // Arrange
        await CreateVaultAsync("synthetic-password");
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.GoogleAccounts);
        row.ProfileId = "missing-profile-id";
        row.Email = "missing@example.test";
        row.Password = "synthetic-password";
        row.HasCredentials = true;
        row.IsSelected = true;

        // Act
        viewModel.BatchLoginCommand.Execute(null);
        await viewModel.BatchLoginTask!;

        // Assert
        Assert.Contains("Batch login completed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0 succeeded", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1 failed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.IsBatchLoginRunning);
        Assert.Null(viewModel.BatchLoginTask);
    }
    [Fact]
    public async Task BatchLoginCommand_reports_google_manual_intervention_as_failure()
    {
        // Arrange
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            _profile.Id, "manual@example.test", "synthetic-password", "NONE"));
        var viewModel = CreateViewModel((_, _, _) => Task.FromResult(
            GoogleLoginResult.ManualInterventionRequired("synthetic manual step")));
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.GoogleAccounts);
        row.IsSelected = true;

        // Act
        viewModel.BatchLoginCommand.Execute(null);
        await viewModel.BatchLoginTask!;

        // Assert
        Assert.Contains("Batch login completed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0 succeeded", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1 failed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task BatchLoginCommand_stops_after_google_runner_returns_cancelled()
    {
        // Arrange
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            _profile.Id, "cancelled@example.test", "synthetic-password", "NONE"));
        var invoked = false;
        var viewModel = CreateViewModel((_, _, _) =>
        {
            invoked = true;
            return Task.FromResult(GoogleLoginResult.Cancelled());
        });
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.GoogleAccounts);
        row.IsSelected = true;

        // Act
        viewModel.BatchLoginCommand.Execute(null);
        await viewModel.BatchLoginTask!;

        // Assert
        Assert.True(invoked);
        Assert.Contains("Batch login cancelled", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0 succeeded", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.IsBatchLoginRunning);
    }
    [Fact]
    public async Task DisposeAsync_cancels_an_active_batch_and_waits_for_runner()
    {
        // Arrange
        var runnerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runnerCancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            _profile.Id, "dispose@example.test", "synthetic-password", "NONE"));
        var viewModel = CreateViewModel((_, _, cancellationToken) =>
        {
            runnerStarted.TrySetResult(true);
            return WaitForCancellationAsync(cancellationToken, runnerCancelled);
        });
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.GoogleAccounts);
        row.IsSelected = true;
        viewModel.BatchLoginCommand.Execute(null);
        await runnerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act
        await viewModel.DisposeAsync();

        // Assert
        await runnerCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(viewModel.IsBatchLoginRunning);
        Assert.Null(viewModel.BatchLoginTask);
    }
    [Fact]
    public async Task CancelBatchLoginAsync_is_safe_when_no_batch_is_running()
    {
        // Arrange
        var viewModel = CreateViewModel();
        await WaitForAsync(() => viewModel.InitializationTask.IsCompleted);

        // Act
        await viewModel.CancelBatchLoginAsync();

        // Assert
        Assert.False(viewModel.IsBatchLoginRunning);
        Assert.Null(viewModel.BatchLoginTask);
    }
    [Fact]
    public async Task BatchLoginCommand_continues_when_a_selected_row_has_invalid_credentials()
    {
        var receivedEmails = new List<string>();
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile",
            "first@example.test",
            "synthetic-login-password",
            "NONE"));
        var viewModel = CreateViewModel((_, credential, _) =>
        {
            receivedEmails.Add(credential.Email);
            return Task.FromResult(GoogleLoginResult.Success());
        });

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var firstRow = Assert.Single(viewModel.GoogleAccounts);
        firstRow.IsSelected = true;
        firstRow.Email = "not-an-email";
        firstRow.Password = "synthetic-login-password";

        viewModel.GoogleAccounts.Add(new GoogleAccountRowViewModel
        {
            ProfileId = _profile.Id,
            ProfileName = _profile.Name,
            Email = "second@example.test",
            Password = "synthetic-login-password",
            TotpSecret = "NONE",
            HasCredentials = true,
            IsSelected = true,
            IsVaultUnlocked = true
        });

        viewModel.BatchLoginCommand.Execute(null);
        await WaitForAsync(() => viewModel.StatusMessage.Contains(
            "Batch login completed", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(new[] { "second@example.test" }, receivedEmails);
        Assert.Contains("1 succeeded", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1 failed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task StopBatchLoginCommand_wins_when_runner_returns_after_cancellation()
    {
        var runnerStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var runnerRelease = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile",
            "first@example.test",
            "synthetic-login-password",
            "NONE"));
        var viewModel = CreateViewModel(async (_, _, _) =>
        {
            runnerStarted.TrySetResult(true);
            await runnerRelease.Task;
            return GoogleLoginResult.Success();
        });

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.GoogleAccounts);
        row.IsSelected = true;

        viewModel.BatchLoginCommand.Execute(null);
        await runnerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.StopBatchLoginCommand.Execute(null);
        runnerRelease.SetResult(true);
        await WaitForAsync(() => viewModel.StatusMessage.Contains(
            "Batch login cancelled", StringComparison.OrdinalIgnoreCase));

        Assert.Contains("0 succeeded", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.IsBatchLoginRunning);
    }
    [Fact]
    public async Task StopBatchLoginCommand_cancels_active_runner_and_does_not_start_next_row()
    {
        var runnerStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var runnerCancelled = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var receivedEmails = new List<string>();
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile",
            "first@example.test",
            "synthetic-login-password",
            "NONE"));
        var viewModel = CreateViewModel((_, credential, cancellationToken) =>
        {
            receivedEmails.Add(credential.Email);
            runnerStarted.TrySetResult(true);
            return WaitForCancellationAsync(cancellationToken, runnerCancelled);
        });

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var firstRow = Assert.Single(viewModel.GoogleAccounts);
        firstRow.IsSelected = true;

        viewModel.GoogleAccounts.Add(new GoogleAccountRowViewModel
        {
            ProfileId = _profile.Id,
            ProfileName = _profile.Name,
            Email = "second@example.test",
            Password = "synthetic-login-password",
            TotpSecret = "NONE",
            HasCredentials = true,
            IsSelected = true,
            IsVaultUnlocked = true
        });

        viewModel.BatchLoginCommand.Execute(null);
        await runnerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        viewModel.StopBatchLoginCommand.Execute(null);
        await runnerCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForAsync(() => !viewModel.IsBatchLoginRunning);

        Assert.Equal(new[] { "first@example.test" }, receivedEmails);
        Assert.Contains("cancelled", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public async Task BatchLoginCommand_waits_for_current_runner_before_starting_next_row()
    {
        var firstRunnerCompleted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondRunnerStarted = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var receivedEmails = new List<string>();
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile",
            "first@example.test",
            "synthetic-login-password",
            "NONE"));
        var viewModel = CreateViewModel((_, credential, _) =>
        {
            receivedEmails.Add(credential.Email);
            if (credential.Email.StartsWith("first", StringComparison.Ordinal))
            {
                return firstRunnerCompleted.Task.ContinueWith(
                    _ => GoogleLoginResult.Success(),
                    TaskScheduler.Default);
            }

            secondRunnerStarted.TrySetResult(true);
            return Task.FromResult(GoogleLoginResult.Success());
        });

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var firstRow = Assert.Single(viewModel.GoogleAccounts);
        firstRow.IsSelected = true;
        viewModel.GoogleAccounts.Add(new GoogleAccountRowViewModel
        {
            ProfileId = _profile.Id,
            ProfileName = _profile.Name,
            Email = "second@example.test",
            Password = "synthetic-login-password",
            TotpSecret = "NONE",
            HasCredentials = true,
            IsSelected = true,
            IsVaultUnlocked = true
        });

        viewModel.BatchLoginCommand.Execute(null);
        await WaitForAsync(() => receivedEmails.Count == 1);
        Assert.False(secondRunnerStarted.Task.IsCompleted);

        firstRunnerCompleted.SetResult(true);
        await secondRunnerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForAsync(() => viewModel.StatusMessage.Contains(
            "Batch login completed", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(
            new[] { "first@example.test", "second@example.test" },
            receivedEmails);
    }
    [Fact]
    public async Task SaveRowCommand_is_disabled_while_batch_running()
    {
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile", "user@example.test", "synthetic-password", "NONE"));
        var runnerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runnerRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var automationCalls = 0;
        var viewModel = CreateViewModel(async (_, _, _) =>
        {
            Interlocked.Increment(ref automationCalls);
            runnerStarted.TrySetResult(true);
            await runnerRelease.Task;
            return GoogleLoginResult.Success();
        });

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.GoogleAccounts);
        row.IsSelected = true;

        viewModel.BatchLoginCommand.Execute(null);
        await runnerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act - try to save while batch is running
        var saveRow = new GoogleAccountRowViewModel
        {
            ProfileId = _profile.Id,
            ProfileName = _profile.Name,
            Email = "new@example.test",
            Password = "new-password",
            TotpSecret = "NONE"
        };

        try
        {
            // Real command behavior: the save button is disabled while a batch
            // runs, so executing it is skipped and the batch is undisturbed.
            Assert.False(viewModel.SaveRowCommand.CanExecute(saveRow));

            viewModel.SaveRowCommand.Execute(saveRow);
            await Task.Delay(50);

            Assert.True(viewModel.IsBatchLoginRunning);
            Assert.Equal(1, automationCalls);
        }
        finally
        {
            // Always release the runner: without this, a failed assert would
            // leave the batch task pending and DisposeAsync would hang forever.
            runnerRelease.TrySetResult(true);
        }

        await WaitForAsync(() => !viewModel.IsBatchLoginRunning);
    }
    [Fact]
    public async Task CheckHealthRowCommand_is_disabled_while_batch_running()
    {
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile", "user@example.test", "synthetic-password", "NONE"));
        var runnerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runnerRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var automationCalls = 0;
        var viewModel = CreateViewModel(async (_, _, _) =>
        {
            Interlocked.Increment(ref automationCalls);
            runnerStarted.TrySetResult(true);
            await runnerRelease.Task;
            return GoogleLoginResult.Success();
        });

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.GoogleAccounts);
        row.IsSelected = true;

        viewModel.BatchLoginCommand.Execute(null);
        await runnerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            // Real command behavior: the row button is disabled while a batch
            // runs, so executing it is skipped and the batch is undisturbed.
            Assert.False(viewModel.CheckHealthRowCommand.CanExecute(row));

            viewModel.CheckHealthRowCommand.Execute(row);
            await Task.Delay(50);

            Assert.True(viewModel.IsBatchLoginRunning);
            Assert.Equal(1, automationCalls);
        }
        finally
        {
            // Always release the runner: without this, a failed assert would
            // leave the batch task pending and DisposeAsync would hang forever.
            runnerRelease.TrySetResult(true);
        }

        await WaitForAsync(() => !viewModel.IsBatchLoginRunning);
    }
    [Fact]
    public async Task BatchLoginCommand_reports_google_runner_exception_without_crashing()
    {
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile", "user@example.test", "synthetic-password", "NONE"));
        var viewModel = CreateViewModel(
            automation: (_, _, _) => throw new IOException("synthetic network error"));
        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var row = Assert.Single(viewModel.GoogleAccounts);
        row.IsSelected = true;

        viewModel.BatchLoginCommand.Execute(null);
        await viewModel.BatchLoginTask!;

        Assert.Contains("Batch login completed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0 succeeded", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1 failed", viewModel.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(viewModel.IsBatchLoginRunning);
        Assert.DoesNotContain("synthetic-password", viewModel.StatusMessage, StringComparison.Ordinal);
    }
    [Fact]
    public async Task CanModifyCredentials_is_false_during_batch_login()
    {
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile", "user@example.test", "synthetic-password", "NONE"));
        var runnerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runnerRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = CreateViewModel(async (_, _, _) =>
        {
            runnerStarted.TrySetResult(true);
            await runnerRelease.Task;
            return GoogleLoginResult.Success();
        });

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        Assert.True(viewModel.CanModifyCredentials);

        var row = Assert.Single(viewModel.GoogleAccounts);
        row.IsSelected = true;
        viewModel.BatchLoginCommand.Execute(null);
        await runnerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        try
        {
            Assert.False(viewModel.CanModifyCredentials);
        }
        finally
        {
            // Always release the runner: without this, a failed assert would
            // leave the batch task pending and DisposeAsync would hang forever.
            runnerRelease.TrySetResult(true);
        }

        await WaitForAsync(() => !viewModel.IsBatchLoginRunning);
        Assert.True(viewModel.CanModifyCredentials);
    }
    [Fact]
    public async Task LoginRowCommand_is_disabled_while_batch_running()
    {
        await CreateVaultAsync("synthetic-password", new GoogleLoginCredential(
            "Test Profile", "user@example.test", "synthetic-password", "NONE"));
        var runnerStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runnerRelease = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var automationCalls = 0;
        var viewModel = CreateViewModel(async (_, _, _) =>
        {
            Interlocked.Increment(ref automationCalls);
            runnerStarted.TrySetResult(true);
            await runnerRelease.Task;
            return GoogleLoginResult.Success();
        });

        await WaitForAsync(() => viewModel.GoogleAccounts.Count == 1);
        await viewModel.UnlockVaultAsync("synthetic-password", remember: false);
        var batchRow = Assert.Single(viewModel.GoogleAccounts);
        batchRow.IsSelected = true;

        viewModel.BatchLoginCommand.Execute(null);
        await runnerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var loginRow = new GoogleAccountRowViewModel
        {
            ProfileId = _profile.Id,
            ProfileName = _profile.Name,
            Email = "other@example.test",
            Password = "other-password",
            TotpSecret = "NONE",
            HasCredentials = true
        };

        try
        {
            // Real command behavior: the row button is disabled while a batch
            // runs, so executing it is skipped and the batch is undisturbed.
            Assert.False(viewModel.LoginRowCommand.CanExecute(loginRow));

            viewModel.LoginRowCommand.Execute(loginRow);
            await Task.Delay(50);

            Assert.True(viewModel.IsBatchLoginRunning);
            Assert.Equal(1, automationCalls);
        }
        finally
        {
            // Always release the runner: without this, a failed assert would
            // leave the batch task pending and DisposeAsync would hang forever.
            runnerRelease.TrySetResult(true);
        }

        await WaitForAsync(() => !viewModel.IsBatchLoginRunning);
    }
}
