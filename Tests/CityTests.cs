using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Geography;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Tests;

/// <summary>
/// City-level reputation: cities are the stored truth, regions are derived
/// rollups, fame lands where you fight, and geography is a booking decision.
/// </summary>
public class CityTests
{
    [Fact]
    public void TheWorld_HasSevenCitiesPerRegion_WithAnAnchorMetro()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5 });
        foreach (var region in Enum.GetValues<Region>())
        {
            var cities = world.Cities.Values.Where(c => c.Region == region).ToList();
            Assert.Equal(7, cities.Count);
            Assert.True(cities.Max(c => c.Market) >= 72, $"{region} needs an anchor metro");
            Assert.All(cities, c => Assert.Equal(3, world.Venues.Values.Count(v => v.City == c.Id)));   // hall, forum, arena
        }
        Assert.Equal(49, world.Cities.Count);
    }

    [Fact]
    public void EveryFighter_HasAHometown_InTheirHomeRegion()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5 });
        Assert.All(world.Fighters.Values, f =>
        {
            Assert.NotNull(f.HomeCity);
            Assert.Equal(f.Nationality, world.Get(f.HomeCity!.Value)!.Region);
        });
    }

    [Fact]
    public void RegionFame_IsTheMarketWeightedAverageOfItsCities()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5 });
        var f = world.Fighters.Values.First();
        f.CityReputation.Clear();

        var cities = world.Cities.Values.Where(c => c.Region == Region.Europe).ToList();
        foreach (var c in cities) f.CityReputation[c.Id] = 40;
        Assert.Equal(40, Reputation.FighterInRegion(world, f, Region.Europe));

        // Fame only in the anchor metro counts for more than fame in a small town.
        f.CityReputation.Clear();
        var anchor = cities.OrderByDescending(c => c.Market).First();
        var town = cities.OrderBy(c => c.Market).First();
        f.CityReputation[anchor.Id] = 60;
        int viaAnchor = Reputation.FighterInRegion(world, f, Region.Europe);
        f.CityReputation.Clear();
        f.CityReputation[town.Id] = 60;
        int viaTown = Reputation.FighterInRegion(world, f, Region.Europe);
        Assert.True(viaAnchor > viaTown, $"anchor fame {viaAnchor} should outweigh small-town fame {viaTown}");
    }

    [Fact]
    public void AnEvent_GrowsTheHostCity_MoreThanTheEcho()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
        var promo = world.Promotions.Values.First(p => p.Id != world.PlayerPromotion);
        var cities = world.Cities.Values.Where(c => c.Region == promo.BaseRegion).ToList();
        var host = cities[3];
        var other = cities[4];
        int hostBefore = Reputation.OrgInCity(promo, host.Id);
        int otherBefore = Reputation.OrgInCity(promo, other.Id);

        WorldMap.GrowCityPopularity(world, promo, host.Id, starPower: 80, attendance: 12_000);

        int hostGain = Reputation.OrgInCity(promo, host.Id) - hostBefore;
        int otherGain = Reputation.OrgInCity(promo, other.Id) - otherBefore;
        Assert.True(hostGain > 0);
        Assert.True(hostGain > otherGain, $"host +{hostGain} should beat the regional echo +{otherGain}");
    }

    [Fact]
    public void BrandGrowth_IsRelativeToCurrentStanding()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
        var promo = world.Promotions.Values.First(p => p.Id != world.PlayerPromotion);
        promo.Prestige = 10;   // a small brand, so growth rides on the card's draw, not the org's name
        var host = world.Cities.Values.First(c => c.Region == promo.BaseRegion).Id;

        int GainAtStanding(int standing)
        {
            promo.PopularityByCity[host] = standing;
            int before = Reputation.OrgInCity(promo, host);
            WorldMap.GrowCityPopularity(world, promo, host, starPower: 30, attendance: 6_000);
            return Reputation.OrgInCity(promo, host) - before;
        }

        int low = GainAtStanding(2);    // lots of headroom below the card's draw
        int high = GainAtStanding(26);  // nearly caught up to it
        int over = GainAtStanding(45);  // already a bigger name than the card

        Assert.True(low > high, $"a smaller brand grows faster off the same card ({low} vs {high})");
        Assert.Equal(0, over);          // a card beneath the org's standing wins no new fans
    }

    [Fact]
    public void MmaInterest_IsSeeded_WithASpread_AcrossCitiesAndRegions()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
        var interests = world.Cities.Values.Select(c => c.MmaInterest).ToList();
        Assert.True(interests.Distinct().Count() > 5, "cities should have varied MMA interest, not one value");
        Assert.All(interests, i => Assert.InRange(i, 0, 100));
        // Region interest is the market-weighted average of its cities — derived, in range.
        foreach (Region r in Enum.GetValues<Region>())
            Assert.InRange(MmaInterest.InRegion(world, r), 0, 100);
    }

    [Fact]
    public void AHotMarket_DrawsABiggerHouse_ThanAColdOne()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
        var icf = world.Promotions.Values.First(p => p.Abbreviation == "ICF");
        var card = new Matchmaker().BuildCard(world, icf.Id, targetFights: 6);
        var ev = new EventService().CreateEvent(world, icf.Id, "Card", world.CurrentDate, card);
        var city = world.Get(WorldMap.CityOf(world, ev)!.Value)!;

        city.MmaInterest = 95;
        int hot = Financials.Compute(world, ev).Attendance;
        city.MmaInterest = 30;
        int cold = Financials.Compute(world, ev).Attendance;

        Assert.True(hot > cold, $"a fight-mad market should out-draw a cold one ({hot} vs {cold})");
    }

    [Fact]
    public void Interest_DecaysBackTowardItsBaseline()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
        var city = world.Cities.Values.First();
        int baseline = city.MmaInterestBase;
        city.MmaInterest = Math.Min(100, baseline + 20);   // a temporary boost

        MmaInterest.Tick(world);

        Assert.True(city.MmaInterest < baseline + 20, "interest should decay back toward baseline");
        Assert.True(city.MmaInterest >= baseline, "but never below the cultural floor");
    }

    [Fact]
    public void ANationalHero_LiftsTheirHomeRegionsInterest()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
        var org = world.Promotions.Values.First(p =>
            MmaManager.Core.Sim.Org.OrgSizing.SizeOf(p) >= OrgSize.National && !p.Defunct);
        var hero = world.RosterOf(org.Id).First(f => f.Status == FighterStatus.Active);
        hero.Nationality = Region.MiddleEast;    // a home in a cooler market
        hero.Intangibles.Popularity = 90;        // a genuine national star
        int before = MmaInterest.InRegion(world, Region.MiddleEast);

        for (int m = 0; m < 12; m++) MmaInterest.Tick(world);

        Assert.True(MmaInterest.InRegion(world, Region.MiddleEast) > before,
            "a homegrown star should lift interest in their home region");
    }

    [Fact]
    public void ABigOrgShow_GrowsInterestInTown()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
        var afc = world.Promotions.Values.First(p => p.Abbreviation == "AFC");   // International
        var card = new Matchmaker().BuildCard(world, afc.Id, targetFights: 6);
        var ev = new EventService().CreateEvent(world, afc.Id, "Card", world.CurrentDate, card);
        var host = WorldMap.CityOf(world, ev)!.Value;
        world.Cities[host].MmaInterest = 30;   // a genuinely cool market the big show can convert
        int before = MmaInterest.InCity(world, host);

        new EventService().RunEvent(world, ev);

        Assert.True(MmaInterest.InCity(world, host) > before, "a big promotion's show should grow the sport in a cool town");
    }

    [Fact]
    public void ACrossover_LiftsRegionalInterest()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5, PlayerPromotionIndex = 8 });
        // The first crossover fires at month 6 (July 2026); it lands in Europe.
        int before = MmaInterest.InRegion(world, Region.Europe);
        MmaManager.Core.Sim.Living.WorldEvents.RunMonthly(world, new DateOnly(2026, 7, 1),
            new MmaManager.Core.Sim.Rng.SimRandom(1234));
        Assert.True(MmaInterest.InRegion(world, Region.Europe) >= before,
            "a crossover should not lower regional interest");
    }

    [Fact]
    public void SharingARegionWithAPeer_ShrinksTheHouse()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
        var icf = world.Promotions.Values.First(p => p.Abbreviation == "ICF");   // alone in Asia at build
        var card = new Matchmaker().BuildCard(world, icf.Id, targetFights: 6);
        var ev = new EventService().CreateEvent(world, icf.Id, "Card", world.CurrentDate, card);
        int solo = Financials.Compute(world, ev).Attendance;

        // Drop a same-tier rival into ICF's backyard — the gate splits.
        world.Promotions.Values.First(p => p.Abbreviation == "VMMA").BaseRegion = icf.BaseRegion;
        int contested = Financials.Compute(world, ev).Attendance;

        Assert.True(contested < solo, $"a contested market draws a smaller house ({contested} vs {solo})");
    }

    [Fact]
    public void AnIdleOrg_FadesFromView()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5 });
        var promo = world.Get(world.PlayerPromotion)!;   // player org — never auto-booked, so it can go idle
        var city = world.Cities.Values.First(c => c.Region == promo.BaseRegion).Id;
        Reputation.BumpOrg(world, promo, city, 12);   // an established brand in this city

        // One card so the org has a last-event date, then it goes dark for months.
        new EventService().BookAndRun(world, promo.Id, "Opener", world.CurrentDate, new Matchmaker());
        int before = Reputation.OrgInCity(promo, city);

        new WorldTick().AdvanceWeeks(world, 20);   // ~4 idle months, no shows booked

        Assert.True(Reputation.OrgInCity(promo, city) < before,
            $"an idle promotion should fade (was {before})");
    }

    [Fact]
    public void HometownHero_LiftsTheHouse()
    {
        // Trim the org's draw (both worlds identically) so the house sits below
        // venue capacity — a sold-out building hides the lift being measured.
        var (world, promo, pair) = TopPair(seed: 5);
        promo.Prestige = 45;
        var svc = new EventService();
        var ev = svc.CreateEvent(world, promo.Id, "Away", world.CurrentDate,
            new[] { new Matchup(pair[0].Id, pair[1].Id, WeightClass.Lightweight, 5, false, null) });
        // Make sure neither man is at home for the baseline.
        var host = WorldMap.CityOf(world, ev)!.Value;
        var elsewhere = world.Cities.Values.First(c => c.Id != host && c.Region != world.Get(host)!.Region);
        pair[0].HomeCity = elsewhere.Id;
        pair[1].HomeCity = elsewhere.Id;
        svc.RunEvent(world, ev);

        var (world2, promo2, pair2) = TopPair(seed: 5);
        promo2.Prestige = 45;
        var svc2 = new EventService();
        var ev2 = svc2.CreateEvent(world2, promo2.Id, "Home", world2.CurrentDate,
            new[] { new Matchup(pair2[0].Id, pair2[1].Id, WeightClass.Lightweight, 5, false, null) });
        pair2[0].HomeCity = WorldMap.CityOf(world2, ev2);   // hometown headliner
        pair2[1].HomeCity = elsewhere.Id;
        svc2.RunEvent(world2, ev2);

        Assert.True(ev2.Financials!.Attendance > ev.Financials!.Attendance,
            $"hometown card {ev2.Financials.Attendance} should outdraw away card {ev.Financials.Attendance}");
    }

    [Fact]
    public void AiPromotions_TourTheirRegion()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 11 });
        new WorldTick().AdvanceWeeks(world, 40);

        var promo = world.Promotions.Values.First(p => p.Id != world.PlayerPromotion && !p.Defunct);
        var visited = world.Events.Values
            .Where(e => e.Promotion == promo.Id && e.Status == EventStatus.Completed)
            .Select(e => WorldMap.CityOf(world, e))
            .Where(c => c is not null)
            .Distinct()
            .Count();
        Assert.True(visited >= 3, $"expected a touring promotion, saw {visited} distinct host cities");
    }

    [Fact]
    public void ThePlayer_CanBookASpecificCity()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 13 });
        var away = world.Cities.Values.First(c => c.Region == Region.Asia);

        var ev = new PlayerBooking().CreateEvent(world, world.CurrentDate.AddDays(14), city: away.Id);

        Assert.Equal(away.Id, WorldMap.CityOf(world, ev));
        Assert.Equal(Region.Asia, ev.Region);
    }

    private static (World World, Promotion Promo, List<Fighter> Pair) TopPair(ulong seed)
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed, UseDesignedRoster = false });
        var promo = world.Promotions.Values.OrderByDescending(p => p.Prestige.Value).First();
        var pair = world.RosterOf(promo.Id)
            .Where(f => f.WeightClass == WeightClass.Lightweight && f.Status == FighterStatus.Active)
            .Take(2).ToList();
        return (world, promo, pair);
    }
}
