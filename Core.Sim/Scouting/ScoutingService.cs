using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;

namespace MmaManager.Core.Sim.Scouting;

/// <summary>How precisely the player can read a stat: nothing, a rough band, or the real number.</summary>
public enum StatReveal { Hidden, Approximate, Exact }

/// <summary>The coarse qualitative band shown while a stat is only approximately known.</summary>
public enum StatBand { Weak, Average, Strong, Elite }

/// <summary>
/// Evidence buckets. Every individual fighter stat is revealed by exactly one of
/// these channels, and each channel fills up from the kinds of things a fight (or
/// a fighter's known pedigree) actually shows. <see cref="StatChannel.Public"/> is
/// for stats that are never hidden (draw, charisma, style tendencies).
/// </summary>
public enum StatChannel
{
    Power, Striking, StrikingDef, Clinch,
    Wrestling, WrestlingDef, Ground, Submission, SubmissionDef,
    Cardio, Chin, Athletic, Mind,
    Public,
}

/// <summary>
/// One bout as a scout would read it — extracted from a completed
/// <see cref="FightOutcome"/> for the fighter being assessed.
/// </summary>
public readonly record struct BoutObservation(
    bool Win,
    bool Loss,
    FightMethod Method,
    int EndRound,
    int ScheduledRounds,
    int SignificantStrikesLanded,
    int KnockdownsScored,
    int KnockdownsAgainst,
    int TakedownsLanded,
    int TakedownsAgainst,
    int SubmissionAttempts,
    int ControlTimeSeconds)
{
    public bool WonByKo => Win && Method is FightMethod.Knockout or FightMethod.TechnicalKnockout;
    public bool LostByKo => Loss && Method is FightMethod.Knockout or FightMethod.TechnicalKnockout;
    public bool WonBySub => Win && Method is FightMethod.SubmissionTap or FightMethod.TechnicalSubmission;
    public bool LostBySub => Loss && Method is FightMethod.SubmissionTap or FightMethod.TechnicalSubmission;
    public bool WentToDecision => Method is FightMethod.UnanimousDecision or FightMethod.SplitDecision
        or FightMethod.MajorityDecision or FightMethod.Draw;

    /// <summary>Read a fighter's side of a completed bout into the scout's terms.</summary>
    public static BoutObservation FromOutcome(FighterId self, FightOutcome o, int scheduledRounds)
    {
        var mine = o.Stats.TryGetValue(self, out var m) ? m : new FightStatline();
        var oppEntry = o.Stats.FirstOrDefault(kv => kv.Key != self);
        var opp = oppEntry.Value ?? new FightStatline();
        return new BoutObservation(
            Win: o.Winner == self,
            Loss: o.Loser == self,
            Method: o.Method,
            EndRound: o.EndRound,
            ScheduledRounds: scheduledRounds,
            SignificantStrikesLanded: mine.SignificantStrikesLanded,
            KnockdownsScored: mine.KnockdownsScored,
            KnockdownsAgainst: opp.KnockdownsScored,
            TakedownsLanded: mine.TakedownsLanded,
            TakedownsAgainst: opp.TakedownsLanded,
            SubmissionAttempts: mine.SubmissionAttempts,
            ControlTimeSeconds: mine.ControlTimeSeconds);
    }
}

/// <summary>What the player currently knows about one fighter's ratings.</summary>
public sealed class ScoutReport
{
    private readonly IReadOnlyDictionary<StatChannel, double> _reveal;

    /// <summary>Bouts the player has actually watched (detailed tape drives targeted reveals).</summary>
    public int ObservedFights { get; }

    /// <summary>The fighter's whole pro record — public knowledge, drives general familiarity.</summary>
    public int CareerBouts { get; }

    internal ScoutReport(IReadOnlyDictionary<StatChannel, double> reveal, int observed, int career)
    {
        _reveal = reveal;
        ObservedFights = observed;
        CareerBouts = career;
    }

    /// <summary>0 = nothing known, 1 = fully scouted (exact numbers).</summary>
    public double Reveal(StatChannel c) =>
        c == StatChannel.Public ? 1.0 : _reveal.TryGetValue(c, out var v) ? v : 0.0;

    public StatReveal Precision(StatChannel c)
    {
        double r = Reveal(c);
        return r >= ScoutingService.ExactAt ? StatReveal.Exact
            : r >= ScoutingService.ApproxAt ? StatReveal.Approximate
            : StatReveal.Hidden;
    }

    /// <summary>The band a true 0–100 rating falls into (only shown when Approximate).</summary>
    public static StatBand Band(int value) =>
        value >= 80 ? StatBand.Elite : value >= 65 ? StatBand.Strong : value >= 45 ? StatBand.Average : StatBand.Weak;
}

