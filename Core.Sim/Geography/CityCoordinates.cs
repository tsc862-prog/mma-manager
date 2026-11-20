using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Geography;

/// <summary>
/// Where the world's cities and countries sit on the globe, as plain real-world
/// facts (WGS84 degrees: north and east positive). A static lookup, not model
/// data: <c>City</c> has no coordinate field, so nothing here is saved.
///
/// Keying. City ids are NOT a safe key (they are handed out in atlas order, so
/// they follow row order). A city is keyed by its country's atlas code plus its
/// name, exactly as <see cref="WorldAtlas.Cities"/> states them; both are
/// persisted on every world, so a loaded save resolves the same. Every city ever
/// created comes from <c>WorldMap.Build</c> over that atlas (today's mode and
/// Origins share it; nothing creates a city at runtime), and a test asserts every
/// atlas city and country has a row here.
/// Country centres are rough (the middle of the land, nudged toward where people
/// live); city points are the city centre, to about 0.1 degree.
/// </summary>
public static class CityCoordinates
{
    /// <summary>The city's centre; false when the city is not in the atlas table.</summary>
    public static bool TryGet(World world, CityId id, out (double Lat, double Lon) at)
    {
        at = default;
        var city = world.Get(id);
        if (city is null) return false;
        var country = world.Get(city.Country);
        if (country is null) return false;
        return TryGet(country.Code, city.Name, out at);
    }

    /// <summary>The city's centre, or throws when it has none (an atlas row without a coordinate row).</summary>
    public static (double Lat, double Lon) Get(World world, CityId id) =>
        TryGet(world, id, out var at) ? at
            : throw new KeyNotFoundException($"No coordinates for city {id.Value} ({world.Get(id)?.Name}).");

    /// <summary>By atlas country code and city name (e.g. "BRA", "Sao Paulo").</summary>
    public static bool TryGet(string countryCode, string cityName, out (double Lat, double Lon) at) =>
        Cities.TryGetValue((countryCode, cityName), out at);

    /// <summary>A country's rough centre; false for a code that is not in the atlas.</summary>
    public static bool TryGetCountry(string countryCode, out (double Lat, double Lon) at) =>
        Countries.TryGetValue(countryCode, out at);

    public static (double Lat, double Lon) GetCountry(string countryCode) =>
        Countries.TryGetValue(countryCode, out var at) ? at
            : throw new KeyNotFoundException($"No centre for country {countryCode}.");

    /// <summary>Country centre by the world's own country id.</summary>
    public static bool TryGetCountry(World world, CountryId id, out (double Lat, double Lon) at)
    {
        at = default;
        var c = world.Get(id);
        return c is not null && Countries.TryGetValue(c.Code, out at);
    }

