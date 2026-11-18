using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Engine;

/// <summary>Knobs for a single simulated bout.</summary>
public sealed record FightParams
{
    /// <summary>Scheduled rounds — 3 for a normal bout, 5 for main events / title fights.</summary>
    public int ScheduledRounds { get; init; } = 3;

    /// <summary>Seconds per round (the promotion's ruleset sets this; 300 is the sport's standard).</summary>
    public int RoundSeconds { get; init; } = 300;

    /// <summary>
    /// When false, the outcome's <c>PlayByPlay</c> is left null and no commentary text is
    /// built at all (faster mass sims). The result is identical either way.
    /// </summary>
    public bool GeneratePlayByPlay { get; init; } = true;

    /// <summary>
    /// Picks the commentary wording (which of several equivalent lines is used) by hash,
    /// never by the bout's random stream, so it can never change a result. A booked fight
    /// passes its fight id; zero is fine for lab bouts.
    /// </summary>
    public ulong TextSeed { get; init; }

    /// <summary>
    /// The rules the bout is fought under (Origins: one long round, no judges, gloves, ground
    /// restrictions, rope escapes, open weight). Null or <see cref="FightRules.Modern"/> = today's
    /// sport, byte for byte. <see cref="FightRules.Rounds"/> and <see cref="FightRules.RoundMinutes"/>,
    /// when set, override <see cref="ScheduledRounds"/> and <see cref="RoundSeconds"/>.
    /// </summary>
    public FightRules? Rules { get; init; }

    /// <summary>
    /// The red corner's condition at the opening bell, when it is not a fresh fighter (the second
    /// and third bouts of a one-night tournament). Null = fresh (today).
    /// </summary>
    public BoutCondition? RedStart { get; init; }

    /// <summary>The blue corner's condition at the opening bell (see <see cref="RedStart"/>).</summary>
    public BoutCondition? BlueStart { get; init; }

    /// <summary>
    /// Under a no-judges rule set only: a bout still level at the limit gets ONE overtime period of
    /// this many seconds (no rest, same rules); a draw after it stands. 0 = none (the default; a
    /// tournament final asks for 300). Ignored with judges or on today's rules.
    /// </summary>
    public int OvertimeSeconds { get; init; }

    /// <summary>
    /// A bout inside a BANDED division (Origins, after a weight split: the light division runs from flyweight
    /// to middleweight), or an open-weight bout kept after the house drew its lines: the size term applies
    /// although the rules are not open weight. Only the Origins booker sets it, for a card bout and a bracket
    /// bout alike (EventService.SizeTermFor); today's mode never carries it. Under a custom rule set the full size
    /// term runs; under today's rules only its relative edge does (Bout.ApplyRelativeSize), on the modern path and
    /// on the custom path a bracket's carried condition puts a later bout on.
    /// </summary>
    public bool BandedSizeTerm { get; init; }

    public static FightParams ThreeRound { get; } = new() { ScheduledRounds = 3 };
    public static FightParams FiveRound { get; } = new() { ScheduledRounds = 5 };
    public static FightParams FastThreeRound { get; } = new() { ScheduledRounds = 3, GeneratePlayByPlay = false };
}

/// <summary>
/// A fighter's physical state carried between bouts on one night: what is left of his health
/// (0-100) and gas tank (0-100), how bad a cut is (0 none; the doctor stops a bout at 3+) and how
/// much power his hands still have (1 = whole; a damaged bare hand hits softer). Not saved: it
/// lives only for the length of a tournament night.
/// </summary>
public sealed record BoutCondition(double Health, double Stamina, int Cut, double HandPower)
{
    public static BoutCondition Fresh { get; } = new(100, 100, 0, 1);

    /// <summary>Health below this after the rest, and he cannot go on.</summary>
    public const double WithdrawHealth = 30;

    /// <summary>Fit to fight the next bout: enough left in him, and no cut the doctor would stop.</summary>
    public bool CanContinue => Health >= WithdrawHealth && Cut < 3;

    // --- Rest between bouts on a tournament night (tunables) ---------------
    /// <summary>Health back during the rest: base + this much per Recovery/100.</summary>
    public const double RestHealthBase = 6, RestHealthRecovery = 14;
    /// <summary>Stamina back during the rest: base + this much per Recovery/100 (a long rest; most comes back)...</summary>
    public const double RestStaminaBase = 35, RestStaminaRecovery = 35;
    /// <summary>...but the night's minutes in the cage leave a debt the rest cannot repay: the tank's
    /// ceiling drops this many points per minute fought, scaled by (1.3 - 0.6 * Cardio/100), floor 40.</summary>
    public const double FatigueDebtPerMinute = 1.1;

    /// <summary>
    /// The rest between bouts on one night: health and wind partly come back, a small cut is closed
    /// up by the cutman (one level; a bad one, 3+, stays), the hand stays as it is.
    /// <paramref name="minutesFoughtTonight"/> is the whole night's cage time so far, including the
    /// bout just finished. Deterministic.
    /// </summary>
    public BoutCondition AfterRest(Fighter f, double minutesFoughtTonight)
    {
        double rec = f.Physical.Recovery / 100.0;
        double ceiling = Math.Clamp(
            100 - minutesFoughtTonight * FatigueDebtPerMinute * (1.3 - 0.6 * f.Physical.Cardio / 100.0), 40, 100);
        double health = Math.Min(100, Health + RestHealthBase + RestHealthRecovery * rec);
        double stamina = Math.Max(Math.Min(Stamina, ceiling), Math.Min(ceiling, Stamina + RestStaminaBase + RestStaminaRecovery * rec));
        int cut = Cut >= 3 ? Cut : Math.Max(0, Cut - 1);
        return new BoutCondition(health, stamina, cut, HandPower);
    }
}
