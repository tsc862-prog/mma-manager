using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Org;
using MmaManager.Wpf.Art;
using MmaManager.Wpf.Mvvm;

namespace MmaManager.Wpf.ViewModels;

// =============================================================================
// THE MONDAY BRIEFING: when the clock stops, one app on the desk says why, in
// plain words, with the people it is about; one button goes straight to the fix;
// a short digest says what else happened since the last stop; and the calendar
// says what is next.
//
// Nothing here is a game rule. It reads what the sim already did:
//   * the advance's own report (fight night, a main event lost);
//   * a before/after look at the player's books (BriefingBaseline: ids only),
//     which is how a bout pulled off a card, a signing, a departure or an
//     injury is noticed — the sim already mailed the player about each;
//   * the standing calls on the desk (decisions, deals on their last fight,
//     offers, the tier step-up, an empty calendar), each headlined ONCE, then
//     kept in "also on your desk" until it is answered.
// Ordering by the scout's read uses FighterRow.Scouted (fog of war); no true
// ability or potential is read anywhere.
// =============================================================================

/// <summary>Why the briefing is on the desk.</summary>
public enum BriefingKind
{
    /// <summary>Opened by hand, no stop yet: the desk as it stands.</summary>
    Desk,
    /// <summary>One of your cards came due: the clock is held until it is fought.</summary>
    FightNight,
    /// <summary>A main event fell off one of your cards: the advance stopped for it.</summary>
    MainEventLost,
    /// <summary>A bout came off one of your cards during the stretch (the clock did not stop for it).</summary>
    CardHole,
    /// <summary>One of your cards was just fought.</summary>
    CardDone,
    /// <summary>A call on the desk only you can make.</summary>
    Decision,
    /// <summary>Deals on their last fight (the softer, grouped heads-up).</summary>
    ExpiringDeal,
    /// <summary>A deal is up: the 30-day exclusive re-sign window has opened (DealWindow).</summary>
    DealUp,
    /// <summary>A re-sign window in its last week (or talks broken off: he will test the market).</summary>
    LastWeek,
    /// <summary>A sponsor or media offer waiting for an answer.</summary>
    Offer,
    /// <summary>Eligible to step up a tier.</summary>
    StepUp,
    /// <summary>A card on the calendar with no fights on it.</summary>
    EmptyCard,
    /// <summary>Fighters and no card at all.</summary>
    NoCard,
    /// <summary>The stretch ran out and nothing needs you.</summary>
    Quiet,
}

/// <summary>
/// The player's books just before time moves (ids and counts only, never rows):
/// what the briefing compares against to say what changed.
/// </summary>
public sealed class BriefingBaseline
{
    public required DateOnly Date { get; init; }
    public required decimal Balance { get; init; }
    public required int LedgerCount { get; init; }
    public required HashSet<int> Roster { get; init; }
    public required HashSet<int> Injured { get; init; }
    /// <summary>Each scheduled card of the player's: its bouts (fight id, corners), last = main event.</summary>
    public required Dictionary<int, List<(int Fight, int Red, int Blue)>> Cards { get; init; }
    public MailMessage? LastMail { get; init; }
    public NewsItem? LastNews { get; init; }

    public static BriefingBaseline Take(World w)
    {
        var player = w.Get(w.PlayerPromotion);
        var roster = player is null ? new List<Fighter>() : w.RosterOf(player.Id).ToList();
        return new BriefingBaseline
        {
            Date = w.CurrentDate,
            Balance = player?.Balance.Amount ?? 0m,
            LedgerCount = player?.Ledger.Count ?? 0,
            Roster = roster.Select(f => f.Id.Value).ToHashSet(),
            Injured = roster.Where(f => f.Status == FighterStatus.Injured).Select(f => f.Id.Value).ToHashSet(),
            Cards = w.Events.Values
                .Where(e => e.Status == EventStatus.Scheduled && e.Promotion == w.PlayerPromotion)
                .ToDictionary(e => e.Id.Value, e => e.Card.Select(f => (f.Id.Value, f.RedCorner.Value, f.BlueCorner.Value)).ToList()),
            LastMail = w.Mail.Count > 0 ? w.Mail[^1] : null,
            LastNews = w.News.Count > 0 ? w.News[^1] : null,
        };
    }

    /// <summary>Mail posted since the baseline (by position; falls back to the date if the box was trimmed).</summary>
    public IEnumerable<MailMessage> NewMail(World w)
    {
        int at = LastMail is null ? -1 : w.Mail.FindLastIndex(m => ReferenceEquals(m, LastMail));
        if (LastMail is not null && at < 0) return w.Mail.Where(m => m.Date > Date);
        return w.Mail.Skip(at + 1);
    }

    /// <summary>News posted since the baseline.</summary>
    public IEnumerable<NewsItem> NewNews(World w)
    {
        int at = -1;
        if (LastNews is not null)
            for (int i = w.News.Count - 1; i >= 0; i--)
                if (ReferenceEquals(w.News[i], LastNews)) { at = i; break; }
        if (LastNews is not null && at < 0) return w.News.Where(n => n.Date > Date);
        return w.News.Skip(at + 1);
    }
}

/// <summary>A face in the headline (the people the stop is about).</summary>
public sealed record BriefingPerson(int FighterId, string Name, string Detail, ImageSource? Picture)
{
    public bool HasPicture => Picture is not null;
    public string Icon => "People";
}

/// <summary>A button under the headline.</summary>
public sealed record BriefingAction(string Text, ICommand Command, bool IsPrimary = false, string Hint = "");

/// <summary>One line of the digest, or one thing still on the desk: a face or an icon, a sentence, a link.</summary>
public sealed class BriefingItem
{
    public required string Icon { get; init; }
    public ImageSource? Picture { get; init; }
    public bool HasPicture => Picture is not null;
    public string Kicker { get; init; } = "";
    public required string Text { get; init; }
    public string Detail { get; init; } = "";
    public bool HasDetail => Detail.Length > 0;
    public CardTone Tone { get; init; } = CardTone.Neutral;
    public string ActionText { get; init; } = "";
    public bool HasAction => ActionText.Length > 0 && Command is not null;
    public ICommand? Command { get; init; }
    /// <summary>Sort weight inside the digest (higher first).</summary>
    internal int Weight { get; init; }
}

public sealed class BriefingViewModel : ObservableObject
{
    public const int DigestCap = 5;

    // --- Doors into the rest of the app (wired by MainViewModel) -------------------
    public Action<int>? Navigate { get; set; }
    public Action<int>? OpenCard { get; set; }
    public Action<int>? OpenFighter { get; set; }
    public Action<int>? OpenEvent { get; set; }
    public Action? Close { get; set; }
    public ICommand? NegotiateCommand { get; set; }
    public ICommand? WatchTonightCommand { get; set; }
    public ICommand? SimTonightCommand { get; set; }
    public ICommand? AcceptDecisionCommand { get; set; }
    public ICommand? DeclineDecisionCommand { get; set; }
    public ICommand? AdvanceWeekCommand { get; set; }
    /// <summary>Ask before something costly (heading, body, detail, confirm label); null = go ahead (harness).</summary>
    public Func<string, string, string, string, bool>? Confirm { get; set; }

    public BriefingViewModel()
    {
        CarryOnCommand = new RelayCommand(() => Close?.Invoke());
        ToggleEverythingCommand = new RelayCommand(() => ShowEverything = !ShowEverything);
    }

    public ICommand CarryOnCommand { get; }
    public ICommand ToggleEverythingCommand { get; }

    // --- What stopped the clock (ids only; rebuilt into words on every refresh) ----
    private sealed record Stop(BriefingKind Kind, string Key, DateOnly On, int Weeks,
        int EventId = 0, int OutId = 0, int StayId = 0, string Reason = "", int CardCount = 0, int Extra = 0, bool Grouped = false);

    private Stop? _stop;
    private World? _world;
    private readonly HashSet<string> _announced = new();
    private List<BriefingItem> _digest = new();
    private string _digestWindow = "";

    /// <summary>A new career or a loaded save: nothing has been headlined yet.</summary>
    public void Reset()
    {
        _stop = null;
        _announced.Clear();
        _digest = new();
        _collapsed = new(); _collapsedSummary = null;
        _digestWindow = "";
        _world = null;
        ShowEverything = false;
    }

    public BriefingKind Kind => _stop?.Kind ?? BriefingKind.Desk;
    public bool IsQuiet => Kind == BriefingKind.Quiet;

    /// <summary>
    /// After an advance: decide the headline (the report's hard stops first, then a
    /// bout pulled off a card, then the first call on the desk not yet headlined),
    /// freeze the digest of what happened, and draw.
    /// </summary>
    public void AfterAdvance(World w, BriefingBaseline before, WorldTickReport r, int weeks)
    {
        var holes = Holes(w, before);
        Stop stop;
        if (r.PendingPlayerEvent is { } due)
            stop = new Stop(BriefingKind.FightNight, $"night:{due.Id.Value}", w.CurrentDate, weeks, due.Id.Value);
        else if (r.MainEventLost is { } lost && holes.FirstOrDefault(h => h.EventId == lost.Id.Value && h.WasMain) is { } mh)
            stop = new Stop(BriefingKind.MainEventLost, $"main:{lost.Id.Value}", w.CurrentDate, weeks, lost.Id.Value,
                mh.OutId, mh.StayId, mh.Reason, mh.CardBefore, holes.Count - 1);
        else if (r.MainEventLost is { } lost2)
            stop = new Stop(BriefingKind.MainEventLost, $"main:{lost2.Id.Value}", w.CurrentDate, weeks, lost2.Id.Value,
                CardCount: before.Cards.TryGetValue(lost2.Id.Value, out var c) ? c.Count : 0, Extra: holes.Count);
        else if (holes.FirstOrDefault() is { } h)
            stop = new Stop(BriefingKind.CardHole, $"hole:{h.EventId}:{h.OutId}", w.CurrentDate, weeks, h.EventId,
                h.OutId, h.StayId, h.Reason, h.CardBefore, holes.Count - 1);
        else
            stop = PickNeed(w, weeks);

        _stop = stop;
        _announced.Add(stop.Key);
        _digest = Digest(w, before, holes, skipHole: stop.Kind is BriefingKind.MainEventLost or BriefingKind.CardHole ? (stop.EventId, stop.OutId) : null);
        int days = Math.Max(0, w.CurrentDate.DayNumber - before.Date.DayNumber);
        _digestWindow = $"Since {before.Date:ddd d MMM}  ·  {Plural(days, "day")}" +
                        (r.EventsRun > 0 ? $"  ·  {Plural(r.EventsRun, "card")} around the sport, {Plural(r.Fights, "fight")}" : "");
        ShowEverything = false;
        Attach(w);
    }