/// <summary>
/// Fog of War for fighter stats. A PLAYER-FACING knowledge layer only — the true
/// ratings never change and the sim always uses them; this just decides how much
/// of each stat the player has earned the right to see. Knowledge grows from how
/// many times a fighter has been watched, HOW they fought (a KO reveals power, a
/// sub reveals grappling, going the distance reveals cardio, getting finished
/// reveals the chin), and their publicly-known style. The player's own fighters
/// clear faster than outsiders. Computed on the fly — no saved state.
/// </summary>
public static class ScoutingService
{
    internal const double ExactAt = 1.0;
    internal const double ApproxAt = 0.28;
    // Ambient exposure is deliberately weak: watching a fighter teaches only a
    // little about stats he never puts on display. A one-dimensional KO artist
    // keeps his untested dimensions (cardio, ground) foggy — that's the point.
    // Most fighters reveal everything over ~8-10 bouts because those bouts are
    // VARIED (a few decisions, a takedown here, a scramble there).
    private const double GeneralPerFight = 0.05;
    // Record alone can't fully solve a fighter: no matter how long his résumé, if
    // you've never studied his tape you top out at a strong band, not exact numbers.
    // Exact comes from watching him (targeted evidence) or having him on your books.
    private const double GeneralCap = 0.85;
    private const double OwnFactor = 1.7;          // your own roster clears quicker

    public static ScoutReport Assess(Fighter f, bool ownedByPlayer, IReadOnlyList<BoutObservation> bouts, double scoutingBonus = 0)
    {
        var targeted = new Dictionary<StatChannel, double>();
        void Add(StatChannel c, double amt) => targeted[c] = targeted.GetValueOrDefault(c) + amt;

        // (1) Publicly-known style gives a head start on its signature channels, so a
        //     known wrestler isn't a total mystery on the mat before he's thrown a hand.
        SeedStyle(f.PrimaryStyle, Add);

        // (2) A fighter's PRO RECORD is public: the more career bouts, the more
        //     roughly known they are across the board (a 14-0 champ is no mystery,
        //     even if you haven't personally studied every one of his fights).
        int career = f.Record.TotalBouts;
        double general = Math.Min(career * GeneralPerFight, GeneralCap);

        // (3) The tape you've actually watched shows SPECIFIC stats — a KO reveals
        //     power, a sub reveals grappling, the distance reveals the gas tank.
        foreach (var b in bouts)
        {
            if (b.WonByKo || b.KnockdownsScored > 0) { Add(StatChannel.Power, 0.24); Add(StatChannel.Striking, 0.10); }
            if (b.SignificantStrikesLanded >= 35) { Add(StatChannel.Striking, 0.10); Add(StatChannel.Athletic, 0.05); }
            if (b.LostByKo || b.KnockdownsAgainst > 0) Add(StatChannel.Chin, 0.28);
            if (b.TakedownsLanded >= 1) Add(StatChannel.Wrestling, 0.14);
            if (b.TakedownsAgainst >= 1) Add(StatChannel.WrestlingDef, 0.14);
            if (b.WonBySub || b.SubmissionAttempts >= 1) { Add(StatChannel.Submission, 0.22); Add(StatChannel.Ground, 0.10); }
            if (b.LostBySub) { Add(StatChannel.SubmissionDef, 0.28); Add(StatChannel.Ground, 0.10); }
            if (b.ControlTimeSeconds >= 120) { Add(StatChannel.Wrestling, 0.10); Add(StatChannel.Ground, 0.08); Add(StatChannel.Clinch, 0.06); }
            if (b.WentToDecision) { Add(StatChannel.Cardio, 0.18); Add(StatChannel.Mind, 0.06); Add(StatChannel.Chin, 0.05); Add(StatChannel.StrikingDef, 0.06); }
            if (b.EndRound >= 3) Add(StatChannel.Cardio, 0.10);
        }

        // Your scouting department clears the fog faster across the board — a top
        // scout sees exact numbers on prospects and rivals well before a promotion
        // that's just going off tape and reputation.
        double own = (ownedByPlayer ? OwnFactor : 1.0) * (1.0 + Math.Max(0, scoutingBonus));
        var reveal = new Dictionary<StatChannel, double>();
        foreach (StatChannel c in Enum.GetValues<StatChannel>())
        {
            if (c == StatChannel.Public) continue;
            reveal[c] = (general + targeted.GetValueOrDefault(c)) * own;
        }
        return new ScoutReport(reveal, bouts.Count, career);
    }

