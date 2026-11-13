using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Org;
using MmaManager.Wpf.Art;
using MmaManager.Wpf.Mvvm;

namespace MmaManager.Wpf.ViewModels;

/// <summary>A career milestone, earned or still to come, for the achievements strip.</summary>
public sealed class MilestoneRow
{
    public required string Title { get; init; }
    public required bool Earned { get; init; }
    public string Icon => Earned ? "Trophy" : "Star";
}

/// <summary>One promotion on the home page's ladder.</summary>
public sealed class LadderRow
{
    public required int Rank { get; init; }
    public required int PromotionId { get; init; }
    public required ImageSource Logo { get; init; }
    public required string Name { get; init; }
    public required string Tier { get; init; }
    public required int Prestige { get; init; }
    public required bool IsPlayer { get; init; }
    public string RankText => $"#{Rank}";
}

/// <summary>A fighter in the main event of the next card (face, name, record).</summary>
public sealed class CornerModel
{
    public required int Id { get; init; }
    public required ImageSource Portrait { get; init; }
    public required string Name { get; init; }
    public required string Line { get; init; }
}

/// <summary>
/// The home page: walking into your promotion. Who you are and where you are on
/// the climb, what the boss has to say, your next card as a poster, the people
/// who matter this week, what needs a decision, and the sport talking. A brand
/// new founder gets a three-step path instead of empty panels. Every panel links
/// to the app that acts on it; nothing here decides anything for the sim.
/// </summary>
public sealed class DashboardViewModel : ObservableObject
{
    /// <summary>Set by the shell; jumps to another app.</summary>
    public Action<int>? Navigate { get; set; }

    /// <summary>Set by the shell: the world reference for desk actions + a refresh hook.</summary>
    public Func<World?>? GetWorld { get; set; }
    public Action? OnDecisionMade { get; set; }

    /// <summary>Set by the shell (interactive only): ask before a costly yes (heading, body, detail, confirm label).</summary>
    public Func<string, string, string, string, bool>? Confirm { get; set; }

    /// <summary>Set by the shell: fight night's two doors, contract talks, and whether a card is due tonight.</summary>
    public ICommand? WatchTonightCommand { get; set; }
    public ICommand? SimTonightCommand { get; set; }
    public ICommand? NegotiateCommand { get; set; }
    /// <summary>Stop courting a free agent a rival has made an offer to (parameter = fighter id; asks first).</summary>
    public ICommand? LetCourtedGoCommand { get; set; }
    public ICommand? OpenFighterCommand { get; set; }
    public Func<bool>? IsFightNight { get; set; }

    public ICommand BookCommand { get; }
    public ICommand ContractsCommand { get; }
    public ICommand InboxCommand { get; }
    public ICommand RosterCommand { get; }
    public ICommand NewsCommand { get; }
    public ICommand OrgsCommand { get; }
    public ICommand OrgCommand { get; }
    public ICommand MediaCommand { get; }
    public ICommand FinanceCommand { get; }
    public ICommand AcceptDecisionCommand { get; }
    public ICommand DeclineDecisionCommand { get; }
    public ICommand ToggleLadderCommand { get; }

    public DashboardViewModel()
    {
        BookCommand = new RelayCommand(() => Navigate?.Invoke(Tab.BookEvent));
        ContractsCommand = new RelayCommand(() => Navigate?.Invoke(Tab.Contracts));
        InboxCommand = new RelayCommand(() => Navigate?.Invoke(Tab.Inbox));
        RosterCommand = new RelayCommand(() => Navigate?.Invoke(Tab.MyRoster));
        NewsCommand = new RelayCommand(() => Navigate?.Invoke(Tab.News));
        OrgsCommand = new RelayCommand(() => Navigate?.Invoke(Tab.Orgs));
        OrgCommand = new RelayCommand(() => Navigate?.Invoke(Tab.Org));
        MediaCommand = new RelayCommand(() => Navigate?.Invoke(Tab.Media));
        FinanceCommand = new RelayCommand(() => Navigate?.Invoke(Tab.Finance));
        AcceptDecisionCommand = new RelayCommand<Decision>(d =>
        {
            if (GetWorld?.Invoke() is not { } w) return;
            // Expensive or destructive yeses ask first (every surface runs this command:
            // Home, the Inbox letter and the Briefing).
            if (Confirm is { } ask && DeskCalls.YesCosts(w, d) is { } c && !ask(c.Heading, c.Body, c.Detail, c.Confirm)) return;
            MmaManager.Core.Sim.Living.Decisions.Accept(w, d, w.CurrentDate);
            OnDecisionMade?.Invoke();
        });
        DeclineDecisionCommand = new RelayCommand<Decision>(d =>
        {
            if (GetWorld?.Invoke() is not { } w) return;
            MmaManager.Core.Sim.Living.Decisions.Decline(w, d, w.CurrentDate);
            OnDecisionMade?.Invoke();
        });
        ToggleLadderCommand = new RelayCommand(() => LadderExpanded = !LadderExpanded);
    }

