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
    public Event CreateEvent(World world, DateOnly date, string? name = null, EventTier tier = EventTier.FightNight, CityId? city = null) =>
        TryCreateEvent(world, date, out var refused, name, tier, city)
        ?? throw new InvalidOperationException(refused);

    /// <summary>
    /// As <see cref="CreateEvent"/>, but null with the reason in words when no card can be created: an
    /// Origins house whose every reachable hall a ban has closed has nowhere to put a night (it never gets a
    /// card with no venue). Today's mode always creates the card.
    /// </summary>
    public Event? TryCreateEvent(World world, DateOnly date, out string? refused, string? name = null,
        EventTier tier = EventTier.FightNight, CityId? city = null)
    {
        refused = null;
        // An observer has no house to book for: no card is ever created under the empty id.
        if (NoHouse(world, out refused)) return null;
        var promo = world.Get(world.PlayerPromotion);
        int number = NextCardNumber(world, promo);
        bool origins = world.Mode == WorldMode.Origins && promo is not null;
        if (!origins && promo is not null && city is { } want && !Org.OrgReach.CanBookIn(world, promo, want))
            city = promo.HomeCity;
        if (promo is not null && tier > Org.OrgPerks.MaxEventTier(promo))
            tier = Org.OrgPerks.MaxEventTier(promo);   // a city outfit can't stage a PPV
        // An Origins house books as its AI nights do: its own room by default (exile when a ban has closed it),
        // or a room its size in a city he names that has a scene (awake or up for grabs) and no ban.
        var venue = promo is null ? null
            : origins ? Living.OriginsShows.PlayerVenue(world, promo, date, city == promo!.HomeCity ? null : city)
            : city is { } chosen ? Geography.WorldMap.PickVenueIn(world, promo, chosen)
            : Geography.WorldMap.PickVenue(world, promo);
        if (origins && venue is null)
        {
            refused = "Every hall you could take that night to is closed by a ban. Win a licence (it needs rounds, judges and gloves), wait for the ban to lapse, or name a city abroad with a scene.";
            return null;
        }
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
        if (NoHouse(world, out error)) return false;
        if (IsBracket(ev, out error)) return false;
        var r = world.Get(red);
        var b = world.Get(blue);
        if (r is null || b is null) { error = "Fighter not found."; return false; }
        if (red == blue) { error = "A fighter can't fight themselves."; return false; }
        if (r.Contract?.Promotion != world.PlayerPromotion || b.Contract?.Promotion != world.PlayerPromotion)
        { error = "Both fighters must be on your roster."; return false; }
        if (Contracts.DealWindow.OutOfDeal(world, r) || Contracts.DealWindow.OutOfDeal(world, b))
        { error = "A fighter's deal is up — re-sign them before you book them."; return false; }
        // An open-weight house (Origins) matches any man against any man; a house split into weight bands
        // (Origins) matches men of one band; everyone else keeps to the class.
        bool open = OpenWeight(world);
        var band = open ? null : BandOf(world, r);
        if (!open && r.WeightClass != b.WeightClass && (band is null || !band.Matches(b)))
        { error = band is null ? "Fighters must be in the same weight class." : "Fighters must be in the same division."; return false; }
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
            WeightClass = open ? Division.OpenClass : band?.WeightClass ?? r.WeightClass,
            OpenWeight = open,
            ScheduledRounds = rounds,
            IsTitleFight = titleFight,
            Title = title,
        });
        Living.LocalCircuit.YieldTo(world, new[] { red, blue });   // your booking beats a staged club card
        return true;
    }

    /// <summary>The words an observer (no promotion of his own) hears from every booking mutator.</summary>
    public const string NoHouseText = "You don't run a promotion yet. Found one to book fights.";

    /// <summary>No house of the player's (<see cref="World.HasPlayer"/> false): refuse, with the reason.</summary>
    internal static bool NoHouse(World world, out string? error)
    {
        error = world.HasPlayer ? null : NoHouseText;
        return error is not null;
    }

    /// <summary>A one-night tournament (Origins) has no card to book by hand: its bracket is drawn on the night.</summary>
    internal static bool IsBracket(Event ev, out string? error)
    {
        error = ev.Format == EventFormat.Tournament
            ? "A tournament night has no card to book: the bracket is drawn from your roster on the night." : null;
        return error is not null;
    }

    /// <summary>Does the player's house fight at open weight (an Origins house rule; never in today's mode)?</summary>
    private static bool OpenWeight(World world) => world.Get(world.PlayerPromotion)?.Rules.Fight?.OpenWeight == true;

    /// <summary>The player's BANDED division this man fights in (Origins, after a weight split); null otherwise (always in today's mode).</summary>
    private static Division? BandOf(World world, Fighter f) =>
        world.Mode != WorldMode.Origins ? null
        : world.Get(world.PlayerPromotion)?.Divisions.FirstOrDefault(d => d.Lightest is not null && d.Matches(f));

    /// <summary>Is this man in the player's division filed under this class (a band takes several natural classes)?</summary>
    private static bool InBand(World world, Fighter f, WeightClass wc) => BandOf(world, f)?.WeightClass == wc;

    /// <summary>
    /// Book one of your fighters against unnamed local talent — the small-org
    /// special (City/Regional reach only). Cheap, easy, builds a prospect up;
    /// the local is minted from the host city's club scene.
    /// </summary>
    public bool AddLocalBout(World world, Event ev, FighterId red, PrimaryStyle style, int rounds, out string? error)
    {
        error = null;
        if (NoHouse(world, out error)) return false;
        if (IsBracket(ev, out error)) return false;
        var promo = world.Get(world.PlayerPromotion);
        var r = world.Get(red);
        if (promo is null || r is null) { error = "Fighter not found."; return false; }
        if (Org.OrgSizing.SizeOf(promo) > OrgSize.Regional)
        { error = "Local fights are beneath the promotion now — book real opponents."; return false; }
        if (r.Contract?.Promotion != world.PlayerPromotion) { error = "The fighter must be on your roster."; return false; }
        if (Contracts.DealWindow.OutOfDeal(world, r)) { error = "The fighter's deal is up — re-sign them before you book them."; return false; }
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
        Living.LocalCircuit.YieldTo(world, new[] { red });
        return true;
    }

    public void RemoveBout(Event ev, FightId fight) => RemoveBout(null, ev, fight);

    /// <summary>Remove a bout; with a world, an unfought local opponent evaporates back into the scene.</summary>
    public void RemoveBout(World? world, Event ev, FightId fight)
    {
        if (world is not null && !world.HasPlayer) return;   // an observer edits nobody's card
        var removed = ev.Card.Where(f => f.Id == fight).ToList();
        ev.Card.RemoveAll(f => f.Id == fight);
        if (world is not null)
            foreach (var f in removed)
                ReleaseLocals(world, f);
    }

    public void CancelEvent(World world, Event ev)
    {
        if (!world.HasPlayer) return;   // an observer cancels nobody's card
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
            .Where(f => (f.WeightClass == weightClass || OpenWeight(world) || InBand(world, f, weightClass)) && f.Sex == sex && f.IsMatchReady(ev.Date) && !BookedOnCard(ev, f.Id)
                        && !Contracts.DealWindow.OutOfDeal(world, f))
            .OrderByDescending(f => MatchmakingAdvisor.ScoutedRead(world, f))   // the scout's read, never the truth
            .ThenBy(f => f.Id.Value)
            .ToList();

    /// <summary>Eligible opponents for a chosen fighter (same class AND sex, available, not already booked).</summary>
    public IReadOnlyList<Fighter> EligibleOpponents(World world, Event ev, FighterId forFighter)
    {
        var f = world.Get(forFighter);
        if (f is null) return Array.Empty<Fighter>();
        return world.RosterOf(world.PlayerPromotion)
            .Where(o => o.Id != forFighter && (o.WeightClass == f.WeightClass || OpenWeight(world) || BandOf(world, f)?.Matches(o) == true) && o.Sex == f.Sex && o.IsMatchReady(ev.Date) && !BookedOnCard(ev, o.Id)
                        && !Contracts.DealWindow.OutOfDeal(world, o))
            .OrderByDescending(o => MatchmakingAdvisor.ScoutedRead(world, o))   // the scout's read, never the truth
            .ThenBy(o => o.Id.Value)
            .ToList();
    }

    /// <summary>The player's scheduled cards this fighter is booked on (date order).</summary>
    public static IReadOnlyList<Event> CardsBooking(World world, FighterId fighter) =>
        world.Events.Values
            .Where(e => world.HasPlayer && e.Promotion == world.PlayerPromotion && e.Status == EventStatus.Scheduled
                        && world.Get(e.Promotion) is not null && BookedOnCard(e, fighter))
            .OrderBy(e => e.Date).ThenBy(e => e.Id.Value)
            .ToList();

    public IEnumerable<Event> UpcomingEvents(World world) =>
        world.Events.Values
            .Where(e => world.HasPlayer && e.Promotion == world.PlayerPromotion && e.Status == EventStatus.Scheduled)
            .OrderBy(e => e.Date)
            .ThenBy(e => e.Id.Value);

    private static bool BookedOnCard(Event ev, FighterId id) =>
        ev.Card.Any(f => f.RedCorner == id || f.BlueCorner == id);

    // Committed to any other scheduled card, the AI's announced cards included.
    private static bool BookedElsewhere(World world, Event ev, FighterId id) =>
        Living.ForwardCalendar.IsCommitted(world, id, ev.Id);
}
