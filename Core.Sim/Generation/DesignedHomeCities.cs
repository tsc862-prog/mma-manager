using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Generation;

/// <summary>
/// Where the authored people are FROM, by full name ("First Last" — the same
/// strict key the bond resolver prefers, so a shared surname never lands on the
/// wrong person). Applied to designed fighters and legends BEFORE the hometown
/// roll, which then returns early: that roll runs on a fresh per-fighter keyed
/// stream (<c>FighterGenerator.OriginRng</c>), never on a shared one, so skipping
/// it changes nobody else's world. A city the atlas doesn't have is a no-op —
/// the fighter keeps the rolled hometown.
///
/// Only atlas cities can be named (adding a city to the atlas draws from the
/// builder stream and reshuffles every seed). Where the bio's town isn't in the
/// atlas, the nearest atlas city stands in and the bio keeps the real town in
/// prose (Detroit → Chicago, Osaka → Tokyo, the Baltic → Stockholm).
///
/// Append-only during a feature, like every other authored table.
/// </summary>
public static class DesignedHomeCities
{
    public static IReadOnlyDictionary<string, string> All { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // --- AFC ---------------------------------------------------------
            ["Diego Fuentes"] = "Buenos Aires",       // a Buenos Aires academy owner's son
            ["Kenji Morita"] = "Tokyo",               // Yokohama
            ["Maximo Duarte"] = "Sao Paulo",          // São Paulo building sites
            ["Joaquin Vela"] = "Santiago",            // a Santiago street kid
            ["Samir Al-Rashid"] = "Amman",            // Amman shuts down when he fights
            ["Katya Volkova"] = "Moscow",             // a Moscow boxing stylist
            ["Amanda Silva"] = "Curitiba",            // Curitiba royalty

            // --- VMMA --------------------------------------------------------
            ["Callum Brody"] = "Manchester",          // a Manchester council-estate kid
            ["Ingrid Bergström"] = "Stockholm",       // a Stockholm firefighter's daughter

            // --- ICF ---------------------------------------------------------
            ["Somchai Rattanak"] = "Bangkok",         // Bangkok temple fairs
            ["Daichi Sato"] = "Tokyo",                // Osaka gymnasiums (Osaka isn't in the atlas)
            ["Rohan Malhotra"] = "Mumbai",            // Mumbai's first true MMA star
            ["Ji-ho Kang"] = "Seoul",                 // Busan (not in the atlas)
            ["Yuna Park"] = "Seoul",                  // a Seoul judo prodigy

            // --- Cage Kings --------------------------------------------------
            ["Mateo Quiroga"] = "Buenos Aires",       // a Buenos Aires prodigy
            ["Gustavo Leal"] = "Curitiba",            // a Paraná farm kid (Curitiba is Paraná's capital)
            ["Wallace Bandeira"] = "Rio de Janeiro",  // a former Rio nightclub doorman
            ["Larissa Mendes"] = "Sao Paulo",         // a São Paulo physiotherapy student

            // --- GFC ---------------------------------------------------------
            ["Tariq Nasser"] = "Riyadh",              // Riyadh's national combat-sports project
            ["Khalid Al-Suwaidi"] = "Dubai",          // the Falcon of Dubai

            // --- IFA ---------------------------------------------------------
            ["Dmitri Volkov"] = "Moscow",             // the Volk Sambo Club's own
            ["Kirill Baranov"] = "Moscow",            // a Perm youth club (Perm isn't in the atlas); drilled in Moscow

            // --- RCS ---------------------------------------------------------
            ["Rafael Souza"] = "Rio de Janeiro",      // the hurricane of Rocinha
            ["Bruno Carvalho"] = "Rio de Janeiro",    // Ipanema beach boxing
            ["Vinicius Rocha"] = "Rio de Janeiro",    // Niterói, across the bay
            ["Caio Siqueira"] = "Rio de Janeiro",     // Niterói; Rio's belt stays home

            // --- UFL ---------------------------------------------------------
            ["Cole Jackson"] = "Los Angeles",         // an LA stuntman
            ["Damon Pierce"] = "Chicago",             // a Chicago union laborer

            // --- SCM ---------------------------------------------------------
            ["Kai Marsh"] = "Brisbane",               // a Gold Coast lifeguard
            ["Jack Rennie"] = "Perth",                // a Perth mining-site brawler
            ["Manaia Rahiri"] = "Auckland",           // an Auckland scaffolder
            ["Sione Tuilagi"] = "Auckland",           // Tonga's hope, fighting out of Auckland's islands community

            // --- The regional feeders and local halls ------------------------
            ["Tyshawn Cole"] = "New York",            // Baltimore (not in the atlas)
            ["Chidi Okafor"] = "Lagos",               // Surulere
            ["Kofi Mensah"] = "Accra",                // Accra's contribution to the project
            ["Wyatt Halloran"] = "Chicago",           // Dubuque, Iowa — the Midwest's buckle
            ["DeShawn Pryor"] = "Chicago",            // Milwaukee
            ["Haruki Nakamura"] = "Tokyo",            // Kansai (Osaka isn't in the atlas)
            ["Rin Takeda"] = "Tokyo",                 // Osaka's darling (Osaka isn't in the atlas)
            ["Ayaka Kondo"] = "Tokyo",                // Osaka (not in the atlas)
            ["Mateo Quispe"] = "Lima",                // the Sacred Valley
            ["Alonso Huanca"] = "Lima",               // El Alto (not in the atlas)
            ["Rico Barone"] = "Chicago",              // Detroit (not in the atlas)
            ["Jamal Weeks"] = "Chicago",              // Detroit's east side (not in the atlas)
            ["Tomas Kazlauskas"] = "Stockholm",       // Vilnius (no Baltic city in the atlas)
            ["Ivars Ozols"] = "Stockholm",            // Riga (no Baltic city in the atlas)
            ["Dovydas Urbonas"] = "Stockholm",        // Kaunas (no Baltic city in the atlas)
            ["Somchai Chaiya"] = "Bangkok",           // Chiang Mai stadiums (not in the atlas)
            ["Arthit Saengkham"] = "Bangkok",         // the golden boy of the Mekong
            ["Sipho Ndlovu"] = "Cape Town",           // Khayelitsha
            ["Thabo Mokoena"] = "Cape Town",          // Gugulethu
            ["Lucas Ferreyra"] = "Buenos Aires",      // Buenos Aires sings his name

            // --- Free agents -------------------------------------------------
            ["Fatima Diallo"] = "Dakar",              // Dakar's secret
            ["Diego Rivas"] = "Buenos Aires",         // the name still draws in Buenos Aires

            // --- Legends (where they live now) -------------------------------
            ["Randall Boyd"] = "Las Vegas",           // runs Apex Combat Club
            ["Danielle Cross"] = "Las Vegas",         // the Original: built the division where the AFC was born
            ["Nathan Kroll"] = "Chicago",             // runs Iron Range Wrestling
            ["Paulo Bandeira"] = "Rio de Janeiro",    // went home to Rio
            ["Hendrik Vos"] = "Amsterdam",            // holds the door at Low Country
            ["Alfie Rooke"] = "Manchester",           // Ironside, above the old boxing gym
            ["Margaux Bisset"] = "Paris",             // French television
            ["Yuji Matsuda"] = "Tokyo",               // Kobudo Dojo
            ["Chatchai Wongsawat"] = "Bangkok",       // took the belt home to Bangkok
            ["Waldemar Pires"] = "Sao Paulo",         // the room he built in São Paulo
            ["Ronaldo Vieira"] = "Curitiba",          // runs the Curitiba room
            ["Grigori Antonov"] = "Moscow",           // the sambo club that made him
            ["Bryce Kilgore"] = "Montreal",           // coaches in Montreal now
            ["Michiko Arai"] = "Tokyo",               // Kansai (Osaka isn't in the atlas)
            ["Petrus van Zyl"] = "Johannesburg",      // built Highveld
            ["Emeka Obiora"] = "Lagos",               // came back to Lagos
            ["Fadi Haddad"] = "Amman",                 // Beirut is not in the atlas; the nearest Levant fight town
            ["Damon Pruitt"] = "Las Vegas",           // Oklahoma is not in the atlas; the nearest fight town
            ["Teodor Lund"] = "Stockholm",            // Bergen is not in the atlas
            ["Kwabena Mensah"] = "Accra",             // came home with the belt and very little else
            ["Elias Varga"] = "Warsaw",               // Debrecen is not in the atlas
            ["Shinobu Tachikawa"] = "Tokyo",          // Tuesdays at the Kobudo Dojo
            ["Seok-hwan Ahn"] = "Seoul",              // the crowd that still sings
        };

    /// <summary>The atlas city an authored person calls home, if the table names one the world has.</summary>
    public static CityId? Resolve(World world, string first, string last) =>
        All.TryGetValue($"{first} {last}", out var name)
            ? world.Cities.Values
                .Where(c => string.Equals(c.Name, name, StringComparison.Ordinal))
                .OrderBy(c => c.Id.Value)
                .Select(c => (CityId?)c.Id)
                .FirstOrDefault()
            : null;
}
