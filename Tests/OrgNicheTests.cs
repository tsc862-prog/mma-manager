using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Tests;

/// <summary>
/// Org niches: promotions aren't interchangeable — some run the lighter weights,
/// some big men only, some a tight core; some run women's divisions and some
/// don't. The designed world is authored that way, rosters/talent tiering respect
/// it, and procedural startups pick a lane of their own.
/// </summary>
public class OrgNicheTests
{
    [Fact]
    public void TheDesignedWorld_HasTexture_NotNineteenIdenticalOrgs()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
        Division[] Male(string abbr) => world.Promotions.Values.First(p => p.Abbreviation == abbr)
            .Divisions.Where(d => d.Sex == Sex.Male).ToArray();

        // The Japanese lighter-weights house runs no big men…
        Assert.DoesNotContain(Male("KAC"), d => d.WeightClass is WeightClass.Middleweight or WeightClass.Heavyweight);
        Assert.Contains(Male("KAC"), d => d.WeightClass == WeightClass.Featherweight);
        // …the Detroit and Baltic halls run nothing small…
        Assert.DoesNotContain(Male("MCC"), d => d.WeightClass < WeightClass.Middleweight);
        Assert.DoesNotContain(Male("BLB"), d => d.WeightClass < WeightClass.Middleweight);
        // …and the giants run the full slate.
        Assert.True(Male("AFC").Length >= 8);
    }

    [Fact]
    public void WomensDivisions_AreAnIdentity_NotAPrestigeReward()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
        bool RunsWomen(string abbr) => world.Promotions.Values.First(p => p.Abbreviation == abbr)
            .Divisions.Any(d => d.Sex == Sex.Female);

        Assert.True(RunsWomen("AFC"));
        Assert.True(RunsWomen("KAC"), "the small Japanese house runs women — its niche, not its prestige");
        Assert.False(RunsWomen("GFC"), "the Gulf org doesn't run women despite its money");
        Assert.False(RunsWomen("IFA"));
    }

    [Fact]
    public void EveryRosteredFighter_HasADivisionToFightIn()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
        foreach (var promo in world.Promotions.Values)
            foreach (var f in world.RosterOf(promo.Id))
                Assert.True(promo.Divisions.Any(d => d.Matches(f)),
                    $"{f.FullName} ({f.Sex} {f.WeightClass}) is signed to {promo.Abbreviation}, which doesn't run that division");
    }

    [Fact]
    public void DesignedChampions_StillLandTheirBelts_InsideTheNiches()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
        // Spot-check niche orgs whose designed champion sits in their lane.
        foreach (var (abbr, wc) in new[]
                 {
                     ("KAC", WeightClass.Featherweight),      // Nakamura, the lighter-weights house
                     ("BLB", WeightClass.LightHeavyweight),   // Kazlauskas, the Baltic hall
                     ("MCC", WeightClass.Middleweight),       // Barone, Motor City
                 })
        {
            var promo = world.Promotions.Values.First(p => p.Abbreviation == abbr);
            var title = world.Titles.Values.First(t => t.Promotion == promo.Id && t.WeightClass == wc);
            Assert.NotNull(title.CurrentChampion);
            Assert.True(world.Fighters[title.CurrentChampion!.Value].Designed,
                $"{abbr} {wc} should be held by its designed champion");
        }
    }

    [Fact]
    public void Startups_PickALane_OfTheirOwn()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
        var dayOne = world.Promotions.Keys.ToHashSet();
        new WorldTick().AdvanceWeeks(world, 260);   // five years of startups

        var startups = world.Promotions.Values
            .Where(p => !dayOne.Contains(p.Id) && !p.Defunct).ToList();
        Assert.True(startups.Count >= 2, "five years should birth a few startups");
        foreach (var s in startups)
            // A lane, not a monolith — and a startup opens only the divisions of
            // its lane it could staff at a price it can carry (at least two
            // bouts' worth each), so a three-division core may open with two.
            Assert.InRange(s.Divisions.Count(d => d.Sex == Sex.Male), 2, 7);
        // The old world stood every startup up with the same 7 divisions — the
        // regression guard is that lean lanes actually happen.
        Assert.Contains(startups, s => s.Divisions.Count(d => d.Sex == Sex.Male) < 7);
    }
}
