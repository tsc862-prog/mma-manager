using System.Collections.Concurrent;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Org;

// Modes:
//   (default)            single 30-year sim with yearly breakdowns (seed 2026)
//   compare <n> <years>  n seeds (1..n) x years, run in parallel, comparison report
// Checking modes (simulation QA) live in Check/: invariants, scripted player, save/reload soak, determinism.
if (args.Length > 0 && (args[0] == "check" || args[0] == "playcheck"))
{ Environment.ExitCode = LongSim.Check.CheckModes.Run(args[0], args); return; }
if (args.Length > 0 && args[0] == "determinism")
{ Environment.ExitCode = LongSim.Check.CheckModes.Determinism(args); return; }
if (args.Length > 0 && args[0] == "digest")
{ Environment.ExitCode = LongSim.Check.CheckModes.Digest(args); return; }

if (args.Length > 0 && args[0] == "compare")
{
    int n = args.Length > 1 ? int.Parse(args[1]) : 10;
    int yrs = args.Length > 2 ? int.Parse(args[2]) : 5;
    RunCompare(n, yrs);
    return;
}

if (args.Length > 0 && args[0] == "diagnose")
{
    RunDiagnose(args.Length > 1 ? ulong.Parse(args[1]) : 7UL,
                args.Length > 2 ? int.Parse(args[2]) : 3);
    return;
}

if (args.Length > 0 && args[0] == "shoestring")
{
    RunShoestring(args.Length > 1 ? ulong.Parse(args[1]) : 42UL,
                  args.Length > 2 ? int.Parse(args[2]) : 20);
    return;
}

