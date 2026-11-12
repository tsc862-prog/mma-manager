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
/// shoesweep &lt;fromSeed&gt; &lt;toSeed&gt; &lt;years&gt; [--script-only] [--ai-renewals] [--no-resign] [--par N]: the shoestring
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
        public int ReSigned, Lapsed;
        public List<Year> Years = new();
    }

    public static void Main(string[] args)
    {
        ulong from = ulong.Parse(args[1]), to = ulong.Parse(args[2]);
        int years = int.Parse(args[3]);
        bool scriptOnly = args.Contains("--script-only");
        bool dealRules = PlayerDealRules(args), resign = !args.Contains("--no-resign");
        var runs = new ConcurrentBag<Run>();
        Parallel.ForEach(Enumerable.Range((int)from, (int)(to - from + 1)).Select(i => (ulong)i),
            new ParallelOptions { MaxDegreeOfParallelism = Parallelism(args) },
            seed => runs.Add(One(seed, years, scriptOnly, dealRules, resign)));

        var all = runs.OrderBy(r => r.Seed).ToList();
        Console.WriteLine($"=== SHOESWEEP seeds {from}-{to} · {years} years · {(scriptOnly ? "SCRIPT ONLY" : "default harness (world AI also signs)")}"
            + $" · {(dealRules ? (resign ? "player deal rules, script re-signs in the window" : "player deal rules, NO re-signing") : "AI renewals")} ===");
        Console.WriteLine($"  re-signed in the window (all seeds): {all.Sum(r => r.ReSigned)} · windows lapsed to free agency: {all.Sum(r => r.Lapsed)}");
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

    // Half the cores (at least 2) unless --par N says otherwise: a local run must not take the whole machine.
    private static int Parallelism(string[] args)
    {
        int i = Array.IndexOf(args, "--par");
        if (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int n) && n > 0) return n;
        return Math.Max(2, Environment.ProcessorCount / 2);
    }

    /// <summary>
    /// The founded promotion lives under the PLAYER's deal rules (Contracts.DealWindow):
    /// its deals end into a 30-day exclusive re-sign window and then free agency,
    /// instead of the AI's monthly renew-or-walk. Default on for shoestring and
    /// shoesweep since the founded org stands in for the player; --ai-renewals
    /// restores the old harness (the world's AI renews for it, poaching at expiry).
    /// </summary>
    public static bool PlayerDealRules(string[] args) => !args.Contains("--ai-renewals");

    /// <summary>
    /// The scripted owner's renewals, inside the window: re-sign at the table at his
    /// terms (the honest player path, NegotiationSession) when the bank has a runway,
    /// he isn't in free fall, and his number fits the shoestring purse budget or is
    /// within 1.5x what he is paid now. --no-resign skips this (the owner who lets
    /// every deal end). Called before each 4-week advance, so every 30-day window
    /// sees at least one pass. Returns how many were re-signed.
    /// </summary>
    public static int ReSignInWindow(World world, Promotion org, decimal maxAsk, FighterValuation valuation)
    {
        int n = 0;
        foreach (var f in world.RosterOf(org.Id).Where(f => DealWindow.InWindow(world, f)).OrderBy(f => f.Id.Value).ToList())
        {
            if (org.Balance.Amount < 100_000m) break;
            if (f.Record.CurrentStreak <= -3) continue;
            var exp = valuation.Evaluate(world, f);
            var session = NegotiationSession.Resume(world, f, org, exp);
            var offer = session.TheirTermsAsOffer(Negotiator.WantsExclusivity(org), clause: false);
            if (offer.PursePerFight.Amount > Math.Max(maxAsk, f.Contract!.PursePerFight.Amount * 1.5m)) continue;
            if (session.Table(offer).Verdict != TalkVerdict.Accept) continue;
            ContractLifecycle.Sign(world, f, offer, world.CurrentDate);
            n++;
        }
        return n;
    }

    private static double Median(List<double> s) => s.Count == 0 ? 0 : s.Count % 2 == 1 ? s[s.Count / 2] : (s[s.Count / 2 - 1] + s[s.Count / 2]) / 2;

    private static Run One(ulong seed, int years, bool scriptOnly, bool dealRules, bool resign)
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed, Founding = new WorldBuilder.FoundingSpec() });
        var nwf = world.Get(world.PlayerPromotion)!;
        world.PlayerPromotion = default;
        if (dealRules) DealWindow.HarnessPlayerRules(world, nwf.Id);
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
                for (int m = 0; m < 13 && !nwf.Defunct; m++)
                {
                    if (dealRules && resign && !nwf.Defunct) run.ReSigned += ReSignInWindow(world, nwf, MaxAsk(), valuation);
                    PlayerAgent();
                    var inWindow = dealRules ? world.RosterOf(nwf.Id).Where(f => DealWindow.InWindow(world, f)).Select(f => f.Id).ToList() : new();
                    tick.AdvanceWeeks(world, 4);
                    // a window that closed in these four weeks: he walked to free agency
                    run.Lapsed += inWindow.Count(id => world.Get(id) is { } f && f.Contract?.Promotion != nwf.Id && f.Status != FighterStatus.Retired);
                }
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
