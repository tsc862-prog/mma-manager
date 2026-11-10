namespace MmaManager.Core.Sim.Geography;

/// <summary>
/// The buildings the sport fights in.
///
/// Every city on earth used to get the identical three rooms — "{City} Fight
/// Hall", "The {City} Forum", "{City} Arena" — which meant a poster from Osaka
/// and a poster from Lagos named the same venue, and ICF's storied New Year
/// dome show was staged in "Tokyo Arena". Named halls give each fight town its
/// own address: the sweaty club room a shoestring can afford, the mid-size
/// forum, and the big house the summit sells out.
///
/// All invented, like every other proper noun in the world — but invented to
/// sound like they've been there for fifty years.
/// </summary>
public static class Venues
{
    /// <summary>The three rooms in a named fight town, small to large.</summary>
    public static (string Hall, string Forum, string Arena) For(string city) =>
        Named.TryGetValue(city, out var v) ? v : ($"{city} Fight Hall", $"The {city} Forum", $"{city} Arena");

    private static readonly Dictionary<string, (string, string, string)> Named =
        new(StringComparer.Ordinal)
        {
            // North America
            ["Las Vegas"] = ("The Sandbox", "Silverline Hall", "The Monarch Grand Garden"),
            ["New York"] = ("The Boiler Room", "Hudson Armory", "Cathedral Square Garden"),
            ["Los Angeles"] = ("The Bell House", "Sunset Forum", "The Pacific Colosseum"),
            ["Chicago"] = ("Kelly's Gym", "The Stockyard", "Lakeshore Coliseum"),
            ["Toronto"] = ("The Icehouse", "Dominion Hall", "Maple Crown Arena"),
            ["Montreal"] = ("Salle Bercy", "Le Forum St-Laurent", "Le Grand Vieux-Port"),
            ["Mexico City"] = ("Arena Chica", "Palacio de los Puños", "El Coloso de Anáhuac"),

            // South America
            ["Sao Paulo"] = ("Ginásio da Vila", "Pavilhão Paulista", "Arena Bandeirantes"),
            ["Rio de Janeiro"] = ("O Barracão", "Ginásio da Lagoa", "Arena Guanabara"),
            ["Curitiba"] = ("O Galpão", "Pavilhão Araucária", "Arena do Pinhão"),
            ["Buenos Aires"] = ("El Galpón de Boedo", "Palacio Rivadavia", "Estadio del Plata"),
            ["Santiago"] = ("La Bodega", "Teatro Caupolicán Nuevo", "Arena Cordillera"),
            ["Bogota"] = ("El Cuadrilátero", "Palacio Muisca", "Coliseo El Dorado"),
            ["Lima"] = ("La Casona", "Coliseo Miraflores", "Gran Arena Pacífico"),

            // Europe
            ["London"] = ("The Bethnal Green Institute", "Ironmongers Hall", "The Empire Dome"),
            ["Manchester"] = ("The Ardwick Baths", "Cotton Exchange Hall", "Northern Powerhouse Arena"),
            ["Paris"] = ("La Cave", "Palais des Sports Vaugirard", "Le Grand Zénith"),
            ["Amsterdam"] = ("De Kelder", "Het Concertgebouw Sportpaleis", "Oranje Dome"),
            ["Stockholm"] = ("Källaren", "Kungshallen", "Nordkronan Arena"),
            ["Warsaw"] = ("Hala Mokotów", "Pałac Sportu", "Arena Wisła"),
            ["Moscow"] = ("Zal Number Four", "Dinamo Sports Palace", "The Krasny Dome"),

            // Asia
            ["Tokyo"] = ("Korakuen Basement", "Ryōgoku Hall", "The Sunrise Dome"),
            ["Seoul"] = ("Dongdaemun Gym", "Jangchung Hall", "Han River Dome"),
            ["Shanghai"] = ("The Bund Room", "Jing'an Sports Hall", "Pearl Tower Arena"),
            ["Bangkok"] = ("Soi Nine Stadium", "Rajadamnern New Hall", "The Chao Phraya Dome"),
            ["Manila"] = ("The Cockpit", "Araneta Hall", "Pearl of the Orient Arena"),
            ["Singapore"] = ("The Shophouse", "Kallang Hall", "Marina Bay Dome"),
            ["Mumbai"] = ("The Akhara", "Worli Sports Palace", "Gateway Arena"),

            // Oceania
            ["Sydney"] = ("The Woolshed", "Harbour Pavilion", "The Southern Cross Dome"),
            ["Melbourne"] = ("Festival Hall Annexe", "Flinders Pavilion", "Yarra Grand Arena"),
            ["Auckland"] = ("The Boatshed", "Ponsonby Hall", "Sky Tower Arena"),
            ["Brisbane"] = ("The Tin Shed", "Riverside Hall", "Sunstate Arena"),
            ["Perth"] = ("The Goldfields Room", "Swan Pavilion", "Indian Ocean Arena"),
            ["Adelaide"] = ("The Cellar", "Torrens Hall", "Festival State Arena"),
            ["Wellington"] = ("The Windbreak", "Cuba Street Hall", "Cook Strait Arena"),

            // Africa
            ["Johannesburg"] = ("The Reef Room", "Gold Exchange Hall", "Highveld Dome"),
            ["Lagos"] = ("The Yard", "Surulere Sports Hall", "Eko Grand Arena"),
            ["Cape Town"] = ("The Dockside", "Table Mountain Hall", "Cape Point Arena"),
            ["Nairobi"] = ("The Tin Roof", "Kasarani Hall", "Rift Valley Arena"),
            ["Accra"] = ("The Bukom Room", "Independence Hall", "Black Star Arena"),
            ["Casablanca"] = ("La Medina", "Palais Mohammedia", "Atlantique Arena"),
            ["Dakar"] = ("L'Arène Sablé", "Palais des Sports Dakar", "Teranga Grand Arena"),

            // Middle East
            ["Abu Dhabi"] = ("The Majlis Room", "Corniche Hall", "The Falcon Dome"),
            ["Dubai"] = ("The Warehouse District", "Creek Hall", "Skyline Grand Arena"),
            ["Riyadh"] = ("The Diriyah Room", "Kingdom Hall", "Desert Crown Arena"),
            ["Doha"] = ("The Souq Room", "Pearl Hall", "Al Bayt Grand Arena"),
            ["Manama"] = ("The Harbour Room", "Pearl Diver Hall", "Bahrain Bay Arena"),
            ["Amman"] = ("The Citadel Room", "Al-Hussein Hall", "Petra Grand Arena"),
            ["Istanbul"] = ("The Bosphorus Room", "Beşiktaş Hall", "Golden Horn Arena"),
        };
}
