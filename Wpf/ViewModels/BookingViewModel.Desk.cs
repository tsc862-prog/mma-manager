using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Media;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Wpf.Mvvm;

namespace MmaManager.Wpf.ViewModels;

// The Card Desk half of the booking view model: the bill, the rail (the night,
// the picker, the room chooser, the new-card form, bulk proposals), the table
// and the poster. Every number is the sim's (PlayerBooking.Project,
// VenueOptions, PreviewPadding, Suggest, MatchmakingAdvisor); every change goes
// through PlayerBooking. Nothing here writes ev.Card or ev.MainCardFights.
public sealed partial class BookingViewModel
{
    private void InitDesk()
    {
        ShowRailCommand = new RelayCommand<string>(s => { if (Enum.TryParse<DeskRail>(s, out var r)) OpenRail(r); });
        CloseRailCommand = new RelayCommand(() => Rail = DeskRail.Night);
        PlanCardCommand = new RelayCommand(() => { _planFromEmpty = false; Rail = DeskRail.Plan; });
        ChangeRoomCommand = new RelayCommand(() => OpenRail(DeskRail.Room), () => SelectedEvent is not null);
        ChooseRoomCommand = new RelayCommand<VenueRow>(ChooseRoom);
        OpenSlotCommand = new RelayCommand<string>(s => { if (Enum.TryParse<BoutSlot>(s, out var slot)) OpenPicker(slot); });
        OpenNextSlotCommand = new RelayCommand(() => OpenPicker(_nextSlot));
        ReplaceCornerCommand = new RelayCommand<BillBout>(OpenReplace);
        ReplaceSideCommand = new RelayCommand<string>(s => { _replaceBlue = s == "Blue"; RefreshRail(); });
        ReplaceWithCommand = new RelayCommand<LadderEntry>(ReplaceWith);
        BookProposalCommand = new RelayCommand<ProposalCard>(BookProposal);
        PickLadderCommand = new RelayCommand<LadderEntry>(PickLadder);
        ClearRedCommand = new RelayCommand(() => { SelectedRed = null; });
        ClearBlueCommand = new RelayCommand(() => { SelectedBlue = null; });
        MoveUpCommand = new RelayCommand<BillBout>(b => Move(b, +1));
        MoveDownCommand = new RelayCommand<BillBout>(b => Move(b, -1));
        HeadlineCommand = new RelayCommand<BillBout>(Headline);
        RemoveBoutCommand = new RelayCommand<BillBout>(Remove);
        ProposeUndercardCommand = new RelayCommand(() => Propose(wholeCard: false), () => SelectedEvent is not null && HasRoster);
        ProposeWholeCardCommand = new RelayCommand(() => Propose(wholeCard: true), () => SelectedEvent is not null && HasRoster);
        BookProposedCommand = new RelayCommand(BookProposed, () => Proposed.Any(p => p.IsChecked));
        PairCommand = new RelayCommand<IReadOnlyList<int>>(Pair);
        OpenPosterCommand = new RelayCommand(() => View = DeskView.Poster);
    }

    public ICommand ShowRailCommand { get; private set; } = null!;
    public ICommand CloseRailCommand { get; private set; } = null!;
    public ICommand PlanCardCommand { get; private set; } = null!;
    public ICommand ChangeRoomCommand { get; private set; } = null!;
    public ICommand ChooseRoomCommand { get; private set; } = null!;
    public ICommand OpenSlotCommand { get; private set; } = null!;
    public ICommand OpenNextSlotCommand { get; private set; } = null!;
    public ICommand ReplaceCornerCommand { get; private set; } = null!;
    public ICommand ReplaceSideCommand { get; private set; } = null!;
    public ICommand ReplaceWithCommand { get; private set; } = null!;
    public ICommand BookProposalCommand { get; private set; } = null!;
    public ICommand PickLadderCommand { get; private set; } = null!;
    public ICommand ClearRedCommand { get; private set; } = null!;
    public ICommand ClearBlueCommand { get; private set; } = null!;
    public ICommand MoveUpCommand { get; private set; } = null!;
    public ICommand MoveDownCommand { get; private set; } = null!;
    public ICommand HeadlineCommand { get; private set; } = null!;
    public ICommand RemoveBoutCommand { get; private set; } = null!;
    public ICommand ProposeUndercardCommand { get; private set; } = null!;
    public ICommand ProposeWholeCardCommand { get; private set; } = null!;
    public ICommand BookProposedCommand { get; private set; } = null!;
    public ICommand PairCommand { get; private set; } = null!;
    public ICommand OpenPosterCommand { get; private set; } = null!;

    // === Surfaces ==============================================================

    private DeskRail _rail = DeskRail.Night;
    /// <summary>What the right-hand rail shows.</summary>
    public DeskRail Rail
    {
        get => _rail;
        set
        {
            if (!Set(ref _rail, value)) return;
            if (value != DeskRail.Picker) { _pickSlot = null; _replaceFight = null; }
            RaiseRail();
            RefreshRail();
        }
    }
    public bool IsRailNight => _rail == DeskRail.Night;
    public bool IsRailPicker => _rail == DeskRail.Picker;
    public bool IsRailRoom => _rail == DeskRail.Room;
    public bool IsRailPlan => _rail == DeskRail.Plan;
    public bool IsRailProposals => _rail == DeskRail.Proposals;
    /// <summary>The table takes the whole width until the rail has something to say (the picker, a proposal...).</summary>
    public bool ShowRail => !(_view == DeskView.Table && _rail == DeskRail.Night);
    /// <summary>The night stays on screen as one line while the rail is busy with something else.</summary>
    public bool ShowNightLine => _rail != DeskRail.Night && HasSelectedEvent && !HasNoEvents;

    private void RaiseRail()
    {
        OnPropertyChanged(nameof(IsRailNight)); OnPropertyChanged(nameof(IsRailPicker));
        OnPropertyChanged(nameof(IsRailRoom)); OnPropertyChanged(nameof(IsRailPlan));
        OnPropertyChanged(nameof(IsRailProposals)); OnPropertyChanged(nameof(ShowNightLine));
        OnPropertyChanged(nameof(IsReplacing)); OnPropertyChanged(nameof(IsPickingSlot));
        OnPropertyChanged(nameof(ShowRail));
    }

    private bool _planFromEmpty = true;

    private void OpenRail(DeskRail rail)
    {
        if (rail == DeskRail.Plan) _planFromEmpty = false;
        Rail = rail;
        RefreshRail();
    }

    private DeskView _view = DeskView.Card;
    public DeskView View
    {
        get => _view;
        set
        {
            if (!Set(ref _view, value)) return;
            OnPropertyChanged(nameof(ShowCardView)); OnPropertyChanged(nameof(ShowTableView)); OnPropertyChanged(nameof(ShowPosterView));
            OnPropertyChanged(nameof(ShowRail));
            if (value == DeskView.Table) RefreshTable();
            if (value == DeskView.Poster) RefreshPoster(force: true);
        }
    }
    public bool ShowCardView { get => _view == DeskView.Card; set { if (value) View = DeskView.Card; } }
    public bool ShowTableView { get => _view == DeskView.Table; set { if (value) View = DeskView.Table; } }
    public bool ShowPosterView { get => _view == DeskView.Poster; set { if (value) View = DeskView.Poster; } }

    // === The card's header =========================================================

    private string _eventName = "";
    public string EventName { get => _eventName; private set => Set(ref _eventName, value); }
    private string _tagline = "";
    /// <summary>The poster line (EventHype), "" for just another fight night.</summary>
    public string Tagline { get => _tagline; private set { if (Set(ref _tagline, value)) OnPropertyChanged(nameof(HasTagline)); } }
    public bool HasTagline => _tagline.Length > 0;
    private string _tierLabel = "";
    public string TierLabel { get => _tierLabel; private set => Set(ref _tierLabel, value); }
    private string _dateLine = "";
    public string DateLine { get => _dateLine; private set => Set(ref _dateLine, value); }
    private string _roomLine = "";
    public string RoomLine { get => _roomLine; private set => Set(ref _roomLine, value); }
    private string _broadcastLine = "";
    /// <summary>Who airs it ("on Arena+ and United Broadcasting"), "" when nobody does.</summary>
    public string BroadcastLine { get => _broadcastLine; private set { if (Set(ref _broadcastLine, value)) OnPropertyChanged(nameof(HasBroadcastLine)); } }
    public bool HasBroadcastLine => _broadcastLine.Length > 0;
    private bool _isDue;
    /// <summary>Fight night: the card's date has come (the two doors show: watch it or sim it).</summary>
    public bool IsDue { get => _isDue; private set => Set(ref _isDue, value); }

    // === The bill ==================================================================

