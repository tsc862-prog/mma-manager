using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;

namespace MmaManager.Core.Sim.Contracts;

/// <summary>
/// Works out what a fighter is worth and what they want in their next deal. Pure
/// and deterministic — a function of the fighter's ability, draw, résumé, age and
/// standing plus the world date — so the same fighter always values out the same
/// way (no RNG state to drift a save). The scouted player sees this; the
/// <c>Negotiator</c> measures offers against it.
/// </summary>
public sealed class FighterValuation
{
    public ContractExpectations Evaluate(World world, Fighter f)
    {
        int ability = FighterAbility.CurrentAbility(f);
        int pop = f.Intangibles.Popularity;
        int age = f.AgeOn(world.CurrentDate);
        int momentum = Math.Clamp(f.Momentum, -100, 100);
        bool champion = IsChampion(world, f.Id);
        int toughness = AgentToughness(f);

        // --- Market value (0-100), ability-dominated ------------------------
        double mv = ability
                  + pop * 0.20
                  + RecordBonus(f)
                  + AgeAdjustment(age)
                  + momentum * 0.05
                  + (champion ? 8 : 0);
        int marketValue = Math.Clamp((int)Math.Round(mv), 0, 100);

        // --- Money ----------------------------------------------------------
        // Cheap at the bottom of the market (a small-hall fighter fights for a
        // few grand), steep at the top — stars price small promotions out.
        // The ability money is scaled by the RÉSUMÉ: a fighter gets paid for
        // what they've PROVEN. A rookie two fights into their career fights for
        // exposure money no matter how good the tape looks, and a journeyman on
        // a losing record is the cheapest hire in the sport — this is the
        // affordable talent pool a city-level hall actually staffs from.
        // Popularity money is NOT discounted (fame is its own proof — a famous
        // 0-0 crossover still charges for the name), and it only pays once a
        // fighter is actually a draw.
        decimal purse = 2_500m + Math.Max(0, ability - 25) * 350m + Math.Max(0, ability - 50) * 1_800m;
        purse *= (decimal)ResumeFactor(f);
        purse += Math.Max(0, pop - 30) * 600m;
        purse += Math.Max(0, momentum) * 150m;
        if (champion) purse += 30_000m;
        purse *= 1m + toughness / 100m * 0.25m;   // tough agents push for more
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
        // happened, the ceiling is the story, and the agent prices the hype —
        // an unproven kid, but never a bargain-bin one. The floor rides their
        // scouted ceiling and fades once the career becomes the résumé. (The org
        // that DISCOVERED them still deals at the gratitude rate — see
        // Negotiator.GratitudeFactor — this floor is the open market's price.)
        if (f.Record.TotalBouts <= 4
            && f.Accolades.Any(a => a.Title.StartsWith("Discovered on the local circuit")))
            purse = Math.Max(purse, 5_000m + Math.Max(0, f.PotentialAbility.Value - 55) * 150m);
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

        int desiredFights = (champion || ability >= 78) ? 3 : ability >= 60 ? 4 : 5;
        bool wantsSigningBonus = champion || pop >= 55 || ability >= 78;

        return new ContractExpectations
        {
            MarketValue = marketValue,
            AskingPurse = askingPurse,
            AskingWinBonus = askingWinBonus,
            DesiredFights = desiredFights,
            WantsSigningBonus = wantsSigningBonus,
            Priorities = Priorities(f, ability, pop, age, momentum, toughness),
        };
    }

    // What a fighter WANTS out of a deal is character as much as circumstance: a
    // mercenary is at the table for the cheque, a company man will take less to
    // stay, a warhorse wants the fight that means something.
    private static OfferPriorities Priorities(Fighter f, int ability, int pop, int age, int momentum, int toughness) =>
        new()
        {
            Money = Clamp(55 + toughness / 4 + Jitter(f, 1)
                          + (f.Temperament.Ambition.Value - 50) / 4),
            TitleOpportunity = Clamp(30 + (ability - 50) / 2 + Math.Max(0, momentum) / 4 + Jitter(f, 2)
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
