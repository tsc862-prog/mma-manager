using System.Diagnostics;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Living;

namespace LongSim;

/// <summary>
/// tickprofile &lt;seed&gt; &lt;years&gt; [--origins] [--counts]: advance one all-AI world week by week and say where
/// WorldTick.AdvanceWeek spends its time, per phase, in ms/week for each simulated year. The sim only names
/// its phases (Core.Sim/Living/TickProbe); the stopwatch is here. Phases nest: an inner phase's time is also
/// inside its outer phase's. Run one at a time, Release build: the numbers are wall time.
/// </summary>
public static class TickProfile
{
    private sealed class Probe : ITickProbe
    {
        public readonly Dictionary<string, long> Ticks = new();
        public readonly Dictionary<string, long> Calls = new();
        public readonly Dictionary<string, long> Counts = new();
        private readonly Stack<(string Name, long Start)> _open = new();

        public void Enter(string phase) => _open.Push((phase, Stopwatch.GetTimestamp()));
        public void Exit(string phase)
        {
            var (name, start) = _open.Pop();
            if (name != phase) throw new InvalidOperationException($"probe: exit {phase} while {name} is open");
            long d = Stopwatch.GetTimestamp() - start;
            Ticks[name] = Ticks.GetValueOrDefault(name) + d;
            Calls[name] = Calls.GetValueOrDefault(name) + 1;
        }
        public void Count(string helper) => Counts[helper] = Counts.GetValueOrDefault(helper) + 1;
    }

    public static int Run(string[] args)
    {
        ulong seed = args.Length > 1 ? ulong.Parse(args[1]) : 11UL;
        int years = args.Length > 2 ? int.Parse(args[2]) : 8;
        bool origins = args.Contains("--origins");

        var world = new WorldBuilder().Build(origins ? WorldBuilder.Options.Origins(seed, 0) : new WorldBuilder.Options { Seed = seed });
        if (!origins)
        {
            world.PlayerPromotion = default;
            foreach (var p in world.Promotions.Values) p.IsPlayerControlled = false;
        }
        else world.PlayerPromotion = default;
        var tick = new WorldTick();
        var perYear = new List<(int Year, int Weeks, double TotalMs, Probe Probe, int Events, int Fighters, int Active)>();

        Console.WriteLine($"tickprofile seed {seed}, {years} years, {(origins ? "Origins" : "Modern")} world, all-AI; start {world.CurrentDate:yyyy-MM-dd}, {world.Events.Count} events, {world.Fighters.Count} fighters");
        var wall = Stopwatch.StartNew();
        for (int y = 1; y <= years; y++)
        {
            var probe = new Probe();
            TickProbe.Current = probe;
            long total = 0;
            for (int w = 0; w < 52; w++)
            {
                long t0 = Stopwatch.GetTimestamp();
                tick.AdvanceWeek(world);
                total += Stopwatch.GetTimestamp() - t0;
            }
            TickProbe.Current = null;
            int active = world.Fighters.Values.Count(f => f.Status == FighterStatus.Active);
            perYear.Add((y, 52, total * 1000.0 / Stopwatch.Frequency, probe, world.Events.Count, world.Fighters.Count, active));
            Console.WriteLine($"  year {y}: {total * 1000.0 / Stopwatch.Frequency / 52,7:0.0} ms/week  ({world.CurrentDate:yyyy-MM-dd}, {world.Events.Count} events, {world.Fighters.Count} fighters, {active} active, {world.News.Count} news, {world.Relationships.Count} bonds, {world.Rivalries.Count} rivalries, {world.Mail.Count} mail, {world.Titles.Values.Sum(t => t.Lineage.Count)} reigns)  wall {wall.Elapsed.TotalSeconds:0}s");
        }

        // Columns: years 1, 4, 8 (or whatever of them ran) plus the last.
        var cols = new[] { 1, 4, 8 }.Where(c => c <= years).ToList();
        if (!cols.Contains(years)) cols.Add(years);
        double Ms(Probe p, string k) => p.Ticks.GetValueOrDefault(k) * 1000.0 / Stopwatch.Frequency / 52;

        Console.WriteLine();
        Console.WriteLine($"{"phase (ms/week)",-34}" + string.Join("", cols.Select(c => $"{"y" + c,9}")) + $"{"growth",9}{"calls/wk",10}{"us/call",9}");
        var names = perYear.SelectMany(r => r.Probe.Ticks.Keys).Distinct()
            .OrderByDescending(k => Ms(perYear[^1].Probe, k)).ThenBy(k => k, StringComparer.Ordinal).ToList();
        foreach (var k in names)
        {
            var vals = cols.Select(c => Ms(perYear[c - 1].Probe, k)).ToList();
            string growth = vals[0] > 0.05 ? $"{vals[^1] / vals[0],8:0.0}x" : "        -";
            var last = perYear[cols[^1] - 1].Probe;
            double calls = last.Calls.GetValueOrDefault(k) / 52.0;
            string per = calls > 0 ? $"{vals[^1] * 1000 / calls,9:0}" : "        -";
            Console.WriteLine($"{k,-34}" + string.Join("", vals.Select(v => $"{v,9:0.00}")) + growth + $"{calls,10:0.0}" + per);
        }
        var tot = cols.Select(c => perYear[c - 1].TotalMs / 52).ToList();
        Console.WriteLine($"{"TOTAL AdvanceWeek",-34}" + string.Join("", tot.Select(v => $"{v,9:0.00}")) + $"{tot[^1] / tot[0],8:0.0}x");
        Console.WriteLine($"{"events in world",-34}" + string.Join("", cols.Select(c => $"{perYear[c - 1].Events,9}")));
        Console.WriteLine($"{"fighters in world",-34}" + string.Join("", cols.Select(c => $"{perYear[c - 1].Fighters,9}")));
        Console.WriteLine($"{"active fighters",-34}" + string.Join("", cols.Select(c => $"{perYear[c - 1].Active,9}")));

        var countKeys = perYear.SelectMany(r => r.Probe.Counts.Keys).Distinct().OrderBy(k => k, StringComparer.Ordinal).ToList();
        if (countKeys.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"{"helper calls (per week)",-34}" + string.Join("", cols.Select(c => $"{"y" + c,9}")));
            foreach (var k in countKeys)
                Console.WriteLine($"{k,-34}" + string.Join("", cols.Select(c => $"{perYear[c - 1].Probe.Counts.GetValueOrDefault(k) / 52.0,9:0.0}")));
        }
        Console.WriteLine($"wall {wall.Elapsed.TotalMinutes:0.0} min");
        return 0;
    }
}
