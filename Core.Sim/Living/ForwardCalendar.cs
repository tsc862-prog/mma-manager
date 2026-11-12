using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Org;

namespace MmaManager.Core.Sim.Living;

/// <summary>One card on the sport's calendar, as the UI and the digest read it.</summary>
public sealed record UpcomingCard(
    Event Event,
    Promotion Promotion,
    DateOnly Date,
    DateOnly AnnouncedOn,
    Region Region,
    string? Venue,
    Fight? MainEvent,
    Fight? CoMain,
    bool TitleOnTheLine,
    bool IsSignature,
    bool IsPlayer)
{
    /// <summary>"Sato vs Kang" (empty when the card has no bouts yet).</summary>
    public string MainEventLabel(World world) =>
        MainEvent is { } m && world.Get(m.RedCorner) is { } r && world.Get(m.BlueCorner) is { } b
            ? $"{r.LastName} vs {b.LastName}" : "";
}

/// <summary>A rival card near a date, and whether the box office counts it against the house there.</summary>
public sealed record RivalCard(UpcomingCard Card, bool Charged);

/// <summary>
/// THE WORLD'S CLOCK. AI promotions used to build a card and fight it in the
/// same tick, so nothing was ever on the calendar ahead of the player. Now every
/// AI house announces its shows ahead of time with a chosen main event (and a
/// co-main), fills in the undercard closer to the date, and runs the card when
/// the date arrives — and everything that can happen to a booked fight in
/// between (camp injuries, a fighter leaving, retiring or being suspended, the
/// promotion going under) happens to it, visibly.
///
/// The CADENCE is unchanged: the same staggered weekly slots the old same-day
/// booker used (biweekly for the big leagues, monthly for everyone else), the
/// same card sizes and the same signature months — the change is WHEN a card is
/// created, not how many. Everything here is deterministic and draws no RNG.
///
/// The rule for a booked fighter who leaves the promotion (cut, sold, retired,
/// suspended, signed elsewhere): he comes off the card at once. More than a
/// week out the bout is saved on short notice if anybody plausible will take
/// it (the camp-withdrawal machinery); inside fight week the bout is scrapped.
/// There is no "honours the bout" exception: a contracted fighter's deal cannot
/// run out under an announced bout (the matchmaker never books a spent deal
/// ahead, and a fighter is committed to one scheduled card at a time, so
/// nothing else can spend his last fight first), and every bout on an AI card
/// at run time is between two fighters signed to the promotion running it (or
/// the unsigned local-scene filler a small card is padded with).
/// </summary>
public static class ForwardCalendar
{
    // --- The clock: every lead time lives here --------------------------------

    /// <summary>
    /// How many weeks ahead an ordinary card is announced, by the promotion's
    /// reach. Never shorter than the org's cadence, so every house ALWAYS has
    /// its next night on the books: the monthly houses (City to National) go up
    /// a month out, the biweekly Continental houses five weeks, the giants six.
    /// </summary>
    public static int LeadWeeks(OrgSize size) => size switch
    {
        OrgSize.International => 6,
        OrgSize.Continental => 5,
        _ => 4,
    };

    /// <summary>A flagship night (<see cref="SignatureEvents"/>) is announced two months out.</summary>
    public const int SignatureLeadWeeks = 8;

    /// <summary>The undercard is filled in this close to the date (and topped up each week after).</summary>
    public const int UndercardFillDays = 14;

    /// <summary>Inside fight week a lost bout is scrapped; before it the matchmaker looks for a replacement.</summary>
    public const int FightWeekDays = 7;

    /// <summary>Bad blood this hot headlines a card over the matchmaker's belt (the belt becomes the co-main).</summary>
    public const int HeadlineFeud = 50;

    /// <summary>A feud rematch doesn't headline again this soon after the last one.</summary>
    private const int FeudRematchDays = 180;

    /// <summary>The weekly slot pattern: biweekly for the big leagues, monthly for the rest.</summary>
    public static int CadenceWeeks(Promotion promo) => OrgSizing.SizeOf(promo) >= OrgSize.Continental ? 2 : 4;

    /// <summary>How many bouts the matchmaker books (a real PPV bill is 12-14; a club card 10).</summary>
    public static int CardSize(Promotion promo) => OrgSizing.SizeOf(promo) >= OrgSize.Continental ? 13 : 10;

    /// <summary>
    /// Is <paramref name="date"/> (a tick date) one of this promotion's show
    /// weeks? Keyed off the org's id rather than its place in a list, so a rival
    /// folding never shifts anyone else's already-announced calendar.
    /// </summary>
    public static bool IsShowWeek(Promotion promo, DateOnly date)
    {
        int period = CadenceWeeks(promo);
        return (date.DayNumber / 7) % period == promo.Id.Value % period;
    }

    /// <summary>When a scheduled card was (or would have been) announced.</summary>
    public static DateOnly AnnouncedOn(World world, Event ev)
    {
        var promo = world.Get(ev.Promotion);
        int days = promo is null ? 0
            : 7 * (SignatureEvents.IsSignature(world, ev) ? SignatureLeadWeeks : LeadWeeks(OrgSizing.SizeOf(promo)));
        // A night an authored booking names goes up when the table says, if that's earlier.
        if (promo is not null)
            foreach (var (night, rows) in BookedNights(world, promo))
                if (night == ev.Date) days = Math.Max(days, rows.Max(r => r.AnnounceDaysBefore));
        return ev.Date.AddDays(-days);
    }

    // --- The tick -----------------------------------------------------------

    /// <summary>
    /// The weekly pass, run at the top of the tick: take departed fighters off
    /// the cards, announce every show inside its lead window that isn't on the
    /// books yet, and fill in the undercards of the shows that are close.
    /// </summary>
    public static void RunWeekly(World world, DateOnly date, Matchmaker matchmaker, EventService events)
    {
        Audit(world, date);
        Announce(world, date, matchmaker, events, postNews: true);
        FillUndercards(world, date, matchmaker);
    }

    /// <summary>
    /// Day one: hand over the calendar as if the sport had been running all
    /// along — every active promotion's shows inside its lead window are on the
    /// books (signature nights two months out), the close ones with their
    /// undercards. Posts nothing: the prologue reports the announcements, dated
    /// on the day each one would have been made. Draws no RNG.
    /// </summary>
    public static void SeedOpening(World world)
    {
        var matchmaker = new Matchmaker();
        var events = new EventService();
        Announce(world, world.CurrentDate, matchmaker, events, postNews: false, startAfterToday: true);
        FillUndercards(world, world.CurrentDate, matchmaker);
    }

