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
        public Diag D = new();
    }

    /// <summary>
    /// Why the founded org keeps or loses its fighters (reporting only, read-only): every
    /// deal that ended is classed by what became of him, renewals are priced against the
    /// deal they replace and against what the AI's own renewal would have offered, and
    /// the script's in-window refusals are counted by reason.
    /// </summary>
    public sealed class Diag
    {
        public int AiRenewals, ScriptRenewals, EarlyRenewals, PoachedOrSigned, ToMarket, Retired;
        public double AiRenewRatioSum, ScriptRenewRatioSum, ScriptVsAiOfferSum;
        public int ScriptVsAiN, AiOfferWouldBeTaken;
        public decimal ScriptSigningBonus;
        public int SkipSkid, SkipDear, SkipBroke, SkipRefused;
        public double SkipDearAskOverPurseSum, SkipDearAskOverCapSum;
        public double RenewedCaSum, LeftCaSum, RenewedPopSum, LeftPopSum;
        public int RenewedN, LeftN;
        public long IdleChecks, RosterChecks;
        public int DaysToResignSum, DaysToResignN;
        public int ChampAiRenewals, ChampAiRenewBelowAsk, ChampLeft, AiRenewBelowAsk, BeltChecks, BeltHeldChecks, TitleFights, Cards;
        // Rival offers (Contracts.OfferMarket): how the script answered, and how each ended.
        public int OffersLodged, OffersMatchedByScript, OffersLetGoByScript, OfferEndMatched, OfferEndBeaten, OfferEndSigned, OfferEndWithdrawn, OffersOpen, LiferRefusals;
        public int OfferChampions, OfferTop2, OfferStarsLost;
        public decimal MatchPurseSum;
        public void Add(Diag o)
        {
            foreach (var fl in typeof(Diag).GetFields())
            {
                var a = fl.GetValue(this)!; var b = fl.GetValue(o)!;
                fl.SetValue(this, a switch { int x => (object)(x + (int)b), long x => x + (long)b, double x => x + (double)b, decimal x => x + (decimal)b, _ => a });
            }
        }
    }

    public static void Main(string[] args)
    {
        ulong from = ulong.Parse(args[1]), to = ulong.Parse(args[2]);
        int years = int.Parse(args[3]);
        bool scriptOnly = args.Contains("--script-only");
        bool dealRules = PlayerDealRules(args), resign = !args.Contains("--no-resign");
        Early = args.Contains("--early-resign"); KeepToCap = args.Contains("--keep-to-cap"); DepthBookable = args.Contains("--depth-bookable"); MimicAi = args.Contains("--mimic-ai") || args.Contains("--mimic-ai-no-champ"); MimicNoChampForce = args.Contains("--mimic-ai-no-champ");
        var runs = new ConcurrentBag<Run>();
        Parallel.ForEach(Enumerable.Range((int)from, (int)(to - from + 1)).Select(i => (ulong)i),
            new ParallelOptions { MaxDegreeOfParallelism = Parallelism(args) },
            seed => runs.Add(One(seed, years, scriptOnly, dealRules, resign)));

        var all = runs.OrderBy(r => r.Seed).ToList();
        Console.WriteLine($"=== SHOESWEEP seeds {from}-{to} · {years} years · {(scriptOnly ? "SCRIPT ONLY" : "default harness (world AI also signs)")}"
            + $" · {(dealRules ? (resign ? "player deal rules, script re-signs in the window" : "player deal rules, NO re-signing") : "AI renewals")}"
            + $"{(Early ? " · re-signs on the LAST FIGHT" : "")}{(KeepToCap ? " · keeps up to the org's purse cap" : "")}{(DepthBookable ? " · hires around men in their window" : "")} ===");
        Console.WriteLine($"  re-signed in the window (all seeds): {all.Sum(r => r.ReSigned)} · windows lapsed to free agency: {all.Sum(r => r.Lapsed)}");
        Console.WriteLine("  per seed: " + string.Join(" ", all.Select(r => $"{r.Seed}:{r.FinalPrestige}{(r.Folded ? "F" : "")}")));
        var d = new Diag(); foreach (var r in all) d.Add(r.D);
        static string R(double sum, int n) => n == 0 ? "-" : (sum / n).ToString("0.00");
        int ended = d.AiRenewals + d.ScriptRenewals + d.PoachedOrSigned + d.ToMarket;
        Console.WriteLine($"  DIAG deals ended (all seeds): {ended} · renewed by AI {d.AiRenewals} (new/old purse {R(d.AiRenewRatioSum, d.AiRenewals)})"
            + $" · renewed by script {d.ScriptRenewals} (of which before expiry {d.EarlyRenewals}; new/old purse {R(d.ScriptRenewRatioSum, d.ScriptRenewals)};"
            + $" table purse / AI renewal offer {R(d.ScriptVsAiOfferSum, d.ScriptVsAiN)}, AI offer would have been taken {d.AiOfferWouldBeTaken}/{d.ScriptVsAiN}; signing bonuses {d.ScriptSigningBonus:C0})"
            + $" · left to another org {d.PoachedOrSigned} · left to the market {d.ToMarket} · retired off the roster {d.Retired}");
        Console.WriteLine($"  DIAG script skipped in window: skid {d.SkipSkid} · too dear {d.SkipDear} (ask/purse {R(d.SkipDearAskOverPurseSum, d.SkipDear)}, ask/PurseCap {R(d.SkipDearAskOverCapSum, d.SkipDear)}) · bank under $100k {d.SkipBroke} · table refused {d.SkipRefused}"
            + $" · days from deal end to re-sign {R(d.DaysToResignSum, d.DaysToResignN)}");
        Console.WriteLine($"  DIAG who: renewed mean CA {R(d.RenewedCaSum, d.RenewedN)} pop {R(d.RenewedPopSum, d.RenewedN)} · left mean CA {R(d.LeftCaSum, d.LeftN)} pop {R(d.LeftPopSum, d.LeftN)}"
            + $" · roster share sitting at 0 fights (monthly checks) {(d.RosterChecks == 0 ? 0 : 100.0 * d.IdleChecks / d.RosterChecks):0.0}%");
        int seedYears = Math.Max(1, all.Sum(r => r.Years.Count));
        Console.WriteLine($"  DIAG rival offers: lodged {d.OffersLodged} ({(double)d.OffersLodged / seedYears:0.00} per seed-year; champions {d.OfferChampions}, top-2 in division {d.OfferTop2})"
            + $" · script matched {d.OffersMatchedByScript} (mean match purse {(d.OffersMatchedByScript == 0 ? 0 : d.MatchPurseSum / d.OffersMatchedByScript):N0}) · let go {d.OffersLetGoByScript}"
            + $" · ended: matched {d.OfferEndMatched} · beaten at the table {d.OfferEndBeaten} · LOST to the rival {d.OfferEndSigned} (champion/top-2 {d.OfferStarsLost})"
            + $" · withdrawn {d.OfferEndWithdrawn} · still open {d.OffersOpen} · lifer refusals {d.LiferRefusals}");
        Console.WriteLine($"  DIAG belts: champions renewed by the AI {d.ChampAiRenewals} (below his ask {d.ChampAiRenewBelowAsk}) · all AI renewals below his ask {d.AiRenewBelowAsk}/{d.AiRenewals}"
            + $" · champions who left {d.ChampLeft} · belts filled {(d.BeltChecks == 0 ? 0 : 100.0 * d.BeltHeldChecks / d.BeltChecks):0.0}% of monthly checks · title fights {d.TitleFights} on {d.Cards} cards");
        foreach (var r in all.Where(r => r.Seed is 3 or 5))
            Console.WriteLine($"  DIAG seed {r.Seed}: final {r.FinalPrestige} · AI renew {r.D.AiRenewals} · script renew {r.D.ScriptRenewals} (early {r.D.EarlyRenewals}) · left org {r.D.PoachedOrSigned} · left market {r.D.ToMarket} · skip dear {r.D.SkipDear} skid {r.D.SkipSkid} broke {r.D.SkipBroke}"
                + " · prestige by year " + string.Join(",", r.Years.Select(y => y.Prestige)) + " · roster by year " + string.Join(",", r.Years.Select(y => y.Roster)));
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
    public static int ReSignInWindow(World world, Promotion org, decimal maxAsk, FighterValuation valuation, Diag? diag = null)
    {
        int n = 0;
        // --early-resign: a real owner re-signs on the LAST fight of the deal, before it
        // ends, as well as anyone already in his window. --keep-to-cap: the budget for a
        // renewal is at least the org's own purse cap (what the AI renewal pays up to),
        // not just the shoestring hiring budget or 1.5x his old purse.
        var due = world.RosterOf(org.Id)
            .Where(f => DealWindow.InWindow(world, f) || (Early && f.Status == FighterStatus.Active && f.Contract is { FightsRemaining: 1 }))
            .OrderBy(f => f.Contract!.FightsRemaining).ThenBy(f => f.Id.Value).ToList();
        if (MimicAi)
        {
            // --mimic-ai: renew exactly as the AI expiry pass would for this org — a
            // champion is ALWAYS re-signed at the AI's capped opening offer (no
            // acceptance check: ContractLifecycle.ProcessExpiries' champion branch),
            // anyone else at that offer if the org wants him and he accepts.
            // --mimic-ai-no-champ: the same, but a champion must accept like anyone.
            var neg = new Negotiator();
            foreach (var f in due.Where(f => f.Contract!.FightsRemaining <= 0))
            {
                var exp = valuation.Evaluate(world, f);
                var ai = neg.AiOpeningOffer(world, org, f, exp);
                bool champ = world.Titles.Values.Any(t => t.CurrentChampion == f.Id);
                bool ok = (champ && !MimicNoChampForce) || (RosterPolicy.Wants(world, org, f) && neg.Evaluate(world, f, ai, exp).Verdict == NegotiationVerdict.Accept);
                if (!ok) continue;
                if (diag is not null) { diag.ScriptRenewals++; if (champ) diag.ChampAiRenewals++; }
                ContractLifecycle.Sign(world, f, ai, world.CurrentDate);
                n++;
            }
            return n;
        }
        foreach (var f in due)
        {
            bool early = f.Contract!.FightsRemaining > 0;
            if (org.Balance.Amount < 100_000m) { if (diag is not null && !early) diag.SkipBroke++; continue; }
            if (f.Record.CurrentStreak <= -3) { if (diag is not null && !early) diag.SkipSkid++; continue; }
            var exp = valuation.Evaluate(world, f);
            var session = NegotiationSession.Resume(world, f, org, exp);
            var offer = session.TheirTermsAsOffer(Negotiator.WantsExclusivity(org), clause: false);
            decimal budget = Math.Max(maxAsk, f.Contract.PursePerFight.Amount * 1.5m);
            if (KeepToCap) budget = Math.Max(budget, Negotiator.PurseCap(world, org));
            if (offer.PursePerFight.Amount > budget)
            {
                if (diag is not null && !early)
                {
                    diag.SkipDear++;
                    diag.SkipDearAskOverPurseSum += (double)(offer.PursePerFight.Amount / Math.Max(1m, f.Contract.PursePerFight.Amount));
                    diag.SkipDearAskOverCapSum += (double)(offer.PursePerFight.Amount / Math.Max(1m, Negotiator.PurseCap(world, org)));
                }
                continue;
            }
            if (session.Table(offer).Verdict != TalkVerdict.Accept) { if (diag is not null && !early) diag.SkipRefused++; continue; }
            if (diag is not null)
            {
                diag.ScriptRenewals++;
                if (early) diag.EarlyRenewals++;
                diag.ScriptRenewRatioSum += (double)(offer.PursePerFight.Amount / Math.Max(1m, f.Contract.PursePerFight.Amount));
                diag.ScriptSigningBonus += offer.SigningBonus.Amount;
                var ai = new Negotiator().AiOpeningOffer(world, org, f, exp);
                diag.ScriptVsAiOfferSum += (double)(offer.PursePerFight.Amount / Math.Max(1m, ai.PursePerFight.Amount));
                diag.ScriptVsAiN++;
                if (new Negotiator().Evaluate(world, f, ai, exp).Verdict == NegotiationVerdict.Accept) diag.AiOfferWouldBeTaken++;
                diag.RenewedN++; diag.RenewedCaSum += FighterAbility.CurrentAbility(f); diag.RenewedPopSum += f.Intangibles.Popularity.Value;
                if (!early && f.Contract.WindowEndsOn is { } end)
                {
                    diag.DaysToResignSum += world.CurrentDate.DayNumber - (end.DayNumber - DealWindow.WindowDays);
                    diag.DaysToResignN++;
                }
            }
            ContractLifecycle.Sign(world, f, offer, world.CurrentDate);
            n++;
        }
        return n;
    }

    /// <summary>
    /// The scripted owner's answer to each rival offer on the desk (Contracts.OfferMarket):
    /// MATCH when the fighter matters (a champion, one of the two best in his division on
    /// the roster, or a draw at popularity 40+) and the match is affordable (the bank keeps
    /// a $150k runway after the signing bonus, and the purse is within 1.25x of the larger
    /// of the shoestring budget and the org's own purse cap); otherwise LET HIM GO (he can
    /// still be re-signed at the table in his window, at the offer's floor). Reads true
    /// ability, as the rest of the script does.
    /// </summary>
    public static void AnswerOffers(World world, Promotion org, decimal maxAsk, Diag? diag = null)
    {
        foreach (var o in world.RivalOffers.Where(o => o.Holder == org.Id && o.State == RivalOfferState.Pending).OrderBy(o => o.Id).ToList())
        {
            if (world.Get(o.Fighter) is not { } f) continue;
            var (champ, top2) = Matters(world, org, f);
            var terms = OfferMarket.MatchTerms(world, o);
            bool matters = champ || top2 || f.Intangibles.Popularity.Value >= 40;
            bool affordable = org.Balance.Amount >= 150_000m + terms.SigningBonus.Amount
                              && terms.PursePerFight.Amount <= Math.Max(maxAsk, Negotiator.PurseCap(world, org)) * 1.25m;
            if (matters && affordable && OfferMarket.Match(world, o, world.CurrentDate))
            {
                if (diag is not null) { diag.OffersMatchedByScript++; diag.MatchPurseSum += terms.PursePerFight.Amount; }
                continue;
            }
            OfferMarket.LetGo(world, o, world.CurrentDate);
            if (diag is not null) diag.OffersLetGoByScript++;
        }
    }

    private static (bool Champ, bool Top2) Matters(World world, Promotion org, Fighter f)
    {
        bool champ = world.Titles.Values.Any(t => t.Promotion == org.Id && t.CurrentChampion == f.Id);
        var div = org.Divisions.FirstOrDefault(d => d.Matches(f));
        bool top2 = div is not null && world.RosterOf(org.Id).Where(x => x.Status == FighterStatus.Active && div.Matches(x))
            .OrderByDescending(FighterAbility.CurrentAbility).ThenBy(x => x.Id.Value).Take(2).Any(x => x.Id == f.Id);
        return (champ, top2);
    }

    /// <summary>
    /// Bookkeeping for the offer DIAG (reporting only): every offer for the org seen this
    /// block, its last state, and lifers who turned a rival down (one per deal).
    /// </summary>
    public sealed class OfferLog
    {
        private readonly Dictionary<(FighterId, DateOnly), RivalOffer> _seen = new();   // (ids are reused once old offers are dropped)
        private readonly HashSet<(FighterId, DateOnly)> _refusals = new();
        private readonly HashSet<RivalOffer> _stars = new(ReferenceEqualityComparer.Instance);
        public void Scan(World world, Promotion org, Diag d)
        {
            foreach (var o in world.RivalOffers.Where(o => o.Holder == org.Id))
                if (_seen.TryAdd((o.Fighter, o.Made), o))
                {
                    d.OffersLodged++;
                    if (world.Get(o.Fighter) is { } f)
                    {
                        var (champ, top2) = Matters(world, org, f);
                        if (champ) d.OfferChampions++;
                        if (top2) d.OfferTop2++;
                        if (champ || top2) _stars.Add(o);
                    }
                }
            foreach (var f in world.RosterOf(org.Id))
                if (f.Contract is { BondNoticeOn: { } b } c && b == Contract.RefusalTold && _refusals.Add((f.Id, c.SignedOn)))
                    d.LiferRefusals++;
        }
        public void Finish(Diag d)
        {
            foreach (var o in _seen.Values)
                switch (o.State)
                {
                    case RivalOfferState.Matched: d.OfferEndMatched++; break;
                    case RivalOfferState.Beaten: d.OfferEndBeaten++; break;
                    case RivalOfferState.Signed: d.OfferEndSigned++; if (_stars.Contains(o)) d.OfferStarsLost++; break;
                    case RivalOfferState.Withdrawn: d.OfferEndWithdrawn++; break;
                    default: d.OffersOpen++; break;
                }
        }
    }

    private static bool Early, KeepToCap, DepthBookable, MimicAi, MimicNoChampForce;   // sweep options (one sweep per process); --depth-bookable: a man in his window is not counted as division depth when hiring

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
                    .Where(d => world.RosterOf(nwf.Id).Count(f => d.Matches(f) && f.Status == FighterStatus.Active && !(DepthBookable && DealWindow.OutOfDeal(world, f))) < depth)
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
        var offers = new OfferLog();
        var lastTier = OrgSizing.SizeOf(nwf);
        try
        {
            for (int year = 1; year <= years; year++)
            {
                for (int m = 0; m < 13 && !nwf.Defunct; m++)
                {
                    foreach (var div in nwf.Divisions)
                        if (div.Title is { } tt && world.Get(tt) is { } belt) { run.D.BeltChecks++; if (belt.CurrentChampion is not null) run.D.BeltHeldChecks++; }
                    foreach (var f in world.RosterOf(nwf.Id).Where(f => f.Status == FighterStatus.Active))
                    {
                        run.D.RosterChecks++;
                        if (f.Contract is { FightsRemaining: <= 0 }) run.D.IdleChecks++;
                    }
                    if (dealRules && !nwf.Defunct) AnswerOffers(world, nwf, MaxAsk(), run.D);
                    if (dealRules && resign && !nwf.Defunct) run.ReSigned += ReSignInWindow(world, nwf, MaxAsk(), valuation, run.D);
                    PlayerAgent();
                    var inWindow = dealRules ? world.RosterOf(nwf.Id).Where(f => DealWindow.InWindow(world, f)).Select(f => f.Id).ToList() : new();
                    // Every deal on the books before the four weeks, to class what became of it.
                    var before = world.RosterOf(nwf.Id).Where(f => f.Contract is not null)
                        .ToDictionary(f => f.Id, f => (f.Contract!.SignedOn, Purse: f.Contract.PursePerFight.Amount, Ca: FighterAbility.CurrentAbility(f), Pop: f.Intangibles.Popularity.Value,
                            Champ: world.Titles.Values.Any(t => t.Promotion == nwf.Id && t.CurrentChampion == f.Id),
                            Ask: f.Contract.FightsRemaining <= 1 ? Negotiator.EffectiveAskingPurse(world, nwf, f, valuation.Evaluate(world, f)).Amount : 0m));
                    for (int w = 0; w < 4; w++) { tick.AdvanceWeek(world); offers.Scan(world, nwf, run.D); }
                    foreach (var (id, was) in before)
                    {
                        var f = world.Get(id);
                        if (f is null || f.Status == FighterStatus.Retired) { run.D.Retired++; continue; }
                        if (f.Contract?.Promotion == nwf.Id)
                        {
                            if (f.Contract.SignedOn == was.SignedOn) continue;
                            run.D.AiRenewals++;   // re-signed during the advance: the world AI's renewal
                            bool below = was.Ask > 0 && f.Contract.PursePerFight.Amount < was.Ask * 0.995m;
                            if (below) run.D.AiRenewBelowAsk++;
                            if (was.Champ) { run.D.ChampAiRenewals++; if (below) run.D.ChampAiRenewBelowAsk++; }
                            run.D.AiRenewRatioSum += (double)(f.Contract.PursePerFight.Amount / Math.Max(1m, was.Purse));
                            run.D.RenewedN++; run.D.RenewedCaSum += was.Ca; run.D.RenewedPopSum += was.Pop;
                            continue;
                        }
                        if (f.Contract is null) run.D.ToMarket++; else run.D.PoachedOrSigned++;
                        if (was.Champ) run.D.ChampLeft++;
                        run.D.LeftN++; run.D.LeftCaSum += was.Ca; run.D.LeftPopSum += was.Pop;
                    }
                    // a window that closed in these four weeks: he walked to free agency
                    run.Lapsed += inWindow.Count(id => world.Get(id) is { } f && f.Contract?.Promotion != nwf.Id && f.Status != FighterStatus.Retired);
                }
                if (nwf.Defunct) { run.Folded = true; break; }
                var tier = OrgSizing.SizeOf(nwf);
                for (var t = lastTier + 1; t <= tier; t++) run.Crossed.TryAdd(t, year);
                lastTier = tier > lastTier ? tier : lastTier;

                var evs = world.Events.Values.Where(e => e.Promotion == nwf.Id && e.Financials is not null && e.Date > world.CurrentDate.AddDays(-365)).ToList();
                run.D.Cards += evs.Count; run.D.TitleFights += evs.Sum(e => e.Card.Count(x => x.IsTitleFight));
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
        offers.Finish(run.D);
        run.FinalPrestige = nwf.Prestige.Value;
        run.FinalTier = OrgSizing.SizeOf(nwf);
        return run;
    }
}
