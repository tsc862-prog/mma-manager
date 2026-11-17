using System.Text;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Generation.Origins;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Scouting;

namespace LongSim;

/// <summary>
/// <c>origins &lt;seeds&gt; &lt;years&gt;</c>: the 1993 world (Origins mode) run headless. For each seed it runs
/// two worlds, each TWICE (determinism): AI-run (all three houses are the AI's) and a scripted player
/// running one of the three (seed % 3) through the sim APIs. Prints a per-year summary with the chapters
/// of the timeline as they fire (and the Backlash's bans and licences), the invariant checks, and a
/// HEALTH line for a young world growing up:
/// <list type="bullet">
/// <item>no exception;</item>
/// <item>the sport survives: at every year-end at least one house is running (houses may fold: the
/// lean years are meant to cost the weakest), and every house running all year keeps near its cadence
/// (at least <see cref="CadenceShare"/> of the nights its cadence allows; the first year excepted);</item>
/// <item>the sport keeps its size: ACTIVE men between <see cref="MinActives"/> and a ceiling that grows with
/// the number of houses, at every year-end, and new men turned pro;</item>
/// <item>rosters are replenished: every live solvent house ends holding <c>OriginsShows.MinRoster</c> men on its
/// books (not retired, deal not run out, or run out in the last week and in the player's re-sign window); how many are fit that day is a note;</item>
/// <item>the chapter order holds: every fired chapter fired inside its window (by its deadline, a week's
/// grace) and strictly after every chapter it requires;</item>
/// <item>nothing is used before the sport has it: no media deal, staged night, weight division, title or
/// woman before its chapter unlocked it (the first use IS the chapter, so the same week counts), and no
/// night in a region a ban had closed;</item>
/// <item>the sport spreads honestly (phase 3, the yearly SPORT line): every scene awake two months has a gym,
/// every house a commission binds fights under the unified code, no list of the sport's best is published before
/// the code, and no new man arrives cross-trained before the first famous camp;</item>
/// <item>determinism: the same seed twice gives an identical world digest (<c>Check.WorldGraph.Digest</c>,
/// every saved field), for the AI world and the scripted one.</item>
/// </list>
/// Money is reported (each founding house's bank against its opening one, as notes) but is no longer a
/// failure once the Backlash can bite. After the seeds, ORIGINS MONEY sums every seed's money: the founders'
/// end banks and folds, how long new houses last, the scripted player's bank, and where a night's and a
/// house-year's money goes by kind of house (with each night's no-walls demand, read the week it ran).
/// <c>ORIGINS_NIGHTS=&lt;abbr&gt;</c> prints every night that house runs. Exit code 1 when any seed fails its
/// health line or breaks an invariant.
/// </summary>
public static partial class OriginsMode
{
    public const int MinActives = 80, MaxActives = 160, ActivesPerExtraHouse = 30;
    public const double CadenceShare = 0.6;
    public const decimal MoneyFloor = 0.4m, MoneyCeiling = 2.5m;

    public static int Run(string[] args)
    {
        int seeds = args.Length > 1 ? int.Parse(args[1]) : 3;
        int years = args.Length > 2 ? int.Parse(args[2]) : 5;
        int failures = 0;
        var totals = new List<string>();
        var aiRuns = new List<Outcome>();
        var scriptedRuns = new List<Outcome>();
        for (int s = 1; s <= seeds; s++)
        {
            ulong seed = (ulong)s;
            int playerIndex = (int)(seed % 3);
            var ai = RunOne(seed, years, player: null);
            bool aiSame = ai.Digest == RunOne(seed, years, player: null).Digest;
            var scripted = RunOne(seed, years, player: playerIndex);
            aiRuns.Add(ai); scriptedRuns.Add(scripted);
            bool scriptedSame = scripted.Digest == RunOne(seed, years, player: playerIndex).Digest;

            Console.WriteLine($"===== origins seed {seed}, {years} years: AI-run =====");
            Console.Write(ai.Summary);
            Console.WriteLine(ai.Health(aiSame));
            Console.WriteLine($"===== origins seed {seed}, {years} years: scripted player runs {scripted.PlayerAbbr} =====");
            Console.Write(scripted.Summary);
            Console.WriteLine(scripted.Health(scriptedSame));
            Console.WriteLine();
            if (!ai.Healthy(aiSame) || !scripted.Healthy(scriptedSame)) failures++;
            totals.Add($"seed {seed}: AI {(ai.Healthy(aiSame) ? "HEALTHY" : "UNHEALTHY")}, scripted ({scripted.PlayerAbbr}) {(scripted.Healthy(scriptedSame) ? "HEALTHY" : "UNHEALTHY")}"
                       + $"  chapters AI {ai.Chapters} scripted {scripted.Chapters}  banks AI {ai.Banks}  scripted {scripted.Banks}");
        }
        Console.WriteLine("===== origins: all seeds =====");
        foreach (var t in totals) Console.WriteLine(t);
        MoneyReport(aiRuns, scriptedRuns, years);
        LadderReport(aiRuns, scriptedRuns);
        TitleShotReport(aiRuns);
        SpreadReport(aiRuns);
        Console.WriteLine(failures == 0 ? "ORIGINS HEALTH: OK" : $"ORIGINS HEALTH: {failures} seed(s) failed");
        return failures == 0 ? 0 : 1;
    }

    private sealed class Outcome
    {
        public string Summary = "";
        public string Digest = "";
        public string Banks = "";
        public string? PlayerAbbr;
        public int Chapters;
        public string? Exception;
        public List<string> Violations = new();
        public List<string> Folds = new();
        public List<string> Problems = new();
        public List<string> Notes = new();
        public Dictionary<string, List<string>> Trace = new();
        public Dictionary<string, HashSet<FighterId>> LastRoster = new();
        public List<House> Houses = new();
        public List<(string Key, DateOnly On, string Who)> Ladder = new();   // the weight-ladder chapters: when, and who (or the time limit)
        public List<(int Year, TitleShots.Shot Shot)> Shots = new();         // title shots at a reigning champion (AI runs), by year fought
        public int Years;
        public ObserverPlan? Observer;

        public bool Healthy(bool deterministic) =>
            Exception is null && Violations.Count == 0 && Folds.Count == 0 && Problems.Count == 0 && deterministic;

        public string Health(bool deterministic)
        {
            var parts = new List<string>
            {
                Exception is null ? "no crash" : $"CRASH: {Exception}",
                Folds.Count == 0 ? "the sport lives" : "SPORT DIED: " + string.Join("; ", Folds),
                Violations.Count == 0 ? "chapter order holds, nothing used before it was unlocked" : "VIOLATION: " + string.Join("; ", Violations.Distinct().Take(6)),
            };
            parts.AddRange(Problems.Distinct().Take(8));
            parts.AddRange(Notes.Distinct().Take(6).Select(n => "note: " + n));
            parts.Add(deterministic ? "deterministic (same seed twice, identical world digest)" : "NOT DETERMINISTIC");
            return $"HEALTH {(Healthy(deterministic) ? "OK" : "FAIL")}: " + string.Join(" | ", parts);
        }
    }

    /// <summary>One house's money over the run (the ORIGINS MONEY block reads these).</summary>
    private sealed class House
    {
        public string Abbr = "";
        public string Kind = "";            // founder, authored (a scripted arrival), founded (a generic new house)
        public string Country = "";         // its home city's country code
        public int FoundedYear;
        public bool Player;
        public double FoundedAt;            // years into the run
        public double? FoldedAt;            // years into the run (null: alive at the end)
        public decimal StartBank, EndBank;
        public double YearsAlive;
        public Dictionary<FinanceCategory, decimal> Ledger = new();
        public int Nights;
        public long Crowd, Seats, Demand;
        public decimal Gate, Production, Rent, Payroll;
        public decimal NightNet => Gate - Production - Payroll;
    }

