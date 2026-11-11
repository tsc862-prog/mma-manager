using System.Windows;
using System.Windows.Controls;
using MmaManager.Wpf.ViewModels;

namespace MmaManager.Wpf.Views;

public partial class PlannerView : UserControl
{
    public PlannerView()
    {
        InitializeComponent();
        // The planner builds only once its window has been opened (and on every refresh after).
        DataContextChanged += (_, _) => (DataContext as MainViewModel)?.Planner.Wanted();
    }
}

/// <summary>The rail's compact face for each kind of planner item.</summary>
public sealed class PlannerRailSelector : DataTemplateSelector
{
    public DataTemplate? Mine { get; set; }
    public DataTemplate? Rival { get; set; }
    public DataTemplate? Marker { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) => item switch
    {
        MyCardModel => Mine,
        RivalCardModel => Rival,
        PlannerMarker => Marker,
        _ => base.SelectTemplate(item, container),
    };
}