    public BillBout? MainEvent => Bouts.FirstOrDefault(b => b.IsMainEvent);
    public bool HasMainEvent => MainEvent is not null;
    /// <summary>The bouts under the main event, main card first, then the prelims.</summary>
    public ObservableCollection<BillBout> Undercard { get; } = new();
    public ObservableCollection<BillBout> MainCardRows { get; } = new();
    public ObservableCollection<BillBout> PrelimRows { get; } = new();
    public bool HasPrelims => PrelimRows.Count > 0;
    public bool HasMainCardRows => MainCardRows.Count > 0;

    /// <summary>Main-event ideas for an empty card (Suggest(MainEvent)).</summary>
    public ObservableCollection<ProposalCard> HeadlineIdeas { get; } = new();
    public bool HasHeadlineIdeas => HeadlineIdeas.Count > 0;
    /// <summary>Two ideas for the next open slot, right on the bill.</summary>
    public ObservableCollection<ProposalCard> NextSlotIdeas { get; } = new();
    public bool HasNextSlotIdeas => NextSlotIdeas.Count > 0;
    private BoutSlot _nextSlot = BoutSlot.MainEvent;
    /// <summary>The open slot sits under the prelims (else under the main card).</summary>
    public bool NextSlotIsPrelim => _nextSlot == BoutSlot.Prelim && HasMainEvent;
    public bool NextSlotIsMainCard => _nextSlot != BoutSlot.Prelim && HasMainEvent;
    private string _nextSlotLabel = "";
    public string NextSlotLabel { get => _nextSlotLabel; private set => Set(ref _nextSlotLabel, value); }
    private string _nextSlotHint = "";
    public string NextSlotHint { get => _nextSlotHint; private set => Set(ref _nextSlotHint, value); }

    private string _mainCardNote = "";
    /// <summary>"5 on the main card on the night (auto: United Broadcasting's window)".</summary>
    public string MainCardNote { get => _mainCardNote; private set => Set(ref _mainCardNote, value); }
    private string _prelimNote = "";
    public string PrelimNote { get => _prelimNote; private set => Set(ref _prelimNote, value); }

    // The house's padding, shown before the night (declining it is not available yet).
    private string _paddingLine = "";
    public string PaddingLine { get => _paddingLine; private set { if (Set(ref _paddingLine, value)) OnPropertyChanged(nameof(HasPadding)); } }
    public bool HasPadding => _paddingLine.Length > 0;
    public ObservableCollection<string> PaddingBouts { get; } = new();

    // === The night ==================================================================

    private CardProjection? _projection;
    private string _houseHeadline = "";
    public string HouseHeadline { get => _houseHeadline; private set => Set(ref _houseHeadline, value); }
    private string _houseDetail = "";
    public string HouseDetail { get => _houseDetail; private set => Set(ref _houseDetail, value); }
    private double _houseFill;
    /// <summary>Share of the room the house fills (0..100).</summary>
    public double HouseFill { get => _houseFill; private set => Set(ref _houseFill, value); }
    private string _ppvLine = "";
    public string PpvLine { get => _ppvLine; private set { if (Set(ref _ppvLine, value)) OnPropertyChanged(nameof(HasPpv)); } }
    public bool HasPpv => _ppvLine.Length > 0;
    public ObservableCollection<MoneyLine> MoneyIn { get; } = new();
    public ObservableCollection<MoneyLine> MoneyOut { get; } = new();
    private string _netRange = "";
    public string NetRange { get => _netRange; private set => Set(ref _netRange, value); }
    private string _netExpected = "";
    public string NetExpected { get => _netExpected; private set => Set(ref _netExpected, value); }
    private CardTone _netTone;
    public CardTone NetTone { get => _netTone; private set => Set(ref _netTone, value); }
    private bool _hasMoney;
    /// <summary>The card has a bout to price (an empty card can't be priced).</summary>
    public bool HasMoney { get => _hasMoney; private set => Set(ref _hasMoney, value); }
    public ObservableCollection<PartnerLine> Partners { get; } = new();
    public bool HasPartners => Partners.Count > 0;
    private string _partnersSummary = "";
    public string PartnersSummary { get => _partnersSummary; private set => Set(ref _partnersSummary, value); }
    public ObservableCollection<RiskLine> Risks { get; } = new();
    public bool HasRisks => Risks.Count > 0;
    /// <summary>Other promotions' cards announced near ours that cost us nothing (public facts).</summary>
    public ObservableCollection<string> AlsoThatWeek { get; } = new();
    public bool HasAlsoThatWeek => AlsoThatWeek.Count > 0;
    private string _nightLine = "";
    /// <summary>The night in one line (the rail's busy, or the window is small).</summary>
    public string NightLine { get => _nightLine; private set => Set(ref _nightLine, value); }
    private string _termsShortLine = "";
    public string TermsShortLine { get => _termsShortLine; private set { if (Set(ref _termsShortLine, value)) OnPropertyChanged(nameof(HasTermsShort)); } }
    public bool HasTermsShort => _termsShortLine.Length > 0;

    // === The poster =================================================================

    private ImageSource? _poster;
    /// <summary>The auto-generated fight poster for the selected event (drawn when the main event changes).</summary>
    public ImageSource? Poster { get => _poster; private set => Set(ref _poster, value); }
    private string _posterKey = "";

    private void RefreshPoster(bool force = false)
    {
        if (_world is null || SelectedEvent is null) { Poster = null; _posterKey = ""; return; }
        var ev = SelectedEvent.Model;
        var main = ev.Card.Count > 0 ? ev.Card[^1] : null;
        string key = $"{ev.Id.Value}|{ev.Name}|{ev.Tier}|{ev.VenueId?.Value}|{main?.Id.Value}|{main?.RedCorner.Value}|{main?.BlueCorner.Value}|{main?.IsTitleFight}|{Tagline}";
        if (!force && key == _posterKey && _poster is not null) return;
        _posterKey = key;
        Poster = Art.PosterFactory.For(_world, ev);
    }

    // === Refresh: one pass per committed change =====================================

    /// <summary>A booking change landed: the card, the night, the tab's bout count, the picker's lists.</summary>
    private void AfterCardChange()
    {
        RefreshBouts();
        RefreshRedOptions();
        SelectedEvent?.SyncBoutCount();
        if (_view == DeskView.Table) RefreshTable();
    }