    private static void Announce(World world, DateOnly today, Matchmaker matchmaker, EventService events,
        bool postNews, bool startAfterToday = false)
    {
        var committed = Unbookable(world);
        var lastMet = LastMeetings(world);
        int first = startAfterToday ? 1 : 0;

        // Which show weeks already have a card (any status: a cancelled night is
        // not re-booked), so each week is announced once.
        var taken = new HashSet<(int, int)>();
        foreach (var e in world.Events.Values)
            if (e.Date >= today && !Storylines.IsSuperfight(e)) taken.Add((e.Promotion.Value, e.Date.DayNumber));

        foreach (var promo in world.Promotions.Values
                     .Where(p => p.Id != world.PlayerPromotion && !p.Defunct)
                     .OrderBy(p => p.Id.Value)
                     .ToList())
        {
            // The nights an authored booking names go up when the table says,
            // and its fighters are held back from the org's other nights until then.
            var reserved = new HashSet<FighterId>();
            foreach (var (night, rows) in BookedNights(world, promo))
            {
                if (night < today.AddDays(7 * first) || taken.Contains((promo.Id.Value, night.DayNumber))) continue;
                if (night.DayNumber - today.DayNumber <= rows.Max(r => r.AnnounceDaysBefore))
                {
                    if (AnnounceCard(world, promo, night, today, matchmaker, events, committed, lastMet, postNews))
                        taken.Add((promo.Id.Value, night.DayNumber));
                }
                else if (night.DayNumber - today.DayNumber <= ReserveWeeks * 7)
                    foreach (var row in rows)
                        foreach (var name in new[] { row.Red, row.Blue })
                            if (name is not null && FindDesigned(world, name) is { } held) reserved.Add(held.Id);
            }
            var around = reserved.Count == 0 ? committed : new HashSet<FighterId>(committed.Concat(reserved));

            // Then the flagship (furthest out) and the ordinary nights in the lead window.
            int lead = LeadWeeks(OrgSizing.SizeOf(promo));
            int horizon = SignatureEvents.NameOf(promo) is null ? lead : Math.Max(lead, SignatureLeadWeeks);
            for (int k = first; k <= horizon; k++)
            {
                var when = today.AddDays(7 * k);
                if (!IsShowWeek(promo, when) || taken.Contains((promo.Id.Value, when.DayNumber))) continue;
                bool flagship = SignatureEvents.For(promo, when) is not null && !HasSignature(world, promo, when.Year);
                if (k > lead && !flagship) continue;
                var pool = flagship ? committed : around;
                if (AnnounceCard(world, promo, when, today, matchmaker, events, pool, lastMet, postNews))
                {
                    taken.Add((promo.Id.Value, when.DayNumber));
                    // Keep both views in step: whoever was just booked is committed everywhere.
                    if (!ReferenceEquals(around, committed))
                    {
                        if (ReferenceEquals(pool, around)) committed.UnionWith(around.Except(reserved));
                        else around.UnionWith(committed);
                    }
                }
            }
        }
    }

    /// <summary>How far ahead of an authored booking's night its fighters start being held back.</summary>
    public const int ReserveWeeks = 16;

    /// <summary>
    /// The fighters this org's authored bookings are holding back from its other
    /// nights: those named for a night inside <see cref="ReserveWeeks"/> that has
    /// no card yet.
    /// </summary>
    private static HashSet<FighterId> Reserved(World world, Promotion promo, DateOnly today)
    {
        var held = new HashSet<FighterId>();
        foreach (var (night, rows) in BookedNights(world, promo))
        {
            if (night < today || night.DayNumber - today.DayNumber > ReserveWeeks * 7) continue;
            if (world.Events.Values.Any(e => e.Promotion == promo.Id && e.Date == night && !Storylines.IsSuperfight(e))) continue;
            foreach (var row in rows)
                foreach (var name in new[] { row.Red, row.Blue })
                    if (name is not null && FindDesigned(world, name) is { } f) held.Add(f.Id);
        }
        return held;
    }

    /// <summary>
    /// The night each of this org's authored bookings (<see cref="DesignedBookings"/>)
    /// resolves to, with its rows in card order. A flagship row lands on the
    /// flagship's night in its month; a numbered-card row on the org's show week
    /// nearest its day. Rows that resolve to no night are skipped.
    /// </summary>
    public static List<(DateOnly Night, List<DesignedBooking> Rows)> BookedNights(World world, Promotion promo)
    {
        var nights = new List<(DateOnly Night, List<DesignedBooking> Rows)>();
        foreach (var row in DesignedBookings.All.Where(r => r.Org == promo.Abbreviation))
        {
            if (NightOf(world, promo, row) is not { } night) continue;
            int at = nights.FindIndex(n => n.Night == night);
            if (at >= 0) nights[at].Rows.Add(row);
            else nights.Add((night, new List<DesignedBooking> { row }));
        }
        foreach (var n in nights) n.Rows.Sort((x, y) => x.Order.CompareTo(y.Order));
        return nights.OrderBy(n => n.Night).ToList();
    }

    private static DateOnly? NightOf(World world, Promotion promo, DesignedBooking row)
    {
        var target = world.StartDate.AddDays(row.Day);
        if (row.EventName is { } show)
        {
            if (SignatureEvents.NameOf(promo) != show || SignatureEvents.MonthOf(promo) is not { } month) return null;
            for (var d = FirstTickOnOrAfter(world, new DateOnly(target.Year, month, 1)); d.Month == month; d = d.AddDays(7))
                if (IsShowWeek(promo, d)) return d;
            return null;
        }
        DateOnly? best = null;
        for (var d = FirstTickOnOrAfter(world, target.AddDays(-21)); d <= target.AddDays(21); d = d.AddDays(7))
            if (IsShowWeek(promo, d)
                && (best is null || Math.Abs(d.DayNumber - target.DayNumber) < Math.Abs(best.Value.DayNumber - target.DayNumber)))
                best = d;
        return best;
    }

    // The first tick date (start + 7k, k >= 1) on or after the day.
    private static DateOnly FirstTickOnOrAfter(World world, DateOnly day)
    {
        int diff = day.DayNumber - world.StartDate.DayNumber;
        int k = Math.Max(1, (diff + 6) / 7);
        return world.StartDate.AddDays(7 * k);
    }

    private static Fighter? FindDesigned(World world, string fullName) => world.Fighters.Values
        .Where(f => f.Designed && f.Status != FighterStatus.Retired && $"{f.FirstName} {f.LastName}" == fullName)
        .OrderBy(f => f.Id.Value).FirstOrDefault();

    /// <summary>
    /// Who the matchmaker can't book ahead: anyone already committed to a
    /// scheduled card, and anyone whose deal is SPENT (no fights left — the
    /// monthly expiry pass will renew him or let him walk, and an announced bout
    /// must not hang on a contract that's about to end).
    /// </summary>
    private static HashSet<FighterId> Unbookable(World world)
    {
        var set = Committed(world);
        foreach (var f in world.Fighters.Values)
            if (f.Contract is { FightsRemaining: <= 0 }) set.Add(f.Id);
        return set;
    }

    private static bool HasSignature(World world, Promotion promo, int year) =>
        SignatureEvents.NameOf(promo) is { } name
        && world.Events.Values.Any(e => e.Promotion == promo.Id && e.Date.Year == year && e.Name.StartsWith(name, StringComparison.Ordinal));

