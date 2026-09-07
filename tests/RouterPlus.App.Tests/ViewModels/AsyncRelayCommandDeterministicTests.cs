using RouterPlus.App.ViewModels;

namespace RouterPlus.App.Tests.ViewModels;

public sealed class AsyncRelayCommandDeterministicTests
{
    [Fact]
    public async Task Execute_DoesNotStartAnotherExecutionWhileRunning()
    {
        // Arrange
        var executionCount = 0;
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var eventCount = 0;
        var command = new AsyncRelayCommand(async () =>
        {
            Interlocked.Increment(ref executionCount);
            started.SetResult(true);
            await release.Task;
        });
        command.CanExecuteChanged += (_, _) =>
        {
            if (Interlocked.Increment(ref eventCount) == 2)
            {
                completed.SetResult(true);
            }
        };

        // Act
        command.Execute(null);
        await started.Task;
        command.Execute(null);
        release.SetResult(true);
        await completed.Task;

        // Assert
        Assert.Equal(1, executionCount);
        Assert.Equal(2, eventCount);
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public void Execute_SkipsExecutionWhenCanExecuteIsFalse()
    {
        // Arrange
        var executionCount = 0;
        var command = new AsyncRelayCommand(
            () =>
            {
                executionCount++;
                return Task.CompletedTask;
            },
            () => false);

        // Act
        command.Execute(null);

        // Assert
        Assert.Equal(0, executionCount);
        Assert.False(command.CanExecute(null));
    }

    [Fact]
    public async Task GenericExecute_UsesTypedParameterAndSignalsCompletion()
    {
        // Arrange
        var receivedValue = string.Empty;
        var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var eventCount = 0;
        var command = new AsyncRelayCommand<string>(async value =>
        {
            receivedValue = value;
            started.SetResult(true);
            await release.Task;
        });
        command.CanExecuteChanged += (_, _) =>
        {
            if (Interlocked.Increment(ref eventCount) == 2)
            {
                completed.SetResult(true);
            }
        };

        // Act
        command.Execute("expected");
        await started.Task;
        release.SetResult(true);
        await completed.Task;

        // Assert
        Assert.Equal("expected", receivedValue);
        Assert.Equal(2, eventCount);
        Assert.True(command.CanExecute("expected"));
    }

    [Fact]
    public void GenericExecute_SkipsExecutionForWrongParameterType()
    {
        // Arrange
        var executionCount = 0;
        var command = new AsyncRelayCommand<string>(_ =>
        {
            executionCount++;
            return Task.CompletedTask;
        });

        // Act
        command.Execute(123);

        // Assert
        Assert.Equal(0, executionCount);
        Assert.False(command.CanExecute(123));
    }
}
