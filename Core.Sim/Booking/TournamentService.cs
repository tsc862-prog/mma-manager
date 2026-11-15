using MmaManager.Core.Model;
using MmaManager.Core.Sim.Engine;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Core.Sim.Booking;

/// <summary>What a one-night tournament produced: the card, the champion (null when the final was drawn
/// even after overtime, or nobody was left to fight it) and how every slot of the bracket was filled.</summary>
public sealed record OneNightResult(Event Event, Fighter? Champion, BracketNight Night);

/// <summary>
/// A one-night tournament: a single-elimination bracket, decided on one super-card. The whole thing
/// runs at once — winners advance round by round on the same night — then settles like any other
/// event and crowns a Grand Prix champion (prize money out of the promotion's pocket, and a headline).
/// The bracket's own rules (seeding, carried condition, withdrawals, the alternate, draws and the
/// final's overtime) live in <see cref="OneNightBracket"/>, shared with the lab.
/// </summary>
public sealed class TournamentService
{
    private readonly FightEngine _engine;
    private readonly EventService _events;

    public TournamentService(FightEngine? engine = null)
    {
        _engine = engine ?? new FightEngine();
        _events = new EventService(_engine);
    }

    /// <summary>
    /// Run a <paramref name="size"/>-fighter (power of two) one-night tournament in a weight class,
    /// under the promotion's own rules. Returns the card, or null if it can't be filled. (Nothing in
    /// the game calls this yet; it now runs through <see cref="OneNightBracket"/> like the overload below,
    /// so its seeding, round length and carried condition changed with the Origins work.)
    /// </summary>
    public Event? RunOneNight(World world, PromotionId promoId, WeightClass wc, int size, DateOnly date, Money prize) =>
        RunOneNight(world, promoId, size, date, prize, wc, rules: null)?.Event;

    /// <summary>
    /// Run a <paramref name="size"/>-fighter (power of two) one-night tournament. Returns the result,
    /// or null if the field can't be filled.
    /// <list type="bullet">
    /// <item><paramref name="wc"/> null = open weight: any of the promotion's match-ready men.</item>
    /// <item><paramref name="rules"/> null = the promotion's house rules (<c>OrgRules.Fight</c>, else
    /// today's). Rounds and minutes come from the rules (else 3 rounds of the house's minutes).</item>
    /// <item>The field is seeded by what the promotion THINKS of each man (scouting, not true ability).
    /// The next man in that order, if there is one, is the alternate.</item>
    /// <item>The rest is <see cref="OneNightBracket.Run"/>; "injured" means the results processor gave
    /// the winner an injury in the bout.</item>
    /// <item>No champion (a final drawn after overtime): the prize is split between the finalists and the
    /// news says so. Nobody left for the final: no prize, and the news says that too.</item>
    /// </list>
    /// Deterministic: every bout is seeded from its fight id, as any booked fight is.
    /// </summary>
    public OneNightResult? RunOneNight(World world, PromotionId promoId, int size, DateOnly date, Money prize,
        WeightClass? wc, FightRules? rules)
    {
        if (size < 2 || (size & (size - 1)) != 0) return null;   // must be a power of two
        if (world.Get(promoId) is not { } promo) return null;
        if (Field(world, promo, size, date, wc) is not { } field) return null;

        var venue = Geography.WorldMap.PickVenue(world, promo);
        string label = wc is { } c0 ? FriendlyClass(c0) : "Open";
        var ev = new Event
        {
            Id = world.NewEventId(),
            Promotion = promoId,
            Name = $"{promo.Abbreviation} {label} Grand Prix",
            Date = date,
            Region = venue is not null && world.Get(venue.City) is { } c ? c.Region : promo.BaseRegion,
            Venue = venue?.Name,
            VenueId = venue?.Id,
            Tier = EventTier.SuperCard,
            Status = EventStatus.Scheduled,
        };
        world.Events[ev.Id] = ev;
        return Run(world, promo, ev, field.Seeded, field.Alternate, prize, wc, rules);
    }

    /// <summary>
    /// Run a bracket into a night already on the calendar (an Origins tournament night: announced with an
    /// empty card, drawn and fought on its date). Same rules as the overload above; the event's own date
    /// and venue are kept. Null, and the event untouched, when the field can't be filled.
    /// </summary>
    public OneNightResult? RunOneNight(World world, Event ev, int size, Money prize, WeightClass? wc, FightRules? rules)
    {
        if (size < 2 || (size & (size - 1)) != 0) return null;
        if (world.Get(ev.Promotion) is not { } promo) return null;
        if (Field(world, promo, size, ev.Date, wc) is not { } field) return null;
        return Run(world, promo, ev, field.Seeded, field.Alternate, prize, wc, rules);
    }

