namespace MmaManager.Core.Model;

/// <summary>What the fighters wear on their hands.</summary>
public enum GloveType
{
    /// <summary>Today's small open-fingered MMA glove.</summary>
    Modern,
    /// <summary>A thin open-fingered glove: a little padding, little protection for the hand.</summary>
    ThinOpenFinger,
    /// <summary>Bare hands (tape at most).</summary>
    Bare,
    /// <summary>Each fighter chooses: strikers pull on thin gloves to save their hands, grapplers go bare.</summary>
    Optional,
}

/// <summary>Where the fight happens: it only changes the words ("against the ropes", "the fence").</summary>
public enum Enclosure
{
    /// <summary>A fenced cage (today's sport).</summary>
    Cage,
    /// <summary>A roped ring.</summary>
    Ring,
    /// <summary>An open floor with the crowd around it (no fence, no ropes).</summary>
    OpenFloor,
}

/// <summary>
/// The rules a bout is fought under. The default (<see cref="Modern"/>) is today's sport exactly:
/// the booked rounds, three judges, modern gloves, everything legal on the ground, no rope
/// escapes, fighters of one weight class. The early-era promotions of Origins mode change it:
/// one long round, no judges (a draw at the time limit), bare hands, rope escapes, open weight.
///
/// Every field's default reproduces today's fight byte for byte, so a promotion with no rule set
/// (<c>OrgRules.Fight</c> null) and one with <see cref="Modern"/> fight identical bouts.
/// </summary>
public sealed record FightRules
{
    /// <summary>Rounds per bout; null = whatever the bout was booked for (today).</summary>
    public int? Rounds { get; init; }

    /// <summary>Minutes per round; null = the promotion's <c>OrgRules.RoundMinutes</c> (today). A single
    /// round of 20 or 30 minutes is legal.</summary>
    public int? RoundMinutes { get; init; }

    /// <summary>Judges score a bout that reaches the limit. False = no judges: a bout that reaches
    /// the limit unfinished is a draw.</summary>
    public bool Judges { get; init; } = true;

    public GloveType Gloves { get; init; } = GloveType.Modern;

    /// <summary>Closed-fist punches to the head of a grounded man are legal (false: palms and body shots only).</summary>
    public bool GroundedHeadPunches { get; init; } = true;

    /// <summary>Kicks to a grounded man are legal. The engine models no kicks on the ground, so this
    /// changes nothing in the cage today; it is carried so the house rules read true.</summary>
    public bool GroundedKicks { get; init; } = true;

    /// <summary>Elbow strikes are legal (false: fewer cuts, none in the clinch or on the ground).</summary>
    public bool Elbows { get; init; } = true;

    /// <summary>Rope escapes per fighter per bout: grabbing the rope breaks a submission and the
    /// fighters restart standing. 0 = no such rule. Past the allowance the rope still breaks the
    /// hold, but costs the fighter a point on every judge's card (and with no judges, the rope
    /// no longer saves him).</summary>
    public int RopeEscapes { get; init; }

    /// <summary>No weight classes: size matters in the cage (the engine's size term is on).</summary>
    public bool OpenWeight { get; init; }

    /// <summary>Cage, ring or open floor (text only).</summary>
    public Enclosure Enclosure { get; init; } = Enclosure.Cage;

    /// <summary>Today's rules.</summary>
    public static FightRules Modern { get; } = new();

    /// <summary>True when these are today's rules in every field.</summary>
    public bool IsModern => Equals(Modern);
}
