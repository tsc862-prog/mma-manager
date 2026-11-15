using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Org;

namespace MmaManager.Core.Sim.Generation.Origins;

/// <summary>How a promotion treats the hands.</summary>
public enum GloveRule
{
    /// <summary>Bare hands; the fighter's own tape.</summary>
    Bare,
    /// <summary>Each fighter chooses: bare, or a light glove with the fingers free.</summary>
    Optional,
    /// <summary>Thin open-fingered gloves for everyone.</summary>
    ThinOpenFinger,
}

/// <summary>
/// A founding promotion's house rules, in data-friendly fields so the later rule-set work can read
/// them without parsing prose. Nothing in the sim reads this yet; it is authored ahead of the wiring.
/// </summary>
/// <param name="Format">One line: what a night here is.</param>
/// <param name="Rounds">Rounds per bout (1 = a single continuous round).</param>
/// <param name="RoundMinutes">Length of each round, in minutes (the whole limit when Rounds is 1).</param>
/// <param name="TimeLimitMinutes">Hard time limit for a bout in minutes; null = none, the referee decides.</param>
/// <param name="Judges">True when judges score a bout that reaches the limit; false = a draw.</param>
/// <param name="DrawAtLimit">True when reaching the limit with no finish is simply a draw.</param>
/// <param name="Gloves">The hand rule.</param>
/// <param name="TournamentSize">Fighters in a one-night bracket; 0 = single bouts.</param>
/// <param name="OpenWeight">True when no weight classes exist and any man may face any man.</param>
/// <param name="RopeEscapesAllowed">Rope or fence grabs allowed per bout before a penalty; 0 = the idea does not exist here.</param>
/// <param name="Banned">What is not allowed, in plain words (short, sporting; the house list is deliberately thin).</param>
/// <param name="WaysToWin">How a bout is ended.</param>
public sealed record OriginsRuleSet(
    string Format, int Rounds, int RoundMinutes, int? TimeLimitMinutes, bool Judges, bool DrawAtLimit,
    GloveRule Gloves, int TournamentSize, bool OpenWeight, int RopeEscapesAllowed,
    string[] Banned, string[] WaysToWin);

/// <summary>The person whose idea it was.</summary>
public sealed record OriginsFounder(string Name, int Age, string Bio);

/// <summary>
/// A day-one promotion of the Origins timeline (1993). Carries everything
/// <see cref="WorldBuilder.PromotionSpec"/> carries, plus the founder, the first venue and the house
/// rules, which the existing spec has no room for. <see cref="ToPromotionSpec"/> is the bridge for the
/// builder; nothing calls it yet.
/// </summary>
public sealed record OriginsPromotionSpec(
    string Name, string Abbreviation, Region Region, string Country, string HomeCity,
    int Prestige, int RosterSize, Money Balance, int FoundedYear, int FirstShowYear,
    OriginsFounder Founder, string HomeVenue, string HomeVenueLore,
    OriginsRuleSet Rules, string Lore, string Niche)
{
    /// <summary>The existing builder shape. Open weight has no WeightClass list, so Divisions stays null.</summary>
    public WorldBuilder.PromotionSpec ToPromotionSpec() =>
        new(Name, Abbreviation, Prestige, RosterSize, Balance, Region, Divisions: null, Women: false,
            Lore: Lore, HomeCity: HomeCity);

    /// <summary>The owner row the staff layer reads (same shape as <see cref="DesignedStaff.Owners"/>).</summary>
    public DesignedOwner ToOwner() => new(Abbreviation, Founder.Name, FoundedYear, Founder.Bio);
}

