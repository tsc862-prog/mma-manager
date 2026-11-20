using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Generation;
using MmaManager.Core.Sim.Geography;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Tests;

/// <summary>
/// An AI house in today's mode books the room its card's crowd fills (<see cref="RoomFit"/>): it reads the box
/// office's own demand for the card and takes the best-netting room the crowd fills to three quarters, else the
/// smallest. Before this rule the pick (<see cref="WorldMap.PickVenueIn"/>) estimated the crowd with a flat 900 for
/// the headliner and no market multipliers, and the flagship played a 12,750-seat room against 26,800 who wanted in
/// (longsim, 200 seeds). The player's house and Origins houses keep their old rooms.
/// </summary>
public class RoomFitTests
{
    private static World Build(ulong seed) => new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });

    private static Promotion House(World w, string abbr) => w.Promotions.Values.Single(p => p.Abbreviation == abbr);

    private static Venue Room(World w, CityId city, int capacity, decimal rentPerSeat = 9m) =>
        new() { Id = w.NewVenueId(), Name = $"Room {capacity}", City = city, Capacity = capacity, Rent = new Money(capacity * rentPerSeat) };

    private static Event Card(World w, Promotion p) => new()
    {
        Id = w.NewEventId(), Promotion = p.Id, Name = "Test Night", Date = w.CurrentDate.AddDays(30), Region = p.BaseRegion,
    };

    // Two of the house's own men in one division, made as popular as asked.
    private static Matchup Headliner(World w, Promotion p, int pop)
    {
        foreach (var d in p.Divisions.OrderBy(d => d.Sex).ThenBy(d => d.WeightClass))
        {
            var pair = w.RosterOf(p.Id).Where(f => d.Matches(f) && f.Status == FighterStatus.Active)
                .OrderBy(f => f.Id.Value).Take(2).ToList();
            if (pair.Count < 2) continue;
            foreach (var f in pair) f.Intangibles.Popularity = pop;
            return new Matchup(pair[0].Id, pair[1].Id, d.WeightClass, 5, false, null);
        }
        throw new InvalidOperationException("no pair");
    }

    [Fact]
    public void A25kDraw_TakesThe25kRoom_NotThe12kRoom()
    {
        var w = Build(4);
        var afc = House(w, "AFC");
        var city = afc.HomeCity!.Value;
        var small = Room(w, city, 12_000);
        var big = Room(w, city, 25_000);
        var ev = Card(w, afc);

        Assert.Same(big, RoomFit.Choose(w, ev, new[] { small, big }, 25_000));
        Assert.Same(big, RoomFit.Choose(w, ev, new[] { big, small }, 26_800));   // more than either holds: the bigger one
        // The big room pays: its extra 13,000 seats are worth more than its extra rent.
        Assert.True(Financials.DoorNet(w, ev, big, 25_000) > Financials.DoorNet(w, ev, small, 25_000));
    }

    [Fact]
    public void ASmallHouse_IsNotMovedToABarn()
    {
        var w = Build(4);
        var small = w.Promotions.Values.Where(p => !p.Defunct && p.Id != w.PlayerPromotion && p.HomeCity is not null)
            .OrderBy(p => p.Prestige.Value).ThenBy(p => p.Id.Value).First();
        var city = small.HomeCity!.Value;
        var hall = Room(w, city, 1_000);
        var forum = Room(w, city, 3_500);
        var arena = Room(w, city, 18_000);
        var rooms = new[] { arena, forum, hall };
        var ev = Card(w, small);

        Assert.Same(hall, RoomFit.Choose(w, ev, rooms, 300));      // fills nothing to three quarters: the smallest room
        Assert.Same(hall, RoomFit.Choose(w, ev, rooms, 2_000));    // the forum would be 57% full
        Assert.Same(forum, RoomFit.Choose(w, ev, rooms, 3_000));   // the forum at 86% nets more than the full hall
        Assert.Same(forum, RoomFit.Choose(w, ev, rooms, 12_000));  // the arena would be two-thirds empty
    }

    [Fact]
    public void ATie_GoesToTheSmallerRoom_ThenTheLowerId()
    {
        var w = Build(4);
        var afc = House(w, "AFC");
        var city = afc.HomeCity!.Value;
        var a = Room(w, city, 10_000);
        var b = Room(w, city, 10_000);
        var ev = Card(w, afc);
        Assert.Same(a, RoomFit.Choose(w, ev, new[] { b, a }, 20_000));
    }

    [Fact]
    public void AnAiCard_IsStagedInTheRoomItsOwnDemandFits()
    {
        var w = Build(4);
        w.PlayerPromotion = default;   // the default build hands the player the AFC: an all-AI world, as longsim runs it
        var afc = House(w, "AFC");
        Assert.True(RoomFit.AppliesTo(w, afc));
        var city = afc.HomeCity!.Value;
        var ev = new EventService().CreateEvent(w, afc.Id, "AFC Test Night", w.CurrentDate.AddDays(30),
            new[] { Headliner(w, afc, 100) }, city);

        var room = w.Get(ev.VenueId!.Value)!;
        Assert.Equal(city, room.City);
        Assert.Equal(room.Name, ev.Venue);
        var rooms = w.Venues.Values.Where(v => v.City == city).OrderBy(v => v.Id.Value).ToList();
        Assert.Same(RoomFit.Choose(w, ev, rooms, Financials.Demand(w, ev)), room);
        // The flagship with two 100-popularity headliners fills its home town's arena (the old pick put it in a
        // 4,080-seat hall with 16,700 wanting in).
        Assert.Equal(rooms.Max(v => v.Capacity), room.Capacity);
        Assert.True(Financials.Demand(w, ev) >= RoomFit.MinFill * room.Capacity);
        Assert.True(WorldMap.PickVenueIn(w, afc, city)!.Capacity < room.Capacity);
    }

    [Fact]
    public void ThePlayersHouse_KeepsTheOldPick()
    {
        var w = Build(4);
        var mine = w.Get(w.PlayerPromotion)!;
        Assert.False(RoomFit.AppliesTo(w, mine));
        var city = mine.HomeCity ?? WorldMap.AnchorCity(w, mine.BaseRegion)!.Id;
        var expected = WorldMap.PickVenueIn(w, mine, city);
        var ev = new EventService().CreateEvent(w, mine.Id, "Player Night", w.CurrentDate.AddDays(30),
            new[] { Headliner(w, mine, 100) }, city);
        Assert.Equal(expected?.Id, ev.VenueId);

        // ...and fight night does not move his room either.
        var booking = new PlayerBooking();
        var own = booking.CreateEvent(w, w.CurrentDate.AddDays(40), city: city);
        var smallest = w.Venues.Values.Where(v => v.City == city).OrderBy(v => v.Capacity).ThenBy(v => v.Id.Value).First();
        own.VenueId = smallest.Id; own.Venue = smallest.Name;
        ForwardCalendar.GuardAtRun(w, own);
        Assert.Equal(smallest.Id, own.VenueId);
    }

    [Fact]
    public void AnOriginsHouse_KeepsTheOldPick()
    {
        var w = OriginsWorldTests.AiWorld(5);
        var house = w.Promotions.Values.Where(p => !p.Defunct && p.HomeCity is not null).OrderBy(p => p.Id.Value).First();
        Assert.False(RoomFit.AppliesTo(w, house));
        var city = house.HomeCity!.Value;
        var expected = WorldMap.PickVenueIn(w, house, city);
        var pair = w.RosterOf(house.Id).OrderBy(f => f.Id.Value).Take(2).ToList();
        var ev = new EventService().CreateEvent(w, house.Id, "Night", w.CurrentDate.AddDays(30),
            new[] { new Matchup(pair[0].Id, pair[1].Id, pair[0].WeightClass, 3, false, null) }, city);
        Assert.Equal(expected?.Id, ev.VenueId);
    }
}
