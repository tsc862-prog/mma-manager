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

if (args.Length > 0 && args[0] == "fightlab") { FightLabMode.Run(args); return; }   // engine benchmark: FightLabMode.cs
if (args.Length > 0 && args[0] == "asks") { LongSim.Asks.Run(args); return; }       // price levels by stage/ability: Asks.cs
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
                args.Length > 2 ? int.Parse(args[2]) : 3,
                args.Skip(3).ToArray());
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
    int agentHires = 0;
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
            agentHires++;
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
        {
            double cards = Math.Max(1, myEvents.Count);
            int bouts = myEvents.Sum(e => e.Card.Count);
            int locals = myEvents.Sum(e => e.Card.Count(fi => IsLocal(world, fi)));
            var paid = roster.Where(f => f.Contract is not null).Select(f => f.Contract!.PursePerFight.Amount).ToList();
            Console.WriteLine($"  money     payroll/card {myEvents.Sum(e => e.Financials!.FighterPayroll.Amount) / (decimal)cards:N0} · gate/card {myEvents.Sum(e => e.Financials!.GateRevenue.Amount) / (decimal)cards:N0} · bouts/card {bouts / cards:0.0} · local bouts/card {locals / cards:0.0} · "
                + $"roster purse mean {(paid.Count == 0 ? 0 : paid.Average()):N0} · roster CA mean {(roster.Count == 0 ? 0 : roster.Average(f => (double)FighterAbility.CurrentAbility(f))):0.0} · agent hires {agentHires}");
        }
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
// diagnose <seed> <years> [ABBR ...]: with org abbreviations, the detailed
// trace for just those orgs (every ledger line, media deals and strikes,
// dropped deals and why, acquisitions, houses vs rooms); without, the old
// mid-tier summary.
static void RunDiagnose(ulong seed, int years, string[] orgs)
{
    if (orgs.Length > 0) { LongSim.Diagnose.Detailed(seed, years, orgs); return; }
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

        var startDate = world.CurrentDate;   // day-one back-dated history is excluded from the rates below
        var dayOneOrgs = world.Promotions.Keys.ToHashSet();
        var retiredAtStart = world.Fighters.Values.Where(f => f.Status == FighterStatus.Retired).Select(f => f.Id).ToHashSet();
        var seenOrg = new Dictionary<PromotionId, OrgVitals>();
        NewsItem? lastSeen = world.News.Count > 0 ? world.News[^1] : null;

        for (int yr = 1; yr <= years; yr++)
        {
            // AdvanceWeeks is just AdvanceWeek in a loop, so stepping one week at a
            // time is identical to 52 at once -- and lets us sample the news feed
            // every week, well inside its 1000-item cap.
            for (int w = 0; w < 52; w++)
            {
                tick.AdvanceWeeks(world, 1);
                int from = 0;
                if (lastSeen is not null)
                {
                    int idx = -1;
                    for (int i = world.News.Count - 1; i >= 0; i--)
                        if (ReferenceEquals(world.News[i], lastSeen)) { idx = i; break; }
                    if (idx < 0) res.NewsOverflow++;   // cap trimmed past our marker within one week: undercount
                    from = idx + 1;
                }
                for (int i = from; i < world.News.Count; i++)
                {
                    var n = world.News[i];
                    if (n.Category != NewsCategory.Withdrawal) continue;
                    if (n.Headline.Contains(" steps in for ")) res.CampWithdrawalsSaved++;
                    else if (n.Body.StartsWith("Injured in camp")) res.CampWithdrawalsLost++;
                    else if (n.Body.StartsWith("Pulled with")) res.FightWeekPullouts++;
                }
                if (world.News.Count > 0) lastSeen = world.News[^1];

                // Weekly sample of org vitals for the autopsies (roster and tier of
                // a promotion in its last live week; the week a fold is first seen).
                foreach (var op in world.Promotions.Values)
                {
                    bool known = seenOrg.TryGetValue(op.Id, out var cur);
                    if (op.Defunct)
                    {
                        if (known && cur!.DeathSeen == default) cur.DeathSeen = world.CurrentDate;
                        continue;
                    }
                    int rs = world.RosterOf(op.Id).Count();
                    if (!known) seenOrg[op.Id] = new OrgVitals { First = world.CurrentDate, Roster0 = rs, RosterLast = rs, TierLast = OrgSizing.SizeOf(op) };
                    else { cur!.RosterLast = rs; cur.TierLast = OrgSizing.SizeOf(op); }
                }
            }
            foreach (var p in world.Promotions.Values.Where(p => p.Defunct))
                deadAtYear.TryAdd(p.Abbreviation, yr);
        }

        // Durable, from event/fight state (events are never pruned for AI orgs).
        var simEvents = world.Events.Values.Where(e => e.Date > startDate).ToList();
        var doneEvents = simEvents.Where(e => e.Status == EventStatus.Completed && e.Card.Count > 0).ToList();
        // ---- autopsies (DURABLE: events, fights, ledgers; SAMPLED weekly: roster, tier, death week) ----
        var endDate = world.CurrentDate;
        foreach (var op in world.Promotions.Values.OrderBy(o => o.Abbreviation))
        {
            bool startup = !dayOneOrgs.Contains(op.Id);
            var cards = doneEvents.Where(e => e.Promotion == op.Id).ToList();
            seenOrg.TryGetValue(op.Id, out var vit);
            vit ??= new OrgVitals();
            var firstLedger = op.Ledger.Count > 0 ? op.Ledger.Min(l => l.Date) : default;
            var o = new OrgAutopsy
            {
                Seed = seed, Abbrev = op.Abbreviation, Startup = startup, Defunct = op.Defunct, Region = op.BaseRegion,
                Founded = startup ? (firstLedger != default ? firstLedger : vit.First) : default,
                Died = vit.DeathSeen != default ? vit.DeathSeen : (op.Defunct ? (op.Ledger.Count > 0 ? op.Ledger.Max(l => l.Date) : default) : default),
                Tier = op.Defunct ? vit.TierLast : OrgSizing.SizeOf(op),
                Cards = cards.Count,
                Bouts = cards.Sum(e => e.Card.Count),
                LocalBouts = cards.Sum(e => e.Card.Count(f => IsLocal(world, f))),
                Attendance = cards.Sum(e => e.Financials?.Attendance ?? 0),
                Gate = cards.Sum(e => e.Financials?.GateRevenue.Amount ?? 0m),
                Purses = cards.Sum(e => e.Financials?.FighterPayroll.Amount ?? 0m),
                Roster0 = vit.Roster0, RosterLast = op.Defunct ? vit.RosterLast : world.RosterOf(op.Id).Count(),
                Income = op.Ledger.Where(l => l.Amount.Amount > 0).Sum(l => l.Amount.Amount),
                Expense = op.Ledger.Where(l => l.Amount.Amount < 0).Sum(l => l.Amount.Amount),
                ByCat = op.Ledger.GroupBy(l => l.Category).ToDictionary(g => g.Key, g => g.Sum(l => l.Amount.Amount)),
            };
            var born = o.Startup ? (o.Founded != default ? o.Founded : startDate) : startDate;
            var until = o.Defunct && o.Died != default ? o.Died : endDate;
            o.YearsAlive = Math.Max(1.0 / 52, (until.DayNumber - born.DayNumber) / 365.25);
            res.Orgs.Add(o);
        }
        res.Superfights = doneEvents.Count(e => e.Name.StartsWith("CHAMP VS CHAMP"));
        res.WeightMisses = doneEvents.Sum(e => e.Card.Count(f => f.WeightMissBy is not null));
        res.CancelledEvents = simEvents.Count(e => e.Status == EventStatus.Cancelled);
        res.CompletedEvents = doneEvents.Count;
        res.TitleMainEvents = doneEvents.Count(e => e.Card[^1].IsTitleFight || e.Card[^1].Title is not null);
        res.Years = years;

        // In-world fight outcomes (DURABLE: every bout on a completed card after day one).
        foreach (var e in doneEvents)
            foreach (var f in e.Card)
            {
                if (f.Outcome is not { } o) continue;
                var red = world.Get(f.RedCorner);
                res.Bouts.Add(new BoutRow(o.Method, f.ScheduledRounds, red?.Sex ?? Sex.Male, f.WeightClass, IsLocal(world, f),
                    o.EndRound, o.Stats.Values.Sum(s => s.KnockdownsScored),
                    o.RoundScores?.Count(s => Math.Min(s.Red, s.Blue) <= 8) ?? 0, o.RoundScores?.Count ?? 0,
                    MmaManager.Core.Sim.Flavor.MatchRating.Of(f)));
            }
        // Card quality exactly as DealCompliance's MinCardQuality reads it: the average MatchRating
        // of the bouts that ran. Tier is the promotion's size at the end of the run (SAMPLED).
        foreach (var e in doneEvents)
        {
            var ran = e.Card.Where(f => f.Outcome is not null).ToList();
            if (ran.Count == 0 || world.Get(e.Promotion) is not { } op) continue;
            res.Cards.Add(new CardRow(OrgSizing.SizeOf(op), ran.Average(f => (double)MmaManager.Core.Sim.Flavor.MatchRating.Of(f))));
        }
        var retiredInRun = world.Fighters.Values.Where(f => f.Status == FighterStatus.Retired && !retiredAtStart.Contains(f.Id) && !f.LocalTalent).ToList();
        res.RetiredInRun = retiredInRun.Count;
        res.RetiredCareerBouts = retiredInRun.Count == 0 ? 0 : retiredInRun.Average(f => (double)f.Record.TotalBouts);

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
        foreach (var ao in res.Orgs)
            ao.Bought = ao.Defunct && boughtNames.Contains(world.Promotions.Values.First(p => p.Abbreviation == ao.Abbrev).Name);
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

    // Per-year rates, averaged over seeds (min..max across seeds in brackets).
    string Rate(Func<SeedResult, int> pick)
    {
        var v = all.Select(r => (double)pick(r) / Math.Max(1, r.Years)).ToList();
        return $"{v.Average(),6:0.00}/yr  [{v.Min():0.00}..{v.Max():0.00}]";
    }
    Console.WriteLine();
    Console.WriteLine("  --- per-year rates, world-wide, AI orgs (day-one history excluded) ---");
    Console.WriteLine($"  completed events (DURABLE: Event.Status):                    {Rate(r => r.CompletedEvents)}");
    Console.WriteLine($"  superfights, champ-vs-champ (DURABLE: completed events named 'CHAMP VS CHAMP'): {Rate(r => r.Superfights)}");
    Console.WriteLine($"  cancelled events (DURABLE: Event.Status = Cancelled; incl. never-booked, collapsed cards, solvency/acquisition): {Rate(r => r.CancelledEvents)}");
    Console.WriteLine($"  weight misses (DURABLE: Fight.WeightMissBy on completed cards): {Rate(r => r.WeightMisses)}");
    Console.WriteLine($"  camp withdrawals, bout lost (HEADLINE-SAMPLED weekly, no durable trace): {Rate(r => r.CampWithdrawalsLost)}");
    Console.WriteLine($"  camp withdrawals, replaced by a stand-in (HEADLINE-SAMPLED weekly):      {Rate(r => r.CampWithdrawalsSaved)}");
    Console.WriteLine($"  fight-week pullouts, bout lost (HEADLINE-SAMPLED weekly, no durable trace): {Rate(r => r.FightWeekPullouts)}");
    Console.WriteLine($"  bouts lost before fight night = camp-lost + fight-week pullouts (weight misses NOT lost: bout goes ahead at catchweight): {Rate(r => r.CampWithdrawalsLost + r.FightWeekPullouts)}");
    int totDone = all.Sum(r => r.CompletedEvents);
    Console.WriteLine($"  completed cards with a title fight on top (DURABLE; weight-miss title fights are un-flagged so slightly undercounted): " +
        $"{100.0 * all.Sum(r => r.TitleMainEvents) / Math.Max(1, totDone):0.0}% of {totDone}");
    Console.WriteLine($"  pro retirements during the run (DURABLE: Status = Retired, not retired on day one, not local): {Rate(r => r.RetiredInRun)}; " +
        $"their career bouts avg {all.Average(r => r.RetiredCareerBouts):0.0}");
    PrintFightMix(all.SelectMany(r => r.Bouts).ToList());
    PrintCardQuality(all.SelectMany(r => r.Cards).ToList());
    PrintDeaths(results.Values.OrderBy(r => r.Seed).SelectMany(r => r.Orgs).ToList());
    // Every org's completed cards over the run, pooled by its tier at the end (or when it died): what each tier pays a card.
    Console.WriteLine();
    Console.WriteLine("  --- ALL ORGS by end tier (DURABLE: completed cards over the run; day-one orgs and startups) ---");
    foreach (var g in results.Values.SelectMany(r => r.Orgs).GroupBy(o => o.Tier).OrderBy(g => g.Key))
    {
        double c = Math.Max(1, g.Sum(o => o.Cards));
        Console.WriteLine($"  {OrgSizing.Label(g.Key),-14} orgs {g.Count(),4} · cards {g.Sum(o => o.Cards),6} · bouts/card {g.Sum(o => o.Bouts) / c:0.00} · local bouts/card {g.Sum(o => o.LocalBouts) / c:0.00} · "
            + $"house {g.Sum(o => o.Attendance) / c:0} · gate/card {g.Sum(o => o.Gate) / (decimal)c:N0} · purses/card {g.Sum(o => o.Purses) / (decimal)c:N0} · purses/gate {(g.Sum(o => o.Gate) == 0 ? 0 : g.Sum(o => o.Purses) / g.Sum(o => o.Gate)):0.00}");
    }
    if (all.Sum(r => r.NewsOverflow) > 0)
        Console.WriteLine($"  WARNING: news sampler lost its place {all.Sum(r => r.NewsOverflow)} time(s); headline-sampled rates are undercounts");
}

