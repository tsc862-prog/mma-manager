using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Tests;

public class WorldTickTests
{
    [Fact]
    public void WorldBuilder_TiersTalentByPrestige()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 1 });
        var promos = world.Promotions.Values.OrderByDescending(p => p.Prestige.Value).ToList();

        double TopAvg(Promotion p) => world.RosterOf(p.Id).Average(FighterAbility.CurrentAbility);

        // The biggest promotion should, on average, hold better fighters than the smallest.
        Assert.True(TopAvg(promos.First()) > TopAvg(promos.Last()) + 3,
            $"talent not tiered: {TopAvg(promos.First()):0.0} vs {TopAvg(promos.Last()):0.0}");
        Assert.True(world.FreeAgents().Any(), "expected a free-agent pool");
    }

    [Fact]
    public void AdvanceWeeks_RunsEventsAndKeepsPopulationBounded()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 3 });
        int startActive = world.Fighters.Values.Count(f => f.Status != FighterStatus.Retired);

        var report = new WorldTick().AdvanceWeeks(world, 104);   // two years

        Assert.True(report.EventsRun > 50, $"too few events: {report.EventsRun}");
        Assert.True(report.Fights > 0 && report.Newgens > 0 && report.Retired > 0);

        int endActive = world.Fighters.Values.Count(f => f.Status != FighterStatus.Retired);
        // Self-balancing intake: population should neither collapse nor explode.
        Assert.InRange(endActive, startActive / 2, startActive * 2);
    }

    [Fact]
    public void AdvanceWeeks_IsDeterministic()
    {
        var a = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 42 });
        var b = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 42 });

        new WorldTick().AdvanceWeeks(a, 52);
        new WorldTick().AdvanceWeeks(b, 52);

        Assert.Equal(a.CurrentDate, b.CurrentDate);
        Assert.Equal(a.Fighters.Count, b.Fighters.Count);

        // Champions across the top promotion should match exactly.
        var pa = a.Promotions.Values.OrderByDescending(p => p.Prestige.Value).First();
        var pb = b.Promotions.Values.OrderByDescending(p => p.Prestige.Value).First();
        var champsA = pa.Divisions.Select(d => a.Get(d.Title!.Value)!.CurrentChampion).ToList();
        var champsB = pb.Divisions.Select(d => b.Get(d.Title!.Value)!.CurrentChampion).ToList();
        Assert.Equal(champsA, champsB);
    }

    [Fact]
    public void Lifecycle_HealsFightersWhoseLayoffIsOver()
    {
        var world = new LeagueBuilder().Build(new LeagueBuilder.Options { RosterSize = 30 });
        var f = world.Fighters.Values.First();
        f.Status = FighterStatus.Injured;
        f.CurrentInjury = new Injury
        {
            Severity = InjurySeverity.Moderate,
            Description = "broken hand",
            OccurredOn = world.CurrentDate,
            ExpectedReturn = world.CurrentDate.AddDays(30),
        };

        Assert.Equal(0, Lifecycle.HealReturning(world, world.CurrentDate.AddDays(10)));  // still out
        Assert.Equal(FighterStatus.Injured, f.Status);

        Assert.Equal(1, Lifecycle.HealReturning(world, world.CurrentDate.AddDays(40)));  // back now
        Assert.Equal(FighterStatus.Active, f.Status);
        Assert.Null(f.CurrentInjury);
    }

    [Fact]
    public void Retire_StripsRankingsAndHandsOffTheBelt()
    {
        var world = new LeagueBuilder().Build(new LeagueBuilder.Options { Seed = 8 });
        var promo = world.Get(world.PlayerPromotion)!;
        var div = promo.Divisions.First(d => d.Rankings.Count > 0);
        var title = world.Get(div.Title!.Value)!;

        var champ = world.Get(title.CurrentChampion!.Value)!;
        var heirId = div.Rankings[0];

        Lifecycle.Retire(world, champ, world.CurrentDate);

        Assert.Equal(FighterStatus.Retired, champ.Status);
        Assert.Null(champ.Contract);
        Assert.Equal(heirId, title.CurrentChampion);              // belt handed to the top contender
        Assert.DoesNotContain(heirId, div.Rankings);              // who leaves the contender list
        Assert.All(promo.Divisions, d => Assert.DoesNotContain(champ.Id, d.Rankings));
    }
}
