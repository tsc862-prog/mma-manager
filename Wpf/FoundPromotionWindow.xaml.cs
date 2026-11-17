using System.Windows;
using MmaManager.Wpf.ViewModels;

namespace MmaManager.Wpf;

/// <summary>
/// The FOUND YOUR PROMOTION window (Origins, from ringside). Founding itself is the view model's
/// (<see cref="FoundingViewModel"/>, which calls the sim); the window wires the ask-first dialog and closes
/// once the house exists.
/// </summary>
public partial class FoundPromotionWindow : Window
{
    public FoundPromotionWindow(FoundingViewModel vm, bool interactive = true)
    {
        InitializeComponent();
        DataContext = vm;
        if (interactive)
            vm.Confirm = (body, detail) => MessageWindow.Ask($"Open {vm.Name.Trim()}?", body, detail, "Open the doors", "Not yet");
        vm.Done = () => { if (IsLoaded) DialogResult = true; };
        Loaded += (_, _) => NameBox.Focus();
    }

    public FoundingViewModel Model => (FoundingViewModel)DataContext;
}
