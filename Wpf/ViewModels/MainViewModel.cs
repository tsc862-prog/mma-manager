using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using MmaManager.Wpf.Art;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Living;
using MmaManager.Persistence;
using MmaManager.Wpf.Mvvm;
using RetentionService = MmaManager.Core.Sim.Contracts.RetentionService;
using MarketingService = MmaManager.Core.Sim.Org.MarketingService;

namespace MmaManager.Wpf.ViewModels;

/// <summary>
/// Tab indexes for the main TabControl — the single source of truth, matching
/// the TabItem order in MainWindow.xaml. Your-promotion tabs first (gold),
/// then the disabled WORLD divider, then the world-research tabs (blue).
/// </summary>
public static class Tab
{
    public const int Home = 0, MyRoster = 1, BookEvent = 2, Contracts = 3, Finance = 4, Org = 5, Inbox = 6;
    // 7 = the disabled "WORLD" section header. (The broadcast is not a tab —
    // watching a card opens the fight-night WatchWindow.)
    public const int Fighters = 8, Fighter = 9, Rankings = 10, WorldRankings = 11, Events = 12, News = 13, WorldMap = 14, Camps = 15;
    // Desktop-only apps (never had sidebar tabs).
    public const int Notes = 16;
    public const int Media = 17;   // the Media desk: rights deals + the in-house platform
    public const int Orgs = 18;    // the Organizations pages (every promotion in the sport)
}

