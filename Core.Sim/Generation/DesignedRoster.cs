using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Generation;

/// <summary>
/// A hand-designed fighter in the initial database: identity, archetype, level,
/// record and (optionally) which promotion they start with and whether they hold
/// its belt. The generator fills in everything beneath the design — the full
/// skill spread, tendencies, measurables, potential — deterministically.
/// Country is the atlas code ("GEO"); Region stays as its continental rollup.
/// </summary>
public sealed record DesignedFighter(
    string First, string Last, string? Nickname,
    Region Region, WeightClass WeightClass, PrimaryStyle Style,
    int Age, int Overall, int Wins, int Losses,
    string? Org = null, bool Champion = false, int? Popularity = null,
    string? Country = null, int? Loyalty = null, int? Streak = null,
    Sex Sex = Sex.Male,
    // Day-one bad blood: the LAST NAME of another designed fighter this one
    // already can't stand — world build seeds the rivalry at real heat, so the
    // matchmaker and the box office have stories to chew on from the first card.
    string? RivalOf = null,
    // Authored backstory prose, shown on the fighter profile ahead of the
    // generated scouting notes. Who they are before the sim takes over.
    string? Bio = null);

/// <summary>
/// The designed initial database — the memorable top of the fictional world:
/// champions and contenders across the established promotions, plus free agents
/// with story hooks. Hand-tuned to exploit the sim's systems — loyalty (franchise
/// lifers the giants can't afford vs GFC's hired guns and poachable prospects),
/// MMA interest (a homegrown hero propping up a cool market), head-to-head turf
/// wars (VMMA vs IFA, Cage Kings vs RCS), and win-streak stardom. Every designed
/// fighter carries an authored BIO — the backstory the Biographer leads with
/// before appending the live career facts. Only the *start* of a new game is
/// designed; depth talent and every newgen after day one come from the
/// procedural <see cref="FighterGenerator"/>.
/// </summary>
public static class DesignedRoster
{
    public static IReadOnlyList<DesignedFighter> All { get; } = new DesignedFighter[]
    {
        // ============ AFC — the summit (champions) ============
        new("Diego", "Fuentes", "Relámpago", Region.SouthAmerica, WeightClass.Strawweight, PrimaryStyle.BrazilianJiuJitsu, 29, 86, 17, 2, "AFC", Champion: true, Popularity: 62, Country: "ARG",
            Bio: "The son of a Buenos Aires academy owner who paid the bills teaching schoolkids, Fuentes was rolling before he could read and treats the back-take like a birthright. He arrived at the AFC an unheralded import and blitzed the strawweight division inside two years."),
        new("Kenji", "Morita", "Silent Storm", Region.Asia, WeightClass.Flyweight, PrimaryStyle.WellRounded, 27, 87, 15, 1, "AFC", Champion: true, Popularity: 68, Country: "JPN",
            Bio: "Morita barely speaks at press conferences and has never needed to — a Shooto prodigy out of Yokohama who was beating grown men at seventeen. The quietest man in the sport, and at flyweight, the most complete."),
        new("Tyrell", "Boone", "Showtime", Region.NorthAmerica, WeightClass.Bantamweight, PrimaryStyle.Striker, 28, 88, 16, 2, "AFC", Champion: true, Popularity: 74, Country: "USA",
            Bio: "A former high-school point guard from Atlanta who found the boxing gym after a knee injury ended the hoop dreams. Boone fights the way the nickname demands — hands low, chin high, crowd on its feet."),
        new("Cian", "O'Rourke", "The Hound", Region.Europe, WeightClass.Featherweight, PrimaryStyle.Kickboxer, 26, 88, 14, 0, "AFC", Champion: true, Popularity: 79, Country: "IRL",
            Bio: "Dublin-born and pub-famous by twenty, O'Rourke turned a national kickboxing title into the loudest arrival featherweight has ever seen. He predicts rounds, calls shots, and so far the fights keep agreeing with him."),
        new("Dane", "Mercer", "Ice Water", Region.NorthAmerica, WeightClass.Lightweight, PrimaryStyle.WellRounded, 30, 90, 21, 3, "AFC", Champion: true, Popularity: 85, Country: "USA",
            Bio: "The face of the sport: a small-town Ohio kid who worked night shifts at a meatpacking plant between his first pro fights. Nothing rattles him — hostile crowds, bad rounds, championship pressure — hence the name."),
        new("Sefu", "Okonjo", "The Lion", Region.Africa, WeightClass.Welterweight, PrimaryStyle.Wrestler, 29, 89, 18, 1, "AFC", Champion: true, Popularity: 77, Country: "NGA",
            Bio: "Okonjo wrestled barefoot on packed dirt in Kano before a university scholarship added technique to the strength. He sends half of every purse home and insists the belt lives in Nigeria even when he defends it on the far side of the world."),
        new("Viktor", "Roth", "The Surgeon", Region.Europe, WeightClass.Middleweight, PrimaryStyle.Striker, 31, 89, 19, 4, "AFC", Champion: true, Popularity: 72, Country: "GER",
            Bio: "A former engineering student from Stuttgart who deconstructs opponents the way he once stripped engines — methodically, joint by joint. Roth doesn't produce highlight reels; he produces autopsies."),
        new("Marcus", "Hale", "Warpath", Region.NorthAmerica, WeightClass.LightHeavyweight, PrimaryStyle.Wrestler, 32, 87, 17, 5, "AFC", Champion: true, Popularity: 66, Country: "USA",
            Bio: "A two-time NCAA All-American who spent four years grinding the regional circuit because promoters called his style unwatchable. Hale kept winning until the style became the blueprint and the blueprint became a belt."),
        new("Maximo", "Duarte", "The Monolith", Region.SouthAmerica, WeightClass.Heavyweight, PrimaryStyle.Grappler, 33, 88, 18, 2, "AFC", Champion: true, Popularity: 81, Country: "BRA",
            Bio: "Duarte dug foundations on São Paulo building sites while collecting grappling world titles nobody paid him for. The sport's scariest clinch, wrapped around the sport's gentlest man."),

        // ============ AFC — the men chasing them (#1 contenders) ============
        new("Rizal", "Putra", "The Mongoose", Region.Asia, WeightClass.Strawweight, PrimaryStyle.Grappler, 25, 82, 12, 1, "AFC", Popularity: 41, Country: "INA",
            Bio: "Discovered at a pencak silat exhibition in Jakarta, Putra scrambles exactly like the animal on his fight shorts. He is the first Indonesian to crack an AFC title picture, and the whole archipelago has noticed."),
        new("Joaquin", "Vela", "Loco", Region.SouthAmerica, WeightClass.Flyweight, PrimaryStyle.Brawler, 26, 82, 13, 3, "AFC", Popularity: 48, Country: "CHI",
            Bio: "Vela fights exactly as advertised: a Santiago street kid who treats every round like an insult that needs answering. Coaches keep trying to add defense; crowds keep paying them not to."),
        new("Emil", "Vestergaard", "The Metronome", Region.Europe, WeightClass.Bantamweight, PrimaryStyle.Kickboxer, 27, 84, 14, 2, "AFC", Popularity: 45, Country: "DEN",
            Bio: "A Copenhagen jazz drummer's son who counts rhythm the way other fighters count reach. Vestergaard's output never spikes and never dips — it just keeps landing until the other man stops."),
        new("Tendai", "Moyo", "Nightfall", Region.Africa, WeightClass.Featherweight, PrimaryStyle.Striker, 24, 83, 11, 0, "AFC", Popularity: 52, Country: "ZIM",
            Bio: "Moyo came out of Harare's amateur boxing program with the fastest hands the scouts had ever timed and a switch-hitting style nobody remembers teaching him. Eleven fights, none lost, and the scouts have stopped timing the rounds."),
        // #1 contender with real history with the champ — the lightweight title
        // picture opens as a GRUDGE (Mercer took a razor-thin decision off him).
        new("Jae-won", "Seo", "Steel", Region.Asia, WeightClass.Lightweight, PrimaryStyle.Wrestler, 28, 85, 16, 2, "AFC", Popularity: 55, Country: "KOR", RivalOf: "Mercer",
            Bio: "A wrestling silver medallist for Korea who worked steel-mill shifts as a teenager to fund the singlets — and never lost the shift-worker's grudge. Mercer took a razor-thin decision off him in a title fight; Seo has replayed it every day since."),
        new("Cole", "Vandermeer", "Bad News", Region.NorthAmerica, WeightClass.Welterweight, PrimaryStyle.Kickboxer, 27, 84, 13, 1, "AFC", Popularity: 58, Country: "CAN",
            Bio: "A Winnipeg hockey enforcer who discovered he liked the fights more than the ice time. Vandermeer's left high kick has its own highlight package, and his press conferences need a censor."),
        new("Kane", "Te Ariki", "Riptide", Region.Oceania, WeightClass.Middleweight, PrimaryStyle.ClinchFighter, 29, 84, 15, 3, "AFC", Popularity: 49, Country: "NZL",
            Bio: "Raised in a Māori wrestling family in Rotorua, Te Ariki drags fights into the clinch and drowns people there. The haka his whānau leads before title fights has become appointment viewing."),
        new("Samir", "Al-Rashid", "The Falcon", Region.MiddleEast, WeightClass.LightHeavyweight, PrimaryStyle.WellRounded, 28, 83, 12, 2, "AFC", Popularity: 46, Country: "JOR",
            Bio: "A Jordanian army officer's son who trained across three continents before settling into a style with no holes. Amman shuts down when he fights."),
        new("Bram", "Kowalczyk", "The Bulldozer", Region.Europe, WeightClass.Heavyweight, PrimaryStyle.Brawler, 30, 83, 14, 4, "AFC", Popularity: 57, Country: "POL",
            Bio: "A former strongman competitor from Gdańsk who found out heavyweights fall over easier than atlas stones. Subtlety is not the product."),

        // ============ VMMA — Europe's stage (champions) ============
        new("Luka", "Petrovic", "The Spider", Region.Europe, WeightClass.Strawweight, PrimaryStyle.BrazilianJiuJitsu, 27, 77, 13, 2, "VMMA", Champion: true, Popularity: 51, Country: "SRB",
            Bio: "Long-limbed and endlessly patient, Petrovic learned his grappling in a Belgrade basement club with one mat and no heating. Opponents describe fighting him as being slowly webbed."),
        new("Mateus", "Farias", "Ginga", Region.SouthAmerica, WeightClass.Flyweight, PrimaryStyle.Striker, 26, 78, 12, 3, "VMMA", Champion: true, Popularity: 47, Country: "BRA",
            Bio: "A capoeira kid from Salvador whose rhythm never left him — Farias fights to music only he can hear. Europe adopted him the first time he wheel-kicked someone on a European card."),
        new("Callum", "Brody", "The Ghost", Region.Europe, WeightClass.Bantamweight, PrimaryStyle.WellRounded, 28, 79, 14, 2, "VMMA", Champion: true, Popularity: 55, Country: "GBR",
            Bio: "A Manchester council-estate kid who has been slipping punches since age six. Brody wins rounds opponents can't remember landing anything in."),
        new("Ilya", "Orlov", "Sledge", Region.Europe, WeightClass.Featherweight, PrimaryStyle.Brawler, 29, 78, 15, 4, "VMMA", Champion: true, Popularity: 58, Country: "RUS",
            Bio: "Orlov fought bare-knuckle in St Petersburg car parks before the sport gave him rules to almost follow. Featherweight power that makes light heavyweights flinch."),
        new("Noah", "Weiss", "Clockwork", Region.Europe, WeightClass.Lightweight, PrimaryStyle.Kickboxer, 27, 80, 13, 1, "VMMA", Champion: true, Popularity: 54, Country: "SUI",
            Bio: "A watchmaker's apprentice from Geneva — genuinely — who treats distance and timing as precision instruments. Weiss has lost once in fourteen fights; the defense is Swiss-engineered."),
        new("Andrei", "Codrea", "The Wolf", Region.Europe, WeightClass.Welterweight, PrimaryStyle.Wrestler, 30, 80, 17, 5, "VMMA", Champion: true, Popularity: 60, Country: "ROU",
            Bio: "A shepherd's son from the Carpathians who came up through Romania's junior wrestling system on black bread and stubbornness. Codrea hunts takedowns in packs and finishes what he catches."),
        new("Yusuf", "Demir", "Anatolia", Region.MiddleEast, WeightClass.Middleweight, PrimaryStyle.ClinchFighter, 29, 79, 14, 3, "VMMA", Champion: true, Popularity: 50, Country: "TUR",
            Bio: "An oil-wrestling champion from Edirne carrying six centuries of grip fighting into the cage. Once Demir's hands lock, the fight happens where he says it happens."),
        new("Henrik", "Dahl", "North Star", Region.Europe, WeightClass.LightHeavyweight, PrimaryStyle.Striker, 31, 78, 15, 6, "VMMA", Champion: true, Popularity: 52, Country: "NOR",
            Bio: "A late starter who spent his twenties as an Oslo firefighter, Dahl fights with the calm of a man who has walked into worse. His jab is Scandinavia's most reliable export."),
        // VMMA's franchise heavyweight — the Croatian Bear bleeds the house colours;
        // a real draw the AFC wants, but loyalty this deep money can barely touch.
        new("Goran", "Vukovic", "Bear", Region.Europe, WeightClass.Heavyweight, PrimaryStyle.Grappler, 32, 80, 17, 4, "VMMA", Champion: true, Popularity: 75, Country: "CRO", Loyalty: 85,
            Bio: "VMMA found Vukovic hauling nets on a Split fishing boat, and he has never forgotten it — he has turned down three AFC offers, publicly, and burned the third one on camera. Croatia's favourite son and the house's cornerstone."),

        // ============ VMMA — contenders with day-one heat ============
        // The lightweight picture opens as a needle match: the Marseille stylist
        // thinks the champion's belt is a watch that tells the wrong time.
        new("Théo", "Marchand", "Le Tranchant", Region.Europe, WeightClass.Lightweight, PrimaryStyle.Kickboxer, 27, 76, 14, 3, "VMMA", Popularity: 44, Country: "FRA", RivalOf: "Weiss",
            Bio: "A Marseille savate stylist who has said, loudly and often, that Weiss's title is a watch that tells the wrong time. Their staredown at last season's awards gala needed six security guards."),
        new("Xavi", "Ferrer", "El Matador", Region.Europe, WeightClass.Welterweight, PrimaryStyle.Striker, 26, 75, 12, 2, "VMMA", Popularity: 41, Country: "ESP",
            Bio: "A Barcelona amateur boxing standout who crossed over when the Olympic funding dried up. Ferrer's footwork is the reason VMMA's slow-motion cameras earn their keep."),

        // ============ ICF — Asia's established stage (champions) ============
        new("Somchai", "Rattanak", "The Viper", Region.Asia, WeightClass.Strawweight, PrimaryStyle.Kickboxer, 26, 75, 13, 2, "ICF", Champion: true, Popularity: 50, Country: "THA",
            Bio: "Two hundred muay thai fights before his twentieth birthday, most of them for meal money at Bangkok temple fairs. Rattanak's elbows arrive from angles the sport hasn't named yet."),
        new("Haruto", "Kubo", "Zero", Region.Asia, WeightClass.Flyweight, PrimaryStyle.BrazilianJiuJitsu, 25, 76, 12, 1, "ICF", Champion: true, Popularity: 53, Country: "JPN",
            Bio: "A former competitive gamer who took up jiu-jitsu to fix his posture and broke the Japanese grappling circuit instead. Kubo hunts submissions like frame-perfect inputs."),
        new("Arman", "Batyr", "Berkut", Region.Asia, WeightClass.Bantamweight, PrimaryStyle.Wrestler, 27, 77, 15, 2, "ICF", Champion: true, Popularity: 55, Country: "KAZ",
            Bio: "Raised in a wrestling dynasty on the Kazakh steppe — grandfather, father, three uncles, all champions. Batyr's takedowns are a family heirloom."),
        new("Minh", "Nguyen", "The Mantis", Region.Asia, WeightClass.Featherweight, PrimaryStyle.Striker, 26, 76, 14, 3, "ICF", Champion: true, Popularity: 49, Country: "VIE",
            Bio: "Ho Chi Minh City's first MMA export, built on traditional võ thuật and a jab like a bicycle spoke. He fights in front of a wall of Vietnamese flags wherever ICF books him."),
        // Japan's beloved son and ICF's franchise: a genuine draw the AFC has circled
        // for years, but he's a lifer — it would cost the earth to pry him from Tokyo.
        new("Daichi", "Sato", "Typhoon", Region.Asia, WeightClass.Lightweight, PrimaryStyle.WellRounded, 29, 80, 18, 3, "ICF", Champion: true, Popularity: 79, Country: "JPN", Loyalty: 88,
            Bio: "Sato sold out Osaka gymnasiums as a teenage karate phenom and has spent his career repaying the country's faith — every offer from abroad declined. Japan's beloved son, and he means to retire that way."),
        new("Bataar", "Ganbold", "Iron Horse", Region.Asia, WeightClass.Welterweight, PrimaryStyle.Wrestler, 29, 77, 17, 4, "ICF", Champion: true, Popularity: 51, Country: "MGL",
            Bio: "A herder's son from the Mongolian steppe who won Naadam wrestling titles before he ever saw a cage. Ganbold rides opponents for the full twenty-five minutes without once dismounting."),
        new("Rohan", "Malhotra", "The Tiger", Region.Asia, WeightClass.Middleweight, PrimaryStyle.Kickboxer, 28, 76, 14, 4, "ICF", Champion: true, Popularity: 48, Country: "IND",
            Bio: "Mumbai's first true MMA star, who quit a call-center job for a fight camp against his family's wishes. A hundred million Indians now watch him justify the decision."),
        new("Timur", "Zhaksylyk", "The Anvil", Region.Asia, WeightClass.LightHeavyweight, PrimaryStyle.Grappler, 30, 75, 15, 5, "ICF", Champion: true, Popularity: 47, Country: "KAZ",
            Bio: "A judo Olympian from Almaty who found the cage after federation politics found him. Zhaksylyk's top pressure has made hard men wave to referees."),
        new("Tavita", "Fetu", "Tsunami", Region.Oceania, WeightClass.Heavyweight, PrimaryStyle.Brawler, 31, 76, 14, 5, "ICF", Champion: true, Popularity: 52, Country: "SAM",
            Bio: "One of eleven siblings from a village outside Apia, Fetu hits like the weather system he's named for. The islands stop working when he fights."),

        // ============ ICF — the border grudge ============
        // Korea's counterpoint to Japan's beloved son: a title picture that sells
        // out two countries.
        new("Ji-ho", "Kang", "The Hammer of Busan", Region.Asia, WeightClass.Lightweight, PrimaryStyle.Wrestler, 27, 76, 14, 3, "ICF", Popularity: 42, Country: "KOR", RivalOf: "Sato",
            Bio: "Busan's hardest export, who has called Sato's legend a museum piece and promised to prove it in Tokyo. The Korea–Japan rivalry sells out arenas in two countries, and Kang stokes it in both languages."),

        // ============ Cage Kings — the frontier (champions) ============
        new("Mateo", "Quiroga", "Aguja", Region.SouthAmerica, WeightClass.Strawweight, PrimaryStyle.BrazilianJiuJitsu, 25, 62, 9, 2, "CK", Champion: true, Popularity: 28, Country: "ARG",
            Bio: "The needle: a Buenos Aires prodigy who threads chokes through gaps other grapplers can't see. He still teaches the kids' class at his neighborhood academy between title defenses."),
        new("Santiago", "Ibarra", "El Rayo", Region.SouthAmerica, WeightClass.Flyweight, PrimaryStyle.Striker, 24, 63, 8, 1, "CK", Champion: true, Popularity: 31, Country: "ECU",
            Bio: "Quito lightning — Ibarra's straight right arrives at altitude speed. The face of Ecuadorian fighting on every card he headlines."),
        new("Pedro", "Sales", "Rocket", Region.SouthAmerica, WeightClass.Bantamweight, PrimaryStyle.Kickboxer, 26, 64, 10, 3, "CK", Champion: true, Popularity: 30, Country: "BRA",
            Bio: "A Recife beach-circuit kickboxer who fought his way off the sand and into São Paulo's arenas. Launches, lands, repeats."),
        new("Facundo", "Barrios", "The Fox", Region.SouthAmerica, WeightClass.Featherweight, PrimaryStyle.WellRounded, 27, 63, 11, 4, "CK", Champion: true, Popularity: 27, Country: "URU",
            Bio: "Montevideo's cleverest fighter: Barrios wins with feints, angles and an infuriating smile. Uruguay's small scene made him crafty; the belt made him insufferable about it."),
        new("Thiago", "Prado", "Granite", Region.SouthAmerica, WeightClass.Lightweight, PrimaryStyle.Brawler, 28, 65, 12, 5, "CK", Champion: true, Popularity: 33, Country: "BRA",
            Bio: "Quarried out of Minas Gerais, Prado has eaten flush shots from two divisions of punchers without blinking. His chin has its own fan club."),
        new("Nicolas", "Paredes", "La Sombra", Region.SouthAmerica, WeightClass.Welterweight, PrimaryStyle.Striker, 27, 64, 10, 2, "CK", Champion: true, Popularity: 29, Country: "COL",
            Bio: "A Medellín southpaw who slips punches like gossip — always half a step gone. The shadow only turns solid to counter."),
        new("Gustavo", "Leal", "The Huntsman", Region.SouthAmerica, WeightClass.Middleweight, PrimaryStyle.Grappler, 29, 63, 11, 6, "CK", Champion: true, Popularity: 26, Country: "BRA",
            Bio: "A quiet Paraná farm kid who stalks takedowns for fifteen patient minutes at a time. Nobody escapes the ground with him twice."),
        new("Ramiro", "Salazar", "El Toro", Region.SouthAmerica, WeightClass.LightHeavyweight, PrimaryStyle.Wrestler, 30, 62, 12, 7, "CK", Champion: true, Popularity: 28, Country: "VEN",
            Bio: "Salazar wrestled for Venezuela through the lean years, training on donated mats. Every belt he wins goes home to Caracas for the gym wall."),
        new("Wallace", "Bandeira", "The Wall", Region.SouthAmerica, WeightClass.Heavyweight, PrimaryStyle.ClinchFighter, 31, 63, 10, 4, "CK", Champion: true, Popularity: 34, Country: "BRA",
            Bio: "A former Rio nightclub doorman who has been professionally immovable since age nineteen. Bandeira leans on people until their ideas run out."),

        // ============ GFC — the Gulf's oil-money upstart (champions) ============
        // Oil money buys talent, not hearts: GFC's champions are hired guns on rich
        // deals — a raid target the day a bigger cheque comes calling (low loyalty).
        new("Rashid", "Al-Marri", "The Sandstorm", Region.MiddleEast, WeightClass.Lightweight, PrimaryStyle.Wrestler, 28, 79, 16, 1, "GFC", Champion: true, Popularity: 66, Country: "UAE", Loyalty: 28,
            Bio: "The Gulf's marquee signing: an Emirati wrestling prodigy polished in American rooms and paid like a footballer. The GFC built its stadium shows around him — and everyone knows he reads every offer sheet that lands."),
        new("Tariq", "Nasser", "Desert Hawk", Region.MiddleEast, WeightClass.Welterweight, PrimaryStyle.Kickboxer, 27, 77, 15, 2, "GFC", Champion: true, Popularity: 58, Country: "KSA", Loyalty: 24,
            Bio: "A graduate of Riyadh's national combat-sports project with sniper timing and the richest purse-per-fight deal in the division. Loyalty was not part of the negotiation."),
        new("Yusuf", "Haddad", "The Cedar", Region.MiddleEast, WeightClass.Heavyweight, PrimaryStyle.Brawler, 31, 76, 14, 3, "GFC", Champion: true, Popularity: 62, Country: "LIB", Loyalty: 30,
            Bio: "A Beirut dockworker who punched his way onto Gulf television, Haddad falls like his namesake — rarely, and all at once. He follows the money and doesn't apologize for it."),
        new("Movsar", "Dudaev", "The Collector", Region.Europe, WeightClass.Middleweight, PrimaryStyle.Wrestler, 29, 77, 15, 2, "GFC", Champion: true, Popularity: 50, Country: "RUS", Loyalty: 26,
            Bio: "A Caucasus wrestling machine collecting the Gulf's money with professional detachment — he has changed banners before and mourned none of them. The GFC is a chapter, not a home."),
        // The exception in a house of hired guns: the one local kid who fights for
        // the badge — the homegrown star the Gulf project was supposed to produce.
        new("Khalid", "Al-Suwaidi", "The Falcon of Dubai", Region.MiddleEast, WeightClass.Middleweight, PrimaryStyle.Wrestler, 22, 64, 8, 0, "GFC", Popularity: 38, Country: "UAE", Loyalty: 78, Streak: 8,
            Bio: "The exception in a house of mercenaries: a Dubai kid who grew up cageside at GFC shows and fights for the badge, not the bonus. Eight straight wins and the Gulf finally has a homegrown star to build on."),

        // ============ IFA — Europe's hard-nosed challenger, at war with VMMA (champions) ============
        // A whole roster of grinders who'd never defect to the establishment across
        // town — loyal to the hard school even as the turf war squeezes the books.
        new("Miron", "Kovac", "Cold Iron", Region.Europe, WeightClass.Lightweight, PrimaryStyle.Wrestler, 28, 78, 17, 3, "IFA", Champion: true, Popularity: 60, Country: "SRB", Loyalty: 72,
            Bio: "A product of Belgrade's hard school who slept on the gym mats he now headlines above. Kovac bleeds Ironhold black and calls VMMA 'the theatre across town'."),
        new("Emil", "Toma", "The Scythe", Region.Europe, WeightClass.Featherweight, PrimaryStyle.Kickboxer, 27, 76, 15, 2, "IFA", Champion: true, Popularity: 49, Country: "ROU", Loyalty: 66,
            Bio: "A Bucharest butcher's son whose body kicks fold people at the waist. Toma answered VMMA's last contract overture with a one-word press release."),
        new("Bohdan", "Marchenko", "Anvil", Region.Europe, WeightClass.Welterweight, PrimaryStyle.ClinchFighter, 29, 77, 16, 4, "IFA", Champion: true, Popularity: 47, Country: "GEO", Loyalty: 64,
            Bio: "Marchenko came up through Georgian chidaoba wrestling and an amateur career fought in unheated halls. What he grips, wilts."),
        new("Dmitri", "Volkov", "The Hammer", Region.Europe, WeightClass.Middleweight, PrimaryStyle.Wrestler, 31, 78, 21, 3, "IFA", Champion: true, Popularity: 62, Country: "RUS", Loyalty: 80,   // the IFA franchise
            Bio: "The face of the hard school: Volkov came up in IFA's parking-lot years and keeps a locker there with his name welded on. Twenty-one wins, no entrance music, no interest in leaving."),
        new("Andrzej", "Nowak", "Ironjaw", Region.Europe, WeightClass.LightHeavyweight, PrimaryStyle.Grappler, 30, 76, 18, 4, "IFA", Champion: true, Popularity: 50, Country: "POL", Loyalty: 70,
            Bio: "A Silesian coal-country grappler who has lost only four times in twenty-two fights — the jaw earned the name honestly. Nowak signs whatever IFA puts in front of him, mostly unread."),
        new("Borys", "Zielinski", "Bison", Region.Europe, WeightClass.Heavyweight, PrimaryStyle.Brawler, 32, 77, 17, 5, "IFA", Champion: true, Popularity: 55, Country: "POL", Loyalty: 68,
            Bio: "A former rugby prop from Kraków who charges in straight lines that somehow keep working. The IFA faithful's favourite fifteen minutes."),

        // ============ RCS — the Brazilian upstart at war with Cage Kings (champions) ============
        // Young, proud, and hungry — Rio's answer to Cage Kings. A Brazilian turf war
        // in a fight-mad market that (just) has the fans for two.
        new("Rafael", "Souza", "Furacao", Region.SouthAmerica, WeightClass.Lightweight, PrimaryStyle.BrazilianJiuJitsu, 26, 67, 12, 2, "RCS", Champion: true, Popularity: 54, Country: "BRA", Loyalty: 58, Streak: 6,
            Bio: "The hurricane of Rocinha: Souza learned jiu-jitsu at a favela social project and now funds three of them. Rio's proof that RCS can grow its own stars instead of renting São Paulo's."),
        new("Bruno", "Carvalho", "Tubarao", Region.SouthAmerica, WeightClass.Welterweight, PrimaryStyle.Striker, 27, 66, 13, 3, "RCS", Champion: true, Popularity: 45, Country: "BRA", Loyalty: 52,
            Bio: "An Ipanema beach-boxing showman with a shark's instinct for a hurt opponent. Carvalho headlined RCS's first sold-out card and will not let anyone forget it."),
        new("Vinicius", "Rocha", "Pedra", Region.SouthAmerica, WeightClass.Middleweight, PrimaryStyle.Grappler, 28, 65, 11, 4, "RCS", Champion: true, Popularity: 40, Country: "BRA", Loyalty: 50,
            Bio: "A quarry worker's son from Niterói, Rocha grinds fights down to gravel. Not pretty; extremely permanent."),
        new("Caio", "Siqueira", "Correnteza", Region.SouthAmerica, WeightClass.LightHeavyweight, PrimaryStyle.Grappler, 28, 65, 11, 3, "RCS", Champion: true, Popularity: 38, Country: "BRA", Loyalty: 52,
            Bio: "A Niterói bouncer the RCS founders recruited off a viral street-grappling clip. Rio's light heavyweight belt has stayed home ever since."),
        new("Anderson", "Costa", "O Gigante", Region.SouthAmerica, WeightClass.Heavyweight, PrimaryStyle.ClinchFighter, 30, 66, 12, 3, "RCS", Champion: true, Popularity: 47, Country: "BRA", Loyalty: 55,
            Bio: "Two meters of Bahia dockworker who once carried a broken cage door out of the arena under one arm. Rio's giant — and contractually, Rio's."),

        // ============ UFL — the US national scene beneath the AFC (champions) ============
        // The rung between the regionals and the summit: solid American pros, some
        // eyeing the AFC's money, some content to headline at home.
        new("Jaylen", "Brooks", "Blue Chip", Region.NorthAmerica, WeightClass.Lightweight, PrimaryStyle.WellRounded, 26, 68, 13, 2, "UFL", Champion: true, Popularity: 52, Country: "USA", Loyalty: 34,
            Bio: "A four-sport athlete from Houston who picked cage fighting because it was the hardest option on the table. Brooks calls the UFL belt a stepping stone, out loud, at his own title celebrations."),
        new("Cole", "Jackson", "Hollywood", Region.NorthAmerica, WeightClass.Welterweight, PrimaryStyle.Kickboxer, 28, 67, 14, 3, "UFL", Champion: true, Popularity: 49, Country: "USA", Loyalty: 30,
            Bio: "An LA stuntman with a face the camera loves and a check hook casting directors don't. Jackson's agent takes more meetings than his coach."),
        new("Marcus", "Webb", "The Mechanic", Region.NorthAmerica, WeightClass.Middleweight, PrimaryStyle.Wrestler, 29, 68, 15, 4, "UFL", Champion: true, Popularity: 44, Country: "USA", Loyalty: 40,
            Bio: "A Pittsburgh garage owner who still does brake jobs between camps. Webb strips opponents down to parts and bills by the round."),
        new("Devon", "Slater", "Special Teams", Region.NorthAmerica, WeightClass.LightHeavyweight, PrimaryStyle.Striker, 29, 67, 13, 4, "UFL", Champion: true, Popularity: 42, Country: "USA", Loyalty: 32,
            Bio: "A former college linebacker from Cleveland who hits like the kickoff coverage he used to run. Slater treats every UFL defense as an AFC audition and doesn't pretend otherwise."),
        new("Damon", "Pierce", "Concrete", Region.NorthAmerica, WeightClass.Heavyweight, PrimaryStyle.Brawler, 31, 67, 13, 5, "UFL", Champion: true, Popularity: 51, Country: "USA", Loyalty: 36,
            Bio: "A Chicago union laborer who poured sidewalks by day and flattened regional heavyweights by night until the UFL made it official. What he hits, sets."),

        // ============ SCM — the ANZ scene (champions) ============
        new("Kai", "Marsh", "Thunder", Region.Oceania, WeightClass.Welterweight, PrimaryStyle.Brawler, 26, 71, 13, 2, "SCM", Champion: true, Popularity: 57, Country: "AUS",
            Bio: "A Gold Coast lifeguard who fights like the rips he pulls tourists out of — sudden, loud, and indifferent to your plans. Australia's favourite walk-off knockout artist."),
        new("Jack", "Rennie", "Roughneck", Region.Oceania, WeightClass.Middleweight, PrimaryStyle.Brawler, 28, 69, 14, 4, "SCM", Champion: true, Popularity: 48, Country: "AUS",
            Bio: "A Perth mining-site brawler who served his apprenticeship in outback tent fights. Rennie's chin is regional folklore and his after-parties are regional policy problems."),
        new("Manaia", "Rahiri", "The Tide", Region.Oceania, WeightClass.Heavyweight, PrimaryStyle.ClinchFighter, 30, 70, 12, 3, "SCM", Champion: true, Popularity: 51, Country: "NZL",
            Bio: "An Auckland scaffolder from a long line of Māori boxers, Rahiri rolls in slow and leaves nothing standing. The mana in his corner is generational."),
        // Tonga's heavyweight hope — the trans-Tasman island rivalry writes itself.
        new("Sione", "Tuilagi", "The Reef", Region.Oceania, WeightClass.Heavyweight, PrimaryStyle.Brawler, 28, 68, 11, 2, "SCM", Popularity: 44, Country: "TGA", RivalOf: "Rahiri",
            Bio: "Tonga's heavyweight hope, who says Rahiri's belt is 'on loan to Auckland'. When the islands' two big men share a card, the whole Pacific picks sides."),

        // ============ CC — a US regional feeder (its blue-chip champ) ============
        // A 22-year-old phenom running unbeaten through the regionals — no ties to the
        // feeder and the big orgs already circling; he'll take the first real offer.
        new("Tyshawn", "Cole", "Young Money", Region.NorthAmerica, WeightClass.Lightweight, PrimaryStyle.WellRounded, 22, 63, 10, 0, "CC", Champion: true, Popularity: 46, Country: "USA", Loyalty: 16,
            Bio: "The phenom the whole ladder is watching: a Baltimore prodigy who has been beating grown men since seventeen without taking a mark. Cole's loyalty is to the climb — the first serious offer takes him."),

        // ============ SCL — the emerging African scene (its brightest star) ============
        // The Lion of Lagos: Africa's first true superstar, dragging a whole cool
        // continent's interest up by himself. Loyal to the cause of building the
        // African scene — but on a tiny deal, and the giants are circling hard.
        new("Chidi", "Okafor", "Lion of Lagos", Region.Africa, WeightClass.Middleweight, PrimaryStyle.Striker, 26, 69, 13, 1, "SCL", Champion: true, Popularity: 84, Country: "NGA", Loyalty: 74, Streak: 9,
            Bio: "Africa's first true superstar: a Surulere tailor's son whose title fights stop traffic across four time zones. Okafor fights for peanuts by choice — building the African scene is the mission — while the giants keep calling with life-changing money."),
        // The project needs more than one believer: Accra's Greco man headlines
        // beside Okafor as a matter of principle.
        new("Kofi", "Mensah", "The Fortress", Region.Africa, WeightClass.Welterweight, PrimaryStyle.ClinchFighter, 25, 62, 11, 3, "SCL", Popularity: 33, Country: "GHA", Loyalty: 60,
            Bio: "Accra's contribution to the African project: a former Greco-Roman junior who believes the continent's scene should be built, not exported. He and Okafor headline SCL's biggest cards side by side as a matter of principle."),

        // ============ The wider base — feeders and local halls, each with a face ============
        // GLF (US Midwest Regional): a beloved veteran gatekeeper-champ, and the undefeated
        // hometown kid every bigger org is already circling.
        new("Wyatt", "Halloran", "Buckshot", Region.NorthAmerica, WeightClass.Welterweight, PrimaryStyle.Wrestler, 31, 55, 24, 11, "GLF", Champion: true, Popularity: 52, Country: "USA", Loyalty: 66,
            Bio: "A Dubuque farm boy in his second decade of small-hall main events — the fans have started calling the GLF belt 'Wyatt's buckle'. Every hot prospect in the Midwest has to come through him, and most don't."),
        new("DeShawn", "Pryor", "Young Lightning", Region.NorthAmerica, WeightClass.Lightweight, PrimaryStyle.Striker, 22, 54, 10, 0, "GLF", Popularity: 43, Country: "USA", Streak: 10,
            Bio: "Milwaukee's fastest hands since the amateurs started keeping records. Pryor headlines GLF cards for gas money while every national scout in the sport sits cageside pretending not to take notes."),
        // KAC (Kansai, Japan Regional): a loyal lifer who'll die in his hometown promotion.
        new("Haruki", "Nakamura", "The Craftsman", Region.Asia, WeightClass.Featherweight, PrimaryStyle.Striker, 32, 56, 26, 9, "KAC", Champion: true, Popularity: 55, Country: "JPN", Loyalty: 84,
            Bio: "Twenty-six pro wins, most of them under Kansai Cage's lights: Nakamura sold tickets outside the building as a schoolboy and says he'll retire behind its curtain. The craft in the nickname is a career of the same six punches, perfected."),
        // CDC (Andes Regional): an altitude-bred grinder who never gets tired.
        new("Mateo", "Quispe", "El Cóndor", Region.SouthAmerica, WeightClass.Flyweight, PrimaryStyle.Grappler, 27, 54, 17, 6, "CDC", Champion: true, Popularity: 49, Country: "PER", Loyalty: 58,
            Bio: "Born at four thousand meters in the Sacred Valley, Quispe's lungs are local legend — visiting fighters gas in a round while he breathes through fifteen. The condor circles until things stop moving."),
        // MCC (Detroit City): a blue-collar brawler, the pride of the Motor City.
        new("Rico", "Barone", "Motown", Region.NorthAmerica, WeightClass.Middleweight, PrimaryStyle.Brawler, 29, 48, 15, 10, "MCC", Champion: true, Popularity: 41, Country: "USA",
            Bio: "A third-generation Detroit assembly-line Barone who fights like the city built him: no reverse gear. The MCC belt is the only thing he's ever wanted that wasn't on wheels."),
        // BLB (Baltic City): a granite-chinned sambo man from the old school.
        new("Tomas", "Kazlauskas", "The Oak", Region.Europe, WeightClass.LightHeavyweight, PrimaryStyle.Wrestler, 30, 47, 16, 9, "BLB", Champion: true, Popularity: 38, Country: "LTU",
            Bio: "An old-school Vilnius sambo man taught by coaches who were taught by the Soviets. Kazlauskas doesn't cut weight, doesn't trash talk, and doesn't fall over."),
        // MFC (Mekong City): a slick Muay Thai stylist with a highlight reel of leg kicks.
        new("Somchai", "Chaiya", "Thunderfoot", Region.Asia, WeightClass.Bantamweight, PrimaryStyle.Kickboxer, 26, 49, 19, 7, "MFC", Champion: true, Popularity: 41, Country: "THA",
            Bio: "A Chiang Mai stadium veteran whose left kick has its own sound — MFC crowds go quiet just to hear it land. Two hundred muay thai rounds a year, forever."),
        // CPF (Cape Town City): a giant out of the townships who throws people around.
        new("Sipho", "Ndlovu", "Table Mountain", Region.Africa, WeightClass.Heavyweight, PrimaryStyle.Grappler, 28, 46, 12, 7, "CPF", Champion: true, Popularity: 39, Country: "RSA",
            Bio: "A Khayelitsha giant discovered lifting engine blocks at a scrapyard, Ndlovu throws heavyweights around like laundry. Cape Town's cards sell out on his shoulders."),
        // PMP (Pampas, Argentina City): a swaggering gaucho who fights to a crowd.
        new("Lucas", "Ferreyra", "El Sombrero", Region.SouthAmerica, WeightClass.Welterweight, PrimaryStyle.Brawler, 27, 48, 14, 8, "PMP", Champion: true, Popularity: 40, Country: "ARG",
            Bio: "A gaucho's son from the pampas who fights to the crowd like it's a rodeo — hat on the cage post, chin in the air. Buenos Aires sings his name; his defense remains a rumor."),

        // ============ The women's scene — designed headliners ============
        // (Women's divisions at build run Strawweight/Flyweight/Bantamweight; only
        // the orgs whose NICHE includes women get authored stars.)
        // AFC: the women's division opens with a genuine superstar — and a blood
        // feud at the top of the bantamweight bill from day one.
        new("Katya", "Volkova", "The Tsarina", Region.Europe, WeightClass.Bantamweight, PrimaryStyle.Striker, 28, 84, 15, 1, "AFC", Champion: true, Popularity: 78, Country: "RUS", Sex: Sex.Female,
            Bio: "The most famous woman in the sport: a Moscow boxing stylist with Olympic silver, movie-star poise, and a jab that ends arguments. Volkova headlines anywhere on earth and knows it."),
        new("Sarah", "Donovan", "Wildcard", Region.NorthAmerica, WeightClass.Bantamweight, PrimaryStyle.Brawler, 27, 81, 13, 3, "AFC", Popularity: 69, Country: "USA", Sex: Sex.Female, RivalOf: "Volkova",
            Bio: "A Boston bartender who brawled her way up the regional circuit on spite and overhand rights. She has called Volkova a porcelain princess in four languages — the feud sells itself."),
        new("Amanda", "Silva", "The Panther", Region.SouthAmerica, WeightClass.Flyweight, PrimaryStyle.BrazilianJiuJitsu, 26, 83, 14, 1, "AFC", Champion: true, Popularity: 71, Country: "BRA", Sex: Sex.Female,
            Bio: "Curitiba royalty — her mother ran one of Brazil's first women's jiu-jitsu programs, and Amanda was drilling armbars at five. Smooth, patient, inevitable."),
        new("Mei-Lin", "Zhang", "The Needle", Region.Asia, WeightClass.Strawweight, PrimaryStyle.Striker, 25, 82, 12, 1, "AFC", Champion: true, Popularity: 64, Country: "CHN", Sex: Sex.Female,
            Bio: "A sanda champion from Chengdu whose straight punches thread the tightest guards in the sport. She brought China its first AFC belt, and a hundred gyms opened the year she won it."),
        // The Guadalajara counter-puncher already crashing the champion's interviews.
        new("Valentina", "Reyes", "La Pantera", Region.NorthAmerica, WeightClass.Strawweight, PrimaryStyle.Striker, 25, 79, 11, 2, "AFC", Popularity: 52, Country: "MEX", Sex: Sex.Female, RivalOf: "Zhang",
            Bio: "The first daughter of a Guadalajara boxing family, with the fastest counter left in the women's game. She has crashed two of Zhang's interviews already; the champion has stopped smiling about it."),
        // The other women-running orgs each open with a face of their own.
        new("Ingrid", "Bergström", "Valkyrie", Region.Europe, WeightClass.Flyweight, PrimaryStyle.Kickboxer, 27, 74, 12, 2, "VMMA", Champion: true, Popularity: 56, Country: "SWE", Sex: Sex.Female,
            Bio: "A Stockholm firefighter's daughter with textbook Swedish kickboxing and zero wasted motion. VMMA sells its women's division on her shoulders."),
        new("Petra", "Novakova", "The Baton", Region.Europe, WeightClass.Bantamweight, PrimaryStyle.Kickboxer, 27, 72, 11, 2, "VMMA", Champion: true, Popularity: 45, Country: "CZE", Sex: Sex.Female,
            Bio: "A Prague policewoman who kept winning European kickboxing titles on her rest days until the day job became optional. VMMA's women's bantamweight belt has barely left her wrist since she took it."),
        new("Yuna", "Park", "Silk", Region.Asia, WeightClass.Strawweight, PrimaryStyle.BrazilianJiuJitsu, 25, 73, 11, 1, "ICF", Champion: true, Popularity: 54, Country: "KOR", Sex: Sex.Female,
            Bio: "A Seoul judo prodigy who switched to jiu-jitsu after the national team passed her over — their loss, as her opponents can medically confirm. The nickname describes her transitions, not her grip."),
        new("Sakura", "Fujimoto", "First Frost", Region.Asia, WeightClass.Flyweight, PrimaryStyle.Grappler, 24, 71, 10, 1, "ICF", Champion: true, Popularity: 48, Country: "JPN", Sex: Sex.Female,
            Bio: "A third-generation judoka from Kyoto who treats the ICF women's flyweight belt as a family duty. Her groundwork is a masterclass her opponents pay full price to attend."),
        new("Beatriz", "Lima", "Borboleta", Region.SouthAmerica, WeightClass.Flyweight, PrimaryStyle.BrazilianJiuJitsu, 24, 60, 9, 2, "CK", Champion: true, Popularity: 33, Country: "BRA", Sex: Sex.Female,
            Bio: "A butterfly out of Fortaleza's beach academies with the prettiest guard passing in the women's game. Cage Kings is bringing her along carefully; the sharks upstream already circle."),
        new("Larissa", "Mendes", "A Cirurgiã", Region.SouthAmerica, WeightClass.Bantamweight, PrimaryStyle.Grappler, 25, 61, 10, 2, "CK", Champion: true, Popularity: 31, Country: "BRA", Sex: Sex.Female,
            Bio: "A São Paulo physiotherapy student who pays her tuition with title defenses. Mendes's mat control is clinical — she can tell you exactly which ligament she's isolating."),
        new("Jordan", "Hayes", "All Gas", Region.NorthAmerica, WeightClass.Bantamweight, PrimaryStyle.Kickboxer, 26, 65, 11, 2, "UFL", Champion: true, Popularity: 46, Country: "USA", Loyalty: 28, Sex: Sex.Female,
            Bio: "A former college soccer striker from Denver who fights at exactly one speed — the nickname is a warranty. The UFL knows she's built for bigger stages; so does she."),
        new("Mereana", "Kahu", "Storm Tide", Region.Oceania, WeightClass.Flyweight, PrimaryStyle.ClinchFighter, 27, 66, 12, 3, "SCM", Champion: true, Popularity: 50, Country: "NZL", Sex: Sex.Female,
            Bio: "From a Ngāpuhi family of shearers and boxers, Kahu smothers opponents like weather rolling in off the Tasman. She carries New Zealand's women's fighting scene on her shoulders."),
        // KAC — the soul of its niche: the small Japanese house that RUNS women.
        // A hometown lifer holds the belt, and a 21-year-old phenom on a tear is
        // exactly the poach-bait the giants circle.
        new("Rin", "Takeda", "Shooting Star", Region.Asia, WeightClass.Strawweight, PrimaryStyle.Striker, 24, 58, 12, 1, "KAC", Champion: true, Popularity: 55, Country: "JPN", Loyalty: 90, Sex: Sex.Female,
            Bio: "Osaka's darling: Takeda sold merchandise at KAC shows at eighteen and kept sneaking into the fighters' warm-ups until the coaches surrendered. Six years later she's the face of the house — and swears she will never fight anywhere else."),
        new("Ayaka", "Kondo", "Battle Cry", Region.Asia, WeightClass.Bantamweight, PrimaryStyle.Grappler, 21, 52, 8, 0, "KAC", Popularity: 40, Country: "JPN", Streak: 8, Sex: Sex.Female,
            Bio: "A judo-school runaway who screams into every entrance like it's a war she has already won — and Osaka screams back. Twenty-one years old, eight straight wins, and her name is on every big org's shortlist."),
        // Women on the open market: the hottest free agent in the sport, and a
        // West African prospect a smart scout signs before anyone's heard of her.
        new("Roxanne", "Vance", "Riot", Region.NorthAmerica, WeightClass.Bantamweight, PrimaryStyle.Brawler, 25, 70, 8, 0, Popularity: 58, Country: "USA", Sex: Sex.Female,
            Bio: "The hottest free agent in the sport: an ex-punk-band roadie who accepted an internet troll's challenge on camera, knocked him cold, and turned the clip into a career. Eight fights, no losses, zero contracts signed."),
        new("Fatima", "Diallo", "The Gazelle", Region.Africa, WeightClass.Flyweight, PrimaryStyle.Striker, 22, 56, 6, 0, Popularity: 30, Country: "SEN", Sex: Sex.Female,
            Bio: "Dakar's secret: a track-scholarship sprinter who found striking late and moves like nobody has told her the sport has limits. The scout who signs her first will look like a genius in three years."),

        // ============ Niche anchors — the lanes get institutions, not just a face ============
        // BLB, the Baltic big-man hall: a day-one HEAVYWEIGHT BLOOD FEUD — two wars
        // fought, one apiece, the whole scene waiting on the rubber match.
        new("Ivars", "Ozols", "Siege", Region.Europe, WeightClass.Heavyweight, PrimaryStyle.Wrestler, 29, 49, 13, 6, "BLB", Champion: true, Popularity: 36, Country: "LAT",
            Bio: "A Riga dockside crane operator who wrestles like heavy machinery. His two wars with Urbonas stand at one apiece, and the whole Baltic has picked a side."),
        new("Dovydas", "Urbonas", "Wrecking Ball", Region.Europe, WeightClass.Heavyweight, PrimaryStyle.Brawler, 31, 48, 14, 9, "BLB", Popularity: 34, Country: "LTU", RivalOf: "Ozols",
            Bio: "A Kaunas demolition contractor — literally — who stopped Ozols in their first war and lost the belt back to him on the cards in the second. The rubber match with Ozols is the Baltic's biggest conversation."),
        // MCC: Detroit's crosstown war — the pride of the east side wants Barone's belt.
        new("Jamal", "Weeks", "Eight Mile", Region.NorthAmerica, WeightClass.Middleweight, PrimaryStyle.Wrestler, 27, 47, 12, 8, "MCC", Popularity: 35, Country: "USA", RivalOf: "Barone",
            Bio: "The pride of Detroit's east side, still wrestling out of the rec center he grew up in. Weeks says Barone's belt lives on the wrong side of Woodward — the crosstown fight books itself."),
        // CDC: the altitude-bred flyweight jewel — 7-0 at 20, no loyalty, pure poach-bait.
        new("Alonso", "Huanca", "Colibrí", Region.SouthAmerica, WeightClass.Flyweight, PrimaryStyle.Striker, 20, 52, 7, 0, "CDC", Popularity: 35, Country: "BOL", Loyalty: 18, Streak: 7,
            Bio: "The hummingbird of El Alto: a twenty-year-old altitude prodigy whose hands blur where other men wheeze. CDC can't pay him what's coming, and everyone — including CDC — knows it."),
        // MFC: the golden boy of the Mekong — undefeated, beautiful to watch.
        new("Arthit", "Saengkham", "Golden Boy", Region.Asia, WeightClass.Featherweight, PrimaryStyle.Kickboxer, 21, 50, 9, 0, "MFC", Popularity: 33, Country: "THA", Streak: 9,
            Bio: "The golden boy of the Mekong, undefeated and beautiful to watch — a temple-fair prodigy whose teeps land the way poetry scans. Bangkok's big money is already whispering."),
        // CPF: the township rival — Cape Town isn't big enough for two giants.
        new("Thabo", "Mokoena", "The Rhino", Region.Africa, WeightClass.Heavyweight, PrimaryStyle.Wrestler, 30, 45, 11, 6, "CPF", Popularity: 30, Country: "RSA", RivalOf: "Ndlovu",
            Bio: "A Gugulethu security guard who charges low and hits like his namesake. Cape Town isn't big enough for him and Ndlovu, and neither man is leaving."),
        // GFC's light-heavy belt: another hired gun on oil money (the raid list grows).
        new("Zurab", "Kipiani", "The Executioner", Region.Europe, WeightClass.LightHeavyweight, PrimaryStyle.Grappler, 29, 76, 15, 3, "GFC", Champion: true, Popularity: 49, Country: "GEO", Loyalty: 22,
            Bio: "A Tbilisi judoka collecting the Gulf's money with grim professionalism — no entrance theatrics, no interviews, no wasted minutes. His agent keeps three offer sheets warm at all times."),
        // IFA: the hard school's heir apparent — young, loyal, learning at Volkov's feet.
        new("Kirill", "Baranov", "The Hatchet", Region.Europe, WeightClass.Middleweight, PrimaryStyle.Wrestler, 23, 68, 9, 1, "IFA", Popularity: 34, Country: "RUS", Loyalty: 55,
            Bio: "The hard school's heir apparent: Volkov found him in a Perm youth club and has drilled him personally for three years. Baranov splits guards the way his mentor splits paychecks — without ceremony."),

        // ============ Free agents — story hooks on the open market ============
        // The 21-year-old blue-chipper every scout is whispering about.
        new("Ezekiel", "Grant", "The Prodigy", Region.NorthAmerica, WeightClass.Lightweight, PrimaryStyle.WellRounded, 21, 58, 4, 0, Popularity: 45, Country: "USA",
            Bio: "The scouts' favorite whisper: a homeschooled Kansas kid raised on film study and farm work by an obsessive ex-fighter father. Four fights in, the tape already looks unfair."),
        // The dethroned AFC king on the wrong side of 35, chasing one more run.
        new("Brock", "Tanner", "The Reckoning", Region.NorthAmerica, WeightClass.LightHeavyweight, PrimaryStyle.Wrestler, 36, 74, 22, 9, Popularity: 61, Country: "USA",
            Bio: "The dethroned AFC king, two surgeries past the reign, still certain the story has one more chapter. Every org in the sport has weighed the name against the mileage."),
        // The gatekeeper: beat him and you're somebody.
        new("Oskar", "Lindqvist", "The Test", Region.Europe, WeightClass.Welterweight, PrimaryStyle.Kickboxer, 33, 60, 14, 11, Popularity: 22, Country: "SWE",
            Bio: "The sport's honest yardstick: twenty-five fights and a simple deal — beat Lindqvist and you're somebody; lose and you aren't. He has made more careers than most promoters."),
        // Unbeaten and avoided — nobody wants to grapple him.
        new("Giorgi", "Tsereteli", "The Cliff", Region.Europe, WeightClass.Middleweight, PrimaryStyle.Grappler, 24, 66, 7, 0, Popularity: 26, Country: "GEO",
            Bio: "An unbeaten Georgian grappler managers cross the street to avoid — seven fights, no losses, and a growing list of men who regret the ground. Fluent in three languages; has declined to trash talk in any of them."),
        // Never wins boring: a small-hall promoter's dream.
        new("Sonny", "Delgado", "Fireworks", Region.NorthAmerica, WeightClass.Featherweight, PrimaryStyle.Brawler, 27, 55, 9, 6, Popularity: 38, Country: "PUR",
            Bio: "San Juan's guarantee: Delgado has never been in a boring fight and has the medical bills to prove it. Promoters love him; cutmen charge him double."),
        // The aging blade — past his prime, still dangerous, big name locally.
        new("Kazuki", "Hattori", "The Blade", Region.Asia, WeightClass.Bantamweight, PrimaryStyle.Striker, 34, 63, 18, 8, Popularity: 41, Country: "JPN",
            Bio: "A former ICF title challenger from Nagoya on the far side of his prime, still carrying the counter right hand that made him famous. Japanese crowds buy tickets for the name; opponents still respect the blade."),
        // The Olympic wrestling medalist gone pro — undefeated and studiously avoided.
        new("Aslan", "Batyrov", "Gold", Region.Asia, WeightClass.Welterweight, PrimaryStyle.Wrestler, 24, 70, 6, 0, Popularity: 52, Country: "KAZ",
            Bio: "An Olympic wrestling gold medallist gone pro — the purest pedigree on the open market, studiously avoided by every ranked welterweight with a memory. The bidding war is a matter of time."),
        // A former CK champ shopping his name around the mid-tier orgs.
        new("Diego", "Rivas", "El Fantasma", Region.SouthAmerica, WeightClass.Bantamweight, PrimaryStyle.BrazilianJiuJitsu, 29, 68, 15, 3, Popularity: 40, Country: "ARG",
            Bio: "A former Cage Kings champion who walked out over a purse dispute and has haunted the mid-tier orgs since. The name still draws in Buenos Aires; the chip on his shoulder draws everywhere."),
        // Egyptian ex-GFC headliner cut loose after a title-shot loss — still a Gulf draw.
        new("Karim", "Farouk", "The Pharaoh", Region.Africa, WeightClass.Featherweight, PrimaryStyle.Kickboxer, 30, 66, 17, 5, Popularity: 43, Country: "EGY",
            Bio: "Cairo's biggest fight export, cut loose by the GFC after a single night under its lights — a loss. Farouk's leg kicks still make Gulf highlight reels, and he wants the rematch money owes him."),
        // The whisper out of the Caucasus — the generational prospect nobody has signed.
        new("Shamil", "Gadzhiev", null, Region.Europe, WeightClass.Lightweight, PrimaryStyle.Wrestler, 19, 55, 3, 0, Popularity: 24, Country: "RUS",
            Bio: "The whisper out of the Caucasus: a nineteen-year-old who handled two seasoned pros like sparring partners, then vanished back into the mountains to train. Every org has a file on him; nobody has a signature."),
        // The comeback: a former AFC title challenger who left for the right reasons.
        new("Nate", "Corrigan", "Second Bell", Region.NorthAmerica, WeightClass.Welterweight, PrimaryStyle.WellRounded, 33, 64, 16, 6, Popularity: 44, Country: "USA",
            Bio: "A former AFC title challenger who walked away mid-contract when his daughter got sick, and stepped back to be a full-time dad. She's better now; he's back; the comeback story writes itself."),
    };

