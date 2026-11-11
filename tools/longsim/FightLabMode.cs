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