    /// <summary>
    /// Put one show on the books: the authored bookings for this night if they
    /// can be honoured (an "off" headline for any that can't), else the hottest
    /// feud on the roster, else the matchmaker's most pressing belt, and a
    /// co-main around it.
    /// </summary>
    private static bool AnnounceCard(World world, Promotion promo, DateOnly when, DateOnly today,
        Matchmaker matchmaker, EventService events, HashSet<FighterId> committed,
        Dictionary<(int, int), DateOnly> lastMet, bool postNews)
    {
        string? signature = SignatureEvents.For(promo, when) is { } sig && !HasSignature(world, promo, when.Year) ? sig : null;

        Matchup? main = null, co = null;
        var off = new List<(DesignedBooking Row, string Why)>();
        var taken = new HashSet<FighterId>(committed);
        var rows = BookedNights(world, promo).Where(n => n.Night == when).SelectMany(n => n.Rows).ToList();
        foreach (var row in rows)
        {
            if (row.Order == 0 ? main is not null : co is not null) continue;
            var (m, why) = ResolveRow(world, promo, row, when, taken);
            if (m is not { } bout) { off.Add((row, why!)); continue; }
            if (row.Order == 0) main = bout; else co = bout;
            taken.Add(bout.Red); taken.Add(bout.Blue);
        }
        if (main is null && FeudHeadliner(world, promo, when, taken, today, lastMet) is { } feud) main = feud;
        if (main is null && co is not null) main = matchmaker.BestBout(world, promo, when, taken, titleLength: true);
        if (main is { } chosenMain) { taken.Add(chosenMain.Red); taken.Add(chosenMain.Blue); }

        IReadOnlyList<Matchup> top;
        if (main is { } chosen)
        {
            co ??= matchmaker.BestBout(world, promo, when, taken, titleLength: false);
            top = co is { } c ? new[] { c, chosen } : new[] { chosen };
        }
        else if (co is { } onlyCo) top = new[] { onlyCo with { Rounds = promo.Rules.TitleRounds } };
        else top = matchmaker.Headliners(world, promo.Id, when, committed);
        if (top.Count == 0) return false;   // nobody to book this week, try again next tick

        int number = world.Events.Values.Count(e => e.Promotion == promo.Id) + 1;
        Event ev;
        if (signature is not null)
        {
            // A tradition has an ADDRESS: the flagship goes home, to the biggest
            // room in the org's own town (read off the promotion, never assumed).
            var home = promo.HomeCity ?? Geography.WorldMap.PickTourStop(world, promo, when);
            ev = events.CreateEvent(world, promo.Id, $"{signature} {when.Year}", when, top, home, biggestRoom: true);
            ev.Tier = OrgPerks.MaxEventTier(promo);
        }
        else
        {
            ev = events.CreateEvent(world, promo.Id, $"{promo.Abbreviation} Fight Night {number}", when, top,
                Geography.WorldMap.PickTourStop(world, promo, when));
        }

        foreach (var m in top) { committed.Add(m.Red); committed.Add(m.Blue); }
        if (!postNews) return true;

        // The authored fight that can't happen is news in its own right.
        foreach (var (row, why) in off)
            News.Post(world, today, NewsCategory.Withdrawal, OffHeadline(promo, ev, row), $"{why}. {promo.Abbreviation} goes ahead on {ev.Date:d MMMM} with another bout in its place.",
                promo.Id, FindDesigned(world, row.Red)?.Id, ev.Id);

        var mainRow = AuthoredBookingOn(world, ev);
        if (MeritsHeadline(world, ev, mainRow is not null))
        {
            var (head, body) = Announcement(world, ev, mainRow);
            News.Post(world, today, signature is not null || mainRow is not null ? NewsCategory.Milestone : NewsCategory.General,
                head, body, promo.Id, ev.Card[^1].RedCorner, ev.Id);
        }
        return true;
    }

    /// <summary>"Off: Sato vs Kang will not happen at the Emperor's New Year".</summary>
    public static string OffHeadline(Promotion promo, Event ev, DesignedBooking row) =>
        $"Off: {Surname(row.Red)} vs {(row.Blue is { } b ? Surname(b) : "the title shot")} will not happen at {ShowLabel(promo, ev)}";

    private static string Surname(string fullName) =>
        fullName.IndexOf(' ') is var sp and >= 0 ? fullName[(sp + 1)..] : fullName;

    /// <summary>
    /// An authored row as a bout on this night, or why it can't be: both
    /// fighters must exist, be active, be signed here on a live deal, be in the
    /// row's division, be fit on the night and not be committed elsewhere.
    /// Authored rows override the "friends won't fight" rule (the story IS the
    /// fight). A null Blue is the matchmaker's pick for the shot. The belt is on
    /// the line only while one of the two holds it, or it's vacant.
    /// </summary>
    private static (Matchup? Bout, string? Why) ResolveRow(World world, Promotion promo, DesignedBooking row, DateOnly when,
        IReadOnlySet<FighterId> committed)
    {
        var division = promo.Divisions.FirstOrDefault(d => d.WeightClass == row.Wc && d.Sex == row.Sex);
        if (division is null) return (null, $"{promo.Abbreviation} no longer runs that division");

        string? Unavailable(Fighter? f, string name)
        {
            if (f is null) return $"{name} is no longer an active fighter";
            if (f.Contract is not { } c || c.Promotion != promo.Id)
                return f.Contract is { } other && world.Get(other.Promotion) is { } o
                    ? $"{name} has left for {o.Abbreviation}" : $"{name} is no longer with {promo.Abbreviation}";
            if (c.FightsRemaining <= 0) return $"{name} is out of contract";
            if (!division.Matches(f)) return $"{name} no longer fights in that division";
            if (!f.IsMatchReady(when)) return f.CurrentInjury is not null ? $"{name} is injured" : $"{name} won't be cleared to fight in time";
            if (committed.Contains(f.Id)) return $"{name} is booked elsewhere";
            return null;
        }

        var red = FindDesigned(world, row.Red);
        if (Unavailable(red, row.Red) is { } whyRed) return (null, whyRed);
        var belt = division.Title is { } tid ? world.Get(tid) : null;

        Fighter? blue;
        if (row.Blue is { } blueName)
        {
            blue = FindDesigned(world, blueName);
            if (Unavailable(blue, blueName) is { } whyBlue) return (null, whyBlue);
        }
        else if (belt?.CurrentChampion == red!.Id)
        {
            blue = division.Rankings.Select(world.Get)
                .FirstOrDefault(c => c is not null && c.Id != red.Id && Unavailable(c, c.FullName) is null && Matchmaker.Worthy(c));
            if (blue is null) return (null, $"{row.Red} has nobody fit to challenge him");
        }
        else if (belt?.CurrentChampion is { } champId)
        {
            blue = world.Get(champId);
            if (Unavailable(blue, "the champion") is { } whyChamp) return (null, whyChamp);
        }
        else
        {
            blue = division.Rankings.Select(world.Get)
                .FirstOrDefault(c => c is not null && c.Id != red.Id && Unavailable(c, c.FullName) is null);
            if (blue is null) return (null, $"{row.Red} has nobody to fight for the vacant belt");
        }

        bool title = row.Title && belt is not null
            && (belt.CurrentChampion is null || belt.CurrentChampion == red!.Id || belt.CurrentChampion == blue!.Id);
        // The champion takes the red corner.
        var (a, b) = title && belt!.CurrentChampion == blue!.Id ? (blue!, red!) : (red!, blue!);
        int rounds = title || row.Order == 0 ? promo.Rules.TitleRounds : promo.Rules.StandardRounds;
        return (new Matchup(a.Id, b.Id, division.WeightClass, rounds, title, title ? belt!.Id : null), null);
    }

