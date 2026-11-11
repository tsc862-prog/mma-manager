using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace MmaManager.Wpf.Controls;

/// <summary>A group heading inside a <see cref="CardWall"/> ("Lightweight · 24 fighters").</summary>
public sealed class CardGroup
{
    public required string Title { get; init; }
    public string Detail { get; init; } = "";
}

/// <summary>
/// A full-width feature line inside a <see cref="CardWall"/> (the News lead story):
/// drawn with the wall's <see cref="CardWall.BannerTemplate"/>, scrolling with the cards.
/// </summary>
public sealed class CardBanner
{
    public required object Content { get; init; }
    /// <summary>Equal keys draw identically (lets a refresh keep the drawn banner).</summary>
    public string Key { get; init; } = "";
}

/// <summary>
/// A card that can say whether it would draw exactly like another one: two cards
/// with the same key are interchangeable, so a refresh that changed nothing on
/// screen rebuilds nothing on screen.
/// </summary>
public interface ICardKey
{
    string CardKey { get; }
}

/// <summary>One laid-out line of the wall: a heading, or up to N cards side by side.</summary>
public sealed class CardWallLine
{
    public CardGroup? Header { get; init; }
    public CardBanner? Banner { get; init; }
    public IReadOnlyList<object> Cards { get; init; } = Array.Empty<object>();
    public bool IsHeader => Header is not null;
    public bool IsBanner => Banner is not null;
    public bool HasCards => Cards.Count > 0;

    // How to draw the line, carried on the line itself rather than found by walking up
    // to the wall: an ancestor binding resolves late, so the list's first measure saw
    // template-less 36-px lines and built 24 of them where 6 fill the screen.
    public DataTemplate? CardTemplate { get; init; }
    public DataTemplate? BannerTemplate { get; init; }
    public int Columns { get; init; } = 1;
    public double Width { get; init; } = double.NaN;
    public double BannerWidth => double.IsNaN(Width) ? double.NaN : Math.Max(0, Width - 12);
    internal string Key { get; init; } = "";
}

