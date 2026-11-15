using System.Windows;
using MmaManager.Wpf.ViewModels;

namespace MmaManager.Wpf;

/// <summary>The Table: a modal sit-down with a fighter, driven by <see cref="NegotiationViewModel"/>.</summary>
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
                else Close();                  // walked away, matched or let go — the caller reads the VM
            };
            // Matching and letting him go are costly or final: ask first (in the live app).
            vm.Confirm ??= (heading, body, detail, confirm, keep) => MessageWindow.Ask(heading, body, detail, confirm, keep);
            vm.Transcript.CollectionChanged += (_, _) => TranscriptScroll.ScrollToEnd();
        };
    }
}
