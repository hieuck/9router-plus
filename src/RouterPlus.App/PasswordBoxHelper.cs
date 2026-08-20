using System.Windows;
using System.Windows.Controls;

namespace RouterPlus.App;

public static class PasswordBoxHelper
{
    public static readonly DependencyProperty BindPasswordProperty =
        DependencyProperty.RegisterAttached(
            "BindPassword",
            typeof(bool),
            typeof(PasswordBoxHelper),
            new PropertyMetadata(false, OnBindPasswordChanged));

    public static readonly DependencyProperty BoundPasswordProperty =
        DependencyProperty.RegisterAttached(
            "BoundPassword",
            typeof(string),
            typeof(PasswordBoxHelper),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
                OnBoundPasswordChanged));

    private static readonly DependencyProperty IsUpdatingProperty =
        DependencyProperty.RegisterAttached(
            "IsUpdating",
            typeof(bool),
            typeof(PasswordBoxHelper),
            new PropertyMetadata(false));

    public static void SetBindPassword(DependencyObject element, bool value) =>
        element.SetValue(BindPasswordProperty, value);

    public static bool GetBindPassword(DependencyObject element) =>
        (bool)element.GetValue(BindPasswordProperty);

    public static void SetBoundPassword(DependencyObject element, string value) =>
        element.SetValue(BoundPasswordProperty, value ?? string.Empty);

    public static string GetBoundPassword(DependencyObject element) =>
        (string)element.GetValue(BoundPasswordProperty);

    private static void SetIsUpdating(DependencyObject element, bool value) =>
        element.SetValue(IsUpdatingProperty, value);

    private static bool GetIsUpdating(DependencyObject element) =>
        (bool)element.GetValue(IsUpdatingProperty);

    private static void OnBindPasswordChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not PasswordBox passwordBox)
        {
            return;
        }

        if ((bool)e.NewValue)
        {
            passwordBox.PasswordChanged += PasswordBox_OnPasswordChanged;
            UpdatePasswordBox(passwordBox, GetBoundPassword(passwordBox));
        }
        else
        {
            passwordBox.PasswordChanged -= PasswordBox_OnPasswordChanged;
        }
    }

    private static void OnBoundPasswordChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is PasswordBox passwordBox && !GetIsUpdating(passwordBox))
        {
            UpdatePasswordBox(passwordBox, e.NewValue as string ?? string.Empty);
        }
    }

    private static void PasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox passwordBox || GetIsUpdating(passwordBox))
        {
            return;
        }

        SetIsUpdating(passwordBox, true);
        try
        {
            SetBoundPassword(passwordBox, passwordBox.Password);
        }
        finally
        {
            SetIsUpdating(passwordBox, false);
        }
    }

    private static void UpdatePasswordBox(PasswordBox passwordBox, string value)
    {
        if (string.Equals(passwordBox.Password, value, StringComparison.Ordinal))
        {
            return;
        }

        SetIsUpdating(passwordBox, true);
        try
        {
            passwordBox.Password = value;
        }
        finally
        {
            SetIsUpdating(passwordBox, false);
        }
    }
}