    private void RefreshBouts()
    {
        Bouts.Clear(); Undercard.Clear(); MainCardRows.Clear(); PrelimRows.Clear();
        HeadlineIdeas.Clear(); NextSlotIdeas.Clear(); PaddingBouts.Clear();
        Warnings.Clear();
        if (_world is null || SelectedEvent is null)
        {
            _projection = null;
            EventName = ""; Tagline = ""; TierLabel = ""; DateLine = ""; RoomLine = ""; BroadcastLine = ""; IsDue = false;
            PaddingLine = ""; Poster = null; _posterKey = "";
            ClearNight();
            RaiseBill();
            RaiseHints();
            return;
        }

        var w = _world;
        var ev = SelectedEvent.Model;
        var promo = w.Get(w.PlayerPromotion);
        var p = _booking.Project(w, ev);
        _projection = p;

        // Header
        EventName = ev.Name;
        Tagline = Core.Sim.Flavor.EventHype.Tagline(w, ev) ?? "";
        TierLabel = ev.Tier switch { EventTier.Ppv => "Pay-per-view", EventTier.SuperCard => "Super-card", _ => "Fight Night" };
        int days = ev.Date.DayNumber - w.CurrentDate.DayNumber;
        DateLine = $"{ev.Date:dddd d MMMM yyyy}" + (days <= 0 ? " · fight night" : days < 14 ? $" · in {days} days" : $" · in {days / 7} weeks");
        var venue = ev.VenueId is { } vid ? w.Get(vid) : null;
        var city = venue is not null ? w.Get(venue.City) : null;
        RoomLine = venue is null ? (ev.Venue ?? "No room yet")
            : $"{venue.Name}, {city?.Name ?? "?"} · {venue.Capacity:N0} seats";
        var airs = promo?.MediaDeals.Where(d => d.Kind != MediaKind.PpvCarrier || ev.Tier >= EventTier.Ppv).Select(d => d.Partner).ToList() ?? new();
        BroadcastLine = airs.Count == 0 ? "" : "on " + JoinAnd(airs);
        IsDue = ev.Date <= w.CurrentDate;

        // The bill, top-billed. A booked bout is on the main card when its place
        // from the top is inside the split the night will run with (padding goes below).
        int n = ev.Card.Count;
        int mainOnNight = p.MainCardOnTheNight;
        var host = city?.Id;
        var lastFought = LastFought();
        int prelimNo = 0;
        for (int i = n - 1; i >= 0; i--)
        {
            var fight = ev.Card[i];
            int fromTop = n - i;
            bool prelim = fromTop > mainOnNight;
            bool mainEv = i == n - 1;
            bool coMain = !mainEv && i == n - 2 && !prelim;
            string label = mainEv ? "MAIN EVENT" : coMain ? "CO-MAIN" : prelim ? $"PRELIM {++prelimNo}" : $"MAIN {fromTop}";
            var bout = BuildBout(fight, i, n, label, mainEv, coMain, prelim, host, p, lastFought);
            if (bout is null) continue;
            Bouts.Add(bout);
            if (!mainEv)
            {
                Undercard.Add(bout);
                (prelim ? PrelimRows : MainCardRows).Add(bout);
            }
        }

        // The split, in words.
        int mainBooked = Math.Min(n, mainOnNight);
        MainCardNote = n == 0 ? ""
            : ev.MainCardFights > 0 ? $"Main card: the top {ev.MainCardFights} ({mainOnNight} on the night)"
            : promo is not null && Core.Sim.Finance.MediaService.BroadcastWindow(promo) is { } window
                ? $"Main card: auto, {mainOnNight} on the night (the broadcast window is {window})"
                : $"Main card: auto, the whole card ({mainOnNight} on the night)";
        PrelimNote = PrelimRows.Count == 0 ? "" : "Not on the broadcast: cheaper exposure, less fame for the fighters.";

        // The house's padding, before the night.
        var pad = p.Padding;
        if (pad.WouldPad && n > 0)
        {
            PaddingLine = $"A card runs at least {CardFiller.MinCardBouts} bouts. As booked it has {n}, so on the night the house adds "
                          + $"{pad.Bouts.Count} below yours, about {pad.Cost} all in. This is automatic for now.";
            // The house pairs your fighters on our scouts' read (the same pick the night makes),
            // so roster pairs are named; local pairs are unnamed slots until fight night.
            foreach (var fb in pad.Bouts)
                PaddingBouts.Add(fb.LocalFiller || fb.Red is null || fb.Blue is null
                    ? $"Local vs local · {FighterRow.Pretty(fb.WeightClass.ToString())}"
                    : $"{w.Get(fb.Red.Value)?.LastName} vs {w.Get(fb.Blue.Value)?.LastName} · {FighterRow.Pretty(fb.WeightClass.ToString())}");
        }
        else PaddingLine = "";

        // The open slot: what the card needs next, and two ideas for it.
        _nextSlot = NextSlot(ev, n);
        NextSlotLabel = _nextSlot switch
        {
            BoutSlot.MainEvent => "Main event",
            BoutSlot.CoMain => "Co-main",
            BoutSlot.MainCard => $"Main card, slot {n + 1}",
            _ => "Prelims",
        };
        NextSlotHint = !HasRoster ? "Sign fighters first: every bout needs one of yours."
            : _nextSlot == BoutSlot.Prelim ? "Add a prelim, or let the matchmaker propose the undercard."
            : "Pick a pairing below, or open the picker for the whole division.";
        if (n == 0)
            foreach (var s in _booking.Suggest(w, ev, BoutSlot.MainEvent, 3)) HeadlineIdeas.Add(Card(s, BoutSlot.MainEvent));
        else
            foreach (var s in _booking.Suggest(w, ev, _nextSlot, 2)) NextSlotIdeas.Add(Card(s, _nextSlot));
        if (n > 0 && NextSlotIdeas.Count == 0 && HasRoster)
            NextSlotHint = HasLocalOption
                ? "Nobody else of yours can be paired on this date. Open the picker for a local opponent, or sign more fighters."
                : "Nobody else of yours can be paired on this date.";

        BuildNight(p, ev, promo);
        foreach (var partner in n == 0 ? Array.Empty<PartnerProjection>() : p.Partners)
            foreach (var t in partner.Terms.Where(t => t.Status == TermStatus.Missed))
                Warnings.Add($"{partner.Partner}: {t.Text}{(t.Gap is { } g ? $" ({g})" : "")}");

        RefreshPoster(force: _view == DeskView.Poster);
        RaiseBill();
        RaiseHints();
    }

    private BillBout? BuildBout(Fight fight, int index, int count, string label, bool mainEv, bool coMain, bool prelim,
        CityId? host, CardProjection p, Dictionary<FighterId, DateOnly> lastFought)
    {
        var w = _world!;
        var r = w.Get(fight.RedCorner);
        var b = w.Get(fight.BlueCorner);
        if (r is null || b is null) return null;
        var red = new BillCorner(r, w, _advisor, body: mainEv, hostCity: host);
        var blue = new BillCorner(b, w, _advisor, body: mainEv, hostCity: host);
        var a = _advisor.Appraise(w, r.Id, b.Id);
        string division = FighterRow.DivisionLabel(fight.WeightClass, r.Sex);

        // Why it sells: public facts only.
        var sells = new List<DeskTag>();
        if (fight.IsTitleFight) sells.Add(new DeskTag(red.IsChampion || blue.IsChampion ? "Title fight" : "For the vacant belt", CardTone.Warn));
        else if (a.Stakes == Stakes.TitleEliminator) sells.Add(new DeskTag($"Eliminator · {red.Rank} vs {blue.Rank}", CardTone.Info));
        else if (red.HasRank && blue.HasRank && !r.LocalTalent && !b.LocalTalent) sells.Add(new DeskTag($"{red.Rank} vs {blue.Rank}", CardTone.Info));
        if (Core.Sim.Living.Storylines.RivalryBetween(w, r.Id, b.Id) is { Intensity: >= 20 } feud)
            sells.Add(new DeskTag($"Bad blood · heat {feud.Intensity}", CardTone.Bad));
        int met = r.BoutHistory.Intersect(b.BoutHistory).Count();
        if (met == 1) sells.Add(new DeskTag("The rematch", CardTone.Warn));
        else if (met >= 2) sells.Add(new DeskTag(met == 2 ? "The trilogy" : $"Meeting no. {met + 1}", CardTone.Warn));
        foreach (var c in new[] { red, blue }.Where(c => c.AtHome)) sells.Add(new DeskTag($"{c.Short} fights at home", CardTone.Good));
        var star = new[] { r, b }.Where(f => !f.LocalTalent).OrderByDescending(f => f.Intangibles.Popularity.Value).FirstOrDefault();
        if (star is not null && star.Intangibles.Popularity.Value >= 60) sells.Add(new DeskTag($"{star.LastName} draws a crowd", CardTone.Good));
        foreach (var f in new[] { r, b }.Where(f => !f.LocalTalent && f.Record.Wins + f.Record.Losses + f.Record.Draws == 0))
            sells.Add(new DeskTag($"{f.LastName}'s debut", CardTone.Info));
        if (r.LocalTalent || b.LocalTalent) sells.Add(new DeskTag("Against the local scene", CardTone.Neutral));
        if (sells.Count == 0) sells.Add(new DeskTag(a.StakesTag, a.Stakes == Stakes.Mismatch ? CardTone.Bad : CardTone.Neutral));

        // Watch for: the risks the night already carries, plus long layoffs.
        var watch = new List<DeskTag>();
        foreach (var risk in p.Risks.Where(x => x.Fighter is { } id && (id == r.Id || id == b.Id)))
        {
            string who = w.Get(risk.Fighter!.Value)?.LastName ?? "?";
            watch.Add(risk.Kind switch
            {
                BookingRiskKind.LastFightOnDeal => new DeskTag($"{who}: last fight on the deal", CardTone.Warn),
                BookingRiskKind.NotMatchReady => new DeskTag($"{who}: not cleared for this date", CardTone.Bad),
                BookingRiskKind.NotUnderContract => new DeskTag($"{who}: no longer under contract", CardTone.Bad),
                _ => new DeskTag(risk.Text, CardTone.Warn),
            });
        }
        foreach (var f in new[] { r, b }.Where(f => !f.LocalTalent))
            if (lastFought.TryGetValue(f.Id, out var lf) && w.CurrentDate.DayNumber - lf.DayNumber > 270)
                watch.Add(new DeskTag($"{f.LastName} hasn't fought since {lf:MMM yyyy}", CardTone.Warn));
        if (a.Stakes == Stakes.Mismatch && !r.LocalTalent && !b.LocalTalent)
            watch.Add(new DeskTag($"Mismatch on our reads: {red.ReadText} vs {blue.ReadText}", CardTone.Bad));

        string rounds = $"{fight.ScheduledRounds} rd{(fight.ScheduledRounds == 1 ? "" : "s")}";
        string kicker = (mainEv ? "MAIN EVENT" : coMain ? "CO-MAIN EVENT" : label) + " · "
                        + (fight.IsTitleFight ? $"{division.ToUpperInvariant()} TITLE" : division.ToUpperInvariant())
                        + $" · {fight.ScheduledRounds} ROUNDS";
        string matchmaker = (mainEv || coMain) && !r.LocalTalent && !b.LocalTalent
            ? $"On what we've seen: {red.Short} reads {red.ReadText}, {blue.Short} {blue.ReadText}. {char.ToUpperInvariant(a.Summary[0])}{a.Summary[1..]}."
              + (HeadToHeadLine(r, b) is { Length: > 0 } h2h ? " " + h2h : "")
            : "";

        return new BillBout
        {
            Desk = this, Id = fight.Id, CardIndex = index, CardCount = count, SlotLabel = label,
            Red = red, Blue = blue, Kicker = kicker, Division = division, Rounds = rounds,
            IsTitle = fight.IsTitleFight, IsMainEvent = mainEv, IsCoMain = coMain, IsPrelim = prelim,
            Sells = sells, WatchFor = watch, Matchmaker = matchmaker,
        };
    }

