using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MmaManager.Wpf.Mvvm;
using MmaManager.Wpf.ViewModels;

namespace MmaManager.Wpf.Shell;

/// <summary>A toast in the desktop's notification corner (auto-expires).</summary>
public sealed class Toast
{
    public required string Glyph { get; init; }
    public required string Title { get; init; }
    public string Body { get; init; } = "";
    public ICommand? ClickCommand { get; internal set; }
    public ICommand? DismissCommand { get; internal set; }
}

/// <summary>
/// One installed "app" on the desktop shell: an id, a launcher face (glyph +
/// title), a default floating size, and a factory for its screen — the same
/// UserControls the sidebar shell hosts as tabs.
/// </summary>
public sealed class AppSpec
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Glyph { get; init; }
    public required Func<Control> Create { get; init; }
    /// <summary>World-research app (blue accent) vs your-promotion app (org accent).</summary>
    public bool IsWorld { get; init; }
    public double W { get; init; } = 1180;
    public double H { get; init; } = 720;

    /// <summary>The app's own accent colour (title-bar identity, taskbar underline).</summary>
    public string Accent { get; init; } = "#E8B23A";
}

/// <summary>A running app window on the desktop: position, size, stacking, state.</summary>
public sealed class OpenWindow : ObservableObject
{
    public OpenWindow(AppSpec spec, Control content)
    {
        Spec = spec;
        Content = content;
        _w = spec.W;
        _h = spec.H;
        AccentBrush = Freeze(spec.Accent);
    }

    public AppSpec Spec { get; }
    public Control Content { get; }
    public string Title => Spec.Title;
    public string Glyph => Spec.Glyph;
    public bool IsWorld => Spec.IsWorld;

    /// <summary>The app's accent, frozen once per window.</summary>
    public System.Windows.Media.Brush AccentBrush { get; }

    private static System.Windows.Media.Brush Freeze(string hex)
    {
        var b = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }

    private double _x, _y, _w, _h;
    public double X { get => _x; set => Set(ref _x, value); }
    public double Y { get => _y; set => Set(ref _y, value); }
    public double W { get => _w; set => Set(ref _w, Math.Max(360, value)); }
    public double H { get => _h; set => Set(ref _h, Math.Max(220, value)); }

    private int _z;
    public int Z { get => _z; set => Set(ref _z, value); }

    private bool _isActive;
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }

    private bool _isMinimized;
    public bool IsMinimized { get => _isMinimized; set => Set(ref _isMinimized, value); }

    private bool _isMaximized;   // apps open floating; maximize is a click away
    public bool IsMaximized { get => _isMaximized; set => Set(ref _isMaximized, value); }

    // Wired by the desktop when the window opens.
    public ICommand? CloseCommand { get; internal set; }
    public ICommand? MinimizeCommand { get; internal set; }
    public ICommand? MaximizeCommand { get; internal set; }
    internal Action<OpenWindow>? OnActivate { get; set; }

    /// <summary>Bring-to-front + focus (any mouse-down on the window).</summary>
    public void Activate() => OnActivate?.Invoke(this);
}

/// <summary>
/// The pseudo-OS shell: the desktop's installed apps (launcher icons + start
/// menu) and the windows currently running on it. Windows are singletons per
/// app — launching an open app restores and focuses it.
/// </summary>
public sealed class DesktopViewModel : ObservableObject
{
    private int _zCounter;

