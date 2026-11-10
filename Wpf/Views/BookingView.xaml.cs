using System.Windows;
using System.Windows.Controls;

namespace MmaManager.Wpf.Views;

/// <summary>
/// Book Event's layout follows the window, not the other way round. The fight
/// card is the star: it always gets the spare width. Below a wide window the
/// poster folds into a tab beside the matchup builder, and below a normal one
/// the side rails narrow. The card's matchup cell stacks the two corners when
/// the card column is too narrow to show both full names on one line. All of
/// this is presentation only — nothing here reads or writes the game.
/// </summary>
public partial class BookingView : UserControl
{
    // Thresholds on this screen's own width (the app window minus its chrome).
    private const double WideFrom = 1780;      // room for rail + card + builder + poster
    private const double NormalFrom = 1200;    // below this the rails narrow further
    private const double SideBySideFrom = 1000; // card width at which red | vs | blue fits on one line for most names

    private enum Mode { Unset, Narrow, Normal, Wide }
    private Mode _mode = Mode.Unset;
    private bool? _sideBySide;

    public BookingView()
    {
        InitializeComponent();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => ApplyMode(e.NewSize.Width);

    private void ApplyMode(double width)
    {
        var mode = width >= WideFrom ? Mode.Wide : width >= NormalFrom ? Mode.Normal : Mode.Narrow;
        if (mode == _mode) return;
        _mode = mode;

        switch (mode)
        {
            case Mode.Wide:
                RailCol.Width = new GridLength(250);
                BuilderCol.Width = new GridLength(430);
                PosterCol.Width = new GridLength(330);
                break;
            case Mode.Normal:
                RailCol.Width = new GridLength(230);
                BuilderCol.Width = new GridLength(400);
                PosterCol.Width = new GridLength(0);
                break;
            default:
                RailCol.Width = new GridLength(200);
                BuilderCol.Width = new GridLength(370);
                PosterCol.Width = new GridLength(0);
                break;
        }

        bool wide = mode == Mode.Wide;
        RightTabs.Visibility = wide ? Visibility.Collapsed : Visibility.Visible;
        BuilderLabel.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
        PosterLabel.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumn(PosterPanel, wide ? 3 : 2);
        Grid.SetRow(PosterPanel, wide ? 0 : 1);
        Grid.SetRowSpan(PosterPanel, wide ? 2 : 1);
        PosterPanel.Margin = wide ? new Thickness(10, 0, 0, 0) : new Thickness(0);
        ShowRightTab();
    }

    private void OnRightTabChanged(object sender, RoutedEventArgs e)
    {
        if (_mode != Mode.Unset) ShowRightTab();
    }

    // Wide: builder and poster both show. Otherwise the checked tab wins the column.
    private void ShowRightTab()
    {
        bool wide = _mode == Mode.Wide;
        bool poster = PosterTab.IsChecked == true;
        BuilderHost.Visibility = wide || !poster ? Visibility.Visible : Visibility.Collapsed;
        PosterPanel.Visibility = wide || poster ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnCardSizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool side = e.NewSize.Width >= SideBySideFrom;
        if (side == _sideBySide) return;
        _sideBySide = side;
        MatchupColumn.CellTemplate = (DataTemplate)FindResource(side ? "MatchupSideBySide" : "MatchupStacked");
    }

    /// <summary>Harness hook: open the poster tab (no effect on a wide window, where it always shows).</summary>
    internal void ShowPosterTab() => PosterTab.IsChecked = true;
}