    private static Outcome RunOne(ulong seed, int years, int? player, ObserverPlan? plan = null)
    {
        var o = new Outcome();
        var sb = new StringBuilder();
        World world;
        try
        {
            var options = plan is not null ? WorldBuilder.Options.OriginsObserver(seed) : WorldBuilder.Options.Origins(seed, player ?? 0);
            world = new WorldBuilder().Build(options);
            if (player is null)
            {
                // Nobody's house: every promotion is the AI's.
                if (world.Get(world.PlayerPromotion) is { } p) p.IsPlayerControlled = false;
                world.PlayerPromotion = default;
            }
            o.PlayerAbbr = world.Get(world.PlayerPromotion)?.Abbreviation;
        }
        catch (Exception ex) { o.Exception = ex.GetType().Name + ": " + ex.Message; o.Summary = ""; return o; }

        var start = world.Promotions.Values.ToDictionary(p => p.Id, p => p.Balance.Amount);
        var tick = new WorldTick();
        var script = player is null && plan is null ? null : new Script();
        o.Observer = plan;
        sb.AppendLine($"day one {world.StartDate:yyyy-MM-dd}: {world.Promotions.Count} promotions, {world.Fighters.Count} men " +
                      $"({world.Fighters.Values.Count(f => f.Designed)} pioneers), {world.Fighters.Values.Count(f => f.Contract is null)} unsigned, " +
                      $"{world.Camps.Count} gyms; unlocked: {string.Join(", ", world.Era.Unlocked)}");
        Check(world, o);

        int fightersAtStart = world.Fighters.Count;
        int peakTarget = 0;
        o.Years = years;
        var firstSeen = world.Promotions.Values.ToDictionary(p => p.Id, _ => world.StartDate);
        var bankAtFounding = world.Promotions.Values.ToDictionary(p => p.Id, p => p.Balance.Amount);
        var foldedOn = new Dictionary<PromotionId, DateOnly>();
        var demand = new Dictionary<PromotionId, long>();   // what each night would have drawn with no walls (read the week it ran)
        var shots = player is null ? new TitleShots() : null;   // the challenger's rank at booking (AI runs; read-only)
        try
        {
            for (int y = 0; y < years; y++)
            {
                int year = world.StartDate.Year + y;
                int newgens = 0, retired = 0, washouts = 0;
                int firstNewId = world.Fighters.Keys.Select(k => k.Value).DefaultIfEmpty(0).Max() + 1;
                var aliveAtStart = world.Promotions.Values.Where(p => !p.Defunct).Select(p => p.Id).ToHashSet();
                do
                {
                    plan?.Before(world, o.Problems);
                    if (plan is null || plan.ScriptOn) script?.Week(world);
                    shots?.BeforeWeek(world);
                    int shotsBefore = shots?.Shots.Count ?? 0;
                    var r = tick.AdvanceWeek(world);
                    plan?.After(world, o.Problems);
                    if (shots is not null)
                    {
                        shots.AfterWeek(world, monthBoundary: false);
                        for (int k = shotsBefore; k < shots.Shots.Count; k++) o.Shots.Add((world.CurrentDate.Year, shots.Shots[k]));
                    }
                    newgens += r.Newgens; retired += r.Retired; washouts += r.Washouts;
                    if (y >= years - 2) RosterTrace(world, o);
                    var weekAgo = world.CurrentDate.AddDays(-7);
                    foreach (var e in world.Events.Values.Where(e => e.Status == EventStatus.Completed && e.Date > weekAgo && e.Date <= world.CurrentDate))
                    {
                        demand[e.Promotion] = demand.GetValueOrDefault(e.Promotion) + Financials.Demand(world, e);
                        // ORIGINS_NIGHTS=<abbr>: every night that house runs, with what drew its crowd (a diagnosis aid).
                        if (Environment.GetEnvironmentVariable("ORIGINS_NIGHTS") is { } who && world.Get(e.Promotion) is { } ep && ep.Abbreviation == who)
                        {
                            var m = e.Card.Count > 0 ? e.Card[^1] : null;
                            var hc = MmaManager.Core.Sim.Geography.WorldMap.CityOf(world, e);
                            Console.WriteLine($"NIGHT seed {seed} {(player is null ? "AI" : "PL")} {e.Date:yyyy-MM-dd} {e.Name} crowd {e.Financials!.Attendance}/{(e.VenueId is { } nv ? world.Get(nv)?.Capacity : 0)} demand {Financials.Demand(world, e)} hype {e.HypeBonus:0.00} " +
                                $"cityPop {(hc is { } c ? MmaManager.Core.Sim.Geography.Reputation.OrgInCity(ep, c) : -1)} regionPop {MmaManager.Core.Sim.Geography.Reputation.OrgInRegion(world, ep, e.Region)} prestige {ep.Prestige.Value} " +
                                $"main {world.Get(m?.RedCorner ?? default)?.Intangibles.Popularity.Value}/{world.Get(m?.BlueCorner ?? default)?.Intangibles.Popularity.Value} bouts {e.Card.Count} clash {world.Events.Values.Any(o => Financials.IsClash(world, e.Promotion, e.Date, e.Id, o))}");
                        }
                    }
                    foreach (var p in world.Promotions.Values)
                    {
                        // (A house founded this week: its opening bank is what it had before its first week's costs.)
                        if (firstSeen.TryAdd(p.Id, world.CurrentDate))
                            bankAtFounding[p.Id] = p.Balance.Amount - p.Ledger.Sum(l => l.Amount.Amount);
                        if (p.Defunct) foldedOn.TryAdd(p.Id, world.CurrentDate);
                    }
                } while (world.CurrentDate.Year == year);
                Check(world, o);
                YearLines(world, year, sb, newgens, retired, washouts);
                SportLine(world, year, sb, firstNewId, o);
                ChapterLines(world, year, sb);

                int alive = world.Promotions.Values.Count(p => !p.Defunct);
                if (alive == 0) o.Folds.Add($"{year}: no house left running");
                int actives = world.Fighters.Values.Count(f => f.Status == FighterStatus.Active);
                peakTarget = Math.Max(peakTarget, OriginsTalent.TargetFor(world));
                int ceiling = Math.Max(MaxActives, peakTarget + 50);   // (men outlast the houses that folded under them)
                if (actives < MinActives || actives > ceiling) o.Problems.Add($"{year}: {actives} active men (band {MinActives}-{ceiling})");
                if (y == 0) continue;   // the first year carries each house's opening offset
                // (Houses running all year and solvent at its end: a broke house cutting back is dying, not the sport.)
                foreach (var p in world.Promotions.Values.Where(p => !p.Defunct && aliveAtStart.Contains(p.Id) && p.Balance.Amount >= 0).OrderBy(p => p.Id.Value))
                {
                    int nights = world.Events.Values.Count(e => e.Promotion == p.Id && e.Status == EventStatus.Completed && e.Date.Year == year);
                    int due = (int)Math.Floor(52.0 / OriginsShows.CadenceWeeks(p) * CadenceShare);
                    if (nights < due) o.Problems.Add($"{p.Abbreviation} ran {nights} nights in {year} (cadence floor {due})");
                }
            }
        }
        catch (Exception ex)
        {
            o.Exception = $"{ex.GetType().Name} on {world.CurrentDate:yyyy-MM-dd}: {ex.Message} @ {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}";
        }

        // Health at the end.
        var banks = new List<string>();
        foreach (var p in world.Promotions.Values.OrderBy(p => p.Id.Value))
        {
            decimal s0 = start.GetValueOrDefault(p.Id);
            banks.Add($"{p.Abbreviation} {s0 / 1000m:0}k->{p.Balance.Amount / 1000m:0}k");
            if (p.Defunct)
            {
                o.Notes.Add($"{p.Abbreviation} folded (last night {LastNight(world, p):yyyy-MM-dd}, balance {p.Balance.Amount:N0})");
                continue;
            }
            int need = OriginsShows.MinRoster(p);
            int roster = world.RosterOf(p.Id).Count(f => f.Status == FighterStatus.Active);
            // The floor counts the men on the books, as the house's own signing pass does: not retired and the
            // deal not run out. A man recovering from the last night is still the house's (the check's last week
            // is often a house's night), so how many are fit that day is a note, never a failure.
            // (A man whose deal ran out in the run's last week is still the house's to keep: the last night often
            // ends a few deals the week the check samples, before the owner has had one of his thirty days. A man
            // longer out of his deal than that is not counted: the owner has let him sit.)
            bool JustOut(Fighter f) => DealWindow.InWindow(world, f) && f.Contract?.WindowEndsOn is { } ends
                                       && ends.AddDays(-DealWindow.WindowDays) >= world.CurrentDate.AddDays(-7);
            int onBooks = world.RosterOf(p.Id).Count(f => f.Status != FighterStatus.Retired && (!DealWindow.OutOfDeal(world, f) || JustOut(f)));
            if (roster < need && p.Balance.Amount >= 0)
                o.Notes.Add($"{p.Abbreviation} {roster} of {onBooks} men fit at the end " +
                            (LastNight(world, p) is { } ln ? $"({world.CurrentDate.DayNumber - ln.DayNumber} days after its last night)" : "(no night yet)"));
            // (A house in the red keeps a thin, cheap roster on its way out: that is a fold coming, not a starved sport.)
            if (onBooks < need && p.Balance.Amount >= 0)
            {
                o.Problems.Add($"{p.Abbreviation} roster {onBooks} on the books < {need}");
                var men = world.RosterOf(p.Id).ToList();
                int counted = men.Count(f => f.Status != FighterStatus.Retired);
                int target = OriginsShows.DivisionRosterTarget(p, MmaManager.Core.Sim.Org.Solvency.Distress(p, world) > 0)
                             * Math.Max(1, p.Divisions.Count(d => d.Sex == Sex.Male));
                int down = men.Count(f => f.Status is FighterStatus.Injured or FighterStatus.Suspended);
                var last = LastNight(world, p);
                var next = world.Events.Values.Where(e => e.Promotion == p.Id && e.Status == EventStatus.Scheduled)
                    .Select(e => (DateOnly?)e.Date).DefaultIfEmpty(null).Min();
                int readyByNext = next is { } n ? men.Count(f => f.Status != FighterStatus.Retired && f.IsMatchReady(n)) : -1;
                sb.AppendLine($"  ROSTER DIAG {p.Abbreviation}: active {roster}, on the books {counted} (signing target {target}), injured/suspended {down}, " +
                              $"last night {last:yyyy-MM-dd} ({(last is { } lastOn ? world.CurrentDate.DayNumber - lastOn.DayNumber : -1)} days before the check), " +
                              $"next night {next:yyyy-MM-dd} with {readyByNext} men ready by then, bank {p.Balance.Amount:N0}");
                // Why the signing pass does not fill a division under its target: what the free men in it say
                // to this house's opening offer (the pass's own valuation and offer; read-only).
                var negotiator = new Negotiator();
                var valuation = new FighterValuation();
                decimal cap = Negotiator.PurseCap(world, p);
                int perDiv = OriginsShows.DivisionRosterTarget(p, MmaManager.Core.Sim.Org.Solvency.Distress(p, world) > 0);
                foreach (var d in p.Divisions.Where(d => d.Sex == Sex.Male))
                {
                    int have = men.Count(f => d.Matches(f) && f.Status != FighterStatus.Retired);
                    if (have >= perDiv) continue;
                    var pool = world.FreeAgents().Where(f => d.Matches(f) && f.Status == FighterStatus.Active).ToList();
                    int yes = 0, overCap = 0, no = 0;
                    var asks = new List<decimal>();
                    foreach (var fa in pool)
                    {
                        var exp = valuation.Evaluate(world, fa);
                        asks.Add(exp.AskingPurse.Amount);
                        var offer = negotiator.AiOpeningOffer(world, p, fa, exp);
                        if (negotiator.Evaluate(world, fa, offer, exp).Verdict == NegotiationVerdict.Accept) yes++;
                        else if (exp.AskingPurse.Amount * Negotiator.NonExclusiveMoneyFactor > cap) overCap++;
                        else no++;
                    }
                    asks.Sort();
                    sb.AppendLine($"    {(d.Open ? "Open" : d.WeightClass.ToString())} {have}/{perDiv}: {pool.Count} free men, would accept {yes}, ask over the purse cap {cap:N0}: {overCap}, other refusals {no}"
                                  + (asks.Count > 0 ? $", median ask {asks[asks.Count / 2]:N0}, cheapest {asks[0]:N0}" : ""));
                }
                if (o.Trace.TryGetValue(p.Abbreviation, out var lines))
                {
                    sb.AppendLine($"  ROSTER TRACE {p.Abbreviation} (last two years, weekly):");
                    foreach (var l in lines) sb.AppendLine("    " + l);
                }
            }
            if (s0 == 0) continue;   // a house founded during the run
            // (Money was a failure line in phase 1's five quiet years; the Backlash is meant to bite.)
            if (p.Balance.Amount < s0 * MoneyFloor) o.Notes.Add($"{p.Abbreviation} money down ({s0:N0} -> {p.Balance.Amount:N0})");
            if (p.Balance.Amount > s0 * MoneyCeiling) o.Notes.Add($"{p.Abbreviation} money up ({s0:N0} -> {p.Balance.Amount:N0})");
        }
        // Each house's money over the run, for the cross-seed ORIGINS MONEY block.
        var founderAbbrs = OriginsPromotions.All.Select(x => x.Abbreviation).ToHashSet();
        var arrivalNames = OriginsArrivals.All.Where(a => a.Kind == OriginsArrivalKind.Promotion).Select(a => a.Name).ToHashSet();
        double YearsIn(DateOnly d) => (d.DayNumber - world.StartDate.DayNumber) / 365.25;
        foreach (var p in world.Promotions.Values.OrderBy(p => p.Id.Value))
        {
            var h = new House
            {
                Abbr = p.Abbreviation,
                Kind = founderAbbrs.Contains(p.Abbreviation) ? "founder" : arrivalNames.Contains(p.Name) ? "authored" : "founded",
                Player = p.Id == world.PlayerPromotion,
                FoundedAt = YearsIn(firstSeen.GetValueOrDefault(p.Id, world.StartDate)),
                FoldedAt = foldedOn.TryGetValue(p.Id, out var fo) ? YearsIn(fo) : null,
                Country = p.HomeCity is { } hcy && world.Get(hcy) is { } hcc ? world.Get(hcc.Country)?.Code ?? "?" : "?",
                FoundedYear = firstSeen.GetValueOrDefault(p.Id, world.StartDate).Year,
                StartBank = bankAtFounding.GetValueOrDefault(p.Id),
                EndBank = p.Balance.Amount,
                Demand = demand.GetValueOrDefault(p.Id),
            };
            h.YearsAlive = (h.FoldedAt ?? YearsIn(world.CurrentDate)) - h.FoundedAt;
            foreach (var l in p.Ledger) h.Ledger[l.Category] = h.Ledger.GetValueOrDefault(l.Category) + l.Amount.Amount;
            foreach (var e in world.Events.Values.Where(e => e.Promotion == p.Id && e.Status == EventStatus.Completed && e.Financials is not null))
            {
                h.Nights++;
                h.Crowd += e.Financials!.Attendance;
                var room = e.VenueId is { } vid ? world.Get(vid) : null;
                h.Seats += room?.Capacity ?? 0;
                h.Rent += Financials.RentOf(world, room).Amount;
                h.Gate += e.Financials.GateRevenue.Amount;
                h.Production += e.Financials.ProductionCost.Amount;
                h.Payroll += e.Financials.FighterPayroll.Amount;
            }
            o.Houses.Add(h);
        }

        o.Chapters = world.Era.Chapters.Count;
        foreach (var r in world.Era.Chapters.Where(r => OriginsReforms.Ladder.Contains(r.Key)).OrderBy(r => r.FiredOn))
        {
            var by = r.Promotion is { } lp ? world.Get(lp) : null;
            string who = by is null ? "time limit"
                : $"{by.Abbreviation} ({(founderAbbrs.Contains(by.Abbreviation) ? "founder" : arrivalNames.Contains(by.Name) ? "authored" : "founded")}{(by.Id == world.PlayerPromotion ? ", player" : "")})";
            o.Ladder.Add((r.Key, r.FiredOn, who));
        }
        if (world.Fighters.Count <= fightersAtStart) o.Problems.Add("no new men turned pro");
        if (plan is not null) { plan.Finish(world, o.Problems, sb); o.PlayerAbbr = world.Get(world.PlayerPromotion)?.Abbreviation ?? "(none)"; }
        o.Banks = string.Join(", ", banks);
        o.Summary = sb.ToString();
        o.Digest = LongSim.Check.WorldGraph.Digest(world, canonical: false);
        return o;
    }

