using System.Text.RegularExpressions;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Engine;
using MmaManager.Core.Sim.Generation;
using MmaManager.Core.Sim.Generation.Origins;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Tests;

/// <summary>
/// The Origins engine: early-era rule sets, the open-weight size term, carried condition and the
/// one-night bracket. First and foremost: today's fights must not move by a single byte.
/// </summary>
public class OriginsEngineTests
{
    /// <summary>
    /// <see cref="FightLab.TextGolden"/> captured on main (f88491b) BEFORE any Origins engine change:
    /// 400 bouts, results, cards and every play-by-play line. It must never move with this work.
    /// </summary>
    private const string TextGoldenMain = "65b8adb69e8d5ab5";

    private static FightRules Crucible => OriginsPromotions.All[0].Rules.ToFightRules();
    private static FightRules Shinken => OriginsPromotions.All[1].Rules.ToFightRules();
    private static FightRules Duelo => OriginsPromotions.All[2].Rules.ToFightRules();

    // ------------------------------------------------------------ default path identity

    [Fact]
    public void DefaultPath_PlayByPlayIsByteIdenticalToMain()
    {
        Assert.Equal(TextGoldenMain, FightLab.TextGolden());
    }

    [Fact]
    public void ExplicitModernRules_AreTheDefaultPath()
    {
        Assert.True(new FightRules().IsModern);
        Assert.Equal(TextGoldenMain, FightLab.TextGolden(template: new FightParams { Rules = FightRules.Modern }));
        Assert.Equal(TextGoldenMain, FightLab.TextGolden(template: new FightParams { Rules = new FightRules() }));
    }

    [Fact]
    public void SizeTerm_IsExactlyNeutralAtEqualSize()
    {
        Assert.Equal(0.0, SizeTerm.Edge(80, 80));
        // Open weight switched on, every other rule modern, both men the same natural weight (under the
        // heavy-gas line): every result, statline and card must equal the modern bout's exactly.
        var pool = FightLab.GoldenFighters(60);
        foreach (var f in pool) f.NaturalWeightLbs = 170;
        var engine = new FightEngine();
        var open = new FightRules { OpenWeight = true };
        for (int i = 0; i < 300; i++)
        {
            var red = pool[i % 60]; var blue = pool[(i * 7 + 13) % 60];
            if (red == blue) continue;
            ulong seed = SimRandom.Mix(77, (ulong)i);
            var a = engine.Simulate(red, blue, new FightParams { GeneratePlayByPlay = false }, seed);
            var b = engine.Simulate(red, blue, new FightParams { GeneratePlayByPlay = false, Rules = open }, seed);
            Assert.Equal(Fingerprint(a, red, blue), Fingerprint(b, red, blue));
        }
    }

    // ------------------------------------------------------------ the size term

    [Fact]
    public void BiggerMan_WinsMore_AllElseEqual()
    {
        var cells = FightLab.SizeTable(Crucible, fightsPerCell: 600, seed: 31, smallKg: 77,
            bigKg: new double[] { 77, 93, 113 }, skillGaps: new[] { 0 });
        var big = cells.Select(c => c.BigWinPct).ToArray();
        var small = cells.Select(c => c.SmallWinPct).ToArray();
        Assert.True(big[0] < big[1] && big[1] < big[2], $"big man's win % by size: {string.Join(", ", big)}");
        Assert.True(small[0] > small[2], $"small man's win % by size: {string.Join(", ", small)}");
        // Not decisive: a much better small man still beats a big one more often than he loses.
        var skilled = FightLab.SizeTable(Crucible, 600, 31, 77, new double[] { 102 }, new[] { 20 })[0];
        Assert.True(skilled.SmallWinPct > skilled.BigWinPct, $"+20 small man vs 102 kg: {skilled}");
    }

    [Fact]
    public void ACompetentBigMan_RarelyLosesToAMuchSmallerOne()
    {
        var c = FightLab.SizeTable(Crucible, 600, 47, smallKg: 62, bigKg: new double[] { 110 }, skillGaps: new[] { 0 })[0];
        Assert.True(c.SmallWinPct < 12, $"62 kg vs 110 kg at equal skill: {c}");
        Assert.True(c.BigWinPct > 3 * c.SmallWinPct, $"62 kg vs 110 kg at equal skill: {c}");
    }

