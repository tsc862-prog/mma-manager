using MmaManager.Core.Model;
using MmaManager.Core.Sim.Org;

namespace MmaManager.Core.Sim.Contracts;

/// <summary>A promise as the UI reads it (<see cref="Promises.For"/>): what was promised, by when, how it stands, in words.</summary>
public sealed record PromiseStatus(
    int Id, PromiseKind Kind, PromotionId Promotion, string What, PromiseState State,
    DateOnly Made, DateOnly Deadline, int? DaysLeft, string Progress, string StatusText, bool AtRisk);

/// <summary>
/// One promise the owner could put on the slip at the table, as the UI reads it
/// (<see cref="NegotiationView.PromiseOptions"/>): whether it can be made at all (and why not),
/// what it is worth to him in words, and how much it takes off his number on these terms.
/// </summary>
public sealed record PromiseOption(PromiseKind Kind, int Fights, bool Allowed, string Why, string Worth, Money PurseOff);

/// <summary>
/// PROMISES WITH TEETH (contracts stage 9). The owner may write three promises into a
/// fighter's deal at the table — ACTIVITY (a number of fights inside the year), a HOME SHOW
/// (a fight on one of his cards in the fighter's home country inside the year) and a TITLE
/// SHOT (a title fight inside the year) — and each is worth something to the fighter
/// (<see cref="AppealOf"/>, read by <see cref="Negotiator.Appeal"/>, so it can stand in for
/// money): a title shot to a contender who wants a belt, a home show to a man who wants to
/// fight at home and doesn't already, activity to one who has been sitting.
///
/// Every promise has a deadline and a monthly test (<see cref="RunMonthly"/>) against the
/// live world — the promotion's cards, where it staged them, its belts:
/// <list type="bullet">
/// <item>KEPT: a little loyalty and a word of thanks from him.</item>
/// <item>BROKEN (the deadline passed, or he left the promotion, without it): a real loss of
///   loyalty, a standing <see cref="Lifers"/> grievance for a year (a lifer stops holding),
///   and the next table with him remembers (<see cref="BrokenBy"/>: he asks more, has less
///   patience, and takes no more promises for two years).</item>
/// <item>VOID: he retired or the promotion closed.</item>
/// </list>
/// The owner is warned while there is still time (<see cref="WarnDays"/> before the deadline,
/// or when his deal reaches its last fight). A promise he can't possibly be kept — no belt in
/// his division, no venue within reach in his country, more fights than the deal holds — is
/// refused at the table (<see cref="Refusal"/>).
///
/// Player-rules promotions only (<see cref="DealWindow.PlayerRules"/>): an AI promotion never
/// puts a promise on a slip, and with <see cref="World.Promises"/> empty every hook here is a
/// no-op, so an all-AI world is untouched. No RNG anywhere; everything reads public facts
/// (rankings, records, cards, geography) and his own wants — never ability.
/// </summary>
public static class Promises
{
    /// <summary>How long a promise runs from the day it is made.</summary>
    public const int DeadlineDays = 365;

    /// <summary>The owner is warned when this many days or fewer are left.</summary>
    public const int WarnDays = 60;

    /// <summary>A fight booked before the deadline on a card up to this many days after it still keeps the promise.</summary>
    public const int GraceDays = 60;

    /// <summary>The fewest and most fights an activity promise can name.</summary>
    public const int MinActivity = 2, MaxActivity = 4;

    /// <summary>Loyalty a kept promise earns.</summary>
    public const int KeptLoyalty = 3;

    /// <summary>Loyalty a broken promise costs (while he is still with the promotion).</summary>
    public const int BrokenLoyalty = 15;

    /// <summary>How long a broken promise stands as a <see cref="Lifers"/> grievance (unless a later promise is kept).</summary>
    public const int GrievanceDays = 365;

    /// <summary>How long a broken promise sours the table with that promotion (<see cref="BrokenBy"/>).</summary>
    public const int TrustDays = 730;

    /// <summary>How much more he asks at the table per broken promise remembered (at most two count).</summary>
    public const decimal DistrustPerBreak = 0.10m;

    /// <summary>Resolved promises stay on the books this long, then are dropped.</summary>
    public const int KeepResolvedDays = 1095;