/// <summary>
/// A wrapping wall of cards that stays fast with hundreds of items: the cards
/// are chunked into lines as wide as the window allows, and the lines sit in a
/// virtualising list, so only what is on screen is ever built (portraits load
/// as their line scrolls into view). Mix <see cref="CardGroup"/> items into
/// <see cref="ItemsSource"/> to break the wall into titled sections.
///
/// Usage:
/// <code>
/// &lt;controls:CardWall ItemsSource="{Binding Cards}" CardWidth="300"
///                     CardTemplate="{StaticResource FighterCardTemplate}" /&gt;
/// </code>
/// Every card is given exactly <see cref="CardWidth"/> (stretched to fill the
/// line evenly), so the card template should not set its own Width.
/// </summary>
public sealed class CardWall : Control
{
    static CardWall() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(CardWall), new FrameworkPropertyMetadata(typeof(CardWall)));

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource), typeof(IEnumerable), typeof(CardWall),
        new PropertyMetadata(null, (d, e) => ((CardWall)d).OnSourceChanged(e.OldValue as IEnumerable, e.NewValue as IEnumerable)));

    public static readonly DependencyProperty CardTemplateProperty = DependencyProperty.Register(
        nameof(CardTemplate), typeof(DataTemplate), typeof(CardWall), new PropertyMetadata(null, (d, _) => ((CardWall)d).ScheduleRebuild()));

    public static readonly DependencyProperty BannerTemplateProperty = DependencyProperty.Register(
        nameof(BannerTemplate), typeof(DataTemplate), typeof(CardWall), new PropertyMetadata(null, (d, _) => ((CardWall)d).ScheduleRebuild()));

    /// <summary>How a <see cref="CardBanner"/>'s content is drawn (full width).</summary>
    public DataTemplate? BannerTemplate { get => (DataTemplate?)GetValue(BannerTemplateProperty); set => SetValue(BannerTemplateProperty, value); }

    public static readonly DependencyProperty CardWidthProperty = DependencyProperty.Register(
        nameof(CardWidth), typeof(double), typeof(CardWall), new PropertyMetadata(300.0, (d, _) => ((CardWall)d).Rebuild()));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(CardWall), new PropertyMetadata(12.0));


    private static readonly DependencyPropertyKey LineWidthKey = DependencyProperty.RegisterReadOnly(
        nameof(LineWidth), typeof(double), typeof(CardWall), new PropertyMetadata(double.NaN));
    public static readonly DependencyProperty LineWidthProperty = LineWidthKey.DependencyProperty;
    /// <summary>The width every line (and the banner) is drawn at: the wall's own width, so a
    /// card with a long unwrapped line can never push the wall wider than its window.</summary>
    public double LineWidth => (double)GetValue(LineWidthProperty);

    private static readonly DependencyPropertyKey ColumnsKey = DependencyProperty.RegisterReadOnly(
        nameof(Columns), typeof(int), typeof(CardWall), new PropertyMetadata(1));
    public static readonly DependencyProperty ColumnsProperty = ColumnsKey.DependencyProperty;

    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public DataTemplate? CardTemplate { get => (DataTemplate?)GetValue(CardTemplateProperty); set => SetValue(CardTemplateProperty, value); }
    /// <summary>The narrowest a card may be; the wall fits as many as the width allows.</summary>
    public double CardWidth { get => (double)GetValue(CardWidthProperty); set => SetValue(CardWidthProperty, value); }
    public double Gap { get => (double)GetValue(GapProperty); set => SetValue(GapProperty, value); }
    /// <summary>The laid-out lines (bound by the template's virtualising list). Lines whose
    /// cards draw the same as before are kept as-is, so their visuals are not rebuilt.</summary>
    public System.Collections.ObjectModel.ObservableCollection<CardWallLine> Lines { get; } = new();
    /// <summary>Cards per line at the current width.</summary>
    public int Columns => (int)GetValue(ColumnsProperty);

    public CardWall()
    {
        // Rebuild after the layout pass that resized us, not inside it: rebuilding mid-pass
        // let the virtualising list measure its new lines before it knew its viewport, and
        // it built four screens of cards instead of one.
        SizeChanged += (_, e) => { if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 0.5) ScheduleRebuild(); };
    }

    /// <summary>
    /// Never measure the wall without a height limit. An app window can be measured once
    /// before its size is settled; a virtualising list told "infinite height" builds every
    /// card it has (the Roster built 24 lines of cards to show 5). Capping the guess at the
    /// work area keeps that first pass to about a screenful.
    /// </summary>
    protected override Size MeasureOverride(Size constraint)
    {
        if (double.IsInfinity(constraint.Height))
            constraint = new Size(constraint.Width, SystemParameters.WorkArea.Height);
        return base.MeasureOverride(constraint);
    }

    private void OnSourceChanged(IEnumerable? oldSource, IEnumerable? newSource)
    {
        if (oldSource is INotifyCollectionChanged o) o.CollectionChanged -= OnCollectionChanged;
        if (newSource is INotifyCollectionChanged n) n.CollectionChanged += OnCollectionChanged;
        Rebuild();
    }

    // A screen refresh clears and refills its collection item by item; one
    // rebuild once the dispatcher is free covers the whole refill.
    private bool _pending;
    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScheduleRebuild();

    private void ScheduleRebuild()
    {
        if (_pending) return;
        _pending = true;
        Dispatcher.BeginInvoke(new Action(() => { _pending = false; Rebuild(); }), System.Windows.Threading.DispatcherPriority.DataBind);
    }

    private void Rebuild()
    {
        // Not laid out yet: wait for the first size (SizeChanged rebuilds), rather than
        // building one-card lines now and throwing every one of them away a moment later.
        if (ActualWidth <= 0) { if (Lines.Count > 0) Lines.Clear(); return; }
        // Room for the scrollbar so a full line never forces a horizontal scroll.
        double width = ActualWidth - Padding.Left - Padding.Right - 14;
        // Every line is held to the wall's width: a card with a long unwrapped line
        // must never push a line (and the wall) wider than the window it sits in.
        double lineWidth = Math.Max(0, width + Gap);
        SetValue(LineWidthKey, lineWidth);
        int cols = width <= 0 ? 1 : Math.Max(1, (int)((width + Gap) / (CardWidth + Gap)));
        SetValue(ColumnsKey, cols);

        // A heading rides on top of the first line of its cards (one container, not two):
        // lines then share one height, so the virtualising list's size guess is right
        // and it builds only the lines on screen — a lone 40-px heading line made it
        // guess small and build three screens of cards on open.
        var lines = new List<CardWallLine>();
        var run = new List<object>();
        CardGroup? heading = null;
        void Flush()
        {
            for (int i = 0; i < run.Count; i += cols)
            {
                var cards = run.Skip(i).Take(cols).ToList();
                bool keyed = cards.All(c => c is ICardKey);
                string head = heading is null ? "" : "H#" + heading.Title + "¦" + heading.Detail + "¦";
                lines.Add(new CardWallLine
                {
                    Header = heading,
                    Cards = cards,
                    CardTemplate = CardTemplate,
                    Columns = cols,
                    Width = lineWidth,
                    Key = keyed && CardTemplate is not null ? head + cols + "#" + lineWidth + "#" + string.Join("¦", cards.Select(c => ((ICardKey)c).CardKey)) : "",
                });
                heading = null;
            }
            if (heading is not null) lines.Add(new CardWallLine { Header = heading, Key = "H#" + heading.Title + "¦" + heading.Detail });
            heading = null;
            run.Clear();
        }
        if (ItemsSource is not null)
            foreach (var item in ItemsSource)
            {
                if (item is CardGroup g) { Flush(); heading = g; }
                else if (item is CardBanner b)
                {
                    Flush();
                    lines.Add(new CardWallLine { Banner = b, BannerTemplate = BannerTemplate, Width = lineWidth,
                        Key = b.Key.Length > 0 && BannerTemplate is not null ? "B#" + lineWidth + "#" + b.Key : "" });
                }
                else run.Add(item);
            }
        Flush();

        // Sync in place: keep every line that would draw the same; replace the rest.
        for (int i = 0; i < lines.Count; i++)
        {
            if (i < Lines.Count)
            {
                if (lines[i].Key.Length == 0 || lines[i].Key != Lines[i].Key) Lines[i] = lines[i];
            }
            else Lines.Add(lines[i]);
        }
        while (Lines.Count > lines.Count) Lines.RemoveAt(Lines.Count - 1);
    }
}
