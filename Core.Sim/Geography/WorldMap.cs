using MmaManager.Core.Model;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Geography;

/// <summary>
/// Builds the map — a handful of fight cities per region, each with a small and a
/// large venue sized to the local market — and picks where a promotion stages a
/// card (a venue in its base region, sized to how big the promotion is). Bigger
/// venues cost more rent but let a real draw sell more tickets.
/// </summary>
public static class WorldMap
{
    /// <summary>
    /// Build the real-world geography from <see cref="WorldAtlas"/>: every
    /// country (most with no fight scene yet), then the launch cities — each in
    /// its real country (and area), with venues sized to its rolled market.
    /// </summary>
    public static void Build(World world, SimRandom rng)
    {
        var byCode = new Dictionary<string, Country>(StringComparer.Ordinal);
        foreach (var spec in WorldAtlas.Countries)
        {
            var country = new Country
            {
                Id = world.NewCountryId(),
                Name = spec.Name,
                Code = spec.Code,
                Continent = spec.Continent,
                // A cultural baseline from how much the nation lives the sport — the
                // deep fight nations (high talent Weight) are fight-mad, others cooler.
                MmaInterest = Math.Clamp(35 + spec.Weight * 18 / 10 + Hash(spec.Code, 1, 8), 20, 95),
            };
            country.MmaInterestBase = country.MmaInterest;
            if (spec.Areas is { } areas) country.Areas.AddRange(areas);
            world.Countries[country.Id] = country;
            byCode[spec.Code] = country;
        }

        foreach (var spec in WorldAtlas.Cities)
        {
            var country = byCode[spec.CountryCode];
            // One anchor metro per region (its fight capital); the rest spread down.
            int market = spec.Anchor ? rng.Range(72, 90) : rng.Range(20, 74);
            var city = new City
            {
                Id = world.NewCityId(),
                Name = spec.Name,
                Region = country.Continent,
                Country = country.Id,
                Area = spec.Area is { } a ? country.Areas.IndexOf(a) is var idx && idx >= 0 ? idx : null : null,
                Market = market,
                // Local appetite varies around the national baseline; the fight
                // capitals (anchors) run a touch hotter.
                MmaInterest = Math.Clamp(country.MmaInterest + Hash(spec.Name, 2, 12) + (spec.Anchor ? 5 : 0), 15, 98),
            };
            city.MmaInterestBase = city.MmaInterest;
            world.Cities[city.Id] = city;

            // A small, cheap club hall for the tiny promotions (City/Regional), a
            // mid-size forum, and a full arena for the big draws. Small orgs live in
            // the hall — its low rent is what keeps a shoestring's card affordable.
            //
            // Every city used to get the SAME three buildings — "{City} Fight
            // Hall", "The {City} Forum", "{City} Arena" — the whole world over, so
            // a poster from Tokyo and a poster from Lagos named the same room.
            // Named halls give each town its own address; anywhere the atlas
            // doesn't name falls back to the generic three.
            var (hall, forum, arena) = Venues.For(spec.Name);
            AddVenue(world, city, hall, capacity: 500 + market * 8, rng);
            AddVenue(world, city, forum, capacity: 1500 + market * 30, rng);
            AddVenue(world, city, arena, capacity: 5000 + market * 180, rng);
        }
    }

    // A stable, seed-independent jitter in [-range, range] from a string key — used
    // to vary static geography (MMA interest) without touching the world RNG stream.
    private static int Hash(string key, int salt, int range)
    {
        uint h = 2166136261u ^ (uint)salt;
        foreach (char c in key) { h = (h ^ c) * 16777619u; }
        h ^= h >> 15;
        return (int)(h % (uint)(2 * range + 1)) - range;
    }

    /// <summary>Resolve a country by its atlas code (e.g. "BRA").</summary>
    public static Country? CountryByCode(World world, string code) =>
        world.Countries.Values.FirstOrDefault(c => c.Code == code);

    /// <summary>The region's biggest-market city — where a promotion headquartered there naturally lives.</summary>
    public static City? AnchorCity(World world, Region region) =>
        world.Cities.Values.Where(c => c.Region == region)
            .OrderByDescending(c => c.Market).ThenBy(c => c.Id.Value)
            .FirstOrDefault();

    /// <summary>
    /// Auto-pick a home-region venue sized to the promotion's prestige (bigger
    /// promotions fill bigger rooms), within the cities its reach tier allows.
    /// Going abroad is always an explicit city choice, never the default.
    /// </summary>
    public static Venue? PickVenue(World world, Promotion promo)
    {
        int target = promo.Prestige.Value * 220;   // roughly how many seats it can move
        return world.Venues.Values
            .Where(v => world.Get(v.City)?.Region == promo.BaseRegion
                        && Org.OrgReach.CanBookIn(world, promo, v.City))
            .OrderBy(v => Math.Abs(v.Capacity - target))
            .ThenBy(v => v.Id.Value)
            .FirstOrDefault();
    }

