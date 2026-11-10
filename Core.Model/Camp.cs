namespace MmaManager.Core.Model;

/// <summary>
/// A fight camp / team — the gym a fighter actually trains at (the American Top
/// Team / Gracie Barra of this world). Fighters develop THROUGH their camp, not
/// their promotion: a great room grows talent faster (and shapes what it can
/// see — campmates know each other). Camps live in real map cities, carry a
/// specialty, and are led by a head coach (a retired great, once the coaching
/// pipeline lands). The world starts with designed camps; new ones get founded
/// when famous fighters retire.
/// </summary>
public sealed class Camp
{
    public required CampId Id { get; init; }
    public required string Name { get; init; }
    public required CityId City { get; init; }

    /// <summary>What the room teaches best — members matching it grow faster.</summary>
    public required PrimaryStyle Specialty { get; init; }

    /// <summary>The camp's name in the sport, 0–100. Grows with its members' wins and belts.</summary>
    public int Reputation { get; set; }

    /// <summary>The retired great running the room (null = journeyman coaching staff).</summary>
    public FighterId? HeadCoach { get; set; }

    /// <summary>How good the coaching is, 0–100 — the development lever.</summary>
    public int CoachQuality { get; set; }

    /// <summary>Elite rooms are selective; a local gym takes anyone.</summary>
    public int Capacity { get; set; } = 999;

    /// <summary>What training here costs a fighter (or whoever pays their way).</summary>
    public Money MonthlyFee { get; set; }

    /// <summary>
    /// Authored identity prose — the room's story and culture (designed day-one
    /// camps). Null for procedural local gyms and later foundings. Persisted.
    /// </summary>
    public string? Lore { get; set; }

    public override string ToString() => Name;
}
