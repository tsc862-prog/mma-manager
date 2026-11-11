using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using MmaManager.Core.Model;
using MmaManager.Wpf.Art;
using MmaManager.Wpf.Controls;
using MmaManager.Wpf.Mvvm;

namespace MmaManager.Wpf.ViewModels;

/// <summary>
/// One of YOUR fighters on the Roster screen: the front-office view — contract
/// terms, loyalty, camp and availability precomputed, since this screen is about
/// running the gym, not scouting the world.
/// </summary>
public sealed class MyRosterRow
{
    private readonly Fighter _f;
    private readonly World _w;

    public MyRosterRow(Fighter f, World w, Event? bookedOn, int scouted)
    {
        _f = f;
        _w = w;
        Ovr = scouted;

        var c = f.Contract;
        ContractLine = c is null ? "—"
            : $"{c.FightsRemaining} bout{(c.FightsRemaining == 1 ? "" : "s")} · {c.PursePerFight}/fight{(c.IsExclusive ? "" : " · non-excl")}";
        ExpiringSoon = c is { FightsRemaining: <= 1 };

        if (f.Status == FighterStatus.Injured)
        {
            Availability = f.ReadyToFightOn is { } back ? $"Injured — back {back:d MMM}" : "Injured";
            IsSidelined = true;
        }
        else if (bookedOn is not null)
            Availability = $"Booked: {bookedOn.Name} · {bookedOn.Date:d MMM}";
        else if (f.ReadyToFightOn is { } ready && ready > w.CurrentDate)
            Availability = $"Recovering — ready {ready:d MMM}";
        else
        {
            Availability = "Ready";
            IsReady = true;
        }
    }

    public int FighterId => _f.Id.Value;
    public Fighter Model => _f;
    public ImageSource Portrait => Portraits.Thumb(_f, _w);
    public string Name => _f.FullName;
    public string Badges
    {
        get
        {
            string b = "";
            if (_w.Titles.Values.Any(t => t.CurrentChampion == _f.Id)) b += "👑";
            if (Core.Sim.Org.Franchise.IsFace(_w, _f)) b += "📣";
            return b;
        }
    }
    public string Division => FighterRow.DivisionLabel(_f.WeightClass, _f.Sex);
    public int Age => _f.AgeOn(_w.CurrentDate);
    public string Record => _f.Record.ToString();
    public int RecordSort => FighterRow.RecordKey(_f.Record);
    /// <summary>The scouted read (fog of war), never the truth.</summary>
    public int Ovr { get; }
    public string Form => _f.Momentum >= 20 ? $"▲ {_f.Momentum}" : _f.Momentum <= -20 ? $"▼ {_f.Momentum}" : _f.Momentum.ToString();
    public bool Hot => _f.Momentum >= 20;
    public bool Cold => _f.Momentum <= -20;
    public int Morale => _f.Morale.Value;
    public int Loyalty => _f.Loyalty;
    public string CampName => _f.Camp is { } c && _w.Get(c) is { } camp ? camp.Name : "local scene";

    public string ContractLine { get; }
    /// <summary>Last bout on the deal — the giants may be circling.</summary>
    public bool ExpiringSoon { get; }
    public string Availability { get; }
    public bool IsReady { get; }
    public bool IsSidelined { get; }

    /// <summary>Division order: men's light→heavy, then women's.</summary>
    public int DivisionKey => (int)_f.Sex * 100 + (int)_f.WeightClass;
}

/// <summary>
/// The Roster screen: every fighter under contract to YOUR promotion. Cards by
/// default (a face, the scout's read in words, the deal and what is going on),
/// grouped by division with champions first; the full front-office table is one
/// toggle away and the choice is remembered for the session. Filters and sort
/// apply to both. Selection is held as a fighter id, so it survives refreshes.
/// </summary>
public sealed class MyRosterViewModel : ObservableObject
{
    public const string Everyone = "Everyone", ReadyOnly = "Ready to fight", InjuredOnly = "Injured", Ending = "Deals ending";
    public const string AllDivisions = "All divisions";
    public const string BestFirst = "Best first (scout's read)", ByName = "Name", EndingFirst = "Deal ending first", YoungestFirst = "Youngest first";

    /// <summary>Set by the shell: opens the Contracts app (the empty gym's way out).</summary>
    public Action? OpenContracts { get; set; }
    public ICommand OpenContractsCommand { get; }

    public MyRosterViewModel()
    {
        OpenContractsCommand = new RelayCommand(() => OpenContracts?.Invoke());
    }