/// <summary>
/// The three founding promotions of 1993, one per continent, each a different idea of what this new
/// thing is. All invented: no real promotion, broadcaster or founder stands behind any of them.
/// Append-only. Row order will feed ids once these are wired in.
/// </summary>
public static class OriginsPromotions
{
    public static IReadOnlyList<OriginsPromotionSpec> All { get; } = new OriginsPromotionSpec[]
    {
        // (a) North America: the spectacle, sold on pay-per-view.
        new("Crucible Challenge", "CRU", Region.NorthAmerica, "USA", "Los Angeles",
            Prestige: 8, RosterSize: 10, Balance: new Money(650_000m), FoundedYear: 1992, FirstShowYear: 1993,
            Founder: new OriginsFounder("Leonard Pastor", 46,
                "Len Pastor spent two decades packaging late-night cable: kung-fu double bills, celebrity bowling, a wrestling hour that nobody admitted watching. He had never been in a fight and had never promoted one until a gym-owning friend told him over dinner that nobody actually knew who would win if a boxer met a wrestler. Pastor went home, wrote 'WHO WINS?' on a legal pad, and spent the next year selling the idea to a pay-per-view carrier that is not yet sure it believes him."),
            HomeVenue: "Pico Sports Pavilion",
            HomeVenueLore: "A converted trade-show hall off the freeway with a rented grandstand and a ring of waist-high wire fence on a raised canvas. It holds four thousand on the night and smells of fresh paint for a week afterwards.",
            Rules: new OriginsRuleSet(
                Format: "One night, eight men, three fights each for the finalists; no weight classes",
                Rounds: 1, RoundMinutes: 20, TimeLimitMinutes: 20, Judges: false, DrawAtLimit: true,
                Gloves: GloveRule.Optional, TournamentSize: 8, OpenWeight: true, RopeEscapesAllowed: 0,
                Banned: new[] { "biting", "eye gouging", "strikes to the groin" },
                WaysToWin: new[] { "knockout", "submission", "corner stoppage", "referee stoppage" }),
            Lore: "Crucible Challenge sells one question to the cable box: who wins? Eight men from eight disciplines, a bracket drawn on a cocktail napkin, one long round per fight and a draw that eliminates both men. It is a television show before it is a sport, produced by people who have never watched a real fight and know exactly where to point the cameras. The crowd comes for the spectacle; the tape sells in martial-arts magazine ads for years afterwards. Nobody involved is calling it a sport yet.",
            Niche: "The one-night open-weight spectacle: style against style, sold on pay-per-view and on tape."),

        // (b) Japan: grown out of the pro-wrestling halls.
        new("Shinken Fighting Association", "SFA", Region.Asia, "JPN", "Tokyo",
            Prestige: 8, RosterSize: 10, Balance: new Money(520_000m), FoundedYear: 1992, FirstShowYear: 1993,
            Founder: new OriginsFounder("Kosaku Hiyama", 54,
                "Hiyama spent thirty years in the back offices of professional wrestling, where he learned how to fill a hall and, more quietly, which of the men in the ring could really fight. In 1991 he fell out with his bosses over whether the audience could be trusted with the truth. The Shinken Fighting Association is his answer: the same ring, the same ropes, the same bowing crowd, and nothing agreed in advance."),
            HomeVenue: "Eitoku Hall",
            HomeVenueLore: "A 2,800-seat municipal hall in Tokyo that hosted boxing, concerts and a week of flower-arranging every spring until the Association took its Friday nights. Spectators sit in silence through the exchanges and applaud the way people applaud in a temple.",
            Rules: new OriginsRuleSet(
                Format: "Single bouts in a roped ring; main events carry more rounds than the undercard",
                Rounds: 3, RoundMinutes: 5, TimeLimitMinutes: 15, Judges: true, DrawAtLimit: false,
                Gloves: GloveRule.ThinOpenFinger, TournamentSize: 0, OpenWeight: true, RopeEscapesAllowed: 3,
                Banned: new[] { "headbutts", "strikes to the back of the head", "kicks to a grounded man's head", "elbows", "closed-fist strikes to the head of a grounded man" },
                WaysToWin: new[] { "knockout", "submission", "referee stoppage", "judges' decision", "rope-escape points" }),
            Lore: "The Shinken Fighting Association grew in the shadow of the professional-wrestling halls and took their furniture: a roped ring, a bell, a referee in a bow tie. What it removed was the script. A man may grab the rope to escape a hold, but only three times a bout; the fourth costs him points. Fighters bow to the ring, to each other and to the crowd, and the crowd, famously, bows back. Eitoku Hall is quieter than any arena in the world until a hold is locked, and then it is louder.",
            Niche: "The respectful ring: rope-break rules, bows, judges, and fighters who came out of wrestling halls."),

        // (c) Brazil: gym rivalries, tiny rooms, huge local passion.
        new("Duelo Livre", "DLB", Region.SouthAmerica, "BRA", "Rio de Janeiro",
            Prestige: 5, RosterSize: 11, Balance: new Money(90_000m), FoundedYear: 1991, FirstShowYear: 1992,
            Founder: new OriginsFounder("Anibal Sampaio", 58,
                "Sampaio is a retired sports lawyer who has been arguing with Rio's gym owners about whose style is best since before most of his fighters were born. In 1991 he got tired of the arguing and offered to hold the argument in a rented parking garage with a referee, a doctor and a cash box. Nobody has been able to stop the Saturday nights since, and the police have given up trying to find the address in advance."),
            HomeVenue: "Garagem Tijuca",
            HomeVenueLore: "The lower level of a Tijuca parking garage, swept every Saturday afternoon and lit by the headlights of a dozen parked cars. It holds six hundred standing and twice that number on the ramps, and when the roll-down door goes up the sound reaches the next district.",
            Rules: new OriginsRuleSet(
                Format: "Challenge matches between gyms, four to six a night, no weight limits",
                Rounds: 1, RoundMinutes: 30, TimeLimitMinutes: 30, Judges: false, DrawAtLimit: true,
                Gloves: GloveRule.Bare, TournamentSize: 0, OpenWeight: true, RopeEscapesAllowed: 0,
                Banned: new[] { "biting", "eye gouging" },
                WaysToWin: new[] { "submission", "knockout", "corner towel", "doctor's stoppage" }),
            Lore: "Duelo Livre is a gym argument with a cash box. A challenge is issued between two academies, accepted in front of witnesses, and settled on a Saturday night in a Tijuca parking garage: one round, no weight limits, no judges, and almost nothing forbidden. The rooms are tiny and the passion is out of proportion to every figure in the ledger. Fighters carry their academies' reputations into the ring, and the loser's gym hears about it for a year.",
            Niche: "The gym duel: challenge matches in tiny rooms with enormous local passion."),
    };

    /// <summary>The founders as owner rows, in the existing staff shape.</summary>
    public static IReadOnlyList<DesignedOwner> Owners { get; } = All.Select(p => p.ToOwner()).ToArray();
}