    // What each promise can be worth inside Appeal (each pull is 0..1 before its weight).
    private const double TitleWeight = 0.40, HomeWeight = 0.25, ActivityWeight = 0.20;

    // --- What a promise is worth to him ---------------------------------------------------

    /// <summary>
    /// What the promises on this slip add to his appeal for the promotion (0..0.85): each
    /// one's pull times its weight, nothing for one that can't be kept, and nothing at all
    /// from a promotion that broke its word to him in the last <see cref="TrustDays"/> days.
    /// 0 for a slip without promises (every AI offer), without reading anything.
    /// </summary>
    public static double AppealOf(World world, Fighter f, ContractOffer o, ContractExpectations exp)
    {
        if (!o.HasPromises || world.Get(o.Promotion) is not { } promo) return 0.0;
        if (BrokenBy(world, f.Id, o.Promotion, world.CurrentDate) > 0) return 0.0;
        double a = 0.0;
        if (o.PromiseTitleShot && Refusal(world, promo, f, PromiseKind.TitleShot, 0, int.MaxValue) is null)
            a += TitleWeight * TitlePull(world, promo, f, exp);
        if (o.PromiseHomeShow && Refusal(world, promo, f, PromiseKind.HomeShow, 0, int.MaxValue) is null)
            a += HomeWeight * HomePull(world, promo, f, exp);
        if (o.PromiseFights > 0 && Refusal(world, promo, f, PromiseKind.Activity, o.PromiseFights, int.MaxValue) is null)
            a += ActivityWeight * ActivityPull(world, f, o.PromiseFights);
        return a;
    }

    // A title shot means most to a contender who wants a belt: his place in this promotion's
    // rankings (or, before he is on its roster, the sport's), times how much he cares.
    private static double TitlePull(World world, Promotion promo, Fighter f, ContractExpectations exp)
    {
        double want = exp.Priorities.TitleOpportunity / 100.0;
        int rank = promo.Divisions.FirstOrDefault(d => d.Matches(f))?.Rankings.IndexOf(f.Id) ?? -1;
        double contender;
        if (rank >= 0) contender = rank <= 1 ? 1.0 : rank <= 4 ? 0.7 : rank <= 9 ? 0.45 : 0.2;
        else
        {
            int g = world.GlobalRankings.TryGetValue(f.WeightClass, out var list) ? list.IndexOf(f.Id) : -1;
            contender = g is >= 0 and < 10 ? 0.6 : g is >= 0 and < 30 ? 0.4 : 0.2;
        }
        return want * contender;
    }

    // A home show means most to a man who wants to fight at home and whose promotion
    // doesn't already stage its cards in his country.
    private static double HomePull(World world, Promotion promo, Fighter f, ContractExpectations exp) =>
        exp.Priorities.HomeRegion / 100.0 * (HqIn(world, promo, f.Country) ? 0.15 : 1.0);

    // Activity means most to a man who has been sitting: how long since his last fight,
    // and how many fights are promised.
    private static double ActivityPull(World world, Fighter f, int fights)
    {
        int idle = LastFought(world, f) is { } last ? world.CurrentDate.DayNumber - last.DayNumber : 365;
        double idleness = Math.Clamp((idle - 60) / 240.0, 0.0, 1.0);
        double count = fights >= 4 ? 1.0 : fights == 3 ? 0.85 : 0.6;
        return count * (0.3 + 0.7 * idleness);
    }

    private static bool HqIn(World world, Promotion promo, CountryId country) =>
        promo.HomeCity is { } hq && world.Get(hq)?.Country == country;

    private static DateOnly? LastFought(World world, Fighter f)
    {
        DateOnly? last = null;
        foreach (var ev in world.Events.Values)
        {
            if (ev.Status != EventStatus.Completed || (last is { } l && ev.Date <= l)) continue;
            foreach (var x in ev.Card)
                if (x.RedCorner == f.Id || x.BlueCorner == f.Id) { last = ev.Date; break; }
        }
        return last;
    }

    // --- Can it be promised -------------------------------------------------------------

