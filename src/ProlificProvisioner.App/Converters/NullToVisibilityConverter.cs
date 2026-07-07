using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ProlificProvisioner.App.Converters;

/// <summary>Null/empty string -> Collapsed, otherwise Visible. Used for the "unrecognized cable" banner.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
