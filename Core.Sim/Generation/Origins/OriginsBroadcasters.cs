using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Generation.Origins;

/// <summary>What sort of outfit a media partner is.</summary>
public enum OriginsPartnerKind
{
    TapeDistributor, PpvCarrier, LateNightCable, SportsCable, NationalNetwork, ForeignNetwork, Streamer, StreamingPlatform,
}

/// <summary>
/// A fictional media partner of an era. <see cref="Media"/> is the world-level media kind it belongs to
/// (null for tape sales and for foreign networks, which are country-local and use their country's scene
/// rather than a world capability). <see cref="EarliestMonth"/> is months since 1 January 1993 and is never
/// earlier than the chapter that unlocks its kind.
/// </summary>
public sealed record OriginsMediaPartner(
    string Name, string Tag, OriginsPartnerKind Kind, MediaKind? Media, int Era, string HomeCountry, int EarliestMonth, string Lore);

/// <summary>A fictional sponsor of an era; <see cref="Type"/> is a key of <see cref="OriginsCapabilities.SponsorKeys"/>.</summary>
public sealed record OriginsEraSponsor(
    string Name, string Tag, string Type, int Era, string HomeCountry, int EarliestMonth, string Lore);

/// <summary>
/// The fictional media partners and sponsors of the Origins timeline, by era. Nothing real stands behind
/// any of them; the names avoid everything in today's <c>MediaService</c> ladder and <c>Sponsorships</c>
/// market. Authored ahead of the wiring. Append-only.
/// </summary>
public static class OriginsBroadcasters
{
    public static IReadOnlyList<OriginsMediaPartner> Media { get; } = new OriginsMediaPartner[]
    {
        new("Lantern Video Distribution", "The tape that goes through the post.", OriginsPartnerKind.TapeDistributor, null, 1, "USA", 0,
            "A two-room company in Burbank that duplicates videotapes in a back office and ships them in padded envelopes. It buys the rights to the first Crucible cards for almost nothing and sells them through the back pages of martial-arts magazines at twenty-nine dollars a tape. By the end of the first year its mail-order clerks know more about the sport's fans than any promoter does."),
        new("Keystone Event Network", "Events, in your living room, for a price.", OriginsPartnerKind.PpvCarrier, MediaKind.PpvCarrier, 1, "USA", 0,
            "A pay-per-view carrier built for boxing nights and concert specials, run by executives who are still working out whether the new combat cards are an event or an embarrassment. It takes its cut off the top and keeps a lawyer on speed-dial. When the Backlash comes it is the first to blink, and the last to admit it."),
        new("Nocturne Cable", "Where the night shift lives.", OriginsPartnerKind.LateNightCable, MediaKind.SpecialtyCable, 2, "USA", 54,
            "A small cable channel that fills its small hours with old kung-fu films, bowling and an astrologer. It has a hole at one in the morning and no one to complain, which is how it becomes the first television home the sport can find during the lean years. Its audience is mostly taxi drivers, nurses and bartenders. Every one of them writes letters."),
        new("Marquee Home Events", "The big night, brought home.", OriginsPartnerKind.PpvCarrier, MediaKind.PpvCarrier, 3, "USA", 84,
            "A second pay-per-view carrier founded by a former boxing publicist who thinks the first carriers were fools to leave. It returns to the sport only after the commissions have written their rules, and signs sanctioned promotions with a smile and a long contract. Its trademark is a gold curtain that opens at the start of every card."),
        new("Hokuto Television", "Friday nights, for the whole family.", OriginsPartnerKind.ForeignNetwork, null, 3, "JPN", 72,
            "A mid-sized Tokyo network whose Friday schedule once belonged to professional wrestling. When the Association's founder proposes a weekly show with no script, its programmers are sceptical and its accountants are intrigued. The first season's ratings surprise them both. The sport's first network deal anywhere is struck in a lacquered meeting room with a very long table."),
        new("Rede Litoral", "O Brasil inteiro, todo sabado.", OriginsPartnerKind.ForeignNetwork, null, 3, "BRA", 84,
            "A Brazilian network that began in a coastal city with a single transmitter and a stubborn owner. It picks up the gym-duel style of Rio's Saturday nights and puts it in front of a country that has always loved a challenge. Its commentators shout, its cameras shake and its audience, which is huge, is not remotely interested in whether either is a problem."),
        new("Frontline Sports Network", "Everything that is a sport, all day.", OriginsPartnerKind.SportsCable, MediaKind.CableSports, 4, "USA", 108,
            "A national cable sports channel that has run out of things to buy: it has the basketball, the golf and half the hockey. Its programming chief, a former radio announcer with a nose for an audience, takes the first sizeable combat deal in a boardroom with a view of the harbour. He is told he will regret it. He does not."),
        new("Northlight Sports", "The north's own sports channel.", OriginsPartnerKind.SportsCable, MediaKind.CableSports, 4, "GBR", 114,
            "A British cable sports channel with a loyal evening audience and a reputation for taking unfashionable sports seriously. It buys European rights to the new sport on a three-year deal and stages the first British broadcast from a boxing hall in Manchester. Its commentary team, mostly retired boxers, learns the ground game with open admiration."),
        new("Harbor Broadcasting Company", "America's Saturday night.", OriginsPartnerKind.NationalNetwork, MediaKind.PrimeBroadcast, 5, "USA", 144,
            "One of the country's three big over-the-air networks, with a family-hour policy that fills a standards department. It resists the sport for years and then, after a night when everyone watched, reverses itself in a week. Its contract is the strictest the sport has seen: language, conduct, no stunts. In return it delivers the largest audience in the sport's history."),
        new("Austral Television", "From Perth to Cairns.", OriginsPartnerKind.NationalNetwork, MediaKind.PrimeBroadcast, 5, "AUS", 156,
            "A national Australian broadcaster whose weekend schedule has always been built around football, cricket and racing. Its sports editor, a one-time wrestler, signs the sport on a hunch and a handshake. The first card, from a stadium in Sydney, outdraws the cricket. The sports pages stop calling it a novelty."),
        new("Estrella Television", "Para toda la familia.", OriginsPartnerKind.NationalNetwork, MediaKind.PrimeBroadcast, 5, "MEX", 168,
            "A Mexico City network with a hundred years of masked wrestling and boxing on its tapes. It builds its Saturday night around the sport and sells it across Latin America, with a house style of long ring entrances and shouting announcers. Fighters from a dozen countries learn that to be famous in the region, they must first be famous on this channel."),
        new("Gulf Horizon Media", "The region's sports, in every language.", OriginsPartnerKind.SportsCable, MediaKind.CableSports, 6, "UAE", 216,
            "A well-funded broadcaster in Abu Dhabi that pays rights fees large enough to move whole cards to its own arenas. It commissions glossy documentaries about fighters from the Caucasus to the Cape and broadcasts them to thirty countries. Promoters regard its offers as a prize and a leash in equal measure."),
        new("Cascade Live", "Your own channel, built for you.", OriginsPartnerKind.StreamingPlatform, MediaKind.InHouseStream, 6, "USA", 204,
            "A platform company founded by two engineers who had worked for a dozen failed video start-ups and learned from each. It builds subscription services for promotions that would rather own their audiences than rent them: a branded app, a billing system, a library and a live feed that survives a main event. Its customer list reads like a map of the sport's mid-size promoters."),
        new("Skybridge Video", "The sport, across the borders.", OriginsPartnerKind.Streamer, MediaKind.MajorStream, 6, "NED", 252,
            "A European streaming service headquartered in Amsterdam that begins with football highlights and ends, to its own surprise, as the continent's home of the sport. It buys the rights to events from six promotions and dubs them into nine languages. Its viewers are the youngest of any broadcaster in the business."),
        new("Lumen Play", "Every night, anywhere.", OriginsPartnerKind.Streamer, MediaKind.MajorStream, 6, "USA", 240,
            "A global streaming giant with hundreds of millions of subscribers and an appetite for live events it cannot get anywhere else. Its programming executives study the sport for a year before they open their chequebook. The fee they pay is the largest in its history, and the condition is plain: spectacle. Its logo, a white circle on a dark screen, becomes the last thing a fighter sees before the walk."),
        new("Tamarind Play", "Aapka apna khel.", OriginsPartnerKind.Streamer, MediaKind.MajorStream, 6, "IND", 264,
            "India's largest homegrown streaming service, built on cheap data and an enormous audience that came to the sport late and watches it hungrily. It signs three of the biggest promotions in a single season and builds a studio in Mumbai to produce its own commentary in six languages. When a home fighter wins a title, its servers go down."),
    };