    // === AI-side Fog of War =================================================
    // The player isn't the only one squinting: rival promotions ALSO misjudge
    // talent they haven't seen enough of. An org's PERCEIVED ability/potential is
    // the true value nudged by an estimation error that shrinks as (a) the fighter
    // builds a record, (b) the fighter is on the org's own books, and (c) the org
    // is bigger — the giants have real scouting departments; the regional scenes
    // guess. The error is a deterministic function of (org, fighter), so an org is
    // consistently high or low on a given fighter (no per-tick flicker), the sim
    // stays reproducible, and no new save state is needed. Net effect: orgs
    // sometimes chase a prospect who busts or pass on a hidden gem — a market a
    // sharp operator (or the player) can exploit. Veterans are valued correctly.
    private const int MaxError = 10;              // worst-case misjudgement on a total unknown
    private const int FullyKnownFights = 17;      // a record this long ⇒ no guesswork left
    private const double OwnKnowledgeFloor = 0.85;// you know your own roster, mostly

    /// <summary>What <paramref name="evaluator"/> THINKS this fighter's ability is (0–100).</summary>
    public static int PerceivedAbility(Promotion evaluator, Fighter f) =>
        PerceivedAbility(null, evaluator, f);

    /// <summary>
    /// World-aware overload: a famous camp puts its members on everyone's tape,
    /// so rival orgs misjudge them less — your hidden gem won't stay hidden at a
    /// big room (and a nobody from a legendary gym gets priced seriously).
    /// </summary>
    public static int PerceivedAbility(World? world, Promotion evaluator, Fighter f)
    {
        int trueAbility = FighterAbility.CurrentAbility(f);
        double familiarity = Familiarity(evaluator, f);
        if (world is not null)
            familiarity = Math.Max(familiarity, Living.Camps.PublicExposure(world, f));
        if (familiarity >= 0.999) return trueAbility;
        double competence = Math.Clamp((evaluator.Prestige.Value - 20) / 60.0, 0, 1);
        double error = MaxError * (1 - familiarity) * (1 - competence * 0.6);
        double dir = SignedNoise(evaluator.Id.Value, f.Id.Value, salt: 101);   // stable [-1,1] per (org,fighter)
        return Math.Clamp((int)Math.Round(trueAbility + dir * error), 1, 100);
    }

    private static double Familiarity(Promotion org, Fighter f)
    {
        double fam = Math.Min(f.Record.TotalBouts / (double)FullyKnownFights, 1.0);
        if (f.Contract?.Promotion == org.Id) fam = Math.Max(fam, OwnKnowledgeFloor);
        return fam;
    }

    // Deterministic hash of (org, fighter, salt) → [-1, 1]. FNV mix + a murmur-style
    // finalizer for a clean avalanche, so each org's error averages out to its true
    // competence-scaled magnitude (a weak hash lets one org look luckier than it is).
    // No RNG stream, so it never perturbs the seeded sim and survives save/load.
    private static double SignedNoise(int a, int b, int salt)
    {
        unchecked
        {
            uint h = 2166136261u;
            h = (h ^ (uint)a) * 16777619u;
            h = (h ^ (uint)b) * 16777619u;
            h = (h ^ (uint)salt) * 16777619u;
            h ^= h >> 16; h *= 2246822519u;
            h ^= h >> 13; h *= 3266489917u;
            h ^= h >> 16;
            return h / (double)uint.MaxValue * 2.0 - 1.0;
        }
    }

    private static void SeedStyle(PrimaryStyle style, Action<StatChannel, double> add)
    {
        const double Prime = 0.34, Second = 0.20, Third = 0.13;   // Prime clears ApproxAt on its own
        switch (style)
        {
            case PrimaryStyle.Striker:
                add(StatChannel.Striking, Prime); add(StatChannel.Power, Second); add(StatChannel.StrikingDef, Third); break;
            case PrimaryStyle.Kickboxer:
                add(StatChannel.Striking, Prime); add(StatChannel.Power, Second + 0.04); add(StatChannel.StrikingDef, Third); break;
            case PrimaryStyle.Brawler:
                add(StatChannel.Power, Prime); add(StatChannel.Striking, Third); add(StatChannel.Chin, Third); break;
            case PrimaryStyle.Wrestler:
                add(StatChannel.Wrestling, Prime); add(StatChannel.WrestlingDef, Second); add(StatChannel.Ground, Third); break;
            case PrimaryStyle.BrazilianJiuJitsu:
                add(StatChannel.Submission, Prime); add(StatChannel.Ground, Second); add(StatChannel.SubmissionDef, Third); break;
            case PrimaryStyle.Grappler:
                add(StatChannel.Wrestling, Second + 0.04); add(StatChannel.Ground, Second + 0.04); add(StatChannel.Submission, Third); break;
            case PrimaryStyle.ClinchFighter:
                add(StatChannel.Clinch, Prime); add(StatChannel.Wrestling, Third); break;
            case PrimaryStyle.WellRounded:
                foreach (var c in new[] { StatChannel.Striking, StatChannel.Wrestling, StatChannel.Submission,
                             StatChannel.Ground, StatChannel.Cardio, StatChannel.Chin }) add(c, 0.10);
                break;
        }
    }
}
