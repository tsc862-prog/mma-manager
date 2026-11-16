using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Tests;

/// <summary>
/// The young sport's money (Origins tuning): a 1993 room's rent, the room a house picks for the crowd it has
/// seen, and what a one-night bracket pays its men. Today's mode keeps its rents.
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
    public void ABracketNight_PaysEachManHisPurseOnce_AndTheChequeIsTheBonus()
    {
        var w = _year.World;
        var cru = House(w, "CRU");
        var night = w.Events.Values.Where(e => e.Promotion == cru.Id && e.Format == EventFormat.Tournament && e.Status == EventStatus.Completed)
            .OrderBy(e => e.Date).ThenBy(e => e.Id.Value).First();
        var men = night.Card.SelectMany(f => new[] { f.RedCorner, f.BlueCorner }).Distinct().ToList();
        Assert.True(night.Card.Count > men.Count / 2, "a bracket has men who fought more than once");
        decimal once = men.Sum(id => w.Get(id)?.Contract?.PursePerFight.Amount ?? 0m);
        Assert.Equal(once, Financials.Compute(w, night).FighterPayroll.Amount);
    }

    [Fact]
    public void AHouseThatSellsOut_WithAQueueOutside_TakesABiggerRoom()
    {
        // The Garage sells out its 1,200 every night with a crowd outside: in its own city's rooms it would
        // take a bigger one (the old rule could never step past a small hall to a forum three times its size).
        var w = _year.World;
        var dlb = House(w, "DLB");
        var nights = w.Events.Values.Where(e => e.Promotion == dlb.Id && e.Status == EventStatus.Completed).ToList();
        Assert.NotEmpty(nights);
        var garage = OriginsShows.HomeVenue(w, dlb)!;
        Assert.True(nights.Average(e => Financials.DemandAt(w, e, garage)) > garage.Capacity * 1.5);
        var room = OriginsShows.RoomIn(w, dlb, dlb.HomeCity!.Value)!;
        Assert.True(room.Capacity > garage.Capacity, $"{room.Name} holds {room.Capacity}");
    }

    [Fact]
    public void AHouseWithNoNightsYet_TakesASmallRoom_NotAnArena()
    {
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