    /// <summary>Tried to move the clock with a card of yours due tonight.</summary>
    public void Blocked(World w, Event due)
    {
        _stop = new Stop(BriefingKind.FightNight, $"night:{due.Id.Value}", w.CurrentDate, 0, due.Id.Value);
        _announced.Add(_stop.Key);
        Attach(w);
    }

    /// <summary>One of your cards was just fought (the Moment, if any, shows first).</summary>
    public void AfterCard(World w, BriefingBaseline before, Event ev)
    {
        _stop = new Stop(BriefingKind.CardDone, $"done:{ev.Id.Value}", w.CurrentDate, 0, ev.Id.Value);
        _announced.Add(_stop.Key);
        _collapsed = new(); _collapsedSummary = null;
        _digest = CardDigest(w, before, ev);
        _digestWindow = $"Tonight at {ev.Name}  ·  {Plural(ev.Card.Count(f => f.Outcome is not null), "bout")}";
        ShowEverything = false;
        Attach(w);
    }

    // --- The drawn state ------------------------------------------------------------
    public string Kicker { get; private set; } = "";
    public string Headline { get; private set; } = "";
    public string Lede { get; private set; } = "";
    public string Quote { get; private set; } = "";
    public string QuoteBy { get; private set; } = "";
    public bool HasQuote => Quote.Length > 0;
    public CardTone Tone { get; private set; }
    public string Icon { get; private set; } = "Bell";
    public ImageSource? Art { get; private set; }
    public bool HasArt => Art is not null;
    /// <summary>The art is a poster or a logo (drawn framed), not a fighter standing in the light.</summary>
    public bool ArtIsFramed { get; private set; }
    public bool IsResolved { get; private set; }
    public string ResolvedText { get; private set; } = "";
    public ObservableCollection<BriefingPerson> People { get; } = new();
    public bool HasPeople => People.Count > 0;
    public ObservableCollection<BriefingAction> Actions { get; } = new();

    public string DigestWindow => _digestWindow.Length > 0 ? _digestWindow : "Your promotion and your people, the last two weeks";
    public string DigestTitle => _stop is null ? "Lately" : _stop.Kind == BriefingKind.CardDone ? "The night" : "Since the last stop";
    public ObservableCollection<BriefingItem> DigestShown { get; } = new();
    public bool HasDigest => _digest.Count > 0;
    public bool DigestEmpty => _digest.Count == 0;
    public bool HasMoreDigest => _digest.Count > DigestCap || _collapsed.Count > 0;
    public string EverythingText => ShowEverything ? "Show fewer ▴" : $"Show everything ({FullDigest().Count}) ▾";

    // Low-profile retirements folded into one digest line; "show everything" lays them out.
    private List<BriefingItem> _collapsed = new();
    private BriefingItem? _collapsedSummary;

    private List<BriefingItem> FullDigest() =>
        _digest.SelectMany(i => ReferenceEquals(i, _collapsedSummary) ? _collapsed : new List<BriefingItem> { i }).ToList();

    private bool _showEverything;
    public bool ShowEverything
    {
        get => _showEverything;
        set
        {
            if (!Set(ref _showEverything, value)) return;
            FillDigest();
        }
    }

    public ObservableCollection<BriefingItem> Waiting { get; } = new();
    public bool HasWaiting => Waiting.Count > 0;

    // What is next
    public bool HasNextCard { get; private set; }
    public string NextTitle { get; private set; } = "";
    public string NextWhen { get; private set; } = "";
    public string NextDetail { get; private set; } = "";
    public ImageSource? NextPoster { get; private set; }
    public string NextCountdown { get; private set; } = "";
    public string NextCountdownUnit { get; private set; } = "";
    public string NextDeadline { get; private set; } = "";
    public bool HasNextDeadline => NextDeadline.Length > 0;
    public string NoCardLine { get; private set; } = "";
    public ICommand? NextCommand { get; private set; }
    public string NextActionText { get; private set; } = "";
    private int _nextPosterEvent = -1;
    private int _nextPosterBouts = -1;

    /// <summary>Redraw against the live world (every app refresh): the headline's fix may be done by now.</summary>
    public void Attach(World w)
    {
        _world = w;
        var needs = Needs(w);
        BuildHeadline(w, needs);
        BuildWaiting(w, needs);
        BuildNext(w);
        if (_stop is null) { _collapsed = new(); _collapsedSummary = null; _digest = DeskDigest(w); _digestWindow = ""; }
        FillDigest();
        OnPropertyChanged(string.Empty);   // everything above is plain properties: redraw it all
    }

    private void FillDigest()
    {
        DigestShown.Clear();
        foreach (var i in ShowEverything ? FullDigest() : _digest.Take(DigestCap)) DigestShown.Add(i);
        OnPropertyChanged(nameof(EverythingText));
        OnPropertyChanged(nameof(HasMoreDigest));
        OnPropertyChanged(nameof(HasDigest));
        OnPropertyChanged(nameof(DigestEmpty));
    }

    // =================================================================================
    // Needs: the standing calls on the desk, most pressing first.
    // =================================================================================
    private sealed record Need(BriefingKind Kind, string Key, int Rank, int Id = 0, DealOffer? Offer = null, DateOnly? Due = null, bool Single = true);

    /// <summary>
    /// A fighter whose deal is worth a headline of its own: a champion, the face of the
    /// company, or top-five in a division of yours (public facts only).
    /// </summary>
    private static bool Matters(World w, Promotion player, Fighter f) =>
        w.Titles.Values.Any(t => t.Promotion == player.Id && t.CurrentChampion == f.Id)
        || player.FaceOfCompany == f.Id
        || player.Divisions.Any(d => d.Matches(f) && d.Rankings.Take(5).Contains(f.Id));

    private static bool IsChampion(World w, Fighter f) => w.Titles.Values.Any(t => t.CurrentChampion == f.Id);

    // Your fighters in their re-sign window of one stage (last week or not), fewest days left first.
    private static List<Fighter> WindowFighters(World w, bool lastWeek) => w.Get(w.PlayerPromotion) is not { } p
        ? new List<Fighter>()
        : w.RosterOf(p.Id).Where(f => DealWindow.InWindow(w, f)
                                      && ((DealWindow.DaysLeft(w, f) ?? 99) <= DealWindow.WarnDays) == lastWeek)
            .OrderBy(f => DealWindow.DaysLeft(w, f) ?? 99).ThenBy(f => f.Id.Value).ToList();

    private static string DaysLeftText(int days) =>
        days <= 0 ? "last day" : days == 1 ? "1 day left" : $"{days} days left";

    private static List<Need> Needs(World w)
    {
        var list = new List<Need>();
        if (w.Get(w.PlayerPromotion) is not { } player) return list;
        var today = w.CurrentDate;

        foreach (var d in w.Decisions.Where(d => d.State == DecisionState.Pending).OrderBy(d => d.Deadline).ThenBy(d => d.Id))
            list.Add(new Need(BriefingKind.Decision, $"decision:{d.Id}", 100, d.Id, Due: d.Deadline));

        var roster = w.RosterOf(player.Id).Where(f => f.Status != FighterStatus.Retired).ToList();

        // Deals already up: the 30-day exclusive re-sign window (DealWindow). Fewest days left
        // first; a fighter who matters gets his own headline, the rest are grouped.
        foreach (var f in roster.Where(f => DealWindow.InWindow(w, f))
                     .OrderBy(f => DealWindow.DaysLeft(w, f) ?? DealWindow.WindowDays).ThenBy(f => f.Id.Value))
        {
            int left = DealWindow.DaysLeft(w, f) ?? DealWindow.WindowDays;
            var end = f.Contract!.WindowEndsOn ?? today.AddDays(left);
            bool single = Matters(w, player, f);
            if (left <= DealWindow.WarnDays)
                list.Add(new Need(BriefingKind.LastWeek, $"closing:{f.Id.Value}:{end.DayNumber}", single ? 90 : 95, f.Id.Value, Due: end, Single: single));
            else
                list.Add(new Need(BriefingKind.DealUp, $"window:{f.Id.Value}:{end.DayNumber}", single ? 105 : 150, f.Id.Value, Due: end, Single: single));
        }

        // Deals on their last fight: the softer, grouped heads-up.
        foreach (var f in roster.Where(f => f.Contract is { FightsRemaining: 1 } && !DealWindow.InWindow(w, f))
                     .OrderByDescending(f => FighterRow.Scouted(w, f)).ThenBy(f => f.Id.Value))
            list.Add(new Need(BriefingKind.ExpiringDeal, $"deal:{f.Id.Value}:1", 300, f.Id.Value));

        foreach (var o in player.Offers.Where(o => o.ExpiresOn >= today).OrderBy(o => o.ExpiresOn))
            list.Add(new Need(BriefingKind.Offer, $"offer:{o.Partner}:{o.ExpiresOn.DayNumber}", 400, Offer: o, Due: o.ExpiresOn));

        if (PromotionLadder.IsEligible(player))
            list.Add(new Need(BriefingKind.StepUp, $"stepup:{PromotionLadder.NextTier(player)}", 500));

        var mine = w.Events.Values.Where(e => e.Status == EventStatus.Scheduled && e.Promotion == player.Id)
            .OrderBy(e => e.Date).ThenBy(e => e.Id.Value).ToList();
        foreach (var e in mine.Where(e => e.Card.Count == 0 && e.Date.DayNumber - today.DayNumber <= 42))
            list.Add(new Need(BriefingKind.EmptyCard, $"empty:{e.Id.Value}", 600, e.Id.Value));
        if (mine.Count == 0 && roster.Count >= 2)
        {
            int last = w.Events.Values.Where(e => e.Promotion == player.Id && e.Status == EventStatus.Completed)
                .Select(e => e.Id.Value).DefaultIfEmpty(0).Max();
            list.Add(new Need(BriefingKind.NoCard, $"nocard:{last}", 700));
        }
        return list.OrderBy(n => n.Rank).ToList();
    }

