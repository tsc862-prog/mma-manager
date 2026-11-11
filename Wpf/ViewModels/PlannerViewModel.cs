using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Finance;
using MmaManager.Core.Sim.Living;
using MmaManager.Wpf.Art;
using MmaManager.Wpf.Mvvm;

namespace MmaManager.Wpf.ViewModels;

/// <summary>
/// THE PLANNER: the promoter's calendar. Your own cards as rich entries (the
/// main event's faces, the poster, what the sim projects for the house and the
/// night's net, and the risks it sees), the sport's ANNOUNCED cards as lighter
/// entries (public facts only: promotion, date, venue, main event, belt and
/// flagship markers), and the dates on your timeline that matter: a rights
/// partner's cadence clock, fighters coming back from injury or suspension,
/// deals that end with a card, decisions and offers with a deadline.
///
/// Everything is read from the sim: <see cref="ForwardCalendar.Upcoming"/> for
/// the cards, <see cref="ForwardCalendar.RivalCardsNear"/> and
/// <see cref="Financials.ClashingCards(World, Event)"/> for what costs the house,
/// <see cref="PlayerBooking.Project"/> for your cards' money and risks. The
/// planner judges nothing itself and writes nothing to the world.
/// Rows are rebuilt on every refresh; the screen holds dates and ids only.
/// </summary>
public sealed class PlannerViewModel : ObservableObject
{
    /// <summary>How many months the planner looks ahead (this one included).</summary>
    public const int MonthsAhead = 6;

    public const string ScopeMine = "Just mine", ScopeNear = "Near me", ScopeAll = "Everywhere", ScopeFlagships = "Flagships";

    /// <summary>Set by the shell: open Book Event with its plan form on this date.</summary>
    public Action<DateOnly>? PlanCardOn { get; set; }
    /// <summary>Set by the shell: open one of your cards on the booking desk.</summary>
    public Action<EventId>? OpenMyCard { get; set; }
    /// <summary>Set by the shell: jump to another app (a <see cref="Tab"/> index).</summary>
    public Action<int>? Navigate { get; set; }

    private readonly PlayerBooking _booking = new();
    private World? _world;
    private bool _wanted;
    private bool _browsed;   // the player turned the page (the grid stops following the clock)

    public PlannerViewModel()
    {
        PrevMonthCommand = new RelayCommand(() => { _browsed = true; Month = Month.AddMonths(-1); });
        NextMonthCommand = new RelayCommand(() => { _browsed = true; Month = Month.AddMonths(1); });
        ThisMonthCommand = new RelayCommand(() => { Selected = null; _browsed = false; Month = DefaultMonth; });
        SelectDayCommand = new RelayCommand<DayCell>(d =>
        {
            if (d is null) return;
            Selected = Selected == d.Date ? null : d.Date;
        });
        ClearSelectionCommand = new RelayCommand(() => Selected = null);
        PlanSelectedCommand = new RelayCommand(() =>
        {
            if (Selected is { } d && _world is not null && d > _world.CurrentDate) PlanCardOn?.Invoke(d);
        });
        PlanNextCommand = new RelayCommand(() => { if (_world is not null) PlanCardOn?.Invoke(SuggestedPlanDate); });
        OpenCardCommand = new RelayCommand<int>(id =>
        {
            if (_world is null) return;
            var eid = new EventId(id);
            if (_world.Get(eid) is { } ev && ev.Promotion == _world.PlayerPromotion && OpenMyCard is not null) OpenMyCard(eid);
            else AppCommands.OpenEvent.Execute(id);
        });
    }

    // --- Commands -------------------------------------------------------------
    public ICommand PrevMonthCommand { get; }
    public ICommand NextMonthCommand { get; }
    public ICommand ThisMonthCommand { get; }
    public ICommand SelectDayCommand { get; }
    public ICommand ClearSelectionCommand { get; }
    public ICommand PlanSelectedCommand { get; }
    public ICommand PlanNextCommand { get; }
    /// <summary>Open a card (parameter = event id): yours on the booking desk, anyone else's on its event page.</summary>
    public ICommand OpenCardCommand { get; }

    // --- View state (kept across refreshes) -------------------------------------
    private DateOnly _month;
    /// <summary>The first of the month the grid shows.</summary>
    public DateOnly Month { get => _month; set { var m = Clamp(new DateOnly(value.Year, value.Month, 1)); if (Set(ref _month, m)) { OnPropertyChanged(nameof(MonthTitle)); BuildGrid(); } } }
    public string MonthTitle => Month.ToString("MMMM yyyy");
    private DateOnly FirstMonth => _world is null ? Month : new DateOnly(_world.CurrentDate.Year, _world.CurrentDate.Month, 1);
    private DateOnly LastMonth => FirstMonth.AddMonths(MonthsAhead - 1);
    /// <summary>Where the grid opens: this month, or next month once this one is in its last week
    /// (time moves a week at a time, so a nearly spent month would open on a page of the past).</summary>
    private DateOnly DefaultMonth => _world is { } w && w.CurrentDate.Day > DateTime.DaysInMonth(w.CurrentDate.Year, w.CurrentDate.Month) - 7
        ? FirstMonth.AddMonths(1) : FirstMonth;
    private DateOnly Clamp(DateOnly m) => _world is null ? m : m < FirstMonth ? FirstMonth : m > LastMonth ? LastMonth : m;