    // --- Hero ----------------------------------------------------------------
    private ImageSource? _logo;
    public ImageSource? Logo { get => _logo; private set => Set(ref _logo, value); }
    private string _orgName = "";
    public string OrgName { get => _orgName; private set => Set(ref _orgName, value); }
    private string _dateLine = "";
    public string DateLine { get => _dateLine; private set => Set(ref _dateLine, value); }
    private string _tierChip = "";
    public string TierChip { get => _tierChip; private set => Set(ref _tierChip, value); }
    private string _hqChip = "";
    public string HqChip { get => _hqChip; private set => Set(ref _hqChip, value); }
    private string _rankChip = "";
    public string RankChip { get => _rankChip; private set => Set(ref _rankChip, value); }
    private string _balanceText = "";
    public string BalanceText { get => _balanceText; private set => Set(ref _balanceText, value); }
    private string _rosterLine = "";
    public string RosterLine { get => _rosterLine; private set => Set(ref _rosterLine, value); }
    private string _greeting = "";
    /// <summary>What the boss (or, for a founder, the accountant) says when you walk in.</summary>
    public string Greeting { get => _greeting; private set => Set(ref _greeting, value); }
    private string _greetingFrom = "";
    public string GreetingFrom { get => _greetingFrom; private set => Set(ref _greetingFrom, value); }
    private string _ownerNote = "";
    public string OwnerNote { get => _ownerNote; private set => Set(ref _ownerNote, value); }

    // --- The climb -------------------------------------------------------------
    public ObservableCollection<JourneyStop> TierStops { get; } = new();
    private int _tierProgress;
    /// <summary>0–100 fill toward the next reach tier.</summary>
    public int TierProgress { get => _tierProgress; private set => Set(ref _tierProgress, value); }
    private string _climbLine = "";
    public string ClimbLine { get => _climbLine; private set => Set(ref _climbLine, value); }
    private string _climbDetail = "";
    public string ClimbDetail { get => _climbDetail; private set => Set(ref _climbDetail, value); }
    private int _prestige;
    public int Prestige { get => _prestige; private set => Set(ref _prestige, value); }
    private int _reputation;
    public int Reputation { get => _reputation; private set => Set(ref _reputation, value); }

    // --- First steps (a founder before the first show) ------------------------
    private bool _showFirstSteps;
    public bool ShowFirstSteps { get => _showFirstSteps; private set => Set(ref _showFirstSteps, value); }
    public bool ShowCareerPanels => !ShowFirstSteps;
    public ObservableCollection<JourneyStop> FirstSteps { get; } = new();

    // --- Next card -------------------------------------------------------------
    private bool _hasNextEvent;
    public bool HasNextEvent { get => _hasNextEvent; private set => Set(ref _hasNextEvent, value); }
    private ImageSource? _poster;
    public ImageSource? Poster { get => _poster; private set => Set(ref _poster, value); }
    private string _nextEventTitle = "";
    public string NextEventTitle { get => _nextEventTitle; private set => Set(ref _nextEventTitle, value); }
    private string _nextEventWhen = "";
    public string NextEventWhen { get => _nextEventWhen; private set => Set(ref _nextEventWhen, value); }
    private string _nextEventWhere = "";
    public string NextEventWhere { get => _nextEventWhere; private set => Set(ref _nextEventWhere, value); }
    private string _nextEventTagline = "";
    public string NextEventTagline { get => _nextEventTagline; private set => Set(ref _nextEventTagline, value); }
    private string _nextEventCard = "";
    public string NextEventCard { get => _nextEventCard; private set => Set(ref _nextEventCard, value); }
    private CornerModel? _mainRed;
    public CornerModel? MainRed { get => _mainRed; private set => Set(ref _mainRed, value); }
    private CornerModel? _mainBlue;
    public CornerModel? MainBlue { get => _mainBlue; private set => Set(ref _mainBlue, value); }
    public bool HasMainEvent => MainRed is not null && MainBlue is not null;
    private bool _fightNight;
    public bool FightNight { get => _fightNight; private set => Set(ref _fightNight, value); }
    private string _noCardTitle = "";
    public string NoCardTitle { get => _noCardTitle; private set => Set(ref _noCardTitle, value); }
    private string _noCardMessage = "";
    public string NoCardMessage { get => _noCardMessage; private set => Set(ref _noCardMessage, value); }
    private string _noCardAction = "";
    public string NoCardAction { get => _noCardAction; private set => Set(ref _noCardAction, value); }
    private ICommand? _noCardCommand;
    public ICommand? NoCardCommand { get => _noCardCommand; private set => Set(ref _noCardCommand, value); }

