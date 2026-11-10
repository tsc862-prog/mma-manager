using System.Windows;
using MmaManager.Wpf.ViewModels;

namespace MmaManager.Wpf;

/// <summary>The negotiating room — a modal sit-down driven by <see cref="NegotiationViewModel"/>.</summary>
public partial class NegotiationWindow : Window
{
    public NegotiationWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is not NegotiationViewModel vm) return;
            vm.CloseRequested += ok =>
            {
                if (ok) DialogResult = true;   // a signed deal closes the room on the spot
                else Close();                  // walked away — nothing to commit
            };
            vm.Transcript.CollectionChanged += (_, _) => TranscriptScroll.ScrollToEnd();
        };
    }
}
