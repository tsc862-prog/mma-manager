using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MmaManager.Wpf.ViewModels;

namespace MmaManager.Wpf.Views;

/// <summary>
/// The Card Desk (Book Event). The bill takes every spare pixel; the rail on
/// the right narrows on a small window. Presentation only: nothing here reads
/// or writes the game, it hands the player's picks to the view model.
/// Keys: Ctrl+F (or typing on the list) searches the roster; arrows move through
/// the results; Enter picks the selected one.
/// </summary>
public partial class BookingView : UserControl
{
    private const double NarrowFrom = 1420;   // below this the rail narrows (Adaptive.IsNarrow flips at the same width)

    public BookingView()
    {
        InitializeComponent();
        SizeChanged += (_, e) => Rail.Width = e.NewSize.Width < NarrowFrom ? 350 : 410;
        PreviewKeyDown += OnDeskKey;
    }

    // The table's two selected fighters go to the picker as a pairing (by id).
    private void OnPairClick(object sender, RoutedEventArgs e)
    {
        if (Root.DataContext is not BookingViewModel vm) return;
        var ids = TableGrid.SelectedItems.OfType<DeskTableRow>().Select(r => r.Id).ToList();
        vm.PairCommand.Execute(ids);
    }

    // Ctrl+F: the search box (the table's in the Table view, else the picker's finder).
    private void OnDeskKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F || Keyboard.Modifiers != ModifierKeys.Control || Desk is not { } vm) return;
        e.Handled = true;
        if (vm.ShowTableView) { TableSearchBox.Focus(); TableSearchBox.SelectAll(); return; }
        vm.OpenFinder();
        Dispatcher.BeginInvoke(() => { FinderSearchBox.Focus(); FinderSearchBox.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Input);
    }

    // Down from the search box goes into the results; Enter picks the first one.
    private void OnFinderSearchKey(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Down or Key.Enter && FinderList.Items.Count > 0)
        {
            e.Handled = true;
            FinderList.SelectedIndex = Math.Max(0, FinderList.SelectedIndex);
            if (e.Key == Key.Enter) { Pick(FinderList.SelectedItem); return; }
            FocusSelected();
        }
    }

    // Enter picks the selected row; typing goes to the search; Up from the top returns to it.
    private void OnFinderListKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; Pick(FinderList.SelectedItem); return; }
        if (e.Key == Key.Up && FinderList.SelectedIndex <= 0) { e.Handled = true; FinderSearchBox.Focus(); return; }
        if (Keyboard.Modifiers == ModifierKeys.None && e.Key is >= Key.A and <= Key.Z)
        {
            FinderSearchBox.Focus();   // the key itself lands in the box
            FinderSearchBox.CaretIndex = FinderSearchBox.Text.Length;
        }
    }

    private void FocusSelected()
    {
        FinderList.ScrollIntoView(FinderList.SelectedItem);
        if (FinderList.ItemContainerGenerator.ContainerFromIndex(FinderList.SelectedIndex) is ListBoxItem item) item.Focus();
        else FinderList.Focus();
    }

    private static void Pick(object? row)
    {
        if (row is LadderEntry e && e.Command?.CanExecute(e) == true) e.Command.Execute(e);
    }

    /// <summary>Harness hook: the desk's view model, as the screen sees it.</summary>
    internal BookingViewModel? Desk => Root.DataContext as BookingViewModel;
}