    private DateOnly? _selected;
    /// <summary>The date the player clicked (null = none): the rail reads it out.</summary>
    public DateOnly? Selected
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value)) return;
            if (value is { } d && (d.Year != Month.Year || d.Month != Month.Month) && _world is not null) { _browsed = true; Month = d; }
            foreach (var c in Days) c.IsSelected = c.Date == value;
            BuildSelection();
        }
    }

    private bool _agenda;
    /// <summary>The list view instead of the month grid.</summary>
    public bool ShowAgenda { get => _agenda; set { if (Set(ref _agenda, value)) OnPropertyChanged(nameof(ShowMonth)); } }
    public bool ShowMonth { get => !_agenda; set => ShowAgenda = !value; }

    private string _scope = ScopeNear;
    /// <summary>Which of the world's cards show: none, your regions, everywhere, or the flagship nights.</summary>
    public string Scope { get => _scope; set { if (Set(ref _scope, value ?? ScopeNear)) { RaiseScope(); Rebuild(); } } }
    public bool ScopeIsMine { get => Scope == ScopeMine; set { if (value) Scope = ScopeMine; } }
    public bool ScopeIsNear { get => Scope == ScopeNear; set { if (value) Scope = ScopeNear; } }
    public bool ScopeIsAll { get => Scope == ScopeAll; set { if (value) Scope = ScopeAll; } }
    public bool ScopeIsFlagships { get => Scope == ScopeFlagships; set { if (value) Scope = ScopeFlagships; } }
    private void RaiseScope()
    {
        OnPropertyChanged(nameof(ScopeIsMine)); OnPropertyChanged(nameof(ScopeIsNear));
        OnPropertyChanged(nameof(ScopeIsAll)); OnPropertyChanged(nameof(ScopeIsFlagships));
    }

    // --- What the screen draws ----------------------------------------------------
    public ObservableCollection<DayCell> Days { get; } = new();
    public IReadOnlyList<string> WeekdayNames { get; } = new[] { "MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN" };
    /// <summary>The agenda: week headings, your cards, markers and the world's cards in date order.</summary>
    public ObservableCollection<object> Agenda { get; } = new();
    /// <summary>The rail when no date is picked: what is coming on your timeline.</summary>
    public ObservableCollection<PlannerMarker> HeadsUp { get; } = new();
    /// <summary>The flagship nights inside the horizon (announced ones, and the months they are due).</summary>
    public ObservableCollection<FlagshipModel> Flagships { get; } = new();
    /// <summary>The rail for a picked date: what is on that day.</summary>
    public ObservableCollection<object> DayItems { get; } = new();
    /// <summary>Cards inside the clash window that the box office would charge against a card on the picked date.</summary>
    public ObservableCollection<RivalCardModel> ClashCharged { get; } = new();
    /// <summary>Cards inside the clash window that are only nearby (not charged).</summary>
    public ObservableCollection<RivalCardModel> ClashNearby { get; } = new();

    private string _headline = "", _subline = "", _selectedTitle = "", _selectedLine = "", _clashLine = "", _planHint = "";
    /// <summary>"3 cards booked · next: Fight Night 4 in 12 days".</summary>
    public string Headline { get => _headline; private set => Set(ref _headline, value); }
    public string Subline { get => _subline; private set => Set(ref _subline, value); }
    public string SelectedTitle { get => _selectedTitle; private set => Set(ref _selectedTitle, value); }
    public string SelectedLine { get => _selectedLine; private set => Set(ref _selectedLine, value); }
    /// <summary>The clash read for the picked date, in plain words.</summary>
    public string ClashLine { get => _clashLine; private set => Set(ref _clashLine, value); }
    public string PlanHint { get => _planHint; private set => Set(ref _planHint, value); }

    private bool _hasSelection, _canPlanSelected, _hasMyCards, _agendaEmpty, _hasDayItems, _hasHeadsUp, _hasFlagships, _hasClashCharged, _hasClashNearby;
    public bool HasSelection { get => _hasSelection; private set { if (Set(ref _hasSelection, value)) OnPropertyChanged(nameof(NoSelection)); } }
    public bool NoSelection => !HasSelection;
    public bool CanPlanSelected { get => _canPlanSelected; private set => Set(ref _canPlanSelected, value); }
    private bool _hasMineHere;
    private int _mineHereId;
    private string _mineHereText = "";
    /// <summary>One of your cards is on the picked date: the rail offers it instead of "Plan a card here".</summary>
    public bool HasMineHere { get => _hasMineHere; private set => Set(ref _hasMineHere, value); }
    public int MineHereId { get => _mineHereId; private set => Set(ref _mineHereId, value); }
    public string MineHereText { get => _mineHereText; private set => Set(ref _mineHereText, value); }
    public bool HasMyCards { get => _hasMyCards; private set { if (Set(ref _hasMyCards, value)) OnPropertyChanged(nameof(NoMyCards)); } }
    public bool NoMyCards => !HasMyCards;
    public bool AgendaEmpty { get => _agendaEmpty; private set => Set(ref _agendaEmpty, value); }
    public bool HasDayItems { get => _hasDayItems; private set => Set(ref _hasDayItems, value); }
    public bool HasHeadsUp { get => _hasHeadsUp; private set { if (Set(ref _hasHeadsUp, value)) OnPropertyChanged(nameof(NoHeadsUp)); } }
    public bool NoHeadsUp => !HasHeadsUp;
    public bool HasFlagships { get => _hasFlagships; private set => Set(ref _hasFlagships, value); }
    public bool HasClashCharged { get => _hasClashCharged; private set => Set(ref _hasClashCharged, value); }
    public bool HasClashNearby { get => _hasClashNearby; private set => Set(ref _hasClashNearby, value); }

    /// <summary>The plan form's own default lead (Book Event opens three weeks out).</summary>
    private DateOnly SuggestedPlanDate => _world!.CurrentDate.AddDays(21);

    // --- Build ----------------------------------------------------------------------

    /// <summary>The window is on screen: build now, and on every refresh from here on.</summary>
    public void Wanted()
    {
        if (_wanted) return;
        _wanted = true;
        Rebuild();
    }

    public void Attach(World world)
    {
        bool sameWorld = ReferenceEquals(world, _world);
        _world = world;
        if (!sameWorld) { _month = DefaultMonth; _selected = null; OnPropertyChanged(nameof(Month)); OnPropertyChanged(nameof(MonthTitle)); }
        else
        {
            // A page the player turned to stays put; otherwise the grid follows the clock.
            _month = _browsed ? Clamp(_month) : DefaultMonth;
            OnPropertyChanged(nameof(MonthTitle));
            if (_selected is { } s && s < world.CurrentDate) _selected = null;
        }
        if (_wanted) Rebuild();
    }

    // Everything the views draw comes from these per-build lists.
    private List<UpcomingCard> _cards = new();
    private readonly Dictionary<int, MyCardModel> _mine = new();
    private readonly Dictionary<int, RivalCardModel> _rivals = new();
    private List<PlannerMarker> _markers = new();
    private HashSet<Region> _nearRegions = new();
    private DateOnly _horizonEnd;

    private void Rebuild()
    {
        if (_world is null || !_wanted) return;
        var w = _world;
        var today = w.CurrentDate;
        _horizonEnd = FirstMonth.AddMonths(MonthsAhead).AddDays(-1);
        _cards = ForwardCalendar.Upcoming(w, today, _horizonEnd).ToList();
        var player = w.Get(w.PlayerPromotion);

        // Your cards first: they decide which rival cards are a clash.
        _mine.Clear();
        _rivals.Clear();
        var myEvents = _cards.Where(c => c.IsPlayer).ToList();
        foreach (var c in myEvents) _mine[c.Event.Id.Value] = BuildMine(w, c);
        var chargedAgainst = new Dictionary<int, Event>();   // rival event id -> the card of yours it costs
        foreach (var c in myEvents)
            foreach (var o in Financials.ClashingCards(w, c.Event))
                chargedAgainst.TryAdd(o.Id.Value, c.Event);

        _nearRegions = new HashSet<Region>();
        if (player is not null) _nearRegions.Add(player.BaseRegion);
        foreach (var c in myEvents) _nearRegions.Add(c.Region);

        foreach (var c in _cards.Where(c => !c.IsPlayer))
            _rivals[c.Event.Id.Value] = BuildRival(w, c, chargedAgainst.GetValueOrDefault(c.Event.Id.Value));

        _markers = BuildMarkers(w, player, myEvents);

        // Headline.
        HasMyCards = myEvents.Count > 0;
        if (myEvents.Count == 0)
        {
            Headline = "Nothing booked yet";
            Subline = player is null ? "" : $"Your calendar is empty. Pick a date and plan a card, or see what the sport has announced.";
        }
        else
        {
            var next = myEvents[0];
            int days = next.Date.DayNumber - today.DayNumber;
            Headline = myEvents.Count == 1 ? "1 card on your calendar" : $"{myEvents.Count} cards on your calendar";
            Subline = $"Next: {next.Event.Name}, {When(days)}";
        }

        // Rail: heads up + flagships.
        HeadsUp.Clear();
        foreach (var m in _markers.Take(8)) HeadsUp.Add(m);
        HasHeadsUp = HeadsUp.Count > 0;
        BuildFlagships(w, today);

        BuildGrid();
        BuildAgenda();
        BuildSelection();
    }

    /// <summary>Whether a world card shows under the current scope (a card that costs one of your houses always shows).</summary>
    private bool Shows(RivalCardModel r) => r.Charged || Scope switch
    {
        ScopeMine => false,
        ScopeAll => true,
        ScopeFlagships => r.IsSignature,
        _ => _nearRegions.Contains(r.Region),
    };

    private void BuildGrid()
    {
        Days.Clear();
        if (_world is null || !_wanted) return;
        var today = _world.CurrentDate;
        var first = Month;
        int back = ((int)first.DayOfWeek + 6) % 7;           // Monday-first weeks
        var start = first.AddDays(-back);
        int weeks = (int)Math.Ceiling((back + DateTime.DaysInMonth(first.Year, first.Month)) / 7.0);
        WeekRows = weeks;

        for (int i = 0; i < weeks * 7; i++)
        {
            var d = start.AddDays(i);
            var cell = new DayCell(d)
            {
                InMonth = d.Month == first.Month,
                IsToday = d == today,
                IsPast = d < today,
                IsSelected = d == _selected,
            };
            var chips = new List<DayChip>();
            foreach (var c in _cards.Where(c => c.Date == d))
            {
                if (c.IsPlayer && _mine.TryGetValue(c.Event.Id.Value, out var m))
                    chips.Add(new DayChip(DayChipKind.Mine, m.Name, m.MainEventShort, m.StatusTone, null));
                else if (_rivals.TryGetValue(c.Event.Id.Value, out var r) && Shows(r))
                    chips.Add(new DayChip(r.Charged ? DayChipKind.Clash : DayChipKind.Rival, r.Abbreviation,
                        r.MainEventShort.Length > 0 ? r.MainEventShort : r.Name, r.Charged ? CardTone.Bad : CardTone.Neutral, r.Promo)
                    { IsSignature = r.IsSignature, IsTitle = r.IsTitle });
            }
            // Mine first, then the clashes, then markers, then the rest of the world.
            var markers = _markers.Where(m => m.Date == d)
                .Select(m => new DayChip(DayChipKind.Marker, m.Short, "", m.Tone, null) { Icon = m.Icon });
            var ordered = chips.Where(x => x.Kind == DayChipKind.Mine)
                .Concat(chips.Where(x => x.Kind == DayChipKind.Clash))
                .Concat(markers)
                .Concat(chips.Where(x => x.Kind == DayChipKind.Rival))
                .ToList();
            const int fit = 3;
            foreach (var x in ordered.Take(ordered.Count > fit ? fit - 1 : fit)) cell.Chips.Add(x);
            cell.More = ordered.Count > fit ? $"+{ordered.Count - (fit - 1)} more" : "";
            cell.HasMine = ordered.Any(x => x.Kind == DayChipKind.Mine);
            cell.Tooltip = ordered.Count == 0 ? null : string.Join("\n", ordered.Select(x => x.Kind switch
            {
                DayChipKind.Mine => $"Your card: {x.Text}",
                DayChipKind.Marker => x.Text,
                _ => $"{x.Text}: {x.Sub}",
            }));
            Days.Add(cell);
        }
        CanPrev = Month > FirstMonth;
        CanNext = Month < LastMonth;
    }

    private bool _canPrev, _canNext;
    public bool CanPrev { get => _canPrev; private set => Set(ref _canPrev, value); }
    public bool CanNext { get => _canNext; private set => Set(ref _canNext, value); }

    private int _weekRows = 5;
    /// <summary>How many week rows this month needs (4-6).</summary>
    public int WeekRows { get => _weekRows; private set => Set(ref _weekRows, value); }

    private void BuildAgenda()
    {
        Agenda.Clear();
        if (_world is null) return;
        var today = _world.CurrentDate;
        var rows = new List<(DateOnly Date, int Order, object Item)>();
        foreach (var m in _mine.Values) rows.Add((m.DateValue, 0, m));
        foreach (var m in _markers) rows.Add((m.Date, 1, m));
        foreach (var r in _rivals.Values.Where(Shows)) rows.Add((r.DateValue, r.Charged ? 2 : 3, r));

        int weekStart(DateOnly d) => d.DayNumber - (((int)d.DayOfWeek + 6) % 7);
        int thisWeek = weekStart(today);
        int? current = null;
        foreach (var (date, _, item) in rows.OrderBy(x => x.Date).ThenBy(x => x.Order))
        {
            int wk = weekStart(date);
            if (wk != current)
            {
                current = wk;
                var monday = DateOnly.FromDayNumber(wk);
                string title = wk == thisWeek ? "This week" : wk == thisWeek + 7 ? "Next week" : $"Week of {monday:d MMMM}";
                Agenda.Add(new AgendaWeek(title, $"{monday:d MMM} – {monday.AddDays(6):d MMM}"));
            }
            Agenda.Add(item);
        }
        AgendaEmpty = Agenda.Count == 0;
    }

    private void BuildSelection()
    {
        DayItems.Clear();
        ClashCharged.Clear();
        ClashNearby.Clear();
        HasSelection = _selected is not null && _world is not null;
        if (_selected is not { } d || _world is null)
        {
            CanPlanSelected = HasMineHere = false;
            HasDayItems = HasClashCharged = HasClashNearby = false;
            return;
        }
        var w = _world;
        var today = w.CurrentDate;
        int days = d.DayNumber - today.DayNumber;
        SelectedTitle = d.ToString("dddd d MMMM");
        SelectedLine = days < 0 ? "Already gone" : days == 0 ? "Today" : $"{When(days)} from today";

        foreach (var m in _mine.Values.Where(m => m.DateValue == d)) DayItems.Add(m);
        foreach (var m in _markers.Where(m => m.Date == d)) DayItems.Add(m);
        // The world's cards that day are read out below, in the clash window (every region: the box office has no region test).
        HasDayItems = DayItems.Count > 0;

        // The clash read: a card of yours already there, or one you might plan here.
        var mineHere = _mine.Values.FirstOrDefault(m => m.DateValue == d);
        var (near, penalty) = ForwardCalendar.RivalCardsNear(w, w.PlayerPromotion, d,
            except: mineHere is null ? null : new EventId(mineHere.Id));
        foreach (var rc in near)
        {
            var model = _rivals.TryGetValue(rc.Card.Event.Id.Value, out var known) ? known : BuildRival(w, rc.Card, null);
            (rc.Charged ? ClashCharged : ClashNearby).Add(model.WithDistance(d));
        }
        HasClashCharged = ClashCharged.Count > 0;
        HasClashNearby = ClashNearby.Count > 0;
        int window = Financials.ClashWindowDays;
        string subject = mineHere is null ? "A card here" : mineHere.Name;
        ClashLine = penalty > 0
            ? $"{subject} would share its week with another promotion's show: the box office takes {penalty:P0} off the house."
            + (ClashCharged.Count == 0 ? " (The local scene's club shows count too.)" : "")
            : near.Count == 0
                ? $"Nobody else has a card announced within {window} days. A clear week."
                : $"Nothing announced within {window} days would cost the house. The cards below are close by, not charged.";

        CanPlanSelected = days > 0 && mineHere is null;
        HasMineHere = mineHere is not null;
        MineHereId = mineHere?.Id ?? 0;
        MineHereText = mineHere is null ? "" : $"Open {mineHere.Name} on the booking desk";
        PlanHint = days <= 0 ? "That date has gone; pick one ahead."
            : mineHere is not null ? ""
            : "";
    }

    // --- Models -------------------------------------------------------------------

    private MyCardModel BuildMine(World w, UpcomingCard c)
    {
        var ev = c.Event;
        var proj = _booking.Project(w, ev);
        int days = c.Date.DayNumber - w.CurrentDate.DayNumber;
        var red = c.MainEvent is { } m ? w.Get(m.RedCorner) : null;
        var blue = c.MainEvent is { } m2 ? w.Get(m2.BlueCorner) : null;

        // Deals that end with this card (his last fight on the deal is on it).
        var ending = ev.Card.SelectMany(f => new[] { f.RedCorner, f.BlueCorner })
            .Select(id => w.Get(id))
            .Where(f => f?.Contract is { FightsRemaining: 1 } k && k.Promotion == w.PlayerPromotion)
            .Select(f => f!.LastName).ToList();

        CardTone tone; string status;
        if (days <= 0) { status = "Fight night"; tone = CardTone.Warn; }
        else if (ev.Card.Count == 0) { status = "No bouts yet"; tone = CardTone.Bad; }
        else if (proj.Risks.Count > 0) { status = proj.Risks.Count == 1 ? "1 thing to fix" : $"{proj.Risks.Count} things to fix"; tone = CardTone.Warn; }
        else { status = "On track"; tone = CardTone.Good; }

        string house = proj.Capacity is { } cap
            ? $"{proj.Attendance:N0} of {cap:N0} seats"
            : $"{proj.Attendance:N0} expected";

        return new MyCardModel(ev, w)
        {
            Name = ev.Name,
            DateValue = c.Date,
            DateLine = $"{c.Date:ddd d MMM}  ·  {When(days).ToUpperInvariant()}",
            Venue = Join(c.Venue, FighterRow.Pretty(c.Region.ToString())),
            MainEvent = red is not null && blue is not null ? $"{red.FullName} vs {blue.FullName}" : "Main event not set",
            MainEventShort = c.MainEventLabel(w),
            RedId = red?.Id.Value ?? 0,
            BlueId = blue?.Id.Value ?? 0,
            RedFace = red is null ? null : new LazyFace(ready => Portraits.Thumb(red, w, ready)),
            BlueFace = blue is null ? null : new LazyFace(ready => Portraits.Thumb(blue, w, ready)),
            HasMainEvent = red is not null && blue is not null,
            IsTitle = c.TitleOnTheLine,
            Bouts = ev.Card.Count == 1 ? "1 bout booked" : $"{ev.Card.Count} bouts booked",
            HouseLine = ev.Card.Count == 0 ? "No main event to sell yet" : house,
            NetLine = ev.Card.Count == 0 ? "" : $"{Signed(proj.Net)} on the night",
            NetTone = proj.Net.Amount >= 0 ? CardTone.Good : CardTone.Bad,
            StatusText = status,
            StatusTone = tone,
            // The deal-ending risks are already said in one line (EndingLine): the fixes come first.
            Risks = proj.Risks.OrderBy(r => r.Kind == BookingRiskKind.LastFightOnDeal ? 1 : 0).Take(2).Select(r => r.Text).ToList(),
            MoreRisks = proj.Risks.Count > 2 ? $"and {proj.Risks.Count - 2} more on the booking desk" : "",
            EndingLine = ending.Count == 0 ? ""
                : ending.Count == 1 ? $"{ending[0]}'s deal ends with this card"
                : $"{ending.Count} deals end with this card: {string.Join(", ", ending.Take(4))}{(ending.Count > 4 ? "…" : "")}",
        };
    }

    private RivalCardModel BuildRival(World w, UpcomingCard c, Event? chargedAgainst)
    {
        var promo = c.Promotion;
        return new RivalCardModel
        {
            Id = c.Event.Id.Value,
            PromotionId = promo.Id.Value,
            Abbreviation = promo.Abbreviation,
            Promo = promo,
            Name = c.Event.Name,
            DateValue = c.Date,
            DateLine = c.Date.ToString("ddd d MMM"),
            Region = c.Region,
            Venue = Join(c.Venue, FighterRow.Pretty(c.Region.ToString())),
            MainEventShort = c.MainEventLabel(w),
            CoMain = c.CoMain is { } cm && w.Get(cm.RedCorner) is { } a && w.Get(cm.BlueCorner) is { } b ? $"Co-main: {a.LastName} vs {b.LastName}" : "",
            IsTitle = c.TitleOnTheLine,
            IsSignature = c.IsSignature,
            Announced = $"Announced {c.AnnouncedOn:d MMM}",
            Charged = chargedAgainst is not null,
            ChargedLine = chargedAgainst is null ? "" : $"Costs your {chargedAgainst.Name} house ({Financials.ClashPenalty:P0})",
        };
    }

    private List<PlannerMarker> BuildMarkers(World w, Promotion? player, List<UpcomingCard> myEvents)
    {
        var list = new List<PlannerMarker>();
        if (player is null) return list;
        var today = w.CurrentDate;
        var end = _horizonEnd;
        bool inRange(DateOnly d) => d >= today && d <= end;

        // A rights partner's content clock: the deal promises a card every N days.
        foreach (var deal in player.MediaDeals)
        {
            var cadence = deal.Requirements.FirstOrDefault(r => r.Kind == DealRequirementKind.EventCadence);
            if (cadence is null) continue;
            var due = (deal.LastAiredOn ?? deal.SignedOn).AddDays(cadence.Threshold);
            if (due < today) due = today;
            if (!inRange(due)) continue;
            var cover = myEvents.FirstOrDefault(e => e.Date <= due);
            string strikes = deal.Strikes > 0 ? $" Strike {deal.Strikes} of {MediaService.StrikeLimit} already." : "";
            list.Add(new PlannerMarker
            {
                Date = due,
                Icon = "Broadcast",
                Short = $"Card due · {deal.Partner.Split(' ')[0]}",
                Text = cover is null ? $"{deal.Partner} needs a card by {due:d MMM}" : $"{deal.Partner} wants a card by {due:d MMM}",
                Detail = cover is null
                    ? $"The deal promises an event every {cadence.Threshold} days. Miss it and they take a strike.{strikes}"
                    : $"Covered by {cover.Event.Name} on {cover.Date:d MMM}.{strikes}",
                Tone = cover is null ? (deal.Strikes > 0 ? CardTone.Bad : CardTone.Warn) : CardTone.Info,
                Open = new RelayCommand(() => Navigate?.Invoke(Tab.Media)),
                OpenText = "Media desk",
            });
        }

        // Your fighters coming back: injuries and suspensions with a return date.
        foreach (var f in w.RosterOf(w.PlayerPromotion))
        {
            DateOnly? back = null; string why = "";
            if (f.Status == FighterStatus.Suspended && f.SuspendedUntil is { } s) { back = s; why = "suspension ends"; }
            else if (f.CurrentInjury is { } inj) { back = f.ReadyToFightOn ?? inj.ExpectedReturn; why = inj.Description; }
            if (back is not { } b || !inRange(b) || b == today) continue;
            int id = f.Id.Value;
            list.Add(new PlannerMarker
            {
                Date = b,
                Icon = "Check",
                Short = $"{f.LastName} cleared",
                Text = $"{f.FirstName} {f.LastName} cleared to fight",
                Detail = f.Status == FighterStatus.Suspended ? "His suspension ends." : $"Back from {why}.",
                Tone = CardTone.Good,
                FighterId = id,
                Face = new LazyFace(ready => Portraits.Thumb(f, w, ready)),
                Open = AppCommands.OpenFighter,
                OpenParameter = id,
                OpenText = "Profile",
            });
        }

        // Decisions on the desk with a deadline.
        foreach (var dcs in w.Decisions.Where(x => x.State == DecisionState.Pending && inRange(x.Deadline)))
        {
            var who = dcs.Fighter is { } fighter ? w.Get(fighter) : null;
            list.Add(new PlannerMarker
            {
                Date = dcs.Deadline,
                Icon = "Bell",
                Short = "Answer due",
                Text = DecisionTitle(dcs, who, w),
                Detail = dcs.Detail,
                Tone = CardTone.Warn,
                FighterId = who?.Id.Value,
                Open = new RelayCommand(() => Navigate?.Invoke(Tab.Home)),
                OpenText = "On the desk",
            });
        }

        // Offers on the table that lapse.
        foreach (var o in player.Offers.Where(o => inRange(o.ExpiresOn)))
            list.Add(new PlannerMarker
            {
                Date = o.ExpiresOn,
                Icon = o.Media is null ? "Money" : "Broadcast",
                Short = $"{o.Partner} offer ends",
                Text = $"{o.Partner}'s offer expires",
                Detail = o.Media is { } mk ? $"A {MediaService.Label(mk)} rights offer, {Compact(o.FeePerEvent)} a card." : $"A sponsorship, {Compact(o.FeePerEvent)} a card.",
                Tone = CardTone.Info,
                Open = new RelayCommand(() => Navigate?.Invoke(o.Media is null ? Tab.Finance : Tab.Media)),
                OpenText = o.Media is null ? "Finance" : "Media desk",
            });

        return list.OrderBy(m => m.Date).ThenBy(m => m.Tone == CardTone.Bad ? 0 : m.Tone == CardTone.Warn ? 1 : 2).ToList();
    }

    private static string DecisionTitle(Decision d, Fighter? who, World w) => d.Kind switch
    {
        DecisionKind.TitleShotRequest => $"{who?.LastName ?? "A contender"} wants an answer on the title shot",
        DecisionKind.SponsorShowcase => $"{(d.Sponsor.Length > 0 ? d.Sponsor : "A sponsor")} wants a showcase answer",
        DecisionKind.VeteranCut => $"The board wants an answer on {who?.LastName ?? "a veteran"}",
        DecisionKind.AcquisitionOffer => $"{(d.Promotion is { } p && w.Get(p) is { } org ? org.Abbreviation : "A rival")} is for sale: answer due",
        _ => "A decision is due",
    };

    private void BuildFlagships(World w, DateOnly today)
    {
        Flagships.Clear();
        var seen = new HashSet<int>();
        // Announced flagship nights (public: the card is on the calendar).
        foreach (var c in _cards.Where(c => c.IsSignature && !c.IsPlayer))
        {
            seen.Add(c.Promotion.Id.Value);
            Flagships.Add(new FlagshipModel
            {
                Name = c.Event.Name,
                When = c.Date.ToString("ddd d MMMM"),
                Line = c.MainEventLabel(w) is { Length: > 0 } me ? $"{me}{(c.TitleOnTheLine ? " · title on the line" : "")}" : "Main event to come",
                Where = Join(c.Venue, FighterRow.Pretty(c.Region.ToString())),
                Promo = c.Promotion,
                EventId = c.Event.Id.Value,
                IsAnnounced = true,
                Sort = c.Date,
            });
        }
        // Flagships due inside the horizon that are not announced yet: the month is public (every year).
        foreach (var p in w.Promotions.Values.Where(p => !p.Defunct && p.Id != w.PlayerPromotion).OrderByDescending(p => p.Prestige.Value))
        {
            if (seen.Contains(p.Id.Value) || SignatureEvents.MonthOf(p) is not { } month || SignatureEvents.NameOf(p) is not { } name) continue;
            for (int k = 0; k < MonthsAhead; k++)
            {
                var m = FirstMonth.AddMonths(k);
                if (m.Month != month) continue;
                if (k == 0 && w.Events.Values.Any(e => e.Promotion == p.Id && e.Date.Year == m.Year && SignatureEvents.IsSignature(w, e))) break;
                Flagships.Add(new FlagshipModel
                {
                    Name = name,
                    When = $"Every {m:MMMM}",
                    Line = "Not announced yet",
                    Where = FighterRow.Pretty(p.BaseRegion.ToString()),
                    Promo = p,
                    EventId = 0,
                    IsAnnounced = false,
                    Sort = m,
                });
                break;
            }
        }
        var ordered = Flagships.OrderBy(f => f.Sort).ToList();
        Flagships.Clear();
        foreach (var f in ordered) Flagships.Add(f);
        HasFlagships = Flagships.Count > 0;
    }

    // --- Words ------------------------------------------------------------------------

    private static string When(int days) => days switch
    {
        < 0 => $"{-days} days ago",
        0 => "today",
        1 => "tomorrow",
        < 14 => $"in {days} days",
        < 63 => $"in {days / 7} weeks",
        _ => $"in {Math.Round(days / 30.4)} months",
    };

    private static string Join(string? a, string b) => string.IsNullOrWhiteSpace(a) ? b : $"{a}  ·  {b}";

    /// <summary>"$1.2M", "$340k", "$9,500".</summary>
    public static string Compact(Money m)
    {
        decimal a = Math.Abs(m.Amount);
        string s = a >= 1_000_000m ? $"${a / 1_000_000m:0.#}M" : a >= 10_000m ? $"${a / 1000m:0}k" : $"${a:N0}";
        return m.Amount < 0 ? "-" + s : s;
    }

    private static string Signed(Money m) => m.Amount >= 0 ? "+" + Compact(m) : "−" + Compact(new Money(-m.Amount));
}