    // Each house's roster, weekly over the last two years: who is on it in what state, what the signing pass
    // aims for, the free men who would fit, and who left since last week (and why). Printed only for a house
    // that ends under its floor.
    private static void RosterTrace(World world, Outcome o)
    {
        foreach (var p in world.Promotions.Values.Where(p => !p.Defunct).OrderBy(p => p.Id.Value))
        {
            var men = world.RosterOf(p.Id).ToList();
            int active = men.Count(f => f.Status == FighterStatus.Active);
            int counted = men.Count(f => f.Status != FighterStatus.Retired);
            int outOfDeal = men.Count(f => DealWindow.OutOfDeal(world, f));
            int target = OriginsShows.DivisionRosterTarget(p, MmaManager.Core.Sim.Org.Solvency.Distress(p, world) > 0);
            var free = world.FreeAgents().Where(f => f.Status == FighterStatus.Active).ToList();
            string divs = string.Join(" ", p.Divisions.Select(d =>
                $"{(d.Open ? "Open" : d.WeightClass.ToString())}:{men.Count(f => d.Matches(f) && f.Status != FighterStatus.Retired)}/{target} fa{free.Count(d.Matches)}"));
            var ids = men.Select(f => f.Id).ToHashSet();
            var left = new List<string>();
            if (o.LastRoster.TryGetValue(p.Abbreviation, out var prev))
                foreach (var id in prev.Where(id => !ids.Contains(id)).OrderBy(id => id.Value))
                {
                    var f = world.Get(id);
                    string why = f is null ? "gone" : f.Status == FighterStatus.Retired ? "retired"
                        : f.Contract is null ? "deal ended" : $"to {world.Get(f.Contract.Promotion)?.Abbreviation}";
                    left.Add($"{f?.LastName ?? "?"}({f?.WeightClass}) {why}");
                }
            var joined = o.LastRoster.TryGetValue(p.Abbreviation, out var prev2) ? men.Count(f => !prev2.Contains(f.Id)) : 0;
            o.LastRoster[p.Abbreviation] = ids;
            if (!o.Trace.TryGetValue(p.Abbreviation, out var lines)) o.Trace[p.Abbreviation] = lines = new();
            lines.Add($"{world.CurrentDate:yyyy-MM-dd} active {active} counted {counted} outOfDeal {outOfDeal} bal {p.Balance.Amount / 1000m:0}k " +
                      $"fmt {p.Rules.ShowFormat} [{divs}] +{joined}" + (left.Count > 0 ? " -" + string.Join(", ", left) : ""));
        }
    }

