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
/// SAVE-STABLE: the names of these members, the <see cref="Core.Model.WeightClass"/>
/// member names used as weight-class keys, and the atlas country codes used as scene
/// keys are all written into saves. Renaming any of them makes old saves refuse to
/// load (the reader rejects an unknown id rather than silently re-locking it).
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
    /// <summary>The key, normalised however it was given: empty is no key, country codes are upper case.</summary>
    public string? Key { get; } = Normalise(Kind, Key);

    private static string? Normalise(CapabilityKind kind, string? key) =>
        string.IsNullOrEmpty(key) ? null
        : kind == CapabilityKind.CountryScene ? key.ToUpperInvariant()
        : key;

    /// <summary>The stable saved form: the kind's name, then ":key" for the keyed kinds.</summary>
    public string Id => string.IsNullOrEmpty(Key) ? Kind.ToString() : $"{Kind}:{Key}";

    public override string ToString() => Id;

    public static Capability Class(WeightClass wc) => new(CapabilityKind.WeightClass, wc.ToString());
    public static Capability Scene(string countryCode) => new(CapabilityKind.CountryScene, countryCode);

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

    /// <summary>
    /// Read a saved <see cref="Id"/> back; false for a kind this build doesn't know,
    /// a keyed kind without its key (or a plain kind with one), or a weight-class key
    /// that isn't a <see cref="Core.Model.WeightClass"/> member name.
    /// </summary>
    public static bool TryParse(string id, out Capability capability)
    {
        capability = default;
        int colon = id.IndexOf(':');
        string kind = colon < 0 ? id : id[..colon];
        string? key = colon < 0 ? null : id[(colon + 1)..];
        if (kind.Length == 0 || !char.IsLetter(kind[0])
            || !Enum.TryParse<CapabilityKind>(kind, ignoreCase: false, out var k) || !Enum.IsDefined(k))
            return false;
        bool keyed = k is CapabilityKind.WeightClass or CapabilityKind.CountryScene;
        if (keyed != !string.IsNullOrEmpty(key)) return false;
        if (k == CapabilityKind.WeightClass
            && (!Enum.TryParse<WeightClass>(key, ignoreCase: false, out var wc) || !Enum.IsDefined(wc) || wc.ToString() != key))
            return false;
        capability = new Capability(k, key);
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

/// <summary>Which of a chapter's three narrations was told: somebody else did it, the player did, or it simply happened.</summary>
public enum ChapterVariant { World, Player, Rival }

/// <summary>
/// A chapter of the sport's story that has happened: when, and (when somebody
/// made it happen) who. The key names a row of the chapter table (Origins
/// phase 2); <see cref="Note"/> is a small free payload for what the chapter
/// needs to remember (a weight class, a country code). <see cref="Variant"/>,
/// <see cref="City"/> and <see cref="Partner"/> are fixed when it fires, so the
/// story reads the same however long after it is told.
/// </summary>
public sealed class ChapterRecord
{
    public string Key { get; init; } = "";
    public DateOnly FiredOn { get; init; }
    public PromotionId? Promotion { get; init; }
    public FighterId? Fighter { get; init; }
    public string? Note { get; init; }

    /// <summary>Which narration the story tells (saved by name).</summary>
    public ChapterVariant Variant { get; init; }

    /// <summary>Where it happened, when it happened somewhere (the night abroad, the licensed hall).</summary>
    public CityId? City { get; init; }

    /// <summary>The sponsor or broadcaster the chapter names, when it names one.</summary>
    public string? Partner { get; init; }
}

/// <summary>
/// The Backlash closing a region's halls to the sport (Origins): every card in that country's area
/// (or the whole country when <see cref="Area"/> is null) is refused from <see cref="Since"/> until the
/// ban lapses on <see cref="Until"/> or a commission lifts it (<see cref="LiftedOn"/>).
/// </summary>
public sealed class SceneBan
{
    public string Country { get; init; } = "";
    public int? Area { get; init; }
    public DateOnly Since { get; init; }
    public DateOnly Until { get; init; }
    public DateOnly? LiftedOn { get; set; }

    public bool ActiveOn(DateOnly date) => date >= Since && date < Until && (LiftedOn is null || date < LiftedOn);
}

/// <summary>
/// A commission's licence for a region (Origins): the region's halls are open to sanctioned cards from
/// <see cref="On"/>. <see cref="Promotion"/> is the house whose application won it (null when the
/// commission moved on its own).
/// </summary>
public sealed class Sanction
{
    public string Country { get; init; } = "";
    public int? Area { get; init; }
    public DateOnly On { get; init; }
    public PromotionId? Promotion { get; init; }
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

    /// <summary>Regions the Backlash closed to the sport, in the order they were closed (live and lapsed alike).</summary>
    public List<SceneBan> Bans { get; init; } = new();

    /// <summary>Regions a commission has licensed, in the order they were licensed.</summary>
    public List<Sanction> Sanctions { get; init; } = new();

    public bool HasFired(string chapterKey) => Chapters.Any(c => c.Key == chapterKey);
    public ChapterRecord? Chapter(string chapterKey) => Chapters.FirstOrDefault(c => c.Key == chapterKey);
    public bool IsUnlocked(Capability capability) => Unlocked.Contains(capability.Id);
}
