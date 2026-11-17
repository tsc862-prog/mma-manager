using MmaManager.Core.Model;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Tests;

/// <summary>
/// The Boom's small houses can still sign men (Origins): an unknown's ask bottoms out at the sport's floor
/// purse, the purse the smallest house can always budget, however rich the biggest house has grown. (Asks
/// used to bottom out at 15% of the richest house's purse cap; once that passed ~4,200 every unknown asked
/// 750 against a small hall's 500, and the small houses of 2009-12 signed nobody and missed their nights.)
/// </summary>
public class OriginsBoomRosterTests
{
    private static Promotion House(World w, string abbr) => w.Promotions.Values.Single(p => p.Abbreviation == abbr);

    [Fact]
    public void AnUnknown_AsksTheFloorPurse_WhenTheRichestHouseHasOutgrownTheSmallOnes()
    {
        var w = OriginsWorldTests.AiWorld(11);
        // A giant: a famous house in a stadium (the Boom's richest purse) ...
        var giant = House(w, "SFA");
        giant.Prestige = 60;
        OriginsShows.HomeVenue(w, giant)!.Capacity = 20_000;
        // ... and a small hall at the floor purse.
        var small = House(w, "DLB");
        small.Prestige = 1;
        OriginsShows.HomeVenue(w, small)!.Capacity = 150;
        Assert.Equal(Negotiator.OriginsPurseFloor, Negotiator.PurseCap(w, small));
        decimal ceiling = FighterValuation.OriginsMarketCeiling(w);
        // (The old bottom of the scale, 15% of the ceiling, is over the floor here: the case that starved the small houses.)
        Assert.True(Math.Round(ceiling * 0.15m / 250m) * 250m > Negotiator.OriginsPurseFloor, $"ceiling {ceiling}");

        var valuation = new FighterValuation();
        var negotiator = new Negotiator();
        var unknowns = w.FreeAgents()
            .Where(f => f.Status == FighterStatus.Active && f.Sex == Sex.Male && f.Record.TotalBouts == 0 && small.Divisions.Any(d => d.Matches(f)))
            .OrderBy(f => f.Id.Value).ToList();
        Assert.NotEmpty(unknowns);
        int signable = 0;
        foreach (var f in unknowns)
        {
            var exp = valuation.Evaluate(w, f);
            Assert.True(exp.AskingPurse.Amount >= Negotiator.OriginsPurseFloor);
            if (exp.AskingPurse.Amount > Negotiator.OriginsPurseFloor) continue;
            var offer = negotiator.AiOpeningOffer(w, small, f, exp);
            Assert.True(offer.PursePerFight.Amount <= Negotiator.OriginsPurseFloor);
            if (negotiator.Evaluate(w, f, offer, exp).Verdict == NegotiationVerdict.Accept) signable++;
        }
        Assert.True(signable > 0, $"none of {unknowns.Count} unknown free agents would sign at the small house's floor purse");
    }

    [Fact]
    public void ANamedMan_StillAsksHisShareOfTheBestPurse()
    {
        var w = OriginsWorldTests.AiWorld(11);
        var giant = House(w, "SFA");
        giant.Prestige = 60;
        OriginsShows.HomeVenue(w, giant)!.Capacity = 20_000;
        decimal ceiling = FighterValuation.OriginsMarketCeiling(w);
        var valuation = new FighterValuation();
        // Asks still run up to the ceiling: the best-known man in the world asks well over the floor, and no man over it.
        var asks = w.Fighters.Values.Where(f => f.Status == FighterStatus.Active)
            .Select(f => valuation.Evaluate(w, f).AskingPurse.Amount).ToList();
        Assert.True(asks.Max() > Negotiator.OriginsPurseFloor * 2, $"top ask {asks.Max()}");
        Assert.All(asks, a => Assert.True(a <= Math.Max(Negotiator.OriginsPurseFloor, Math.Round(ceiling / 250m) * 250m), $"{a} over the ceiling {ceiling}"));
    }
}