/// <summary>
/// The whole application state for the observe-and-advance UI: it owns the World,
/// exposes it as bindable collections for the four screens, and drives New Game /
/// Save / Load and time advancement through the sim.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    /// <summary>Set by App when any command-line render/selftest hook is running:
    /// boot straight into the deterministic quick-start world (seed 2026) instead
    /// of showing the interactive start screen — the harness needs a game to exist
    /// before it can screenshot anything.</summary>
    public static bool HeadlessAutoBoot;

    private World _world = null!;   // null until a career starts (start screen showing)

    /// <summary>The live world, read only by the crash handler's rescue save;
    /// null while the start screen is up.</summary>
    internal World? LiveWorld => _world;

    /// <summary>The desktop shell's window manager.</summary>
    public Shell.DesktopViewModel Desktop { get; } = new();
    public Shell.DisplayPreferences Display => Shell.DisplayPreferences.Current;

    /// <summary>The navigation seam: every internal jump opens (or focuses) the
    /// matching app window on the desktop.</summary>
    public void NavigateTo(int tab) => Desktop.OpenForTab(tab);
    private readonly WorldTick _tick = new();
    private IReadOnlyDictionary<FightId, (Fight, Event)> _fightIndex = new Dictionary<FightId, (Fight, Event)>();

    public MainViewModel()
    {
        NewGameCommand = new RelayCommand(NewGame);
        SaveCommand = new RelayCommand(Save, () => _world is not null);
        LoadCommand = new RelayCommand(Load);
        QuickStartCommand = new RelayCommand(QuickStart);
        ContinueCommand = new RelayCommand(ContinueCareer, () => HasContinue);
        ExitCommand = new RelayCommand(() => System.Windows.Application.Current?.Shutdown());

        // The front door's CONTINUE: the newest save in the default directory.
        RefreshContinue();
        AdvanceWeekCommand = new RelayCommand(() => Advance(1));
        AdvanceMonthCommand = new RelayCommand(() => Advance(4));
        ViewProfileCommand = new RelayCommand(() => NavigateTo(Tab.Fighter), () => SelectedFighter is not null);
        OpenFighterCommand = new RelayCommand<int>(OpenFighter);
        OpenInboxCommand = new RelayCommand(() => NavigateTo(Tab.Inbox));
        OpenEventCommand = new RelayCommand<int>(id => { EventsPage.Select(id); NavigateTo(Tab.Events); });
        ToggleShortlistCommand = new RelayCommand<int>(ToggleShortlist);
        OpenOrgCommand = new RelayCommand<int>(id =>
        {
            OrgsPage.Select(id);
            NavigateTo(Tab.Orgs);
        });
        WatchTonightCommand = new RelayCommand(() => { if (DuePlayerEvent() is { } ev) RunLive(ev); }, () => IsFightNight);
        SimTonightCommand = new RelayCommand(SimPendingEvent, () => IsFightNight);
        RetainFighterCommand = new RelayCommand(RetainSelected, () => CanRetainSelected);
        NegotiateFighterCommand = new RelayCommand<int>(NegotiateFighter);
        ToggleMarketingCommand = new RelayCommand(ToggleMarketingSelected, () => SelectedIsMine);
        ToggleCampStipendCommand = new RelayCommand(ToggleCampStipendSelected, () => SelectedIsMine);
        MakeFaceCommand = new RelayCommand(MakeFaceSelected, () => SelectedIsMine);
        ClearStatusCommand = new RelayCommand(() => StatusText = "");
        Desktop.ScreenContext = this;       // app windows host the same screens the tabs do
        Contracts.OnChanged = RefreshAll;   // a signing/release moves the balance and roster
        Org.OnChanged = RefreshAll;         // hiring/firing changes staff and overhead
        Camps.OnChanged = RefreshAll;       // a team deal / camp report moves the balance
        Booking.OnRunLive = RunLive;        // "run & watch" a card
        Booking.OpenContracts = () => NavigateTo(Tab.Contracts);   // empty roster: go sign someone
        Booking.SimTonightCommand = SimTonightCommand;             // fight night's second door, on the card's header
        // Destructive booking actions ask first — in the interactive app only;
        // harness runs never open a dialog (null = go ahead as scripted).
        if (!HeadlessAutoBoot)
            Booking.Confirm = (heading, body, detail, confirm) =>
                MessageWindow.Ask(heading, body, detail, confirm, keep: "Keep the event");
        Dashboard.Navigate = NavigateTo;                  // home-page shortcuts jump to their screen
        Dashboard.GetWorld = () => _world;                // the desk acts on the live world
        Dashboard.OnDecisionMade = RefreshAll;            // a decision moves loyalty/rankings/money
        Dashboard.WatchTonightCommand = WatchTonightCommand;   // home "needs you": fight night's two doors
        Dashboard.SimTonightCommand = SimTonightCommand;
        Dashboard.NegotiateCommand = NegotiateFighterCommand;  // home "needs you": re-sign a fighter
        Dashboard.OpenFighterCommand = OpenFighterCommand;
        Dashboard.IsFightNight = () => IsFightNight;
        MyRoster.OpenContracts = () => NavigateTo(Tab.Contracts);   // the empty gym's way out
        AppCommands.OpenFighter = OpenFighterCommand;             // what every card template binds
        AppCommands.ToggleShortlist = ToggleShortlistCommand;
        AppCommands.OpenOrg = OpenOrgCommand;
        AppCommands.OpenEvent = OpenEventCommand;
        News.OpenFighterCommand = OpenFighterCommand;             // follow a story to its people
        News.OpenOrgCommand = OpenOrgCommand;
        News.OpenEventCommand = OpenEventCommand;
        WireMail();

        RankingDivisions = new ObservableCollection<DivisionPick>(
            Enum.GetValues<WeightClass>().Select(w => new DivisionPick(w, Sex.Male))
                .Concat(new[] { WeightClass.Strawweight, WeightClass.Flyweight, WeightClass.Bantamweight }
                    .Select(w => new DivisionPick(w, Sex.Female))));

        // Headless render/selftest runs boot straight into the deterministic
        // quick-start world; the interactive app shows the start screen instead
        // and waits for New Career / Load Career / Quick Start.
        if (HeadlessAutoBoot)
            StartWorld(new WorldBuilder.Options { Seed = 2026 },
                "Quick start — you run the Apex Fighting Championship. Hit New Game to found your own promotion.");
    }

    /// <summary>
    /// The inbox acts inside its letters through commands that already exist —
    /// the desk's decisions (Home), the sponsor and media offers (Finance, Media),
    /// contract talks, and the tier step-up (HQ). Nothing new is decided here.
    /// </summary>
    private void WireMail()
    {
        Mail.AcceptDecisionCommand = Dashboard.AcceptDecisionCommand;   // refreshes the app itself
        Mail.DeclineDecisionCommand = Dashboard.DeclineDecisionCommand;
        Mail.AcceptSponsorCommand = Finance.AcceptOfferCommand;
        Mail.DeclineSponsorCommand = Finance.DeclineOfferCommand;
        Mail.AcceptMediaCommand = Media.AcceptOfferCommand;
        Mail.DeclineMediaCommand = Media.DeclineOfferCommand;
        Mail.NegotiateCommand = NegotiateFighterCommand;
        Mail.PromoteCommand = Org.AcceptPromotionCommand;                 // Org.OnChanged refreshes
        Mail.Navigate = NavigateTo;
        Mail.AfterAction = RefreshAll;
        if (!HeadlessAutoBoot)
            Mail.Confirm = (heading, body, detail, confirm) =>
                MessageWindow.Ask(heading, body, detail, confirm, keep: "Not yet");
    }

    /// <summary>Start-screen shortcut: a fresh random world with you at the helm of the AFC.</summary>
    private void QuickStart() =>
        StartWorld(new WorldBuilder.Options { Seed = (ulong)Random.Shared.Next(1, 1_000_000) },
            "Quick start — you run the Apex Fighting Championship, the biggest stage in the sport.");

    // --- Commands -----------------------------------------------------------
    public ICommand NewGameCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand LoadCommand { get; }

    /// <summary>Start-screen doors: resume the latest save, jump into a fresh world, or leave.</summary>
    public ICommand QuickStartCommand { get; }
    public ICommand ContinueCommand { get; }
    public ICommand ExitCommand { get; }

    private bool _hasContinue;
    /// <summary>A save exists in the default directory — the front door offers CONTINUE.</summary>
    public bool HasContinue { get => _hasContinue; private set => Set(ref _hasContinue, value); }

    private string _continueDetail = "";
    public string ContinueDetail { get => _continueDetail; private set => Set(ref _continueDetail, value); }

    private void ContinueCareer()
    {
        if (SaveLocator.MostRecent() is { } recent) LoadFromPath(recent.FullName);
    }

    private bool _isGameStarted;
    /// <summary>False until a career starts — the start screen shows over the game shell.</summary>
    public bool IsGameStarted { get => _isGameStarted; private set => Set(ref _isGameStarted, value); }
    public ICommand AdvanceWeekCommand { get; }
    public ICommand AdvanceMonthCommand { get; }
    public ICommand ViewProfileCommand { get; }

    /// <summary>Jump to any fighter's profile from anywhere (parameter = fighter id).</summary>
    public ICommand OpenFighterCommand { get; }

    /// <summary>The tray envelope: open the mail app.</summary>
    public ICommand OpenInboxCommand { get; }

    /// <summary>Jump to an event's page from anywhere (parameter = event id).</summary>
    public ICommand OpenEventCommand { get; }

    /// <summary>Pin/unpin a fighter on the shortlist from anywhere (parameter = fighter id).</summary>
    public ICommand ToggleShortlistCommand { get; }

    /// <summary>Open a promotion's page (Rankings & Champions) from anywhere (parameter = promotion id).</summary>
    public ICommand OpenOrgCommand { get; }

    /// <summary>The profile's shortlist toggle, labelled for the selected fighter.</summary>
    public string ShortlistButtonText =>
        SelectedFighter?.Model is { } sf && _world is not null && _world.Shortlist.Contains(sf.Id)
            ? "★ On the shortlist — remove"
            : "☆ Add to shortlist";

    private void ToggleShortlist(int fighterId)
    {
        if (_world is null || _world.Get(new FighterId(fighterId)) is not { } f) return;
        if (_world.Shortlist.Remove(f.Id))
            StatusText = $"{f.FullName} taken off the shortlist.";
        else
        {
            _world.Shortlist.Add(f.Id);
            StatusText = $"☆ {f.FullName} pinned to the shortlist — manage it in Notes.";
        }
        Notes.Attach(_world);
        FightersPage.ShortlistChanged();
        OnPropertyChanged(nameof(ShortlistButtonText));
    }

    /// <summary>Open contract talks from a profile (parameter = fighter id; free agents and own roster).</summary>
    public ICommand NegotiateFighterCommand { get; }

    /// <summary>Front-office spending on the selected fighter (only your own roster).</summary>
    public ICommand RetainFighterCommand { get; }
    public ICommand ToggleMarketingCommand { get; }
    public ICommand ToggleCampStipendCommand { get; }
    public ICommand MakeFaceCommand { get; }

    /// <summary>Fight night's two doors: the broadcast, or straight to the results.</summary>
    public ICommand WatchTonightCommand { get; }
    public ICommand SimTonightCommand { get; }

    /// <summary>Open the Fighter tab on the given fighter, wherever they were clicked.</summary>
    public void OpenFighter(int fighterId)
    {
        if (_world.Get(new FighterId(fighterId)) is not { } f) return;
        SelectedFighter = new FighterRow(f, _world, _fightIndex);
        MyRoster.SelectedFighterId = fighterId;   // the roster remembers who was opened last
        FightersPage.SelectedFighterId = fighterId;   // and so does the world database
        NavigateTo(Tab.Fighter);
    }

    /// <summary>
    /// Open contract talks with a fighter straight from their profile: lands on
    /// the Contracts screen with them across the table (free agents and our own
    /// roster — anyone else is under someone else's paper).
    /// </summary>
    private void NegotiateFighter(int fighterId)
    {
        var id = new FighterId(fighterId);
        if (Contracts.OpenTalksWith(id))
        {
            NavigateTo(Tab.Contracts);
        }
        else if (_world.Get(id) is { Contract: { } c } f && _world.Get(c.Promotion) is { } theirs)
        {
            StatusText = $"Can't open talks with {f.FullName}: under contract with {theirs.Abbreviation}. Wait for the deal to run out, then bid.";
        }
    }

    /// <summary>The home page (your promotion at a glance).</summary>
    public DashboardViewModel Dashboard { get; } = new();

    /// <summary>The Book Event screen (player matchmaking).</summary>
    public BookingViewModel Booking { get; } = new();

    /// <summary>The Contracts screen (free agency, renewals, releases).</summary>
    public ContractsViewModel Contracts { get; } = new();

    /// <summary>The Finance screen (ledger, P&amp;L, sponsors).</summary>
    public FinanceViewModel Finance { get; } = new();

    /// <summary>The Media desk (rights deals, offers, the in-house platform).</summary>
    public MediaViewModel Media { get; } = new();

    /// <summary>The News feed.</summary>
    public NewsViewModel News { get; } = new();

    /// <summary>The player's inbox.</summary>
    public MailViewModel Mail { get; } = new();

    /// <summary>The org screen (staff + objectives).</summary>
    public OrgViewModel Org { get; } = new();

    /// <summary>Cross-org world rankings (P4P + per division).</summary>
    public WorldRankingsViewModel WorldRankings { get; } = new();

    public WorldMapViewModel WorldMap { get; } = new();

    /// <summary>The Camps tab (every room in the sport + team deals and camp reports).</summary>
    public CampsViewModel Camps { get; } = new();

    /// <summary>The Roster screen (your fighters with the front-office columns).</summary>
    public MyRosterViewModel MyRoster { get; } = new();

    /// <summary>The Fighters app: every fighter in the world, searchable.</summary>
    public FightersViewModel FightersPage { get; } = new();

    /// <summary>The Fighters app's current matches (best first by the scout's read) — the harness reads these.</summary>
    public ObservableCollection<FighterRow> Roster => FightersPage.Rows;

    /// <summary>The Events screen (world card list + full event pages).</summary>
    public EventsViewModel EventsPage { get; } = new();

    /// <summary>The Notes app (the owner's pad + the fighter shortlist).</summary>
    public NotesViewModel Notes { get; } = new();

    /// <summary>The Organizations app (full org pages for every promotion).</summary>
    public OrgsViewModel OrgsPage { get; } = new();

    /// <summary>The live broadcast of a just-run card.</summary>
    public WatchViewModel Watch { get; } = new();

    

    // --- Header -------------------------------------------------------------
    private string _title = "PromoterOS";
    public string Title { get => _title; private set => Set(ref _title, value); }

    private string _dateText = "";
    public string DateText { get => _dateText; private set => Set(ref _dateText, value); }

    private string _balanceText = "";
    public string BalanceText { get => _balanceText; private set => Set(ref _balanceText, value); }

    private string _statusText = "";
    /// <summary>
    /// The latest result of something the player did (shown in the shell's
    /// result strip above the taskbar). Every set raises a change, even for the
    /// same text, so a repeated result still flashes the strip. A plain set is
    /// a routine result; <see cref="Alert"/> marks a must-not-miss one.
    /// </summary>
    public string StatusText
    {
        get => _statusText;
        private set
        {
            _statusText = value ?? "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasStatus));
            StatusIsAlert = false;
        }
    }

    public bool HasStatus => _statusText.Length > 0;

    private bool _statusIsAlert;
    /// <summary>True when the strip is showing a must-not-miss result (game over, failed save/load).</summary>
    public bool StatusIsAlert { get => _statusIsAlert; private set => Set(ref _statusIsAlert, value); }

    public ICommand ClearStatusCommand { get; }

    /// <summary>
    /// A result the player must not miss: it goes on the strip (in red) and, in
    /// the interactive app, also opens a blocking message window. Headless
    /// harness runs never open a dialog.
    /// </summary>
    internal void Alert(string heading, string strip, string body, string? detail = null)
    {
        StatusText = strip;
        StatusIsAlert = true;
        if (HeadlessAutoBoot) return;
        MessageWindow.ShowOver(heading, body, detail, severe: true);
    }

    private ImageSource? _playerLogo;
    public ImageSource? PlayerLogo { get => _playerLogo; private set => Set(ref _playerLogo, value); }

    // --- Sidebar identity: who you are, worn on the shell -------------------
    private string _orgName = "";
    public string OrgName { get => _orgName; private set => Set(ref _orgName, value); }

    private string _orgTierLine = "";
    /// <summary>The climb at a glance ("National · 46% to Continental").</summary>
    public string OrgTierLine { get => _orgTierLine; private set => Set(ref _orgTierLine, value); }

    private int _orgTierProgress;
    public int OrgTierProgress { get => _orgTierProgress; private set => Set(ref _orgTierProgress, value); }

    private string _lastAdvance = "New game started.";
    public string LastAdvance { get => _lastAdvance; private set => Set(ref _lastAdvance, value); }

    // --- Collections --------------------------------------------------------
    public ObservableCollection<PromotionRow> Promotions { get; } = new();
    public ObservableCollection<RankRow> Standings { get; } = new();

    public ObservableCollection<DivisionPick> RankingDivisions { get; }

    // --- Selections ---------------------------------------------------------
    private FighterRow? _selectedFighter;
    public FighterRow? SelectedFighter
    {
        get => _selectedFighter;
        set
        {
            if (!Set(ref _selectedFighter, value)) return;
            OnPropertyChanged(nameof(HasSelectedFighter));
            OnPropertyChanged(nameof(SelectedIsMine));
            OnPropertyChanged(nameof(RetentionButtonText));
            OnPropertyChanged(nameof(MarketingButtonText));
            OnPropertyChanged(nameof(CampButtonText));
            OnPropertyChanged(nameof(FaceButtonText));
            OnPropertyChanged(nameof(ShortlistButtonText));
        }
    }
    public bool HasSelectedFighter => _selectedFighter is not null;

    // --- Front office: spend on the selected fighter (own roster only) -------
    /// <summary>Is the selected fighter under contract to the player's promotion?</summary>
    public bool SelectedIsMine =>
        SelectedFighter?.Model is { } f && _world is not null && f.Contract?.Promotion == _world.PlayerPromotion;

    private bool CanRetainSelected =>
        _world is not null && SelectedFighter?.Model is { } f && _world.Get(_world.PlayerPromotion) is { } promo
        && RetentionService.CanPay(_world, promo, f, out _);

    /// <summary>Label for the retention-bonus button — the price/loyalty on offer, or why you can't.</summary>
    public string RetentionButtonText
    {
        get
        {
            if (_world is null || SelectedFighter?.Model is not { } f) return "Pay retention bonus";
            if (f.Loyalty >= 100) return "Fully loyal — no bonus needed";
            return $"Pay retention bonus — {RetentionService.Quote(f)} (+{RetentionService.LoyaltyGain(f)} loyalty, now {f.Loyalty})";
        }
    }

    /// <summary>Label for the marketing-push toggle — start (with cost) or stop.</summary>
    public string MarketingButtonText
    {
        get
        {
            if (SelectedFighter?.Model is not { } f) return "Marketing push";
            return f.MarketingPush
                ? "Stop marketing push"
                : $"Start marketing push — {MarketingService.MonthlyCost(f)}/mo";
        }
    }

    private void RetainSelected()
    {
        if (_world is null || SelectedFighter?.Model is not { } f || _world.Get(_world.PlayerPromotion) is not { } promo) return;
        StatusText = RetentionService.TryPayBonus(_world, promo, f, out var reason)
            ? $"Locked down {f.FullName} with a retention bonus — loyalty up to {f.Loyalty}."
            : $"No retention bonus for {f.FullName}: {reason}";
        RefreshAll();
    }

    private void ToggleMarketingSelected()
    {
        if (_world is null || SelectedFighter?.Model is not { } f) return;
        f.MarketingPush = !f.MarketingPush;
        StatusText = f.MarketingPush
            ? $"Marketing push started on {f.FullName} — {MarketingService.MonthlyCost(f)}/mo to build their name."
            : $"Marketing push on {f.FullName} ended.";
        RefreshAll();
    }

    /// <summary>Label for the face-of-the-company toggle — crown them, or step back from the bet.</summary>
    public string FaceButtonText
    {
        get
        {
            if (_world is null || SelectedFighter?.Model is not { } f) return "Face of the company";
            if (_world.Get(_world.PlayerPromotion)?.FaceOfCompany == f.Id) return "📣 Our face — step back from the bet";
            return "Make the face of the company — their fame feeds the brand (and their asking price)";
        }
    }

    private void MakeFaceSelected()
    {
        if (_world is null || SelectedFighter?.Model is not { } f || _world.Get(_world.PlayerPromotion) is not { } promo) return;
        if (promo.FaceOfCompany == f.Id)
        {
            promo.FaceOfCompany = null;
            StatusText = $"{f.FullName} is no longer the face of the company.";
        }
        else
        {
            MmaManager.Core.Sim.Org.Franchise.SetFace(_world, promo, f, _world.CurrentDate);
            StatusText = $"{f.FullName} is the face of the company — the brand (and the partners) run through them now.";
        }
        RefreshAll();
    }

    /// <summary>Label for the camp-stipend toggle — the move on offer, or the fee being paid.</summary>
    public string CampButtonText
    {
        get
        {
            if (_world is null || SelectedFighter?.Model is not { } f) return "Camp stipend";
            if (f.CampStipend && f.Camp is { } cur && _world.Get(cur) is { } room)
                return $"Stop camp stipend ({room.Name}, {room.MonthlyFee}/mo)";
            return "Pay camp stipend — move them to a better room (org pays the fee, they remember it)";
        }
    }

    private void ToggleCampStipendSelected()
    {
        if (_world is null || SelectedFighter?.Model is not { } f) return;
        if (f.CampStipend)
        {
            MmaManager.Core.Sim.Living.Camps.StopStipend(_world, f);
            StatusText = $"Stopped paying {f.FullName}'s camp fees — they're back on the local scene.";
        }
        else
        {
            var promo = _world.Get(_world.PlayerPromotion);
            var room = promo is null ? null : MmaManager.Core.Sim.Living.Camps.PayStipend(_world, promo, f);
            StatusText = room is null
                ? $"No better room has space for {f.FullName} right now."
                : $"{f.FullName} moves to {room.Name} ({room.MonthlyFee}/mo on our books) — loyalty up to {f.Loyalty}.";
        }
        RefreshAll();
    }

    private DivisionPick _rankingDivision = new(WeightClass.Lightweight, Sex.Male);
    public DivisionPick RankingDivision { get => _rankingDivision; set { if (value is not null && Set(ref _rankingDivision, value)) RefreshStandings(); } }

    // Which promotion's belts/rankings to show — every org has its own.
    public ObservableCollection<Promotion> Organizations { get; } = new();

    private Promotion? _selectedOrg;
    public Promotion? SelectedOrg { get => _selectedOrg; set { if (Set(ref _selectedOrg, value)) RefreshStandings(); } }

    private string _championText = "";
    public string ChampionText { get => _championText; private set => Set(ref _championText, value); }

    private string _championReign = "";
    public string ChampionReign { get => _championReign; private set => Set(ref _championReign, value); }

    private ImageSource? _championPortrait;
    public ImageSource? ChampionPortrait { get => _championPortrait; private set => Set(ref _championPortrait, value); }

    private int _championId;
    public int ChampionId { get => _championId; private set => Set(ref _championId, value); }

    /// <summary>The selected promotion's popularity by region (shown on the Rankings screen).</summary>
    public ObservableCollection<RatingBar> OrgRegions { get; } = new();

    private string _selectedOrgBio = "";
    public string SelectedOrgBio { get => _selectedOrgBio; private set => Set(ref _selectedOrgBio, value); }

    // --- Actions ------------------------------------------------------------
    private void NewGame()
    {
        var dialog = new NewGameWindow();
        if (System.Windows.Application.Current?.MainWindow is { IsLoaded: true } owner)
            dialog.Owner = owner;
        if (dialog.ShowDialog() != true || dialog.Result is null) return;   // cancelled — keep the current world
        StartWorld(dialog.Result, dialog.Summary);

        // A founded promotion starts with an empty gym — land on the free-agent
        // market so the first move (hiring a roster) is right in front of you.
        if (dialog.Result.Founding is not null)
        {
            NavigateTo(Tab.Contracts);
            StatusText = $"{OrgName} is open for business with an empty roster — sign fighters from the free-agent market, then book your first card.";
        }
    }

    // The art pack as a portrait-first generation catalog, built once.
    private static Core.Sim.Generation.IdentityCatalog? _artCatalog;
    private static Core.Sim.Generation.IdentityCatalog? ArtCatalog =>
        _artCatalog ??= Art.PaintedLibrary.BuildCatalog();

    /// <summary>Build a fresh world from <paramref name="options"/> and rebind every screen to it.</summary>
    public void StartWorld(WorldBuilder.Options options, string summary)
    {
        // Generate fighters to match real portraits from the pack.
        _world = new WorldBuilder().Build(options with { IdentityCatalog = ArtCatalog });
        LastAdvance = summary;
        StatusText = "";   // the last career's results don't carry over
        IsGameStarted = true;
        RefreshAll();
        BeginSession();
    }

    private void Advance(int weeks)
    {
        if (_world is null) return;
        int unreadBefore = _world.Mail.Count(m => !m.Read);

        // Fight night is a hard stop: a due card must be watched or simmed
        // before time moves on — no skipping past your own show.
        if (DuePlayerEvent() is { } due)
        {
            Booking.SelectEvent(due.Id);
            NavigateTo(Tab.BookEvent);
            StatusText = $"▶ Time can't move on: {due.Name} is tonight. Use Watch Live or Sim Result in the red fight-night bar first.";
            return;
        }

        // A new stretch of time: the last action's result is old news (the
        // advance itself is summarised in the tray).
        StatusText = "";

        var r = _tick.AdvanceWeeksStoppingForPlayer(_world, weeks);
        LastAdvance =
            $"Advanced to {r.Date:d MMM yyyy}: {r.EventsRun} events, {r.Fights} fights " +
            $"({(r.Fights == 0 ? 0 : 100.0 * r.Finishes / r.Fights):0.0}% finishes), " +
            $"{r.Injuries} injuries, {r.Retired} retired, {r.Newgens} prospects in, {r.FreeAgentSignings} signed" +
            (r.Cuts > 0 ? $", {r.Cuts} cut" : "") +
            (r.LocalFights > 0 ? $", {r.LocalFights} local-circuit fights." : ".");
        RefreshAll();

        // Bankruptcy is the end of the road (no autosave — CONTINUE keeps the
        // last world that was still alive).
        if (_world.Get(_world.PlayerPromotion) is { Defunct: true } dead)
        {
            Alert("GAME OVER",
                $"GAME OVER — {dead.Name} is bankrupt. Start menu → New Game for a new career, or Load an earlier save.",
                $"{dead.Name} has gone bankrupt and closed its doors. Your career with it is over.",
                "The collapse was not autosaved, so your saves (and Continue) still hold the career as it was before it. " +
                "Start menu → New Game begins a new career; Load picks up an earlier save.");
            return;
        }
        Autosave();

        // New mail lands as a toast in the notification corner.
        if (!HeadlessAutoBoot)
        {
            int newMail = _world.Mail.Count(m => !m.Read) - unreadBefore;
            if (newMail > 0)
                Desktop.Notify("✉", $"{newMail} new message{(newMail == 1 ? "" : "s")}",
                    _world.Mail.LastOrDefault(m => !m.Read)?.Subject ?? "",
                    () => NavigateTo(Tab.Inbox));
        }

        // One of your cards lost its main event — time stops for the decision.
        if (r.MainEventLost is { } hit)
        {
            Booking.SelectEvent(hit.Id);
            NavigateTo(Tab.BookEvent);
            StatusText = $"▶ Time stopped: {hit.Name} lost its main event. Your inbox has the details; book a new headliner.";
        }

        // Reached one of your cards — the advance force-pauses on fight night.
        if (r.PendingPlayerEvent is { } pending)
        {
            Booking.SelectEvent(pending.Id);
            NavigateTo(Tab.BookEvent);
            StatusText = $"▶ Time stopped: {pending.Name} is tonight. Use Watch Live or Sim Result in the red fight-night bar.";
        }
    }

    /// <summary>The player card whose night has arrived (time can't move past it).</summary>
    private Core.Model.Event? DuePlayerEvent() => _world?.Events.Values
        .Where(e => e.Status == EventStatus.Scheduled && e.Promotion == _world.PlayerPromotion && e.Date <= _world.CurrentDate)
        .OrderBy(e => e.Date).ThenBy(e => e.Id.Value)
        .FirstOrDefault();

    private bool _isFightNight;
    /// <summary>True while a due player card is waiting on the watch/sim decision.</summary>
    public bool IsFightNight { get => _isFightNight; private set => Set(ref _isFightNight, value); }

    private string _fightNightLine = "";
    public string FightNightLine { get => _fightNightLine; private set => Set(ref _fightNightLine, value); }

    // Sim tonight's card without the broadcast and land on the results.
    private void SimPendingEvent()
    {
        if (DuePlayerEvent() is not { } ev) return;
        _tick.RunEventLive(_world, ev, playByPlay: false);
        RefreshAll();
        Autosave();
        EventsPage.Select(ev.Id.Value);
        NavigateTo(Tab.Events);
        var fin = ev.Financials;
        StatusText = fin is null
            ? $"{ev.Name} is in the books — results are on the event page."
            : $"{ev.Name} is in the books — house {fin.Attendance:N0}, gate {fin.GateRevenue}.";
    }

    // Run one of the player's booked cards now, with commentary, and open the
    // fight-night broadcast window over the shell.
    private void RunLive(Core.Model.Event ev)
    {
        _tick.RunEventLive(_world, ev);
        Watch.Load(_world, ev);
        RefreshAll();
        Autosave();

        // Headless harness runs drive Watch state directly (no window to show).
        if (HeadlessAutoBoot) return;

        var win = new WatchWindow { DataContext = this };
        if (System.Windows.Application.Current?.MainWindow is { IsLoaded: true } owner)
            win.Owner = owner;
        Watch.Start();
        win.ShowDialog();
        Watch.Pause();   // leaving the arena stops the broadcast clock
    }

    private void Save()
    {
        if (_world is not { } world) return;

        // Default the save name to the org so a career's saves sort together.
        string name = world.Get(world.PlayerPromotion)?.Abbreviation.ToLowerInvariant() is { Length: > 0 } abbr
            ? $"{abbr}-career" : "career";
        var dialog = new SaveFileDialog
        {
            Filter = "MMA save (*.mmasave)|*.mmasave", DefaultExt = ".mmasave",
            FileName = name, InitialDirectory = SaveLocator.Ensure(),
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            SaveGame.Save(world, dialog.FileName);
            StatusText = $"Career saved to {Path.GetFileName(dialog.FileName)}.";
            RefreshContinue();
        }
        catch (Exception ex)
        {
            Alert("SAVE FAILED",
                $"Save failed — {Path.GetFileName(dialog.FileName)} was not written: {ex.Message}",
                $"The career was not saved to {Path.GetFileName(dialog.FileName)}. Nothing in the game has changed; try saving again, to another name or folder.",
                ex.Message);
        }
    }

    /// <summary>
    /// A safety-net save after every advance and every settled fight night,
    /// written silently to autosave.mmasave in the default save directory —
    /// so CONTINUE always has tonight's world even if the player never saves.
    /// </summary>
    private void Autosave()
    {
        // Headless render/selftest runs advance time too — never let the
        // harness write into the player's real save directory.
        if (_world is null || HeadlessAutoBoot) return;
        try
        {
            SaveGame.Save(_world, Path.Combine(SaveLocator.Ensure(), "autosave.mmasave"));
            RefreshContinue();
        }
        catch { /* an autosave must never interrupt play */ }
    }

    private void RefreshContinue()
    {
        if (SaveLocator.MostRecent() is not { } recent) return;
        HasContinue = true;
        ContinueDetail = $"{Path.GetFileNameWithoutExtension(recent.Name)}  ·  saved {recent.LastWriteTime:d MMM yyyy, HH:mm}";
    }

    private void Load()
    {
        var picker = new LoadGameWindow();
        if (System.Windows.Application.Current?.MainWindow is { IsLoaded: true } owner)
            picker.Owner = owner;
        if (picker.ShowDialog() != true || picker.ChosenPath is null) return;
        LoadFromPath(picker.ChosenPath);
    }

    private void LoadFromPath(string path)
    {
        try
        {
            _world = SaveGame.Load(path);
            _world.ArtCatalog = ArtCatalog;                      // re-attach for future newgens
            LastAdvance = $"Loaded {Path.GetFileName(path)}.";
            StatusText = "";   // the last career's results don't carry over
            IsGameStarted = true;
            RefreshAll();
            BeginSession();
        }
        catch (Exception ex)
        {
            StartScreenError = IsGameStarted ? "" : $"Couldn't load {Path.GetFileName(path)} — {ex.Message}";
            Alert("LOAD FAILED",
                $"Load failed — {Path.GetFileName(path)} could not be opened: {ex.Message}",
                $"{Path.GetFileName(path)} could not be loaded. It may be damaged or from an older version of the game " +
                "(saves don't carry across versions). Load another save, or start a new career.",
                ex.Message);
        }
    }

    private string _startScreenError = "";
    /// <summary>A load failure surfaced on the start screen (the status strip is hidden there).</summary>
    public string StartScreenError { get => _startScreenError; private set => Set(ref _startScreenError, value); }

    // --- The sign-in moment (the OS boots, then the day begins) -------------
    private bool _isBooting;
    /// <summary>True during the brief PromoterOS sign-in interstitial.</summary>
    public bool IsBooting { get => _isBooting; private set => Set(ref _isBooting, value); }

    private string _bootLine = "";
    public string BootLine { get => _bootLine; private set => Set(ref _bootLine, value); }

    private void BeginSession()
    {
        // The harness has no patience for theatre.
        if (HeadlessAutoBoot) { Desktop.Open("home"); return; }

        IsBooting = true;
        BootLine = $"Signing in — the desk of {OrgName}";
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.9) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            IsBooting = false;
            Desktop.Open("home");
            MorningBriefing();
        };
        timer.Start();
    }

    /// <summary>The first toast of the day: what's waiting on the desk.</summary>
    private void MorningBriefing()
    {
        if (_world is null) return;
        int unread = _world.Mail.Count(m => !m.Read);
        var next = _world.Events.Values
            .Where(e => e.Status == EventStatus.Scheduled && e.Promotion == _world.PlayerPromotion)
            .OrderBy(e => e.Date).FirstOrDefault();
        string agenda = next is null
            ? "No card on the calendar — the cage is waiting."
            : $"Next card: {next.Name}, {Math.Max(0, next.Date.DayNumber - _world.CurrentDate.DayNumber)} days out.";
        Desktop.Notify("☀", "Good morning, boss",
            $"{(unread > 0 ? $"{unread} unread message{(unread == 1 ? "" : "s")}. " : "")}{agenda}",
            () => NavigateTo(unread > 0 ? Tab.Inbox : Tab.BookEvent));
    }

    /// <summary>The sticky note on the desk: what actually needs doing.</summary>
    public ObservableCollection<string> DeskNotes { get; } = new();

    private void RefreshDeskNotes(Promotion? promo)
    {
        DeskNotes.Clear();
        if (_world is null || promo is null) return;

        var next = _world.Events.Values
            .Where(e => e.Status == EventStatus.Scheduled && e.Promotion == promo.Id)
            .OrderBy(e => e.Date).FirstOrDefault();
        DeskNotes.Add(next is null
            ? "book the next card!"
            : $"{next.Name} — {Math.Max(0, next.Date.DayNumber - _world.CurrentDate.DayNumber)} days out");

        int expiring = _world.RosterOf(promo.Id).Count(f =>
            f.Status == FighterStatus.Active && f.Contract is { FightsRemaining: <= 1 });
        if (expiring > 0) DeskNotes.Add($"{expiring} deal{(expiring == 1 ? "" : "s")} running out — re-sign or lose them");

        int unread = _world.Mail.Count(m => !m.Read);
        if (unread > 0) DeskNotes.Add($"{unread} unread message{(unread == 1 ? "" : "s")}");

        if (promo.Balance.Amount < 0) DeskNotes.Add("the books are IN THE RED");
        if (DeskNotes.Count == 1 && next is not null) DeskNotes.Add("all quiet — build something");
    }

    // --- Refresh ------------------------------------------------------------
    private void RefreshAll()
    {
        if (_world is null) return;
        var player = _world.Get(_world.PlayerPromotion);
        Title = player is null ? "PromoterOS" : $"PromoterOS — {player.Name}";
        PlayerLogo = player is null ? null : LogoFactory.ForPromotion(player);
        DateText = _world.CurrentDate.ToString("dddd, d MMMM yyyy");
        BalanceText = player?.Balance.ToString() ?? "—";

        // The sidebar wears the org: name, the climb, and the org's signature
        // accent colour (the same one that dresses its posters) pushed into the
        // app resources so every {DynamicResource OrgAccent} follows the player.
        OrgName = player?.Name ?? "";
        if (player is not null)
        {
            var standing = Core.Sim.Org.OrgSizing.Progress(player);
            OrgTierProgress = standing.Percent;
            OrgTierLine = standing.Next is { } next
                ? $"{Core.Sim.Org.OrgSizing.Label(standing.Tier)} · {standing.Percent}% to {Core.Sim.Org.OrgSizing.Label(next)}"
                : $"{Core.Sim.Org.OrgSizing.Label(standing.Tier)} · top of the sport";
        }
        if (System.Windows.Application.Current is { } app)
        {
            var accent = PosterFactory.AccentFor(player);
            var brush = new SolidColorBrush(accent); brush.Freeze();
            var dim = new SolidColorBrush(Color.FromArgb(0x46, accent.R, accent.G, accent.B)); dim.Freeze();
            app.Resources["OrgAccent"] = brush;
            app.Resources["OrgAccentDim"] = dim;
        }
        // (StatusText is the player's last result — a refresh must never
        // overwrite it, or every action's message is lost the moment it lands.)

        Promotions.Clear();
        Organizations.Clear();
        foreach (var p in _world.Promotions.Values.Where(p => !p.Defunct).OrderByDescending(p => p.Prestige.Value))
        {
            Promotions.Add(new PromotionRow(p, _world));
            Organizations.Add(p);
        }
        // Default the rankings view to the player's promotion (set the backing
        // field so we don't refresh standings twice during RefreshAll).
        _selectedOrg = _world.Get(_world.PlayerPromotion) ?? Organizations.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedOrg));

        // Stamp portrait personas onto the model: the marketable look baked into
        // each fighter's assigned portrait (glam/adonis/menace/inked/babyface)
        // feeds draw power and popularity growth in the sim.
        foreach (var f in _world.Fighters.Values)
            f.PersonaTag = Art.PaintedLibrary.PersonaFor(f) ?? f.PersonaTag;

        // Index every simulated bout so fighter profiles can show their history.
        _fightIndex = _world.Events.Values
            .SelectMany(e => e.Card.Select(f => (fight: f, ev: e)))
            .ToDictionary(x => x.fight.Id, x => (x.fight, x.ev));

        // Fight night: a due player card holds time hostage until watched/simmed.
        var tonight = DuePlayerEvent();
        IsFightNight = tonight is not null;
        FightNightLine = tonight is null ? "" : $"🔔  FIGHT NIGHT — {tonight.Name}, {tonight.Card.Count} bout{(tonight.Card.Count == 1 ? "" : "s")}";

        RefreshRoster();
        RefreshStandings();
        EventsPage.Attach(_world);
        Dashboard.Attach(_world);
        Booking.Attach(_world);
        Contracts.Attach(_world);
        Finance.Attach(_world);
        Media.Attach(_world);
        News.Attach(_world);
        Mail.Attach(_world);
        Org.Attach(_world);
        WorldRankings.Attach(_world);
        WorldMap.Attach(_world);
        Camps.Attach(_world);
        MyRoster.Attach(_world);
        Notes.Attach(_world);
        OrgsPage.Attach(_world);
        RefreshDeskNotes(player);
    }

    private void RefreshRoster()
    {
        if (_world is null) return;
        int? keepId = SelectedFighter?.Id;
        // The world database: search and filters run over everyone (built lazily,
        // only once the Fighters app has been opened).
        FightersPage.Attach(_world, _fightIndex);

        // Keep the same fighter on the profile across advances — whoever was opened,
        // wherever from: the profile must never silently become someone else. A fresh
        // world with no selection starts on the best fighter by the scout's read.
        SelectedFighter = keepId is { } id && _world.Get(new FighterId(id)) is { } kept
            ? new FighterRow(kept, _world, _fightIndex)
            : _world.Fighters.Values.Where(f => f.Status != FighterStatus.Retired)
                .OrderByDescending(f => FighterRow.Scouted(_world, f)).ThenBy(f => f.Id.Value)
                .Select(f => new FighterRow(f, _world, _fightIndex)).FirstOrDefault();
    }

    private void RefreshStandings()
    {
        if (_world is null) return;
        Standings.Clear();
        ChampionText = "";
        ChampionReign = "";
        ChampionPortrait = null;
        ChampionId = 0;
        var org = SelectedOrg ?? _world.Get(_world.PlayerPromotion);

        // Regional popularity + bio for the selected promotion.
        OrgRegions.Clear();
        SelectedOrgBio = org is null ? "" : Core.Sim.Flavor.Biographer.Promotion(_world, org);
        if (org is not null)
            foreach (var region in Enum.GetValues<Region>())
                OrgRegions.Add(new RatingBar(FighterRow.Pretty(region.ToString()),
                    Core.Sim.Geography.Reputation.OrgInRegion(_world, org, region)));

        var div = org?.Divisions.FirstOrDefault(d => d.WeightClass == RankingDivision.WeightClass && d.Sex == RankingDivision.Sex);
        if (div is null) return;

        if (div.Title is { } tid && _world.Get(tid) is { CurrentChampion: { } cid } title && _world.Get(cid) is { } champ)
        {
            ChampionText = $"{champ.FullName}  ({champ.Record})";
            ChampionPortrait = Portraits.For(champ, _world);
            ChampionId = champ.Id.Value;
            var reign = title.Lineage.LastOrDefault(r => r.LostOn is null);
            string beltName = $"belt prestige {title.Prestige.Value}";
            ChampionReign = reign is null
                ? beltName
                : $"Champion since {reign.WonOn:MMM yyyy}  ·  {reign.Defenses} defense{(reign.Defenses == 1 ? "" : "s")}  ·  {beltName}";
            Standings.Add(new RankRow("C", champ, _world));
        }
        else ChampionText = "Vacant — the belt is waiting for a first champion";

        int rank = 1;
        foreach (var id in div.Rankings)
            if (_world.Get(id) is { } f) Standings.Add(new RankRow($"#{rank++}", f, _world));
    }

}