// A bout "involves local filler" when either corner is a LocalTalent fighter (a durable flag on
// the fighter, read at the end of the run -- a filler who later signed a real deal still counts).
static bool IsLocal(World world, Fight f) =>
    (world.Get(f.RedCorner)?.LocalTalent ?? false) || (world.Get(f.BlueCorner)?.LocalTalent ?? false);

// The engine's in-world method mix: every bout on a completed card after day one, pooled over seeds.
static void PrintFightMix(List<BoutRow> b)
{
    Console.WriteLine();
    Console.WriteLine("  --- IN-WORLD FIGHT OUTCOMES (DURABLE: every bout on a completed card after day one, pooled over seeds) ---");
    static bool Fin(FightMethod m) => m is FightMethod.Knockout or FightMethod.TechnicalKnockout or FightMethod.SubmissionTap or FightMethod.TechnicalSubmission;
    void Line(string label, List<BoutRow> g)
    {
        if (g.Count == 0) return;
        double P(Func<BoutRow, bool> f) => 100.0 * g.Count(f) / g.Count;
        Console.WriteLine($"  {label,-20} n={g.Count,7}  fin {P(x => Fin(x.Method)),5:0.0}%  KO {P(x => x.Method == FightMethod.Knockout),5:0.0} TKO {P(x => x.Method == FightMethod.TechnicalKnockout),5:0.0} " +
            $"SUB {P(x => x.Method is FightMethod.SubmissionTap or FightMethod.TechnicalSubmission),5:0.0} | UD {P(x => x.Method == FightMethod.UnanimousDecision),5:0.0} " +
            $"SD {P(x => x.Method == FightMethod.SplitDecision),5:0.0} MD {P(x => x.Method == FightMethod.MajorityDecision),4:0.0} Draw {P(x => x.Method == FightMethod.Draw),4:0.0} " +
            $"other {P(x => x.Method is FightMethod.NoContest or FightMethod.Disqualification),4:0.0}");
    }
    Line("all bouts", b);
    Line("pro vs pro", b.Where(x => !x.Local).ToList());
    Line("with a local", b.Where(x => x.Local).ToList());
    Line("3 rounds", b.Where(x => x.Rounds == 3).ToList());
    Line("5 rounds", b.Where(x => x.Rounds == 5).ToList());
    Line("men", b.Where(x => x.Sex == Sex.Male).ToList());
    Line("women", b.Where(x => x.Sex == Sex.Female).ToList());
    foreach (var sx in new[] { Sex.Male, Sex.Female })
        foreach (var wc in Enum.GetValues<WeightClass>())
            Line($" {(sx == Sex.Male ? "M" : "W")} {wc}", b.Where(x => x.Sex == sx && x.Class == wc).ToList());
    var three = b.Where(x => x.Rounds == 3).ToList();
    if (three.Count > 0)
        Console.WriteLine("  3-round finishes by round (share of all 3-round bouts): " +
            string.Join("  ", Enumerable.Range(1, 3).Select(r => $"R{r} {100.0 * three.Count(x => Fin(x.Method) && x.EndRound == r) / three.Count:0.0}%")));
    int rounds = b.Sum(x => x.CardRounds);
    Console.WriteLine($"  knockdowns per bout {b.Average(x => x.Knockdowns):0.00}; 10-8 share of stored-card rounds {100.0 * b.Sum(x => x.TenEights) / Math.Max(1, rounds):0.0}%");
    foreach (var r in new[] { 3, 5 })
    {
        var g = b.Where(x => x.Rounds == r).Select(x => (double)x.Rating).ToList();
        if (g.Count > 0) Console.WriteLine($"  fight rating (MatchRating), {r} rounds: {Dist(g)}");
    }
}

