namespace MmaManager.Core.Model;

/// <summary>How two fighters are bound off the cage.</summary>
public enum RelationshipKind
{
    /// <summary>Blood — brothers, cousins, a famous father's son. Never fades.</summary>
    Family = 0,
    /// <summary>Friends — training partners, came up together, tight since the amateurs.</summary>
    Friend = 1,
    /// <summary>A retired great in a prospect's corner. A is the mentor, B the protégé.</summary>
    Mentor = 2,
}

/// <summary>
/// A bond between two fighters — the warm counterpart to <see cref="Rivalry"/>.
/// Bonds shape the world the way bad blood does: friends won't be booked against
/// each other on an undercard (only a title forces it, and the friendship pays),
/// free agents drift toward rosters where their people are, and family ties make
/// newgen dynasties. Strength is 0–100; a broken friendship can flip into a rivalry.
/// </summary>
public sealed class Relationship
{
    public required FighterId A { get; init; }
    public required FighterId B { get; init; }
    public required RelationshipKind Kind { get; init; }

    /// <summary>How deep it runs, 0–100. Grows with time together; a forced fight cuts it.</summary>
    public int Strength { get; set; }

    public required DateOnly Since { get; set; }

    /// <summary>Flavor for the profile/news: "younger brother", "longtime training partners"…</summary>
    public string? Note { get; set; }

    public bool Involves(FighterId id) => A == id || B == id;
    public bool IsBetween(FighterId x, FighterId y) => (A == x && B == y) || (A == y && B == x);
    public FighterId Other(FighterId id) => A == id ? B : A;
}
