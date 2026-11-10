using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Tests;

public class NegotiationTests
{
    private static World World(ulong seed = 7)
    {
        var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });
        new WorldTick().AdvanceWeeks(w, 16);
        return w;
    }

    private static Promotion RichestPromotion(World w) =>
        w.Promotions.Values.OrderByDescending(p => p.Balance.Amount).First();

    // A modest free agent who won't demand a signing bonus (keeps offer tests clean).
    private static Fighter PlainFreeAgent(World w) =>
        w.FreeAgents().First(f => f.Status == FighterStatus.Active
            && FighterAbility.CurrentAbility(f) < 78 && f.Intangibles.Popularity < 55);

    private static ContractOffer Scaled(Fighter f, Promotion p, ContractExpectations e, decimal purseMul, decimal winMul, int round = 0) =>
        new()
        {
            Fighter = f.Id,
            Promotion = p.Id,
            PursePerFight = new Money(e.AskingPurse.Amount * purseMul),
            WinBonus = new Money(e.AskingWinBonus.Amount * winMul),
            Fights = e.DesiredFights,
            Round = round,
        };

    [Fact]
    public void AskingPrices_FollowTheResume_NotJustTheTalent()
    {
        // Same fighter, same ability — the price follows what they've PROVEN.
        // Rookies and losing-record journeymen are the cheap, city-hall-affordable
        // end of the market; the proven winner charges full freight.
        var world = World();
        var val = new FighterValuation();
        var f = world.FreeAgents().First(x => x.Status == FighterStatus.Active
            && FighterAbility.CurrentAbility(x) >= 45 && x.Intangibles.Popularity < 30);

        f.Record.Wins = 12; f.Record.Losses = 3;
        decimal proven = val.Evaluate(world, f).AskingPurse.Amount;

        f.Record.Wins = 0; f.Record.Losses = 0;
        decimal rookie = val.Evaluate(world, f).AskingPurse.Amount;

        f.Record.Wins = 5; f.Record.Losses = 15;
        decimal journeyman = val.Evaluate(world, f).AskingPurse.Amount;

        Assert.True(rookie <= proven * 0.45m, $"an unproven rookie should fight for exposure money ({rookie:C0} vs proven {proven:C0})");
        Assert.True(journeyman <= proven * 0.60m, $"a losing record should slash the ask ({journeyman:C0} vs proven {proven:C0})");
        Assert.True(rookie >= 1_500m, "nobody fights pro for nothing");
    }

    [Fact]
    public void FairOffer_IsAccepted()
    {
        var world = World();
        var negotiator = new Negotiator();
        var val = new FighterValuation();
        var promo = RichestPromotion(world);
        // A FAIR offer needs an ask the org can actually meet: above its purse cap
        // the AI opens below the ask by design (that's the counter path, tested
        // elsewhere). Picking "the first free agent" made this depend on who
        // happened to be on the market that week.
        var f = world.FreeAgents().First(x => x.Status == FighterStatus.Active
            && val.Evaluate(world, x).AskingPurse.Amount <= Negotiator.PurseCap(world, promo));
        var exp = val.Evaluate(world, f);

        var offer = negotiator.AiOpeningOffer(world, promo, f, exp);
        Assert.Equal(NegotiationVerdict.Accept, negotiator.Evaluate(world, f, offer, exp).Verdict);
    }

    [Fact]
    public void Lowball_IsRejected()
    {
        var world = World();
        var negotiator = new Negotiator();
        var val = new FighterValuation();
        var promo = RichestPromotion(world);
        var f = PlainFreeAgent(world);
        var exp = val.Evaluate(world, f);

        var offer = Scaled(f, promo, exp, purseMul: 0.4m, winMul: 0m);
        Assert.Equal(NegotiationVerdict.Reject, negotiator.Evaluate(world, f, offer, exp).Verdict);
    }

    [Fact]
    public void CloseOffer_IsCountered_AndAsksForMore()
    {
        var world = World();
        var negotiator = new Negotiator();
        var val = new FighterValuation();
        var promo = RichestPromotion(world);
        var f = PlainFreeAgent(world);
        var exp = val.Evaluate(world, f);

        // Purse-only offer near the ask: enough to be in range, short of the total ask (which includes win bonus).
        var offer = Scaled(f, promo, exp, purseMul: 1.1m, winMul: 0m);
        var resp = negotiator.Evaluate(world, f, offer, exp);

        Assert.Equal(NegotiationVerdict.Counter, resp.Verdict);
        Assert.NotNull(resp.Counter);
        Assert.Equal(offer.Round + 1, resp.Counter!.Round);
        // The counter demands more total guaranteed money than was offered.
        Assert.True(resp.Counter.PursePerFight.Amount + resp.Counter.WinBonus.Amount
                    > offer.PursePerFight.Amount + offer.WinBonus.Amount);
    }

    [Fact]
    public void NonExclusive_ComesCheaper()
    {
        var world = World();
        var negotiator = new Negotiator();
        var val = new FighterValuation();
        var promo = RichestPromotion(world);
        var f = PlainFreeAgent(world);
        var exp = val.Evaluate(world, f);

        // The same money that only earns a counter on an exclusive deal is enough
        // to sign the fighter non-exclusively — they keep their outside earnings.
        var exclusive = Scaled(f, promo, exp, purseMul: 1.1m, winMul: 0m);
        exclusive.Exclusive = true;
        var nonExclusive = Scaled(f, promo, exp, purseMul: 1.1m, winMul: 0m);
        nonExclusive.Exclusive = false;

        Assert.NotEqual(NegotiationVerdict.Accept, negotiator.Evaluate(world, f, exclusive, exp).Verdict);
        Assert.Equal(NegotiationVerdict.Accept, negotiator.Evaluate(world, f, nonExclusive, exp).Verdict);
    }

    [Fact]
    public void Evaluate_IsDeterministic()
    {
        var world = World();
        var negotiator = new Negotiator();
        var val = new FighterValuation();
        var promo = RichestPromotion(world);
        var f = PlainFreeAgent(world);
        var exp = val.Evaluate(world, f);
        var offer = Scaled(f, promo, exp, 0.85m, 0m);

        Assert.Equal(negotiator.Evaluate(world, f, offer, exp).Verdict,
                     negotiator.Evaluate(world, f, offer, exp).Verdict);
    }

    [Fact]
    public void Release_FreesTheFighter_AndPaysGuaranteedMoney()
    {
        var world = World();
        var f = world.Fighters.Values.First(x =>
            x.Contract is not null && x.Contract.Promotion != world.PlayerPromotion && x.Status == FighterStatus.Active);
        var c = f.Contract!;
        c.FightsRemaining = 3;
        c.Guaranteed = true;
        var promo = world.Get(c.Promotion)!;
        var before = promo.Balance.Amount;
        var expectedOwed = c.PursePerFight.Amount * 3;

        var owed = ContractLifecycle.Release(world, f);

        Assert.Null(f.Contract);
        Assert.Equal(expectedOwed, owed.Amount);
        Assert.Equal(before - expectedOwed, promo.Balance.Amount);
        Assert.All(promo.Divisions, d => Assert.DoesNotContain(f.Id, d.Rankings));
    }

    [Fact]
    public void ChampionsClause_TopsUpAnExpiringChampion()
    {
        var world = World();
        var f = world.Fighters.Values.First(x => x.Contract is not null);
        f.Contract!.HasChampionsClause = true;
        f.Contract.FightsRemaining = 0;

        ContractLifecycle.ApplyChampionsClause(f);
        Assert.True(f.Contract.FightsRemaining >= 3);

        // Without the clause it is left alone.
        var g = world.Fighters.Values.First(x => x.Contract is not null && x.Id != f.Id);
        g.Contract!.HasChampionsClause = false;
        g.Contract.FightsRemaining = 0;
        ContractLifecycle.ApplyChampionsClause(g);
        Assert.Equal(0, g.Contract.FightsRemaining);
    }

    [Fact]
    public void ProcessExpiries_LeavesNoRunOutDealsHanging()
    {
        var world = World();
        // Expire a handful of non-player deals.
        foreach (var f in world.Fighters.Values
                     .Where(x => x.Contract is not null && x.Contract.Promotion != world.PlayerPromotion
                                 && x.Status == FighterStatus.Active)
                     .Take(10))
            f.Contract!.FightsRemaining = 0;

        ContractLifecycle.ProcessExpiries(world, world.CurrentDate);

        bool anyStuck = world.Fighters.Values.Any(x =>
            x.Contract is { FightsRemaining: 0 } && x.Contract.Promotion != world.PlayerPromotion
            && x.Status == FighterStatus.Active);
        Assert.False(anyStuck);
    }

    [Fact]
    public void FreeAgency_RefillsAThinDivision()
    {
        var world = World();
        var promo = world.Promotions.Values
            .Where(p => p.Id != world.PlayerPromotion)
            .OrderByDescending(p => p.Prestige.Value).First();
        var wc = promo.Divisions.First().WeightClass;

        // Release most of that division to free agency so it's clearly thin.
        var roster = world.RosterOf(promo.Id)
            .Where(f => f.WeightClass == wc && f.Status == FighterStatus.Active).ToList();
        foreach (var f in roster.Skip(1)) ContractLifecycle.Release(world, f);

        int before = world.RosterOf(promo.Id).Count(f => f.WeightClass == wc && f.Status != FighterStatus.Retired);
        int signings = FreeAgencyService.RunMonthly(world, world.CurrentDate);
        int after = world.RosterOf(promo.Id).Count(f => f.WeightClass == wc && f.Status != FighterStatus.Retired);

        Assert.True(signings > 0);
        Assert.True(after > before);
    }
}
