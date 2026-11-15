using System.Text;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Scouting;

namespace LongSim;

/// <summary>
/// <c>origins &lt;seeds&gt; &lt;years&gt;</c>: the 1993 world (Origins mode) run headless. For each seed it runs
/// two worlds: AI-run (nobody's house is the player's: all three are run by the AI) and a scripted
/// player running one of the three (seed % 3) through the sim APIs. Prints a per-year summary, the
/// invariant checks for systems that must not exist yet, and a HEALTH line for a young world:
/// <list type="bullet">
/// <item>no exception;</item>
/// <item>the sport does not die: all three houses alive and still running nights in the last year
/// (or: which folded, when, and on what balance);</item>
/// <item>rosters are replenished: every live house can still field its night (a bracket plus an
/// alternate; six men for a card) and new men turned pro every year;</item>
/// <item>money neither explodes nor collapses: every live house ends between a quarter of its starting
/// balance and ten times it plus a million;</item>
/// <item>determinism: the AI world run twice gives an identical summary.</item>
/// </list>
/// Exit code 1 when any seed fails its health line or breaks an invariant.
/// </summary>
public static class OriginsMode
{
    public static int Run(string[] args)
    {
        int seeds = args.Length > 1 ? int.Parse(args[1]) : 3;
        int years = args.Length > 2 ? int.Parse(args[2]) : 5;
        int failures = 0;
        var totals = new List<string>();
        for (int s = 1; s <= seeds; s++)
        {
            ulong seed = (ulong)s;
            var ai = RunOne(seed, years, player: null);
            var again = RunOne(seed, years, player: null);
            bool deterministic = ai.Summary == again.Summary;
            int playerIndex = (int)(seed % 3);
            var scripted = RunOne(seed, years, player: playerIndex);

            Console.WriteLine($"===== origins seed {seed}, {years} years: AI-run =====");
            Console.Write(ai.Summary);
            Console.WriteLine(ai.Health(deterministic));
            Console.WriteLine($"===== origins seed {seed}, {years} years: scripted player runs {scripted.PlayerAbbr} =====");
            Console.Write(scripted.Summary);
            Console.WriteLine(scripted.Health(deterministic: null));
            Console.WriteLine();
            if (!ai.Healthy(deterministic) || !scripted.Healthy(null)) failures++;
            totals.Add($"seed {seed}: AI {(ai.Healthy(deterministic) ? "HEALTHY" : "UNHEALTHY")}, scripted ({scripted.PlayerAbbr}) {(scripted.Healthy(null) ? "HEALTHY" : "UNHEALTHY")}");
        }
        Console.WriteLine("===== origins: all seeds =====");
        foreach (var t in totals) Console.WriteLine(t);
        Console.WriteLine(failures == 0 ? "ORIGINS HEALTH: OK" : $"ORIGINS HEALTH: {failures} seed(s) failed");
        return failures == 0 ? 0 : 1;
    }

    private sealed class Outcome
    {
        public string Summary = "";
        public string? PlayerAbbr;
        public string? Exception;
        public List<string> Violations = new();
        public List<string> Folds = new();
        public List<string> Problems = new();

        public bool Healthy(bool? deterministic) =>
            Exception is null && Violations.Count == 0 && Folds.Count == 0 && Problems.Count == 0 && deterministic != false;