    /// <summary>
    /// ORIGINS MONEY: across every seed, the founders' end banks (and folds), how long new houses last, the
    /// scripted player's bank, and where a house's money goes (per night and per house-year) by kind.
    /// Read-only reporting; never a failure.
    /// </summary>
    private static void MoneyReport(List<Outcome> ai, List<Outcome> scripted, int years)
    {
        static string Dist(IEnumerable<decimal> xs)
        {
            var v = xs.OrderBy(x => x).ToList();
            if (v.Count == 0) return "-";
            decimal Q(double q) => v[(int)Math.Round(q * (v.Count - 1))];
            return $"min {Q(0) / 1000m:0}k p25 {Q(0.25) / 1000m:0}k median {Q(0.5) / 1000m:0}k p75 {Q(0.75) / 1000m:0}k max {Q(1) / 1000m:0}k";
        }
        Console.WriteLine($"===== ORIGINS MONEY ({ai.Count} seeds, {years} years) =====");
        var aiHouses = ai.SelectMany(o => o.Houses).ToList();
        foreach (var abbr in OriginsPromotions.All.Select(x => x.Abbreviation))
        {
            var hs = aiHouses.Where(h => h.Abbr == abbr && h.Kind == "founder").ToList();
            if (hs.Count == 0) continue;
            int below = hs.Count(h => h.EndBank < h.StartBank);
            Console.WriteLine($"AI founder {abbr} (start {hs[0].StartBank / 1000m:0}k): end bank {Dist(hs.Select(h => h.EndBank))}; " +
                              $"below start {below}/{hs.Count}; folded {hs.Count(h => h.FoldedAt is not null)}/{hs.Count}");
        }
        foreach (var kind in new[] { "founded", "authored" })
        {
            var hs = aiHouses.Where(h => h.Kind == kind).ToList();
            if (hs.Count == 0) continue;
            string Survive(int y)
            {
                var old = hs.Where(h => h.FoundedAt <= years - y).ToList();
                return old.Count == 0 ? $"{y}y n/a" : $"{y}y {old.Count(h => h.FoldedAt is null || h.FoldedAt - h.FoundedAt >= y)}/{old.Count}";
            }
            var folds = hs.Where(h => h.FoldedAt is not null).Select(h => h.FoldedAt!.Value - h.FoundedAt).OrderBy(x => x).ToList();
            Console.WriteLine($"AI {kind} houses: {hs.Count} opened ({hs.Count / (double)ai.Count:0.0}/world), folded {folds.Count}, alive at the end {hs.Count(h => h.FoldedAt is null)}; " +
                              $"survived {Survive(3)}, {Survive(6)}, {Survive(9)}; median age at fold {(folds.Count == 0 ? 0 : folds[folds.Count / 2]):0.0}y; " +
                              $"opening bank {Dist(hs.Select(h => h.StartBank))}; end bank of the living {Dist(hs.Where(h => h.FoldedAt is null).Select(h => h.EndBank))}");
            // A house's money a year: what it gained or lost per year alive (a folded house's to its fold).
            Console.WriteLine($"   bank change a year: {Dist(hs.Select(h => (h.EndBank - h.StartBank) / (decimal)Math.Max(0.25, h.YearsAlive)))}");
            foreach (int y in new[] { 6, 9, years })
                if (y <= years) Console.WriteLine($"   houses of this kind alive at the end of year {y}: {hs.Count(h => h.FoundedAt <= y && (h.FoldedAt is null || h.FoldedAt > y)) / (double)ai.Count:0.00}/world");
        }
        // Where the money goes, by kind: per night, and per house-year by ledger line.
        foreach (var kind in new[] { "founder", "founded", "authored" })
        {
            var hs = aiHouses.Where(h => h.Kind == kind).ToList();
            int nights = hs.Sum(h => h.Nights);
            if (nights == 0) continue;
            double houseYears = hs.Sum(h => h.YearsAlive);
            Console.WriteLine($"AI {kind} nights ({nights}): crowd {hs.Sum(h => h.Crowd) / (double)nights:0} in {hs.Sum(h => h.Seats) / (double)nights:0} seats (demand {hs.Sum(h => h.Demand) / (double)nights:0}) " +
                              $"({100.0 * hs.Sum(h => h.Crowd) / Math.Max(1, hs.Sum(h => h.Seats)):0}% full), ticket {hs.Sum(h => h.Gate) / Math.Max(1, hs.Sum(h => h.Crowd)):0.0}/head; " +
                              $"per night gate {hs.Sum(h => h.Gate) / nights / 1000m:0.0}k, production {(hs.Sum(h => h.Production) - hs.Sum(h => h.Rent)) / nights / 1000m:0.0}k + room {hs.Sum(h => h.Rent) / nights / 1000m:0.0}k, " +
                              $"payroll {hs.Sum(h => h.Payroll) / nights / 1000m:0.0}k, net {hs.Sum(h => h.NightNet) / nights / 1000m:+0.0;-0.0}k; nights/house-year {nights / Math.Max(0.01, houseYears):0.0}");
            var cats = hs.SelectMany(h => h.Ledger).GroupBy(kv => kv.Key).Select(g => (Cat: g.Key, Sum: g.Sum(kv => kv.Value)))
                .Where(x => x.Sum != 0).OrderByDescending(x => Math.Abs(x.Sum)).ThenBy(x => x.Cat).ToList();
            Console.WriteLine($"   per house-year ({houseYears:0} house-years): " + string.Join(", ", cats.Select(x => $"{x.Cat} {x.Sum / (decimal)houseYears / 1000m:+#,0.0;-#,0.0}k"))
                              + $"; net {cats.Sum(x => x.Sum) / (decimal)houseYears / 1000m:+0.0;-0.0}k");
        }
        foreach (var abbr in OriginsPromotions.All.Select(x => x.Abbreviation))
        {
            var hs = aiHouses.Where(h => h.Abbr == abbr && h.Kind == "founder").ToList();
            int nights = hs.Sum(h => h.Nights);
            if (nights == 0) continue;
            double hy = hs.Sum(h => h.YearsAlive);
            var cats = hs.SelectMany(h => h.Ledger).GroupBy(kv => kv.Key).Select(g => (Cat: g.Key, Sum: g.Sum(kv => kv.Value)))
                .Where(x => x.Sum != 0).OrderByDescending(x => Math.Abs(x.Sum)).ThenBy(x => x.Cat).ToList();
            Console.WriteLine($"AI {abbr}: crowd {hs.Sum(h => h.Crowd) / (double)nights:0} in {hs.Sum(h => h.Seats) / (double)nights:0} seats (demand {hs.Sum(h => h.Demand) / (double)nights:0}), room {hs.Sum(h => h.Rent) / nights / 1000m:0.0}k/night, " +
                              $"{nights / hy:0.0} nights/yr; per year " + string.Join(", ", cats.Select(x => $"{x.Cat} {x.Sum / (decimal)hy / 1000m:+#,0.0;-#,0.0}k")));
        }
        var players = scripted.SelectMany(o => o.Houses).Where(h => h.Player).ToList();
        foreach (var g in players.GroupBy(h => h.Abbr).OrderBy(g => g.Key))
        {
            var hs = g.ToList();
            Console.WriteLine($"scripted player at {g.Key} ({hs.Count} seeds, start {hs[0].StartBank / 1000m:0}k): end bank {Dist(hs.Select(h => h.EndBank))}; " +
                              $"below start {hs.Count(h => h.EndBank < h.StartBank)}/{hs.Count}; folded {hs.Count(h => h.FoldedAt is not null)}/{hs.Count}");
        }
        var sh = scripted.SelectMany(o => o.Houses).ToList();
        foreach (var abbr in OriginsPromotions.All.Select(x => x.Abbreviation))
        {
            var hs = sh.Where(h => h.Abbr == abbr && h.Kind == "founder" && !h.Player).ToList();
            if (hs.Count > 0) Console.WriteLine($"   ({abbr} under the AI in the scripted worlds: end bank {Dist(hs.Select(h => h.EndBank))}, folded {hs.Count(h => h.FoldedAt is not null)}/{hs.Count})");
        }
        var shFounded = sh.Where(h => h.Kind == "founded").ToList();
        if (shFounded.Count > 0)
            Console.WriteLine($"   (founded houses in the scripted worlds: {shFounded.Count} opened, alive at the end {shFounded.Count(h => h.FoldedAt is null)})");
    }

