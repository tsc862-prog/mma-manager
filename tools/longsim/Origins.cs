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
/// <item>rosters are replenished: every live house ends holding <c>OriginsShows.MinRoster</c>;</item>
/// <item>the chapter order holds: every fired chapter fired inside its window (by its deadline, a week's
/// grace) and strictly after every chapter it requires;</item>
/// <item>nothing is used before the sport has it: no media deal, staged night, weight division, title or
/// woman before its chapter unlocked it (the first use IS the chapter, so the same week counts), and no
/// night in a region a ban had closed;</item>
/// <item>determinism: the same seed twice gives an identical world digest (<c>Check.WorldGraph.Digest</c>,
/// every saved field), for the AI world and the scripted one.</item>
/// </list>
/// Money is reported (each founding house's bank against its opening one, as notes) but is no longer a
/// failure once the Backlash can bite. Exit code 1 when any seed fails its health line or breaks an invariant.
/// </summary>
public static class OriginsMode
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
        for (int s = 1; s <= seeds; s++)
        {
            ulong seed = (ulong)s;
            int playerIndex = (int)(seed % 3);
            var ai = RunOne(seed, years, player: null);
            bool aiSame = ai.Digest == RunOne(seed, years, player: null).Digest;
            var scripted = RunOne(seed, years, player: playerIndex);
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

    private static Outcome RunOne(ulong seed, int years, int? player)
    {
        var o = new Outcome();
        var sb = new StringBuilder();
        World world;
        try
        {
            var options = WorldBuilder.Options.Origins(seed, player ?? 0);
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
        var script = player is null ? null : new Script();
        sb.AppendLine($"day one {world.StartDate:yyyy-MM-dd}: {world.Promotions.Count} promotions, {world.Fighters.Count} men " +
                      $"({world.Fighters.Values.Count(f => f.Designed)} pioneers), {world.Fighters.Values.Count(f => f.Contract is null)} unsigned, " +
                      $"{world.Camps.Count} gyms; unlocked: {string.Join(", ", world.Era.Unlocked)}");
        Check(world, o);

        int fightersAtStart = world.Fighters.Count;
        int peakTarget = 0;
        try
        {
            for (int y = 0; y < years; y++)
            {
                int year = world.StartDate.Year + y;
                int newgens = 0, retired = 0, washouts = 0;
                var aliveAtStart = world.Promotions.Values.Where(p => !p.Defunct).Select(p => p.Id).ToHashSet();
                do
                {
                    script?.Week(world);
                    var r = tick.AdvanceWeek(world);
                    newgens += r.Newgens; retired += r.Retired; washouts += r.Washouts;
                } while (world.CurrentDate.Year == year);
                Check(world, o);
                YearLines(world, year, sb, newgens, retired, washouts);
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
            // (A house in the red keeps a thin, cheap roster on its way out: that is a fold coming, not a starved sport.)
            if (roster < need && p.Balance.Amount >= 0) o.Problems.Add($"{p.Abbreviation} roster {roster} < {need}");
            if (s0 == 0) continue;   // a house founded during the run
            // (Money was a failure line in phase 1's five quiet years; the Backlash is meant to bite.)
            if (p.Balance.Amount < s0 * MoneyFloor) o.Notes.Add($"{p.Abbreviation} money down ({s0:N0} -> {p.Balance.Amount:N0})");
            if (p.Balance.Amount > s0 * MoneyCeiling) o.Notes.Add($"{p.Abbreviation} money up ({s0:N0} -> {p.Balance.Amount:N0})");
        }
        o.Chapters = world.Era.Chapters.Count;
        if (world.Fighters.Count <= fightersAtStart) o.Problems.Add("no new men turned pro");
        o.Banks = string.Join(", ", banks);
        o.Summary = sb.ToString();
        o.Digest = LongSim.Check.WorldGraph.Digest(world, canonical: false);
        return o;
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
        }
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

        if (world.Promotions.Values.Count(p => !p.Defunct) > 3 && !world.Era.HasFired("rivals-appear"))
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
                if (f.WeightMissBy is not null) o.Violations.Add($"{when}: {e.Name} had a weight miss");
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

            // Monthly: keep the house deep enough for its night (men whose deals are fought out don't count).
            if (today.Day > 7) return;
            // ...and make the first reform the sport offers (rounds, gloves, a weight line): the player's own path to a chapter.
            if (OriginsReforms.Options(w, promo, today).FirstOrDefault() is { } reform)
                OriginsReforms.Adopt(w, promo, reform.Chapter, today);
            int need = OriginsShows.MinRoster(promo) + 2;
            int roster = w.RosterOf(promo.Id).Count(f => f.Status == FighterStatus.Active && !DealWindow.OutOfDeal(w, f));
            for (int i = roster; i < need && promo.Balance.Amount > 10_000m; i++)
            {
                var cap = Negotiator.PurseCap(w, promo);
                var pick = w.FreeAgents()
                    .Where(f => f.Status == FighterStatus.Active && f.Sex == Sex.Male)
                    .Select(f => (F: f, Exp: _valuation.Evaluate(w, f)))
                    .Where(x => x.Exp.AskingPurse.Amount <= cap)
                    .OrderByDescending(x => ScoutingService.PerceivedAbility(w, promo, x.F))
                    .ThenBy(x => x.F.Id.Value)
                    .FirstOrDefault();
                if (pick.F is null) break;
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
