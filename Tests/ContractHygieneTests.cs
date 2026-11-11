using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Org;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Tests;

/// <summary>
/// Contract fog and hygiene: the inbox reads the market, not true ability;
/// startups sign through the market at a payroll their gate can carry; a deal
/// counts down only on its holder's cards; a merger pays the guarantees it
/// tears up; and letting a finished deal go is not a cut.
/// </summary>
public class ContractHygieneTests
{
    private static World Build(ulong seed = 3) =>
        new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });

    private const string Circling = "circling";

    // --- (1) the circling line ---------------------------------------------

    [Fact]
    public void CirclingLine_IsNotWrittenOffTrueAbility_WhenNoRivalWantsHim()
    {
        var world = Build();
        // The player's best fighter by TRUE ability — the old line fired on that alone.
        var f = world.RosterOf(world.PlayerPromotion)
            .Where(x => x.Status == FighterStatus.Active && x.Contract is not null)
            .OrderByDescending(FighterAbility.CurrentAbility).ThenBy(x => x.Id.Value).First();
        f.Contract!.FightsRemaining = 1;
        // No market at all: every rival is gone.
        foreach (var p in world.Promotions.Values.Where(p => p.Id != world.PlayerPromotion)) p.Defunct = true;

        Assert.False(PoachingService.HasSuitor(world, f));
        Mail.MonthlyReview(world, world.CurrentDate);

        var mail = world.Mail.Single(m => m.Category == MailCategory.ContractAlert && m.AboutFighter == f.Id);
        Assert.DoesNotContain(Circling, mail.Body);
    }

    [Fact]
    public void CirclingLine_FollowsARealSuitor()
    {
        var world = Build();
        // Run a small house, so there is a bigger stage to come calling.
        world.PlayerPromotion = world.Promotions.Values
            .Where(p => !p.Defunct && OrgSizing.SizeOf(p) == OrgSize.Regional)
            .OrderBy(p => p.Id.Value).First().Id;
        var f = world.RosterOf(world.PlayerPromotion)
            .Where(x => x.Status == FighterStatus.Active && x.Contract is not null && x.Record.CurrentStreak >= 0)
            .OrderBy(FighterAbility.CurrentAbility).ThenBy(x => x.Id.Value).First();
        f.Contract!.FightsRemaining = 1;
        f.Loyalty = 20;
        f.Intangibles.Popularity = 100;   // a public draw every bigger stage chases

        Assert.True(PoachingService.HasSuitor(world, f));
        Mail.MonthlyReview(world, world.CurrentDate);

        var mail = world.Mail.Single(m => m.Category == MailCategory.ContractAlert && m.AboutFighter == f.Id);
        Assert.Contains(Circling, mail.Body);
    }

    // --- (2) startups sign through the market --------------------------------

    private static Promotion NewStartup(World world, Region region)
    {
        var promo = new Promotion
        {
            Id = world.NewPromotionId(), Name = "Test Startup FC", Abbreviation = "TSF",
            BaseRegion = region, Balance = new Money(200_000m), Prestige = 9, Reputation = 50,
        };
        promo.TierClearance = OrgSizing.SizeOf(promo);
        world.Promotions[promo.Id] = promo;
        return promo;
    }

    private static readonly WeightClass[] Core3 =
        { WeightClass.Lightweight, WeightClass.Welterweight, WeightClass.Middleweight };

    // A fresh crop of affordable free agents: turn the first few in each class
    // into unknown debutants (the cheapest pros on the market), so a small new
    // house has someone it can afford to sign.
    private static void SeedDebutants(World world, int perClass)
    {
        foreach (var wc in Core3)
            foreach (var f in world.FreeAgents().Where(f => f.WeightClass == wc && f.Sex == Sex.Male
                                                            && f.Status == FighterStatus.Active)
                         .OrderBy(f => f.Id.Value).Take(perClass).ToList())
            {
                f.Record.Wins = f.Record.Losses = f.Record.Draws = f.Record.NoContests = 0;
                f.Record.CurrentStreak = 0;
                f.Momentum = 0;
                f.Intangibles.Popularity = 5;
            }
    }

    [Fact]
    public void FoundingCap_IsBelowTheEstablishedCap_ForAHouseWithNoFollowing()
    {
        var world = Build();
        var promo = NewStartup(world, Region.NorthAmerica);
        promo.Prestige = 12;   // a startup big enough to book a real building (the ones that used to die)
        decimal founding = Negotiator.FoundingPurseCap(world, promo);
        Assert.True(founding > 0);
        Assert.True(founding <= Negotiator.PurseCap(world, promo));
        Assert.True(founding < Negotiator.PurseCap(world, promo),
            "a startup's first gates are well under an established house's — its budget must say so");
    }

    [Fact]
    public void Startup_SignsAtAffordablePurses_ThroughTheNormalPath()
    {
        var world = Build();
        var promo = NewStartup(world, Region.NorthAmerica);
        SeedDebutants(world, 8);
        var pool = world.FreeAgents().Where(f => Core3.Contains(f.WeightClass) && f.Sex == Sex.Male
                                                  && f.Status == FighterStatus.Active).ToList();
        decimal cap = Negotiator.FoundingPurseCap(world, promo);

        var signed = WorldEvents.StockStartup(world, promo, promo.BaseRegion, Core3, world.CurrentDate, new SimRandom(11));

        Assert.NotEmpty(signed);
        Assert.All(Core3, wc => Assert.True(signed.Count(f => f.WeightClass == wc) <= WorldEvents.StartupSigningsPerDivision));
        var valuation = new FighterValuation();
        foreach (var f in signed)
        {
            Assert.Equal(promo.Id, f.Contract!.Promotion);
            Assert.True(f.Contract.PursePerFight.Amount <= cap * WorldEvents.StartupAskCeiling,
                $"{f.FullName} paid {f.Contract.PursePerFight}, over twice the founding cap {cap}");
            Assert.Equal(20, f.Loyalty);
        }

        // Nobody on the board was signed at a fraction of a far bigger ask: a
        // fighter asking more than twice what the house can pay stays on the market.
        foreach (var f in pool.Where(f => !signed.Contains(f)))
            Assert.Null(f.Contract);
        var priced = pool.Select(f => (f, ask: Negotiator.EffectiveAskingPurse(world, promo, f, valuation.Evaluate(world, f)).Amount));
        Assert.All(priced.Where(p => p.ask > cap * WorldEvents.StartupAskCeiling),
            p => Assert.DoesNotContain(p.f, signed));
        Assert.Contains(pool, f => f.Contract is null);   // the stars are left for the stages that can pay them

        // Each division's purses average at most one founding cap a head over a
        // full division: a card it books costs what its gate can carry.
        foreach (var wc in Core3)
            Assert.True(signed.Where(f => f.WeightClass == wc).Sum(f => f.Contract!.PursePerFight.Amount)
                        <= cap * WorldEvents.StartupDivisionBudgetCaps);
    }

    [Fact]
    public void Startup_TakesOneSharedDrawPerSigning()
    {
        var world = Build();
        var promo = NewStartup(world, Region.Europe);
        SeedDebutants(world, 8);
        var a = new SimRandom(77);
        var b = new SimRandom(77);

        var signed = WorldEvents.StockStartup(world, promo, promo.BaseRegion, Core3, world.CurrentDate, a);
        Assert.NotEmpty(signed);
        for (int i = 0; i < signed.Count; i++) b.NextULong();

        Assert.Equal(b.NextULong(), a.NextULong());
    }

    [Fact]
    public void Startup_RanksOnItsOwnRead_NotTrueAbility()
    {
        var world = Build();
        var promo = NewStartup(world, Region.NorthAmerica);
        SeedDebutants(world, 8);
        // The signing order follows the house's read (home region first), so a
        // prospect it overrates goes ahead of a better man it underrates.
        var signed = WorldEvents.StockStartup(world, promo, promo.BaseRegion, Core3, world.CurrentDate, new SimRandom(5));
        foreach (var wc in Core3)
        {
            var inClass = signed.Where(f => f.WeightClass == wc).ToList();
            var expected = inClass
                .OrderByDescending(f => f.Nationality == promo.BaseRegion ? 1 : 0)
                .ThenByDescending(f => Core.Sim.Scouting.ScoutingService.PerceivedAbility(world, promo, f))
                .ThenBy(f => f.Id.Value).Select(f => f.Id).ToList();
            Assert.Equal(expected, inClass.Select(f => f.Id).ToList());
        }
    }

    [Fact]
    public void Startup_ThatCannotStaffACard_SignsNobody_AndDrawsNothing()
    {
        var world = Build();
        var promo = NewStartup(world, Region.NorthAmerica);
        // Nobody on the market is cheap enough: every free agent is a proven name.
        foreach (var f in world.FreeAgents()) { f.Record.Wins = 30; f.Intangibles.Popularity = 90; }
        var a = new SimRandom(21);
        var b = new SimRandom(21);

        var signed = WorldEvents.StockStartup(world, promo, promo.BaseRegion, Core3, world.CurrentDate, a);

        Assert.Empty(signed);
        Assert.Empty(world.RosterOf(promo.Id));
        Assert.Equal(b.NextULong(), a.NextULong());
    }

    // --- (3) a deal counts down only on its holder's cards ---------------------

    private static (Fight fight, Event ev) Bout(World world, PromotionId on, Fighter red, Fighter blue)
    {
        var fight = new Fight
        {
            Id = world.NewFightId(), RedCorner = red.Id, BlueCorner = blue.Id, WeightClass = red.WeightClass,
            ScheduledRounds = 3,
            Outcome = new FightOutcome { Winner = red.Id, Loser = blue.Id, Method = FightMethod.UnanimousDecision, EndRound = 3, EndTimeSeconds = 300 },
        };
        var ev = new Event { Id = world.NewEventId(), Promotion = on, Name = "Night", Date = world.CurrentDate };
        ev.Card.Add(fight);
        return (fight, ev);
    }

    [Fact]
    public void LocalCircuitBout_DoesNotBurnTheHomeDeal()
    {
        var world = Build();
        var pair = world.Fighters.Values.Where(f => f.Status == FighterStatus.Active && f.Contract is { FightsRemaining: >= 2 })
            .GroupBy(f => f.WeightClass).First(g => g.Count() >= 2).Take(2).ToList();
        int redLeft = pair[0].Contract!.FightsRemaining, blueLeft = pair[1].Contract!.FightsRemaining;

        var (fight, ev) = Bout(world, default, pair[0], pair[1]);   // nobody's show: the local circuit
        ResultsProcessor.Apply(world, ev, fight);

        Assert.Equal(redLeft, pair[0].Contract!.FightsRemaining);
        Assert.Equal(blueLeft, pair[1].Contract!.FightsRemaining);
    }

    [Fact]
    public void OnlyTheHoldersCard_CountsDown()
    {
        var world = Build();
        var home = world.Fighters.Values.First(f => f.Status == FighterStatus.Active && f.Contract is { FightsRemaining: >= 2 });
        var other = world.Fighters.Values.First(f => f.Status == FighterStatus.Active && f.WeightClass == home.WeightClass
            && f.Contract is { FightsRemaining: >= 2 } c && c.Promotion != home.Contract!.Promotion);
        int homeLeft = home.Contract!.FightsRemaining, otherLeft = other.Contract!.FightsRemaining;

        var (fight, ev) = Bout(world, home.Contract.Promotion, home, other);
        ResultsProcessor.Apply(world, ev, fight);

        Assert.Equal(homeLeft - 1, home.Contract.FightsRemaining);   // his own promotion's card
        Assert.Equal(otherLeft, other.Contract!.FightsRemaining);     // a moonlighter on someone else's
    }

    // --- (4) a merger pays the guarantees it tears up --------------------------

    [Fact]
    public void Acquisition_PaysOutTheGuaranteesOfFightersItCutsLoose()
    {
        var world = Build(5);
        var buyer = world.Promotions.Values.First(p => p.Abbreviation == "AFC");
        var target = world.Promotions.Values.Where(p => !p.Defunct && p.Id != buyer.Id && p.Id != world.PlayerPromotion)
            .OrderByDescending(p => world.RosterOf(p.Id).Count()).ThenBy(p => p.Id.Value).First();
        var deals = world.RosterOf(target.Id).Where(f => f.Contract is not null)
            .ToDictionary(f => f.Id, f => (f.Contract!, f.Status));

        decimal bankBefore = buyer.Balance.Amount, cashIn = Math.Max(0m, target.Balance.Amount);
        var price = new Money(500_000_000m);   // a price that covers every guarantee
        Acquisitions.Execute(world, buyer, target, world.CurrentDate, price);

        var cut = deals.Where(kv => world.Get(kv.Key)!.Contract is null && kv.Value.Status != FighterStatus.Retired).ToList();
        Assert.NotEmpty(cut);
        decimal owed = cut.Sum(kv => ContractLifecycle.GuaranteedOwed(kv.Value.Item1).Amount);
        Assert.True(owed > 0);
        decimal paid = -buyer.Ledger.Where(e => e.Category == FinanceCategory.ContractBuyout && e.Date == world.CurrentDate
                                                && e.Description.Contains($"{target.Abbreviation} merger"))
            .Sum(e => e.Amount.Amount);
        Assert.Equal(owed, paid);
        // Settled out of the price, not on top of it: the buyer's outlay is the price.
        Assert.Equal(bankBefore - price.Amount + cashIn, buyer.Balance.Amount);
    }

    [Fact]
    public void Acquisition_OfAnUnderwaterCompany_SharesThePriceAmongTheCut()
    {
        var world = Build(5);
        var buyer = world.Promotions.Values.First(p => p.Abbreviation == "AFC");
        var target = world.Promotions.Values.Where(p => !p.Defunct && p.Id != buyer.Id && p.Id != world.PlayerPromotion)
            .OrderByDescending(p => world.RosterOf(p.Id).Count()).ThenBy(p => p.Id.Value).First();
        decimal bankBefore = buyer.Balance.Amount, cashIn = Math.Max(0m, target.Balance.Amount);
        var price = new Money(1_000m);

        Acquisitions.Execute(world, buyer, target, world.CurrentDate, price);

        decimal buyouts = -buyer.Ledger.Where(e => e.Category == FinanceCategory.ContractBuyout
                                                   && e.Description.Contains($"{target.Abbreviation} merger"))
            .Sum(e => e.Amount.Amount);
        Assert.InRange(buyouts, 900m, 1_100m);   // the whole price, rounded per head
        Assert.InRange(bankBefore - buyer.Balance.Amount + cashIn, 900m, 1_100m);
    }

    // --- (5) a finished deal lapses without a reputation hit ---------------------

    [Fact]
    public void ReleasingAFighterWithNoFightsLeft_CostsNoReputation()
    {
        var world = Build(4);
        var f = world.RosterOf(world.PlayerPromotion).First(x => x.Contract is not null);
        var promo = world.Get(f.Contract!.Promotion)!;
        f.Contract.FightsRemaining = 0;
        int before = promo.Reputation.Value;

        var owed = ContractLifecycle.Release(world, f);

        Assert.Equal(before, promo.Reputation.Value);
        Assert.Equal(Money.Zero, owed);
        Assert.Null(f.Contract);
    }
}