    /// <summary>
    /// Where the authored fighters actually train, by last name. The bios name
    /// these rooms; without this the camp sort put them wherever ability and
    /// noise landed, so a Bangkok muay thai lifer could end up at a Moscow sambo
    /// hall. Claimed before the rest of the sport sorts itself into gyms.
    /// </summary>
    public static IReadOnlyDictionary<string, string> CampHomes { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Mercer"] = "Apex Combat Club",
            ["Hale"] = "Iron Range Wrestling",
            ["Okonjo"] = "Iron Range Wrestling",          // the scholarship that added technique to the strength
            ["Boone"] = "Sunset Striking Lab",
            ["Reyes"] = "Sunset Striking Lab",
            ["Vandermeer"] = "Northern Lights MMA",
            ["Duarte"] = "Aço Paulista",
            ["Souza"] = "Bandeira Jiu-Jitsu",
            ["Silva"] = "Furia Curitiba",
            ["Prado"] = "Furia Curitiba",
            ["Fuentes"] = "Monte Andino MMA",
            ["Quiroga"] = "Monte Andino MMA",
            ["Roth"] = "Low Country Kickboxing",
            ["O'Rourke"] = "Ironside MMA",
            ["Brody"] = "Ironside MMA",
            ["Volkov"] = "Volk Sambo Club",
            ["Baranov"] = "Volk Sambo Club",
            ["Morita"] = "Kobudo Dojo",
            ["Sato"] = "Kobudo Dojo",
            ["Rattanak"] = "Emerald Tiger",
            ["Chaiya"] = "Emerald Tiger",
            ["Saengkham"] = "Emerald Tiger",
            ["Marsh"] = "Harbour City Fight Team",
            ["Rennie"] = "Harbour City Fight Team",
            ["Rahiri"] = "Harbour City Fight Team",
            ["Te Ariki"] = "Harbour City Fight Team",
            ["Ndlovu"] = "Highveld Fight Club",
            ["Mokoena"] = "Highveld Fight Club",
            ["Okafor"] = "Sahel Warriors",
            ["Mensah"] = "Sahel Warriors",
        };

