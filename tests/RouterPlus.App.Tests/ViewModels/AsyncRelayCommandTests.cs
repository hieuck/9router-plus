using RouterPlus.App.ViewModels;
using Xunit;

namespace RouterPlus.App.Tests.ViewModels;

/// <summary>
/// TDD tests for AsyncRelayCommand and AsyncRelayCommand&lt;T&gt;
/// Async ICommand implementations with _isRunning guard and exception handling
/// </summary>
public sealed class AsyncRelayCommandTests
{
    [Fact]
    public void Constructor_ThrowsWhenExecuteIsNull()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AsyncRelayCommand(null!));
    }

    [Fact]
    public void Constructor_AcceptsNullCanExecute()
    {
        // Arrange & Act
        var command = new AsyncRelayCommand(async () => await Task.CompletedTask, canExecute: null);

        // Assert
        Assert.NotNull(command);
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task Execute_InvokesAsyncAction()
    {
        // Arrange
        var executionCount = 0;
        var tcs = new TaskCompletionSource<bool>();
        var command = new AsyncRelayCommand(async () =>
        {
            executionCount++;
            await Task.Delay(1);
        });

        // Act
        command.Execute(null);
        await Task.Delay(50); // Give async execution time to complete

        // Assert
        Assert.Equal(1, executionCount);
    }

    [Fact]
    public void CanExecute_ReturnsTrueWhenCanExecuteIsNull()
    {
        // Arrange
        var command = new AsyncRelayCommand(async () => await Task.CompletedTask);

        // Act & Assert
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public void CanExecute_ReturnsCanExecuteResult()
    {
        // Arrange
        var canExecute = true;
        var command = new AsyncRelayCommand(async () => await Task.CompletedTask, () => canExecute);

        // Act & Assert
        Assert.True(command.CanExecute(null));

        canExecute = false;
        Assert.False(command.CanExecute(null));
    }

    [Fact]
    public async Task CanExecute_ReturnsFalseWhileRunning()
    {
        // Arrange
        var tcs = new TaskCompletionSource<bool>();
        var command = new AsyncRelayCommand(async () => await tcs.Task);

        // Act
        command.Execute(null);
        await Task.Delay(10); // Let execution start

        // Assert - should be false while running
        Assert.False(command.CanExecute(null));

        // Complete the task
        tcs.SetResult(true);
        await Task.Delay(10); // Let execution complete

        // Assert - should be true after completion
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task Execute_RaisesCanExecuteChangedWhenStartingAndCompleting()
    {
        // Arrange
        var tcs = new TaskCompletionSource<bool>();
        var command = new AsyncRelayCommand(async () => await tcs.Task);
        var canExecuteChangedCount = 0;
        command.CanExecuteChanged += (s, e) => canExecuteChangedCount++;

        // Act
        command.Execute(null);
        await Task.Delay(10); // Let execution start
        var countAfterStart = canExecuteChangedCount;

        tcs.SetResult(true);
        await Task.Delay(10); // Let execution complete
        var countAfterComplete = canExecuteChangedCount;

        // Assert
        Assert.Equal(1, countAfterStart); // Raised when starting
        Assert.Equal(2, countAfterComplete); // Raised again when completing
    }

    [Fact]
    public void RaiseCanExecuteChanged_InvokesEvent()
    {
        // Arrange
        var command = new AsyncRelayCommand(async () => await Task.CompletedTask);
        var eventRaised = false;
        command.CanExecuteChanged += (s, e) => eventRaised = true;

        // Act
        command.RaiseCanExecuteChanged();

        // Assert
        Assert.True(eventRaised);
    }

    [Fact]
    public async Task Execute_HandlesExceptionGracefully()
    {
        // Arrange
        var command = new AsyncRelayCommand(async () =>
        {
            await Task.Delay(1);
            throw new InvalidOperationException("Test exception");
        });

        // Act & Assert - should not throw, exception is caught and logged
        command.Execute(null);
        await Task.Delay(50); // Give async execution time to complete

        // Command should be executable again after exception
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public void Execute_IgnoresParameter()
    {
        // Arrange
        var executed = false;
        var command = new AsyncRelayCommand(async () =>
        {
            executed = true;
            await Task.CompletedTask;
        });

        // Act
        command.Execute("some parameter");

        // Assert
        Assert.True(executed);
    }
}

/// <summary>
/// Tests for generic AsyncRelayCommand&lt;T&gt;
/// </summary>
public sealed class AsyncRelayCommandGenericTests
{
    [Fact]
    public void Constructor_ThrowsWhenExecuteIsNull()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new AsyncRelayCommand<string>(null!));
    }

    [Fact]
    public void Constructor_AcceptsNullCanExecute()
    {
        // Arrange & Act
        var command = new AsyncRelayCommand<string>(async s => await Task.CompletedTask, canExecute: null);

        // Assert
        Assert.NotNull(command);
        Assert.True(command.CanExecute("test"));
    }

    [Fact]
    public async Task Execute_InvokesAsyncActionWithParameter()
    {
        // Arrange
        string? receivedValue = null;
        var command = new AsyncRelayCommand<string>(async value =>
        {
            receivedValue = value;
            await Task.Delay(1);
        });

        // Act
        command.Execute("test-value");
        await Task.Delay(50); // Give async execution time to complete

        // Assert
        Assert.Equal("test-value", receivedValue);
    }

    [Fact]
    public void CanExecute_ReturnsFalseForWrongParameterType()
    {
        // Arrange
        var command = new AsyncRelayCommand<string>(async s => await Task.CompletedTask);

        // Act & Assert
        Assert.False(command.CanExecute(123)); // int instead of string
        Assert.False(command.CanExecute(null)); // null for non-nullable string
    }

    [Fact]
    public void CanExecute_ReturnsTrueForCorrectParameterType()
    {
        // Arrange
        var command = new AsyncRelayCommand<string>(async s => await Task.CompletedTask);

        // Act & Assert
        Assert.True(command.CanExecute("test"));
    }

    [Fact]
    public void CanExecute_InvokesCanExecuteFunctionWithParameter()
    {
        // Arrange
        var command = new AsyncRelayCommand<int>(
            async i => await Task.CompletedTask,
            i => i > 0);

        // Act & Assert
        Assert.True(command.CanExecute(5));
        Assert.False(command.CanExecute(-1));
        Assert.False(command.CanExecute(0));
    }

    [Fact]
    public async Task CanExecute_ReturnsFalseWhileRunning()
    {
        // Arrange
        var tcs = new TaskCompletionSource<bool>();
        var command = new AsyncRelayCommand<string>(async s => await tcs.Task);

        // Act
        command.Execute("test");
        await Task.Delay(10); // Let execution start

        // Assert - should be false while running
        Assert.False(command.CanExecute("test"));

        // Complete the task
        tcs.SetResult(true);
        await Task.Delay(10); // Let execution complete

        // Assert - should be true after completion
        Assert.True(command.CanExecute("test"));
    }

    [Fact]
    public async Task Execute_RaisesCanExecuteChangedWhenStartingAndCompleting()
    {
        // Arrange
        var tcs = new TaskCompletionSource<bool>();
        var command = new AsyncRelayCommand<string>(async s => await tcs.Task);
        var canExecuteChangedCount = 0;
        command.CanExecuteChanged += (s, e) => canExecuteChangedCount++;

        // Act
        command.Execute("test");
        await Task.Delay(10); // Let execution start
        var countAfterStart = canExecuteChangedCount;

        tcs.SetResult(true);
        await Task.Delay(10); // Let execution complete
        var countAfterComplete = canExecuteChangedCount;

        // Assert
        Assert.Equal(1, countAfterStart); // Raised when starting
        Assert.Equal(2, countAfterComplete); // Raised again when completing
    }

    [Fact]
    public void RaiseCanExecuteChanged_InvokesEvent()
    {
        // Arrange
        var command = new AsyncRelayCommand<string>(async s => await Task.CompletedTask);
        var eventRaised = false;
        command.CanExecuteChanged += (s, e) => eventRaised = true;

        // Act
        command.RaiseCanExecuteChanged();

        // Assert
        Assert.True(eventRaised);
    }

    [Fact]
    public async Task Execute_HandlesExceptionGracefully()
    {
        // Arrange
        var command = new AsyncRelayCommand<string>(async s =>
        {
            await Task.Delay(1);
            throw new InvalidOperationException("Test exception");
        });

        // Act & Assert - should not throw, exception is caught and logged
        command.Execute("test");
        await Task.Delay(50); // Give async execution time to complete

        // Command should be executable again after exception
        Assert.True(command.CanExecute("test"));
    }

    [Fact]
    public async Task Execute_SkipsExecutionForWrongParameterType()
    {
        // Arrange
        var executionCount = 0;
        var command = new AsyncRelayCommand<string>(async s =>
        {
            executionCount++;
            await Task.CompletedTask;
        });

        // Act
        command.Execute(123); // Wrong type
        await Task.Delay(50);

        // Assert
        Assert.Equal(0, executionCount);
    }
}
