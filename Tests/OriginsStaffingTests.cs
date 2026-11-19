using MmaManager.Core.Model;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Org;

namespace MmaManager.Tests;

/// <summary>
/// The Origins giants stop taking the small houses' men once a division is as deep as they sign to on the open market
/// (<see cref="PoachingService.RoomTarget"/>: <see cref="OriginsShows.DivisionRosterTarget"/>, about six a division once
/// a house splits by weight, plus the usual two seats of slack). Read at today's tier depth (14-24 a division) the room
/// gate let the giants take a small house's man at every deal's end until they held 155% of the men they sign to, the free
/// market thinned and the late small houses lived on open tryouts (longsim origins 40 20, ORIGINS STAFFING).
/// Today's mode keeps its tier depth.
/// </summary>
public class OriginsStaffingTests
{
    private static Promotion House(World w, string abbr) => w.Promotions.Values.Single(p => p.Abbreviation == abbr);

    /// <summary>An AI Origins world whose giant has split by weight (heavy and light), its men filed into the two lines.</summary>
    private static (World W, Promotion Giant, Division Div) SplitGiant()
    {
        var w = OriginsWorldTests.AiWorld(11);
        var giant = House(w, "SFA");
        giant.Prestige = 60;   // (a bigger stage than City: today's City draw floor is 0, every man a draw)
        Assert.True(OrgSizing.SizeOf(giant) > OrgSize.City);
        var lines = OriginsReforms.LinesOf(OriginsReforms.Ladder[0]).ToList();
        foreach (var wc in lines) Era.Unlock(w, Capability.Class(wc));
        OriginsReforms.DrawLines(w, giant, lines, w.CurrentDate, announce: false);
        Assert.Equal(2, giant.Divisions.Count(d => d.Sex == Sex.Male && !d.Open));
        return (w, giant, giant.Divisions.First(d => d.Sex == Sex.Male && !d.Open));
    }

    private static int InDivision(World w, Promotion p, Division d) =>
        w.RosterOf(p.Id).Count(x => x.Status != FighterStatus.Retired && d.Matches(x));

    /// <summary>Signs free men of the division to the house until it holds <paramref name="n"/> there.</summary>
    private static void FillTo(World w, Promotion p, Division d, int n, Fighter keepOut)
    {
        foreach (var f in w.FreeAgents().Where(f => f.Id != keepOut.Id && f.Status == FighterStatus.Active && d.Matches(f)).OrderBy(f => f.Id.Value).ToList())
        {
            if (InDivision(w, p, d) >= n) break;
            ContractLifecycle.Sign(w, f, new ContractOffer
            {
                Fighter = f.Id, Promotion = p.Id, PursePerFight = new Money(Negotiator.OriginsPurseFloor), Fights = 3, Exclusive = true,
            }, w.CurrentDate);
        }
        // (Too few free men: generate the rest as tryout men would be.)
        for (int guard = 0; InDivision(w, p, d) < n && guard < 400; guard++)
        {
            var gen = MmaManager.Core.Sim.Generation.FighterGenerator.ForWorld(w);
            var man = OriginsFounding.TryoutMan(w, gen, p, "USA", w.CurrentDate, new MmaManager.Core.Sim.Rng.SimRandom(0xC0FFEEUL + (ulong)w.Fighters.Count));
            if (man is not null && !d.Matches(man)) ContractLifecycle.Release(w, man, dentReputation: false);
        }
        Assert.Equal(n, InDivision(w, p, d));
    }

    [Fact]
    public void AnOriginsGiant_HasRoomForAnotherHousesMan_OnlyUpToTheDepthItSignsTo()
    {
        var (w, giant, div) = SplitGiant();
        int target = OriginsShows.DivisionRosterTarget(giant, distressed: false);
        Assert.Equal(target, PoachingService.RoomTarget(w, giant));
        // Today's tier depth is far deeper: the gate that let the giants hoard.
        Assert.True(OrgSizing.RosterTargetPerDivision(OrgSizing.SizeOf(giant)) > target + 2,
            $"tier depth {OrgSizing.RosterTargetPerDivision(OrgSizing.SizeOf(giant))} vs Origins {target}");

        // A plain man of that division (no draw), signed elsewhere.
        var man = w.Fighters.Values
            .Where(f => f.Status == FighterStatus.Active && div.Matches(f) && f.Contract is not null && f.Contract.Promotion != giant.Id)
            .OrderBy(f => f.Id.Value).First();
        man.Intangibles.Popularity = new Rating(0);

        FillTo(w, giant, div, target + 1, man);
        Assert.True(PoachingService.HasRoom(w, giant, man), "one seat of slack left: room");
        FillTo(w, giant, div, target + 2, man);
        Assert.False(PoachingService.HasRoom(w, giant, man), "the division is at its depth and its slack: no room");
        Assert.NotEqual(giant.Id, PoachingService.BestSuitor(w, w.Get(man.Contract!.Promotion)!, man)?.Id);
    }