    /// <summary>
    /// Why this promise can't go on the slip, in his words; null when it can. Refused: a
    /// promotion not under the player's deal rules; one he already has open from this
    /// promotion; an activity promise outside <see cref="MinActivity"/>-<see cref="MaxActivity"/>
    /// or for more fights than the deal holds (<paramref name="dealFights"/>); a home show
    /// where the promotion can't stage a card in his country (no venue within its reach); a
    /// title shot with no belt in his division, or to the man who holds it.
    /// </summary>
    public static string? Refusal(World world, Promotion promo, Fighter f, PromiseKind kind, int fights, int dealFights)
    {
        if (!DealWindow.PlayerRules(world, promo.Id)) return "Only your own promotion's deals carry promises.";
        if (world.Promises.Any(p => p.Live && p.Fighter == f.Id && p.Promotion == promo.Id && p.Kind == kind))
            return Lifers.Gendered(f, "You already promised him that, and it still stands.");
        switch (kind)
        {
            case PromiseKind.Activity:
                if (fights < MinActivity || fights > MaxActivity) return $"An activity promise is {MinActivity} to {MaxActivity} fights in the year.";
                if (fights > dealFights) return "You can't promise more fights than the deal holds.";
                return null;
            case PromiseKind.HomeShow:
                if (world.Get(f.Country) is not { } country) return Lifers.Gendered(f, "There's nowhere on file he calls home.");
                bool reachable = world.Venues.Values.Any(v => world.Get(v.City)?.Country == f.Country && OrgReach.CanBookIn(world, promo, v.City));
                return reachable ? null : $"You can't stage a card in {country.Name}: it's beyond {promo.Abbreviation}'s reach.";
            default:
                var div = promo.Divisions.FirstOrDefault(d => d.Matches(f));
                if (div is null) return Lifers.Gendered(f, $"{promo.Abbreviation} doesn't run his division.");
                if (div.Title is not { } tid || world.Get(tid) is not { } title) return Lifers.Gendered(f, $"There's no belt in his division at {promo.Abbreviation} to fight for.");
                if (title.CurrentChampion == f.Id) return Lifers.Gendered(f, "He's the champion already.");
                return null;
        }
    }

    /// <summary>The first promise on this slip that can't be made, as (kind, why); null when all can.</summary>
    public static (PromiseKind Kind, string Why)? FirstRefusal(World world, Promotion promo, Fighter f, ContractOffer o)
    {
        if (o.PromiseTitleShot && Refusal(world, promo, f, PromiseKind.TitleShot, 0, o.Fights) is { } t) return (PromiseKind.TitleShot, t);
        if (o.PromiseHomeShow && Refusal(world, promo, f, PromiseKind.HomeShow, 0, o.Fights) is { } h) return (PromiseKind.HomeShow, h);
        if (o.PromiseFights > 0 && Refusal(world, promo, f, PromiseKind.Activity, o.PromiseFights, o.Fights) is { } a) return (PromiseKind.Activity, a);
        return null;
    }

    // --- Memory -------------------------------------------------------------------------

    /// <summary>Promises this promotion broke to him in the <see cref="TrustDays"/> days to <paramref name="date"/>.</summary>
    public static int BrokenBy(World world, FighterId fighter, PromotionId promo, DateOnly date)
    {
        if (world.Promises.Count == 0) return 0;
        int n = 0;
        foreach (var p in world.Promises)
            if (p.State == PromiseState.Broken && p.Fighter == fighter && p.Promotion == promo
                && p.Resolved is { } r && date.DayNumber - r.DayNumber < TrustDays)
                n++;
        return n;
    }

    /// <summary>
    /// What a promotion that broke its word to him pays for it at the table: his number goes
    /// up <see cref="DistrustPerBreak"/> for each promise it broke in the last
    /// <see cref="TrustDays"/> days (two at most). 1 when there are none (every AI table).
    /// </summary>
    public static decimal Distrust(World world, FighterId fighter, PromotionId promo, DateOnly date) =>
        1m + DistrustPerBreak * Math.Min(2, BrokenBy(world, fighter, promo, date));

