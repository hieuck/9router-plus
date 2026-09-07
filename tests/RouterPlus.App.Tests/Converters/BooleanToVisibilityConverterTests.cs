using System.Globalization;
using System.Windows;
using RouterPlus.App.Converters;

namespace RouterPlus.App.Tests.Converters;

public sealed class BooleanToVisibilityConverterTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    [Fact]
    public void Convert_true_returns_visible()
    {
        // Arrange
        var converter = new BooleanToVisibilityConverter();

        // Act
        var result = converter.Convert(true, typeof(Visibility), null!, Culture);

        // Assert
        Assert.Equal(Visibility.Visible, result);
    }

    [Fact]
    public void Convert_false_returns_collapsed()
    {
        // Arrange
        var converter = new BooleanToVisibilityConverter();

        // Act
        var result = converter.Convert(false, typeof(Visibility), null!, Culture);

        // Assert
        Assert.Equal(Visibility.Collapsed, result);
    }

    [Fact]
    public void Convert_non_boolean_returns_collapsed()
    {
        // Arrange
        var converter = new BooleanToVisibilityConverter();

        // Act
        var result = converter.Convert("true", typeof(Visibility), null!, Culture);

        // Assert
        Assert.Equal(Visibility.Collapsed, result);
    }

    [Fact]
    public void ConvertBack_visible_returns_true()
    {
        // Arrange
        var converter = new BooleanToVisibilityConverter();

        // Act
        var result = converter.ConvertBack(Visibility.Visible, typeof(bool), null!, Culture);

        // Assert
        Assert.Equal(true, result);
    }

    [Fact]
    public void ConvertBack_collapsed_returns_false()
    {
        // Arrange
        var converter = new BooleanToVisibilityConverter();

        // Act
        var result = converter.ConvertBack(Visibility.Collapsed, typeof(bool), null!, Culture);

        // Assert
        Assert.Equal(false, result);
    }

    [Fact]
    public void ConvertBack_non_visibility_returns_false()
    {
        // Arrange
        var converter = new BooleanToVisibilityConverter();

        // Act
        var result = converter.ConvertBack(true, typeof(bool), null!, Culture);

        // Assert
        Assert.Equal(false, result);
    }
}
