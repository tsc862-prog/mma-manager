using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Tests;

public class ContractValuationTests
{
    private static World SeasonedWorld(ulong seed = 42)
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });
        new WorldTick().AdvanceWeeks(world, 20);   // give fighters records/momentum
        return world;
    }

    [Fact]
    public void Evaluate_IsDeterministic()
    {
        var world = SeasonedWorld();
        var val = new FighterValuation();
        var f = world.Fighters.Values.First(x => x.Status == FighterStatus.Active);

        Assert.Equal(val.Evaluate(world, f), val.Evaluate(world, f));
    }

    [Fact]
    public void MoreAbleFighter_AsksForMoreAndIsWorthMore()
    {
        var world = SeasonedWorld();
        var val = new FighterValuation();
        var ranked = world.Fighters.Values
            .Where(f => f.Status == FighterStatus.Active)
            .OrderByDescending(FighterAbility.CurrentAbility)
            .ToList();
        var top = val.Evaluate(world, ranked.First());
        var bottom = val.Evaluate(world, ranked.Last());

        Assert.True(top.MarketValue > bottom.MarketValue);
        Assert.True(top.AskingPurse.Amount > bottom.AskingPurse.Amount);
    }

    [Fact]
    public void Champion_CommandsAPremiumOverAJourneyman()
    {
        var world = SeasonedWorld();
        var val = new FighterValuation();
        var title = world.Titles.Values.First(t => t.CurrentChampion is not null);
        var champ = world.Get(title.CurrentChampion!.Value)!;

        // Cheapest active fighter in the champion's division.
        var journeyman = world.Fighters.Values
            .Where(f => f.Status == FighterStatus.Active && f.WeightClass == champ.WeightClass && f.Id != champ.Id)
            .OrderBy(FighterAbility.CurrentAbility)
            .First();

        Assert.True(val.Evaluate(world, champ).AskingPurse.Amount
                    > val.Evaluate(world, journeyman).AskingPurse.Amount);
    }

    [Fact]
    public void Expectations_StayWithinSaneBounds()
    {
        var world = SeasonedWorld();
        var val = new FighterValuation();

        foreach (var f in world.Fighters.Values.Where(f => f.Status != FighterStatus.Retired))
        {
            var e = val.Evaluate(world, f);
            Assert.InRange(e.MarketValue, 0, 100);
            Assert.True(e.AskingPurse.Amount > 0);
            Assert.True(e.AskingWinBonus.Amount >= 0);
            Assert.InRange(e.DesiredFights, 3, 5);
            Assert.InRange(e.Priorities.Money, 0, 100);
            Assert.InRange(e.Priorities.TitleOpportunity, 0, 100);
            Assert.InRange(e.Priorities.Loyalty, 0, 100);
            Assert.InRange(e.Priorities.HomeRegion, 0, 100);
        }
    }
}