// ---------------------------------------------------------------------------
// The zero-to-hero fantasy, simulated: found a shoestring promotion (prestige 5,
// $250k, empty roster) and run it for N years. The world's AI books its cards,
// manages its money and takes its promotion leaps — but hiring needs a PLAYER
// AGENT: at prestige 5 the purse cap is ~zero, so AI negotiation can never land
// anyone (the known founded-org artifact). The agent does what a human does —
// follow the signing advisor and pay the asking price while the bank allows.
static void RunShoestring(ulong seed, int years)
{
    var world = new WorldBuilder().Build(new WorldBuilder.Options
    {
        Seed = seed,
        Founding = new WorldBuilder.FoundingSpec(),   // NWF, prestige 5, $250k, LW/WW/MW (+W straw/fly)
    });
    var nwf = world.Get(world.PlayerPromotion)!;
    world.PlayerPromotion = default;                  // the world AI books/markets/promotes it
    var tick = new WorldTick();

    var valuation = new MmaManager.Core.Sim.Contracts.FighterValuation();
    void PlayerAgent()
    {
        if (nwf.Defunct) return;
        // Play it like a careful shoestring owner: run each division at the
        // 4-man card minimum (City cards pad with $1k locals anyway), sign only
        // BARGAINS the gate can carry (the purse budget scales with prestige),
        // prefer youth/upside among the cheap, keep a runway, max 3 hires/month.
        int depth = 4 + (nwf.Balance.Amount > 600_000m ? 1 : 0) + (nwf.Balance.Amount > 1_500_000m ? 1 : 0);
        decimal maxAsk = 3_000m + nwf.Prestige.Value * 300m;   // ~$4.5k at prestige 5, grows with the gate
        for (int hires = 0; hires < 3; hires++)
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
            MmaManager.Core.Sim.Contracts.ContractLifecycle.Sign(world, pick.Fighter, new ContractOffer
            {
                Fighter = pick.Fighter.Id, Promotion = nwf.Id,
                PursePerFight = pick.Exp.AskingPurse,
                WinBonus = pick.Exp.AskingWinBonus,
                Fights = pick.Exp.DesiredFights,
            }, world.CurrentDate);
        }
    }

    Console.WriteLine($"=== SHOESTRING PLAYTHROUGH — {nwf.Name} ({nwf.Abbreviation}), seed {seed}, {years} years ===");
    Console.WriteLine($"Day one: prestige {nwf.Prestige.Value} ({OrgSizing.Label(OrgSizing.SizeOf(nwf))}) · {nwf.Balance} · roster {world.RosterOf(nwf.Id).Count()} · divisions {nwf.Divisions.Count} · HQ {world.Get(nwf.HomeCity!.Value)?.Name}");
    Console.WriteLine();

    var lastTier = OrgSizing.SizeOf(nwf);
    int lastLedger = 0;
    for (int year = 1; year <= years; year++)
    {
        // Month-by-month so the player agent can work the market like a human would.
        for (int m = 0; m < 13 && !nwf.Defunct; m++)
        {
            PlayerAgent();
            tick.AdvanceWeeks(world, 4);
        }

        if (nwf.Defunct)
        {
            Console.WriteLine($"--- YEAR {year} ({world.CurrentDate:yyyy}) ---");
            Console.WriteLine($"  *** {nwf.Abbreviation} FOLDED — the books ran out at {nwf.Balance}. The story ends here. ***");
            foreach (var g in nwf.Ledger.GroupBy(l => l.Category).OrderBy(g => g.Sum(l => l.Amount.Amount)))
                Console.WriteLine($"      {g.Key,-16} {g.Sum(l => l.Amount.Amount),12:C0}  ({g.Count()} entries)");
            var evs = world.Events.Values.Where(e => e.Promotion == nwf.Id && e.Financials is not null).ToList();
            Console.WriteLine($"      events run: {evs.Count} · avg house {evs.Select(e => (double)e.Financials!.Attendance).DefaultIfEmpty(0).Average():0} · avg gate {evs.Select(e => (double)e.Financials!.GateRevenue.Amount).DefaultIfEmpty(0).Average():C0} · avg payroll {evs.Select(e => (double)e.Financials!.FighterPayroll.Amount).DefaultIfEmpty(0).Average():C0}");
            break;
        }

        var fresh = nwf.Ledger.Skip(lastLedger).ToList();
        lastLedger = nwf.Ledger.Count;
        decimal income = fresh.Where(l => l.Amount.Amount > 0).Sum(l => l.Amount.Amount);
        decimal expense = fresh.Where(l => l.Amount.Amount < 0).Sum(l => l.Amount.Amount);

        var myEvents = world.Events.Values
            .Where(e => e.Promotion == nwf.Id && e.Financials is not null && e.Date > world.CurrentDate.AddDays(-365))
            .ToList();
        double avgHouse = myEvents.Select(e => (double)e.Financials!.Attendance).DefaultIfEmpty(0).Average();

        var roster = world.RosterOf(nwf.Id).Where(f => f.Status == FighterStatus.Active).ToList();
        var ace = roster.OrderByDescending(FighterAbility.CurrentAbility).FirstOrDefault();
        var draw = roster.OrderByDescending(f => f.Intangibles.Popularity.Value).FirstOrDefault();
        var tier = OrgSizing.SizeOf(nwf);
        var standing = OrgSizing.Progress(nwf);

        Console.WriteLine($"--- YEAR {year} ({world.CurrentDate:yyyy}) ---");
        Console.WriteLine($"  org       prestige {nwf.Prestige.Value} · {OrgSizing.Label(tier)} ({standing.Percent}% to {(standing.Next is { } nx ? OrgSizing.Label(nx) : "the top")}) · rep {nwf.Reputation.Value} · bank {nwf.Balance}");
        Console.WriteLine($"  year      {myEvents.Count} events · avg house {avgHouse:0} · in {income:C0} / out {expense:C0} (net {income + expense:C0}) · roster {roster.Count}");
        if (tier != lastTier)
        {
            Console.WriteLine($"  ▲▲▲       TIER CROSSING: {OrgSizing.Label(lastTier)} → {OrgSizing.Label(tier)}");
            lastTier = tier;
        }
        if (PromotionLadder.IsEligible(nwf))
            Console.WriteLine($"  ladder    queued for {OrgSizing.Label(OrgSizing.NextTier(nwf.TierClearance))}: "
                + $"door {PromotionLadder.PromotionCushion(world, nwf):C0} (base {PromotionLadder.PromotionCushion(nwf):C0}, "
                + $"crowding +{PromotionLadder.TierCrowding(world, nwf)}) vs bank {nwf.Balance}");
        var dealBits = nwf.SponsorDeals.Select(d => $"spons {d.Partner} {d.FeePerEvent}/ev")
            .Concat(nwf.MediaDeals.Select(d => $"{d.Kind} {d.Partner} {d.RightsFeePerEvent}/ev"))
            .Concat(nwf.Stream is { } st ? new[] { $"{st.Name} {st.Subscribers:N0} subs" } : Array.Empty<string>())
            .ToList();
        if (dealBits.Count > 0)
            Console.WriteLine($"  deals     {string.Join(" · ", dealBits)}");
        foreach (var div in nwf.Divisions)
        {
            var title = div.Title is { } t ? world.Get(t) : null;
            var champ = title?.CurrentChampion is { } cid ? world.Get(cid) : null;
            if (champ is not null)
                Console.WriteLine($"  champ     {div.WeightClass}: {champ.FullName} ({champ.Record}, pop {champ.Intangibles.Popularity.Value})");
        }
        if (ace is not null)
            Console.WriteLine($"  ace       {ace.FullName} · CA {FighterAbility.CurrentAbility(ace)} · {ace.WeightClass}");
        if (draw is not null && draw != ace)
            Console.WriteLine($"  draw      {draw.FullName} · pop {draw.Intangibles.Popularity.Value}");

        // One line of world context.
        var top = world.Promotions.Values.Where(p => !p.Defunct).OrderByDescending(p => p.Prestige.Value).Take(3).ToList();
        int rank = world.Promotions.Values.Where(p => !p.Defunct).Count(p => p.Prestige.Value > nwf.Prestige.Value) + 1;
        Console.WriteLine($"  world     #{rank} of {world.Promotions.Values.Count(p => !p.Defunct)} orgs · top: {string.Join(" | ", top.Select(p => $"{p.Abbreviation} {p.Prestige.Value}"))}");
        Console.WriteLine();
    }

    if (!nwf.Defunct)
    {
        Console.WriteLine($"=== AFTER {years} YEARS ===");
        Console.WriteLine($"{nwf.Name}: prestige {nwf.Prestige.Value} ({OrgSizing.Label(OrgSizing.SizeOf(nwf))}) · {nwf.Balance} · " +
            $"#{world.Promotions.Values.Where(p => !p.Defunct).Count(p => p.Prestige.Value > nwf.Prestige.Value) + 1} in the world");
    }
}