// mean and p10/p25/median/p75/p90 of a sample
static string Dist(List<double> v)
{
    v.Sort();
    double Q(double q) => v[Math.Clamp((int)Math.Round(q * (v.Count - 1)), 0, v.Count - 1)];
    return $"n={v.Count} mean {v.Average():0.0}  p10 {Q(0.1):0.0} p25 {Q(0.25):0.0} median {Q(0.5):0.0} p75 {Q(0.75):0.0} p90 {Q(0.9):0.0}";
}

// Card quality as DealCompliance.MinCardQuality reads it (average MatchRating of the bouts that ran).
static void PrintCardQuality(List<CardRow> cards)
{
    Console.WriteLine();
    Console.WriteLine("  --- CARD QUALITY (DURABLE: avg MatchRating per completed card, as MinCardQuality reads it; tier = promotion size at end, SAMPLED) ---");
    void Line(string label, List<CardRow> g)
    {
        if (g.Count == 0) return;
        double P(double t) => 100.0 * g.Count(c => c.Quality >= t) / g.Count;
        Console.WriteLine($"  {label,-14} {Dist(g.Select(c => c.Quality).ToList())}  | clears 40: {P(40):0.0}%  42: {P(42):0.0}%  45: {P(45):0.0}%  50: {P(50):0.0}%");
    }
    Line("all cards", cards);
    foreach (var t in Enum.GetValues<OrgSize>().Reverse())
        Line(OrgSizing.Label(t), cards.Where(c => c.Tier == t).ToList());
}

