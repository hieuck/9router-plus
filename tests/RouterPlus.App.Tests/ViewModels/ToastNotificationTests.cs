using RouterPlus.App.ViewModels;
using Xunit;

namespace RouterPlus.App.Tests.ViewModels;

/// <summary>
/// TDD tests for ToastNotification
/// Simple ViewModel for temporary UI notifications
/// </summary>
public sealed class ToastNotificationTests
{
    [Fact]
    public void Constructor_InitializesProperties()
    {
        // Arrange & Act
        var toast = new ToastNotification("Test message", ToastType.Success, TimeSpan.FromSeconds(3));

        // Assert
        Assert.Equal("Test message", toast.Message);
        Assert.Equal(ToastType.Success, toast.Type);
        Assert.Equal(TimeSpan.FromSeconds(3), toast.Duration);
        Assert.False(toast.IsVisible);
    }

    [Theory]
    [InlineData(ToastType.Success)]
    [InlineData(ToastType.Error)]
    [InlineData(ToastType.Info)]
    [InlineData(ToastType.Warning)]
    public void Constructor_AcceptsAllToastTypes(ToastType type)
    {
        // Arrange & Act
        var toast = new ToastNotification("Message", type, TimeSpan.FromSeconds(1));

        // Assert
        Assert.Equal(type, toast.Type);
    }

    [Fact]
    public void IsVisible_DefaultsToFalse()
    {
        // Arrange & Act
        var toast = new ToastNotification("Test", ToastType.Info, TimeSpan.FromSeconds(1));

        // Assert
        Assert.False(toast.IsVisible);
    }

    [Fact]
    public void IsVisible_UpdatesAndRaisesPropertyChanged()
    {
        // Arrange
        var toast = new ToastNotification("Test", ToastType.Info, TimeSpan.FromSeconds(1));
        var propertyChanged = false;
        toast.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ToastNotification.IsVisible))
                propertyChanged = true;
        };

        // Act
        toast.IsVisible = true;

        // Assert
        Assert.True(toast.IsVisible);
        Assert.True(propertyChanged);
    }

    [Fact]
    public void IsVisible_WhenSetToSameValue_DoesNotRaisePropertyChanged()
    {
        // Arrange
        var toast = new ToastNotification("Test", ToastType.Info, TimeSpan.FromSeconds(1));
        toast.IsVisible = true;

        var propertyChangedCount = 0;
        toast.PropertyChanged += (s, e) => propertyChangedCount++;

        // Act
        toast.IsVisible = true; // Same value

        // Assert
        Assert.Equal(0, propertyChangedCount);
    }

    [Fact]
    public void Show_SetsIsVisibleToTrue()
    {
        // Arrange
        var toast = new ToastNotification("Test", ToastType.Info, TimeSpan.FromSeconds(1));

        // Act
        toast.Show();

        // Assert
        Assert.True(toast.IsVisible);
    }

    [Fact]
    public void Hide_SetsIsVisibleToFalse()
    {
        // Arrange
        var toast = new ToastNotification("Test", ToastType.Info, TimeSpan.FromSeconds(1));
        toast.Show();

        // Act
        toast.Hide();

        // Assert
        Assert.False(toast.IsVisible);
    }

    [Fact]
    public void Hide_CanBeCalledWhenNotVisible()
    {
        // Arrange
        var toast = new ToastNotification("Test", ToastType.Info, TimeSpan.FromSeconds(1));

        // Act & Assert - Should not throw
        toast.Hide();
        Assert.False(toast.IsVisible);
    }

    [Fact]
    public void Message_IsReadOnly()
    {
        // Arrange
        var toast = new ToastNotification("Test", ToastType.Info, TimeSpan.FromSeconds(1));
        var type = toast.GetType();

        // Act & Assert
        Assert.Null(type.GetProperty(nameof(ToastNotification.Message))?.SetMethod);
    }

    [Fact]
    public void Type_IsReadOnly()
    {
        // Arrange
        var toast = new ToastNotification("Test", ToastType.Info, TimeSpan.FromSeconds(1));
        var type = toast.GetType();

        // Act & Assert
        Assert.Null(type.GetProperty(nameof(ToastNotification.Type))?.SetMethod);
    }

    [Fact]
    public void Duration_IsReadOnly()
    {
        // Arrange
        var toast = new ToastNotification("Test", ToastType.Info, TimeSpan.FromSeconds(1));
        var type = toast.GetType();

        // Act & Assert
        Assert.Null(type.GetProperty(nameof(ToastNotification.Duration))?.SetMethod);
    }

    [Fact]
    public void Show_RaisesPropertyChanged()
    {
        // Arrange
        var toast = new ToastNotification("Test", ToastType.Info, TimeSpan.FromSeconds(1));
        var propertyChanged = false;
        toast.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ToastNotification.IsVisible))
                propertyChanged = true;
        };

        // Act
        toast.Show();

        // Assert
        Assert.True(propertyChanged);
    }

    [Fact]
    public void Hide_RaisesPropertyChanged()
    {
        // Arrange
        var toast = new ToastNotification("Test", ToastType.Info, TimeSpan.FromSeconds(1));
        toast.Show();

        var propertyChanged = false;
        toast.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ToastNotification.IsVisible))
                propertyChanged = true;
        };

        // Act
        toast.Hide();

        // Assert
        Assert.True(propertyChanged);
    }

    [Fact]
    public void Constructor_AcceptsZeroDuration()
    {
        // Arrange & Act
        var toast = new ToastNotification("Test", ToastType.Info, TimeSpan.Zero);

        // Assert
        Assert.Equal(TimeSpan.Zero, toast.Duration);
    }

    [Fact]
    public void Constructor_AcceptsLongDuration()
    {
        // Arrange & Act
        var toast = new ToastNotification("Test", ToastType.Info, TimeSpan.FromHours(1));

        // Assert
        Assert.Equal(TimeSpan.FromHours(1), toast.Duration);
    }
}