    [Fact]
    public void AVeryBigMan_Gasses_InALongRound()
    {
        // The same man, once at 100 kg and once at 168 kg, against the same 85 kg opponent: carrying
        // the extra weight through twenty minutes drains the tank far faster.
        var gen = new FighterGenerator(5);
        var a = gen.Generate(new FighterId(1), OriginsPioneers.StartDate, new GenOptions { Overall = 60, Style = PrimaryStyle.Wrestler, Age = 28 });
        var b = gen.Generate(new FighterId(2), OriginsPioneers.StartDate, new GenOptions { Overall = 60, Style = PrimaryStyle.Wrestler, Age = 28 });
        b.NaturalWeightLbs = 187;
        var engine = new FightEngine();
        double Tank(int lbs)
        {
            a.NaturalWeightLbs = lbs;
            double sum = 0; int n = 0;
            for (int i = 0; i < 200; i++)
            {
                engine.Simulate(a, b, new FightParams { Rules = Crucible, GeneratePlayByPlay = false }, new SimRandom((ulong)i), out var end, out _);
                sum += end.Stamina; n++;
            }
            return sum / n;
        }
        double heavy = Tank(370), normal = Tank(220);
        Assert.True(heavy < normal - 10, $"stamina left: 168 kg {heavy:0.0}, 100 kg {normal:0.0}");
    }

    // ------------------------------------------------------------ rule sets

    [Fact]
    public void OriginsHouseRules_MapToTheEngine()
    {
        var cru = Crucible;
        Assert.Equal(1, cru.Rounds); Assert.Equal(20, cru.RoundMinutes);
        Assert.False(cru.Judges); Assert.Equal(GloveType.Optional, cru.Gloves); Assert.True(cru.OpenWeight);
        var sfa = Shinken;
        Assert.Equal(3, sfa.Rounds); Assert.True(sfa.Judges); Assert.Equal(3, sfa.RopeEscapes);
        Assert.False(sfa.Elbows); Assert.False(sfa.GroundedHeadPunches); Assert.False(sfa.GroundedKicks);
        Assert.Equal(GloveType.ThinOpenFinger, sfa.Gloves);
        var dlb = Duelo;
        Assert.Equal(30, dlb.RoundMinutes); Assert.Equal(GloveType.Bare, dlb.Gloves); Assert.False(dlb.Judges);
        Assert.True(dlb.Elbows && dlb.GroundedHeadPunches);
    }

    [Fact]
    public void DrawsAtTheLimit_OnlyWhenThereAreNoJudges()
    {
        var pool = FightLab.GoldenFighters(80);
        var engine = new FightEngine();
        var noJudges = new FightRules { Rounds = 1, RoundMinutes = 20, Judges = false };
        var judges = noJudges with { Judges = true };
        int drawsOff = 0, drawsOn = 0, limitOn = 0;
        for (int i = 0; i < 400; i++)
        {
            var red = pool[i % 80]; var blue = pool[(i * 11 + 3) % 80];
            if (red == blue) continue;
            ulong seed = SimRandom.Mix(91, (ulong)i);
            var off = engine.Simulate(red, blue, new FightParams { Rules = noJudges, GeneratePlayByPlay = false }, seed);
            if (!IsFinish(off.Method))
            {
                Assert.Equal(FightMethod.Draw, off.Method);         // no judges: the limit is a draw, always
                Assert.Null(off.Winner);
                Assert.Empty(off.RoundScores!);                      // nobody scored it
                Assert.Equal((1, 1200), (off.EndRound, off.EndTimeSeconds));
                drawsOff++;
            }
            else Assert.Empty(off.RoundScores!);

            var on = engine.Simulate(red, blue, new FightParams { Rules = judges, GeneratePlayByPlay = false }, seed);
            if (!IsFinish(on.Method))
            {
                limitOn++;
                Assert.Single(on.RoundScores!);                     // the judges scored the one long round
                if (on.Method == FightMethod.Draw) drawsOn++;
            }
        }
        Assert.True(drawsOff > 40, $"no-judges draws: {drawsOff}");
        Assert.True(limitOn > 40 && drawsOn < limitOn / 3, $"judged: {drawsOn} draws of {limitOn} that went the distance");
    }