    /// <summary>The field, seeded by what the promotion THINKS of each man, and the alternate; null when it can't be filled.</summary>
    private static (List<Fighter> Seeded, Fighter? Alternate)? Field(World world, Promotion promo, int size, DateOnly date, WeightClass? wc)
    {
        var seeded = world.RosterOf(promo.Id)
            .Where(f => (wc is { } w ? f.WeightClass == w : f.Sex == Sex.Male) && f.IsMatchReady(date))
            .OrderByDescending(f => Scouting.ScoutingService.PerceivedAbility(world, promo, f))
            .ThenBy(f => f.Id.Value)
            .Take(size + 1)
            .ToList();
        if (seeded.Count < size) return null;
        return (seeded.Take(size).ToList(), seeded.Count > size ? seeded[size] : null);
    }

    private OneNightResult Run(World world, Promotion promo, Event ev, List<Fighter> field, Fighter? alternate, Money prize,
        WeightClass? wc, FightRules? rules)
    {
        rules ??= promo.Rules.Fight ?? FightRules.Modern;
        var promoId = promo.Id;
        var date = ev.Date;
        int roundSeconds = (rules.RoundMinutes ?? promo.Rules.RoundMinutes) * 60;
        var night = OneNightBracket.Run(field, alternate, rules, roundSeconds,
            (red, blue, p) =>
            {
                var fight = new Fight
                {
                    Id = world.NewFightId(),
                    RedCorner = red.Id,
                    BlueCorner = blue.Id,
                    // Open weight is filed under the open division's nominal class and labelled as such.
                    WeightClass = wc ?? Division.OpenClass,
                    OpenWeight = wc is null,
                    ScheduledRounds = p.ScheduledRounds,
                };
                LocalCircuit.YieldTo(world, new[] { fight.RedCorner, fight.BlueCorner });
                var o = _engine.Simulate(fight, world, p, out var redEnd, out var blueEnd);   // no screen shows these bouts' text
                ResultsProcessor.Apply(world, ev, fight);
                ev.Card.Add(fight);
                return new BracketBoutResult(o, redEnd, blueEnd, fight.Id);
            },
            injured: f => f.CurrentInjury is not null);

        _events.Settle(world, ev);

        foreach (var b in night.Bouts)
            if (b.Withdrew is { } wid && world.Get(wid) is { } w)
                News.Post(world, date, NewsCategory.Result,
                    $"{w.FullName} cannot continue in the {ev.Name}",
                    "Won the bout but is in no state to fight again tonight.", promoId, wid);

        if (night.Champion is { } champion)
        {
            if (prize.Amount > 0)
                Finance.Ledger.Expense(promo, date, FinanceCategory.Other, prize, $"{ev.Name} — winner's purse");
            News.Post(world, date, NewsCategory.Result,
                $"{champion.FullName} wins the {ev.Name}",
                wc is { } c1 ? $"Ran the table to take the {FriendlyClass(c1)} Grand Prix." : "Ran the table on a one-night open-weight bracket.",
                promoId, champion.Id);
        }
        else if (night.FinalDrawn && night.Bouts[^1] is { Red: { } r, Blue: { } bl })
        {
            var a = world.Get(r); var b = world.Get(bl);
            if (prize.Amount > 0)
            {
                var half = new Money(prize.Amount / 2);
                Finance.Ledger.Expense(promo, date, FinanceCategory.Other, half, $"{ev.Name} — purse split ({a?.FullName})");
                Finance.Ledger.Expense(promo, date, FinanceCategory.Other, new Money(prize.Amount - half.Amount), $"{ev.Name} — purse split ({b?.FullName})");
            }
            News.Post(world, date, NewsCategory.Result,
                $"No winner in the {ev.Name}",
                $"{a?.FullName} and {b?.FullName} fought the final to a standstill, overtime included. The purse is split and nobody takes the title.",
                promoId, r);
        }
        else
        {
            News.Post(world, date, NewsCategory.Result,
                $"The {ev.Name} ends without a champion",
                "Nobody was left fit to fight the final.", promoId);
        }

        return new OneNightResult(ev, night.Champion, night);
    }

    private static string FriendlyClass(WeightClass wc) =>
        System.Text.RegularExpressions.Regex.Replace(wc.ToString(), "(?<=.)([A-Z])", " $1");
}
