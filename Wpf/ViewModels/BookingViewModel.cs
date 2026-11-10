using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
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
        Divisions = new ObservableCollection<DivisionPick>();   // filled per-world: only divisions you have fighters in
        _rounds = 3;
    }

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
            if (!Set(ref _selectedEvent, value)) return;
            // Sync the window selector to the event WITHOUT writing back through the setter.
            _mainCard = MainCardOptions.FirstOrDefault(o => o.Value == (value?.Model.MainCardFights ?? 0)) ?? MainCardOptions[0];
            OnPropertyChanged(nameof(SelectedMainCard));
            OnPropertyChanged(nameof(HasSelectedEvent)); OnPropertyChanged(nameof(SelectedEventTitle));
            RefreshBouts(); RefreshRedOptions();
        }
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

    private DivisionPick? _division;
    public DivisionPick? SelectedDivision { get => _division; set { if (Set(ref _division, value)) RefreshRedOptions(); } }

    private FighterPick? _red;
    public FighterPick? SelectedRed { get => _red; set { if (Set(ref _red, value)) RefreshBlueOptions(); } }

    private FighterPick? _blue;
    public FighterPick? SelectedBlue { get => _blue; set { if (Set(ref _blue, value)) UpdateAppraisal(); } }

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
        _world = world;
        NewEventDate = world.CurrentDate.ToDateTime(TimeOnly.MinValue).AddDays(21);

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
            if (!IsTitleBout) SelectedRounds = promo.Rules.StandardRounds;
        }

        RefreshDivisions();
        RefreshCities();
        RefreshUpcoming();
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
        Divisions.Clear();
        foreach (var d in mine) Divisions.Add(d);
        if (Divisions.Count > 0 && (SelectedDivision is not { } sel || !Divisions.Contains(sel)))
            SelectedDivision = Divisions[0];
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
        _booking.CancelEvent(_world, SelectedEvent.Model);
        RefreshUpcoming();
        SelectedEvent = UpcomingEvents.FirstOrDefault();
        Ok("Event cancelled.");
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
        if (_world is null || SelectedEvent is null) { Projection = ""; Poster = null; Warnings.Clear(); OnPropertyChanged(nameof(HasWarnings)); return; }
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
                        Warnings.Add($"⚠ {partner}: {Core.Sim.Finance.DealCompliance.Describe(req)}");
                }
        }
        OnPropertyChanged(nameof(HasWarnings));
    }

    private ImageSource? _poster;
    /// <summary>The auto-generated fight poster for the selected event (live preview).</summary>
    public ImageSource? Poster { get => _poster; private set => Set(ref _poster, value); }

    private string _projection = "";
    /// <summary>Projected box office for the card being built ("" = no event/bouts).</summary>
    public string Projection { get => _projection; private set => Set(ref _projection, value); }

    private bool _projectionInTheRed;
    public bool ProjectionInTheRed { get => _projectionInTheRed; private set => Set(ref _projectionInTheRed, value); }

    private void RefreshProjection()
    {
        if (_world is null || SelectedEvent is null || SelectedEvent.Model.Card.Count == 0)
        {
            Projection = "";
            return;
        }

        var est = Core.Sim.Booking.Financials.Estimate(_world, SelectedEvent.Model);
        var income = est.GateRevenue + est.PayPerViewRevenue;
        var costs = est.ProductionCost + est.FighterPayroll;
        var net = income - costs;
        ProjectionInTheRed = net.Amount < 0;
        Projection = $"Projected: {est.Attendance:N0} tickets  ·  gate {est.GateRevenue}"
                     + (est.PayPerViewRevenue.Amount > 0 ? $"  +  PPV {est.PayPerViewRevenue}" : "")
                     + $"  ·  production {est.ProductionCost}  ·  payroll ≈{est.FighterPayroll}  →  net {net}";
    }

    private void RefreshRedOptions()
    {
        RedOptions.Clear();
        SelectedRed = null;
        if (_world is null || SelectedEvent is null || SelectedDivision is not { } d) return;
        var pool = _booking.AvailableRoster(_world, SelectedEvent.Model, d.WeightClass, d.Sex);
        foreach (var f in _advisor.ByStanding(_world, pool))
            RedOptions.Add(new FighterPick(f, _world, _advisor.StandingOf(_world, f.Id).Label));
    }

    private void RefreshBlueOptions()
    {
        BlueOptions.Clear();
        SelectedBlue = null;
        if (_world is not null && SelectedEvent is not null && SelectedRed is not null)
        {
            var pool = _booking.EligibleOpponents(_world, SelectedEvent.Model, SelectedRed.Id);
            foreach (var f in _advisor.ByStanding(_world, pool))
                BlueOptions.Add(new FighterPick(f, _world, _advisor.StandingOf(_world, f.Id).Label));
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
        Num("Punch power", r.Striking.PunchPower, b.Striking.PunchPower);
        Num("Striking def", r.Striking.StrikingDefense, b.Striking.StrikingDefense);
        Num("Takedown def", r.Grappling.TakedownDefense, b.Grappling.TakedownDefense);
        Num("Submissions", r.Grappling.SubmissionOffense, b.Grappling.SubmissionOffense);
        Num("Cardio", r.Physical.Cardio, b.Physical.Cardio);
        Num("Chin", r.Physical.Chin, b.Physical.Chin);
        Num("Fight IQ", r.Mental.FightIQ, b.Mental.FightIQ);
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