    // The first call not yet headlined; else a decision on its last few days; else a quiet stretch.
    private Stop PickNeed(World w, int weeks)
    {
        var needs = Needs(w);
        if (needs.FirstOrDefault(n => !_announced.Contains(n.Key)) is { } fresh)
        {
            bool grouped = fresh.Kind == BriefingKind.ExpiringDeal
                           || (fresh.Kind is BriefingKind.DealUp or BriefingKind.LastWeek && !fresh.Single);
            if (!grouped)
                return new Stop(fresh.Kind, fresh.Key, w.CurrentDate, weeks, fresh.Id);
            // Deals run out in batches on a big roster: one headline for all of them, not one a week.
            var batch = needs.Where(n => n.Kind == fresh.Kind && n.Single == fresh.Single && !_announced.Contains(n.Key)).ToList();
            foreach (var n in batch) _announced.Add(n.Key);
            return new Stop(fresh.Kind, fresh.Key, w.CurrentDate, weeks, fresh.Id, Extra: batch.Count - 1, Grouped: true);
        }
        if (needs.FirstOrDefault(n => n.Kind == BriefingKind.Decision && n.Due is { } due
                                      && due.DayNumber - w.CurrentDate.DayNumber <= 7
                                      && !_announced.Contains("last" + n.Key)) is { } lastCall)
            return new Stop(BriefingKind.Decision, "last" + lastCall.Key, w.CurrentDate, weeks, lastCall.Id);
        return new Stop(BriefingKind.Quiet, $"quiet:{w.CurrentDate.DayNumber}", w.CurrentDate, weeks);
    }

    // =================================================================================
    // The headline
    // =================================================================================
    private void BuildHeadline(World w, List<Need> needs)
    {
        People.Clear();
        Actions.Clear();
        Art = null; ArtIsFramed = false;
        Quote = QuoteBy = "";
        IsResolved = false; ResolvedText = "";
        var player = w.Get(w.PlayerPromotion);
        var stop = _stop;
        var today = w.CurrentDate;
        string stretch = stop is null ? "" : stop.Weeks switch { 0 => "", 1 => "THE WEEK IS UP", _ => $"{stop.Weeks} WEEKS ARE UP" };

        // Opened by hand before any stop: lead with the most pressing call, or say the desk is clear.
        if (stop is null || stop.Kind == BriefingKind.Desk)
        {
            if (needs.FirstOrDefault() is { } top)
            {
                DrawNeed(w, top, $"YOUR DESK  ·  {today:dddd d MMMM}".ToUpperInvariant());
                return;
            }
            DrawQuiet(w, $"YOUR DESK  ·  {today:dddd d MMMM}".ToUpperInvariant(), "Nothing needs you right now.");
            return;
        }

        switch (stop.Kind)
        {
            case BriefingKind.FightNight:
            {
                var ev = w.Get(new EventId(stop.EventId));
                if (ev is null) { DrawQuiet(w, "FIGHT NIGHT", "The card is gone."); return; }
                if (ev.Status != EventStatus.Scheduled)
                {
                    // Fought (the card briefing replaces this one) or called off.
                    Kicker = $"FIGHT NIGHT  ·  {ev.Name}".ToUpperInvariant();
                    Headline = ev.Status == EventStatus.Completed ? $"{ev.Name} is in the books." : $"{ev.Name} was called off.";
                    Lede = "The clock is yours again.";
                    Resolve("Done");
                    Art = Poster(w, ev); ArtIsFramed = true;
                    Actions.Add(new BriefingAction("Carry on", CarryOnCommand, IsPrimary: true));
                    return;
                }
                Icon = "Bell"; Tone = CardTone.Bad;
                Kicker = $"TIME STOPPED  ·  FIGHT NIGHT  ·  {ev.Name}".ToUpperInvariant();
                Headline = $"{ev.Name} is tonight.";
                int bouts = ev.Card.Count;
                Lede = bouts == 0
                    ? "There isn't a bout left on the card. The clock waits until it is run — or called off from the card desk."
                    : $"{Plural(bouts, "bout")} ready to go{Where(w, ev)}. The clock waits for your show: watch it from ringside, or go straight to the results.";
                Art = Poster(w, ev); ArtIsFramed = true;
                AddMainEventFaces(w, ev);
                if (WatchTonightCommand is not null) Actions.Add(new BriefingAction("Watch it live ▸", Then(WatchTonightCommand), IsPrimary: true));
                if (SimTonightCommand is not null) Actions.Add(new BriefingAction("Results only", Then(SimTonightCommand)));
                Actions.Add(new BriefingAction("The card", Do(() => OpenCard?.Invoke(ev.Id.Value))));
                return;
            }

            case BriefingKind.MainEventLost:
            case BriefingKind.CardHole:
            {
                var ev = w.Get(new EventId(stop.EventId));
                var outF = stop.OutId != 0 ? w.Get(new FighterId(stop.OutId)) : null;
                var stay = stop.StayId != 0 ? w.Get(new FighterId(stop.StayId)) : null;
                bool main = stop.Kind == BriefingKind.MainEventLost;
                string evName = ev?.Name ?? "your card";
                int daysOut = ev is null ? 0 : ev.Date.DayNumber - today.DayNumber;
                Icon = main ? "Warning" : "Calendar";
                Tone = main ? CardTone.Bad : CardTone.Warn;
                Kicker = (main ? "TIME STOPPED  ·  YOUR MAIN EVENT" : $"{stretch}  ·  A HOLE IN YOUR CARD") +
                         (ev is null ? "" : $"  ·  {ev.Name}  ·  {DaysOut(daysOut)}");
                Kicker = Kicker.TrimStart(' ', '·').ToUpperInvariant();
                Headline = outF is null
                    ? (main ? $"{evName} lost its main event." : $"A bout came off {evName}.")
                    : main ? $"{outF.FirstName} {outF.LastName} is out of your main event." : $"{outF.FirstName} {outF.LastName} is off {evName}.";
                string why = stop.Reason.Length > 0 ? stop.Reason : (main ? "The headliner can't make the date." : "");
                string fix = stay is not null && !stay.LocalTalent
                    ? $" {stay.FirstName} {stay.LastName} was set to fight {(outF is null ? "them" : Pronoun(outF))} and needs a new opponent."
                    : "";
                string more = stop.Extra > 0 ? $" {Plural(stop.Extra, "other bout")} came off your cards too — the digest has {(stop.Extra == 1 ? "it" : "them")}." : "";
                Lede = (why + fix + (main ? $" {evName} needs a new headliner before {ev?.Date:d MMM}." : "") + more).Trim();
                if (outF is not null) { Art = Portraits.Body(outF, w); }
                else if (ev is not null) { Art = Poster(w, ev); ArtIsFramed = true; }
                if (outF is not null) People.Add(Person(w, outF, "Out"));
                if (stay is not null && !stay.LocalTalent) People.Add(Person(w, stay, "Needs an opponent"));

                bool gone = ev is null || ev.Status != EventStatus.Scheduled;
                bool refilled = ev is not null && ev.Card.Count >= stop.CardCount && stop.CardCount > 0;
                if (gone || refilled)
                {
                    Resolve(gone ? "The card is off the calendar" : "The card is full again");
                    Actions.Add(new BriefingAction("Carry on", CarryOnCommand, IsPrimary: true));
                    if (!gone) Actions.Add(new BriefingAction("The card", Do(() => OpenCard?.Invoke(stop.EventId))));
                    return;
                }
                Actions.Add(new BriefingAction(main ? "Find a new headliner ▸" : "Rebook the bout ▸", Do(() => OpenCard?.Invoke(stop.EventId)), IsPrimary: true,
                    Hint: "Opens the card on the booking desk."));
                if (stay is not null && !stay.LocalTalent)
                    Actions.Add(new BriefingAction($"{stay.LastName}'s profile", Do(() => OpenFighter?.Invoke(stay.Id.Value))));
                Actions.Add(new BriefingAction("Later", CarryOnCommand));
                return;
            }

            case BriefingKind.CardDone:
            {
                var ev = w.Get(new EventId(stop.EventId));
                if (ev is null) { DrawQuiet(w, "FIGHT NIGHT", "The card is in the books."); return; }
                Icon = "Trophy"; Tone = CardTone.Good;
                Kicker = $"{ev.Name}  ·  IN THE BOOKS".ToUpperInvariant();
                var mainBout = ev.Card.LastOrDefault();
                var o = mainBout?.Outcome;
                var winner = o?.Winner is { } wid ? w.Get(wid) : null;
                var loser = o?.Loser is { } lid ? w.Get(lid) : null;
                if (winner is not null && loser is not null && !mainBout!.OverturnedNc)
                {
                    Headline = $"{winner.FirstName} {winner.LastName} beat {loser.FirstName} {loser.LastName}.";
                    Art = Portraits.Body(winner, w);
                    People.Add(Person(w, winner, "Won the main event"));
                    People.Add(Person(w, loser, "Lost"));
                    Quote = MmaManager.Core.Sim.Flavor.Quotes.Say(winner,
                        IsDecision(o!.Method) ? MmaManager.Core.Sim.Flavor.Moment.WinDecision : MmaManager.Core.Sim.Flavor.Moment.WinFinish, ev.Date, loser);
                    QuoteBy = $"{winner.LastName}, after the main event";
                }
                else
                {
                    Headline = $"{ev.Name} is in the books.";
                    Art = Poster(w, ev); ArtIsFramed = true;
                }
                string how = o is null ? "" : $"{TitleMoment.MethodText(o.Method)}{(IsDecision(o.Method) ? "" : $", round {o.EndRound}")}. ";
                string night = ev.Financials is { } fin
                    ? $"{fin.Attendance:N0} in the house, gate {fin.GateRevenue}" + (fin.PayPerViewBuys > 0 ? $", {fin.PayPerViewBuys:N0} pay-per-view buys." : ".")
                    : "";
                Lede = (how + night).Trim();
                Actions.Add(new BriefingAction("The results ▸", Do(() => OpenEvent?.Invoke(ev.Id.Value)), IsPrimary: true));
                Actions.Add(new BriefingAction("Carry on", CarryOnCommand));
                return;
            }

            case BriefingKind.Quiet:
                DrawQuiet(w, $"{stretch}  ·  NOTHING NEEDS YOU".TrimStart(' ', '·'),
                    stop.Weeks > 1 ? "A quiet month." : "A quiet week.");
                return;

            default:
            {
                // A call on the desk. Find it again by key; gone = answered.
                string key = stop.Key.StartsWith("last", StringComparison.Ordinal) ? stop.Key[4..] : stop.Key;
                var need = needs.FirstOrDefault(n => n.Key == key);
                // A grouped headline stays up while any of its group is still open.
                if (need is null && stop.Grouped)
                    need = needs.FirstOrDefault(n => n.Kind == stop.Kind && (stop.Kind == BriefingKind.ExpiringDeal || !n.Single));
                string kicker = stretch.Length > 0 ? stretch : today.ToString("dddd d MMMM").ToUpperInvariant();
                if (need is null)
                {
                    DrawAnswered(w, stop, kicker);
                    return;
                }
                DrawNeed(w, need, kicker, lastCall: stop.Key.StartsWith("last", StringComparison.Ordinal), extra: stop.Extra);
                return;
            }
        }
    }

