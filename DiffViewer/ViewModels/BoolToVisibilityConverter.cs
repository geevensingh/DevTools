using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DiffViewer.ViewModels;

/// <summary>
/// Inverts a bool into <see cref="Visibility.Visible"/> / <see cref="Visibility.Collapsed"/>.
/// Used by <c>FileListView</c> to flip between the flat-list and directory-tree presentations.
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public static readonly BoolToVisibilityConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}
