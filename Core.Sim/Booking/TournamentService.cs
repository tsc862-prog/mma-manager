using MmaManager.Core.Model;
using MmaManager.Core.Sim.Engine;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Core.Sim.Booking;

/// <summary>
/// A one-night tournament: a single-elimination bracket in a weight class, decided
/// on one super-card. The whole thing runs at once — winners advance round by round
/// on the same night — then settles like any other event and crowns a Grand Prix
/// champion (prize money out of the promotion's pocket, and a headline).
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

    /// <summary>Run a <paramref name="size"/>-fighter (power of two) one-night tournament in a weight class,
    /// under the promotion's own rules. Returns the card, or null if it can't be filled.</summary>
    public Event? RunOneNight(World world, PromotionId promoId, WeightClass wc, int size, DateOnly date, Money prize) =>
        RunOneNight(world, promoId, size, date, prize, wc, rules: null);

    /// <summary>
    /// Run a <paramref name="size"/>-fighter (power of two) one-night tournament. Returns the card, or
    /// null if it can't be filled.
    /// <list type="bullet">
    /// <item><paramref name="wc"/> null = open weight: any of the promotion's match-ready men.</item>
    /// <item><paramref name="rules"/> null = the promotion's house rules (<c>OrgRules.Fight</c>, else
    /// today's). Rounds and minutes come from the rules (else 3 rounds of the house's minutes).</item>
    /// <item>The field is seeded by what the promotion THINKS of each man (scouting, not true ability):
    /// 1 v 8, 4 v 5, 2 v 7, 3 v 6. The next man in that order, if there is one, is the alternate.</item>
    /// <item>Condition carries: a winner goes into his next bout with what he has left after a rest
    /// (<see cref="BoutCondition.AfterRest"/>): damage, wind, a cut, a hurt hand.</item>
    /// <item>A winner who cannot go on (spent, a bad cut, or injured in the bout) withdraws: the
    /// alternate takes his place if he has not been used; otherwise his next opponent walks over.</item>
    /// <item>A draw: with judges, the higher seed advances (as ever); with no judges (a draw at the time
    /// limit) both men are out, and the slot goes to the alternate or becomes a walkover.</item>
    /// </list>
    /// Deterministic: every bout is seeded from its fight id, as any booked fight is.
    /// </summary>
    public Event? RunOneNight(World world, PromotionId promoId, int size, DateOnly date, Money prize,
        WeightClass? wc, FightRules? rules)
    {
        if (size < 2 || (size & (size - 1)) != 0) return null;   // must be a power of two
        if (world.Get(promoId) is not { } promo) return null;
        rules ??= promo.Rules.Fight ?? FightRules.Modern;

        var seeded = world.RosterOf(promoId)
            .Where(f => (wc is { } w ? f.WeightClass == w : f.Sex == Sex.Male) && f.IsMatchReady(date))
            .OrderByDescending(f => Scouting.ScoutingService.PerceivedAbility(world, promo, f))
            .ThenBy(f => f.Id.Value)
            .Take(size + 1)
            .ToList();
        if (seeded.Count < size) return null;
        Fighter? alternate = seeded.Count > size ? seeded[size] : null;

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

        int rounds = rules.Rounds ?? 3;
        int roundSeconds = (rules.RoundMinutes ?? promo.Rules.RoundMinutes) * 60;
        var condition = new Dictionary<FighterId, BoutCondition>();
        var minutes = new Dictionary<FighterId, double>();
        bool alternateUsed = false;

        var round = BracketOrder(size).Select(i => (Fighter?)seeded[i]).ToList();
        while (round.Count > 1)
        {
            var next = new List<Fighter?>();
            for (int i = 0; i + 1 < round.Count; i += 2)
            {
                var red = round[i]; var blue = round[i + 1];
                if (red is null || blue is null) { next.Add(red ?? blue); continue; }   // a walkover (or an empty slot)

                var fight = new Fight
                {
                    Id = world.NewFightId(),
                    RedCorner = red.Id,
                    BlueCorner = blue.Id,
                    WeightClass = wc ?? red.WeightClass,
                    ScheduledRounds = rounds,
                };
                Living.LocalCircuit.YieldTo(world, new[] { fight.RedCorner, fight.BlueCorner });
                var opts = new FightParams
                {
                    ScheduledRounds = rounds,
                    RoundSeconds = roundSeconds,
                    GeneratePlayByPlay = false,       // no screen shows these bouts' text
                    Rules = rules,
                    RedStart = condition.GetValueOrDefault(red.Id),
                    BlueStart = condition.GetValueOrDefault(blue.Id),
                };
                var o = _engine.Simulate(fight, world, opts, out var redEnd, out var blueEnd);
                ResultsProcessor.Apply(world, ev, fight);
                ev.Card.Add(fight);

                double fought = ((o.EndRound - 1) * roundSeconds + o.EndTimeSeconds) / 60.0;
                minutes[red.Id] = minutes.GetValueOrDefault(red.Id) + fought;
                minutes[blue.Id] = minutes.GetValueOrDefault(blue.Id) + fought;

                Fighter? winner = o.Winner == red.Id ? red : o.Winner == blue.Id ? blue : null;
                if (winner is null && rules.Judges) winner = red;                  // a judged draw: the higher seed
                if (winner is not null)
                {
                    var rested = (winner == red ? redEnd : blueEnd).AfterRest(winner, minutes[winner.Id]);
                    condition[winner.Id] = rested;
                    bool final = round.Count == 2;
                    if (!final && (!rested.CanContinue || winner.CurrentInjury is not null))
                    {
                        News.Post(world, date, NewsCategory.Result,
                            $"{winner.FullName} cannot continue in the {ev.Name}",
                            "Won the bout but is in no state to fight again tonight.", promoId, winner.Id);
                        winner = null;
                    }
                }
                if (winner is null && round.Count > 2 && alternate is not null && !alternateUsed)
                {
                    winner = alternate;
                    alternateUsed = true;
                }
                next.Add(winner);
            }
            round = next;
        }

        _events.Settle(world, ev);

        if (round[0] is not { } champion) return ev;   // nobody left standing: no champion tonight
        if (prize.Amount > 0)
            Finance.Ledger.Expense(promo, date, FinanceCategory.Other, prize, $"{ev.Name} — winner's purse");
        News.Post(world, date, NewsCategory.Result,
            $"{champion.FullName} wins the {ev.Name}",
            wc is { } c1 ? $"Ran the table to take the {FriendlyClass(c1)} Grand Prix." : "Ran the table on a one-night open-weight bracket.",
            promoId, champion.Id);

        return ev;
    }

    /// <summary>Standard bracket order for seeds 0..size-1: 1 v 8, 4 v 5, 2 v 7, 3 v 6 for eight.</summary>
    internal static int[] BracketOrder(int size)
    {
        var order = new List<int> { 0 };
        for (int n = 1; n < size; n *= 2)
        {
            var grown = new List<int>(order.Count * 2);
            foreach (int s in order) { grown.Add(s); grown.Add(2 * n - 1 - s); }
            order = grown;
        }
        return order.ToArray();
    }

    private static string FriendlyClass(WeightClass wc) =>
        System.Text.RegularExpressions.Regex.Replace(wc.ToString(), "(?<=.)([A-Z])", " $1");
}