    /// <summary>
    /// ORIGINS LADDER: when each weight-ladder chapter fired in every seed and who drew it (a house crowning the first
    /// champion of the new class, or the chapter's time limit), then per chapter across the seeds: how often it fired,
    /// the median and range of its date, and how often it fell to the time limit. Read-only reporting.
    /// </summary>
    private static void LadderReport(List<Outcome> ai, List<Outcome> scripted)
    {
        Console.WriteLine($"===== ORIGINS LADDER ({ai.Count} seeds) =====");
        for (int i = 0; i < ai.Count; i++)
            Console.WriteLine($"seed {i + 1} AI: " + string.Join("; ", ai[i].Ladder.Select(l => $"{l.Key} {l.On:yyyy-MM} {l.Who}")));
        foreach (var (label, runs) in new[] { ("AI", ai), ("scripted", scripted) })
            foreach (var key in OriginsReforms.Ladder)
            {
                var fired = runs.SelectMany(o => o.Ladder.Where(l => l.Key == key)).ToList();
                if (fired.Count == 0) { Console.WriteLine($"{label} {key}: fired 0/{runs.Count}"); continue; }
                var days = fired.Select(l => l.On).OrderBy(d => d).ToList();
                Console.WriteLine($"{label} {key}: fired {fired.Count}/{runs.Count}, median {days[days.Count / 2]:yyyy-MM}, earliest {days[0]:yyyy-MM}, latest {days[^1]:yyyy-MM}; " +
                                  $"time limit {fired.Count(l => l.Who == "time limit")}, founder {fired.Count(l => l.Who.Contains("founder"))}, " +
                                  $"authored {fired.Count(l => l.Who.Contains("authored"))}, founded {fired.Count(l => l.Who.Contains("(founded"))}, player {fired.Count(l => l.Who.Contains("player"))}");
            }
    }

    /// <summary>
    /// ORIGINS TITLE SHOTS: every title fight at a reigning champion the AI houses fought (snapshotted the week before,
    /// <see cref="TitleShots"/>), by the challenger's rank in the house's division going in: top 3, top 5, unranked.
    /// By era (the bracket and card years before the unified code, then after). Card-night title fights only: a bracket
    /// night's card is empty until it runs, so its title fights are never snapshotted. Read-only reporting.
    /// </summary>
    private static void TitleShotReport(List<Outcome> ai)
    {
        Console.WriteLine($"===== ORIGINS TITLE SHOTS ({ai.Count} seeds, AI runs; challenger's rank at booking; card-night title fights only, bracket finals are not counted) =====");
        foreach (var (label, lo, hi) in new[] { ("all years", 0, 9999), ("1993-2005", 0, 2005), ("2006-2009", 2006, 2009), ("2010+", 2010, 9999) })
        {
            var s = ai.SelectMany(o => o.Shots).Where(x => x.Year >= lo && x.Year <= hi).Select(x => x.Shot).ToList();
            if (s.Count == 0) { Console.WriteLine($"{label}: none"); continue; }
            double P(Func<TitleShots.Shot, bool> f) => 100.0 * s.Count(f) / s.Count;
            var ranked = s.Where(x => x.Rank > 0).ToList();
            Console.WriteLine($"{label}: {s.Count} title shots ({s.Count / (double)ai.Count:0.0}/world); #1 {P(x => x.Rank == 1):0.0}%, top-3 {P(x => x.Rank is >= 1 and <= 3):0.0}%, " +
                              $"top-5 {P(x => x.Rank is >= 1 and <= 5):0.0}%, ranked 6+ {P(x => x.Rank > 5):0.0}%, unranked {P(x => x.Rank == 0):0.0}%; " +
                              $"mean rank (ranked) {(ranked.Count == 0 ? 0 : ranked.Average(x => x.Rank)):0.0}; challenger on a loss {P(x => x.Streak < 0):0.0}%");
        }
    }

    /// <summary>
    /// ORIGINS SPREAD: where the AI's new houses (founded and authored arrivals) open, by era of their founding, as
    /// country codes, and the share outside the USA, Japan and Brazil. Read-only reporting.
    /// </summary>
    private static void SpreadReport(List<Outcome> ai)
    {
        Console.WriteLine($"===== ORIGINS SPREAD ({ai.Count} seeds, AI runs; new houses by country and era of founding) =====");
        var core = new HashSet<string>(StringComparer.Ordinal) { "USA", "JPN", "BRA" };
        foreach (var (label, lo, hi) in new[] { ("1993-1999", 0, 1999), ("2000-2005", 2000, 2005), ("2006-2009", 2006, 2009), ("2010+", 2010, 9999), ("all", 0, 9999) })
        {
            var hs = ai.SelectMany(o => o.Houses).Where(h => h.Kind != "founder" && h.FoundedYear >= lo && h.FoundedYear <= hi).ToList();
            if (hs.Count == 0) { Console.WriteLine($"{label}: none"); continue; }
            var by = hs.GroupBy(h => h.Country).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => $"{g.Key}:{g.Count()}");
            int outside = hs.Count(h => !core.Contains(h.Country));
            Console.WriteLine($"{label}: {hs.Count} houses ({hs.Count / (double)ai.Count:0.0}/world), outside USA/JPN/BRA {outside} ({100.0 * outside / hs.Count:0}%), " +
                              $"{hs.Select(h => h.Country).Distinct().Count()} countries; folded {hs.Count(h => h.FoldedAt is not null)} [{string.Join(" ", by)}]");
        }
    }

