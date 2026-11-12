using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Org;

namespace MmaManager.Tests;

/// <summary>
/// Roster growth is gated by solvency: an org bleeding toward its debt ceiling
/// stops building depth, shops for cheap developable prospects instead of
/// names, and lets expensive non-champions walk at expiry — so nobody signs
/// their way into the grave.
/// </summary>
public class SolvencyGateTests
{
    [Fact]
    public void Distress_ScalesWithTheDebtCeiling()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 4 });
        var promo = world.Promotions.Values.First(p => p.Abbreviation == "VMMA");   // Continental: ceiling -3M

        promo.Balance = new Money(500_000m);
        Assert.Equal(0, Solvency.Distress(promo));
        promo.Balance = new Money(-500_000m);
        Assert.Equal(1, Solvency.Distress(promo));
        promo.Balance = new Money(-2_000_000m);   // past half the -3M ceiling
        Assert.Equal(2, Solvency.Distress(promo));
    }

    [Fact]
    public void ADistressedOrg_StopsBuildingDepth_AndOnlySignsBargains()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 4 });
        var promo = world.Promotions.Values.First(p => p.Abbreviation == "VMMA");
        var div = promo.Divisions.First(d => d.Sex == Sex.Male && d.WeightClass == WeightClass.Lightweight);

        // Thin the division to 2 (below the card minimum of 4) and put the org in the red.
        var inDiv = world.RosterOf(promo.Id).Where(f => div.Matches(f)).ToList();
        foreach (var f in inDiv.Skip(2)) f.Contract = null;
        promo.Balance = new Money(-1_000_000m);

        var before = world.RosterOf(promo.Id).Where(f => div.Matches(f)).Select(f => f.Id).ToHashSet();
        // What each free agent asked on the market (signing to a bigger stage
        // raises the ask after the fact: the stage is part of his public sheet).
        var valuation = new FighterValuation();
        var asked = world.FreeAgents().ToDictionary(f => f.Id, f => valuation.Evaluate(world, f).AskingPurse.Amount);
        FreeAgencyService.RunMonthly(world, world.CurrentDate);
        var after = world.RosterOf(promo.Id).Where(f => div.Matches(f)).ToList();

        // Fills only to the card minimum — no depth-building while in the red —
        // and every new signing fits the distressed bargain budget.
        Assert.True(after.Count <= 4, $"a distressed org should stop at {4}, had {after.Count}");
        decimal ceiling = Negotiator.PurseCap(world, promo) * Solvency.DistressedPurseFactor(1);
        foreach (var signed in after.Where(f => !before.Contains(f.Id)))
            Assert.True(asked[signed.Id] <= ceiling,
                $"{signed.FullName} asks more than a bleeding org should pay");
    }

    [Fact]
    public void AHealthyOrg_StillBuildsToItsTierTarget()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 4 });
        var promo = world.Promotions.Values.First(p => p.Abbreviation == "VMMA");
        var div = promo.Divisions.First(d => d.Sex == Sex.Male && d.WeightClass == WeightClass.Lightweight);

        var inDiv = world.RosterOf(promo.Id).Where(f => div.Matches(f)).ToList();
        foreach (var f in inDiv.Skip(2)) f.Contract = null;
        promo.Balance = new Money(5_000_000m);

        FreeAgencyService.RunMonthly(world, world.CurrentDate);
        int after = world.RosterOf(promo.Id).Count(f => div.Matches(f));
        Assert.True(after > 4, $"a healthy org should build past the card minimum, had {after}");
    }

    [Fact]
    public void TheRosterRetainer_IsNominalInsideTarget_AndPunitiveAboveIt()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 4 });
        var promo = world.Promotions.Values.First(p => p.Abbreviation == "VMMA");

        decimal OverheadCharge()
        {
            int before = promo.Ledger.Count;
            MmaManager.Core.Sim.Finance.Ledger.ChargeMonthlyOverhead(world, world.CurrentDate);
            return -promo.Ledger.Skip(before).Single(l => l.Category == FinanceCategory.Overhead).Amount.Amount;
        }

        // Grow the roster by N spots beyond wherever it sits and re-charge: each
        // extra exclusive over the target must cost the hoarding rate, not $1k.
        decimal baseline = OverheadCharge();
        int target = OrgSizing.RosterTargetPerDivision(OrgSizing.SizeOf(promo)) * promo.Divisions.Count;
        int retained = world.RosterOf(promo.Id).Count(f => f.Status != FighterStatus.Retired && (f.Contract?.IsExclusive ?? true));

        var extras = world.FreeAgents().Where(f => f.Status == FighterStatus.Active)
            .Take(Math.Max(0, target - retained) + 10).ToList();
        foreach (var f in extras)
            f.Contract = new Contract { Promotion = promo.Id, SignedOn = world.CurrentDate, PursePerFight = new Money(5_000m), FightsRemaining = 4, IsExclusive = true };

        decimal grown = OverheadCharge();
        int nowOver = retained + extras.Count - target;
        Assert.True(nowOver >= 10, "setup: roster should now exceed the tier target");
        // The last 10 spots are all over-target: they alone cost 10 x hoarding rate.
        Assert.True(grown - baseline >= 10 * MmaManager.Core.Sim.Finance.Ledger.HoardingRetainerPerSpot,
            $"hoarded spots should be taxed: charge rose {grown - baseline:C0}");
    }

    [Fact]
    public void AnOverstuffedDivision_ShedsNonChampions_AtExpiry()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 4 });
        world.PlayerPromotion = default;   // all-AI: the AFC resolves expiries itself
        var afc = world.Promotions.Values.First(p => p.Abbreviation == "AFC");
        afc.Balance = new Money(10_000_000m);
        var div = afc.Divisions.First(d => d.Sex == Sex.Male && d.WeightClass == WeightClass.Lightweight);
        int target = OrgSizing.RosterTargetPerDivision(OrgSizing.SizeOf(afc));

        // Stuff the division well past target with free agents.
        var extras = world.FreeAgents().Where(f => div.Matches(f) && f.Status == FighterStatus.Active).Take(6).ToList();
        foreach (var f in extras)
            f.Contract = new Contract { Promotion = afc.Id, SignedOn = world.CurrentDate, PursePerFight = new Money(5_000m), FightsRemaining = 4, IsExclusive = true };
        Assert.True(world.RosterOf(afc.Id).Count(f => div.Matches(f)) > target, "setup: division should be over target");

        // A wanted non-champion comes up for renewal — the over-target org lets them walk.
        var expiring = world.RosterOf(afc.Id).First(f => div.Matches(f)
            && !world.Titles.Values.Any(t => t.CurrentChampion == f.Id)
            && RosterPolicy.Wants(world, afc, f));
        expiring.Contract!.FightsRemaining = 0;
        ContractLifecycle.ProcessExpiries(world, world.CurrentDate);
        Assert.Null(expiring.Contract);
    }

    [Fact]
    public void ADistressedOrg_LetsAnExpensiveNonChampionWalk_AtExpiry()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 4 });
        world.PlayerPromotion = default;   // all-AI: the AFC resolves expiries itself
        var afc = world.Promotions.Values.First(p => p.Abbreviation == "AFC");
        afc.Balance = new Money(10_000_000m);

        // Find an AFC non-champion who is expensive (over the distress budget) but
        // WOULD accept a fair renewal — so if he walks, it's the solvency gate.
        var negotiator = new Negotiator();
        var valuation = new FighterValuation();
        decimal distressCeiling = Negotiator.PurseCap(world, afc) * Solvency.DistressedPurseFactor(2);
        var star = world.RosterOf(afc.Id).First(f =>
            f.Sex == Sex.Male
            && !world.Titles.Values.Any(t => t.CurrentChampion == f.Id)
            && f.AgeOn(world.CurrentDate) < 35           // no sunset retirement at expiry
            && world.RosterOf(afc.Id).Count(x => x.WeightClass == f.WeightClass && x.Sex == f.Sex && x.Status != FighterStatus.Retired)
               <= OrgSizing.RosterTargetPerDivision(OrgSizing.SizeOf(afc))   // not trimmed as hoarded depth
            && RosterPolicy.Wants(world, afc, f)
            && valuation.Evaluate(world, f).AskingPurse.Amount > distressCeiling
            && negotiator.Evaluate(world, f,
                   negotiator.AiOpeningOffer(world, afc, f, valuation.Evaluate(world, f)),
                   valuation.Evaluate(world, f)).Verdict == NegotiationVerdict.Accept);

        // Healthy books: the renewal goes through.
        star.Contract!.FightsRemaining = 0;
        ContractLifecycle.ProcessExpiries(world, world.CurrentDate);
        Assert.NotNull(star.Contract);

        // Deep in the red: the same renewal is refused before it's even offered.
        afc.Balance = new Money(-5_000_000m);   // past half the -8M International ceiling
        star.Contract!.FightsRemaining = 0;
        ContractLifecycle.ProcessExpiries(world, world.CurrentDate);
        Assert.Null(star.Contract);
    }
}
