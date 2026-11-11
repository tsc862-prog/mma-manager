using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using MmaManager.Wpf.ViewModels;

namespace MmaManager.Wpf.Mvvm;

/// <summary>
/// A <see cref="CardTone"/> as one of the shared Tone brushes (App.xaml): the
/// strong colour by default, the dim background with ConverterParameter=Dim.
/// Neutral reads as TextSoft (or CardFill when dim). Shared as {StaticResource ToneBrush}.
/// </summary>
public sealed class ToneBrush : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool dim = parameter is string s && s == "Dim";
        string key = value is CardTone t ? t switch
        {
            CardTone.Good => dim ? "ToneGoodDim" : "ToneGood",
            CardTone.Warn => dim ? "ToneWarnDim" : "ToneWarn",
            CardTone.Bad => dim ? "ToneBadDim" : "ToneBad",
            CardTone.Info => dim ? "ToneInfoDim" : "ToneInfo",
            _ => dim ? "CardFill" : "TextSoft",
        } : dim ? "CardFill" : "TextSoft";
        return Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