    private void DrawNeed(World w, Need need, string kicker, bool lastCall = false, int extra = 0)
    {
        var today = w.CurrentDate;
        var player = w.Get(w.PlayerPromotion);
        switch (need.Kind)
        {
            case BriefingKind.Decision when w.Decisions.FirstOrDefault(d => d.Id == need.Id) is { } d:
            {
                int days = d.Deadline.DayNumber - today.DayNumber;
                var who = d.Fighter is { } fid ? w.Get(fid) : null;
                var target = d.Promotion is { } pid ? w.Get(pid) : null;
                Icon = d.Kind switch { DecisionKind.TitleShotRequest => "Crown", DecisionKind.SponsorShowcase => "Money", DecisionKind.AcquisitionOffer => "Building", _ => "Warning" };
                Tone = days <= 7 ? CardTone.Bad : CardTone.Warn;
                Kicker = $"{kicker}  ·  {(lastCall ? "LAST CALL" : "A CALL ONLY YOU CAN MAKE")}  ·  ANSWER {(days <= 0 ? "TODAY" : days == 1 ? "BY TOMORROW" : $"BY {d.Deadline:d MMM}".ToUpperInvariant())}";
                Headline = d.Kind switch
                {
                    DecisionKind.TitleShotRequest => who is null ? "A contender wants a title shot." : $"{who.FirstName} {who.LastName} wants a title shot.",
                    DecisionKind.SponsorShowcase => $"{(d.Sponsor.Length > 0 ? d.Sponsor : "A sponsor")} wants a hometown showcase.",
                    DecisionKind.VeteranCut => who is null ? "The board wants a release." : $"The board wants {who.FirstName} {who.LastName} released.",
                    DecisionKind.AcquisitionOffer => target is null ? "A promotion is for sale." : $"{target.Name} is for sale.",
                    _ => "A decision for you.",
                };
                Lede = d.Detail + $" Ignoring it past {d.Deadline:d MMM} counts as an answer.";
                if (who is not null) { Art = Portraits.Body(who, w); People.Add(Person(w, who, FighterRow.DivisionLabel(who.WeightClass, who.Sex))); }
                else if (target is not null) { Art = LogoFactory.ForPromotion(target); ArtIsFramed = true; }
                else if (player is not null) { Art = LogoFactory.ForPromotion(player); ArtIsFramed = true; }

                if (AcceptDecisionCommand is not null && DeclineDecisionCommand is not null)
                {
                    var (yes, no) = d.Kind switch
                    {
                        DecisionKind.TitleShotRequest => ("Grant the shot", "Tell them to earn it"),
                        DecisionKind.SponsorShowcase => ("Agree to the showcase", "Pass"),
                        DecisionKind.VeteranCut => (who is null ? "Release him" : $"Release {who.LastName}", "Keep him"),
                        DecisionKind.AcquisitionOffer => (d.Price.Amount > 0 ? $"Buy it — {d.Price}" : "Buy it", "Let it go"),
                        _ => ("Do it", "Pass"),
                    };
                    if (who is not null && who.Sex == Sex.Female) { yes = yes.Replace("him", "her"); no = no.Replace("him", "her"); }
                    var accept = AcceptDecisionCommand;
                    ICommand yesCmd = d.Kind switch
                    {
                        // Expensive or destructive: ask first, and say what it costs.
                        DecisionKind.AcquisitionOffer => Ask(accept, d, $"Buy {target?.Name ?? "the promotion"}?",
                            $"{player?.Name ?? "You"} pays {d.Price} and takes over {target?.Name ?? "the promotion"} — roster, brand and all.",
                            $"That money comes straight out of the bank ({player?.Balance} today). There's no selling it back.", "Buy it"),
                        DecisionKind.VeteranCut when who is not null => Ask(accept, d, $"Release {who.FullName}?",
                            $"{who.FullName} comes off your roster and onto the free-agent market, where any promotion can sign {Pronoun(who)}.",
                            "The board gets its way, and the locker room notices how a name they respect was let go.", "Release"),
                        _ => Then(accept, d),
                    };
                    Actions.Add(new BriefingAction(yes, yesCmd, IsPrimary: true));
                    Actions.Add(new BriefingAction(no, Then(DeclineDecisionCommand, d)));
                }
                if (who is not null) Actions.Add(new BriefingAction($"{who.LastName}'s profile", Do(() => OpenFighter?.Invoke(who.Id.Value))));
                Actions.Add(new BriefingAction("Think it over", CarryOnCommand));
                return;
            }

            case BriefingKind.ExpiringDeal when w.Get(new FighterId(need.Id)) is { Contract: { } } f:
            {
                // The softer heads-up: deals with one fight left (not up yet). Always read as a group.
                var all = w.RosterOf(w.PlayerPromotion)
                    .Where(x => x.Status != FighterStatus.Retired && x.Contract is { FightsRemaining: 1 } && !DealWindow.InWindow(w, x))
                    .OrderBy(x => x.Id == f.Id ? 0 : 1).ThenByDescending(x => FighterRow.Scouted(w, x)).ThenBy(x => x.Id.Value).ToList();
                Icon = "Pen"; Tone = CardTone.Warn;
                Kicker = $"{kicker}  ·  CONTRACTS  ·  HEADS UP";
                Headline = all.Count > 1
                    ? $"{all.Count} deals are on their last fight."
                    : $"{f.FirstName} {f.LastName} is on the last fight of the deal.";
                var booked = w.Events.Values.Where(e => e.Status == EventStatus.Scheduled && e.Promotion == w.PlayerPromotion
                                                         && e.Card.Any(x => x.RedCorner == f.Id || x.BlueCorner == f.Id))
                    .OrderBy(e => e.Date).FirstOrDefault();
                Lede = (all.Count > 1
                           ? $"Nothing is up yet. When each of them fights out the deal, a {DealWindow.WindowDays}-day window opens to re-sign him before he walks — or talk terms now, while he's still under contract."
                           : $"{FighterRow.DivisionLabel(f.WeightClass, f.Sex)}, {f.Record}. " +
                             (booked is not null ? $"Booked on {booked.Name} ({booked.Date:d MMM}); after that bout the deal is done and a {DealWindow.WindowDays}-day re-sign window opens. " : "") +
                             "Re-sign now and there's no window to worry about.");
                Art = Portraits.Body(f, w);
                foreach (var x in all.Take(4))
                    People.Add(Person(w, x, x.Id == f.Id && all.Count == 1 ? "Final fight on the deal" : "Final fight"));
                if (NegotiateCommand is not null) Actions.Add(new BriefingAction($"Talk terms with {f.LastName} ▸", Then(NegotiateCommand, f.Id.Value), IsPrimary: true));
                if (all.Count > 1) Actions.Add(new BriefingAction("All of them on Contracts", Do(() => Navigate?.Invoke(Tab.Contracts))));
                else Actions.Add(new BriefingAction("Profile", Do(() => OpenFighter?.Invoke(f.Id.Value))));
                Actions.Add(new BriefingAction("Later", CarryOnCommand));
                return;
            }

            case BriefingKind.DealUp or BriefingKind.LastWeek when w.Get(new FighterId(need.Id)) is { Contract: { } c } f && DealWindow.InWindow(w, f):
            {
                bool lastWeek = need.Kind == BriefingKind.LastWeek;
                bool grouped = !need.Single;
                var end = c.WindowEndsOn ?? today.AddDays(DealWindow.WindowDays);
                int left = DealWindow.DaysLeft(w, f) ?? DealWindow.WindowDays;
                bool walked = DealWindow.TalksClosed(w, f);
                bool champ = IsChampion(w, f);
                Icon = "Pen"; Tone = lastWeek ? CardTone.Bad : CardTone.Warn;
                var group = grouped ? WindowFighters(w, lastWeek).Where(x => player is null || !Matters(w, player, x)).ToList() : new List<Fighter> { f };
                if (group.Count == 0) group.Add(f);
                var first = group[0];
                var firstEnd = first.Contract!.WindowEndsOn ?? end;

                if (grouped && group.Count > 1)
                {
                    Kicker = $"{kicker}  ·  {(lastWeek ? "LAST WEEK TO RE-SIGN" : "DEALS UP")}  ·  FIRST CLOSES {firstEnd:d MMM}".ToUpperInvariant();
                    Headline = lastWeek
                        ? $"Last week for {group.Count} deals: re-sign by {firstEnd:d MMM}."
                        : $"{group.Count} deals are up: re-sign by {firstEnd:d MMM}.";
                    Lede = $"They've fought out their deals. Each talks to nobody but you until his window closes, can't be booked until he re-signs, "
                         + "and walks to free agency the day it closes. Fewest days left first:";
                    Art = Portraits.Body(first, w);
                    foreach (var x in group.Take(5))
                        People.Add(Person(w, x, DealWindow.TalksClosed(w, x) ? "Walked out of talks" : DaysLeftText(DealWindow.DaysLeft(w, x) ?? 0)));
                    if (NegotiateCommand is not null && !DealWindow.TalksClosed(w, first))
                        Actions.Add(new BriefingAction($"Talk terms with {first.LastName} ▸", Then(NegotiateCommand, first.Id.Value), IsPrimary: true));
                    Actions.Add(new BriefingAction("All of them on Contracts", Do(() => Navigate?.Invoke(Tab.Contracts)), IsPrimary: Actions.Count == 0));
                    Actions.Add(new BriefingAction("Later", CarryOnCommand));
                    return;
                }

                string he = Pronoun(f) == "her" ? "she" : "he";
                string He = he == "she" ? "She" : "He";
                Art = Portraits.Body(f, w);
                if (walked)
                {
                    Tone = CardTone.Bad;
                    Kicker = $"{kicker}  ·  CONTRACTS  ·  WINDOW CLOSES {end:d MMM}".ToUpperInvariant();
                    Headline = $"{f.FirstName} {f.LastName} walked out. {He} tests the market on {end:d MMM}.";
                    Lede = $"{He} won't sit down with you again before {Possessive(f)} window closes. Then {he}'s a free agent and anyone can have {Pronoun(f)} — you included."
                         + (champ ? $" If {he} walks, the belt is vacant." : "");
                    People.Add(Person(w, f, "Walked out of talks"));
                    Actions.Add(new BriefingAction("Profile ▸", Do(() => OpenFighter?.Invoke(f.Id.Value)), IsPrimary: true));
                    Actions.Add(new BriefingAction("Contracts", Do(() => Navigate?.Invoke(Tab.Contracts))));
                    Actions.Add(new BriefingAction("Later", CarryOnCommand));
                    return;
                }
                Kicker = $"{kicker}  ·  {(lastWeek ? "LAST WEEK TO RE-SIGN" : "A DEAL IS UP")}  ·  {(champ ? "YOUR CHAMPION" : "CONTRACTS")}  ·  WINDOW CLOSES {end:d MMM}".ToUpperInvariant();
                Headline = lastWeek
                    ? (left <= 0 ? $"Last day to re-sign {f.FirstName} {f.LastName}." : $"{Plural(left, "day")} left to re-sign {f.FirstName} {f.LastName}.")
                    : $"{f.FirstName} {f.LastName}'s deal is up. {Plural(left, "day")} to re-sign.";
                Lede = $"{FighterRow.DivisionLabel(f.WeightClass, f.Sex)}, {f.Record}. {He} talks to nobody but you until {end:d MMMM} and can't be booked until {he} re-signs. "
                     + $"After that {he}'s a free agent and anyone can have {Pronoun(f)}."
                     + (champ ? $" {He} keeps the belt until then; if {he} walks, it's vacant." : "")
                     + (player?.FaceOfCompany == f.Id ? $" {He}'s the face of the company." : "");
                People.Add(Person(w, f, DaysLeftText(left)));
                if (NegotiateCommand is not null)
                    Actions.Add(new BriefingAction($"Talk terms with {f.LastName} ▸", Then(NegotiateCommand, f.Id.Value), IsPrimary: true,
                        Hint: "Opens contract talks with him on the Contracts desk."));
                Actions.Add(new BriefingAction("Profile", Do(() => OpenFighter?.Invoke(f.Id.Value))));
                Actions.Add(new BriefingAction("Later", CarryOnCommand));
                return;
            }

            case BriefingKind.Offer when need.Offer is { } o:
            {
                bool media = o.Media is not null;
                int days = o.ExpiresOn.DayNumber - today.DayNumber;
                Icon = media ? "Broadcast" : "Money"; Tone = CardTone.Info;
                Kicker = $"{kicker}  ·  AN OFFER ON THE TABLE  ·  LAPSES {(days <= 1 ? "TOMORROW" : $"{o.ExpiresOn:d MMM}".ToUpperInvariant())}";
                Headline = media ? $"{o.Partner} wants to put you on air." : $"{o.Partner} wants to sponsor you.";
                Lede = $"{o.FeePerEvent} an event. Say yes or no before {o.ExpiresOn:d MMM}, or the offer lapses on its own.";
                if (player is not null) { Art = LogoFactory.ForPromotion(player); ArtIsFramed = true; }
                Actions.Add(new BriefingAction(media ? "The media desk ▸" : "Finance ▸", Do(() => Navigate?.Invoke(media ? Tab.Media : Tab.Finance)), IsPrimary: true));
                Actions.Add(new BriefingAction("Later", CarryOnCommand));
                return;
            }

            case BriefingKind.StepUp when player is not null:
            {
                var to = PromotionLadder.NextTier(player);
                Icon = "Building"; Tone = CardTone.Good;
                Kicker = $"{kicker}  ·  THE CLIMB";
                Headline = $"You can step up to {OrgSizing.Label(to)}.";
                Lede = $"Bigger gates and better talent at {OrgSizing.Label(to)} — and bigger running costs. Only take the leap when the bank can carry it ({player.Balance} today).";
                Art = LogoFactory.ForPromotion(player); ArtIsFramed = true;
                Actions.Add(new BriefingAction("HQ ▸", Do(() => Navigate?.Invoke(Tab.Org)), IsPrimary: true, Hint: "The step up is made from HQ, and it asks before it costs you anything."));
                Actions.Add(new BriefingAction("Not yet", CarryOnCommand));
                return;
            }

            case BriefingKind.EmptyCard when w.Get(new EventId(need.Id)) is { } ev:
            {
                int days = ev.Date.DayNumber - today.DayNumber;
                Icon = "Calendar"; Tone = CardTone.Warn;
                Kicker = $"{kicker}  ·  {ev.Name}  ·  {DaysOut(days)}".ToUpperInvariant();
                Headline = $"{ev.Name} has no fights on it.";
                Lede = $"The date and the room are set{Where(w, ev)}. {DaysOut(days, sentence: true)} — give people a reason to buy a ticket.";
                Art = Poster(w, ev); ArtIsFramed = true;
                Actions.Add(new BriefingAction("Build the card ▸", Do(() => OpenCard?.Invoke(ev.Id.Value)), IsPrimary: true));
                Actions.Add(new BriefingAction("Later", CarryOnCommand));
                return;
            }

            case BriefingKind.NoCard when player is not null:
            {
                int roster = w.RosterOf(player.Id).Count(f => f.Status != FighterStatus.Retired);
                Icon = "Calendar"; Tone = CardTone.Warn;
                Kicker = $"{kicker}  ·  THE CALENDAR";
                Headline = "Nothing on the calendar.";
                Lede = $"{roster} fighters and no show to fight on. Fighters want fights, and a promotion that goes dark slips out of the conversation.";
                Art = LogoFactory.ForPromotion(player); ArtIsFramed = true;
                Actions.Add(new BriefingAction("Plan a card ▸", Do(() => Navigate?.Invoke(Tab.Planner)), IsPrimary: true, Hint: "The Planner shows the open dates and the rival cards around them."));
                Actions.Add(new BriefingAction("Later", CarryOnCommand));
                return;
            }
        }
        DrawQuiet(w, kicker, "Nothing needs you right now.");
    }

