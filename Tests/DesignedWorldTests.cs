using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;

namespace MmaManager.Tests;

/// <summary>
/// The 2026 roster pass: the women's scene opens with AUTHORED stars (not
/// anonymous belts), the niche orgs get institutions (rivals, prospects,
/// poach-bait) instead of a single face, and designed feuds enter the world
/// already burning so the first cards have stories.
/// </summary>
public class DesignedWorldTests
{
    private static World Build() => new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });

    [Fact]
    public void TheWomensScene_OpensWithAuthoredStars()
    {
        var world = Build();

        // The AFC women's bantamweight throne belongs to the Tsarina…
        var afc = world.Promotions.Values.First(p => p.Abbreviation == "AFC");
        var bw = world.Titles.Values.First(t => t.Promotion == afc.Id
            && t.WeightClass == WeightClass.Bantamweight
            && world.Fighters[t.CurrentChampion!.Value].Sex == Sex.Female);
        var tsarina = world.Fighters[bw.CurrentChampion!.Value];
        Assert.Equal("Volkova", tsarina.LastName);
        Assert.True(tsarina.StarPower, "a 78-pop designed megastar carries the it-factor");

        // …and her blood feud with Donovan is live before the first bell.
        var donovan = world.Fighters.Values.First(f => f.LastName == "Donovan" && f.Designed);
        Assert.NotNull(world.Rivalries.FirstOrDefault(r => r.IsBetween(tsarina.Id, donovan.Id)));
    }

    [Fact]
    public void KansaiCage_IsTheWomensHouse_ItWasDesignedToBe()
    {
        var world = Build();
        var kac = world.Promotions.Values.First(p => p.Abbreviation == "KAC");

        // The lifer holds the strawweight belt; the phenom is on the roster with a streak.
        var straw = world.Titles.Values.First(t => t.Promotion == kac.Id
            && t.WeightClass == WeightClass.Strawweight
            && world.Fighters[t.CurrentChampion!.Value].Sex == Sex.Female);
        Assert.Equal("Takeda", world.Fighters[straw.CurrentChampion!.Value].LastName);

        var kondo = world.RosterOf(kac.Id).First(f => f.LastName == "Kondo");
        Assert.Equal(8, kondo.Record.CurrentStreak);
    }

    [Fact]
    public void DayOneFeuds_AreAlreadyBurning()
    {
        var world = Build();
        Fighter F(string last) => world.Fighters.Values.First(f => f.LastName == last && f.Designed);

        // The AFC lightweight title picture is a grudge, the Baltic hall has its
        // heavyweight war, and Detroit and Cape Town each have a crosstown feud.
        foreach (var (a, b) in new[]
                 { ("Seo", "Mercer"), ("Urbonas", "Ozols"), ("Weeks", "Barone"), ("Mokoena", "Ndlovu") })
        {
            var rivalry = world.Rivalries.FirstOrDefault(r => r.IsBetween(F(a).Id, F(b).Id));
            Assert.True(rivalry is { Intensity: >= 50 }, $"{a} vs {b} should enter the world burning");
        }
    }

    [Fact]
    public void NicheAnchors_LandInsideTheirLanes()
    {
        var world = Build();

        // Every designed fighter with an org tag is on that org's roster, in a
        // division the org actually runs — women included.
        foreach (var f in world.Fighters.Values.Where(f => f.Designed && f.Contract is not null))
        {
            var promo = world.Get(f.Contract!.Promotion)!;
            Assert.True(promo.Divisions.Any(d => d.Matches(f)),
                $"{f.FullName} ({f.Sex} {f.WeightClass}) has no division at {promo.Abbreviation}");
        }

        // And the designed women free agents exist for scouts to find.
        Assert.Contains(world.Fighters.Values, f => f.LastName == "Vance" && f.Contract is null);
        Assert.Contains(world.Fighters.Values, f => f.LastName == "Diallo" && f.Contract is null);
    }

    [Fact]
    public void AuthoredLore_LandsOnFightersOrgsAndCamps()
    {
        var world = Build();

        // Every designed fighter enters the world with their authored backstory…
        Assert.All(world.Fighters.Values.Where(f => f.Designed),
            f => Assert.False(string.IsNullOrWhiteSpace(f.Bio), $"{f.FullName} has no bio"));
        // …procedural fighters stay bio-less (the Biographer derives theirs).
        Assert.Contains(world.Fighters.Values, f => !f.Designed && f.Bio is null);

        // Every designed promotion carries its house identity.
        Assert.All(world.Promotions.Values,
            p => Assert.False(string.IsNullOrWhiteSpace(p.Lore), $"{p.Abbreviation} has no lore"));

        // The designed camps have stories; the procedural local gyms don't.
        Assert.Contains(world.Camps.Values, c => c.Name == "Apex Combat Club" && !string.IsNullOrWhiteSpace(c.Lore));
        Assert.Contains(world.Camps.Values, c => c.Lore is null);
    }
}