    public static IReadOnlyList<OriginsEraSponsor> Sponsors { get; } = new OriginsEraSponsor[]
    {
        new("Peak Protein Mail-Order", "Get big. Get it posted.", "Supplement", 1, "USA", 1,
            "A mail-order outfit that sells powders by the tub from a post-office box in Ohio, with a catalogue of before-and-after photographs and a free sample for every order. It has never sponsored anything. It buys a corner of a Crucible poster for a hundred dollars and sells a thousand tubs. From that moment it is the sport's most loyal advertiser."),
        new("Dojo Digest", "The monthly magazine of the martial arts.", "Local", 1, "USA", 1,
            "A glossy monthly with a circulation of sixty thousand and a back-page ad section that has run for twenty years. It trades full-page adverts for ringside seats, and runs the first photograph of a ring in a mainstream-looking magazine. Its editors disagree among themselves about whether the sport is a vulgar development or the best thing that has happened to the arts."),
        new("Cafe Tucano", "O cafe de Tijuca.", "Local", 1, "BRA", 2,
            "A coffee roaster from Rio's north zone with a chain of six cafes and a loyal clientele of gym owners. Its founder is a friend of the lawyer who started the Saturday nights, and pays for the generator on the nights the garage loses power. His logo on the rolling door is the first sponsorship in the history of Brazilian fighting."),
        new("Kumano Instant Noodles", "Ready in three minutes, like a good fight.", "Local", 1, "JPN", 6,
            "A noodle maker from a Tokyo ward who sponsors baseball teams, a sumo stable and a children's choir. Its president, a keen watcher of the wrestling halls, decides one winter that the Association's Friday nights need a banner. He is right; the banner, a cartoon bowl with a pair of bared fists, becomes one of the best-known in the country."),
        new("Ironhide Gear", "Built to be hit.", "Equipment", 3, "USA", 60,
            "A small maker of heavy bags, training gloves and mouthguards from a factory in Pittsburgh, run by a former boxer. When the first promotions begin to require gloves it is the first to offer a purpose-built pair: thin, open-fingered and double-stitched. It becomes the sport's default supplier within two seasons, and its founder's cheerful face appears in every training-gym catalogue."),
        new("Longneck Lager", "The beer for fight night.", "Brewery", 4, "USA", 84,
            "A regional brewery from the American heartland with a retro label, a pickup-truck fleet and a marketing manager who has noticed that the crowd at a fight drinks. Its sponsorship of the first sanctioned cards is tied to attendance, which it grows by handing out coupons at the door. It sponsors the sport for twenty years, and never once changes the label."),
        new("Thunderhead Energy", "Hit the ceiling.", "EnergyDrink", 4, "USA", 108,
            "A canned energy drink launched by a former snowboard promoter with an unlimited marketing budget and a taste for tattoo-covered ambassadors. It chooses the sport because the audience matches its customer: young, loud and in love with excess. It pays for star power and title fights, and the cheque comes with a requirement of its own: no one may drink a rival's can on camera."),
        new("Prairie Life Insurance", "Because you never know.", "National", 4, "USA", 120,
            "A Midwestern insurance company, old and careful, whose advertising has never been bolder than a cartoon owl. When a young marketing executive proposes a title-fight sponsorship it takes eighteen months of committee meetings and a letter from the commission. The slogan, 'Because you never know', becomes an unlikely hit with the fight crowd."),
        new("Beacon Mobile", "Always in range.", "National", 5, "USA", 150,
            "A telephone company that sells cheap plans to young people and needs a way to reach them. It sponsors the sport's first big television nights, with a banner over the title fight and a text-message vote for the fight of the night. The vote crashes its own network on a Saturday in the first year. The company loves the publicity."),
        new("Hearthstone Trucks", "Work hard. Haul harder.", "National", 5, "USA", 156,
            "A pickup-truck maker with a factory in the heartland and an audience that matches the sport's own: men who work with their hands. It sponsors national title fights and builds a one-off truck for the champion of every division. The trucks are given away at the arena doors to a screaming crowd, which is about as good as advertising gets."),
        new("Aurora Athletics", "Wear the arena.", "Global", 5, "GBR", 168,
            "A sportswear giant based in the English Midlands whose logo is on shirts in every football league on earth. It chooses the sport because its customers are beginning to ask for fight shorts. It signs the first global apparel contract, and designs the champions' walk-out kits in a studio of forty people. The whole world's gyms buy them."),
        new("Solstice Air", "The world, closer.", "Global", 6, "UAE", 204,
            "A Gulf airline that flies to every continent and sponsors football clubs, tennis tournaments and a motor race. It pays to put its name across a canvas in a dozen arenas on four continents, and to fly the fighters to every one of them in business class. Fighters who were once flown to cards in the hold of a night bus now wear its logo on their trunks."),
        new("Quill Technologies", "Everything, connected.", "Global", 6, "KOR", 228,
            "A South Korean electronics maker with phones, televisions and refrigerators in every home on earth. It signs the sport's biggest technology deal, with a banner on every broadcast and an exclusive replay screen in every arena. Its engineers, to everyone's surprise, turn out to be the sport's most dedicated fans, and install scoring tablets for the judges at ringside."),
    };
}
