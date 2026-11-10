using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Flavor;

/// <summary>
/// How good a fight actually was, as a 0–100 score derived from the action
/// (output, knockdowns, submission attempts, the finish) — and as the 1–5 star
/// line the UI shows next to results. Pure function of the recorded statlines,
/// so no schema change and old saves rate their history retroactively. Feeds
/// Fight of the Night and the yearly awards.
/// </summary>
public static class MatchRating
{
    public static int Of(Fight f)
    {
        if (f.Outcome is not { } o) return 0;

        double strikes = o.Stats.Values.Sum(s => s.SignificantStrikesLanded);
        double knockdowns = o.Stats.Values.Sum(s => s.KnockdownsScored);
        double subAttempts = o.Stats.Values.Sum(s => s.SubmissionAttempts);
        bool finish = IsFinish(o.Method);

        double score = strikes * 0.5
                     + knockdowns * 12
                     + subAttempts * 6
                     + (finish ? 20 : 0)
                     + (finish && o.EndRound >= 3 ? 6 : 0);   // a war that ends late

        // A split decision means it was competitive to the wire.
        if (o.Method is FightMethod.SplitDecision or FightMethod.MajorityDecision) score += 8;

        return (int)Math.Clamp(score, 0, 100);
    }

    /// <summary>The score where a fight reads as four stars — the bar other systems use for "an exciting fight".</summary>
    public const int FourStar = 68;

    /// <summary>The five-star score — a genuine show-stealer, rare even on the club scene.</summary>
    public const int FiveStar = 85;

    /// <summary>
    /// The star line shown in results ("★★★★", 1–5). Earned stars only — padding
    /// the line with outline glyphs made every fight read like five stars.
    /// </summary>
    public static string Stars(int rating)
    {
        int stars = rating >= FiveStar ? 5 : rating >= FourStar ? 4 : rating >= 50 ? 3 : rating >= 32 ? 2 : 1;
        return new string('★', stars);
    }

    public static string StarsFor(Fight f) => Stars(Of(f));

    private static bool IsFinish(FightMethod m) => m is
        FightMethod.Knockout or FightMethod.TechnicalKnockout or
        FightMethod.SubmissionTap or FightMethod.TechnicalSubmission;
}
