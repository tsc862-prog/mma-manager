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
/// A fighter's drawing power read at a glance: five rising bars (a crowd getting louder) and the
/// fight sheet's draw word ("a known name"), with the popularity number on hover or, with
/// <see cref="ShowNumber"/>, beside the word. Popularity is a public fact, never fogged. Below a
/// following (<see cref="ViewModels.Fame.DrawShownFrom"/>) the meter is <see cref="IsQuiet"/>: walls
/// collapse it, so a nobody never wears a loud zero. Look is in App.xaml (implicit style).
/// </summary>
public sealed class DrawMeter : Control
{
    static DrawMeter() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(DrawMeter), new FrameworkPropertyMetadata(typeof(DrawMeter)));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(int), typeof(DrawMeter), new PropertyMetadata(0, (d, _) => ((DrawMeter)d).Update()));
    public static readonly DependencyProperty ShowNumberProperty = DependencyProperty.Register(
        nameof(ShowNumber), typeof(bool), typeof(DrawMeter), new PropertyMetadata(false, (d, _) => ((DrawMeter)d).Update()));

    private static readonly DependencyPropertyKey WordKey = DependencyProperty.RegisterReadOnly(nameof(Word), typeof(string), typeof(DrawMeter), new PropertyMetadata(""));
    public static readonly DependencyProperty WordProperty = WordKey.DependencyProperty;
    private static readonly DependencyPropertyKey LevelKey = DependencyProperty.RegisterReadOnly(nameof(Level), typeof(int), typeof(DrawMeter), new PropertyMetadata(0));
    public static readonly DependencyProperty LevelProperty = LevelKey.DependencyProperty;
    private static readonly DependencyPropertyKey IsQuietKey = DependencyProperty.RegisterReadOnly(nameof(IsQuiet), typeof(bool), typeof(DrawMeter), new PropertyMetadata(true));
    public static readonly DependencyProperty IsQuietProperty = IsQuietKey.DependencyProperty;
    private static readonly DependencyPropertyKey IsStarKey = DependencyProperty.RegisterReadOnly(nameof(IsStar), typeof(bool), typeof(DrawMeter), new PropertyMetadata(false));
    public static readonly DependencyProperty IsStarProperty = IsStarKey.DependencyProperty;
    private static readonly DependencyPropertyKey NumberTextKey = DependencyProperty.RegisterReadOnly(nameof(NumberText), typeof(string), typeof(DrawMeter), new PropertyMetadata(""));
    public static readonly DependencyProperty NumberTextProperty = NumberTextKey.DependencyProperty;

    /// <summary>The fighter's popularity (0-100).</summary>
    public int Value { get => (int)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    /// <summary>Print the number beside the word (profile header); walls keep it in the tooltip.</summary>
    public bool ShowNumber { get => (bool)GetValue(ShowNumberProperty); set => SetValue(ShowNumberProperty, value); }
    public string Word => (string)GetValue(WordProperty);
    public int Level => (int)GetValue(LevelProperty);
    public bool IsQuiet => (bool)GetValue(IsQuietProperty);
    public bool IsStar => (bool)GetValue(IsStarProperty);
    /// <summary>"52" when <see cref="ShowNumber"/>, else "".</summary>
    public string NumberText => (string)GetValue(NumberTextProperty);

    public DrawMeter() => Update();

    private void Update()
    {
        int v = Math.Clamp(Value, 0, 100);
        string word = ViewModels.Fame.DrawWord(v);
        SetValue(WordKey, word.Length > 0 ? char.ToUpperInvariant(word[0]) + word[1..] : word);
        SetValue(LevelKey, ViewModels.Fame.DrawLevel(v));
        SetValue(IsQuietKey, v < ViewModels.Fame.DrawShownFrom);
        SetValue(IsStarKey, v >= 75);
        SetValue(NumberTextKey, ShowNumber ? v.ToString() : "");
        ToolTip = $"Drawing power: popularity {v} of 100. Who sells tickets is public; anyone can see it.";
    }
}

/// <summary>The draw meter's five rising bars; <see cref="Level"/> of them lit.</summary>
public sealed class DrawBars : FrameworkElement
{
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(int), typeof(DrawBars), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(DrawBars), new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(DrawBars), new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public int Level { get => (int)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush Track { get => (Brush)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }

    protected override Size MeasureOverride(Size available) =>
        new(double.IsNaN(Width) ? 24 : Width, double.IsNaN(Height) ? 12 : Height);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        const int n = 5;
        double gap = Math.Max(1.5, w / 16), bar = (w - gap * (n - 1)) / n;
        for (int i = 0; i < n; i++)
        {
            double bh = h * (0.36 + 0.16 * i);
            var r = new Rect(i * (bar + gap), h - bh, bar, bh);
            dc.DrawRoundedRectangle(i < Level ? Fill : Track, null, r, 1, 1);
        }
    }
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
