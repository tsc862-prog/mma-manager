using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Geography;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Org;

namespace MmaManager.Tests;

/// <summary>
/// The reach ladder: a promotion's tier is a rung of the real geography
/// (HQ city → in-country area → country → continent → world) that gates where
/// it can book, and the business unlocks (TV at National, PPV at Continental)
/// ride the same ladder.
/// </summary>
public class ReachTests
{
    [Fact]
    public void ReachClimbsTheMap_CityAreaCountryContinentWorld()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5 });
        var promo = world.Get(world.PlayerPromotion)!;
        promo.HomeCity = City(world, "Las Vegas").Id;   // USA · West

        CityId vegas = City(world, "Las Vegas").Id;
        CityId la = City(world, "Los Angeles").Id;      // USA · West
        CityId ny = City(world, "New York").Id;         // USA · Northeast
        CityId toronto = City(world, "Toronto").Id;     // Canada
        CityId london = City(world, "London").Id;       // Europe

        promo.Prestige = 10;    // City: the HQ metro only
        Assert.True(OrgReach.CanBookIn(world, promo, vegas));
        Assert.False(OrgReach.CanBookIn(world, promo, la));

        promo.Prestige = 25;    // Regional: the West
        Assert.True(OrgReach.CanBookIn(world, promo, la));
        Assert.False(OrgReach.CanBookIn(world, promo, ny));

        promo.Prestige = 35;    // National: the United States
        Assert.True(OrgReach.CanBookIn(world, promo, ny));
        Assert.False(OrgReach.CanBookIn(world, promo, toronto));

        promo.Prestige = 50;    // Continental: North America
        Assert.True(OrgReach.CanBookIn(world, promo, toronto));
        Assert.False(OrgReach.CanBookIn(world, promo, london));

        promo.Prestige = 75;    // International: the world
        Assert.True(OrgReach.CanBookIn(world, promo, london));
    }

    [Fact]
    public void BookingBeyondReach_FallsBackToTheHqMetro()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 7 });
        var promo = world.Get(world.PlayerPromotion)!;
        promo.Prestige = 10;    // City tier

        var away = City(world, "London");
        var ev = new PlayerBooking().CreateEvent(world, world.CurrentDate.AddDays(14), city: away.Id);

        Assert.Equal(promo.HomeCity, WorldMap.CityOf(world, ev));
    }

    [Fact]
    public void AiPromotions_TourOnlyWhereTheirReachAllows()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 11 });
        new WorldTick().AdvanceWeeks(world, 40);

        // CK (prestige 33) is a national Brazilian outfit: every card it ran is in Brazil.
        var ck = world.Promotions.Values.First(p => p.Abbreviation == "CK");
        Assert.Equal(OrgSize.National, OrgSizing.SizeOf(ck));
        var hosts = world.Events.Values
            .Where(e => e.Promotion == ck.Id && e.Status == EventStatus.Completed)
            .Select(e => WorldMap.CityOf(world, e))
            .Where(c => c is not null)
            .Select(c => world.Get(c!.Value)!)
            .ToList();
        Assert.NotEmpty(hosts);
        var brazil = WorldMap.CountryByCode(world, "BRA")!;
        Assert.All(hosts, c => Assert.Equal(brazil.Id, c.Country));
    }

    [Fact]
    public void TheEstablishedOrgs_SitWhereTheirIdentitySays()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5 });
        Assert.Equal(OrgSize.International, OrgSizing.SizeOf(Promo(world, "AFC")));
        Assert.Equal(OrgSize.Continental, OrgSizing.SizeOf(Promo(world, "VMMA")));
        Assert.Equal(OrgSize.Continental, OrgSizing.SizeOf(Promo(world, "ICF")));
        Assert.Equal(OrgSize.National, OrgSizing.SizeOf(Promo(world, "CK")));
    }

    [Fact]
    public void TheNetworks_ComeCallingAtNational_AndDropAFadedBrand()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options
        {
            Seed = 42,
            Founding = new WorldBuilder.FoundingSpec { Prestige = 15 },
        });
        var promo = world.Get(world.PlayerPromotion)!;
        Assert.Empty(promo.MediaDeals);

        // Hit National — the biggest org in the world without a screen. The dial
        // is finite and the seeded world sells all of it, so a channel has to
        // come free: strike a rival's cable deal out through the REAL path
        // (dark past its cadence), and the freed network shops straight to the
        // best promotion that could use it — us.
        promo.Prestige = 43;
        promo.TierClearance = OrgSize.National;   // don't let the tier cap pull it back
        var (rival, deal) = world.Promotions.Values.Where(p => p.Id != promo.Id)
            .SelectMany(p => p.MediaDeals.Select(d => (p, d)))
            .First(x => x.d.Kind == MediaKind.CableSports);
        deal.Requirements.Add(new DealRequirement(DealRequirementKind.EventCadence, 60));
        var rng = new MmaManager.Core.Sim.Rng.SimRandom(7);
        for (int i = 0; i < MmaManager.Core.Sim.Finance.MediaService.StrikeLimit; i++)
        {
            deal.LastAiredOn = world.CurrentDate.AddDays(-999);
            MmaManager.Core.Sim.Finance.MediaService.RunMonthly(world, rival, world.CurrentDate, rng);
        }
        Assert.DoesNotContain(rival.MediaDeals, d => d.Kind == MediaKind.CableSports);

        var offer = promo.Offers.FirstOrDefault(o => o.Media is not null);
        Assert.NotNull(offer);
        Assert.Contains(world.Mail, m => m.Subject.Contains("Media offer", StringComparison.OrdinalIgnoreCase));

        // Sign it; then slip below the platform's bar — the deal dies at expiry, not before.
        var signed = MmaManager.Core.Sim.Finance.MediaService.Accept(world, promo, offer!, world.CurrentDate);
        promo.Prestige = 20;
        signed.MonthsRemaining = 1;
        signed.LastAiredOn = world.CurrentDate;   // isolate the reach loss from any cadence strike
        new WorldTick().AdvanceWeeks(world, 5);
        Assert.Empty(promo.MediaDeals);
    }

    [Fact]
    public void PayPerView_NeedsACarrierSignedOn()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 13, UseDesignedRoster = false });
        var promo = world.Promotions.Values.OrderByDescending(p => p.Prestige.Value).First();
        var pair = world.RosterOf(promo.Id)
            .Where(f => f.WeightClass == WeightClass.Lightweight && f.Status == FighterStatus.Active)
            .Take(2).ToList();
        var ev = new EventService().CreateEvent(world, promo.Id, "PPV Test", world.CurrentDate,
            new[] { new Matchup(pair[0].Id, pair[1].Id, WeightClass.Lightweight, 5, false, null) });
        ev.Tier = EventTier.Ppv;

        // The pipe is the gate: with the carrier the buys flow, without it nothing sells.
        Assert.True(MmaManager.Core.Sim.Finance.MediaService.HasPpvCarrier(promo), "a top brand opens with a carrier");
        Assert.True(Financials.Compute(world, ev).PayPerViewBuys > 0);

        promo.MediaDeals.RemoveAll(d => d.Kind == MediaKind.PpvCarrier);
        Assert.Equal(0, Financials.Compute(world, ev).PayPerViewBuys);
    }

    [Fact]
    public void ThePlayer_CannotStageAPpv_BeforeContinental()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options
        {
            Seed = 42,
            Founding = new WorldBuilder.FoundingSpec { Prestige = 15 },
        });

        var ev = new PlayerBooking().CreateEvent(world, world.CurrentDate.AddDays(14), tier: EventTier.SuperCard);
        Assert.Equal(EventTier.FightNight, ev.Tier);
    }

    private static City City(World world, string name) =>
        world.Cities.Values.First(c => c.Name == name);

    private static Promotion Promo(World world, string abbrev) =>
        world.Promotions.Values.First(p => p.Abbreviation == abbrev);
}