    public DesktopViewModel()
    {
        var apps = new AppSpec[]
        {
            new() { Id = "home", Title = "Dashboard", Glyph = "🏠", Accent = "#E8B23A", Create = () => new Views.HomeView() },
            new() { Id = "roster", Title = "Roster", Glyph = "🥊", Accent = "#D97B4F", Create = () => new Views.RosterView() },
            new() { Id = "book", Title = "Book Event", Glyph = "🎟", Accent = "#D64550", Create = () => new Views.BookingView(), W = 1420 },
            new() { Id = "contracts", Title = "Contracts", Glyph = "✍", Accent = "#C9A94F", Create = () => new Views.ContractsView(), W = 1380 },
            new() { Id = "finance", Title = "Finance", Glyph = "💰", Accent = "#57C97E", Create = () => new Views.FinanceView() },
            new() { Id = "media", Title = "Media", Glyph = "📡", Accent = "#B06BD8", Create = () => new Views.MediaView(), W = 1240, H = 760 },
            new() { Id = "org", Title = "HQ", Glyph = "🏛", Accent = "#9C93C9", Create = () => new Views.OrgView(), W = 1400 },
            new() { Id = "inbox", Title = "Inbox", Glyph = "✉", Accent = "#E0A23C", Create = () => new Views.InboxView(), W = 980, H = 640 },
            new() { Id = "notes", Title = "Notes", Glyph = "📝", Accent = "#E2D276", Create = () => new Views.NotesView(), W = 1020, H = 660 },
            new() { Id = "fighters", Title = "Fighters", Glyph = "📇", Accent = "#7FA6C9", IsWorld = true, Create = () => new Views.FightersView() },
            new() { Id = "fighter", Title = "Fighter Profile", Glyph = "👤", Accent = "#82AEE8", IsWorld = true, Create = () => new Views.FighterProfileView(), W = 1380 },
            new() { Id = "orgs", Title = "Organizations", Glyph = "🏢", Accent = "#A98FD9", IsWorld = true, Create = () => new Views.OrgsView(), W = 1420 },
            new() { Id = "rankings", Title = "Rankings", Glyph = "🏆", Accent = "#C9606B", IsWorld = true, Create = () => new Views.RankingsView() },
            new() { Id = "worldrankings", Title = "World Rankings", Glyph = "🌐", Accent = "#4C8DE0", IsWorld = true, Create = () => new Views.WorldRankingsView(), W = 1040 },
            new() { Id = "events", Title = "Events", Glyph = "🎫", Accent = "#6FC4B4", IsWorld = true, Create = () => new Views.EventsView(), W = 1420 },
            new() { Id = "news", Title = "News", Glyph = "📰", Accent = "#4C8DE0", IsWorld = true, Create = () => new Views.NewsView() },
            new() { Id = "map", Title = "World Map", Glyph = "🗺", Accent = "#6FC4B4", IsWorld = true, Create = () => new Views.WorldMapView() },
            new() { Id = "camps", Title = "Camps", Glyph = "🏋", Accent = "#8FBF6E", IsWorld = true, Create = () => new Views.CampsView() },
        };
        foreach (var app in apps)
            (app.IsWorld ? WorldApps : PromotionApps).Add(app);

        OpenCommand = new RelayCommand<AppSpec>(a => { if (a is not null) Open(a.Id); });
        TaskbarClickCommand = new RelayCommand<OpenWindow>(w =>
        {
            if (w is null) return;
            if (w.IsActive && !w.IsMinimized) w.IsMinimized = true;
            else Focus(w);
        });
    }

    /// <summary>The DataContext stamped on every launched screen (the shell's
    /// MainViewModel) — inside an app window the inherited context is the
    /// window state, not the game, so screens must be pointed home explicitly.</summary>
    public object? ScreenContext { get; set; }

    /// <summary>Launcher sections: your promotion's apps, then the world-research ones.</summary>
    public ObservableCollection<AppSpec> PromotionApps { get; } = new();
    public ObservableCollection<AppSpec> WorldApps { get; } = new();

    /// <summary>Running windows, in the order they were opened (the taskbar order).</summary>
    public ObservableCollection<OpenWindow> Windows { get; } = new();

    public ICommand OpenCommand { get; }
    public ICommand TaskbarClickCommand { get; }

    /// <summary>Launch (or restore) an app by id.</summary>
    public void Open(string id)
    {
        if (Windows.FirstOrDefault(w => w.Spec.Id == id) is { } existing)
        {
            Focus(existing);
            return;
        }

        var spec = PromotionApps.Concat(WorldApps).FirstOrDefault(a => a.Id == id);
        if (spec is null) return;

        var content = spec.Create();
        if (ScreenContext is not null) content.DataContext = ScreenContext;
        var win = new OpenWindow(spec, content)
        {
            X = 46 + Windows.Count % 8 * 26,
            Y = 26 + Windows.Count % 8 * 24,
            // Harness renders keep apps full-bleed so screenshots review cleanly.
            IsMaximized = MainViewModel.HeadlessAutoBoot,
        };
        // Reopen where it lived last time (never in harness renders — the
        // machine's remembered layout must not leak into screenshots).
        if (!MainViewModel.HeadlessAutoBoot && _layout.TryGetValue(id, out var last))
        {
            win.X = Math.Max(0, last.X);
            win.Y = Math.Max(0, last.Y);
            win.W = last.W;
            win.H = last.H;
            win.IsMaximized = last.Max;
        }
        FitToArea(win);
        win.CloseCommand = new RelayCommand(() => Close(win));
        win.MinimizeCommand = new RelayCommand(() => win.IsMinimized = true);
        win.MaximizeCommand = new RelayCommand(() => { win.IsMaximized = !win.IsMaximized; Focus(win); });
        win.OnActivate = Focus;
        Windows.Add(win);
        Focus(win);
    }

