using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;
using MmaManager.Wpf.ViewModels;

namespace MmaManager.Wpf;

/// <summary>
/// `MmaManager.Wpf --closecheck`: a closed window must be let go. In a shown (off-screen) shell, every
/// desktop app is opened, laid out, closed and opened and closed again, in three worlds that between them
/// install every app (today's quick start; Origins from ringside: Ringside and The Story So Far; an
/// Origins house just founded: the Open Tryout). Only weak references to the closed screens are kept;
/// after a full collection each one must be gone, while the shell itself is still running. The modal
/// windows (the Table, fight night, new game, load, found a promotion, a message) get the same test.
/// A closed screen that stays alive keeps its bindings on the shared view model live for the rest of
/// the session (the second copy of the Card Desk that fought the open one, 2026-11-20).
/// Prints CLOSECHECK OK, or the screens still alive and CLOSECHECK FAIL (exit 1).
/// </summary>
internal static class CloseCheckHarness
{
    private static readonly Size Desk = new(1500, 950);

    public static int Run(bool hold = false)
    {
        int checkedCount = 0;
        var alive = new List<string>();

        foreach (var (label, setup) in Worlds())
        {
            var win = new MainWindow();
            var vm = (MainViewModel)win.DataContext;
            setup(vm);
            Park(win);
            var root = (FrameworkElement)win.Content;
            Lay(root);

            // Every app on this desk, twice: open, lay out, let the opening animation run, close.
            var refs = new List<(string Id, WeakReference View, WeakReference Chrome)>();
            var ids = vm.Desktop.PromotionApps.Concat(vm.Desktop.WorldApps).Select(a => a.Id).ToList();
            foreach (var id in ids)
                for (int pass = 0; pass < 2; pass++)
                {
                    if (OpenAndClose(vm, root, id) is { } r) refs.Add((id, r.View, r.Chrome));
                }
            Collect();

            int before = alive.Count;
            foreach (var (id, view, chrome) in refs)
            {
                checkedCount++;
                if (view.IsAlive) alive.Add($"{label}: closed '{id}' screen ({view.Target?.GetType().Name}) still alive");
                if (chrome.IsAlive) alive.Add($"{label}: closed '{id}' window chrome still alive");
            }
            Console.WriteLine($"{(alive.Count == before ? "OK  " : "FAIL")}  {label,-9} {refs.Count} screens closed ({ids.Count} apps x2), "
                + $"{refs.Count(r => r.View.IsAlive)} screens and {refs.Count(r => r.Chrome.IsAlive)} window chromes still alive after a full GC");

            if (label == "today")
            {
                // The card walls listen to their collections weakly now: an open wall must still follow
                // its collection (after a full GC, so a listener only the GC was keeping would be gone).
                if (WallFollows(vm, root) is { } wallProblem) alive.Add(wallProblem);
                else Console.WriteLine("OK    wall      the open Roster's card wall still follows its filter after a full GC");

                foreach (var (name, wr) in Modals(vm, win))
                {
                    checkedCount++;
                    Collect();
                    bool live = wr.IsAlive;
                    if (live) alive.Add($"modal {name} still alive after it closed");
                    Console.WriteLine($"{(live ? "FAIL" : "OK  ")}  modal     {name} {(live ? "still alive" : "released")} after it closed");
                }
            }
            GC.KeepAlive(win);   // the shell runs on through every check: the session is still going
            GC.KeepAlive(vm);
        }

        foreach (var a in alive) Console.WriteLine("FAIL  " + a);
        Console.WriteLine(alive.Count == 0
            ? $"CLOSECHECK OK: {checkedCount} closed windows, every one released"
            : $"CLOSECHECK FAIL: {alive.Count} problems ({checkedCount} closed windows checked)");
        if (hold)
        {
            // `--hold`: stay up (the shells still open) so a heap dump can be taken and the roots read.
            Console.WriteLine($"HOLD  pid {Environment.ProcessId}, holding 180 s for a heap dump");
            Thread.Sleep(TimeSpan.FromSeconds(180));
        }
        return alive.Count == 0 ? 0 : 1;
    }

    private static IEnumerable<(string, Action<MainViewModel>)> Worlds()
    {
        yield return ("today", vm => vm.AdvanceMonthCommand.Execute(null));
        yield return ("ringside", vm => vm.StartWorld(Core.Sim.Booking.WorldBuilder.Options.OriginsObserver(1993), "Ringside."));
        yield return ("founded", vm =>
        {
            vm.StartWorld(Core.Sim.Booking.WorldBuilder.Options.OriginsObserver(1993), "Ringside.");
            for (int i = 0; i < 12; i++) vm.AdvanceQuarterCommand.Execute(null);
            var f = new FoundingViewModel(vm.LiveWorld!) { Name = "Lantern Street Fighting" };
            f.FoundCommand.Execute(null);
            if (f.Founded is null) throw new InvalidOperationException("founding refused: " + f.Error);
            vm.AfterFounding(f.Founded);   // the tryout is still to call: its app is on the desk
        });
    }

