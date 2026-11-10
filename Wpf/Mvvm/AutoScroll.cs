using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace MmaManager.Wpf.Mvvm;

/// <summary>Attached behavior: keep a ListBox scrolled to its newest item as it grows (for the live feed).</summary>
public static class AutoScroll
{
    public static readonly DependencyProperty ToEndProperty =
        DependencyProperty.RegisterAttached("ToEnd", typeof(bool), typeof(AutoScroll),
            new PropertyMetadata(false, OnChanged));

    public static void SetToEnd(DependencyObject o, bool value) => o.SetValue(ToEndProperty, value);
    public static bool GetToEnd(DependencyObject o) => (bool)o.GetValue(ToEndProperty);

    private static void OnChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        if (o is not ListBox lb || e.NewValue is not true) return;
        if (lb.Items is INotifyCollectionChanged incc)
            incc.CollectionChanged += (_, args) =>
            {
                if (args.Action == NotifyCollectionChangedAction.Add && lb.Items.Count > 0)
                    lb.ScrollIntoView(lb.Items[^1]);
            };
    }
}
