using System.Globalization;
using System.Windows;
using RouterPlus.App.Converters;

namespace RouterPlus.App.Tests.Converters;

public sealed class PureConverterTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void InverseBooleanConverter_Convert_InvertsBoolean(bool value, bool expected)
    {
        // Arrange
        var converter = new InverseBooleanConverter();

        // Act
        var result = converter.Convert(value, typeof(bool), null!, Culture);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void InverseBooleanConverter_Convert_ReturnsFalseForNonBoolean()
    {
        // Arrange
        var converter = new InverseBooleanConverter();

        // Act
        var result = converter.Convert("true", typeof(bool), null!, Culture);

        // Assert
        Assert.Equal(false, result);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void InverseBooleanConverter_ConvertBack_InvertsBoolean(bool value, bool expected)
    {
        // Arrange
        var converter = new InverseBooleanConverter();

        // Act
        var result = converter.ConvertBack(value, typeof(bool), null!, Culture);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void InverseBooleanConverter_ConvertBack_ReturnsFalseForNonBoolean()
    {
        // Arrange
        var converter = new InverseBooleanConverter();

        // Act
        var result = converter.ConvertBack("false", typeof(bool), null!, Culture);

        // Assert
        Assert.Equal(false, result);
    }

    [Theory]
    [InlineData(true, Visibility.Collapsed)]
    [InlineData(false, Visibility.Visible)]
    public void InverseBooleanToVisibilityConverter_Convert_InvertsBooleanVisibility(bool value, Visibility expected)
    {
        // Arrange
        var converter = new InverseBooleanToVisibilityConverter();

        // Act
        var result = converter.Convert(value, typeof(Visibility), null!, Culture);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void InverseBooleanToVisibilityConverter_Convert_CollapsesNonBoolean()
    {
        // Arrange
        var converter = new InverseBooleanToVisibilityConverter();

        // Act
        var result = converter.Convert(null!, typeof(Visibility), null!, Culture);

        // Assert
        Assert.Equal(Visibility.Collapsed, result);
    }

    [Theory]
    [InlineData(Visibility.Visible, false)]
    [InlineData(Visibility.Collapsed, true)]
    [InlineData(Visibility.Hidden, true)]
    public void InverseBooleanToVisibilityConverter_ConvertBack_ReturnsInverseVisibilityState(
        Visibility value,
        bool expected)
    {
        // Arrange
        var converter = new InverseBooleanToVisibilityConverter();

        // Act
        var result = converter.ConvertBack(value, typeof(bool), null!, Culture);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void InverseBooleanToVisibilityConverter_ConvertBack_ReturnsFalseForNonVisibility()
    {
        // Arrange
        var converter = new InverseBooleanToVisibilityConverter();

        // Act
        var result = converter.ConvertBack("Visible", typeof(bool), null!, Culture);

        // Assert
        Assert.Equal(false, result);
    }

    [Theory]
    [InlineData("value")]
    [InlineData(0)]
    [InlineData(false)]
    public void NullToFalseConverter_Convert_ReturnsTrueForNonNullValue(object value)
    {
        // Arrange
        var converter = new NullToFalseConverter();

        // Act
        var result = converter.Convert(value, typeof(bool), null!, Culture);

        // Assert
        Assert.Equal(true, result);
    }

    [Fact]
    public void NullToFalseConverter_Convert_ReturnsFalseForNull()
    {
        // Arrange
        var converter = new NullToFalseConverter();

        // Act
        var result = converter.Convert(null!, typeof(bool), null!, Culture);

        // Assert
        Assert.Equal(false, result);
    }

    [Fact]
    public void NullToFalseConverter_ConvertBack_IsNotSupported()
    {
        // Arrange
        var converter = new NullToFalseConverter();

        // Act and assert
        Assert.Throws<NotImplementedException>(() =>
            converter.ConvertBack(true, typeof(object), null!, Culture));
    }

    [Theory]
    [InlineData(0, "1")]
    [InlineData(7, "8")]
    [InlineData(-1, "0")]
    public void OneBasedIndexConverter_Convert_AddsOneUsingProvidedCulture(int value, string expected)
    {
        // Arrange
        var converter = new OneBasedIndexConverter();

        // Act
        var result = converter.Convert(value, typeof(string), null!, Culture);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("7")]
    [InlineData(null)]
    public void OneBasedIndexConverter_Convert_ReturnsEmptyForNonInteger(object? value)
    {
        // Arrange
        var converter = new OneBasedIndexConverter();

        // Act
        var result = converter.Convert(value!, typeof(string), null!, Culture);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void OneBasedIndexConverter_ConvertBack_IsNotSupported()
    {
        // Arrange
        var converter = new OneBasedIndexConverter();

        // Act and assert
        Assert.Throws<NotSupportedException>(() =>
            converter.ConvertBack("1", typeof(int), null!, Culture));
    }
}
