using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Engine;
using MmaManager.Core.Sim.Generation;

namespace MmaManager.Tests;

/// <summary>
/// One fixed-seed benchmark report shared by the band tests (computed once, ~2-3 s).
/// The +10 attribute block and the timing block are left out: the relevance test
/// runs its own probe and the speed test its own short measurement.
/// </summary>
public sealed class FightBenchmarkFixture
{
    public FightLab.ReportResult Report { get; } = FightLab.Report(FightLab.ReportOptions.Small with
    {
        IncludeAttributes = false,
        IncludeText = false,
        IncludeTiming = false,
    });
}

/// <summary>
/// The fight engine's regression gate (plan step 1). Three kinds of test:
/// <list type="number">
/// <item><b>Golden master</b> — exact hashes over 4,000 fixed bouts. Any change to who wins
/// or how fails it.</item>
/// <item><b>Bands</b> — today's bulk numbers within tolerances, INCLUDING the things that are
/// wrong today (no draws, no majority decisions, a third of rounds 10-8, a stored card that
/// disagrees with the result). They describe the engine as it is; the step-3/4 PRs move
/// them on purpose and update the bands in the same PR.</item>
/// <item><b>Known failures</b> — attribute relevance and style spread, checked against the
/// target with an explicit list of what misses it today, so the list can only shrink
/// knowingly.</item>
/// </list>
/// Every number here is deterministic (fixed seeds), so nothing flakes; only the speed floor
/// reads a clock, and it is set ~20x below what a quiet machine does.
/// </summary>
public class FightBenchmarkTests : IClassFixture<FightBenchmarkFixture>
{
    private readonly FightLab.ReportResult _r;
    public FightBenchmarkTests(FightBenchmarkFixture fx) => _r = fx.Report;

    // =====================================================================
    // 1. Golden master
    // =====================================================================

    /// <summary>
    /// HOW TO UPDATE THE GOLDEN MASTER (only in a PR that MEANS to change outcomes):
    /// <list type="number">
    /// <item>Run <c>cd tools/longsim; dotnet run -- fightlab</c> (or read this test's failure
    /// message) to get the new hashes.</item>
    /// <item>Paste them here, in the same PR as the engine change, and say in the PR which hash
    /// moved and why. A PR that only changes text, speed or structure must leave BOTH hashes
    /// untouched; a PR that changes which scorecard is stored but not who wins moves
    /// <see cref="GoldenScorecards"/> only, and must show <see cref="GoldenResults"/> unchanged.</item>
    /// <item>Attach the before/after <c>fightlab</c> report (and a balance-tester
    /// <c>longsim compare</c> run) so the reviewer sees what moved in bulk.</item>
    /// </list>
    /// The golden fighters are built straight from a fixed random stream, not by the
    /// generator, so changes to generation or the designed roster never move these hashes.
    /// </summary>
    // Moved in "Engine: stoppages and scoring" (plan step 3; was ce24ec976588fde0): referee, doctor
    // and corner stoppages, flash knockouts, fewer knockdowns, division power, harder submissions,
    // and a new keyed stoppage stream seeded by one draw at the opening bell.
    private const string GoldenResults = "5d5ab1507719b6c8";
    // Moved with it (was 937bdf982dea33b4; before that 44461ed3557b9b37): 10-8 only for dominance,
    // no tie-break on a level card, the card of a between-rounds stoppage includes its round.
    private const string GoldenScorecards = "9627fd63b46018f4";

    [Fact]
    public void GoldenMaster_OutcomesAreUnchanged()
    {
        var g = FightLab.GoldenMaster();
        Assert.True(g.Results == GoldenResults && g.Scorecards == GoldenScorecards,
            $"Fight outcomes changed over the {g.Bouts}-bout golden set. results {g.Results} (pinned {GoldenResults}), " +
            $"scorecards {g.Scorecards} (pinned {GoldenScorecards}). If this PR is MEANT to change outcomes, update the " +
            "constants and say so in the PR (see the procedure on GoldenResults); otherwise something changed who wins or how.");
    }

    [Fact]
    public void GoldenMaster_TextOnAndOffGiveIdenticalOutcomes()
    {
        // Building the play-by-play must never draw from the bout's random stream.
        var off = FightLab.GoldenMaster(1500, playByPlay: false);
        var on = FightLab.GoldenMaster(1500, playByPlay: true);
        Assert.Equal(off, on);
    }