    // --- Lists -------------------------------------------------------------------
    public ObservableCollection<ActionItemModel> NeedsYou { get; } = new();
    /// <summary>What the panel draws: the first <see cref="NeedsYouCap"/>, or all of them once expanded.</summary>
    public ObservableCollection<ActionItemModel> NeedsYouShown { get; } = new();
    public const int NeedsYouCap = 4;
    private bool _needsYouExpanded;
    public bool NeedsYouExpanded
    {
        get => _needsYouExpanded;
        set
        {
            if (!Set(ref _needsYouExpanded, value)) return;
            NeedsYouShown.Clear();
            foreach (var a in value ? NeedsYou : NeedsYou.Take(NeedsYouCap)) NeedsYouShown.Add(a);
            OnPropertyChanged(nameof(NeedsYouMoreText));
        }
    }
    public bool HasMoreNeedsYou => NeedsYou.Count > NeedsYouCap;
    public string NeedsYouMoreText => NeedsYouExpanded ? "Show fewer ▴" : $"See all {NeedsYou.Count} ▾";
    public ICommand ToggleNeedsYouCommand => _toggleNeedsYou ??= new RelayCommand(() => NeedsYouExpanded = !NeedsYouExpanded);
    private ICommand? _toggleNeedsYou;
    private string _needsYouHint = "";
    public string NeedsYouHint { get => _needsYouHint; private set => Set(ref _needsYouHint, value); }
    public bool NeedsYouEmpty => NeedsYou.Count == 0;
    /// <summary>A founder's empty "needs you" is noise next to the first-steps path.</summary>
    public bool ShowNeedsYou => !(NeedsYouEmpty && ShowFirstSteps);
    public ObservableCollection<FighterCardModel> People { get; } = new();
    public bool HasPeople => People.Count > 0;
    public ObservableCollection<HeadlineCardModel> Headlines { get; } = new();
    public ObservableCollection<LadderRow> LadderNear { get; } = new();
    public ObservableCollection<LadderRow> Ladder { get; } = new();
    private bool _ladderExpanded;
    public bool LadderExpanded
    {
        get => _ladderExpanded;
        set { if (Set(ref _ladderExpanded, value)) OnPropertyChanged(nameof(LadderToggleText)); }
    }
    public string LadderToggleText => LadderExpanded ? "Just my neighbours ▴" : $"All {Ladder.Count} ▾";
    public ObservableCollection<ObjectiveRow> Objectives { get; } = new();
    public ObservableCollection<MilestoneRow> Milestones { get; } = new();
    private string _milestoneLine = "";
    public string MilestoneLine { get => _milestoneLine; private set => Set(ref _milestoneLine, value); }

    // The poster is the costliest thing on the page; redraw it only when the card changes.
    private string _posterKey = "";

