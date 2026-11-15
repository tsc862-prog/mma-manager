using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Data;

namespace MmaManager.Wpf.Mvvm;

/// <summary>
/// Text written about "him", turned to "her" for a woman. Bind a bool (is she a woman)
/// and pass the text as the parameter:
/// <c>{Binding IsWoman, Converter={StaticResource Pronouns}, ConverterParameter='he wants '}</c>.
/// Handles lower, title and upper case (he/He/HE, his/His/HIS, him, himself, he'll, he's).
/// Shared as {StaticResource Pronouns}; <see cref="For"/> does the same in code.
/// </summary>
public sealed class Pronouns : IValueConverter
{
    private static readonly Regex Words = new(@"\b(he|He|HE|his|His|HIS|him|Him|HIM|himself|Himself|HIMSELF)\b");

    public static string For(bool woman, string text) =>
        !woman ? text : Words.Replace(text, m => m.Value switch
        {
            "he" => "she", "He" => "She", "HE" => "SHE",
            "his" or "him" => "her", "His" or "Him" => "Her", "HIS" or "HIM" => "HER",
            "himself" => "herself", "Himself" => "Herself", _ => "HERSELF",
        });

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        For(value is true, parameter as string ?? "");

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
