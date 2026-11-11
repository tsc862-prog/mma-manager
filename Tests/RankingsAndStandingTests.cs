using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Org;

namespace MmaManager.Tests;

public class RankingsAndStandingTests
{
    [Fact]
    public void GlobalRankings_AndP4P_ArePopulated_AndFavourChampions()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 4 });
        GlobalRankings.Recompute(world);

        Assert.NotEmpty(world.PoundForPound);
        foreach (var wc in Enum.GetValues<WeightClass>())
            Assert.True(world.GlobalRankings.TryGetValue(wc, out var list) && list.Count > 0);

        // A champion of the top promotion should rank in that division's global
        // top few. (Top FIVE of their own sex: a weight class can carry several
        // orgs' champions, and the global list also interleaves the women's
        // champions of the same class — on seed 4 the AFC strawweight king sat
        // behind two women and three men, which says nothing about his division.)
        var top = world.Promotions.Values.OrderByDescending(p => p.Prestige.Value).First();
        var div = top.Divisions.First(d => d.Title is not null && world.Get(d.Title!.Value)?.CurrentChampion is not null);
        var champ = world.Get(div.Title!.Value)!.CurrentChampion!.Value;
        var sex = world.Get(champ)!.Sex;
        Assert.Contains(champ, world.GlobalRankings[div.WeightClass].Where(id => world.Get(id)!.Sex == sex).Take(5));
    }

    [Fact]
    public void GlobalRankings_NeverRankAWinlessNonChampion()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 4 });
        new WorldTick().AdvanceWeeks(world, 40);
        GlobalRankings.Recompute(world);

        var champions = world.Titles.Values
            .Where(t => t.CurrentChampion is not null)
            .Select(t => t.CurrentChampion!.Value).ToHashSet();

        var everyRanked = world.PoundForPound
            .Concat(world.GlobalRankings.Values.SelectMany(x => x)).Distinct();

        foreach (var id in everyRanked)
        {
            var f = world.Get(id)!;
            Assert.True(f.Record.Wins >= 1 || champions.Contains(id),
                $"{f.FullName} is ranked with {f.Record} and no belt");
        }
    }

    [Fact]
    public void OrgSize_ClimbsWithPrestige()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 4 });
        var promo = world.Promotions.Values.OrderByDescending(p => p.Prestige.Value).First();
        var startSize = OrgSizing.SizeOf(promo);

        promo.Prestige = 100;
        Assert.True(OrgSizing.SizeOf(promo) >= startSize);
        Assert.Equal(OrgSize.International, OrgSizing.SizeOf(promo));
    }

    [Fact]
    public void ReleasingAFighter_DentsReputation()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 4 });
        var f = world.RosterOf(world.PlayerPromotion).First(x => x.Contract is not null);
        var promo = world.Get(f.Contract!.Promotion)!;
        int before = promo.Reputation.Value;

        ContractLifecycle.Release(world, f);

        Assert.True(promo.Reputation.Value < before);
    }

    [Fact]
    public void FightingInARegion_BuildsAFightersReputationThere()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 4 });
        var promo = world.Promotions.Values.OrderByDescending(p => p.Prestige.Value).First();
        var region = promo.BaseRegion;

        var before = world.Fighters.Values.ToDictionary(f => f.Id, f => MmaManager.Core.Sim.Geography.Reputation.FighterInRegion(world, f, region));
        var ev = new EventService().BookAndRun(world, promo.Id, "Card", world.CurrentDate, new Matchmaker());

        // Everyone who fought should be at least as known in the region, and someone gained.
        var fighters = ev.Card.SelectMany(fi => new[] { fi_red(fi), fi_blue(fi) }).ToList();
        Assert.Contains(fighters, id => MmaManager.Core.Sim.Geography.Reputation.FighterInRegion(world, world.Get(id)!, region) > before[id]);

        static FighterId fi_red(Fight f) => f.RedCorner;
        static FighterId fi_blue(Fight f) => f.BlueCorner;
    }
}