static void PrintDeaths(List<OrgAutopsy> orgs)
{
    var dead = orgs.Where(o => o.Defunct && !o.Bought).ToList();
    Console.WriteLine();
    Console.WriteLine("=== DEATHS (BANKRUPT orgs only (acquired ones excluded); averages over completed cards in the run; roster/tier/death week SAMPLED weekly; bouts/attendance/gate/purses/ledger DURABLE; LOCAL = either corner LocalTalent) ===");
    Console.WriteLine($"{"seed",4} {"abbr",-5} {"st",2} {"founded",10} {"died",10} {"tier",-12} {"cards",5} {"bouts",5} {"local",5} {"house",6} {"gate/c",9} {"purse/c",9} | {"roster0>last",12} | {"income",11} {"expense",12} | worst/best ledger lines");
    foreach (var o in dead.OrderBy(o => o.Seed).ThenBy(o => o.Died))
    {
        double c = Math.Max(1, o.Cards);
        var top = string.Join(" ", o.ByCat.OrderBy(kv => kv.Value).Take(3).Select(kv => $"{kv.Key}:{kv.Value:N0}")
            .Concat(o.ByCat.OrderByDescending(kv => kv.Value).Take(2).Select(kv => $"{kv.Key}:+{kv.Value:N0}")));
        Console.WriteLine($"{o.Seed,4} {o.Abbrev,-5} {(o.Startup ? "S" : "D"),2} {(o.Startup ? o.Founded.ToString("yyyy-MM-dd") : "day-one"),10} {o.Died:yyyy-MM-dd} {OrgSizing.Label(o.Tier),-12} {o.Cards,5} {o.Bouts / c,5:0.0} {o.LocalBouts / c,5:0.0} {o.Attendance / c,6:0} {o.Gate / (decimal)c,9:N0} {o.Purses / (decimal)c,9:N0} | {o.Roster0,5}>{o.RosterLast,-5}  | {o.Income,11:N0} {o.Expense,12:N0} | {top}");
    }
    Console.WriteLine("  (st: S = startup founded during the run, D = day-one org. Acquired orgs are excluded from the table but counted as defunct in the STARTUPS lines below.)");

    void Line(string label, List<OrgAutopsy> g)
    {
        double cards = g.Sum(o => o.Cards), yrs = g.Sum(o => o.YearsAlive);
        Console.WriteLine($"  {label}: n={g.Count} · defunct={g.Count(o => o.Defunct)} · org-years {yrs:0.0} · cards/yr {cards / Math.Max(0.01, yrs):0.00} · bouts/card {g.Sum(o => o.Bouts) / Math.Max(1, cards):0.00} · local bouts/card {g.Sum(o => o.LocalBouts) / Math.Max(1, cards):0.00} · house {g.Sum(o => o.Attendance) / Math.Max(1, cards):0} · gate/card {g.Sum(o => o.Gate) / (decimal)Math.Max(1, cards):N0} · purses/card {g.Sum(o => o.Purses) / (decimal)Math.Max(1, cards):N0}");
    }
    Console.WriteLine();
    var st = orgs.Where(o => o.Startup).ToList();
    Console.WriteLine("  --- STARTUPS ONLY (promotions founded during the run; cards pooled over all startup cards) ---");
    Line("STARTUPS all", st);
    Line("STARTUPS died", st.Where(o => o.Defunct).ToList());
    Line("STARTUPS alive", st.Where(o => !o.Defunct).ToList());
    Console.WriteLine("  STARTUPS by region (launched/defunct): " + string.Join(" · ",
        Enum.GetValues<Region>().Select(r => $"{r} {st.Count(o => o.Region == r)}/{st.Count(o => o.Region == r && o.Defunct)}")));
    Console.WriteLine($"  STARTUPS roster: avg at first weekly sighting {(st.Count == 0 ? 0 : st.Average(o => o.Roster0)):0.0} (SAMPLED), avg in last live week / at end {(st.Count == 0 ? 0 : st.Average(o => o.RosterLast)):0.0} (SAMPLED)");
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
    public List<OrgAutopsy> Orgs = new();
    public List<string> Folds = new();
    /// <summary>Defunct orgs that were NOT bought — the ones that actually went broke.</summary>
    public int Bankruptcies;
    public int? CkFoldYear, RcsFoldYear;
    public int Founded, OrgsAlive, AfcRoster, AfcPrestige, ActiveFighters, Zombies, Internationals;
    public string Pyramid = "", TopOrgs = "";
    public double AvgPop;
    public decimal IfaBalance;
    public int Years, Superfights, WeightMisses, CancelledEvents, CompletedEvents, TitleMainEvents;
    public int CampWithdrawalsLost, CampWithdrawalsSaved, FightWeekPullouts, NewsOverflow;
    public List<BoutRow> Bouts = new();
    public List<CardRow> Cards = new();
    public int RetiredInRun;
    public double RetiredCareerBouts;
}

readonly record struct BoutRow(FightMethod Method, int Rounds, Sex Sex, WeightClass Class, bool Local, int EndRound, int Knockdowns, int TenEights, int CardRounds, int Rating);
readonly record struct CardRow(OrgSize Tier, double Quality);

sealed class OrgVitals
{
    public DateOnly First, DeathSeen;
    public int Roster0, RosterLast;
    public OrgSize TierLast;
}

sealed class OrgAutopsy
{
    public ulong Seed;
    public string Abbrev = "";
    public bool Startup, Defunct, Bought;
    public Region Region;
    public DateOnly Founded, Died;
    public int Cards, Bouts, LocalBouts, Attendance, Roster0, RosterLast;
    public OrgSize Tier;
    public decimal Gate, Purses, Income, Expense;
    public double YearsAlive;
    public Dictionary<FinanceCategory, decimal> ByCat = new();
}

