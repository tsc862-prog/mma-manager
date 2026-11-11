using System.Diagnostics;
using MmaManager.Core.Sim.Engine;

/// <summary>
/// The fight-engine benchmark, headless (kept in its own file; Program.cs only dispatches).
/// <list type="bullet">
/// <item><c>fightlab [scale] [seed]</c> — <see cref="FightLab.Report"/> plus the golden-master
/// hashes the engine tests pin. Scale 1 is the configuration the tests use (~8 s); 10 gives
/// tighter numbers. Everything except the cost-per-bout line is deterministic.</item>
/// <item><c>fightlab relevance [seed] [low] [high] [field] [seedsPerPair]</c> — the attribute
/// relevance probe behind <c>AttributeRelevance_MatchesTheKnownFailureList</c>; run it at a few
/// seeds to judge a borderline attribute.</item>
/// <item><c>fightlab sections</c> — wall-clock cost of each report block.</item>
/// </list>
/// </summary>
static class FightLabMode
{
    public static void Run(string[] args)
    {
        if (args.Length > 1 && args[1] == "relevance")
        {
            // fightlab relevance [seed] [low] [high] [field] [seedsPerPair]: the attribute-relevance probe the tests pin.
            ulong rs = args.Length > 2 ? ulong.Parse(args[2]) : 2026;
            int lo = args.Length > 3 ? int.Parse(args[3]) : 30, hi = args.Length > 4 ? int.Parse(args[4]) : 90;
            int fs = args.Length > 5 ? int.Parse(args[5]) : 8, sp = args.Length > 6 ? int.Parse(args[6]) : 25;
            var t = Stopwatch.StartNew();
            foreach (var r in FightLab.AttributeRelevance(rs, lo, hi, fs, sp).OrderByDescending(r => double.IsNaN(r.PpPerCaPoint) ? r.MeanPp : r.PpPerCaPoint))
                Console.WriteLine($"  {r.Name,-19} mean {r.MeanPp,7:+0.00;-0.00;0.00} pp   per CA point {r.PpPerCaPoint,6:+0.00;-0.00;0.00}   " +
                    $"per base [{string.Join(" ", r.PerBasePp.Select(v => v.ToString("+0.0;-0.0;0.0")))}]{(r.Dead ? "   DEAD" : "")}");
            Console.WriteLine($"[relevance seed {rs} {lo}->{hi}: {t.Elapsed.TotalSeconds:0.0} s]");
            return;
        }

        if (args.Length > 1 && args[1] == "pbp")
        {
            // fightlab pbp [count] [seed]: print sample play-by-plays (alternating men's/women's, 3/5 rounds).
            int count = args.Length > 2 ? int.Parse(args[2]) : 4;
            ulong ps = args.Length > 3 ? ulong.Parse(args[3]) : 1;
            var gen = new MmaManager.Core.Sim.Generation.FighterGenerator(ps);
            var styles = Enum.GetValues<MmaManager.Core.Model.PrimaryStyle>();
            for (int i = 0; i < count; i++)
            {
                var sex = i % 2 == 0 ? MmaManager.Core.Model.Sex.Female : MmaManager.Core.Model.Sex.Male;
                var wc = sex == MmaManager.Core.Model.Sex.Female ? MmaManager.Core.Model.WeightClass.Flyweight : MmaManager.Core.Model.WeightClass.Lightweight;
                MmaManager.Core.Model.Fighter Make(int id) => gen.Generate(new MmaManager.Core.Model.FighterId(id), new DateOnly(2026, 1, 1),
                    new MmaManager.Core.Sim.Generation.GenOptions { Overall = 65, Style = styles[(id * 7) % styles.Length], Sex = sex, WeightClass = wc, WithRecord = false });
                var o = new FightEngine().Simulate(Make(2 * i + 1), Make(2 * i + 2),
                    new FightParams { ScheduledRounds = i % 4 < 2 ? 3 : 5, TextSeed = (ulong)i }, MmaManager.Core.Sim.Rng.SimRandom.Mix(ps, (ulong)i));
                Console.WriteLine($"##### {o.Method} R{o.EndRound} card [{string.Join(" ", o.RoundScores!.Select(s => $"{s.Red}-{s.Blue}"))}]");
                foreach (var line in o.PlayByPlay!) Console.WriteLine(line);
                Console.WriteLine();
            }
            return;
        }

        if (args.Length > 1 && args[1] == "sections")
        {
            // fightlab sections: what each block of the scale-1 report costs (wall clock).
            var b = FightLab.ReportOptions.Small with { IncludeStyles = false, IncludeAttributes = false, IncludeText = false, IncludeTiming = false };
            foreach (var (name, o) in new[] { ("populations only", b), ("+styles", b with { IncludeStyles = true }),
                         ("+attributes", b with { IncludeAttributes = true }), ("+text", b with { IncludeText = true }), ("+timing", b with { IncludeTiming = true }) })
            {
                var t0 = Stopwatch.StartNew(); FightLab.Report(o); Console.WriteLine($"  {name,-18} {t0.Elapsed.TotalSeconds,5:0.0} s");
            }
            var t1 = Stopwatch.StartNew(); FightLab.GoldenMaster(); Console.WriteLine($"  golden master      {t1.Elapsed.TotalSeconds,5:0.0} s");
            return;
        }
        if (args.Length > 1 && args[1] == "quick")
        {
            // fightlab quick [scale] [seed]: the populations only (method mix, divisions, upset curve,
            // cards) — no style matrix, attribute block, text or timing. For tuning loops.
            int qs = args.Length > 2 ? int.Parse(args[2]) : 1;
            ulong qseed = args.Length > 3 ? ulong.Parse(args[3]) : FightLab.ReportOptions.Small.Seed;
            var t = Stopwatch.StartNew();
            Console.Write(FightLab.Report(FightLab.ReportOptions.Small.Scaled(qs) with
            {
                Seed = qseed, IncludeStyles = false, IncludeAttributes = false, IncludeText = false, IncludeTiming = false,
            }).ToText());
            Console.WriteLine($"[fightlab quick scale {qs} seed {qseed}: {t.Elapsed.TotalSeconds:0.0} s]");
            return;
        }

        int scale = args.Length > 1 ? int.Parse(args[1]) : 1;
        ulong seed = args.Length > 2 ? ulong.Parse(args[2]) : FightLab.ReportOptions.Small.Seed;
        var opts = FightLab.ReportOptions.Small.Scaled(scale) with { Seed = seed };

        var sw = Stopwatch.StartNew();
        var report = FightLab.Report(opts);
        Console.Write(report.ToText());
        var g = FightLab.GoldenMaster();
        Console.WriteLine($"Golden master ({g.Bouts} bouts): results {g.Results}  scorecards {g.Scorecards}");
        Console.WriteLine($"[fightlab scale {scale} seed {seed}: {sw.Elapsed.TotalSeconds:0.0} s]");
    }
}