// --- Row models (rebuilt on every refresh; hold the ids, not these) ----------------------

public enum DayChipKind { Mine, Clash, Marker, Rival }

/// <summary>One line inside a calendar day.</summary>
public sealed class DayChip
{
    public DayChip(DayChipKind kind, string text, string sub, CardTone tone, Promotion? promo)
    {
        Kind = kind; Text = text; Sub = sub; Tone = tone; _promo = promo;
    }
    private readonly Promotion? _promo;
    private ImageSource? _logo;
    public DayChipKind Kind { get; }
    public string Text { get; }
    public string Sub { get; }
    public bool HasSub => Sub.Length > 0;
    public CardTone Tone { get; }
    public string Icon { get; init; } = "";
    public bool IsSignature { get; init; }
    public bool IsTitle { get; init; }
    public ImageSource? Logo => _promo is null ? null : _logo ??= LogoFactory.ForPromotion(_promo);
    /// <summary>The promotion's poster colour: the stripe that tells the world's cards apart at a glance.</summary>
    public Brush OrgBrush => _orgBrush ??= MakeBrush();
    private Brush? _orgBrush;
    private Brush MakeBrush()
    {
        var b = new SolidColorBrush(PosterFactory.AccentFor(_promo));
        b.Freeze();
        return b;
    }
}

