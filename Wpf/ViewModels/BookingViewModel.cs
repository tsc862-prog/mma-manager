using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Wpf.Art;
using MmaManager.Wpf.Mvvm;

namespace MmaManager.Wpf.ViewModels;

/// <summary>A host-city choice for a new event, with what the org can expect there.</summary>
public sealed class CityPick
{
    public CityPick(City c, World w, Promotion promo)
    {
        Id = c.Id;
        bool home = promo.HomeCity == c.Id;
        int pop = MmaManager.Core.Sim.Geography.Reputation.OrgInCity(promo, c.Id);
        string nation = w.Get(c.Country)?.Name ?? FighterRow.Pretty(c.Region.ToString());
        Display = $"{c.Name}, {nation}  ·  pop {pop}{(home ? "  ·  HQ" : "")}";
    }
    public CityId Id { get; }
    public string Display { get; }
    public override string ToString() => Display;
}

/// <summary>A fighter as a pick in the matchmaker's lists (the ladder, the corners).</summary>
public sealed class FighterPick
{
    public FighterPick(Fighter f, World w, string rank)
    {
        Id = f.Id;
        Rank = rank;
        Display = $"{f.FullName}   Sct {FighterRow.Scouted(w, f)}   ({f.Record})";
    }
    public FighterId Id { get; }
    public string Rank { get; }        // "C", "#3", or "NR"
    public string Display { get; }
}