    /// <summary>
    /// Who the authored fighters ARE, by last name. The bios already say all of
    /// this out loud — O'Rourke "predicts rounds, calls shots", Volkov has "no
    /// entrance music, no interest in leaving", Al-Marri "reads every offer sheet
    /// that lands" — but until temperament existed the sim couldn't hear it, and
    /// read a fighter's character off which portrait the art pack handed them.
    /// Anyone not listed gets the rolled disposition like everybody else.
    /// </summary>
    public static IReadOnlyDictionary<string, Disposition> Dispositions { get; } =
        new Dictionary<string, Disposition>(StringComparer.Ordinal)
        {
            // The talkers.
            ["O'Rourke"] = Disposition.Loudmouth,        // predicts rounds, calls shots
            ["Boone"] = Disposition.Showman,             // hands low, chin high, crowd on its feet
            ["Vandermeer"] = Disposition.Loudmouth,      // press conferences need a censor
            ["Donovan"] = Disposition.Loudmouth,         // called Volkova a porcelain princess in four languages
            ["Marchand"] = Disposition.Loudmouth,        // "a watch that tells the wrong time"
            ["Kang"] = Disposition.Loudmouth,            // stokes the Korea–Japan rivalry in both languages
            ["Vance"] = Disposition.Showman,             // knocked out a troll on camera and made a career of it
            ["Ferreyra"] = Disposition.Showman,          // hat on the cage post, chin in the air
            ["Jackson"] = Disposition.Showman,           // the agent takes more meetings than the coach
            ["Barrios"] = Disposition.Showman,           // insufferable about the belt, and knows it
            ["Kondo"] = Disposition.Showman,             // screams into every entrance
            ["Volkova"] = Disposition.Showman,           // movie-star poise; headlines anywhere and knows it
            ["Carvalho"] = Disposition.Showman,          // Ipanema beach-boxing showman, won't let anyone forget the sell-out
            ["Reyes"] = Disposition.Loudmouth,           // crashing the champion's interviews

            // The quiet ones.
            ["Morita"] = Disposition.QuietProfessional,  // has never needed to speak
            ["Mercer"] = Disposition.QuietProfessional,  // nothing rattles him
            ["Duarte"] = Disposition.QuietProfessional,  // the sport's gentlest man
            ["Tsereteli"] = Disposition.QuietProfessional, // declined to trash talk in three languages
            ["Nowak"] = Disposition.QuietProfessional,   // signs whatever IFA puts in front of him, mostly unread
            ["Batyrov"] = Disposition.QuietProfessional, // the purest pedigree on the market
            ["Kipiani"] = Disposition.QuietProfessional, // no entrance theatrics, no interviews

            // The students and technicians.
            ["Roth"] = Disposition.Student,              // deconstructs opponents joint by joint
            ["Weiss"] = Disposition.Student,             // distance and timing as precision instruments
            ["Vestergaard"] = Disposition.Student,       // counts rhythm the way others count reach
            ["Kubo"] = Disposition.Student,              // hunts submissions like frame-perfect inputs
            ["Fuentes"] = Disposition.Student,           // rolling before he could read
            ["Grant"] = Disposition.Student,             // raised on film study and farm work

            // The hard men and the honest ones.
            ["Volkov"] = Disposition.CompanyMan,         // a locker with his name welded on
            ["Vukovic"] = Disposition.CompanyMan,        // burned an AFC offer on camera
            ["Sato"] = Disposition.CompanyMan,           // means to retire in Japan
            ["Takeda"] = Disposition.CompanyMan,         // swears she will never fight anywhere else
            ["Fujimoto"] = Disposition.CompanyMan,       // treats the belt as a family duty
            ["Nakamura"] = Disposition.CompanyMan,       // says he'll retire behind the Kansai Cage curtain
            ["Al-Suwaidi"] = Disposition.CompanyMan,     // fights for the badge, not the bonus
            ["Okafor"] = Disposition.CompanyMan,         // fights for peanuts by choice; the mission is the scene
            ["Mensah"] = Disposition.CompanyMan,         // the continent's scene should be built, not exported
            ["Halloran"] = Disposition.Warhorse,         // second decade of small-hall main events
            ["Lindqvist"] = Disposition.Warhorse,        // beat Lindqvist and you're somebody
            ["Kovac"] = Disposition.Warhorse,            // slept on the mats he now headlines above
            ["Delgado"] = Disposition.Warhorse,          // never been in a boring fight
            ["Hale"] = Disposition.Warhorse,             // four years of promoters calling him unwatchable
            ["Tanner"] = Disposition.Warhorse,           // certain the story has one more chapter
            ["Seo"] = Disposition.Warhorse,              // the shift-worker's grudge, replayed every day

            // Money talks.
            ["Al-Marri"] = Disposition.Mercenary,        // reads every offer sheet that lands
            ["Nasser"] = Disposition.Mercenary,          // loyalty was not part of the negotiation
            ["Haddad"] = Disposition.Mercenary,          // follows the money and doesn't apologize
            ["Dudaev"] = Disposition.Mercenary,          // three orgs' belts, mourned none of them
            ["Cole"] = Disposition.Mercenary,            // loyalty is to the climb
            ["Huanca"] = Disposition.Mercenary,          // CDC can't pay him what's coming
            ["Brooks"] = Disposition.Mercenary,          // calls the belt a stepping stone at his own party
            ["Slater"] = Disposition.Mercenary,          // treats every defense as an audition
            ["Hayes"] = Disposition.Mercenary,           // built for bigger stages, and knows it

            // Short fuses.
            ["Vela"] = Disposition.Hothead,              // every round is an insult that needs answering
            ["Orlov"] = Disposition.Hothead,             // bare-knuckle in St Petersburg car parks
            ["Rennie"] = Disposition.Hothead,            // after-parties are a regional policy problem
            ["Urbonas"] = Disposition.Hothead,           // a demolition contractor, literally
            ["Weeks"] = Disposition.Hothead,             // the belt lives on the wrong side of Woodward
            ["Rivas"] = Disposition.Hothead,             // walked out over a purse dispute
        };

