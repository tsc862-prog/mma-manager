using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Contracts;

/// <summary>
/// Works out what a fighter is worth and what they want in their next deal. Pure
/// and deterministic — a function of the fighter's PUBLIC standing (the stages
/// he has fought on, his results there, belts, rankings, draw, age), his own
/// character and the world date — so the same fighter always values out the
/// same way (no RNG state to drift a save). The scouted player sees this; the
/// <c>Negotiator</c> measures offers against it.
///
/// FOG OF WAR: the ask never reads a fighter's ratings or potential, directly or
/// through a helper. It is built from <see cref="PublicStanding"/>: what the
/// sport has SEEN. A hidden gem asks like his record (the scouting game is
/// finding him before the record shows it); a name living off an old
/// reputation asks like the name. What a promotion is willing to PAY is the
/// buyer's side — its own read (<c>ScoutingService.PerceivedAbility</c>)
/// decides whether it bids, never what he asks.
/// </summary>
public sealed class FighterValuation
{
    public ContractExpectations Evaluate(World world, Fighter f)
    {
        var standing = PublicStanding.Read(world, f);
        double reputation = standing.Reputation;
        int level = (int)Math.Round(reputation);
        int pop = f.Intangibles.Popularity;
        int age = f.AgeOn(world.CurrentDate);
        int momentum = Math.Clamp(f.Momentum, -100, 100);
        bool champion = IsChampion(world, f.Id);
        int toughness = AgentToughness(f);

        // --- Market value (0-100), reputation-dominated ---------------------
        double mv = reputation
                  + pop * 0.20
                  + RecordBonus(f)
                  + AgeAdjustment(age)
                  + momentum * 0.05
                  + (champion ? 8 : 0);
        int marketValue = Math.Clamp((int)Math.Round(mv), 0, 100);

        // --- Money ----------------------------------------------------------
        // Cheap at the bottom of the market (a small-hall fighter fights for a
        // few grand), steep at the top — stars price small promotions out.
        // The reputation money is scaled by the RÉSUMÉ: a fighter gets paid for
        // what they've PROVEN. A rookie two fights into their career fights for
        // exposure money however the kid looks in the gym, and a journeyman on
        // a losing record is the cheapest hire in the sport — this is the
        // affordable talent pool a city-level hall actually staffs from.
        // Popularity money is NOT discounted (fame is its own proof — a famous
        // 0-0 crossover still charges for the name), and it only pays once a
        // fighter is actually a draw.
        decimal purse = 2_500m + (decimal)Math.Max(0, reputation - 25) * 350m
                               + (decimal)Math.Max(0, reputation - 50) * 1_800m;
        purse *= (decimal)ResumeFactor(f);
        purse += Math.Max(0, pop - 30) * 600m;
        purse += Math.Max(0, momentum) * 150m;
        if (champion) purse += 30_000m;
        purse *= 1m + toughness / 100m * 0.25m;   // tough agents push for more
        // What he thinks he's worth: a mercenary or a showman prices above his
        // reputation, a company man below it (a stable per-fighter lean).
        purse *= SelfWorth(f);
        // The FACE demands face money: when the whole brand runs through them,
        // their agent knows exactly what walking out the door would cost you.
        if (Org.Franchise.IsFace(world, f)) purse *= 1.20m;
        // A skidding FREE AGENT prices to move: the market has seen the slide,
        // the phone isn't ringing at the old number, and a smaller stage that
        // pays is better than a bigger one that won't call — this is how a
        // faded name steps DOWN a level instead of sitting unsigned.
        if (f.Contract is null && f.Record.CurrentStreak <= -2)
            purse *= f.Record.CurrentStreak <= -4 ? 0.5m : 0.7m;
        // A DISCOVERED local is the town's hot ticket: everyone saw the night it
        // happened and the agent prices the hype — an unproven kid, but never a
        // bargain-bin one. The floor is the story the town saw (beating a pro
        // is the bigger one), read off the accolade, and fades once the career
        // becomes the résumé. (The org that DISCOVERED them still deals at the
        // gratitude rate — see Negotiator.GratitudeFactor — this floor is the
        // open market's price.)
        if (f.Record.TotalBouts <= 4 && DiscoveryFloor(f) is { } floor)
            purse = Math.Max(purse, floor);
        // A PODIUM carries a name the fog can't hide: an Olympic or world-
        // championship medal is public proof, and the ask keeps it long after
        // the ceremony (the biggest medal counts; they don't stack).
        decimal medal = 0m;
        foreach (var a in f.Accolades)
        {
            if (a.Title.EndsWith("— gold")) medal = Math.Max(medal, 12_000m);
            else if (a.Title.EndsWith("— silver")) medal = Math.Max(medal, 7_000m);
            else if (a.Title.EndsWith("— bronze")) medal = Math.Max(medal, 4_000m);
        }
        purse += medal;
        purse = Math.Max(1_500m, RoundTo(purse, 500m));

        var askingPurse = new Money(purse);
        var askingWinBonus = new Money(RoundTo(purse * 0.75m, 500m));

        int desiredFights = (champion || level >= StarLevel) ? 3 : level >= 58 ? 4 : 5;
        bool wantsSigningBonus = champion || pop >= 55 || level >= StarLevel;

        return new ContractExpectations
        {
            MarketValue = marketValue,
            AskingPurse = askingPurse,
            AskingWinBonus = askingWinBonus,
            DesiredFights = desiredFights,
            WantsSigningBonus = wantsSigningBonus,
            Priorities = Priorities(f, level, pop, age, momentum, toughness),
        };
    }

