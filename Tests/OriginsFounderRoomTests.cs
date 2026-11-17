using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Tests;

/// <summary>
/// A founder's room and purses follow the crowd it has seen (Origins tuning 2): the Crucible's authored hall holds
/// 4,000, and in a bad decade it drew 1,500-2,300 into it, paid the whole rent and budgeted its purses for a full
/// house (it folded in 2 of 40 seeds at 20 years). Once a founder has <see cref="OriginsShows.SeenNights"/> nights,
/// an AI founder takes a smaller room in its own town when that leaves more of the gate, and every founder prices
/// its men off the crowd it drew. Before its record, and for a house that fills its hall, nothing changes.
/// </summary>
public class OriginsFounderRoomTests
{
    private static Promotion House(World w, string abbr) => w.Promotions.Values.Single(p => p.Abbreviation == abbr);

    private static Venue Authored(World w, Promotion p) =>
        w.Venues.Values.Single(v => v.City == p.HomeCity && v.Name == MmaManager.Core.Sim.Generation.Origins.OriginsPromotions.All
            .Single(s => s.Abbreviation == p.Abbreviation).HomeVenue);

    private static List<Event> Nights(World w, Promotion p) =>
        w.Events.Values.Where(e => e.Promotion == p.Id && e.Status == EventStatus.Completed && e.Financials is not null).ToList();

    /// <summary>Every completed night of the house was in its hall and drew this crowd (seats left over: the crowd is what it saw).</summary>
    private static void Drew(World w, Promotion p, int crowd)
    {
        var hall = Authored(w, p);
        foreach (var e in Nights(w, p))
        {
            e.VenueId = hall.Id; e.Venue = hall.Name;
            e.Financials!.Attendance = crowd;
        }
    }

    private static World YearOn(ulong seed)
    {
        var w = OriginsWorldTests.AiWorld(seed);
        new WorldTick().AdvanceWeeks(w, 52);
        return w;
    }

    [Fact]
    public void AFounder_DrawingFarBelowItsHall_MovesToASmallerRoomInTown_AndPricesItsMenOffItsCrowd()
    {
        var w = YearOn(11);
        var cru = House(w, "CRU");
        var hall = Authored(w, cru);
        Assert.True(Nights(w, cru).Count >= OriginsShows.SeenNights, $"{Nights(w, cru).Count} nights");

        // Drawing nearly a full house: the hall is the best room, and the purse budget is the hall's.
        Drew(w, cru, hall.Capacity - 50);
        Assert.Equal(hall.Id, OriginsShows.HomeVenue(w, cru)!.Id);
        decimal fullCap = Negotiator.PurseCap(w, cru);

        // Drawing a third of it: a smaller room in Los Angeles leaves more of the gate, and the purses come down.
        int small = hall.Capacity / 3;
        var smaller = w.Venues.Values.Where(v => v.City == hall.City && v.Capacity < hall.Capacity && v.Capacity >= small).ToList();
        Assert.NotEmpty(smaller);
        Drew(w, cru, small);
        var room = OriginsShows.HomeVenue(w, cru)!;
        Assert.NotEqual(hall.Id, room.Id);
        Assert.Equal(hall.City, room.City);
        Assert.True(room.Capacity < hall.Capacity);
        Assert.True(Financials.RentOf(w, room).Amount < Financials.RentOf(w, hall).Amount);
        decimal thinCap = Negotiator.PurseCap(w, cru);
        Assert.True(thinCap < fullCap, $"cap {thinCap} with a third of a house, {fullCap} with a full one");
        OriginsShows.HomeRoom(w, cru, out int? seen);
        Assert.Equal(small, seen);
    }

    [Fact]
    public void AFounder_WithoutARecord_KeepsItsHall_AndThePlayerKeepsHisHallWhateverHeDraws()
    {
        var dayOne = OriginsWorldTests.AiWorld(11);
        foreach (var abbr in new[] { "CRU", "SFA", "DLB" })
        {
            var p = House(dayOne, abbr);
            Assert.Equal(Authored(dayOne, p).Id, OriginsShows.HomeVenue(dayOne, p)!.Id);
            OriginsShows.HomeRoom(dayOne, p, out int? none);
            Assert.Null(none);
        }

        var w = YearOn(11);
        var cru = House(w, "CRU");
        var hall = Authored(w, cru);
        Drew(w, cru, hall.Capacity / 3);
        Assert.NotEqual(hall.Id, OriginsShows.HomeVenue(w, cru)!.Id);   // the AI's house moves down...
        w.PlayerPromotion = cru.Id;
        cru.IsPlayerControlled = true;
        Assert.Equal(hall.Id, OriginsShows.HomeVenue(w, cru)!.Id);      // ...the player's stays in his hall (his own call)
        decimal thinCap = Negotiator.PurseCap(w, cru);
        Drew(w, cru, hall.Capacity - 50);
        Assert.True(thinCap < Negotiator.PurseCap(w, cru), "the player's purse budget reads his crowd too");
        Drew(w, cru, hall.Capacity / 3);
        OriginsShows.HomeRoom(w, cru, out int? seen);
        Assert.Equal(hall.Capacity / 3, seen);
    }

    [Fact]
    public void AFounder_ThatOutdrawsItsHall_NeverMovesToABiggerRoom()
    {
        var w = YearOn(11);
        foreach (var abbr in new[] { "CRU", "SFA", "DLB" })
        {
            var p = House(w, abbr);
            if (Nights(w, p).Count < OriginsShows.SeenNights) continue;
            var hall = Authored(w, p);
            foreach (var e in Nights(w, p)) e.Financials!.Attendance = w.Get(e.VenueId!.Value)!.Capacity;   // sold out: the queue counts
            var room = OriginsShows.HomeVenue(w, p)!;
            Assert.True(room.Capacity <= hall.Capacity, $"{abbr} moved from {hall.Capacity} to {room.Capacity} seats");
        }
    }

    [Fact]
    public void TodaysMode_FoundersRoomRuleNeverApplies()
    {
        var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 3 });
        foreach (var p in w.Promotions.Values)
        {
            OriginsShows.HomeRoom(w, p, out int? seen);
            Assert.Null(seen);
        }
    }
}
