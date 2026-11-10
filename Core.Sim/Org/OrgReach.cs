using MmaManager.Core.Model;
using MmaManager.Core.Sim.Geography;

namespace MmaManager.Core.Sim.Org;

/// <summary>
/// Where a promotion is allowed to stage cards — its reach tier walked up the
/// real geography. A City outfit fills halls in its HQ metro; Regional opens
/// the HQ's in-country area (the West, Southeast Brazil); National the whole
/// country; Continental the home continent; International the world. Growing
/// the brand literally grows the map.
/// </summary>
public static class OrgReach
{
    /// <summary>Can this promotion stage a card in this city at its current reach?</summary>
    public static bool CanBookIn(World world, Promotion promo, CityId cityId)
    {
        if (world.Get(cityId) is not { } city) return false;
        var size = OrgSizing.SizeOf(promo);
        if (size == OrgSize.International) return true;
        if (city.Region != promo.BaseRegion) return false;
        if (size == OrgSize.Continental) return true;

        // Below continental everything hangs off the HQ; without one (shouldn't
        // happen — every promotion gets an anchor-metro HQ) fall back to open.
        if (Home(world, promo) is not { } home) return true;
        return size switch
        {
            OrgSize.National => city.Country == home.Country,
            OrgSize.Regional => city.Country == home.Country
                                && (home.Area is null || city.Area == home.Area),
            _ => city.Id == home.Id,   // City tier: the HQ metro only
        };
    }

    /// <summary>Every city the promotion can currently book, HQ first, then by market.</summary>
    public static IReadOnlyList<City> Bookable(World world, Promotion promo) =>
        world.Cities.Values
            .Where(c => CanBookIn(world, promo, c.Id))
            .OrderBy(c => c.Id == promo.HomeCity ? 0 : 1)
            .ThenByDescending(c => c.Market)
            .ThenBy(c => c.Id.Value)
            .ToList();

    /// <summary>
    /// One line for the booking screen: what the current reach covers, and what
    /// the next tier opens ("Reach: United States · North America at 45 prestige").
    /// </summary>
    public static string Describe(World world, Promotion promo)
    {
        var size = OrgSizing.SizeOf(promo);
        string now = Territory(world, promo, size);
        if (size == OrgSize.International) return $"Reach: {now}";

        var next = size + 1;
        return $"Reach: {now}  ·  {Territory(world, promo, next)} at {OrgSizing.PrestigeFor(next)} prestige";
    }

    private static string Territory(World world, Promotion promo, OrgSize size)
    {
        var home = Home(world, promo);
        var country = home is not null ? world.Get(home.Country) : null;
        return size switch
        {
            OrgSize.International => "worldwide",
            OrgSize.Continental => Pretty(promo.BaseRegion),
            OrgSize.National => country?.Name ?? Pretty(promo.BaseRegion),
            OrgSize.Regional => home?.Area is { } a && country is not null && a < country.Areas.Count
                ? $"{country.Areas[a]} ({country.Code})"
                : country?.Name ?? Pretty(promo.BaseRegion),
            _ => home?.Name ?? "home city",
        };
    }

    private static City? Home(World world, Promotion promo) =>
        promo.HomeCity is { } hq ? world.Get(hq) : WorldMap.AnchorCity(world, promo.BaseRegion);

    private static string Pretty(Region r) => r switch
    {
        Region.NorthAmerica => "North America",
        Region.SouthAmerica => "South America",
        Region.MiddleEast => "the Middle East",
        _ => r.ToString(),
    };
}
