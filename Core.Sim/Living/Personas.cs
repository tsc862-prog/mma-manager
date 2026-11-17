using MmaManager.Core.Model;
using MmaManager.Core.Sim.Generation;

namespace MmaManager.Core.Sim.Living;

/// <summary>
/// Stamps <see cref="Fighter.PersonaTag"/> (the marketable look of the fighter's portrait) inside the sim, from
/// the art catalog the world carries (<see cref="World.ArtCatalog"/>, <see cref="IdentityCatalog.LookOf"/>).
/// <para>
/// Once per fighter, when he is created: the world builders stamp everyone at the end of the build (so the
/// back-dated history runs without personas, as it always has), and every mid-game creation path (newgens,
/// Origins arrivals, founding tryouts, local opponents, crossovers) stamps the man it makes before handing
/// him back. The look is a pure function of identity fields that never change after creation (sex, country,
/// region, natural weight, style, the designed flag, the id), so stamping once is stamping for good, and the
/// world no longer depends on when the UI happens to refresh.
/// </para>
/// No catalog, or a catalog without a look (tests, longsim): nothing happens and nothing is drawn, so a
/// headless world runs exactly as before. A null look never clears a tag already set.
/// </summary>
public static class Personas
{
    public static void Stamp(World world, Fighter f)
    {
        if (world.ArtCatalog is IdentityCatalog { LookOf: { } look } && look(f, world) is { } tag)
            f.PersonaTag = tag;
    }

    /// <summary>Stamp every fighter in the world (the end of a build). Order is irrelevant: no draws, no shared state.</summary>
    public static void StampAll(World world)
    {
        if (world.ArtCatalog is not IdentityCatalog { LookOf: not null }) return;
        foreach (var f in world.Fighters.Values.OrderBy(f => f.Id.Value))
            Stamp(world, f);
    }
}