    /// <summary>
    /// The broken promise that stands as a grievance against the promotion he is signed to:
    /// broken in the last <see cref="GrievanceDays"/> days, and no promise of theirs kept for
    /// him since. Null when there is none (always, in a world without promises).
    /// </summary>
    public static Promise? Grievance(World world, Fighter f, PromotionId promo, DateOnly date)
    {
        if (world.Promises.Count == 0) return null;
        Promise? broken = null;
        DateOnly? keptSince = null;
        foreach (var p in world.Promises)
        {
            if (p.Fighter != f.Id || p.Promotion != promo || p.Resolved is not { } r) continue;
            if (p.State == PromiseState.Broken && date.DayNumber - r.DayNumber < GrievanceDays
                && (broken is null || r >= broken.Resolved!.Value)) broken = p;
            else if (p.State == PromiseState.Kept && (keptSince is null || r > keptSince)) keptSince = r;
        }
        return broken is not null && (keptSince is not { } k || k < broken.Resolved!.Value) ? broken : null;
    }

    /// <summary>The promise in a phrase ("a title shot", "3 fights in a year", "a fight at home in Brazil").</summary>
    public static string What(World world, Promise p) => What(world, p.Kind, p.Fights, world.Get(p.Fighter));

    private static string What(World world, PromiseKind kind, int fights, Fighter? f) => kind switch
    {
        PromiseKind.Activity => $"{fights} fights in a year",
        PromiseKind.HomeShow => f is not null && world.Get(f.Country) is { } c ? $"a fight at home in {c.Name}" : "a fight at home",
        _ => "a title shot",
    };

    // --- Made at signing ----------------------------------------------------------------

    /// <summary>
    /// A deal was signed on <paramref name="o"/>: its promises are written down (each with a
    /// <see cref="DeadlineDays"/>-day deadline), and he says he'll hold the owner to them. Only
    /// for a player-rules promotion; any promise that can't be made is left off (the table
    /// refuses it first). Called by <see cref="ContractLifecycle.Sign"/>.
    /// </summary>
    internal static void OnSigned(World world, Fighter f, ContractOffer o, DateOnly date)
    {
        if (!o.HasPromises || world.Get(o.Promotion) is not { } promo || !DealWindow.PlayerRules(world, promo.Id)) return;
        var made = new List<Promise>();
        void Make(PromiseKind kind, int fights)
        {
            if (Refusal(world, promo, f, kind, fights, o.Fights) is not null) return;
            var p = new Promise
            {
                Id = world.Promises.Count == 0 ? 1 : world.Promises.Max(x => x.Id) + 1,
                Fighter = f.Id, Promotion = promo.Id, Kind = kind, Fights = kind == PromiseKind.Activity ? fights : 0,
                Made = date, Deadline = date.AddDays(DeadlineDays),
            };
            world.Promises.Add(p);
            made.Add(p);
        }
        if (o.PromiseTitleShot) Make(PromiseKind.TitleShot, 0);
        if (o.PromiseHomeShow) Make(PromiseKind.HomeShow, 0);
        if (o.PromiseFights > 0) Make(PromiseKind.Activity, o.PromiseFights);
        if (made.Count == 0 || promo.Id != world.PlayerPromotion) return;
        string list = string.Join(" and ", made.Select(p => What(world, p)));
        Living.Mail.Post(world, date, MailCategory.FighterRequest, f.FullName,
            $"{f.FullName} will hold us to it",
            Lifers.Gendered(f, $"“You gave me your word: {list}, by {made[0].Deadline:d MMMM yyyy}. I'll hold you to it.” "
                + $"Keep it and he'll remember; break it and he'll remember that too — and so will the next table."),
            aboutFighter: f.Id);
    }

    // --- The monthly test ------------------------------------------------------------------

    /// <summary>
    /// Monthly, before the lifers' pass (so a fresh break is a grievance this month): every
    /// open promise is tested against the live world — kept, broken, void, or still running
    /// (warned once when time is short). Resolved promises past <see cref="KeepResolvedDays"/>
    /// are dropped. A no-op in a world without promises. No RNG.
    /// </summary>
    public static void RunMonthly(World world, DateOnly date)
    {
        if (world.Promises.Count == 0) return;
        foreach (var p in world.Promises.Where(p => p.Live).ToList())   // id order
            Test(world, p, date);
        world.Promises.RemoveAll(p => p.Resolved is { } r && date.DayNumber - r.DayNumber > KeepResolvedDays);
    }

