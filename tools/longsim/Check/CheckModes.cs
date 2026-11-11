using System.Collections.Concurrent;
using System.Diagnostics;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Org;

namespace LongSim.Check;

/// <summary>
/// The checking modes of longsim:
///   check &lt;seeds&gt; &lt;years&gt; [--soak]        all-AI worlds, seeds 1..N, invariants at every month boundary
///   playcheck &lt;seeds&gt; &lt;years&gt; [--soak]    a scripted player (founded on odd seeds, takeover on even) plus the same invariants
///   determinism &lt;seeds&gt; &lt;years&gt;           every scenario run twice, end states compared
///   digest &lt;seeds&gt; &lt;years&gt;                 print end-state digests (run under Debug and Release and diff the text)
/// Flags: --par N (parallel runs), --tmp DIR (soak scratch), --soak-every N (months between digest compares), --hang SECS,
///        --seed-from N (seeds N..N+count-1), --only ai|founded|takeover, --samples N, --quiet.
/// </summary>
public static class CheckModes
{
    public sealed class Options
    {
        public int Seeds = 10, Years = 5, Par = Math.Max(1, Math.Min(6, Environment.ProcessorCount / 4));
        public int SeedFrom = 1;
        public bool Soak, Quiet, SelfTest;
        public int SoakEvery = 6, HangSecs = 900, Samples = 4;
        public string Tmp = Path.Combine(Path.GetTempPath(), "longsim-soak");
        public string? Only;
        public static Options Parse(string[] a)
        {
            var o = new Options();
            var pos = new List<string>();
            for (int i = 1; i < a.Length; i++)
            {
                switch (a[i])
                {
                    case "--soak": o.Soak = true; break;
                    case "--quiet": o.Quiet = true; break;
                    case "--selftest": o.SelfTest = true; break;
                    case "--par": o.Par = int.Parse(a[++i]); break;
                    case "--tmp": o.Tmp = a[++i]; break;
                    case "--soak-every": o.SoakEvery = int.Parse(a[++i]); break;
                    case "--hang": o.HangSecs = int.Parse(a[++i]); break;
                    case "--seed-from": o.SeedFrom = int.Parse(a[++i]); break;
                    case "--only": o.Only = a[++i]; break;
                    case "--samples": o.Samples = int.Parse(a[++i]); break;
                    default: pos.Add(a[i]); break;
                }
            }
            if (pos.Count > 0) o.Seeds = int.Parse(pos[0]);
            if (pos.Count > 1) o.Years = int.Parse(pos[1]);
            return o;
        }
    }

    private sealed class RunResult
    {
        public string Label = "";
        public DateOnly End;
        public int Months;
        public double TickSecs, CheckSecs, WallSecs;
        public bool Crashed;
        public string Note = "";
        public string Strict = "", Canonical = "";
        public int Forks, RoundTripDiffs, ValueDiffs, OrderDiffs;
        public List<string> SoakNotes = new();
        public int Fighters, Events;
    }

    private static string BuildInfo =>
#if DEBUG
        "Debug";
#else
        "Release";
#endif

    private static readonly (string, string)[] RunCatalog =
    {
        ("CRASH",           "an exception escaped the sim, the script or a UI call"),
        ("HANG",            "a run made no progress for the watchdog window"),
        ("SOAK-ROUNDTRIP",  "save then load gives a different world from the one saved (a value differs)"),
        ("SOAK-VALUE",      "the loaded twin and the never-saved world diverged in a VALUE after continuing"),
        ("SOAK-ORDER",      "same values but dictionary enumeration order differs (documented after a non-newest removal); NOT counted as failing"),
        ("NONDETERMINISM",  "the same seed and code produced a different world on a second run"),
    };

    // -------------------------------------------------------------------------- check / playcheck