    // The hottest feud on this roster that can headline on the date.
    private static Matchup? FeudHeadliner(World world, Promotion promo, DateOnly when, IReadOnlySet<FighterId> committed,
        DateOnly today, Dictionary<(int, int), DateOnly> lastMet)
    {
        foreach (var rv in world.Rivalries
                     .Where(r => r.Intensity >= HeadlineFeud)
                     .OrderByDescending(r => r.Intensity)
                     .ThenBy(r => Math.Min(r.A.Value, r.B.Value)).ThenBy(r => Math.Max(r.A.Value, r.B.Value)))
        {
            if (world.Get(rv.A) is not { } a || world.Get(rv.B) is not { } b) continue;
            if (a.Contract?.Promotion != promo.Id || b.Contract?.Promotion != promo.Id) continue;
            if (lastMet.TryGetValue(Key(a.Id, b.Id), out var met) && today.DayNumber - met.DayNumber < FeudRematchDays) continue;
            if (Bookable(world, promo, a, b, when, committed, requireWorthy: true) is { } m) return m;
        }
        return null;
    }

    /// <summary>
    /// Two named fighters as a headliner on this promotion's card, or null if
    /// they can't be: both signed here and active, sharing one of its divisions,
    /// fit on the night and uncommitted, not refusing each other. If one holds
    /// that division's belt it's a title fight (against a WORTHY challenger when
    /// <paramref name="requireWorthy"/>); a champion of some other belt only
    /// fights for his own.
    /// </summary>
    private static Matchup? Bookable(World world, Promotion promo, Fighter a, Fighter b, DateOnly when,
        IReadOnlySet<FighterId> committed, bool requireWorthy)
    {
        if (a.Id == b.Id) return null;
        foreach (var f in new[] { a, b })
        {
            if (f.Contract is not { } c || c.Promotion != promo.Id || c.FightsRemaining <= 0) return null;
            if (!f.IsMatchReady(when) || committed.Contains(f.Id)) return null;
        }
        var division = promo.Divisions.FirstOrDefault(d => d.Matches(a) && d.Matches(b));
        if (division is null) return null;
        if (Bonds.WontFight(world, a.Id, b.Id)) return null;

        var belt = division.Title is { } tid ? world.Get(tid) : null;
        bool OtherBelt(Fighter f) => world.Titles.Values.Any(t => t.CurrentChampion == f.Id && t.Id != belt?.Id);
        if (OtherBelt(a) || OtherBelt(b)) return null;

        bool aChamp = belt?.CurrentChampion == a.Id, bChamp = belt?.CurrentChampion == b.Id;
        if (aChamp || bChamp)
        {
            var challenger = aChamp ? b : a;
            if (requireWorthy && !Matchmaker.Worthy(challenger)) return null;
            return aChamp
                ? new Matchup(a.Id, b.Id, division.WeightClass, promo.Rules.TitleRounds, true, belt!.Id)
                : new Matchup(b.Id, a.Id, division.WeightClass, promo.Rules.TitleRounds, true, belt!.Id);
        }
        if (a.Camp is { } room && room == b.Camp) return null;   // teammates only meet for a belt
        return new Matchup(a.Id, b.Id, division.WeightClass, promo.Rules.TitleRounds, false, null);
    }

    private static (int, int) Key(FighterId a, FighterId b) =>
        a.Value < b.Value ? (a.Value, b.Value) : (b.Value, a.Value);

    // When each pair last met in a completed bout — for the feud-rematch cooldown.
    private static Dictionary<(int, int), DateOnly> LastMeetings(World world)
    {
        var met = new Dictionary<(int, int), DateOnly>();
        var cutoff = world.CurrentDate.AddDays(-FeudRematchDays);
        foreach (var e in world.Events.Values)
        {
            if (e.Status != EventStatus.Completed || e.Date < cutoff) continue;
            foreach (var f in e.Card)
            {
                var k = Key(f.RedCorner, f.BlueCorner);
                if (!met.TryGetValue(k, out var d) || e.Date > d) met[k] = e.Date;
            }
        }
        return met;
    }

    /// <summary>
    /// Close to the date, the matchmaker fills in the rest of the bill — the
    /// prelims go in ahead of the announced headliners, from whoever will be fit
    /// on the night and isn't committed anywhere else. Re-run each week inside
    /// the window, so a bout lost to a withdrawal gets its slot refilled.
    /// </summary>
    private static void FillUndercards(World world, DateOnly today, Matchmaker matchmaker)
    {
        var committed = Unbookable(world);
        foreach (var ev in world.Events.Values
                     .Where(e => e.Status == EventStatus.Scheduled && e.Promotion != world.PlayerPromotion
                                 && e.Date >= today && !Storylines.IsSuperfight(e)
                                 // A card that lost every bout is rebuilt whenever it is,
                                 // the rest are filled in close to the date.
                                 && (e.Card.Count == 0 || e.Date.DayNumber - today.DayNumber <= UndercardFillDays))
                     .OrderBy(e => e.Date).ThenBy(e => e.Id.Value)
                     .ToList())
        {
            if (world.Get(ev.Promotion) is not { Defunct: false } promo) continue;
            int need = CardSize(promo) - ev.Card.Count;
            if (need <= 0) continue;
            var reserved = SignatureEvents.IsSignature(world, ev) ? new HashSet<FighterId>() : Reserved(world, promo, today);
            var around = reserved.Count == 0 ? committed : new HashSet<FighterId>(committed.Concat(reserved));
            // An emptied card is rebuilt top to bottom (a headliner and its
            // undercard, from whoever is free); otherwise just the undercard.
            var under = ev.Card.Count == 0
                ? (ev.Date.DayNumber - today.DayNumber <= UndercardFillDays
                    ? matchmaker.BuildCard(world, promo.Id, need, ev.Date, around)
                    : matchmaker.Headliners(world, promo.Id, ev.Date, around))
                : matchmaker.BuildUndercard(world, promo.Id, need, ev.Date, around);
            if (under.Count == 0) continue;

            var fights = under.Select(m => new Fight
            {
                Id = world.NewFightId(),
                RedCorner = m.Red,
                BlueCorner = m.Blue,
                WeightClass = m.WeightClass,
                ScheduledRounds = m.Rounds,
            }).ToList();
            for (int i = 0; i < fights.Count; i++)
            {
                fights[i].IsTitleFight = under[i].IsTitle;
                fights[i].Title = under[i].Title;
            }
            ev.Card.InsertRange(0, fights);
            LocalCircuit.YieldTo(world, fights);   // a promotion's booking beats a club card
            foreach (var m in under) { committed.Add(m.Red); committed.Add(m.Blue); }
        }
    }

    // --- Between announcement and fight night --------------------------------

