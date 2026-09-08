using System.Globalization;
using System.Windows;
using RouterPlus.App.Converters;
using Xunit;

namespace RouterPlus.App.Tests;

public sealed class ConvertersTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    [Fact]
    public void BooleanToVisibilityConverter_ConvertMapsBooleanValuesAndNonBooleanToCollapsed()
    {
        // Arrange
        var converter = new BooleanToVisibilityConverter();

        // Act
        var visible = converter.Convert(true, typeof(Visibility), null!, Culture);
        var collapsed = converter.Convert(false, typeof(Visibility), null!, Culture);
        var nonBoolean = converter.Convert("true", typeof(Visibility), null!, Culture);

        // Assert
        Assert.Equal(Visibility.Visible, visible);
        Assert.Equal(Visibility.Collapsed, collapsed);
        Assert.Equal(Visibility.Collapsed, nonBoolean);
    }

    [Fact]
    public void BooleanToVisibilityConverter_ConvertBackMapsVisibleAndNonVisibleValues()
    {
        // Arrange
        var converter = new BooleanToVisibilityConverter();

        // Act
        var visible = converter.ConvertBack(Visibility.Visible, typeof(bool), null!, Culture);
        var collapsed = converter.ConvertBack(Visibility.Collapsed, typeof(bool), null!, Culture);
        var nonVisibility = converter.ConvertBack(true, typeof(bool), null!, Culture);

        // Assert
        Assert.Equal(true, visible);
        Assert.Equal(false, collapsed);
        Assert.Equal(false, nonVisibility);
    }

    [Fact]
    public void InverseBooleanConverter_ConvertAndConvertBackInvertBooleansAndReturnFalseForNonBoolean()
    {
        // Arrange
        var converter = new InverseBooleanConverter();

        // Act
        var convertedTrue = converter.Convert(true, typeof(bool), null!, Culture);
        var convertedFalse = converter.Convert(false, typeof(bool), null!, Culture);
        var convertedNonBoolean = converter.Convert(null!, typeof(bool), null!, Culture);
        var convertedBackTrue = converter.ConvertBack(true, typeof(bool), null!, Culture);
        var convertedBackFalse = converter.ConvertBack(false, typeof(bool), null!, Culture);
        var convertedBackNonBoolean = converter.ConvertBack(null!, typeof(bool), null!, Culture);

        // Assert
        Assert.Equal(false, convertedTrue);
        Assert.Equal(true, convertedFalse);
        Assert.Equal(false, convertedNonBoolean);
        Assert.Equal(false, convertedBackTrue);
        Assert.Equal(true, convertedBackFalse);
        Assert.Equal(false, convertedBackNonBoolean);
    }

    [Fact]
    public void InverseBooleanToVisibilityConverter_ConvertShowsOnlyForFalseBoolean()
    {
        // Arrange
        var converter = new InverseBooleanToVisibilityConverter();

        // Act
        var falseValue = converter.Convert(false, typeof(Visibility), null!, Culture);
        var trueValue = converter.Convert(true, typeof(Visibility), null!, Culture);
        var nonBoolean = converter.Convert(null!, typeof(Visibility), null!, Culture);

        // Assert
        Assert.Equal(Visibility.Visible, falseValue);
        Assert.Equal(Visibility.Collapsed, trueValue);
        Assert.Equal(Visibility.Collapsed, nonBoolean);
    }

    [Fact]
    public void InverseBooleanToVisibilityConverter_ConvertBackShowsTrueForNonVisibleValues()
    {
        // Arrange
        var converter = new InverseBooleanToVisibilityConverter();

        // Act
        var visible = converter.ConvertBack(Visibility.Visible, typeof(bool), null!, Culture);
        var collapsed = converter.ConvertBack(Visibility.Collapsed, typeof(bool), null!, Culture);
        var hidden = converter.ConvertBack(Visibility.Hidden, typeof(bool), null!, Culture);
        var nonVisibility = converter.ConvertBack(null!, typeof(bool), null!, Culture);

        // Assert
        Assert.Equal(false, visible);
        Assert.Equal(true, collapsed);
        Assert.Equal(true, hidden);
        Assert.Equal(false, nonVisibility);
    }

    [Fact]
    public void NullToFalseConverter_ConvertReturnsWhetherValueIsNonNull()
    {
        // Arrange
        var converter = new NullToFalseConverter();

        // Act
        var nullValue = converter.Convert(null!, typeof(bool), null!, Culture);
        var nonNullValue = converter.Convert("value", typeof(bool), null!, Culture);

        // Assert
        Assert.Equal(false, nullValue);
        Assert.Equal(true, nonNullValue);
    }

    [Fact]
    public void NullToFalseConverter_ConvertBackIsNotSupported()
    {
        // Arrange
        var converter = new NullToFalseConverter();

        // Act & Assert
        Assert.Throws<NotImplementedException>(() => converter.ConvertBack(false, typeof(object), null!, Culture));
    }

    [Fact]
    public void OneBasedIndexConverter_ConvertAddsOneUsingCulture()
    {
        // Arrange
        var converter = new OneBasedIndexConverter();

        // Act
        var first = converter.Convert(0, typeof(string), null, Culture);
        var negative = converter.Convert(-1, typeof(string), null, Culture);
        var nonInteger = converter.Convert("0", typeof(string), null, Culture);

        // Assert
        Assert.Equal("1", first);
        Assert.Equal("0", negative);
        Assert.Equal(string.Empty, nonInteger);
    }

    [Fact]
    public void OneBasedIndexConverter_ConvertBackIsNotSupported()
    {
        // Arrange
        var converter = new OneBasedIndexConverter();

        // Act & Assert
        Assert.Throws<NotSupportedException>(() => converter.ConvertBack("1", typeof(int), null, Culture));
    }
}
