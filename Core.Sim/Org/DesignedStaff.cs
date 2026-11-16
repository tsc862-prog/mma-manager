using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Org;

/// <summary>A named back-office professional: who they are, how good, and what they've done.</summary>
public sealed record DesignedStaffMember(
    string Name, StaffRole Role, int Quality, string Background,
    // The org they already work for (by abbreviation); null = on the open market.
    string? Org = null);

/// <summary>The person whose money it is, and what they're like to work for.</summary>
public sealed record DesignedOwner(string Org, string Name, int FoundedYear, string Note);

/// <summary>
/// The people behind the desks.
///
/// The back office used to be four anonymous candidates per role with names
/// pasted together from two word lists, no history, and no promotion in the
/// world employing a single one of them — so hiring was picking a number, and
/// poaching a rival's scout was impossible because rivals had no scouts. And
/// every board note in the game was signed "The Owner", who did not exist.
/// </summary>
public static class DesignedStaff
{
    public static IReadOnlyList<DesignedOwner> Owners { get; } = new DesignedOwner[]
    {
        new("AFC", "Katherine Ross", 1997,
            "Inherited the company from her father and has spent twenty years proving she was the better operator. Rewards growth, forgives one bad quarter, and has never once been talked out of a decision. Bill Hennessy's name is the one she does not discuss in meetings."),
        new("VMMA", "Baron Aleksander Reuss", 2004,
            "Old European federation money in a very good suit. Cares about prestige, television, and being seen to be first — the balance sheet is somebody else's department until suddenly it isn't. Describes Pavel Ilic's gym as a former tenant, and himself as a man with a great many."),
        new("IFA", "Pavel Ilic", 2009,
            "Ran the gym before he ran the company and still tapes hands on fight night. Wants hard fights and full houses; treats marketing spend as a personal insult. His gym rented a hall belonging to the Baron's family federation until the lease was not renewed; the mats went into a car park, and so did the next years of the company's history. He remembers the date and does not say it."),
        new("ICF", "Hideo Kanemura", 1991,
            "The sport's most patient man — third-generation promoter, keeper of the New Year tradition. Judges a year by whether the belts meant more in December than they did in January."),
        new("GFC", "Sheikh Faisal bin Rashid", 2019,
            "Runs the promotion as a statement, not a business. Money is not the constraint; being embarrassed is. Wants names, stadiums and headlines, this quarter."),
        new("UFL", "Bill Hennessy", 2006,
            "A television man who bought a fight promotion and thinks in ratings windows. Loyal to whoever fills the slot; ruthless about whoever doesn't. His network carried the AFC's early television years until the Ross family took the rights in-house, and the league he bought is, among other things, a slot he intends to keep."),
        new("RCS", "Tiago Moreira", 2018,
            "One of the three cariocas who started it with a truck and a grievance, and the only one still speaking to the other two. Pays late, apologises sincerely, and would sell his car for a sell-out. The other two cariocas still bill themselves as founders on the invoice and have not returned the truck."),
        new("SCM", "Denise Cartwright", 2010,
            "Ran arenas before she ran fights and knows exactly what a building costs by the hour. Unflappable, unsentimental, and genuinely popular with the roster."),
        new("CK", "Dona Marisa Bittencourt", 1988,
            "The widow of the founder and the reason the doors are still open. Keeps the family estate together by instinct and grudge; regards RCS as children who need a smack. Waldemar Pires was her husband's great signing, and she still sends the old champion a case of coffee at Christmas."),
        new("CC", "Marty Feldstein", 2012,
            "Knows every matchmaker's mobile number and every fighter's mother. Makes his money selling the talent upward and has made peace with it."),
        new("SCL", "Amara Nwosu", 2021,
            "Left a corporate job to bet everything on a continent. Runs shows on borrowed chairs and sheer conviction, and would rather fold than export her best fighter cheaply."),
        new("GLF", "Duane Pfeiffer", 2003,
            "Third generation of a family that has promoted something in that gymnasium since the war. Wrestling first, opinions second, expenses never."),
        new("KAC", "Ayumi Sekine", 2008,
            "Was a fighter, then a matchmaker, now the owner — the only person in the building who has done all three. The women's division exists because she refused to stop asking. She shared cards with Michiko Arai before she ever matched one, and kept the gloves."),
        new("CDC", "Ernesto Chambi", 2014,
            "A former altitude cyclist who understood immediately what visiting fighters' lungs would do. Small budget, long memory, excellent coffee. Drives his champion Mateo Quispe to the cards himself, and has not yet admitted what he will do about Alonso Huanca."),
        new("MCC", "Sal Rizzo", 2011,
            "Owns two body shops and a fight promotion, in that order of profitability. Wants knockouts and doesn't want to hear about footwork. Fixes the cars of both Barone's family and Weeks's, and has been asked by both to stay out of it."),
        new("BLB", "Rihards Kalnins", 2007,
            "Announces the shows himself because nobody else does it properly. Has never taken a salary and would be offended by the suggestion."),
        new("MFC", "Nok Wattana", 2013,
            "Grew up on the temple-fair circuit and books the cards from memory. Every scout in Asia has her number; she charges them all the same. A cousin of hers sold Arthit Saengkham his first pair of shorts, which she raises in negotiations."),
        new("CPF", "Bongani Zulu", 2015,
            "A former doorman who noticed the neighbourhood's arguments could be sold tickets. Fiercely local, allergic to debt."),
        new("PMP", "Hector Ravelo", 2009,
            "Believes an entrance should take longer than a round and has been proved right at the box office and wrong by his accountants."),
    };

