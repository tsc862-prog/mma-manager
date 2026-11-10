namespace MmaManager.Core.Model;

/// <summary>
/// A real country. Belongs to one of the seven continental <see cref="Region"/>s
/// and may define in-country <see cref="Areas"/> (only the deepest fight nations
/// need them). A country can exist with NO cities — a real nationality with no
/// MMA presence yet; adding a scene later is just adding a city.
/// </summary>
public sealed class Country
{
    public required CountryId Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Short code ("USA", "BRA") for compact display.</summary>
    public required string Code { get; init; }

    public required Region Continent { get; init; }

    /// <summary>Optional in-country regions ("West", "Northeast", …). Empty = the whole country is one area.</summary>
    public List<string> Areas { get; init; } = new();

    /// <summary>
    /// How much this nation cares about MMA (0–100). Set at world creation (a
    /// cultural baseline — some countries are fight-mad, others indifferent) and
    /// mostly static, though a national hero or big shows lift it. Drives ticket
    /// sales, fanbase growth and how many promotions a market can support. It decays
    /// back toward <see cref="MmaInterestBase"/> when the boosts fade.
    /// </summary>
    public int MmaInterest { get; set; } = 40;

    /// <summary>The cultural floor interest decays back to (the seeded baseline).</summary>
    public int MmaInterestBase { get; set; } = 40;
}

/// <summary>
/// A fight market. Its <see cref="Market"/> (0–100 fan interest) sizes the venues
/// built there and, once a promotion runs shows in the region, how much gate the
/// city can draw.
/// </summary>
public sealed class City
{
    public required CityId Id { get; init; }
    public required string Name { get; init; }
    public required Region Region { get; init; }
    public required CountryId Country { get; init; }

    /// <summary>Index into the country's <see cref="Country.Areas"/> (null = no area / countrywide).</summary>
    public int? Area { get; init; }

    public int Market { get; set; }

    /// <summary>
    /// Local appetite for MMA (0–100) — how fight-mad this particular city is,
    /// varying around its country's baseline. Higher interest fills seats and grows
    /// a promotion's local following faster. Distinct from <see cref="Market"/> (raw
    /// market size): a small city can be an MMA hotbed, a big one indifferent. Rises
    /// with a hometown hero or a big show and decays back to <see cref="MmaInterestBase"/>.
    /// </summary>
    public int MmaInterest { get; set; } = 40;

    /// <summary>The cultural floor interest decays back to (the seeded baseline).</summary>
    public int MmaInterestBase { get; set; } = 40;
}

/// <summary>
/// A building an event is staged in. Capacity caps attendance (and therefore the
/// gate); rent is billed per event. Bigger venues cost more but let a big draw
/// sell more tickets.
/// </summary>
public sealed class Venue
{
    public required VenueId Id { get; init; }
    public required string Name { get; init; }
    public required CityId City { get; init; }
    public int Capacity { get; set; }
    public Money Rent { get; set; }
}
