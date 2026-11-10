using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Tests;

public class StorylinesTests
{
    [Fact]
    public void RunningASeason_ProducesLivelyNews()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 2 });
        new WorldTick().AdvanceWeeks(world, 78);   // 18 months

        // A busy year and a half should throw off awards and the odd storyline.
        Assert.Contains(world.News, n => n.Category == NewsCategory.Award);
        Assert.Contains(world.News, n => n.Category is NewsCategory.Milestone or NewsCategory.Debut);
        // At least one rivalry should have formed somewhere.
        Assert.NotEmpty(world.Rivalries);
    }

    [Fact]
    public void WinLossStreak_IsTracked()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 2 });
        new WorldTick().AdvanceWeeks(world, 52);

        // Somebody, somewhere, should be on a multi-fight run in either direction.
        Assert.Contains(world.Fighters.Values, f => f.Record.CurrentStreak >= 3);
        Assert.Contains(world.Fighters.Values, f => f.Record.CurrentStreak <= -2);
    }

    [Fact]
    public void ArrangedRivals_GetAGateBoost()
    {
        // Designed roster off (isolates the grudge boost). Use the SECOND-biggest org,
        // not the summit — the top promotion sells its building out on prestige alone,
        // capping the house so the boost can't show.
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 2, UseDesignedRoster = false });
        var promo = world.Promotions.Values.OrderByDescending(p => p.Prestige.Value).Skip(1).First();
        var wc = WeightClass.Lightweight;
        var two = world.RosterOf(promo.Id).Where(f => f.WeightClass == wc && f.Status == FighterStatus.Active).Take(2).ToList();

        // Baseline card headlined by the pair.
        var baseline = RunHeadlinedBy(world, promo, two[0], two[1], wc);

        // Now give them bad blood and run the identical main event on a fresh world.
        var world2 = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 2, UseDesignedRoster = false });
        var promo2 = world2.Promotions.Values.OrderByDescending(p => p.Prestige.Value).Skip(1).First();
        var two2 = world2.RosterOf(promo2.Id).Where(f => f.WeightClass == wc && f.Status == FighterStatus.Active).Take(2).ToList();
        world2.Rivalries.Add(new Rivalry { A = two2[0].Id, B = two2[1].Id, Intensity = 70, Since = world2.CurrentDate });
        var grudge = RunHeadlinedBy(world2, promo2, two2[0], two2[1], wc);

        Assert.True(grudge.Financials!.Attendance > baseline.Financials!.Attendance);
    }

    [Fact]
    public void SuspendedFighter_ReturnsWhenTheBanEnds()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 2 });
        var f = world.Fighters.Values.First(x => x.Status == FighterStatus.Active);
        f.Status = FighterStatus.Suspended;
        f.SuspendedUntil = world.CurrentDate.AddDays(30);

        Assert.Equal(0, Lifecycle.HealReturning(world, world.CurrentDate.AddDays(10)));
        Assert.Equal(FighterStatus.Suspended, f.Status);

        Assert.Equal(1, Lifecycle.HealReturning(world, world.CurrentDate.AddDays(40)));
        Assert.Equal(FighterStatus.Active, f.Status);
        Assert.Null(f.SuspendedUntil);
    }

    private static Event RunHeadlinedBy(World world, Promotion promo, Fighter a, Fighter b, WeightClass wc)
    {
        var svc = new EventService();
        var ev = svc.CreateEvent(world, promo.Id, "Main", world.CurrentDate,
            new[] { new Matchup(a.Id, b.Id, wc, 5, false, null) });
        svc.RunEvent(world, ev);
        return ev;
    }
}
