using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Geography;

/// <summary>One country in the atlas: where it sits, how much fight talent it produces, and how its people are named.</summary>
public sealed record CountrySpec(string Name, string Code, Region Continent, int Weight, string Culture, string[]? Areas = null);

/// <summary>A real city placed in a real country (and optionally one of its in-country areas).</summary>
public sealed record CitySpec(string Name, string CountryCode, string? Area = null, bool Anchor = false);

/// <summary>
/// The real-world atlas the game world is built from: 100+ countries (most with
/// NO fight scene yet — real nationalities first, MMA presence where it really
/// exists), the deepest fight nations subdivided into areas, and the 49 launch
/// cities. Adding a scene to a country later = adding one CitySpec.
/// Weight = how much fight talent a country produces (0 = effectively none);
/// Culture keys the name pools in NameData.
/// </summary>
public static class WorldAtlas
{
    public static readonly CountrySpec[] Countries =
    {
        // --- North America ---------------------------------------------------
        new("United States", "USA", Region.NorthAmerica, 30, "anglo", new[] { "West", "Midwest", "Northeast", "South" }),
        new("Canada", "CAN", Region.NorthAmerica, 6, "anglo"),
        new("Mexico", "MEX", Region.NorthAmerica, 6, "latino"),
        new("Cuba", "CUB", Region.NorthAmerica, 2, "latino"),
        new("Jamaica", "JAM", Region.NorthAmerica, 1, "anglo"),
        new("Dominican Republic", "DOM", Region.NorthAmerica, 1, "latino"),
        new("Puerto Rico", "PUR", Region.NorthAmerica, 1, "latino"),
        new("Costa Rica", "CRC", Region.NorthAmerica, 1, "latino"),
        new("Panama", "PAN", Region.NorthAmerica, 1, "latino"),
        new("Guatemala", "GUA", Region.NorthAmerica, 1, "latino"),
        new("Honduras", "HON", Region.NorthAmerica, 1, "latino"),
        new("Haiti", "HAI", Region.NorthAmerica, 1, "french"),

        // --- South America ---------------------------------------------------
        new("Brazil", "BRA", Region.SouthAmerica, 24, "brazilian", new[] { "Southeast", "South", "Northeast", "Central-West" }),
        new("Argentina", "ARG", Region.SouthAmerica, 4, "latino"),
        new("Chile", "CHI", Region.SouthAmerica, 2, "latino"),
        new("Peru", "PER", Region.SouthAmerica, 2, "latino"),
        new("Colombia", "COL", Region.SouthAmerica, 3, "latino"),
        new("Ecuador", "ECU", Region.SouthAmerica, 2, "latino"),
        new("Venezuela", "VEN", Region.SouthAmerica, 2, "latino"),
        new("Uruguay", "URU", Region.SouthAmerica, 1, "latino"),
        new("Paraguay", "PAR", Region.SouthAmerica, 1, "latino"),
        new("Bolivia", "BOL", Region.SouthAmerica, 1, "latino"),

        // --- Europe ------------------------------------------------------------
        new("United Kingdom", "GBR", Region.Europe, 8, "anglo", new[] { "England", "Scotland", "Wales", "Northern Ireland" }),
        new("Ireland", "IRL", Region.Europe, 4, "celtic"),
        new("France", "FRA", Region.Europe, 5, "french"),
        new("Germany", "GER", Region.Europe, 4, "german"),
        new("Netherlands", "NED", Region.Europe, 4, "dutch"),
        new("Belgium", "BEL", Region.Europe, 1, "dutch"),
        new("Spain", "ESP", Region.Europe, 2, "iberian"),
        new("Portugal", "POR", Region.Europe, 1, "iberian"),
        new("Italy", "ITA", Region.Europe, 2, "italian"),
        new("Sweden", "SWE", Region.Europe, 3, "scandinavian"),
        new("Norway", "NOR", Region.Europe, 1, "scandinavian"),
        new("Denmark", "DEN", Region.Europe, 1, "scandinavian"),
        new("Finland", "FIN", Region.Europe, 2, "scandinavian"),
        new("Iceland", "ISL", Region.Europe, 1, "scandinavian"),
        new("Poland", "POL", Region.Europe, 5, "polish"),
        new("Czechia", "CZE", Region.Europe, 2, "czech"),
        new("Slovakia", "SVK", Region.Europe, 1, "czech"),
        new("Austria", "AUT", Region.Europe, 1, "german"),
        new("Switzerland", "SUI", Region.Europe, 1, "german"),
        new("Hungary", "HUN", Region.Europe, 1, "hungarian"),
        new("Romania", "ROU", Region.Europe, 2, "romanian"),
        new("Bulgaria", "BUL", Region.Europe, 1, "balkan"),
        new("Serbia", "SRB", Region.Europe, 2, "balkan"),
        new("Croatia", "CRO", Region.Europe, 2, "balkan"),
        new("Bosnia and Herzegovina", "BIH", Region.Europe, 1, "balkan"),
        new("Albania", "ALB", Region.Europe, 1, "balkan"),
        new("North Macedonia", "MKD", Region.Europe, 1, "balkan"),
        new("Greece", "GRE", Region.Europe, 1, "greek"),
        new("Ukraine", "UKR", Region.Europe, 4, "russian"),
        new("Belarus", "BLR", Region.Europe, 2, "russian"),
        new("Lithuania", "LTU", Region.Europe, 1, "baltic"),
        new("Latvia", "LAT", Region.Europe, 1, "baltic"),
        new("Estonia", "EST", Region.Europe, 1, "baltic"),
        new("Moldova", "MDA", Region.Europe, 1, "romanian"),
        new("Russia", "RUS", Region.Europe, 20, "russian", new[] { "Central", "Caucasus", "Siberia", "Far East" }),
        new("Georgia", "GEO", Region.Europe, 4, "georgian"),
        new("Armenia", "ARM", Region.Europe, 2, "armenian"),
        new("Azerbaijan", "AZE", Region.Europe, 2, "turkic"),

        // --- Asia --------------------------------------------------------------
        new("Japan", "JPN", Region.Asia, 10, "japanese", new[] { "Kanto", "Kansai", "Kyushu" }),
        new("South Korea", "KOR", Region.Asia, 5, "korean"),
        new("China", "CHN", Region.Asia, 6, "chinese"),
        new("Taiwan", "TPE", Region.Asia, 1, "chinese"),
        new("Mongolia", "MGL", Region.Asia, 3, "mongolian"),
        new("Thailand", "THA", Region.Asia, 4, "thai"),
        new("Vietnam", "VIE", Region.Asia, 1, "vietnamese"),
        new("Philippines", "PHI", Region.Asia, 3, "filipino"),
        new("Indonesia", "INA", Region.Asia, 2, "indonesian"),
        new("Malaysia", "MAS", Region.Asia, 1, "indonesian"),
        new("Singapore", "SGP", Region.Asia, 1, "chinese"),
        new("India", "IND", Region.Asia, 3, "indian"),
        new("Pakistan", "PAK", Region.Asia, 1, "indian"),
        new("Bangladesh", "BAN", Region.Asia, 1, "indian"),
        new("Sri Lanka", "SRI", Region.Asia, 1, "indian"),
        new("Nepal", "NEP", Region.Asia, 1, "indian"),
        new("Kazakhstan", "KAZ", Region.Asia, 5, "centralasian"),
        new("Kyrgyzstan", "KGZ", Region.Asia, 3, "centralasian"),
        new("Uzbekistan", "UZB", Region.Asia, 3, "centralasian"),
        new("Tajikistan", "TJK", Region.Asia, 2, "centralasian"),
        new("Turkmenistan", "TKM", Region.Asia, 1, "centralasian"),
        new("Myanmar", "MYA", Region.Asia, 1, "thai"),
        new("Cambodia", "CAM", Region.Asia, 1, "thai"),
        new("Laos", "LAO", Region.Asia, 1, "thai"),

        // --- Oceania -----------------------------------------------------------
        new("Australia", "AUS", Region.Oceania, 7, "anglo"),
        new("New Zealand", "NZL", Region.Oceania, 4, "polynesian"),
        new("Fiji", "FIJ", Region.Oceania, 1, "polynesian"),
        new("Samoa", "SAM", Region.Oceania, 2, "polynesian"),
        new("Tonga", "TGA", Region.Oceania, 1, "polynesian"),
        new("Papua New Guinea", "PNG", Region.Oceania, 1, "polynesian"),

        // --- Africa ------------------------------------------------------------
        new("South Africa", "RSA", Region.Africa, 5, "southernafrican"),
        new("Nigeria", "NGA", Region.Africa, 6, "westafrican"),
        new("Ghana", "GHA", Region.Africa, 2, "westafrican"),
        new("Senegal", "SEN", Region.Africa, 2, "westafrican"),
        new("Cameroon", "CMR", Region.Africa, 3, "westafrican"),
        new("Ivory Coast", "CIV", Region.Africa, 1, "westafrican"),
        new("Mali", "MLI", Region.Africa, 1, "westafrican"),
        new("DR Congo", "COD", Region.Africa, 2, "centralafrican"),
        new("Congo", "CGO", Region.Africa, 1, "centralafrican"),
        new("Angola", "ANG", Region.Africa, 1, "southernafrican"),
        new("Kenya", "KEN", Region.Africa, 2, "eastafrican"),
        new("Uganda", "UGA", Region.Africa, 1, "eastafrican"),
        new("Tanzania", "TAN", Region.Africa, 1, "eastafrican"),
        new("Ethiopia", "ETH", Region.Africa, 1, "eastafrican"),
        new("Zimbabwe", "ZIM", Region.Africa, 1, "southernafrican"),
        new("Zambia", "ZAM", Region.Africa, 1, "southernafrican"),
        new("Botswana", "BOT", Region.Africa, 1, "southernafrican"),
        new("Namibia", "NAM", Region.Africa, 1, "southernafrican"),
        new("Mozambique", "MOZ", Region.Africa, 1, "southernafrican"),
        new("Morocco", "MAR", Region.Africa, 3, "arabic"),
        new("Algeria", "ALG", Region.Africa, 2, "arabic"),
        new("Tunisia", "TUN", Region.Africa, 1, "arabic"),
        new("Egypt", "EGY", Region.Africa, 2, "arabic"),

        // --- Middle East -------------------------------------------------------
        new("United Arab Emirates", "UAE", Region.MiddleEast, 2, "arabic"),
        new("Saudi Arabia", "KSA", Region.MiddleEast, 2, "arabic"),
        new("Qatar", "QAT", Region.MiddleEast, 1, "arabic"),
        new("Bahrain", "BRN", Region.MiddleEast, 2, "arabic"),
        new("Kuwait", "KUW", Region.MiddleEast, 1, "arabic"),
        new("Oman", "OMA", Region.MiddleEast, 1, "arabic"),
        new("Jordan", "JOR", Region.MiddleEast, 2, "arabic"),
        new("Lebanon", "LIB", Region.MiddleEast, 2, "arabic"),
        new("Iraq", "IRQ", Region.MiddleEast, 2, "arabic"),
        new("Iran", "IRI", Region.MiddleEast, 4, "persian"),
        new("Afghanistan", "AFG", Region.MiddleEast, 2, "persian"),
        new("Israel", "ISR", Region.MiddleEast, 2, "israeli"),
        new("Turkey", "TUR", Region.MiddleEast, 4, "turkic"),
        new("Yemen", "YEM", Region.MiddleEast, 1, "arabic"),
    };

