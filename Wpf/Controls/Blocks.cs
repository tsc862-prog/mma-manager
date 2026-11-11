using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MmaManager.Core.Sim.Scouting;

namespace MmaManager.Wpf.Controls;

/// <summary>
/// The heading of a panel: a title, an optional one-line hint under it, and an
/// optional link-style action on the right ("See all 19 ▸"). Look is in
/// App.xaml (implicit style).
/// <code>
/// &lt;controls:SectionHeader Title="Your people" Hint="Who matters this week"
///                          ActionText="Whole roster ▸" ActionCommand="{Binding RosterCommand}" /&gt;
/// </code>
/// </summary>
public sealed class SectionHeader : Control
{
    static SectionHeader() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SectionHeader), new FrameworkPropertyMetadata(typeof(SectionHeader)));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(SectionHeader), new PropertyMetadata(""));
    public static readonly DependencyProperty HintProperty = DependencyProperty.Register(nameof(Hint), typeof(string), typeof(SectionHeader), new PropertyMetadata(""));
    public static readonly DependencyProperty ActionTextProperty = DependencyProperty.Register(nameof(ActionText), typeof(string), typeof(SectionHeader), new PropertyMetadata(""));
    public static readonly DependencyProperty ActionCommandProperty = DependencyProperty.Register(nameof(ActionCommand), typeof(ICommand), typeof(SectionHeader));
    public static readonly DependencyProperty ActionParameterProperty = DependencyProperty.Register(nameof(ActionParameter), typeof(object), typeof(SectionHeader));

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Hint { get => (string)GetValue(HintProperty); set => SetValue(HintProperty, value); }
    public string ActionText { get => (string)GetValue(ActionTextProperty); set => SetValue(ActionTextProperty, value); }
    public ICommand? ActionCommand { get => (ICommand?)GetValue(ActionCommandProperty); set => SetValue(ActionCommandProperty, value); }
    public object? ActionParameter { get => GetValue(ActionParameterProperty); set => SetValue(ActionParameterProperty, value); }
}

/// <summary>
/// What an empty panel shows instead of blank controls: a picture or glyph, a
/// friendly title, one sentence on what to do, and the button that does it.
/// <code>
/// &lt;controls:EmptyState Icon="People" Title="Your gym is empty — for now."
///                       Message="..." ActionText="Find fighters ▸" ActionCommand="{Binding ...}" /&gt;
/// </code>
/// </summary>
public sealed class EmptyState : Control
{
    static EmptyState() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(EmptyState), new FrameworkPropertyMetadata(typeof(EmptyState)));

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(EmptyState), new PropertyMetadata(""));
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(EmptyState), new PropertyMetadata(""));
    public static readonly DependencyProperty MessageProperty = DependencyProperty.Register(nameof(Message), typeof(string), typeof(EmptyState), new PropertyMetadata(""));
    public static readonly DependencyProperty ActionTextProperty = DependencyProperty.Register(nameof(ActionText), typeof(string), typeof(EmptyState), new PropertyMetadata(""));
    public static readonly DependencyProperty ActionCommandProperty = DependencyProperty.Register(nameof(ActionCommand), typeof(ICommand), typeof(EmptyState));
    public static readonly DependencyProperty FootnoteProperty = DependencyProperty.Register(nameof(Footnote), typeof(string), typeof(EmptyState), new PropertyMetadata(""));

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(string), typeof(EmptyState), new PropertyMetadata(""));

    /// <summary>Text glyph (kept for compatibility); prefer <see cref="Icon"/>.</summary>
    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    /// <summary>A line icon from the App.xaml set (Envelope, Calendar, People, Search, …).</summary>
    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Message { get => (string)GetValue(MessageProperty); set => SetValue(MessageProperty, value); }
    public string ActionText { get => (string)GetValue(ActionTextProperty); set => SetValue(ActionTextProperty, value); }
    public ICommand? ActionCommand { get => (ICommand?)GetValue(ActionCommandProperty); set => SetValue(ActionCommandProperty, value); }
    /// <summary>A small tip under the button.</summary>
    public string Footnote { get => (string)GetValue(FootnoteProperty); set => SetValue(FootnoteProperty, value); }
}

/// <summary>
/// The scout's read of a fighter, said like a person would: a short bar, the
/// number, and the band word (Elite / Strong / Solid / Raw) from
/// <see cref="ScoutReport.Band"/>. Bind <see cref="Value"/> to a SCOUTED
/// value only (<c>FighterRow.Scouted</c>) — never the truth.
/// </summary>
public sealed class RatingBadge : Control
{
    static RatingBadge() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(RatingBadge), new FrameworkPropertyMetadata(typeof(RatingBadge)));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(int), typeof(RatingBadge), new PropertyMetadata(0, (d, _) => ((RatingBadge)d).Update()));

    private static readonly DependencyPropertyKey WordKey = DependencyProperty.RegisterReadOnly(nameof(Word), typeof(string), typeof(RatingBadge), new PropertyMetadata("Raw"));
    public static readonly DependencyProperty WordProperty = WordKey.DependencyProperty;
    private static readonly DependencyPropertyKey ToneKey = DependencyProperty.RegisterReadOnly(nameof(Tone), typeof(Brush), typeof(RatingBadge), new PropertyMetadata(Brushes.Gray));
    public static readonly DependencyProperty ToneProperty = ToneKey.DependencyProperty;
    private static readonly DependencyPropertyKey FractionKey = DependencyProperty.RegisterReadOnly(nameof(Fraction), typeof(GridLength), typeof(RatingBadge), new PropertyMetadata(new GridLength(0, GridUnitType.Star)));
    public static readonly DependencyProperty FractionProperty = FractionKey.DependencyProperty;
    private static readonly DependencyPropertyKey RestKey = DependencyProperty.RegisterReadOnly(nameof(Rest), typeof(GridLength), typeof(RatingBadge), new PropertyMetadata(new GridLength(1, GridUnitType.Star)));
    public static readonly DependencyProperty RestProperty = RestKey.DependencyProperty;

    public int Value { get => (int)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Word => (string)GetValue(WordProperty);
    public Brush Tone => (Brush)GetValue(ToneProperty);
    /// <summary>Filled share of the bar (star length) and the empty remainder.</summary>
    public GridLength Fraction => (GridLength)GetValue(FractionProperty);
    public GridLength Rest => (GridLength)GetValue(RestProperty);

    public RatingBadge() => Update();

    private void Update()
    {
        int v = Math.Clamp(Value, 0, 100);
        var band = ScoutReport.Band(v);
        SetValue(WordKey, band switch
        {
            StatBand.Elite => "Elite",
            StatBand.Strong => "Strong",
            StatBand.Average => "Solid",
            _ => "Raw",
        });
        string key = band switch
        {
            StatBand.Elite => "Gold",
            StatBand.Strong => "Positive",
            StatBand.Average => "Steel",
            _ => "TextSecondary",
        };
        SetValue(ToneKey, TryFindResource(key) as Brush ?? Application.Current?.TryFindResource(key) as Brush ?? Brushes.Gray);
        SetValue(FractionKey, new GridLength(Math.Max(v, 1), GridUnitType.Star));
        SetValue(RestKey, new GridLength(Math.Max(100 - v, 0.001), GridUnitType.Star));
    }
}