    [Fact]
    public void AnOriginsGiant_StillHasRoomForARealDraw()
    {
        var (w, giant, div) = SplitGiant();
        var man = w.Fighters.Values
            .Where(f => f.Status == FighterStatus.Active && div.Matches(f) && f.Contract is not null && f.Contract.Promotion != giant.Id)
            .OrderBy(f => f.Id.Value).First();
        man.Intangibles.Popularity = new Rating(0);
        FillTo(w, giant, div, PoachingService.RoomTarget(w, giant) + 4, man);
        Assert.False(PoachingService.HasRoom(w, giant, man));
        man.Intangibles.Popularity = new Rating(OrgSizing.DrawFloor(OrgSizing.SizeOf(giant)));
        Assert.True(PoachingService.HasRoom(w, giant, man), "a draw is always worth squeezing in");
    }

    [Fact]
    public void TodaysMode_KeepsItsTierDepth()
    {
        var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 4 });
        Assert.All(w.Promotions.Values, p =>
        {
            Assert.Equal(OrgSizing.RosterTargetPerDivision(OrgSizing.SizeOf(p)), PoachingService.RoomTarget(w, p));
            Assert.Equal(OrgSizing.RosterTargetPerDivision(OrgSizing.SizeOf(p)), OrgSizing.DivisionDepth(w, p));
        });
    }

    /// <summary>
    /// At a deal's end an Origins giant split by weight lets a man go once his division holds more than the depth it
    /// signs to (counting him), not today's tier depth of 14-24: the renewal trim reads the same depth as the free-agent
    /// pass and the poaching room (<see cref="OrgSizing.DivisionDepth"/>).
    /// </summary>
    [Fact]
    public void AnOriginsGiant_TrimsAtRenewal_ToTheDepthItSignsTo()
    {
        var (w, giant, div) = SplitGiant();
        int target = OriginsShows.DivisionRosterTarget(giant, distressed: false);
        Assert.Equal(target, OrgSizing.DivisionDepth(w, giant));
        Assert.True(OrgSizing.RosterTargetPerDivision(OrgSizing.SizeOf(giant)) > target + 1);

        var free = w.Fighters.Values.First(f => f.Status == FighterStatus.Active && div.Matches(f));
        FillTo(w, giant, div, target, free);
        var man = w.RosterOf(giant.Id).Where(f => div.Matches(f) && f.Status != FighterStatus.Retired).OrderBy(f => f.Id.Value).First();
        Assert.False(ContractLifecycle.DivisionOverTarget(w, giant, man), "at its depth: renewed as usual");
        FillTo(w, giant, div, target + 1, free);
        Assert.True(ContractLifecycle.DivisionOverTarget(w, giant, man), "one past its depth: the deal runs out to trim the division");
    }

    /// <summary>
    /// When an Origins giant buys a house it carries the bought men into a division only up to its own depth and two seats
    /// of slack (as a poach), not today's tier depth: the rest are cut loose to the market. Champions always come over.
    /// </summary>
    [Fact]
    public void AnOriginsBuyer_CarriesMenOnlyUpToItsOwnDepth()
    {
        var (w, giant, div) = SplitGiant();
        var seller = House(w, "DLB");
        int target = OrgSizing.DivisionDepth(w, giant);
        Assert.True(OrgSizing.RosterTargetPerDivision(OrgSizing.SizeOf(giant)) > target + 4);

        // The seller holds at least five men of the buyer's division (tryout men of its own, as its gyms would send).
        var gen = MmaManager.Core.Sim.Generation.FighterGenerator.ForWorld(w);
        for (int guard = 0; InDivision(w, seller, div) < 5 && guard < 400; guard++)
        {
            var m = OriginsFounding.TryoutMan(w, gen, seller, "USA", w.CurrentDate, new MmaManager.Core.Sim.Rng.SimRandom(0xBEEFUL + (ulong)w.Fighters.Count));
            if (m is not null && !div.Matches(m)) ContractLifecycle.Release(w, m, dentReputation: false);
        }
        Assert.True(InDivision(w, seller, div) >= 5);
        var champions = seller.Titles.Select(t => w.Get(t)?.CurrentChampion).Where(c => c is not null).Select(c => c!.Value).ToHashSet();
        var sellersMen = w.RosterOf(seller.Id).Where(f => f.Status == FighterStatus.Active && div.Matches(f) && f.Contract is not null).ToList();
        int champs = sellersMen.Count(f => champions.Contains(f.Id));

        // The buyer's division one seat short of its depth and slack: room for exactly one bought man (or the champion).
        FillTo(w, giant, div, target + 1, sellersMen[0]);
        Acquisitions.Execute(w, giant, seller, w.CurrentDate, new Money(1_000));

        Assert.Equal(Math.Max(target + 2, target + 1 + champs), InDivision(w, giant, div));
        Assert.True(sellersMen.Count(f => f.Contract is null) >= sellersMen.Count - 1 - champs, "the rest were cut loose");
    }

    /// <summary>
    /// A distressed Origins house's per-man budget never falls under the sport's floor purse, the purse its own open
    /// tryouts pay: a small house's cap at 35% (or 15%) is under what any man asks, so before this every free agent was
    /// "over the distress budget" and it lived on tryouts. Today's mode keeps the cap times the factor.
    /// </summary>
    [Fact]
    public void ADistressedOriginsHouse_BudgetsAtLeastTheFloorPurse()
    {
        var w = OriginsWorldTests.AiWorld(11);
        foreach (var p in w.Promotions.Values)
            foreach (int distress in new[] { 1, 2 })
            {
                decimal raw = Negotiator.PurseCap(w, p) * Solvency.DistressedPurseFactor(distress);
                Assert.Equal(Math.Max(raw, Negotiator.OriginsPurseFloor), Solvency.DistressedBudget(w, p, distress));
            }
        var small = w.Promotions.Values.OrderBy(p => Negotiator.PurseCap(w, p)).ThenBy(p => p.Id.Value).First();
        Assert.True(Negotiator.PurseCap(w, small) * Solvency.DistressedPurseFactor(1) < Negotiator.OriginsPurseFloor,
            $"a small house's 35% ({Negotiator.PurseCap(w, small) * Solvency.DistressedPurseFactor(1)}) is under the floor");
        Assert.Equal(Negotiator.OriginsPurseFloor, Solvency.DistressedBudget(w, small, 1));

        var m = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 4 });
        Assert.All(m.Promotions.Values, p => Assert.Equal(
            Negotiator.PurseCap(m, p) * Solvency.DistressedPurseFactor(1), Solvency.DistressedBudget(m, p, 1)));
    }

    /// <summary>
    /// A distressed small Origins house whose men have walked signs men asking the floor purse off the open market in the
    /// monthly pass (before, its 35% budget reached nobody), and never pays a man under the floor.
    /// </summary>
    [Fact]
    public void ADistressedSmallOriginsHouse_SignsFloorMenAtTheTable()
    {
        var w = OriginsWorldTests.AiWorld(11);
        var small = w.Promotions.Values.OrderBy(p => Negotiator.PurseCap(w, p)).ThenBy(p => p.Id.Value).First();
        // Everyone else is the player's or hands-off: the market is the small house's to shop.
        var other = w.Promotions.Values.Where(p => p.Id != small.Id).OrderBy(p => p.Id.Value).ToList();
        w.PlayerPromotion = other[0].Id;
        other[0].IsPlayerControlled = true;
        foreach (var p in other.Skip(1)) p.Defunct = true;
        foreach (var f in w.RosterOf(small.Id).ToList()) f.Contract = null;   // its men walk: it needs bodies
        small.Balance = new Money(-1m);                                       // in the red: distress 1
        Assert.Equal(1, Solvency.Distress(small, w));
        Assert.True(Negotiator.PurseCap(w, small) * Solvency.DistressedPurseFactor(1) < Negotiator.OriginsPurseFloor);

        var val = new FighterValuation();
        var floorMen = w.FreeAgents().Where(f => f.Status == FighterStatus.Active && small.Divisions.Any(d => d.Matches(f))
                                                 && Negotiator.AffordAsk(w, small, f, val.Evaluate(w, f)) <= Negotiator.OriginsPurseFloor)
                        .Select(f => f.Id).ToHashSet();
        Assert.NotEmpty(floorMen);

        FreeAgencyService.RunMonthly(w, w.CurrentDate);
        var signed = w.RosterOf(small.Id).Where(f => f.Status != FighterStatus.Retired).ToList();
        Assert.Contains(signed, f => floorMen.Contains(f.Id));
        Assert.All(signed, f => Assert.True(f.Contract!.PursePerFight.Amount >= Negotiator.OriginsPurseFloor));
    }
}