    // =====================================================================
    // 2. Bands: today's numbers (seed 2026, ReportOptions.Small)
    // =====================================================================

    // Step 3 (stoppages and scoring) moved every band below on purpose. Targets, from general
    // knowledge of major-promotion MMA outcome shares (approximate): with the in-world finish rate
    // HELD at ~44% (owner's decision), finishes split about 60/40 strikes/submissions, KO/TKO about
    // 40/60, R1 the most common finishing round, ~0.5 knockdowns a fight, 10-8s a few % of rounds,
    // and among decisions split ~20%, majority ~5%, draws ~1% of fights. Heavier divisions finish
    // more with strikes; the lightest and the women's divisions less.

    [Fact]
    public void Band_ThreeRoundMethodMix()
    {
        var m = _r.ThreeRound;   // today: finish 37.1  KO 10.4  TKO 12.6  SUB 14.1  UD 49.0  SD 10.0  MD 2.5  Draw 1.5  12.2 min
        Assert.InRange(m.FinishPct, 34.0, 41.0);
        Assert.InRange(m.KoPct, 7.5, 13.5);
        Assert.InRange(m.TkoPct, 9.5, 16.0);          // was 0.4: there was no standing stoppage
        Assert.InRange(m.SubPct, 11.0, 17.5);         // was 23: submissions were the top finish
        Assert.InRange(m.UdPct, 45.0, 53.5);
        Assert.InRange(m.SdPct, 7.0, 13.0);
        Assert.InRange(m.MdPct, 1.0, 4.5);            // was unreachable
        Assert.InRange(m.DrawPct, 0.4, 3.0);          // was unreachable
        Assert.Equal(0, m.Other);
        Assert.InRange(m.AvgMinutes, 11.5, 13.0);
    }

    [Fact]
    public void Band_FiveRoundMethodMix()
    {
        var m = _r.FiveRound;    // today: finish 57.9  KO 15.5  TKO 21.9  SUB 20.5  17.3 min
        Assert.InRange(m.FinishPct, 54.0, 62.0);
        Assert.InRange(m.KoPct, 12.0, 19.5);
        Assert.InRange(m.TkoPct, 18.0, 26.0);
        Assert.InRange(m.SubPct, 17.0, 24.5);
        Assert.InRange(m.AvgMinutes, 16.3, 18.3);
    }

    [Fact]
    public void Band_SexesAndDivisions()
    {
        // Today: men 38.2, women 30.3 (n=432). Women's bouts finish less, almost all of it strikes.
        Assert.InRange(_r.Men3.FinishPct, 34.5, 42.0);
        Assert.InRange(_r.Women3.FinishPct, 24.0, 36.0);
        Assert.True(_r.Women3.KoPct + _r.Women3.TkoPct < _r.Men3.KoPct + _r.Men3.TkoPct - 4,
            $"women KO/TKO {_r.Women3.KoPct + _r.Women3.TkoPct:0.0} vs men {_r.Men3.KoPct + _r.Men3.TkoPct:0.0}");

        // Heavyweights stop more men with strikes than strawweights (today 35.0 vs 20.3, n=300 each).
        double Strikes(string div) { var m = _r.ByDivision.Single(d => d.Division == div).Mix; return m.KoPct + m.TkoPct; }
        Assert.True(Strikes("M Heavyweight") > Strikes("M Strawweight") + 7,
            $"HW KO/TKO {Strikes("M Heavyweight"):0.0} vs straw {Strikes("M Strawweight"):0.0}");
        Assert.True(Strikes("W Strawweight") < Strikes("M Lightweight"));
    }

    [Fact]
    public void Band_RoundOneFinishesMost()
    {
        // Was flat (R1 11.7 / R2 13.5 / R3 12.2 % of all fights). Today R1 12.7 / R2 12.5 / R3 11.8
        // (5 rounds: 13.1 / 12.8 / 12.4 / 11.2 / 8.4). Fatigue (step 4) should steepen it further.
        Assert.True(_r.FinishByRound3[0] >= _r.FinishByRound3[2], "R1 should finish more fights than R3");
        Assert.True(_r.FinishByRound5[0] >= _r.FinishByRound5[2] && _r.FinishByRound5[0] > _r.FinishByRound5[4]);
        Assert.InRange(_r.FinishByRound3[0], 10.0, 16.0);
    }

