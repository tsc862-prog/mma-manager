using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Engine;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Living;

/// <summary>
/// The regional scene beneath the promotions, staged as REAL events. Fighters
/// nobody is booking — free agents, and signed fighters on NON-exclusive deals
/// who haven't fought in six months — get booked onto small "Local Fight Night"
/// cards in their home region: no promotion, a rented hall, no box office.
/// Most bouts put a pro against an unnamed local; now and then two idle pros
/// in the same region get matched with each other. Every bout is simulated by
/// the real engine and lands on the real record, the real fight history, and
/// the real calendar — a fighter's tally is never a number without a story.
/// Locals can still be DISCOVERED here by upsetting a pro (see LocalScene).
///
/// A month's cards are STAGED in the monthly pass (every roll the circuit
/// makes — who takes a fight, pro-vs-pro, the locals' styles — is made then,
/// on the week's shared stream, in the order it always was) and dated later in
/// the month. They sit on the calendar as scheduled cards, so the fighters on
/// them are committed (no promotion books them meanwhile), and run when the
/// world's clock reaches their date (<see cref="RunDue"/>, which draws no RNG:
/// the bouts seed off their fight ids). On the night anyone who can no longer
/// fight it — retired, suspended, hurt, signed to an exclusive deal, booked on
/// a promotion's card — comes off it; the pros who fought get a short layoff
/// (<see cref="LocalRecoveryDays"/>). A staged club card does NOT commit its
/// fighters: it loses to any promotion that books one of them (<see cref="YieldTo"/>).
/// </summary>
public static class LocalCircuit
{
    private const double MonthlyChance = 0.30;   // an idle fighter's odds of taking a club fight this month
    private const double ProVsProChance = 0.25;  // occasionally two idle pros in one region get matched
    private const int IdleDays = 180;
    private const int MaxBoutsPerCard = 6;

    private static readonly FightEngine Engine = new();

    /// <summary>A card of the local scene (nobody's show), not a promotion's.</summary>
    public static bool IsLocalCard(Event ev) => ev.Promotion.Value == 0;

    /// <summary>Stage this month's club cards (they run on their dates). Returns the bouts staged.</summary>
    public static int RunMonthly(World world, DateOnly date, SimRandom rng)
    {
        // Anyone who fought recently, or has a booking coming, isn't idle —
        // and local cards themselves now count, so the circuit self-paces.
        var busy = new HashSet<FighterId>();
        foreach (var e in world.Events.Values)
        {
            bool recent = e.Status == EventStatus.Completed && e.Date >= date.AddDays(-IdleDays);
            bool upcoming = e.Status == EventStatus.Scheduled;
            if (!recent && !upcoming) continue;
            foreach (var fight in e.Card) { busy.Add(fight.RedCorner); busy.Add(fight.BlueCorner); }
        }

        var takers = new List<Fighter>();
        foreach (var f in world.Fighters.Values.OrderBy(f => f.Id.Value))
        {
            if (!f.IsMatchReady(date) || f.LocalTalent) continue;
            bool freeAgent = f.Contract is null;
            bool moonlighting = f.Contract is { IsExclusive: false };
            if ((!freeAgent && !moonlighting) || busy.Contains(f.Id)) continue;
            if (!rng.Chance(MonthlyChance)) continue;
            takers.Add(f);
        }
        if (takers.Count == 0) return 0;

        int bouts = 0;
        foreach (var regionGroup in takers.GroupBy(f => RegionOf(world, f)).OrderBy(g => g.Key))
            bouts += StageCard(world, regionGroup.Key, regionGroup.ToList(), date, rng);
        return bouts;
    }

    private static Region RegionOf(World world, Fighter f) =>
        f.HomeCity is { } hc && world.Get(hc) is { } city ? city.Region : f.Nationality;

