using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Flavor;

namespace MmaManager.Tests;

/// <summary>
/// The house ruleset: round length flows into the engine, round counts drive
/// the matchmaker's formats, and the Fight of the Night bonus pays real money
/// to the card's best fight.
/// </summary>
public class OrgRulesTests
{
    [Fact]
    public void RoundLength_IsTheHousesCall()
    {
        // Same seed, same card — the 3-minute org's decisions end at 3:00.
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 31, UseDesignedRoster = false });
        var promo = world.Promotions.Values.OrderByDescending(p => p.Prestige.Value).First();
        promo.Rules.RoundMinutes = 3;

        var ev = new EventService().BookAndRun(world, promo.Id, "Short Rounds", world.CurrentDate, new Matchmaker());
        var fought = ev.Card.Where(f => f.Outcome is not null).ToList();
        Assert.NotEmpty(fought);
        Assert.All(fought, f => Assert.InRange(f.Outcome!.EndTimeSeconds, 0, 180));
        // Decisions run the full final round: exactly 3:00 under these rules.
        Assert.Contains(fought, f => f.Outcome!.EndRound == f.ScheduledRounds && f.Outcome.EndTimeSeconds == 180);
    }

    [Fact]
    public void TheMatchmaker_BooksTheHousesFormats()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 32 });
        var promo = world.Promotions.Values.OrderByDescending(p => p.Prestige.Value).First();
        promo.Rules.StandardRounds = 2;
        promo.Rules.TitleRounds = 3;

        var card = new Matchmaker().BuildCard(world, promo.Id, targetFights: 6);
        Assert.NotEmpty(card);
        Assert.All(card.SkipLast(1), m => Assert.Equal(2, m.Rounds));
        Assert.Equal(3, card[card.Count - 1].Rounds);   // headliner runs at title length
    }

    [Fact]
    public void FightOfTheNight_PaysBothCorners_AndTheLedgerShowsIt()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 33, UseDesignedRoster = false });
        var promo = world.Promotions.Values.OrderByDescending(p => p.Prestige.Value).First();
        promo.Rules.FightOfNightBonus = new Money(10_000m);

        var ev = new EventService().BookAndRun(world, promo.Id, "Bonus Night", world.CurrentDate, new Matchmaker());
        var best = ev.Card.Where(f => f.Outcome is not null).OrderByDescending(MatchRating.Of).First();
        if (MatchRating.Of(best) < 32) return;   // dud card on this seed — nothing owed

        var bonuses = promo.Ledger.Where(l => l.Description.Contains("Fight of the Night")).ToList();
        Assert.Equal(2, bonuses.Count);
        Assert.All(bonuses, b => Assert.Equal(-10_000m, b.Amount.Amount));
    }

    [Fact]
    public void HouseRules_Roundtrip_ThroughASave()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 34 });
        var promo = world.Get(world.PlayerPromotion)!;
        promo.Rules.RoundMinutes = 3;
        promo.Rules.StandardRounds = 2;
        promo.Rules.TitleRounds = 3;
        promo.Rules.FightOfNightBonus = new Money(7_500m);

        string path = Path.Combine(Path.GetTempPath(), $"mma_rules_{Guid.NewGuid():N}.db");
        try
        {
            MmaManager.Persistence.SaveGame.Save(world, path);
            var loaded = MmaManager.Persistence.SaveGame.Load(path);
            var rules = loaded.Get(loaded.PlayerPromotion)!.Rules;
            Assert.Equal(3, rules.RoundMinutes);
            Assert.Equal(2, rules.StandardRounds);
            Assert.Equal(3, rules.TitleRounds);
            Assert.Equal(7_500m, rules.FightOfNightBonus.Amount);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
