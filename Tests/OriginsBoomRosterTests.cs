using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
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
    public void AnAiHouse_FoundedAfterTheWholeLadder_OpensOnlyTheLinesItsPurseCanStaff()
    {
        var w = new WorldBuilder().Build(WorldBuilder.Options.OriginsObserver(4));
        Era.Unlock(w, Capability.Rounds);
        Era.Unlock(w, Capability.Gloves);
        foreach (var chapter in OriginsReforms.Ladder)
            foreach (var wc in OriginsReforms.LinesOf(chapter)) Era.Unlock(w, Capability.Class(wc));
        Assert.Equal(8, OriginsFounding.DrawnLines(w).Count);

        var house = OriginsFounding.Found(w, w.CurrentDate, null)!;
        var lines = house.Divisions.Where(d => d.Sex == Sex.Male).ToList();
        int staff = OriginsReforms.DivisionsItCanStaff(w, house);
        Assert.True(Negotiator.PurseCap(w, house) < 2_000m, $"a new house's purse {Negotiator.PurseCap(w, house)}");
        Assert.InRange(lines.Count, 2, staff);
        Assert.True(lines.Count < 8);
        Assert.DoesNotContain(lines, d => d.Open);
        // Whole chapters, heavy and light first: the heaviest line is there, so every man has a division.
        foreach (var wc in OriginsReforms.LinesOf(OriginsReforms.Ladder[0])) Assert.Contains(lines, d => d.WeightClass == wc);
        Assert.All(w.Fighters.Values.Where(f => f.Sex == Sex.Male), f => Assert.Contains(lines, d => d.Matches(f)));
        // Its signing target follows: a few wide divisions, not eight thin ones.
        Assert.True(OriginsShows.RosterDemand(house) <= 24, $"wants {OriginsShows.RosterDemand(house)} men");
    }

    [Fact]
    public void WhatAHouseCanStaff_RisesWithItsPurse_FromTheFirstSplitToTheWholeLadder()
    {
        var w = OriginsWorldTests.AiWorld(11);
        var small = House(w, "DLB");
        small.Prestige = 1;
        OriginsShows.HomeVenue(w, small)!.Capacity = 150;
        Assert.Equal(2, OriginsReforms.DivisionsItCanStaff(w, small));
        var giant = House(w, "SFA");
        giant.Prestige = 60;
        OriginsShows.HomeVenue(w, giant)!.Capacity = 20_000;
        Assert.Equal(8, OriginsReforms.DivisionsItCanStaff(w, giant));
    }

    [Fact]
    public void ASmallHouseShortOfMen_HoldsAnOpenTryout_OnceAYear_AndNeverForThePlayer()
    {
        var w = OriginsWorldTests.AiWorld(11);
        var sfa = House(w, "SFA");
        foreach (var f in w.RosterOf(sfa.Id).OrderBy(f => f.Id.Value).Skip(3).ToList()) f.Contract = null;
        Assert.Equal(3, w.RosterOf(sfa.Id).Count());
        int before = w.Fighters.Count;

        OriginsFounding.RunTryouts(w, w.CurrentDate);
        var men = w.RosterOf(sfa.Id).ToList();
        Assert.Equal(OriginsShows.MinRoster(sfa) + 2, men.Count);
        Assert.All(men.Where(f => f.Id.Value > before), f =>
        {
            Assert.Equal(sfa.Id, f.DiscoveredBy);
            Assert.Equal(Negotiator.OriginsPurseFloor, f.Contract!.PursePerFight.Amount);
        });
        Assert.Single(sfa.Ledger, e => e.Description == OriginsFounding.TryoutLedgerLine);

        // Short again inside the year: no second tryout.
        foreach (var f in men.Skip(3).ToList()) f.Contract = null;
        OriginsFounding.RunTryouts(w, w.CurrentDate.AddDays(200));
        Assert.Equal(3, w.RosterOf(sfa.Id).Count());
        OriginsFounding.RunTryouts(w, w.CurrentDate.AddDays(OriginsFounding.TryoutEveryDays));
        Assert.Equal(OriginsShows.MinRoster(sfa) + 2, w.RosterOf(sfa.Id).Count());

        // The player's house is his own: nobody holds a tryout for him.
        var pw = new WorldBuilder().Build(WorldBuilder.Options.Origins(5, 1));
        var mine = pw.Get(pw.PlayerPromotion)!;
        foreach (var f in pw.RosterOf(mine.Id).OrderBy(f => f.Id.Value).Skip(3).ToList()) f.Contract = null;
        OriginsFounding.RunTryouts(pw, pw.CurrentDate);
        Assert.Equal(3, pw.RosterOf(mine.Id).Count());
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