    private static string Title(string caps) =>
        CultureInfo.InvariantCulture.TextInfo.ToTitleCase(caps.ToLowerInvariant());

    private void RaiseBill()
    {
        OnPropertyChanged(nameof(MainEvent)); OnPropertyChanged(nameof(HasMainEvent));
        OnPropertyChanged(nameof(HasPrelims)); OnPropertyChanged(nameof(HasMainCardRows));
        OnPropertyChanged(nameof(HasHeadlineIdeas)); OnPropertyChanged(nameof(HasNextSlotIdeas));
        OnPropertyChanged(nameof(HasWarnings)); OnPropertyChanged(nameof(ShowNightLine));
        OnPropertyChanged(nameof(HasPartners)); OnPropertyChanged(nameof(HasRisks));
        OnPropertyChanged(nameof(NextSlotIsPrelim)); OnPropertyChanged(nameof(NextSlotIsMainCard));
    }

    // Who has fought when (any promotion): one pass over the record, kept until the world moves.
    private Dictionary<FighterId, DateOnly>? _lastFought;
    private (World?, DateOnly, int) _lastFoughtKey;
    private Dictionary<FighterId, DateOnly> LastFought()
    {
        var w = _world!;
        var key = (w, w.CurrentDate, w.Events.Count);
        if (_lastFought is not null && _lastFoughtKey == key) return _lastFought;
        var map = new Dictionary<FighterId, DateOnly>();
        foreach (var e in w.Events.Values)
        {
            if (e.Status != EventStatus.Completed) continue;
            foreach (var f in e.Card)
            {
                if (f.Outcome is null) continue;
                if (!map.TryGetValue(f.RedCorner, out var dr) || dr < e.Date) map[f.RedCorner] = e.Date;
                if (!map.TryGetValue(f.BlueCorner, out var db) || db < e.Date) map[f.BlueCorner] = e.Date;
            }
        }
        _lastFoughtKey = key;
        return _lastFought = map;
    }

    // === The night panel, from the sim's projection =================================

    private void ClearNight()
    {
        HouseHeadline = ""; HouseDetail = ""; HouseFill = 0; PpvLine = "";
        MoneyIn.Clear(); MoneyOut.Clear(); Partners.Clear(); Risks.Clear(); AlsoThatWeek.Clear();
        OnPropertyChanged(nameof(HasAlsoThatWeek));
        NetRange = ""; NetExpected = ""; HasMoney = false; NightLine = ""; PartnersSummary = ""; TermsShortLine = "";
    }

    private void BuildNight(CardProjection p, Event ev, Promotion? promo)
    {
        ClearNight();
        int n = ev.Card.Count;

        // The house.
        if (p.Capacity is { } cap && cap > 0)
        {
            HouseHeadline = n == 0 ? $"{cap:N0} seats" : p.Attendance >= cap ? "Sold out" : $"{p.Attendance:N0}";
            HouseDetail = n == 0 ? "The house depends on who headlines."
                : $"{p.Attendance:N0} of {cap:N0} seats"
                  + (p.Demand > cap ? $" · about {p.Demand - cap:N0} more want in" : p.Attendance < cap ? $" · {100 * p.Attendance / cap}% full" : "");
            HouseFill = n == 0 ? 0 : Math.Min(100, 100.0 * p.Attendance / cap);
        }
        else
        {
            HouseHeadline = n == 0 ? "—" : $"{p.Attendance:N0}";
            HouseDetail = "No room booked.";
        }
        if (p.PpvBuys > 0) PpvLine = $"About {p.PpvBuys:N0} pay-per-view buys";

        HasMoney = n > 0;
        if (n > 0)
        {
            // Money in.
            void In(string label, Money m, string detail = "") { if (m.Amount != 0) MoneyIn.Add(new MoneyLine(label, Signed(m), CardTone.Good, detail)); }
            In(p.Attendance > 0 ? $"Tickets ({p.Attendance:N0} sold)" : "Tickets", p.Gate);
            In("Pay-per-view", p.Ppv);
            In("Media rights", p.MediaRights);
            In("Sponsors", p.Sponsors);
            In("Merch table", p.Merch);
            // Money out.
            void Out(string label, Money m, string detail = "") { if (m.Amount != 0) MoneyOut.Add(new MoneyLine(label, Signed(new Money(-m.Amount)), CardTone.Bad, detail)); }
            Out("Production and staff", p.Production);
            Out("Room hire", p.Rent);
            Out($"Purses ({n * 2} fighters)", p.Purses);
            Out("PPV points", p.PpvPoints);
            if (p.WinBonusMax.Amount > 0)
                MoneyOut.Add(new MoneyLine("Win bonuses (winners only)",
                    p.WinBonusMin.Amount == p.WinBonusMax.Amount ? Signed(new Money(-p.WinBonusMax.Amount))
                        : $"{Signed(new Money(-p.WinBonusMin.Amount))} to {Signed(new Money(-p.WinBonusMax.Amount))}", CardTone.Bad));
            Out("Fight of the Night bonus", p.FightOfTheNight);
            if (p.Padding.WouldPad) Out($"House padding ({p.Padding.Bouts.Count} bouts)", p.Filler);

            NetRange = p.NetLow.Amount == p.NetHigh.Amount ? Signed(p.Net) : $"{Signed(p.NetLow)} to {Signed(p.NetHigh)}";
            NetExpected = $"About {Signed(p.Net)} expected"
                          + (p.PartnerFeesAtStakeOnTheNight.Amount > 0 ? $"; {p.PartnerFeesAtStakeOnTheNight} rides on terms judged on the night" : "");
            NetTone = p.NetHigh.Amount < 0 ? CardTone.Bad : p.NetLow.Amount < 0 ? CardTone.Warn : CardTone.Good;
        }

        // Partners and their terms.
        int missed = 0;
        // Trouble first: a deal that ends, then one that pays half, then the rest by what they pay.
        var ordered = p.Partners
            .OrderBy(pp => pp.GoneBeforeCard || pp.WouldWalk ? 0 : pp.AtRisk ? 1 : pp.HasClausesOnTheNight ? 2 : 3)
            .ThenByDescending(pp => pp.FullFee.Amount);
        foreach (var pp in ordered)
        {
            var all = pp.Terms.Select(t => new TermLine(t.Text, t.Status, t.Gap ?? "")).ToList();
            // A partner whose every term holds folds to one line; the clauses show when one doesn't.
            // An empty card isn't judged yet: the partners fold until there's a bout.
            if (n == 0 && !pp.GoneBeforeCard)
            {
                Partners.Add(new PartnerLine
                {
                    Name = pp.Partner, Kind = pp.IsMedia ? "Media" : "Sponsor",
                    Fee = pp.FullFee.Amount == 0 ? "No rights fee" : $"{pp.FullFee} a show",
                    Strikes = pp.StrikesBeforeCard == 0 ? "No strikes" : $"{pp.StrikesBeforeCard} of {pp.StrikeLimit} strikes",
                    Tone = CardTone.Neutral, Terms = Array.Empty<TermLine>(),
                    Verdict = all.Count == 0 ? "No terms." : "Wants: " + string.Join("; ", all.Select(t => t.Text)) + ".",
                });
                continue;
            }
            missed += all.Count(t => t.IsMissed);
            bool open = pp.AtRisk || pp.GoneBeforeCard || pp.HasClausesOnTheNight;
            var terms = open ? all.Where(t => !t.IsMet).ToList() : new List<TermLine>();
            bool noFee = pp.FullFee.Amount == 0;
            string fee = pp.GoneBeforeCard ? "Gone before this card: pays nothing"
                : noFee ? (pp.IsMedia ? "Carries the card; no rights fee" : "No fee")
                : pp.AtRisk ? $"Pays half: {pp.ExpectedFee} of {pp.FullFee}"
                : $"{pp.FullFee} a show";
            string strikes = pp.StrikesBeforeCard == 0 ? "No strikes"
                : pp.PendingStrikes > 0 ? $"{pp.StrikesBeforeCard} of {pp.StrikeLimit} strikes by fight night"
                : $"{pp.Strikes} of {pp.StrikeLimit} strikes";
            int met = all.Count(t => t.IsMet);
            string verdict = pp.GoneBeforeCard ? "The calendar ends this deal before the card airs."
                : pp.WouldWalk ? "A miss on this card ends the deal."
                : all.Count == 0 ? "No terms: pays when the show goes ahead."
                : pp.AtRisk ? (noFee ? "A miss still earns a strike." : "")
                : pp.HasClausesOnTheNight ? (met > 0 ? $"{met} term{(met == 1 ? "" : "s")} met; the rest is judged on the night." : "Judged on the night.")
                : met == 1 ? $"Its term is met: {all[0].Text}." : $"All {met} terms met.";
            Partners.Add(new PartnerLine
            {
                Name = pp.Partner, Kind = pp.IsMedia ? "Media" : "Sponsor", Fee = fee, Strikes = strikes,
                Tone = pp.GoneBeforeCard || pp.WouldWalk ? CardTone.Bad : pp.AtRisk ? CardTone.Warn : CardTone.Good,
                Terms = terms, Verdict = verdict,
            });
        }
        PartnersSummary = p.Partners.Count == 0
            ? "No sponsors or broadcasters yet. Partners come through the Media desk."
            : n == 0 ? "They judge the card once it has bouts. What each wants:"
            : missed == 0 ? $"{p.Partners.Count} partner{(p.Partners.Count == 1 ? "" : "s")}: no term missed by the card as it stands."
            : $"{missed} term{(missed == 1 ? "" : "s")} missed: that partner pays half and adds a strike.";
        TermsShortLine = missed == 0 ? "" : $"{missed} partner term{(missed == 1 ? "" : "s")} short";

        // Risks (the padding and missed terms have their own places on the screen).
        foreach (var r in p.Risks)
        {
            if (r.Kind is BookingRiskKind.CardPadded or BookingRiskKind.PartnerTermMissed) continue;
            var (tone, icon) = r.Kind switch
            {
                BookingRiskKind.EmptyCard => (CardTone.Info, "Calendar"),
                BookingRiskKind.LastFightOnDeal => (CardTone.Warn, "Pen"),
                BookingRiskKind.NotUnderContract => (CardTone.Bad, "Pen"),
                BookingRiskKind.NotMatchReady => (CardTone.Bad, "Warning"),
                BookingRiskKind.CadenceBroken => (CardTone.Bad, "Broadcast"),
                BookingRiskKind.RivalCardClash => (CardTone.Warn, "Calendar"),
                BookingRiskKind.RoomTooSmall => (CardTone.Info, "Building"),
                BookingRiskKind.RoomTooBig => (CardTone.Warn, "Building"),
                _ => (CardTone.Warn, "Warning"),
            };
            Risks.Add(new RiskLine(r.Text, tone, icon));
        }

        // Rival cards announced near ours that the house doesn't charge for: public
        // information only, never a cost (the charged ones are clashes, in the risks).
        AlsoThatWeek.Clear();
        foreach (var rc in p.NearbyRivalCards.Where(r => !r.Charged).OrderBy(r => r.Card.Date))
        {
            string main = rc.Card.MainEventLabel(_world!);
            AlsoThatWeek.Add($"{rc.Card.Event.Name} ({rc.Card.Promotion.Abbreviation}), {rc.Card.Date:ddd d MMM}"
                             + (main.Length > 0 ? $": {main}" : "") + (rc.Card.TitleOnTheLine ? ", a belt on the line" : ""));
        }
        OnPropertyChanged(nameof(HasAlsoThatWeek));

        NightLine = n == 0 ? "No bouts yet"
            : $"{HouseHeadline}{(p.Capacity is { } c3 && p.Attendance < c3 ? $" of {c3:N0}" : "")}"
              + (p.PpvBuys > 0 ? $" · ≈{(p.PpvBuys >= 10_000 ? $"{p.PpvBuys / 1000:N0}k" : $"{p.PpvBuys:N0}")} PPV" : "")
              + $" · {Compact(p.NetLow)} to {Compact(p.NetHigh)} all in";
        OnPropertyChanged(nameof(HasPartners)); OnPropertyChanged(nameof(HasRisks));
    }

