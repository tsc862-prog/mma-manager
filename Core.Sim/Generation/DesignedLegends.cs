using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Generation;

/// <summary>
/// A reign a legend held: whose belt, which division, and when — expressed in
/// YEARS BEFORE the world's start date so the sport's history slides with
/// whatever date a new game opens on.
/// </summary>
public sealed record DesignedReign(
    string Org, WeightClass WeightClass, int WonYearsAgo, int LostYearsAgo, int Defenses,
    Sex Sex = Sex.Male);

/// <summary>
/// A retired great from the era before the player arrived: the names on the
/// belts' lineages, the faces in the camps' offices, and the standard today's
/// champions are measured against.
/// </summary>
public sealed record DesignedLegend(
    string First, string Last, string? Nickname,
    Region Region, string Country, WeightClass WeightClass, PrimaryStyle Style,
    int AgeNow, int Wins, int Losses, int Popularity,
    DesignedReign[] Reigns,
    // The designed camp this one runs today (by name). They become its head
    // coach, and a room with a name like that on the door coaches a little better.
    string? HeadCoachOf = null,
    Sex Sex = Sex.Male,
    string? Bio = null);

/// <summary>
/// THE ERA BEFORE. Every belt in the designed world is supposed to have made
/// legends — this is who they were. These fighters enter the world RETIRED, so
/// they never take a mat or a roster slot; what they do is give the sport a
/// past: their reigns sit at the head of the title lineages (so a belt reads as
/// a line of succession rather than one man's souvenir), they run the famous
/// rooms, and today's designed fighters are written as their heirs — Sato
/// inherited Matsuda's division, Takeda grew up watching Arai, Volkova holds
/// the belt Cross had to fight the promoters to create.
///
/// Only the ESTABLISHED houses get one. The Gulf's oil-money project, Rio's
/// upstart and the African league are new by design and have no history to
/// claim — their belts are supposed to feel young.
/// </summary>
public static class DesignedLegends
{
    /// <summary>
    /// Who the retired greats ARE, by full name ("First Last" — Paulo Bandeira
    /// shares a surname with a living champion). Authored for the same reason the
    /// active roster's are: left to the roll, a legend whose bio says "has never
    /// once raised his voice" came out a Hothead on some seeds. Applied through
    /// <see cref="Personality.ApplyTo"/>, which is hash-derived — it costs the
    /// generation stream nothing.
    /// </summary>
    public static IReadOnlyDictionary<string, Disposition> Dispositions { get; } =
        new Dictionary<string, Disposition>(StringComparer.Ordinal)
        {
            ["Randall Boyd"] = Disposition.QuietProfessional,     // makes everyone sweep the mats; says little
            ["Emiliano Vasquez"] = Disposition.Showman,           // Mexican flags in American arenas, the first pay-per-view lightweight
            ["Nathan Kroll"] = Disposition.Loudmouth,             // the sermons he gave referees
            ["Paulo Bandeira"] = Disposition.QuietProfessional,   // quietly building other people's champions
            ["Danielle Cross"] = Disposition.Loudmouth,           // argued a division into existence
            ["Hendrik Vos"] = Disposition.QuietProfessional,      // holds the gym door open, does not miss it
            ["Alfie Rooke"] = Disposition.Warhorse,               // delivered every time, for plumber's wages
            ["Margaux Bisset"] = Disposition.Showman,             // makes it look easy; French television now
            ["Yuji Matsuda"] = Disposition.Showman,               // the dome show a nation measures its holidays by
            ["Chatchai Wongsawat"] = Disposition.QuietProfessional, // has never once raised his voice
            ["Yerlan Amanov"] = Disposition.Student,              // title fights studied like chess games
            ["Waldemar Pires"] = Disposition.Warhorse,            // the last champion of the dust
            ["Osvaldo Ferraz"] = Disposition.QuietProfessional,   // never hurried, never panicked
            ["Ronaldo Vieira"] = Disposition.Hothead,             // not one dull second
            ["Hugo Sosa"] = Disposition.Warhorse,                 // nine years of being the man everyone beat
            ["Grigori Antonov"] = Disposition.Warhorse,           // considers modern warm-ups an indulgence
            ["Marek Baran"] = Disposition.CompanyMan,             // a fixture in the IFA corner sections
            ["Bobby Deene"] = Disposition.Warhorse,               // four defenses and not one call from the AFC
            ["Bryce Kilgore"] = Disposition.Warhorse,             // every short-notice fight the league offered
            ["Tama Solomona"] = Disposition.Warhorse,             // pads six days a week; "the boy"
            ["Michiko Arai"] = Disposition.CompanyMan,            // the same two thousand people, the house's whole reason
            ["Petrus van Zyl"] = Disposition.Student,             // checking his own arithmetic
            ["Emeka Obiora"] = Disposition.Warhorse,              // a bad hip and a bag of pads
        };

