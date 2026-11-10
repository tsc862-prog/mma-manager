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
    public void FinishRate_IsWithinRealisticBand()
    {
        var a = Make(1, 70, PrimaryStyle.WellRounded);
        var b = Make(2, 70, PrimaryStyle.WellRounded);
        var split = FightLab.Compare(a, b, 2000, seed: 9);
        // Not everything is a finish, and not everything is a decision.
        Assert.InRange(split.FinishPct, 15.0, 60.0);
    }
}
