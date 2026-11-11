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
    public IReadOnlyList<object> Cards { get; init; } = Array.Empty<object>();
    public bool IsHeader => Header is not null;
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
        nameof(CardTemplate), typeof(DataTemplate), typeof(CardWall));

    public static readonly DependencyProperty CardWidthProperty = DependencyProperty.Register(
        nameof(CardWidth), typeof(double), typeof(CardWall), new PropertyMetadata(300.0, (d, _) => ((CardWall)d).Rebuild()));

    public static readonly DependencyProperty GapProperty = DependencyProperty.Register(
        nameof(Gap), typeof(double), typeof(CardWall), new PropertyMetadata(12.0));


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
        SizeChanged += (_, e) => { if (Math.Abs(e.NewSize.Width - e.PreviousSize.Width) > 0.5) Rebuild(); };
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
    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_pending) return;
        _pending = true;
        Dispatcher.BeginInvoke(new Action(() => { _pending = false; Rebuild(); }), System.Windows.Threading.DispatcherPriority.DataBind);
    }

    private void Rebuild()
    {
        // Room for the scrollbar so a full line never forces a horizontal scroll.
        double width = ActualWidth - Padding.Left - Padding.Right - 14;
        int cols = width <= 0 ? 1 : Math.Max(1, (int)((width + Gap) / (CardWidth + Gap)));
        SetValue(ColumnsKey, cols);

        var lines = new List<CardWallLine>();
        var run = new List<object>();
        void Flush()
        {
            for (int i = 0; i < run.Count; i += cols)
            {
                var cards = run.Skip(i).Take(cols).ToList();
                bool keyed = cards.All(c => c is ICardKey);
                lines.Add(new CardWallLine
                {
                    Cards = cards,
                    Key = keyed ? cols + "#" + string.Join("¦", cards.Select(c => ((ICardKey)c).CardKey)) : "",
                });
            }
            run.Clear();
        }
        if (ItemsSource is not null)
            foreach (var item in ItemsSource)
            {
                if (item is CardGroup g) { Flush(); lines.Add(new CardWallLine { Header = g, Key = "H#" + g.Title + "¦" + g.Detail }); }
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
