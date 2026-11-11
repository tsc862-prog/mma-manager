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
    private const string GoldenResults = "ce24ec976588fde0";
    private const string GoldenScorecards = "44461ed3557b9b37";

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

    [Fact]
    public void Band_ThreeRoundMethodMix()
    {
        var m = _r.ThreeRound;   // today: finish 37.4  KO 14.0  TKO 0.4  SUB 23.0  UD 52.2  SD 10.4  12.4 min
        Assert.InRange(m.FinishPct, 34.0, 41.0);
        Assert.InRange(m.KoPct, 11.5, 16.5);
        Assert.InRange(m.TkoPct, 0.0, 1.5);           // wrong today: almost no TKOs (no standing stoppage) — step 3
        Assert.InRange(m.SubPct, 20.0, 26.0);         // wrong today: submissions are the top finish — step 3
        Assert.InRange(m.UdPct, 48.0, 56.5);
        Assert.InRange(m.SdPct, 7.5, 13.5);
        Assert.InRange(m.MdPct, 0.0, 0.2);            // wrong today: majority decisions unreachable — step 3
        Assert.InRange(m.DrawPct, 0.0, 0.2);          // wrong today: draws unreachable — step 3
        Assert.Equal(0, m.Other);
        Assert.InRange(m.AvgMinutes, 11.6, 13.2);
    }

    [Fact]
    public void Band_FiveRoundMethodMix()
    {
        var m = _r.FiveRound;    // today: finish 59.3  KO 22.6  SUB 36.1  UD 33.9  SD 6.8  17.3 min
        Assert.InRange(m.FinishPct, 56.0, 63.0);
        Assert.InRange(m.KoPct, 19.5, 26.0);
        Assert.InRange(m.SubPct, 32.5, 40.0);
        Assert.InRange(m.AvgMinutes, 16.3, 18.3);
    }

    [Fact]
    public void Band_SexesFinishAlike()
    {
        // Today the engine never sees sex: men 36.8, women 41.0 (n=432) — same machine.
        Assert.InRange(_r.Men3.FinishPct, 33.5, 40.5);
        Assert.InRange(_r.Women3.FinishPct, 33.0, 48.0);
    }

    [Fact]
    public void Band_FinishesSpreadEvenlyOverRounds()
    {
        // Wrong today: finishes are flat across rounds (R1 11.7 / R2 13.5 / R3 12.2 % of all fights);
        // real MMA finishes most in R1. Step 3 should make R1 the top finishing round.
        Assert.InRange(_r.FinishByRound3[0], 9.0, 14.5);
        Assert.InRange(_r.FinishByRound3[1], 10.5, 16.5);
        Assert.InRange(_r.FinishByRound3[2], 9.5, 15.0);
    }

    [Fact]
    public void Band_KnockdownsTakedownsAndSubs()
    {
        Assert.InRange(_r.KnockdownsPerFight, 1.25, 1.80);      // today 1.52 (real MMA ~0.3-0.5)
        Assert.InRange(_r.KnockdownFinishPct, 7.0, 12.0);       // today 9.3% of knockdowns end it
        Assert.InRange(_r.TakedownsPerFight, 4.0, 5.2);         // today 4.57 (real ~1.5)
        Assert.InRange(_r.SubAttemptsPerFight, 2.5, 3.4);       // today 2.94 (real ~1)
    }

    [Fact]
    public void Band_Scorecards()
    {
        Assert.InRange(_r.TenEightPct, 29.0, 36.5);             // wrong today: any knockdown is a 10-8 — step 3
        // Wrong today: the stored card is built apart from the judges who decide the result
        // and does not favour the official winner in ~15% of decisions. Plan step 2 stores a
        // deciding judge's card instead and moves this to 0 (and updates this band).
        Assert.InRange(_r.CardMismatchPct, 11.0, 18.5);         // today 14.6
        Assert.InRange(_r.CardReversedPct, 2.0, 6.5);           // today 4.0: the card says the LOSER won
    }

    [Fact]
    public void Band_MatchRating()
    {
        Assert.InRange(_r.MatchRatingMean3, 45.0, 51.0);        // today 48.1
        Assert.InRange(_r.Stars3[4], 6.0, 10.5);                // 5-star 3-rounders: today 8.0%
        Assert.InRange(_r.Stars5[4], 25.0, 33.5);               // 5-star 5-rounders: today 29.3% (too many)
    }

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
    /// Recovery: the same. ClinchControl: helps some styles and hurts others, net about zero.
    /// PunchPower / KickPower: only the larger of the two is read. Plan step 4.
    /// </summary>
    private static readonly string[] KnownWeak =
        { "Cardio", "Recovery", "ClinchControl", "PunchPower", "KickPower" };

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
    private const bool StyleSpreadIsKnownFailure = true;

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
