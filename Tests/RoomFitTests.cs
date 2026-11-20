using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Generation;
using MmaManager.Core.Sim.Geography;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Tests;

/// <summary>
/// An AI house in today's mode books the room its card's crowd fills (<see cref="RoomFit"/>): it reads the box
/// office's own demand for the card and takes the best-netting room the crowd fills to half, else the
/// smallest. Before this rule the pick (<see cref="WorldMap.PickVenueIn"/>) estimated the crowd with a flat 900 for
/// the headliner and no market multipliers, and the flagship played a 12,750-seat room against 26,800 who wanted in
/// (longsim, 200 seeds). The player's house is never refitted (his new card starts in the fitted room, which the Card
/// Desk marks, and only he moves it after that); Origins houses keep their old rooms.
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

        Assert.Same(hall, RoomFit.Choose(w, ev, rooms, 300));      // fills nothing to half: the smallest room
        Assert.Same(hall, RoomFit.Choose(w, ev, rooms, 1_500));    // the forum would be 43% full
        Assert.Same(forum, RoomFit.Choose(w, ev, rooms, 2_500));   // the forum at 71% nets more than the full hall
        Assert.Same(forum, RoomFit.Choose(w, ev, rooms, 8_000));   // the arena would be more than half empty
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

        // ...and fight night does not move his room either: his card, run through the same fight-night path as an
        // AI card's below, plays the club hall he left it in although the flagship's crowd would fill the arena.
        var own = new EventService().CreateEvent(w, mine.Id, "Player Night", w.CurrentDate.AddDays(3), ReadyBouts(w, mine, 3), city);
        var smallest = Smallest(w, city);
        own.VenueId = smallest.Id; own.Venue = smallest.Name;
        new WorldTick().RunEventLive(w, own, playByPlay: false);
        Assert.Equal(EventStatus.Completed, own.Status);
        Assert.Equal(smallest.Id, own.VenueId);
        Assert.Equal(smallest.Capacity, own.Financials!.Attendance);   // sold out: the crowd was there
    }

    private static Venue Smallest(World w, CityId city) =>
        w.Venues.Values.Where(v => v.City == city).OrderBy(v => v.Capacity).ThenBy(v => v.Id.Value).First();

    // Bouts of the house's own fit, free, signed men (none on another card), all made popular: a card the guard keeps.
    private static List<Matchup> ReadyBouts(World w, Promotion p, int n, int pop = 90)
    {
        var busy = ForwardCalendar.Committed(w);
        var bouts = new List<Matchup>();
        foreach (var d in p.Divisions.OrderBy(d => d.Sex).ThenBy(d => d.WeightClass))
        {
            var free = w.RosterOf(p.Id).Where(f => d.Matches(f) && f.Status == FighterStatus.Active && !busy.Contains(f.Id)
                                                 && f.Contract is { FightsRemaining: > 0 } && f.IsMatchReady(w.CurrentDate.AddDays(3)))
                .OrderBy(f => f.Id.Value).Take(2).ToList();
            if (free.Count < 2) continue;
            foreach (var f in free) { f.Intangibles.Popularity = pop; busy.Add(f.Id); }
            bouts.Add(new Matchup(free[0].Id, free[1].Id, d.WeightClass, 3, false, null));
            if (bouts.Count == n) return bouts;
        }
        throw new InvalidOperationException("not enough free pairs");
    }

    [Fact]
    public void FightNight_MovesAnAiCardOutOfARoomTooSmall_IntoTheFittedRoom()
    {
        // An announced AI card left in the club hall is moved on fight night to the room its card fits (the
        // arena, for the flagship with popular men on top), not to the old estimate's room (a 4,080-seat hall).
        var w = Build(4);
        w.PlayerPromotion = default;
        var afc = House(w, "AFC");
        var city = afc.HomeCity!.Value;
        var ev = new EventService().CreateEvent(w, afc.Id, "AFC Test Night", w.CurrentDate.AddDays(3), ReadyBouts(w, afc, 3), city);
        var smallest = Smallest(w, city);
        ev.VenueId = smallest.Id; ev.Venue = smallest.Name;
        var arena = w.Venues.Values.Where(v => v.City == city).OrderByDescending(v => v.Capacity).ThenBy(v => v.Id.Value).First();
        Assert.True(WorldMap.PickVenueIn(w, afc, city)!.Capacity < arena.Capacity);   // the old rule would not get there

        new WorldTick().RunEventLive(w, ev, playByPlay: false);

        Assert.Equal(EventStatus.Completed, ev.Status);
        Assert.Equal(arena.Id, ev.VenueId);
        Assert.Equal(arena.Name, ev.Venue);
        Assert.True(ev.Financials!.Attendance >= RoomFit.MinFill * arena.Capacity);
    }

    [Fact]
    public void ANightsTier_IsInThePrice_AndASuperfightIsFittedAtItsTier()
    {
        var w = Build(4);
        w.PlayerPromotion = default;
        var afc = House(w, "AFC");
        var city = afc.HomeCity!.Value;
        decimal price = Financials.TicketPrice(w, afc.Prestige.Value);
        // Two rooms whose rent gap sits between what 500 more seats are worth on a Fight Night and on a pay-per-view.
        var small = Room(w, city, 10_000, 0m);
        var big = new Venue { Id = w.NewVenueId(), Name = "Room 20000", City = city, Capacity = 20_000, Rent = new Money(500m * (1.15m * price - 14.4m)) };
        var night = Card(w, afc);
        var ppv = Card(w, afc);
        ppv.Tier = EventTier.Ppv;
        Assert.Same(small, RoomFit.Choose(w, night, new[] { small, big }, 10_500));
        Assert.Same(big, RoomFit.Choose(w, ppv, new[] { small, big }, 10_500));

        // The booking carries the tier into the fit (a superfight is created at its pay-per-view tier).
        var sf = new EventService().CreateEvent(w, afc.Id, "CHAMP VS CHAMP: Test", w.CurrentDate.AddDays(28),
            new[] { Headliner(w, afc, 95) }, city, tier: EventTier.Ppv);
        Assert.Equal(EventTier.Ppv, sf.Tier);
        var rooms = w.Venues.Values.Where(v => v.City == city).OrderBy(v => v.Id.Value).ToList();
        Assert.Same(RoomFit.Choose(w, sf, rooms, Financials.Demand(w, sf)), w.Get(sf.VenueId!.Value));
    }

    // ---- the player's default room and the Card Desk's marker -----------------------------------------------

    // A free man of the house (match-ready, in deal, on no card), made the biggest draw on the roster.
    private static Fighter MadeTheStar(World w, Promotion p, DateOnly date)
    {
        var busy = ForwardCalendar.Committed(w);
        var star = w.RosterOf(p.Id).Where(f => !f.LocalTalent && !busy.Contains(f.Id) && f.Contract is { FightsRemaining: > 0 }
                                              && f.IsMatchReady(date))
            .OrderBy(f => f.Id.Value).First();
        foreach (var f in w.RosterOf(p.Id)) f.Intangibles.Popularity = Math.Min(f.Intangibles.Popularity.Value, 60);
        star.Intangibles.Popularity = 100;
        return star;
    }

    [Fact]
    public void APlayersNewCard_StartsInTheFittedRoom_AndTheMarkerPointsToIt()
    {
        // The default build hands the player the AFC. With a 100-popularity man free to headline, his new card in his
        // home town starts in the room the sim fits an AI card with that man on top to: bigger than the old estimate's
        // room (a flat 900 for the headliner, no market), which put the flagship in a hall a fraction of its crowd.
        var w = Build(4);
        var mine = w.Get(w.PlayerPromotion)!;
        Assert.False(RoomFit.AppliesTo(w, mine));   // the player's cards are still never refitted by the sim
        var city = mine.HomeCity!.Value;
        var date = w.CurrentDate.AddDays(30);
        var star = MadeTheStar(w, mine, date);
        var booking = new PlayerBooking();
        var ev = booking.CreateEvent(w, date, city: city);

        var room = w.Get(ev.VenueId!.Value)!;
        Assert.Equal(city, room.City);
        Assert.Equal(room.Name, ev.Venue);
        // Exactly the AI's rule for the card with its likely headliner on top.
        var staged = new Event
        {
            Id = ev.Id, Promotion = ev.Promotion, Name = ev.Name, Date = ev.Date, VenueId = ev.VenueId, Region = ev.Region, Tier = ev.Tier,
            Card = { new Fight { Id = default, RedCorner = star.Id, BlueCorner = star.Id } },
        };
        Assert.Same(RoomFit.Pick(w, staged, city), room);
        Assert.True(WorldMap.PickVenueIn(w, mine, city)!.Capacity < room.Capacity);

        // The Card Desk marks that room as the sim's pick.
        var options = booking.VenueOptions(w, ev);
        Assert.Equal(ev.VenueId, Assert.Single(options, o => o.SimPick).Venue);
        Assert.Equal(ev.VenueId, Assert.Single(options, o => o.Selected).Venue);
    }

    [Fact]
    public void TheMarker_FollowsTheCard_ButTheCardsRoomNeverMovesByItself()
    {
        var w = Build(4);
        var mine = w.Get(w.PlayerPromotion)!;
        var city = mine.HomeCity!.Value;
        var booking = new PlayerBooking();
        var ev = booking.CreateEvent(w, w.CurrentDate.AddDays(10), city: city);
        var chosen = Smallest(w, city);
        Assert.True(booking.SetVenue(w, ev, chosen.Id, out var err), err);

        // A big main event on top: the marker moves to the room the card now fits; the card stays where he put it.
        foreach (var m in ReadyBouts(w, mine, 3, pop: 95))
            Assert.True(booking.AddBout(w, ev, m.Red, m.Blue, 3, false, out var why), why);
        var fit = RoomFit.Pick(w, ev, city)!;
        Assert.NotEqual(chosen.Id, fit.Id);
        Assert.Equal(fit.Id, Assert.Single(booking.VenueOptions(w, ev), o => o.SimPick).Venue);
        Assert.Equal(chosen.Id, ev.VenueId);

        // The weeks before (announcements, the weekly pass) and fight night leave his room alone.
        var tick = new WorldTick();
        for (int i = 0; i < 3 && ev.Status == EventStatus.Scheduled; i++)
        {
            tick.AdvanceWeek(w);
            Assert.Equal(chosen.Id, ev.VenueId);
            Assert.Equal(chosen.Name, ev.Venue);
        }
        Assert.Equal(EventStatus.Completed, ev.Status);
        Assert.Equal(chosen.Id, ev.VenueId);
        Assert.Equal(chosen.Capacity, ev.Financials!.Attendance);   // sold out: the crowd was there
    }

    [Fact]
    public void AnOriginsPlayersCard_KeepsItsOwnRoomAndMarker()
    {
        var w = new WorldBuilder().Build(WorldBuilder.Options.Origins(3, 0));
        var mine = w.Get(w.PlayerPromotion)!;
        var date = w.CurrentDate.AddDays(30);
        var expected = OriginsShows.PlayerVenue(w, mine, date, null);
        var booking = new PlayerBooking();
        var ev = booking.CreateEvent(w, date);
        Assert.Equal(expected?.Id, ev.VenueId);

        var city = w.Get(ev.VenueId!.Value)!.City;
        var marker = booking.VenueOptions(w, ev).Where(o => o.SimPick).Select(o => (VenueId?)o.Venue).SingleOrDefault();
        Assert.Equal(WorldMap.PickVenueIn(w, mine, city)?.Id, marker);
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