    public void Attach(World world)
    {
        var player = world.Get(world.PlayerPromotion);
        if (player is null) return;

        var roster = world.RosterOf(player.Id).Where(f => f.Status != FighterStatus.Retired).ToList();
        var scheduled = world.Events.Values
            .Where(e => e.Status == EventStatus.Scheduled && e.Promotion == player.Id)
            .OrderBy(e => e.Date).ThenBy(e => e.Id.Value).ToList();
        bool everShowed = world.Events.Values.Any(e => e.Promotion == player.Id && e.Status == EventStatus.Completed);
        bool fightNight = IsFightNight?.Invoke() ?? false;
        var next = scheduled.FirstOrDefault();

        // --- Hero -----------------------------------------------------------------
        Logo = LogoFactory.ForPromotion(player);
        OrgName = player.Name;
        DateLine = world.CurrentDate.ToString("dddd d MMMM yyyy").ToUpperInvariant();
        Prestige = player.Prestige.Value;
        Reputation = player.Reputation.Value;
        BalanceText = player.Balance.ToString();
        var byPrestige = world.Promotions.Values.Where(p => !p.Defunct)
            .OrderByDescending(p => p.Prestige.Value).ThenBy(p => p.Id.Value).ToList();
        int rank = byPrestige.FindIndex(p => p.Id == player.Id) + 1;
        string hq = player.HomeCity is { } hc && world.Get(hc) is { } hqCity ? hqCity.Name : FighterRow.Pretty(player.BaseRegion.ToString());
        TierChip = $"{OrgSizing.Label(OrgSizing.SizeOf(player))} promotion";
        HqChip = $"HQ {hq}";
        RankChip = $"#{rank} of {byPrestige.Count} in the world";
        RosterLine = roster.Count == 0
            ? $"no fighters yet  ·  {player.Divisions.Count} divisions open"
            : $"{roster.Count} fighters  ·  {player.Divisions.Count} divisions";

        string owner = MmaManager.Core.Sim.Living.Mail.Owner(world);
        bool founder = player.OwnerName is not { Length: > 0 };
        // A founded promotion has no owner but the player: the accountant speaks,
        // as a role (no invented name).
        GreetingFrom = founder ? "your accountant" : $"{owner}, owner";
        OwnerNote = player.OwnerNote ?? "";
        int deskCalls = world.Decisions.Count(d => d.State == DecisionState.Pending);
        Greeting = GreetingFor(world, player, roster.Count, next, fightNight, deskCalls, everShowed, hq);

        // --- The climb ----------------------------------------------------------------
        var standing = OrgSizing.Progress(player);
        TierProgress = standing.Percent;
        TierStops.Clear();
        var tiers = Enum.GetValues<OrgSize>();
        for (int i = 0; i < tiers.Length; i++)
            TierStops.Add(new JourneyStop
            {
                Label = OrgSizing.Label(tiers[i]),
                Done = tiers[i] < standing.Tier,
                Current = tiers[i] == standing.Tier,
                IsFirst = i == 0,
                IsLast = i == tiers.Length - 1,
            });
        if (standing.Next is { } nextTier)
        {
            ClimbLine = $"{standing.Percent}% of the way to {OrgSizing.Label(nextTier)}";
            ClimbDetail = $"{OrgSizing.Label(nextTier)} opens at prestige {OrgSizing.PrestigeFor(nextTier)} — you're at {player.Prestige.Value}. Reputation {player.Reputation.Value}.";
        }
        else
        {
            ClimbLine = "The top of the sport";
            ClimbDetail = $"International and {standing.Percent}% of the way to untouchable. Prestige {player.Prestige.Value}, reputation {player.Reputation.Value}.";
        }

        // --- First steps: a founder until the first show is in the books -------------
        ShowFirstSteps = !everShowed && (roster.Count == 0 || scheduled.Count == 0 || scheduled.All(e => e.Card.Count == 0));
        OnPropertyChanged(nameof(ShowCareerPanels));
        FirstSteps.Clear();
        if (ShowFirstSteps)
        {
            bool signed = roster.Count >= 2;
            bool booked = scheduled.Any(e => e.Card.Count > 0);
            FirstSteps.Add(new JourneyStop
            {
                Number = signed ? "✓" : "1", Label = "Sign your first fighters",
                Detail = signed
                    ? $"{roster.Count} fighters on the books. That's a gym."
                    : "The free-agent market is full of hungry locals. Two in the same division is a fight.",
                Done = signed, Current = !signed, IsFirst = true,
                ActionText = "Find fighters ▸", ActionCommand = ContractsCommand,
            });
            FirstSteps.Add(new JourneyStop
            {
                Number = booked ? "✓" : "2", Label = "Book your first card",
                Detail = booked ? "The card is set. Now sell it." : "Pick a date and a room, then match your fighters up. Start small.",
                Done = booked, Current = signed && !booked,
                ActionText = "Book a card ▸", ActionCommand = BookCommand,
            });
            FirstSteps.Add(new JourneyStop
            {
                Number = "3", Label = "Fight night",
                Detail = booked && next is not null
                    ? $"{next.Name}, {next.Date:ddd d MMM}. Advance the calendar to get there."
                    : "Watch it live from ringside or jump straight to the results.",
                Current = signed && booked, IsLast = true,
                ActionText = fightNight ? "Watch it live ▸" : "",
                ActionCommand = WatchTonightCommand,
            });
        }

        // --- Next card ------------------------------------------------------------------
        FightNight = fightNight;
        HasNextEvent = next is not null;
        MainRed = MainBlue = null;
        if (next is not null)
        {
            int days = next.Date.DayNumber - world.CurrentDate.DayNumber;
            NextEventTitle = next.Name;
            NextEventWhen = $"{next.Date:dddd d MMMM}  ·  {(days <= 0 ? "TONIGHT" : days == 1 ? "tomorrow" : $"in {days} days")}";
            string city = next.VenueId is { } vid && world.Get(vid) is { } venue && world.Get(venue.City) is { } vc ? $", {vc.Name}" : "";
            NextEventWhere = next.Venue is { Length: > 0 } v ? v + city : "Venue to be confirmed";
            NextEventTagline = MmaManager.Core.Sim.Flavor.EventHype.Tagline(world, next) ?? "";
            int bouts = next.Card.Count;
            NextEventCard = bouts == 0 ? "No bouts on the card yet." : $"{bouts} bout{(bouts == 1 ? "" : "s")} on the card";
            if (next.Card.LastOrDefault() is { } main && world.Get(main.RedCorner) is { } red && world.Get(main.BlueCorner) is { } blue)
            {
                MainRed = Corner(world, red);
                MainBlue = Corner(world, blue);
            }
            string key = $"{next.Id.Value}|{bouts}|{next.Card.LastOrDefault()?.RedCorner.Value}|{next.Card.LastOrDefault()?.BlueCorner.Value}|{next.Venue}";
            if (key != _posterKey || Poster is null)
            {
                Poster = PosterFactory.For(world, next);
                _posterKey = key;
            }
        }
        else
        {
            Poster = null;
            _posterKey = "";
            NoCardTitle = "Nothing on the calendar";
            if (roster.Count >= 2)
            {
                NoCardMessage = $"{roster.Count} fighters and no show to fight on. Pick a date and build a card.";
                NoCardAction = "Book a card ▸";
                NoCardCommand = BookCommand;
            }
            else
            {
                NoCardMessage = "You need fighters before you can put on a show.";
                NoCardAction = "Find fighters ▸";
                NoCardCommand = ContractsCommand;
            }
        }
        OnPropertyChanged(nameof(HasMainEvent));

        // --- Needs you --------------------------------------------------------------------
        BuildNeedsYou(world, player, roster, next, fightNight);

        // --- Your people --------------------------------------------------------------------
        BuildPeople(world, player, roster, scheduled);

        // --- Around the sport ---------------------------------------------------------------
        Headlines.Clear();
        var picks = new List<NewsItem>();
        var recent = Enumerable.Reverse(world.News).Take(40).ToList();
        if (recent.FirstOrDefault(n => n.Promotion == player.Id) is { } ours) picks.Add(ours);
        foreach (var n in recent.Where(n => n.AboutFighter is not null && n.Body.Contains('“')))
            if (picks.Count < 3 && !picks.Contains(n)) picks.Add(n);
        foreach (var n in recent.Where(n => n.AboutFighter is not null))
            if (picks.Count < 3 && !picks.Contains(n)) picks.Add(n);
        foreach (var n in recent)
            if (picks.Count < 3 && !picks.Contains(n)) picks.Add(n);
        foreach (var n in picks.OrderByDescending(n => n.Date))
        {
            var who = n.AboutFighter is { } fid ? world.Get(fid) : null;
            ImageSource? pic = who is not null ? Portraits.Thumb(who, world)
                : world.Get(n.Promotion) is { } promo ? LogoFactory.ForPromotion(promo) : null;
            Headlines.Add(new HeadlineCardModel
            {
                Picture = pic,
                Kicker = $"{FighterRow.Pretty(n.Category.ToString()).ToUpperInvariant()}  ·  {n.Date:d MMM}",
                Headline = n.Headline,
                Quote = QuoteIn(n.Body),
                AboutPlayer = n.Promotion == player.Id,
                OpenCommand = who is not null ? OpenFighterCommand : NewsCommand,
                OpenParameter = who?.Id.Value,
            });
        }

        // --- The ladder: your neighbours, the rest one click away ---------------------------
        Ladder.Clear();
        LadderNear.Clear();
        for (int i = 0; i < byPrestige.Count; i++)
        {
            var p = byPrestige[i];
            Ladder.Add(new LadderRow
            {
                Rank = i + 1, PromotionId = p.Id.Value, Logo = LogoFactory.ForPromotion(p), Name = p.Name,
                Tier = OrgSizing.Label(OrgSizing.SizeOf(p)), Prestige = p.Prestige.Value, IsPlayer = p.Id == player.Id,
            });
        }
        int me = Math.Max(0, rank - 1);
        int from = Math.Clamp(me - 2, 0, Math.Max(0, Ladder.Count - 5));
        foreach (var row in Ladder.Skip(from).Take(5)) LadderNear.Add(row);
        OnPropertyChanged(nameof(LadderToggleText));

        // --- Goals ---------------------------------------------------------------------------
        Objectives.Clear();
        foreach (var o in player.Objectives)
            Objectives.Add(new ObjectiveRow(o));
        Milestones.Clear();
        int earned = 0;
        foreach (var (mkey, title) in MmaManager.Core.Sim.Org.Milestones.Catalog
                     .OrderByDescending(m => player.Milestones.Contains(m.Key)))
        {
            bool got = player.Milestones.Contains(mkey);
            if (got) earned++;
            Milestones.Add(new MilestoneRow { Title = title, Earned = got });
        }
        MilestoneLine = $"{earned} of {MmaManager.Core.Sim.Org.Milestones.Catalog.Count} milestones";
    }

