using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Tests;

/// <summary>
/// The young sport's money (Origins tuning): a 1993 room's rent (and the Card Desk showing it), the room a
/// house picks for the crowd it has seen, what a one-night bracket pays its men and takes off their deals,
/// and a bracket house's deals carrying no win bonus. Today's mode keeps its rents.
/// </summary>
public class OriginsMoneyTests : IClassFixture<OriginsYearFixture>
{
    private readonly OriginsYearFixture _year;
    public OriginsMoneyTests(OriginsYearFixture year) => _year = year;

    private static Promotion House(World w, string abbr) => w.Promotions.Values.Single(p => p.Abbreviation == abbr);

    [Fact]
    public void ARoom_InAnOriginsWorld_RentsAtA1993Price_AndTodaysRoomsKeepTheirRent()
    {
        var w = _year.DayOne;
        foreach (var v in w.Venues.Values)
        {
            var rent = Financials.RentOf(w, v).Amount;
            Assert.True(rent <= v.Capacity * Financials.OriginsRentPerSeat, $"{v.Name}: {rent} for {v.Capacity} seats");
            Assert.True(rent <= v.Rent.Amount);
        }
        // The founders' own halls were authored at the 1993 price: unchanged.
        foreach (var abbr in new[] { "CRU", "SFA", "DLB" })
        {
            var home = OriginsShows.HomeVenue(w, House(w, abbr))!;
            Assert.Equal(home.Rent, Financials.RentOf(w, home));
        }

        var today = new World();   // today's mode
        var room = new Venue { Id = new VenueId(1), Name = "Hall", City = new CityId(1), Capacity = 2_000, Rent = new Money(19_000m) };
        Assert.Equal(room.Rent, Financials.RentOf(today, room));
    }

    [Fact]
    public void TheCardDesk_InAnOriginsWorld_ShowsTheRentTheNightIsCharged()
    {
        var w = new WorldBuilder().Build(WorldBuilder.Options.Origins(5, 1));   // the player runs SFA (a card house)
        var sfa = w.Get(w.PlayerPromotion)!;
        var home = OriginsShows.HomeVenue(w, sfa)!;
        // An atlas room in his city (priced for today's sport), not his authored hall.
        var atlas = w.Venues.Values.Where(v => v.City == sfa.HomeCity && v.Id != home.Id).OrderBy(v => v.Id.Value).First();
        Assert.True(atlas.Rent.Amount > Financials.RentOf(w, atlas).Amount);

        var booking = new PlayerBooking();
        var ev = booking.CreateEvent(w, w.CurrentDate.AddDays(35));
        ev.VenueId = atlas.Id; ev.Venue = atlas.Name;
        var p = booking.Project(w, ev);
        Assert.Equal(Financials.RentOf(w, atlas), p.Rent);
        Assert.True(p.Production.Amount >= 0, $"production {p.Production}");
        Assert.Equal(Financials.Compute(w, ev).ProductionCost, p.Rent + p.Production);
        var option = booking.VenueOptions(w, ev).Single(o => o.Venue == atlas.Id);
        Assert.Equal(Financials.RentOf(w, atlas), option.Rent);
    }

    [Fact]
    public void ABracketNight_PaysEachManHisPurseOnce_AndTakesOneFightOffHisDeal()
    {
        var w = OriginsWorldTests.AiWorld(11);
        var cru = House(w, "CRU");
        var tick = new WorldTick();
        Event? night = null;
        Dictionary<FighterId, (DateOnly Signed, int Left, decimal Purse)> before = new();
        for (int i = 0; i < 20 && night is null; i++)
        {
            before = w.RosterOf(cru.Id).Where(f => f.Contract is not null)
                .ToDictionary(f => f.Id, f => (f.Contract!.SignedOn, f.Contract.FightsRemaining, f.Contract.PursePerFight.Amount));
            tick.AdvanceWeek(w);
            night = w.Events.Values.Where(e => e.Promotion == cru.Id && e.Format == EventFormat.Tournament && e.Status == EventStatus.Completed)
                .OrderBy(e => e.Date).ThenBy(e => e.Id.Value).FirstOrDefault();
        }
        Assert.NotNull(night);
        var bouts = night!.Card.SelectMany(f => new[] { f.RedCorner, f.BlueCorner }).GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());
        var men = bouts.Keys.ToList();
        Assert.Contains(bouts, kv => kv.Value >= 2);   // somebody fought more than once that night

        // The settled payroll is one purse a man, at the terms he fought under.
        decimal once = men.Sum(id => before.TryGetValue(id, out var b) ? b.Purse : 0m);
        Assert.True(once > 0);
        Assert.Equal(once, night.Financials!.FighterPayroll.Amount);