    private static DateOnly? LastNight(World world, Promotion p) =>
        world.Events.Values.Where(e => e.Promotion == p.Id && e.Status == EventStatus.Completed)
            .Select(e => (DateOnly?)e.Date).DefaultIfEmpty(null).Max();

    private static bool IsFinish(FightMethod m) => m is FightMethod.Knockout or FightMethod.TechnicalKnockout
        or FightMethod.SubmissionTap or FightMethod.TechnicalSubmission;

    private static void YearLines(World world, int year, StringBuilder sb, int newgens, int retired, int washouts)
    {
        int active = world.Fighters.Values.Count(f => f.Status == FighterStatus.Active);
        int free = world.Fighters.Values.Count(f => f.Status == FighterStatus.Active && f.Contract is null);
        sb.AppendLine($"{year}: active men {active}, free agents {free}, new pros {newgens}, retired {retired}, washed out {washouts}, promotions alive {world.Promotions.Values.Count(p => !p.Defunct)}");
        foreach (var p in world.Promotions.Values.OrderBy(p => p.Id.Value))
        {
            var nights = world.Events.Values.Where(e => e.Promotion == p.Id && e.Date.Year == year).ToList();
            var done = nights.Where(e => e.Status == EventStatus.Completed).ToList();
            int cancelled = nights.Count(e => e.Status == EventStatus.Cancelled);
            var bouts = done.SelectMany(e => e.Card).Where(f => f.Outcome is not null).ToList();
            int finishes = bouts.Count(f => IsFinish(f.Outcome!.Method));
            int draws = bouts.Count(f => f.Outcome!.Method == FightMethod.Draw);
            int decisions = bouts.Count(f => f.Outcome!.Method is FightMethod.UnanimousDecision or FightMethod.SplitDecision or FightMethod.MajorityDecision);
            int tournaments = done.Count(e => e.Format == EventFormat.Tournament);
            var title = p.Titles.Select(world.Get).OfType<Title>().FirstOrDefault();
            // A bracket night has a champion when its winner took the belt (a new reign or the holder again).
            int crowned = title?.Lineage.Count(r => r.WonOn.Year == year) ?? 0;
            int brackets = done.Count(e => e.Format == EventFormat.Tournament);
            // (Read off the winners' accolades, a durable record: the news feed is capped.)
            int champions = done.Count(e => e.Format == EventFormat.Tournament
                && world.Fighters.Values.Any(f => f.Accolades.Any(a => a.Date == e.Date && a.Title == $"Won {e.Name}")));
            int noChampion = brackets - champions;
            int roster = world.RosterOf(p.Id).Count(f => f.Status == FighterStatus.Active);
            string champ = title?.CurrentChampion is { } c && world.Get(c) is { } cf ? $"{cf.FirstName} {cf.LastName}" : "-";
            double pct(int n) => bouts.Count == 0 ? 0 : 100.0 * n / bouts.Count;
            sb.AppendLine($"  {p.Abbreviation,-4} {(p.Defunct ? "FOLDED" : "alive "),-6} bal {p.Balance.Amount,12:N0}  prestige {p.Prestige.Value,2}  " +
                          $"nights {done.Count}{(tournaments > 0 ? $" ({tournaments} brackets)" : "")}{(cancelled > 0 ? $" +{cancelled} off" : "")}  bouts {bouts.Count,3}  " +
                          $"finish {pct(finishes),3:0}% draw {pct(draws),3:0}% dec {pct(decisions),3:0}%  " +
                          (title is null ? "no belt" : brackets > 0 ? $"new champs {crowned} no-champ nights {noChampion} holder {champ}" : $"new champs {crowned} holder {champ}") +
                          $"  roster {roster}  avg house {(done.Count == 0 ? 0 : done.Average(e => e.Financials?.Attendance ?? 0)):0}/{(done.Select(e => e.VenueId).FirstOrDefault() is { } v && world.Get(v) is { } room ? room.Capacity : 0)}");
            var money = p.Ledger.Where(l => l.Date.Year == year)
                .GroupBy(l => l.Category).Select(g => (Cat: g.Key, Sum: g.Sum(l => l.Amount.Amount)))
                .Where(x => x.Sum != 0).OrderByDescending(x => Math.Abs(x.Sum)).ThenBy(x => x.Cat);
            sb.AppendLine("       money: " + string.Join(", ", money.Select(x => $"{x.Cat} {x.Sum / 1000m:+#,0;-#,0}k")));
            if (!p.Defunct) sb.AppendLine("       " + RosterLine(world, p));
        }
    }

    /// <summary>
    /// A house's year-end roster against what it wants, and its purse cap against what the free men in its
    /// divisions ask (and how many would take its opening offer): read-only, the signing pass's own numbers.
    /// </summary>
    private static string RosterLine(World world, Promotion p)
    {
        bool distressed = MmaManager.Core.Sim.Org.Solvency.Distress(p, world) > 0;
        var divs = p.Divisions.Where(d => d.Sex == Sex.Male).ToList();
        int per = OriginsShows.DivisionRosterTarget(p, distressed);
        var men = world.RosterOf(p.Id).Where(f => f.Status != FighterStatus.Retired).ToList();
        int bookable = men.Count(f => OriginsShows.CanBook(world, p, f, world.CurrentDate.AddDays(28)));
        decimal cap = Negotiator.PurseCap(world, p);
        var negotiator = new Negotiator();
        var valuation = new FighterValuation();
        var pool = world.FreeAgents().Where(f => f.Status == FighterStatus.Active && divs.Any(d => d.Matches(f))).OrderBy(f => f.Id.Value).ToList();
        int yes = 0;
        var mine = new List<decimal>();
        foreach (var fa in pool)
        {
            var exp = valuation.Evaluate(world, fa);
            mine.Add(exp.AskingPurse.Amount);
            var offer = negotiator.AiOpeningOffer(world, p, fa, exp);
            if (negotiator.Evaluate(world, fa, offer, exp).Verdict == NegotiationVerdict.Accept) yes++;
        }
        mine.Sort();
        string short_ = string.Join(" ", divs.Where(d => men.Count(f => d.Matches(f)) < per)
            .Select(d => $"{(d.Open ? "Open" : d.WeightClass.ToString())}:{men.Count(f => d.Matches(f))}"));
        return $"roster: {divs.Count} div x {per} = {per * Math.Max(1, divs.Count)} wanted, {men.Count} held ({bookable} bookable in 4w); " +
               $"fmt {p.Rules.ShowFormat} cadence {OriginsShows.CadenceWeeks(p)}w; cap {cap:N0} (ceiling {FighterValuation.OriginsMarketCeiling(world):N0}); " +
               $"free men in its divisions {pool.Count}, asks min {(mine.Count > 0 ? mine[0] : 0):N0} median {(mine.Count > 0 ? mine[mine.Count / 2] : 0):N0}, would accept {yes}" +
               (distressed ? " DISTRESSED" : "") + (short_.Length > 0 ? $"; short: {short_}" : "");
    }