RunSingle(args.Length > 0 && ulong.TryParse(args[0], out var s) ? s : 2026UL,
          args.Length > 1 && int.TryParse(args[1], out var y) ? y : 30);

// ---------------------------------------------------------------------------
// P&L trace of the designed mid-tier: why do ICF/VMMA/IFA bleed out?
static void RunDiagnose(ulong seed, int years)
{
    var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });
    world.PlayerPromotion = default;
    var tick = new WorldTick();
    string[] watch = { "VMMA", "ICF", "IFA", "GFC", "CK", "RCS", "UFL", "SCM" };

    Console.WriteLine($"=== DIAGNOSIS — seed {seed}, {years} years, quarterly P&L ===");
    Console.WriteLine();
    Console.WriteLine("Day one:");
    foreach (var a in watch)
    {
        var p = world.Promotions.Values.First(x => x.Abbreviation == a);
        int roster = world.RosterOf(p.Id).Count();
        var committed = world.RosterOf(p.Id).Sum(f => f.Contract!.PursePerFight.Amount);
        Console.WriteLine($"  {a,-5} prestige {p.Prestige.Value,2} · {p.Balance,12} · roster {roster,3} · committed purse/card-cycle {committed,12:C0} · divisions {p.Divisions.Count}");
    }
    Console.WriteLine();

    var lastLedgerCount = watch.ToDictionary(a => a, a => 0);
    for (int q = 1; q <= years * 4; q++)
    {
        tick.AdvanceWeeks(world, 13);
        Console.WriteLine($"--- Q{q} ({world.CurrentDate:yyyy-MM}) ---");
        foreach (var a in watch)
        {
            var p = world.Promotions.Values.First(x => x.Abbreviation == a);
            var fresh = p.Ledger.Skip(lastLedgerCount[a]).ToList();
            lastLedgerCount[a] = p.Ledger.Count;

            if (p.Defunct && fresh.Count == 0) { Console.WriteLine($"  {a,-5} (defunct)"); continue; }

            var byCat = fresh.GroupBy(l => l.Category)
                .ToDictionary(g => g.Key, g => g.Sum(l => l.Amount.Amount));
            decimal In(FinanceCategory c) => byCat.GetValueOrDefault(c);
            decimal income = byCat.Where(kv => kv.Value > 0).Sum(kv => kv.Value);
            decimal expense = byCat.Where(kv => kv.Value < 0).Sum(kv => kv.Value);
            int events = world.Events.Values.Count(e => e.Promotion == p.Id && e.Financials is not null
                && e.Date > world.CurrentDate.AddDays(-91));
            var att = world.Events.Values.Where(e => e.Promotion == p.Id && e.Financials is not null
                && e.Date > world.CurrentDate.AddDays(-91)).Select(e => e.Financials!.Attendance).DefaultIfEmpty(0).Average();
            int roster = world.RosterOf(p.Id).Count();

            Console.WriteLine($"  {a,-5} bal {p.Balance,14} pr {p.Prestige.Value,2} roster {roster,3} · {events} ev avg house {att,5:0} · " +
                $"in {income,11:C0} (gate {In(FinanceCategory.EventGate),10:C0} tv {In(FinanceCategory.Broadcast),9:C0} spon {In(FinanceCategory.Sponsorship),9:C0}) · " +
                $"out {expense,12:C0} (purse {In(FinanceCategory.FighterPurse),12:C0} prod {In(FinanceCategory.Production),10:C0} ovh {In(FinanceCategory.Overhead),10:C0} sign {In(FinanceCategory.SigningBonus),10:C0})" +
                (p.Defunct ? "  *** FOLDED ***" : ""));
        }
        Console.WriteLine();
    }
}

