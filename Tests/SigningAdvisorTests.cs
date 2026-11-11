using System.Text.RegularExpressions;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Scouting;

namespace MmaManager.Tests;

/// <summary>
/// The scout's pick respects fog of war: it ranks and describes fighters by what
/// the advised promotion has SEEN (its scouted read, public age and record) and
/// quotes the ask that promotion will actually be given — never true ability,
/// never true potential.
/// </summary>
public class SigningAdvisorTests
{
    private static World Founded(ulong seed) =>
        new WorldBuilder().Build(new WorldBuilder.Options
        {
            Seed = seed,
            Founding = new WorldBuilder.FoundingSpec
            {
                Prestige = 15,
                StartingBalance = new Money(250_000m),
                TalentPool = 20,
                Divisions = new[] { WeightClass.Lightweight, WeightClass.Welterweight },
            },
        });

    [Fact]
    public void Reason_QuotesTheScoutedNumber_NeverTheTrueOverall()
    {
        // Find a founded world whose pick is misjudged by the player's scouts.
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var world = Founded(seed);
            var promo = world.Get(world.PlayerPromotion)!;
            if (SigningAdvisor.Suggest(world, promo) is not { } pick) continue;
            int truth = FighterAbility.CurrentAbility(pick.Fighter);
            int seen = ScoutingService.PerceivedAbility(world, promo, pick.Fighter);
            if (truth == seen) continue;

            Assert.Equal(seen, pick.Scouted);
            Assert.Contains($"scouted {seen}", pick.Reason);
            Assert.DoesNotContain("OVR", pick.Reason);
            Assert.DoesNotMatch(new Regex($@"(?<![\d,$]){truth}(?![\d,])"), pick.Reason);
            return;
        }
        Assert.Fail("no seed produced a misjudged pick to test against");
    }

    [Fact]
    public void Ranking_FollowsThePromotionsRead_NotTheTruth()
    {
        // Across several orgs, find one where ranking by the TRUTH would pick a
        // different fighter than ranking by the org's scouted read: the advisor
        // must give the scouted pick.
        int divergent = 0;
        foreach (ulong seed in new ulong[] { 3, 7, 11, 19 })
        {
            var world = Founded(seed);
            foreach (var promo in world.Promotions.Values.OrderBy(p => p.Id.Value))
            {
                var real = SigningAdvisor.Suggest(world, promo);
                var byRead = SigningAdvisor.Suggest(world, promo, f => ScoutingService.PerceivedAbility(world, promo, f));
                var byTruth = SigningAdvisor.Suggest(world, promo, FighterAbility.CurrentAbility);
                Assert.Equal(byRead?.Fighter.Id, real?.Fighter.Id);
                if (real is not null && byTruth is not null && byTruth.Fighter.Id != real.Fighter.Id) divergent++;
            }
        }
        Assert.True(divergent > 0, "expected at least one org whose scouted pick differs from the true-ability pick");
    }

    [Fact]
    public void TruePotential_NeverMovesThePickOrTheReason()
    {
        var world = Founded(11);
        var promo = world.Get(world.PlayerPromotion)!;
        var before = SigningAdvisor.Suggest(world, promo)!;

        // Scramble every free agent's hidden ceiling: the scout can't see it.
        foreach (var f in world.FreeAgents())
            f.PotentialAbility = new Rating(f.Id.Value % 2 == 0 ? 99 : 1);

        var after = SigningAdvisor.Suggest(world, promo)!;
        Assert.Equal(before.Fighter.Id, after.Fighter.Id);
        Assert.Equal(before.Reason, after.Reason);
    }

    [Fact]
    public void Ask_IsTheFigureThisPromotionIsActuallyQuoted()
    {
        var world = Founded(11);
        var promo = world.Get(world.PlayerPromotion)!;
        var pick = SigningAdvisor.Suggest(world, promo)!;

        // The figure the negotiating room opens at, on the room's opening terms.
        var room = new NegotiationSession(world, pick.Fighter, promo, pick.Expectations)
            .DemandPurse(Negotiator.WantsExclusivity(promo), clause: false);
        Assert.Equal(room, pick.Ask);
        Assert.Contains($"asking ~{room}/fight", pick.Reason);
        // A small hall deals non-exclusive, so its quote undercuts the exclusive
        // rate — and the advisor names both, with the real reason.
        Assert.False(Negotiator.WantsExclusivity(promo));
        Assert.True(pick.Ask.Amount < pick.Expectations.AskingPurse.Amount);
        Assert.Contains("non-exclusive deal", pick.Reason);
        Assert.Contains($"market rate {pick.Expectations.AskingPurse}", pick.Reason);
    }
}
