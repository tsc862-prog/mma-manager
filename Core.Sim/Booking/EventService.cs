using MmaManager.Core.Model;
using MmaManager.Core.Sim.Engine;

namespace MmaManager.Core.Sim.Booking;

/// <summary>
/// The booking loop's front door: turn a set of matchups into a booked event,
/// then run the card — simulating every bout, applying results to the world, and
/// settling the box office. Stateless glue over the engine and the processors.
/// </summary>
public sealed class EventService
{
    private readonly FightEngine _engine;

    // Above this interest a market is already warm; big shows only convert cooler ones.
    private const int WarmMarketThreshold = 58;

    public EventService(FightEngine? engine = null) => _engine = engine ?? new FightEngine();

    /// <summary>
    /// Book a card of matchups into a scheduled <see cref="Event"/> (optionally in
    /// a chosen host city). <paramref name="biggestRoom"/> is for the once-a-year
    /// traditions: a flagship goes in the big house rather than the room that
    /// matches an ordinary night's draw — that IS the occasion.
    /// </summary>
    public Event CreateEvent(World world, PromotionId promoId, string name, DateOnly date, IReadOnlyList<Matchup> card,
        CityId? city = null, bool biggestRoom = false)
    {
        var promo = world.Get(promoId);
        var region = promo?.BaseRegion ?? Region.NorthAmerica;
        var venue = promo is null ? null
            : biggestRoom && city is { } big ? Geography.WorldMap.BiggestVenueIn(world, big)
            : city is { } chosen ? Geography.WorldMap.PickVenueIn(world, promo, chosen)
            : Geography.WorldMap.PickVenue(world, promo);
        var ev = new Event
        {
            Id = world.NewEventId(),
            Promotion = promoId,
            Name = name,
            Date = date,
            Region = venue is not null && world.Get(venue.City) is { } c ? c.Region : region,
            Venue = venue?.Name,
            VenueId = venue?.Id,
            Status = EventStatus.Scheduled,
        };

        foreach (var m in card)
        {
            ev.Card.Add(new Fight
            {
                Id = world.NewFightId(),
                RedCorner = m.Red,
                BlueCorner = m.Blue,
                WeightClass = m.WeightClass,
                ScheduledRounds = m.Rounds,
                IsTitleFight = m.IsTitle,
                Title = m.Title,
            });
        }

        world.Events[ev.Id] = ev;
        return ev;
    }

    /// <summary>
    /// Simulate every bout on the card in running order, apply the results to
    /// records/rankings/titles, settle the finances, and mark the event complete.
    /// Pass <paramref name="playByPlay"/> true to keep each bout's commentary.
    /// </summary>
    public void RunEvent(World world, Event ev, bool playByPlay = false)
    {
        // Every card runs at a realistic length — flesh out the undercard from the
        // roster, or (for a small org) with local-vs-local filler.
        CardFiller.PadToMinimum(world, ev);

        // A card booked without an explicit main-card split fits itself to the
        // broadcast window the org's media deals demand (the whole card, if nobody's airing it).
        if (ev.MainCardFights == 0 && world.Get(ev.Promotion) is { } host
            && Finance.MediaService.BroadcastWindow(host) is { } window)
            ev.MainCardFights = Math.Min(ev.Card.Count, window);

        // Bouts run under the HOUSE rules — the promotion staging the card
        // decides how long its rounds go.
        int roundSeconds = (world.Get(ev.Promotion)?.Rules.RoundMinutes ?? 5) * 60;
        foreach (var fight in ev.Card)
        {
            var opts = new FightParams
            {
                ScheduledRounds = fight.ScheduledRounds,
                RoundSeconds = roundSeconds,
                GeneratePlayByPlay = playByPlay,
            };
            _engine.Simulate(fight, world, opts);
            ResultsProcessor.Apply(world, ev, fight);
        }

        Settle(world, ev);
    }

