using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;

namespace MmaManager.Core.Sim.Booking;

/// <summary>
/// The player's matchmaking surface: create and edit their own promotion's
/// scheduled events, book bouts from their roster with validation, and query
/// who's available/eligible. The <see cref="WorldTick"/> runs these events when
/// their date arrives — this class only builds and schedules them.
/// </summary>
public sealed partial class PlayerBooking
{
    /// <summary>
    /// Create an empty scheduled card for the player's promotion — in the chosen
    /// host city (the best-fitting venue there), or the auto-picked home room.
    /// A city beyond the promotion's reach tier (see <see cref="Org.OrgReach"/>)
    /// falls back to the HQ metro: you can't book Tokyo from a city outfit.
    /// </summary>
    public Event CreateEvent(World world, DateOnly date, string? name = null, EventTier tier = EventTier.FightNight, CityId? city = null)
    {
        var promo = world.Get(world.PlayerPromotion);
        int number = NextCardNumber(world, promo);
        if (promo is not null && city is { } want && !Org.OrgReach.CanBookIn(world, promo, want))
            city = promo.HomeCity;
        if (promo is not null && tier > Org.OrgPerks.MaxEventTier(promo))
            tier = Org.OrgPerks.MaxEventTier(promo);   // a city outfit can't stage a PPV
        var venue = promo is null ? null
            : city is { } chosen ? Geography.WorldMap.PickVenueIn(world, promo, chosen)
            : Geography.WorldMap.PickVenue(world, promo);
        var ev = new Event
        {
            Id = world.NewEventId(),
            Promotion = world.PlayerPromotion,
            Name = name ?? $"{promo?.Abbreviation ?? "Fight Night"} {number}",
            Date = date,
            Region = venue is not null && world.Get(venue.City) is { } c ? c.Region : (promo?.BaseRegion ?? Region.NorthAmerica),
            Venue = venue?.Name,
            VenueId = venue?.Id,
            Tier = tier,
            Status = EventStatus.Scheduled,
        };
        world.Events[ev.Id] = ev;
        return ev;
    }

    /// <summary>
    /// The number the promotion's next card carries: one past the highest number
    /// any of its cards has used ("AFC 161", "AFC Fight Night 162") or past its
    /// card count, whichever is higher. Derived from the cards themselves, so a
    /// cancelled card's number is never handed to a new one while any card still
    /// carries it (counting cards did that whenever a card other than the newest
    /// was cancelled).
    /// </summary>
    internal static int NextCardNumber(World world, Promotion? promo)
    {
        int count = 0, highest = 0;
        foreach (var e in world.Events.Values)
        {
            if (e.Promotion != world.PlayerPromotion) continue;
            count++;
            if (promo is null || !e.Name.StartsWith(promo.Abbreviation + " ", StringComparison.Ordinal)) continue;
            int end = e.Name.Length, start = end;
            while (start > 0 && char.IsAsciiDigit(e.Name[start - 1])) start--;
            if (start < end && start > 0 && e.Name[start - 1] == ' '
                && int.TryParse(e.Name.AsSpan(start, end - start), out int n))
                highest = Math.Max(highest, n);
        }
        return Math.Max(count, highest) + 1;
    }

    /// <summary>Add a bout to a card, or return false with a reason.</summary>
    public bool AddBout(World world, Event ev, FighterId red, FighterId blue, int rounds, bool titleFight, out string? error)
    {
        error = null;
        var r = world.Get(red);
        var b = world.Get(blue);
        if (r is null || b is null) { error = "Fighter not found."; return false; }
        if (red == blue) { error = "A fighter can't fight themselves."; return false; }
        if (r.Contract?.Promotion != world.PlayerPromotion || b.Contract?.Promotion != world.PlayerPromotion)
        { error = "Both fighters must be on your roster."; return false; }
        if (r.WeightClass != b.WeightClass) { error = "Fighters must be in the same weight class."; return false; }
        if (r.Sex != b.Sex) { error = "A bout can't cross men's and women's divisions."; return false; }
        if (!r.IsMatchReady(ev.Date) || !b.IsMatchReady(ev.Date)) { error = "A fighter is injured or still recovering."; return false; }
        if (BookedOnCard(ev, red) || BookedOnCard(ev, blue)) { error = "A fighter is already on this card."; return false; }
        if (BookedElsewhere(world, ev, red) || BookedElsewhere(world, ev, blue))
        { error = "A fighter is already booked on another upcoming event."; return false; }

        TitleId? title = null;
        if (titleFight)
        {
            var div = world.Get(world.PlayerPromotion)?.Divisions.FirstOrDefault(d => d.Matches(r));
            var t = div?.Title is { } tid ? world.Get(tid) : null;
            if (t is null) { error = "There's no title in this weight class."; return false; }
            if (t.CurrentChampion is { } champ && champ != red && champ != blue)
            { error = "A title fight must include the champion."; return false; }
            title = t.Id;
        }

        // New bouts join the BOTTOM of the bill (the front of the sim's card
        // list): the reigning main event keeps its slot, and the booker
        // promotes fights up the card deliberately, not by accident of order.
        ev.Card.Insert(0, new Fight
        {
            Id = world.NewFightId(),
            RedCorner = red,
            BlueCorner = blue,
            WeightClass = r.WeightClass,
            ScheduledRounds = rounds,
            IsTitleFight = titleFight,
            Title = title,
        });
        return true;
    }