    private void DrawAnswered(World w, Stop stop, string kicker)
    {
        var player = w.Get(w.PlayerPromotion);
        Icon = "Check"; Tone = CardTone.Good;
        Kicker = kicker;
        Headline = stop.Kind switch
        {
            BriefingKind.Decision => "That call is made.",
            BriefingKind.ExpiringDeal when w.Get(new FighterId(stop.EventId)) is { } f => $"{f.FirstName} {f.LastName}'s deal is settled.",
            BriefingKind.DealUp or BriefingKind.LastWeek when w.Get(new FighterId(stop.EventId)) is { } f =>
                f.Contract?.Promotion == w.PlayerPromotion ? $"{f.FirstName} {f.LastName} re-signed."
                : f.Contract is null ? $"{f.FirstName} {f.LastName} is a free agent now." : $"{f.FirstName} {f.LastName} has moved on.",
            BriefingKind.Offer => "The offer is answered.",
            BriefingKind.StepUp => "The step up is settled.",
            BriefingKind.EmptyCard => "The card has fights on it now.",
            BriefingKind.NoCard => "There's a card on the calendar.",
            _ => "Sorted.",
        };
        Lede = "Nothing else on this needs you. The rest of the desk is below.";
        Resolve("Done");
        if (stop.Kind is BriefingKind.ExpiringDeal or BriefingKind.DealUp or BriefingKind.LastWeek && w.Get(new FighterId(stop.EventId)) is { } who)
        {
            Art = Portraits.Body(who, w);
            People.Add(Person(w, who, who.Contract?.Promotion == w.PlayerPromotion ? $"{Plural(who.Contract.FightsRemaining, "fight")} on the deal" : "No longer yours"));
        }
        else if (player is not null) { Art = LogoFactory.ForPromotion(player); ArtIsFramed = true; }
        Actions.Add(new BriefingAction("Carry on", CarryOnCommand, IsPrimary: true));
    }

    private void DrawQuiet(World w, string kicker, string headline)
    {
        var player = w.Get(w.PlayerPromotion);
        Icon = "Check"; Tone = CardTone.Good;
        Kicker = kicker;
        Headline = headline;
        var next = NextCard(w);
        bool waiting = Needs(w).Count > 0;
        if (waiting) Kicker = Kicker.Replace("NOTHING NEEDS YOU", "NOTHING NEW NEEDS YOU");
        string calm = waiting
            ? "Nothing new landed on the desk; the calls still open are under “Also on your desk”."
            : "No call on the desk needs an answer.";
        Lede = next is null
            ? $"{calm} The digest below has what moved while the clock ran."
            : $"{calm} {next.Name} is {DaysOut(next.Date.DayNumber - w.CurrentDate.DayNumber).ToLowerInvariant()}" +
              (next.Card.Count > 0 ? $", {Plural(next.Card.Count, "bout")} on it." : ", and there's nothing on it yet.");
        if (next is not null) { Art = Poster(w, next); ArtIsFramed = true; AddMainEventFaces(w, next); }
        else if (player is not null) { Art = LogoFactory.ForPromotion(player); ArtIsFramed = true; }
        Actions.Add(new BriefingAction("Carry on", CarryOnCommand, IsPrimary: true));
        if (AdvanceWeekCommand is not null)
            Actions.Add(new BriefingAction("Advance another week ▸", Then(AdvanceWeekCommand)));
    }

    private void Resolve(string text)
    {
        IsResolved = true;
        ResolvedText = "✓  " + text;
        Tone = CardTone.Good;
    }

