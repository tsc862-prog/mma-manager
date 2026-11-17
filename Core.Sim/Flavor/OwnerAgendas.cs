using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Flavor;

/// <summary>What an owner is after. Text-only for now: the AI does not read it (see the hand-off notes).</summary>
public enum AgendaKind
{
    /// <summary>Growth, the next quarter, the operator's pride.</summary>
    Growth,
    /// <summary>Being seen to be first: television, the best building, the best name.</summary>
    Prestige,
    /// <summary>Burying a rival house.</summary>
    BuryRival,
    /// <summary>The belts must mean something; the long view.</summary>
    Legacy,
    /// <summary>Names, stadiums, headlines, now.</summary>
    Spectacle,
    /// <summary>Ratings, the slot, the contract.</summary>
    Money,
    /// <summary>Sell the best talent upward and live on the commission.</summary>
    Sell,
    /// <summary>A hometown champion, a home crowd, a place that is proud.</summary>
    Hometown,
    /// <summary>The craft itself: the standard of the fights.</summary>
    Craft,
    /// <summary>Keep the doors open.</summary>
    Survive,
}

/// <summary>
/// One owner's agenda in words: what they want, a line for the org page, and what they say when their house signs
/// a name, crowns a champion, loses a star, or is outbid. Lines are quoted, in the owner's voice, and never carry
/// a number, a rating or a prediction the sim could overturn. Tokens: {F} the fighter's full name, {L} the last
/// name, {Org} the house that did the outbidding. Keyed by the designed house (its abbreviation) AND the owner's name AND the world's mode, so a house founded
/// later with a random owner name can never pick one up by coincidence. Lines carry no word about the fighter's sex
/// and no claim of fact about the fighter or the house beyond what the moment is: attitude, not assertion. Append-only.
/// </summary>
public sealed record OwnerAgenda(
    string Owner, string House, WorldMode Mode, AgendaKind Wants, string PageLine,
    string[] Signs, string[] Crowns, string[] Left, string[] Retired, string[] Outbid);