/// <summary>A main-card size choice ("Auto" fits whatever the media deal's window demands).</summary>
public sealed record MainCardPick(int Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>One comparison line in the tale-of-the-tape panel.</summary>
public sealed class TaleRow
{
    public required string Label { get; init; }
    public required string Red { get; init; }
    public required string Blue { get; init; }
    /// <summary>-1 = red has the edge, 1 = blue, 0 = even/not comparable.</summary>
    public int Edge { get; init; }
    public bool RedBold => Edge < 0;
    public bool BlueBold => Edge > 0;
}

/// <summary>A player-promotion scheduled event: one tab across the top of the desk.</summary>
public sealed class EventBookingRow : ObservableObject
{
    public EventBookingRow(Event e, World w)
    {
        Model = e;
        Name = e.Name;
        Date = e.Date.ToString("ddd d MMM yyyy");
        int days = e.Date.DayNumber - w.CurrentDate.DayNumber;
        When = days <= 0 ? "tonight" : days < 14 ? $"in {days} days" : $"in {days / 7} weeks";
        _bouts = e.Card.Count;
    }
    public Event Model { get; }
    public string Name { get; }
    public string Date { get; }
    public string When { get; }

    private int _bouts;
    public int Bouts { get => _bouts; private set { if (Set(ref _bouts, value)) OnPropertyChanged(nameof(Line)); } }
    /// <summary>"Thu 29 Jan · in 4 weeks · 3 bouts".</summary>
    public string Line => $"{Model.Date:ddd d MMM} · {When} · {(Bouts == 0 ? "empty" : Bouts == 1 ? "1 bout" : $"{Bouts} bouts")}";

    /// <summary>Re-read the card size in place (no collection rebuild, so the tab selection survives).</summary>
    public void SyncBoutCount() => Bouts = Model.Card.Count;
}

/// <summary>Which surface the desk's right-hand rail is showing.</summary>
public enum DeskRail { Night, Picker, Room, Plan, Proposals }

/// <summary>How the desk shows the card: the bill (default), the dense roster table, or the poster.</summary>
public enum DeskView { Card, Table, Poster }

/// <summary>The picker's ways in: the matchmaker's suggestions, a division's ladder, or a local opponent.</summary>
public enum PickMode { Suggested, Division, Local }

/// <summary>
/// Drives the Card Desk (the Book Event app). The fight card is the screen: the
/// main event large, the rest stepping down, open slots offering the
/// matchmaker's suggestions. A rail on the right shows the night (house, money,
/// partner terms, risks), or the opponent picker, the room chooser, the
/// new-card form or a bulk proposal. Every change goes through
/// <see cref="PlayerBooking"/>; this class only asks the sim and draws what it
/// says. Picks are held by id across refreshes.
/// </summary>
public sealed partial class BookingViewModel : ObservableObject
{
    private readonly PlayerBooking _booking = new();
    private readonly MatchmakingAdvisor _advisor = new();
    private World? _world;

    public BookingViewModel()
    {
        NewEventCommand = new RelayCommand(NewEvent, () => _world is not null);
        CancelEventCommand = new RelayCommand(CancelEvent, () => SelectedEvent is not null);
        AddBoutCommand = new RelayCommand(AddBout, () => SelectedEvent is not null && SelectedRed is not null && SelectedBlue is not null);
        SuggestOpponentCommand = new RelayCommand(SuggestOpponent, () => SelectedEvent is not null && SelectedRed is not null && BlueOptions.Count > 0);
        AddLocalBoutCommand = new RelayCommand(AddLocalBout, () => SelectedEvent is not null && SelectedRed is not null);
        // Watching is a fight-night privilege — a card can't be run before its date.
        RunLiveCommand = new RelayCommand(RunLive,
            () => SelectedEvent is not null && SelectedEvent.Model.Card.Count > 0
                  && _world is not null && SelectedEvent.Model.Date <= _world.CurrentDate);
        OpenContractsCommand = new RelayCommand(() => OpenContracts?.Invoke());
        Divisions = new ObservableCollection<DivisionPick>();   // filled per-world: only divisions you have fighters in
        _rounds = 3;
        InitDesk();
    }

    /// <summary>Empty-roster shortcut: the shell opens the Contracts screen.</summary>
    public ICommand OpenContractsCommand { get; }
    public Action? OpenContracts { get; set; }

    /// <summary>
    /// Asks the player to confirm something destructive (heading, body, detail,
    /// confirm-button text) and returns true to go ahead. The shell wires a
    /// dialog in the interactive app; left null (headless harness runs, which
    /// must never open a dialog) the action goes ahead as scripted.
    /// </summary>
    public Func<string, string, string?, string, bool>? Confirm { get; set; }

    public ICommand NewEventCommand { get; }
    public ICommand CancelEventCommand { get; }
    public ICommand AddBoutCommand { get; }
    public ICommand SuggestOpponentCommand { get; }
    public ICommand AddLocalBoutCommand { get; }
    public ICommand RunLiveCommand { get; }

    /// <summary>Fight night's other door (sim the card): the shell's command, handed in.</summary>
    public ICommand? SimTonightCommand { get; set; }

    // --- Local fights: the small-org special ---------------------------------
    public PrimaryStyle[] LocalStyles { get; } = MmaManager.Core.Sim.Living.LocalScene.OfferedStyles;

    private PrimaryStyle _localStyle = PrimaryStyle.Striker;
    public PrimaryStyle SelectedLocalStyle { get => _localStyle; set => Set(ref _localStyle, value); }

    private bool _hasLocalOption;
    /// <summary>Local fights are on the menu only while the org is small (City/Regional reach).</summary>
    public bool HasLocalOption { get => _hasLocalOption; private set => Set(ref _hasLocalOption, value); }

    /// <summary>Raised when the player runs a card live — the shell runs it and opens the broadcast.</summary>
    public Action<Event>? OnRunLive { get; set; }

    private void RunLive()
    {
        if (SelectedEvent is { } e) OnRunLive?.Invoke(e.Model);
    }

    public ObservableCollection<EventBookingRow> UpcomingEvents { get; } = new();
    public ObservableCollection<CityPick> Cities { get; } = new();

    private CityPick? _selectedCity;
    public CityPick? SelectedCity { get => _selectedCity; set => Set(ref _selectedCity, value); }

    private string _reachLine = "";
    /// <summary>What the reach tier lets you book, and what the next tier opens.</summary>
    public string ReachLine { get => _reachLine; private set => Set(ref _reachLine, value); }
    /// <summary>The bill, top-billed: the main event first.</summary>
    public ObservableCollection<BillBout> Bouts { get; } = new();
    public ObservableCollection<DivisionPick> Divisions { get; }
    public ObservableCollection<FighterPick> RedOptions { get; } = new();
    public ObservableCollection<FighterPick> BlueOptions { get; } = new();
    public ObservableCollection<int> RoundsOptions { get; } = new() { 3, 5 };

    private DateTime _newEventDate = DateTime.Today.AddDays(21);
    public DateTime NewEventDate { get => _newEventDate; set => Set(ref _newEventDate, value); }

    /// <summary>Only the card tiers the org's level can stage (PPV at Continental, super-cards at International).</summary>
    public ObservableCollection<EventTier> Tiers { get; } = new() { EventTier.FightNight };
    private EventTier _selectedTier = EventTier.FightNight;
    public EventTier SelectedTier { get => _selectedTier; set => Set(ref _selectedTier, value); }

    private string _status = "";
    public string Status { get => _status; private set { if (Set(ref _status, value)) OnPropertyChanged(nameof(HasStatus)); } }
    public bool HasStatus => _status.Length > 0;

    private bool _statusIsError;
    /// <summary>Colors the status line: red for a refusal, green for a booking confirmation.</summary>
    public bool StatusIsError { get => _statusIsError; private set => Set(ref _statusIsError, value); }

    private void Ok(string message) { StatusIsError = false; Status = message; }
    private void Err(string message) { StatusIsError = true; Status = message; }

    private EventBookingRow? _selectedEvent;
    public EventBookingRow? SelectedEvent
    {
        get => _selectedEvent;
        set
        {
            if (!Set(ref _selectedEvent, value) || _quiet) return;
            OnEventChanged();
        }
    }

    private void OnEventChanged()
    {
        // Sync the window selector to the event WITHOUT writing back through the setter.
        _mainCard = MainCardOptions.FirstOrDefault(o => o.Value == (_selectedEvent?.Model.MainCardFights ?? 0)) ?? MainCardOptions[0];
        OnPropertyChanged(nameof(SelectedMainCard));
        OnPropertyChanged(nameof(HasSelectedEvent));
        OnPropertyChanged(nameof(SelectedEventTitle));
        // Another card: a half-built pick on the old one doesn't follow it.
        if (_rail is DeskRail.Picker or DeskRail.Room or DeskRail.Proposals) Rail = DeskRail.Night;
        RefreshBouts(); RefreshRedOptions();
    }

    /// <summary>
    /// True while a refresh rebuilds the lists: selection setters only store,
    /// so clearing a list (which pushes a null selection back through the
    /// bindings) can't cascade into wiping the matchup being built. The
    /// selection is put back by id afterwards (see <see cref="Attach"/>).
    /// </summary>
    private bool _quiet;

    /// <summary>The org has at least one active fighter to book (a founded promotion starts with none).</summary>
    public bool HasRoster => Divisions.Count > 0;

    private void RaiseHints()
    {
        OnPropertyChanged(nameof(HasRoster));
        OnPropertyChanged(nameof(HasNoEvents));
        OnPropertyChanged(nameof(HasSelectedEvent));
    }
    public bool HasSelectedEvent => _selectedEvent is not null;
    /// <summary>No card planned at all: the desk opens on the new-card form.</summary>
    public bool HasNoEvents => _world is not null && UpcomingEvents.Count == 0;
    public string SelectedEventTitle => _selectedEvent is null
        ? "No event selected"
        : $"{_selectedEvent.Name} — {_selectedEvent.Date}"
          + (_world is not null && _selectedEvent.Model.VenueId is { } vid && _world.Get(vid) is { } v && _world.Get(v.City) is { } c
              ? $"  ·  {v.Name}, {c.Name}" : "");

    // --- The broadcast window: how much of the card is the main card ---------
    public ObservableCollection<MainCardPick> MainCardOptions { get; } = new()
    {
        new(0, "Auto"), new(3, "3 fights"), new(4, "4 fights"),
        new(5, "5 fights"), new(6, "6 fights"), new(7, "7 fights"), new(8, "8 fights"),
    };

    private MainCardPick? _mainCard;
    public MainCardPick? SelectedMainCard
    {
        get => _mainCard;
        set
        {
            if (!Set(ref _mainCard, value) || value is null || SelectedEvent is null || _world is null) return;
            if (!_booking.SetMainCardSize(_world, SelectedEvent.Model, value.Value, out var err)) { Err(err ?? "Couldn't change the main card."); return; }
            RefreshBouts();
            Ok(value.Value == 0 ? "The main card fits the broadcast deals again." : $"The main card is now the top {value.Value} fights.");
        }
    }

    /// <summary>Contract clauses (sponsors + media) the card as it will run would miss, one line each.</summary>
    public ObservableCollection<string> Warnings { get; } = new();
    public bool HasWarnings => Warnings.Count > 0;

    private DivisionPick? _division;
    public DivisionPick? SelectedDivision { get => _division; set { if (Set(ref _division, value) && !_quiet) RefreshRedOptions(); } }

    private FighterPick? _red;
    public FighterPick? SelectedRed { get => _red; set { if (Set(ref _red, value) && !_quiet) RefreshBlueOptions(); } }

    private FighterPick? _blue;
    public FighterPick? SelectedBlue { get => _blue; set { if (Set(ref _blue, value) && !_quiet) UpdateAppraisal(); } }

    private int _rounds;
    public int SelectedRounds { get => _rounds; set => Set(ref _rounds, value); }

    private bool _isTitle;
    public bool IsTitleBout
    {
        get => _isTitle;
        set
        {
            if (!Set(ref _isTitle, value)) return;
            // A belt on the line books at the house's title length.
            if (value && _world?.Get(_world.PlayerPromotion) is { } promo)
                SelectedRounds = promo.Rules.TitleRounds;
        }
    }

    private bool _canBeTitle;
    /// <summary>The pairing could be for a belt (a champion is in it, or the belt is vacant).</summary>
    public bool CanBeTitle { get => _canBeTitle; private set => Set(ref _canBeTitle, value); }

    // --- Live matchup appraisal (shown once both corners are chosen) ---------
    private bool _hasAppraisal;
    public bool HasAppraisal { get => _hasAppraisal; private set => Set(ref _hasAppraisal, value); }

    private string _appraisal = "Pick both corners to preview the matchup.";
    public string Appraisal { get => _appraisal; private set => Set(ref _appraisal, value); }

    private string _headToHead = "";
    /// <summary>Prior meetings between the chosen corners ("" = first encounter).</summary>
    public string HeadToHead { get => _headToHead; private set => Set(ref _headToHead, value); }
    public bool HasHeadToHead => _headToHead.Length > 0;

    private int _competitiveness;
    public int Competitiveness { get => _competitiveness; private set => Set(ref _competitiveness, value); }

    private int _appeal;
    public int Appeal { get => _appeal; private set => Set(ref _appeal, value); }

    // --- Tale of the tape (the two chosen corners, side by side) -------------
    public ObservableCollection<TaleRow> Tape { get; } = new();

    private ImageSource? _redBig;
    public ImageSource? RedBig { get => _redBig; private set => Set(ref _redBig, value); }

    private ImageSource? _blueBig;
    public ImageSource? BlueBig { get => _blueBig; private set => Set(ref _blueBig, value); }

    private string _redHead = "";
    public string RedHead { get => _redHead; private set => Set(ref _redHead, value); }

    private string _blueHead = "";
    public string BlueHead { get => _blueHead; private set => Set(ref _blueHead, value); }

    private int _redCornerId;
    /// <summary>The chosen red corner's fighter id (0 = none) — the comparison links to the profile.</summary>
    public int RedCornerId { get => _redCornerId; private set => Set(ref _redCornerId, value); }

    private int _blueCornerId;
    public int BlueCornerId { get => _blueCornerId; private set => Set(ref _blueCornerId, value); }

    /// <summary>Select a specific upcoming event (used when an advance pauses on a due card).</summary>
    public void SelectEvent(EventId id) =>
        SelectedEvent = UpcomingEvents.FirstOrDefault(r => r.Model.Id == id);

    /// <summary>Point the screen at the current world (called on New Game / Load / Advance).</summary>
    public void Attach(World world)
    {
        // The same world coming back (a signing, a desk decision, an advance
        // anywhere in the app) must not wipe the card being planned or the
        // matchup being built: remember the picks by id, rebuild every list
        // quietly, then put back whatever is still valid and say what wasn't.
        // A different world (new game, load) starts the screen fresh.
        bool sameWorld = ReferenceEquals(world, _world);
        var keep = sameWorld ? Snapshot() : null;
        _world = world;
        _lastFought = null;

        var today = world.CurrentDate.ToDateTime(TimeOnly.MinValue);
        var dropped = new List<string>();
        if (!sameWorld)
        {
            NewEventDate = today.AddDays(21);
            Rail = DeskRail.Night;
            View = DeskView.Card;
            Status = "";
        }
        else if (DateOnly.FromDateTime(NewEventDate) <= world.CurrentDate)
        {
            NewEventDate = today.AddDays(21);
            dropped.Add($"the planned date had passed, so it moved to {NewEventDate:d MMM yyyy}");
        }

        _quiet = true;
        try
        {
            // Round options and default follow the org's ruleset.
            if (world.Get(world.PlayerPromotion) is { } promo)
            {
                var options = new[] { 2, 3, 5, promo.Rules.StandardRounds, promo.Rules.TitleRounds }
                    .Distinct().OrderBy(x => x).ToList();
                if (!options.SequenceEqual(RoundsOptions))
                {
                    RoundsOptions.Clear();
                    foreach (var o in options) RoundsOptions.Add(o);
                }
                if (!sameWorld) { IsTitleBout = false; SelectedRounds = promo.Rules.StandardRounds; }
            }

            RefreshDivisions();
            RefreshCities();
            RefreshUpcoming();
        }
        finally { _quiet = false; }

        Restore(keep, dropped);
    }

    /// <summary>The player's picks on this screen, by id — never the row objects, which every refresh rebuilds.</summary>
    private sealed record Picks(EventId? Event, DivisionPick? Division,
        FighterId? Red, string? RedName, FighterId? Blue, string? BlueName, int Rounds, bool Title);

    private Picks Snapshot() => new(
        _selectedEvent?.Model.Id, _division,
        _red?.Id, _red is null ? null : _world?.Get(_red.Id)?.FullName,
        _blue?.Id, _blue is null ? null : _world?.Get(_blue.Id)?.FullName,
        _rounds, _isTitle);

    private void Restore(Picks? keep, List<string> dropped)
    {
        // The event: the same card if it's still scheduled, else the next one up.
        var ev = keep?.Event is { } eid ? UpcomingEvents.FirstOrDefault(r => r.Model.Id == eid) : null;
        if (keep?.Event is { } gone && ev is null)
            dropped.Add($"{_world?.Get(gone)?.Name ?? "the card you had open"} is no longer on your schedule");
        ev ??= UpcomingEvents.FirstOrDefault();
        bool eventChanged = keep?.Event is { } was && ev?.Model.Id != was;
        _selectedEvent = ev;
        OnPropertyChanged(nameof(SelectedEvent));
        if (eventChanged && _rail is DeskRail.Picker or DeskRail.Room or DeskRail.Proposals) _rail = DeskRail.Night;
        _mainCard = MainCardOptions.FirstOrDefault(o => o.Value == (_selectedEvent?.Model.MainCardFights ?? 0)) ?? MainCardOptions[0];
        OnPropertyChanged(nameof(SelectedMainCard));
        OnPropertyChanged(nameof(SelectedEventTitle));
        RefreshBouts();        // the card, the night, the poster, once
        RefreshRedOptions();

        if (keep is null) { AfterRestore(); return; }

        if (keep.Division is { } div && !Divisions.Contains(div))
            dropped.Add($"you no longer have anyone at {div}");

        if (keep.Red is { } rid && SelectedEvent is not null)
        {
            var red = RedOptions.FirstOrDefault(o => o.Id == rid);
            if (red is not null) SelectedRed = red;
            else dropped.Add($"{keep.RedName ?? "your red corner"} is no longer available for this card");
        }
        if (keep.Blue is { } bid2 && SelectedRed is not null)
        {
            var blue = BlueOptions.FirstOrDefault(o => o.Id == bid2);
            if (blue is not null) SelectedBlue = blue;
            else dropped.Add($"{keep.BlueName ?? "your blue corner"} is no longer available for this card");
        }
        // The appraisal re-derives the title box from the pairing; the player's
        // own tick and round count go back on top of it (Add Bout still checks them).
        if (SelectedRed is not null && SelectedBlue is not null) IsTitleBout = keep.Title;
        if (RoundsOptions.Contains(keep.Rounds)) SelectedRounds = keep.Rounds;

        if (dropped.Count > 0)
            Err("Things changed since you last looked: " + string.Join("; ", dropped) + ".");
        AfterRestore();
    }

    private void AfterRestore()
    {
        // No card at all: the desk opens on the new-card form.
        if (UpcomingEvents.Count == 0 && _world is not null) Rail = DeskRail.Plan;
        else if (_rail == DeskRail.Plan && SelectedEvent is not null && _planFromEmpty) Rail = DeskRail.Night;
        RefreshRail();
        RaiseHints();
    }

    // Only offer weight classes the org actually has fighters in — a founded
    // promotion starts with none at all until the player hires.
    private void RefreshDivisions()
    {
        if (_world is null) return;
        // Only divisions (weight AND sex) you actually have fighters in.
        var mine = _world.RosterOf(_world.PlayerPromotion)
            .Where(f => f.Status != FighterStatus.Retired)
            .Select(f => new DivisionPick(f.WeightClass, f.Sex))
            .Distinct()
            .OrderBy(d => d.Sex)
            .ThenBy(d => d.WeightClass)
            .ToList();

        if (mine.SequenceEqual(Divisions)) return;   // don't reset the user's pick on every refresh
        var keep = SelectedDivision;                  // clearing the list pushes a null pick back through the binding
        Divisions.Clear();
        foreach (var d in mine) Divisions.Add(d);
        SelectedDivision = keep is { } k && Divisions.Contains(k) ? k : Divisions.FirstOrDefault();
        RaiseHints();
    }

    // Only the cities the promotion's reach tier can actually book — HQ first,
    // then by market. Growing prestige literally grows this list.
    private void RefreshCities()
    {
        if (_world?.Get(_world.PlayerPromotion) is not { } promo) return;
        var keep = SelectedCity?.Id;
        Cities.Clear();
        foreach (var c in MmaManager.Core.Sim.Org.OrgReach.Bookable(_world, promo))
            Cities.Add(new CityPick(c, _world, promo));
        SelectedCity = Cities.FirstOrDefault(c => c.Id == keep)
                       ?? Cities.FirstOrDefault(c => c.Id == promo.HomeCity)
                       ?? Cities.FirstOrDefault();

        // Card tiers the org can stage right now, plus the money milestones ahead.
        var maxTier = MmaManager.Core.Sim.Org.OrgPerks.MaxEventTier(promo);
        var tiers = Enum.GetValues<EventTier>().Where(t => t <= maxTier).ToList();
        if (!tiers.SequenceEqual(Tiers))
        {
            Tiers.Clear();
            foreach (var t in tiers) Tiers.Add(t);
        }
        if (SelectedTier > maxTier) SelectedTier = EventTier.FightNight;

        string business = maxTier switch
        {
            EventTier.FightNight => "  ·  PPV at 45",
            EventTier.Ppv => "  ·  super-cards at 70",
            _ => "",
        };
        ReachLine = MmaManager.Core.Sim.Org.OrgReach.Describe(_world, promo) + business;
        HasLocalOption = MmaManager.Core.Sim.Org.OrgSizing.SizeOf(promo) <= OrgSize.Regional;
        PlayerLogo = Art.LogoFactory.ForPromotion(promo);
    }

    private ImageSource? _playerLogo;
    /// <summary>Your promotion's logo, for the card's header.</summary>
    public ImageSource? PlayerLogo { get => _playerLogo; private set => Set(ref _playerLogo, value); }

    private void NewEvent()
    {
        if (_world is null) return;
        var date = DateOnly.FromDateTime(NewEventDate);
        if (date <= _world.CurrentDate) { Err("Pick a date in the future."); return; }
        var ev = _booking.CreateEvent(_world, date, name: null, tier: SelectedTier, city: SelectedCity?.Id);
        RefreshUpcoming();
        SelectedEvent = UpcomingEvents.FirstOrDefault(r => r.Model.Id == ev.Id);
        Rail = DeskRail.Night;
        View = DeskView.Card;
        Ok($"{ev.Name} is on the calendar for {ev.Date:ddd d MMM}. Start with the main event.");
    }

    private void CancelEvent()
    {
        if (_world is null || SelectedEvent is null) return;
        var ev = SelectedEvent.Model;
        if (Confirm is { } ask)
        {
            var (body, detail) = CancelCost(ev);
            if (!ask($"Cancel {ev.Name}?", body, detail, "Cancel the event")) { Ok($"{ev.Name} stays on the schedule."); return; }
        }
        string name = ev.Name;
        _booking.CancelEvent(_world, ev);
        RefreshUpcoming();
        SelectedEvent = UpcomingEvents.FirstOrDefault();
        if (UpcomingEvents.Count == 0) Rail = DeskRail.Plan;
        RaiseHints();
        Ok($"{name} cancelled.");
    }

    /// <summary>Harness hook: the cancel question for the selected event, as the dialog would ask it.</summary>
    internal (string Heading, string Body, string? Detail)? DescribeCancel() =>
        _world is null || SelectedEvent is null ? null
        : CancelCost(SelectedEvent.Model) is var (b, d) ? ($"Cancel {SelectedEvent.Model.Name}?", b, d) : null;

    /// <summary>
    /// What cancelling throws away, told from what the model already holds —
    /// no money is estimated here. <see cref="PlayerBooking.CancelEvent"/> drops
    /// the card and lets its local-scene opponents go; partners are only paid
    /// for a card that airs; a media deal's calendar clause is quoted as the
    /// contract states it, with the dates the player needs to judge it.
    /// </summary>
    private (string Body, string? Detail) CancelCost(Event ev)
    {
        var w = _world!;
        int bouts = ev.Card.Count;
        int titles = ev.Card.Count(f => f.IsTitleFight);
        int locals = ev.Card.SelectMany(f => new[] { f.RedCorner, f.BlueCorner })
            .Count(id => w.Get(id)?.LocalTalent == true);

        string body = $"{ev.Name} on {ev.Date:ddd d MMM yyyy} will be scrapped"
            + (bouts == 0 ? "." : $", with its {bouts} booked bout{(bouts == 1 ? "" : "s")}{(titles > 0 ? $" ({titles} for a title)" : "")}.")
            + (bouts > 0 ? " Your fighters go back to the roster unbooked." : "")
            + (locals > 0 ? $" The {locals} local-scene opponent{(locals == 1 ? "" : "s")} booked for it {(locals == 1 ? "is" : "are")} let go." : "")
            + " There is no cancellation fee, but the night earns nothing: no gate, no sponsor fees, no media rights."
            + " This can't be undone.";

        var lines = new List<string>();
        if (w.Get(w.PlayerPromotion) is { } promo)
        {
            var next = _booking.UpcomingEvents(w).FirstOrDefault(e => e.Id != ev.Id);
            foreach (var deal in promo.MediaDeals)
                foreach (var req in deal.Requirements.Where(r => r.Kind == DealRequirementKind.EventCadence))
                {
                    lines.Add($"{deal.Partner}'s deal asks for {Core.Sim.Finance.DealCompliance.Describe(req)}; "
                              + (deal.LastAiredOn is { } aired
                                  ? $"your last card for them aired {aired:d MMM yyyy}."
                                  : $"signed {deal.SignedOn:d MMM yyyy}, nothing aired for them yet."));
                }
            if (lines.Count > 0)
                lines.Add(next is null
                    ? "You have no other card scheduled."
                    : $"Your next other card is {next.Name} on {next.Date:d MMM yyyy}.");
        }
        return (body, lines.Count == 0 ? null : string.Join("\n", lines));
    }

    private void AddBout()
    {
        if (_world is null || SelectedEvent is null || SelectedRed is null || SelectedBlue is null) return;
        var ev = SelectedEvent.Model;
        string pairing = $"{_world.Get(SelectedRed.Id)?.LastName} vs {_world.Get(SelectedBlue.Id)?.LastName}";
        if (_booking.AddBout(_world, ev, SelectedRed.Id, SelectedBlue.Id, SelectedRounds, IsTitleBout, out var err))
        {
            var slot = _pickSlot ?? NextSlot(ev, ev.Card.Count - 1);
            Place(ev, ev.Card[0].Id, slot);
            IsTitleBout = false;
            if (_rail == DeskRail.Picker) Rail = DeskRail.Night;
            AfterCardChange();
            ClearPairing();
            Ok($"{pairing} is booked{SlotWords(slot)}.");
        }
        else Err(err ?? "Couldn't add that bout.");
    }

    private void AddLocalBout()
    {
        if (_world is null || SelectedEvent is null || SelectedRed is null) return;
        var ev = SelectedEvent.Model;
        string who = _world.Get(SelectedRed.Id)?.LastName ?? "Your fighter";
        if (_booking.AddLocalBout(_world, ev, SelectedRed.Id, SelectedLocalStyle, SelectedRounds, out var err))
        {
            var slot = _pickSlot ?? NextSlot(ev, ev.Card.Count - 1);
            Place(ev, ev.Card[0].Id, slot);
            if (_rail == DeskRail.Picker) Rail = DeskRail.Night;
            AfterCardChange();
            ClearPairing();
            Ok($"{who} meets a local {FighterRow.Pretty(SelectedLocalStyle.ToString()).ToLowerInvariant()}{SlotWords(slot)}: an easy night, if {who} shows up ready.");
        }
        else Err(err ?? "Couldn't book the local fight.");
    }

    // --- refresh helpers ----------------------------------------------------
    private void RefreshUpcoming()
    {
        UpcomingEvents.Clear();
        if (_world is null) return;
        foreach (var e in _booking.UpcomingEvents(_world)) UpcomingEvents.Add(new EventBookingRow(e, _world));
        if (SelectedEvent is not null && UpcomingEvents.All(r => r.Model.Id != SelectedEvent.Model.Id))
            SelectedEvent = UpcomingEvents.FirstOrDefault();
        RaiseHints();
    }

    private void RefreshRedOptions()
    {
        RedOptions.Clear();
        SelectedRed = null;
        if (_world is null || SelectedEvent is null || SelectedDivision is not { } d) { RebuildLadder(); return; }
        var pool = _booking.AvailableRoster(_world, SelectedEvent.Model, d.WeightClass, d.Sex);
        foreach (var (f, standing) in ScanOrder(pool))
            RedOptions.Add(new FighterPick(f, _world, standing.Label));
        RebuildLadder();
    }

    // The matchmaker's scan order — champ, #1, #2 … then the unranked by YOUR
    // scout's read (MatchmakingAdvisor.ByStanding: never the truth, id last).
    private IEnumerable<(Fighter F, FighterStanding S)> ScanOrder(IEnumerable<Fighter> pool) =>
        _advisor.ByStanding(_world!, pool).Select(f => (f, _advisor.StandingOf(_world!, f.Id)));

    private void RefreshBlueOptions()
    {
        BlueOptions.Clear();
        SelectedBlue = null;
        if (_world is not null && SelectedEvent is not null && SelectedRed is not null)
        {
            var pool = _booking.EligibleOpponents(_world, SelectedEvent.Model, SelectedRed.Id);
            foreach (var (f, standing) in ScanOrder(pool))
                BlueOptions.Add(new FighterPick(f, _world, standing.Label));
        }
        UpdateAppraisal();   // the red corner shows the moment it is picked
        RebuildLadder();
    }

    private void UpdateAppraisal()
    {
        var red = _world is null || SelectedRed is null ? null : _world.Get(SelectedRed.Id);
        var blue = _world is null || SelectedBlue is null ? null : _world.Get(SelectedBlue.Id);

        // Each corner's walkout shot appears as soon as that corner is chosen.
        RedBig = red is null ? null : Portraits.Body(red, _world!);
        BlueBig = blue is null ? null : Portraits.Body(blue, _world!);
        RedHead = red?.FullName ?? "";
        BlueHead = blue?.FullName ?? "";
        RedCornerId = red?.Id.Value ?? 0;
        BlueCornerId = blue?.Id.Value ?? 0;
        OnPropertyChanged(nameof(HasPairing));

        if (_world is null || red is null || blue is null)
        {
            HasAppraisal = false;
            Appraisal = "Pick both corners to preview the matchup.";
            Competitiveness = 0;
            Appeal = 0;
            HeadToHead = "";
            CanBeTitle = false;
            OnPropertyChanged(nameof(HasHeadToHead));
            Tape.Clear();
            return;
        }
        var a = _advisor.Appraise(_world, red.Id, blue.Id);
        Appraisal = a.Summary;
        Competitiveness = a.Competitiveness;
        Appeal = a.Appeal;
        HasAppraisal = true;
        IsTitleBout = a.TitleImplied;   // a belt can only be on the line when a champion is in the bout
        CanBeTitle = a.TitleImplied || VacantBelt(red);
        HeadToHead = HeadToHeadLine(red, blue);
        OnPropertyChanged(nameof(HasHeadToHead));
        BuildTape(red, blue);
    }

    // A vacant belt in this division: any two may fight for it (AddBout's rule).
    private bool VacantBelt(Fighter f) =>
        _world!.Get(_world.PlayerPromotion)?.Divisions.FirstOrDefault(d => d.Matches(f)) is { Title: { } tid }
        && _world.Get(tid) is { CurrentChampion: null };

    // "They've met before" — the booker should never rebook a fight by accident.
    // Cheap: intersect the two bout histories first; only when they share a
    // fight does the event scan run at all.
    private string HeadToHeadLine(Fighter red, Fighter blue)
    {
        if (_world is null) return "";
        var shared = red.BoutHistory.Intersect(blue.BoutHistory).ToHashSet();
        if (shared.Count == 0) return "";

        var meetings = _world.Events.Values
            .SelectMany(e => e.Card.Where(f => shared.Contains(f.Id)).Select(f => (Fight: f, e.Date)))
            .Where(x => x.Fight.Outcome is not null)
            .OrderBy(x => x.Date)
            .ToList();
        if (meetings.Count == 0) return "";

        int redWins = meetings.Count(x => x.Fight.Outcome!.Winner == red.Id && !x.Fight.OverturnedNc);
        int blueWins = meetings.Count(x => x.Fight.Outcome!.Winner == blue.Id && !x.Fight.OverturnedNc);
        var last = meetings[^1];
        string lastLine = last.Fight.Outcome!.Winner is { } lw
            ? $"last: {(_world.Get(lw)?.LastName ?? "?")} by {FighterRow.Pretty(last.Fight.Outcome.Method.ToString())}, {last.Date:MMM yyyy}"
            : $"last: {FighterRow.Pretty(last.Fight.Outcome.Method.ToString())}, {last.Date:MMM yyyy}";
        string series = meetings.Count == 1
            ? "They've fought before"
            : $"They've fought {meetings.Count} times ({red.LastName} {redWins}–{blueWins})";
        return $"{series} — {lastLine}." + (meetings.Count >= 2 ? " This one's a trilogy-maker." : " The rematch sells itself.");
    }

    private void BuildTape(Fighter r, Fighter b)
    {
        Tape.Clear();
        void Num(string label, int rv, int bv, bool higherIsBetter = true)
        {
            int edge = rv == bv ? 0 : (rv > bv ? -1 : 1);
            if (!higherIsBetter) edge = -edge;
            Tape.Add(new TaleRow { Label = label, Red = rv.ToString(), Blue = bv.ToString(), Edge = edge });
        }
        void Txt(string label, string rv, string bv) =>
            Tape.Add(new TaleRow { Label = label, Red = rv, Blue = bv, Edge = 0 });

        Txt("Rank", _advisor.StandingOf(_world!, r.Id).Label, _advisor.StandingOf(_world!, b.Id).Label);
        Txt("Record", r.Record.ToString(), b.Record.ToString());
        Num("Scouted", FighterRow.Scouted(_world!, r), FighterRow.Scouted(_world!, b));
        Num("Momentum", r.Momentum, b.Momentum);
        Txt("Age", r.AgeOn(_world!.CurrentDate).ToString(), b.AgeOn(_world!.CurrentDate).ToString());
        Num("Height (cm)", r.HeightCm, b.HeightCm);
        Num("Reach (cm)", r.ReachCm, b.ReachCm);
        Txt("Stance", r.Stance.ToString(), b.Stance.ToString());
        Txt("Style", FighterRow.Pretty(r.PrimaryStyle.ToString()), FighterRow.Pretty(b.PrimaryStyle.ToString()));

        // The skills: the SAME fogged bars the fighter profile draws (FighterRow's
        // scout report) — a number when it's known, a band word when it's rough,
        // "?" when it's unseen. The edge only bolds what the readouts can back up.
        var taped = TapeIndex(r, b);
        var rr = new FighterRow(r, _world!, taped);
        var br = new FighterRow(b, _world!, taped);
        void Scout(string label, string bar)
        {
            var x = rr.ScoutedRating(bar);
            var y = br.ScoutedRating(bar);
            Tape.Add(new TaleRow { Label = label, Red = x.ValueText, Blue = y.ValueText, Edge = RatingBar.ShownEdge(x, y) });
        }
        Scout("Punch power", "Punch Pow");
        Scout("Striking def", "Str Defense");
        Scout("Takedown def", "TD Defense");
        Scout("Submissions", "Sub Offense");
        Scout("Cardio", "Cardio");
        Scout("Chin", "Chin");
        Scout("Fight IQ", "Fight IQ");
    }

    // The fight index FighterRow's scout report reads, cut down to the two
    // corners' bouts since the save began (older bouts are record, not tape,
    // and FighterRow skips them anyway). One pass over the events, only when a
    // pairing is picked.
    private IReadOnlyDictionary<FightId, (Fight, Event)> TapeIndex(Fighter red, Fighter blue)
    {
        var wanted = red.BoutHistory.Concat(blue.BoutHistory).ToHashSet();
        var index = new Dictionary<FightId, (Fight, Event)>();
        foreach (var ev in _world!.Events.Values)
        {
            if (ev.Date < _world.StartDate) continue;
            foreach (var fight in ev.Card)
                if (wanted.Contains(fight.Id)) index[fight.Id] = (fight, ev);
        }
        return index;
    }

    private void SuggestOpponent()
    {
        if (_world is null || SelectedEvent is null || SelectedRed is null) return;
        var pool = _booking.EligibleOpponents(_world, SelectedEvent.Model, SelectedRed.Id);
        var pick = _advisor.SuggestOpponent(_world, SelectedRed.Id, pool);
        if (pick is { } id) SelectedBlue = BlueOptions.FirstOrDefault(o => o.Id == id);
        else Err("No eligible opponents to suggest.");
    }
}
