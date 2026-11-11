using System.Windows;

namespace MmaManager.Wpf.Controls;

/// <summary>
/// Lets a screen tighten when its WINDOW is short (a 1366×768 laptop, a small
/// floating window) so real content shows on first open. Put
/// <c>controls:Adaptive.CompactBelowHeight="820"</c> on the screen's root; every
/// element inside can then trigger on the inherited
/// <c>controls:Adaptive.IsCompact</c> (smaller padding, a hidden detail line).
/// </summary>
public static class Adaptive
{
    public static readonly DependencyProperty CompactBelowHeightProperty = DependencyProperty.RegisterAttached(
        "CompactBelowHeight", typeof(double), typeof(Adaptive), new PropertyMetadata(0.0, OnThresholdChanged));

    public static readonly DependencyProperty IsCompactProperty = DependencyProperty.RegisterAttached(
        "IsCompact", typeof(bool), typeof(Adaptive),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    public static double GetCompactBelowHeight(DependencyObject d) => (double)d.GetValue(CompactBelowHeightProperty);
    public static void SetCompactBelowHeight(DependencyObject d, double value) => d.SetValue(CompactBelowHeightProperty, value);
    public static bool GetIsCompact(DependencyObject d) => (bool)d.GetValue(IsCompactProperty);
    public static void SetIsCompact(DependencyObject d, bool value) => d.SetValue(IsCompactProperty, value);

    private static void OnThresholdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe) return;
        fe.SizeChanged -= OnSizeChanged;
        fe.SizeChanged += OnSizeChanged;
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var fe = (FrameworkElement)sender;
        bool compact = e.NewSize.Height > 0 && e.NewSize.Height < GetCompactBelowHeight(fe);
        if (GetIsCompact(fe) != compact) SetIsCompact(fe, compact);
    }
}
