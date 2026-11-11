using MmaManager.Core.Model;
using MmaManager.Core.Sim.Engine;
using MmaManager.Core.Sim.Generation;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Tests;

public class FightEngineTests
{
    private static readonly DateOnly Today = new(2026, 1, 1);
    private static readonly FighterGenerator Gen = new(2026);

    private static Fighter Make(int id, int overall, PrimaryStyle style) =>
        Gen.Generate(new FighterId(id), Today, new GenOptions { Overall = overall, Style = style, Age = 28 });

    [Fact]
    public void SameSeed_ReproducesIdenticalOutcome()
    {
        var red = Make(1, 72, PrimaryStyle.WellRounded);
        var blue = Make(2, 68, PrimaryStyle.Striker);
        var engine = new FightEngine();

        var a = engine.Simulate(red, blue, FightParams.ThreeRound, new SimRandom(500));
        var b = engine.Simulate(red, blue, FightParams.ThreeRound, new SimRandom(500));

        Assert.Equal(a.Winner, b.Winner);
        Assert.Equal(a.Method, b.Method);
        Assert.Equal(a.EndRound, b.EndRound);
        Assert.Equal(a.EndTimeSeconds, b.EndTimeSeconds);
        Assert.Equal(a.PlayByPlay!, b.PlayByPlay!);
    }

    [Fact]
    public void Outcome_HasCoherentWinnerLoserAndRound()
    {
        var red = Make(1, 70, PrimaryStyle.Wrestler);
        var blue = Make(2, 70, PrimaryStyle.Kickboxer);
        var engine = new FightEngine();

        for (uint s = 1; s <= 200; s++)
        {
            var o = engine.Simulate(red, blue, FightParams.ThreeRound, new SimRandom(s));
            Assert.InRange(o.EndRound, 1, 3);
            if (o.Winner is not null)
            {
                Assert.NotEqual(o.Winner, o.Loser);
                Assert.Contains(o.Winner.Value, new[] { red.Id, blue.Id });
            }
            Assert.Equal(2, o.Stats.Count);
        }
    }

    [Fact]
    public void BigFavourite_WinsLargeMajority()
    {
        var fav = Make(1, 85, PrimaryStyle.WellRounded);
        var dog = Make(2, 55, PrimaryStyle.WellRounded);
        var split = FightLab.Compare(fav, dog, 1000, seed: 123);
        Assert.True(split.RedWinPct > 80, $"favourite only won {split.RedWinPct:0.0}%");
    }

    [Fact]
    public void EvenMirrorMatchup_IsRoughlySymmetric()
    {
        var a = Make(1, 72, PrimaryStyle.WellRounded);
        var b = Make(2, 72, PrimaryStyle.Wrestler);
        var asRed = FightLab.Compare(a, b, 2000, seed: 77);
        var asBlue = FightLab.Compare(b, a, 2000, seed: 77);
        // A fighter's win rate shouldn't swing much just by switching corners.
        Assert.True(Math.Abs(asRed.RedWinPct - asBlue.BlueWinPct) < 6,
            $"corner bias: {asRed.RedWinPct:0.0} vs {asBlue.BlueWinPct:0.0}");
    }

    [Fact]
    public void StoredCard_CoversExactlyTheRoundsThatWereFought()
    {
        // In-round finish: the rounds before it. Doctor or corner between rounds: the round just
        // fought too, with the stoppage stamped at the end of that round. Decision: every round.
        var gen = new FighterGenerator(31);
        var engine = new FightEngine();
        int betweenRounds = 0, inRound = 0;
        for (int i = 0; i < 6000; i++)
        {
            Fighter Make(int id) => gen.Generate(new FighterId(id), Today, new GenOptions
            { Overall = 40 + (id * 7) % 45, Style = (PrimaryStyle)(id % 8), WeightClass = WeightClass.Heavyweight, WithRecord = false });
            var a = Make(2 * i + 1); var b = Make(2 * i + 2);
            var p = new FightParams { ScheduledRounds = 5, GeneratePlayByPlay = false };
            var o = engine.Simulate(a, b, p, SimRandom.Mix(808, (ulong)i), out var detail);
            int expected = detail is null ? 5 : o.EndRound - 1;
            if (detail is "TKO: corner" or "TKO: doctor, between rounds")
            {
                expected = o.EndRound; betweenRounds++;
                Assert.True(o.EndRound < 5);
                Assert.Equal(p.RoundSeconds, o.EndTimeSeconds);
            }
            else if (detail is not null) inRound++;
            Assert.Equal(expected, o.RoundScores!.Count);
        }
        Assert.True(betweenRounds > 10 && inRound > 1000, $"between rounds {betweenRounds}, in round {inRound}");
    }

    [Fact]
    public void DrawsAndMajorityDecisions_AreReachable_AndAgreeWithTheCards()
    {
        var a = Make(1, 66, PrimaryStyle.WellRounded);
        var b = Make(2, 66, PrimaryStyle.WellRounded);
        var engine = new FightEngine();
        int draws = 0, majority = 0;
        for (uint s = 1; s <= 4000; s++)
        {
            var o = engine.Simulate(a, b, FightParams.FastThreeRound, new SimRandom(s));
            if (o.Method == FightMethod.Draw)
            {
                draws++;
                Assert.Null(o.Winner);
                Assert.Equal(o.RoundScores!.Sum(x => x.Red), o.RoundScores!.Sum(x => x.Blue));   // a level card is stored
            }
            if (o.Method == FightMethod.MajorityDecision) majority++;
        }
        Assert.True(draws > 0 && majority > 0, $"draws {draws}, majority decisions {majority}");
    }

    [Fact]
    public void FinishRate_IsWithinRealisticBand()
    {
        var a = Make(1, 70, PrimaryStyle.WellRounded);
        var b = Make(2, 70, PrimaryStyle.WellRounded);
        var split = FightLab.Compare(a, b, 2000, seed: 9);
        // Not everything is a finish, and not everything is a decision.
        Assert.InRange(split.FinishPct, 15.0, 60.0);
    }
}