    /// <summary>The biggest house in town — where a tradition goes once a year.</summary>
    public static Venue? BiggestVenueIn(World world, CityId city) =>
        world.Venues.Values.Where(v => v.City == city)
            .OrderByDescending(v => v.Capacity).ThenBy(v => v.Id.Value)
            .FirstOrDefault();

    /// <summary>
    /// The best room for this promotion in a specific city: the venue whose
    /// capacity best matches what the org can expect to draw there.
    /// </summary>
    public static Venue? PickVenueIn(World world, Promotion promo, CityId city)
    {
        // Mirrors the box office's FANBASE-driven house (Financials.Compute):
        // 190 + prestige·38 + cityPop·78 + regionPop·14, plus ~900 for a typical
        // main event's pull (unknown when the room is picked). A no-name startup
        // expects a few hundred heads and books the cheap fight hall — not an
        // arena whose rent buries the card (the old estimate started at 2,500).
        int expected = 190 + promo.Prestige.Value * 38 + 900
                     + Reputation.OrgInCity(promo, city) * 78
                     + (world.Get(city) is { } c ? Reputation.OrgInRegion(world, promo, c.Region) * 14 : 0);
        return world.Venues.Values
            .Where(v => v.City == city)
            .OrderBy(v => Math.Abs(v.Capacity - expected))
            .ThenBy(v => v.Id.Value)
            .FirstOrDefault();
    }

    /// <summary>
    /// Where an AI promotion stages its next card: mostly the HQ metro, with
    /// regular trips around everywhere its reach tier lets it book — a national
    /// org tours its country, a continental org its continent.
    /// </summary>
    public static CityId? PickTourStop(World world, Promotion promo, DateOnly date)
    {
        var home = promo.HomeCity ?? AnchorCity(world, promo.BaseRegion)?.Id;
        var tourCities = world.Cities.Values
            .Where(c => Org.OrgReach.CanBookIn(world, promo, c.Id))
            .OrderBy(c => c.Id.Value)
            .ToList();
        if (tourCities.Count == 0) return home;

        // Deterministic rotation: every other show at home, the rest touring.
        int beat = (date.DayNumber / 28 + promo.Id.Value) % (tourCities.Count * 2);
        return beat % 2 == 0 && home is not null
            ? home
            : tourCities[(beat / 2) % tourCities.Count].Id;
    }

    /// <summary>
    /// Running a card lifts the promotion's standing in the host city (echoing
    /// around its region) — but the gain is RELATIVE to where the org already
    /// stands. A promotion climbs easily toward the pull of the names it puts on,
    /// then flattens as it catches up: to grow past its current standing it has to
    /// feature fighters more popular than the ones that got it there (down low
    /// almost any card lifts you; higher up it takes bigger names). A fuller house
    /// converts more of that headroom into new fans.
    /// </summary>
    public static void GrowCityPopularity(World world, Promotion promo, CityId city, int starPower, int attendance)
    {
        int standing = Reputation.OrgInCity(promo, city);
        // The brand itself pulls a crowd — a prestigious promotion draws on its name
        // even with a modest card — and its stars pull more on top. The org climbs
        // toward whichever is higher; a small org (low prestige) leans on its fighters.
        int target = Math.Max(starPower, promo.Prestige.Value);
        double headroom = target - standing;
        if (headroom <= 0) return;                       // already at the pull of its name and its names — no new fans
        double house = Math.Clamp(attendance / 6000.0, 0.4, 1.4);
        // Word spreads faster where the sport is loved.
        double interest = MmaInterest.Factor(MmaInterest.InCity(world, city), 0.5);
        int gain = (int)Math.Round(headroom * 0.25 * house * interest);
        if (gain <= 0) return;
        Reputation.BumpOrg(world, promo, city, Math.Min(gain, 6));
    }

    /// <summary>The city an event happens in (via its venue; falls back to the region's anchor metro).</summary>
    public static CityId? CityOf(World world, Event ev) =>
        ev.VenueId is { } vid && world.Get(vid) is { } venue
            ? venue.City
            : AnchorCity(world, ev.Region)?.Id;

    private static void AddVenue(World world, City city, string name, int capacity, SimRandom rng)
    {
        var venue = new Venue
        {
            Id = world.NewVenueId(),
            Name = name,
            City = city.Id,
            Capacity = capacity,
            // Rent scales with the building — a small hall is cheap, an arena costs a
            // fortune — so a shoestring in the club isn't buried by an arena's overhead.
            Rent = new Money(capacity * (7m + rng.Range(0, 5))),
        };
        world.Venues[venue.Id] = venue;
    }
}
