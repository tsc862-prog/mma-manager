using System.Text;
using LongSim.Check;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Generation.Origins;
using MmaManager.Core.Sim.Living;

namespace LongSim;

/// <summary>
/// <c>origins-observer &lt;seeds&gt; &lt;years&gt;</c>: the watch-then-found path of Origins mode, one world per seed (run twice, for
/// determinism). The player begins as an OBSERVER (no house), watches for a seed-derived stretch (hash of the seed picks an
/// early, mid or late era and a day inside it), then founds a house through <see cref="PlayerFounding"/> with a valid choice
/// (city, name, rules picked by hash from <c>Choices</c>/<c>RulesIn</c>), holds the one free tryout, and runs the house to the
/// end with the same scripted player the other Origins arms use.
/// <list type="bullet">
/// <item>While observing: no mail, desk decision, rival offer, talks memory or shortlist exists (there is no one to address),
/// no promotion is player controlled, nothing in the world names the default PromotionId as an owner, no crash, and the
/// Origins health checks hold.</item>
/// <item>Probes on the founding day (counted by refusal reason): a bad name, a taken abbreviation, a reserved name, a closed city,
/// no city, a tryout with no house; each must refuse WITH a reason and leave the world untouched.</item>
/// <item>At founding: <c>Found</c> succeeds on a choice <c>Choices</c> called open (a refusal of one is a finding), the bank equals
/// <see cref="PlayerFounding.Bank"/>, the roster is empty, the accountant's letter opens the inbox and no earlier mail exists, a
/// second <c>Found</c> refuses. The tryout can be held exactly once (the second call refuses and changes nothing), signs at most
/// <see cref="PlayerFounding.TryoutTurnout"/> men, all on the player's books, and costs nothing. Until the tryout nothing signs a
/// man into the house.</item>
/// <item>The monthly <see cref="WorldChecker"/> invariants (rankings, titles, booking, contracts, ledgers, records, ids ...) hold for
/// the whole run, before and after founding; the house runs nights; same seed twice gives the same world digest.</item>
/// </list>
/// Exit code 1 when any seed fails. The plain <c>origins</c> output is unchanged.
/// </summary>
public static partial class OriginsMode
{
    private static readonly string[] HouseNames =
    {
        "Lantern Street Open", "Harbor Light Fights", "Ironwood Combat League", "Black Cedar Fighting", "Northgate Fight Club",
        "Red Lantern Pit", "Saltmarsh Cagefighting", "Copperline Combat", "Tidewater Open Fights", "Gold Coast Brawl",
    };