    // =================================================================================
    // Also on your desk: every standing call but the headline's, most pressing first.
    // =================================================================================
    private void BuildWaiting(World w, List<Need> needs)
    {
        Waiting.Clear();
        var today = w.CurrentDate;
        string? headKey = _stop?.Key is { } k && k.StartsWith("last", StringComparison.Ordinal) ? k[4..] : _stop?.Key;
        if (_stop is null && needs.Count > 0) headKey = needs[0].Key;   // the desk view headlines the first
        // A grouped headline already shows its whole group.
        // (The desk view, with no stop, headlines the first need and its group the same way.)
        var top = _stop is null ? needs.FirstOrDefault() : null;
        (BriefingKind Kind, bool Grouped)? head = _stop is { } st ? (st.Kind, st.Grouped)
            : top is null ? null : (top.Kind, top.Kind == BriefingKind.ExpiringDeal || !top.Single);
        bool InHeadGroup(Need n) => head is { Grouped: true } h && n.Kind == h.Kind
                                    && (h.Kind == BriefingKind.ExpiringDeal || !n.Single);
        var rest = needs.Where(n => n.Key != headKey && !InHeadGroup(n)).ToList();

        // Deals already up, one row each with the days left (fewest first); a long tail is one line.
        bool IsWindow(Need n) => n.Kind is BriefingKind.DealUp or BriefingKind.LastWeek;
        var windows = rest.Where(IsWindow).OrderBy(n => n.Due ?? DateOnly.MaxValue).ThenBy(n => n.Id).ToList();
        if (windows.Count > 0)
        {
            int at = rest.FindIndex(IsWindow);
            rest.RemoveAll(IsWindow);
            var shown = windows.Take(windows.Count > 4 ? 3 : 4).ToList();
            rest.InsertRange(at, shown);
            if (windows.Count > shown.Count)
                rest.Insert(at + shown.Count, new Need(BriefingKind.DealUp, "moredeals", 0, -(windows.Count - shown.Count)));
        }
        // Several deals running out read as one line (the Contracts desk lists them all).
        var deals = rest.Where(n => n.Kind == BriefingKind.ExpiringDeal).ToList();
        if (deals.Count > 1)
        {
            int at = rest.IndexOf(deals[0]);
            rest.RemoveAll(n => n.Kind == BriefingKind.ExpiringDeal);
            rest.Insert(at, new Need(BriefingKind.ExpiringDeal, "deals", 300, -deals.Count));
        }
        foreach (var n in rest.Take(6))
        {
            if (n.Kind == BriefingKind.DealUp && n.Id < 0)
            {
                Waiting.Add(new BriefingItem
                {
                    Icon = "Pen", Text = $"{-n.Id} more deal{(n.Id == -1 ? " is" : "s are")} up",
                    Detail = "Every open re-sign window, soonest first", Tone = CardTone.Warn,
                    ActionText = "Contracts ▸", Command = Do(() => Navigate?.Invoke(Tab.Contracts)),
                });
                continue;
            }
            if (IsWindow(n) && w.Get(new FighterId(n.Id)) is { } wf)
            {
                int left = DealWindow.DaysLeft(w, wf) ?? 0;
                bool walked = DealWindow.TalksClosed(w, wf);
                Waiting.Add(new BriefingItem
                {
                    Icon = "Pen", Picture = Portraits.Thumb(wf, w),
                    Text = walked ? $"{wf.FirstName} {wf.LastName} walked out of talks" : $"{wf.FirstName} {wf.LastName}'s deal is up",
                    Detail = walked ? $"Tests the market on {n.Due:d MMM}" : $"{DaysLeftText(left)}  ·  re-sign by {n.Due:d MMM}",
                    Tone = n.Kind == BriefingKind.LastWeek || walked ? CardTone.Bad : CardTone.Warn,
                    ActionText = walked ? "Profile ▸" : "Talk terms ▸",
                    Command = walked || NegotiateCommand is null ? Do(() => OpenFighter?.Invoke(wf.Id.Value)) : Then(NegotiateCommand, wf.Id.Value),
                });
                continue;
            }
            if (n.Kind == BriefingKind.ExpiringDeal && n.Id < 0)
            {
                var faces = deals.Select(d => w.Get(new FighterId(d.Id))).OfType<Fighter>().ToList();
                Waiting.Add(new BriefingItem
                {
                    Icon = "Pen", Picture = faces.Count > 0 ? Portraits.Thumb(faces[0], w) : null,
                    Text = $"{-n.Id} deals are on their last fight",
                    Detail = string.Join(", ", faces.Take(3).Select(f => f.LastName)) + (faces.Count > 3 ? $" and {faces.Count - 3} more" : ""),
                    Tone = CardTone.Warn,
                    ActionText = "Contracts ▸", Command = Do(() => Navigate?.Invoke(Tab.Contracts)),
                });
                continue;
            }
            switch (n.Kind)
            {
                case BriefingKind.Decision when w.Decisions.FirstOrDefault(d => d.Id == n.Id) is { } d:
                {
                    var who = d.Fighter is { } fid ? w.Get(fid) : null;
                    int days = d.Deadline.DayNumber - today.DayNumber;
                    Waiting.Add(new BriefingItem
                    {
                        Icon = "Warning", Picture = who is null ? null : Portraits.Thumb(who, w),
                        Text = d.Kind switch
                        {
                            DecisionKind.TitleShotRequest => who is null ? "A title shot is being demanded" : $"{who.FirstName} {who.LastName} wants a title shot",
                            DecisionKind.SponsorShowcase => $"{d.Sponsor} wants a showcase",
                            DecisionKind.VeteranCut => "The board wants a release",
                            DecisionKind.AcquisitionOffer => "A promotion is for sale",
                            _ => "A decision for you",
                        },
                        Detail = days <= 0 ? "Answer due today" : $"Answer by {d.Deadline:d MMM}",
                        Tone = days <= 7 ? CardTone.Bad : CardTone.Warn,
                        ActionText = "The letter ▸", Command = Do(() => Navigate?.Invoke(Tab.Inbox)),
                    });
                    break;
                }
                case BriefingKind.ExpiringDeal when w.Get(new FighterId(n.Id)) is { Contract: { } c } f:
                    Waiting.Add(new BriefingItem
                    {
                        Icon = "Pen", Picture = Portraits.Thumb(f, w),
                        Text = $"{f.FirstName} {f.LastName}: last fight on the deal",
                        Detail = $"{FighterRow.DivisionLabel(f.WeightClass, f.Sex)}  ·  {f.Record}",
                        Tone = CardTone.Warn,
                        ActionText = "Talk terms ▸", Command = NegotiateCommand is null ? null : Then(NegotiateCommand, f.Id.Value),
                    });
                    break;
                case BriefingKind.Offer when n.Offer is { } o:
                {
                    bool media = o.Media is not null;
                    Waiting.Add(new BriefingItem
                    {
                        Icon = media ? "Broadcast" : "Money",
                        Text = media ? $"{o.Partner} has a media offer on the table" : $"{o.Partner} wants to sponsor you",
                        Detail = $"{o.FeePerEvent} an event  ·  lapses {o.ExpiresOn:d MMM}",
                        Tone = CardTone.Info,
                        ActionText = media ? "Media ▸" : "Finance ▸", Command = Do(() => Navigate?.Invoke(media ? Tab.Media : Tab.Finance)),
                    });
                    break;
                }
                case BriefingKind.StepUp when w.Get(w.PlayerPromotion) is { } p:
                    Waiting.Add(new BriefingItem
                    {
                        Icon = "Building", Text = $"You can step up to {OrgSizing.Label(PromotionLadder.NextTier(p))}",
                        Detail = "Bigger gates, bigger costs", Tone = CardTone.Good,
                        ActionText = "HQ ▸", Command = Do(() => Navigate?.Invoke(Tab.Org)),
                    });
                    break;
                case BriefingKind.EmptyCard when w.Get(new EventId(n.Id)) is { } ev:
                    Waiting.Add(new BriefingItem
                    {
                        Icon = "Calendar", Text = $"{ev.Name} has no fights yet",
                        Detail = $"{ev.Date:ddd d MMM}  ·  {DaysOut(ev.Date.DayNumber - today.DayNumber).ToLowerInvariant()}", Tone = CardTone.Warn,
                        ActionText = "Build it ▸", Command = Do(() => OpenCard?.Invoke(ev.Id.Value)),
                    });
                    break;
                case BriefingKind.NoCard:
                    Waiting.Add(new BriefingItem
                    {
                        Icon = "Calendar", Text = "Nothing on the calendar", Detail = "Fighters want fights", Tone = CardTone.Warn,
                        ActionText = "Planner ▸", Command = Do(() => Navigate?.Invoke(Tab.Planner)),
                    });
                    break;
            }
        }
        int unread = w.Mail.Count(m => !m.Read);
        if (unread > 0)
        {
            var latest = Enumerable.Reverse(w.Mail).FirstOrDefault(m => !m.Read);
            Waiting.Add(new BriefingItem
            {
                Icon = "Envelope", Text = unread == 1 ? "1 unread letter" : $"{unread} unread letters",
                Detail = latest is null ? "" : $"Latest: {latest.Subject}", Tone = CardTone.Info,
                ActionText = "Inbox ▸", Command = Do(() => Navigate?.Invoke(Tab.Inbox)),
            });
        }
        OnPropertyChanged(nameof(HasWaiting));
    }

    // =================================================================================
    // What is next
    // =================================================================================
    private static Event? NextCard(World w) => w.Events.Values
        .Where(e => e.Status == EventStatus.Scheduled && e.Promotion == w.PlayerPromotion)
        .OrderBy(e => e.Date).ThenBy(e => e.Id.Value).FirstOrDefault();

    private void BuildNext(World w)
    {
        var today = w.CurrentDate;
        var next = NextCard(w);
        HasNextCard = next is not null;
        if (next is not null)
        {
            int days = next.Date.DayNumber - today.DayNumber;
            NextTitle = next.Name;
            NextWhen = $"{next.Date:dddd d MMMM}{Where(w, next)}";
            NextCountdown = days <= 0 ? "TONIGHT" : days.ToString();
            NextCountdownUnit = days <= 0 ? "" : days == 1 ? "day to go" : "days to go";
            string main = next.Card.LastOrDefault() is { } m && w.Get(m.RedCorner) is { } r && w.Get(m.BlueCorner) is { } b
                ? $"{r.LastName} vs {b.LastName}" + (m.IsTitleFight ? " for the belt" : "") : "";
            NextDetail = next.Card.Count == 0 ? "No bouts on it yet."
                : $"{Plural(next.Card.Count, "bout")}" + (main.Length > 0 ? $"  ·  main event {main}" : "");
            if (next.Id.Value != _nextPosterEvent || next.Card.Count != _nextPosterBouts || NextPoster is null)
            {
                NextPoster = Poster(w, next);
                _nextPosterEvent = next.Id.Value;
                _nextPosterBouts = next.Card.Count;
            }
            int id = next.Id.Value;
            NextActionText = next.Card.Count == 0 ? "Build the card ▸" : "The card ▸";
            NextCommand = Do(() => OpenCard?.Invoke(id));
            NoCardLine = "";
        }
        else
        {
            NextTitle = NextWhen = NextDetail = NextCountdown = NextCountdownUnit = "";
            NextPoster = null; _nextPosterEvent = -1;
            NoCardLine = "Nothing booked. Pick a date that doesn't clash with a bigger show nearby.";
            NextActionText = "Plan a card ▸";
            NextCommand = Do(() => Navigate?.Invoke(Tab.Planner));
        }

        // The nearest answer the desk owes somebody.
        var player = w.Get(w.PlayerPromotion);
        var deadlines = new List<(DateOnly When, string What)>();
        foreach (var d in w.Decisions.Where(d => d.State == DecisionState.Pending))
            deadlines.Add((d.Deadline, "an answer on the desk"));
        if (player is not null)
            foreach (var o in player.Offers.Where(o => o.ExpiresOn >= today))
                deadlines.Add((o.ExpiresOn, $"{o.Partner}'s offer lapses"));
        var soonest = deadlines.OrderBy(x => x.When).FirstOrDefault();
        NextDeadline = soonest.What is null ? ""
            : $"Next deadline: {soonest.What}, {soonest.When:ddd d MMM} ({DaysOut(soonest.When.DayNumber - today.DayNumber).ToLowerInvariant()})";
    }

