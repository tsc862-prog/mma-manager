namespace MmaManager.Core.Sim.Engine;

/// <summary>Knobs for a single simulated bout.</summary>
public sealed record FightParams
{
    /// <summary>Scheduled rounds — 3 for a normal bout, 5 for main events / title fights.</summary>
    public int ScheduledRounds { get; init; } = 3;

    /// <summary>Seconds per round (the promotion's ruleset sets this; 300 is the sport's standard).</summary>
    public int RoundSeconds { get; init; } = 300;

    /// <summary>When false, the outcome's <c>PlayByPlay</c> is left null (faster mass sims).</summary>
    public bool GeneratePlayByPlay { get; init; } = true;

    public static FightParams ThreeRound { get; } = new() { ScheduledRounds = 3 };
    public static FightParams FiveRound { get; } = new() { ScheduledRounds = 5 };
    public static FightParams FastThreeRound { get; } = new() { ScheduledRounds = 3, GeneratePlayByPlay = false };
}
