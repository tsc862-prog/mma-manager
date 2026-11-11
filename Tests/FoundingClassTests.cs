using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Org;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Tests;

/// <summary>
/// A new promotion's founding class: local kids from its open tryouts, green
/// and cheap, grateful to the house that gave them the shot — and never enough
/// on their own to launch it.
/// </summary>
public class FoundingClassTests
{
    private static World Build(ulong seed = 3) =>
        new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });

    private static readonly WeightClass[] Core3 =
        { WeightClass.Lightweight, WeightClass.Welterweight, WeightClass.Middleweight };

    private static Promotion NewStartup(World world, Region region)
    {
        var promo = new Promotion
        {
            Id = world.NewPromotionId(), Name = "Tryout Test FC", Abbreviation = "TTF",
            BaseRegion = region, Balance = new Money(200_000m), Prestige = 9, Reputation = 50,
        };
        promo.TierClearance = OrgSizing.SizeOf(promo);
        world.Promotions[promo.Id] = promo;
        return promo;
    }

    [Fact]
    public void TheClass_IsLocal_Green_AndDiscoveredByTheHouse()
    {
        var world = Build();
        var promo = NewStartup(world, Region.Europe);
        var cls = FoundingClass.Generate(world, promo, Region.Europe, Core3, world.CurrentDate);

        Assert.Equal(FoundingClass.PerDivision * Core3.Length, cls.Count);
        foreach (var wc in Core3) Assert.Equal(FoundingClass.PerDivision, cls.Count(f => f.WeightClass == wc));
        foreach (var f in cls)
        {
            Assert.Same(f, world.Get(f.Id));
            Assert.Null(f.Contract);
            Assert.Equal(0, f.Record.TotalBouts);
            Assert.Empty(f.BoutHistory);
            Assert.Equal(Region.Europe, f.Nationality);
            Assert.InRange(f.AgeOn(world.CurrentDate), 18, 23);
            Assert.Equal(promo.Id, f.DiscoveredBy);
            Assert.False(f.LocalTalent);
            Assert.NotNull(f.HomeCity);
            Assert.Contains(f.Accolades, a => a.Title.StartsWith("Founding class"));
        }
    }

    [Fact]
    public void TheClass_IsCheap_AndCheapestForItsOwnHouse()
    {
        var world = Build();
        var promo = NewStartup(world, Region.NorthAmerica);
        var other = world.Promotions.Values.Where(p => !p.Defunct && p.Id != promo.Id && !Negotiator.WantsExclusivity(p))
            .OrderBy(p => p.Id.Value).First();
        var cls = FoundingClass.Generate(world, promo, Region.NorthAmerica, Core3, world.CurrentDate);
        var valuation = new FighterValuation();
        decimal cap = Negotiator.FoundingPurseCap(world, promo);
        foreach (var f in cls)
        {
            var exp = valuation.Evaluate(world, f);
            decimal mine = Negotiator.EffectiveAskingPurse(world, promo, f, exp).Amount;
            decimal theirs = Negotiator.EffectiveAskingPurse(world, other, f, exp).Amount;
            Assert.True(mine <= theirs, $"{f.FullName}: {mine} at the house that found him, {theirs} elsewhere");
            Assert.True(mine <= cap, $"{f.FullName} asks {mine} of a house that can pay {cap}");
        }
    }

    [Fact]
    public void TheClass_IsTheSame_EveryTime_AndTakesNoSharedDraw()
    {
        var a = Build(); var b = Build();
        var pa = NewStartup(a, Region.Asia); var pb = NewStartup(b, Region.Asia);
        var ca = FoundingClass.Generate(a, pa, Region.Asia, Core3, a.CurrentDate);
        var cb = FoundingClass.Generate(b, pb, Region.Asia, Core3, b.CurrentDate);
        Assert.Equal(ca.Select(f => (f.Id, f.FullName, f.BirthDate, f.PotentialAbility.Value, f.HomeCity)),
                     cb.Select(f => (f.Id, f.FullName, f.BirthDate, f.PotentialAbility.Value, f.HomeCity)));
        // The signature has no shared stream to draw from: a launch that stocks
        // itself still takes exactly one shared draw per signing (see
        // ContractHygieneTests.Startup_TakesOneSharedDrawPerSigning).
        Assert.DoesNotContain(typeof(FoundingClass).GetMethod(nameof(FoundingClass.Generate))!.GetParameters(),
            p => p.ParameterType == typeof(SimRandom));
    }

    [Fact]
    public void AClassAlone_CannotLaunchAPromotion_ButCarriesOneThatHasAFewPros()
    {
        var world = Build();
        var promo = NewStartup(world, Region.Africa);
        // An empty market in its divisions: everyone else in those weights is signed elsewhere.
        var parking = world.Promotions.Values.Where(p => !p.Defunct && p.Id != promo.Id).OrderBy(p => p.Id.Value).First();
        var market = world.FreeAgents().Where(f => Core3.Contains(f.WeightClass) && f.Sex == Sex.Male).ToList();
        foreach (var f in market)
            f.Contract = new Contract { Promotion = parking.Id, SignedOn = world.CurrentDate, FightsRemaining = 3 };

        var cls = FoundingClass.Generate(world, promo, Region.Africa, Core3, world.CurrentDate);
        var signed = WorldEvents.StockStartup(world, promo, Region.Africa, Core3, world.CurrentDate, new SimRandom(5));
        Assert.Empty(signed);   // three kids a division is not a card
        Assert.All(cls, f => Assert.Null(f.Contract));

        // One affordable pro in each of two divisions, and the class carries the launch.
        foreach (var wc in Core3.Take(2))
        {
            var pro = market.Where(f => f.WeightClass == wc).OrderBy(f => f.Record.TotalBouts).ThenBy(f => f.Id.Value).First();
            pro.Contract = null;
            pro.Intangibles.Popularity = 5;
            pro.Momentum = 0;
        }
        signed = WorldEvents.StockStartup(world, promo, Region.Africa, Core3, world.CurrentDate, new SimRandom(5));
        Assert.True(signed.Count >= WorldEvents.StartupMinRoster, $"signed {signed.Count}");
        Assert.Contains(signed, f => f.DiscoveredBy == promo.Id);
        Assert.Contains(signed, f => f.DiscoveredBy != promo.Id);
    }

    [Fact]
    public void AFailedLaunch_TakesItsClassBackOut()
    {
        var world = Build();
        var promo = NewStartup(world, Region.Oceania);
        int before = world.Fighters.Count;
        var cls = FoundingClass.Generate(world, promo, Region.Oceania, Core3, world.CurrentDate);
        Assert.Equal(before + cls.Count, world.Fighters.Count);
        FoundingClass.Withdraw(world, cls);
        Assert.Equal(before, world.Fighters.Count);
        Assert.All(cls, f => Assert.Null(world.Get(f.Id)));
    }
}