    // =================================================================================
    // The digest: what happened since the last stop, people first.
    // =================================================================================
    private sealed record Hole(int EventId, int OutId, int StayId, string Reason, bool WasMain, int CardBefore);

    // Bouts that were on a still-scheduled card of yours before the stretch and are not now.
    private static List<Hole> Holes(World w, BriefingBaseline before)
    {
        var list = new List<Hole>();
        var newMail = before.NewMail(w).ToList();
        foreach (var (evId, bouts) in before.Cards.OrderBy(kv => w.Get(new EventId(kv.Key))?.Date ?? DateOnly.MaxValue).ThenBy(kv => kv.Key))
        {
            if (w.Get(new EventId(evId)) is not { Status: EventStatus.Scheduled } ev) continue;
            var now = ev.Card.Select(f => f.Id.Value).ToHashSet();
            for (int i = 0; i < bouts.Count; i++)
            {
                var (fight, red, blue) = bouts[i];
                if (now.Contains(fight)) continue;
                // The matchmaking letter names who is out and why.
                var letter = newMail.LastOrDefault(m => m.From == "Matchmaking" && m.Subject.EndsWith($" is off {ev.Name}", StringComparison.Ordinal)
                                                        && m.AboutFighter is { } a && (a.Value == red || a.Value == blue));
                int outId = letter?.AboutFighter?.Value ?? Unavailable(w, ev, red, blue);
                int stayId = outId == red ? blue : outId == blue ? red : 0;
                string reason = letter is null ? "" : FirstSentence(letter.Body);
                list.Add(new Hole(evId, outId, stayId, reason, i == bouts.Count - 1, bouts.Count));
            }
        }
        // Main events first, then by date (already in date order).
        return list.OrderByDescending(h => h.WasMain).ToList();
    }

    // No letter: the corner who can't be there (retired, gone, hurt past the date), else red.
    private static int Unavailable(World w, Event ev, int red, int blue)
    {
        bool Off(int id) => w.Get(new FighterId(id)) is not { } f || f.Status == FighterStatus.Retired
                            || f.Contract?.Promotion != ev.Promotion && !f.LocalTalent
                            || f.Status == FighterStatus.Injured;
        return Off(red) ? red : Off(blue) ? blue : red;
    }

    private static string FirstSentence(string body)
    {
        // "X is off AFC 1: he retired. His bout with Y needs..." -> "X is off AFC 1: he retired."
        int dot = body.IndexOf(". ", StringComparison.Ordinal);
        string s = dot > 0 ? body[..(dot + 1)] : body;
        int colon = s.IndexOf(": ", StringComparison.Ordinal);
        if (colon > 0 && colon + 2 < s.Length)
        {
            string why = s[(colon + 2)..];
            return char.ToUpperInvariant(why[0]) + why[1..];
        }
        return s;
    }

    private List<BriefingItem> Digest(World w, BriefingBaseline before, List<Hole> holes, (int Event, int Out)? skipHole)
    {
        var items = new List<BriefingItem>();
        _collapsed = new();
        _collapsedSummary = null;
        var player = w.Get(w.PlayerPromotion);
        if (player is null) return items;
        var rosterNow = w.RosterOf(player.Id).ToList();
        var nowIds = rosterNow.Select(f => f.Id.Value).ToHashSet();

        // Other bouts off your cards.
        foreach (var h in holes)
        {
            if (skipHole is { } s && s.Event == h.EventId && s.Out == h.OutId) continue;
            var ev = w.Get(new EventId(h.EventId));
            var outF = w.Get(new FighterId(h.OutId));
            int id = h.EventId;
            items.Add(new BriefingItem
            {
                Icon = "Calendar", Picture = outF is null ? null : Portraits.Thumb(outF, w), Weight = 90,
                Kicker = "OFF YOUR CARD",
                Text = outF is null ? $"A bout came off {ev?.Name}" : $"{outF.FirstName} {outF.LastName} is off {ev?.Name}",
                Detail = h.Reason, Tone = CardTone.Warn,
                ActionText = "Rebook ▸", Command = Do(() => OpenCard?.Invoke(id)),
            });
        }

        // Signings and departures.
        foreach (var f in rosterNow.Where(f => !before.Roster.Contains(f.Id.Value)).OrderBy(f => f.Id.Value))
        {
            int id = f.Id.Value;
            items.Add(new BriefingItem
            {
                Icon = "People", Picture = Portraits.Thumb(f, w), Weight = 70, Kicker = "SIGNED",
                Text = $"{f.FirstName} {f.LastName} joined the roster",
                Detail = $"{FighterRow.DivisionLabel(f.WeightClass, f.Sex)}  ·  {f.Record}", Tone = CardTone.Good,
                ActionText = "Profile ▸", Command = Do(() => OpenFighter?.Invoke(id)),
            });
        }
        var newMail = before.NewMail(w).ToList();
        var quietRetirements = new List<BriefingItem>();
        foreach (int gone in before.Roster.Where(id => !nowIds.Contains(id)).OrderBy(id => id))
        {
            var f = w.Get(new FighterId(gone));
            if (f is null || f.LocalTalent) continue;
            // Walked when his re-sign window closed (DealWindow mails "X is a free agent").
            bool windowClosed = f.Contract is null && newMail.Any(m => m.From == "Matchmaking" && m.AboutFighter == f.Id
                                                                       && m.Subject == $"{f.FullName} is a free agent");
            bool retired = f.Status == FighterStatus.Retired;
            string where = retired ? "retired"
                : windowClosed ? $"left as a free agent when {Possessive(f)} window closed"
                : f.Contract is { } c && w.Get(c.Promotion) is { } other ? $"signed with {other.Name}"
                : "is a free agent now";
            var item = new BriefingItem
            {
                Icon = "People", Picture = Portraits.Thumb(f, w), Kicker = retired ? "RETIRED" : "LEFT",
                Weight = retired ? 60 : windowClosed ? 88 : 85,
                Text = $"{f.FirstName} {f.LastName} {where}",
                Detail = $"{FighterRow.DivisionLabel(f.WeightClass, f.Sex)}  ·  {f.Record}",
                Tone = retired ? CardTone.Neutral : CardTone.Bad,
                ActionText = "Profile ▸", Command = Do(() => OpenFighter?.Invoke(gone)),
            };
            // A retirement nobody outside the gym will notice is folded into one line.
            bool notable = f.Record.Wins >= 12 || w.Titles.Values.Any(t => t.Lineage.Any(r => r.Champion == f.Id));
            if (retired && !notable) quietRetirements.Add(item);
            else items.Add(item);
        }
        if (quietRetirements.Count == 1) items.Add(quietRetirements[0]);
        else if (quietRetirements.Count > 1)
        {
            var names = quietRetirements.Select(i => i.Text.Replace(" retired", "")).ToList();
            _collapsedSummary = new BriefingItem
            {
                Icon = "People", Kicker = "RETIRED", Weight = 20,
                Text = $"{quietRetirements.Count} fighters retired",
                Detail = string.Join(", ", names.Take(4)) + (names.Count > 4 ? $" and {names.Count - 4} more" : "") + "  ·  show everything to see each",
            };
            items.Add(_collapsedSummary);
            _collapsed = quietRetirements;
        }

        // Injuries.
        foreach (var f in rosterNow.Where(f => f.Status == FighterStatus.Injured && !before.Injured.Contains(f.Id.Value)).OrderBy(f => f.Id.Value))
        {
            int id = f.Id.Value;
            items.Add(new BriefingItem
            {
                Icon = "Warning", Picture = Portraits.Thumb(f, w), Weight = 60, Kicker = "HURT",
                Text = $"{f.FirstName} {f.LastName} is hurt" + (f.CurrentInjury is { } inj ? $": {inj.Description}" : ""),
                Detail = f.ReadyToFightOn is { } back ? $"Out until {back:d MMM}" : "Out for now", Tone = CardTone.Bad,
                ActionText = "Profile ▸", Command = Do(() => OpenFighter?.Invoke(id)),
            });
        }

        // The news that touches you: your promotion, your people (now or before), your shortlist.
        var touches = new HashSet<int>(before.Roster);
        touches.UnionWith(nowIds);
        touches.UnionWith(w.Shortlist.Select(s => s.Value));
        foreach (var n in before.NewNews(w).Reverse())
        {
            bool ours = n.Promotion == player.Id;
            bool aboutUs = n.AboutFighter is { } af && touches.Contains(af.Value);
            if (!ours && !aboutUs) continue;
            // Signings and departures are already in the digest from the books.
            if (n.Category is NewsCategory.Signing or NewsCategory.Retirement && aboutUs) continue;
            var who = n.AboutFighter is { } fid ? w.Get(fid) : null;
            int weight = n.Category switch
            {
                NewsCategory.TitleChange => 85, NewsCategory.Withdrawal => 50, NewsCategory.Callout or NewsCategory.Rivalry => 45,
                NewsCategory.Award or NewsCategory.Milestone => 40, _ => 30,
            };
            int? openId = who?.Id.Value;
            items.Add(new BriefingItem
            {
                Icon = n.Category switch { NewsCategory.TitleChange or NewsCategory.TitleDefense => "Crown", NewsCategory.Callout or NewsCategory.Rivalry => "Broadcast", _ => "Newspaper" },
                Picture = who is null ? null : Portraits.Thumb(who, w), Weight = weight,
                Kicker = (ours ? "YOUR PROMOTION" : w.Shortlist.Contains(n.AboutFighter ?? default) ? "ON YOUR SHORTLIST" : "ONE OF YOURS") + $"  ·  {n.Date:d MMM}",
                Text = n.Headline, Detail = QuoteIn(n.Body),
                ActionText = openId is null ? "" : "Profile ▸",
                Command = openId is { } oid ? Do(() => OpenFighter?.Invoke(oid)) : null,
            });
        }

        // Letters from your own fighters (callouts, asks) — the rest is in the inbox count.
        foreach (var m in before.NewMail(w).Where(m => m.Category == MailCategory.FighterRequest).Reverse().Take(3))
        {
            var who = m.AboutFighter is { } fid ? w.Get(fid) : null;
            items.Add(new BriefingItem
            {
                Icon = "Envelope", Picture = who is null ? null : Portraits.Thumb(who, w), Weight = 35,
                Kicker = $"A LETTER  ·  {m.From}".ToUpperInvariant(), Text = m.Subject, Tone = CardTone.Info,
                ActionText = "Inbox ▸", Command = Do(() => Navigate?.Invoke(Tab.Inbox)),
            });
        }

        // A thin stretch on your side still gets the sport's biggest stories (faces first).
        if (items.Count < 3)
            foreach (var n in before.NewNews(w).Reverse()
                         .Where(n => n.Promotion != player.Id && n.AboutFighter is not null
                                     && n.Category is NewsCategory.TitleChange or NewsCategory.TitleDefense or NewsCategory.Signing or NewsCategory.Retirement)
                         .OrderBy(n => n.Category switch { NewsCategory.TitleChange => 0, NewsCategory.Signing => 1, NewsCategory.TitleDefense => 2, _ => 3 })
                         .Take(3 - items.Count).ToList())
            {
                var who = w.Get(n.AboutFighter!.Value);
                var org = w.Get(n.Promotion);
                int? openId = who?.Id.Value;
                items.Add(new BriefingItem
                {
                    Icon = "Newspaper", Picture = who is null ? null : Portraits.Thumb(who, w), Weight = 5,
                    Kicker = $"AROUND THE SPORT  ·  {org?.Abbreviation ?? ""}  ·  {n.Date:d MMM}",
                    Text = n.Headline, Detail = QuoteIn(n.Body),
                    ActionText = openId is null ? "" : "Profile ▸",
                    Command = openId is { } oid ? Do(() => OpenFighter?.Invoke(oid)) : null,
                });
            }

        var ordered = items.OrderByDescending(i => i.Weight).ToList();
        if (MoneyLine(player, before) is { } money) ordered.Add(money);   // the money always closes the digest
        return ordered;
    }

