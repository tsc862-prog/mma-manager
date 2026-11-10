namespace MmaManager.Core.Model;

/// <summary>A dated career honour shown on the fighter's profile.</summary>
public sealed record Accolade(DateOnly Date, string Title);

/// <summary>
/// The central entity. Skills live in grouped rating objects; dynamic career
/// state (morale, momentum, injury, record) is mutated by the sim as time
/// passes. Relationships to other entities are held by id, never by object
/// reference, so the whole graph stays acyclic and easy to serialise.
/// </summary>
public sealed class Fighter
{
    public required FighterId Id { get; init; }

    public required string FirstName { get; set; }
    public required string LastName { get; set; }
    public string? Nickname { get; set; }
    public string FullName => Nickname is null
        ? $"{FirstName} {LastName}"
        : $"{FirstName} \"{Nickname}\" {LastName}";

    // --- Fixed / physical ---------------------------------------------------
    public required DateOnly BirthDate { get; init; }
    public Sex Sex { get; set; } = Sex.Male;
    public Region Nationality { get; set; }
    public Stance Stance { get; set; }
    public int HeightCm { get; set; }
    public int ReachCm { get; set; }
    public WeightClass WeightClass { get; set; }

    /// <summary>
    /// The fighter's natural (walk-around) weight in pounds, set once at creation
    /// from their division's realistic band. Stable — distinct from the class they
    /// currently compete in — so it can later drive moving up/down weight classes,
    /// and it's what weight-locked portrait faces match against.
    /// </summary>
    public int NaturalWeightLbs { get; set; }

    // --- Skills (see Attributes.cs) -----------------------------------------
    public StrikingRatings Striking { get; init; } = new();
    public GrapplingRatings Grappling { get; init; } = new();
    public PhysicalRatings Physical { get; init; } = new();
    public MentalRatings Mental { get; init; } = new();
    public Intangibles Intangibles { get; init; } = new();
    public Tendencies Tendencies { get; init; } = new();
    public PrimaryStyle PrimaryStyle { get; set; }

    /// <summary>
    /// Who they are outside the cage — the label the sport would use ("a quiet
    /// professional", "a loudmouth"). Authored on the designed roster, rolled
    /// for everyone else, and stable for a career. Picks the words they say.
    /// </summary>
    public Disposition Disposition { get; set; } = Disposition.QuietProfessional;

    /// <summary>The numbers behind the <see cref="Disposition"/> — what the sim reads.</summary>
    public Temperament Temperament { get; init; } = new();

    /// <summary>
    /// Marketable look baked into the fighter's portrait, stamped by the art
    /// layer when a portrait identity is assigned (glam · adonis · menace ·
    /// inked · babyface; null = none). Flavor/popularity-side only — feeds draw
    /// power and popularity growth, never competitive stats.
    /// </summary>
    public string? PersonaTag { get; set; }

    /// <summary>
    /// Authored backstory prose (the designed initial database; later, story
    /// systems may stamp one). Null for the procedurally generated — the
    /// Biographer derives their story from stats instead. Persisted.
    /// </summary>
    public string? Bio { get; set; }

    /// <summary>
    /// The rare it-factor (~2% of fighters): the camera loves them. Popularity
    /// grows faster from every win, and above ~60 pop their gate contribution
    /// scales super-linearly — the fighter who can carry a company. Flavor and
    /// money only; never competitive stats.
    /// </summary>
    public bool StarPower { get; set; }

    /// <summary>
    /// From the hand-designed initial database (vs procedurally generated).
    /// Designed headliners get first pick of the portrait identities that span
    /// all three life stages, so the world's stars visibly age.
    /// </summary>
    public bool Designed { get; set; }

    /// <summary>
    /// An unnamed local-scene opponent ("Local Striker") a small promotion books
    /// to build up its own talent. Retires after the bout — unless they pull the
    /// upset, in which case they're "discovered": renamed into a real prospect
    /// who wants to sign with the org that gave them the shot.
    /// </summary>
    public bool LocalTalent { get; set; }

    /// <summary>
    /// Hidden ceiling on overall ability. The growth model in the sim layer
    /// can raise skills toward — but not past — this cap (à la Football
    /// Manager's Current Ability / Potential Ability). Set once at creation.
    /// </summary>
    public Rating PotentialAbility { get; set; }

    // --- Dynamic career state (mutated by the sim) --------------------------
    public FighterStatus Status { get; set; } = FighterStatus.Active;
    public Rating Morale { get; set; } = 50;

    /// <summary>
    /// -100 (ice cold, long skid) to +100 (red hot). Plain signed int because
    /// it's bipolar; the sim clamps it. Feeds confidence and matchmaking.
    /// </summary>
    public int Momentum { get; set; }

    public Injury? CurrentInjury { get; set; }

    /// <summary>
    /// Earliest date the fighter is recovered enough to be booked again — set after
    /// every bout (a routine camp-and-recover turnaround; longer after a war or a
    /// knockout, scaled by age and the Recovery stat), and to an injury's return
    /// date when hurt. Null => ready now. Gates matchmaking, not signing.
    /// </summary>
    public DateOnly? ReadyToFightOn { get; set; }

    /// <summary>Recovered and available to be put on a card as of <paramref name="date"/>.</summary>
    public bool IsMatchReady(DateOnly date) =>
        Status == FighterStatus.Active && CurrentInjury is null
        && (ReadyToFightOn is null || ReadyToFightOn.Value <= date);

    /// <summary>When a suspension ends (fighter is <see cref="FighterStatus.Suspended"/> until then). Null => not suspended.</summary>
    public DateOnly? SuspendedUntil { get; set; }

    public Contract? Contract { get; set; }              // null => free agent

