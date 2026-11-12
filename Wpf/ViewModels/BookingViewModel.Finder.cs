using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Wpf.Art;
using MmaManager.Wpf.Mvvm;

namespace MmaManager.Wpf.ViewModels;

// The Card Desk's fighter finder: the picker's "Find a fighter" list and the
// Table read the same facts, the same search, the same filter chips. Every fact
// is public (record, rankings, age, cards, home city) or your own books (purse,
// deal); ability is the scouts' read. One pass builds the facts for the roster
// (per refresh); a keystroke or a chip only filters and sorts that list.

/// <summary>A filter chip on the finder (combinable).</summary>
public enum FinderFilter { Available, NeedsFight, LastOfDeal, Ranked, Prospects, Home, WinStreak, OffLoss, UnhappyLifer, NotOnCard }

/// <summary>How the finder orders its rows.</summary>
public enum FinderSort { BestFight, Ranking, Read, Draw, Idle, PurseLow, PurseHigh, FightsLeft, Age, Record }

/// <summary>One sort choice ("Days idle").</summary>
public sealed record SortOption(FinderSort Sort, string Label)
{
    public override string ToString() => Label;
}

/// <summary>The finder's division scope: one division, or all of them (null).</summary>
public sealed record ScopeOption(DivisionPick? Division, string Label)
{
    public override string ToString() => Label;
}

/// <summary>A filter chip with its count ("Needs a fight · 12"); toggling it refilters the list.</summary>
public sealed class FinderChip : ObservableObject
{
    public FinderChip(BookingViewModel desk, FinderFilter filter, string label, string tip)
    {
        _desk = desk; Filter = filter; Label = label; Tip = tip;
    }
    private readonly BookingViewModel _desk;
    public FinderFilter Filter { get; }
    public string Label { get; }
    public string Tip { get; }
    private int _count;
    public int Count { get => _count; set { if (Set(ref _count, value)) OnPropertyChanged(nameof(Text)); } }
    public string Text => $"{Label} · {Count}";
    private bool _isOn;
    public bool IsOn { get => _isOn; set { if (Set(ref _isOn, value)) _desk.OnFinderChanged(); } }
    internal void SetQuietly(bool on) { _isOn = on; OnPropertyChanged(nameof(IsOn)); }
}

/// <summary>
/// What the desk knows about one of your fighters for this card, read once per
/// refresh: public facts, your own books, and the scouts' read (never the truth).
/// </summary>
internal sealed class FighterFacts
{
    public required Fighter F { get; init; }
    public required FighterStanding Standing { get; init; }
    public required int RankSort { get; init; }
    public required int Read { get; init; }
    public required int Popularity { get; init; }
    public required DateOnly? LastFought { get; init; }
    /// <summary>Days since his last fight (null: never fought).</summary>
    public required int? IdleDays { get; init; }
    /// <summary>Days unused: since his last fight or his signing, whichever is later (the shelf the lifer rules read).</summary>
    public required int ShelfDays { get; init; }
    public required int Age { get; init; }
    public required bool Ready { get; init; }
    public required string Why { get; init; }
    public required bool OnCard { get; init; }
    /// <summary>From the card's region (how the partners' local-fighter terms count).</summary>
    public required bool Home { get; init; }
    /// <summary>From the host city itself: a hometown night.</summary>
    public required bool HomeCity { get; init; }
    public required bool Prospect { get; init; }
    public required Lifers.Grievance? Grievance { get; init; }
    public required bool IsLifer { get; init; }
    public Money Purse => F.Contract?.PursePerFight ?? Money.Zero;
    public Money WinBonus => F.Contract?.WinBonus ?? Money.Zero;
    public int FightsLeft => F.Contract?.FightsRemaining ?? 0;
    public bool LastOfDeal => F.Contract is { FightsRemaining: <= 1 };
    public int Streak => F.Record.CurrentStreak;
    /// <summary>Ready, unbooked and unused for four months: the neglect starts to bite (Lifers reads 120 days).</summary>
    public bool NeedsFight => Ready && ShelfDays >= BookingViewModel.NeedsFightDays;
    /// <summary>A grievance a booking on this card would repair (on the shelf, or owed the title shot).</summary>
    public bool BookingHelps => Grievance is { Kind: Lifers.GrievanceKind.Benched or Lifers.GrievanceKind.PassedOver };
    /// <summary>The #1 contender in a division with a reigning champion.</summary>
    public required bool TitleDue { get; init; }
}