    /// <summary>
    /// Keep every announced AI card honest: a corner who has retired, been
    /// suspended, left the promotion, been hurt past the date, or is somehow
    /// booked on another card comes off it — saved on short notice more than a
    /// week out, scrapped inside fight week — and a belt that is no longer on the
    /// line comes off the bout. Run at the top and the bottom of every tick, so
    /// the monthly exits (expiries, cuts, retirements, sales) never leave a stale
    /// name on a card overnight. Draws no RNG. The player's own cards get the
    /// same guarantee but are never re-booked for him: a lost bout comes off the
    /// card and the player is told (<see cref="PullPlayerBout"/>). The player
    /// wins any double-booking.
    /// </summary>
    public static void Audit(World world, DateOnly today)
    {
        var scheduled = world.Events.Values
            .Where(e => e.Status == EventStatus.Scheduled && world.Get(e.Promotion) is not null)
            .OrderBy(e => e.Promotion == world.PlayerPromotion ? 0 : 1)   // the player's bookings stand first
            .ThenBy(e => e.Date).ThenBy(e => e.Id.Value)
            .ToList();

        // Who is booked where, first claim wins: a fighter's earliest booking
        // stands and any later one is the double-booking.
        var claim = new Dictionary<FighterId, FightId>();
        foreach (var ev in scheduled)
            foreach (var f in ev.Card)
                foreach (var id in new[] { f.RedCorner, f.BlueCorner })
                    claim.TryAdd(id, f.Id);

        var committed = new HashSet<FighterId>(claim.Keys);
        foreach (var ev in scheduled)
        {
            if (ev.Promotion == world.PlayerPromotion)
            {
                foreach (var fight in ev.Card.ToList())
                {
                    bool Elsewhere(FighterId id) => claim.TryGetValue(id, out var first) && first != fight.Id;
                    KeepPlayerBoutHonest(world, ev, fight, Elsewhere, today);
                }
                continue;
            }
            if (world.Get(ev.Promotion) is not { } promo) continue;
            if (promo.Defunct) { ev.Status = EventStatus.Cancelled; continue; }

            int daysOut = ev.Date.DayNumber - today.DayNumber;
            var announcedMain = ev.Card.Count > 0 ? ev.Card[^1] : null;
            bool mainGone = false;
            foreach (var fight in ev.Card.ToList())
            {
                bool Elsewhere(FighterId id) => claim.TryGetValue(id, out var first) && first != fight.Id;
                var red = WhyOff(world, ev, fight.RedCorner, Elsewhere);
                var blue = WhyOff(world, ev, fight.BlueCorner, Elsewhere);
                if (red is null && blue is null)
                {
                    CheckBelt(world, ev, fight);
                    continue;
                }

                var (outId, why) = red is not null ? (fight.RedCorner, red) : (fight.BlueCorner, blue!);
                bool bothGone = red is not null && blue is not null;
                bool saved = !bothGone && daysOut > FightWeekDays
                    && Storylines.SaveBout(world, ev, fight, outId, committed, today, why);
                if (!saved && ev.Card.Contains(fight))
                    Storylines.ScrapBout(world, ev, fight, outId, today, why);
                // A stand-in keeps the headline slot; only a bout that came off
                // the card leaves the show without its announced main event.
                if (!saved && ReferenceEquals(fight, announcedMain)) mainGone = true;
            }
            // An emptied card stays on the calendar to be rebuilt (FillUndercards);
            // only a card still empty on fight night collapses.
            if (ev.Card.Count > 0 && mainGone) RestoreHeadliner(world, ev);
        }
    }

    /// <summary>
    /// An announced AI card that reaches fight night with no bouts (everything
    /// fell through and nobody could be found to rebuild it) is cancelled with
    /// the same consequence as a card that collapses in fight week (the
    /// promotion's standing takes the hit) and a headline.
    /// </summary>
    internal static void Collapse(World world, Event ev, DateOnly date)
    {
        if (ev.Status != EventStatus.Scheduled || world.Get(ev.Promotion) is not { } promo) return;
        ev.Status = EventStatus.Cancelled;
        Org.Standing.OnCancelled(world, promo, ev);
        News.Post(world, date, NewsCategory.Withdrawal,
            $"{ev.Name} is cancelled",
            $"Every bout on the card fell through. {promo.Abbreviation} had to call off its {ev.Date:d MMMM} show.",
            promo.Id, aboutEvent: ev.Id);
    }

    /// <summary>
    /// Read-only, for display: whether (and in the audit's words, why) this fighter can't
    /// be on this card, other bookings aside. Null = he can. Changes nothing, draws no RNG.
    /// </summary>
    public static string? WhyUnavailable(World world, Event ev, FighterId id) => WhyOff(world, ev, id, _ => false);

    // Why this corner can't be on this card any more (null = he can).
    private static string? WhyOff(World world, Event ev, FighterId id, Func<FighterId, bool> bookedElsewhere)
    {
        if (world.Get(id) is not { } f) return "is no longer in the sport";
        if (f.LocalTalent) return null;   // the local scene's filler fights for nobody
        if (bookedElsewhere(id)) return "is already committed to another card";
        if (f.Status == FighterStatus.Retired) return "has retired";
        if (f.Status == FighterStatus.Suspended && (f.SuspendedUntil is not { } until || until > ev.Date))
            return "is suspended";
        if (f.Contract?.Promotion != ev.Promotion)
            return f.Contract is { } c && world.Get(c.Promotion) is { } other
                ? $"has left for {other.Abbreviation}" : "is no longer under contract";
        // A player-rules deal fought out: he is in his re-sign window, bookable by
        // nobody until he signs again (AI deals resolve in the monthly expiry pass).
        if (Contracts.DealWindow.OutOfDeal(world, f)) return "is out of contract — his deal is up";
        if (f.Status == FighterStatus.Injured && f.CurrentInjury is { } inj && inj.ExpectedReturn > ev.Date)
            return "is injured";
        if (f.ReadyToFightOn is { } ready && ready > ev.Date) return "won't be cleared to fight in time";
        return null;
    }

    // A belt is on the line only while it's still the promotion's to give and
    // its holder (if any) is in the cage — the champion's walk-out, a strip on
    // the scale or a retirement elsewhere takes it off the bout.
    private static bool CheckBelt(World world, Event ev, Fight fight)
    {
        if (!fight.IsTitleFight) return false;
        bool ok = fight.Title is { } tid && world.Get(tid) is { } belt && belt.Promotion == ev.Promotion
            && (belt.CurrentChampion is not { } holder || holder == fight.RedCorner || holder == fight.BlueCorner);
        if (ok) return false;
        fight.IsTitleFight = false;
        fight.Title = null;
        return true;
    }

    /// <summary>
    /// After a card has LOST ITS ANNOUNCED MAIN EVENT, keep it a proper show: a
    /// belt still on the card moves to the top of the bill, and whatever
    /// headlines goes the title distance. Callers use this only when the main
    /// event came off the card; a feud headliner over a belt co-main is a choice,
    /// not a mistake to correct. (AI cards only: the player orders their own bill.)
    /// </summary>
    internal static void RestoreHeadliner(World world, Event ev)
    {
        if (ev.Card.Count == 0 || ev.Promotion == world.PlayerPromotion || world.Get(ev.Promotion) is not { } promo) return;
        if (!ev.Card[^1].IsTitleFight && ev.Card.FindLastIndex(f => f.IsTitleFight) is var t and >= 0)
        {
            var belt = ev.Card[t];
            ev.Card.RemoveAt(t);
            ev.Card.Add(belt);
        }
        var main = ev.Card[^1];
        if (main.ScheduledRounds < promo.Rules.TitleRounds) main.ScheduledRounds = promo.Rules.TitleRounds;
    }

