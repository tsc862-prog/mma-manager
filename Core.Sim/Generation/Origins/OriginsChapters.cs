using MmaManager.Core.Model;
using MmaManager.Core.Sim.Geography;

namespace MmaManager.Core.Sim.Generation.Origins;

/// <summary>How a chapter is fired. A structured hint for the later timeline engine; the plain-words
/// <see cref="OriginsChapter.Trigger"/> is the authoritative description.</summary>
public enum OriginsTriggerKind
{
    /// <summary>Fires inside its window by the calendar alone (the Backlash always comes).</summary>
    Dated,
    /// <summary>Fires for whichever promotion first does the thing; the narration variant depends on who it was.</summary>
    FirstPromotionTo,
    /// <summary>Fires once the world reaches a state (counts, tiers, fame).</summary>
    WorldCondition,
}

/// <summary>
/// A small structured trigger: a kind, a condition key ("Name" or "Name:arg") from
/// <see cref="OriginsChapters.KnownConditions"/>, and an optional number. Every chapter also fires at its
/// LatestMonth whether or not the condition was met ("shaped" history: the order is mostly fixed, the
/// timing and the doer depend on the world).
/// </summary>
public sealed record OriginsTrigger(OriginsTriggerKind Kind, string Condition, int Value = 0);

/// <summary>A headline and a body. Slots: {Org} {Founder} {City} {Country} {Fighter} {Year} {Partner}.</summary>
public sealed record OriginsNarration(string Headline, string Body);

/// <summary>
/// One chapter of the Origins timeline. <see cref="Key"/> is saved and must never be renamed.
/// Months are offsets from 1 January 1993 (<see cref="OriginsPioneers.StartDate"/>); 12 months to a year.
/// A variant is null when it makes no sense (explained in <see cref="Note"/>); <see cref="ByWorld"/> is
/// always present and is the fallback.
/// </summary>
public sealed record OriginsChapter(
    string Key, int Era, string Title, int EarliestMonth, int LatestMonth, string[] Requires,
    string Trigger, OriginsTrigger Structured, string[] Unlocks, string[] Effects,
    OriginsNarration? ByPlayer, OriginsNarration? ByRival, OriginsNarration ByWorld,
    string Digest, string Note = "")
{
    public int EarliestYear => 1993 + EarliestMonth / 12;
    public int LatestYear => 1993 + LatestMonth / 12;
}

/// <summary>
/// The capability vocabulary a chapter can unlock, as "Kind" or "Kind:key" strings. A sibling branch
/// defines the matching enum; this class is the authored list the data and tests agree on.
/// </summary>
public static class OriginsCapabilities
{
    public static readonly string[] MediaKeys = { "InHouseStream", "SpecialtyCable", "CableSports", "PrimeBroadcast", "MajorStream", "PpvCarrier" };
    public static readonly string[] SponsorKeys = { "Local", "Supplement", "Equipment", "EnergyDrink", "Brewery", "National", "Global" };
    public static readonly string[] PlainKinds = { "WomensDivisions", "Rounds", "Judges", "Gloves", "NewPromotions", "Crossovers", "Superfights", "Acquisitions", "CoPromotion" };

    /// <summary>The kinds that carry a key.</summary>
    public static readonly string[] KeyedKinds = { "Media", "Sponsor", "WeightClass", "CountryScene" };

    /// <summary>Every capability except the country scenes (those are one per atlas country; see <see cref="OriginsChapters.SceneOrder"/>).</summary>
    public static IReadOnlyList<string> All { get; } =
        MediaKeys.Select(k => "Media:" + k)
            .Concat(SponsorKeys.Select(k => "Sponsor:" + k))
            .Concat(Enum.GetNames<WeightClass>().Select(k => "WeightClass:" + k))
            .Concat(PlainKinds)
            .ToArray();