    /// <summary>The table view's rows (after filters).</summary>
    public ObservableCollection<MyRosterRow> Fighters { get; } = new();
    /// <summary>The card view: <see cref="CardGroup"/> headings and <see cref="FighterCardModel"/>s, in display order.</summary>
    public ObservableCollection<object> Cards { get; } = new();

    // Cards or Table — remembered for the whole session (static: survives a new career too).
    private static bool s_showTable;
    public bool ShowTable
    {
        get => s_showTable;
        set
        {
            if (s_showTable == value) return;
            s_showTable = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowCards));
        }
    }
    public bool ShowCards { get => !ShowTable; set => ShowTable = !value; }

    public IReadOnlyList<string> ShowOptions { get; } = new[] { Everyone, ReadyOnly, InjuredOnly, Ending };
    public IReadOnlyList<string> SortOptions { get; } = new[] { BestFirst, EndingFirst, YoungestFirst, ByName };
    public ObservableCollection<string> DivisionOptions { get; } = new() { AllDivisions };

    private string _show = Everyone;
    public string Show { get => _show; set { if (Set(ref _show, value ?? Everyone)) Rebuild(); } }
    public bool ShowEveryone { get => Show == Everyone; set { if (value) Show = Everyone; } }
    public bool ShowReady { get => Show == ReadyOnly; set { if (value) Show = ReadyOnly; } }
    public bool ShowInjured { get => Show == InjuredOnly; set { if (value) Show = InjuredOnly; } }
    public bool ShowEnding { get => Show == Ending; set { if (value) Show = Ending; } }

    private string _division = AllDivisions;
    public string Division { get => _division; set { if (Set(ref _division, value ?? AllDivisions)) Rebuild(); } }

    private string _sort = BestFirst;
    public string Sort { get => _sort; set { if (Set(ref _sort, value ?? BestFirst)) Rebuild(); } }

    private int _selectedFighterId;
    /// <summary>The fighter last picked here (or opened anywhere) — an id, so it survives refreshes.</summary>
    public int SelectedFighterId
    {
        get => _selectedFighterId;
        set
        {
            if (!Set(ref _selectedFighterId, value)) return;
            var row = Fighters.FirstOrDefault(r => r.FighterId == value);
            if (!ReferenceEquals(row, _selectedRow)) { _selectedRow = row; OnPropertyChanged(nameof(SelectedRow)); }
        }
    }
    private MyRosterRow? _selectedRow;
    public MyRosterRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (ReferenceEquals(_selectedRow, value)) return;
            _selectedRow = value;
            OnPropertyChanged();
            // A refresh clearing the grid must not forget who was picked.
            if (value is not null) SelectedFighterId = value.FighterId;
        }
    }

    private string _summary = "";
    public string Summary { get => _summary; private set => Set(ref _summary, value); }
    public ObservableCollection<string> SummaryChips { get; } = new();

    private bool _isEmpty;
    public bool IsEmpty { get => _isEmpty; private set { if (Set(ref _isEmpty, value)) OnPropertyChanged(nameof(HasFighters)); } }
    public bool HasFighters => !IsEmpty;

    private bool _filteredEmpty;
    /// <summary>The roster has people but the current filter shows none of them.</summary>
    public bool FilteredEmpty { get => _filteredEmpty; private set => Set(ref _filteredEmpty, value); }

    private sealed record Entry(MyRosterRow Row, Fighter F, Event? Booked, int Scout, bool Champion, int TitleOrder);
    private List<Entry> _all = new();
    private World? _world;

    public void Attach(World world)
    {
        _world = world;
        // Who's already booked on an upcoming card (availability at a glance).
        var booked = new Dictionary<FighterId, Event>();
        foreach (var ev in world.Events.Values.Where(e => e.Status == EventStatus.Scheduled).OrderBy(e => e.Date).ThenBy(e => e.Id.Value))
            foreach (var fight in ev.Card)
            {
                booked.TryAdd(fight.RedCorner, ev);
                booked.TryAdd(fight.BlueCorner, ev);
            }
        var champs = world.Titles.Values.Where(t => t.CurrentChampion is not null && t.Promotion == world.PlayerPromotion)
            .Select(t => t.CurrentChampion!.Value).ToHashSet();

        _all = world.Fighters.Values
            .Where(f => f.Status != FighterStatus.Retired && f.Contract?.Promotion == world.PlayerPromotion)
            .Select(f =>
            {
                int scout = FighterRow.Scouted(world, f);
                var ev = booked.GetValueOrDefault(f.Id);
                return new Entry(new MyRosterRow(f, world, ev, scout), f, ev, scout, champs.Contains(f.Id), 0);
            })
            .ToList();

        var divisions = _all.Select(e => (e.Row.DivisionKey, e.Row.Division)).Distinct().OrderBy(d => d.DivisionKey).Select(d => d.Division).ToList();
        if (!DivisionOptions.Skip(1).SequenceEqual(divisions))
        {
            DivisionOptions.Clear();
            DivisionOptions.Add(AllDivisions);
            foreach (var d in divisions) DivisionOptions.Add(d);
        }
        if (!DivisionOptions.Contains(_division)) { _division = AllDivisions; OnPropertyChanged(nameof(Division)); }

        IsEmpty = _all.Count == 0;
        int divs = world.Get(world.PlayerPromotion)?.Divisions.Count ?? 0;
        SummaryChips.Clear();
        if (_all.Count > 0)
        {
            SummaryChips.Add($"{_all.Count} fighter{(_all.Count == 1 ? "" : "s")}");
            if (champs.Count > 0) SummaryChips.Add($"{champs.Count} champion{(champs.Count == 1 ? "" : "s")}");
            SummaryChips.Add($"{_all.Count(e => e.Row.IsReady)} ready to fight");
            int injured = _all.Count(e => e.Row.IsSidelined);
            if (injured > 0) SummaryChips.Add($"{injured} injured");
            int ending = _all.Count(e => e.Row.ExpiringSoon);
            if (ending > 0) SummaryChips.Add($"{ending} on the last fight of a deal");
        }
        Summary = _all.Count == 0
            ? "No fighters under contract."
            : $"{_all.Count} fighter{(_all.Count == 1 ? "" : "s")} under contract  ·  {divs} divisions  ·  " +
              $"{_all.Count(e => e.Row.IsReady)} ready  ·  {_all.Count(e => !e.Row.IsReady && !e.Row.IsSidelined)} booked or recovering  ·  " +
              $"{_all.Count(e => e.Row.IsSidelined)} injured";
        Rebuild();
    }

    private void Rebuild()
    {
        if (_world is null) return;
        var world = _world;
        OnPropertyChanged(nameof(ShowEveryone)); OnPropertyChanged(nameof(ShowReady));
        OnPropertyChanged(nameof(ShowInjured)); OnPropertyChanged(nameof(ShowEnding));

        IEnumerable<Entry> shown = _all;
        shown = Show switch
        {
            ReadyOnly => shown.Where(e => e.Row.IsReady),
            InjuredOnly => shown.Where(e => e.Row.IsSidelined),
            Ending => shown.Where(e => e.Row.ExpiringSoon),
            _ => shown,
        };
        if (Division != AllDivisions) shown = shown.Where(e => e.Row.Division == Division);

        // Sort inside each division; ability order is the SCOUT's read, never the truth.
        Func<IEnumerable<Entry>, IOrderedEnumerable<Entry>> order = Sort switch
        {
            ByName => s => s.OrderBy(e => e.F.LastName).ThenBy(e => e.F.FirstName),
            EndingFirst => s => s.OrderBy(e => e.F.Contract?.FightsRemaining ?? 99).ThenByDescending(e => e.Scout),
            YoungestFirst => s => s.OrderBy(e => e.Row.Age).ThenByDescending(e => e.Scout),
            _ => s => s.OrderByDescending(e => e.Scout),
        };
        var groups = shown.GroupBy(e => e.Row.DivisionKey).OrderBy(g => g.Key).ToList();

        Fighters.Clear();
        Cards.Clear();
        foreach (var g in groups)
        {
            var members = order(g.OrderByDescending(e => e.Champion)).ThenBy(e => e.Row.FighterId).ToList();
            var champ = members.FirstOrDefault(e => e.Champion);
            Cards.Add(new CardGroup
            {
                Title = members[0].Row.Division,
                Detail = $"{members.Count} fighter{(members.Count == 1 ? "" : "s")}" + (champ is null ? "  ·  belt vacant or held elsewhere" : $"  ·  champion {champ.F.FirstName} {champ.F.LastName}"),
            });
            foreach (var e in members)
            {
                Fighters.Add(e.Row);
                Cards.Add(new FighterCardModel(e.F, world, e.Booked) { IsSelected = e.Row.FighterId == _selectedFighterId, HideDivision = true });
            }
        }
        FilteredEmpty = !IsEmpty && Fighters.Count == 0;

        // Re-point the table's selection at the rebuilt row for the same fighter.
        _selectedRow = Fighters.FirstOrDefault(r => r.FighterId == _selectedFighterId);
        OnPropertyChanged(nameof(SelectedRow));
    }
}