    public static IReadOnlyList<DesignedStaffMember> Staff { get; } = new DesignedStaffMember[]
    {
        // The names the industry knows — mostly already employed, which is what
        // makes them worth poaching.
        new("Gloria Pemberton", StaffRole.Marketing, 88,
            "Sold out a stadium for a fight nobody wanted by making it about something else entirely. Expensive, and worth it. Yusuf Bakr, who built the Gulf's stadium shows, was her junior before the money called, and she says the best tricks he is using on her are hers.", "AFC"),
        new("Dr. Ruth Nakashima", StaffRole.Medical, 90,
            "Wrote the concussion protocol half the sport now uses. Will overrule a promoter in front of a live camera and has. Katherine Ross says she would not have it any other way, and has the transcripts.", "AFC"),
        new("Emmett Cole", StaffRole.Scouting, 86,
            "Found four champions in regional halls before anyone had heard of them. Watches tape at 0.25 speed and takes notes on paper. Dane Mercer was one of the four, a meatpacking night-shifter he watched on tape for a year before anyone would let him make the call.", "AFC"),
        new("Bernard Adjei", StaffRole.HeadCoach, 84,
            "Corner voice of a generation — calm at the worst possible moments. Fighters ask for him by name in their contracts. Théo Marchand and Noah Weiss have both asked for him; he says he will work either corner, as long as nobody expects him to pick.", "VMMA"),
        new("Sabine Kroeger", StaffRole.Marketing, 82,
            "Ran opera houses before fight nights and treats a card like a programme: pacing, lighting, and a proper interval. Baron Reuss has asked her to stop calling the fights productions, and she calls them that to his face.", "VMMA"),
        new("Dr. Yelena Sorokina", StaffRole.Medical, 79,
            "Trauma surgeon two nights a week, cageside the rest. Nothing that happens in a cage has ever surprised her. She stitched up a bare-knuckle car-park fighter named Orlov in St Petersburg before either had a job in the sport, and has not mentioned it to Vanguard.", "IFA"),
        new("Kenzo Arakaki", StaffRole.Scouting, 83,
            "Has watched every amateur card in Japan for eleven years and remembers all of them. Files by hand, in pencil. His file on Arthit Saengkham is older than the ICF's offer, and he would like that noted.", "ICF"),
        new("Yusuf Bakr", StaffRole.Marketing, 80,
            "Made a stadium show out of a promotion nobody had heard of in eighteen months, on a budget nobody will confirm. He learned the trade as Gloria Pemberton's junior at the AFC, is grateful, and says the money is very good.", "GFC"),
        new("Chuck Delaney", StaffRole.Marketing, 71,
            "Twenty years of local television and a rolodex of every regional sponsor in the Midwest. Talks in ad breaks. Bill Hennessy has taken to timing his meetings against the commercial clock.", "UFL"),
        new("Paulinho Serra", StaffRole.HeadCoach, 76,
            "Grew up in the Cage Kings warm-up room and can fix a fighter's hips in one round of pad work. Carried Waldemar Pires's gym bag as a boy, and has not yet been forgiven for putting it down.", "CK"),
        new("Wiremu Tane", StaffRole.HeadCoach, 72,
            "Coaches like a shop foreman: everything written on a whiteboard, nothing raised above a conversation. At Harbour City, Tane writes the plan on the whiteboard and Tama Solomona holds the pads, and the two have never agreed on the plan.", "SCM"),
        new("Dr. Femi Balogun", StaffRole.Medical, 68,
            "Runs a clinic in Lagos six days a week and works fight nights for expenses. Has stopped more fights than any referee. Has stitched Chidi Okafor more often than he has billed him, which is to say not at all.", "SCL"),

        // On the market — the people a smart owner hires before somebody else does.
        new("Rosalind Okoye", StaffRole.Scouting, 81,
            "Left Vanguard's scouting department after it refused to sign a Nigerian welterweight off a university mat. He is the AFC champion now, and she has never mentioned it once. She has kept a file on Fatima Diallo since early in Diallo's career."),
        new("Dr. Anton Vrba", StaffRole.Medical, 77,
            "Spent a decade in ringside medicine in three countries, then a year off after a bad night he doesn't discuss."),
        new("Marguerite Lavoie", StaffRole.Marketing, 74,
            "Fashion PR until she discovered fighters photograph better than models and complain less about the hours."),
        new("Big Tom Halvorsen", StaffRole.HeadCoach, 73,
            "Forty years on the mats, two artificial knees, and an unbroken record of turning brawlers into wrestlers. Coached Nathan Kroll as a teenager and still disapproves of the sermons."),
        new("Priya Ramanathan", StaffRole.Scouting, 70,
            "Built a database of every amateur result in South Asia in her spare time and got head-hunted for it. Her database had Rohan Malhotra's amateur results before the call centre he quit had finished his exit paperwork."),
        new("Ossie Blackwood", StaffRole.HeadCoach, 66,
            "Ran a boxing gym above a chip shop for thirty years. Everything he teaches is about the feet. The room above his old gym is now Alfie Rooke's Ironside, and he considers the rent overdue."),
    };
}