    /// <summary>"+$75,144" / "−$9,696" (a real minus, never accounting brackets).</summary>
    public static string Signed(Money m) =>
        (m.Amount < 0 ? "−" : "+") + Math.Abs(m.Amount).ToString("C0", CultureInfo.GetCultureInfo("en-US"));

    /// <summary>"+$21.3M", "−$4.2k", "+$850": a figure for a one-line summary.</summary>
    public static string Compact(Money m)
    {
        decimal a = Math.Abs(m.Amount);
        string sign = m.Amount < 0 ? "−" : "+";
        var inv = CultureInfo.InvariantCulture;
        return a >= 1_000_000m ? $"{sign}${(a / 1_000_000m).ToString("0.0", inv)}M"
            : a >= 10_000m ? $"{sign}${(a / 1_000m).ToString("0", inv)}k"
            : a >= 1_000m ? $"{sign}${(a / 1_000m).ToString("0.0", inv)}k"
            : $"{sign}${a.ToString("0", inv)}";
    }

    private static string JoinAnd(IReadOnlyList<string> xs) =>
        xs.Count <= 1 ? string.Concat(xs) : string.Join(", ", xs.Take(xs.Count - 1)) + " and " + xs[^1];

    // === Where a new bout goes ====================================================

    /// <summary>What the card needs next: a main event, a co-main, main-card fights, then prelims.</summary>
    private BoutSlot NextSlot(Event ev, int booked)
    {
        if (booked <= 0) return BoutSlot.MainEvent;
        if (booked == 1) return BoutSlot.CoMain;
        int target = ev.MainCardFights > 0 ? ev.MainCardFights : 5;
        return booked < target ? BoutSlot.MainCard : BoutSlot.Prelim;
    }

    private static string SlotWords(BoutSlot slot) => slot switch
    {
        BoutSlot.MainEvent => " as the main event",
        BoutSlot.CoMain => " as the co-main",
        BoutSlot.MainCard => " on the main card",
        _ => " on the prelims",
    };

    /// <summary>Put a just-booked bout (the sim adds it at the bottom) where its slot is, through the sim.</summary>
    private void Place(Event ev, FightId fight, BoutSlot slot)
    {
        if (_world is null) return;
        int n = ev.Card.Count;
        int to = slot switch
        {
            BoutSlot.MainEvent => n - 1,
            BoutSlot.CoMain => Math.Max(0, n - 2),
            BoutSlot.MainCard => Math.Max(0, n - Math.Min(n, _booking.MainCardOnTheNight(_world, ev))),
            _ => 0,
        };
        if (to > 0 && !_booking.MoveBout(_world, ev, fight, to, out var err))
            Err(err ?? "Couldn't place that bout.");
    }

    // === Reorder, headline, remove ================================================

    private void Move(BillBout? b, int direction)
    {
        if (_world is null || SelectedEvent is null || b is null) return;
        var ev = SelectedEvent.Model;
        int i = ev.Card.FindIndex(f => f.Id == b.Id);
        int j = i + direction;   // +1 = up the bill (toward the main event, the END of the sim's list)
        if (i < 0 || j < 0 || j >= ev.Card.Count) return;
        if (!_booking.MoveBout(_world, ev, b.Id, j, out var err)) { Err(err ?? "Couldn't move that bout."); return; }
        AfterCardChange();
        Ok(j == ev.Card.Count - 1 ? $"{b.Pairing} is the main event now." : direction > 0 ? $"{b.Pairing} moved up the bill." : $"{b.Pairing} moved down the bill.");
    }

    private void Headline(BillBout? b)
    {
        if (_world is null || SelectedEvent is null || b is null) return;
        var ev = SelectedEvent.Model;
        if (!_booking.MoveBout(_world, ev, b.Id, ev.Card.Count - 1, out var err)) { Err(err ?? "Couldn't move that bout."); return; }
        AfterCardChange();
        Ok($"{b.Pairing} headlines {ev.Name} now.");
    }

    private void Remove(BillBout? b)
    {
        if (_world is null || SelectedEvent is null || b is null) return;
        _booking.RemoveBout(_world, SelectedEvent.Model, b.Id);
        AfterCardChange();
        Ok($"{b.Pairing} is off the card. Both go back to the roster unbooked.");
    }

    // === The picker =================================================================

    private BoutSlot? _pickSlot;
    private FightId? _replaceFight;
    private bool _replaceBlue = true;

    public bool IsPickingSlot => _rail == DeskRail.Picker && _replaceFight is null;
    public bool IsReplacing => _rail == DeskRail.Picker && _replaceFight is not null;

    private string _pickerTitle = "";
    public string PickerTitle { get => _pickerTitle; private set => Set(ref _pickerTitle, value); }
    private string _pickerHint = "";
    public string PickerHint { get => _pickerHint; private set => Set(ref _pickerHint, value); }

    private PickMode _pickMode = PickMode.Suggested;
    public PickMode PickMode
    {
        get => _pickMode;
        set
        {
            if (!Set(ref _pickMode, value)) return;
            OnPropertyChanged(nameof(PickSuggested)); OnPropertyChanged(nameof(PickDivision)); OnPropertyChanged(nameof(PickLocal));
            RefreshRail();
        }
    }
    public bool PickSuggested { get => _pickMode == PickMode.Suggested; set { if (value) PickMode = PickMode.Suggested; } }
    public bool PickDivision { get => _pickMode == PickMode.Division; set { if (value) PickMode = PickMode.Division; } }
    public bool PickLocal { get => _pickMode == PickMode.Local; set { if (value) PickMode = PickMode.Local; } }

