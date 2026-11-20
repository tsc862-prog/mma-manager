using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Geography;

namespace MmaManager.Tests;

/// <summary>Every city and country the game can hold has a real-world position, and the table is not mis-signed.</summary>
public class CityCoordinatesTests
{
    private static IEnumerable<World> Worlds()
    {
        foreach (ulong seed in new ulong[] { 1, 7, 42 })
        {
            yield return new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });
            yield return new WorldBuilder().Build(WorldBuilder.Options.Origins(seed));
        }
    }

    // Generous per-region boxes; Oceania straddles the antimeridian so it is a predicate.
    private static bool InRegionBox(Region r, double lat, double lon) => r switch
    {
        Region.NorthAmerica => lat is > 5 and < 75 && lon is > -170 and < -50,
        Region.SouthAmerica => lat is > -57 and < 14 && lon is > -83 and < -33,
        Region.Europe => lat is > 33 and < 72 && lon is > -25 and < 180,
        Region.Asia => lat is > -12 and < 56 && lon is > 55 and < 150,
        Region.Oceania => lat is > -50 and < 5 && (lon > 110 || lon < -150),
        Region.Africa => lat is > -36 and < 38 && lon is > -20 and < 52,
        Region.MiddleEast => lat is > 11 and < 43 && lon is > 25 and < 75,
        _ => false,
    };

    [Fact]
    public void Every_city_has_coordinates_in_both_modes_and_across_seeds()
    {
        foreach (var w in Worlds())
        {
            Assert.Equal(WorldAtlas.Cities.Length, w.Cities.Count);
            foreach (var c in w.Cities.Values)
            {
                Assert.True(CityCoordinates.TryGet(w, c.Id, out var at), $"no coordinates for {c.Name}");
                Assert.InRange(at.Lat, -90, 90);
                Assert.InRange(at.Lon, -180, 180);
                Assert.True(InRegionBox(c.Region, at.Lat, at.Lon), $"{c.Name} ({at.Lat},{at.Lon}) is outside {c.Region}");
                Assert.Equal(at, CityCoordinates.Get(w, c.Id));
            }
        }
    }

    [Fact]
    public void Table_has_no_orphans_and_no_shared_points()
    {
        var atlas = WorldAtlas.Cities.Select(c => (c.CountryCode, c.Name)).ToHashSet();
        Assert.Equal(atlas.Count, CityCoordinates.Cities.Count);
        foreach (var key in CityCoordinates.Cities.Keys) Assert.Contains(key, atlas);
        Assert.Equal(CityCoordinates.Cities.Count, CityCoordinates.Cities.Values.Distinct().Count());
    }

    [Fact]
    public void Every_country_has_a_centre_inside_its_continent_box()
    {
        Assert.Equal(WorldAtlas.Countries.Length, CityCoordinates.Countries.Count);
        foreach (var spec in WorldAtlas.Countries)
        {
            Assert.True(CityCoordinates.TryGetCountry(spec.Code, out var at), $"no centre for {spec.Name}");
            Assert.InRange(at.Lat, -90, 90);
            Assert.InRange(at.Lon, -180, 180);
            Assert.True(InRegionBox(spec.Continent, at.Lat, at.Lon), $"{spec.Name} ({at.Lat},{at.Lon}) is outside {spec.Continent}");
        }
    }

    [Fact]
    public void A_city_sits_near_its_own_country_centre_or_at_least_on_its_side_of_the_world()
    {
        foreach (var spec in WorldAtlas.Cities)
        {
            var city = CityCoordinates.Cities[(spec.CountryCode, spec.Name)];
            var country = CityCoordinates.GetCountry(spec.CountryCode);
            Assert.True(Math.Abs(city.Lat - country.Lat) < 40 && Math.Abs(city.Lon - country.Lon) < 60,
                $"{spec.Name} is implausibly far from {spec.CountryCode}'s centre");
        }
    }

    [Theory]
    [InlineData("USA", "Las Vegas", 36.2, -115.1)]
    [InlineData("BRA", "Sao Paulo", -23.5, -46.6)]
    [InlineData("GBR", "London", 51.5, -0.1)]
    [InlineData("JPN", "Tokyo", 35.7, 139.7)]
    [InlineData("AUS", "Sydney", -33.9, 151.2)]
    [InlineData("RSA", "Cape Town", -33.9, 18.4)]
    [InlineData("NZL", "Auckland", -36.9, 174.8)]
    [InlineData("ARG", "Buenos Aires", -34.6, -58.4)]
    [InlineData("TUR", "Istanbul", 41.0, 29.0)]
    [InlineData("UAE", "Dubai", 25.2, 55.3)]
    public void Spot_checks(string country, string city, double lat, double lon)
    {
        Assert.True(CityCoordinates.TryGet(country, city, out var at));
        Assert.InRange(at.Lat, lat - 0.1, lat + 0.1);
        Assert.InRange(at.Lon, lon - 0.1, lon + 0.1);
    }
}
