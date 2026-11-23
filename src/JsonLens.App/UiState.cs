using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace JsonLens.App;

/// <summary>
/// Attached properties used by the theme. <see cref="IsHot"/> shows the hover/focus look without a real
/// mouse or keyboard, which lets the frame recorder show what a user would see.
/// </summary>
public static class UiState
{
    public static readonly DependencyProperty IsHotProperty = DependencyProperty.RegisterAttached(
        "IsHot", typeof(bool), typeof(UiState), new FrameworkPropertyMetadata(false));

    public static bool GetIsHot(DependencyObject d) => (bool)d.GetValue(IsHotProperty);
    public static void SetIsHot(DependencyObject d, bool value) => d.SetValue(IsHotProperty, value);

    /// <summary>Inherited: draw list selections as active even without keyboard focus (used by --frames).</summary>
    public static readonly DependencyProperty ForceActiveSelectionProperty = DependencyProperty.RegisterAttached(
        "ForceActiveSelection", typeof(bool), typeof(UiState),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static bool GetForceActiveSelection(DependencyObject d) => (bool)d.GetValue(ForceActiveSelectionProperty);
    public static void SetForceActiveSelection(DependencyObject d, bool value) => d.SetValue(ForceActiveSelectionProperty, value);

    public static readonly DependencyProperty HasErrorProperty = DependencyProperty.RegisterAttached(
        "HasError", typeof(bool), typeof(UiState), new FrameworkPropertyMetadata(false));

    public static bool GetHasError(DependencyObject d) => (bool)d.GetValue(HasErrorProperty);
    public static void SetHasError(DependencyObject d, bool value) => d.SetValue(HasErrorProperty, value);

    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached(
        "Icon", typeof(string), typeof(UiState), new FrameworkPropertyMetadata(null));

    public static string? GetIcon(DependencyObject d) => (string?)d.GetValue(IconProperty);
    public static void SetIcon(DependencyObject d, string? value) => d.SetValue(IconProperty, value);

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached(
        "Placeholder", typeof(string), typeof(UiState), new FrameworkPropertyMetadata(""));

    public static string GetPlaceholder(DependencyObject d) => (string)d.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject d, string value) => d.SetValue(PlaceholderProperty, value);
}

/// <summary>Tree depth to a left margin.</summary>
public sealed class IndentConverter : IValueConverter
{
    public double Step { get; set; } = 16;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        new Thickness(value is int depth ? depth * Step : 0, 0, 0, 0);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool b = value switch
        {
            bool x => x,
            string s => !string.IsNullOrEmpty(s),
            null => false,
            _ => true,
        };
        if (Invert) b = !b;
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Binds a RadioButton to a bool: checked when the bool equals ConverterParameter.</summary>
public sealed class BoolMatchConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && b == bool.Parse((string)parameter);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? bool.Parse((string)parameter) : Binding.DoNothing;
}