    /// <summary>
    /// The reputation at which a fighter carries himself like a star (short deals,
    /// a signing bonus). Was true ability 78; reputation runs on a compressed
    /// scale (public facts can't separate the very best), and 74 keeps the same
    /// share of the world in star mode: 4.4% of active fighters read 74+ on day
    /// one against 4.1% who were 78+, and 8.0% against 8.7% after five years
    /// (40 seeds). The four-fight line moved 60 -> 58 for the same reason: 28%
    /// and 53% read 58+ against 33% and 48% who were truly 60+.
    /// </summary>
    public const int StarLevel = 74;

    /// <summary>
    /// The discovered local's open-market floor, from the accolade the town saw:
    /// the kid who upset a pro carries a bigger story than the one who stole
    /// the show. Null when he wasn't discovered on the local circuit.
    /// </summary>
    private static decimal? DiscoveryFloor(Fighter f)
    {
        decimal? floor = null;
        foreach (var a in f.Accolades)
        {
            if (!a.Title.StartsWith("Discovered on the local circuit")) continue;
            decimal v = a.Title.Contains("— upset ") ? 7_000m : 5_500m;
            floor = Math.Max(floor ?? 0m, v);
        }
        return floor;
    }

    /// <summary>
    /// How far above or below his public reputation a fighter prices himself:
    /// his disposition (the mercenary follows the money, the showman believes
    /// his own billing, the company man signs what's put in front of him) plus
    /// a stable per-fighter lean off a hash of his id — never a stream draw.
    /// </summary>
    public static decimal SelfWorth(Fighter f)
    {
        decimal lean = f.Disposition switch
        {
            Disposition.Mercenary => 1.08m,
            Disposition.Showman => 1.06m,
            Disposition.Loudmouth => 1.05m,
            Disposition.Hothead => 1.02m,
            Disposition.Warhorse => 0.97m,
            Disposition.CompanyMan => 0.92m,
            _ => 1.00m,
        };
        return lean * (1m + Jitter(f, 11) / 400m);   // ±5%
    }

    // What a fighter WANTS out of a deal is character as much as circumstance: a
    // mercenary is at the table for the cheque, a company man will take less to
    // stay, a warhorse wants the fight that means something.
    private static OfferPriorities Priorities(Fighter f, int level, int pop, int age, int momentum, int toughness) =>
        new()
        {
            Money = Clamp(55 + toughness / 4 + Jitter(f, 1)
                          + (f.Temperament.Ambition.Value - 50) / 4),
            TitleOpportunity = Clamp(30 + (level - 50) / 2 + Math.Max(0, momentum) / 4 + Jitter(f, 2)
                          + (f.Temperament.Ego.Value - 50) / 4),
            Loyalty = Clamp(35 + (age - 27) * 2 - Math.Max(0, momentum) / 5 + Jitter(f, 3)
                          + (50 - f.Temperament.Ambition.Value) / 3),
            HomeRegion = Clamp(40 + (50 - pop) / 3 + Jitter(f, 4)),
        };

    // Agent toughness rises with fame/charisma, with a stable per-fighter streak
    // — and with how much the fighter thinks they're worth, which is what an
    // agent is really being paid to relay.
    public static int AgentToughness(Fighter f) =>
        Clamp(30 + f.Intangibles.Popularity / 3 + f.Intangibles.Charisma / 5 + Jitter(f, 7)
              + (f.Temperament.Ego.Value - 50) / 4);

    private static double RecordBonus(Fighter f)
    {
        int decisive = f.Record.Wins + f.Record.Losses;
        int total = decisive + f.Record.Draws + f.Record.NoContests;
        if (decisive == 0) return -2;                          // unproven
        double winPct = f.Record.Wins / (double)decisive;
        double bonus = (winPct - 0.5) * 10 + Math.Min(total, 20) * 0.1;
        return Math.Clamp(bonus, -5, 6);
    }

    /// <summary>
    /// How much of their ability money a fighter can actually command: FOG OF WAR
    /// as pricing. The market pays for what it has SEEN — an unproven fighter is
    /// exposure-money cheap (~15%) no matter how good the tape looks or how much
    /// they've grown in the gym, fully proven only by ~12 decisive bouts, and a
    /// losing record drags the price down toward 35% of even that. An 0-1 always
    /// prices below an 0-0 (the one thing the market saw was a loss).
    /// </summary>
    private static double ResumeFactor(Fighter f)
    {
        int decisive = f.Record.Wins + f.Record.Losses;
        double experience = Math.Min(1.0, (decisive + f.Record.Draws * 0.5) / 12.0);
        double winPct = decisive == 0 ? 0.5 : f.Record.Wins / (double)decisive;
        double form = winPct >= 0.5 ? 1.0 : Math.Max(0.35, Math.Pow(winPct / 0.5, 1.2));
        return (0.15 + 0.85 * experience) * form;
    }

    private static double AgeAdjustment(int age) =>
        age < 23 ? -3 :
        age <= 31 ? 0 :
        Math.Max(-14, -(age - 31) * 2);

    private static bool IsChampion(World world, FighterId id) =>
        world.Titles.Values.Any(t => t.CurrentChampion == id);

    // Deterministic per-fighter "personality" jitter in [-20, 20]; salt varies the axis.
    private static int Jitter(Fighter f, int salt)
    {
        uint h = (uint)f.Id.Value * 2654435761u + (uint)salt * 40503u;
        h ^= h >> 15;
        return (int)(h % 41) - 20;
    }

    private static int Clamp(int v) => Math.Clamp(v, 0, 100);

    private static decimal RoundTo(decimal v, decimal step) => Math.Round(v / step) * step;
}