    [Fact]
    public void Band_StoppagesHappenEveryWay()
    {
        // How the 3-round finishes happened (share of all fights). Today: SUB 14.1, KO after a
        // knockdown 8.9, standing TKO 5.5, TKO on the follow-up after a knockdown 5.2, flash KO 1.6,
        // ground-and-pound TKO 1.3, doctor 0.6, corner ~0 (5 rounds: doctor 2.3, corner 0.1).
        double D(IReadOnlyList<(string Detail, double Pct)> ds, string k) => ds.Where(d => d.Detail.StartsWith(k, StringComparison.Ordinal)).Sum(d => d.Pct);
        Assert.InRange(D(_r.FinishDetails, "TKO: standing strikes"), 3.0, 9.0);
        Assert.InRange(D(_r.FinishDetails, "TKO: follow-up after knockdown"), 2.5, 8.5);
        Assert.InRange(D(_r.FinishDetails, "KO: flash"), 0.5, 3.5);
        Assert.InRange(D(_r.FinishDetails, "TKO: ground strikes"), 0.3, 4.0);
        Assert.InRange(D(_r.FinishDetails, "TKO: doctor") + D(_r.FinishDetails5, "TKO: doctor"), 0.5, 5.0);
        Assert.True(D(_r.FinishDetails5, "TKO: corner") > 0, "no corner stoppage in 3,000 five-rounders");
    }

    [Fact]
    public void Band_KnockdownsTakedownsAndSubs()
    {
        Assert.InRange(_r.KnockdownsPerFight, 0.38, 0.70);      // today 0.53 (was 1.52; real MMA ~0.3-0.5)
        Assert.InRange(_r.KnockdownFinishPct, 13.0, 27.0);      // today 19.7% of knockdowns are a KO (more end in a TKO)
        Assert.InRange(_r.TakedownsPerFight, 3.8, 5.0);         // today 4.41 (real ~1.5; step 5)
        Assert.InRange(_r.SubAttemptsPerFight, 2.5, 3.6);       // today 3.07 (real ~1; step 5)
    }

    [Fact]
    public void Band_Scorecards()
    {
        Assert.InRange(_r.TenEightPct, 2.0, 6.5);               // today 3.9 (was 33: any knockdown was a 10-8)
        // The stored card is a deciding judge's card and there is no tie-break: a judge with a
        // level card votes draw, so a winner's stored card always has the winner ahead.
        Assert.Equal(0.0, _r.CardReversedPct);
        Assert.Equal(0.0, _r.CardMismatchPct);                   // was 6.0 (level cards won on the tie-break)
        var totals = _r.CardTotals.ToDictionary(c => c.Card, c => c.Pct);
        Assert.InRange(totals.GetValueOrDefault("29-28"), 50.0, 70.0);   // today 60.9
        Assert.InRange(totals.GetValueOrDefault("30-27"), 22.0, 38.0);   // today 29.9
    }

    [Fact]
    public void Band_MatchRating()
    {
        // MatchRating's knockdown weight was raised (12 -> 30) with the knockdown rate cut to a third,
        // so the rating's spread — which feeds Fight of the Night, fame and sponsor terms — holds.
        Assert.InRange(_r.MatchRatingMean3, 43.5, 51.0);        // today 46.6 (was 48.1)
        Assert.InRange(_r.Stars3[4], 6.0, 12.5);                // 5-star 3-rounders: today 8.9% (was 8.0)
        Assert.InRange(_r.Stars5[4], 22.0, 32.0);               // 5-star 5-rounders: today 26.9% (was 29.3)
    }

    // Deliberately NOT moved by step 3: the favourite's edge by ability gap is held to the
    // pre-step-3 curve (today 53.2 / 65.9 / 78.1 / 85.1 / 91.1).
    [Theory]
    [InlineData(1, 2, 51.4)]
    [InlineData(5, 6, 64.0)]
    [InlineData(10, 12, 78.2)]
    [InlineData(13, 17, 83.0)]
    [InlineData(18, 22, 89.4)]
    public void Band_UpsetCurve(int lo, int hi, double today)
    {
        var bin = _r.Upset.Single(b => b.Lo == lo && b.Hi == hi);
        Assert.InRange(bin.FavouriteWinPct, today - 5, today + 5);
    }