    /// <summary>
    /// Close out a card whose bouts are already simulated: settle the box office
    /// into the ledger, grow regional popularity, refresh rankings, mark complete.
    /// Split out so specials (e.g. a one-night tournament) can build their bracket
    /// and then settle the same way.
    /// </summary>
    public void Settle(World world, Event ev)
    {
        ev.Financials = Financials.Compute(world, ev);
        if (world.Get(ev.Promotion) is { } promo)
        {
            // Settle the box office as categorised ledger entries (netting to NetProfit).
            var fin = ev.Financials;
            Finance.Ledger.Income(promo, ev.Date, FinanceCategory.EventGate, fin.GateRevenue, $"{ev.Name} — gate");
            Finance.Ledger.Income(promo, ev.Date, FinanceCategory.EventPpv, fin.PayPerViewRevenue, $"{ev.Name} — PPV");
            Finance.Ledger.Expense(promo, ev.Date, FinanceCategory.Production, fin.ProductionCost, $"{ev.Name} — production");
            Finance.Ledger.Expense(promo, ev.Date, FinanceCategory.FighterPurse, fin.FighterPayroll, $"{ev.Name} — fighter payroll");

            PayFightOfTheNight(world, promo, ev);

            // Fight night is when the commercial side gets paid: sponsor checks,
            // media rights fees, the merch table, the streaming platform's bump.
            Finance.RevenueService.SettleEvent(world, promo, ev);

            // Promises come due too: an accepted sponsor showcase is judged
            // against the card the player actually put on.
            if (promo.Id == world.PlayerPromotion)
                Living.Decisions.OnPlayerEventSettled(world, promo, ev);

            // Staging the card builds the promotion's standing in that region,
            // and a big, well-run show lifts its prestige and reputation. How far
            // the brand climbs depends on the pull of the names it put on.
            if (Geography.WorldMap.CityOf(world, ev) is { } host)
            {
                Geography.WorldMap.GrowCityPopularity(world, promo, host, CardStarPower(world, ev), fin.Attendance);
                // A big promotion bringing cards to a COOL market grows the sport
                // there — the bigger the stage, the more new eyes. A market that's
                // already hot doesn't need converting, so the lift only applies below
                // a threshold and outpaces the monthly decay only if the shows keep coming.
                int reach = (int)Org.OrgSizing.SizeOf(promo);
                if (reach >= (int)OrgSize.National
                    && Geography.MmaInterest.InCity(world, host) < WarmMarketThreshold)
                    Geography.MmaInterest.BumpCity(world, host, reach - (int)OrgSize.National + 2);
            }
            Org.Standing.AfterEvent(world, promo, ev);
            Org.Milestones.CheckAfterEvent(world, promo, ev);

            // Refresh contender rankings from the updated body of work.
            RankingService.RecomputePromotion(world, promo, ev.Date);
        }

        ev.Status = EventStatus.Completed;
    }

    /// <summary>
    /// The pull of the card's biggest names — the average popularity of its top
    /// three fighters. This is the ceiling the promotion's brand can climb toward
    /// (a card of nobodies won't grow an established org; a stacked card will).
    /// </summary>
    private static int CardStarPower(World world, Event ev)
    {
        var pops = ev.Card
            .SelectMany(f => new[] { f.RedCorner, f.BlueCorner })
            .Select(id => world.Get(id)?.Intangibles.Popularity.Value ?? 0)
            .OrderByDescending(p => p)
            .Take(3)
            .ToList();
        return pops.Count == 0 ? 0 : pops.Sum() / pops.Count;
    }

    /// <summary>
    /// The org's discretionary Fight of the Night: both fighters of the card's
    /// best-rated bout get the bonus. Real money out the door — and the kind of
    /// thing that makes fighters want to work here (morale, a touch of rep).
    /// </summary>
    private static void PayFightOfTheNight(World world, Promotion promo, Event ev)
    {
        if (promo.Rules.FightOfNightBonus.Amount <= 0) return;
        var best = ev.Card
            .Where(f => f.Outcome is not null)
            .OrderByDescending(Flavor.MatchRating.Of)
            .FirstOrDefault();
        if (best is null || Flavor.MatchRating.Of(best) < 32) return;   // nobody bonuses a dud card

        foreach (var id in new[] { best.RedCorner, best.BlueCorner })
        {
            if (world.Get(id) is not { } f) continue;
            Finance.Ledger.Expense(promo, ev.Date, FinanceCategory.FighterPurse,
                promo.Rules.FightOfNightBonus, $"{ev.Name} — Fight of the Night ({f.LastName})");
            f.Morale = Math.Clamp(f.Morale + 6, 0, 100);
            f.Intangibles.Popularity = f.Intangibles.Popularity.Value + 1;
        }
        promo.Reputation = Math.Min(100, promo.Reputation.Value + 1);

        var (r, b) = (world.Get(best.RedCorner), world.Get(best.BlueCorner));
        if (ev.Promotion == world.PlayerPromotion && r is not null && b is not null)
            Living.Mail.Post(world, ev.Date, MailCategory.BoardNote, "Matchmaking",
                $"Fight of the Night: {r.LastName} vs {b.LastName}",
                $"{Flavor.MatchRating.StarsFor(best)} — both men collect the {promo.Rules.FightOfNightBonus} bonus. Money well spent; the locker room noticed.",
                aboutEvent: ev.Id);
    }

    /// <summary>Convenience: matchmake, book, and run a full card in one call.</summary>
    public Event BookAndRun(World world, PromotionId promoId, string name, DateOnly date,
        Matchmaker matchmaker, int targetFights = 9, bool playByPlay = false)
    {
        var card = matchmaker.BuildCard(world, promoId, targetFights);
        var ev = CreateEvent(world, promoId, name, date, card);
        RunEvent(world, ev, playByPlay);
        return ev;
    }
}
