using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace MmaManager.Wpf.Shell;

/// <summary>
/// The chrome of one desktop app window: title bar (drag to move, double-click
/// to maximize/restore), minimize/maximize/close, and a resize grip. All state
/// lives on the bound <see cref="OpenWindow"/>.
/// </summary>
public partial class AppWindow : UserControl
{
    public AppWindow() { InitializeComponent(); }

    private OpenWindow? Win => DataContext as OpenWindow;

    // Any click anywhere in the window brings it to the front.
    private void OnAnyMouseDown(object sender, MouseButtonEventArgs e) => Win?.Activate();

    private void OnDragTitle(object sender, DragDeltaEventArgs e)
    {
        if (Win is not { IsMaximized: false } w) return;
        w.X = Math.Max(0, w.X + e.HorizontalChange);
        w.Y = Math.Max(0, w.Y + e.VerticalChange);
    }

    private void OnTitleDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Win is { } w) w.MaximizeCommand?.Execute(null);
    }

    private void OnDragResize(object sender, DragDeltaEventArgs e)
    {
        if (Win is not { IsMaximized: false } w) return;
        w.W += e.HorizontalChange;
        w.H += e.VerticalChange;
    }
}
