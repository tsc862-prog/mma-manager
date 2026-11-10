using System.Windows;
using System.Windows.Controls;

namespace MmaManager.Wpf.Controls;

/// <summary>
/// Lays a screen's panels out in as many columns as the WINDOW is wide enough
/// for (1 to <see cref="MaxColumns"/>), each panel dropping into the shortest
/// column — so an app reads the same floating at 1180 px or maximised on a
/// 2560 monitor, and nothing is clipped: the panel grows tall and the host
/// ScrollViewer scrolls. Order children by importance; the first ones land at
/// the top.
///
/// A child with <c>controls:MasonryPanel.FullWidth="True"</c> spans every column
/// (a hero band, a strip of cards) and starts a fresh row of columns below it.
/// Host it in a ScrollViewer with HorizontalScrollBarVisibility="Disabled".
/// </summary>
public sealed class MasonryPanel : Panel
{
    public static readonly DependencyProperty MinColumnWidthProperty = DependencyProperty.Register(
        nameof(MinColumnWidth), typeof(double), typeof(MasonryPanel),
        new FrameworkPropertyMetadata(380.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MaxColumnsProperty = DependencyProperty.Register(
        nameof(MaxColumns), typeof(int), typeof(MasonryPanel),
        new FrameworkPropertyMetadata(3, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(MasonryPanel),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty FullWidthProperty = DependencyProperty.RegisterAttached(
        "FullWidth", typeof(bool), typeof(MasonryPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static bool GetFullWidth(DependencyObject d) => (bool)d.GetValue(FullWidthProperty);
    public static void SetFullWidth(DependencyObject d, bool value) => d.SetValue(FullWidthProperty, value);

    /// <summary>The narrowest a column may get before the panel drops a column.</summary>
    public double MinColumnWidth { get => (double)GetValue(MinColumnWidthProperty); set => SetValue(MinColumnWidthProperty, value); }
    public int MaxColumns { get => (int)GetValue(MaxColumnsProperty); set => SetValue(MaxColumnsProperty, value); }
    /// <summary>Space between columns and between stacked panels.</summary>
    public double Gap { get => (double)GetValue(GapProperty); set => SetValue(GapProperty, value); }

    /// <summary>How many columns are showing right now (read by views that adapt their own content).</summary>
    public int Columns { get; private set; } = 1;

    private readonly List<Rect> _slots = new();

    protected override Size MeasureOverride(Size available)
    {
        double width = double.IsInfinity(available.Width) ? MinColumnWidth * MaxColumns : available.Width;
        double height = Place(width, measure: true);
        return new Size(width, height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        Place(final.Width, measure: false);
        for (int i = 0; i < InternalChildren.Count && i < _slots.Count; i++)
            InternalChildren[i].Arrange(_slots[i]);
        return final;
    }

    private double Place(double width, bool measure)
    {
        double gap = Gap;
        int cols = Math.Clamp((int)((width + gap) / (MinColumnWidth + gap)), 1, Math.Max(1, MaxColumns));
        Columns = cols;
        double colWidth = Math.Max(0, (width - gap * (cols - 1)) / cols);
        var heights = new double[cols];
        _slots.Clear();

        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                if (measure) child.Measure(new Size(0, 0));
                _slots.Add(new Rect());
                continue;
            }
            if (GetFullWidth(child))
            {
                double top = heights.Max();
                if (measure) child.Measure(new Size(width, double.PositiveInfinity));
                double h = child.DesiredSize.Height;
                _slots.Add(new Rect(0, top, width, h));
                for (int c = 0; c < cols; c++) heights[c] = top + h + gap;
            }
            else
            {
                int col = Array.IndexOf(heights, heights.Min());
                if (measure) child.Measure(new Size(colWidth, double.PositiveInfinity));
                double h = child.DesiredSize.Height;
                _slots.Add(new Rect(col * (colWidth + gap), heights[col], colWidth, h));
                heights[col] += h + gap;
            }
        }
        return Math.Max(0, heights.Max() - gap);
    }
}