public static class OwnerAgendas
{
    public static IReadOnlyList<OwnerAgenda> All { get; } = new OwnerAgenda[]
    {
        new("Katherine Ross", "AFC", WorldMode.Modern, AgendaKind.Growth,
            "What she wants is growth, quarter on quarter, and the proof that the company was always hers to run better than her father did.",
            new[] { "{F} joins a company that likes to grow. We will see what the quarter says.", "The books will tell me about {F} in time. I am hopeful." },
            new[] { "A champion is the company's best argument, and {F} has made it again.", "I do not give speeches. {F} has given one." },
            new[] { "{F} leaves a hole, and I intend to fill it with growth, not sentiment." },
            new[] { "{F} gave the company a great deal. I will say so in daylight, and in writing." },
            new[] { "{Org} paid a price for {F} that I would not have paid in my father's time, and I did not pay it in mine." }),
        new("Baron Aleksander Reuss", "VMMA", WorldMode.Modern, AgendaKind.Prestige,
            "What he wants is to be seen to be first: the best television, the best building, the name that the continent says before the others.",
            new[] { "Vanguard has {F}, which is what the continent will say and the others will repeat.", "We hope {F} will look well under our lights. A name should be seen under good lights." },
            new[] { "A Vanguard champion is photographed differently. {F} will learn that.", "The continent has a champion, and the name is {F}." },
            new[] { "{F} has left the Vanguard picture. It will be remembered as a loan." },
            new[] { "{F} retires with Vanguard's thanks. The portrait will be hung properly." },
            new[] { "{Org} has bought {F}. I am told it was a great deal of money, which is the only impressive thing about it." }),
        new("Pavel Ilic", "IFA", WorldMode.Modern, AgendaKind.BuryRival,
            "What he wants is hard fights, full houses and, quietly, the Baron's name on a bankruptcy notice; he has not forgotten the lease or who did not renew it.",
            new[] { "{F} signs. That is the whole of the interview.", "We put hard fights on. {F} has been told what that means." },
            new[] { "{F} is champion. Somebody tell the people with the velvet ropes.", "I tape hands on fight night. I will tape {F}'s tighter." },
            new[] { "{F} goes. Fine. The car park was always where we built things." },
            new[] { "{F} is done, and fought honestly. I will tape one more pair of hands in {F}'s honour." },
            new[] { "{Org} outbid us for {F}. I remember the date, as I do all the dates." }),
        new("Hideo Kanemura", "ICF", WorldMode.Modern, AgendaKind.Legacy,
            "What he wants is a year in which the belts meant more in December than they did in January; everything else is a rounding error to him.",
            new[] { "{F} joins a lineage. I hope {L} learns how long it is.", "The belts are older than I am. {F} is welcome to try to be worthy of them." },
            new[] { "A belt on the right shoulder is the whole of my business. {F} has it now.", "The lineage continues. {F} has added a line." },
            new[] { "{F} leaves, and the lineage does not. That is what lineages are for." },
            new[] { "{F} retires. The belts will remember the name." },
            new[] { "{Org} has taken {F}. The belts are still here, and so are we." }),
        new("Sheikh Faisal bin Rashid", "GFC", WorldMode.Modern, AgendaKind.Spectacle,
            "What he wants is names, stadiums and headlines, this quarter; being embarrassed is the one cost he will not pay.",
            new[] { "I did not sign {F} for the money, which I have. I signed for the headline, which I do not.", "{F} will be asked to fill a stadium. If not, I will fill it with something else." },
            new[] { "A Gulf champion. Print the name large: {F}.", "{F} is a statement, and I make statements for a living." },
            new[] { "{F} has gone. Every house is entitled to a bad week; I am entitled to a short one." },
            new[] { "{F} retires. I will see that the farewell is large." },
            new[] { "{Org} outbid me for {F}. It will not be repeated." }),
        new("Bill Hennessy", "UFL", WorldMode.Modern, AgendaKind.Money,
            "What he wants is a slot he can sell, with names in it that sell it; ratings are his unit of account and loyalty is a line item.",
            new[] { "{F} is in the slot. That is the entire review, for now.", "I like {F}. I like ratings. They had better be the same sentence." },
            new[] { "A champion is a promotional asset. {F} is now ours.", "{F} on the belt is a ratings event. I will see you all on Saturday." },
            new[] { "{F} is gone, the slot is not. Somebody call the next name." },
            new[] { "{F} retires. The slot stays open for the next name." },
            new[] { "{Org} paid {F} a rate I would not put on a telecast. I wish them good luck with the invoice." }),
        new("Tiago Moreira", "RCS", WorldMode.Modern, AgendaKind.Hometown,
            "What he wants is a sell-out in Rio and a roster the favelas believe in; the rest, including the truck, can be argued about later.",
            new[] { "{F} signs and Rio is invited to sing. The cheque will arrive on time, I promise.", "I would sell my car for a good name. I hope this is one: {F}." },
            new[] { "Rio has a champion. {F}, take the night; the rest of us will settle up in the morning.", "The truck is parked. The belt is on {F}'s shoulder. Nothing else matters tonight." },
            new[] { "{F} is gone, and I am sorry, truly. Please do not ask me for numbers." },
            new[] { "{F} retires, and I am sorry, and grateful. I will sell the truck for the party." },
            new[] { "{Org} outbid us for {F}. They did not have the truck, and that, in the end, is what we had." }),
        new("Denise Cartwright", "SCM", WorldMode.Modern, AgendaKind.Money,
            "What she wants is a full building at the lowest hourly rate; she knows what a seat costs and what a name adds to it.",
            new[] { "Signing {F} is a good use of an hour of arena time, if it works.", "I like a name that understands a room. I hope {F} does." },
            new[] { "A champion who fills a building is a champion I can afford. Let us see {F} do it.", "{F} on the belt. I will do the arithmetic on Monday." },
            new[] { "{F} has gone. The building has not, and I will fill it." },
            new[] { "{F} retires. I will find the hall a fitting date." },
            new[] { "{Org} paid more for {F} than a building can recoup. I wish them every success with that." }),
        new("Dona Marisa Bittencourt", "CK", WorldMode.Modern, AgendaKind.BuryRival,
            "What she wants is to see the Rio upstart taught some manners, and the family estate handed on whole; she is patient about the first and stubborn about the second.",
            new[] { "{F} comes to an old house and is welcome in it. The old house has manners.", "We sign the way my husband signed: with a handshake and a long memory. {F}, welcome." },
            new[] { "The family has a champion: {F}. The children at the other end of town may take notes.", "{F} on the belt. My husband would have been quiet and pleased, and so am I." },
            new[] { "{F} is gone from the house. The house is still here, and so am I." },
            new[] { "{F} retires with the family's gratitude. The coffee will be sent at Christmas." },
            new[] { "{Org} took {F}. The estate has outlasted worse." }),
        new("Marty Feldstein", "CC", WorldMode.Modern, AgendaKind.Sell,
            "What he wants is a good price for talent that outgrows him: he builds them in the small houses, sells them upward, and has made his peace with the arithmetic.",
            new[] { "{F} has a future I may be able to sell somebody. It just has to happen here first.", "I called every matchmaker's number about {F}. We signed." },
            new[] { "{F} is a champion at Coastal Combat. Somebody, somewhere, is looking at the phone number.", "The belt is the best brochure I have printed, and {F} is in it." },
            new[] { "{F} moves on. That was always the plan, and I am invoicing on time." },
            new[] { "{F} retires. I did not get to sell that one upward, and I am a little sad." },
            new[] { "{Org} paid for {F}. I would have preferred they paid more, and I have told them so." }),
        new("Amara Nwosu", "SCL", WorldMode.Modern, AgendaKind.Hometown,
            "What she wants is a continent's own champions on a continent's own stage; she would rather fold than export her best fighter cheaply.",
            new[] { "I hope {F} stays on the continent. That is what the signature is for.", "A name like {F} is an argument for what we are building, if the signature holds." },
            new[] { "A champion for the continent: {F}. Nobody can say we are not building this.", "{F} has the belt. The next question is who gets to take it from us, and I intend the answer to be no one." },
            new[] { "{F} is gone from the house. I wish it were for a reason I could be proud of; I will find one." },
            new[] { "{F} retires, and the continent is a little smaller for it, and a little prouder." },
            new[] { "{Org} outbid me for {F}. I would rather have folded than matched it, and I did not match it." }),
        new("Duane Pfeiffer", "GLF", WorldMode.Modern, AgendaKind.Craft,
            "What he wants is wrestling first and opinions second: three generations of a family that has promoted something in that gymnasium since the war.",
            new[] { "Wrestling first, opinions second. {F} is welcome to the gymnasium.", "The gymnasium has room for {F}, as it has for any who will work." },
            new[] { "{F} on the belt, in the gymnasium, on folding chairs. This is what we are for.", "A new champion: {F}. My father would have said the same, and said it better." },
            new[] { "{F} is gone. They all go eventually. The gymnasium will have the next one." },
            new[] { "{F} retires. The gymnasium will keep the mat warm." },
            new[] { "{Org} took {F} to a bigger gymnasium. Ours is still warm." }),
        new("Ayumi Sekine", "KAC", WorldMode.Modern, AgendaKind.Craft,
            "What she wants is the best pound-for-pound fighting on earth in a small room, and the women's division kept honest; she was a fighter and a matchmaker first and has not stopped being either.",
            new[] { "{F} signs, and I hope the house suits {L}. That is the review.", "I have been a fighter and a matchmaker, and I know what a signing asks of both. Welcome, {F}." },
            new[] { "Kansai Cage has a champion in {F}. The faithful have always said the best fights are in rooms this size.", "{F} on the belt. I know what the work costs, and so does {L}." },
            new[] { "{F} is gone from the house. I wish {L} well, and the gloves stay on the shelf." },
            new[] { "{F} retires. I kept the gloves, and I will keep these too." },
            new[] { "{Org} outbid us for {F}. I know what the offer was, and I understand it." }),
        new("Ernesto Chambi", "CDC", WorldMode.Modern, AgendaKind.Hometown,
            "What he wants is for the altitude to keep earning its reputation; small budget, long memory, and a stubborn belief that the mountains should send somebody to the world.",
            new[] { "{F} has signed at altitude. I am delighted, and I will drive to the cards myself.", "We count lungs, not cheques, and I hope {F} does the arithmetic of lungs well." },
            new[] { "A Cordillera champion. The mountains have spoken, and the name is {F}.", "{F} has the belt, and the altitude did its share. The mountains always do." },
            new[] { "{F} goes down the mountain. I understand. The coffee is better up here regardless." },
            new[] { "{F} retires. The mountains will keep the name." },
            new[] { "{Org} outbid us for {F}. A small house learns to count lungs, not cheques." }),
        new("Sal Rizzo", "MCC", WorldMode.Modern, AgendaKind.Money,
            "What he wants is knockouts: it is a promotion that sits on top of two body shops, and he says the shops make more but the knockouts are more fun.",
            new[] { "I signed {F}. Footwork does not sell tickets; I hope the rest does.", "{F} is on the card. I am told that is all a person can ask of me." },
            new[] { "A Motor City champion. {F}, congratulations. The paint is on me.", "{F} on top, and nobody asking about footwork. I am a happy owner." },
            new[] { "{F} is gone. Somebody else's problem now; somebody else's car, too." },
            new[] { "{F} is done. I will fix whatever car is outside the hall." },
            new[] { "{Org} outbid us for {F}. I fix cars. I do not bid on them." }),
        new("Rihards Kalnins", "BLB", WorldMode.Modern, AgendaKind.Craft,
            "What he wants is to announce the shows himself and have them done properly; he has never taken a salary and is offended by the suggestion.",
            new[] { "{F} joins us, and I will announce the name properly. Nobody else does.", "Signing {F} is an honour for a small house with a very loud announcer." },
            new[] { "Ladies and gentlemen, the champion: {F}. I have waited a long time to say that and mean it.", "{F} has the belt. I shall announce it again at the next show, with the same pleasure." },
            new[] { "{F} is gone from the card. I shall announce the next name with the same enthusiasm." },
            new[] { "{F} retires. I shall announce the farewell myself, and properly." },
            new[] { "{Org} has taken {F}. They will not announce the name as well as I would have." }),
        new("Nok Wattana", "MFC", WorldMode.Modern, AgendaKind.Hometown,
            "What she wants is the temple-fair circuit kept alive and every scout in Asia calling her first; she charges them all the same.",
            new[] { "{F} signs here, and I will book the card from memory.", "A scout will call me about {F} by lunch. They always do." },
            new[] { "The circuit has a champion: {F}. Every scout in Asia will be told.", "{F} wears the belt. I have the date for the next festival." },
            new[] { "{F} is gone from the house. A scout will call me about the next name by Monday." },
            new[] { "{F} retires from the fair circuit. The festival will find a date for a farewell." },
            new[] { "{Org} outbid us for {F}. A small house sells its best and keeps its memory." }),
        new("Bongani Zulu", "CPF", WorldMode.Modern, AgendaKind.Hometown,
            "What he wants is for the neighbourhood's arguments to sell out, and for the house never to owe anyone a cent; he is fiercely local and allergic to debt.",
            new[] { "{F} signed for what we can afford. I hope the neighbourhood will be loud about it.", "We do not bid with money we do not have. {F} signed on our terms." },
            new[] { "A new champion: {F}. The mountain has heard.", "{F} holds the belt, and Cape Town has a new argument to have." },
            new[] { "{F} is gone. We owe nobody a cent, and we are going to keep it that way." },
            new[] { "{F} retires, and we owe nothing but thanks." },
            new[] { "{Org} outbid us for {F}. We do not bid with money we do not have." }),
        new("Hector Ravelo", "PMP", WorldMode.Modern, AgendaKind.Spectacle,
            "What he wants is entrances that run longer than some of the fights; he has been proved right at the box office and wrong by his accountants.",
            new[] { "{F} signs. The entrance will be longer than the contract. That is the plan.", "{F} will be introduced. At length. With lights." },
            new[] { "A Pampas champion: {F}. The entrance will be a week long.", "{F} has the belt, and the song is already written. I wrote it myself." },
            new[] { "{F} is gone. The accountants will say it is a relief; the crowd will sing anyway." },
            new[] { "{F} retires. The farewell entrance will be longer than the career." },
            new[] { "{Org} outbid us for {F}. Their entrance will be shorter than ours, and that is some consolation." }),
        new("Leonard Pastor", "CRU", WorldMode.Origins, AgendaKind.Spectacle,
            "What he wants is one big night a year that the whole country talks about the next morning; the eight men, the one rule and the pay-per-view audience are all he has.",
            new[] { "{F} may make a good night better, and a good night is the whole business.", "{F} has been invited. I hope there is a hook." },
            new[] { "{F} won the night. I want everybody to see the next one.", "The crowd stood up for {F}, and that is the only review I take." },
            new[] { "{F} has left the Crucible. The next eight will be better." },
            new[] { "{F} retires. The Crucible will remember the night." },
            new[] { "{Org} has taken {F}. A good night does not need any one name." }),
        new("Kosaku Hiyama", "SFA", WorldMode.Origins, AgendaKind.Legacy,
            "What he wants is a ring that is respected: three rounds, judges, a bow before and after, and a sport that Japan can be proud of.",
            new[] { "{F} joins us. I hope the bow is understood to be part of the fight.", "{F} is welcome. The ring is waiting, and so are the judges." },
            new[] { "The ring has a winner, and I hope the winner bowed. {F}.", "{F} has won. I hope the bow was remembered. I ask for nothing more." },
            new[] { "{F} leaves the Association. I hope the bow is remembered." },
            new[] { "{F} retires. The ring will bow once more." },
            new[] { "{Org} has taken {F}. The ring is not diminished." }),
        new("Anibal Sampaio", "DLB", WorldMode.Origins, AgendaKind.Hometown,
            "What he wants is Rio's garage-gym argument settled in his own garage, bare-handed and in public, so that the north zone and the tin roof can stop sending messages by cousin.",
            new[] { "{F} comes to the garage. The garage is a good place to be argued with.", "{F} signs with us, and I hope the family will be pleased." },
            new[] { "The garage has a winner: {F}. The north zone and the tin roof can talk about something else.", "{F} won it in the garage. That is how it is done here." },
            new[] { "{F} leaves the garage. The garage will be here when the thinking is done." },
            new[] { "{F} retires. The garage door will stay open for the farewell." },
            new[] { "{Org} has taken {F}. The garage is not for sale." }),
        new("Ryoji Fukuhara", "SGC", WorldMode.Origins, AgendaKind.Growth,
            "What he wants is a place for a fight in a seat that has been filling and emptying for other sports for eleven years; he sells a seat, not a result.",
            new[] { "{F} may sell a seat, and I know the price of a seat to the yen.", "A name makes the arena look bigger. I hope {F} is one." },
            new[] { "{F} has won. I will put the name on the poster for as long as the poster will hold it.", "A good night in a good seat: {F}." },
            new[] { "{F} has gone elsewhere. There are many seats and many names." },
            new[] { "{F} retires. A seat will be kept empty for the night." },
            new[] { "{Org} outbid us for {F}. I know what a seat costs. I did not know what a name cost." }),
        new("Dale Kessler", "BFC", WorldMode.Origins, AgendaKind.Money,
            "What he wants is a licence, a house on the Strip and a crowd that pays at the door; he runs a casino's entertainment floor and he knows what a night costs.",
            new[] { "{F} signs. The house will treat the name well, and the floor will treat it better.", "A draw is the business. I hope {F} is one." },
            new[] { "A winner on the Strip: {F}. Put the name on the big sign.", "{F} has won. Let us see whether the house fills." },
            new[] { "{F} has gone. The floor stays open." },
            new[] { "{F} retires. The floor will give the name a send-off." },
            new[] { "{Org} outbid us for {F}. We have the licence; they have the one name." }),
        new("Marguerite Beaulieu", "BLF", WorldMode.Origins, AgendaKind.Legacy,
            "What she wants is a ring that is not booked on her fighters' big nights, and a women's division nobody can say was a novelty.",
            new[] { "{F} signs with a house that has always wanted the name. That is a good reason to sign.", "{F} comes to Bellona, and the ring is theirs on the night." },
            new[] { "Bellona has a champion: {F}. The ring was theirs; the night was theirs.", "{F} has won. I have waited a long time to say it and I will say it again." },
            new[] { "{F} goes. The name will be missed, and it will be spoken of." },
            new[] { "{F} retires. The ring will be booked for the farewell, and nobody else will have it." },
            new[] { "{Org} outbid us for {F}. We are used to being outbid. We are not used to being forgotten." }),
    };

    private static readonly Dictionary<string, OwnerAgenda> ByOwner = All.ToDictionary(a => a.Owner, StringComparer.Ordinal);

    /// <summary>The agenda of this designed owner, by name alone (for tables and tests). Prefer <see cref="Of(Promotion, WorldMode)"/> in play.</summary>
    public static OwnerAgenda? Of(string? ownerName) =>
        ownerName is not null && ByOwner.TryGetValue(ownerName, out var a) ? a : null;

    /// <summary>The agenda that belongs to this house in this world: the designed house, its designed owner, the right era.</summary>
    public static OwnerAgenda? Of(Promotion promo, WorldMode mode) =>
        Of(promo.OwnerName) is { } a && a.Mode == mode && a.House == promo.Abbreviation ? a : null;

    /// <summary>The sentence(s) the org page carries, or null for a house with no authored owner.</summary>
    public static string? PageLine(Promotion promo, WorldMode mode) => Of(promo, mode)?.PageLine;
}