    public static int Run(string mode, string[] args)
    {
        var o = Options.Parse(args);
        bool play = mode == "playcheck";
        var log = new ViolationLog();
        foreach (var (n, d) in WorldChecker.Catalog) log.Declare(n, d);
        foreach (var (n, d) in RunCatalog) log.Declare(n, d);
        if (play) foreach (var (n, d) in PlayerScript.PlayCatalog) log.Declare(n, d);
        Directory.CreateDirectory(o.Tmp);
        if (o.SelfTest) SelfTestSoakDetector();

        var jobs = Jobs(o, play);
        Console.WriteLine($"=== {mode.ToUpperInvariant()} — {jobs.Count} runs, {o.Years} years each, build {BuildInfo}, {Environment.ProcessorCount} cores, par {o.Par}{(o.Soak ? ", save/reload soak on" : "")} ===");
        Console.WriteLine($"    started {DateTime.Now:HH:mm:ss}; seeds {o.SeedFrom}..{o.SeedFrom + o.Seeds - 1}");

        var results = new ConcurrentDictionary<string, RunResult>();
        var live = new ConcurrentDictionary<string, SimRun>();
        var wall = Stopwatch.StartNew();
        using var watchdog = Watchdog(live, log, o, results, wall);

        Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = o.Par }, job =>
        {
            var res = Execute(job.Seed, job.Kind, job.Idx, o, log, play, live);
            results[res.Label] = res;
            if (!o.Quiet)
                Console.WriteLine($"  done {res.Label,-16} {res.End:yyyy-MM-dd} {res.Months,3} months  tick {res.TickSecs,6:0}s check {res.CheckSecs,5:0}s  {res.Note}");
        });

        return Report(mode, o, log, results, wall.Elapsed);
    }

    private static List<(ulong Seed, RunKind Kind, int Idx)> Jobs(Options o, bool play)
    {
        var jobs = new List<(ulong, RunKind, int)>();
        for (int i = 0; i < o.Seeds; i++)
        {
            ulong seed = (ulong)(o.SeedFrom + i);
            if (play) { var (k, ix) = SimRun.PlayScenario(seed); jobs.Add((seed, k, ix)); }
            else jobs.Add((seed, RunKind.Ai, 0));
        }
        if (o.Only is not null)
            jobs = jobs.Where(j => j.Item2.ToString().Equals(o.Only, StringComparison.OrdinalIgnoreCase)).ToList();
        return jobs;
    }

    private static RunResult Execute(ulong seed, RunKind kind, int idx, Options o, ViolationLog log, bool play, ConcurrentDictionary<string, SimRun> live)
    {
        var sw = Stopwatch.StartNew();
        var run = SimRun.Create(seed, kind, idx, log, script: play);
        live[run.Label] = run;
        var res = new RunResult { Label = run.Label };
        SimRun? twin = null;
        int months = o.Years * 12;
        var soakAt = o.Soak ? SoakMonths(seed, months) : new HashSet<int>();
        int soaks = 0;
        var soakFile = Path.Combine(o.Tmp, $"{run.Label.Replace('#', '_')}.sav");

        if (o.SelfTest) run.Checker.InjectBeforeCheck = SelfTestInjection(run);
        try
        {
            run.CheckNow();
            for (int m = 1; m <= months; m++)
            {
                run.StepMonth();
                if (twin is not null) twin.StepMonth();

                if (twin is not null && (m % Math.Max(1, o.SoakEvery) == 0 || soakAt.Contains(m)))
                {
                    var bad = CompareTwins(run, twin, log, res, strict: soakAt.Contains(m));
                    if (bad) twin = null;   // reported; the next soak point forks afresh
                }
                if (soakAt.Contains(m))
                {
                    res.Forks++;
                    var (t, rt) = run.ForkViaSave(soakFile, log);
                    soaks++;
                    if (rt.Values.Count > 0)
                    {
                        res.RoundTripDiffs++;
                        log.Report("SOAK-ROUNDTRIP", run.Label, run.World.CurrentDate, $"rt{m}:{rt.Values[0]}",
                            $"save/load of the world at {run.World.CurrentDate:yyyy-MM-dd} (month {m}) differs on {rt.Values.Count}+ value(s); first: {rt.Values[0]}" + (rt.Values.Count > 1 ? $"; then: {rt.Values[1]}" : ""));
                    }
                    if (rt.Order.Count > 0)
                        log.Report("SOAK-ORDER", run.Label, run.World.CurrentDate, $"rt-order{m}", $"immediately after load: {rt.Order[0]}" + (rt.Order.Count > 1 ? $" (+{rt.Order.Count - 1} more)" : ""));
                    twin = t;
                    twin.CheckNow();
                }
            }
            if (twin is not null) CompareTwins(run, twin, log, res, strict: true);
        }
        catch (Exception e)
        {
            res.Crashed = true;
            log.Report("CRASH", run.Label, run.World.CurrentDate, $"{e.GetType().Name}:{PlayerScript.FirstFrame(e)}",
                $"{e.GetType().Name}: {e.Message} @ {PlayerScript.FirstFrame(e)}" + (run.Script is { } s ? $" — recent actions: {s.Recent(8)}" : ""));
            res.Note = "CRASHED";
        }

        res.End = run.World.CurrentDate;
        res.Months = run.Months;
        res.TickSecs = run.TickMillis / 1000.0;
        res.CheckSecs = run.CheckMillis / 1000.0;
        res.WallSecs = sw.Elapsed.TotalSeconds;
        res.Fighters = run.World.Fighters.Count;
        res.Events = run.World.Events.Count;
        res.Strict = WorldGraph.Digest(run.World, canonical: false);
        res.Canonical = WorldGraph.Digest(run.World, canonical: true);
        if (run.Script is { } sc)
        {
            var p = run.World.Get(run.World.PlayerPromotion);
            res.Note = $"{(res.Crashed ? "CRASHED " : "")}{run.Kind}{(run.Kind == RunKind.Takeover ? $" {run.World.Get(run.World.PlayerPromotion)?.Abbreviation}" : "")}: {sc.ActionsTaken} actions"
                + (p is null ? "" : $", {(p.Defunct ? "BANKRUPT" : OrgSizing.Label(OrgSizing.SizeOf(p)))}, bank {p.Balance}, roster {run.World.RosterOf(p.Id).Count()}"
                   + (p.Defunct && p.Ledger.Count > 0 ? $", folded {p.Ledger[^1].Date:yyyy-MM}" : "")
                   + $" | ledger " + string.Join(" ", p.Ledger.GroupBy(l => l.Category).OrderBy(g => g.Sum(l => l.Amount.Amount)).Take(4).Select(g => $"{g.Key}={g.Sum(l => l.Amount.Amount) / 1000m:N0}k"))
                   + $" | actions " + string.Join(" ", sc.Counts.OrderByDescending(kv => kv.Value).Take(8).Select(kv => $"{kv.Key}x{kv.Value}")));
        }
        live.TryRemove(run.Label, out _);
        return res;
    }

    /// <summary>Compare the never-saved world with its loaded twin. True when they differ and were reported.</summary>
    private static bool CompareTwins(SimRun orig, SimRun twin, ViolationLog log, RunResult res, bool strict)
    {
        string a = WorldGraph.Digest(orig.World, canonical: true);
        string b = WorldGraph.Digest(twin.World, canonical: true);
        bool valueDiff = a != b;
        bool orderDiff = false;
        if (!valueDiff && strict) orderDiff = WorldGraph.Digest(orig.World, false) != WorldGraph.Digest(twin.World, false);
        if (!valueDiff && !orderDiff) return false;

        var d = WorldGraph.Diff(orig.World, twin.World);
        var date = orig.World.CurrentDate;
        if (d.Values.Count > 0 || valueDiff)
        {
            res.ValueDiffs++;
            string first = d.Values.Count > 0 ? d.Values[0] : "(digests differ; no field-level difference found by the comparer — a member the comparer does not see)";
            log.Report("SOAK-VALUE", orig.Label, date, $"v:{first}",
                $"never-saved and loaded-then-continued worlds differ at {date:yyyy-MM-dd} (the twin was loaded from a save of this world): first diverging field {first}" +
                (d.Values.Count > 1 ? $"; next {d.Values[1]}" : "") + (d.Order.Count > 0 ? $"; also {d.Order.Count} key-order difference(s)" : ""));
        }
        else if (d.Order.Count > 0)
        {
            res.OrderDiffs++;
            log.Report("SOAK-ORDER", orig.Label, date, $"o:{d.Order[0]}", $"values identical at {date:yyyy-MM-dd} but {d.Order.Count} dictionary(ies) enumerate in a different order, first: {d.Order[0]}" + (d.Order.Count > 1 ? $"; then {d.Order[1]}" : ""));
        }
        return true;
    }

    /// <summary>
    /// --selftest: prove the checker can fail. In month 3 of each run, break the world in memory five ways and let the
    /// checker find them: a retired fighter in a ranking, a balance moved with no ledger line, an id that resolves to
    /// nothing, two open reigns on one belt, and a card booking the same fighter twice. Never used outside this flag.
    /// </summary>
    private static Action<World> SelfTestInjection(SimRun run) => w =>
    {
        if (run.Months != 3) return;
        var promo = w.Promotions.Values.Where(p => !p.Defunct && p.Divisions.Count > 0).OrderBy(p => p.Id.Value).First();
        var retired = w.Fighters.Values.Where(f => f.Status == FighterStatus.Retired).OrderBy(f => f.Id.Value).First();
        promo.Divisions[0].Rankings.Add(retired.Id);                                   // RANK-CONTRACT
        promo.Balance += new Money(1m);                                                // LEDGER
        w.Shortlist.Add(new FighterId(987654321));                                     // REF-DANGLING
        var title = w.Titles.Values.Where(t => t.CurrentChampion is not null).OrderBy(t => t.Id.Value).First();
        title.Lineage.Add(new TitleReign { Champion = title.CurrentChampion!.Value, WonOn = w.CurrentDate });   // LINEAGE (two open reigns)
        var live = w.Fighters.Values.First(f => f.Status == FighterStatus.Active);
        var ev = new Event { Id = w.NewEventId(), Promotion = promo.Id, Name = "SELFTEST double booking", Date = w.CurrentDate.AddDays(10) };
        ev.Card.Add(new Fight { Id = w.NewFightId(), RedCorner = live.Id, BlueCorner = live.Id });   // BOOK-DOUBLE-DATE (fights himself)
        w.Events[ev.Id] = ev;
    };

    /// <summary>--selftest, soak half: the comparer must tell a value difference from an order-only one (otherwise SOAK-ORDER = 0 proves nothing).</summary>
    private static void SelfTestSoakDetector()
    {
        World Two(bool reversed)
        {
            var w = new World();
            var ids = reversed ? new[] { 3, 2, 1 } : new[] { 1, 2, 3 };
            foreach (var i in ids)
                w.Countries[new CountryId(i)] = new Country { Id = new CountryId(i), Name = "C" + i, Code = "C" + i, Continent = Region.Europe };
            return w;
        }
        var order = WorldGraph.Diff(Two(false), Two(true));
        var w3 = Two(false); w3.Countries[new CountryId(2)].MmaInterest = 99;
        var value = WorldGraph.Diff(Two(false), w3);
        var same = WorldGraph.Diff(Two(false), Two(false));
        bool ok = order.Values.Count == 0 && order.Order.Count > 0 && value.Values.Count > 0 && same.Identical
            && WorldGraph.Digest(Two(false), true) == WorldGraph.Digest(Two(true), true)
            && WorldGraph.Digest(Two(false), false) != WorldGraph.Digest(Two(true), false)
            && WorldGraph.Digest(Two(false), true) != WorldGraph.Digest(w3, true);
        Console.WriteLine($"selftest soak detector: {(ok ? "OK" : "FAILED")} (order-only: {order.Order.Count} order/{order.Values.Count} value; value change: {value.Values.Count} value; identical: {same.Identical})");
        if (!ok) Environment.ExitCode = 3;
    }

    private static HashSet<int> SoakMonths(ulong seed, int months)
    {
        var set = new HashSet<int>();
        if (months < 8) { set.Add(Math.Max(1, months / 2)); return set; }
        var rng = new MmaManager.Core.Sim.Rng.SimRandom(SimRun.HashSeed(seed, "soak"));
        int lo = Math.Min(6, months / 4), hi = months - 2;
        for (int i = 0; i < 3; i++) set.Add(rng.Range(lo, hi));
        return set;
    }

    private static Timer Watchdog(ConcurrentDictionary<string, SimRun> live, ViolationLog log, Options o,
        ConcurrentDictionary<string, RunResult> results, Stopwatch wall)
    {
        var last = new ConcurrentDictionary<string, (int Beat, DateTime At)>();
        return new Timer(_ =>
        {
            foreach (var (label, run) in live)
            {
                int beat = run.Heartbeat;
                if (!last.TryGetValue(label, out var prev) || prev.Beat != beat) { last[label] = (beat, DateTime.UtcNow); continue; }
                if ((DateTime.UtcNow - prev.At).TotalSeconds > o.HangSecs)
                {
                    log.Report("HANG", label, run.World.CurrentDate, "hang", $"no week completed for {o.HangSecs}s at {run.World.CurrentDate:yyyy-MM-dd}" + (run.Script is { } s ? $"; recent actions: {s.Recent(8)}" : ""));
                    Console.WriteLine($"HANG: {label} stuck at {run.World.CurrentDate:yyyy-MM-dd} for {o.HangSecs}s — aborting the process.");
                    Console.WriteLine(log.Render(o.Samples));
                    Environment.Exit(4);
                }
            }
        }, null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
    }

    private static int Report(string mode, Options o, ViolationLog log, IDictionary<string, RunResult> results, TimeSpan wall)
    {
        Console.WriteLine();
        Console.WriteLine($"=== {mode.ToUpperInvariant()} RESULT — wall {wall.TotalMinutes:0.0} min, build {BuildInfo} ===");
        Console.WriteLine($"{"run",-16} {"end",-10} {"months",6} {"tick s",7} {"check s",7} {"fighters",8} {"events",7}  note");
        foreach (var r in results.Values.OrderBy(r => r.Label, StringComparer.Ordinal))
            Console.WriteLine($"{r.Label,-16} {r.End:yyyy-MM-dd} {r.Months,6} {r.TickSecs,7:0} {r.CheckSecs,7:0} {r.Fighters,8} {r.Events,7}  {r.Note}"
                + (o.Soak ? $"  [soak: {r.Forks} forks, {r.RoundTripDiffs} roundtrip, {r.ValueDiffs} value, {r.OrderDiffs} order]" : ""));
        Console.WriteLine();
        Console.WriteLine(log.Render(o.Samples));

        int failing = log.TotalDistinct - log.Count("SOAK-ORDER");
        Console.WriteLine(failing == 0
            ? $"RESULT: no violations ({log.Count("SOAK-ORDER")} order-only soak difference(s))."
            : $"RESULT: {failing} failing finding(s) (plus {log.Count("SOAK-ORDER")} order-only soak difference(s)).");
        return failing == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------- determinism / digest

    public static int Determinism(string[] args)
    {
        var o = Options.Parse(args);
        var log = new ViolationLog();
        foreach (var (n, d) in RunCatalog) log.Declare(n, d);
        foreach (var (n, d) in WorldChecker.Catalog) log.Declare(n, d);
        foreach (var (n, d) in PlayerScript.PlayCatalog) log.Declare(n, d);
        var jobs = new List<(ulong Seed, RunKind Kind, int Idx)>();
        for (int i = 0; i < o.Seeds; i++)
        {
            ulong seed = (ulong)(o.SeedFrom + i);
            jobs.Add((seed, RunKind.Ai, 0));
            var (k, ix) = SimRun.PlayScenario(seed); jobs.Add((seed, k, ix));
        }
        Console.WriteLine($"=== DETERMINISM — {jobs.Count} scenarios x 2 runs, {o.Years} years, build {BuildInfo} ===");
        var rows = new ConcurrentBag<string>();
        var wall = Stopwatch.StartNew();
        Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = o.Par }, job =>
        {
            string[] series = new string[2]; string[] strict = new string[2], canon = new string[2];
            for (int pass = 0; pass < 2; pass++)
            {
                var run = SimRun.Create(job.Seed, job.Kind, job.Idx, log, script: job.Kind != RunKind.Ai);
                var sb = new System.Text.StringBuilder();
                for (int m = 1; m <= o.Years * 12; m++)
                {
                    run.StepMonth(check: false);
                    if (m % 6 == 0) sb.Append(WorldGraph.Digest(run.World, true)).Append(',');
                }
                series[pass] = sb.ToString();
                strict[pass] = WorldGraph.Digest(run.World, false);
                canon[pass] = WorldGraph.Digest(run.World, true);
            }
            string label = SimRun.LabelFor(job.Seed, job.Kind, job.Idx);
            bool same = strict[0] == strict[1] && canon[0] == canon[1];
            string where = "";
            if (!same)
            {
                var a = series[0].Split(','); var b = series[1].Split(',');
                int i = 0; while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
                where = $" first differing half-year checkpoint #{i + 1} (months {(i + 1) * 6 - 5}..{(i + 1) * 6})";
                log.Report("NONDETERMINISM", label, new DateOnly(2026, 1, 1), "nd", $"two runs of {label} for {o.Years} years ended in different worlds;{where}; strict {strict[0]} vs {strict[1]}, canonical {canon[0]} vs {canon[1]}");
            }
            rows.Add($"  {label,-16} {(same ? "identical" : "DIFFERENT"),-10} strict {strict[0]} canonical {canon[0]}{where}");
        });
        foreach (var r in rows.OrderBy(r => r, StringComparer.Ordinal)) Console.WriteLine(r);
        Console.WriteLine($"wall {wall.Elapsed.TotalMinutes:0.0} min");
        Console.WriteLine(log.Count("NONDETERMINISM") == 0 ? "RESULT: deterministic." : $"RESULT: {log.Count("NONDETERMINISM")} nondeterministic scenario(s).\n" + log.Render(o.Samples));
        return log.Count("NONDETERMINISM") == 0 ? 0 : 1;
    }

    public static int Digest(string[] args)
    {
        var o = Options.Parse(args);
        var log = new ViolationLog();
        var jobs = new List<(ulong Seed, RunKind Kind, int Idx)>();
        for (int i = 0; i < o.Seeds; i++)
        {
            ulong seed = (ulong)(o.SeedFrom + i);
            jobs.Add((seed, RunKind.Ai, 0));
            var (k, ix) = SimRun.PlayScenario(seed); jobs.Add((seed, k, ix));
        }
        var rows = new ConcurrentBag<string>();
        Parallel.ForEach(jobs, new ParallelOptions { MaxDegreeOfParallelism = o.Par }, job =>
        {
            var run = SimRun.Create(job.Seed, job.Kind, job.Idx, log, script: job.Kind != RunKind.Ai);
            for (int m = 1; m <= o.Years * 12; m++) run.StepMonth(check: false);
            rows.Add($"DIGEST {run.Label,-16} {run.World.CurrentDate:yyyy-MM-dd} strict {WorldGraph.Digest(run.World, false)} canonical {WorldGraph.Digest(run.World, true)}");
        });
        foreach (var r in rows.OrderBy(r => r, StringComparer.Ordinal)) Console.WriteLine(r);
        return 0;
    }
}
