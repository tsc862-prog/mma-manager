using System.Globalization;
using System.Windows.Data;

namespace MmaManager.Wpf.Controls;

/// <summary>Multiplies a bound size: <c>Converter={x:Static controls:Scale.Half}</c> (an icon half its disc).</summary>
public sealed class Scale : IValueConverter
{
    public static readonly Scale Half = new(0.5);
    private readonly double _factor, _offset, _fallback;
    public Scale(double factor, double offset = 0, double fallback = 20.0) { _factor = factor; _offset = offset; _fallback = fallback; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double d && d > 0 && !double.IsNaN(d) && !double.IsInfinity(d) ? Math.Max(0, d * _factor + _offset) : _fallback;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
