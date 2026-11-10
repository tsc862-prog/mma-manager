using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Booking;

/// <summary>
/// Facade for the cross-org and pound-for-pound rankings. The real work lives in
/// <see cref="RankingService.RecomputeGlobal"/>, which scores fighters on the same
/// earned-in-the-cage résumé the per-promotion rankings use. Derived and
/// deterministic, so it's recomputed (new game, monthly, on load) rather than saved.
/// </summary>
public static class GlobalRankings
{
    public static void Recompute(World world) => RankingService.RecomputeGlobal(world);
}
