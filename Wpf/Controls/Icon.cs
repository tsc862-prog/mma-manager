using System.Windows;
using System.Windows.Media;

namespace MmaManager.Wpf.Controls;

/// <summary>
/// A small line icon drawn from the named geometry set in App.xaml
/// (<c>Icon.Envelope</c>, <c>Icon.Calendar</c>, … all drawn on a 24×24 grid).
/// Replaces emoji glyphs, which WPF renders flat and monochrome. Set
/// <see cref="Kind"/> to the name without the prefix; the stroke follows
/// <see cref="Brush"/> (default: the text colour it sits in).
/// <code>
/// &lt;controls:Icon Kind="Envelope" Width="20" Height="20" Brush="{DynamicResource OrgAccent}" /&gt;
/// </code>
/// </summary>
public sealed class Icon : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind), typeof(string), typeof(Icon),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BrushProperty = DependencyProperty.Register(
        nameof(Brush), typeof(Brush), typeof(Icon),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(Icon),
        new FrameworkPropertyMetadata(1.7, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The icon's name: Envelope, Document, Warning, Trophy, Calendar, Search, People, Money, Bell, Star, Crown, Broadcast, Pen, Check, Newspaper.</summary>
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public Brush? Brush { get => (Brush?)GetValue(BrushProperty); set => SetValue(BrushProperty, value); }
    /// <summary>Line weight on the 24-unit grid (scaled with the icon).</summary>
    public double StrokeThickness { get => (double)GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }

    static Icon()
    {
        // Defaults as metadata (not local values) so templates and styles can size an icon.
        WidthProperty.OverrideMetadata(typeof(Icon), new FrameworkPropertyMetadata(20.0));
        HeightProperty.OverrideMetadata(typeof(Icon), new FrameworkPropertyMetadata(20.0));
        SnapsToDevicePixelsProperty.OverrideMetadata(typeof(Icon), new FrameworkPropertyMetadata(true));
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (string.IsNullOrEmpty(Kind)) return;
        if ((TryFindResource("Icon." + Kind) ?? Application.Current?.TryFindResource("Icon." + Kind)) is not Geometry g) return;
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        var brush = Brush ?? (GetValue(TextElementForeground) as Brush) ?? Brushes.Gray;
        double scale = size / 24.0;
        var pen = new Pen(brush, StrokeThickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        dc.PushTransform(new TranslateTransform((ActualWidth - size) / 2, (ActualHeight - size) / 2));
        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.DrawGeometry(null, pen, g);
        dc.Pop();
        dc.Pop();
    }

    // The inherited text colour, so an icon in a button or a heading matches its words.
    private static readonly DependencyProperty TextElementForeground =
        System.Windows.Documents.TextElement.ForegroundProperty.AddOwner(typeof(Icon),
            new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));
}
