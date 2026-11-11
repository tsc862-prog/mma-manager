using System.Windows;
using System.Windows.Controls;
using MmaManager.Wpf.ViewModels;

namespace MmaManager.Wpf.Views;

/// <summary>
/// The Card Desk (Book Event). The bill takes every spare pixel; the rail on
/// the right narrows on a small window. Presentation only: nothing here reads
/// or writes the game, it hands the player's picks to the view model.
/// </summary>
public partial class BookingView : UserControl
{
    private const double NarrowFrom = 1420;   // below this the rail narrows (Adaptive.IsNarrow flips at the same width)

    public BookingView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => Rail.Width = e.NewSize.Width < NarrowFrom ? 350 : 410;
    }

    // The table's two selected fighters go to the picker as a pairing (by id).
    private void OnPairClick(object sender, RoutedEventArgs e)
    {
        if (Root.DataContext is not BookingViewModel vm) return;
        var ids = TableGrid.SelectedItems.OfType<DeskTableRow>().Select(r => r.Id).ToList();
        vm.PairCommand.Execute(ids);
    }

    /// <summary>Harness hook: the desk's view model, as the screen sees it.</summary>
    internal BookingViewModel? Desk => Root.DataContext as BookingViewModel;
}
