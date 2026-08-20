using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WpfControl = System.Windows.Controls.Control;

namespace RouterPlus.App;

public static class FontScaleBehavior
{
    public static readonly DependencyProperty ScaleProperty =
        DependencyProperty.RegisterAttached(
            "Scale",
            typeof(double),
            typeof(FontScaleBehavior),
            new PropertyMetadata(1d, OnScaleChanged));

    private static readonly DependencyProperty BaseFontSizeProperty =
        DependencyProperty.RegisterAttached(
            "BaseFontSize",
            typeof(double),
            typeof(FontScaleBehavior),
            new PropertyMetadata(0d));

    public static void SetScale(DependencyObject element, double value) =>
        element.SetValue(ScaleProperty, value);

    public static double GetScale(DependencyObject element) =>
        (double)element.GetValue(ScaleProperty);

    private static void OnScaleChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not FrameworkElement root)
        {
            return;
        }

        root.RemoveHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler(DescendantLoaded));
        root.AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler(DescendantLoaded), true);
        root.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(() => Apply(root, GetScale(root))));
    }

    private static void DescendantLoaded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not FrameworkElement element)
        {
            return;
        }

        var window = Window.GetWindow(element);
        if (window is not null)
        {
            Apply(element, GetScale(window));
        }
    }

    private static void Apply(DependencyObject root, double scale)
    {
        foreach (var element in Enumerate(root))
        {
            if (element is WpfControl control)
            {
                ApplyFontSize(control, control.FontSize, scale);
            }
            else if (element is TextBlock textBlock)
            {
                ApplyFontSize(textBlock, textBlock.FontSize, scale);
            }
        }
    }

    private static void ApplyFontSize(DependencyObject element, double currentSize, double scale)
    {
        var baseSize = (double)element.GetValue(BaseFontSizeProperty);
        if (baseSize <= 0 || double.IsNaN(baseSize))
        {
            baseSize = currentSize;
            element.SetValue(BaseFontSizeProperty, baseSize);
        }

        var scaledSize = Math.Max(8d, Math.Round(baseSize * scale, 2));
        if (element is WpfControl control)
        {
            control.FontSize = scaledSize;
        }
        else if (element is TextBlock textBlock)
        {
            textBlock.FontSize = scaledSize;
        }
    }

    private static IEnumerable<DependencyObject> Enumerate(DependencyObject root)
    {
        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Enumerate(child))
            {
                yield return descendant;
            }
        }
    }
}
