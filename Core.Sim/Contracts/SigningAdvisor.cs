using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;

namespace MmaManager.Core.Sim.Contracts;

/// <summary>
/// The scout's pick for the promotion's next signing: weighs where the roster
/// is too thin to book, what the bankroll can actually afford, and the best
/// ability (plus youth upside) on the free-agent market. Purely advisory — the
/// player still negotiates the deal themselves.
/// </summary>
public static class SigningAdvisor
{
    /// <summary>A division needs roughly this many fighters before it books cleanly.</summary>
    private const int DivisionTarget = 6;

    public sealed record Suggestion(Fighter Fighter, ContractExpectations Expectations, string Reason);

    public static Suggestion? Suggest(World world, Promotion promo)
    {
        var valuation = new FighterValuation();
        var roster = world.RosterOf(promo.Id).Where(f => f.Status != FighterStatus.Retired).ToList();
        if (promo.Divisions.Count == 0) return null;

        // Only suggest fighters for a division the org actually runs (weight AND sex).
        var market = world.FreeAgents()
            .Where(f => f.Status == FighterStatus.Active && promo.Divisions.Any(d => d.Matches(f)))
            .Select(f => (Fighter: f, Expectations: valuation.Evaluate(world, f)))
            .ToList();
        if (market.Count == 0) return null;

        // Affordable = the asking purse fits the bankroll many times over; if
        // nothing on the market does, fall back to whatever asks the least.
        decimal purseCeiling = Math.Max(6_000m, promo.Balance.Amount / 12m);
        var affordable = market.Where(m => m.Expectations.AskingPurse.Amount <= purseCeiling).ToList();
        if (affordable.Count == 0)
            affordable = new() { market.OrderBy(m => m.Expectations.AskingPurse.Amount).First() };

        var best = affordable
            .OrderByDescending(m => Score(m.Fighter, m.Expectations, roster, world))
            .First();

        int have = roster.Count(r => r.WeightClass == best.Fighter.WeightClass && r.Sex == best.Fighter.Sex);
        return new Suggestion(best.Fighter, best.Expectations, ReasonFor(best.Fighter, best.Expectations, have, world));
    }

    private static double Score(Fighter f, ContractExpectations exp, List<Fighter> roster, World world)
    {
        int have = roster.Count(r => r.WeightClass == f.WeightClass && r.Sex == f.Sex);
        int need = Math.Max(0, DivisionTarget - have);
        int ability = FighterAbility.CurrentAbility(f);
        int upside = Math.Max(0, f.PotentialAbility.Value - ability);

        // Thin divisions dominate; then raw ability, youth upside, and value for money.
        return need * 14
               + ability
               + upside * 0.35
               - (double)exp.AskingPurse.Amount / 1_000.0;
    }

    private static string ReasonFor(Fighter f, ContractExpectations exp, int have, World world)
    {
        int ability = FighterAbility.CurrentAbility(f);
        int upside = Math.Max(0, f.PotentialAbility.Value - ability);
        string why = have < DivisionTarget
            ? $"{f.WeightClass} is thin ({have} on the roster)"
            : $"best value on the market at {f.WeightClass}";
        string extra = upside >= 8 ? $", {f.AgeOn(world.CurrentDate)} with real upside" : "";
        return $"Scout's pick: {f.FullName} — {why}; OVR {ability}{extra}, asking ~{exp.AskingPurse}/fight.";
    }
}
