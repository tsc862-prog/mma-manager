using System.Windows;

namespace MmaManager.Wpf;

/// <summary>
/// The fight-night broadcast, as its own window: opens when the player watches a
/// card live and closes back to the shell when the night is over. DataContext is
/// the shell MainViewModel (the hosted WatchView binds to its Watch property).
/// </summary>
public partial class WatchWindow : Window
{
    public WatchWindow() { InitializeComponent(); }
}
