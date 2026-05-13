using System;
using System.Globalization;
using System.Windows.Data;

namespace DiffViewer.ViewModels;

/// <summary>XAML helper: inverts a <see cref="bool"/>. Used by the
/// Settings dialog to grey out the color-scheme dropdown when a
/// hand-edited custom palette is in effect.</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public static readonly InverseBoolConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is bool b ? !b : value;
}
