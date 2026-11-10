using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Tests;

public class PlayerControlTests
{
    private static List<Fighter> PlayerPair(World world)
    {
        // Two available, prime-age fighters in the same division on the player roster.
        return world.RosterOf(world.PlayerPromotion)
            .Where(f => f.Status == FighterStatus.Active && f.CurrentInjury is null && f.AgeOn(world.CurrentDate) < 33)
            .GroupBy(f => f.WeightClass)
            .First(g => g.Count() >= 2)
            .Take(2).ToList();
    }

    [Fact]
    public void PlayerPromotion_IsNotAutoBooked()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 3 });
        var player = world.PlayerPromotion;

        new WorldTick().AdvanceWeeks(world, 20);

        // Pre-history cards exist for everyone; nothing NEW gets booked for the player.
        Assert.Equal(0, world.Events.Values.Count(e => e.Promotion == player && e.Date >= world.StartDate));
        Assert.True(world.Events.Values.Count(e => e.Promotion != player && e.Date >= world.StartDate) > 0);
    }

    [Fact]
    public void PlayerPromotion_IsNotAutoRestocked()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5 });
        var player = world.PlayerPromotion;
        var before = world.RosterOf(player).Select(f => f.Id).ToHashSet();

        new WorldTick().AdvanceWeeks(world, 30);

        // No fighter should have been auto-signed to the player (roster only shrinks via retirement).
        Assert.Empty(world.RosterOf(player).Select(f => f.Id).Where(id => !before.Contains(id)));
    }

    [Fact]
    public void PlayerScheduledEvent_RunsWhenDue_AndUpdatesRecords()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 7 });
        var booking = new PlayerBooking();
        var pair = PlayerPair(world);
        var (red, blue) = (pair[0], pair[1]);
        int redBefore = red.Record.TotalBouts;

        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(14), "Player Card");
        Assert.True(booking.AddBout(world, ev, red.Id, blue.Id, 3, false, out var err), err);
        Assert.Equal(EventStatus.Scheduled, ev.Status);

        new WorldTick().AdvanceWeeks(world, 4);   // 28 days, past the 14-day date

        Assert.Equal(EventStatus.Completed, ev.Status);
        Assert.NotNull(ev.Card[0].Outcome);
        Assert.Equal(redBefore + 1, red.Record.TotalBouts);
    }

    [Fact]
    public void Booking_RejectsInvalidBouts()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
        var booking = new PlayerBooking();
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(10));

        var groups = world.RosterOf(world.PlayerPromotion)
            .Where(f => f.Status == FighterStatus.Active && f.CurrentInjury is null)
            .GroupBy(f => f.WeightClass).ToList();
        var same = groups.First(g => g.Count() >= 2).Take(2).ToList();
        var other = groups.First(g => g.Key != same[0].WeightClass).First();

        Assert.False(booking.AddBout(world, ev, same[0].Id, same[0].Id, 3, false, out _));   // self vs self
        Assert.False(booking.AddBout(world, ev, same[0].Id, other.Id, 3, false, out _));      // cross weight class
        Assert.True(booking.AddBout(world, ev, same[0].Id, same[1].Id, 3, false, out _));     // valid
        Assert.False(booking.AddBout(world, ev, same[0].Id, same[1].Id, 3, false, out _));    // already booked
    }
}
