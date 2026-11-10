using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Generation;

/// <summary>
/// Optional constraints on a generated fighter. Anything left null is rolled
/// from the seeded RNG. Use it to pin, say, a heavyweight wrestler prospect
/// while letting everything else vary.
/// </summary>
public sealed class GenOptions
{
    public WeightClass? WeightClass { get; init; }
    public PrimaryStyle? Style { get; init; }
    public Region? Nationality { get; init; }

    /// <summary>Force the sex (else male). Women's divisions pin this to Female.</summary>
    public Sex? Sex { get; init; }

    /// <summary>Force the name culture (atlas key, e.g. "japanese"). Else derived from the country.</summary>
    public string? Culture { get; init; }

    /// <summary>
    /// Force the country (atlas code, e.g. "GEO"). Else rolled from the atlas's
    /// talent weights within the fighter's continent; the name follows the country.
    /// </summary>
    public string? CountryCode { get; init; }

    /// <summary>Force an age (else drawn from a realistic curve, ~19–41).</summary>
    public int? Age { get; init; }

    /// <summary>
    /// Force the overall skill level (~mean of ratings, 0–100). Else drawn from
    /// a distribution skewed toward regional/mid-tier talent. Pin high for a
    /// champion, low for a debutant.
    /// </summary>
    public int? Overall { get; init; }

    /// <summary>
    /// Roll a backstory record (the default). The initial world build turns
    /// those tallies into REAL back-dated careers (HistoryBuilder), so it's
    /// only right for fighters who exist before day one. Set false for anyone
    /// whose story starts NOW — a newgen turning pro debuts 0-0; a record
    /// with no fights behind it is a lie the profile can't cash.
    /// </summary>
    public bool WithRecord { get; init; } = true;

    /// <summary>
    /// Cap the rolled backstory at this many bouts (else career length follows
    /// from age). Use for deliberately green talent — a founding-pool prospect
    /// or unsigned intake kid whose story is still ahead of them.
    /// </summary>
    public int? MaxBouts { get; init; }

    public static readonly GenOptions Default = new();
}
