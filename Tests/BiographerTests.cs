using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Flavor;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Tests;

public class BiographerTests
{
    [Fact]
    public void FighterBio_IsWritten_Deterministic_AndMentionsTheFighter()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5 });
        new WorldTick().AdvanceWeeks(world, 20);
        // A procedural fighter — designed ones lead with authored prose instead.
        var f = world.Fighters.Values.First(x => x.Status == FighterStatus.Active && x.Bio is null);

        var bio = Biographer.Fighter(world, f);

        Assert.False(string.IsNullOrWhiteSpace(bio));
        Assert.Contains(f.FullName, bio);
        Assert.Equal(bio, Biographer.Fighter(world, f));   // deterministic
    }

    [Fact]
    public void AuthoredBio_LeadsTheStory_AndTheLiveCareerLineFollows()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5 });
        var mercer = world.Fighters.Values.First(f => f.LastName == "Mercer" && f.Designed);

        var bio = Biographer.Fighter(world, mercer);

        Assert.StartsWith("The face of the sport", bio);   // the authored backstory leads
        Assert.Contains("champion", bio);                  // the live career line still follows
    }

    [Fact]
    public void CrossoverBio_NotesThePastLife()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5, PlayerPromotionIndex = 8 });
        // The first crossover (a former boxing world champion) fires in July 2026.
        WorldEvents.RunMonthly(world, new DateOnly(2026, 7, 1), new SimRandom(1234));
        var star = world.Fighters.Values.First(f => f.Accolades.Any(a => a.Title.Contains("crossover")));

        var bio = Biographer.Fighter(world, star);

        Assert.Contains("boxing world champion", bio);   // the past life is the story
        Assert.Contains("crossing over", bio);
    }

    [Fact]
    public void ChampionBio_NotesTheBelt()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5 });
        var title = world.Titles.Values.First(t => t.CurrentChampion is not null);
        var champ = world.Get(title.CurrentChampion!.Value)!;

        Assert.Contains("champion", Biographer.Fighter(world, champ));
    }

    [Fact]
    public void PromotionBio_NamesTheOrgAndSize()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5 });
        var promo = world.Promotions.Values.OrderByDescending(p => p.Prestige.Value).First();

        var bio = Biographer.Promotion(world, promo);

        Assert.Contains(promo.Name, bio);          // the authored lore names the house
        Assert.Contains("weight classes", bio);    // …and the live facts still follow

        // An org with no authored lore (a procedural startup) still gets the
        // derived description.
        var startup = new Promotion { Id = new PromotionId(999), Name = "Test Fights", Abbreviation = "TF" };
        Assert.Contains("promotion", Biographer.Promotion(world, startup));
    }
}