    /// <summary>The matchmaker's pairings for the open slot.</summary>
    public ObservableCollection<ProposalCard> Suggestions { get; } = new();
    public bool HasSuggestions => Suggestions.Count > 0;
    private string _suggestionsEmpty = "";
    public string SuggestionsEmpty { get => _suggestionsEmpty; private set => Set(ref _suggestionsEmpty, value); }

    /// <summary>The division's ladder (no corner picked) or the opponents for the picked corner.</summary>
    public ObservableCollection<LadderEntry> Ladder { get; } = new();
    public bool HasLadder => Ladder.Count > 0;
    private string _ladderTitle = "";
    public string LadderTitle { get => _ladderTitle; private set => Set(ref _ladderTitle, value); }
    private string _ladderEmpty = "";
    public string LadderEmpty { get => _ladderEmpty; private set => Set(ref _ladderEmpty, value); }
    /// <summary>A red corner is picked (the ladder shows opponents).</summary>
    public bool HasRedPick => SelectedRed is not null;
    /// <summary>Both corners are picked: the comparison and the Book button show.</summary>
    public bool HasPairing => SelectedRed is not null && SelectedBlue is not null;

    // Replace mode: the bout and the two corners.
    private string _replaceRedName = "", _replaceBlueName = "";
    public string ReplaceRedName { get => _replaceRedName; private set => Set(ref _replaceRedName, value); }
    public string ReplaceBlueName { get => _replaceBlueName; private set => Set(ref _replaceBlueName, value); }
    public bool ReplacingBlue { get => _replaceBlue; set { if (value != _replaceBlue) { _replaceBlue = value; OnPropertyChanged(); OnPropertyChanged(nameof(ReplacingRed)); RefreshRail(); } } }
    public bool ReplacingRed { get => !_replaceBlue; set => ReplacingBlue = !value; }

    private void OpenPicker(BoutSlot slot)
    {
        if (SelectedEvent is null) return;
        _replaceFight = null;
        _pickSlot = slot;
        ClearPairing();
        if (_rail != DeskRail.Picker) { _rail = DeskRail.Picker; RaiseRail(); }
        else RaiseRail();
        _pickMode = PickMode.Suggested;
        OnPropertyChanged(nameof(PickSuggested)); OnPropertyChanged(nameof(PickDivision)); OnPropertyChanged(nameof(PickLocal));
        RefreshRail();
    }

    private void OpenReplace(BillBout? b)
    {
        if (b is null) return;
        _replaceFight = b.Id;
        ClearPairing();
        _replaceBlue = !b.Blue.IsChampion || b.Red.IsChampion;   // the challenger's corner by default
        if (b.Blue.Rank == "Local") _replaceBlue = true;
        _pickSlot = null;
        if (_rail != DeskRail.Picker) { _rail = DeskRail.Picker; }
        RaiseRail();
        OnPropertyChanged(nameof(ReplacingBlue)); OnPropertyChanged(nameof(ReplacingRed));
        RefreshRail();
    }

    private void ClearPairing()
    {
        bool q = _quiet;
        _quiet = true;
        try { SelectedBlue = null; SelectedRed = null; }
        finally { _quiet = q; }
        BlueOptions.Clear();
        UpdateAppraisal();
        OnPropertyChanged(nameof(HasRedPick));
        RebuildLadder();
    }

    private ProposalCard Card(BoutProposal s, BoutSlot slot)
    {
        var w = _world!;
        var host = SelectedEvent?.Model is { } ev && Core.Sim.Geography.WorldMap.CityOf(w, ev) is { } c ? c : (CityId?)null;
        var r = w.Get(s.Red)!;
        var b = w.Get(s.Blue)!;
        return new ProposalCard
        {
            Desk = this, Proposal = s, Slot = slot,
            Red = new BillCorner(r, w, _advisor, hostCity: host),
            Blue = new BillCorner(b, w, _advisor, hostCity: host),
            Division = FighterRow.DivisionLabel(s.WeightClass, r.Sex),
            Reason = s.Reason,
            Terms = (s.TitleFight ? "Title fight · " : "") + $"{s.Rounds} rounds",
            Read = s.Appraisal.Summary,
            SlotLabel = slot switch { BoutSlot.MainEvent => "Main event", BoutSlot.CoMain => "Co-main", BoutSlot.MainCard => "Main card", _ => "Prelim" },
        };
    }

    /// <summary>Rebuild whatever the rail shows (cheap surfaces are rebuilt on every refresh).</summary>
    private void RefreshRail()
    {
        OnPropertyChanged(nameof(ShowNightLine));
        switch (_rail)
        {
            case DeskRail.Picker: RefreshPicker(); break;
            case DeskRail.Room: RefreshRooms(); break;
        }
    }

    private void RefreshPicker()
    {
        Suggestions.Clear();
        if (_world is null || SelectedEvent is null) { OnPropertyChanged(nameof(HasSuggestions)); return; }
        var w = _world;
        var ev = SelectedEvent.Model;

        if (_replaceFight is { } fid)
        {
            var fight = ev.Card.FirstOrDefault(f => f.Id == fid);
            if (fight is null) { Rail = DeskRail.Night; return; }
            var r = w.Get(fight.RedCorner);
            var b = w.Get(fight.BlueCorner);
            ReplaceRedName = r?.LastName ?? "?";
            ReplaceBlueName = b?.LastName ?? "?";
            PickerTitle = $"Change a corner: {r?.LastName} vs {b?.LastName}";
            PickerHint = "The bout keeps its place on the bill, its rounds and, while the champion stays in it, the belt.";
            RebuildLadder();
            return;
        }

        var slot = _pickSlot ?? _nextSlot;
        PickerTitle = slot switch
        {
            BoutSlot.MainEvent => "Pick the main event",
            BoutSlot.CoMain => "Pick the co-main",
            BoutSlot.MainCard => $"Main card, slot {ev.Card.Count + 1}",
            _ => "Add a prelim",
        };
        PickerHint = slot switch
        {
            BoutSlot.MainEvent => $"Fights the matchmaker likes to headline, from fighters ready on {ev.Date:d MMM}.",
            BoutSlot.Prelim => "Even fights for the undercard; the names are saved for the main card.",
            _ => $"Fights the matchmaker likes for this card, from fighters ready on {ev.Date:d MMM}.",
        };
        if (_pickMode == PickMode.Suggested)
        {
            foreach (var s in _booking.Suggest(w, ev, slot, 8)) Suggestions.Add(Card(s, slot));
            SuggestionsEmpty = !HasRoster
                ? "You have no fighters to pair yet. Sign two in the same division, or put one of yours against the local scene."
                : "No pairing fits this slot: nobody else in your divisions is ready and unbooked on this date. Try a division by hand"
                  + (HasLocalOption ? ", or a local opponent." : ".");
        }
        OnPropertyChanged(nameof(HasSuggestions));
        RebuildLadder();
    }

    /// <summary>The ladder: your fighters in the chosen division, ready first, the rest greyed with why.</summary>
    private void RebuildLadder()
    {
        Ladder.Clear();
        _elsewhere = null;
        OnPropertyChanged(nameof(HasRedPick));
        OnPropertyChanged(nameof(HasPairing));
        if (_world is null || SelectedEvent is null || _rail != DeskRail.Picker) { OnPropertyChanged(nameof(HasLadder)); return; }
        var w = _world;
        var ev = SelectedEvent.Model;

        if (_replaceFight is { } fid)
        {
            var fight = ev.Card.FirstOrDefault(f => f.Id == fid);
            if (fight is null) { OnPropertyChanged(nameof(HasLadder)); return; }
            var stay = _replaceBlue ? fight.RedCorner : fight.BlueCorner;
            var stayF = w.Get(stay);
            var pool = _booking.EligibleOpponents(w, ev, stay);
            foreach (var f in _advisor.ByStanding(w, pool))
            {
                string why = BookedElsewhereName(f, ev) is { } other ? $"Booked on {other}" : "";
                var a = _advisor.Appraise(w, stay, f.Id);
                Ladder.Add(new LadderEntry(f, w, this, _advisor.StandingOf(w, f.Id).Label,
                    $"vs {stayF?.LastName}: {a.Summary}", why.Length == 0, why) { Command = ReplaceWithCommand });
            }
            LadderTitle = $"Who could face {stayF?.LastName ?? "them"} instead";
            LadderEmpty = $"Nobody else in {stayF?.LastName}'s division is ready and unbooked on {ev.Date:d MMM}.";
            OnPropertyChanged(nameof(HasLadder));
            return;
        }

        if (SelectedDivision is not { } d)
        {
            LadderTitle = "";
            LadderEmpty = HasRoster ? "Pick a division."
                : "You have no fighters yet. Every bout needs one of yours, even against a local: sign a fighter on the Contracts desk first.";
            OnPropertyChanged(nameof(HasLadder));
            return;
        }
        var everyone = w.RosterOf(w.PlayerPromotion)
            .Where(f => f.Status != FighterStatus.Retired && f.WeightClass == d.WeightClass && f.Sex == d.Sex).ToList();
        if (SelectedRed is null)
        {
            var ready = RedOptions.Select(o => w.Get(o.Id)).OfType<Fighter>().ToList();
            foreach (var f in ready)
            {
                string? other = BookedElsewhereName(f, ev);
                Ladder.Add(new LadderEntry(f, w, this, _advisor.StandingOf(w, f.Id).Label, DealLine(f),
                    other is null, other is null ? "" : $"Booked on {other}") { Command = PickLadderCommand });
            }
            AddUnavailable(everyone.Where(f => ready.All(r => r.Id != f.Id)), ev);
            LadderTitle = $"{d.Label}: pick the first corner";
            LadderEmpty = $"You have nobody at {d.Label}.";
        }
        else
        {
            var red = w.Get(SelectedRed.Id);
            var opp = BlueOptions.Select(o => w.Get(o.Id)).OfType<Fighter>().ToList();
            foreach (var f in opp)
            {
                string? other = BookedElsewhereName(f, ev);
                var a = _advisor.Appraise(w, SelectedRed.Id, f.Id);
                Ladder.Add(new LadderEntry(f, w, this, _advisor.StandingOf(w, f.Id).Label, a.Summary,
                    other is null, other is null ? "" : $"Booked on {other}") { Command = PickLadderCommand });
            }
            AddUnavailable(everyone.Where(f => f.Id != SelectedRed.Id && opp.All(o => o.Id != f.Id)), ev);
            LadderTitle = $"Opponents for {red?.LastName}";
            LadderEmpty = $"Nobody else at {d.Label} is ready on {ev.Date:d MMM}.";
        }
        OnPropertyChanged(nameof(HasLadder));
    }

