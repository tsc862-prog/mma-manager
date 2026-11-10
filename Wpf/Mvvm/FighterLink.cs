using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MmaManager.Wpf.ViewModels;

namespace MmaManager.Wpf.Mvvm;

/// <summary>
/// Attached behavior: double-clicking a row on any fighter grid jumps to that
/// fighter's profile page. Set <c>mvvm:FighterLink.OpenOnDoubleClick="True"</c>
/// on a DataGrid whose rows are any of the fighter-bearing row types. Using
/// PreviewMouseLeftButtonDown + ClickCount is the most reliable way to catch a
/// row double-click regardless of which cell/element was hit.
/// </summary>
public static class FighterLink
{
    public static readonly DependencyProperty OpenOnDoubleClickProperty =
        DependencyProperty.RegisterAttached("OpenOnDoubleClick", typeof(bool), typeof(FighterLink),
            new PropertyMetadata(false, OnChanged));

    public static bool GetOpenOnDoubleClick(DependencyObject d) => (bool)d.GetValue(OpenOnDoubleClickProperty);
    public static void SetOpenOnDoubleClick(DependencyObject d, bool value) => d.SetValue(OpenOnDoubleClickProperty, value);

    private static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement el) return;
        if ((bool)e.NewValue) el.PreviewMouseLeftButtonDown += OnPreviewDown;
        else el.PreviewMouseLeftButtonDown -= OnPreviewDown;
    }

    private static void OnPreviewDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;

        var node = e.OriginalSource as DependencyObject;
        while (node is not null and not DataGridRow)
            node = VisualTreeHelper.GetParent(node);

        int? id = (node as DataGridRow)?.Item switch
        {
            FighterRow f => f.Id,
            MyRosterRow mr => mr.FighterId,
            RankRow r => r.FighterId,
            GlobalRankRow g => g.FighterId,
            FreeAgentRow fa => fa.Id.Value,
            MyContractRow mc => mc.Id.Value,
            _ => null,
        };
        if (id is not { } fighterId || fighterId == 0) return;

        if (Window.GetWindow((DependencyObject)sender)?.DataContext is MainViewModel vm)
        {
            vm.OpenFighter(fighterId);
            e.Handled = true;
        }
    }
}
