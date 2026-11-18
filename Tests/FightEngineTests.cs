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
        // Every judged result agrees with the three cards: unanimous 3-0, split 2-1, majority
        // two for the winner and one level, and a draw has no side on two cards.
        int draws = 0, majority = 0, majorityDraws = 0;
        for (uint s = 1; s <= 6000; s++)
        {
            var bout = new Bout(a, b, FightParams.FastThreeRound, new SimRandom(s));
            var o = bout.Run();
            if (bout.JudgeVotes is not { } v) continue;            // a finish
            int red = v.Count(x => x == 0), blue = v.Count(x => x == 1), lvl = v.Count(x => x == -1);
            int forWinner = o.Winner == a.Id ? red : blue, forLoser = o.Winner == a.Id ? blue : red;
            switch (o.Method)
            {
                case FightMethod.UnanimousDecision: Assert.Equal(3, forWinner); break;
                case FightMethod.SplitDecision: Assert.True(forWinner == 2 && forLoser == 1, $"split {red}-{blue}-{lvl}"); break;
                case FightMethod.MajorityDecision:
                    majority++;
                    Assert.True(forWinner == 2 && lvl == 1, $"majority decision {red}-{blue}-{lvl}");
                    break;
                case FightMethod.Draw:
                    draws++;
                    Assert.Null(o.Winner);
                    Assert.True(red < 2 && blue < 2, $"a draw with a side on two cards: {red}-{blue}-{lvl}");
                    if (red + blue == 1) majorityDraws++;
                    Assert.Equal(o.RoundScores!.Sum(x => x.Red), o.RoundScores!.Sum(x => x.Blue));   // a level card is stored
                    break;
                default: Assert.Fail($"judged fight ended {o.Method}"); break;
            }
        }
        Assert.True(draws > 0 && majority > 0 && majorityDraws > 0, $"draws {draws} (majority draws {majorityDraws}), majority decisions {majority}");
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

    // ------------------------------------------------ the size term on today's rules (BandedSizeTerm)

    /// <summary>A pair of the same build in the same division label; only natural weight differs.</summary>
    private static (Fighter A, Fighter B) SizePair(int i)
    {
        var gen = new FighterGenerator((ulong)(4100 + i));
        Fighter M(int id) => gen.Generate(new FighterId(id), Today, new GenOptions
        { Overall = 55 + (i * 5) % 25, Style = (PrimaryStyle)((i + id) % 8), WeightClass = WeightClass.Heavyweight, WithRecord = false, Age = 28 });
        return (M(1), M(2));
    }

    private static string Print(FightOutcome o, Fighter red, Fighter blue)
    {
        string S(FighterId id)
        {
            var s = o.Stats[id];
            return $"{s.SignificantStrikesLanded}/{s.SignificantStrikesAttempted}/{s.TakedownsLanded}/{s.TakedownsAttempted}/{s.SubmissionAttempts}/{s.KnockdownsScored}/{s.ControlTimeSeconds}";
        }
        string card = o.RoundScores is null ? "-" : string.Join(",", o.RoundScores.Select(r => $"{r.Red}-{r.Blue}"));
        string text = o.PlayByPlay is null ? "-" : string.Join("\n", o.PlayByPlay);
        return $"{o.Winner?.Value}:{o.Method}:{o.EndRound}:{o.EndTimeSeconds}:{S(red.Id)}:{S(blue.Id)}:{card}\n{text}";
    }

    [Fact]
    public void BandedSizeTerm_OnTodaysRules_GivesTheBiggerManHisEdge()
    {
        // 113 kg against 77 kg in one banded division, fought under today's rules (a cage house under the
        // unified code). Each pair fights twice with the weights swapped, so skill cancels and only size is left.
        var engine = new FightEngine();
        int bigWins = 0, bigWinsOff = 0, smallWins = 0, n = 0;
        for (int i = 0; i < 300; i++)
        {
            var (a, b) = SizePair(i);
            for (int swap = 0; swap < 2; swap++)
            {
                var big = swap == 0 ? a : b; var small = swap == 0 ? b : a;
                big.NaturalWeightLbs = 249; small.NaturalWeightLbs = 170;
                ulong seed = SimRandom.Mix(919, (ulong)(2 * i + swap));
                var on = engine.Simulate(a, b, new FightParams { GeneratePlayByPlay = false, BandedSizeTerm = true }, seed);
                var off = engine.Simulate(a, b, new FightParams { GeneratePlayByPlay = false }, seed);
                n++;
                if (on.Winner == big.Id) bigWins++;
                if (on.Winner == small.Id) smallWins++;
                if (off.Winner == big.Id) bigWinsOff++;
                Assert.InRange(on.EndRound, 1, 3);                          // still today's three rounds
            }
        }
        double pOn = 100.0 * bigWins / n, pOff = 100.0 * bigWinsOff / n, pSmall = 100.0 * smallWins / n;
        Assert.True(pOn > pOff + 12, $"bigger man's win %: flagged {pOn:0.0}, unflagged {pOff:0.0}");
        Assert.True(pOn > 2 * pSmall, $"bigger man {pOn:0.0}% vs smaller man {pSmall:0.0}%");
        Assert.InRange(pOff, 40.0, 60.0);                                   // unflagged, size is ignored
    }

    [Fact]
    public void WithoutTheFlag_TodaysBoutIgnoresNaturalWeight()
    {
        // The unflagged modern bout is the one the golden masters pin; it never reads natural weight, so a
        // real size gap changes nothing - word for word, the same fight as two men of one size.
        var engine = new FightEngine();
        for (int i = 0; i < 150; i++)
        {
            var (a, b) = SizePair(i);
            ulong seed = SimRandom.Mix(920, (ulong)i);
            var p = new FightParams { GeneratePlayByPlay = true, TextSeed = (ulong)i };
            a.NaturalWeightLbs = 249; b.NaturalWeightLbs = 170;
            string gap = Print(engine.Simulate(a, b, p, seed), a, b);
            a.NaturalWeightLbs = 0; b.NaturalWeightLbs = 0;
            string level = Print(engine.Simulate(a, b, p, seed), a, b);
            Assert.Equal(level, gap);
        }
    }

    [Fact]
    public void BandedSizeTerm_OnTodaysRules_IsExactlyNeutralBetweenSameSizeMen()
    {
        // Flag on, both men the same natural weight: every relative term is x1.0 or +0.0, the absolute part
        // stays today's, and there is no tale-of-the-tape line, so the fight is byte-identical, text included.
        var engine = new FightEngine();
        foreach (int lbs in new[] { 0, 170, 249 })
            for (int i = 0; i < 150; i++)
            {
                var (a, b) = SizePair(i);
                a.NaturalWeightLbs = lbs; b.NaturalWeightLbs = lbs;
                ulong seed = SimRandom.Mix(921, (ulong)i);
                var off = engine.Simulate(a, b, new FightParams { GeneratePlayByPlay = true, TextSeed = (ulong)i }, seed);
                var on = engine.Simulate(a, b, new FightParams { GeneratePlayByPlay = true, TextSeed = (ulong)i, BandedSizeTerm = true }, seed);
                Assert.Equal(Print(off, a, b), Print(on, a, b));
            }
    }

    // A later bout of a bracket night carries condition, which puts a bout under today's rules on the custom path.
    // The size term there is still today's (the relative edge only), applied once.

    [Fact]
    public void BandedSizeTerm_WithCarriedCondition_OnTodaysRules_IsExactlyNeutralBetweenSameSizeMen()
    {
        var engine = new FightEngine();
        var worn = new BoutCondition(70, 55, 1, 1);
        foreach (int lbs in new[] { 0, 170, 249 })
            for (int i = 0; i < 150; i++)
            {
                var (a, b) = SizePair(i);
                a.NaturalWeightLbs = lbs; b.NaturalWeightLbs = lbs;
                ulong seed = SimRandom.Mix(922, (ulong)i);
                // Text too below 110 kg; a man of 110 kg+ who gasses gets the size-term wording for it (text only).
                var p = new FightParams { GeneratePlayByPlay = lbs < 240, TextSeed = (ulong)i, RedStart = worn, BlueStart = BoutCondition.Fresh };
                var off = engine.Simulate(a, b, p, seed);
                var on = engine.Simulate(a, b, p with { BandedSizeTerm = true }, seed);
                Assert.Equal(Print(off, a, b), Print(on, a, b));
            }
    }

    [Fact]
    public void BandedSizeTerm_WithCarriedCondition_OnTodaysRules_MatchesTheFreshBoutsEdge()
    {
        // 113 kg against 77 kg, both corners carrying a fresh condition (the custom path) against the same pair
        // with no carry (the modern path): the bigger man's edge is the same size term, not a second or bigger one.
        var engine = new FightEngine();
        int fresh = 0, carried = 0, n = 0;
        for (int i = 0; i < 300; i++)
        {
            var (a, b) = SizePair(i);
            for (int swap = 0; swap < 2; swap++)
            {
                var big = swap == 0 ? a : b; var small = swap == 0 ? b : a;
                big.NaturalWeightLbs = 249; small.NaturalWeightLbs = 170;
                ulong seed = SimRandom.Mix(923, (ulong)(2 * i + swap));
                var p = new FightParams { GeneratePlayByPlay = false, BandedSizeTerm = true };
                if (engine.Simulate(a, b, p, seed).Winner == big.Id) fresh++;
                if (engine.Simulate(a, b, p with { RedStart = BoutCondition.Fresh, BlueStart = BoutCondition.Fresh }, seed).Winner == big.Id) carried++;
                n++;
            }
        }
        double pFresh = 100.0 * fresh / n, pCarried = 100.0 * carried / n;
        Assert.True(pCarried > 65, $"bigger man's win % with carried condition {pCarried:0.0}");
        Assert.True(Math.Abs(pCarried - pFresh) < 8, $"fresh path {pFresh:0.0}%, carried path {pCarried:0.0}%");
    }
}
