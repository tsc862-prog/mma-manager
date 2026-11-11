using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Generation;

/// <summary>
/// An authored line of the day-one news feed. <paramref name="DaysBeforeStart"/> is
/// counted back from the world's start date; <paramref name="Org"/> is a promotion
/// abbreviation and <paramref name="About"/> a full "First Last" (roster or legend),
/// both optional. A headline whose person or house is not in the built world is
/// dropped rather than posted without its subject.
/// </summary>
public sealed record DesignedHeadline(
    int DaysBeforeStart, NewsCategory Cat, string? Org, string Headline, string Body, string? About);

/// <summary>
/// The stories the sport was already telling when the player arrived, written by
/// hand — the part of the feed the sim cannot derive. <see cref="Living.Prologue"/>
/// merges these into the same oldest-first list as the derived backlog.
///
/// House rules, enforced by CanonTests: nothing here may state a count, a date, a
/// duration, a price or a ranking the data cannot guarantee on every seed, and
/// nothing may promise a fixture, a contract stage or a home city that no table
/// holds. People speak for themselves ("says this is the last year"); the feed
/// never asserts what the sim might still change. Every line is spin, ambition or
/// attitude, and each is true on day one whether or not a later table lands.
/// Append-only, like every other designed table.
/// </summary>
public static class DesignedHeadlines
{
    public static IReadOnlyList<DesignedHeadline> All { get; } = new DesignedHeadline[]
    {
        // --- the AFC lightweight picture -----------------------------------------
        new(21, NewsCategory.Signing, "GFC",
            "Sheikh Faisal: 'We would build a stadium around Dane Mercer'",
            "The GFC's owner says the offer is ready the day the AFC lightweight champion's deal runs out, and that the architects have already been told.",
            "Dane Mercer"),
        new(9, NewsCategory.Rivalry, "AFC",
            "Seo: 'I have watched that decision four hundred times'",
            "“I'd like five rounds and three new judges.” — Jae-won Seo, on the lightweight title fight he has not stopped re-scoring.",
            "Jae-won Seo"),

        // --- the women's bantamweight picture -------------------------------------
        new(40, NewsCategory.Rivalry, "AFC",
            "Cross on Volkova: 'Nobody has asked her a hard question yet'",
            "The woman who argued the division into existence says its champion has not been tested, and that she knows who should do it.",
            "Sarah Donovan"),
        new(16, NewsCategory.General, null,
            "Roxanne Vance: still unsigned, still unbeaten",
            "The AFC calls her an internet clip. The people she has fought have a different word for it.",
            "Roxanne Vance"),

        // --- Ironhold's heir -------------------------------------------------------
        new(12, NewsCategory.Signing, "VMMA",
            "Vanguard reported to have sounded out Baranov",
            "Baron Reuss's people are said to have offered Volkov's heir apparent the money Ironhold won't. The hard school calls it an insult; Baranov has not been heard refusing.",
            "Kirill Baranov"),

        // --- Kansai Cage ------------------------------------------------------------
        new(30, NewsCategory.Finance, "ICF",
            "Kanemura bids for Kansai Cage's women's division",
            "Ayumi Sekine's answer, in full: “It is not a division. It is a family.”",
            null),
        new(6, NewsCategory.Milestone, "KAC",
            "Nakamura: 'This is the last year. I've told the curtain.'",
            "Twenty-six pro wins, most of them in Kansai Cage colours, and the Craftsman says this is the last year of it.",
            "Haruki Nakamura"),

        // --- the temple-fair circuit -------------------------------------------------
        new(15, NewsCategory.General, "MFC",
            "Chaiya wants his cousin home for one night",
            "“Somchai fights for Iron Circle. Once, just once, he should fight for the river.” — Somchai Chaiya, on his cousin Somchai Rattanak, and on the confusion of sharing a first name.",
            "Somchai Chaiya"),
        new(27, NewsCategory.Signing, "ICF",
            "The ICF has made an offer for Mekong's golden boy",
            "Undefeated and twenty-one, Arthit Saengkham is the best thing on the temple-fair circuit. Nok Wattana says the price is the price.",
            "Arthit Saengkham"),

        // --- the Brazilian estate war -------------------------------------------------
        new(18, NewsCategory.Finance, "RCS",
            "RCS purses late again",
            "“Late is not never.” — Tiago Moreira",
            null),
        new(50, NewsCategory.Rivalry, "CK",
            "Dona Marisa on Rio: 'Children with a truck'",
            "The Cage Kings matriarch was asked about RCS and its sell-outs. She asked who had paid for the tickets.",
            null),

        // --- the ghost of Cage Kings ------------------------------------------------------
        new(33, NewsCategory.Signing, "PMP",
            "Ravelo wants El Fantasma",
            "“Bring the hat, bring the ghost, bring the grudge. Buenos Aires will bring the noise.” — Hector Ravelo",
            "Diego Rivas"),

        // --- peanuts by choice ----------------------------------------------------------------
        new(10, NewsCategory.Signing, "AFC",
            "Okonjo to Okafor: 'Come and get paid, brother'",
            "The AFC welterweight champion says Africa's biggest star is working for the wrong pay. Amara Nwosu's reply: “He is working for the right reasons. That is what scares you.”",
            "Chidi Okafor"),

        // --- the Rust Belt unbeatens ---------------------------------------------------------------
        new(13, NewsCategory.Rivalry, "CC",
            "Feldstein: 'Pfeiffer has a good kid. I have the kid.'",
            "Duane Pfeiffer's reply came by fax, which Coastal Combat says it no longer owns a machine to read.",
            "Tyshawn Cole"),
        new(44, NewsCategory.Milestone, "GLF",
            "Wyatt's buckle: still in Iowa",
            "Halloran is in his second decade of small-hall main events and the belt is still his. The Midwest's prospects keep coming; the buckle keeps staying.",
            "Wyatt Halloran"),

        // --- Bisset's pick -----------------------------------------------------------------------------
        new(35, NewsCategory.General, "VMMA",
            "Bisset: 'The flyweight everyone will be talking about in three years is in Dakar'",
            "Vanguard's former champion said it on French television, then said it again, slower. Nobody has signed her yet.",
            "Fatima Diallo"),

        // --- the gulf's one homegrown star ------------------------------------------------------------------
        new(25, NewsCategory.Milestone, "GFC",
            "Sheikh Faisal on Al-Suwaidi: 'The one star my money didn't buy'",
            "Eight straight wins for the Falcon of Dubai, the one name on a roster of hired guns who fights for the badge. The rest, like Dudaev, have changed banners before and mourned none of them.",
            "Khalid Al-Suwaidi"),

        // --- one more chapter ---------------------------------------------------------------------------------------
        new(26, NewsCategory.General, null,
            "Tanner: 'One more run, then the farm'",
            "The dethroned AFC king, two surgeries past the reign, says this is the last year. His surgeon has been quoted agreeing.",
            "Brock Tanner"),
        new(19, NewsCategory.General, null,
            "Hattori wants a Tokyo goodbye",
            "The Blade has said publicly that this is his last year, and that he would like it to end in Tokyo.",
            "Kazuki Hattori"),
        new(11, NewsCategory.General, null,
            "The Test sets an exam date",
            "Oskar Lindqvist says this is his last year, which means a few more people are about to find out whether they are somebody.",
            "Oskar Lindqvist"),
        new(7, NewsCategory.General, null,
            "Second Bell is answering the phone",
            "Nate Corrigan has stopped saying he is done. His agent has stopped saying 'no comment'.",
            "Nate Corrigan"),
    };
}
