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
/// name, {Org} the house that did the outbidding. Keyed by the owner's full name (the same name the promotion
/// carries). Append-only.
/// </summary>
public sealed record OwnerAgenda(
    string Owner, AgendaKind Wants, string PageLine,
    string[] Signs, string[] Crowns, string[] LosesStar, string[] Outbid);

public static class OwnerAgendas
{
    private static OwnerAgenda G(string owner, AgendaKind k, string page, string[] signs, string[] crowns, string[] loses, string[] outbid) =>
        new(owner, k, page, signs, crowns, loses, outbid);

    public static IReadOnlyList<OwnerAgenda> All { get; } = new OwnerAgenda[]
    {
        // ============ Today ============
        G("Katherine Ross", AgendaKind.Growth,
            "What she wants is growth, quarter on quarter, and the proof that the company was always hers to run better than her father did.",
            new[] { "{F} is the kind of growth I like to sign in daylight.", "I signed {F} because the numbers will look better with {L} in them than without." },
            new[] { "A champion is the company's best argument, and {F} has just made it again.", "I do not give speeches. {F} has given a better one." },
            new[] { "{F} leaves a hole, and I intend to fill it with growth, not sentiment.", "Everyone leaves the summit eventually, {F} included. Not everyone leaves it in good order." },
            new[] { "{Org} paid a price for {F} that I would not have paid in my father's time, and I did not pay it in mine." }),
        G("Baron Aleksander Reuss", AgendaKind.Prestige,
            "What he wants is to be seen to be first: the best television, the best building, the name that the continent says before the others.",
            new[] { "{F} will look very well under our lights, and a man should be seen under good lights.", "Vanguard has acquired {F}, which is what the continent will say and the others will repeat." },
            new[] { "A Vanguard champion is photographed differently. {F} understands that already.", "The continent has a champion again, and it is {F}." },
            new[] { "{F} has left Vanguard. It will be remembered as a loan." },
            new[] { "{Org} has bought {F}. I am told it was a great deal of money, which is the only impressive thing about it." }),
        G("Pavel Ilic", AgendaKind.BuryRival,
            "What he wants is hard fights, full houses and, quietly, the Baron's name on a bankruptcy notice; he has not forgotten the lease or who did not renew it.",
            new[] { "{F} can fight, and that is the whole of the interview.", "{F} signs because we are putting the hardest fights in the sport on, and everybody knows who else is not." },
            new[] { "{F} is champion. Somebody tell the people with the velvet ropes.", "I tape hands on fight night. {F} gave me a reason to tape them tighter." },
            new[] { "{F} goes. Fine. The car park was always where we built things." },
            new[] { "{Org} outbid us for {F}. I remember the date, as I do all the dates." }),
        G("Hideo Kanemura", AgendaKind.Legacy,
            "What he wants is a year in which the belts meant more in December than they did in January; everything else is a rounding error to him.",
            new[] { "{F} joins a lineage. I hope {L} knows how long it is.", "The belt is older than I am. {F} is welcome to try to be worthy of it." },
            new[] { "A belt on the right shoulder is the whole of my business. {F} is wearing it well.", "The lineage continues. {F} has added a line, and the line is good." },
            new[] { "{F} leaves, and the lineage does not. That is what lineages are for." },
            new[] { "{Org} has taken {F}. The belt is still here, and so are we." }),
        G("Sheikh Faisal bin Rashid", AgendaKind.Spectacle,
            "What he wants is names, stadiums and headlines, this quarter; being embarrassed is the one cost he will not pay.",
            new[] { "I did not sign {F} for the money, which I have. I signed {F} for the headline, which I do not.", "{F} will fill a stadium. If not, I will fill it with something else." },
            new[] { "A Gulf champion. Print the name large: {F}.", "{F} is a statement, and I make statements for a living." },
            new[] { "{F} has left. Every house is entitled to a bad week; I am entitled to a short one." },
            new[] { "{Org} outbid me for {F}. It will not be repeated." }),
        G("Bill Hennessy", AgendaKind.Money,
            "What he wants is a slot he can sell, with names in it that sell it; ratings are his unit of account and loyalty is a line item.",
            new[] { "{F} fills the slot. That is the entire review.", "I like {F}. I like ratings. They are the same sentence." },
            new[] { "A champion is a promotional asset, and {F} is a very good one.", "{F} on the belt is a ratings event. I will see you all on Saturday." },
            new[] { "{F} is gone, the slot is not. Somebody call the next one." },
            new[] { "{Org} paid {F} a rate I would not put on a telecast. I wish them good luck with the invoice." }),
        G("Tiago Moreira", AgendaKind.Hometown,
            "What he wants is a sell-out in Rio and a roster the favelas believe in; the rest, including the truck, can be argued about later.",
            new[] { "{F} signs and Rio sings. It is that simple, and I will pay him on time, I promise.", "I would sell my car for {F}. I have said this about a lot of fighters; this time I mean it." },
            new[] { "Rio has a champion. {F}, take the night; the rest of us will settle up in the morning.", "The truck is parked. The belt is on {F}'s shoulder. Nothing else matters." },
            new[] { "{F} is gone, and I am sorry, truly. Please do not ask me for numbers." },
            new[] { "{Org} outbid us for {F}. They did not have the truck, and that, in the end, is what we had." }),
        G("Denise Cartwright", AgendaKind.Money,
            "What she wants is a full building at the lowest hourly rate; she knows what a seat costs and what a name adds to it.",
            new[] { "{F} will fill the building we have booked. I like a man who understands a room.", "Signing {F} is a good use of an hour of arena time." },
            new[] { "{F} on the belt, a sold-out building and a clean ledger. A good night.", "A champion who fills the building, as {F} does, is a champion I can afford." },
            new[] { "{F} has gone. The building will not, and I will fill it." },
            new[] { "{Org} paid more for {F} than the building can recoup. I wish them every success with that." }),
        G("Dona Marisa Bittencourt", AgendaKind.BuryRival,
            "What she wants is to see the Rio upstart taught some manners, and the family estate handed on whole; she is patient about the first and stubborn about the second.",
            new[] { "{F} comes to an old house and is welcome in it. The old house has manners.", "We sign {F} the way my husband signed his great ones: with a handshake and a long memory." },
            new[] { "The family has a champion again: {F}. The children at the other end of town may take notes.", "{F} on the belt. My husband would have been quiet and pleased, and so am I." },
            new[] { "{F} goes where the money calls. The house is still here, and so am I." },
            new[] { "{Org} took {F}. The estate has outlasted worse." }),
        G("Marty Feldstein", AgendaKind.Sell,
            "What he wants is a good price for talent that outgrows him: he builds them in the small houses, sells them upward, and has made his peace with the arithmetic.",
            new[] { "{F} has the kind of future I can sell somebody. It just has to happen here first.", "I know every matchmaker's number and I called all of them about {F}. We signed." },
            new[] { "{F} is a champion at Coastal Combat. Somebody, somewhere, is looking at the phone number.", "The belt is the best brochure I have ever printed, and {F} is wearing it." },
            new[] { "{F} goes upward. That was always the plan, and I am invoicing on time." },
            new[] { "{Org} paid for {F}. I would have preferred they paid more, and I have told them so." }),
        G("Amara Nwosu", AgendaKind.Hometown,
            "What she wants is a continent's own champions on a continent's own stage; she would rather fold than export her best fighter cheaply.",
            new[] { "{F} stays on the continent. That is what the signature is for.", "A fighter like {F} is the best argument I have for what we are building." },
            new[] { "An African champion, on African soil: {F}. Nobody can say we are not building this.", "{F} has the belt. The next question is who gets to take it from the continent, and the answer is nobody." },
            new[] { "{F} leaves. I wish it were for a reason I could be proud of; I will find one." },
            new[] { "{Org} outbid me for {F}. I would rather have folded than matched it, and I did not match it." }),
        G("Duane Pfeiffer", AgendaKind.Craft,
            "What he wants is wrestling first and opinions second: three generations of a family that has promoted something in that gymnasium since the war.",
            new[] { "{F} can wrestle, and the rest we will worry about on Monday.", "The gymnasium has room for {F}. It always has had room for a good wrestler." },
            new[] { "{F} on the belt, in the gymnasium, on folding chairs. This is what we are for.", "A wrestler on top, and it is {F}: good. My father would have said the same, and he said it better." },
            new[] { "{F} leaves for a bigger stage. They all do. The gymnasium will have the next one." },
            new[] { "{Org} took {F} to a bigger gymnasium. Ours is still warm." }),
        G("Ayumi Sekine", AgendaKind.Craft,
            "What she wants is the best pound-for-pound fighting on earth in a small room, and the women's division kept honest; she was a fighter and a matchmaker first and has not stopped being either.",
            new[] { "{F} fights the way the house is meant to fight. That is the review.", "I have been a fighter and a matchmaker, and I look at {F} and see a fight I would book." },
            new[] { "Kansai Cage has a champion in {F}. The faithful have always said the best fights are in rooms this size.", "{F} on the belt. I know what the work costs. She knows too." },
            new[] { "{F} has gone to a bigger room. I wish her well, and I will keep her gloves on the shelf." },
            new[] { "{Org} outbid us for {F}. I know what the offer was, and I understand it." }),
        G("Ernesto Chambi", AgendaKind.Hometown,
            "What he wants is for the altitude to keep earning its reputation; small budget, long memory, and a stubborn belief that the mountains should send somebody to the world.",
            new[] { "{F} breathes where others wheeze. I like a man who can do the arithmetic of lungs.", "{F} signed at altitude. I am delighted, and I will drive him to the cards myself." },
            new[] { "A Cordillera champion. The mountains have spoken, and the name is {F}.", "{F} holds the belt, and the altitude held {F}'s opponent. The mountains do their share." },
            new[] { "{F} goes down the mountain. I understand. The coffee is better up here regardless." },
            new[] { "{Org} outbid us for {F}. A small house learns to count lungs, not cheques." }),
        G("Sal Rizzo", AgendaKind.Money,
            "What he wants is knockouts: it is a promotion that sits on top of two body shops, and he says the shops make more but the knockouts are more fun.",
            new[] { "{F} hits hard? Good. Footwork does not sell tickets.", "I signed {F} because the last guy who looked at him ended up flat on his back." },
            new[] { "A Motor City champion. {F}, you earned it. The paint is on me.", "{F} on top, a knockout in the book, and nobody asking about footwork. I am a happy man." },
            new[] { "{F} is gone. Somebody else's problem now; somebody else's car, too." },
            new[] { "{Org} outbid us for {F}. I fix cars. I do not bid on them." }),
        G("Rihards Kalnins", AgendaKind.Craft,
            "What he wants is to announce the shows himself and have them done properly; he has never taken a salary and is offended by the suggestion.",
            new[] { "{F} joins us, and I will announce the name properly. Nobody else does.", "Signing {F} is an honour for a small house with a very loud announcer." },
            new[] { "Ladies and gentlemen, the champion: {F}. I have been waiting years to say that and mean it.", "{F} has the belt. I shall announce it again at the next show, with the same pleasure." },
            new[] { "{F} leaves. I wish him well and I shall announce his next fight with the same enthusiasm." },
            new[] { "{Org} has taken {F}. They did not announce him as well as I would have." }),
        G("Nok Wattana", AgendaKind.Hometown,
            "What she wants is the temple-fair circuit kept alive and every scout in Asia calling her first; she charges them all the same.",
            new[] { "{F} signs here, and every scout in Asia will have my number by lunch.", "A temple-fair fighter like {F} is a good sign. I will book the card from memory." },
            new[] { "The temple-fair circuit has a champion: {F}. Every scout in Asia has been told.", "{F} wears the belt. I have the date for the next festival." },
            new[] { "{F} goes to a bigger stage. A scout will call me about the next one by Monday." },
            new[] { "{Org} outbid us for {F}. A small house sells its best and keeps its memory." }),
        G("Bongani Zulu", AgendaKind.Hometown,
            "What he wants is for the neighbourhood's arguments to sell out, and for the house never to owe anyone a cent; he is fiercely local and allergic to debt.",
            new[] { "{F} is one of ours, and the neighbourhood will be loud about it.", "We signed {F} for what we can afford, and what we can afford is plenty for a man who fights for his street." },
            new[] { "A champion from the townships: {F}. The mountain has heard.", "{F} holds the belt, and Cape Town has a new argument to have." },
            new[] { "{F} goes. We owe nobody a cent, and we are going to keep it that way." },
            new[] { "{Org} outbid us for {F}. We do not bid with money we do not have." }),
        G("Hector Ravelo", AgendaKind.Spectacle,
            "What he wants is entrances that run longer than some of the fights; he has been proved right at the box office and wrong by his accountants.",
            new[] { "{F} signs. The entrance will be longer than the contract. That is the plan.", "{F} will be introduced. At length. With lights." },
            new[] { "A Pampas champion: {F}. The entrance will be a week long.", "{F} has the belt, and the song is already written. I wrote it myself." },
            new[] { "{F} leaves. The accountants will say it is a relief; the crowd will sing anyway." },
            new[] { "{Org} outbid us for {F}. Their entrance will be shorter than ours, and that is some consolation." }),

        // ============ Origins ============
        G("Leonard Pastor", AgendaKind.Spectacle,
            "What he wants is one big night a year that the whole country talks about the next morning; the eight men, the one rule and the pay-per-view audience are all he has.",
            new[] { "{F} will make a good night better, and a good night is the whole business.", "{F} has a hook, and the hook is what the promoter in me sees." },
            new[] { "{F} won the night. I want everybody to see the next one.", "The crowd stood up for {F}, and that is the only review I take." },
            new[] { "{F} has left the Crucible. The next eight will be better." },
            new[] { "{Org} has taken {F}. A good night does not need any one man." }),
        G("Kosaku Hiyama", AgendaKind.Legacy,
            "What he wants is a ring that is respected: three rounds, judges, a bow before and after, and a sport that Japan can be proud of.",
            new[] { "{F} joins us. I hope he understands that the bow is part of the fight.", "{F} is welcome. The ring is waiting, and so are the judges." },
            new[] { "The ring has a champion, and the champion has bowed. That is a good night: {F}.", "{F} has won, and conducted himself well. I ask for nothing more." },
            new[] { "{F} leaves the Association. I hope he remembers how to bow." },
            new[] { "{Org} has taken {F}. The ring is not diminished by his absence." }),
        G("Anibal Sampaio", AgendaKind.Hometown,
            "What he wants is Rio's garage-gym argument settled in his own garage, bare-handed and in public, so that the north zone and the tin roof can stop sending messages by cousin.",
            new[] { "{F} comes to the garage. The garage is a good place to be argued with.", "{F} signs with us, and the family will be pleased." },
            new[] { "The garage has a winner: {F}. The north zone and the tin roof can talk about something else.", "{F} won it bare-handed. That is how it is done here." },
            new[] { "{F} leaves the garage. The garage will be here when he thinks about it." },
            new[] { "{Org} has taken {F}. The garage is not for sale." }),
        G("Ryoji Fukuhara", AgendaKind.Growth,
            "What he wants is a place for a fight in a seat that has been filling and emptying for other sports for eleven years; he sells a seat, not a result.",
            new[] { "{F} will sell a seat, and I know the price of a seat to the yen.", "A fighter like {F} makes the arena look bigger. That is useful." },
            new[] { "{F} has won. I will put him on the poster for as long as the poster will hold him.", "A good night in a good seat: {F}." },
            new[] { "{F} has gone elsewhere. There are many seats and many men." },
            new[] { "{Org} outbid us for {F}. I know what a seat costs. I did not know what a man cost." }),
        G("Dale Kessler", AgendaKind.Money,
            "What he wants is a licence, a house on the Strip and a crowd that pays at the door; he runs a casino's entertainment floor and he knows what a night costs.",
            new[] { "{F} signs. The house will treat him well, and the floor will treat him better.", "{F} is a draw. A draw is the business." },
            new[] { "A winner on the Strip: {F}. Put him on the big sign.", "{F} has won, and the house is full. I could ask for nothing more." },
            new[] { "{F} has gone. The floor stays open." },
            new[] { "{Org} outbid us for {F}. We have the licence; they have the one man." }),
        G("Marguerite Beaulieu", AgendaKind.Legacy,
            "What she wants is a ring that is not booked on her fighters' big nights, and a women's division nobody can say was a novelty.",
            new[] { "{F} signs with a house that has always wanted her. That is a good reason to sign.", "{F} comes to Bellona, and the ring is hers on the night." },
            new[] { "Bellona has a champion: {F}. The ring was hers; the night was hers.", "{F} has won. I have waited a long time to say it and I will say it again." },
            new[] { "{F} goes. She will be missed, and she will be spoken of." },
            new[] { "{Org} outbid us for {F}. We are used to being outbid. We are not used to being forgotten." }),
    };

    private static readonly Dictionary<string, OwnerAgenda> ByOwner = All.ToDictionary(a => a.Owner, StringComparer.Ordinal);

    public static OwnerAgenda? Of(string? ownerName) =>
        ownerName is not null && ByOwner.TryGetValue(ownerName, out var a) ? a : null;

    /// <summary>The sentence(s) the org page carries, or null for a house with no authored owner.</summary>
    public static string? PageLine(string? ownerName) => Of(ownerName)?.PageLine;
}