/// <summary>One square of the month grid.</summary>
public sealed class DayCell : ObservableObject
{
    public DayCell(DateOnly date) { Date = date; Day = date.Day.ToString(); }
    public DateOnly Date { get; }
    public string Day { get; }
    public bool InMonth { get; init; }
    public bool IsToday { get; init; }
    public bool IsPast { get; init; }
    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }
    public bool HasMine { get; set; }
    public List<DayChip> Chips { get; } = new();
    public string More { get; set; } = "";
    public bool HasMore => More.Length > 0;
    public string? Tooltip { get; set; }
    /// <summary>What a screen reader / keyboard user hears for the day.</summary>
    public string Spoken => $"{Date:dddd d MMMM}{(Chips.Count == 0 ? ", nothing on" : $", {Chips.Count + (HasMore ? 1 : 0)} things on")}";
}

/// <summary>A week heading in the agenda.</summary>
public sealed record AgendaWeek(string Title, string Range);

/// <summary>One of your cards: faces, poster, the sim's projection, the risks.</summary>
public sealed class MyCardModel
{
    private readonly Event _ev;
    private readonly World _w;
    private ImageSource? _poster;
    public MyCardModel(Event ev, World w) { _ev = ev; _w = w; Id = ev.Id.Value; }

    public int Id { get; }
    public required string Name { get; init; }
    public DateOnly DateValue { get; init; }
    public required string DateLine { get; init; }
    public required string Venue { get; init; }
    public required string MainEvent { get; init; }
    public string MainEventShort { get; init; } = "";
    public bool HasMainEvent { get; init; }
    public int RedId { get; init; }
    public int BlueId { get; init; }
    public LazyFace? RedFace { get; init; }
    public LazyFace? BlueFace { get; init; }
    public bool IsTitle { get; init; }
    public string Bouts { get; init; } = "";
    public string HouseLine { get; init; } = "";
    public string NetLine { get; init; } = "";
    public CardTone NetTone { get; init; }
    public string StatusText { get; init; } = "";
    public CardTone StatusTone { get; init; }
    public List<string> Risks { get; init; } = new();
    public bool HasRisks => Risks.Count > 0;
    public string MoreRisks { get; init; } = "";
    public bool HasMoreRisks => MoreRisks.Length > 0;
    public string EndingLine { get; init; } = "";
    public bool HasEnding => EndingLine.Length > 0;
    /// <summary>The card's poster, drawn on first use only.</summary>
    public ImageSource? Poster => _ev.Card.Count == 0 ? null : _poster ??= PosterFactory.For(_w, _ev);
    public bool HasPoster => _ev.Card.Count > 0;
}