// ---------------------------------------------------------------------------
static void RunCompare(int seeds, int years)
{
    var results = new ConcurrentDictionary<ulong, SeedResult>();
    Parallel.ForEach(Enumerable.Range(1, seeds).Select(i => (ulong)i), seed =>
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });
        world.PlayerPromotion = default;
        var tick = new WorldTick();
        var res = new SeedResult { Seed = seed };
        var deadAtYear = new Dictionary<string, int>();

        for (int yr = 1; yr <= years; yr++)
        {
            tick.AdvanceWeeks(world, 52);
            foreach (var p in world.Promotions.Values.Where(p => p.Defunct))
                deadAtYear.TryAdd(p.Abbreviation, yr);
        }

        var live = world.Promotions.Values.Where(p => !p.Defunct).ToList();
        var tiers = live.GroupBy(p => OrgSizing.SizeOf(p)).ToDictionary(g => g.Key, g => g.Count());
        var afc = world.Promotions.Values.First(p => p.Abbreviation == "AFC");
        var top = live.OrderByDescending(p => p.Prestige.Value).Take(3).ToList();
        var active = world.Fighters.Values.Where(f => f.Status == FighterStatus.Active && !f.LocalTalent).ToList();

        // A defunct org either went BANKRUPT or was BOUGHT — very different
        // health signals, and reading a wave of consolidation as a wave of
        // insolvency sends you tuning the wrong system. The durable tell is the
        // buyer's ledger (news caps at 1,000 items and prunes the headlines).
        var boughtNames = world.Promotions.Values
            .SelectMany(p => p.Ledger
                .Where(e => e.Category == FinanceCategory.Acquisition && e.Amount.Amount < 0)
                .Select(e => e.Description.Replace("Acquisition of ", "")))
            .ToHashSet();
        string Tag(string abbrev, int yr)
        {
            var org = world.Promotions.Values.First(p => p.Abbreviation == abbrev);
            return $"{abbrev}@Y{yr}{(boughtNames.Contains(org.Name) ? " (bought)" : "")}";
        }
        res.Folds = deadAtYear.OrderBy(kv => kv.Value).Select(kv => Tag(kv.Key, kv.Value)).ToList();
        res.Bankruptcies = deadAtYear.Count(kv =>
            !boughtNames.Contains(world.Promotions.Values.First(p => p.Abbreviation == kv.Key).Name));
        res.CkFoldYear = deadAtYear.TryGetValue("CK", out var cy) ? cy : null;
        res.RcsFoldYear = deadAtYear.TryGetValue("RCS", out var ry) ? ry : null;
        res.Founded = world.Promotions.Count - 19;
        res.OrgsAlive = live.Count;
        res.Pyramid = $"{tiers.GetValueOrDefault(OrgSize.International)}·{tiers.GetValueOrDefault(OrgSize.Continental)}·{tiers.GetValueOrDefault(OrgSize.National)}·{tiers.GetValueOrDefault(OrgSize.Regional)}·{tiers.GetValueOrDefault(OrgSize.City)}";
        res.TopOrgs = string.Join(" ", top.Select(p => $"{p.Abbreviation}:{p.Prestige.Value}"));
        res.AfcRoster = world.RosterOf(afc.Id).Count();
        res.AfcPrestige = afc.Prestige.Value;
        res.ActiveFighters = active.Count;
        res.AvgPop = active.Average(f => (double)f.Intangibles.Popularity.Value);
        res.Zombies = live.Count(p => world.RosterOf(p.Id).Count() <= 1);
        res.IfaBalance = world.Promotions.Values.First(p => p.Abbreviation == "IFA").Balance.Amount;
        res.Internationals = tiers.GetValueOrDefault(OrgSize.International);
        results[seed] = res;
    });

    Console.WriteLine($"=== {seeds} SEEDS x {years} YEARS — comparison ===");
    Console.WriteLine();
    Console.WriteLine($"{"seed",4} | {"active",6} {"avgPop",6} | {"orgs",4} {"pyramid",11} | {"AFC",8} | {"top 3",-24} | {"IFA bank",12} | {"zmb",3} | folds");
    foreach (var r in results.Values.OrderBy(r => r.Seed))
        Console.WriteLine($"{r.Seed,4} | {r.ActiveFighters,6} {r.AvgPop,6:0.0} | {r.OrgsAlive,4} {r.Pyramid,11} | {r.AfcPrestige,3} r{r.AfcRoster,-4} | {r.TopOrgs,-24} | {r.IfaBalance,12:N0} | {r.Zombies,3} | {(r.Folds.Count == 0 ? "—" : string.Join(", ", r.Folds))}");

    Console.WriteLine();
    var all = results.Values.ToList();
    Console.WriteLine("=== AGGREGATE ===");
    Console.WriteLine($"  CK folded in {all.Count(r => r.CkFoldYear is not null)}/{seeds} seeds " +
        $"(years: {string.Join(",", all.Where(r => r.CkFoldYear is not null).Select(r => r.CkFoldYear))})");
    Console.WriteLine($"  RCS folded in {all.Count(r => r.RcsFoldYear is not null)}/{seeds} seeds " +
        $"(years: {string.Join(",", all.Where(r => r.RcsFoldYear is not null).Select(r => r.RcsFoldYear))})");
    var otherFolds = all.SelectMany(r => r.Folds.Select(f => f.Split('@')[0]))
        .Where(a => a != "CK" && a != "RCS")
        .GroupBy(a => a).OrderByDescending(g => g.Count()).ToList();
    Console.WriteLine($"  other folds: {(otherFolds.Count == 0 ? "none" : string.Join(", ", otherFolds.Select(g => $"{g.Key} x{g.Count()}")))}");
    Console.WriteLine($"  of all deaths, actual BANKRUPTCIES: {all.Sum(r => r.Bankruptcies)} (the rest were acquisitions)");
    Console.WriteLine($"  orgs alive:  min {all.Min(r => r.OrgsAlive)} · avg {all.Average(r => r.OrgsAlive):0.0} · max {all.Max(r => r.OrgsAlive)}");
    Console.WriteLine($"  internationals: {string.Join(",", all.OrderBy(r => r.Seed).Select(r => r.Internationals))} (avg {all.Average(r => r.Internationals):0.0})");
    Console.WriteLine($"  active:      min {all.Min(r => r.ActiveFighters)} · avg {all.Average(r => r.ActiveFighters):0.0} · max {all.Max(r => r.ActiveFighters)}");
    Console.WriteLine($"  avg pop:     min {all.Min(r => r.AvgPop):0.0} · avg {all.Average(r => r.AvgPop):0.0} · max {all.Max(r => r.AvgPop):0.0}");
    Console.WriteLine($"  AFC roster:  min {all.Min(r => r.AfcRoster)} · avg {all.Average(r => r.AfcRoster):0.0} · max {all.Max(r => r.AfcRoster)}");
    Console.WriteLine($"  AFC #1 in {all.Count(r => r.TopOrgs.StartsWith("AFC"))}/{seeds} seeds");
    Console.WriteLine($"  zombie orgs (roster<=1): avg {all.Average(r => r.Zombies):0.0}");
    Console.WriteLine($"  IFA in the red after Y{years}: {all.Count(r => r.IfaBalance < 0)}/{seeds}");
}