    /// <summary>
    /// The sport spreading (phase 3), one line a year: the awake scenes and where the houses are, the gyms and how
    /// many teach the whole game, the all-rounders among the active men and among the year's new men, the weight
    /// classes the new men came in at, and whether the rankings across promotions are recognised. Also the phase 3
    /// checks: every scene awake a month has a gym, every house the commissions bind fights their code, and no
    /// list of the sport's best before the code.
    /// </summary>
    private static void SportLine(World world, int year, StringBuilder sb, int firstNewId, Outcome o)
    {
        var scenes = OriginsScenes.Scenes(world);
        var active = world.Fighters.Values.Where(f => f.Status == FighterStatus.Active).ToList();
        var fresh = world.Fighters.Values.Where(f => f.Id.Value >= firstNewId && !f.Designed).ToList();
        double pct(int n, int d) => d == 0 ? 0 : 100.0 * n / d;
        int crossGyms = world.Camps.Values.Count(c => OriginsCamps.CrossTrains(world, c));
        var housesBy = world.Promotions.Values.Where(p => !p.Defunct && p.HomeCity is not null)
            .GroupBy(p => world.Get(world.Get(p.HomeCity!.Value)!.Country)!.Code).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Key}:{g.Count()}");
        var sizes = fresh.GroupBy(f => f.WeightClass).OrderBy(g => g.Key).Select(g => $"{g.Key.ToString()[..Math.Min(3, g.Key.ToString().Length)]}{g.Count()}");
        int famous = world.Camps.Values.Count(c => c.Reputation >= 55);
        var (arWins, arBouts) = OriginsTimeline.AllRounderRecord(world, world.CurrentDate.AddYears(-1));
        sb.AppendLine($"  SPORT   scenes {scenes.Count} [{string.Join(" ", scenes.Select(s => s.Code))}]{(OriginsScenes.SecondCentre(world) is { } sc ? $" centre2 {sc}" : "")}  houses [{string.Join(" ", housesBy)}]  " +
                      $"gyms {world.Camps.Count} ({crossGyms} whole-game, {famous} famous)  all-rounders {pct(active.Count(OriginsTalent.IsAllRounder), active.Count):0}% of active, " +
                      $"{pct(fresh.Count(OriginsTalent.IsAllRounder), fresh.Count):0}% of {fresh.Count} new, won {arWins}/{arBouts} vs specialists  new sizes [{string.Join(" ", sizes)}]  " +
                      $"code {world.Promotions.Values.Count(p => !p.Defunct && OriginsReforms.HasUnified(p))}/{world.Promotions.Values.Count(p => !p.Defunct)}  rankings {(OriginsReforms.RankingsRecognised(world) ? "recognised" : "house only")}");

