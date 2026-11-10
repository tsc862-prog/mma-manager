using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Generation;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Tests;

/// <summary>
/// The initial database is hand-designed (champions, AFC contenders, free-agent
/// hooks) and everything after day one — depth talent and newgens — is
/// procedural. These lock the seam between the two.
/// </summary>
public class DesignedRosterTests
{
    [Fact]
    public void DesignedData_IsInternallyConsistent()
    {
        var orgs = WorldBuilder.Options.DefaultPromotions.Select(s => s.Abbreviation).ToHashSet();
        foreach (var d in DesignedRoster.All)
        {
            Assert.True(d.Org is null || orgs.Contains(d.Org), $"{d.First} {d.Last}: unknown org {d.Org}");
            Assert.False(d.Champion && d.Org is null, $"{d.First} {d.Last}: a champion must belong to an org");
            Assert.InRange(d.Overall, 20, 95);
            Assert.InRange(d.Age, 18, 40);
        }

        // At most one designed champion per belt (men's and women's are separate belts).
        var duplicates = DesignedRoster.All.Where(d => d.Champion)
            .GroupBy(d => (d.Org, d.WeightClass, d.Sex)).Where(g => g.Count() > 1).ToList();
        Assert.Empty(duplicates);
    }

    // The generator's name pools can coincidentally mint a namesake, so a
    // designed fighter is identified by their full designed identity.
    private static bool Matches(MmaManager.Core.Model.Fighter f, DesignedFighter d) =>
        f.FirstName == d.First && f.LastName == d.Last && f.Nickname == d.Nickname
        && f.WeightClass == d.WeightClass && f.Nationality == d.Region;

    [Fact]
    public void DesignedHeadliners_AreInTheWorld_WithTheirIdentity()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 2026 });

        foreach (var d in DesignedRoster.All)
        {
            var f = Assert.Single(world.Fighters.Values, x => Matches(x, d));
            Assert.Equal(d.Style, f.PrimaryStyle);
            Assert.Equal(d.Wins, f.Record.Wins);
            Assert.Equal(d.Losses, f.Record.Losses);
            Assert.Equal(d.Age, f.AgeOn(world.CurrentDate));

            var promo = f.Contract is { } c ? world.Get(c.Promotion) : null;
            Assert.Equal(d.Org, promo?.Abbreviation);
        }
    }

    [Fact]
    public void DesignedChampions_HoldEveryStartingBelt()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 7 });

        // Every designed champion holds their promotion's belt in their division
        // at game start (orgs without a designed champ crown a procedural one).
        foreach (var d in DesignedRoster.All.Where(d => d.Champion && d.Org is not null))
        {
            var promo = world.Promotions.Values.First(p => p.Abbreviation == d.Org);
            var div = promo.Divisions.First(x => x.WeightClass == d.WeightClass && x.Sex == d.Sex);
            var champ = world.Get(world.Get(div.Title!.Value)!.CurrentChampion!.Value)!;
            Assert.Equal($"{d.First} {d.Last}", $"{champ.FirstName} {champ.LastName}");
        }
    }

    [Fact]
    public void Newgens_StayProcedural()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 11 });
        var before = world.Fighters.Keys.ToHashSet();

        new WorldTick().AdvanceWeeks(world, 52);

        // Prospects arrived, and none of them reuse a designed identity.
        var newcomers = world.Fighters.Values.Where(f => !before.Contains(f.Id)).ToList();
        Assert.True(newcomers.Count > 0, "a year should bring newgen prospects");
        Assert.DoesNotContain(newcomers, f => DesignedRoster.All.Any(d => Matches(f, d)));
    }

    [Fact]
    public void DesignedRoster_CanBeTurnedOff()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 2026, UseDesignedRoster = false });
        Assert.DoesNotContain(world.Fighters.Values,
            f => DesignedRoster.All.Any(d => Matches(f, d)));
    }
}