    private static void Test(World world, Promise p, DateOnly date)
    {
        var f = world.Get(p.Fighter);
        var promo = world.Get(p.Promotion);
        if (f is null || f.Status == FighterStatus.Retired) { Resolve(p, PromiseState.Void, date, "he retired"); return; }
        if (promo is null || promo.Defunct) { Resolve(p, PromiseState.Void, date, "the promotion closed"); return; }
        // He is no longer ours by a door that didn't settle it (a sale moved his deal): nobody's word to keep.
        if (f.Contract?.Promotion != p.Promotion) { Resolve(p, PromiseState.Void, date, "he's no longer with the promotion"); return; }

        if (Met(world, p, f)) { Keep(world, p, f, promo, date); return; }

        // Hurt at the deadline: the clock stops until he can fight again.
        if (date > p.Deadline && f.CurrentInjury is { } inj && inj.ExpectedReturn.AddDays(GraceDays) > p.Deadline)
            p.Deadline = inj.ExpectedReturn.AddDays(GraceDays);

        if (date > p.Deadline && (date > p.Deadline.AddDays(GraceDays) || !Honouring(world, p, f)))
        {
            Break(world, p, f, promo, date, "the deadline passed");
            return;
        }
        if (!p.Warned && (p.Deadline.DayNumber - date.DayNumber <= WarnDays || f.Contract is { FightsRemaining: <= 1 }))
            Warn(world, p, f, promo, date);
    }

    /// <summary>
    /// He is leaving <paramref name="from"/> by some door (released, his deal run out, signed
    /// elsewhere): any promise it made him and hasn't kept is broken now — nobody can keep it
    /// any more — unless it was kept after all. He takes the memory with him; the loyalty is
    /// moot. Called by <see cref="ContractLifecycle.LeavePromotion"/>.
    /// </summary>
    internal static void OnLeaving(World world, Fighter f, PromotionId from, string door, DateOnly date)
    {
        if (world.Promises.Count == 0) return;
        foreach (var p in world.Promises.Where(p => p.Live && p.Fighter == f.Id && p.Promotion == from).ToList())
        {
            if (world.Get(from) is not { } promo) { Resolve(p, PromiseState.Void, date, "the promotion closed"); continue; }
            if (Met(world, p, f)) { Resolve(p, PromiseState.Kept, date, "kept before he left"); continue; }
            Resolve(p, PromiseState.Broken, date, Lifers.Gendered(f, $"he left before it was kept ({door})"));
            if (from == world.PlayerPromotion)
                Living.Mail.Post(world, date, MailCategory.BoardNote, Living.Mail.Owner(world),
                    $"{f.FullName} left with our promise unkept",
                    Lifers.Gendered(f, $"We promised {f.FullName} {What(world, p)} and he's gone without it: {door}. "
                        + "He'll remember it if we ever sit down with him again."),
                    aboutFighter: f.Id);
        }
    }

    /// <summary>He retired: his open promises fall away (nobody's fault). Called by <see cref="Living.Lifecycle.Retire"/>.</summary>
    internal static void OnRetired(World world, Fighter f, DateOnly date)
    {
        if (world.Promises.Count == 0) return;
        foreach (var p in world.Promises)
            if (p.Live && p.Fighter == f.Id) Resolve(p, PromiseState.Void, date, "he retired");
    }

    // Has the promotion done what it promised, on cards fought since the promise (up to the
    // deadline and its grace)?
    private static bool Met(World world, Promise p, Fighter f)
    {
        if (p.Kind == PromiseKind.TitleShot
            && world.Titles.Values.Any(t => t.Promotion == p.Promotion && t.CurrentChampion == f.Id))
            return true;
        var until = p.Deadline.AddDays(GraceDays);
        int fought = 0;
        foreach (var ev in world.Events.Values)
        {
            if (ev.Promotion != p.Promotion || ev.Status != EventStatus.Completed || ev.Date <= p.Made || ev.Date > until) continue;
            foreach (var x in ev.Card)
            {
                if (x.Outcome is null || (x.RedCorner != f.Id && x.BlueCorner != f.Id)) continue;
                if (Qualifies(world, p, f, ev, x) && ++fought >= Need(p)) return true;
            }
        }
        return false;
    }

