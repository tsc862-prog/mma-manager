using MmaManager.Core.Model;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Living;

/// <summary>
/// Fights leave a mark. After an event this rolls for injuries — most fighters
/// walk away fine, but a bad knockout loss or a long war can shelve someone for
/// weeks or months, and a rare catastrophic injury ends a career. Even those who
/// come through clean need a routine camp-and-recover turnaround before their
/// next bout. Both layoffs scale with age (older heals slower) and the fighter's
/// Recovery stat (a good engine bounces back). Injured fighters drop out of
/// matchmaking until <see cref="Lifecycle.HealReturning"/> brings them back;
/// everyone else is gated by <see cref="Fighter.ReadyToFightOn"/>.
/// </summary>
public static class InjuryModel
{
    public static int RollForEvent(World world, Event ev, DateOnly date, SimRandom rng)
    {
        int injured = 0;
        foreach (var fight in ev.Card)
        {
            if (fight.Outcome is null) continue;
            if (RollFighter(world, fight.RedCorner, fight, date, rng)) injured++;
            if (RollFighter(world, fight.BlueCorner, fight, date, rng)) injured++;
        }
        return injured;
    }

    private static bool RollFighter(World world, FighterId id, Fight fight, DateOnly date, SimRandom rng)
    {
        var f = world.Get(id);
        if (f is null || f.Status != FighterStatus.Active) return false;
        if (f.LocalTalent) return false;           // locals fade back to the scene, no recovery to track

        var o = fight.Outcome!;
        bool lostByKo = o.Loser == id && o.Method is FightMethod.Knockout or FightMethod.TechnicalKnockout;

        double chance = 0.05;                      // baseline wear and tear
        if (lostByKo) chance += 0.16;              // getting finished hurts
        if (o.EndRound >= 3) chance += 0.03;       // long fights take a toll

        if (!rng.Chance(chance))
        {
            // No injury — but everyone still needs to recover between fights.
            ScheduleRoutineRecovery(f, o, id, date);
            return false;
        }

        var severity = RollSeverity(rng, lostByKo);
        if (severity == InjurySeverity.Career)
        {
            Lifecycle.Retire(world, f, date);
            return true;
        }

        int weeks = severity switch
        {
            InjurySeverity.Minor => rng.Range(2, 4),
            InjurySeverity.Moderate => rng.Range(6, 12),
            _ => rng.Range(16, 32),                // Serious
        };
        // Good medical staff shortens the layoff; age and a poor engine lengthen it.
        double factor = RecoveryScale(f, date);
        if (world.Get(f.Contract?.Promotion ?? default) is { } promo)
            factor *= Org.StaffService.MedicalRecoveryFactor(promo);
        weeks = Math.Max(1, (int)Math.Ceiling(weeks * factor));

        var returnOn = date.AddDays(weeks * 7);
        f.CurrentInjury = new Injury
        {
            Severity = severity,
            Description = rng.Pick(Descriptions),
            OccurredOn = date,
            ExpectedReturn = returnOn,
        };
        f.Status = FighterStatus.Injured;
        f.ReadyToFightOn = returnOn;                // cleared to fight once healed
        return true;
    }

    /// <summary>The rest a fighter needs after a clean bout — before they can be booked again.</summary>
    internal static void ScheduleRoutineRecovery(Fighter f, FightOutcome o, FighterId id, DateOnly date)
    {
        double weeks = 3;                          // a base camp-and-recover turnaround
        if (o.EndRound >= 3) weeks += 1;           // a long, draining fight
        if (o.Loser == id) weeks += 1;             // took the beating
        if (o.Loser == id && o.Method is FightMethod.Knockout or FightMethod.TechnicalKnockout)
            weeks += 2;                            // knocked out — extra caution
        weeks *= RecoveryScale(f, date);
        f.ReadyToFightOn = date.AddDays((int)Math.Round(weeks * 7));
    }

    /// <summary>
    /// How age and conditioning stretch or shorten a layoff: a fighter past their
    /// prime heals slower (~5%/yr past 30), and the Recovery stat swings it from
    /// 0.7× (elite engine) to 1.3× (poor). ~1.0 for a prime, average athlete.
    /// </summary>
    private static double RecoveryScale(Fighter f, DateOnly date)
    {
        double ageFactor = 1.0 + Math.Max(0, f.AgeOn(date) - 30) * 0.05;
        double recFactor = 1.3 - f.Physical.Recovery.Value / 100.0 * 0.6;
        return ageFactor * recFactor;
    }

    private static InjurySeverity RollSeverity(SimRandom rng, bool lostByKo)
    {
        // A KO loss skews toward the more serious end of the scale.
        Span<double> w = lostByKo
            ? stackalloc double[] { 0.45, 0.38, 0.15, 0.02 }
            : stackalloc double[] { 0.62, 0.30, 0.075, 0.005 };
        return (InjurySeverity)rng.WeightedIndex(w);
    }

    private static readonly string[] Descriptions =
    {
        "broken hand", "torn ACL", "orbital fracture", "cracked rib", "knee sprain",
        "concussion", "shoulder dislocation", "torn meniscus", "broken foot", "herniated disc",
    };
}
