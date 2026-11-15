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

    private static FightRules Crucible => OriginsPromotions.All[0].ToFightRules();
    private static FightRules Shinken => OriginsPromotions.All[1].ToFightRules();
    private static FightRules Duelo => OriginsPromotions.All[2].ToFightRules();

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
        // Open weight switched on, every other rule modern, both men the same size: every relative size
        // effect is exactly neutral. Open weight's power comes from natural weight, not the class; at the
        // lightweight middle (73 kg, the class fallback) the curve gives exactly the lightweight's 1.00,
        // so every result, statline and card must equal the modern bout's exactly.
        var pool = FightLab.GoldenFighters(60);
        foreach (var f in pool) { f.NaturalWeightLbs = 0; f.WeightClass = WeightClass.Lightweight; }
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
        Assert.Equal(new[] { 0, 7, 3, 4, 1, 6, 2, 5 }, OneNightBracket.Order(8));
        Assert.Equal(new[] { 0, 3, 1, 2 }, OneNightBracket.Order(4));
    }

    [Fact]
    public void OpenWeightOneNightBracket_RunsUnderHouseRules_AndIsDeterministic()
    {
        string Run()
        {
            var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
            var promo = world.Promotions.Values.OrderByDescending(p => p.Prestige.Value).First();
            promo.Rules.Fight = Crucible;
            var result = new TournamentService().RunOneNight(world, promo.Id, 8, world.CurrentDate, new Money(50_000m), wc: null, rules: null);
            Assert.NotNull(result);
            var ev = result!.Event;
            // The real invariant: one card bout per fought slot; a walkover adds none.
            Assert.Equal(result.Night.BoutsFought, ev.Card.Count);
            Assert.Equal(7, result.Night.Bouts.Count);
            foreach (var f in ev.Card)
            {
                Assert.NotNull(f.Outcome);
                Assert.Equal(1, f.ScheduledRounds);
                Assert.NotEqual(FightMethod.UnanimousDecision, f.Outcome!.Method);   // no judges here
                Assert.True(f.Outcome.EndRound <= 2 && f.Outcome.EndTimeSeconds <= 1200);   // round 2 = the final's overtime
            }
            // A champion, or a night that says it has none.
            Assert.True(result.Champion is not null || world.News.Any(n => n.Headline.Contains(ev.Name) && n.Headline.Contains("No winner") || n.Headline.Contains("without a champion")));
            var opening = ev.Card.Take(4).SelectMany(f => new[] { f.RedCorner, f.BlueCorner }).ToList();
            Assert.Equal(8, opening.Distinct().Count());
            return string.Join("|", ev.Card.Select(f => $"{f.RedCorner.Value}-{f.BlueCorner.Value}:{f.Outcome!.Winner?.Value}:{f.Outcome.Method}:{f.Outcome.EndTimeSeconds}"))
                   + $"#{result.Champion?.Id.Value}";
        }
        Assert.Equal(Run(), Run());
    }

    // --- the bracket's rules, scripted (no engine: every outcome is chosen) --------------------------

    private static List<Fighter> Field(int n) => Enumerable.Range(1, n).Select(i => new Fighter
    {
        Id = new FighterId(i), FirstName = "Seed", LastName = $"{i}", BirthDate = new DateOnly(1965, 1, 1),
    }).ToList();

    private static BracketBoutResult Result(Fighter red, Fighter blue, Fighter? winner, BoutCondition? redEnd = null, BoutCondition? blueEnd = null) =>
        new(new FightOutcome
        {
            Winner = winner?.Id, Loser = winner is null ? null : (winner == red ? blue.Id : red.Id),
            Method = winner is null ? FightMethod.Draw : FightMethod.SubmissionTap, EndRound = 1, EndTimeSeconds = 120,
        }, redEnd ?? new BoutCondition(90, 80, 0, 1), blueEnd ?? new BoutCondition(90, 80, 0, 1));

    [Fact]
    public void Bracket_FinalDrawnAtTheLimit_GoesToOvertime_ThenHasNoChampion()
    {
        var field = Field(8);
        var noJudges = Crucible with { OpenWeight = false };
        var overtimeAsked = new List<int>();
        var night = OneNightBracket.Run(field, null, noJudges, 1200, (r, b, p) =>
        {
            overtimeAsked.Add(p.OvertimeSeconds);
            return Result(r, b, overtimeAsked.Count == 7 ? null : r);   // the seventh bout is the final: level
        });
        Assert.Equal(new[] { 0, 0, 0, 0, 0, 0, OneNightBracket.FinalOvertimeSeconds }, overtimeAsked);
        Assert.Null(night.Champion);
        Assert.True(night.FinalDrawn);
        Assert.Equal(BracketAdvance.NobodyAdvanced, night.Bouts[^1].How);
    }

    [Fact]
    public void FinalOvertime_IsFoughtByTheEngine_AsRoundTwo()
    {
        var pool = FightLab.GoldenFighters(80);
        var engine = new FightEngine();
        var noJudges = new FightRules { Rounds = 1, RoundMinutes = 20, Judges = false };
        int checkedBouts = 0;
        for (int i = 0; i < 200 && checkedBouts < 5; i++)
        {
            var red = pool[i % 80]; var blue = pool[(i * 11 + 3) % 80];
            if (red == blue) continue;
            ulong seed = SimRandom.Mix(91, (ulong)i);
            var plain = engine.Simulate(red, blue, new FightParams { Rules = noJudges }, seed);
            if (plain.Method != FightMethod.Draw) continue;
            var ot = engine.Simulate(red, blue, new FightParams { Rules = noJudges, OvertimeSeconds = 300 }, seed);
            Assert.Equal(2, ot.EndRound);
            Assert.True(ot.EndTimeSeconds <= 300);
            Assert.Contains(ot.PlayByPlay!, l => l.StartsWith("--- Round 2: overtime"));
            // Up to the end of regulation the two bouts are the same bout.
            Assert.Equal(plain.PlayByPlay!.Where(l => l.StartsWith("R1 ") && !l.Contains("Time!")),
                         ot.PlayByPlay!.Where(l => l.StartsWith("R1 ") && !l.Contains("Time!")));
            checkedBouts++;
        }
        Assert.True(checkedBouts > 0);
    }

    [Fact]
    public void Bracket_BothSemiWinnersWithdraw_WithNoAlternate_NoChampion()
    {
        var field = Field(8);
        int bout = 0;
        var spent = new BoutCondition(5, 5, 0, 1);
        var night = OneNightBracket.Run(field, null, Crucible, 1200, (r, b, p) =>
        {
            bout++;
            return bout is 5 or 6 ? Result(r, b, r, redEnd: spent) : Result(r, b, r);   // both semi winners spent
        });
        Assert.Null(night.Champion);
        Assert.False(night.FinalDrawn);
        Assert.Equal(6, night.BoutsFought);
        Assert.Equal(2, night.Bouts.Count(x => x.Withdrew is not null));
        Assert.Equal(BracketAdvance.NobodyAdvanced, night.Bouts[^1].How);
        Assert.Null(night.Bouts[^1].Fight);
    }

    [Fact]
    public void Bracket_AWithdrawnWinner_IsReplacedByTheAlternate_Once()
    {
        var field = Field(8);
        var alternate = Field(9)[8];
        int bout = 0;
        var spent = new BoutCondition(5, 5, 0, 1);
        var night = OneNightBracket.Run(field, alternate, Crucible, 1200, (r, b, p) =>
        {
            bout++;
            return bout is 1 or 2 ? Result(r, b, r, redEnd: spent) : Result(r, b, r);
        });
        Assert.Equal(BracketAdvance.AlternateForWithdrawal, night.Bouts[0].How);
        Assert.Equal(alternate.Id, night.Bouts[0].Advanced);
        Assert.Equal(BracketAdvance.NobodyAdvanced, night.Bouts[1].How);       // the alternate is used up
        Assert.Equal(BracketAdvance.Walkover, night.Bouts[4].How);             // the alternate walks into the final
        Assert.Equal(alternate.Id, night.Champion?.Id);                        // and (red) wins it
    }

    [Fact]
    public void Bracket_JudgedDraw_AdvancesTheBetterOriginalSeed_NotTheRedCorner()
    {
        var field = Field(8);
        var judged = Shinken with { OpenWeight = false };
        int bout = 0;
        var night = OneNightBracket.Run(field, null, judged, 300, (r, b, p) =>
        {
            bout++;
            return bout switch
            {
                1 => Result(r, b, b),       // 1 v 8: seed 8 (blue) upsets seed 1
                2 => Result(r, b, r),       // 4 v 5: seed 4
                5 => Result(r, b, null),    // semi: seed 8 is now RED against seed 4 - a judged draw
                _ => Result(r, b, r),
            };
        });
        var semi = night.Bouts[4];
        Assert.Equal(new FighterId(8), semi.Red);
        Assert.Equal(new FighterId(4), semi.Blue);
        Assert.Equal(BracketAdvance.JudgedDrawBetterSeed, semi.How);
        Assert.Equal(new FighterId(4), semi.Advanced);
    }

    [Fact]
    public void RingAndOpenFloor_ChangeTheWords_NotTheFight()
    {
        var pool = FightLab.GoldenFighters(40);
        var engine = new FightEngine();
        var cage = new FightRules { Judges = true, Gloves = GloveType.Bare };   // custom, cage
        var ring = cage with { Enclosure = Enclosure.Ring };
        var floor = cage with { Enclosure = Enclosure.OpenFloor };
        var cageWords = new Regex(@"\bcage\b|\bfence\b|wall-walk");
        bool sawRopes = false;
        for (int i = 0; i < 120; i++)
        {
            var red = pool[i % 40]; var blue = pool[(i * 7 + 5) % 40];
            if (red == blue) continue;
            ulong seed = SimRandom.Mix(13, (ulong)i);
            var c = engine.Simulate(red, blue, new FightParams { Rules = cage }, seed);
            var r = engine.Simulate(red, blue, new FightParams { Rules = ring }, seed);
            var f = engine.Simulate(red, blue, new FightParams { Rules = floor }, seed);
            Assert.Equal(Fingerprint(c, red, blue), Fingerprint(r, red, blue));
            Assert.Equal(Fingerprint(c, red, blue), Fingerprint(f, red, blue));
            Assert.DoesNotContain(r.PlayByPlay!, l => cageWords.IsMatch(l));
            Assert.DoesNotContain(f.PlayByPlay!, l => cageWords.IsMatch(l) || l.Contains("ropes"));
            sawRopes |= r.PlayByPlay!.Any(l => l.Contains("ropes"));
        }
        Assert.True(sawRopes);
    }

    [Fact]
    public void RoundHeaders_CarryOnlyTheRoundNumberAsDigits()
    {
        // The broadcast window reads every digit in a "---" line as the round number.
        var pool = FightLab.GoldenFighters(10);
        var o = new FightEngine().Simulate(pool[0], pool[1], new FightParams { Rules = Duelo }, 3UL);
        var header = o.PlayByPlay!.First(l => l.StartsWith("---"));
        Assert.Equal("1", new string(header.Where(char.IsAsciiDigit).ToArray()));
        Assert.Contains("thirty", header);
    }

    [Fact]
    public void OnlyPunchesHurtTheHand()
    {
        Assert.True(Bout.IsPunch("uppercut"));
        Assert.True(Bout.IsPunch("short hook"));
        Assert.True(Bout.IsPunch("overhand right"));
        Assert.False(Bout.IsPunch("knee to the body"));
        Assert.False(Bout.IsPunch("short elbow"));
        Assert.False(Bout.IsPunch("knee"));
        Assert.False(Bout.IsPunch("head kick"));
    }

    [Fact]
    public void NoClosedFistsOnTheGround_MakesTheFollowUpTkoRarer()
    {
        var pool = FightLab.GoldenFighters(80);
        var engine = new FightEngine();
        var open = new FightRules { Gloves = GloveType.Bare };
        var palms = open with { GroundedHeadPunches = false };
        int followOpen = 0, followPalms = 0;
        for (int i = 0; i < 3000; i++)
        {
            var red = pool[i % 80]; var blue = pool[(i * 11 + 3) % 80];
            if (red == blue) continue;
            ulong seed = SimRandom.Mix(17, (ulong)i);
            engine.Simulate(red, blue, new FightParams { Rules = open, GeneratePlayByPlay = false }, seed, out var a, out _, out _);
            engine.Simulate(red, blue, new FightParams { Rules = palms, GeneratePlayByPlay = false }, seed, out var b, out _, out _);
            if (a == "TKO: follow-up after knockdown") followOpen++;
            if (b == "TKO: follow-up after knockdown") followPalms++;
        }
        Assert.True(followPalms < followOpen * 0.6, $"follow-up TKOs: fists {followOpen}, palms {followPalms}");
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
