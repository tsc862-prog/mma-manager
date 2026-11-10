using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace MmaManager.Wpf.Mvvm;

/// <summary>One piece of a broadcast line: plain text, or a fighter's name tinted by corner.</summary>
public sealed record CornerSeg(string Text, char Corner);   // corner: 'R', 'B', or ' ' for plain

/// <summary>
/// Renders a broadcast line as inline runs, tinting each fighter's name in
/// their corner color (light red / light blue) so the play-by-play is easy
/// to follow at a glance. Non-name text keeps the TextBlock's own styling.
/// </summary>
public static class CornerText
{
    private static readonly Brush Red = Frozen(0xE8, 0x8A, 0x80);
    private static readonly Brush Blue = Frozen(0x82, 0xAE, 0xE8);

    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.RegisterAttached(
        "Segments", typeof(IReadOnlyList<CornerSeg>), typeof(CornerText), new PropertyMetadata(null, OnSegmentsChanged));

    public static void SetSegments(DependencyObject d, IReadOnlyList<CornerSeg>? value) => d.SetValue(SegmentsProperty, value);
    public static IReadOnlyList<CornerSeg>? GetSegments(DependencyObject d) => (IReadOnlyList<CornerSeg>?)d.GetValue(SegmentsProperty);

    private static void OnSegmentsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;
        tb.Inlines.Clear();
        if (e.NewValue is not IReadOnlyList<CornerSeg> segs) return;
        foreach (var seg in segs)
        {
            var run = new Run(seg.Text);
            if (seg.Corner == 'R') { run.Foreground = Red; run.FontWeight = FontWeights.SemiBold; }
            else if (seg.Corner == 'B') { run.Foreground = Blue; run.FontWeight = FontWeights.SemiBold; }
            tb.Inlines.Add(run);
        }
    }

    /// <summary>Split a line into plain/red/blue segments wherever either fighter's name appears.</summary>
    public static IReadOnlyList<CornerSeg> Segment(string text, string red, string blue)
    {
        var segs = new List<CornerSeg>();
        int pos = 0;
        while (pos < text.Length)
        {
            int ir = red.Length > 0 ? text.IndexOf(red, pos, StringComparison.Ordinal) : -1;
            int ib = blue.Length > 0 ? text.IndexOf(blue, pos, StringComparison.Ordinal) : -1;
            if (ir < 0 && ib < 0) { segs.Add(new CornerSeg(text[pos..], ' ')); break; }

            // Nearest match wins; on a tie (one name prefixes the other) take the longer.
            bool takeRed = ib < 0 || (ir >= 0 && (ir < ib || (ir == ib && red.Length >= blue.Length)));
            int at = takeRed ? ir : ib;
            string name = takeRed ? red : blue;
            if (at > pos) segs.Add(new CornerSeg(text[pos..at], ' '));
            segs.Add(new CornerSeg(name, takeRed ? 'R' : 'B'));
            pos = at + name.Length;
        }
        if (segs.Count == 0) segs.Add(new CornerSeg(text, ' '));
        return segs;
    }

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