    [Fact]
    public void UpsetCurve_RisesWithTheGap()
    {
        // Shape, not just points: a bigger ability gap never makes the favourite LESS likely
        // to win by more than noise (bins 1-2 .. 23+).
        var bins = _r.Upset.Where(b => b.Lo >= 1).ToList();
        for (int i = 1; i < bins.Count; i++)
            Assert.True(bins[i].FavouriteWinPct >= bins[i - 1].FavouriteWinPct - 3,
                $"upset curve dips at {bins[i].Lo}-{bins[i].Hi}: {bins[i].FavouriteWinPct:0.0} after {bins[i - 1].FavouriteWinPct:0.0}");
        Assert.True(bins[^1].FavouriteWinPct > 88);
    }

    [Fact]
    public void Speed_BoutsPerSecondFloor()
    {
        // ~80,000 bouts/s single-threaded with text off on a quiet machine (12 us). The floor is
        // generous so a loaded CI box never flakes; it catches an order-of-magnitude regression.
        var fast = FightLab.Report(FightLab.ReportOptions.Small with
        {
            MatchedPairs = 1, WidePairs = 1, IncludeStyles = false, IncludeAttributes = false, IncludeText = false,
            IncludeTiming = true, TimingBouts = 4000,
        });
        Assert.True(fast.BoutsPerSecondTextOff > 2_000, $"{fast.BoutsPerSecondTextOff:0} bouts/s (text off)");
    }

    // =====================================================================
    // 3. Known failures against the targets
    // =====================================================================

    /// <summary>
    /// Priced attributes and frame values the cage NEVER reads today: raising them leaves every
    /// bout bit-identical. Plan step 4 makes them matter; remove each from this list in the PR
    /// that does. (Height and reach are on the tale of the tape; stance is on the profile.)
    /// </summary>
    private static readonly string[] KnownDead =
        { "Strength", "Composure", "Heart", "Discipline", "Height", "Reach", "Stance" };

    /// <summary>
    /// Read, but worth well under the price (below <see cref="MattersFloor"/>, ~40% of an
    /// average CurrentAbility point). Cardio: stamina never runs low, so the tank barely matters.
    /// Recovery: the same. PunchPower: only the larger of PunchPower and KickPower is read, and
    /// generated fighters' kick power often wins that (1.09 at this seed, just under the floor).
    /// Plan step 4. (Step 3 lifted KickPower to 1.34 and ClinchControl to 1.33: power now drives
    /// flash knockouts and the referee's count, clinch elbows open cuts.)
    /// </summary>
    private static readonly string[] KnownWeak =
        { "Cardio", "Recovery", "PunchPower" };

    /// <summary>Tendencies that barely move win rate (Pace: fatigue never bites). Plan step 4.</summary>
    private static readonly string[] KnownWeakTendencies = { "Pace" };

    /// <summary>
    /// "Matters" for a priced attribute: the win % change from 30 to 90, divided by the
    /// CurrentAbility that swing costs, is at least this many points. The average CA point is
    /// worth ~2.5. Measured at the test's fixed seed: the weak list tops out at 0.95 (KickPower);
    /// the lowest attribute not on it is 1.38 (Flexibility).
    /// </summary>
    private const double MattersFloor = 1.15;

    /// <summary>"Matters" for a tendency: mean |win change| from 20 to 80 across the six bases, pp.
    /// Today Pace is 1.4; the next lowest (StrikingPreference) is 4.9.</summary>
    private const double TendencyFloor = 2.5;

