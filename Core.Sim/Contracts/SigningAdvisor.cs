using MmaManager.Core.Model;
using MmaManager.Core.Sim.Scouting;

namespace MmaManager.Core.Sim.Contracts;

/// <summary>
/// The scout's pick for the promotion's next signing: weighs where the roster
/// is too thin to book, what the bankroll can actually afford, and the best
/// SCOUTED ability (plus the runway youth gives) on the free-agent market.
/// Purely advisory — the player still negotiates the deal themselves.
///
/// Fog of war: the scout only knows what the promotion knows. Ability is read
/// through <see cref="ScoutingService.PerceivedAbility(World, Promotion, Fighter)"/>
/// (the same "Scouted" number every screen shows), upside comes only from public
/// facts (age, record, current run) and never from true potential, and the ask is
/// the one this promotion will actually be quoted at its own terms
/// (<see cref="Negotiator.EffectiveAskingPurse"/>: a small hall's non-exclusive
/// rate, the discoverer's gratitude discount), the same figure the market row shows.
/// </summary>
public static class SigningAdvisor
{
    /// <summary>A division needs roughly this many fighters before it books cleanly.</summary>
    private const int DivisionTarget = 6;

    /// <summary>Age up to which a fighter is read as still having years to develop.</summary>
    private const int YouthAge = 23;

    /// <param name="Fighter">The recommended free agent.</param>
    /// <param name="Expectations">The fighter's full expectations (the exclusive-rate ask).</param>
    /// <param name="Ask">The per-fight purse this promotion will actually be asked, at its own terms.</param>
    /// <param name="Scouted">The promotion's scouted read of the fighter's overall.</param>
    /// <param name="Reason">One line explaining the pick.</param>
    public sealed record Suggestion(Fighter Fighter, ContractExpectations Expectations, Money Ask, int Scouted, string Reason);

    private readonly record struct Candidate(Fighter Fighter, ContractExpectations Expectations, Money Ask, int Scouted);

    public static Suggestion? Suggest(World world, Promotion promo) =>
        Suggest(world, promo, f => ScoutingService.PerceivedAbility(world, promo, f));

    /// <summary>
    /// The pick under a given read of each fighter's ability. The public overload
    /// always passes the promotion's scouted read; tests pass others to show the
    /// ranking follows the read and not the truth.
    /// </summary>
    internal static Suggestion? Suggest(World world, Promotion promo, Func<Fighter, int> abilityRead)
    {
        var valuation = new FighterValuation();
        var roster = world.RosterOf(promo.Id).Where(f => f.Status != FighterStatus.Retired).ToList();
        if (promo.Divisions.Count == 0) return null;

        // Only suggest fighters for a division the org actually runs (weight AND sex).
        var market = world.FreeAgents()
            .Where(f => f.Status == FighterStatus.Active && promo.Divisions.Any(d => d.Matches(f)))
            .Select(f =>
            {
                var exp = valuation.Evaluate(world, f);
                return new Candidate(f, exp, Negotiator.EffectiveAskingPurse(promo, f, exp), abilityRead(f));
            })
            .ToList();
        if (market.Count == 0) return null;

        // Affordable = the purse this org would actually pay fits the bankroll many
        // times over; if nothing on the market does, fall back to the cheapest ask.
        decimal purseCeiling = Math.Max(6_000m, promo.Balance.Amount / 12m);
        var affordable = market.Where(m => m.Ask.Amount <= purseCeiling).ToList();
        if (affordable.Count == 0)
            affordable = new() { market.OrderBy(m => m.Ask.Amount).ThenBy(m => m.Fighter.Id.Value).First() };

        var best = affordable
            .OrderByDescending(m => Score(m, roster, world))
            .ThenBy(m => m.Fighter.Id.Value)
            .First();

        int have = roster.Count(r => r.WeightClass == best.Fighter.WeightClass && r.Sex == best.Fighter.Sex);
        return new Suggestion(best.Fighter, best.Expectations, best.Ask, best.Scouted, ReasonFor(best, have, world, promo));
    }

    private static double Score(Candidate c, List<Fighter> roster, World world)
    {
        var f = c.Fighter;
        int have = roster.Count(r => r.WeightClass == f.WeightClass && r.Sex == f.Sex);
        int need = Math.Max(0, DivisionTarget - have);

        // Thin divisions dominate; then scouted ability, the runway youth and a
        // winning run suggest, and value for money.
        return need * 14
               + c.Scouted
               + Prospect(f, world)
               - (double)c.Ask.Amount / 1_000.0;
    }

    /// <summary>
    /// What a scout can honestly say about upside from public facts: years of
    /// runway left (age) and an unbeaten or winning start. Worth up to ~10 points,
    /// about what the old true-potential term was worth on a typical prospect.
    /// </summary>
    private static double Prospect(Fighter f, World world)
    {
        int age = f.AgeOn(world.CurrentDate);
        double runway = Math.Clamp(27 - age, 0, 6) * 1.25;            // 21 or younger: 7.5
        double run = f.Record.Wins > 0 && f.Record.Losses == 0 ? 2.5   // unbeaten
                   : f.Record.CurrentStreak >= 2 ? 1.5                  // on a run
                   : 0;
        return runway + run;
    }

    private static string ReasonFor(Candidate c, int have, World world, Promotion promo)
    {
        var f = c.Fighter;
        int age = f.AgeOn(world.CurrentDate);
        string why = have < DivisionTarget
            ? $"{f.WeightClass} is thin ({have} on the roster)"
            : $"best value on the market at {f.WeightClass}";

        bool unbeaten = f.Record.Wins > 0 && f.Record.Losses == 0;
        string extra =
            age <= YouthAge && unbeaten ? $", {age} and unbeaten ({f.Record})"
            : age <= YouthAge && f.Record.TotalBouts == 0 ? $", {age} and yet to debut"
            : age <= YouthAge ? $", {age} with years to develop"
            : unbeaten && f.Record.Wins >= 3 ? $", unbeaten ({f.Record})"
            : f.Record.CurrentStreak >= 3 ? $", on a {f.Record.CurrentStreak}-fight win streak"
            : "";

        return $"Scout's pick: {f.FullName} — {why}; scouted {c.Scouted}{extra}, {AskText(c, promo)}.";
    }

    // The ask this org will be quoted (the market row's figure); when its terms
    // undercut the open-market exclusive rate, say why and name that rate too.
    private static string AskText(Candidate c, Promotion promo)
    {
        if (c.Ask.Amount >= c.Expectations.AskingPurse.Amount) return $"asking ~{c.Ask}/fight";
        var terms = new List<string>();
        if (!Negotiator.WantsExclusivity(promo)) terms.Add("non-exclusive");
        if (Negotiator.GratitudeFactor(c.Fighter, promo.Id) < 1m) terms.Add("discoverer's discount");
        return $"asking ~{c.Ask}/fight ({string.Join(", ", terms)}; exclusive rate {c.Expectations.AskingPurse})";
    }
}
