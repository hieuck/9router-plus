using System.Globalization;
using System.Windows;
using RouterPlus.App.Converters;

namespace RouterPlus.App.Tests;

public sealed class ConverterTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    [Theory]
    [InlineData(true, Visibility.Visible)]
    [InlineData(false, Visibility.Collapsed)]
    public void BooleanToVisibility_converts_boolean(bool value, Visibility expected)
    {
        Assert.Equal(expected, new BooleanToVisibilityConverter().Convert(value, typeof(Visibility), null!, Culture));
    }

    [Fact]
    public void BooleanToVisibility_non_boolean_collapses_and_converts_back()
    {
        var converter = new BooleanToVisibilityConverter();

        Assert.Equal(Visibility.Collapsed, converter.Convert("true", typeof(Visibility), null!, Culture));
        Assert.True((bool)converter.ConvertBack(Visibility.Visible, typeof(bool), null!, Culture));
        Assert.False((bool)converter.ConvertBack(Visibility.Hidden, typeof(bool), null!, Culture));
        Assert.False((bool)converter.ConvertBack("visible", typeof(bool), null!, Culture));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void InverseBoolean_converts(bool value, bool expected)
    {
        var converter = new InverseBooleanConverter();

        Assert.Equal(expected, converter.Convert(value, typeof(bool), null!, Culture));
        Assert.Equal(expected, converter.ConvertBack(value, typeof(bool), null!, Culture));
        Assert.False((bool)converter.Convert("true", typeof(bool), null!, Culture));
    }

    [Theory]
    [InlineData(true, Visibility.Collapsed)]
    [InlineData(false, Visibility.Visible)]
    public void InverseBooleanToVisibility_converts(bool value, Visibility expected)
    {
        var converter = new InverseBooleanToVisibilityConverter();

        Assert.Equal(expected, converter.Convert(value, typeof(Visibility), null!, Culture));
        Assert.Equal(value, (bool)converter.ConvertBack(expected, typeof(bool), null!, Culture));
    }

    [Fact]
    public void NullToFalse_returns_presence_and_rejects_reverse_conversion()
    {
        var converter = new NullToFalseConverter();

        Assert.True((bool)converter.Convert("value", typeof(bool), null!, Culture));
        Assert.False((bool)converter.Convert(null!, typeof(bool), null!, Culture));
        Assert.Throws<NotImplementedException>(() => converter.ConvertBack(true, typeof(object), null!, Culture));
    }

    [Theory]
    [InlineData(0, "1")]
    [InlineData(4, "5")]
    public void OneBasedIndex_formats_index(int value, string expected)
    {
        var converter = new OneBasedIndexConverter();

        Assert.Equal(expected, converter.Convert(value, typeof(string), null!, Culture));
        Assert.Equal(string.Empty, converter.Convert("4", typeof(string), null!, Culture));
        Assert.Throws<NotSupportedException>(() => converter.ConvertBack("1", typeof(int), null!, Culture));
    }
}
