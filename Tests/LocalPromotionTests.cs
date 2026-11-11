using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Generation;
using MmaManager.Core.Sim.Geography;
using MmaManager.Core.Sim.Org;

namespace MmaManager.Tests;

/// <summary>
/// Local promotions are local: every designed promotion lives in the city its
/// lore names, the authored people come from the towns their bios name, and
/// the small halls' rosters are built from their own scene first.
/// </summary>
public class LocalPromotionTests
{
    private static readonly Dictionary<ulong, World> Worlds = new();
    private static World Build(ulong seed)
    {
        lock (Worlds)
            return Worlds.TryGetValue(seed, out var w) ? w
                : Worlds[seed] = new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });
    }

    private static string? CityName(World w, CityId? id) => id is { } c ? w.Get(c)?.Name : null;

    [Fact]
    public void EveryDesignedPromotion_NamesAnAtlasCity_InItsOwnRegion()
    {
        var specs = WorldBuilder.Options.DefaultPromotions;
        Assert.Equal(19, specs.Length);
        Assert.All(specs, s =>
        {
            Assert.False(string.IsNullOrEmpty(s.HomeCity), $"{s.Abbreviation} has no authored home city");
            var city = WorldAtlas.Cities.SingleOrDefault(c => c.Name == s.HomeCity);
            Assert.True(city is not null, $"{s.Abbreviation}: {s.HomeCity} is not an atlas city");
            Assert.Equal(s.Region, WorldAtlas.ByCode(city!.CountryCode)!.Continent);
        });
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(7UL)]
    [InlineData(42UL)]
    public void EveryDesignedPromotion_LivesInItsAuthoredCity(ulong seed)
    {
        var world = Build(seed);
        foreach (var spec in WorldBuilder.Options.DefaultPromotions)
        {
            var promo = world.Promotions.Values.Single(p => p.Abbreviation == spec.Abbreviation);
            Assert.Equal(spec.HomeCity, CityName(world, promo.HomeCity));
            // The brand is strongest at home.
            int home = promo.PopularityByCity[promo.HomeCity!.Value].Value;
            Assert.True(promo.PopularityByCity.Where(kv => kv.Key != promo.HomeCity).All(kv => kv.Value.Value <= home),
                $"{spec.Abbreviation} is better known somewhere other than {spec.HomeCity}");
        }
    }

    [Fact]
    public void EveryAuthoredHomeCity_NamesOneAuthoredPerson_AndAnAtlasCityOnTheirContinent()
    {
        var people = DesignedRoster.All.Select(d => (Name: $"{d.First} {d.Last}", d.Region, d.Country))
            .Concat(DesignedLegends.All.Select(l => (Name: $"{l.First} {l.Last}", l.Region, Country: (string?)l.Country)))
            .ToList();
        foreach (var (name, city) in DesignedHomeCities.All)
        {
            var who = people.Where(p => p.Name == name).ToList();
            Assert.True(who.Count == 1, $"'{name}' must name exactly one designed fighter or legend (found {who.Count})");
            var spec = WorldAtlas.Cities.SingleOrDefault(c => c.Name == city);
            Assert.True(spec is not null, $"{name}: {city} is not an atlas city");
            Assert.Equal(who[0].Region, WorldAtlas.ByCode(spec!.CountryCode)!.Continent);
            // The hometown invariant every fighter keeps (CountryTests): a country
            // with a fight scene raises its own â€” an authored home can't move a
            // New Zealander to Sydney, only stand in where the atlas has no city.
            if (who[0].Country is { } code && WorldAtlas.Cities.Any(c => c.CountryCode == code))
                Assert.True(spec.CountryCode == code, $"{name} ({code}) can't be homed in {city} ({spec.CountryCode})");
        }
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(42UL)]
    public void EveryAuthoredHomeCity_Lands(ulong seed)
    {
        var world = Build(seed);
        foreach (var (name, city) in DesignedHomeCities.All)
        {
            var f = world.Fighters.Values.Single(x => x.Designed && $"{x.FirstName} {x.LastName}" == name);
            Assert.Equal(city, CityName(world, f.HomeCity));
            Assert.True(f.CityReputation.ContainsKey(f.HomeCity!.Value), $"{name} is nobody at home");
        }
    }

    /// <summary>
    /// The RNG guard. The hometown roll runs on a per-fighter keyed stream, so an
    /// authored home that skips it must change NOTHING but those people's home
    /// cities: every generated fighter is the same fighter, signed to the same
    /// org, from the same town, whether the table is on or off.
    /// </summary>
    [Fact]
    public void AuthoredHomeCities_ChangeOnlyTheAuthoredHomes()
    {
        var on = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 11 });
        var off = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 11, AuthoredHomeCities = false });

        Assert.Equal(off.Fighters.Count, on.Fighters.Count);
        int movedHome = 0;
        foreach (var a in on.Fighters.Values)
        {
            var b = off.Fighters[a.Id];
            Assert.Equal(b.FirstName, a.FirstName);
            Assert.Equal(b.LastName, a.LastName);
            Assert.Equal(b.Nickname, a.Nickname);
            Assert.Equal(b.BirthDate, a.BirthDate);
            Assert.Equal(b.Country, a.Country);
            Assert.Equal(b.OriginCountryCode, a.OriginCountryCode);
            Assert.Equal(b.WeightClass, a.WeightClass);
            Assert.Equal(b.PrimaryStyle, a.PrimaryStyle);
            Assert.Equal(b.HeightCm, a.HeightCm);
            Assert.Equal(b.ReachCm, a.ReachCm);
            Assert.Equal(b.PotentialAbility, a.PotentialAbility);
            Assert.Equal(b.Striking.PunchPower, a.Striking.PunchPower);
            Assert.Equal(b.Grappling.TakedownOffense, a.Grappling.TakedownOffense);
            Assert.Equal(b.Physical.Cardio, a.Physical.Cardio);
            Assert.Equal(b.Mental.FightIQ, a.Mental.FightIQ);
            Assert.Equal(b.Contract?.Promotion, a.Contract?.Promotion);
            Assert.Equal(b.Contract?.PursePerFight, a.Contract?.PursePerFight);
            Assert.Equal(b.Record.Wins, a.Record.Wins);
            Assert.Equal(b.Record.Losses, a.Record.Losses);

            bool authored = a.Designed && DesignedHomeCities.All.ContainsKey($"{a.FirstName} {a.LastName}");
            if (!authored) Assert.Equal(b.HomeCity, a.HomeCity);
            else if (a.HomeCity != b.HomeCity) movedHome++;
        }
        Assert.True(movedHome > 0, "the table moved nobody â€” is it wired in?");
    }

    // Own-continent share of the active roster, averaged over several seeds.
    private static (double Region, double Champions) Nativity(IEnumerable<World> worlds, Func<Promotion, bool> which)
    {
        var shares = new List<double>();
        var champs = new List<double>();
        foreach (var w in worlds)
            foreach (var p in w.Promotions.Values.Where(which))
            {
                var roster = w.RosterOf(p.Id).Where(f => f.Status == FighterStatus.Active).ToList();
                shares.Add(roster.Count(f => f.Nationality == p.BaseRegion) / (double)roster.Count);
                champs.AddRange(p.Titles.Select(t => w.Get(t)!.CurrentChampion).OfType<FighterId>()
                    .Select(id => w.Get(id)!.Nationality == p.BaseRegion ? 1.0 : 0.0));
            }
        return (shares.Average(), champs.Average());
    }

    [Fact]
    public void SmallHalls_AreBuiltFromTheirOwnScene_TheGiantsStayInternational()
    {
        var worlds = new[] { 1UL, 7UL, 42UL }.Select(Build).ToList();
        var city = Nativity(worlds, p => OrgSizing.SizeOf(p) == OrgSize.City);
        var regional = Nativity(worlds, p => OrgSizing.SizeOf(p) == OrgSize.Regional);
        var big = Nativity(worlds, p => OrgSizing.SizeOf(p) >= OrgSize.National);

        // Measured at build (seeds 1-6): City ~70%, Regional ~69%, National+ ~22%.
        // Before this change: City ~29%, Regional ~22%, the same as everyone else.
        Assert.InRange(city.Region, 0.60, 0.90);
        Assert.InRange(regional.Region, 0.55, 0.85);
        Assert.InRange(big.Region, 0.10, 0.35);
        Assert.True(city.Champions >= 0.75, $"City champions {city.Champions:P0} own-continent");
        Assert.True(regional.Champions >= 0.75, $"Regional champions {regional.Champions:P0} own-continent");
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(42UL)]
    public void NoPromotion_IsLeftUnableToFillACard(ulong seed)
    {
        var world = Build(seed);
        foreach (var p in world.Promotions.Values)
            foreach (var d in p.Divisions)
            {
                int have = world.RosterOf(p.Id).Count(f => d.Matches(f) && f.Status == FighterStatus.Active);
                Assert.True(have >= 4, $"{p.Abbreviation} {d.Sex} {d.WeightClass} has {have} fighters");
            }
    }

    [Fact]
    public void ASmallHall_BackDatedHistory_HappenedAtHome()
    {
        var world = Build(42);
        foreach (var abbr in new[] { "CPF", "MFC", "PMP", "BLB", "MCC" })
        {
            var promo = world.Promotions.Values.Single(p => p.Abbreviation == abbr);
            var past = world.Events.Values.Where(e => e.Promotion == promo.Id && e.Status == EventStatus.Completed).ToList();
            Assert.NotEmpty(past);
            Assert.All(past, e => Assert.Equal(promo.HomeCity, WorldMap.CityOf(world, e)));
        }
    }

    [Fact]
    public void ASmallHall_LeansTowardTheHometownKid_TheGiantsDoNot()
    {
        var world = Build(42);
        var cpf = world.Promotions.Values.Single(p => p.Abbreviation == "CPF");
        var afc = world.Promotions.Values.Single(p => p.Abbreviation == "AFC");
        var local = new Fighter { Id = new FighterId(900001), FirstName = "A", LastName = "B", BirthDate = new DateOnly(2000, 1, 1), HomeCity = cpf.HomeCity };
        local.CityReputation[cpf.HomeCity!.Value] = 20;
        var stranger = new Fighter { Id = new FighterId(900001), FirstName = "A", LastName = "B", BirthDate = new DateOnly(2000, 1, 1), HomeCity = afc.HomeCity };

        double pull = FreeAgencyService.HomePull(cpf, local);
        Assert.InRange(pull, 3.0, 6.0);                       // somewhat more attractive, not a different fighter
        Assert.Equal(0.0, FreeAgencyService.HomePull(cpf, stranger));
        Assert.Equal(0.0, FreeAgencyService.HomePull(afc, new Fighter { Id = new FighterId(900001), FirstName = "A", LastName = "B", BirthDate = new DateOnly(2000, 1, 1), HomeCity = afc.HomeCity }));   // the summit tours
    }

    // A shoestring division: the top seed is a 1-0 kid, the next a 6-1 veteran.
    private static (World World, Promotion Promo, Fighter Novice, Fighter Veteran) NoviceTopsTheSeeds()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 31, UseDesignedRoster = false });
        var pool = world.FreeAgents()
            .Where(f => f.WeightClass == WeightClass.Lightweight && f.Sex == Sex.Male && f.Status == FighterStatus.Active)
            .OrderByDescending(FighterAbility.CurrentAbility).ThenBy(f => f.Id.Value)
            .ToList();
        var novice = pool[0];
        var veteran = pool.First(f => FighterAbility.CurrentAbility(f) <= FighterAbility.CurrentAbility(novice) - 5);
        novice.Record.Wins = 1; novice.Record.Losses = 0; novice.Record.Draws = 0; novice.Record.NoContests = 0;
        veteran.Record.Wins = 6; veteran.Record.Losses = 1; veteran.Record.Draws = 0; veteran.Record.NoContests = 0;
        Assert.True(PromotionSetup.SeedScore(novice) > PromotionSetup.SeedScore(veteran));

        var promo = new Promotion
        {
            Id = world.NewPromotionId(), Name = "Test Shoestring", Abbreviation = "TST",
            BaseRegion = Region.NorthAmerica, Prestige = 12, Reputation = 50,
        };
        Assert.True(RosterNativity.IsLocalStage(promo.Prestige.Value));
        world.Promotions[promo.Id] = promo;
        return (world, promo, novice, veteran);
    }

    [Fact]
    public void AMidGameStartup_StillCrownsItsTopSeed()
    {
        // WorldEvents' startups and LeagueBuilder call without dayOneBuild: as on main.
        var (world, promo, novice, veteran) = NoviceTopsTheSeeds();
        PromotionSetup.ConfigureDivisions(world, promo, new[] { novice, veteran },
            new[] { WeightClass.Lightweight }, world.CurrentDate, 15, null, Sex.Male);
        var title = world.Get(promo.Divisions.Single().Title!.Value)!;
        Assert.Equal(novice.Id, title.CurrentChampion);
    }

    [Fact]
    public void TheDayOneBuild_AtALocalStage_CrownsAFighterWithARecord()
    {
        var (world, promo, novice, veteran) = NoviceTopsTheSeeds();
        PromotionSetup.ConfigureDivisions(world, promo, new[] { novice, veteran },
            new[] { WeightClass.Lightweight }, world.CurrentDate, 15, null, Sex.Male, dayOneBuild: true);
        var title = world.Get(promo.Divisions.Single().Title!.Value)!;
        Assert.Equal(veteran.Id, title.CurrentChampion);
    }
}
