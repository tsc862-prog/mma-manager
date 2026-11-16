using System.Windows.Input;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Generation.Origins;
using MmaManager.Core.Sim.Living;
using MmaManager.Wpf.Mvvm;

namespace MmaManager.Wpf.ViewModels;

/// <summary>
/// Watching from ringside (Origins, no house of the player's: <see cref="World.HasPlayer"/> false) and the
/// step from the seats to running a house: the spectator's clock (week, month, three months, to the next chapter,
/// each stopping early on the sport's big moments and saying why), founding a promotion through
/// <see cref="PlayerFounding"/>, and the walk to its one free open tryout and its first night. The tick itself is
/// the sim's, unchanged (<see cref="WorldTick.AdvanceWeeksStoppingForPlayer"/>, a week at a time).
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>The longest "to the next chapter" skip runs before it stops anyway (five years).</summary>
    public const int ChapterSkipWeeks = 52 * 5;

    /// <summary>The player has no promotion: the world is watched from ringside.</summary>
    public bool IsObserving => _world is { HasPlayer: false };

    /// <summary>Whose read the scout number on a profile is: his scouts', or (watching) the public one.</summary>
    public string ScoutTip => IsObserving
        ? "The public read: what the sport can see of him from his record and his fights. Nobody scouts for you yet."
        : "Your scouts' read — the truth may differ until they're proven";

    /// <summary>What the player is told when something needs a house he doesn't have.</summary>
    internal const string NoHouseLine = "You don't run a promotion yet: found one from Ringside to sign or book fighters.";

    /// <summary>Ringside: the observer's home.</summary>
    public RingsideViewModel Ringside { get; } = new();

    /// <summary>The founded house's one free open tryout.</summary>
    public TryoutViewModel Tryout { get; } = new();

    public ICommand AdvanceQuarterCommand { get; private set; } = null!;
    public ICommand AdvanceChapterCommand { get; private set; } = null!;
    public ICommand FoundPromotionCommand { get; private set; } = null!;

    /// <summary>
    /// Is there a free open tryout to offer: a house the player founded mid-game (not one of the founding three he
    /// may have started with) that has not held it yet. The sim's own refusal decides the rest.
    /// </summary>
    private bool TryoutApplies =>
        _world is { Mode: WorldMode.Origins, HasPlayer: true } w && w.Get(w.PlayerPromotion) is { Defunct: false } p
        && OriginsPromotions.All.All(s => s.Abbreviation != p.Abbreviation)
        && PlayerFounding.TryoutOptions(w, w.CurrentDate).Available;

    private void WireRingside()
    {
        AdvanceQuarterCommand = new RelayCommand(() => AdvanceWatching(13, toChapter: false), () => IsObserving);
        AdvanceChapterCommand = new RelayCommand(() => AdvanceWatching(ChapterSkipWeeks, toChapter: true), () => IsObserving);
        FoundPromotionCommand = new RelayCommand(FoundPromotion, () => IsObserving);
        Ringside.FoundCommand = FoundPromotionCommand;
        Ringside.OpenStoryCommand = new RelayCommand(() => Desktop.Open("story"));
        Tryout.OnChanged = () =>
        {
            RefreshAll();
            Autosave();
            StatusText = Tryout.Signed.Count == 0
                ? "The open tryout is done, and nobody signed. The free-agent market is where the fighters are."
                : $"The open tryout signed {Tryout.Signed.Count} local men. Book your first night when you're ready.";
        };
        Tryout.BookCommand = new RelayCommand(() => { CloseTryout(); NavigateTo(Tab.BookEvent); });
        Tryout.LaterCommand = new RelayCommand(CloseTryout);
        var before = Desktop.BeforeOpen;
        Desktop.BeforeOpen = id =>
        {
            before?.Invoke(id);
            if (_world is null) return;
            if (id == "ringside" && IsObserving) Ringside.Attach(_world);
            if (id == "tryout") Tryout.Attach(_world);
        };
    }

    private void CloseTryout()
    {
        if (Desktop.Windows.FirstOrDefault(w => w.Spec.Id == "tryout") is { } win) Desktop.Close(win);
        Desktop.ShowTryoutApp(TryoutApplies);
    }

    // ------------------------------------------------------------------ the spectator's clock

    /// <summary>
    /// Run time from the seats, a week at a time, up to <paramref name="weeks"/>: stop at the end of any week in
    /// which a chapter fired or a house opened or folded, so he never skips past the moment he might want to step
    /// in. "To the next chapter" stops on those too, or after <see cref="ChapterSkipWeeks"/> with nothing.
    /// Ringside then says why the clock stopped.
    /// </summary>
    private void AdvanceWatching(int weeks, bool toChapter)
    {
        if (_world is not { } w || !IsObserving) return;
        Moments.Reset();
        StatusText = "";
        int chaptersBefore = w.Era.Chapters.Count;
        var liveBefore = w.Promotions.Values.Where(p => !p.Defunct).Select(p => p.Id).ToHashSet();
        int events = 0, fights = 0, ran = 0;
        List<string> moments = new();
        string? headline = null, detail = null;
        while (ran < weeks)
        {
            var r = _tick.AdvanceWeeksStoppingForPlayer(w, 1);
            events += r.EventsRun; fights += r.Fights; ran++;
            if (BigMoments(w, chaptersBefore, liveBefore, moments, out headline, out detail)) break;
        }

        LastAdvance = $"Advanced to {w.CurrentDate:d MMM yyyy}: {events} night{(events == 1 ? "" : "s")}, {fights} fight{(fights == 1 ? "" : "s")}.";
        RefreshAll();
        Autosave();

        bool early = moments.Count > 0 && ran < weeks;
        string span = toChapter ? Span(ran) : weeks switch { 1 => "The week", 4 => "The month", 13 => "Three months", _ => Span(ran) };
        RingsideStop stop;
        if (moments.Count > 0)
        {
            bool chapter = moments.Any(m => !m.StartsWith("open:", StringComparison.Ordinal) && !m.StartsWith("fold:", StringComparison.Ordinal));
            string kicker = toChapter
                ? chapter ? $"THE NEXT CHAPTER  ·  {w.CurrentDate:d MMMM yyyy}  ·  {Later(ran)} LATER"
                          : $"TIME STOPPED BEFORE THE NEXT CHAPTER  ·  {w.CurrentDate:d MMMM yyyy}  ·  AFTER {Later(ran)}"
                : early ? $"TIME STOPPED EARLY  ·  {w.CurrentDate:d MMMM yyyy}  ·  AFTER {Later(ran)}"
                : $"{span} IS UP  ·  {w.CurrentDate:d MMMM yyyy}";
            stop = new RingsideStop(kicker.ToUpperInvariant(), headline!, detail!, Big: true);
            StatusText = $"▶ Time stopped: {headline}";
        }
        else if (toChapter)
        {
            stop = new RingsideStop($"FIVE YEARS ON  ·  {w.CurrentDate:d MMMM yyyy}".ToUpperInvariant(),
                "Five years, and no new chapter.",
                $"{events} nights and {fights} fights went by without a turn in the story. The clock stops here so you can look around.", Big: false);
            StatusText = "Five years went by without a new chapter. The clock stops here so you can look around.";
        }
        else
        {
            stop = new RingsideStop($"{span.ToUpperInvariant()} IS UP  ·  {w.CurrentDate:d MMMM yyyy}".ToUpperInvariant(),
                events == 0 ? "A quiet stretch: no nights." : $"{events} night{(events == 1 ? "" : "s")}, {fights} fight{(fights == 1 ? "" : "s")}.",
                "Nothing that changes the shape of the sport: no new chapter, no house opened or closed.", Big: false);
            StatusText = $"{span} is up: nothing that changes the shape of the sport.";
        }
        Ringside.Stop = stop;
        Desktop.Open("ringside");
    }

    /// <summary>The week's big moments since the clock started (chapters told, houses opened or folded); true when any.</summary>
    private static bool BigMoments(World w, int chaptersBefore, HashSet<PromotionId> liveBefore, List<string> moments,
        out string? headline, out string? detail)
    {
        moments.Clear();
        headline = detail = null;
        var lines = new List<string>();
        // The first moment is the headline (a chapter is told in full on Ringside below it, so the banner only names
        // it); the rest go in the detail.
        foreach (var rec in w.Era.Chapters.Skip(chaptersBefore))
            if (OriginsStory.View(w, rec) is { } ch)
            {
                moments.Add(ch.Key);
                lines.Add($"A new chapter: {ch.Title}.");
            }
        foreach (var p in w.Promotions.Values.Where(p => !p.Defunct && !liveBefore.Contains(p.Id)).OrderBy(p => p.Id.Value))
        {
            moments.Add($"open:{p.Id.Value}");
            string where = p.HomeCity is { } hc && w.Get(hc) is { } c ? c.Name : "a new city";
            lines.Add($"{p.Name} opens in {where}" + (string.IsNullOrWhiteSpace(p.OwnerName) ? "." : $": {p.OwnerName}'s house."));
        }
        foreach (var id in liveBefore.OrderBy(i => i.Value))
            if (w.Get(id) is { Defunct: true } gone)
            {
                moments.Add($"fold:{id.Value}");
                lines.Add($"{gone.Name} has closed its doors: one house fewer in the sport.");
            }
        if (moments.Count == 0) return false;
        headline = lines[0];
        detail = string.Join(" ", lines.Skip(1).Append("If you mean to be part of what comes next, Found your promotion is on the right."));
        return true;
    }

    /// <summary>"one week", "3 weeks", "5 months", "2 years".</summary>
    private static string Later(int weeks) => weeks switch
    {
        <= 1 => "one week",
        < 8 => $"{weeks} weeks",
        < 52 => $"{Math.Max(2, (int)Math.Round(weeks / 4.33))} months",
        < 104 => "a year",
        _ => $"{weeks / 52} years",
    };

    private static string Span(int weeks) => weeks switch
    {
        <= 1 => "The week",
        < 8 => $"{weeks} weeks",
        < 52 => $"{Math.Max(2, (int)Math.Round(weeks / 4.33))} months",
        _ => weeks / 52 == 1 ? "A year" : $"{weeks / 52} years",
    };

    /// <summary>The sticky note while watching: where you are, the next night, the one thing to do.</summary>
    private void RingsideNotes()
    {
        DeskNotes.Add("watching from ringside");
        var next = _world.Events.Values.Where(e => e.Status == EventStatus.Scheduled && e.Date >= _world.CurrentDate)
            .OrderBy(e => e.Date).ThenBy(e => e.Id.Value).FirstOrDefault();
        if (next is not null)
            DeskNotes.Add($"{next.Name}: {Math.Max(0, next.Date.DayNumber - _world.CurrentDate.DayNumber)} days out");
        DeskNotes.Add("found your promotion when you're ready");
    }

    // ------------------------------------------------------------------ founding

    private void FoundPromotion()
    {
        if (_world is null || !IsObserving || HeadlessAutoBoot) return;
        var vm = new FoundingViewModel(_world);
        var win = new FoundPromotionWindow(vm);
        if (System.Windows.Application.Current?.MainWindow is { IsLoaded: true } owner) win.Owner = owner;
        if (win.ShowDialog() != true || vm.Founded is null) return;
        AfterFounding(vm.Founded);
    }

    /// <summary>
    /// A house now exists: the full desktop comes back (RefreshAll), the briefing starts fresh, and the player is
    /// walked straight to his one free open tryout, which points on to booking his first night.
    /// </summary>
    internal void AfterFounding(Promotion promo)
    {
        Ringside.Stop = null;
        Moments.Reset();
        Briefing.Reset();
        RefreshAll();
        Autosave();
        var w = _world;
        bool late = PlayerFounding.LateInYear(w.CurrentDate);
        string where = promo.HomeCity is { } hc && w.Get(hc) is { } c ? c.Name : "its new home";
        Tryout.Welcome = $"{promo.Name} is open for business in {where}, with {promo.Balance} in the bank and nobody signed. "
                       + (late ? $"There's too little of {w.CurrentDate.Year} left to judge you on it: the owner's targets start on 1 January. " : "")
                       + "First, fighters.";
        Desktop.Open("home");
        Desktop.Open("tryout");
        StatusText = $"{promo.Name} is open. Call your free open tryout, then book your first night."
                   + (late ? " The owner's targets start on 1 January." : "");
    }
}
