using System.Collections.Concurrent;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Org;
using MmaManager.Core.Sim.Scouting;

namespace LongSim;

/// <summary>
/// shoesweep &lt;fromSeed&gt; &lt;toSeed&gt; &lt;years&gt; [--script-only]: the shoestring
/// playthrough (same founding, same scripted owner as <c>shoestring</c>) over many
/// seeds in parallel, reported as a distribution: final prestige (mean, median),
/// share reaching each tier, median year of each tier crossing, and the yearly
/// money lines averaged over seeds — plus the MARKET the script shops in each
/// year (free agents in its divisions, how many it can afford, the best
/// scouted read among those). Reporting only; the script reads true ability
/// exactly as <c>shoestring</c> does.
/// </summary>
public static class ShoeSweep
{
    private sealed class Year
    {
        public int Prestige, Roster, Hires, Cards, FaPool, FaHome, Affordable, BestRead, AffordableTop5True;
        public double RosterCa, Payroll, Gate, Locals, Bouts;
    }

    private sealed class Run
    {
        public ulong Seed;
        public bool Folded;
        public int FinalPrestige;
        public OrgSize FinalTier;
        public Dictionary<OrgSize, int> Crossed = new();
        public List<Year> Years = new();
    }

    public static void Main(string[] args)
    {
        ulong from = ulong.Parse(args[1]), to = ulong.Parse(args[2]);
        int years = int.Parse(args[3]);
        bool scriptOnly = args.Contains("--script-only");
        var runs = new ConcurrentBag<Run>();
        Parallel.ForEach(Enumerable.Range((int)from, (int)(to - from + 1)).Select(i => (ulong)i),
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount) },
            seed => runs.Add(One(seed, years, scriptOnly)));

        var all = runs.OrderBy(r => r.Seed).ToList();
        Console.WriteLine($"=== SHOESWEEP seeds {from}-{to} · {years} years · {(scriptOnly ? "SCRIPT ONLY" : "default harness (world AI also signs)")} ===");
        Console.WriteLine("  per seed: " + string.Join(" ", all.Select(r => $"{r.Seed}:{r.FinalPrestige}{(r.Folded ? "F" : "")}")));
        var finals = all.Select(r => (double)r.FinalPrestige).OrderBy(x => x).ToList();
        Console.WriteLine($"  final prestige: mean {finals.Average():0.0} · median {Median(finals):0.0} · min {finals.Min()} · max {finals.Max()} · folded {all.Count(r => r.Folded)}/{all.Count}");
        foreach (var t in new[] { OrgSize.Regional, OrgSize.National, OrgSize.Continental })
        {
            var ys = all.Where(r => r.Crossed.ContainsKey(t)).Select(r => (double)r.Crossed[t]).OrderBy(x => x).ToList();
            Console.WriteLine($"  reached {OrgSizing.Label(t),-12} {ys.Count,3}/{all.Count} · median crossing year {(ys.Count == 0 ? "-" : Median(ys).ToString("0.0"))}");
        }
        Console.WriteLine("  yearly means over seeds:  yr | prestige roster hires(cum) rosterCA | cards payroll/card gate/card bouts/card locals/card | FA pool (home) affordable bestRead top5TrueCA(affordable)");
        for (int y = 0; y < years; y++)
        {
            var ys = all.Where(r => r.Years.Count > y).Select(r => r.Years[y]).ToList();
            if (ys.Count == 0) break;
            Console.WriteLine($"    {y + 1,2} | {ys.Average(v => v.Prestige),5:0.0} {ys.Average(v => v.Roster),5:0.0} {ys.Average(v => v.Hires),6:0.0} {ys.Average(v => v.RosterCa),6:0.0} | "
                + $"{ys.Average(v => v.Cards),4:0.0} {ys.Average(v => v.Payroll),9:N0} {ys.Average(v => v.Gate),9:N0} {ys.Average(v => v.Bouts),4:0.0} {ys.Average(v => v.Locals),4:0.0} | "
                + $"{ys.Average(v => v.FaPool),5:0} ({ys.Average(v => v.FaHome),4:0}) {ys.Average(v => v.Affordable),5:0.0} {ys.Average(v => v.BestRead),5:0.0} {ys.Average(v => v.AffordableTop5True),5:0.0}");
        }
    }

    private static double Median(List<double> s) => s.Count == 0 ? 0 : s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;

    private static Run One(ulong seed, int years, bool scriptOnly)
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed, Founding = new WorldBuilder.FoundingSpec() });
        var nwf = world.Get(world.PlayerPromotion)!;
        world.PlayerPromotion = default;
        FreeAgencyService.HarnessHandsOff = scriptOnly ? nwf.Id : null;
        var tick = new WorldTick();
        var valuation = new FighterValuation();
        int hires = 0;
        decimal MaxAsk() => 3_000m + nwf.Prestige.Value * 300m;

        // The same scripted owner as `shoestring` (Program.cs RunShoestring).
        void PlayerAgent()
        {
            if (nwf.Defunct) return;
            int depth = 4 + (nwf.Balance.Amount > 600_000m ? 1 : 0) + (nwf.Balance.Amount > 1_500_000m ? 1 : 0);
            decimal maxAsk = MaxAsk();
            for (int h = 0; h < 3; h++)
            {
                if (nwf.Balance.Amount < 100_000m) return;
                var thin = nwf.Divisions
                    .Where(d => world.RosterOf(nwf.Id).Count(f => d.Matches(f) && f.Status == FighterStatus.Active) < depth)
                    .ToList();
                if (thin.Count == 0) return;
                var pick = world.FreeAgents()
                    .Where(f => f.Status == FighterStatus.Active && thin.Any(d => d.Matches(f)))
                    .Select(f => (Fighter: f, Exp: valuation.Evaluate(world, f)))
                    .Where(x => x.Exp.AskingPurse.Amount <= maxAsk)
                    .OrderByDescending(x => FighterAbility.CurrentAbility(x.Fighter)
                        + Math.Max(0, x.Fighter.PotentialAbility.Value - FighterAbility.CurrentAbility(x.Fighter)) * 0.5)
                    .FirstOrDefault();
                if (pick.Fighter is null) return;
                hires++;
                ContractLifecycle.Sign(world, pick.Fighter, new ContractOffer
                {
                    Fighter = pick.Fighter.Id, Promotion = nwf.Id,
                    PursePerFight = pick.Exp.AskingPurse, WinBonus = pick.Exp.AskingWinBonus, Fights = pick.Exp.DesiredFights,
                }, world.CurrentDate);
            }
        }

        var run = new Run { Seed = seed };
        var lastTier = OrgSizing.SizeOf(nwf);
        try
        {
            for (int year = 1; year <= years; year++)
            {
                for (int m = 0; m < 13 && !nwf.Defunct; m++) { PlayerAgent(); tick.AdvanceWeeks(world, 4); }
                if (nwf.Defunct) { run.Folded = true; break; }
                var tier = OrgSizing.SizeOf(nwf);
                for (var t = lastTier + 1; t <= tier; t++) run.Crossed.TryAdd(t, year);
                lastTier = tier > lastTier ? tier : lastTier;

                var evs = world.Events.Values.Where(e => e.Promotion == nwf.Id && e.Financials is not null && e.Date > world.CurrentDate.AddDays(-365)).ToList();
                double cards = Math.Max(1, evs.Count);
                var roster = world.RosterOf(nwf.Id).Where(f => f.Status == FighterStatus.Active).ToList();
                // The market the script shops: free agents in its divisions (home region in brackets).
                var pool = world.FreeAgents().Where(f => f.Status == FighterStatus.Active && nwf.Divisions.Any(d => d.Matches(f))).ToList();
                var affordable = pool.Where(f => valuation.Evaluate(world, f).AskingPurse.Amount <= MaxAsk()).ToList();
                run.Years.Add(new Year
                {
                    Prestige = nwf.Prestige.Value, Roster = roster.Count, Hires = hires, Cards = evs.Count,
                    RosterCa = roster.Count == 0 ? 0 : roster.Average(f => (double)FighterAbility.CurrentAbility(f)),
                    Payroll = (double)evs.Sum(e => e.Financials!.FighterPayroll.Amount) / cards,
                    Gate = (double)evs.Sum(e => e.Financials!.GateRevenue.Amount) / cards,
                    Bouts = evs.Sum(e => e.Card.Count) / cards,
                    Locals = evs.Sum(e => e.Card.Count(fi => (world.Get(fi.RedCorner)?.LocalTalent ?? false) || (world.Get(fi.BlueCorner)?.LocalTalent ?? false))) / cards,
                    FaPool = pool.Count, FaHome = pool.Count(f => f.Nationality == nwf.BaseRegion),
                    Affordable = affordable.Count,
                    BestRead = affordable.Count == 0 ? 0 : affordable.Max(f => ScoutingService.PerceivedAbility(world, nwf, f)),
                    AffordableTop5True = affordable.Count == 0 ? 0 : (int)Math.Round(affordable.Select(f => FighterAbility.CurrentAbility(f)).OrderByDescending(x => x).Take(5).Average()),
                });
            }
        }
        finally { FreeAgencyService.HarnessHandsOff = null; }
        run.FinalPrestige = nwf.Prestige.Value;
        run.FinalTier = OrgSizing.SizeOf(nwf);
        return run;
    }
}