public sealed partial class BookingViewModel
{
    /// <summary>Unused this long and ready: "needs a fight" (the neglect the lifer rules start counting at 120 days).</summary>
    internal const int NeedsFightDays = 120;

    private void InitFinder()
    {
        FinderChips = new ObservableCollection<FinderChip>
        {
            new(this, FinderFilter.Available, "Available", "Ready, unbooked and free on this date"),
            new(this, FinderFilter.NeedsFight, "Needs a fight", $"Ready and unused for {NeedsFightDays / 30}+ months: longest idle first"),
            new(this, FinderFilter.LastOfDeal, "Deal ending", "This bout would be the last on his contract"),
            new(this, FinderFilter.Ranked, "Ranked", "Champions and ranked contenders"),
            new(this, FinderFilter.Prospects, "Prospects", "25 or younger with fewer than 10 pro fights (public record)"),
            new(this, FinderFilter.Home, "Home", "From this card's region, the way partners count local fighters (hometown fighters flagged on the row)"),
            new(this, FinderFilter.WinStreak, "Win streak", "Two or more wins in a row"),
            new(this, FinderFilter.OffLoss, "Off a loss", "Lost his last fight"),
            new(this, FinderFilter.UnhappyLifer, "Unhappy lifer", "A lifer whose grievance a booking would fix: on the shelf, or owed the title shot"),
            new(this, FinderFilter.NotOnCard, "Not on card", "Leaves out everyone already booked on this card"),
        };
        InitFinderCommands();
        FindOpponentCommand = new RelayCommand<LadderEntry>(FindOpponentFor);
        FinderPickCommand = new RelayCommand<LadderEntry>(e => e?.Command?.Execute(e));
        ClearFinderCommand = new RelayCommand(() =>
        {
            _search = ""; OnPropertyChanged(nameof(FinderSearch));
            foreach (var c in FinderChips) c.SetQuietly(false);
            OnFinderChanged();
        });
    }

    public ObservableCollection<FinderChip> FinderChips { get; private set; } = new();
    public ICommand FindOpponentCommand { get; private set; } = null!;
    public ICommand FinderPickCommand { get; private set; } = null!;
    public ICommand ClearFinderCommand { get; private set; } = null!;

    private string _search = "";
    /// <summary>Type-to-search by name or nickname, across the whole roster.</summary>
    public string FinderSearch
    {
        get => _search;
        set { if (Set(ref _search, value ?? "")) OnFinderChanged(); }
    }

    public IReadOnlyList<SortOption> SortOptions { get; } = new[]
    {
        new SortOption(FinderSort.BestFight, "Best fight"),
        new SortOption(FinderSort.Ranking, "Ranking"),
        new SortOption(FinderSort.Read, "Our scouts' read"),
        new SortOption(FinderSort.Draw, "Draw"),
        new SortOption(FinderSort.Idle, "Days idle"),
        new SortOption(FinderSort.PurseLow, "Purse: cheapest"),
        new SortOption(FinderSort.PurseHigh, "Purse: dearest"),
        new SortOption(FinderSort.FightsLeft, "Fights left on deal"),
        new SortOption(FinderSort.Age, "Age: youngest"),
        new SortOption(FinderSort.Record, "Record"),
    };

    // Remembered for the session: one sort while browsing, one while finding an opponent.
    private SortOption? _browseSort, _opponentSort;
    private bool OpponentMode => _rail == DeskRail.Picker && (_replaceFight is not null || SelectedRed is not null);

    public SortOption SelectedSort
    {
        get => OpponentMode
            ? _opponentSort ??= SortOptions[0]
            : _browseSort ??= SortOptions[1];
        set
        {
            if (value is null) return;
            if (OpponentMode) _opponentSort = value; else _browseSort = value;
            OnPropertyChanged();
            OnFinderChanged();
        }
    }

    public ObservableCollection<ScopeOption> Scopes { get; } = new();
    private ScopeOption? _scope;
    /// <summary>The division filter (all divisions by default): a filter, not a prerequisite.</summary>
    public ScopeOption? FinderScope
    {
        get => _scope;
        set { if (Set(ref _scope, value) && !_quiet) OnFinderChanged(); }
    }