    // City centres: (country code, atlas name) -> (lat, lon). Grouped as the atlas is.
    public static readonly IReadOnlyDictionary<(string Country, string City), (double Lat, double Lon)> Cities =
        new Dictionary<(string, string), (double, double)>
    {
        // --- North America
        [("USA", "Las Vegas")] = (36.17, -115.14),
        [("USA", "New York")] = (40.71, -74.01),
        [("USA", "Los Angeles")] = (34.05, -118.24),
        [("USA", "Chicago")] = (41.88, -87.63),
        [("CAN", "Toronto")] = (43.65, -79.38),
        [("CAN", "Montreal")] = (45.50, -73.57),
        [("MEX", "Mexico City")] = (19.43, -99.13),

        // --- South America
        [("BRA", "Sao Paulo")] = (-23.55, -46.63),
        [("BRA", "Rio de Janeiro")] = (-22.91, -43.17),
        [("BRA", "Curitiba")] = (-25.43, -49.27),
        [("ARG", "Buenos Aires")] = (-34.60, -58.38),
        [("CHI", "Santiago")] = (-33.45, -70.67),
        [("COL", "Bogota")] = (4.71, -74.07),
        [("PER", "Lima")] = (-12.05, -77.04),

        // --- Europe
        [("GBR", "London")] = (51.51, -0.13),
        [("GBR", "Manchester")] = (53.48, -2.24),
        [("FRA", "Paris")] = (48.86, 2.35),
        [("NED", "Amsterdam")] = (52.37, 4.90),
        [("SWE", "Stockholm")] = (59.33, 18.07),
        [("POL", "Warsaw")] = (52.23, 21.01),
        [("RUS", "Moscow")] = (55.76, 37.62),

        // --- Asia
        [("JPN", "Tokyo")] = (35.68, 139.69),
        [("KOR", "Seoul")] = (37.57, 126.98),
        [("CHN", "Shanghai")] = (31.23, 121.47),
        [("THA", "Bangkok")] = (13.76, 100.50),
        [("PHI", "Manila")] = (14.60, 120.98),
        [("SGP", "Singapore")] = (1.35, 103.82),
        [("IND", "Mumbai")] = (19.08, 72.88),

        // --- Oceania
        [("AUS", "Sydney")] = (-33.87, 151.21),
        [("AUS", "Melbourne")] = (-37.81, 144.96),
        [("NZL", "Auckland")] = (-36.85, 174.76),
        [("AUS", "Brisbane")] = (-27.47, 153.03),
        [("AUS", "Perth")] = (-31.95, 115.86),
        [("AUS", "Adelaide")] = (-34.93, 138.60),
        [("NZL", "Wellington")] = (-41.29, 174.78),

        // --- Africa
        [("RSA", "Johannesburg")] = (-26.20, 28.05),
        [("NGA", "Lagos")] = (6.52, 3.38),
        [("RSA", "Cape Town")] = (-33.92, 18.42),
        [("KEN", "Nairobi")] = (-1.29, 36.82),
        [("GHA", "Accra")] = (5.60, -0.19),
        [("MAR", "Casablanca")] = (33.57, -7.59),
        [("SEN", "Dakar")] = (14.72, -17.47),

        // --- Middle East
        [("UAE", "Abu Dhabi")] = (24.45, 54.38),
        [("UAE", "Dubai")] = (25.20, 55.27),
        [("KSA", "Riyadh")] = (24.71, 46.68),
        [("QAT", "Doha")] = (25.29, 51.53),
        [("BRN", "Manama")] = (26.23, 50.59),
        [("JOR", "Amman")] = (31.95, 35.93),
        [("TUR", "Istanbul")] = (41.01, 28.98),
    };