        // Phase 3 checks.
        foreach (var s in scenes)
            if (s.Gyms == 0 && world.CurrentDate > s.WokeOn.AddMonths(2)) o.Violations.Add($"{year}: scene {s.Code} awake since {s.WokeOn:yyyy-MM} has no gym");
        foreach (var p in world.Promotions.Values.Where(p => !p.Defunct && OriginsReforms.BoundByCode(world, p) && !OriginsReforms.HasUnified(p)
                     && !OriginsReforms.BracketBooked(world, p)))   // (a booked bracket runs as announced; the house takes the code after it)
            o.Violations.Add($"{year}: {p.Abbreviation} is licensed but not under the code");
        if (!OriginsReforms.RankingsRecognised(world) && (world.PoundForPound.Count > 0 || world.GlobalRankings.Values.Any(l => l.Count > 0)))
            o.Violations.Add($"{year}: rankings across promotions published before the code");
        if (!world.Era.HasFired(OriginsCamps.FirstCampChapter) && fresh.Any(f => f.PrimaryStyle == PrimaryStyle.WellRounded))
            o.Violations.Add($"{year}: a cross-trained new man before the first famous camp");
    }

    /// <summary>
    /// The timeline's invariants, checked every year-end (and on day one): the chapter order, and nothing
    /// used before the sport has it. The first use of a contested capability IS its chapter (it fires the
    /// same tick), so a use up to a week before the unlock is the use that unlocked it.
    /// </summary>
    private static void Check(World world, Outcome o)
    {
        string when = world.CurrentDate.ToString("yyyy-MM-dd");
        if (world.Mode != WorldMode.Origins) o.Violations.Add($"{when}: world mode {world.Mode}");
        var start = world.StartDate;

        // The chapter order: inside its window, after everything it requires.
        foreach (var r in world.Era.Chapters)
        {
            var c = OriginsChapters.All.FirstOrDefault(x => x.Key == r.Key);
            if (c is null) { o.Violations.Add($"{when}: unknown chapter {r.Key}"); continue; }
            if (r.FiredOn < start.AddMonths(c.EarliestMonth)) o.Violations.Add($"{r.Key} fired {r.FiredOn:yyyy-MM} before its window");
            if (r.FiredOn > start.AddMonths(c.LatestMonth + 1).AddDays(7)) o.Violations.Add($"{r.Key} fired {r.FiredOn:yyyy-MM}, after its deadline");
            foreach (var req in c.Requires)
                if (world.Era.Chapter(req) is not { } rr || rr.FiredOn >= r.FiredOn)
                    o.Violations.Add($"{r.Key} fired before {req}");
            foreach (var u in c.Unlocks)
                if (Era.TryFromChapterUnlock(u, out var cap) && !world.Era.IsUnlocked(cap))
                    o.Violations.Add($"{r.Key} fired but {u} is locked");
        }
        // Every chapter whose deadline has passed has fired.
        foreach (var c in OriginsChapters.All)
            if (!world.Era.HasFired(c.Key) && world.CurrentDate > start.AddMonths(c.LatestMonth + 1).AddDays(7)
                && c.Requires.All(world.Era.HasFired))
                o.Violations.Add($"{c.Key} is past its deadline and has not fired");

        DateOnly? UnlockedOn(Capability cap)
        {
            if (OriginsWorldBuilder.DayOneUnlocks.Contains(cap)) return start;
            foreach (var r in world.Era.Chapters)
                if (OriginsChapters.All.FirstOrDefault(x => x.Key == r.Key) is { } c
                    && c.Unlocks.Any(u => Era.TryFromChapterUnlock(u, out var k) && k == cap))
                    return r.FiredOn;
            return null;
        }
        bool Before(Capability cap, DateOnly used) => UnlockedOn(cap) is not { } on || used < on.AddDays(-7);

        // (A house the player founded is exempt from the sport's ceiling on houses: PlayerFounding.)
        var foundersHere = OriginsPromotions.All.Select(x => x.Abbreviation).ToHashSet();
        if (world.Promotions.Values.Count(p => !p.Defunct && !(p.IsPlayerControlled && !foundersHere.Contains(p.Abbreviation))) > 3 && !world.Era.HasFired("rivals-appear"))
            o.Violations.Add($"{when}: a fourth house before the rivals appeared");
        foreach (var p in world.Promotions.Values)
        {
            foreach (var d in p.Divisions)
            {
                if (d.Sex != Sex.Male) o.Violations.Add($"{when}: {p.Abbreviation} runs a women's division");
                if (d.Open) continue;
                var cap = Capability.Class(d.WeightClass);
                var opens = OriginsChapters.All.First(c => c.Unlocks.Contains("WeightClass:" + d.WeightClass));
                if (!world.Era.IsUnlocked(cap) && !OriginsTimeline.IsLive(world, opens, world.CurrentDate))
                    o.Violations.Add($"{when}: {p.Abbreviation} runs a {d.WeightClass} division the sport has not opened");
                if (d.Title is { } tid && world.Get(tid) is { Lineage.Count: > 0 } t && Before(cap, t.Lineage[0].WonOn))
                    o.Violations.Add($"{when}: {p.Abbreviation} crowned a {d.WeightClass} champion before the class was unlocked");
            }
            foreach (var m in p.MediaDeals)
                if (Before(Capability.Media(m.Kind), m.SignedOn)) o.Violations.Add($"{when}: {p.Abbreviation} signed a {m.Kind} deal before it was unlocked");
            if (p.Stream is { } s && Before(Capability.InHouseStream, s.LaunchedOn)) o.Violations.Add($"{when}: {p.Abbreviation} launched a stream before it was unlocked");
        }
        if (world.Fighters.Values.Any(f => f.Sex == Sex.Female)) o.Violations.Add($"{when}: a woman in the talent pool (women's MMA is a later phase)");
        if (world.Fighters.Values.Any(f => f.LocalTalent)) o.Violations.Add($"{when}: local filler exists (no club scene)");
        foreach (var e in world.Events.Values.Where(e => e.Status == EventStatus.Completed))
        {
            if (e.Promotion.Value == 0) { o.Violations.Add($"{when}: a club show ran ({e.Name})"); continue; }
            if (world.Get(e.VenueId ?? default) is { } v && world.Get(v.City) is { } city && world.Get(city.Country) is { } country)
            {
                if (Before(Capability.Scene(country.Code), e.Date)) o.Violations.Add($"{when}: {e.Name} staged in {country.Code} before its scene woke");
                if (world.Era.Bans.Any(b => b.Country == country.Code && (b.Area is null || b.Area == city.Area) && b.ActiveOn(e.Date)))
                    o.Violations.Add($"{when}: {e.Name} staged on {e.Date:yyyy-MM-dd} in a banned region ({city.Name})");
            }
            foreach (var f in e.Card)
                if (f.WeightMissBy is not null && f.OpenWeight) o.Violations.Add($"{when}: {e.Name} had a weight miss at open weight");
        }
    }

    /// <summary>The year's chapters as they fired, and the Backlash's bans and licences, and the new houses.</summary>
    private static void ChapterLines(World world, int year, StringBuilder sb)
    {
        foreach (var r in world.Era.Chapters.Where(r => r.FiredOn.Year == year))
        {
            var view = OriginsStory.View(world, r);
            string who = r.Promotion is { } p && world.Get(p) is { } promo ? $" by {promo.Abbreviation}" : "";
            string unlocks = view is null || view.Unlocked.Count == 0 ? "" : " -> " + string.Join(", ", view.Unlocked);
            sb.AppendLine($"  CHAPTER {r.FiredOn:yyyy-MM-dd} {r.Key} [{r.Variant}{who}] \"{view?.Headline}\"{unlocks}");
        }
        foreach (var b in world.Era.Bans.Where(b => b.Since.Year == year))
            sb.AppendLine($"  BAN     {b.Since:yyyy-MM-dd} {OriginsBacklash.RegionName(world, b.Country, b.Area)} until {b.Until:yyyy-MM}");
        foreach (var s in world.Era.Sanctions.Where(s => s.On.Year == year))
            sb.AppendLine($"  LICENCE {s.On:yyyy-MM-dd} {OriginsBacklash.RegionName(world, s.Country, s.Area)}{(s.Promotion is { } p && world.Get(p) is { } promo ? " for " + promo.Abbreviation : "")}");
        var founders = OriginsPromotions.All.Select(x => x.Abbreviation).ToHashSet();
        foreach (var p in world.Promotions.Values.Where(p => p.FoundedYear == year && !founders.Contains(p.Abbreviation)).OrderBy(p => p.Id.Value))
            sb.AppendLine($"  NEW     {p.Name} ({p.Abbreviation}) in {(p.HomeCity is { } hc ? world.Get(hc)?.Name : "?")}");
        foreach (var p in world.Promotions.Values.Where(p => p.Defunct).OrderBy(p => p.Id.Value))
            if (LastNight(world, p) is { } last && last.Year == year)
                sb.AppendLine($"  FOLDED  {p.Name} ({p.Abbreviation}), last night {last:yyyy-MM-dd}");
    }

    /// <summary>
    /// A simple player: keeps his house's next night on the books (the house's own format, through the
    /// sim APIs the UI will call) and keeps the roster deep enough by signing the best man his scouts can
    /// see at the man's asking terms.
    /// </summary>
    private sealed class Script
    {
        private readonly FighterValuation _valuation = new();

        public void Week(World w)
        {
            if (w.Get(w.PlayerPromotion) is not { Defunct: false } promo) return;
            var today = w.CurrentDate;
            bool booked = w.Events.Values.Any(e => e.Promotion == promo.Id && e.Status == EventStatus.Scheduled);
            var due = OriginsShows.NextNightDue(w, promo);
            var when = today.AddDays(21);
            if (!booked && when >= due)
            {
                if (promo.Rules.ShowFormat == ShowFormat.OneNightTournament)
                    OriginsShows.ScheduleTournament(w, promo.Id, when);
                else if (OriginsShows.SuggestCard(w, promo.Id, when) is { Count: >= 2 } card)
                    OriginsShows.ScheduleCard(w, promo.Id, when, card);
            }

            // Take whatever local sponsor knocks (no screens to sell yet).
            foreach (var offer in promo.Offers.Where(o => o.Media is null).ToList())
                MmaManager.Core.Sim.Finance.Sponsorships.Accept(w, promo, offer, today);

            // Keep the men whose deals are fought out (the player's re-sign window), as an owner does: at the
            // table at their terms when they fit the house's purse cap (the shoestring script's renewal pass).
            // Without it every deal lapsed and the house's stars walked: its crowds were a third smaller.
            ShoeSweep.ReSignInWindow(w, promo, Negotiator.PurseCap(w, promo), _valuation);

            // Monthly: keep the house deep enough for its night (men whose deals are fought out don't count).
            if (today.Day > 7) return;
            // ...and make the first reform the sport offers (rounds, gloves, a weight line): the player's own path to a chapter.
            if (OriginsReforms.Options(w, promo, today).FirstOrDefault() is { } reform)
                OriginsReforms.Adopt(w, promo, reform.Chapter, today);
            // ...and pay for the room most of his men train in to teach the whole game, when the bank can stand it.
            if (OriginsCamps.Options(w, promo, today).FirstOrDefault() is { } room && promo.Balance.Amount > room.Cost.Amount * 4)
                OriginsCamps.TryFund(w, promo, room.Camp, today, out _);
            // As the AI's signing pass holds its depth (FreeAgencyService): every man on the books counts (a man
            // recovering from his last night is still ours), each division to its share of the house's depth
            // (a split house fills its bands, not just a head count), and the house's own floor in all.
            int floor = OriginsShows.MinRoster(promo) + 2;
            int perDivision = OriginsShows.DivisionRosterTarget(promo, distressed: false);
            bool Held(Fighter f) => f.Status != FighterStatus.Retired && !DealWindow.OutOfDeal(w, f);
            var nobodyFits = new HashSet<WeightClass>();   // divisions with no free man the house can pay this month
            for (int guard = 0; guard < 60 && promo.Balance.Amount > 10_000m; guard++)
            {
                var men = w.RosterOf(promo.Id).Where(Held).ToList();
                var thin = promo.Divisions.Where(d => d.Sex == Sex.Male && !nobodyFits.Contains(d.WeightClass) && men.Count(d.Matches) < perDivision)
                    .OrderBy(d => men.Count(d.Matches)).ThenBy(d => d.WeightClass).FirstOrDefault();
                if (thin is null && men.Count >= floor) break;
                var cap = Negotiator.PurseCap(w, promo);
                var pick = w.FreeAgents()
                    .Where(f => f.Status == FighterStatus.Active && f.Sex == Sex.Male && (thin is null || thin.Matches(f)))
                    .Select(f => (F: f, Exp: _valuation.Evaluate(w, f)))
                    .Where(x => x.Exp.AskingPurse.Amount <= cap)
                    .OrderByDescending(x => ScoutingService.PerceivedAbility(w, promo, x.F))
                    .ThenBy(x => x.F.Id.Value)
                    .FirstOrDefault();
                if (pick.F is null)
                {
                    if (thin is null) break;
                    nobodyFits.Add(thin.WeightClass);
                    continue;
                }
                ContractLifecycle.Sign(w, pick.F, new ContractOffer
                {
                    Fighter = pick.F.Id, Promotion = promo.Id, PursePerFight = pick.Exp.AskingPurse,
                    WinBonus = pick.Exp.AskingWinBonus, Fights = pick.Exp.DesiredFights,
                    Exclusive = Negotiator.WantsExclusivity(promo),   // as the AI signs: a small house buys a slice
                }, today);
            }
        }
    }
}