    /// <summary>
    /// Fight night's last word on an AI card: nobody who isn't signed to the
    /// promotion (local filler aside), retired, suspended or booked elsewhere
    /// walks out — any such bout is scrapped — and no belt is on a bout it
    /// can't be on. The audit normally leaves nothing for this to do.
    /// </summary>
    internal static void GuardAtRun(World world, Event ev)
    {
        if (world.Get(ev.Promotion) is not { } promo) return;
        if (ev.Promotion == world.PlayerPromotion) { GuardPlayerCardAtRun(world, ev); return; }
        RefreshVenue(world, promo, ev);

        // Spoken for elsewhere: a card that already ran today, the player's
        // held cards, and anything that comes before this one. (A later card
        // that shares a name with this one loses him — this card runs first.)
        var elsewhere = new HashSet<FighterId>();
        foreach (var e in world.Events.Values)
        {
            if (e.Id == ev.Id) continue;
            bool first = e.Status == EventStatus.Completed ? e.Date == ev.Date
                       : e.Status == EventStatus.Scheduled && !LocalCircuit.IsLocalCard(e)   // a club card loses to us
                         && ((e.Promotion == world.PlayerPromotion && world.Get(e.Promotion) is not null) || e.Date < ev.Date);
            if (!first) continue;
            foreach (var f in e.Card) { elsewhere.Add(f.RedCorner); elsewhere.Add(f.BlueCorner); }
        }
        var seen = new HashSet<FighterId>();   // and nobody fights twice on one card

        foreach (var fight in ev.Card.ToList())
        {
            bool Elsewhere(FighterId id) => elsewhere.Contains(id) || seen.Contains(id);
            var why = WhyOff(world, ev, fight.RedCorner, Elsewhere) is { } r ? (fight.RedCorner, r)
                    : WhyOff(world, ev, fight.BlueCorner, Elsewhere) is { } b ? (fight.BlueCorner, b)
                    : ((FighterId, string)?)null;
            if (why is null) { seen.Add(fight.RedCorner); seen.Add(fight.BlueCorner); }
            if (why is { } off) Storylines.ScrapBout(world, ev, fight, off.Item1, ev.Date, off.Item2);
            else CheckBelt(world, ev, fight);
        }
        // (The caller restores a headliner if the announced main event is gone.)
    }

    // --- The player's cards ---------------------------------------------------
    //
    // The same guarantee as an AI card: no bout runs with a fighter who has
    // retired, is suspended, is hurt past the date, is no longer signed to the
    // player's promotion (the unsigned local filler aside — the player cannot
    // book anyone else) or is booked on another card; and a belt is on a bout
    // only while its champion is in it or the title is vacant. But the card is
    // the PLAYER's: nothing is re-booked for him. A lost bout comes off the card
    // at once (the model has no "opponent needed" corner), an unfought local
    // opponent goes back to the scene, and the player gets a mail naming who is
    // out and why. Draws no RNG.

    /// <summary>
    /// A fighter has just become unavailable (retired, released, signed away):
    /// take him off every scheduled card of the player's at once, so the Card
    /// Desk and the inbox see it the moment it happens rather than at the next
    /// tick's audit. <paramref name="reason"/> completes "X is off AFC 161: ..."
    /// ("he retired", "he was released", "he signed with Pacific Fight League").
    /// AI cards are left to <see cref="Audit"/> (which may save their bouts).
    /// </summary>
    public static void OffPlayerCards(World world, Fighter f, DateOnly date, string reason)
    {
        if (f.LocalTalent || world.Get(world.PlayerPromotion) is null) return;
        foreach (var ev in world.Events.Values
                     .Where(e => e.Status == EventStatus.Scheduled && e.Promotion == world.PlayerPromotion
                                 && e.Card.Any(x => x.RedCorner == f.Id || x.BlueCorner == f.Id))
                     .OrderBy(e => e.Date).ThenBy(e => e.Id.Value)
                     .ToList())
            foreach (var fight in ev.Card.Where(x => x.RedCorner == f.Id || x.BlueCorner == f.Id).ToList())
                PullPlayerBout(world, ev, fight, f.Id, reason, date);
    }

    /// <summary>
    /// The audit of the player's cards alone (see <see cref="Audit"/>): run after
    /// one of his cards is fought between ticks, since a belt that changed hands
    /// on it may be on a later card's bout that no longer has its champion.
    /// </summary>
    public static void AuditPlayerCards(World world, DateOnly today)
    {
        if (world.Get(world.PlayerPromotion) is null) return;
        var mine = world.Events.Values
            .Where(e => e.Status == EventStatus.Scheduled && e.Promotion == world.PlayerPromotion)
            .OrderBy(e => e.Date).ThenBy(e => e.Id.Value)
            .ToList();
        var claim = new Dictionary<FighterId, FightId>();
        foreach (var ev in mine)
            foreach (var f in ev.Card)
                foreach (var id in new[] { f.RedCorner, f.BlueCorner })
                    claim.TryAdd(id, f.Id);
        foreach (var ev in mine)
            foreach (var fight in ev.Card.ToList())
            {
                bool Elsewhere(FighterId id) => claim.TryGetValue(id, out var first) && first != fight.Id;
                KeepPlayerBoutHonest(world, ev, fight, Elsewhere, today);
            }
    }

    // Fight night on the player's card: the audit normally leaves nothing for
    // this to do, but a fighter released or retired after the week's last audit
    // (between ticks, by the player or a test) is caught here.
    private static void GuardPlayerCardAtRun(World world, Event ev)
    {
        // Spoken for elsewhere: a card that already ran tonight, and the
        // player's own cards that come before this one. (The player wins any
        // clash with an AI card, so those don't count against him.)
        var elsewhere = new HashSet<FighterId>();
        foreach (var e in world.Events.Values)
        {
            if (e.Id == ev.Id) continue;
            bool first = e.Status == EventStatus.Completed ? e.Date == ev.Date
                       : e.Status == EventStatus.Scheduled && e.Promotion == ev.Promotion
                         && (e.Date < ev.Date || (e.Date == ev.Date && e.Id.Value < ev.Id.Value));
            if (!first) continue;
            foreach (var f in e.Card) { elsewhere.Add(f.RedCorner); elsewhere.Add(f.BlueCorner); }
        }
        var seen = new HashSet<FighterId>();   // nobody fights twice on one card
        foreach (var fight in ev.Card.ToList())
        {
            bool Elsewhere(FighterId id) => elsewhere.Contains(id) || seen.Contains(id);
            if (!KeepPlayerBoutHonest(world, ev, fight, Elsewhere, ev.Date)) continue;
            seen.Add(fight.RedCorner); seen.Add(fight.BlueCorner);
        }
    }

    // One bout of the player's: pulled (false) if a corner can't fight, else
    // kept (true) with any belt that can no longer be on it taken off.
    private static bool KeepPlayerBoutHonest(World world, Event ev, Fight fight, Func<FighterId, bool> elsewhere, DateOnly date)
    {
        var red = WhyOff(world, ev, fight.RedCorner, elsewhere);
        var blue = red is null ? WhyOff(world, ev, fight.BlueCorner, elsewhere) : null;
        if (red is not null || blue is not null)
        {
            var outId = red is not null ? fight.RedCorner : fight.BlueCorner;
            PullPlayerBout(world, ev, fight, outId, PlayerReason(world, outId, red ?? blue!), date);
            return false;
        }
        var belt = fight.Title is { } tid ? world.Get(tid) : null;
        if (CheckBelt(world, ev, fight) && belt is not null)
        {
            string why = belt.CurrentChampion is { } holder && world.Get(holder) is { } champ
                ? $"{champ.FullName} holds it now and isn't in the bout" : $"it is no longer {world.Get(ev.Promotion)?.Abbreviation ?? "your"}'s to give";
            Mail.Post(world, date, MailCategory.General, "Matchmaking",
                $"Title off the line at {ev.Name}",
                $"{Names(world, fight)} goes ahead at {ev.Name}, but the {belt.Name} is no longer on the line: {why}.",
                aboutFighter: fight.RedCorner);
        }
        return true;
    }

