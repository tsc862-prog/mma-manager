using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Org;

namespace MmaManager.Tests;

/// <summary>
/// Franchise fighters: the rare STAR POWER trait makes fame compound (faster pop
/// growth, super-linear gates), and the FACE OF THE COMPANY bet feeds the org's
/// standing and partner money, inflates the face's asking price — and blows up
/// in the org's hands when the face walks. All flavor/money-side.
/// </summary>
public class FranchiseTests
{
    [Fact]
    public void StarPower_IsRare_AndTheDesignedMegastarsCarryIt()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
        var active = world.Fighters.Values.Where(f => !f.LocalTalent).ToList();

        int stars = active.Count(f => f.StarPower);
        Assert.InRange(stars / (double)active.Count, 0.005, 0.10);   // rare, but present

        // The authored 70+ pop headliners didn't get famous by accident.
        Assert.All(active.Where(f => f.Designed && f.Intangibles.Popularity.Value >= 70),
            f => Assert.True(f.StarPower));
    }

    [Fact]
    public void StarPower_MakesFameCompound()
    {
        var f = new Fighter { Id = new FighterId(1), FirstName = "Sky", LastName = "Rocket", BirthDate = new DateOnly(2000, 1, 1) };

        Assert.Equal(3, Franchise.AmplifyPopGain(f, 3));      // no trait — unchanged
        f.StarPower = true;
        Assert.True(Franchise.AmplifyPopGain(f, 3) > 3);      // the camera loves them

        f.Intangibles.Popularity = 40;
        int modest = Franchise.DrawBonus(f);
        f.Intangibles.Popularity = 90;
        Assert.True(Franchise.DrawBonus(f) > modest + 10, "above 60 pop the pull compounds");
    }

    [Fact]
    public void TheFace_FeedsTheBrand_AndThePartnersPayForIt()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
        var promo = world.Promotions.Values.OrderByDescending(p => p.Prestige.Value).First();
        var star = world.RosterOf(promo.Id).OrderByDescending(f => f.Intangibles.Popularity.Value).First();

        decimal partnersBefore = Franchise.PartnerFactor(world, promo);
        Assert.Equal(0, Franchise.StandingQualityBonus(world, promo));

        Franchise.SetFace(world, promo, star, world.CurrentDate);

        Assert.True(Franchise.StandingQualityBonus(world, promo) > 0, "the face's fame is part of the product");
        Assert.True(Franchise.PartnerFactor(world, promo) > partnersBefore, "sponsors and networks pay for the name");
        Assert.Contains(world.News, n => n.Headline.Contains($"builds around {star.FullName}"));
    }

    [Fact]
    public void TheFace_DemandsFaceMoney()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
        var promo = world.Promotions.Values.First(p => !p.Defunct);
        var f = world.RosterOf(promo.Id).First(x => x.Status == FighterStatus.Active);

        var askBefore = new FighterValuation().Evaluate(world, f).AskingPurse;
        promo.FaceOfCompany = f.Id;
        var askAfter = new FighterValuation().Evaluate(world, f).AskingPurse;

        Assert.True(askAfter.Amount > askBefore.Amount, "when the brand runs through you, your agent knows it");
    }

    [Fact]
    public void LosingTheFace_HurtsThroughWhatStops_NotAnExtraFine()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
        var promo = world.Get(world.PlayerPromotion)!;
        var star = world.RosterOf(promo.Id).OrderByDescending(f => f.Intangibles.Popularity.Value).First();
        Franchise.SetFace(world, promo, star, world.CurrentDate);
        int prestigeBefore = promo.PrestigeHundredths;
        decimal partnersWithFace = Franchise.PartnerFactor(world, promo);

        Lifecycle.Retire(world, star, world.CurrentDate);
        Franchise.RunMonthly(world, world.CurrentDate);

        Assert.Null(promo.FaceOfCompany);
        // No prestige fine on top — losing the fighter IS the blow. The real bill:
        // the fame-fed quality bonus and the partner premium simply stop.
        Assert.Equal(prestigeBefore, promo.PrestigeHundredths);
        Assert.Equal(0, Franchise.StandingQualityBonus(world, promo));
        Assert.True(Franchise.PartnerFactor(world, promo) < partnersWithFace, "partners reprice once the name is gone");
        Assert.Contains(world.News, n => n.Headline.Contains("loses its face"));
        Assert.Contains(world.Mail, m => m.Subject.Contains($"We built this around {star.FullName}"));
    }

    [Fact]
    public void AiOrgs_CrownAFace_WhenABankableNameExists()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });

        Franchise.RunMonthly(world, world.CurrentDate);

        var bigOrgs = world.Promotions.Values
            .Where(p => !p.Defunct && p.Id != world.PlayerPromotion
                        && world.RosterOf(p.Id).Any(f => f.Intangibles.Popularity.Value >= 55));
        Assert.All(bigOrgs, p => Assert.NotNull(p.FaceOfCompany));
    }

    [Fact]
    public void Franchise_RoundTrips_ThroughTheSave()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5 });
        var promo = world.Promotions.Values.First(p => !p.Defunct);
        var star = world.RosterOf(promo.Id).First();
        star.StarPower = true;
        promo.FaceOfCompany = star.Id;

        var path = Path.Combine(Path.GetTempPath(), $"franchise-{Guid.NewGuid():N}.db");
        try
        {
            MmaManager.Persistence.SaveGame.Save(world, path);
            var loaded = MmaManager.Persistence.SaveGame.Load(path);

            Assert.True(loaded.Fighters[star.Id].StarPower);
            Assert.Equal(star.Id, loaded.Promotions[promo.Id].FaceOfCompany);
        }
        finally { File.Delete(path); }
    }
}