    public static IReadOnlyList<DesignedLegend> All { get; } = new DesignedLegend[]
    {
        // ============ AFC — the summit's founding era ============
        new("Randall", "Boyd", "Stonewall", Region.NorthAmerica, "USA", WeightClass.Heavyweight, PrimaryStyle.WellRounded,
            AgeNow: 48, Wins: 24, Losses: 6, Popularity: 88,
            Reigns: new[] { new DesignedReign("AFC", WeightClass.Heavyweight, 19, 13, 7) },
            HeadCoachOf: "Apex Combat Club",
            Bio: "The man who made the AFC respectable. Boyd came out of the casino-ballroom years — no gloves worth the name, no commission, no money — and held the heavyweight belt through seven defenses while the sport learned to call itself a sport. He runs Apex Combat Club now and still makes every fighter in the building sweep the mats."),
        new("Emiliano", "Vasquez", "El Soberano", Region.NorthAmerica, "MEX", WeightClass.Lightweight, PrimaryStyle.Striker,
            AgeNow: 46, Wins: 27, Losses: 5, Popularity: 85,
            Reigns: new[] { new DesignedReign("AFC", WeightClass.Lightweight, 16, 11, 6) },
            HeadCoachOf: "Sunset Striking Lab",
            Bio: "Six defenses, five knockdowns in one round, and a decade of Mexican flags in American arenas. Vasquez was the first lightweight the AFC built a pay-per-view around, and whoever holds that belt is asked, on camera, whether they could have beaten him."),
        new("Nathan", "Kroll", "Old Testament", Region.NorthAmerica, "USA", WeightClass.Middleweight, PrimaryStyle.Wrestler,
            AgeNow: 50, Wins: 22, Losses: 8, Popularity: 74,
            Reigns: new[] { new DesignedReign("AFC", WeightClass.Middleweight, 18, 14, 4) },
            HeadCoachOf: "Iron Range Wrestling",
            Bio: "Four defenses of the ugliest kind, and a nickname earned for the sermons he gave referees. Kroll's fights were unwatchable and his title run was immaculate; the sport spent four years hoping someone would beat him and nobody did until his knees managed it."),
        new("Paulo", "Bandeira", "Mão de Pedra", Region.SouthAmerica, "BRA", WeightClass.Welterweight, PrimaryStyle.BrazilianJiuJitsu,
            AgeNow: 48, Wins: 25, Losses: 4, Popularity: 82,
            Reigns: new[] { new DesignedReign("AFC", WeightClass.Welterweight, 17, 12, 8) },
            HeadCoachOf: "Bandeira Jiu-Jitsu",
            Bio: "Eight defenses, a game built to finish on the mat, and the third generation of his family to run the academy that carries their name. Bandeira never lost the belt — he vacated it at thirty-six, went home to Rio, and has been quietly building other people's champions ever since."),
        new("Danielle", "Cross", "The Original", Region.NorthAmerica, "USA", WeightClass.Bantamweight, PrimaryStyle.Brawler,
            AgeNow: 41, Wins: 18, Losses: 4, Popularity: 79, Sex: Sex.Female,
            Reigns: new[] { new DesignedReign("AFC", WeightClass.Bantamweight, 12, 8, 5, Sex.Female) },
            Bio: "There was no women's division until Cross spent two years embarrassing the promoters who said there was no market. She won the first belt, defended it five times, and retired the moment the sport stopped treating her division as an experiment. Everything Volkova headlines today, Cross argued into existence."),

        // ============ VMMA — the establishment's own past ============
        new("Hendrik", "Vos", "Zwaarhand", Region.Europe, "NED", WeightClass.Heavyweight, PrimaryStyle.Kickboxer,
            AgeNow: 47, Wins: 23, Losses: 7, Popularity: 72,
            Reigns: new[] { new DesignedReign("VMMA", WeightClass.Heavyweight, 15, 10, 5) },
            HeadCoachOf: "Low Country Kickboxing",
            Bio: "Europe's heavyweight for five years and the last man to make the low kick a title-fight weapon. Vos holds the gym door open at Low Country now and has told three separate documentary crews that he does not miss it."),
        new("Alfie", "Rooke", "The Postman", Region.Europe, "GBR", WeightClass.Bantamweight, PrimaryStyle.WellRounded,
            AgeNow: 45, Wins: 20, Losses: 7, Popularity: 58,
            Reigns: new[] { new DesignedReign("VMMA", WeightClass.Bantamweight, 13, 9, 3) },
            HeadCoachOf: "Ironside MMA",
            Bio: "He delivered, every time, for wages that would embarrass a plumber — the most reliable bantamweight of his generation and the least celebrated. Rooke bought the room above the old boxing gym with his last three purses and turned it into Ironside."),
        new("Margaux", "Bisset", "La Lame", Region.Europe, "FRA", WeightClass.Flyweight, PrimaryStyle.Kickboxer,
            AgeNow: 40, Wins: 17, Losses: 3, Popularity: 61, Sex: Sex.Female,
            Reigns: new[] { new DesignedReign("VMMA", WeightClass.Flyweight, 11, 7, 4, Sex.Female) },
            Bio: "A savate technician who made Europe's women's flyweight belt worth winning and then made it look easy. Bisset does French television now and remains the only person Vanguard's matchmakers admit to having been afraid of."),

        // ============ ICF — belts with lineage older than most promotions ============
        new("Yuji", "Matsuda", "Raijin", Region.Asia, "JPN", WeightClass.Lightweight, PrimaryStyle.WellRounded,
            AgeNow: 51, Wins: 31, Losses: 8, Popularity: 86,
            Reigns: new[] { new DesignedReign("ICF", WeightClass.Lightweight, 20, 14, 9) },
            HeadCoachOf: "Kobudo Dojo",
            Bio: "Nine defenses, six years, and a New Year dome show that a nation still measures its holidays by. Matsuda is the reason the ICF lightweight belt is the one Japanese fighters actually want — and the reason Daichi Sato has never seriously considered leaving it."),
        new("Chatchai", "Wongsawat", "The Old Temple", Region.Asia, "THA", WeightClass.Featherweight, PrimaryStyle.Kickboxer,
            AgeNow: 49, Wins: 42, Losses: 11, Popularity: 70,
            Reigns: new[] { new DesignedReign("ICF", WeightClass.Featherweight, 18, 13, 6) },
            HeadCoachOf: "Emerald Tiger",
            Bio: "Three hundred stadium fights before the cage, then five years of taking the ICF's featherweight belt home to Bangkok. He teaches at Emerald Tiger at five every morning and has never once raised his voice."),
        new("Yerlan", "Amanov", "The Long Winter", Region.Asia, "KAZ", WeightClass.Welterweight, PrimaryStyle.Wrestler,
            AgeNow: 48, Wins: 26, Losses: 6, Popularity: 64,
            Reigns: new[] { new DesignedReign("ICF", WeightClass.Welterweight, 17, 12, 5) },
            Bio: "Five defenses, all by decision, all of them twenty-five minutes long. Amanov's title fights are studied on the steppe the way other countries study chess games."),

        // ============ Cage Kings — Brazil's grand old house ============
        new("Waldemar", "Pires", "O Leão de Aço", Region.SouthAmerica, "BRA", WeightClass.Heavyweight, PrimaryStyle.Grappler,
            AgeNow: 56, Wins: 29, Losses: 9, Popularity: 80,
            Reigns: new[] { new DesignedReign("CK", WeightClass.Heavyweight, 24, 18, 6) },
            HeadCoachOf: "Aço Paulista",
            Bio: "The last champion of the dust — Pires fought vale tudo on packed earth before Cage Kings had a cage, and carried the heavyweight belt into the arena era on his back. The room he built in São Paulo is named for what the newspapers called him."),
        new("Osvaldo", "Ferraz", "Vovô", Region.SouthAmerica, "BRA", WeightClass.Lightweight, PrimaryStyle.BrazilianJiuJitsu,
            AgeNow: 52, Wins: 27, Losses: 7, Popularity: 72,
            Reigns: new[] { new DesignedReign("CK", WeightClass.Lightweight, 21, 16, 7) },
            Bio: "They called him grandfather at twenty-eight because he never hurried, never panicked, and always found the back. Seven defenses, and a generation of Brazilian lightweights who all learned the same choke."),
        new("Ronaldo", "Vieira", "Britadeira", Region.SouthAmerica, "BRA", WeightClass.Middleweight, PrimaryStyle.Brawler,
            AgeNow: 47, Wins: 24, Losses: 12, Popularity: 55,
            Reigns: new[] { new DesignedReign("CK", WeightClass.Middleweight, 16, 12, 3) },
            HeadCoachOf: "Furia Curitiba",
            Bio: "Twelve losses, three title defenses, and not one dull second in any of them. Vieira runs the Curitiba room now on the theory that got him the belt: whoever is still standing at the end was right."),
        new("Hugo", "Sosa", "El Yunque Viejo", Region.SouthAmerica, "ARG", WeightClass.Featherweight, PrimaryStyle.Grappler,
            AgeNow: 48, Wins: 21, Losses: 9, Popularity: 45,
            Reigns: new[] { new DesignedReign("CK", WeightClass.Featherweight, 17, 13, 3) },
            HeadCoachOf: "Monte Andino MMA",
            Bio: "The old anvil: Argentina's first Cage Kings champion, who won the belt at thirty-one after nine years of being the man everyone beat on their way up. He takes the Monte Andino fighters to altitude every camp because that is where he finally got fit enough to win."),

        // ============ IFA — the hard school's founders ============
        new("Grigori", "Antonov", "The Foundry", Region.Europe, "RUS", WeightClass.Heavyweight, PrimaryStyle.Grappler,
            AgeNow: 50, Wins: 24, Losses: 9, Popularity: 58,
            Reigns: new[] { new DesignedReign("IFA", WeightClass.Heavyweight, 17, 12, 5) },
            HeadCoachOf: "Volk Sambo Club",
            Bio: "Ironhold's first heavyweight champion, from the years when the purses came out of an envelope and the cage was borrowed. Antonov coaches at the sambo club that made him and considers modern warm-ups an indulgence."),
        new("Marek", "Baran", "Cement", Region.Europe, "POL", WeightClass.LightHeavyweight, PrimaryStyle.Wrestler,
            AgeNow: 46, Wins: 22, Losses: 8, Popularity: 52,
            Reigns: new[] { new DesignedReign("IFA", WeightClass.LightHeavyweight, 14, 10, 4) },
            Bio: "Four defenses in four Silesian sports halls, none of them televised outside Poland. Baran is a fixture in the IFA corner sections and has never been seen at a VMMA show."),

        // ============ UFL — the ones the summit never called ============
        new("Bobby", "Deene", "Second Prize", Region.NorthAmerica, "USA", WeightClass.Welterweight, PrimaryStyle.Brawler,
            AgeNow: 44, Wins: 21, Losses: 9, Popularity: 55,
            Reigns: new[] { new DesignedReign("UFL", WeightClass.Welterweight, 12, 8, 4) },
            Bio: "Four defenses of the UFL welterweight belt and not one phone call from the AFC — the nickname started as a joke he made about himself and stuck hard enough to hurt. Every prospect the UFL signs gets told his story on day one, for one reason or another."),
        new("Bryce", "Kilgore", "Last Bus", Region.NorthAmerica, "CAN", WeightClass.Lightweight, PrimaryStyle.WellRounded,
            AgeNow: 46, Wins: 23, Losses: 10, Popularity: 52,
            Reigns: new[] { new DesignedReign("UFL", WeightClass.Lightweight, 14, 10, 3) },
            HeadCoachOf: "Northern Lights MMA",
            Bio: "He took every short-notice fight the league offered for years and won the belt on the last one anybody expected him to. Kilgore coaches in Montreal now and will still, at forty-six, answer the phone on a Thursday."),

        // ============ Southern Cross — the island export ============
        new("Tama", "Solomona", "Harbour Light", Region.Oceania, "NZL", WeightClass.Heavyweight, PrimaryStyle.Brawler,
            AgeNow: 47, Wins: 19, Losses: 6, Popularity: 60,
            Reigns: new[] { new DesignedReign("SCM", WeightClass.Heavyweight, 15, 10, 5) },
            HeadCoachOf: "Harbour City Fight Team",
            Bio: "Five defenses and the first fighter the northern hemisphere ever flew south to face. Solomona holds pads at the Sydney shed six days a week and refers to every heavyweight he trains as 'the boy'."),

        // ============ Kansai Cage — the heart of the women's scene ============
        new("Michiko", "Arai", "First Light", Region.Asia, "JPN", WeightClass.Strawweight, PrimaryStyle.Striker,
            AgeNow: 43, Wins: 20, Losses: 5, Popularity: 58, Sex: Sex.Female,
            Reigns: new[] { new DesignedReign("KAC", WeightClass.Strawweight, 13, 9, 6, Sex.Female) },
            Bio: "Six defenses in a room that holds two thousand people, in front of the same two thousand people. Arai is the reason Kansai Cage runs women's divisions at all, and Rin Takeda has been carrying her bags since she was fourteen."),

        // ============ The uncrowned — famous without a belt ============
        new("Petrus", "van Zyl", "Thornveld", Region.Africa, "RSA", WeightClass.Middleweight, PrimaryStyle.Striker,
            AgeNow: 46, Wins: 19, Losses: 8, Popularity: 48,
            Reigns: Array.Empty<DesignedReign>(),
            HeadCoachOf: "Highveld Fight Club",
            Bio: "The best African fighter of his generation, born ten years too early: no continental promotion worth fighting for, no visa when the offers finally came. Van Zyl built Highveld so the next one wouldn't have to leave, and watches Chidi Okafor's fights like a man checking his own arithmetic."),
        new("Emeka", "Obiora", "Talking Drum", Region.Africa, "NGA", WeightClass.Heavyweight, PrimaryStyle.Brawler,
            AgeNow: 49, Wins: 17, Losses: 11, Popularity: 44,
            Reigns: Array.Empty<DesignedReign>(),
            HeadCoachOf: "Sahel Warriors",
            Bio: "Seventeen wins on four continents for money that never once got him home in comfort. Obiora came back to Lagos with a bad hip and a bag of pads, and the room he opened has a leaking roof and a waiting list."),
    };
}