/// <summary>Another promotion's announced card: public facts only.</summary>
public sealed class RivalCardModel
{
    private ImageSource? _logo;
    public int Id { get; init; }
    public int PromotionId { get; init; }
    public required string Abbreviation { get; init; }
    public required Promotion Promo { get; init; }
    public ImageSource Logo => _logo ??= LogoFactory.ForPromotion(Promo);
    public required string Name { get; init; }
    public DateOnly DateValue { get; init; }
    public required string DateLine { get; init; }
    public Region Region { get; init; }
    public required string Venue { get; init; }
    public string MainEventShort { get; init; } = "";
    public string MainEventLine => MainEventShort.Length > 0 ? MainEventShort : "Main event to come";
    public string CoMain { get; init; } = "";
    public bool HasCoMain => CoMain.Length > 0;
    public bool IsTitle { get; init; }
    public bool IsSignature { get; init; }
    public string Announced { get; init; } = "";
    /// <summary>The box office counts this card against one of your houses.</summary>
    public bool Charged { get; init; }
    public string ChargedLine { get; init; } = "";
    /// <summary>"2 days before", set when read against a picked date.</summary>
    public string Distance { get; init; } = "";
    public bool HasDistance => Distance.Length > 0;

    public RivalCardModel WithDistance(DateOnly d)
    {
        int gap = DateValue.DayNumber - d.DayNumber;
        string dist = gap == 0 ? "Same night" : gap > 0 ? $"{gap} day{(gap == 1 ? "" : "s")} after" : $"{-gap} day{(gap == -1 ? "" : "s")} before";
        return new RivalCardModel
        {
            Id = Id, PromotionId = PromotionId, Abbreviation = Abbreviation, Promo = Promo, Name = Name, DateValue = DateValue,
            DateLine = DateLine, Region = Region, Venue = Venue, MainEventShort = MainEventShort, CoMain = CoMain, IsTitle = IsTitle,
            IsSignature = IsSignature, Announced = Announced, Charged = Charged, ChargedLine = ChargedLine,
            Distance = $"{dist}  ·  {DateLine}",
        };
    }
}

/// <summary>A date on your timeline: a deadline, a return, a lapse.</summary>
public sealed class PlannerMarker
{
    public DateOnly Date { get; init; }
    public string DateLine => Date.ToString("ddd d MMM");
    /// <summary>An Icon.* name.</summary>
    public required string Icon { get; init; }
    /// <summary>What fits in a calendar square.</summary>
    public required string Short { get; init; }
    public required string Text { get; init; }
    public string Detail { get; init; } = "";
    public CardTone Tone { get; init; }
    public int? FighterId { get; init; }
    public LazyFace? Face { get; init; }
    public bool HasFace => Face is not null;
    public ICommand? Open { get; init; }
    public object? OpenParameter { get; init; }
    public string OpenText { get; init; } = "";
}

/// <summary>A flagship night: announced, or due in its month.</summary>
public sealed class FlagshipModel
{
    private ImageSource? _logo;
    public required string Name { get; init; }
    public required string When { get; init; }
    public required string Line { get; init; }
    public required string Where { get; init; }
    public required Promotion Promo { get; init; }
    public ImageSource Logo => _logo ??= LogoFactory.ForPromotion(Promo);
    public int EventId { get; init; }
    public bool IsAnnounced { get; init; }
    public DateOnly Sort { get; init; }
}