    /// <summary>The 49 launch cities. Anchor = the region's fight capital (its biggest market).</summary>
    public static readonly CitySpec[] Cities =
    {
        new("Las Vegas", "USA", "West", Anchor: true), new("New York", "USA", "Northeast"), new("Los Angeles", "USA", "West"),
        new("Chicago", "USA", "Midwest"), new("Toronto", "CAN"), new("Montreal", "CAN"), new("Mexico City", "MEX"),

        new("Sao Paulo", "BRA", "Southeast", Anchor: true), new("Rio de Janeiro", "BRA", "Southeast"), new("Curitiba", "BRA", "South"),
        new("Buenos Aires", "ARG"), new("Santiago", "CHI"), new("Bogota", "COL"), new("Lima", "PER"),

        new("London", "GBR", "England", Anchor: true), new("Manchester", "GBR", "England"), new("Paris", "FRA"),
        new("Amsterdam", "NED"), new("Stockholm", "SWE"), new("Warsaw", "POL"), new("Moscow", "RUS", "Central"),

        new("Tokyo", "JPN", "Kanto", Anchor: true), new("Seoul", "KOR"), new("Shanghai", "CHN"), new("Bangkok", "THA"),
        new("Manila", "PHI"), new("Singapore", "SGP"), new("Mumbai", "IND"),

        new("Sydney", "AUS", Anchor: true), new("Melbourne", "AUS"), new("Auckland", "NZL"), new("Brisbane", "AUS"),
        new("Perth", "AUS"), new("Adelaide", "AUS"), new("Wellington", "NZL"),

        new("Johannesburg", "RSA", Anchor: true), new("Lagos", "NGA"), new("Cape Town", "RSA"), new("Nairobi", "KEN"),
        new("Accra", "GHA"), new("Casablanca", "MAR"), new("Dakar", "SEN"),

        new("Abu Dhabi", "UAE", Anchor: true), new("Dubai", "UAE"), new("Riyadh", "KSA"), new("Doha", "QAT"),
        new("Manama", "BRN"), new("Amman", "JOR"), new("Istanbul", "TUR"),
    };

