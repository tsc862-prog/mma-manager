using MmaManager.Core.Model;
using MmaManager.Core.Sim.Org;

namespace MmaManager.Core.Sim.Geography;

/// <summary>
/// How much a place cares about MMA. Cities and countries carry their own interest
/// (set at world creation, mostly static); region interest is DERIVED here as the
/// market-weighted average of the region's cities, so the levels never drift. High
/// interest fills seats, grows a promotion's following faster, and — because a hot
/// market throws off more gate — lets more promotions coexist there. Rare events
/// (a crossover, a national hero) can nudge it; everything is clamped 0–100.
/// </summary>
public static class MmaInterest
{
    public static int InCity(World world, CityId city) =>
        world.Get(city)?.MmaInterest ?? DefaultInterest;

    public static int InCountry(World world, CountryId country) =>
        world.Get(country)?.MmaInterest ?? DefaultInterest;

    /// <summary>Region interest = market-weighted average of the region's cities.</summary>
    public static int InRegion(World world, Region region)
    {
        long weighted = 0, markets = 0;
        foreach (var c in world.Cities.Values)
        {
            if (c.Region != region) continue;
            weighted += (long)c.MmaInterest * Math.Max(1, c.Market);
            markets += Math.Max(1, c.Market);
        }
        return markets == 0 ? DefaultInterest : (int)(weighted / markets);
    }

    /// <summary>
    /// An interest reading turned into an economic BONUS multiplier (≥ 1.0): a
    /// fight-mad market fills more seats and grows a following faster, while an
    /// ordinary market just performs at the baseline (never penalised below it, so
    /// the tuned economy holds). Interest above <see cref="BonusPivot"/> earns the
    /// bonus; <paramref name="swing"/> sets how big it gets at max interest.
    /// </summary>
    public static double Factor(int interest, double swing) =>
        1.0 + Math.Max(0, interest - BonusPivot) / (100.0 - BonusPivot) * swing;

    private const int BonusPivot = 45;

    /// <summary>A moment that grows the sport in a region — lifts every city there a touch.</summary>
    public static void BumpRegion(World world, Region region, int amount)
    {
        foreach (var c in world.Cities.Values)
            if (c.Region == region)
                c.MmaInterest = Math.Clamp(c.MmaInterest + amount, 0, 100);
        foreach (var country in world.Countries.Values)
            if (country.Continent == region)
                country.MmaInterest = Math.Clamp(country.MmaInterest + amount, 0, 100);
    }

    /// <summary>A big show comes to town — nudges the host city's interest up.</summary>
    public static void BumpCity(World world, CityId city, int amount)
    {
        if (world.Get(city) is { } c)
            c.MmaInterest = Math.Clamp(c.MmaInterest + amount, 0, 100);
    }

    /// <summary>
    /// Monthly upkeep: interest DECAYS back toward each place's cultural baseline (so
    /// boosts fade and the map doesn't drift to all-max), then a NATIONAL HERO — a
    /// very popular fighter competing on a national+ stage — props up interest in
    /// their home region, up to a ceiling set by their fame. So a low-interest region
    /// rides high while it has a homegrown star (Pacquiao's Philippines, Ngannou's
    /// Cameroon) and slides back once the star fades.
    /// </summary>
    public static void Tick(World world)
    {
        foreach (var c in world.Cities.Values)
            if (c.MmaInterest > c.MmaInterestBase)
                c.MmaInterest = Math.Max(c.MmaInterestBase, c.MmaInterest - DecayPerMonth);
        foreach (var k in world.Countries.Values)
            if (k.MmaInterest > k.MmaInterestBase)
                k.MmaInterest = Math.Max(k.MmaInterestBase, k.MmaInterest - DecayPerMonth);

        // A hero lifts their home region toward a ceiling set ABOVE the region's own
        // baseline (so a cold market feels it most), but capped absolutely — even a
        // superstar can't turn an indifferent market fight-mad on their own, and a
        // hot market that's already above the cap gets nothing.
        var ceiling = new Dictionary<Region, int>();
        foreach (var f in world.Fighters.Values)
        {
            if (f.Status != FighterStatus.Active || f.Contract is not { } c) continue;
            if (world.Get(c.Promotion) is not { } promo || OrgSizing.SizeOf(promo) < OrgSize.National) continue;
            int pop = f.Intangibles.Popularity.Value;
            if (pop < HeroThreshold) continue;
            int baseline = RegionBaseline(world, f.Nationality);
            int ceil = Math.Min(HeroCeilingCap, baseline + Math.Min(HeroLiftMax, pop - HeroThreshold));
            if (!ceiling.TryGetValue(f.Nationality, out var cur) || ceil > cur) ceiling[f.Nationality] = ceil;
        }
        foreach (var (region, ceil) in ceiling)
            foreach (var city in world.Cities.Values)
                if (city.Region == region && city.MmaInterest < ceil)
                    city.MmaInterest = Math.Min(ceil, city.MmaInterest + HeroBoostPerMonth);
    }

    // Region baseline = market-weighted average of its cities' baselines.
    private static int RegionBaseline(World world, Region region)
    {
        long weighted = 0, markets = 0;
        foreach (var c in world.Cities.Values)
        {
            if (c.Region != region) continue;
            weighted += (long)c.MmaInterestBase * Math.Max(1, c.Market);
            markets += Math.Max(1, c.Market);
        }
        return markets == 0 ? DefaultInterest : (int)(weighted / markets);
    }

    private const int DefaultInterest = 40;
    private const int DecayPerMonth = 1;
    private const int HeroThreshold = 60;    // popularity that makes a fighter a national star
    private const int HeroLiftMax = 20;      // most a hero lifts their region above its baseline
    private const int HeroCeilingCap = 62;   // absolute cap — a hero can't make an indifferent market fight-mad
    private const int HeroBoostPerMonth = 2;
}
