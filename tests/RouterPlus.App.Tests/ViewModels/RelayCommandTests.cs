using RouterPlus.App.ViewModels;
using Xunit;

namespace RouterPlus.App.Tests.ViewModels;

/// <summary>
/// TDD tests for RelayCommand
/// Simple ICommand implementation for WPF button bindings
/// </summary>
public sealed class RelayCommandTests
{
    [Fact]
    public void Constructor_ThrowsWhenExecuteIsNull()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new RelayCommand(null!));
    }

    [Fact]
    public void Constructor_AcceptsNullCanExecute()
    {
        // Arrange & Act
        var command = new RelayCommand(() => { }, canExecute: null);

        // Assert
        Assert.NotNull(command);
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public void Execute_InvokesAction()
    {
        // Arrange
        var executionCount = 0;
        var command = new RelayCommand(() => executionCount++);

        // Act
        command.Execute(null);
        command.Execute(null);

        // Assert
        Assert.Equal(2, executionCount);
    }

    [Fact]
    public void CanExecute_ReturnsTrueWhenCanExecuteIsNull()
    {
        // Arrange
        var command = new RelayCommand(() => { });

        // Act & Assert
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public void CanExecute_ReturnsCanExecuteResult()
    {
        // Arrange
        var canExecute = true;
        var command = new RelayCommand(() => { }, () => canExecute);

        // Act & Assert
        Assert.True(command.CanExecute(null));

        canExecute = false;
        Assert.False(command.CanExecute(null));
    }

    [Fact]
    public void CanExecute_InvokesCanExecuteFunction()
    {
        // Arrange
        var canExecuteCallCount = 0;
        var command = new RelayCommand(() => { }, () => { canExecuteCallCount++; return true; });

        // Act
        command.CanExecute(null);
        command.CanExecute(null);

        // Assert
        Assert.Equal(2, canExecuteCallCount);
    }

    [Fact]
    public void Execute_IgnoresParameter()
    {
        // Arrange
        var executed = false;
        var command = new RelayCommand(() => executed = true);

        // Act
        command.Execute("some parameter");

        // Assert
        Assert.True(executed);
    }

    [Fact]
    public void CanExecute_IgnoresParameter()
    {
        // Arrange
        var command = new RelayCommand(() => { }, () => true);

        // Act
        var result = command.CanExecute("some parameter");

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void RaiseCanExecuteChanged_DoesNotThrow()
    {
        // Arrange
        var command = new RelayCommand(() => { });

        // Act & Assert (verifies CommandManager.InvalidateRequerySuggested doesn't throw)
        command.RaiseCanExecuteChanged();
    }

    [Fact]
    public void CanExecuteChanged_CanBeSubscribedAndUnsubscribed()
    {
        // Arrange
        var command = new RelayCommand(() => { });
        EventHandler handler = (s, e) => { };

        // Act & Assert (verifies event wiring doesn't throw)
        command.CanExecuteChanged += handler;
        command.CanExecuteChanged -= handler;
    }
}