    /// <summary>True when the string is a well-formed, known capability (country keys are checked against the atlas).</summary>
    public static bool IsValid(string cap)
    {
        var parts = cap.Split(':');
        if (parts.Length == 1) return PlainKinds.Contains(cap);
        if (parts.Length != 2) return false;
        return parts[0] switch
        {
            "Media" => MediaKeys.Contains(parts[1]),
            "Sponsor" => SponsorKeys.Contains(parts[1]),
            "WeightClass" => Enum.GetNames<WeightClass>().Contains(parts[1]),
            "CountryScene" => WorldAtlas.ByCode(parts[1]) is not null,
            _ => false,
        };
    }
}

/// <summary>
/// The Origins timeline: the narrated chapters through which the sport grows from 1993 to a modern world.
/// Authored ahead of its engine; nothing reads this yet. Append-only: keys are saved, so never rename or
/// reorder. Everyone and every company is invented; geography is real. The table is split by era across
/// partial files purely for readability.
/// </summary>
public static partial class OriginsChapters
{
    /// <summary>The condition keys a structured trigger may use (the part before any ':').</summary>
    public static readonly string[] KnownConditions =
    {
        "Date",
        // FirstPromotionTo
        "SignMedia", "SignSponsor", "HoldEventInNewCountry", "CrownChampionInClass", "RunWomensBout", "CoPromote",
        "AdoptRules", "SanctionedByCommission", "AcquireRival", "HoldEventAbove", "ChampionVsChampion",
        // WorldCondition
        "PromotionsAtLeast", "PromotionReachesTier", "FighterFameAtLeast", "UpsetOfStar", "AllRoundersBeatSpecialists",
        "CampReputationAtLeast", "SanctionedRegionsAtLeast", "WomenBoutsAtLeast", "BothFightersFameAtLeast",
        "AcquisitionsAtLeast", "ContinentsWithPromotionsAtLeast", "PromotionsOnTheMapAtLeast", "WomensStarFameAtLeast",
        "WomensDivisionBoutsAtLeast", "ChaptersFired",
    };

    /// <summary>All chapters, in authoring order (not chronological: use the windows).</summary>
    public static IReadOnlyList<OriginsChapter> All { get; } = Build();

    public static OriginsChapter ByKey(string key) => All.Single(c => c.Key == key);

    /// <summary>The wake-up order of country scenes: (chapter key, country code), chronologically.</summary>
    public static IReadOnlyList<(string Chapter, string Country)> SceneOrder { get; } =
        All.OrderBy(c => c.EarliestMonth)
            .SelectMany(c => c.Unlocks.Where(u => u.StartsWith("CountryScene:")).Select(u => (c.Key, u.Substring("CountryScene:".Length))))
            .ToArray();

    private static IReadOnlyList<OriginsChapter> Build()
    {
        var all = new List<OriginsChapter>();
        all.AddRange(Era1());
        all.AddRange(Era2());
        all.AddRange(Era3());
        all.AddRange(Era4());
        all.AddRange(Era5());
        all.AddRange(Era6());
        return all;
    }

    // ------------------------------------------------------------------ authoring helpers
    private static OriginsNarration N(string headline, string body) => new(headline, body);
    private static string[] A(params string[] s) => s;
    private static OriginsTrigger Dated() => new(OriginsTriggerKind.Dated, "Date");
    private static OriginsTrigger First(string condition, int value = 0) => new(OriginsTriggerKind.FirstPromotionTo, condition, value);
    private static OriginsTrigger World(string condition, int value = 0) => new(OriginsTriggerKind.WorldCondition, condition, value);

    private static OriginsChapter C(string key, int era, string title, int earliest, int latest, string[] requires,
        string trigger, OriginsTrigger structured, string[] unlocks, string[] effects,
        OriginsNarration? player, OriginsNarration? rival, OriginsNarration world, string digest, string note = "") =>
        new(key, era, title, earliest, latest, requires, trigger, structured, unlocks, effects, player, rival, world, digest, note);
}
