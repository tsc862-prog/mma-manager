using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Tests;

public class MatchmakingTests
{
    // A player-promotion division that has a belt with a sitting champion.
    private static (World world, Division div, FighterId champ) TitledDivision(ulong seed)
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });
        var promo = world.Get(world.PlayerPromotion)!;
        var div = promo.Divisions.First(d =>
            d.Title is { } tid && world.Get(tid)?.CurrentChampion is not null && d.Rankings.Count >= 2);
        var champ = world.Get(div.Title!.Value)!.CurrentChampion!.Value;
        return (world, div, champ);
    }

    [Fact]
    public void StandingOf_IdentifiesChampionAndRankedContenders()
    {
        var (world, div, champ) = TitledDivision(11);
        var advisor = new MatchmakingAdvisor();

        Assert.Equal(Standing.Champion, advisor.StandingOf(world, champ).Kind);

        var contender = div.Rankings.First(id => id != champ);
        var s = advisor.StandingOf(world, contender);
        Assert.Equal(Standing.Ranked, s.Kind);
        Assert.Equal(div.Rankings.IndexOf(contender) + 1, s.Rank);
    }

    [Fact]
    public void Appraise_ChampionVsContender_IsATitleFight()
    {
        var (world, div, champ) = TitledDivision(13);
        var advisor = new MatchmakingAdvisor();
        var contender = div.Rankings.First(id => id != champ);

        var a = advisor.Appraise(world, champ, contender);

        Assert.True(a.TitleImplied);
        Assert.Equal(Stakes.TitleFight, a.Stakes);
    }

    [Fact]
    public void Appraise_CloserAbility_ScoresMoreCompetitiveThanAMismatch()
    {
        var (world, div, _) = TitledDivision(17);
        var advisor = new MatchmakingAdvisor();
        var pool = world.RosterOf(world.PlayerPromotion)
            .Where(f => f.WeightClass == div.WeightClass && f.Status == FighterStatus.Active)
            .OrderByDescending(FighterAbility.CurrentAbility)
            .ToList();
        Assert.True(pool.Count >= 3);

        int close = advisor.Appraise(world, pool[0].Id, pool[1].Id).Competitiveness;
        int far = advisor.Appraise(world, pool[0].Id, pool[^1].Id).Competitiveness;

        Assert.True(close >= far);
    }

    [Fact]
    public void SuggestOpponent_ReturnsAnEligibleFighter()
    {
        var (world, div, _) = TitledDivision(19);
        var advisor = new MatchmakingAdvisor();
        var booking = new PlayerBooking();
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(20));
        var red = world.RosterOf(world.PlayerPromotion)
            .First(f => f.WeightClass == div.WeightClass && f.Status == FighterStatus.Active && f.CurrentInjury is null);

        var opponents = booking.EligibleOpponents(world, ev, red.Id);
        var pick = advisor.SuggestOpponent(world, red.Id, opponents);

        Assert.NotNull(pick);
        Assert.NotEqual(red.Id, pick!.Value);
        Assert.Contains(opponents, o => o.Id == pick.Value);
    }
}