static void RunSingle(ulong seed, int years)
{
    var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });
    world.PlayerPromotion = default;
    var tick = new WorldTick();

    IEnumerable<Fighter> Active() => world.Fighters.Values.Where(f => f.Status == FighterStatus.Active && !f.LocalTalent);
    string OrgOf(Fighter f) => f.Contract is { } c ? world.Get(c.Promotion)?.Abbreviation ?? "FA" : "FA";
    string Tier(Promotion p) => OrgSizing.Label(OrgSizing.SizeOf(p));

    var knownOrgs = world.Promotions.Keys.ToHashSet();
    var deadOrgs = new HashSet<PromotionId>();
    Console.WriteLine($"=== SIMULATION — seed {seed}, {years} years ===");
    Console.WriteLine($"Day one: {world.CurrentDate:yyyy-MM-dd} · {world.Promotions.Count} orgs · {Active().Count()} active fighters");
    Console.WriteLine();

    for (int year = 1; year <= years; year++)
    {
        var r = tick.AdvanceWeeks(world, 52);
        var live = world.Promotions.Values.Where(p => !p.Defunct).ToList();
        var folded = world.Promotions.Values.Where(p => p.Defunct && !deadOrgs.Contains(p.Id)).ToList();
        foreach (var f in folded) deadOrgs.Add(f.Id);
        var born = world.Promotions.Values.Where(p => !knownOrgs.Contains(p.Id)).ToList();
        foreach (var b in born) knownOrgs.Add(b.Id);
        var tiers = live.GroupBy(p => OrgSizing.SizeOf(p)).ToDictionary(g => g.Key, g => g.Count());
        string pyramid = string.Join(" · ", Enum.GetValues<OrgSize>().Reverse()
            .Select(t => $"{OrgSizing.Label(t)} {tiers.GetValueOrDefault(t)}"));
        var top = live.OrderByDescending(p => p.Prestige.Value).Take(6).ToList();

        Console.WriteLine($"--- YEAR {year} ({world.CurrentDate:yyyy}) ---");
        Console.WriteLine($"  activity  {r.EventsRun} events · {r.Fights} fights ({100.0 * r.Finishes / Math.Max(1, r.Fights):0}% finish) · {r.Retired} retired · {r.Newgens} newgens");
        Console.WriteLine($"  world     {Active().Count()} active · {live.Count} orgs [{pyramid}]");
        foreach (var b in born) Console.WriteLine($"  FOUNDED   {b.Abbreviation} in {b.BaseRegion}");
        foreach (var f in folded) Console.WriteLine($"  FOLDED    {f.Abbreviation} — {Tier(f)}");
        Console.WriteLine($"  top orgs  {string.Join(" | ", top.Select(p => $"{p.Abbreviation} {p.Prestige.Value} {p.Balance}"))}");
        Console.WriteLine();
    }

    Console.WriteLine("Belts — most and least storied (prestige · org prestige · title):");
    var belts = world.Titles.Values
        .Where(t => world.Get(t.Promotion) is { Defunct: false })
        .OrderByDescending(t => t.Prestige.Value).ToList();
    foreach (var t in belts.Take(8).Concat(belts.TakeLast(8)))
        Console.WriteLine($"  {t.Prestige.Value,3} · org {world.Get(t.Promotion)!.Prestige.Value,3} · {t.Name}");
}

sealed class SeedResult
{
    public ulong Seed;
    public List<string> Folds = new();
    /// <summary>Defunct orgs that were NOT bought — the ones that actually went broke.</summary>
    public int Bankruptcies;
    public int? CkFoldYear, RcsFoldYear;
    public int Founded, OrgsAlive, AfcRoster, AfcPrestige, ActiveFighters, Zombies, Internationals;
    public string Pyramid = "", TopOrgs = "";
    public double AvgPop;
    public decimal IfaBalance;
}