    // Booked to keep it: a qualifying bout on one of the promotion's scheduled cards inside
    // the grace (for activity, enough of them on top of the fights already fought).
    private static bool Honouring(World world, Promise p, Fighter f)
    {
        var until = p.Deadline.AddDays(GraceDays);
        int count = 0;
        foreach (var ev in world.Events.Values)
        {
            if (ev.Promotion != p.Promotion || ev.Date <= p.Made || ev.Date > until) continue;
            bool done = ev.Status == EventStatus.Completed;
            if (!done && ev.Status != EventStatus.Scheduled) continue;
            foreach (var x in ev.Card)
            {
                if (x.RedCorner != f.Id && x.BlueCorner != f.Id) continue;
                if (done && x.Outcome is null) continue;
                if (Qualifies(world, p, f, ev, x)) count++;
            }
        }
        return count >= Need(p);
    }

    private static int Need(Promise p) => p.Kind == PromiseKind.Activity ? Math.Max(1, p.Fights) : 1;

    private static bool Qualifies(World world, Promise p, Fighter f, Event ev, Fight x) => p.Kind switch
    {
        PromiseKind.TitleShot => x.IsTitleFight,
        PromiseKind.HomeShow => Geography.WorldMap.CityOf(world, ev) is { } c && world.Get(c)?.Country == f.Country,
        _ => true,
    };

    private static void Resolve(Promise p, PromiseState state, DateOnly date, string note)
    {
        p.State = state;
        p.Resolved = date;
        p.Note = note;
    }

    private static void Keep(World world, Promise p, Fighter f, Promotion promo, DateOnly date)
    {
        Resolve(p, PromiseState.Kept, date, "kept");
        f.Loyalty = Math.Min(100, f.Loyalty + KeptLoyalty);
        if (promo.Id == world.PlayerPromotion)
            Living.Mail.Post(world, date, MailCategory.FighterRequest, f.FullName,
                $"{f.FullName}: you kept your word",
                Lifers.Gendered(f, $"“You said {What(world, p)}. You kept your word. I won't forget it.” "
                    + $"{f.FullName}'s loyalty to {promo.Abbreviation} is a little deeper for it."),
                aboutFighter: f.Id);
    }

    private static void Break(World world, Promise p, Fighter f, Promotion promo, DateOnly date, string note)
    {
        Resolve(p, PromiseState.Broken, date, note);
        f.Loyalty = Math.Max(0, f.Loyalty - BrokenLoyalty);
        string what = What(world, p);
        if (Notable(world, f))
            Living.News.Post(world, date, NewsCategory.Milestone,
                $"{f.FullName} says {promo.Abbreviation} broke its word",
                Lifers.Gendered(f, $"{f.FullName} was promised {what} when he signed with {promo.Name}. It never came."),
                promo.Id, f.Id);
        if (promo.Id == world.PlayerPromotion)
            Living.Mail.Post(world, date, MailCategory.BoardNote, Living.Mail.Owner(world),
                $"We broke our word to {f.FullName}",
                Lifers.Gendered(f, $"We promised {f.FullName} {what} by {p.Deadline:d MMMM yyyy}, and it never came. "
                    + $"He's taken it hard: his loyalty is down, and for the next year it's a grievance he holds against us "
                    + $"(a promise kept would help mend it). The next time we sit down with him he'll ask more, have less patience, "
                    + $"and won't take our word for anything for two years."),
                aboutFighter: f.Id);
    }

