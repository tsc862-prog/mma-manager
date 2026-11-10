using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Geography;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Tests;

public class GeographyTests
{
    [Fact]
    public void Build_PopulatesCitiesAndVenuesInEveryRegion()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 3 });

        Assert.NotEmpty(world.Cities);
        Assert.NotEmpty(world.Venues);
        foreach (var region in Enum.GetValues<Region>())
            Assert.Contains(world.Cities.Values, c => c.Region == region);
        Assert.All(world.Venues.Values, v => Assert.True(world.Cities.ContainsKey(v.City)));
    }

    [Fact]
    public void BookedEvent_GetsAVenue_AndAttendanceIsCappedByCapacity()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 3 });
        var promo = world.Promotions.Values.OrderByDescending(p => p.Prestige.Value).First();

        var ev = new EventService().BookAndRun(world, promo.Id, "Card", world.CurrentDate, new Matchmaker());

        Assert.NotNull(ev.VenueId);
        var venue = world.Get(ev.VenueId!.Value)!;
        Assert.True(ev.Financials!.Attendance <= venue.Capacity);
    }

    [Fact]
    public void RunningShows_GrowsRegionalPopularity()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 3 });
        var promo = world.Promotions.Values.OrderByDescending(p => p.Prestige.Value).First();
        var region = promo.BaseRegion;
        int before = MmaManager.Core.Sim.Geography.Reputation.OrgInRegion(world, promo, region);

        var svc = new EventService();
        for (int i = 0; i < 3; i++)
            svc.BookAndRun(world, promo.Id, $"Card {i}", world.CurrentDate.AddDays(i), new Matchmaker());

        Assert.True(MmaManager.Core.Sim.Geography.Reputation.OrgInRegion(world, promo, region) >= before);
        Assert.Contains(promo.PopularityByCity.Values, v => v.Value > 0);
    }
}
