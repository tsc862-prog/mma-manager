using MmaManager.Core.Model;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Generation;

/// <summary>
/// One portrait identity distilled from the art pack: enough to spawn a fighter
/// who actually HAS that portrait. Sex and weight band are hard constraints; a
/// null <see cref="Style"/> is a wildcard (fits any archetype). <see cref="Triptych"/>
/// marks the identities rendered at all three life stages — reserved for the
/// fighters worth watching age.
/// </summary>
public sealed record IdentitySpec(
    Sex Sex, string Culture, int WeightLo, int WeightHi, PrimaryStyle? Style, bool Triptych);

/// <summary>
/// Portrait-first generation: instead of minting a fighter and hoping the art
/// layer can match them, the generator draws a real identity from the pack and
/// builds the fighter to fit it — the fighter's culture (name + nationality) and
/// style come from the identity, so every generated fighter has an exact portrait.
/// Weight and sex are chosen by the caller (to fill a division); the catalog
/// finds an identity that fits. Supplied by the UI (which owns the manifest);
/// absent in headless runs, where generation stays purely procedural.
/// </summary>
public sealed class IdentityCatalog
{
    private readonly IReadOnlyList<IdentitySpec> _all;

    public IdentityCatalog(IReadOnlyList<IdentitySpec> identities) => _all = identities;

    public int Count => _all.Count;

    /// <summary>
    /// An identity for a fighter of this sex and weight class — a hard match on
    /// sex and an overlapping weight band. High-potential fighters prefer a
    /// three-stage (triptych) identity so the world's stars visibly age. Among
    /// what fits, the most weight-SPECIFIC art is preferred (a heavyweight takes
    /// a 206-265 identity over a generic 171-and-up one) — but never at the cost
    /// of shrinking the choice pool below <see cref="MinPool"/>, so a division
    /// with only a couple of precise images doesn't collapse onto them. Returns
    /// null if nothing fits (the generator then rolls procedurally).
    /// </summary>
    public IdentitySpec? Pick(SimRandom rng, Sex sex, WeightClass wc, bool preferTriptych)
    {
        var (lo, hi) = NaturalBand(wc);
        var pool = _all.Where(i => i.Sex == sex && i.WeightHi >= lo && i.WeightLo <= hi).ToList();
        if (pool.Count == 0) return null;

        if (preferTriptych)
        {
            var trips = pool.Where(i => i.Triptych).ToList();
            if (trips.Count > 0) pool = trips;
        }

        pool = PreferSpecific(pool);
        return pool[rng.Range(0, pool.Count - 1)];
    }

    // Width buckets: class-band art (a single division), half-range art
    // (lighter/heavier half of the sport), everything else. Mirrored in the
    // WPF art libraries — keep the values in step.
    private const int SpecificWidth = 40;
    private const int HalfRangeWidth = 80;
    private const int MinPool = 6;

    // The narrowest width bucket that still offers a real choice of faces.
    private static List<IdentitySpec> PreferSpecific(List<IdentitySpec> pool)
    {
        foreach (int cap in new[] { SpecificWidth, HalfRangeWidth })
        {
            var tight = pool.Where(i => i.WeightHi - i.WeightLo <= cap).ToList();
            if (tight.Count >= MinPool) return tight;
        }
        return pool;
    }

    // Mirror of FighterGenerator's natural-weight bands (kept local so the
    // catalog stands alone). Overlap with an identity's band = a plausible fit.
    private static (int Lo, int Hi) NaturalBand(WeightClass wc) => wc switch
    {
        WeightClass.Strawweight => (106, 115),
        WeightClass.Flyweight => (116, 125),
        WeightClass.Bantamweight => (126, 135),
        WeightClass.Featherweight => (136, 145),
        WeightClass.Lightweight => (146, 155),
        WeightClass.Welterweight => (156, 170),
        WeightClass.Middleweight => (171, 185),
        WeightClass.LightHeavyweight => (186, 205),
        WeightClass.Heavyweight => (206, 265),
        _ => (146, 205),
    };
}
