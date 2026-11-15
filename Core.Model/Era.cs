namespace MmaManager.Core.Model;

/// <summary>
/// How the world began. <see cref="Modern"/> is the mature sport the game has
/// always built (every system available from day one); <see cref="Origins"/>
/// starts at the birth of the sport and unlocks the modern machinery chapter by
/// chapter (see <see cref="EraState"/> and the sim's <c>Living.Era</c> gate).
/// </summary>
public enum WorldMode { Modern, Origins }

/// <summary>
/// The families of things an era can hold back. Most are switches on their own;
/// <see cref="WeightClass"/> and <see cref="CountryScene"/> carry a key (the class
/// name, the country's atlas code) in <see cref="Capability.Key"/>.
/// Saved by NAME (<see cref="Capability.Id"/>): never rename a member.
/// </summary>
public enum CapabilityKind
{
    // The media ladder, one per rung (MediaKind).
    InHouseStream, SpecialtyCable, CableSports, PrimeBroadcast, MajorStream, PpvCarrier,

    // Sponsor archetypes (Finance.Sponsorships), smallest to biggest.
    SponsorLocal, SponsorSupplement, SponsorEquipment, SponsorEnergyDrink, SponsorBrewery, SponsorNational, SponsorGlobal,

    // Divisions: women's MMA, and each weight class split out of open weight (keyed by class).
    WomensDivisions, WeightClass,

    // Rule features (read by later phases; nothing consults them yet).
    Rounds, Judges, Gloves,

    // The living world's machinery.
    NewPromotions, Crossovers, Superfights, Acquisitions, CoPromotion,

    // A national scene that hosts shows and grows talent (keyed by country code).
    CountryScene,
}

/// <summary>
/// One thing an era can allow: a kind, plus a key for the keyed kinds
/// (<c>WeightClass:Lightweight</c>, <c>CountryScene:BRA</c>). Its <see cref="Id"/>
/// is the saved form.
/// </summary>
public readonly record struct Capability(CapabilityKind Kind, string? Key = null)
{
    /// <summary>The stable saved form: the kind's name, then ":key" for the keyed kinds.</summary>
    public string Id => string.IsNullOrEmpty(Key) ? Kind.ToString() : $"{Kind}:{Key}";

    public override string ToString() => Id;

    public static Capability Class(WeightClass wc) => new(CapabilityKind.WeightClass, wc.ToString());
    public static Capability Scene(string countryCode) => new(CapabilityKind.CountryScene, countryCode.ToUpperInvariant());

    public static Capability Media(MediaKind kind) => kind switch
    {
        MediaKind.InHouseStream => InHouseStream,
        MediaKind.SpecialtyCable => SpecialtyCable,
        MediaKind.CableSports => CableSports,
        MediaKind.PrimeBroadcast => PrimeBroadcast,
        MediaKind.MajorStream => MajorStream,
        MediaKind.PpvCarrier => PpvCarrier,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
    };

    /// <summary>Read a saved <see cref="Id"/> back; false for a name this build doesn't know.</summary>
    public static bool TryParse(string id, out Capability capability)
    {
        int colon = id.IndexOf(':');
        string kind = colon < 0 ? id : id[..colon];
        if (!Enum.TryParse<CapabilityKind>(kind, ignoreCase: false, out var k) || !Enum.IsDefined(k))
        {
            capability = default;
            return false;
        }
        capability = new Capability(k, colon < 0 ? null : id[(colon + 1)..]);
        return true;
    }

    public static readonly Capability InHouseStream = new(CapabilityKind.InHouseStream);
    public static readonly Capability SpecialtyCable = new(CapabilityKind.SpecialtyCable);
    public static readonly Capability CableSports = new(CapabilityKind.CableSports);
    public static readonly Capability PrimeBroadcast = new(CapabilityKind.PrimeBroadcast);
    public static readonly Capability MajorStream = new(CapabilityKind.MajorStream);
    public static readonly Capability PpvCarrier = new(CapabilityKind.PpvCarrier);

    public static readonly Capability SponsorLocal = new(CapabilityKind.SponsorLocal);
    public static readonly Capability SponsorSupplement = new(CapabilityKind.SponsorSupplement);
    public static readonly Capability SponsorEquipment = new(CapabilityKind.SponsorEquipment);
    public static readonly Capability SponsorEnergyDrink = new(CapabilityKind.SponsorEnergyDrink);
    public static readonly Capability SponsorBrewery = new(CapabilityKind.SponsorBrewery);
    public static readonly Capability SponsorNational = new(CapabilityKind.SponsorNational);
    public static readonly Capability SponsorGlobal = new(CapabilityKind.SponsorGlobal);

    public static readonly Capability WomensDivisions = new(CapabilityKind.WomensDivisions);

    public static readonly Capability Rounds = new(CapabilityKind.Rounds);
    public static readonly Capability Judges = new(CapabilityKind.Judges);
    public static readonly Capability Gloves = new(CapabilityKind.Gloves);

    public static readonly Capability NewPromotions = new(CapabilityKind.NewPromotions);
    public static readonly Capability Crossovers = new(CapabilityKind.Crossovers);
    public static readonly Capability Superfights = new(CapabilityKind.Superfights);
    public static readonly Capability Acquisitions = new(CapabilityKind.Acquisitions);
    public static readonly Capability CoPromotion = new(CapabilityKind.CoPromotion);
}

/// <summary>
/// A chapter of the sport's story that has happened: when, and (when somebody
/// made it happen) who. The key names a row of the chapter table (Origins
/// phase 2); <see cref="Note"/> is a small free payload for what the chapter
/// needs to remember (a weight class, a country code).
/// </summary>
public sealed class ChapterRecord
{
    public string Key { get; init; } = "";
    public DateOnly FiredOn { get; init; }
    public PromotionId? Promotion { get; init; }
    public FighterId? Fighter { get; init; }
    public string? Note { get; init; }
}

/// <summary>
/// Where the sport is in its own history: the chapters that have fired (in the
/// order they fired) and the capabilities they have unlocked. Empty, and never
/// read, in a <see cref="WorldMode.Modern"/> world — everything is available
/// there from day one.
/// </summary>
public sealed class EraState
{
    /// <summary>Fired chapters, oldest first.</summary>
    public List<ChapterRecord> Chapters { get; init; } = new();

    /// <summary>Unlocked capabilities by <see cref="Capability.Id"/>, ordinal order (deterministic iteration).</summary>
    public SortedSet<string> Unlocked { get; init; } = new(StringComparer.Ordinal);

    public bool HasFired(string chapterKey) => Chapters.Any(c => c.Key == chapterKey);
    public ChapterRecord? Chapter(string chapterKey) => Chapters.FirstOrDefault(c => c.Key == chapterKey);
    public bool IsUnlocked(Capability capability) => Unlocked.Contains(capability.Id);
}
