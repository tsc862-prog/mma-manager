using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;
using MmaManager.Wpf.ViewModels;

namespace MmaManager.Wpf.Views;

/// <summary>
/// The Moment's takeover. Shown while <see cref="MomentsViewModel.IsShowing"/>; it takes
/// keyboard focus when it appears so Esc, Enter or Space carry straight on.
/// </summary>
public partial class MomentView : UserControl
{
    public MomentView()
    {
        InitializeComponent();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is not true) return;
            // Harness stills are taken at once: no fade, or the PNG catches it at zero.
            if (!MainViewModel.HeadlessAutoBoot)
            {
                BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
                var grow = new System.Windows.Media.ScaleTransform(0.97, 0.97);
                Card.RenderTransformOrigin = new Point(0.5, 0.5);
                Card.RenderTransform = grow;
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                grow.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, new DoubleAnimation(0.97, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
                grow.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, new DoubleAnimation(0.97, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
            }
            Dispatcher.BeginInvoke(new Action(() => Focus()), System.Windows.Threading.DispatcherPriority.Input);
        };
    }

    // Esc / Enter / Space carry on — wherever focus sits inside the card, except on one of
    // its own buttons, where Enter and Space press that button.
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (DataContext is MomentsViewModel { IsShowing: true } m
            && (e.Key == Key.Escape || ((e.Key is Key.Enter or Key.Space) && e.OriginalSource is not ButtonBase)))
        {
            m.Dismiss();
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }
}
