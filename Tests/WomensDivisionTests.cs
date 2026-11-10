using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Generation;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Tests;

/// <summary>
/// Women's divisions: the world runs them alongside the men's, they crown their
/// own champions, and men and women never share a cage. Plus portrait-first
/// generation, which builds fighters to fit real pack identities.
/// </summary>
public class WomensDivisionTests
{
    [Fact]
    public void TheWorld_RunsWomensDivisions_WithTheirOwnChampions()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });

        var women = world.Fighters.Values.Where(f => f.Sex == Sex.Female).ToList();
        Assert.NotEmpty(women);
        Assert.All(women, w => Assert.True(WomensDivisions.Allows(w.WeightClass)));

        // Every established org runs women's divisions, each with a female champion.
        var afc = world.Promotions.Values.First(p => p.Abbreviation == "AFC");
        var womensDivs = afc.Divisions.Where(d => d.Sex == Sex.Female).ToList();
        Assert.NotEmpty(womensDivs);
        foreach (var d in womensDivs)
        {
            var champ = world.Get(world.Get(d.Title!.Value)!.CurrentChampion!.Value)!;
            Assert.Equal(Sex.Female, champ.Sex);
            Assert.Equal(d.WeightClass, champ.WeightClass);
        }
    }

    [Fact]
    public void MenAndWomen_NeverShareACage()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 11 });
        new WorldTick().AdvanceWeeks(world, 52);

        foreach (var ev in world.Events.Values.Where(e => e.Status == EventStatus.Completed))
            foreach (var fight in ev.Card)
            {
                var r = world.Get(fight.RedCorner);
                var b = world.Get(fight.BlueCorner);
                if (r is null || b is null) continue;
                Assert.Equal(r.Sex, b.Sex);
            }
    }

    [Fact]
    public void WomensBelts_AreDefendedOverASeason()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 11 });
        new WorldTick().AdvanceWeeks(world, 52);

        // At least one women's title reign has a defence on the books.
        var womensTitles = world.Promotions.Values
            .SelectMany(p => p.Divisions).Where(d => d.Sex == Sex.Female && d.Title is not null)
            .Select(d => world.Get(d.Title!.Value)!);
        Assert.Contains(womensTitles, t => t.Lineage.Any(reign => reign.Defenses >= 1));
    }

    [Fact]
    public void PlayerBooking_RejectsAMixedSexBout()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options
        {
            Seed = 5,
            Founding = new WorldBuilder.FoundingSpec { Prestige = 15 },
        });
        var promo = world.Get(world.PlayerPromotion)!;
        // Sign one man and one woman of the same weight.
        var man = world.FreeAgents().First(f => f.Sex == Sex.Male && f.WeightClass == WeightClass.Strawweight);
        var woman = world.FreeAgents().First(f => f.Sex == Sex.Female && f.WeightClass == WeightClass.Strawweight);
        foreach (var f in new[] { man, woman })
            f.Contract = new Contract { Promotion = promo.Id, SignedOn = world.CurrentDate, FightsRemaining = 3 };

        var booking = new PlayerBooking();
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(14));
        bool ok = booking.AddBout(world, ev, man.Id, woman.Id, 3, false, out var error);

        Assert.False(ok);
        Assert.Contains("men's and women's", error);
    }

    // --- Portrait-first generation ------------------------------------------

    [Fact]
    public void PortraitFirst_BuildsFightersToMatchTheCatalog()
    {
        // A tiny catalog: only Japanese wrestlers at lightweight. Every fighter
        // the generator makes from it should read Japanese (culture drives name +
        // nationality) and wrestle.
        var catalog = new IdentityCatalog(new[]
        {
            new IdentitySpec(Sex.Male, "japanese", 146, 155, PrimaryStyle.Wrestler, false),
        });
        var world = new WorldBuilder().Build(new WorldBuilder.Options
        {
            Seed = 3, UseDesignedRoster = false, IncludeWomensDivisions = false, IdentityCatalog = catalog,
        });

        var sample = world.Fighters.Values.Where(f => f.WeightClass == WeightClass.Lightweight).Take(20).ToList();
        Assert.NotEmpty(sample);
        Assert.All(sample, f =>
        {
            Assert.Equal(PrimaryStyle.Wrestler, f.PrimaryStyle);
            Assert.Equal("JPN", world.Get(f.Country)?.Code);
        });
    }

    [Fact]
    public void PortraitFirst_NewgensRespectTheIdentityWeightBand()
    {
        // An identity whose band covers only a slice of lightweight (150-152).
        // Newgen intake goes through the same portrait-first path as the initial
        // build, so every lightweight newgen must land inside the manifest band —
        // never a fighter whose build the portrait can't cover.
        var catalog = new IdentityCatalog(new[]
        {
            new IdentitySpec(Sex.Male, "japanese", 150, 152, PrimaryStyle.Wrestler, false),
        });
        var world = new WorldBuilder().Build(new WorldBuilder.Options
        {
            Seed = 5, UseDesignedRoster = false, IncludeWomensDivisions = false, IdentityCatalog = catalog,
        });

        var intake = Core.Sim.Living.NewgenService.ForWorld(world)
            .GenerateIntake(world, 120, new Core.Sim.Rng.SimRandom(11));

        var lightweights = intake.Where(f => f.WeightClass == WeightClass.Lightweight && f.OriginCountryCode == "JPN").ToList();
        Assert.NotEmpty(lightweights);
        Assert.All(lightweights, f => Assert.InRange(f.NaturalWeightLbs, 150, 152));
    }

    [Fact]
    public void Pick_PrefersTheMostSpecificWeightBand()
    {
        // Six genuinely-heavy identities (206-265) and six generic big-man ones
        // (171-and-up). A heavyweight should always draw from the specific six.
        var specs = new List<IdentitySpec>();
        for (int i = 0; i < 6; i++) specs.Add(new IdentitySpec(Sex.Male, "narrow", 206, 265, null, false));
        for (int i = 0; i < 6; i++) specs.Add(new IdentitySpec(Sex.Male, "wide", 171, 265, null, false));
        var catalog = new IdentityCatalog(specs);

        for (ulong seed = 0; seed < 40; seed++)
        {
            var pick = catalog.Pick(new Core.Sim.Rng.SimRandom(seed), Sex.Male, WeightClass.Heavyweight, false);
            Assert.Equal("narrow", pick!.Culture);
        }
    }

    [Fact]
    public void Pick_KeepsVariety_WhenSpecificArtIsScarce()
    {
        // Only TWO specific identities exist — preferring them outright would put
        // the same face on every heavyweight. The pool merges down instead, so
        // the generic identities stay in play.
        var specs = new List<IdentitySpec>();
        for (int i = 0; i < 2; i++) specs.Add(new IdentitySpec(Sex.Male, "narrow", 206, 265, null, false));
        for (int i = 0; i < 12; i++) specs.Add(new IdentitySpec(Sex.Male, "wide", 106, 265, null, false));
        var catalog = new IdentityCatalog(specs);

        bool sawWide = false;
        for (ulong seed = 0; seed < 40 && !sawWide; seed++)
            sawWide = catalog.Pick(new Core.Sim.Rng.SimRandom(seed), Sex.Male, WeightClass.Heavyweight, false)!.Culture == "wide";
        Assert.True(sawWide);
    }
}