    private List<BriefingItem> CardDigest(World w, BriefingBaseline before, Event ev)
    {
        var items = new List<BriefingItem>();
        var player = w.Get(w.PlayerPromotion);
        // Every bout, main event first.
        for (int i = ev.Card.Count - 1; i >= 0; i--)
        {
            var b = ev.Card[i];
            if (b.Outcome is not { } o) continue;
            var red = w.Get(b.RedCorner);
            var blue = w.Get(b.BlueCorner);
            var win = o.Winner is { } wid ? w.Get(wid) : null;
            var lose = o.Loser is { } lid ? w.Get(lid) : null;
            string how = $"{TitleMoment.MethodText(o.Method)}{(IsDecision(o.Method) ? "" : $", round {o.EndRound}")}";
            string text = b.OverturnedNc ? $"{red?.LastName} vs {blue?.LastName}: no contest"
                : win is not null && lose is not null ? $"{win.FirstName} {win.LastName} beat {lose.FirstName} {lose.LastName}"
                : $"{red?.LastName} and {blue?.LastName} fought to a draw";
            string slot = i == ev.Card.Count - 1 ? "MAIN EVENT" : i == ev.Card.Count - 2 ? "CO-MAIN" : "";
            if (b.IsTitleFight) slot = (slot.Length > 0 ? slot + "  ·  " : "") + "TITLE FIGHT";
            var face = win ?? red;
            int? fid = face?.Id.Value;
            items.Add(new BriefingItem
            {
                Icon = "Trophy", Picture = face is null ? null : Portraits.Thumb(face, w),
                Kicker = slot, Text = text, Detail = b.OverturnedNc ? "" : how,
                Tone = b.IsTitleFight ? CardTone.Good : CardTone.Neutral,
                ActionText = fid is null ? "" : "Profile ▸", Command = fid is { } x ? Do(() => OpenFighter?.Invoke(x)) : null,
            });
        }
        // Who left the night hurt.
        if (player is not null)
            foreach (var f in w.RosterOf(player.Id).Where(f => f.Status == FighterStatus.Injured && !before.Injured.Contains(f.Id.Value)).OrderBy(f => f.Id.Value))
            {
                int id = f.Id.Value;
                items.Add(new BriefingItem
                {
                    Icon = "Warning", Picture = Portraits.Thumb(f, w), Kicker = "HURT",
                    Text = $"{f.FirstName} {f.LastName} left the night hurt" + (f.CurrentInjury is { } inj ? $": {inj.Description}" : ""),
                    Detail = f.ReadyToFightOn is { } back ? $"Out until {back:d MMM}" : "", Tone = CardTone.Bad,
                    ActionText = "Profile ▸", Command = Do(() => OpenFighter?.Invoke(id)),
                });
            }
        if (player is not null && MoneyLine(player, before) is { } money) items.Add(money);
        return items;
    }

    // Opened by hand before any stop: the last fortnight of stories about your promotion and people.
    private List<BriefingItem> DeskDigest(World w)
    {
        var items = new List<BriefingItem>();
        if (w.Get(w.PlayerPromotion) is not { } player) return items;
        var roster = w.RosterOf(player.Id).Select(f => f.Id.Value).ToHashSet();
        var since = w.CurrentDate.AddDays(-14);
        foreach (var n in Enumerable.Reverse(w.News).TakeWhile(n => n.Date >= since)
                     .Where(n => n.Promotion == player.Id || n.AboutFighter is { } af && roster.Contains(af.Value)).Take(8))
        {
            var who = n.AboutFighter is { } fid ? w.Get(fid) : null;
            int? openId = who?.Id.Value;
            items.Add(new BriefingItem
            {
                Icon = "Newspaper", Picture = who is null ? null : Portraits.Thumb(who, w),
                Kicker = $"{FighterRow.Pretty(n.Category.ToString()).ToUpperInvariant()}  ·  {n.Date:d MMM}",
                Text = n.Headline, Detail = QuoteIn(n.Body),
                ActionText = openId is null ? "" : "Profile ▸",
                Command = openId is { } oid ? Do(() => OpenFighter?.Invoke(oid)) : null,
            });
        }
        return items;
    }

    // Money in and out since the baseline, with the biggest of each.
    private BriefingItem? MoneyLine(Promotion player, BriefingBaseline before)
    {
        var entries = player.Ledger.Skip(before.LedgerCount).ToList();
        if (entries.Count == 0) return null;
        decimal inc = entries.Where(e => e.Amount.Amount > 0).Sum(e => e.Amount.Amount);
        decimal exp = -entries.Where(e => e.Amount.Amount < 0).Sum(e => e.Amount.Amount);
        decimal net = player.Balance.Amount - before.Balance;
        var bigIn = entries.Where(e => e.Amount.Amount > 0).OrderByDescending(e => e.Amount.Amount).FirstOrDefault();
        var bigOut = entries.Where(e => e.Amount.Amount < 0).OrderBy(e => e.Amount.Amount).FirstOrDefault();
        var detail = new List<string>();
        if (bigIn is not null) detail.Add($"Biggest in: {Describe(bigIn)} {bigIn.Amount}");
        if (bigOut is not null) detail.Add($"biggest out: {Describe(bigOut)} {new Money(-bigOut.Amount.Amount)}");
        return new BriefingItem
        {
            Icon = "Money", Kicker = "THE BOOKS",
            Text = $"{new Money(inc)} in, {new Money(exp)} out  ·  {(net >= 0 ? "up" : "down")} {new Money(Math.Abs(net))}  ·  {player.Balance} in the bank",
            Detail = string.Join("; ", detail), Tone = net >= 0 ? CardTone.Good : player.Balance.Amount < 0 ? CardTone.Bad : CardTone.Neutral,
            ActionText = "Finance ▸", Command = Do(() => Navigate?.Invoke(Tab.Finance)),
        };
    }

    private static string Describe(LedgerEntry e) =>
        e.Description is { Length: > 0 and <= 48 } d ? d.TrimEnd('.') : FighterRow.Pretty(e.Category.ToString()).ToLowerInvariant();

    // =================================================================================
    // Helpers
    // =================================================================================
    private static ImageSource? Poster(World w, Event ev)
    {
        try { return PosterFactory.For(w, ev); }
        catch { return w.Get(ev.Promotion) is { } p ? LogoFactory.ForPromotion(p) : null; }
    }

    private void AddMainEventFaces(World w, Event ev)
    {
        if (ev.Card.LastOrDefault() is not { } main) return;
        if (w.Get(main.RedCorner) is { } r) People.Add(Person(w, r, main.IsTitleFight ? "Main event · title" : "Main event"));
        if (w.Get(main.BlueCorner) is { } b) People.Add(Person(w, b, b.Record.ToString()));
    }

    private static BriefingPerson Person(World w, Fighter f, string detail) =>
        new(f.Id.Value, $"{f.FirstName} {f.LastName}", detail, f.LocalTalent ? null : Portraits.Thumb(f, w));

    private static string Where(World w, Event ev) =>
        ev.Venue is { Length: > 0 } v
            ? $" at {v}" + (ev.VenueId is { } vid && w.Get(vid) is { } venue && w.Get(venue.City) is { } city ? $", {city.Name}" : "")
            : "";

    private static string DaysOut(int days, bool sentence = false) =>
        days <= 0 ? (sentence ? "It's tonight" : "TONIGHT")
        : days == 1 ? (sentence ? "It's tomorrow" : "TOMORROW")
        : sentence ? $"It's {days} days out" : $"{days} DAYS OUT";

    private static string Plural(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";

    private static string Pronoun(Fighter f) => f.Sex == Sex.Female ? "her" : "him";
    private static string Possessive(Fighter f) => f.Sex == Sex.Female ? "her" : "his";

    private static bool IsDecision(FightMethod m) =>
        m is FightMethod.UnanimousDecision or FightMethod.SplitDecision or FightMethod.MajorityDecision;

    private static string QuoteIn(string body)
    {
        int a = body.IndexOf('“');
        if (a < 0) return "";
        int b = body.IndexOf('”', a + 1);
        return b > a ? body.Substring(a, b - a + 1) : "";
    }

    private static ICommand Do(Action a) => new RelayCommand(a);

    // Run an existing command (with its parameter), then let the app refresh redraw the briefing.
    private static ICommand Then(ICommand inner, object? parameter = null) => new RelayCommand(() =>
    {
        if (inner.CanExecute(parameter)) inner.Execute(parameter);
    });

    // A costly answer: say what it costs first (interactive only), then run it.
    private ICommand Ask(ICommand inner, object parameter, string heading, string body, string detail, string confirm) => new RelayCommand(() =>
    {
        if (Confirm is { } ask && !ask(heading, body, detail, confirm)) return;
        if (inner.CanExecute(parameter)) inner.Execute(parameter);
    });
}