    private static CornerModel Corner(World w, Fighter f) => new()
    {
        Id = f.Id.Value,
        Portrait = Portraits.Thumb(f, w),
        Name = $"{f.FirstName} {f.LastName}",
        Line = f.Record.ToString(),
    };

    /// <summary>The first quoted line in a story body, if somebody said something.</summary>
    private static string QuoteIn(string body)
    {
        int a = body.IndexOf('“');
        if (a < 0) return "";
        int b = body.IndexOf('”', a + 1);
        return b > a ? body.Substring(a, b - a + 1) : "";
    }

    // What the person you answer to says when you walk in. Plain status, in a voice.
    private static string GreetingFor(World w, Promotion p, int roster, Event? next, bool fightNight, int deskCalls, bool everShowed, string hq)
    {
        if (roster == 0 && !everShowed)
            return $"“It's your name on the door now. {p.Balance} in the bank, an empty gym in {hq}, " +
                   "and a city that hasn't heard of us yet. Let's find you some fighters.”";
        if (fightNight && next is not null)
            return $"“It's fight night. {next.Name} — the doors are open and the room is filling. Go and make it a show.”";
        if (deskCalls > 0)
            return deskCalls == 1
                ? "“There's a call on your desk only you can make. Don't sit on it.”"
                : $"“There are {deskCalls} calls on your desk only you can make. Don't sit on them.”";
        if (next is null && roster >= 2)
            return $"“We have {roster} fighters and nothing on the calendar. Fighters want fights — let's get a show on the books.”";
        if (next is not null)
        {
            int days = next.Date.DayNumber - w.CurrentDate.DayNumber;
            return next.Card.Count == 0
                ? $"“{next.Name} is {days} days out and there isn't a fight on it yet. Let's build that card.”"
                : $"“{next.Name} is {days} days out. Make sure it sells.”";
        }
        return "“Quiet week. Use it — sign someone, scout someone, or get a show on the books.”";
    }