    [Fact]
    public void RopeEscapes_AreConsumed_AndSaveMenFromTaps()
    {
        var gen = new FighterGenerator(8);
        var bjj = gen.Generate(new FighterId(1), OriginsPioneers.StartDate, new GenOptions { Overall = 75, Style = PrimaryStyle.BrazilianJiuJitsu, Age = 28 });
        var mark = gen.Generate(new FighterId(2), OriginsPioneers.StartDate, new GenOptions { Overall = 50, Style = PrimaryStyle.Striker, Age = 28 });
        var engine = new FightEngine();
        var ropes = Shinken with { OpenWeight = false };
        var noRopes = ropes with { RopeEscapes = 0 };
        var raw = ropes with { Judges = false };   // past the allowance, with no judges, the rope no longer saves him
        int subsRopes = 0, subsNone = 0, used = 0, maxRaw = 0;
        for (int i = 0; i < 500; i++)
        {
            ulong seed = SimRandom.Mix(5, (ulong)i);
            var a = engine.Simulate(bjj, mark, new FightParams { Rules = ropes, GeneratePlayByPlay = false }, seed, out _, out _, out int blueRopes);
            var b = engine.Simulate(bjj, mark, new FightParams { Rules = noRopes, GeneratePlayByPlay = false }, seed, out _, out _, out int noneUsed);
            engine.Simulate(bjj, mark, new FightParams { Rules = raw, GeneratePlayByPlay = false }, seed, out _, out _, out int rawUsed);
            Assert.Equal(0, noneUsed);
            if (a.Method == FightMethod.SubmissionTap) subsRopes++;
            if (b.Method == FightMethod.SubmissionTap) subsNone++;
            used += blueRopes;
            maxRaw = Math.Max(maxRaw, rawUsed);
        }
        Assert.True(used > 50, $"rope escapes used: {used}");
        Assert.True(subsRopes < subsNone, $"taps with ropes {subsRopes}, without {subsNone}");
        Assert.True(maxRaw <= 3, $"no judges: escapes never pass the allowance (max {maxRaw})");
    }

    [Fact]
    public void RopeEscapesPastTheAllowance_CostPointsOnTheCards()
    {
        var gen = new FighterGenerator(8);
        var bjj = gen.Generate(new FighterId(1), OriginsPioneers.StartDate, new GenOptions { Overall = 75, Style = PrimaryStyle.BrazilianJiuJitsu, Age = 28 });
        var mark = gen.Generate(new FighterId(2), OriginsPioneers.StartDate, new GenOptions { Overall = 50, Style = PrimaryStyle.Striker, Age = 28 });
        var engine = new FightEngine();
        var one = Shinken with { OpenWeight = false, RopeEscapes = 1 };
        bool sawDeduction = false;
        for (int i = 0; i < 400 && !sawDeduction; i++)
        {
            var o = engine.Simulate(bjj, mark, new FightParams { Rules = one }, SimRandom.Mix(6, (ulong)i), out _, out _, out int used);
            if (used < 2) continue;
            var pointLines = o.PlayByPlay!.Where(l => l.Contains("a point is taken")).ToList();
            Assert.NotEmpty(pointLines);
            // Every round in which he lost a point and that reached the card: he cannot score 10 in it.
            foreach (var line in pointLines)
            {
                int round = int.Parse(line[1..line.IndexOf(' ')]);
                if (o.RoundScores is { } card && card.Count >= round)
                {
                    Assert.True(card[round - 1].Blue <= 9, $"round {round}: {card[round - 1]}");
                    sawDeduction = true;
                }
            }
        }
        Assert.True(sawDeduction, "a bout with an escape past the allowance should show the point on the card");
    }

    [Fact]
    public void LongRound_FinishesStillCommon_InStyleMismatches()
    {
        var cru = FightLab.RuleSetMix("cru", Crucible, 1200, 11);
        var dlb = FightLab.RuleSetMix("dlb", Duelo, 1200, 11);
        Assert.InRange(cru.FinishPct, 60, 95);
        Assert.InRange(dlb.FinishPct, 70, 98);
        Assert.True(dlb.DrawPct < cru.DrawPct, "thirty minutes leave fewer draws than twenty");
        Assert.Equal(0, cru.DecisionPct);
    }

    [Fact]
    public void OriginsBouts_AreDeterministic_AndTextFitsWomen()
    {
        var gen = new FighterGenerator(12);
        var a = gen.Generate(new FighterId(1), OriginsPioneers.StartDate, new GenOptions { Overall = 60, Style = PrimaryStyle.Wrestler, Sex = Sex.Female, Age = 27 });
        var b = gen.Generate(new FighterId(2), OriginsPioneers.StartDate, new GenOptions { Overall = 60, Style = PrimaryStyle.Striker, Sex = Sex.Female, Age = 27 });
        a.NaturalWeightLbs = 125; b.NaturalWeightLbs = 150;
        var engine = new FightEngine();
        var he = new Regex(@"\b(he|him|his)\b", RegexOptions.IgnoreCase);
        foreach (var rules in new[] { Crucible, Shinken, Duelo })
            for (int i = 0; i < 40; i++)
            {
                var p = new FightParams { Rules = rules, TextSeed = (ulong)i };
                var x = engine.Simulate(a, b, p, new SimRandom((ulong)i));
                var y = engine.Simulate(a, b, p, new SimRandom((ulong)i));
                Assert.Equal(Fingerprint(x, a, b), Fingerprint(y, a, b));
                Assert.Equal(x.PlayByPlay!, y.PlayByPlay!);
                Assert.DoesNotContain(x.PlayByPlay!, l => he.IsMatch(l));
                Assert.All(x.PlayByPlay!.Skip(1), l => Assert.DoesNotContain("rounds.", l));
            }
    }

