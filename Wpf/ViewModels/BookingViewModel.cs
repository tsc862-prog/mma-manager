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

/// <summary>A fighter as a pick in a booking combo box.</summary>
public sealed class FighterPick
{
    public FighterPick(Fighter f, World w, string rank)
    {
        Id = f.Id;
        Rank = rank;
        Display = $"{f.FullName}   Sct {FighterRow.Scouted(w, f)}   ({f.Record})";
        Portrait = Portraits.For(f, w);
    }
    public FighterId Id { get; }
    public string Rank { get; }        // "C", "#3", or "NR"
    public string Display { get; }
    public ImageSource Portrait { get; }
}

/// <summary>A main-card size choice ("Auto" fits whatever the media deal's window demands).</summary>
public sealed record MainCardPick(int Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>One booked bout in the card editor.</summary>
public sealed class BoutRow
{
    public BoutRow(Fight f, World w, MatchmakingAdvisor advisor, string slot, bool isPrelim = false)
    {
        Id = f.Id;
        Slot = slot;
        IsPrelim = isPrelim;
        IsMainEvent = slot == "Main event";
        RedId = f.RedCorner.Value;
        BlueId = f.BlueCorner.Value;
        var red = w.Get(f.RedCorner);
        var blue = w.Get(f.BlueCorner);
        RedName = red?.FullName ?? "?";
        BlueName = blue?.FullName ?? "?";
        RedPortrait = red is null ? null : Portraits.For(red, w);
        BluePortrait = blue is null ? null : Portraits.For(blue, w);
        Division = FighterRow.Pretty(f.WeightClass.ToString());
        Rounds = $"{f.ScheduledRounds} rd";
        Stakes = f.IsTitleFight ? "★ Title" : advisor.Appraise(w, f.RedCorner, f.BlueCorner).StakesTag;
    }
    public FightId Id { get; }
    public string Slot { get; }
    public bool IsMainEvent { get; }
    /// <summary>Corner fighter ids, for opening profiles straight off the card sheet.</summary>
    public int RedId { get; }
    public int BlueId { get; }
    /// <summary>Below the broadcast window — cheaper exposure, less fame for the fighters.</summary>
    public bool IsPrelim { get; }
    public string RedName { get; }
    public string BlueName { get; }
    public ImageSource? RedPortrait { get; }
    public ImageSource? BluePortrait { get; }
    public string Division { get; }
    public string Rounds { get; }
    public string Stakes { get; }
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

/// <summary>A player-promotion scheduled event in the upcoming list.</summary>
public sealed class EventBookingRow : ObservableObject
{
    public EventBookingRow(Event e)
    {
        Model = e;
        Name = e.Name;
        Date = e.Date.ToString("ddd d MMM yyyy");
        _bouts = e.Card.Count;
    }
    public Event Model { get; }
    public string Name { get; }
    public string Date { get; }

    private int _bouts;
    public int Bouts { get => _bouts; private set => Set(ref _bouts, value); }

    /// <summary>Re-read the card size in place (no collection rebuild, so the list selection survives).</summary>
    public void SyncBoutCount() => Bouts = Model.Card.Count;
}

/// <summary>
/// Drives the Book Event screen: create/cancel your promotion's scheduled cards
/// and add bouts from your roster (validated through <see cref="PlayerBooking"/>).
/// Events run automatically when you Advance past their date.
/// </summary>
public sealed class BookingViewModel : ObservableObject
{
    private readonly PlayerBooking _booking = new();
    private readonly MatchmakingAdvisor _advisor = new();
    private World? _world;

    public BookingViewModel()
    {
        NewEventCommand = new RelayCommand(NewEvent, () => _world is not null);
        CancelEventCommand = new RelayCommand(CancelEvent, () => SelectedEvent is not null);
        AddBoutCommand = new RelayCommand(AddBout, () => SelectedEvent is not null && SelectedRed is not null && SelectedBlue is not null);
        RemoveBoutCommand = new RelayCommand(RemoveBout, () => SelectedBout is not null && SelectedEvent is not null);
        MoveBoutUpCommand = new RelayCommand(() => MoveBout(+1), () => SelectedBout is not null && SelectedEvent is not null);
        MoveBoutDownCommand = new RelayCommand(() => MoveBout(-1), () => SelectedBout is not null && SelectedEvent is not null);
        SuggestOpponentCommand = new RelayCommand(SuggestOpponent, () => SelectedEvent is not null && SelectedRed is not null && BlueOptions.Count > 0);
        AddLocalBoutCommand = new RelayCommand(AddLocalBout, () => SelectedEvent is not null && SelectedRed is not null);
        // Watching is a fight-night privilege — a card can't be run before its date.
        RunLiveCommand = new RelayCommand(RunLive,
            () => SelectedEvent is not null && Bouts.Count > 0
                  && _world is not null && SelectedEvent.Model.Date <= _world.CurrentDate);
        OpenContractsCommand = new RelayCommand(() => OpenContracts?.Invoke());
        Divisions = new ObservableCollection<DivisionPick>();   // filled per-world: only divisions you have fighters in
        _rounds = 3;
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
    public ICommand RemoveBoutCommand { get; }
    public ICommand MoveBoutUpCommand { get; }
    public ICommand MoveBoutDownCommand { get; }
    public ICommand SuggestOpponentCommand { get; }
    public ICommand AddLocalBoutCommand { get; }
    public ICommand RunLiveCommand { get; }

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
    public ObservableCollection<BoutRow> Bouts { get; } = new();
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
    public string Status { get => _status; private set => Set(ref _status, value); }

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
        OnPropertyChanged(nameof(HasSelectedEvent)); OnPropertyChanged(nameof(SelectedEventTitle));
        RefreshBouts(); RefreshRedOptions();
    }

    /// <summary>
    /// True while a refresh rebuilds the lists: selection setters only store,
    /// so clearing a list (which pushes a null selection back through the
    /// bindings) can't cascade into wiping the matchup being built. The
    /// selection is put back by id afterwards (see <see cref="Attach"/>).
    /// </summary>
    private bool _quiet;

    /// <summary>What to tell the player on an empty card column ("" = the card has bouts).</summary>
    public string CardHint =>
        _world is null ? ""
        : _selectedEvent is null
            ? (UpcomingEvents.Count == 0
                ? "No cards planned yet. Pick a date, card tier and host city under PLAN A NEW CARD, then press + Create Event."
                : "Pick one of your upcoming events on the left to work on its card.")
        : Bouts.Count == 0
            ? (HasRoster
                ? "No bouts on this card yet. Choose a division and both corners under BUILD A MATCHUP, then press Add Bout."
                : "No bouts on this card yet — and no fighters to book. Sign fighters on the Contracts screen first.")
            : "";
    public bool HasCardHint => CardHint.Length > 0;

    /// <summary>The org has at least one active fighter to book (a founded promotion starts with none).</summary>
    public bool HasRoster => Divisions.Count > 0;

    private void RaiseHints()
    {
        OnPropertyChanged(nameof(CardHint));
        OnPropertyChanged(nameof(HasCardHint));
        OnPropertyChanged(nameof(HasRoster));
    }
    public bool HasSelectedEvent => _selectedEvent is not null;
    public string SelectedEventTitle => _selectedEvent is null
        ? "No event selected"
        : $"{_selectedEvent.Name} — {_selectedEvent.Date}"
          + (_world is not null && _selectedEvent.Model.VenueId is { } vid && _world.Get(vid) is { } v && _world.Get(v.City) is { } c
              ? $"  ·  {v.Name}, {c.Name}" : "");

    private BoutRow? _selectedBout;
    public BoutRow? SelectedBout { get => _selectedBout; set => Set(ref _selectedBout, value); }

    // --- The broadcast window: how much of the card is the main card ---------
    public ObservableCollection<MainCardPick> MainCardOptions { get; } = new()
    {
        new(0, "Auto (fit the deal)"), new(3, "3 fights"), new(4, "4 fights"),
        new(5, "5 fights"), new(6, "6 fights"), new(7, "7 fights"), new(8, "8 fights"),
    };

    private MainCardPick? _mainCard;
    public MainCardPick? SelectedMainCard
    {
        get => _mainCard;
        set
        {
            if (!Set(ref _mainCard, value) || value is null || SelectedEvent is null) return;
            SelectedEvent.Model.MainCardFights = value.Value;
            RefreshBouts();
        }
    }

    /// <summary>Contract clauses (sponsors + media) the card as booked would break.</summary>
    public ObservableCollection<string> Warnings { get; } = new();
    public bool HasWarnings => Warnings.Count > 0;

    /// <summary>One line for the warnings strip: how many terms the card breaks, and whose.</summary>
    public string WarningSummary { get => _warningSummary; private set => Set(ref _warningSummary, value); }
    private string _warningSummary = "";

    private bool _showAllWarnings;
    /// <summary>The warnings strip is folded to one line until the player opens it.</summary>
    public bool ShowAllWarnings { get => _showAllWarnings; set => Set(ref _showAllWarnings, value); }

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
    /// <summary>The chosen red corner's fighter id (0 = none) — the walkout panel links to the profile.</summary>
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

        var today = world.CurrentDate.ToDateTime(TimeOnly.MinValue);
        var dropped = new List<string>();
        if (!sameWorld) NewEventDate = today.AddDays(21);
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
    private sealed record Picks(EventId? Event, FightId? Bout, DivisionPick? Division,
        FighterId? Red, string? RedName, FighterId? Blue, string? BlueName, int Rounds, bool Title);

    private Picks Snapshot() => new(
        _selectedEvent?.Model.Id, _selectedBout?.Id, _division,
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
        _selectedEvent = ev;
        OnPropertyChanged(nameof(SelectedEvent));
        OnEventChanged();   // rebuilds the card, the poster and the red-corner list once

        if (keep is null) { RaiseHints(); return; }

        if (keep.Division is { } div && !Divisions.Contains(div))
            dropped.Add($"you no longer have anyone at {div}");

        if (keep.Bout is { } bid) SelectedBout = Bouts.FirstOrDefault(b => b.Id == bid);

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
    }

    private void NewEvent()
    {
        if (_world is null) return;
        var date = DateOnly.FromDateTime(NewEventDate);
        if (date <= _world.CurrentDate) { Err("Pick a date in the future."); return; }
        var ev = _booking.CreateEvent(_world, date, name: null, tier: SelectedTier, city: SelectedCity?.Id);
        RefreshUpcoming();
        SelectedEvent = UpcomingEvents.FirstOrDefault(r => r.Model.Id == ev.Id);
        Ok($"Created {ev.Name}. Add bouts below.");
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
        Ok($"{name} cancelled.");
    }

    /// <summary>
    /// What cancelling throws away, told from what the model already holds —
    /// no money is estimated here. <see cref="PlayerBooking.CancelEvent"/> drops
    /// the card and lets its local-scene opponents go; partners are only paid
    /// for a card that airs; a media deal's calendar clause is quoted as the
    /// contract states it, with the dates the player needs to judge it.
    /// </summary>
    /// <summary>Harness hook: the cancel question for the selected event, as the dialog would ask it.</summary>
    internal (string Heading, string Body, string? Detail)? DescribeCancel() =>
        _world is null || SelectedEvent is null ? null
        : CancelCost(SelectedEvent.Model) is var (b, d) ? ($"Cancel {SelectedEvent.Model.Name}?", b, d) : null;

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
        if (_booking.AddBout(_world, SelectedEvent.Model, SelectedRed.Id, SelectedBlue.Id, SelectedRounds, IsTitleBout, out var err))
        {
            IsTitleBout = false;
            RefreshBouts();
            RefreshRedOptions();
            RefreshUpcomingCounts();
            Ok("Bout added.");
        }
        else Err(err ?? "Couldn't add that bout.");
    }

    private void RemoveBout()
    {
        if (SelectedEvent is null || SelectedBout is null) return;
        _booking.RemoveBout(_world, SelectedEvent.Model, SelectedBout.Id);
        RefreshBouts();
        RefreshRedOptions();
        RefreshUpcomingCounts();
    }

    /// <summary>
    /// Rebill the selected fight: +1 moves it UP the bill (toward the main event —
    /// which is the END of the sim's card list), -1 back down toward the prelims.
    /// The booker decides what headlines; slots and the poster follow the order.
    /// </summary>
    private void MoveBout(int direction)
    {
        if (SelectedEvent is null || SelectedBout is null) return;
        var card = SelectedEvent.Model.Card;
        int i = card.FindIndex(f => f.Id == SelectedBout.Id);
        int j = i + direction;
        if (i < 0 || j < 0 || j >= card.Count) return;

        (card[i], card[j]) = (card[j], card[i]);
        var keep = SelectedBout.Id;
        RefreshBouts();
        SelectedBout = Bouts.FirstOrDefault(b => b.Id == keep);
        Ok(direction > 0 ? "Moved up the bill." : "Moved down the bill.");
    }

    private void AddLocalBout()
    {
        if (_world is null || SelectedEvent is null || SelectedRed is null) return;
        if (_booking.AddLocalBout(_world, SelectedEvent.Model, SelectedRed.Id, SelectedLocalStyle, SelectedRounds, out var err))
        {
            RefreshBouts();
            RefreshRedOptions();
            RefreshUpcomingCounts();
            Ok("Local fight booked — an easy night, if your fighter shows up ready.");
        }
        else Err(err ?? "Couldn't book the local fight.");
    }

    // --- refresh helpers ----------------------------------------------------
    private void RefreshUpcoming()
    {
        UpcomingEvents.Clear();
        if (_world is null) return;
        foreach (var e in _booking.UpcomingEvents(_world)) UpcomingEvents.Add(new EventBookingRow(e));
        if (SelectedEvent is not null && UpcomingEvents.All(r => r.Model.Id != SelectedEvent.Model.Id))
            SelectedEvent = UpcomingEvents.FirstOrDefault();
        RaiseHints();
    }

    private void RefreshUpcomingCounts()
    {
        // Update the count on the existing row in place — rebuilding the collection
        // would drop the ListBox selection and null out SelectedEvent mid-edit.
        SelectedEvent?.SyncBoutCount();
    }

    private void RefreshBouts()
    {
        Bouts.Clear();
        if (_world is null || SelectedEvent is null)
        {
            Projection = ""; ProjectionDetail = ""; Poster = null;
            Warnings.Clear(); WarningSummary = ""; OnPropertyChanged(nameof(HasWarnings));
            RaiseHints();
            return;
        }
        // Billing order: the main event (last on the sim's card) tops the sheet.
        // Fights below the broadcast window bill as prelims.
        var ev = SelectedEvent.Model;
        var card = ev.Card;
        int firstMain = card.Count - ev.EffectiveMainCard;
        for (int i = card.Count - 1; i >= 0; i--)
        {
            bool prelim = i < firstMain;
            Bouts.Add(new BoutRow(card[i], _world, _advisor,
                prelim ? $"Prelim {i + 1}" : CardBilling.Slot(i, card.Count), prelim));
        }
        RefreshWarnings();
        RefreshProjection();
        // The poster sells whatever the card currently is — it redraws live as
        // the main event (and its story) changes under the matchmaker's hands.
        Poster = Art.PosterFactory.For(_world, SelectedEvent.Model);
        RaiseHints();
    }

    /// <summary>
    /// Which contract clauses the card AS BOOKED would break — only the
    /// structural ones the booker controls (window size, stars, locals, title,
    /// rules); quality and attendance are judged on the night. An "Auto" window
    /// can't break a max clause (it fits itself), so that check is skipped.
    /// </summary>
    private void RefreshWarnings()
    {
        Warnings.Clear();
        var partners = new List<string>();
        if (_world?.Get(_world.PlayerPromotion) is { } promo && SelectedEvent is { } sel && sel.Model.Card.Count > 0)
        {
            var ev = sel.Model;
            bool auto = ev.MainCardFights == 0;
            var deals = promo.SponsorDeals.Select(d => (d.Partner, d.Requirements))
                .Concat(promo.MediaDeals
                    .Where(d => d.Kind != MediaKind.PpvCarrier || ev.Tier >= EventTier.Ppv)
                    .Select(d => (d.Partner, d.Requirements)));
            foreach (var (partner, reqs) in deals)
                foreach (var req in reqs)
                {
                    if (!Core.Sim.Finance.DealCompliance.IsStructural(req.Kind)) continue;
                    if (auto && req.Kind == DealRequirementKind.MaxMainCard) continue;
                    if (!Core.Sim.Finance.DealCompliance.Meets(_world, ev, req))
                    {
                        Warnings.Add($"{partner}: {Core.Sim.Finance.DealCompliance.Describe(req)}");
                        if (!partners.Contains(partner)) partners.Add(partner);
                    }
                }
        }
        // Folded to one line: the count and whose terms; the clauses open on demand.
        WarningSummary = Warnings.Count == 0 ? ""
            : $"⚠  This card breaks {Warnings.Count} partner term{(Warnings.Count == 1 ? "" : "s")} "
              + $"({string.Join(", ", partners)}) — missed terms cut that partner's pay and earn a strike.";
        OnPropertyChanged(nameof(HasWarnings));
    }

    private ImageSource? _poster;
    /// <summary>The auto-generated fight poster for the selected event (live preview).</summary>
    public ImageSource? Poster { get => _poster; private set => Set(ref _poster, value); }

    private string _projection = "";
    /// <summary>The sim's box-office estimate for the card being built ("" = no event/bouts).</summary>
    public string Projection { get => _projection; private set => Set(ref _projection, value); }

    private string _projectionDetail = "";
    /// <summary>What the estimate leaves out, said plainly so a media-funded card doesn't read as a loss.</summary>
    public string ProjectionDetail { get => _projectionDetail; private set => Set(ref _projectionDetail, value); }

    // The figures are Financials.Estimate's, shown as it returns them. The sim
    // has no planned-card estimate of sponsor fees, media rights or the merch
    // table (those are only computed when a card settles), so this line says
    // so instead of guessing them — and isn't coloured as a profit or a loss.
    private void RefreshProjection()
    {
        if (_world is null || SelectedEvent is null || SelectedEvent.Model.Card.Count == 0)
        {
            Projection = "";
            ProjectionDetail = "";
            return;
        }

        var est = Core.Sim.Booking.Financials.Estimate(_world, SelectedEvent.Model);
        var boxOffice = est.GateRevenue + est.PayPerViewRevenue - est.ProductionCost - est.FighterPayroll;
        Projection = $"{est.Attendance:N0} tickets  ·  gate {est.GateRevenue}"
                     + (est.PayPerViewRevenue.Amount > 0 ? $"  ·  PPV {est.PayPerViewRevenue}" : "")
                     + $"  ·  production {est.ProductionCost}  ·  payroll ≈{est.FighterPayroll}  →  box office {boxOffice}";
        ProjectionDetail = "Gate & PPV only — sponsor fees, media rights and the merch table are paid on fight night and are not in this figure.";
    }

    private void RefreshRedOptions()
    {
        RedOptions.Clear();
        SelectedRed = null;
        if (_world is null || SelectedEvent is null || SelectedDivision is not { } d) return;
        var pool = _booking.AvailableRoster(_world, SelectedEvent.Model, d.WeightClass, d.Sex);
        foreach (var (f, standing) in ScanOrder(pool))
            RedOptions.Add(new FighterPick(f, _world, standing.Label));
    }

    // The matchmaker's scan order — champ, #1, #2 … then the unranked — with the
    // unranked ordered by YOUR scout's read (the Sct number on each pick), never
    // the truth, and the id as the last word so the order is stable.
    private IEnumerable<(Fighter F, FighterStanding S)> ScanOrder(IEnumerable<Fighter> pool) => pool
        .Select(f => (F: f, S: _advisor.StandingOf(_world!, f.Id), Sct: FighterRow.Scouted(_world!, f)))
        .OrderBy(x => x.S.Kind switch { Standing.Champion => 0, Standing.Ranked => x.S.Rank ?? 999, _ => 1000 })
        .ThenByDescending(x => x.Sct)
        .ThenBy(x => x.F.Id.Value)
        .Select(x => (x.F, x.S));

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
        UpdateAppraisal();   // the red portrait shows the moment that corner is picked
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

        if (_world is null || red is null || blue is null)
        {
            HasAppraisal = false;
            Appraisal = "Pick both corners to preview the matchup.";
            Competitiveness = 0;
            Appeal = 0;
            HeadToHead = "";
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
        HeadToHead = HeadToHeadLine(red, blue);
        OnPropertyChanged(nameof(HasHeadToHead));
        BuildTape(red, blue);
    }

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
            ? $"last: {( _world.Get(lw)?.LastName ?? "?")} by {FighterRow.Pretty(last.Fight.Outcome.Method.ToString())}, {last.Date:MMM yyyy}"
            : $"last: {FighterRow.Pretty(last.Fight.Outcome.Method.ToString())}, {last.Date:MMM yyyy}";
        string series = meetings.Count == 1
            ? "They've fought before"
            : $"They've fought {meetings.Count} times ({red.LastName} {redWins}–{blueWins})";
        return $"⚔ {series} — {lastLine}." + (meetings.Count >= 2 ? " This one's a trilogy-maker." : " The rematch sells itself.");
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