    /// <summary>
    /// How attached the fighter is to their CURRENT promotion (0-100). Starts low
    /// (~20) on a fresh signing and grows the more the org uses and rewards them
    /// (getting booked, wins, bonuses, title shots, tenure), decaying when they're
    /// left on the shelf. A loyal fighter is far more expensive for a rival to
    /// poach — never impossible, just a lot more money. Resets when they change orgs.
    /// </summary>
    public int Loyalty { get; set; } = 20;

    /// <summary>
    /// Whether the promotion is running an active marketing push on this fighter —
    /// a paid campaign that grows their <see cref="Intangibles.Popularity"/> month
    /// over month (billed while it runs). The player's lever to build a draw.
    /// </summary>
    public bool MarketingPush { get; set; }

    /// <summary>
    /// The promotion that DISCOVERED them on the local circuit (null for everyone
    /// else). Gratitude is real: early in the career they take a genuine discount
    /// from the org that gave them the shot — and full price from anyone else.
    /// </summary>
    public PromotionId? DiscoveredBy { get; set; }

    public FightRecord Record { get; init; } = new();
    public List<FightId> BoutHistory { get; init; } = new();

    /// <summary>
    /// Career honours that live on the profile forever: yearly awards ("2027
    /// Fighter of the Year") and special-event medals ("2028 Olympic Games — gold").
    /// </summary>
    public List<Accolade> Accolades { get; init; } = new();

    /// <summary>
    /// The fighter's real nationality (a country). <see cref="Nationality"/> keeps
    /// the continental rollup alongside for the systems that think in continents.
    /// </summary>
    public CountryId Country { get; set; }

    /// <summary>
    /// Transient: the atlas country code the generator chose (e.g. "JPN"),
    /// consumed by AssignHometown to resolve <see cref="Country"/> in-world.
    /// Not persisted — Country is.
    /// </summary>
    public string? OriginCountryCode { get; set; }

    /// <summary>The fighter's hometown — where they came up, and where they draw like a star.</summary>
    public CityId? HomeCity { get; set; }

    /// <summary>The fight camp they train at (null = trains solo — a real disadvantage).</summary>
    public CampId? Camp { get; set; }

    /// <summary>Their org covers the camp fee (a stipend) — better rooms, and loyalty for it.</summary>
    public bool CampStipend { get; set; }

    /// <summary>
    /// How well-known the fighter is in each CITY (0–100), grown by fighting
    /// there. Region-level fame is derived from this (market-weighted average),
    /// never stored — see the sim's Reputation helper.
    /// </summary>
    public Dictionary<CityId, Rating> CityReputation { get; init; } = new();

    /// <summary>Age in whole years on a given date (birthdate drives aging).</summary>
    public int AgeOn(DateOnly date)
    {
        int age = date.Year - BirthDate.Year;
        if (date < BirthDate.AddYears(age)) age--;
        return age;
    }
}

/// <summary>
/// Win/loss tally. This is a cache derived from <see cref="Fighter.BoutHistory"/>;
/// the sim updates it when a fight completes so the UI never recomputes it.
/// </summary>
public sealed class FightRecord
{
    public int Wins { get; set; }
    public int Losses { get; set; }
    public int Draws { get; set; }
    public int NoContests { get; set; }

    public int WinsByKo { get; set; }
    public int WinsBySubmission { get; set; }
    public int WinsByDecision { get; set; }

    /// <summary>Current run: positive = consecutive wins, negative = consecutive losses, 0 = last bout was a draw/NC.</summary>
    public int CurrentStreak { get; set; }

    public int TotalBouts => Wins + Losses + Draws + NoContests;

    public override string ToString() =>
        $"{Wins}-{Losses}-{Draws}" + (NoContests > 0 ? $" ({NoContests} NC)" : "");
}

/// <summary>A fighter's deal with a promotion. Absent (null) => free agent.</summary>
public sealed class Contract
{
    public required PromotionId Promotion { get; init; }
    public required DateOnly SignedOn { get; init; }
    public ContractType Type { get; set; }

    public Money PursePerFight { get; set; }             // guaranteed show money
    public Money WinBonus { get; set; }                  // extra paid on a win

    /// <summary>Bouts left on the deal; at zero the fighter hits free agency.</summary>
    public int FightsRemaining { get; set; }

    /// <summary>Exclusive => can't fight elsewhere; false => co-promotional / non-exclusive.</summary>
    public bool IsExclusive { get; set; } = true;

    /// <summary>If set, a title win auto-extends the deal (champion's clause).</summary>
    public bool HasChampionsClause { get; set; }

    // --- Deep deal levers ---------------------------------------------------
    /// <summary>Up-front lump sum paid from the promotion's balance when the deal is signed.</summary>
    public Money SigningBonus { get; set; }

    /// <summary>Share of PPV buys, in "points". 0 => no PPV cut.</summary>
    public int PpvPoints { get; set; }

    /// <summary>Discretionary Fight/Finish-of-the-Night money, paid on the night.</summary>
    public Money PerformanceBonus { get; set; }

    /// <summary>
    /// Whether the remaining purses are guaranteed. When true, releasing the
    /// fighter mid-deal owes <see cref="PursePerFight"/> × <see cref="FightsRemaining"/>.
    /// </summary>
    public bool Guaranteed { get; set; } = true;

    /// <summary>
    /// The owner has been warned a bigger org is circling this deal, so the
    /// warning fires once per contract. Persisted, so a load does not repeat it.
    /// </summary>
    public bool PoachWarned { get; set; }
}

public sealed class Injury
{
    public required InjurySeverity Severity { get; init; }
    public required string Description { get; init; }     // e.g. "torn ACL", "broken hand"
    public required DateOnly OccurredOn { get; init; }
    public required DateOnly ExpectedReturn { get; init; }
}