    /// <summary>
    /// Book one of your fighters against unnamed local talent — the small-org
    /// special (City/Regional reach only). Cheap, easy, builds a prospect up;
    /// the local is minted from the host city's club scene.
    /// </summary>
    public bool AddLocalBout(World world, Event ev, FighterId red, PrimaryStyle style, int rounds, out string? error)
    {
        error = null;
        var promo = world.Get(world.PlayerPromotion);
        var r = world.Get(red);
        if (promo is null || r is null) { error = "Fighter not found."; return false; }
        if (Org.OrgSizing.SizeOf(promo) > OrgSize.Regional)
        { error = "Local fights are beneath the promotion now — book real opponents."; return false; }
        if (r.Contract?.Promotion != world.PlayerPromotion) { error = "The fighter must be on your roster."; return false; }
        if (!r.IsMatchReady(ev.Date)) { error = "The fighter is injured or still recovering."; return false; }
        if (BookedOnCard(ev, red)) { error = "The fighter is already on this card."; return false; }
        if (BookedElsewhere(world, ev, red)) { error = "The fighter is already booked on another upcoming event."; return false; }

        var local = Living.LocalScene.CreateOpponent(world, promo, ev, style, r.WeightClass, r.Sex);
        // Bottom of the bill, same as any new booking — a local fight has no
        // business bumping the main event anyway.
        ev.Card.Insert(0, new Fight
        {
            Id = world.NewFightId(),
            RedCorner = red,
            BlueCorner = local.Id,
            WeightClass = r.WeightClass,
            ScheduledRounds = rounds,
            IsTitleFight = false,
        });
        return true;
    }

    public void RemoveBout(Event ev, FightId fight) => RemoveBout(null, ev, fight);

    /// <summary>Remove a bout; with a world, an unfought local opponent evaporates back into the scene.</summary>
    public void RemoveBout(World? world, Event ev, FightId fight)
    {
        var removed = ev.Card.Where(f => f.Id == fight).ToList();
        ev.Card.RemoveAll(f => f.Id == fight);
        if (world is not null)
            foreach (var f in removed)
                ReleaseLocals(world, f);
    }

    public void CancelEvent(World world, Event ev)
    {
        foreach (var f in ev.Card) ReleaseLocals(world, f);
        world.Events.Remove(ev.Id);
    }

    // A local minted for a booking that never happened has no history to keep.
    internal static void ReleaseLocals(World world, Fight fight)
    {
        foreach (var id in new[] { fight.RedCorner, fight.BlueCorner })
            if (world.Get(id) is { LocalTalent: true } jobber && jobber.BoutHistory.Count == 0)
                world.Fighters.Remove(id);
    }

    /// <summary>Player-roster fighters available to book in a division (weight + sex; not already on this card).</summary>
    public IReadOnlyList<Fighter> AvailableRoster(World world, Event ev, WeightClass weightClass, Sex sex = Sex.Male) =>
        world.RosterOf(world.PlayerPromotion)
            .Where(f => f.WeightClass == weightClass && f.Sex == sex && f.IsMatchReady(ev.Date) && !BookedOnCard(ev, f.Id))
            .OrderByDescending(f => MatchmakingAdvisor.ScoutedRead(world, f))   // the scout's read, never the truth
            .ThenBy(f => f.Id.Value)
            .ToList();

    /// <summary>Eligible opponents for a chosen fighter (same class AND sex, available, not already booked).</summary>
    public IReadOnlyList<Fighter> EligibleOpponents(World world, Event ev, FighterId forFighter)
    {
        var f = world.Get(forFighter);
        if (f is null) return Array.Empty<Fighter>();
        return world.RosterOf(world.PlayerPromotion)
            .Where(o => o.Id != forFighter && o.WeightClass == f.WeightClass && o.Sex == f.Sex && o.IsMatchReady(ev.Date) && !BookedOnCard(ev, o.Id))
            .OrderByDescending(o => MatchmakingAdvisor.ScoutedRead(world, o))   // the scout's read, never the truth
            .ThenBy(o => o.Id.Value)
            .ToList();
    }

    /// <summary>The player's scheduled cards this fighter is booked on (date order).</summary>
    public static IReadOnlyList<Event> CardsBooking(World world, FighterId fighter) =>
        world.Events.Values
            .Where(e => e.Promotion == world.PlayerPromotion && e.Status == EventStatus.Scheduled
                        && world.Get(e.Promotion) is not null && BookedOnCard(e, fighter))
            .OrderBy(e => e.Date).ThenBy(e => e.Id.Value)
            .ToList();

    public IEnumerable<Event> UpcomingEvents(World world) =>
        world.Events.Values
            .Where(e => e.Promotion == world.PlayerPromotion && e.Status == EventStatus.Scheduled)
            .OrderBy(e => e.Date)
            .ThenBy(e => e.Id.Value);

    private static bool BookedOnCard(Event ev, FighterId id) =>
        ev.Card.Any(f => f.RedCorner == id || f.BlueCorner == id);

    // Committed to any other scheduled card, the AI's announced cards included.
    private static bool BookedElsewhere(World world, Event ev, FighterId id) =>
        Living.ForwardCalendar.IsCommitted(world, id, ev.Id);
}
