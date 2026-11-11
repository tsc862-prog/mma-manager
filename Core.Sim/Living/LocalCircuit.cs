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
/// </summary>
public static class LocalCircuit
{
    private const double MonthlyChance = 0.30;   // an idle fighter's odds of taking a club fight this month
    private const double ProVsProChance = 0.25;  // occasionally two idle pros in one region get matched
    private const int IdleDays = 180;
    private const int MaxBoutsPerCard = 6;

    private static readonly FightEngine Engine = new();

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

        var ev = new Event
        {
            Id = world.NewEventId(),
            Promotion = default,   // nobody's show — the scene itself (resolves to null org everywhere)
            Name = $"Local Fight Night — {city?.Name ?? Pretty(region)}",
            Date = new DateOnly(date.Year, date.Month, Math.Min(28, 6 + (int)(SimRandom.Mix(world.RngSeed, (ulong)date.DayNumber) % 20))),
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

        world.Events[ev.Id] = ev;

        // Fight night, small-hall edition: the real engine, the real consequences —
        // records, streaks, fame, discoveries — but no box office and no standing.
        foreach (var fight in ev.Card)
        {
            Engine.Simulate(fight, world, new FightParams { ScheduledRounds = 3, RoundSeconds = 300, GeneratePlayByPlay = false });   // nobody reads club-show commentary
            ResultsProcessor.Apply(world, ev, fight);
            AfterBout(world, ev, fight);
        }
        ev.Status = EventStatus.Completed;
        return ev.Card.Count;
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
