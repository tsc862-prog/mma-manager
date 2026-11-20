using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Booking;

/// <summary>
/// How an AI house in today's sport picks the room for a card in a given city: it reads the crowd the card will
/// draw off the box office's own model (<see cref="Financials.Demand"/>: the house's prestige and following in the
/// city and region, the headliners' popularity and pull, the belt, the feud, the market; all public facts, never
/// ability), then takes the room that nets the most at the door (<see cref="Financials.DoorNet"/>: the seats it can
/// sell against its rent and production) among the rooms that crowd fills to at least <see cref="MinFill"/>.
/// <para>The fill floor is there because a room is also a statement: a half-empty arena costs standing
/// (<c>Org.Standing</c> scores the fill), and the night's demand is an estimate. When the crowd fills no room that
/// far, the house takes the smallest room in town. Ties go to the smaller room, then the lower venue id. (A floor of
/// three quarters was tried first: a town's rooms jump from about 1,000 to 3,500 to 14,000 seats, so a Regional house
/// drawing 2,000-2,600 went back to the 1,000-seat hall, startups' gate per card fell by 30% and 51 Regional houses
/// went broke and were sold in 100 seeds x 5 years, against none on main.)</para>
/// <para>This replaced <c>WorldMap.PickVenueIn</c> for AI cards in today's mode. That estimate used a flat 900 for
/// the headliner and left out the market multipliers, so the giants sat in rooms half their crowd's size (the AFC's
/// mean room was 12,750 against 26,800 who wanted in). The player's house and Origins houses do not come here: the
/// player picks his own room, and Origins keeps its own rooms.</para>
/// </summary>
public static class RoomFit
{
    /// <summary>The least share of a room the night's crowd must fill for the house to book it (unless it fits no room that well).</summary>
    public const double MinFill = 0.5;

    /// <summary>Whether this house's cards are fitted here: an AI house in today's mode.</summary>
    public static bool AppliesTo(World world, Promotion promo) =>
        world.Mode != WorldMode.Origins && promo.Id != world.PlayerPromotion;

    /// <summary>
    /// The room in <paramref name="city"/> for <paramref name="ev"/> as it stands (main event known; the undercard
    /// does not move the crowd). Null when the city has no rooms.
    /// </summary>
    public static Venue? Pick(World world, Event ev, CityId city)
    {
        var rooms = world.Venues.Values.Where(v => v.City == city).OrderBy(v => v.Id.Value).ToList();
        if (rooms.Count == 0) return null;
        // The night's own model. In the card's own city that is exactly what the box office will compute (the
        // clash with a rival's card that week included); in another city, the same card staged there.
        int demand = ev.VenueId is { } vid && world.Get(vid) is { } at && at.City == city
            ? Financials.Demand(world, ev)
            : Financials.DemandAt(world, ev, rooms[0]);
        return Choose(world, ev, rooms, demand);
    }

    /// <summary>The rule on its own: the best-netting room the crowd fills to <see cref="MinFill"/>, else the smallest.</summary>
    internal static Venue? Choose(World world, Event ev, IReadOnlyList<Venue> rooms, int demand)
    {
        var fills = rooms.Where(v => demand >= MinFill * v.Capacity).ToList();
        if (fills.Count == 0)
            return rooms.OrderBy(v => v.Capacity).ThenBy(v => v.Id.Value).FirstOrDefault();
        return fills
            .Select(v => (Room: v, Net: Financials.DoorNet(world, ev, v, demand)))
            .OrderByDescending(x => x.Net).ThenBy(x => x.Room.Capacity).ThenBy(x => x.Room.Id.Value)
            .First().Room;
    }
}