    private void AddUnavailable(IEnumerable<Fighter> fighters, Event ev)
    {
        foreach (var f in _advisor.ByStanding(_world!, fighters))
            Ladder.Add(new LadderEntry(f, _world!, this, _advisor.StandingOf(_world!, f.Id).Label, "", false, Unavailable(f, ev)));
    }

    // Why a fighter can't be booked on this card, said plainly (model state only).
    private string Unavailable(Fighter f, Event ev)
    {
        if (ev.Card.Any(x => x.RedCorner == f.Id || x.BlueCorner == f.Id)) return "Already on this card";
        if (BookedElsewhereName(f, ev) is { } other) return $"Booked on {other}";
        if (f.CurrentInjury is { } inj) return $"Injured ({inj.Description}), back {inj.ExpectedReturn:d MMM}";
        if (f.Status == FighterStatus.Suspended) return f.SuspendedUntil is { } s ? $"Suspended until {s:d MMM}" : "Suspended";
        if (f.ReadyToFightOn is { } r && r > ev.Date) return $"Recovering until {r:d MMM}";
        if (!f.IsMatchReady(ev.Date)) return "Not cleared for this date";
        return "Not available";
    }

    // Who is booked on another scheduled card, built once per list (a refresh
    // rebuilds it): fighter -> "AFC 165 (5 Sep)".
    private Dictionary<FighterId, string>? _elsewhere;
    private EventId? _elsewhereFor;

    private void IndexElsewhere(Event ev)
    {
        var map = new Dictionary<FighterId, string>();
        foreach (var e in _world!.Events.Values.Where(e => e.Id != ev.Id && e.Status == EventStatus.Scheduled).OrderByDescending(e => e.Date))
            foreach (var x in e.Card)
            {
                map[x.RedCorner] = $"{e.Name} ({e.Date:d MMM})";
                map[x.BlueCorner] = $"{e.Name} ({e.Date:d MMM})";
            }
        _elsewhere = map;
        _elsewhereFor = ev.Id;
    }

    private string? BookedElsewhereName(Fighter f, Event ev)
    {
        if (_elsewhere is null || _elsewhereFor != ev.Id) IndexElsewhere(ev);
        return _elsewhere!.TryGetValue(f.Id, out var name) ? name : null;
    }

    private static string DealLine(Fighter f) =>
        f.Contract is { FightsRemaining: <= 1 } ? "Last fight on the deal"
        : f.Record.CurrentStreak >= 3 ? $"{f.Record.CurrentStreak} straight wins"
        : f.Record.CurrentStreak <= -3 ? $"Lost {-f.Record.CurrentStreak} in a row"
        : "";

    private void PickLadder(LadderEntry? e)
    {
        if (e is null || !e.Available) return;
        if (SelectedRed is null)
        {
            SelectedRed = RedOptions.FirstOrDefault(o => o.Id == e.FighterId);
            OnPropertyChanged(nameof(HasRedPick));
        }
        else
        {
            SelectedBlue = BlueOptions.FirstOrDefault(o => o.Id == e.FighterId);
            OnPropertyChanged(nameof(HasPairing));
        }
    }

    private void ReplaceWith(LadderEntry? e)
    {
        if (_world is null || SelectedEvent is null || e is null || _replaceFight is not { } fid || !e.Available) return;
        var ev = SelectedEvent.Model;
        var fight = ev.Card.FirstOrDefault(f => f.Id == fid);
        if (fight is null) return;
        var outgoing = _replaceBlue ? fight.BlueCorner : fight.RedCorner;
        string outName = _world.Get(outgoing)?.LastName ?? "?";
        if (_booking.ReplaceFighter(_world, ev, fid, outgoing, e.FighterId, out var err, out _, out var note))
        {
            Rail = DeskRail.Night;
            AfterCardChange();
            Ok($"{_world.Get(e.FighterId)?.LastName} replaces {outName}." + (note is null ? "" : " " + note));
        }
        else Err(err ?? "Couldn't make that swap.");
    }

    private void BookProposal(ProposalCard? c)
    {
        if (_world is null || SelectedEvent is null || c is null) return;
        var ev = SelectedEvent.Model;
        var s = c.Proposal;
        if (!_booking.AddBout(_world, ev, s.Red, s.Blue, s.Rounds, s.TitleFight, out var err)) { Err(err ?? "Couldn't book that pairing."); return; }
        Place(ev, ev.Card[0].Id, c.Slot);
        Rail = DeskRail.Night;
        AfterCardChange();
        Ok($"{c.Pairing} is booked{SlotWords(c.Slot)}.");
    }

    // === Bulk proposals: review, untick, book =====================================

    public ObservableCollection<ProposalCard> Proposed { get; } = new();
    private string _proposedTitle = "";
    public string ProposedTitle { get => _proposedTitle; private set => Set(ref _proposedTitle, value); }
    private string _proposedHint = "";
    public string ProposedHint { get => _proposedHint; private set => Set(ref _proposedHint, value); }
    public int ProposedCount => Proposed.Count(p => p.IsChecked);
    public string BookProposedText => ProposedCount == 1 ? "Book 1 fight" : $"Book {ProposedCount} fights";
    public bool HasProposed => Proposed.Count > 0;