        public string Health(bool? deterministic)
        {
            var parts = new List<string>
            {
                Exception is null ? "no crash" : $"CRASH: {Exception}",
                Folds.Count == 0 ? "the sport lives (all three houses running)" : "FOLDED: " + string.Join("; ", Folds),
                Violations.Count == 0 ? "no forbidden systems" : "FORBIDDEN: " + string.Join("; ", Violations.Distinct().Take(6)),
            };
            parts.AddRange(Problems);
            if (deterministic is { } d) parts.Add(d ? "deterministic (same seed twice, identical)" : "NOT DETERMINISTIC");
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
        try
        {
            for (int y = 0; y < years; y++)
            {
                int year = world.StartDate.Year + y;
                int newgens = 0, retired = 0, washouts = 0;
                do
                {
                    script?.Week(world);
                    var r = tick.AdvanceWeek(world);
                    newgens += r.Newgens; retired += r.Retired; washouts += r.Washouts;
                } while (world.CurrentDate.Year == year);
                Check(world, o);
                YearLines(world, year, sb, newgens, retired, washouts);
            }
        }
        catch (Exception ex)
        {
            o.Exception = $"{ex.GetType().Name} on {world.CurrentDate:yyyy-MM-dd}: {ex.Message} @ {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim()}";
        }

        // Health.
        int lastYear = world.StartDate.Year + years - 1;
        foreach (var p in world.Promotions.Values.OrderBy(p => p.Id.Value))
        {
            if (p.Defunct)
            {
                o.Folds.Add($"{p.Abbreviation} folded (last night {LastNight(world, p):yyyy-MM-dd}, balance {p.Balance.Amount:N0})");
                continue;
            }
            int nightsLastYear = world.Events.Values.Count(e => e.Promotion == p.Id && e.Status == EventStatus.Completed && e.Date.Year == lastYear);
            if (nightsLastYear == 0) o.Problems.Add($"{p.Abbreviation} ran no night in {lastYear}");
            int need = p.Rules.ShowFormat == ShowFormat.OneNightTournament ? Math.Max(2, p.Rules.TournamentSize) + 1 : 6;
            int roster = world.RosterOf(p.Id).Count(f => f.Status == FighterStatus.Active);
            if (roster < need) o.Problems.Add($"{p.Abbreviation} roster {roster} < {need}");
            decimal s0 = start.GetValueOrDefault(p.Id);
            if (p.Balance.Amount < s0 * 0.25m) o.Problems.Add($"{p.Abbreviation} money collapsing ({s0:N0} -> {p.Balance.Amount:N0})");
            if (p.Balance.Amount > s0 * 10m + 1_000_000m) o.Problems.Add($"{p.Abbreviation} money exploding ({s0:N0} -> {p.Balance.Amount:N0})");
        }
        if (world.Fighters.Count <= fightersAtStart) o.Problems.Add("no new men turned pro");
        o.Summary = sb.ToString();
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
                          $"  roster {roster}");
            var money = p.Ledger.Where(l => l.Date.Year == year)
                .GroupBy(l => l.Category).Select(g => (Cat: g.Key, Sum: g.Sum(l => l.Amount.Amount)))
                .Where(x => x.Sum != 0).OrderByDescending(x => Math.Abs(x.Sum)).ThenBy(x => x.Cat);
            sb.AppendLine("       money: " + string.Join(", ", money.Select(x => $"{x.Cat} {x.Sum / 1000m:+#,0;-#,0}k")));
        }
    }

    /// <summary>What must not exist yet in a phase-1 world, checked every year-end (and on day one).</summary>
    private static void Check(World world, Outcome o)
    {
        string when = world.CurrentDate.ToString("yyyy-MM-dd");
        if (world.Mode != WorldMode.Origins) o.Violations.Add($"{when}: world mode {world.Mode}");
        if (world.Promotions.Count != 3) o.Violations.Add($"{when}: {world.Promotions.Count} promotions (a startup or an acquisition)");
        foreach (var p in world.Promotions.Values)
        {
            foreach (var d in p.Divisions)
            {
                if (!d.Open) o.Violations.Add($"{when}: {p.Abbreviation} runs a {d.WeightClass} division");
                if (d.Sex != Sex.Male) o.Violations.Add($"{when}: {p.Abbreviation} runs a women's division");
            }
            foreach (var m in p.MediaDeals)
                if (!Era.AllowsMedia(world, m.Kind)) o.Violations.Add($"{when}: {p.Abbreviation} has a {m.Kind} deal");
            if (p.Stream is not null) o.Violations.Add($"{when}: {p.Abbreviation} launched a stream");
        }
        foreach (var id in world.Era.Unlocked)
            if (!OriginsWorldBuilder.DayOneUnlocks.Any(c => c.Id == id)) o.Violations.Add($"{when}: {id} unlocked (no timeline yet)");
        if (world.Fighters.Values.Any(f => f.Sex == Sex.Female)) o.Violations.Add($"{when}: a woman in the talent pool");
        if (world.Fighters.Values.Any(f => f.LocalTalent)) o.Violations.Add($"{when}: local filler exists (no club scene in 1993)");
        foreach (var e in world.Events.Values.Where(e => e.Status == EventStatus.Completed))
        {
            if (e.Promotion.Value == 0) { o.Violations.Add($"{when}: a club show ran ({e.Name})"); continue; }
            if (world.Get(e.VenueId ?? default) is { } v && world.Get(v.City) is { } city && world.Get(city.Country) is { } country
                && !Era.AllowsScene(world, country.Code))
                o.Violations.Add($"{when}: {e.Name} staged in {country.Code}, which has no scene");
            foreach (var f in e.Card)
            {
                if (!f.OpenWeight) o.Violations.Add($"{when}: {e.Name} had a bout that was not open weight");
                if (f.WeightMissBy is not null) o.Violations.Add($"{when}: {e.Name} had a weight miss");
            }
        }
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
                }, today);
            }
        }
    }
}
