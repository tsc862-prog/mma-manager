using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Org;
using Standing = MmaManager.Core.Sim.Org.Standing;

namespace MmaManager.Tests;

/// <summary>
/// Opt-in promotion: growth up the ladder is a choice. Prestige is held at the
/// cleared tier's ceiling until the org steps up, so it can deliberately stay small
/// and cheap instead of being forced into a tier it can't pay for.
/// </summary>
public class PromotionLadderTests
{
    private static (World w, Promotion p) Org(int prestige, OrgSize clearance)
    {
        var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5, UseDesignedRoster = false });
        var p = w.Promotions.Values.First();
        p.Prestige = prestige;
        p.TierClearance = clearance;
        return (w, p);
    }

    [Fact]
    public void HeldAtATier_PrestigeCantClimbPastItsCeiling()
    {
        var (w, p) = Org(prestige: 25, clearance: OrgSize.Regional);   // Regional ceiling is 31
        Standing.Apply(w, p, +50, w.CurrentDate, "test");              // try to blow past it
        Assert.Equal(31, p.Prestige.Value);
        Assert.Equal(OrgSize.Regional, OrgSizing.SizeOf(p));           // still Regional, not National
    }

    [Fact]
    public void Eligible_OnlyOnceTheTierIsMaxed()
    {
        var (_, mid) = Org(prestige: 25, clearance: OrgSize.Regional);
        Assert.False(PromotionLadder.IsEligible(mid));                 // hasn't maxed Regional yet

        var (_, maxed) = Org(prestige: 31, clearance: OrgSize.Regional);
        Assert.True(PromotionLadder.IsEligible(maxed));
        Assert.Equal(OrgSize.National, PromotionLadder.NextTier(maxed));
    }

    [Fact]
    public void TheDoor_PricesARush_AndReopensWhenTheArrivalsAge()
    {
        // The gate is a flood backstop, not a queue: it prices the RATE of
        // arrivals into a tier, never the standing population — the sport
        // maturing (more big orgs over the decades) is the world's story, and
        // an org climbing at a natural pace finds the door at the base cushion.
        var (w, p) = Org(prestige: 31, clearance: OrgSize.Regional);
        decimal baseCushion = PromotionLadder.PromotionCushion(p);
        Assert.Equal(baseCushion, PromotionLadder.PromotionCushion(w, p));      // quiet market — door at base

        var others = w.Promotions.Values.Where(x => x.Id != p.Id).Take(3).ToList();
        void Entered(Promotion o, int monthsAgo)
        {
            o.LastClearedTier = OrgSize.National;
            o.LastClearedOn = w.CurrentDate.AddMonths(-monthsAgo);
        }

        // One recent arrival — the market absorbs a second without complaint.
        Entered(others[0], 6);
        Assert.Equal(baseCushion, PromotionLadder.PromotionCushion(w, p));

        // Two inside the window make this org a third — that's a rush, priced.
        Entered(others[1], 12);
        decimal rush = PromotionLadder.PromotionCushion(w, p);
        Assert.True(rush > baseCushion, "a third arrival inside the window is a rush");

        // A fourth would-be entrant is priced out entirely, not queued politely:
        // the markup is convex (squared), so its toll is four times the third's.
        Entered(others[2], 2);
        Assert.Equal((rush - baseCushion) * 4, PromotionLadder.PromotionCushion(w, p) - baseCushion);

        // Nobody leaves; the arrivals simply AGE, and the same door reopens.
        Entered(others[0], 40);
        Entered(others[1], 40);
        Entered(others[2], 40);
        Assert.Equal(baseCushion, PromotionLadder.PromotionCushion(w, p));
    }

    [Fact]
    public void QueueSeniority_SurvivesAPrestigeWobble_ButNotAFall()
    {
        var (w, p) = Org(prestige: 31, clearance: OrgSize.Regional);
        w.PlayerPromotion = default;   // AI path
        p.Balance = new Money(0m);     // can't afford the leap — stays queued

        PromotionLadder.RunMonthly(w, w.CurrentDate);
        Assert.Equal(w.CurrentDate, p.EligibleSince);

        // A one-point dip below the ceiling keeps the org's place in line...
        p.Prestige = 30;
        PromotionLadder.RunMonthly(w, w.CurrentDate.AddMonths(1));
        Assert.NotNull(p.EligibleSince);

        // ...a real fall off the ceiling forfeits it.
        p.Prestige = 26;
        PromotionLadder.RunMonthly(w, w.CurrentDate.AddMonths(2));
        Assert.Null(p.EligibleSince);
    }

    [Fact]
    public void TheQueueClock_StartsAtTheCeiling_AndResetsOnPromotion()
    {
        var (w, p) = Org(prestige: 31, clearance: OrgSize.Regional);
        w.PlayerPromotion = default;   // AI path
        p.Balance = new Money(0m);     // can't afford the leap — stays queued

        PromotionLadder.RunMonthly(w, w.CurrentDate);
        Assert.Equal(w.CurrentDate, p.EligibleSince);

        p.Balance = new Money(50_000_000m);   // now it can — it leaps and leaves the queue
        PromotionLadder.RunMonthly(w, w.CurrentDate.AddMonths(1));
        Assert.Equal(OrgSize.National, p.TierClearance);
        Assert.Null(p.EligibleSince);
    }

    [Fact]
    public void Promote_ClearsTheNextTier_AndStepsOntoIt()
    {
        var (w, p) = Org(prestige: 31, clearance: OrgSize.Regional);
        PromotionLadder.Promote(w, p, w.CurrentDate);

        Assert.Equal(OrgSize.National, p.TierClearance);
        Assert.True(p.Prestige.Value >= OrgSizing.PrestigeFor(OrgSize.National));   // stepped onto the new stage
        Assert.Equal(OrgSize.National, OrgSizing.SizeOf(p));
        // And now it can climb again, up to the National ceiling.
        Standing.Apply(w, p, +50, w.CurrentDate, "test");
        Assert.Equal(OrgSizing.PrestigeCeiling(OrgSize.National), p.Prestige.Value);
    }

    [Fact]
    public void AtTheSummit_ThereIsNothingToPromoteTo()
    {
        var (_, p) = Org(prestige: 80, clearance: OrgSize.International);
        Assert.False(PromotionLadder.IsEligible(p));
    }

    [Fact]
    public void LowerTiers_RunLeanerRosters_ThanTheGiants()
    {
        Assert.True(OrgSizing.RosterTargetPerDivision(OrgSize.City) < OrgSizing.RosterTargetPerDivision(OrgSize.National));
        Assert.True(OrgSizing.RosterTargetPerDivision(OrgSize.National) < OrgSizing.RosterTargetPerDivision(OrgSize.International));
    }
}