    /// <summary>Open one app, lay it out as play would (opening animation on), close it. Only weak
    /// references leave this frame.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference View, WeakReference Chrome)? OpenAndClose(MainViewModel vm, FrameworkElement root, string id)
    {
        if (!vm.Desktop.PromotionApps.Concat(vm.Desktop.WorldApps).Any(a => a.Id == id)) return null;   // left the desk
        foreach (var open in vm.Desktop.Windows.ToList()) vm.Desktop.Close(open);
        Lay(root);
        bool headless = MainViewModel.HeadlessAutoBoot;
        MainViewModel.HeadlessAutoBoot = false;   // the window chrome as in play: its opening storyboard runs
        try
        {
            vm.Desktop.Open(id);
            var w = vm.Desktop.Windows.FirstOrDefault(x => x.Spec.Id == id);
            if (w is null) return null;
            Lay(root);
            Pump(TimeSpan.FromMilliseconds(250));   // let the 0.14 s opening animation finish
            var chrome = FindChrome(root, w);
            var r = (new WeakReference(w.Content), new WeakReference(chrome));
            vm.Desktop.Close(w);
            Lay(root);
            return r;
        }
        finally { MainViewModel.HeadlessAutoBoot = headless; }
    }

    /// <summary>Open the Roster, collect, switch its filter: the wall's cards must change with it.</summary>
    private static string? WallFollows(MainViewModel vm, FrameworkElement root)
    {
        foreach (var open in vm.Desktop.Windows.ToList()) vm.Desktop.Close(open);
        vm.MyRoster.ShowEveryone = true;
        vm.Desktop.Open("roster");
        Lay(root); Pump(TimeSpan.FromMilliseconds(100)); Lay(root);
        var wall = Find<Controls.CardWall>(root);
        if (wall is null) return "wall: no card wall in the open Roster";
        int Cards() => wall.Lines.Sum(l => l.Cards.Count);
        int everyone = Cards();
        Collect();
        vm.MyRoster.ShowInjured = true;   // a different, smaller set of cards
        Lay(root); Pump(TimeSpan.FromMilliseconds(100)); Lay(root);
        int injured = Cards(), want = vm.MyRoster.Cards.Count(c => c is not Controls.CardGroup and not Controls.CardBanner);
        vm.MyRoster.ShowEveryone = true;
        vm.Desktop.Close(vm.Desktop.Windows.First(w => w.Spec.Id == "roster"));
        Lay(root);
        return everyone > 0 && injured == want && injured != everyone
            ? null
            : $"wall: the open Roster's wall did not follow its filter (everyone {everyone} cards, injured {injured} on the wall vs {want} in the list)";
    }

    private static T? Find<T>(DependencyObject root) where T : DependencyObject
    {
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var d = stack.Pop();
            if (d is T t) return t;
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++)
                stack.Push(System.Windows.Media.VisualTreeHelper.GetChild(d, i));
        }
        return null;
    }

    private static Shell.AppWindow? FindChrome(DependencyObject root, object window)
    {
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var d = stack.Pop();
            if (d is Shell.AppWindow aw && ReferenceEquals(aw.DataContext, window)) return aw;
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); i++)
                stack.Push(System.Windows.Media.VisualTreeHelper.GetChild(d, i));
        }
        return null;
    }

    /// <summary>Each modal window shown over the shell, laid out, then closed.</summary>
    private static IEnumerable<(string, WeakReference)> Modals(MainViewModel vm, Window owner)
    {
        yield return ("Table (negotiation)", ShowAndClose(owner, () => Negotiation(vm)));
        yield return ("WatchWindow (fight night)", ShowAndClose(owner, () => new WatchWindow { DataContext = vm }));
        yield return ("NewGameWindow", ShowAndClose(owner, () => new NewGameWindow()));
        yield return ("LoadGameWindow", ShowAndClose(owner, () => new LoadGameWindow()));
        yield return ("FoundPromotionWindow", ShowAndClose(owner, () => new FoundPromotionWindow(new FoundingViewModel(vm.LiveWorld!), interactive: false)));
        yield return ("MessageWindow", ShowAndClose(owner, () => new MessageWindow("CHECK", "A closed message must be let go.")));
    }

    private static Window Negotiation(MainViewModel vm)
    {
        var w = vm.LiveWorld!;
        var mine = w.Get(w.PlayerPromotion)!;
        var who = w.Fighters.Values.First(f => f.Contract?.Promotion == mine.Id);
        var exp = new Core.Sim.Contracts.FighterValuation().Evaluate(w, who);
        var session = new Core.Sim.Contracts.NegotiationSession(w, who, mine, exp);
        return new NegotiationWindow { DataContext = new NegotiationViewModel(w, who, mine, exp, session, renewal: true) };
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ShowAndClose(Window owner, Func<Window> make)
    {
        var dlg = make();
        dlg.Owner = owner;
        dlg.WindowStartupLocation = WindowStartupLocation.Manual;
        dlg.Left = -32000; dlg.Top = -32000; dlg.ShowInTaskbar = false; dlg.ShowActivated = false;
        dlg.Show();   // (ShowDialog would block the harness; the window and its bindings are the same)
        Pump(TimeSpan.FromMilliseconds(250));
        dlg.Close();
        Pump(TimeSpan.FromMilliseconds(50));
        return new WeakReference(dlg);
    }

    private static void Park(Window win)
    {
        win.WindowStartupLocation = WindowStartupLocation.Manual;
        win.Left = -32000; win.Top = -32000; win.Width = Desk.Width; win.Height = Desk.Height;
        win.ShowInTaskbar = false; win.ShowActivated = false;
        win.Show();
    }

    private static void Lay(FrameworkElement el)
    {
        el.Measure(Desk); el.Arrange(new Rect(Desk)); el.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(() => { }));
    }

    /// <summary>Run the dispatcher (render ticks, animations, deferred work) for a while.</summary>
    private static void Pump(TimeSpan span)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = span };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    /// <summary>A full collection, with the dispatcher drained in between (WPF lets go of some things
    /// only on its own idle passes).</summary>
    private static void Collect()
    {
        for (int i = 0; i < 3; i++)
        {
            Pump(TimeSpan.FromMilliseconds(60));
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
    }
}