    private void BuildNeedsYou(World world, Promotion player, List<Fighter> roster, Event? next, bool fightNight)
    {
        NeedsYou.Clear();
        var today = world.CurrentDate;

        if (fightNight && next is not null)
            NeedsYou.Add(new ActionItemModel
            {
                Icon = "Bell", Tone = CardTone.Bad, Due = "tonight",
                Title = $"Fight night: {next.Name}",
                Detail = $"{next.Card.Count} bouts ready to go. Watch it from ringside, or skip straight to the results.",
                PrimaryText = "Watch it live ▸", PrimaryCommand = WatchTonightCommand,
                SecondaryText = "Results only", SecondaryCommand = SimTonightCommand,
            });

        foreach (var d in world.Decisions.Where(d => d.State == DecisionState.Pending).OrderBy(d => d.Deadline).ThenBy(d => d.Id))
        {
            int days = d.Deadline.DayNumber - today.DayNumber;
            var who = d.Fighter is { } fid ? world.Get(fid) : null;
            NeedsYou.Add(new ActionItemModel
            {
                Icon = DeskCalls.Icon(d),
                Title = DeskCalls.Title(world, d),
                Detail = d.Detail,
                Due = days <= 0 ? "today" : days == 1 ? "by tomorrow" : $"{days} days left",
                Tone = days <= 3 ? CardTone.Bad : CardTone.Warn,
                Picture = who is null ? null : Portraits.Thumb(who, world),
                PrimaryText = DeskCalls.YesText(world, d, short_: true),
                PrimaryCommand = AcceptDecisionCommand, PrimaryParameter = d,
                SecondaryText = DeskCalls.NoText(world, d, short_: true),
                SecondaryCommand = DeclineDecisionCommand, SecondaryParameter = d,
            });
        }

        // A rival's offer to a free agent you're courting: no Desk card and no match —
        // beat it at the table, or let him go.
        foreach (var o in DeskCalls.Courted(world))
        {
            int days = o.Deadline.DayNumber - today.DayNumber;
            var who = world.Get(o.Fighter)!;
            NeedsYou.Add(new ActionItemModel
            {
                Icon = "Money",
                Title = DeskCalls.FreeAgentTitle(world, o),
                Detail = DeskCalls.FreeAgentLine(world, o)
                         + MmaManager.Core.Sim.Contracts.Lifers.Gendered(who, " Sit down with him and his people will show you their paper."),
                Due = days <= 0 ? "today" : days == 1 ? "by tomorrow" : $"{days} days left",
                Tone = days <= 7 ? CardTone.Bad : CardTone.Warn,
                Picture = Portraits.Thumb(who, world),
                PrimaryText = DeskCalls.SitDownText(who), PrimaryCommand = NegotiateCommand, PrimaryParameter = who.Id.Value,
                SecondaryText = DeskCalls.LetGoText(who), SecondaryCommand = LetCourtedGoCommand, SecondaryParameter = who.Id.Value,
            });
        }

        if (next is null && roster.Count >= 2)
            NeedsYou.Add(new ActionItemModel
            {
                Icon = "Calendar", Tone = CardTone.Warn,
                Title = "No show on the calendar",
                Detail = $"{roster.Count} fighters with nowhere to fight. Pick a date and build a card.",
                PrimaryText = "Book a card ▸", PrimaryCommand = BookCommand,
            });
        else if (next is not null && next.Card.Count == 0)
            NeedsYou.Add(new ActionItemModel
            {
                Icon = "Calendar", Tone = CardTone.Warn,
                Title = $"{next.Name} has no fights yet",
                Detail = "The date and the room are set. Now give people a reason to buy a ticket.",
                PrimaryText = "Build the card ▸", PrimaryCommand = BookCommand,
            });

        // Deals on their last fight, best of them first (by the scout's read, never the truth).
        var ending = roster.Where(f => f.Contract is { FightsRemaining: <= 1 })
            .OrderByDescending(f => FighterRow.Scouted(world, f)).ThenBy(f => f.Id.Value).ToList();
        foreach (var f in ending.Take(2))
            NeedsYou.Add(new ActionItemModel
            {
                Icon = "Pen", Tone = CardTone.Warn,
                Title = f.Contract!.FightsRemaining <= 0 ? $"{f.FullName}'s deal is up" : $"{f.FullName} is on the last fight of the deal",
                Detail = $"{FighterRow.DivisionLabel(f.WeightClass, f.Sex)}, {f.Record}. Re-sign before the bigger promotions come calling.",
                Picture = Portraits.Thumb(f, world),
                PrimaryText = "Talk terms ▸", PrimaryCommand = NegotiateCommand, PrimaryParameter = f.Id.Value,
                SecondaryText = "Profile", SecondaryCommand = OpenFighterCommand, SecondaryParameter = f.Id.Value,
            });
        if (ending.Count > 2)
            NeedsYou.Add(new ActionItemModel
            {
                Icon = "Document", Tone = CardTone.Neutral,
                Title = $"{ending.Count - 2} more deal{(ending.Count - 2 == 1 ? " is" : "s are")} on the last fight",
                Detail = "Everyone whose contract is about to run out, on one screen.",
                PrimaryText = "Contracts ▸", PrimaryCommand = ContractsCommand,
            });

        foreach (var o in player.Offers.Where(o => o.ExpiresOn >= today && o.ExpiresOn.DayNumber - today.DayNumber <= 14).OrderBy(o => o.ExpiresOn).Take(2))
        {
            int days = o.ExpiresOn.DayNumber - today.DayNumber;
            bool media = o.Media is not null;
            NeedsYou.Add(new ActionItemModel
            {
                Icon = media ? "Broadcast" : "Money", Tone = CardTone.Info,
                Title = media ? $"{o.Partner} has a media offer on the table" : $"{o.Partner} wants to sponsor you",
                Detail = $"{o.FeePerEvent} an event. The offer lapses on {o.ExpiresOn:d MMM}.",
                Due = days <= 1 ? "lapses tomorrow" : $"{days} days left",
                PrimaryText = media ? "Media desk ▸" : "Finance ▸",
                PrimaryCommand = media ? MediaCommand : FinanceCommand,
            });
        }

        int unread = world.Mail.Count(m => !m.Read);
        if (unread > 0)
        {
            var latest = Enumerable.Reverse(world.Mail).FirstOrDefault(m => !m.Read);
            NeedsYou.Add(new ActionItemModel
            {
                Icon = "Envelope", Tone = CardTone.Info,
                Title = unread == 1 ? "1 unread message" : $"{unread} unread messages",
                Detail = latest is null ? "" : $"Latest: {latest.Subject} — {latest.From}",
                PrimaryText = "Open the inbox ▸", PrimaryCommand = InboxCommand,
            });
        }

        // Long lists stay short on Home: the first few, the rest one click away.
        NeedsYouShown.Clear();
        foreach (var a in NeedsYouExpanded ? NeedsYou : NeedsYou.Take(NeedsYouCap)) NeedsYouShown.Add(a);
        OnPropertyChanged(nameof(NeedsYouMoreText));
        OnPropertyChanged(nameof(HasMoreNeedsYou));

        int urgent = NeedsYou.Count(a => a.Tone is CardTone.Bad or CardTone.Warn);
        NeedsYouHint = NeedsYou.Count == 0 ? ""
            : urgent == 0 ? "Nothing urgent — here's what's waiting."
            : urgent == 1 ? "One thing wants an answer soon."
            : $"{urgent} things want an answer soon. Most urgent first.";
        OnPropertyChanged(nameof(NeedsYouEmpty));
        OnPropertyChanged(nameof(ShowNeedsYou));
    }

