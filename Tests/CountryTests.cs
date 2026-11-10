using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Generation;
using MmaManager.Core.Sim.Geography;

namespace MmaManager.Tests;

/// <summary>
/// The country layer: a comprehensive real-world atlas under the cities, real
/// nationalities on every fighter, names that follow the passport, hometowns
/// that prefer the fighter's own country, and country-level fame rollups.
/// </summary>
public class CountryTests
{
    [Fact]
    public void TheWorld_HasAComprehensiveAtlas_WithAreasForTheBigFightNations()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5 });

        Assert.True(world.Countries.Count >= 100, $"expected 100+ countries, got {world.Countries.Count}");

        // Every city sits in a country on its own continent, with a valid area.
        Assert.All(world.Cities.Values, c =>
        {
            var country = world.Get(c.Country);
            Assert.NotNull(country);
            Assert.Equal(c.Region, country!.Continent);
            if (c.Area is { } a) Assert.InRange(a, 0, country.Areas.Count - 1);
        });

        // The deepest fight nations are subdivided into in-country areas.
        foreach (var code in new[] { "USA", "BRA", "RUS", "GBR", "JPN" })
        {
            var country = WorldMap.CountryByCode(world, code);
            Assert.NotNull(country);
            Assert.True(country!.Areas.Count >= 3, $"{code} should have areas");
        }

        // Most of the map has no fight scene yet — that's the point.
        int withCities = world.Countries.Values.Count(k => world.Cities.Values.Any(c => c.Country == k.Id));
        Assert.InRange(withCities, 10, 40);
    }

    [Fact]
    public void EveryFighter_HasARealNationality_OnTheirOwnContinent()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5 });
        Assert.All(world.Fighters.Values, f =>
        {
            var country = world.Get(f.Country);
            Assert.NotNull(country);
            Assert.Equal(f.Nationality, country!.Continent);
        });
    }

    [Fact]
    public void DesignedFighters_KeepTheirAuthoredCountries()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5 });

        void ExpectCountry(string last, string nickname, string code)
        {
            var f = Assert.Single(world.Fighters.Values,
                x => x.Designed && x.LastName == last && x.Nickname == nickname);
            Assert.Equal(code, world.Get(f.Country)?.Code);
        }

        ExpectCountry("Tsereteli", "The Cliff", "GEO");
        ExpectCountry("Batyr", "Berkut", "KAZ");
        ExpectCountry("Okonjo", "The Lion", "NGA");
        ExpectCountry("O'Rourke", "The Hound", "IRL");
        ExpectCountry("Fetu", "Tsunami", "SAM");
    }

    [Fact]
    public void Hometowns_PreferTheFightersOwnCountry()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5 });
        var countriesWithCities = world.Cities.Values.Select(c => c.Country).ToHashSet();

        int locals = 0, expats = 0;
        foreach (var f in world.Fighters.Values)
        {
            Assert.NotNull(f.HomeCity);
            var home = world.Get(f.HomeCity!.Value)!;
            if (countriesWithCities.Contains(f.Country))
            {
                // Their country has a fight scene: they came up in it.
                Assert.Equal(f.Country, home.Country);
                locals++;
            }
            else
            {
                // No scene at home: they came up abroad, on their own continent.
                Assert.Equal(f.Nationality, home.Region);
                expats++;
            }
        }
        Assert.True(locals > 0, "expected fighters homed in their own country");
        Assert.True(expats > 0, "expected fighters from countries with no fight scene yet");
    }

    [Fact]
    public void Names_FollowTheCountry()
    {
        var gen = new FighterGenerator(worldSeed: 42);
        var georgianLasts = new[]
        {
            "Tsereteli", "Beridze", "Lomidze", "Gelashvili", "Japaridze", "Maisuradze",
            "Kiknadze", "Abashidze", "Gogoladze", "Khutsishvili", "Kapanadze", "Machavariani",
        };

        for (int i = 1; i <= 12; i++)
        {
            var f = gen.Generate(new FighterId(i), new DateOnly(2026, 1, 1),
                new GenOptions { Nationality = Region.Europe, CountryCode = "GEO" });
            Assert.Contains(f.LastName, georgianLasts);
        }
    }

    [Fact]
    public void CountryFame_IsDerivedFromTheCountrysCities()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5 });
        var usa = WorldMap.CountryByCode(world, "USA")!;
        var bra = WorldMap.CountryByCode(world, "BRA")!;
        var f = world.Fighters.Values.First();
        f.CityReputation.Clear();

        foreach (var c in world.Cities.Values.Where(c => c.Country == usa.Id))
            f.CityReputation[c.Id] = 50;

        Assert.Equal(50, Reputation.FighterInCountry(world, f, usa.Id));
        Assert.Equal(0, Reputation.FighterInCountry(world, f, bra.Id));
    }
}