    /// <summary>One local card for a region's idle fighters this month.</summary>
    private static int StageCard(World world, Region region, List<Fighter> pros, DateOnly date, SimRandom rng)
    {
        var city = Geography.WorldMap.AnchorCity(world, region);
        var venue = city is null ? null
            : world.Venues.Values.Where(v => v.City == city.Id).OrderBy(v => v.Capacity).ThenBy(v => v.Id.Value).FirstOrDefault();

        // Later this month (a hash of the date, not a draw) — never before today.
        var night = new DateOnly(date.Year, date.Month, Math.Min(28, 6 + (int)(SimRandom.Mix(world.RngSeed, (ulong)date.DayNumber) % 20)));
        if (night < date) night = date;
        var ev = new Event
        {
            Id = world.NewEventId(),
            Promotion = default,   // nobody's show — the scene itself (resolves to null org everywhere)
            Name = $"Local Fight Night — {city?.Name ?? Pretty(region)}",
            Date = night,
            Venue = venue?.Name,
            VenueId = venue?.Id,
            Region = region,
            Status = EventStatus.Scheduled,
        };

        // The card: mostly pro-vs-local; occasionally two idle pros who share a
        // division get matched with each other (the fight both camps actually want).
        var pool = pros.OrderByDescending(FighterAbility.CurrentAbility).ToList();
        var booked = new HashSet<FighterId>();
        foreach (var divisionGroup in pool.GroupBy(f => (f.WeightClass, f.Sex)).OrderBy(g => g.Key.WeightClass).ThenBy(g => g.Key.Sex))
        {
            var mates = divisionGroup.Where(f => !booked.Contains(f.Id)).ToList();
            if (mates.Count >= 2 && rng.Chance(ProVsProChance))
            {
                AddBout(world, ev, mates[0], mates[1]);
                booked.Add(mates[0].Id);
                booked.Add(mates[1].Id);
            }
        }
        foreach (var f in pool)
        {
            if (ev.Card.Count >= MaxBoutsPerCard) break;   // the rest wait for next month's card
            if (booked.Contains(f.Id)) continue;
            var style = LocalScene.OfferedStyles[rng.Range(0, LocalScene.OfferedStyles.Length - 1)];
            var local = LocalScene.CreateOpponent(world, null, ev, style, f.WeightClass, f.Sex);
            AddBout(world, ev, f, local);
            booked.Add(f.Id);
        }
        if (ev.Card.Count == 0) return 0;

        world.Events[ev.Id] = ev;   // on the calendar; it runs when the clock reaches its date
        return ev.Card.Count;
    }

    /// <summary>
    /// Run every staged club card whose date has arrived, in date order. Draws
    /// no RNG. Returns the bouts fought.
    /// </summary>
    public static int RunDue(World world, DateOnly asOf)
    {
        int fought = 0;
        var due = world.Events.Values
            .Where(e => e.Status == EventStatus.Scheduled && IsLocalCard(e) && e.Date <= asOf)
            .OrderBy(e => e.Date).ThenBy(e => e.Id.Value)
            .ToList();
        if (due.Count == 0) return 0;
        var promoted = OnPromotionCards(world);
        foreach (var ev in due) fought += RunCard(world, ev, promoted);
        return fought;
    }

    /// <summary>
    /// Keep every staged club card honest between staging and its night (run at
    /// the end of each tick): a corner who has retired, been suspended, been
    /// hurt, signed an exclusive deal or been booked on a promotion's card
    /// comes off it. Draws no RNG.
    /// </summary>
    public static void Audit(World world)
    {
        var staged = world.Events.Values
            .Where(e => e.Status == EventStatus.Scheduled && IsLocalCard(e))
            .OrderBy(e => e.Date).ThenBy(e => e.Id.Value)
            .ToList();
        if (staged.Count == 0) return;
        var promoted = OnPromotionCards(world);
        foreach (var ev in staged)
        {
            Prune(world, ev, promoted);
            if (ev.Card.Count == 0) world.Events.Remove(ev.Id);   // fell through entirely: the hall is never booked
        }
    }

    /// <summary>
    /// A promotion has just booked these fighters (or one of them signed an
    /// exclusive deal): a club card loses to a promotion, so each comes off any
    /// staged local card at once — the bout is dropped and an unfought local
    /// goes back to the scene. Draws no RNG. It removes nothing from the
    /// world's collections (callers may be enumerating them): the local is
    /// retired, as a local is after any bout, and a card left empty is removed
    /// by the end-of-tick <see cref="Audit"/> (or on its night) and charged as
    /// nobody's clash meanwhile.
    /// </summary>
    public static void YieldTo(World world, IEnumerable<FighterId> fighters)
    {
        List<Event>? staged = null;
        foreach (var e in world.Events.Values)
            if (e.Status == EventStatus.Scheduled && IsLocalCard(e)) (staged ??= new()).Add(e);
        if (staged is null) return;
        var taken = fighters.ToHashSet();
        foreach (var ev in staged)
        {
            foreach (var fight in ev.Card.Where(f => taken.Contains(f.RedCorner) || taken.Contains(f.BlueCorner)).ToList())
            {
                ev.Card.Remove(fight);
                foreach (var id in new[] { fight.RedCorner, fight.BlueCorner })
                    if (world.Get(id) is { LocalTalent: true } local && local.BoutHistory.Count == 0)
                        local.Status = FighterStatus.Retired;
            }
        }
    }