    private void BuildPeople(World world, Promotion player, List<Fighter> roster, List<Event> scheduled)
    {
        People.Clear();
        if (roster.Count == 0) { OnPropertyChanged(nameof(HasPeople)); return; }

        var booked = new Dictionary<FighterId, Event>();
        foreach (var ev in scheduled)
            foreach (var fight in ev.Card) { booked.TryAdd(fight.RedCorner, ev); booked.TryAdd(fight.BlueCorner, ev); }
        var picked = new HashSet<FighterId>();
        void Add(Fighter f, string why)
        {
            if (People.Count >= 5 || !picked.Add(f.Id)) return;
            People.Add(new FighterCardModel(f, world, booked.GetValueOrDefault(f.Id), why));
        }
        int Scout(Fighter f) => FighterRow.Scouted(world, f);

        // Your champions (the best of them by the scout's read).
        var belts = world.Titles.Values.Where(t => t.Promotion == player.Id && t.CurrentChampion is not null)
            .Select(t => (title: t, champ: world.Get(t.CurrentChampion!.Value)))
            .Where(x => x.champ is not null)
            .OrderByDescending(x => Scout(x.champ!)).ThenBy(x => x.title.Id.Value).ToList();
        foreach (var (title, champ) in belts.Take(2))
        {
            var reign = title.Lineage.LastOrDefault(r => r.LostOn is null);
            string defs = reign is null || reign.Defenses == 0 ? "" : $" · {reign.Defenses} defence{(reign.Defenses == 1 ? "" : "s")}";
            Add(champ!, $"CHAMPION{defs.ToUpperInvariant()}");
        }

        if (Franchise.FaceOf(world, player) is { } face && face.Contract?.Promotion == player.Id)
            Add(face, "THE FACE OF THE COMPANY");

        if (roster.OrderByDescending(f => f.Intangibles.Popularity.Value).ThenBy(f => f.Id.Value).FirstOrDefault() is { } draw)
            Add(draw, "YOUR BIGGEST DRAW");

        var prospect = roster.Where(f => f.AgeOn(world.CurrentDate) <= 26 && f.Record.CurrentStreak >= 2 && !picked.Contains(f.Id))
            .OrderByDescending(f => f.Record.CurrentStreak).ThenByDescending(Scout).ThenBy(f => f.Id.Value).FirstOrDefault();
        if (prospect is not null)
            Add(prospect, $"ON A RUN · {prospect.Record.CurrentStreak} STRAIGHT WINS");

        var expiring = roster.Where(f => f.Contract is { FightsRemaining: <= 1 } && !picked.Contains(f.Id))
            .OrderByDescending(Scout).ThenBy(f => f.Id.Value).FirstOrDefault();
        if (expiring is not null)
            Add(expiring, "LAST FIGHT ON THE DEAL");

        // A small roster: fill with the best of the rest so the panel is never thin.
        foreach (var f in roster.OrderByDescending(Scout).ThenBy(f => f.Id.Value))
            Add(f, booked.TryGetValue(f.Id, out var ev) ? $"FIGHTING ON {ev.Name.ToUpperInvariant()}" : "ONE OF YOUR BEST");

        OnPropertyChanged(nameof(HasPeople));
    }
}
