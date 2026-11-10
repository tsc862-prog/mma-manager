using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Finance;

/// <summary>
/// The one place a partner's contract terms are read against a card. Sponsors
/// and networks share the same requirement vocabulary (<see cref="DealRequirement"/>);
/// this audits a settled event against a term sheet and says, per clause, whether
/// the promotion delivered. Structural clauses (rules, card shape, title fight)
/// can also be audited BEFORE the card runs — that subset powers the booking
/// screen's compliance warnings.
/// </summary>
public static class DealCompliance
{
    public readonly record struct Clause(DealRequirement Requirement, bool Met, string Text);

    /// <summary>Audit every requirement against the (settled) event. Cadence clauses are skipped — they're checked monthly, not per card.</summary>
    public static List<Clause> Audit(World world, Event ev, IEnumerable<DealRequirement> requirements) =>
        requirements.Where(r => r.Kind != DealRequirementKind.EventCadence)
            .Select(r => new Clause(r, Meets(world, ev, r), Describe(r)))
            .ToList();

    /// <summary>The clauses that can be judged from the booking alone (no results or box office needed).</summary>
    public static bool IsStructural(DealRequirementKind kind) => kind is
        DealRequirementKind.StandardRules or DealRequirementKind.TitleFight or
        DealRequirementKind.MinMainCard or DealRequirementKind.MaxMainCard or
        DealRequirementKind.MinStarPower or DealRequirementKind.MinLocalFighters;

    public static bool Meets(World world, Event ev, DealRequirement req)
    {
        var promo = world.Get(ev.Promotion);
        switch (req.Kind)
        {
            case DealRequirementKind.MinCardQuality:
                var fought = ev.Card.Where(f => f.Outcome is not null).ToList();
                if (fought.Count == 0) return true;   // nothing ran — judged elsewhere (cancellation)
                return fought.Average(f => (double)Flavor.MatchRating.Of(f)) >= req.Threshold;

            case DealRequirementKind.MinLocalFighters:
                return CountLocals(world, ev) >= req.Threshold;

            case DealRequirementKind.StandardRules:
                return promo is null || (promo.Rules.RoundMinutes >= 5 && promo.Rules.StandardRounds >= 3);

            case DealRequirementKind.MinStarPower:
                if (ev.Card.Count == 0) return false;
                var main = ev.Card[^1];
                return Math.Max(
                    world.Get(main.RedCorner)?.Intangibles.Popularity.Value ?? 0,
                    world.Get(main.BlueCorner)?.Intangibles.Popularity.Value ?? 0) >= req.Threshold;

            case DealRequirementKind.MinAttendance:
                return (ev.Financials?.Attendance ?? 0) >= req.Threshold;

            case DealRequirementKind.TitleFight:
                return ev.Card.Any(f => f.IsTitleFight);

            case DealRequirementKind.MinMainCard:
                return ev.EffectiveMainCard >= req.Threshold;

            case DealRequirementKind.MaxMainCard:
                return ev.EffectiveMainCard <= req.Threshold;

            case DealRequirementKind.EventCadence:
                return true;   // a calendar clause — audited by the monthly tick

            default:
                return true;
        }
    }

    /// <summary>A fighter is "local" to the card if they're from the host region (or are local-scene talent).</summary>
    private static int CountLocals(World world, Event ev) =>
        ev.Card.SelectMany(f => new[] { f.RedCorner, f.BlueCorner })
            .Distinct()
            .Select(id => world.Get(id))
            .Count(f => f is not null && (f.LocalTalent
                || f.Nationality == ev.Region
                || (f.HomeCity is { } hc && world.Get(hc)?.Region == ev.Region)));

    /// <summary>The clause as it reads on the term sheet.</summary>
    public static string Describe(DealRequirement req) => req.Kind switch
    {
        DealRequirementKind.MinCardQuality => $"cards must deliver (avg fight rating {req.Threshold}+)",
        DealRequirementKind.MinLocalFighters => $"{req.Threshold}+ local fighters on the card",
        DealRequirementKind.StandardRules => "orthodox rules (5-minute rounds, 3+ round bouts)",
        DealRequirementKind.MinStarPower => $"a main-eventer people know (popularity {req.Threshold}+)",
        DealRequirementKind.MinAttendance => $"a real house ({req.Threshold:N0}+ attendance)",
        DealRequirementKind.TitleFight => "a belt on the line",
        DealRequirementKind.MinMainCard => $"at least {req.Threshold} main-card fights",
        DealRequirementKind.MaxMainCard => $"at most {req.Threshold} main-card fights",
        DealRequirementKind.EventCadence => $"an event at least every {req.Threshold} days",
        _ => req.Kind.ToString(),
    };
}