        // Each man's deal (the same deal, not re-signed since) lost one fight, however often he fought.
        int checkedMen = 0;
        foreach (var id in men.Where(id => bouts[id] >= 2))
        {
            var f = w.Get(id)!;
            if (!before.TryGetValue(id, out var b) || f.Contract is not { } c || c.SignedOn != b.Signed || b.Left <= 0) continue;
            Assert.Equal(b.Left - 1, c.FightsRemaining);
            checkedMen++;
        }
        Assert.True(checkedMen > 0, "no multi-bout man still on the deal he fought under");
    }

    [Fact]
    public void ABracketHouse_WritesNoWinBonus_AndValuesNone()
    {
        var day = _year.DayOne;
        var cru = House(day, "CRU");
        var sfa = House(day, "SFA");
        Assert.All(day.RosterOf(cru.Id), f => Assert.Equal(Money.Zero, f.Contract!.WinBonus));
        Assert.Contains(day.RosterOf(sfa.Id), f => f.Contract!.WinBonus.Amount > 0);

        // A year on (CRU still runs brackets in this world), every deal it has written carries none.
        var w = _year.World;
        var cruNow = House(w, "CRU");
        Assert.Equal(ShowFormat.OneNightTournament, cruNow.Rules.ShowFormat);
        Assert.All(w.RosterOf(cruNow.Id).Where(f => f.Contract is not null), f => Assert.Equal(Money.Zero, f.Contract!.WinBonus));

        // A bonus on the paper is worth nothing at the bracket house, half its face at a card house.
        var man = w.RosterOf(cruNow.Id).First(f => f.Contract is not null);
        ContractOffer At(Promotion p, decimal bonus) => new()
        {
            Fighter = man.Id, Promotion = p.Id, PursePerFight = new Money(2_000m), WinBonus = new Money(bonus), Fights = 3, Exclusive = true,
        };
        Assert.Equal(Negotiator.MoneyValue(w, At(cruNow, 0m)), Negotiator.MoneyValue(w, At(cruNow, 5_000m)));
        Assert.Equal(Negotiator.MoneyValue(w, At(House(w, "SFA"), 0m)) + 2_500m, Negotiator.MoneyValue(w, At(House(w, "SFA"), 5_000m)));
        var session = NegotiationSession.Resume(w, man, cruNow, new FighterValuation().Evaluate(w, man));
        Assert.Equal(Money.Zero, session.DemandWinBonus(exclusive: true, clause: false));
    }

    [Fact]
    public void AFoundedHouse_SellingOutAHall_StepsUpToTheForum_WhereTheOldRuleStayedPut()
    {
        var w = OriginsWorldTests.AiWorld(7);
        // A big atlas city with a hall and a forum where the old rule (sold out -> the room nearest twice the
        // crowd) stays in the hall: the forum is further from twice the hall than the hall itself is.
        var city = w.Cities.Values
            .Select(c => (City: c, Rooms: w.Venues.Values.Where(v => v.City == c.Id).OrderBy(v => v.Capacity).ThenBy(v => v.Id.Value).ToList()))
            .Where(x => x.Rooms.Count >= 3 && !w.Promotions.Values.Any(p => p.HomeCity == x.City.Id))
            .Where(x => Math.Abs(x.Rooms[1].Capacity - 2 * x.Rooms[0].Capacity) > x.Rooms[0].Capacity)
            .OrderByDescending(x => x.City.Market).ThenBy(x => x.City.Id.Value).First();
        var hall = city.Rooms[0];
        var forum = city.Rooms[1];

        var house = new Promotion
        {
            Id = w.NewPromotionId(), Name = "Test Fight Night", Abbreviation = "TFN", BaseRegion = city.City.Region,
            Balance = new Money(100_000m), Prestige = 6, Reputation = 50, HomeCity = city.City.Id, FoundedYear = 1995,
        };
        house.PopularityByCity[city.City.Id] = 70;   // a following that fills more than a hall
        w.Promotions[house.Id] = house;
        for (int i = 0; i < 3; i++)
        {
            var ev = new Event
            {
                Id = w.NewEventId(), Promotion = house.Id, Name = $"TFN {i + 1}", Date = w.CurrentDate.AddDays(-70 + 21 * i),
                VenueId = hall.Id, Venue = hall.Name, Region = city.City.Region, Status = EventStatus.Completed,
            };
            ev.Financials = new EventFinancials { Attendance = hall.Capacity };   // sold out
            w.Events[ev.Id] = ev;
        }
        Assert.True(w.Events.Values.Where(e => e.Promotion == house.Id).Average(e => Financials.DemandAt(w, e, hall)) > hall.Capacity * 1.5);

        var room = OriginsShows.HomeVenue(w, house)!;
        Assert.True(room.Capacity >= forum.Capacity, $"{room.Name} holds {room.Capacity}; the hall {hall.Capacity}, the forum {forum.Capacity}");
    }

    [Fact]
    public void AHouseWithNoNightsYet_TakesASmallRoom_NotAnArena_Guard()
    {
        // A guard, not a pin: before its first night a house expects a young sport's crowd (600 and 100 a
        // prestige point) and takes a room near that, never the arena (the old rule did the same).
        var w = _year.DayOne;
        var cru = House(w, "CRU");
        var city = w.Cities.Values.Where(c => c.Id != cru.HomeCity && w.Venues.Values.Count(v => v.City == c.Id) >= 3)
            .OrderByDescending(c => c.Market).ThenBy(c => c.Id.Value).First();
        var room = OriginsShows.RoomIn(w, cru, city.Id)!;
        int biggest = w.Venues.Values.Where(v => v.City == city.Id).Max(v => v.Capacity);
        Assert.True(room.Capacity < biggest, $"{room.Name} holds {room.Capacity}");
        Assert.True(room.Capacity <= 2 * (600 + 100 * cru.Prestige.Value), $"{room.Name} holds {room.Capacity}");
    }
}
