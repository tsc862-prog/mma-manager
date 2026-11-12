using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
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

    /// <summary>Run a <paramref name="size"/>-fighter (power of two) one-night tournament. Returns the card, or null if it can't be filled.</summary>
    public Event? RunOneNight(World world, PromotionId promoId, WeightClass wc, int size, DateOnly date, Money prize)
    {
        if (size < 2 || (size & (size - 1)) != 0) return null;   // must be a power of two
        if (world.Get(promoId) is not { } promo) return null;

        var field = world.RosterOf(promoId)
            .Where(f => f.WeightClass == wc && f.IsMatchReady(date))
            .OrderByDescending(FighterAbility.CurrentAbility)
            .Take(size)
            .ToList();
        if (field.Count < size) return null;

        var venue = Geography.WorldMap.PickVenue(world, promo);
        var ev = new Event
        {
            Id = world.NewEventId(),
            Promotion = promoId,
            Name = $"{promo.Abbreviation} {FriendlyClass(wc)} Grand Prix",
            Date = date,
            Region = venue is not null && world.Get(venue.City) is { } c ? c.Region : promo.BaseRegion,
            Venue = venue?.Name,
            VenueId = venue?.Id,
            Tier = EventTier.SuperCard,
            Status = EventStatus.Scheduled,
        };
        world.Events[ev.Id] = ev;

        var round = field;
        while (round.Count > 1)
        {
            var winners = new List<Fighter>();
            for (int i = 0; i + 1 < round.Count; i += 2)
            {
                var fight = new Fight
                {
                    Id = world.NewFightId(),
                    RedCorner = round[i].Id,
                    BlueCorner = round[i + 1].Id,
                    WeightClass = wc,
                    ScheduledRounds = 3,
                };
                Living.LocalCircuit.YieldTo(world, new[] { fight.RedCorner, fight.BlueCorner });
                _engine.Simulate(fight, world, new FightParams { ScheduledRounds = 3, GeneratePlayByPlay = false });   // no screen shows these bouts' text
                ResultsProcessor.Apply(world, ev, fight);
                ev.Card.Add(fight);

                var winnerId = fight.Outcome?.Winner ?? round[i].Id;   // a draw advances the higher seed
                winners.Add(world.Get(winnerId)!);
            }
            round = winners;
        }

        _events.Settle(world, ev);

        var champion = round[0];
        if (prize.Amount > 0)
            Finance.Ledger.Expense(promo, date, FinanceCategory.Other, prize, $"{ev.Name} — winner's purse");
        News.Post(world, date, NewsCategory.Result,
            $"{champion.FullName} wins the {ev.Name}",
            $"Ran the table to take the {FriendlyClass(wc)} Grand Prix.", promoId, champion.Id);

        return ev;
    }

    private static string FriendlyClass(WeightClass wc) =>
        System.Text.RegularExpressions.Regex.Replace(wc.ToString(), "(?<=.)([A-Z])", " $1");
}