    /// <summary>The desk's current size (set by the main window); empty until it has laid out.</summary>
    public System.Windows.Size Area { get; set; } = System.Windows.Size.Empty;

    /// <summary>
    /// Keep a floating window on the desk: a 1420-wide app opened on a 1366
    /// screen (or a layout remembered from a bigger monitor) shrinks to fit
    /// instead of running off the edge.
    /// </summary>
    public void FitToArea(OpenWindow win)
    {
        if (Area.IsEmpty || Area.Width <= 0 || Area.Height <= 0) return;
        const double edge = 8;
        win.W = Math.Min(win.W, Area.Width - win.X - edge);
        if (win.W < 640) { win.X = Math.Max(0, Area.Width - 640 - edge); win.W = Math.Min(640, Area.Width - edge); }
        win.H = Math.Min(win.H, Area.Height - win.Y - edge);
        if (win.H < 420) { win.Y = Math.Max(0, Area.Height - 420 - edge); win.H = Math.Min(420, Area.Height - edge); }
    }

    /// <summary>The sidebar shell's tab indexes, spoken as desktop apps — so every
    /// internal navigation works identically under either shell.</summary>
    public void OpenForTab(int tab)
    {
        string? id = tab switch
        {
            Tab.Home => "home",
            Tab.MyRoster => "roster",
            Tab.BookEvent => "book",
            Tab.Contracts => "contracts",
            Tab.Finance => "finance",
            Tab.Media => "media",
            Tab.Org => "org",
            Tab.Inbox => "inbox",
            Tab.Notes => "notes",
            Tab.Orgs => "orgs",
            Tab.Fighters => "fighters",
            Tab.Fighter => "fighter",
            Tab.Rankings => "rankings",
            Tab.WorldRankings => "worldrankings",
            Tab.Events => "events",
            Tab.News => "news",
            Tab.WorldMap => "map",
            Tab.Camps => "camps",
            _ => null,
        };
        if (id is not null) Open(id);
    }

    public void Close(OpenWindow w)
    {
        _layout[w.Spec.Id] = new WindowRect(w.X, w.Y, w.W, w.H, w.IsMaximized);
        Windows.Remove(w);
        if (w.IsActive && Windows.Count > 0)
            Focus(Windows.OrderByDescending(x => x.Z).First());
    }

    // --- Window-layout memory (per app, persisted across sessions) -----------
    private sealed record WindowRect(double X, double Y, double W, double H, bool Max);

    private static readonly string LayoutPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MmaManager", "desktop-layout.json");

    private readonly Dictionary<string, WindowRect> _layout = LoadLayout();

    private static Dictionary<string, WindowRect> LoadLayout()
    {
        try
        {
            if (File.Exists(LayoutPath))
                return JsonSerializer.Deserialize<Dictionary<string, WindowRect>>(File.ReadAllText(LayoutPath)) ?? new();
        }
        catch { /* a broken layout file must never block the shell */ }
        return new();
    }

    /// <summary>Write the remembered layouts (open windows included) to disk.</summary>
    public void SaveLayout()
    {
        try
        {
            foreach (var w in Windows)
                _layout[w.Spec.Id] = new WindowRect(w.X, w.Y, w.W, w.H, w.IsMaximized);
            Directory.CreateDirectory(Path.GetDirectoryName(LayoutPath)!);
            File.WriteAllText(LayoutPath, JsonSerializer.Serialize(_layout));
        }
        catch { }
    }

    // --- Toasts (the notification corner) ------------------------------------
    public ObservableCollection<Toast> Toasts { get; } = new();

    /// <summary>Pop a notification; it expires on its own, or opens something on click.</summary>
    public void Notify(string glyph, string title, string body = "", Action? onClick = null)
    {
        var toast = new Toast { Glyph = glyph, Title = title, Body = body };
        toast.DismissCommand = new RelayCommand(() => Toasts.Remove(toast));
        toast.ClickCommand = new RelayCommand(() => { onClick?.Invoke(); Toasts.Remove(toast); });
        Toasts.Add(toast);
        while (Toasts.Count > 3) Toasts.RemoveAt(0);   // never a wall of toast
        if (!MainViewModel.HeadlessAutoBoot)
            try { System.Media.SystemSounds.Asterisk.Play(); } catch { }

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
        timer.Tick += (_, _) => { timer.Stop(); Toasts.Remove(toast); };
        timer.Start();
    }

    /// <summary>Bring to front, un-minimize, and mark active.</summary>
    public void Focus(OpenWindow w)
    {
        w.IsMinimized = false;
        w.Z = ++_zCounter;
        foreach (var other in Windows) other.IsActive = other == w;
    }
}
