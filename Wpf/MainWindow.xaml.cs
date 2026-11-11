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

    // The desk's size, so new app windows open no bigger than the screen they land on.
    private void OnDesktopSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.Desktop.Area = e.NewSize;
    }

    // The Moment carries on with Esc, Enter or Space even when focus was left somewhere
    // on the desk behind it (the card itself handles keys pressed inside it).
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (DataContext is MainViewModel { Moments.IsShowing: true } vm
            && e.Key is Key.Escape or Key.Enter or Key.Space
            && !MomentLayer.IsKeyboardFocusWithin)
        {
            vm.Moments.Dismiss();
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
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