    internal void RaiseProposedCount()
    {
        OnPropertyChanged(nameof(ProposedCount));
        OnPropertyChanged(nameof(BookProposedText));
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>
    /// A set of pairings for the rest of the card, no fighter twice: the
    /// undercard by default, the whole card on request. The player reviews,
    /// unticks and books; nothing is booked until then.
    /// </summary>
    private void Propose(bool wholeCard)
    {
        if (_world is null || SelectedEvent is null) return;
        var w = _world;
        var ev = SelectedEvent.Model;
        Proposed.Clear();
        int n = ev.Card.Count;
        var slots = new List<BoutSlot>();
        if (wholeCard)
        {
            if (n == 0) slots.Add(BoutSlot.MainEvent);
            if (n <= 1) slots.Add(BoutSlot.CoMain);
            int mainTarget = ev.MainCardFights > 0 ? ev.MainCardFights : 5;
            for (int k = Math.Max(n, 2); k < mainTarget; k++) slots.Add(BoutSlot.MainCard);
            int total = Math.Max(CardFiller.MinCardBouts + 2, mainTarget + 3);
            for (int k = n + slots.Count; k < total; k++) slots.Add(BoutSlot.Prelim);
        }
        else
        {
            int prelims = Math.Clamp(CardFiller.MinCardBouts - n, 2, 6);
            for (int k = 0; k < prelims; k++) slots.Add(BoutSlot.Prelim);
        }

        // Greedy over the matchmaker's ranked lists: the best pairing for each
        // slot whose fighters aren't already in an earlier proposal.
        var used = new HashSet<FighterId>();
        var cache = new Dictionary<BoutSlot, IReadOnlyList<BoutProposal>>();
        foreach (var slot in slots)
        {
            if (!cache.TryGetValue(slot, out var list)) cache[slot] = list = _booking.Suggest(w, ev, slot, 60);
            var pick = list.FirstOrDefault(x => !used.Contains(x.Red) && !used.Contains(x.Blue));
            if (pick is null) continue;
            used.Add(pick.Red); used.Add(pick.Blue);
            Proposed.Add(Card(pick, slot));
        }
        ProposedTitle = wholeCard ? "The whole card, proposed" : "The undercard, proposed";
        ProposedHint = Proposed.Count == 0
            ? "The matchmaker has nobody left to pair: every ready fighter is booked."
            : $"{Proposed.Count} fight{(Proposed.Count == 1 ? "" : "s")} from fighters ready on {ev.Date:d MMM}, nobody twice. Untick any you don't want, then book the rest in one go.";
        OnPropertyChanged(nameof(HasProposed));
        RaiseProposedCount();
        Rail = DeskRail.Proposals;
    }

    private void BookProposed()
    {
        if (_world is null || SelectedEvent is null) return;
        var ev = SelectedEvent.Model;
        int booked = 0;
        var refused = new List<string>();
        foreach (var c in Proposed.Where(p => p.IsChecked).ToList())
        {
            var s = c.Proposal;
            if (_booking.AddBout(_world, ev, s.Red, s.Blue, s.Rounds, s.TitleFight, out var err))
            {
                Place(ev, ev.Card[0].Id, c.Slot);
                booked++;
            }
            else refused.Add($"{c.Pairing} ({err})");
        }
        Proposed.Clear();
        OnPropertyChanged(nameof(HasProposed));
        Rail = DeskRail.Night;
        AfterCardChange();
        if (refused.Count == 0) Ok($"{booked} fight{(booked == 1 ? "" : "s")} booked on {ev.Name}.");
        else Err($"{booked} booked; not booked: {string.Join("; ", refused)}.");
    }

    // === The room ===================================================================

    public ObservableCollection<VenueRow> Rooms { get; } = new();
    private string _roomsHint = "";
    public string RoomsHint { get => _roomsHint; private set => Set(ref _roomsHint, value); }

    private void RefreshRooms()
    {
        Rooms.Clear();
        if (_world is null || SelectedEvent is null) return;
        var ev = SelectedEvent.Model;
        var options = _booking.VenueOptions(_world, ev);
        foreach (var v in options)
        {
            string house = ev.Card.Count == 0 ? "The house depends on the main event"
                : v.ProjectedAttendance >= v.Capacity ? $"Sold out · demand about {v.Demand:N0}"
                : $"{v.ProjectedAttendance:N0} expected · {100 * v.ProjectedAttendance / Math.Max(1, v.Capacity)}% full";
            var notes = new List<string>();
            if (v.SimPick) notes.Add("The sim's pick for this card");
            if (v.TurnedAway > 0 && ev.Card.Count > 0) notes.Add($"about {v.TurnedAway:N0} turned away");
            Rooms.Add(new VenueRow
            {
                Desk = this, Id = v.Venue, Name = v.Name, Seats = $"{v.Capacity:N0} seats", Rent = $"{v.Rent} hire",
                House = house, BoxOffice = ev.Card.Count == 0 ? "" : $"{Signed(v.BoxOffice)} box office",
                BoxOfficePositive = v.BoxOffice.Amount >= 0, SimPick = v.SimPick, Selected = v.Selected,
                Note = string.Join(" · ", notes),
            });
        }
        var city = Core.Sim.Geography.WorldMap.CityOf(_world, ev) is { } cid ? _world.Get(cid)?.Name : null;
        RoomsHint = Rooms.Count == 0 ? "No rooms on file for this city."
            : $"The rooms in {city ?? "this city"}, smallest first. Box office here is tickets and PPV less staging and hire; "
              + "purses and partners are the same in every room.";
    }

    private void ChooseRoom(VenueRow? v)
    {
        if (_world is null || SelectedEvent is null || v is null) return;
        if (!_booking.SetVenue(_world, SelectedEvent.Model, v.Id, out var err)) { Err(err ?? "Couldn't move the card."); return; }
        Rail = DeskRail.Night;
        AfterCardChange();
        Ok($"{SelectedEvent.Model.Name} moves to {v.Name}.");
    }

    // === The table =================================================================

    public ObservableCollection<DeskTableRow> TableRows { get; } = new();
    private string _tableTitle = "";
    public string TableTitle { get => _tableTitle; private set => Set(ref _tableTitle, value); }

    private void RefreshTable()
    {
        TableRows.Clear();
        _elsewhere = null;
        if (_world is null) return;
        var w = _world;
        var ev = SelectedEvent?.Model;
        var lastFought = LastFought();
        var roster = w.RosterOf(w.PlayerPromotion).Where(f => f.Status != FighterStatus.Retired)
            .OrderBy(f => f.Sex).ThenBy(f => f.WeightClass).ToList();
        foreach (var group in roster.GroupBy(f => (f.Sex, f.WeightClass)))
            foreach (var f in _advisor.ByStanding(w, group))
            {
                var st = _advisor.StandingOf(w, f.Id);
                bool onCard = ev is not null && ev.Card.Any(x => x.RedCorner == f.Id || x.BlueCorner == f.Id);
                string avail = ev is null ? (f.IsMatchReady(w.CurrentDate) ? "Ready" : "Not ready")
                    : onCard ? $"On {ev.Name}" : Unavailable(f, ev) is var why && f.IsMatchReady(ev.Date) && BookedElsewhereName(f, ev) is null ? "Ready" : why;
                var notes = new List<string>();
                if (f.Contract is { FightsRemaining: <= 1 }) notes.Add("last fight on deal");
                if (f.Record.CurrentStreak >= 3) notes.Add($"{f.Record.CurrentStreak} straight");
                if (f.Record.CurrentStreak <= -3) notes.Add($"lost {-f.Record.CurrentStreak}");
                bool known = lastFought.TryGetValue(f.Id, out var lf);
                if (known && w.CurrentDate.DayNumber - lf.DayNumber > 270) notes.Add($"idle {(w.CurrentDate.DayNumber - lf.DayNumber) / 30} mo");
                int read = FighterRow.Scouted(w, f);
                var ff = f;
                TableRows.Add(new DeskTableRow
                {
                    Id = f.Id.Value,
                    Rank = st.Label,
                    RankSort = st.Kind switch { Standing.Champion => 0, Standing.Ranked => st.Rank ?? 999, _ => 1000 },
                    Name = f.FullName,
                    Division = FighterRow.DivisionLabel(f.WeightClass, f.Sex),
                    Record = f.Record.ToString(),
                    RecordSort = FighterRow.RecordKey(f.Record),
                    Read = read,
                    ReadText = ReadWords.Read(read),
                    Popularity = f.Intangibles.Popularity.Value,
                    LastFought = known ? lf.ToString("MMM yyyy") : "—",
                    LastFoughtSort = known ? lf.DayNumber : 0,
                    FightsLeft = f.Contract?.FightsRemaining ?? 0,
                    Morale = f.Morale.Value,
                    Availability = avail,
                    IsAvailable = avail == "Ready",
                    OnThisCard = onCard,
                    Notes = string.Join(" · ", notes),
                    Face = new LazyFace(ready => Art.Portraits.Thumb(ff, w, ready)),
                });
            }
        TableTitle = ev is null ? "Your roster" : $"Your roster for {ev.Date:d MMM} · {TableRows.Count(r => r.IsAvailable)} ready and unbooked";
    }

    /// <summary>Pair two fighters picked in the table: the picker opens on that pairing to compare and book.</summary>
    private void Pair(IReadOnlyList<int>? ids)
    {
        if (_world is null || SelectedEvent is null || ids is null || ids.Count != 2) { Err("Pick two fighters in the table to pair them."); return; }
        var a = _world.Get(new FighterId(ids[0]));
        var b = _world.Get(new FighterId(ids[1]));
        if (a is null || b is null) return;
        if (a.WeightClass != b.WeightClass || a.Sex != b.Sex) { Err($"{a.LastName} and {b.LastName} aren't in the same division."); return; }
        OpenPicker(_nextSlot);
        PickMode = PickMode.Division;
        SelectedDivision = Divisions.FirstOrDefault(d => d.WeightClass == a.WeightClass && d.Sex == a.Sex);
        // The better-placed fighter takes the red corner, as the matchmaker bills it.
        var first = _advisor.ByStanding(_world, new[] { a, b }).First();
        var second = first == a ? b : a;
        SelectedRed = RedOptions.FirstOrDefault(o => o.Id == first.Id);
        if (SelectedRed is null) { Err($"{first.LastName} isn't available for {SelectedEvent.Model.Name}: {Unavailable(first, SelectedEvent.Model)}."); return; }
        SelectedBlue = BlueOptions.FirstOrDefault(o => o.Id == second.Id);
        if (SelectedBlue is null) { Err($"{second.LastName} isn't available for {SelectedEvent.Model.Name}: {Unavailable(second, SelectedEvent.Model)}."); return; }
        RebuildLadder();
        Ok($"{first.LastName} vs {second.LastName}: compare them in the picker, then book.");
    }
}