    /// <summary>A promotion has just booked this card's corners: they come off any staged club card.</summary>
    public static void YieldTo(World world, IEnumerable<Fight> bouts) =>
        YieldTo(world, bouts.SelectMany(f => new[] { f.RedCorner, f.BlueCorner }));

    /// <summary>
    /// The layoff after a club fight: a week (two if he was knocked out) — a
    /// short, low-level bout keeps nobody from fighting twice in a week, and
    /// doesn't take a free agent off the promotions' short-notice market for a month.
    /// </summary>
    public const int LocalRecoveryDays = 7;

    // Fight night, small-hall edition: the real engine, the real consequences —
    // records, streaks, fame, discoveries, the post-fight layoff — but no box
    // office, no standing and no injury roll (that lives on the shared stream).
    private static int RunCard(World world, Event ev, HashSet<FighterId> promoted)
    {
        Prune(world, ev, promoted);
        if (ev.Card.Count == 0) { world.Events.Remove(ev.Id); return 0; }   // nothing left to stage: the hall is never booked
        foreach (var fight in ev.Card)
        {
            Engine.Simulate(fight, world, new FightParams { ScheduledRounds = 3, RoundSeconds = 300, GeneratePlayByPlay = false });   // nobody reads club-show commentary
            ResultsProcessor.Apply(world, ev, fight);
            if (fight.Outcome is { } o)
                foreach (var id in new[] { fight.RedCorner, fight.BlueCorner })
                    if (world.Get(id) is { LocalTalent: false, Status: FighterStatus.Active } pro)
                    {
                        bool koed = o.Loser == id && o.Method is FightMethod.Knockout or FightMethod.TechnicalKnockout;
                        var ready = ev.Date.AddDays(koed ? 2 * LocalRecoveryDays : LocalRecoveryDays);
                        if (pro.ReadyToFightOn is not { } r || r < ready) pro.ReadyToFightOn = ready;
                    }
            AfterBout(world, ev, fight);
        }
        ev.Status = EventStatus.Completed;
        return ev.Card.Count;
    }

    // Everyone booked on a promotion's scheduled card (the player's included).
    private static HashSet<FighterId> OnPromotionCards(World world)
    {
        var set = new HashSet<FighterId>();
        foreach (var e in world.Events.Values)
        {
            if (e.Status != EventStatus.Scheduled || IsLocalCard(e)) continue;
            foreach (var f in e.Card) { set.Add(f.RedCorner); set.Add(f.BlueCorner); }
        }
        return set;
    }

    // Take off the card every bout a pro can no longer fight on its night.
    private static void Prune(World world, Event ev, HashSet<FighterId> promoted)
    {
        foreach (var fight in ev.Card.ToList())
        {
            bool off = false;
            foreach (var id in new[] { fight.RedCorner, fight.BlueCorner })
            {
                if (world.Get(id) is not { } f) { off = true; break; }
                if (f.LocalTalent) continue;
                if (!f.IsMatchReady(ev.Date)                                        // retired, suspended, hurt or still recovering
                    || f.Contract is { IsExclusive: true }                          // signed since: his promotion's fights only
                    || promoted.Contains(id))                                       // on a promotion's card now
                { off = true; break; }
            }
            if (!off) continue;
            ev.Card.Remove(fight);
            PlayerBooking.ReleaseLocals(world, fight);   // an unfought local goes back to the scene
        }
    }

    private static void AddBout(World world, Event ev, Fighter red, Fighter blue) =>
        ev.Card.Add(new Fight
        {
            Id = world.NewFightId(),
            RedCorner = red.Id,
            BlueCorner = blue.Id,
            WeightClass = red.WeightClass,
            ScheduledRounds = 3,
        });

    // The circuit's own beats: a run of club wins gets a fighter noticed.
    private static void AfterBout(World world, Event ev, Fight fight)
    {
        if (fight.Outcome?.Winner is not { } winnerId) return;
        if (world.Get(winnerId) is not { } w || w.LocalTalent) return;
        if (w.Record.CurrentStreak == 3)
            News.Post(world, ev.Date, NewsCategory.Debut,
                $"{w.FullName} is tearing up the local circuit",
                "Three straight wins on the regional scene — promoters are taking notice.", default, w.Id);
    }


    private static string Pretty(Region r) => r switch
    {
        Region.NorthAmerica => "North America",
        Region.SouthAmerica => "South America",
        Region.MiddleEast => "Middle East",
        _ => r.ToString(),
    };
}