    /// <summary>
    /// Deals the LORE already describes, by last name — a multiplier on the
    /// formula purse. Okafor "fights for peanuts by choice"; the Gulf "pays the
    /// richest purses in the sport"; Cole's whole story is that he's underpaid
    /// and about to be gone. None of that was anywhere in the data, so every
    /// fighter in the world was paid the same ability curve regardless of what
    /// their biography said about them.
    /// </summary>
    public static IReadOnlyDictionary<string, decimal> PurseFactors { get; } =
        new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            // The Gulf buys what it can't grow.
            ["Al-Marri"] = 2.2m,
            ["Nasser"] = 2.0m,
            ["Haddad"] = 1.9m,
            ["Dudaev"] = 1.9m,
            ["Kipiani"] = 1.8m,
            // Fighting for the cause, not the cheque.
            ["Okafor"] = 0.35m,      // peanuts by choice — the mission is the scene
            ["Mensah"] = 0.5m,
            ["Al-Suwaidi"] = 0.6m,   // the badge, not the bonus
            ["Takeda"] = 0.55m,      // will never fight anywhere else, and they know it
            ["Nakamura"] = 0.6m,
            // Underpaid and going somewhere.
            ["Cole"] = 0.45m,        // the first serious offer takes him
            ["Huanca"] = 0.4m,       // CDC can't pay him what's coming
            ["Pryor"] = 0.4m,        // headlines for gas money
            ["Kondo"] = 0.5m,
            ["Saengkham"] = 0.5m,
            // Loyalty priced in: the houses pay their franchise men properly.
            ["Vukovic"] = 1.5m,
            ["Volkov"] = 1.3m,
            ["Sato"] = 1.5m,
        };

    /// <summary>
    /// A day-one bond between two authored people. A and B are LAST NAMES, or the
    /// full "First Last" where the last name is shared by more than one authored
    /// person (Wallace and Paulo Bandeira) — a bare shared surname resolves to
    /// nobody rather than to whichever man was written first.
    /// </summary>
    public sealed record DesignedBond(string A, string B, RelationshipKind Kind, int Strength, string Note);

    /// <summary>
    /// The warm half of the relationship graph, which used to start completely
    /// empty even though the bios already asserted every one of these: Volkov
    /// drilled Baranov personally, Okafor and Mensah headline side by side on
    /// principle, Takeda has been carrying Arai's bags since she was fourteen.
    /// A MENTOR is the retired great (A) in a fighter's (B) corner — worth real
    /// development, and a storyline that pays off when the protégé wins gold.
    /// </summary>
    public static IReadOnlyList<DesignedBond> Bonds { get; } = new DesignedBond[]
    {
        // The masters and their people (A is the elder).
        new("Volkov", "Baranov", RelationshipKind.Mentor, 80, "drilled personally since the Perm youth club"),
        new("Matsuda", "Sato", RelationshipKind.Mentor, 85, "the lineage of the ICF lightweight belt, handed down"),
        new("Arai", "Takeda", RelationshipKind.Mentor, 88, "carried her bags at fourteen; carries her belt now"),
        new("Wongsawat", "Rattanak", RelationshipKind.Mentor, 75, "the old temple's last student"),
        new("Vasquez", "Reyes", RelationshipKind.Mentor, 72, "the Guadalajara counter, taught by the Mexican who came first"),
        new("Kilgore", "Vandermeer", RelationshipKind.Mentor, 66, "took the short-notice calls together"),
        new("Obiora", "Okafor", RelationshipKind.Mentor, 78, "the Lagos room that raised the Lion"),
        new("van Zyl", "Ndlovu", RelationshipKind.Mentor, 64, "the man who couldn't leave, teaching the one who might"),
        new("Boyd", "Mercer", RelationshipKind.Mentor, 70, "the old heavyweight great in the lightweight champion's corner"),
        new("Paulo Bandeira", "Souza", RelationshipKind.Mentor, 68, "three generations of the academy, and now a fourth name"),

        // The friendships the sport already knows about.
        new("Okafor", "Mensah", RelationshipKind.Friend, 80, "headline the continent's cards side by side, on principle"),
        new("Takeda", "Kondo", RelationshipKind.Friend, 72, "the house's heart and the house's future, same locker room"),
        new("Halloran", "Barone", RelationshipKind.Friend, 55, "years of Midwest undercards between them"),
        new("Lindqvist", "Corrigan", RelationshipKind.Friend, 50, "the yardstick and the man who came back"),

        // Blood.
        new("Rattanak", "Chaiya", RelationshipKind.Family, 90, "cousins off the same temple-fair circuit"),
    };
}