    /// <summary>
    /// Take one bout off the player's card because <paramref name="outId"/> can't
    /// fight it: the bout comes off, an unfought local opponent goes back to the
    /// scene, and the player gets the mail. The remaining corner stays on the
    /// roster, free to be re-booked from the event screen.
    /// </summary>
    internal static void PullPlayerBout(World world, Event ev, Fight fight, FighterId outId, string reason, DateOnly date)
    {
        if (!ev.Card.Contains(fight)) return;
        bool wasMain = ReferenceEquals(ev.Card[^1], fight);
        ev.Card.Remove(fight);
        var stayId = fight.RedCorner == outId ? fight.BlueCorner : fight.RedCorner;
        var stay = world.Get(stayId);
        bool stayIsLocal = stay is { LocalTalent: true };
        PlayerBooking.ReleaseLocals(world, fight);

        var outF = world.Get(outId);
        string name = outF?.FullName ?? "A booked fighter";
        string body = $"{name} is off {ev.Name}: {reason}. ";
        body += stay is null || stayIsLocal
            ? "The local opponent booked against him has gone back to the scene."
            : $"His bout with {stay.FullName} needs a new opponent — rebook from the event screen.";
        if (wasMain) body += " That was your main event: the card needs a new headliner.";
        if (ev.Card.Count == 0) body += " The card has no bouts left.";
        if (outF is not null) body = Contracts.Lifers.Gendered(outF, body);
        Mail.Post(world, date, MailCategory.General, "Matchmaking",
            $"{name} is off {ev.Name}", body, aboutFighter: outId);   // (no event link: the player may cancel the card)
    }

    // "he retired" / "he was released" / "he signed with X" for the player's mail, from the audit's reason.
    private static string PlayerReason(World world, FighterId id, string whyOff)
    {
        if (world.Get(id) is not { } f) return "he is no longer in the sport";
        if (f.Status == FighterStatus.Retired) return "he retired";
        if (f.Status == FighterStatus.Suspended) return "he is suspended";
        if (f.Contract is null) return "he is no longer under contract";
        if (f.Contract.Promotion != world.PlayerPromotion)
            return $"he signed with {world.Get(f.Contract.Promotion)?.Name ?? "another promotion"}";
        return "he " + whyOff;
    }

    private static string Names(World world, Fight fight) =>
        $"{world.Get(fight.RedCorner)?.FullName ?? "?"} vs {world.Get(fight.BlueCorner)?.FullName ?? "?"}";

    /// <summary>
    /// The room is fitted to the draw the promotion has ON THE NIGHT, as it was
    /// when AI cards were booked on the day. A card announced a month ahead
    /// keeps its city, but moves to the room that now fits its following: a
    /// startup announced in its first week would otherwise be stuck in a
    /// thousand-seat club while twice that many want in (that capped every new
    /// promotion's gate and killed them faster). Flagships keep their big
    /// house, and superfights keep the room they were booked into a month out.
    /// If the card's announcement made the news (and so named the room), the
    /// move is a headline too, so the announcement and the result never disagree.
    /// </summary>
    private static void RefreshVenue(World world, Promotion promo, Event ev)
    {
        if (SignatureEvents.IsSignature(world, ev) || Storylines.IsSuperfight(ev)) return;
        if (ev.VenueId is not { } current || world.Get(current) is not { } room) return;
        if (Geography.WorldMap.PickVenueIn(world, promo, room.City) is not { } fit || fit.Id == current) return;
        ev.VenueId = fit.Id;
        ev.Venue = fit.Name;
        if (world.Get(fit.City) is { } city) ev.Region = city.Region;
        // A card whose announcement made the news named its room: say it moved.
        if (MeritsHeadline(world, ev, AuthoredBookingOn(world, ev) is not null))
            News.Post(world, ev.Date, NewsCategory.General,
                $"{ev.Name} moves to {fit.Name}",
                fit.Capacity > room.Capacity
                    ? $"Demand outgrew {room.Name}; {promo.Abbreviation} takes the bigger room."
                    : $"{promo.Abbreviation} trades {room.Name} for a room it can fill.",
                promo.Id, aboutEvent: ev.Id);
    }

    // --- Reading the calendar -----------------------------------------------

    /// <summary>
    /// Every fighter booked on any scheduled PROMOTION card, anywhere. A staged
    /// local-circuit card commits nobody: it loses to a promotion that books
    /// one of its fighters (<see cref="LocalCircuit.YieldTo(World, IEnumerable{FighterId})"/>).
    /// </summary>
    public static HashSet<FighterId> Committed(World world)
    {
        var set = new HashSet<FighterId>();
        foreach (var e in world.Events.Values)
        {
            if (e.Status != EventStatus.Scheduled || LocalCircuit.IsLocalCard(e)) continue;
            foreach (var f in e.Card) { set.Add(f.RedCorner); set.Add(f.BlueCorner); }
        }
        return set;
    }

    /// <summary>The scheduled promotion card this fighter is committed to (other than <paramref name="except"/>), if any (staged club cards don't count).</summary>
    public static Event? CommittedTo(World world, FighterId fighter, EventId? except = null) =>
        world.Events.Values
            .Where(e => e.Status == EventStatus.Scheduled && e.Id != except && !LocalCircuit.IsLocalCard(e)
                        && e.Card.Any(f => f.RedCorner == fighter || f.BlueCorner == fighter))
            .OrderBy(e => e.Date).ThenBy(e => e.Id.Value)
            .FirstOrDefault();

    /// <summary>Is this fighter booked on some scheduled card (other than <paramref name="except"/>)?</summary>
    public static bool IsCommitted(World world, FighterId fighter, EventId? except = null) =>
        CommittedTo(world, fighter, except) is not null;

    /// <summary>
    /// The sport's calendar: every scheduled promotion card (the player's and
    /// everyone else's) between <paramref name="from"/> (default today) and
    /// <paramref name="to"/>, optionally for one promotion or one region — in
    /// date order, the flagship nights and the bigger houses first on a shared date.
    /// </summary>
    public static IReadOnlyList<UpcomingCard> Upcoming(World world, DateOnly? from = null, DateOnly? to = null,
        PromotionId? promotion = null, Region? region = null)
    {
        var start = from ?? world.CurrentDate;
        var list = new List<UpcomingCard>();
        foreach (var e in world.Events.Values)
        {
            if (e.Status != EventStatus.Scheduled || e.Date < start || (to is { } end && e.Date > end)) continue;
            if (world.Get(e.Promotion) is not { } p) continue;              // the local scene isn't a promotion card
            if (promotion is { } only && e.Promotion != only) continue;
            if (region is { } r && e.Region != r) continue;
            list.Add(Describe(world, e, p));
        }
        return list
            .OrderBy(c => c.Date)
            .ThenByDescending(c => c.IsSignature)
            .ThenByDescending(c => c.Promotion.Prestige.Value)
            .ThenBy(c => c.Event.Id.Value)
            .ToList();
    }