    /// <summary>The same finalizer the project's seed mixing uses in spirit; longsim-side only, draws on nothing.</summary>
    private static ulong H(ulong seed, ulong salt)
    {
        ulong z = seed * 0x9E3779B97F4A7C15UL + salt * 0xBF58476D1CE4E5B9UL + 0x94D049BB133111EBUL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public static int RunObserver(string[] args)
    {
        int seeds = args.Length > 1 ? int.Parse(args[1]) : 3;
        int years = args.Length > 2 ? int.Parse(args[2]) : 6;
        int failures = 0;
        ObserverPlan.Strict = args.Contains("--strict");
        // Diagnosis arm: he founds and holds his tryout as usual, then hands the house to the AI (it stops being the
        // player's), so the AI's own rules run a house opened on the player's terms. Separates "the terms" from "the script".
        ObserverPlan.AiRunsIt = args.Contains("--ai-runs-it");
        if (ObserverPlan.AiRunsIt) Console.WriteLine("(--ai-runs-it: after his tryout the house is handed to the AI)");
        var plans = new List<ObserverPlan>();
        var outcomes = new List<Outcome>();
        for (int s = 1; s <= seeds; s++)
        {
            ulong seed = (ulong)s;
            var plan = new ObserverPlan(seed, years, check: true);
            var run = RunOne(seed, years, null, plan);
            var again = RunOne(seed, years, null, new ObserverPlan(seed, years, check: false));
            bool same = run.Digest == again.Digest;
            plans.Add(plan); outcomes.Add(run);
            Console.WriteLine($"===== origins-observer seed {seed}, {years} years: observes until {plan.FoundOn:yyyy-MM-dd} ({plan.Era}), then founds =====");
            Console.Write(run.Summary);
            Console.WriteLine(run.Health(same));
            Console.WriteLine();
            if (!run.Healthy(same)) failures++;
        }
        Console.WriteLine($"===== ORIGINS OBSERVER ({seeds} seeds, {years} years) =====");
        var founded = plans.Where(p => p.Promo is not null).ToList();
        Console.WriteLine($"founded {founded.Count}/{plans.Count}; by era " + string.Join(", ", new[] { "early", "mid", "late" }
            .Select(e => $"{e} {founded.Count(p => p.Era == e)}/{plans.Count(p => p.Era == e)}")));
        Console.WriteLine("founding dates: " + string.Join(" ", founded.OrderBy(p => p.FoundedOn).Select(p => $"{p.Seed}:{p.FoundedOn:yyyy-MM}")));
        Console.WriteLine($"retired belts kept as history (no division carries them): {plans.Sum(p => p.RetiredBelts)} in {plans.Count(p => p.RetiredBelts > 0)}/{plans.Count} seeds; " +
                          $"TITLE-ORPHAN (a retired belt not cleanly retired, fatal): {plans.Sum(p => p.Orphans)} in {plans.Count(p => p.Orphans > 0)}/{plans.Count} seeds");
        var refusals = plans.SelectMany(p => p.Refusals).GroupBy(kv => kv.Key).Select(g => (Reason: g.Key, N: g.Sum(kv => kv.Value)))
            .OrderByDescending(x => x.N).ThenBy(x => x.Reason, StringComparer.Ordinal).ToList();
        Console.WriteLine($"refusals ({refusals.Sum(r => r.N)}):");
        foreach (var r in refusals) Console.WriteLine($"  {r.N,4}  {r.Reason}");
        var tr = founded.Where(p => p.Tryout is not null).Select(p => p.Tryout!.Count).ToList();
        if (tr.Count > 0)
            Console.WriteLine($"tryouts held {tr.Count}: men signed min {tr.Min()} mean {tr.Average():0.0} max {tr.Max()}; empty {tr.Count(n => n == 0)}");
        Console.WriteLine($"recurring tryouts held {founded.Sum(p => p.RecurringTryouts)} in {founded.Count(p => p.RecurringTryouts > 0)}/{founded.Count} houses; " +
                          $"divisions at founding mean {(founded.Count == 0 ? 0 : founded.Average(p => p.FoundingDivisions)):0.0}");
        var alive = founded.Where(p => p.Promo is { Defunct: false }).ToList();
        Console.WriteLine($"player-founded houses alive at the end {alive.Count}/{founded.Count}; folded " +
            string.Join(" ", founded.Where(p => p.Promo!.Defunct).Select(p => $"{p.Seed}:{p.Promo!.Abbreviation}")));
        foreach (var era in new[] { "early", "mid", "late" })
        {
            var g = founded.Where(p => p.Era == era).ToList();
            if (g.Count == 0) continue;
            var banks = g.Select(p => p.Promo!.Balance.Amount).OrderBy(x => x).ToList();
            Console.WriteLine($"  {era}: n {g.Count}, alive {g.Count(p => !p.Promo!.Defunct)}, end bank min {banks[0] / 1000m:0}k median {banks[banks.Count / 2] / 1000m:0}k max {banks[^1] / 1000m:0}k, " +
                              $"nights run median {g.Select(p => p.Nights).OrderBy(x => x).ElementAt(g.Count / 2)}");
        }
        Console.WriteLine(failures == 0 ? "ORIGINS OBSERVER: OK" : $"ORIGINS OBSERVER: {failures} seed(s) failed");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>One seed's observer script: when and how he founds, and every check around it. Longsim-side; draws on no sim stream.</summary>
    internal sealed class ObserverPlan
    {
        public readonly ulong Seed;
        public readonly int Years;
        public readonly string Era;
        public readonly DateOnly FoundOn;
        public DateOnly? FoundedOn;
        public Promotion? Promo;
        public List<string>? Tryout;
        public int Nights;
        public int Orphans, RetiredBelts;
        public int RecurringTryouts, FoundingDivisions;
        public static bool Strict, AiRunsIt;
        public bool ScriptOn;
        public readonly Dictionary<string, int> Refusals = new();
        private readonly List<string> _lines = new();
        private readonly ViolationLog? _log;
        private readonly WorldChecker? _checker;
        private int _lastMonth = -1, _nameIx, _attempts;
        private DateOnly? _tryoutOn;
        private bool _probed;
        private int _tryoutDelayWeeks;
        private readonly HashSet<string> _reported = new();

        public ObserverPlan(ulong seed, int years, bool check)
        {
            Seed = seed; Years = years;
            ulong h = H(seed, 1);
            int bucket = (int)(h % 3);
            Era = new[] { "early", "mid", "late" }[bucket];
            // Days into the run: early in the first quarter of it, mid in 25%-60%, late in 60% up to a year before the end.
            double total = years * 365.25;
            double lo = bucket switch { 0 => 0.0, 1 => 0.25 * total, _ => 0.60 * total };
            double hi = bucket switch { 0 => 0.25 * total, 1 => 0.60 * total, _ => Math.Max(0.60 * total + 1, total - 365.25) };
            int day = (int)(lo + (H(seed, 2) % (ulong)Math.Max(1, (long)(hi - lo))));
            FoundOn = OriginsWorldBuilder.StartDate.AddDays(day);
            _tryoutDelayWeeks = (int)(H(seed, 3) % 3);   // 0, 1 or 2 weeks between founding and the tryout
            if (check)
            {
                _log = new ViolationLog();
                _checker = new WorldChecker(_log, $"obs#{seed}");
            }
        }

        private void Bad(List<string> problems, string key, string text)
        {
            if (_reported.Add(key)) problems.Add(text);
        }

        private void Count(string reason) => Refusals[reason] = Refusals.GetValueOrDefault(reason) + 1;

        public void Before(World w, List<string> problems)
        {
            if (Promo is null)
            {
                if (w.CurrentDate >= FoundOn) TryFound(w, problems);
            }
            else if (Tryout is null && w.CurrentDate >= _tryoutOn)
            {
                HoldTryout(w, problems);
            }
            if (Promo is not null && Tryout is not null)
            {
                if (AiRunsIt && w.PlayerPromotion == Promo.Id)
                {
                    // (Diagnosis only: the house becomes the AI's; nobody is the player from here on.)
                    Promo.IsPlayerControlled = false;
                    w.PlayerPromotion = default;
                }
                ScriptOn = !AiRunsIt;
            }
        }

        public void After(World w, List<string> problems)
        {
            string when = w.CurrentDate.ToString("yyyy-MM-dd");
            if (Promo is null) Observing(w, problems, when);
            else if (Tryout is null && w.RosterOf(Promo.Id).Any())
                Bad(problems, "preTryoutRoster", $"{when}: the house has men on its books before the tryout and before any signing of his (nothing but he may sign them): " +
                    string.Join(", ", w.RosterOf(Promo.Id).Take(3).Select(f => $"{f.FullName} #{f.Id.Value}")));
            if (_checker is not null && w.CurrentDate.Month != _lastMonth)
            {
                _lastMonth = w.CurrentDate.Month;
                _checker.Check(w);
            }
        }

        /// <summary>Nobody is the player: nothing is addressed to him and nothing names an owner that does not exist.</summary>
        private void Observing(World w, List<string> problems, string when)
        {
            if (w.HasPlayer) Bad(problems, "hasPlayerEarly", $"{when}: HasPlayer is true with no house founded");
            if (w.Mail.Count > 0) Bad(problems, "mail", $"{when}: {w.Mail.Count} mail message(s) in an inbox with no owner (first: \"{w.Mail[0].Subject}\" from {w.Mail[0].From})");
            if (w.Decisions.Count > 0) Bad(problems, "decisions", $"{when}: {w.Decisions.Count} desk decision(s) for a player who does not exist (first: {w.Decisions[0].Kind}, promotion {w.Decisions[0].Promotion?.Value})");
            if (w.RivalOffers.Count > 0) Bad(problems, "rivalOffers", $"{when}: {w.RivalOffers.Count} rival offer(s) for a player's fighters");
            if (w.Talks.Count > 0) Bad(problems, "talks", $"{when}: {w.Talks.Count} contract-talks memories for a player who does not exist");
            if (w.Shortlist.Count > 0) Bad(problems, "shortlist", $"{when}: {w.Shortlist.Count} fighters on a shortlist nobody keeps");
            if (w.Promotions.Values.Any(p => p.IsPlayerControlled))
                Bad(problems, "controlled", $"{when}: a promotion is player controlled with no player: {w.Promotions.Values.First(p => p.IsPlayerControlled).Abbreviation}");
            foreach (var f in w.Fighters.Values)
            {
                if (f.Contract is { } c && c.Promotion.Value == 0) Bad(problems, "contract0", $"{when}: {f.FullName} #{f.Id.Value} is contracted to the default PromotionId");
                if (f.DiscoveredBy is { Value: 0 }) Bad(problems, "disc0", $"{when}: {f.FullName} #{f.Id.Value} was discovered by the default PromotionId");
            }
            foreach (var e in w.Events.Values)
                if (e.Promotion.Value == 0) Bad(problems, "event0", $"{when}: event {e.Name} #{e.Id.Value} is owned by the default PromotionId");
            foreach (var d in w.Decisions.Where(d => d.Promotion is { Value: 0 }))
                Bad(problems, "decision0", $"{when}: a desk decision names the default PromotionId");
            foreach (var x in w.RivalOffers.Where(x => x.From.Value == 0))
                Bad(problems, "offer0", $"{when}: a rival offer is from the default PromotionId");
            foreach (var x in w.Promises.Where(x => x.Promotion.Value == 0))
                Bad(problems, "promise0", $"{when}: a promise is written by the default PromotionId");
            foreach (var r in w.Era.Chapters.Where(r => r.Promotion is { Value: 0 }))
                Bad(problems, "chapter0", $"{when}: chapter {r.Key} is credited to the default PromotionId");
        }

        private int Fresh(World w) => w.Promotions.Count;

        private void TryFound(World w, List<string> problems)
        {
            var date = w.CurrentDate;
            string when = date.ToString("yyyy-MM-dd");
            var choices = PlayerFounding.Choices(w, date);
            if (!_probed)
            {
                _probed = true;
                Probe(w, problems, choices, date);
            }
            if (choices.Bank != PlayerFounding.Bank) Bad(problems, "choicesBank", $"{when}: Choices.Bank {choices.Bank} is not PlayerFounding.Bank {PlayerFounding.Bank}");
            var open = choices.Cities.Where(c => c.Open).ToList();
            if (open.Count == 0)
            {
                Count("no city is open to found in: " + (choices.Cities.FirstOrDefault()?.Refusal is { } r0 ? Trim(r0) : "no cities"));
                return;
            }
            var city = open[(int)((H(Seed, 10) + (ulong)_attempts * 7) % (ulong)open.Count)];
            string name = HouseNames[(int)((H(Seed, 11) + (ulong)_nameIx) % (ulong)HouseNames.Length)];
            var rules = PlayerFounding.RulesIn(w, city.Id);
            int? minutes = rules.SingleRoundMinutes.Count > 0 ? rules.SingleRoundMinutes[(int)(H(Seed, 12) % (ulong)rules.SingleRoundMinutes.Count)] : null;
            GloveType? glove = rules.UnifiedCode || rules.Gloves.Count == 0 ? null : rules.Gloves[(int)(H(Seed, 13) % (ulong)rules.Gloves.Count)];
            Enclosure enc = rules.Enclosures[(int)(H(Seed, 14) % (ulong)rules.Enclosures.Count)];
            string abbr = PlayerFounding.SuggestAbbreviation(w, name);
            if (abbr.Length == 0) { Bad(problems, "noAbbr", $"{when}: SuggestAbbreviation gave nothing for \"{name}\""); return; }
            _attempts++;
            int before = Fresh(w);
            var promo = PlayerFounding.Found(w, date, new PlayerFoundingSpec(name, abbr, city.Id, minutes, glove, enc), out var refused);
            if (promo is null)
            {
                Count(Trim(refused ?? "(refused with no reason)"));
                if (string.IsNullOrWhiteSpace(refused)) Bad(problems, "noReason", $"{when}: Found refused with no reason");
                if (refused is not null && refused.Contains("already a promotion called")) { _nameIx++; return; }
                Bad(problems, "validRefused:" + Trim(refused ?? ""), $"{when}: Found refused a choice Choices called open ({city.Name}, \"{name}\", {abbr}, {minutes}min {glove} {enc}): {refused}");
                _nameIx++;
                return;
            }
            Promo = promo;
            FoundedOn = date;
            FoundingDivisions = promo.Divisions.Count;
            _tryoutOn = date.AddDays(7 * _tryoutDelayWeeks);
            Count("(founded)");
            _lines.Add($"  OBSERVER founded {promo.Name} ({promo.Abbreviation}) on {when} in {city.Name}, {city.Country} ({Era}); divisions {promo.Divisions.Count} ({string.Join(" ", promo.Divisions.Select(d => d.Open ? "Open" : d.WeightClass.ToString()))}), rules {promo.Rules.ShowFormat}, rounds {promo.Rules.StandardRounds}x{promo.Rules.RoundMinutes}, {promo.Rules.Fight?.Gloves} {promo.Rules.Fight?.Enclosure}, bank {promo.Balance}");
            if (w.Promotions.Count != before + 1) Bad(problems, "count", $"{when}: founding changed the promotion count by {w.Promotions.Count - before}");
            if (!w.HasPlayer || w.PlayerPromotion != promo.Id) Bad(problems, "notPlayer", $"{when}: after Found the world's player is not the new house");
            if (promo.Balance.Amount != PlayerFounding.Bank.Amount)
                Bad(problems, "bank", $"{when}: bank after Found is {promo.Balance} but PlayerFounding.Bank says {PlayerFounding.Bank}");
            if (w.RosterOf(promo.Id).Any()) Bad(problems, "rosterAtFound", $"{when}: the new house has a roster at founding ({w.RosterOf(promo.Id).Count()} men)");
            if (PlayerFounding.TryoutHeldOn(promo) is not null) Bad(problems, "tryoutAtFound", $"{when}: the tryout reads as held at founding");
            if (!PlayerFounding.TryoutOptions(w, date).Available) Bad(problems, "tryoutOffer", $"{when}: tryout unavailable at founding: {PlayerFounding.TryoutOptions(w, date).Refusal}");
            if (w.Mail.Count == 0 || w.Mail[0].From != "The Accountant") Bad(problems, "letter", $"{when}: the inbox does not open with the accountant's letter (count {w.Mail.Count})");
            if (w.Mail.Any(m => m.Date < date)) Bad(problems, "oldMail", $"{when}: mail dated before the founding exists");
            // A second founding must refuse: he runs a house now.
            var second = PlayerFounding.Found(w, date, new PlayerFoundingSpec("Second House Open", "SHO9", city.Id), out var why2);
            if (second is not null || string.IsNullOrWhiteSpace(why2)) Bad(problems, "second", $"{when}: a second Found while he runs a house returned {(second is null ? "null without a reason" : "a promotion")}");
            else Count(Trim(why2));
            if (w.Promotions.Count != before + 1) Bad(problems, "secondCount", $"{when}: the refused second founding changed the promotion count");
        }

        /// <summary>Refusals that must come with a reason and change nothing.</summary>
        private void Probe(World w, List<string> problems, FoundingChoices choices, DateOnly date)
        {
            string when = date.ToString("yyyy-MM-dd");
            var open = choices.Cities.FirstOrDefault(c => c.Open);
            var closed = choices.Cities.FirstOrDefault(c => !c.Open);
            var existing = w.Promotions.Values.OrderBy(p => p.Id.Value).First();
            var probes = new List<(string Label, PlayerFoundingSpec Spec)>();
            if (open is not null)
            {
                probes.Add(("short name", new PlayerFoundingSpec("Hi", "HIX", open.Id)));
                probes.Add(("taken abbreviation", new PlayerFoundingSpec("Probe Fighting One", existing.Abbreviation, open.Id)));
                probes.Add(("taken name", new PlayerFoundingSpec(existing.Name, "PRB", open.Id)));
                probes.Add(("reserved founding name", new PlayerFoundingSpec(OriginsPromotions.All[^1].Name, "PRC", open.Id)));
                probes.Add(("bad abbreviation", new PlayerFoundingSpec("Probe Fighting Two", "A", open.Id)));
                probes.Add(("no city", new PlayerFoundingSpec("Probe Fighting Three", "PRD", default)));
                probes.Add(("illegal glove", new PlayerFoundingSpec("Probe Fighting Four", "PRE", open.Id, Gloves: GloveType.Modern)));
            }
            if (closed is not null) probes.Add(("closed city", new PlayerFoundingSpec("Probe Fighting Five", "PRF", closed.Id)));
            foreach (var (label, spec) in probes)
            {
                int promos = w.Promotions.Count; int mail = w.Mail.Count;
                var got = PlayerFounding.Found(w, date, spec, out var why);
                if (got is not null)
                {
                    Bad(problems, "probeFounded:" + label, $"{when}: probe '{label}' FOUNDED a house ({got.Name}); expected a refusal");
                    // (a house exists now: the run would be the probe's, not the plan's. Stop the seed's founding here.)
                    Promo = got; FoundedOn = date; _tryoutOn = date; return;
                }
                if (string.IsNullOrWhiteSpace(why)) Bad(problems, "probeNoReason:" + label, $"{when}: probe '{label}' refused with no reason");
                Count($"{label}: {Trim(why ?? "(no reason)")}");
                if (w.Promotions.Count != promos || w.HasPlayer || w.Mail.Count != mail)
                    Bad(problems, "probeState:" + label, $"{when}: probe '{label}' refused but changed the world");
            }
            var t = PlayerFounding.HoldTryout(w, date, out var tWhy);
            if (t.Count > 0 || string.IsNullOrWhiteSpace(tWhy)) Bad(problems, "tryoutNoHouse", $"{when}: a tryout with no house returned {t.Count} men / reason \"{tWhy}\"");
            else Count("tryout with no house: " + Trim(tWhy));
            if (PlayerFounding.TryoutOptions(w, date).Available) Bad(problems, "tryoutOfferNoHouse", $"{when}: TryoutOptions is available with no house");
            if (w.Mail.Count > 0 || w.HasPlayer) Bad(problems, "tryoutNoHouseState", $"{when}: a refused tryout changed the world");
        }

        private void HoldTryout(World w, List<string> problems)
        {
            var promo = Promo!;
            var date = w.CurrentDate;
            string when = date.ToString("yyyy-MM-dd");
            if (promo.Defunct) { Tryout = new(); return; }   // (folded in the wait: cannot happen with the bank he opens with; the end checks say)
            decimal bank = promo.Balance.Amount;
            int ledger = promo.Ledger.Count;
            if (w.RosterOf(promo.Id).Any()) Bad(problems, "rosterBeforeTryout", $"{when}: roster not empty on the tryout day, before it");
            var signed = PlayerFounding.HoldTryout(w, date, out var refused);
            if (refused is not null) { Bad(problems, "tryoutRefused", $"{when}: the first tryout refused: {refused}"); Tryout = new(); return; }
            Tryout = signed.Select(s => s.Name).ToList();
            if (signed.Count > PlayerFounding.TryoutTurnout) Bad(problems, "tryoutTooMany", $"{when}: tryout signed {signed.Count} > {PlayerFounding.TryoutTurnout}");
            var roster = w.RosterOf(promo.Id).ToList();
            if (roster.Count != signed.Count || signed.Any(s => w.Get(s.Fighter) is not { Contract: { } c } || c.Promotion != promo.Id))
                Bad(problems, "tryoutRoster", $"{when}: the tryout signed {signed.Count} but the roster holds {roster.Count}, or a man is not on his books");
            if (promo.Balance.Amount != bank) Bad(problems, "tryoutCost", $"{when}: the free tryout moved the bank {bank} -> {promo.Balance.Amount}");
            if (PlayerFounding.TryoutHeldOn(promo) != date) Bad(problems, "tryoutMark", $"{when}: TryoutHeldOn is {PlayerFounding.TryoutHeldOn(promo)} after the tryout");
            if (PlayerFounding.TryoutOptions(w, date).Available) Bad(problems, "tryoutAgainOffer", $"{when}: the tryout is still on offer after it was held");
            // Once only.
            var again = PlayerFounding.HoldTryout(w, date, out var why2);
            if (again.Count > 0 || string.IsNullOrWhiteSpace(why2)) Bad(problems, "tryoutTwice", $"{when}: a second tryout returned {again.Count} men, reason \"{why2}\"");
            else Count("second tryout: " + Trim(why2));
            if (w.RosterOf(promo.Id).Count() != roster.Count || promo.Ledger.Count(l => l.Description == PlayerFounding.TryoutLedgerLine) != 1)
                Bad(problems, "tryoutTwiceState", $"{when}: the refused second tryout changed the roster or the ledger");
            if (promo.Ledger.Count < ledger) Bad(problems, "ledgerShrank", $"{when}: ledger shrank across the tryout");
            _lines.Add($"  OBSERVER tryout {when}: {signed.Count} men signed ({string.Join(", ", signed.Select(s => $"{s.Name} {s.Age} {s.WeightClass}"))})");
        }

        public void Finish(World w, List<string> problems, StringBuilder sb)
        {
            foreach (var l in _lines) sb.AppendLine(l);
            if (Promo is null)
            {
                Bad(problems, "neverFounded", $"never founded by {w.CurrentDate:yyyy-MM-dd} (planned {FoundOn:yyyy-MM-dd})");
            }
            else
            {
                Nights = w.Events.Values.Count(e => e.Promotion == Promo.Id && e.Status == EventStatus.Completed);
                var ours = w.Get(Promo.Id)!;
                RecurringTryouts = ours.Ledger.Count(l => l.Category == FinanceCategory.Other && l.Description == OriginsFounding.TryoutLedgerLine);
                double aliveYears = ((ours.Defunct ? (ours.Ledger.Count > 0 ? ours.Ledger[^1].Date : w.CurrentDate) : w.CurrentDate).DayNumber - FoundedOn!.Value.DayNumber) / 365.25;
                sb.AppendLine($"  OBSERVER house {ours.Abbreviation}: {(ours.Defunct ? "FOLDED" : "alive")}, {Nights} nights, bank {ours.Balance.Amount:N0}, roster {w.RosterOf(ours.Id).Count()}, tryout {Tryout?.Count ?? -1} men, recurring tryouts {RecurringTryouts}, divisions {FoundingDivisions} at founding / {ours.Divisions.Count} at the end, alive {aliveYears:0.0}y");
                if (!ours.Defunct && aliveYears >= 1.5 && Nights == 0)
                    Bad(problems, "noNights", $"the house {ours.Abbreviation} founded {FoundedOn:yyyy-MM-dd} ran no night in {aliveYears:0.0} years alive");
                if (Tryout is null && !ours.Defunct) Bad(problems, "noTryout", "the tryout was never held");
            }
            RetiredBelts = w.Titles.Values.Count(t => Belts.IsRetired(w, t));
            if (_log is not null)
            {
                Orphans = _log.Count("TITLE-ORPHAN");
                int n = _log.TotalDistinct;
                if (n > 0) problems.Add($"INVARIANTS: {n} distinct violation(s) from the monthly world checker");
                if (n > 0) sb.Append(_log.Render(3));
            }
        }

        private static string Trim(string s) => s.Length > 110 ? s[..110] : s;
    }
}