    /// <summary>Atlas lookup by code (culture/weight aren't persisted — resolved from here at runtime).</summary>
    public static CountrySpec? ByCode(string code) =>
        Countries.FirstOrDefault(c => c.Code == code);

    /// <summary>
    /// The fight cities of a region, the region's capital first — enough to offer
    /// a home-town choice BEFORE a world exists (the new-game dialog runs before
    /// the geography is built, so it reads the atlas, not the World).
    /// </summary>
    public static IReadOnlyList<CitySpec> CitiesIn(Region region) =>
        Cities.Where(c => ByCode(c.CountryCode)?.Continent == region)
            .OrderByDescending(c => c.Anchor)
            .ToList();

    /// <summary>Every country that uses a given name culture (weighted by talent), for identity-first generation.</summary>
    public static IReadOnlyList<CountrySpec> CountriesWithCulture(string culture) =>
        Countries.Where(c => c.Culture == culture && c.Weight > 0).ToList();

    /// <summary>The continent's flagship fight nation (fallback for migrations and hometown-less countries).</summary>
    public static string FlagshipCode(Region continent) => continent switch
    {
        Region.NorthAmerica => "USA",
        Region.SouthAmerica => "BRA",
        Region.Europe => "GBR",
        Region.Asia => "JPN",
        Region.Oceania => "AUS",
        Region.Africa => "RSA",
        Region.MiddleEast => "UAE",
        _ => "USA",
    };
}