    /// <summary>
    /// For a show <paramref name="promotion"/> stages (or is thinking of staging)
    /// on <paramref name="date"/>: every other promotion's scheduled card inside
    /// the clash window (optionally one region's, for display), each flagged
    /// <see cref="RivalCard.Charged"/> when the box office counts it against the
    /// house (<see cref="Booking.Financials.IsClash"/>), and the attendance cut
    /// that will actually apply. An uncharged card is information only: an AI
    /// card announced for a later date, which today's rule doesn't charge.
    /// </summary>
    public static (IReadOnlyList<RivalCard> Rivals, double Penalty) RivalCardsNear(World world, PromotionId promotion,
        DateOnly date, Region? region = null, EventId? except = null)
    {
        int window = Booking.Financials.ClashWindowDays;
        var rivals = Upcoming(world, date.AddDays(-window), date.AddDays(window), region: region)
            .Where(c => c.Promotion.Id != promotion && c.Event.Id != except)
            .Select(c => new RivalCard(c, Booking.Financials.IsClash(world, promotion, date, except, c.Event)))
            .ToList();
        double penalty = Booking.Financials.ClashingCards(world, promotion, date, except).Count > 0
            ? Booking.Financials.ClashPenalty : 0;
        return (rivals, penalty);
    }

    private static UpcomingCard Describe(World world, Event e, Promotion p) => new(
        e, p, e.Date, AnnouncedOn(world, e), e.Region, e.Venue,
        e.Card.Count > 0 ? e.Card[^1] : null,
        e.Card.Count > 1 ? e.Card[^2] : null,
        e.Card.Any(f => f.IsTitleFight),
        SignatureEvents.IsSignature(world, e),
        e.Promotion == world.PlayerPromotion);

    // --- What the sport says about it ---------------------------------------

    /// <summary>
    /// Does an AI announcement earn a headline? The feed caps at 1,000 items
    /// (about four months of a busy world), and nearly every card has a belt on
    /// it, so most of the calendar stays on the calendar: a flagship or an
    /// authored booking always makes the news; a feud headliner does from a
    /// National-tier house up; a title fight does from a Continental house up.
    /// </summary>
    public static bool MeritsHeadline(World world, Event ev, bool authored = false)
    {
        if (authored || SignatureEvents.IsSignature(world, ev)) return true;
        if (ev.Card.Count == 0 || world.Get(ev.Promotion) is not { } promo) return false;
        var size = OrgSizing.SizeOf(promo);
        if (size < OrgSize.National) return false;
        var main = ev.Card[^1];
        if (Storylines.RivalryBetween(world, main.RedCorner, main.BlueCorner) is { Intensity: >= HeadlineFeud }) return true;
        return main.IsTitleFight && size >= OrgSize.Continental;
    }

    /// <summary>"ICF sets Sato vs Kang for the Emperor's New Year, 15 January, The Sunrise Dome" — and the why.</summary>
    public static (string Headline, string Body) Announcement(World world, Event ev, DesignedBooking? authored = null)
    {
        var promo = world.Get(ev.Promotion)!;
        var main = ev.Card[^1];
        var red = world.Get(main.RedCorner)!;
        var blue = world.Get(main.BlueCorner)!;
        string show = ShowLabel(promo, ev);
        string where = ev.Venue is { } v ? $", {v}" : "";
        string head = $"{promo.Abbreviation} sets {red.LastName} vs {blue.LastName} for {show}, {ev.Date:d MMMM}{where}";

        var parts = new List<string>();
        if (main.IsTitleFight && main.Title is { } tid && world.Get(tid) is { } belt)
            parts.Add(belt.CurrentChampion is { } holder
                ? $"{world.Get(holder)?.FullName ?? "The champion"} puts the {belt.Name} on the line against {(holder == red.Id ? blue : red).FullName}."
                : $"{red.FullName} and {blue.FullName} fight for the vacant {belt.Name}.");
        else
            parts.Add($"{red.FullName} meets {blue.FullName} at the top of the bill.");
        if (authored?.Pitch is { } pitch) parts.Add(pitch);
        else if (Flavor.EventHype.Tagline(world, ev) is { } tag && !tag.StartsWith("FOR THE ", StringComparison.Ordinal))
            parts.Add($"The poster line writes itself: {tag}.");
        if (ev.Card.Count > 1 && world.Get(ev.Card[^2].RedCorner) is { } cr && world.Get(ev.Card[^2].BlueCorner) is { } cb)
            parts.Add($"{cr.FullName} meets {cb.FullName} in the co-main.");
        return (head, string.Join(" ", parts));
    }

    // "the Emperor's New Year" / "Fight Night 412" — the show as a headline names it.
    private static string ShowLabel(Promotion promo, Event ev)
    {
        string name = ev.Name;
        if (SignatureEvents.NameOf(promo) is { } sig && name.StartsWith(sig, StringComparison.Ordinal))
            return "the " + Strip(sig, promo.Abbreviation);
        return Strip(name, promo.Abbreviation);

        static string Strip(string s, string abbr) => s.StartsWith(abbr + " ", StringComparison.Ordinal) ? s[(abbr.Length + 1)..] : s;
    }

    /// <summary>
    /// Day one's forward-looking headlines, straight off the calendar the world
    /// was handed: each scheduled AI card that merits the news, reported on the
    /// day it would have been announced. No invented facts — the names, show,
    /// date and room are the card's own.
    /// </summary>
    public static IEnumerable<(DateOnly Date, NewsCategory Cat, string Head, string Body, PromotionId Promo, FighterId? About)>
        PrologueItems(World world, DateOnly startDate)
    {
        // When each fighter last fought: a bout isn't announced before the
        // headliners' previous fights are over.
        var lastFought = new Dictionary<FighterId, DateOnly>();
        foreach (var e in world.Events.Values.Where(e => e.Status == EventStatus.Completed))
            foreach (var f in e.Card)
                foreach (var id in new[] { f.RedCorner, f.BlueCorner })
                    if (!lastFought.TryGetValue(id, out var d) || e.Date > d) lastFought[id] = e.Date;

        foreach (var e in world.Events.Values
                     .Where(e => e.Status == EventStatus.Scheduled && e.Date > startDate
                                 && e.Promotion != world.PlayerPromotion && e.Card.Count > 0)
                     .OrderBy(e => e.Date).ThenBy(e => e.Id.Value))
        {
            if (world.Get(e.Promotion) is not { } promo) continue;
            var authored = AuthoredBookingOn(world, e);
            if (!MeritsHeadline(world, e, authored is not null)) continue;
            var (head, body) = Announcement(world, e, authored);
            var on = AnnouncedOn(world, e);
            foreach (var id in new[] { e.Card[^1].RedCorner, e.Card[^1].BlueCorner })
                if (lastFought.TryGetValue(id, out var d) && d >= on) on = d.AddDays(1);
            if (on > startDate) on = startDate;
            yield return (on, SignatureEvents.IsSignature(world, e) || authored is not null ? NewsCategory.Milestone : NewsCategory.General,
                head, body, promo.Id, e.Card[^1].RedCorner);
        }
    }

    /// <summary>The authored booking this card's main event fulfils, if it is one.</summary>
    public static DesignedBooking? AuthoredBookingOn(World world, Event ev)
    {
        if (ev.Card.Count == 0 || world.Get(ev.Promotion) is not { } promo) return null;
        var main = ev.Card[^1];
        string? r = world.Get(main.RedCorner) is { } fr ? $"{fr.FirstName} {fr.LastName}" : null;
        string? b = world.Get(main.BlueCorner) is { } fb ? $"{fb.FirstName} {fb.LastName}" : null;
        return BookedNights(world, promo).Where(n => n.Night == ev.Date).SelectMany(n => n.Rows)
            .FirstOrDefault(x => x.Order == 0
                && ((x.Red == r && (x.Blue is null || x.Blue == b)) || (x.Red == b && (x.Blue is null || x.Blue == r))));
    }
}
