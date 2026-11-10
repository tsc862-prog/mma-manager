using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;

namespace MmaManager.Tests;

/// <summary>
/// Every belt carries its own prestige: seeded from the org that created it,
/// then pulled toward the CREDIBILITY of the fighters who contest it — public
/// evidence only (record, streak, standing in the company, fame), judged
/// against what a belt at that stage demands. A storied strap sells its fights
/// harder and mints more fame for the winner.
/// </summary>
public class TitlePrestigeTests
{
    [Fact]
    public void ANewBelt_IsBornWithItsOrgsName()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 4 });
        foreach (var promo in world.Promotions.Values)
            foreach (var tid in promo.Titles)
                Assert.Equal(promo.Prestige.Value, world.Get(tid)!.Prestige.Value);
    }

    [Fact]
    public void AWorthyTitleFight_RestoresATarnishedBelt_Slowly()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 4 });
        var afc = world.Promotions.Values.First(p => p.Abbreviation == "AFC");
        var div = afc.Divisions.First(d => d.Sex == Sex.Male && d.WeightClass == WeightClass.Lightweight);
        var title = world.Get(div.Title!.Value)!;
        var champ = world.Get(title.CurrentChampion!.Value)!;       // designed champion, deep record
        var challenger = world.Get(div.Rankings.First())!;          // the #1 contender

        title.Prestige = 20;   // a tarnished strap, contested by the org's best
        var svc = new EventService();
        var ev = svc.CreateEvent(world, afc.Id, "Title Night", world.CurrentDate,
            new[] { new Matchup(champ.Id, challenger.Id, div.WeightClass, 5, true, title.Id) });
        svc.RunEvent(world, ev);

        Assert.InRange(title.Prestige.Value, 21, 45);   // rises — but a belt's name is its history, not one booking
    }

    [Fact]
    public void AGreenChallenger_IsAStoryAtCityLevel_ButAnInsultToAWorldTitle()
    {
        // The user's example: a 3-0 fighter in a title fight LIFTS a city belt
        // (hot prospect, big night for the hall) but TANKS an international one
        // (the deeper résumé the bigger belt demands just isn't there).
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 4 });
        var afc = world.Promotions.Values.First(p => p.Abbreviation == "AFC");
        var city = world.Promotions.Values.First(p =>
            MmaManager.Core.Sim.Org.OrgSizing.SizeOf(p) == OrgSize.City);
        var afcBelt = world.Get(afc.Titles.First())!;
        var cityBelt = world.Get(city.Titles.First())!;

        var rookie = world.Fighters.Values.First(f => !f.Designed && f.Status == FighterStatus.Active
            && f.Intangibles.Popularity.Value < 30);
        rookie.Record.Wins = 3; rookie.Record.Losses = 0; rookie.Record.CurrentStreak = 3;

        double atCity = TitleCredibility.Of(world, city, null, cityBelt, rookie);
        double atAfc = TitleCredibility.Of(world, afc, null, afcBelt, rookie);

        Assert.True(atCity > atAfc, "the same résumé should read better on a smaller stage");
        Assert.True(atCity > 45, $"3-0 should lift a typical city belt (cred {atCity:0})");
        Assert.True(atAfc < 40, $"3-0 should drag a storied world title down (cred {atAfc:0})");

        // …unless they're the rare CROSSOVER: fame is its own credential.
        rookie.Intangibles.Popularity = 85;
        Assert.True(TitleCredibility.Of(world, afc, null, afcBelt, rookie) >= 75,
            "a famous crossover sells a big belt instead of cheapening it");
    }

    [Fact]
    public void WinningAStoriedBelt_MintsMoreFame_ThanATrinket()
    {
        int GainWithBeltPrestige(int beltPrestige)
        {
            var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 4 });
            var afc = world.Promotions.Values.First(p => p.Abbreviation == "AFC");
            var div = afc.Divisions.First(d => d.Sex == Sex.Male && d.WeightClass == WeightClass.Lightweight);
            var title = world.Get(div.Title!.Value)!;
            title.Prestige = beltPrestige;
            var champ = world.Get(title.CurrentChampion!.Value)!;
            var challenger = world.Get(div.Rankings.First())!;
            // Pre-history hands the champ a real reign streak — at 85 pop the win
            // multiplier saturates the 100 cap in BOTH branches. Start lower so
            // the belt's share of the gain is measurable.
            champ.Intangibles.Popularity = 40;
            challenger.Intangibles.Popularity = 40;
            int champBefore = champ.Intangibles.Popularity.Value;
            int chalBefore = challenger.Intangibles.Popularity.Value;

            var svc = new EventService();
            var ev = svc.CreateEvent(world, afc.Id, "Title Night", world.CurrentDate,
                new[] { new Matchup(champ.Id, challenger.Id, div.WeightClass, 5, true, title.Id) });
            svc.RunEvent(world, ev);

            var winner = world.Get(ev.Card[^1].Outcome!.Winner!.Value)!;
            return winner.Intangibles.Popularity.Value - (winner == champ ? champBefore : chalBefore);
        }

        // Same fight, same seed — only the belt's name differs.
        Assert.True(GainWithBeltPrestige(90) > GainWithBeltPrestige(10),
            "taking a storied strap should make a bigger star than taking a trinket");
    }
}