    private static void Warn(World world, Promise p, Fighter f, Promotion promo, DateOnly date)
    {
        p.Warned = true;
        if (promo.Id != world.PlayerPromotion) return;
        int days = Math.Max(0, p.Deadline.DayNumber - date.DayNumber);
        string left = days >= 45 ? $"{(days + 15) / 30} months left" : days >= 14 ? $"{days / 7} weeks left" : $"{days} days left";
        string fix = p.Kind switch
        {
            PromiseKind.TitleShot => "Book him a title fight",
            PromiseKind.HomeShow => "Book him on a card in his home country",
            _ => $"Book him — he has {Fought(world, p, f)} of the {p.Fights} fights",
        };
        bool lastFight = f.Contract is { FightsRemaining: <= 1 };
        Living.Mail.Post(world, date, MailCategory.ContractAlert, "Matchmaking",
            $"We promised {f.FullName} {What(world, p)}: {left}",
            Lifers.Gendered(f, $"We promised {f.FullName} {What(world, p)} by {p.Deadline:d MMMM yyyy}, and {left}. "
                + $"{fix} (a fight booked before the deadline counts if the card is within {GraceDays} days of it)."
                + (lastFight ? " His deal is down to its last fight: if he leaves us without it, the promise is broken." : "")
                + " Break it and he'll take it hard."),
            aboutFighter: f.Id);
    }

    // Activity fights fought so far for a promise.
    private static int Fought(World world, Promise p, Fighter f)
    {
        int n = 0;
        foreach (var ev in world.Events.Values)
        {
            if (ev.Promotion != p.Promotion || ev.Status != EventStatus.Completed || ev.Date <= p.Made) continue;
            foreach (var x in ev.Card)
                if (x.Outcome is not null && (x.RedCorner == f.Id || x.BlueCorner == f.Id)) n++;
        }
        return n;
    }

    private static bool Notable(World world, Fighter f) =>
        f.Designed || f.Intangibles.Popularity.Value >= 45
        || world.Titles.Values.Any(t => t.CurrentChampion == f.Id);

    // --- The read model -------------------------------------------------------------------

    /// <summary>
    /// Every promise on the books for this fighter (open first, then the newest), as the UI
    /// reads it: what, by when, how it stands and how it is going, in words.
    /// </summary>
    public static IReadOnlyList<PromiseStatus> For(World world, FighterId fighter)
    {
        if (world.Promises.Count == 0 || world.Get(fighter) is not { } f) return Array.Empty<PromiseStatus>();
        var today = world.CurrentDate;
        var list = new List<PromiseStatus>();
        foreach (var p in world.Promises
                     .Where(p => p.Fighter == fighter)
                     .OrderBy(p => p.Live ? 0 : 1).ThenByDescending(p => p.Made).ThenBy(p => p.Id))
        {
            string abbr = world.Get(p.Promotion)?.Abbreviation ?? "the promotion";
            int? daysLeft = p.Live ? Math.Max(0, p.Deadline.DayNumber - today.DayNumber) : null;
            bool booked = p.Live && Honouring(world, p, f);
            string progress = p.Kind switch
            {
                PromiseKind.Activity => $"{Fought(world, p, f)} of {p.Fights} fights",
                _ when !p.Live => "",
                _ => booked ? "booked" : "not booked yet",
            };
            string status = p.State switch
            {
                PromiseState.Open => $"Due by {p.Deadline:d MMMM yyyy}" + (daysLeft <= WarnDays ? $" — {daysLeft} days left" : ""),
                PromiseState.Kept => $"Kept ({p.Resolved:d MMM yyyy})",
                PromiseState.Broken => Lifers.Gendered(f, $"Broken ({p.Resolved:d MMM yyyy}): {p.Note}"),
                _ => $"Fell away: {p.Note}",
            };
            list.Add(new PromiseStatus(p.Id, p.Kind, p.Promotion, $"{abbr} promised {What(world, p)}", p.State,
                p.Made, p.Deadline, daysLeft, progress, status,
                AtRisk: p.Live && !booked && daysLeft <= WarnDays));
        }
        return list;
    }

    /// <summary>What a promise of this kind means to him, in words (for the table's options).</summary>
    internal static string WorthWords(World world, Promotion promo, Fighter f, PromiseKind kind, int fights, ContractExpectations exp)
    {
        double pull = kind switch
        {
            PromiseKind.TitleShot => TitlePull(world, promo, f, exp),
            PromiseKind.HomeShow => HomePull(world, promo, f, exp),
            _ => ActivityPull(world, f, fights),
        };
        string thing = What(world, kind, fights, f);
        return Lifers.Gendered(f, pull >= 0.55 ? $"{thing} matters a lot to him"
            : pull >= 0.25 ? $"he'd like {thing}" : $"{thing} means little to him");
    }
}
