using System.Windows.Input;

namespace MmaManager.Wpf.Mvvm;

/// <summary>
/// The app-wide "go there" commands, reachable from any template with
/// <c>{x:Static mvvm:AppCommands.OpenFighter}</c>. A card wall draws hundreds of
/// cards; finding the window by walking up the tree from every card (a
/// RelativeSource AncestorType binding) costs real time, a static lookup costs none.
/// Set by the shell (MainViewModel) when it starts.
/// </summary>
public static class AppCommands
{
    private static readonly ICommand Nothing = new RelayCommand(() => { }, () => false);

    /// <summary>Open a fighter's profile (parameter = fighter id).</summary>
    public static ICommand OpenFighter { get; set; } = Nothing;
    /// <summary>Pin/unpin a fighter on the shortlist (parameter = fighter id).</summary>
    public static ICommand ToggleShortlist { get; set; } = Nothing;
    /// <summary>Open a promotion's page (parameter = promotion id).</summary>
    public static ICommand OpenOrg { get; set; } = Nothing;
    /// <summary>Open an event's page (parameter = event id).</summary>
    public static ICommand OpenEvent { get; set; } = Nothing;
}