    private void RefreshScopes()
    {
        var keep = _scope?.Division;
        var list = new List<ScopeOption> { new(null, "All divisions") };
        list.AddRange(Divisions.Select(d => new ScopeOption(d, d.Label)));
        if (!list.Select(s => s.Label).SequenceEqual(Scopes.Select(s => s.Label)))
        {
            Scopes.Clear();
            foreach (var s in list) Scopes.Add(s);
        }
        _scope = Scopes.FirstOrDefault(s => s.Division == keep) ?? Scopes.FirstOrDefault();
        OnPropertyChanged(nameof(FinderScope));
    }

    private string _finderCount = "";
    /// <summary>"38 of 298 shown" (and how long the filter took, for the harness).</summary>
    public string FinderCount { get => _finderCount; private set => Set(ref _finderCount, value); }
    /// <summary>Milliseconds the last filter-and-sort took (measured; the harness prints it).</summary>
    public double LastFinderMs { get; private set; }

    /// <summary>The finder's search and chips are showing (browsing the roster, or finding an opponent).</summary>
    public bool ShowFinderTools => _rail == DeskRail.Picker && (IsReplacing || (IsPickingSlot && !PickSuggested && !(PickLocal && SelectedRed is not null)));
    public bool ShowScope => ShowFinderTools && !OpponentMode;
    /// <summary>The picker's hint line: for the suggestions only (the finder and the local box explain themselves).</summary>
    public bool ShowPickerHint => !ShowFinderTools && !(IsPickingSlot && PickLocal);

    // === The facts, once per refresh ==============================================

    private Dictionary<FighterId, FighterFacts>? _facts;
    private (World?, DateOnly, int, EventId?, int) _factsKey;

    /// <summary>Everyone on your roster for this card, read once until the card or the world moves.</summary>
    private Dictionary<FighterId, FighterFacts> Facts()
    {
        var w = _world!;
        var ev = SelectedEvent?.Model;
        var key = (w, w.CurrentDate, w.Events.Count, ev?.Id, ev is null ? 0 : ev.Card.Sum(f => f.RedCorner.Value * 31 + f.BlueCorner.Value));
        if (_facts is not null && _factsKey == key) return _facts;
        var map = new Dictionary<FighterId, FighterFacts>();
        foreach (var f in w.RosterOf(w.PlayerPromotion).Where(f => f.Status != FighterStatus.Retired && !f.LocalTalent))
            map[f.Id] = FactsOf(f, ev);
        _factsKey = key;
        return _facts = map;
    }

    private FighterFacts? FactsFor(FighterId id) =>
        Facts().TryGetValue(id, out var x) ? x : _world?.Get(id) is { } f ? FactsOf(f, SelectedEvent?.Model) : null;

    private FighterFacts FactsOf(Fighter f, Event? ev)
    {
        var w = _world!;
        var lastFought = LastFought();
        var st = _advisor.StandingOf(w, f.Id);
        bool known = lastFought.TryGetValue(f.Id, out var lf);
        var date = ev?.Date ?? w.CurrentDate;
        bool onCard = ev is not null && ev.Card.Any(x => x.RedCorner == f.Id || x.BlueCorner == f.Id);
        string? elsewhere = ev is null ? null : BookedElsewhereName(f, ev);
        bool ready = !onCard && elsewhere is null && f.IsMatchReady(date);
        string why = ready ? "" : ev is null ? "Not ready" : Unavailable(f, ev);
        int since = Math.Max(known ? lf.DayNumber : 0, f.Contract?.SignedOn.DayNumber ?? 0);
        var host = ev is null ? null : Core.Sim.Geography.WorldMap.CityOf(w, ev);
        bool homeCity = host is { } h && f.HomeCity == h;
        // "Local" the way the partners' local-fighter terms count it: from the card's region.
        bool home = homeCity || (ev is not null && (f.Nationality == ev.Region || (f.HomeCity is { } hc && w.Get(hc)?.Region == ev.Region)));
        bool lifer = Lifers.IsLifer(w, f);
        return new FighterFacts
        {
            F = f,
            Standing = st,
            RankSort = st.Kind switch { Standing.Champion => 0, Standing.Ranked => st.Rank ?? 999, _ => 1000 },
            Read = FighterRow.Scouted(w, f),
            Popularity = f.Intangibles.Popularity.Value,
            LastFought = known ? lf : null,
            IdleDays = known ? w.CurrentDate.DayNumber - lf.DayNumber : null,
            ShelfDays = since == 0 ? 0 : w.CurrentDate.DayNumber - since,
            Age = f.AgeOn(w.CurrentDate),
            Ready = ready,
            Why = why,
            OnCard = onCard,
            Home = home,
            HomeCity = homeCity,
            TitleDue = st is { Kind: Standing.Ranked, Rank: 1 }
                       && w.Get(w.PlayerPromotion)?.Divisions.FirstOrDefault(d => d.Matches(f)) is { Title: { } tid } && w.Get(tid)?.CurrentChampion is not null,
            Prospect = f.AgeOn(w.CurrentDate) <= 25 && f.Record.TotalBouts < 10,
            // Only a lifer has a grievance worth reading here (and it is the one costly read).
            Grievance = lifer ? Lifers.GrievanceOf(w, f, w.CurrentDate) : null,
            IsLifer = lifer,
        };
    }