    // Country centres by atlas code. Rough: for the map's labels and tints.
    public static readonly IReadOnlyDictionary<string, (double Lat, double Lon)> Countries =
        new Dictionary<string, (double, double)>(StringComparer.Ordinal)
    {
        // --- North America
        ["USA"] = (39.8, -98.6), ["CAN"] = (56.1, -106.3), ["MEX"] = (23.6, -102.5), ["CUB"] = (21.5, -79.0),
        ["JAM"] = (18.1, -77.3), ["DOM"] = (18.7, -70.2), ["PUR"] = (18.2, -66.5), ["CRC"] = (9.7, -83.8),
        ["PAN"] = (8.5, -80.8), ["GUA"] = (15.8, -90.2), ["HON"] = (14.8, -86.6), ["HAI"] = (19.0, -72.7),

        // --- South America
        ["BRA"] = (-10.8, -52.9), ["ARG"] = (-35.4, -65.2), ["CHI"] = (-37.7, -71.5), ["PER"] = (-9.2, -75.0),
        ["COL"] = (4.6, -74.3), ["ECU"] = (-1.4, -78.4), ["VEN"] = (7.1, -66.2), ["URU"] = (-32.8, -56.0),
        ["PAR"] = (-23.2, -58.4), ["BOL"] = (-16.7, -64.7),

        // --- Europe
        ["GBR"] = (54.0, -2.5), ["IRL"] = (53.2, -8.0), ["FRA"] = (46.6, 2.5), ["GER"] = (51.1, 10.4),
        ["NED"] = (52.2, 5.6), ["BEL"] = (50.6, 4.6), ["ESP"] = (40.2, -3.6), ["POR"] = (39.6, -8.0),
        ["ITA"] = (42.8, 12.6), ["SWE"] = (62.8, 16.7), ["NOR"] = (64.6, 12.7), ["DEN"] = (56.0, 9.4),
        ["FIN"] = (64.5, 26.3), ["ISL"] = (64.9, -18.6), ["POL"] = (52.1, 19.4), ["CZE"] = (49.8, 15.5),
        ["SVK"] = (48.7, 19.5), ["AUT"] = (47.6, 14.1), ["SUI"] = (46.8, 8.2), ["HUN"] = (47.2, 19.4),
        ["ROU"] = (45.9, 25.0), ["BUL"] = (42.7, 25.2), ["SRB"] = (44.0, 20.8), ["CRO"] = (45.2, 15.5),
        ["BIH"] = (44.2, 17.8), ["ALB"] = (41.1, 20.0), ["MKD"] = (41.6, 21.7), ["GRE"] = (39.1, 22.9),
        ["UKR"] = (49.0, 31.4), ["BLR"] = (53.6, 28.0), ["LTU"] = (55.3, 23.9), ["LAT"] = (56.9, 24.9),
        ["EST"] = (58.7, 25.5), ["MDA"] = (47.2, 28.5), ["RUS"] = (58.0, 60.0), ["GEO"] = (42.2, 43.5),
        ["ARM"] = (40.2, 44.9), ["AZE"] = (40.3, 47.7),

        // --- Asia
        ["JPN"] = (36.5, 138.0), ["KOR"] = (36.4, 127.9), ["CHN"] = (35.0, 103.0), ["TPE"] = (23.7, 121.0),
        ["MGL"] = (46.9, 103.8), ["THA"] = (15.1, 101.0), ["VIE"] = (16.0, 106.3), ["PHI"] = (12.9, 122.0),
        ["INA"] = (-2.5, 117.0), ["MAS"] = (4.2, 102.2), ["SGP"] = (1.35, 103.82), ["IND"] = (22.0, 79.0),
        ["PAK"] = (30.0, 69.4), ["BAN"] = (23.8, 90.3), ["SRI"] = (7.6, 80.7), ["NEP"] = (28.3, 84.0),
        ["KAZ"] = (48.0, 67.0), ["KGZ"] = (41.5, 74.5), ["UZB"] = (41.4, 64.6), ["TJK"] = (38.9, 71.0),
        ["TKM"] = (39.0, 59.6), ["MYA"] = (21.0, 96.0), ["CAM"] = (12.6, 104.9), ["LAO"] = (19.9, 102.5),

        // --- Oceania
        ["AUS"] = (-25.7, 134.5), ["NZL"] = (-41.5, 172.5), ["FIJ"] = (-17.8, 178.0), ["SAM"] = (-13.8, -172.1),
        ["TGA"] = (-21.2, -175.2), ["PNG"] = (-6.3, 144.0),

        // --- Africa
        ["RSA"] = (-29.0, 24.7), ["NGA"] = (9.1, 8.7), ["GHA"] = (7.9, -1.0), ["SEN"] = (14.5, -14.5),
        ["CMR"] = (5.7, 12.7), ["CIV"] = (7.5, -5.5), ["MLI"] = (17.6, -4.0), ["COD"] = (-2.9, 23.7),
        ["CGO"] = (-0.7, 15.2), ["ANG"] = (-12.3, 17.5), ["KEN"] = (0.2, 37.9), ["UGA"] = (1.4, 32.3),
        ["TAN"] = (-6.4, 34.9), ["ETH"] = (9.1, 40.5), ["ZIM"] = (-19.0, 29.9), ["ZAM"] = (-13.1, 27.8),
        ["BOT"] = (-22.3, 24.7), ["NAM"] = (-22.6, 17.1), ["MOZ"] = (-18.7, 35.5), ["MAR"] = (31.8, -6.3),
        ["ALG"] = (28.0, 2.6), ["TUN"] = (34.0, 9.5), ["EGY"] = (26.8, 30.8),

        // --- Middle East
        ["UAE"] = (23.9, 54.3), ["KSA"] = (24.0, 45.1), ["QAT"] = (25.3, 51.2), ["BRN"] = (26.0, 50.55),
        ["KUW"] = (29.3, 47.5), ["OMA"] = (21.5, 55.9), ["JOR"] = (31.2, 36.5), ["LIB"] = (33.9, 35.9),
        ["IRQ"] = (33.0, 43.7), ["IRI"] = (32.4, 53.7), ["AFG"] = (33.9, 66.0), ["ISR"] = (31.4, 35.0),
        ["TUR"] = (39.0, 35.2), ["YEM"] = (15.6, 48.5),
    };
}
