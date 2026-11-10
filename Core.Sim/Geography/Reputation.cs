using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Geography;

/// <summary>
/// The one place city-level fame is read and written. Cities are the stored
/// truth (<see cref="Fighter.CityReputation"/>, <see cref="Promotion.PopularityByCity"/>);
/// region numbers are always DERIVED here as the market-weighted average of the
/// region's cities, so the two levels can never drift apart. Writes land on the
/// host city in full and echo faintly around its region (word travels).
/// </summary>
public static class Reputation
{
    // How loudly a city result echoes around the rest of its region.
    private const int EchoDivisor = 3;

    // --- Fighters -------------------------------------------------------------

    public static int FighterInCity(Fighter f, CityId city) =>
        f.CityReputation.GetValueOrDefault(city).Value;

    /// <summary>Region fame = market-weighted average of the region's cities.</summary>
    public static int FighterInRegion(World world, Fighter f, Region region)
    {
        int weighted = 0, markets = 0;
        foreach (var c in world.Cities.Values)
        {
            if (c.Region != region) continue;
            weighted += f.CityReputation.GetValueOrDefault(c.Id).Value * c.Market;
            markets += c.Market;
        }
        return markets == 0 ? 0 : weighted / markets;
    }

    /// <summary>Country fame = market-weighted average of the country's cities (0 if it has no fight scene).</summary>
    public static int FighterInCountry(World world, Fighter f, CountryId country)
    {
        int weighted = 0, markets = 0;
        foreach (var c in world.Cities.Values)
        {
            if (c.Country != country) continue;
            weighted += f.CityReputation.GetValueOrDefault(c.Id).Value * c.Market;
            markets += c.Market;
        }
        return markets == 0 ? 0 : weighted / markets;
    }

    /// <summary>The fighter's best regional fame anywhere in the world (their headline "fame" number).</summary>
    public static int FighterBest(World world, Fighter f) =>
        Enum.GetValues<Region>().Max(r => FighterInRegion(world, f, r));

    /// <summary>A result in a city: full credit there, a faint echo around the region.</summary>
    public static void BumpFighter(World world, Fighter f, CityId city, int amount)
    {
        Add(f.CityReputation, city, amount);
        if (world.Get(city) is not { } host || amount / EchoDivisor == 0) return;
        foreach (var c in world.Cities.Values)
            if (c.Region == host.Region && c.Id != city)
                Add(f.CityReputation, c.Id, amount / EchoDivisor);
    }

    /// <summary>Region-wide news (a televised title win, an Olympic medal) lifts every city in the region.</summary>
    public static void BumpFighterRegion(World world, Fighter f, Region region, int amount)
    {
        foreach (var c in world.Cities.Values)
            if (c.Region == region)
                Add(f.CityReputation, c.Id, amount);
    }

    // --- Promotions -------------------------------------------------------------

    public static int OrgInCity(Promotion p, CityId city) =>
        p.PopularityByCity.GetValueOrDefault(city).Value;

    public static int OrgInRegion(World world, Promotion p, Region region)
    {
        int weighted = 0, markets = 0;
        foreach (var c in world.Cities.Values)
        {
            if (c.Region != region) continue;
            weighted += p.PopularityByCity.GetValueOrDefault(c.Id).Value * c.Market;
            markets += c.Market;
        }
        return markets == 0 ? 0 : weighted / markets;
    }

    /// <summary>Country popularity = market-weighted average of the country's cities (0 if it has no fight scene).</summary>
    public static int OrgInCountry(World world, Promotion p, CountryId country)
    {
        int weighted = 0, markets = 0;
        foreach (var c in world.Cities.Values)
        {
            if (c.Country != country) continue;
            weighted += p.PopularityByCity.GetValueOrDefault(c.Id).Value * c.Market;
            markets += c.Market;
        }
        return markets == 0 ? 0 : weighted / markets;
    }

    /// <summary>
    /// Running a card in a city grows the brand there; a big brand's shows echo
    /// around the region too (they make television).
    /// </summary>
    public static void BumpOrg(World world, Promotion p, CityId city, int amount)
    {
        Add(p.PopularityByCity, city, amount);
        if (world.Get(city) is not { } host) return;
        int echo = p.Prestige.Value >= 45 ? Math.Max(1, amount / EchoDivisor) : amount / EchoDivisor;
        if (echo == 0) return;
        foreach (var c in world.Cities.Values)
            if (c.Region == host.Region && c.Id != city)
                Add(p.PopularityByCity, c.Id, echo);
    }

    /// <summary>
    /// Fade a promotion's fame a touch in every city it's known — the price of
    /// going dark. A brand that stops putting on shows slips out of the
    /// conversation; running cards regularly is what keeps it front of mind.
    /// </summary>
    public static void DecayOrg(Promotion p, int amount)
    {
        foreach (var city in p.PopularityByCity.Keys.ToList())
            p.PopularityByCity[city] = Math.Max(0, p.PopularityByCity[city].Value - amount);
    }

    private static void Add(Dictionary<CityId, Rating> map, CityId city, int amount) =>
        map[city] = Math.Clamp(map.GetValueOrDefault(city).Value + amount, 0, 100);
}