    private static bool Passes(FighterFacts x, FinderFilter filter) => filter switch
    {
        FinderFilter.Available => x.Ready,
        FinderFilter.NeedsFight => x.NeedsFight,
        FinderFilter.LastOfDeal => x.LastOfDeal,
        FinderFilter.Ranked => x.Standing.Kind != Standing.Unranked,
        FinderFilter.Prospects => x.Prospect,
        FinderFilter.Home => x.Home,
        FinderFilter.WinStreak => x.Streak >= 2,
        FinderFilter.OffLoss => x.Streak < 0,
        FinderFilter.UnhappyLifer => x.IsLifer && x.BookingHelps,
        FinderFilter.NotOnCard => !x.OnCard,
        _ => true,
    };

    private bool MatchesSearch(FighterFacts x)
    {
        if (_search.Length == 0) return true;
        var f = x.F;
        return Contains(f.FirstName) || Contains(f.LastName) || Contains(f.Nickname)
               || Contains($"{f.FirstName} {f.LastName}");
        bool Contains(string? s) => s is not null && s.Contains(_search.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A chip or the search moved: refilter (no fact is re-read).</summary>
    internal void OnFinderChanged()
    {
        if (_quiet) return;
        if (_view == DeskView.Table) RefreshTable();
        if (_rail == DeskRail.Picker) RebuildLadder();
    }

    // The finder's filtered, sorted list over a pool of facts. Counts on the chips
    // are what each chip alone would leave of the searched, scoped pool.
    private List<FighterFacts> Filter(IEnumerable<FighterFacts> pool, Func<FighterFacts, double>? bestFight = null)
    {
        var sw = Stopwatch.StartNew();
        var scoped = pool.Where(MatchesSearch).ToList();
        foreach (var chip in FinderChips) chip.Count = scoped.Count(x => Passes(x, chip.Filter));
        var on = FinderChips.Where(c => c.IsOn).Select(c => c.Filter).ToList();
        var shown = scoped.Where(x => on.All(f => Passes(x, f)));
        var sort = SelectedSort.Sort;
        if (sort == FinderSort.BestFight && bestFight is null) sort = FinderSort.Ranking;
        // Ready first, then the sort; the id is always the last word.
        var ordered = shown.OrderByDescending(x => x.Ready);
        ordered = on.Contains(FinderFilter.NeedsFight) && sort == FinderSort.Ranking
            ? ordered.ThenByDescending(x => x.ShelfDays)
            : sort switch
            {
                FinderSort.BestFight => ordered.ThenByDescending(x => bestFight!(x)),
                FinderSort.Read => ordered.ThenByDescending(x => x.Read),
                FinderSort.Draw => ordered.ThenByDescending(x => x.Popularity),
                FinderSort.Idle => ordered.ThenByDescending(x => x.ShelfDays),
                FinderSort.PurseLow => ordered.ThenBy(x => x.Purse.Amount),
                FinderSort.PurseHigh => ordered.ThenByDescending(x => x.Purse.Amount),
                FinderSort.FightsLeft => ordered.ThenBy(x => x.FightsLeft),
                FinderSort.Age => ordered.ThenBy(x => x.Age),
                FinderSort.Record => ordered.ThenByDescending(x => FighterRow.RecordKey(x.F.Record)),
                _ => ordered.ThenBy(x => x.RankSort).ThenByDescending(x => x.Read),
            };
        var list = ordered.ThenBy(x => x.F.Id.Value).ToList();
        LastFinderMs = sw.Elapsed.TotalMilliseconds;
        FinderCount = list.Count == scoped.Count && _search.Length == 0 ? $"{list.Count} fighters"
            : $"{list.Count} of {pool.Count()} shown";
        return list;
    }

    // === Words for a row =============================================================

    /// <summary>"W3", "L2", "" (a single result isn't a streak worth a chip).</summary>
    internal static string StreakText(int streak) => streak >= 2 ? $"W{streak}" : streak <= -2 ? $"L{-streak}" : streak == 1 ? "Won last" : streak == -1 ? "Lost last" : "";

    /// <summary>"12 days ago", "5 months ago", "debut".</summary>
    internal static string AgoText(int? days) => days switch
    {
        null => "no pro fight yet",
        < 14 => $"{days} days ago",
        < 60 => $"{days / 7} weeks ago",
        < 730 => $"{days / 30} months ago",
        _ => $"{days / 365} years ago",
    };

    /// <summary>Short form for a row: "45d", "5 mo".</summary>
    internal static string AgoShort(int? days) => days switch
    {
        null => "debut",
        < 60 => $"{days}d ago",
        < 730 => $"{days / 30} mo ago",
        _ => $"{days / 365} yr ago",
    };

    /// <summary>Popularity in words (a public fact): who sells tickets.</summary>
    internal static string DrawWord(int pop) => pop switch
    {
        >= 75 => "a star",
        >= 60 => "draws a crowd",
        >= 45 => "a known name",
        >= 30 => "a following",
        _ => "little draw yet",
    };

    /// <summary>"$12k" for a row.</summary>
    internal static string Short(Money m) => Compact(m).TrimStart('+');

    /// <summary>The one flag a row shows, most pressing first.</summary>
    private static DeskTag? FlagOf(FighterFacts x) =>
        x.IsLifer && x.BookingHelps ? new DeskTag("Unhappy lifer", CardTone.Bad)
        : x.LastOfDeal ? new DeskTag("Last fight of deal", CardTone.Warn)
        : x.NeedsFight ? new DeskTag($"Idle {x.ShelfDays / 30} mo", CardTone.Warn)
        : x.TitleDue ? new DeskTag("Title due", CardTone.Info)
        : x.HomeCity ? new DeskTag("Hometown", CardTone.Good)
        : null;

    private LadderEntry Row(FighterFacts x, string line, System.Windows.Input.ICommand? command, string cost = "")
    {
        var f = x.F;
        var div = FighterRow.DivisionLabel(f.WeightClass, f.Sex);
        return new LadderEntry(f, _world!, this, x.Standing.Label, line, x.Ready, x.Why)
        {
            Command = command,
            Nickname = f.Nickname is { Length: > 0 } n ? $"“{n}”" : "",
            DisplayName = $"{f.FirstName} {f.LastName}",
            Division = div,
            Streak = StreakText(x.Streak),
            StreakTone = x.Streak >= 2 ? CardTone.Good : x.Streak <= -2 ? CardTone.Bad : CardTone.Neutral,
            LastFoughtText = AgoShort(x.IdleDays),
            Draw = DrawWord(x.Popularity),
            Purse = x.F.Contract is { } c ? $"{Short(c.PursePerFight)} purse" : "",
            Cost = cost,
            Flag = FlagOf(x),
            Tip = $"{f.FirstName} {f.LastName} · {div} · {x.Standing.Label} · {f.Record} · age {x.Age}\n"
                  + $"Last fought: {(x.LastFought is { } d ? $"{d:d MMM yyyy} ({AgoText(x.IdleDays)})" : "no pro fight yet")}\n"
                  + $"Our scouts' read: {ReadWords.Read(x.Read)} · draw: {DrawWord(x.Popularity)} (popularity {x.Popularity})\n"
                  + (f.Contract is { } k ? $"Deal: {k.PursePerFight} to show, {k.WinBonus} to win, {k.FightsRemaining} fight{(k.FightsRemaining == 1 ? "" : "s")} left" : "")
                  + (x.Grievance is { } g ? $"\nUnhappy: {g.Reason}" : ""),
        };
    }

    // === "Who needs a fight": the short list at the top of the picker ===============

    public ObservableCollection<LadderEntry> NeedsAFight { get; } = new();
    public bool HasNeedsAFight => NeedsAFight.Count > 0;
    private string _needsLine = "";
    /// <summary>"4 of yours need a fight" (the Suggested view's way in).</summary>
    public string NeedsLine { get => _needsLine; private set => Set(ref _needsLine, value); }
    public bool HasNeedsLine => _needsLine.Length > 0;

    private void RefreshNeeds()
    {
        NeedsAFight.Clear();
        if (_world is null || SelectedEvent is null || _rail != DeskRail.Picker || _replaceFight is not null || SelectedRed is not null)
        {
            NeedsLine = ""; OnPropertyChanged(nameof(HasNeedsAFight)); OnPropertyChanged(nameof(HasNeedsLine));
            return;
        }
        // Grievances a booking repairs first, then expiring deals, then the longest lay-offs.
        var all = Facts().Values.Where(x => x.Ready && (x.BookingHelps || x.LastOfDeal || x.NeedsFight)).ToList();
        var ranked = all
            .OrderByDescending(x => x.IsLifer && x.BookingHelps)
            .ThenByDescending(x => x.LastOfDeal)
            .ThenByDescending(x => x.ShelfDays)
            .ThenBy(x => x.F.Id.Value)
            .Take(3);
        foreach (var x in ranked)
        {
            string why = x.IsLifer && x.BookingHelps ? Lifers.Gendered(x.F, $"Lifer, unhappy: {x.Grievance!.Reason}")
                : x.LastOfDeal ? (x.NeedsFight ? $"Last fight of his deal, idle {x.ShelfDays / 30} months" : "Last fight of his deal")
                : $"Unused for {x.ShelfDays / 30} months";
            NeedsAFight.Add(Row(x, Lifers.Gendered(x.F, why), FindOpponentCommand));
        }
        int idle = all.Count(x => x.NeedsFight), ending = all.Count(x => x.LastOfDeal), unhappy = all.Count(x => x.IsLifer && x.BookingHelps);
        var parts = new List<string>();
        if (unhappy > 0) parts.Add($"{unhappy} unhappy lifer{(unhappy == 1 ? "" : "s")}");
        if (idle > 0) parts.Add($"{idle} idle {NeedsFightDays / 30}+ months");
        if (ending > 0) parts.Add($"{ending} on the last fight of a deal");
        NeedsLine = all.Count == 0 ? "" : "Who needs a fight: " + string.Join(", ", parts);
        OnPropertyChanged(nameof(HasNeedsAFight)); OnPropertyChanged(nameof(HasNeedsLine)); OnPropertyChanged(nameof(NeedsTitle));
    }

    /// <summary>"Find him an opponent": the finder, with him in the first corner.</summary>
    private void FindOpponentFor(LadderEntry? e)
    {
        if (_world is null || SelectedEvent is null || e is null) return;
        if (_rail != DeskRail.Picker || _replaceFight is not null) OpenPicker(_pickSlot ?? _nextSlot);
        _pickMode = PickMode.Division;
        OnPropertyChanged(nameof(PickSuggested)); OnPropertyChanged(nameof(PickDivision)); OnPropertyChanged(nameof(PickLocal));
        PickFirstCorner(e.FighterId);
    }

    /// <summary>Put a fighter in the first corner, from any division (the finder's scope is not a prerequisite).</summary>
    private void PickFirstCorner(FighterId id)
    {
        var f = _world?.Get(id);
        if (f is null) return;
        var div = Divisions.FirstOrDefault(d => d.WeightClass == f.WeightClass && d.Sex == f.Sex);
        if (div is null) return;
        if (SelectedDivision != div) SelectedDivision = div;
        SelectedRed = RedOptions.FirstOrDefault(o => o.Id == id);
        if (SelectedRed is null && SelectedEvent is not null)
            Err($"{f.FirstName} {f.LastName} isn't available for {SelectedEvent.Model.Name}: {Unavailable(f, SelectedEvent.Model)}.");
        RaiseFinder();
    }

    private void RaiseFinder()
    {
        OnPropertyChanged(nameof(HasRedPick)); OnPropertyChanged(nameof(HasPairing));
        OnPropertyChanged(nameof(SelectedSort)); OnPropertyChanged(nameof(ShowFinderTools)); OnPropertyChanged(nameof(ShowScope)); OnPropertyChanged(nameof(ShowPickerHint));
        OnPropertyChanged(nameof(ShowLocalInstead)); OnPropertyChanged(nameof(ShowNeedsStrip));
        OnPropertyChanged(nameof(ShowSuggestOne)); OnPropertyChanged(nameof(LocalPickLine)); OnPropertyChanged(nameof(FiltersLine));
    }

    /// <summary>With a fighter of yours chosen, the local club scene is one click away (small promotions only).</summary>
    public bool ShowLocalInstead => HasLocalOption && IsPickingSlot && PickDivision && SelectedRed is not null;

    /// <summary>The needs-a-fight strip shows while browsing with nothing typed or filtered.</summary>
    public bool ShowNeedsStrip => HasNeedsAFight && IsPickingSlot && PickDivision && SelectedRed is null
                                  && _search.Length == 0 && !FinderChips.Any(c => c.IsOn);

    /// <summary>"Suggest one" shows while finding an opponent for a chosen fighter.</summary>
    public bool ShowSuggestOne => IsPickingSlot && PickDivision && SelectedRed is not null;

    /// <summary>"Kito Maina meets a local": who the local bout is for.</summary>
    public string LocalPickLine => SelectedRed is { } r && _world?.Get(r.Id) is { } f
        ? Lifers.Gendered(f, $"{f.FirstName} {f.LastName} meets a local: {CornerLine(FactsFor(f.Id), f)}"
          + (f.Contract is { } c ? $" · {c.PursePerFight} to show, {c.WinBonus} to win" + (c.FightsRemaining <= 1 ? " · last fight of his deal" : "") : ""))
        : "Pick your fighter below.";

    /// <summary>Ctrl+F: the picker's finder, opened on the next open slot if it isn't up.</summary>
    public void OpenFinder()
    {
        if (SelectedEvent is null) return;
        if (_rail != DeskRail.Picker || _replaceFight is not null) OpenPicker(_nextSlot);
        if (PickSuggested) PickMode = PickMode.Division;
    }

    public ICommand ShowNeedsCommand { get; private set; } = null!;
    public ICommand ToggleFiltersCommand { get; private set; } = null!;
    public ICommand ToggleNeedsCommand { get; private set; } = null!;

    private bool _needsOpen;
    /// <summary>On a short window the needs-a-fight strip folds to its heading until opened.</summary>
    public bool NeedsOpen => _needsOpen;
    public string NeedsTitle => $"NEEDS A FIGHT · {NeedsAFight.Count}";

    private bool _filtersOpen;
    /// <summary>On a short window the chips fold to one line ("Filters: Ranked, Win streak ▾") until opened.</summary>
    public bool FiltersOpen { get => _filtersOpen; set { if (Set(ref _filtersOpen, value)) OnPropertyChanged(nameof(FiltersLine)); } }
    /// <summary>The folded chips in words.</summary>
    public string FiltersLine => (FinderChips.Where(c => c.IsOn).Select(c => c.Label).ToList() is { Count: > 0 } on
        ? "Filters: " + string.Join(", ", on) : "Filters") + (_filtersOpen ? " ▴" : " ▾");
    public ICommand LocalInsteadCommand { get; private set; } = null!;

    private void InitFinderCommands()
    {
        // The Suggested view's "N of yours need a fight": the finder with that chip on.
        ShowNeedsCommand = new RelayCommand(() =>
        {
            // The finder, clear, with the needs-a-fight strip on top.
            foreach (var c in FinderChips) c.SetQuietly(false);
            _search = ""; OnPropertyChanged(nameof(FinderSearch));
            PickMode = PickMode.Division;
        });
        ToggleFiltersCommand = new RelayCommand(() => FiltersOpen = !FiltersOpen);
        ToggleNeedsCommand = new RelayCommand(() => { _needsOpen = !_needsOpen; OnPropertyChanged(nameof(NeedsOpen)); OnPropertyChanged(nameof(NeedsTitle)); });
        // "Or a local opponent": the chosen fighter stays, the opponent is the club scene.
        LocalInsteadCommand = new RelayCommand(() =>
        {
            _pickMode = PickMode.Local;
            OnPropertyChanged(nameof(PickSuggested)); OnPropertyChanged(nameof(PickDivision)); OnPropertyChanged(nameof(PickLocal));
            RebuildLadder();
        });
    }
}