    // ------------------------------------------------------------ carried condition and the bracket

    [Fact]
    public void CarriedDamage_CompromisesTheNextBout()
    {
        var gen = new FighterGenerator(21);
        var a = gen.Generate(new FighterId(1), OriginsPioneers.StartDate, new GenOptions { Overall = 62, Style = PrimaryStyle.WellRounded, Age = 28 });
        var b = gen.Generate(new FighterId(2), OriginsPioneers.StartDate, new GenOptions { Overall = 62, Style = PrimaryStyle.WellRounded, Age = 28 });
        var engine = new FightEngine();
        var worn = new BoutCondition(45, 50, 1, 1);
        int freshWins = 0, wornWins = 0;
        for (int i = 0; i < 600; i++)
        {
            ulong seed = SimRandom.Mix(3, (ulong)i);
            if (engine.Simulate(a, b, new FightParams { Rules = Crucible, GeneratePlayByPlay = false, RedStart = BoutCondition.Fresh }, seed).Winner == a.Id) freshWins++;
            if (engine.Simulate(a, b, new FightParams { Rules = Crucible, GeneratePlayByPlay = false, RedStart = worn }, seed).Winner == a.Id) wornWins++;
        }
        Assert.True(wornWins < freshWins * 0.75, $"fresh {freshWins}, worn {wornWins}");

        // The rest: a long night leaves a lower ceiling on the tank, and a bad cut stays bad.
        var end = new BoutCondition(60, 20, 3, 0.75);
        var shortNight = end.AfterRest(a, 5);
        var longNight = end.AfterRest(a, 35);
        Assert.True(longNight.Stamina < shortNight.Stamina);
        Assert.True(shortNight.Health > end.Health);
        Assert.Equal(3, shortNight.Cut);
        Assert.False(shortNight.CanContinue);
        Assert.Equal(0.75, shortNight.HandPower);
    }

    [Fact]
    public void BracketOrder_SeedsOneAgainstEight()
    {
        Assert.Equal(new[] { 0, 7, 3, 4, 1, 6, 2, 5 }, TournamentService.BracketOrder(8));
        Assert.Equal(new[] { 0, 3, 1, 2 }, TournamentService.BracketOrder(4));
    }

    [Fact]
    public void OpenWeightOneNightBracket_RunsUnderHouseRules_AndIsDeterministic()
    {
        string Run()
        {
            var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
            var promo = world.Promotions.Values.OrderByDescending(p => p.Prestige.Value).First();
            promo.Rules.Fight = Crucible;
            var ev = new TournamentService().RunOneNight(world, promo.Id, 8, world.CurrentDate, new Money(50_000m), wc: null, rules: null);
            Assert.NotNull(ev);
            Assert.InRange(ev!.Card.Count, 4, 7);
            Assert.Equal(4, ev.Card.Take(4).Count());
            foreach (var f in ev.Card)
            {
                Assert.NotNull(f.Outcome);
                Assert.Equal(1, f.ScheduledRounds);
                Assert.NotEqual(FightMethod.UnanimousDecision, f.Outcome!.Method);   // no judges here
                Assert.True(f.Outcome.EndRound == 1 && f.Outcome.EndTimeSeconds <= 1200);
            }
            // Eight different men open the night, from more than one weight class.
            var opening = ev.Card.Take(4).SelectMany(f => new[] { f.RedCorner, f.BlueCorner }).ToList();
            Assert.Equal(8, opening.Distinct().Count());
            return string.Join("|", ev.Card.Select(f => $"{f.RedCorner.Value}-{f.BlueCorner.Value}:{f.Outcome!.Winner?.Value}:{f.Outcome.Method}:{f.Outcome.EndTimeSeconds}"));
        }
        Assert.Equal(Run(), Run());
    }

    // ------------------------------------------------------------ helpers

    private static bool IsFinish(FightMethod m) =>
        m is FightMethod.Knockout or FightMethod.TechnicalKnockout or FightMethod.SubmissionTap or FightMethod.TechnicalSubmission;

    private static string Fingerprint(FightOutcome o, Fighter red, Fighter blue)
    {
        string S(FighterId id)
        {
            var s = o.Stats[id];
            return $"{s.SignificantStrikesLanded}/{s.SignificantStrikesAttempted}/{s.TakedownsLanded}/{s.TakedownsAttempted}/{s.SubmissionAttempts}/{s.KnockdownsScored}/{s.ControlTimeSeconds}";
        }
        string card = o.RoundScores is null ? "-" : string.Join(",", o.RoundScores.Select(r => $"{r.Red}-{r.Blue}"));
        return $"{o.Winner?.Value}:{o.Method}:{o.EndRound}:{o.EndTimeSeconds}:{S(red.Id)}:{S(blue.Id)}:{card}";
    }
}
