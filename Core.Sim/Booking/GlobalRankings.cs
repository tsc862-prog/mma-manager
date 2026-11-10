using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Booking;

/// <summary>
/// Facade for the cross-org and pound-for-pound rankings. The real work lives in
/// <see cref="RankingService.RecomputeGlobal"/>, which scores fighters on the same
/// earned-in-the-cage résumé the per-promotion rankings use. Derived and
/// deterministic, and recomputed at new game and month end. It is also saved, because the sim
/// reads it between recomputes; do not recompute on load (that replaces the saved lists).
/// </summary>
public static class GlobalRankings
{
    public static void Recompute(World world) => RankingService.RecomputeGlobal(world);
}
