namespace MmaManager.Core.Model;

/// <summary>A back-office role the player can hire for. Each has a distinct effect.</summary>
public enum StaffRole
{
    /// <summary>Marketing/PR — grows the gate and builds regional standing faster.</summary>
    Marketing,
    /// <summary>Medical — your fighters recover from injuries quicker.</summary>
    Medical,
    /// <summary>Head coach — your fighters develop toward their potential faster.</summary>
    HeadCoach,
    /// <summary>Scouting — you see through the Fog of War faster (clearer reads on prospects and rivals).</summary>
    Scouting,
}

/// <summary>
/// A hireable member of staff. Quality (0–100) drives how strong their effect is;
/// the monthly salary is billed through the ledger as overhead.
/// </summary>
public sealed class StaffMember
{
    public required StaffRole Role { get; init; }
    public required string Name { get; init; }
    public int Quality { get; set; }
    public Money MonthlySalary { get; set; }

    /// <summary>
    /// One line on where they came from — "twenty years cornering in Bangkok",
    /// "ran the medical desk at the AFC until the travel beat him". A hire the
    /// player can't picture is just a number with a salary attached.
    /// </summary>
    public string? Background { get; set; }
}

public enum ObjectiveKind { StayProfitable, RunEvents }

public enum ObjectiveStatus { Active, Met, Failed }

/// <summary>
/// A goal the owner sets the player, resolved by a deadline. Meeting or missing it
/// is surfaced through the inbox and the org screen (and, later, job security).
/// </summary>
public sealed class Objective
{
    public required ObjectiveKind Kind { get; init; }
    public required string Description { get; init; }
    public int Target { get; init; }
    public required DateOnly Start { get; init; }
    public required DateOnly Deadline { get; init; }
    public ObjectiveStatus Status { get; set; } = ObjectiveStatus.Active;
}
