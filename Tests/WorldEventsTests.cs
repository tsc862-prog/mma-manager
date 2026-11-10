using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Tests;

/// <summary>
/// The macro living world: crossover stars arrive and new promotions launch on a
/// deterministic calendar — the first beats authored, later ones keeping the
/// world evolving. Idempotent, so nothing double-fires.
/// </summary>
public class WorldEventsTests
{
    private static SimRandom Rng => new(1234);

    [Fact]
    public void ACrossoverStar_Arrives_ARealDraw_UnprovenAndSignedByABigOrg()
    {
        // Take over a small org so the giants are AI (a crossover signs with them).
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5, PlayerPromotionIndex = 8 });
        int before = world.Fighters.Count;

        // The first crossover is scheduled for month 6 (July 2026).
        WorldEvents.RunMonthly(world, new DateOnly(2026, 7, 1), Rng);

        var star = world.Fighters.Values.FirstOrDefault(f => f.Accolades.Any(a => a.Title.Contains("crossover")));
        Assert.NotNull(star);
        Assert.True(world.Fighters.Count > before);
        // A real draw the moment they sign — a boost, not an automatic #1: comfortably
        // above an average newcomer, but shy of the sport's biggest homegrown stars (~79-85).
        Assert.InRange(star!.Intangibles.Popularity.Value, 45, 78);
        Assert.Equal(0, star.Record.TotalBouts);                      // unproven in MMA
        Assert.NotNull(star.Contract);                               // a big org snapped them up
        Assert.NotEqual(world.PlayerPromotion, star.Contract!.Promotion);
        Assert.Contains(world.News, n => n.Headline.Contains($"{star.FirstName} {star.LastName}"));
    }

    [Fact]
    public void Crossovers_FireOnSchedule_AndOnlyOnce()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5, PlayerPromotionIndex = 8 });

        WorldEvents.RunMonthly(world, new DateOnly(2026, 5, 1), Rng);   // before the first — nothing
        Assert.DoesNotContain(world.Fighters.Values, f => f.Accolades.Any(a => a.Title.Contains("crossover")));

        WorldEvents.RunMonthly(world, new DateOnly(2026, 7, 1), Rng);   // month 6 — one arrives
        int after = world.Fighters.Values.Count(f => f.Accolades.Any(a => a.Title.Contains("crossover")));
        Assert.Equal(1, after);

        WorldEvents.RunMonthly(world, new DateOnly(2026, 7, 1), Rng);   // same month again — idempotent
        Assert.Equal(after, world.Fighters.Values.Count(f => f.Accolades.Any(a => a.Title.Contains("crossover"))));

        WorldEvents.RunMonthly(world, new DateOnly(2028, 1, 1), Rng);   // +18 months — a second, different star
        Assert.Equal(2, world.Fighters.Values.Count(f => f.Accolades.Any(a => a.Title.Contains("crossover"))));
    }

    [Fact]
    public void ANewPromotion_Launches_StockedFromFreeAgents_WithBelts()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5 });
        int orgsBefore = world.Promotions.Count;

        WorldEvents.RunMonthly(world, new DateOnly(2027, 3, 1), Rng);   // month 14 — first new org

        Assert.Equal(orgsBefore + 1, world.Promotions.Count);
        var fresh = world.Promotions.Values.OrderByDescending(p => p.Id.Value).First();
        Assert.NotEmpty(world.RosterOf(fresh.Id));
        Assert.NotEmpty(fresh.Divisions);
        Assert.All(fresh.Divisions, d => Assert.NotNull(d.Title));
        Assert.Contains(world.News, n => n.Headline.Contains(fresh.Name));
    }

    [Fact]
    public void OverAConvincingRun_TheWorldGrows_WithNewOrgsAndCrossovers()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9, PlayerPromotionIndex = 8 });
        int orgsBefore = world.Promotions.Count;

        new WorldTick().AdvanceWeeks(world, 220);   // ~4 years

        Assert.True(world.Promotions.Count > orgsBefore, "new promotions should have launched");
        Assert.Contains(world.Fighters.Values, f => f.Accolades.Any(a => a.Title.Contains("crossover")));
        // Everyone still fights only their own sex — the new orgs don't break that.
        foreach (var ev in world.Events.Values.Where(e => e.Status == EventStatus.Completed))
            foreach (var fight in ev.Card)
                if (world.Get(fight.RedCorner) is { } r && world.Get(fight.BlueCorner) is { } b)
                    Assert.Equal(r.Sex, b.Sex);
    }

    [Fact]
    public void TheDeeperLadder_HasNineEstablishedOrgs_AcrossManyRegions()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5 });
        var established = world.Promotions.Values.Where(p => !p.IsPlayerControlled || true).ToList();

        Assert.True(world.Promotions.Count >= 9, $"expected a deep ladder, got {world.Promotions.Count}");
        // The scene spans at least five regions now.
        int regions = world.Promotions.Values.Select(p => p.BaseRegion).Distinct().Count();
        Assert.True(regions >= 5, $"only {regions} regions have a promotion");
    }
}