    [Fact]
    public void AttributeRelevance_MatchesTheKnownFailureList()
    {
        // ~5,000 bouts per variant, common random numbers. Deterministic: the classification only
        // changes when the engine (or the generator's CA-65 fighters) changes. To check a
        // borderline case, run `fightlab relevance <seed>` in tools/longsim at a few seeds.
        var rel = FightLab.AttributeRelevance(seed: 2026, low: 30, high: 90, fieldSize: 12, seedsPerPair: 70);
        var problems = new List<string>();

        foreach (var r in rel)
        {
            bool isTendency = FightLab.TendencyNames.Contains(r.Name);
            bool priced = r.Weight > 0;
            double tendencySwing = r.PerBasePp.Average(Math.Abs);
            bool matters = !r.Dead && (isTendency ? tendencySwing >= TendencyFloor : !priced || r.PpPerCaPoint >= MattersFloor);

            if (KnownDead.Contains(r.Name))
            {
                if (!r.Dead) problems.Add($"{r.Name} is no longer dead (now {r.MeanPp:+0.00} pp) — remove it from KnownDead" +
                                          (priced && r.PpPerCaPoint < MattersFloor ? " and add it to KnownWeak" : ""));
            }
            else if (r.Dead)
                problems.Add($"{r.Name} has NO effect: every bout is bit-identical when it changes");
            else if (KnownWeak.Contains(r.Name) || KnownWeakTendencies.Contains(r.Name))
            {
                if (matters) problems.Add($"{r.Name} now matters ({Describe(r)}) — remove it from the known-weak list");
            }
            else if (!matters)
                problems.Add($"{r.Name} barely matters ({Describe(r)}), below the floor");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));

        static string Describe(FightLab.Relevance r) => double.IsNaN(r.PpPerCaPoint)
            ? $"mean |swing| {r.PerBasePp.Average(Math.Abs):0.00} pp"
            : $"{r.PpPerCaPoint:+0.00} pp per CA point";
    }

    [Fact]
    public void AttributeTable_MatchesFighterAbility()
    {
        // The lab's attribute table must price exactly as FighterAbility does.
        var gen = new FighterGenerator(77);
        double wSum = FightLab.Attributes.Sum(a => a.Weight);
        Assert.Equal(25, FightLab.Attributes.Count);
        for (int i = 0; i < 60; i++)
        {
            var f = gen.Generate(new FighterId(i + 1), new DateOnly(2026, 1, 1), new GenOptions { Overall = 35 + i, WithRecord = false });
            double sum = FightLab.Attributes.Sum(a => a.Get(f) * a.Weight);
            Assert.Equal(FighterAbility.CurrentAbility(f), (int)Math.Round(sum / wSum));
        }
    }

    /// <summary>
    /// Plan step 4 target: no style more than this many points better than another against the
    /// field at equal CurrentAbility. Today it is a strict ladder with Brawler on top and
    /// Well-rounded at the bottom (14.2 pp at this seed). Known failure: flip
    /// <see cref="StyleSpreadIsKnownFailure"/> to false in the PR that meets the target.
    /// </summary>
    private const double StyleSpreadTarget = 6.0;
    private static readonly bool StyleSpreadIsKnownFailure = true;   // readonly, not const: keeps both branches compiled

    [Fact]
    public void StyleSpread_KnownFailureAgainstTarget()
    {
        double spread = _r.StyleSpreadPp;
        if (StyleSpreadIsKnownFailure)
        {
            Assert.True(spread > StyleSpreadTarget,
                $"Style spread is now {spread:0.0} pp, within the {StyleSpreadTarget} pp target — set StyleSpreadIsKnownFailure = false.");
            Assert.InRange(spread, 10.0, 20.0);                         // today 14.2
            Assert.InRange(_r.BrawlerMinusWellRoundedPp, 8.0, 19.0);    // today 13.7: the "complete" fighter loses to the brawler
        }
        else
        {
            Assert.True(spread <= StyleSpreadTarget, $"Style spread {spread:0.0} pp exceeds the {StyleSpreadTarget} pp target.");
        }
    }

    [Fact]
    public void FightLabCompare_CountsOnlyJudgedWinsAsDecisions()
    {
        var gen = new FighterGenerator(5);
        var a = gen.Generate(new FighterId(1), new DateOnly(2026, 1, 1), new GenOptions { Overall = 65, WithRecord = false });
        var b = gen.Generate(new FighterId(2), new DateOnly(2026, 1, 1), new GenOptions { Overall = 65, WithRecord = false });
        var s = FightLab.Compare(a, b, 500, seed: 3);
        Assert.Equal(s.Fights, s.Kos + s.Tkos + s.Submissions + s.Decisions + s.Draws + s.Other);
    }
}
