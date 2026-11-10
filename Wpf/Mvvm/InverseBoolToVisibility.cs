using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MmaManager.Wpf.Mvvm;

/// <summary>Visible when the bound flag is false — for "nothing here yet" placeholders.</summary>
public sealed class InverseBoolToVisibility : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility v && v != Visibility.Visible;
}
