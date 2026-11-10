using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using MmaManager.Wpf.ViewModels;

namespace MmaManager.Wpf;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        Shell.DisplayPreferences.Current.Initialize();
        InitializeComponent();
        DataContext = new MainViewModel();
    }

    // Any click inside the start menu launches/acts and folds the menu away.
    private void CloseStartMenu(object sender, MouseButtonEventArgs e) => StartToggle.IsChecked = false;

    protected override void OnClosing(CancelEventArgs e)
    {
        // Remember where the apps live for the next session.
        (DataContext as MainViewModel)?.Desktop.SaveLayout();
        base.OnClosing(e);
    }
}
