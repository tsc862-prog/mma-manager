using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Org;

/// <summary>
/// The market's answer to a crowded tier: DILUTION. TV hours, big-arena dates
/// and fan wallets at each level of the sport are finite, so when more orgs
/// operate in a band than the market comfortably carries, everyone's slice
/// thins — gates soften and rights fees shave. This is where pyramid-shaping
/// lives now: the promotion-ladder gate only stops floods (v39), and it's the
/// ECONOMY that decides how many big orgs the world can really sustain. The
/// weakest of an over-full band bleed first, decline, and become the org
/// acquisition targets consolidation feeds on — the market slims itself the
/// way real fight business does, through buyouts and busts, not queues.
/// Gentle by design: the drag caps at 20% so a crowded band is a headwind,
/// never a cliff — and it applies to EVERYONE in the band, player included
/// (the market doesn't care whose name is on the door).
/// </summary>
public static class MarketShare
{
    // How many orgs each band comfortably carries — absolute, NOT scaled by
    // world growth: the number of Continental-grade TV slots doesn't double
    // because more promotions exist to want them. Sized just above the healthy
    // 30-yr shape so the drag only bites genuine over-crowding.
    private static int Comfort(OrgSize band) => band switch
    {
        OrgSize.International => 3,
        OrgSize.Continental => 12,
        OrgSize.National => 14,
        OrgSize.Regional => 12,
        _ => int.MaxValue,   // the City scene is bottomless — nobody's fighting over club-hall money
    };

    private const double DragPerExcess = 0.03;
    private const double MaxDrag = 0.20;

    /// <summary>
    /// The revenue multiplier this org's market position earns (1.0 = full
    /// slice, 0.8 = the deepest dilution). Applied to gates and media rights.
    /// </summary>
    public static double Factor(World world, Promotion promo)
    {
        var band = OrgSizing.SizeOf(promo);
        int comfort = Comfort(band);
        if (comfort == int.MaxValue) return 1.0;
        int operators = 0;
        foreach (var p in world.Promotions.Values)
            if (!p.Defunct && OrgSizing.SizeOf(p) == band) operators++;
        int excess = Math.Max(0, operators - comfort);
        return Math.Max(1.0 - MaxDrag, 1.0 - DragPerExcess * excess);
    }
}
