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
        int weeks = promo is null ? 0
            : SignatureEvents.IsSignature(world, ev) ? SignatureLeadWeeks
            : LeadWeeks(OrgSizing.SizeOf(promo));
        return ev.Date.AddDays(-7 * weeks);
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
            // The flagship goes up first and furthest out.
            if (SignatureEvents.NameOf(promo) is not null)
                for (int k = first; k <= SignatureLeadWeeks; k++)
                {
                    var when = today.AddDays(7 * k);
                    if (SignatureEvents.For(promo, when) is null || !IsShowWeek(promo, when)) continue;
                    if (taken.Contains((promo.Id.Value, when.DayNumber)) || HasSignature(world, promo, when.Year)) continue;
                    if (AnnounceCard(world, promo, when, today, matchmaker, events, committed, lastMet, postNews))
                        taken.Add((promo.Id.Value, when.DayNumber));
                }

            // The ordinary nights book around the fighters an authored flagship
            // booking is holding (they're in camp for the big one).
            var reserved = Reserved(world, promo, today);
            var around = reserved.Count == 0 ? committed : new HashSet<FighterId>(committed.Concat(reserved));
            int lead = LeadWeeks(OrgSizing.SizeOf(promo));
            for (int k = first; k <= lead; k++)
            {
                var when = today.AddDays(7 * k);
                if (!IsShowWeek(promo, when) || taken.Contains((promo.Id.Value, when.DayNumber))) continue;
                bool flagship = SignatureEvents.For(promo, when) is not null && !HasSignature(world, promo, when.Year);
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

    /// <summary>How far ahead of its flagship an org starts holding an authored booking's fighters back.</summary>
    public const int ReserveWeeks = 16;

    /// <summary>
    /// The fighters an authored booking (see <see cref="DesignedBookings"/>) for
    /// this org's NEXT flagship is holding back from its ordinary cards: inside
    /// <see cref="ReserveWeeks"/> of the flagship month, until the flagship is
    /// announced, and only while the fight hasn't happened yet.
    /// </summary>
    private static HashSet<FighterId> Reserved(World world, Promotion promo, DateOnly today)
    {
        var held = new HashSet<FighterId>();
        if (SignatureEvents.NameOf(promo) is not { } sig || SignatureEvents.MonthOf(promo) is not { } month) return held;
        var rows = DesignedBookings.For(promo.Abbreviation, sig).ToList();
        if (rows.Count == 0) return held;

        var next = new DateOnly(today.Year, month, 1);
        if (next.AddMonths(1) <= today) next = next.AddYears(1);
        if (next.DayNumber - today.DayNumber > ReserveWeeks * 7 || HasSignature(world, promo, next.Year)) return held;

        foreach (var row in rows)
        {
            if (FindDesigned(world, row.Red) is not { } red || FindDesigned(world, row.Blue) is not { } blue) continue;
            if (red.Contract?.Promotion != promo.Id || blue.Contract?.Promotion != promo.Id) continue;
            if (MetSince(world, red.Id, blue.Id, world.StartDate)) continue;
            held.Add(red.Id);
            held.Add(blue.Id);
        }
        return held;
    }

    private static Fighter? FindDesigned(World world, string last) => world.Fighters.Values
        .Where(f => f.Designed && f.LastName == last && f.Status != FighterStatus.Retired)
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
    /// Put one show on the books: choose the main event — an authored booking
    /// for this show if one can be honoured, else the hottest feud on the roster,
    /// else the matchmaker's most pressing belt — and a co-main around it.
    /// </summary>
    private static bool AnnounceCard(World world, Promotion promo, DateOnly when, DateOnly today,
        Matchmaker matchmaker, EventService events, HashSet<FighterId> committed,
        Dictionary<(int, int), DateOnly> lastMet, bool postNews)
    {
        string? signature = SignatureEvents.For(promo, when) is { } sig && !HasSignature(world, promo, when.Year) ? sig : null;

        Matchup? main = null;
        DesignedBooking? authored = null;
        if (signature is not null)
            foreach (var row in DesignedBookings.For(promo.Abbreviation, signature))
            {
                if (DesignedEligible(world, promo, row, when, committed) is not { } m) continue;
                main = m;
                authored = row;
                break;
            }
        main ??= FeudHeadliner(world, promo, when, committed, today, lastMet);

        IReadOnlyList<Matchup> top;
        if (main is { } chosen)
        {
            var around = new HashSet<FighterId>(committed) { chosen.Red, chosen.Blue };
            top = matchmaker.BestBout(world, promo, when, around, titleLength: false) is { } co
                ? new[] { co, chosen }
                : new[] { chosen };
        }
        else top = matchmaker.Headliners(world, promo.Id, when, committed);
        if (top.Count == 0) return false;   // nobody to book this week — try again next tick

        int number = world.Events.Values.Count(e => e.Promotion == promo.Id) + 1;
        Event ev;
        if (signature is not null)
        {
            // A tradition has an ADDRESS: the flagship goes home, to the biggest
            // room in the org's own town (the ICF's New Year show belongs in the
            // dome its lore has always claimed).
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
        if (postNews && MeritsHeadline(world, ev, authored is not null))
        {
            var (head, body) = Announcement(world, ev, authored);
            News.Post(world, today, signature is not null || authored is not null ? NewsCategory.Milestone : NewsCategory.General,
                head, body, promo.Id, ev.Card[^1].RedCorner, ev.Id);
        }
        return true;
    }

    // An authored booking is honoured only when it can really happen: both men
    // active, signed here, in one division, fit on the night, not committed
    // elsewhere — and only until the fight has actually happened.
    private static Matchup? DesignedEligible(World world, Promotion promo, DesignedBooking row, DateOnly when,
        IReadOnlySet<FighterId> committed)
    {
        if (FindDesigned(world, row.Red) is not { } red || FindDesigned(world, row.Blue) is not { } blue) return null;
        if (MetSince(world, red.Id, blue.Id, world.StartDate)) return null;
        return Bookable(world, promo, red, blue, when, committed, requireWorthy: false);
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

    private static bool MetSince(World world, FighterId a, FighterId b, DateOnly since) =>
        world.Events.Values.Any(e => e.Status == EventStatus.Completed && e.Date >= since
            && e.Card.Any(f => f.Outcome is not null
                && ((f.RedCorner == a && f.BlueCorner == b) || (f.RedCorner == b && f.BlueCorner == a))));

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
                                 && e.Date >= today && e.Date.DayNumber - today.DayNumber <= UndercardFillDays
                                 && !Storylines.IsSuperfight(e))
                     .OrderBy(e => e.Date).ThenBy(e => e.Id.Value)
                     .ToList())
        {
            if (world.Get(ev.Promotion) is not { Defunct: false } promo || ev.Card.Count == 0) continue;
            int need = CardSize(promo) - ev.Card.Count;
            if (need <= 0) continue;
            var reserved = SignatureEvents.IsSignature(world, ev) ? new HashSet<FighterId>() : Reserved(world, promo, today);
            var under = matchmaker.BuildUndercard(world, promo.Id, need, ev.Date,
                reserved.Count == 0 ? committed : new HashSet<FighterId>(committed.Concat(reserved)));
            if (under.Count == 0) continue;

            var fights = under.Select(m => new Fight
            {
                Id = world.NewFightId(),
                RedCorner = m.Red,
                BlueCorner = m.Blue,
                WeightClass = m.WeightClass,
                ScheduledRounds = m.Rounds,
            }).ToList();
            ev.Card.InsertRange(0, fights);
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
    /// name on a card overnight. Draws no RNG. The player's own cards are theirs
    /// to fix and are left alone (but they win any double-booking).
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
            if (ev.Promotion == world.PlayerPromotion) continue;
            if (world.Get(ev.Promotion) is not { } promo) continue;
            if (promo.Defunct) { ev.Status = EventStatus.Cancelled; continue; }

            bool changed = false;
            int daysOut = ev.Date.DayNumber - today.DayNumber;
            foreach (var fight in ev.Card.ToList())
            {
                bool Elsewhere(FighterId id) => claim.TryGetValue(id, out var first) && first != fight.Id;
                var red = WhyOff(world, ev, fight.RedCorner, Elsewhere);
                var blue = WhyOff(world, ev, fight.BlueCorner, Elsewhere);
                if (red is null && blue is null)
                {
                    changed |= CheckBelt(world, ev, fight);
                    continue;
                }

                changed = true;
                var (outId, why) = red is not null ? (fight.RedCorner, red) : (fight.BlueCorner, blue!);
                bool bothGone = red is not null && blue is not null;
                if (!bothGone && daysOut > FightWeekDays)
                    Storylines.SaveBout(world, ev, fight, outId, committed, today, why);
                else
                    Storylines.ScrapBout(world, ev, fight, outId, today, why);
            }
            if (changed) RestoreHeadliner(world, ev);
        }
    }

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
    /// After a card loses bouts, keep it a proper show: a belt still on the card
    /// moves to the top of the bill, and whatever headlines goes the title
    /// distance. (AI cards only — the player orders their own bill.)
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
        if (ev.Promotion == world.PlayerPromotion || world.Get(ev.Promotion) is null) return;

        // Spoken for elsewhere: a card that already ran today, the player's
        // held cards, and anything that comes before this one. (A later card
        // that shares a name with this one loses him — this card runs first.)
        var elsewhere = new HashSet<FighterId>();
        foreach (var e in world.Events.Values)
        {
            if (e.Id == ev.Id) continue;
            bool first = e.Status == EventStatus.Completed ? e.Date == ev.Date
                       : e.Status == EventStatus.Scheduled && (e.Promotion == world.PlayerPromotion || e.Date < ev.Date);
            if (!first) continue;
            foreach (var f in e.Card) { elsewhere.Add(f.RedCorner); elsewhere.Add(f.BlueCorner); }
        }
        var seen = new HashSet<FighterId>();   // and nobody fights twice on one card

        bool changed = false;
        foreach (var fight in ev.Card.ToList())
        {
            bool Elsewhere(FighterId id) => elsewhere.Contains(id) || seen.Contains(id);
            var why = WhyOff(world, ev, fight.RedCorner, Elsewhere) is { } r ? (fight.RedCorner, r)
                    : WhyOff(world, ev, fight.BlueCorner, Elsewhere) is { } b ? (fight.BlueCorner, b)
                    : ((FighterId, string)?)null;
            if (why is null) { seen.Add(fight.RedCorner); seen.Add(fight.BlueCorner); }
            if (why is { } off)
            {
                Storylines.ScrapBout(world, ev, fight, off.Item1, ev.Date, off.Item2);
                changed = true;
            }
            else changed |= CheckBelt(world, ev, fight);
        }
        if (changed) RestoreHeadliner(world, ev);
    }

    // --- Reading the calendar -----------------------------------------------

    /// <summary>Every fighter booked on any scheduled card, anywhere.</summary>
    public static HashSet<FighterId> Committed(World world)
    {
        var set = new HashSet<FighterId>();
        foreach (var e in world.Events.Values)
        {
            if (e.Status != EventStatus.Scheduled) continue;
            foreach (var f in e.Card) { set.Add(f.RedCorner); set.Add(f.BlueCorner); }
        }
        return set;
    }

    /// <summary>The scheduled card this fighter is committed to (other than <paramref name="except"/>), if any.</summary>
    public static Event? CommittedTo(World world, FighterId fighter, EventId? except = null) =>
        world.Events.Values
            .Where(e => e.Status == EventStatus.Scheduled && e.Id != except
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
        if (authored is not null) parts.Add(authored.Pitch);
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
        if (ev.Card.Count == 0 || world.Get(ev.Promotion) is not { } promo || SignatureEvents.NameOf(promo) is not { } sig) return null;
        if (!ev.Name.StartsWith(sig, StringComparison.Ordinal)) return null;
        var main = ev.Card[^1];
        string? r = world.Get(main.RedCorner)?.LastName, b = world.Get(main.BlueCorner)?.LastName;
        return DesignedBookings.For(promo.Abbreviation, sig)
            .FirstOrDefault(x => (x.Red == r && x.Blue == b) || (x.Red == b && x.Blue == r));
    }
}
