using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;

namespace MmaManager.Tests;

/// <summary>
/// Persona tags — the marketable look baked into a fighter's portrait (glam ·
/// adonis · menace · babyface), stamped onto <see cref="Fighter.PersonaTag"/>
/// by the art layer. Effects stay flavor/popularity-side: draw power and
/// popularity growth, never competitive stats.
/// </summary>
public class PersonaTests
{
    private static Event RunHeadlinedBy(World world, Promotion promo, Fighter a, Fighter b, WeightClass wc)
    {
        var svc = new EventService();
        var ev = svc.CreateEvent(world, promo.Id, "Main", world.CurrentDate,
            new[] { new Matchup(a.Id, b.Id, wc, 5, false, null) });
        svc.RunEvent(world, ev);
        return ev;
    }

    private static (World World, Promotion Promo, List<Fighter> Pair) TopPair(ulong seed)
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed, UseDesignedRoster = false });
        var promo = world.Promotions.Values.OrderByDescending(p => p.Prestige.Value).First();
        var pair = world.RosterOf(promo.Id)
            .Where(f => f.WeightClass == WeightClass.Lightweight && f.Status == FighterStatus.Active)
            .Take(2).ToList();
        return (world, promo, pair);
    }

    [Fact]
    public void CoverStarPersona_LiftsTheHouse()
    {
        // Trim the org's draw (both worlds identically) so the house sits below
        // venue capacity — a sold-out building hides the lift being measured.
        var (world, promo, pair) = TopPair(seed: 5);
        promo.Prestige = 45;
        var baseline = RunHeadlinedBy(world, promo, pair[0], pair[1], WeightClass.Lightweight);

        var (world2, promo2, pair2) = TopPair(seed: 5);
        promo2.Prestige = 45;
        pair2[0].PersonaTag = "adonis";
        var starred = RunHeadlinedBy(world2, promo2, pair2[0], pair2[1], WeightClass.Lightweight);

        Assert.True(starred.Financials!.Attendance > baseline.Financials!.Attendance,
            $"expected the cover athlete to outdraw the baseline ({starred.Financials.Attendance} vs {baseline.Financials.Attendance})");
    }

    [Fact]
    public void BabyfaceWinner_GrowsPopularityTwiceAsFast()
    {
        var (world, promo, pair) = TopPair(seed: 9);
        int plainBefore = pair[0].Intangibles.Popularity.Value;
        var ev = RunHeadlinedBy(world, promo, pair[0], pair[1], WeightClass.Lightweight);
        var winner = world.Get(ev.Card[^1].Outcome!.Winner!.Value)!;   // headliner runs as the main event (last)
        int plainGain = winner.Intangibles.Popularity.Value
                        - (winner == pair[0] ? plainBefore : pair[1].Intangibles.Popularity.Value);

        // Same fight, same seed — but the winner-to-be is a babyface this time.
        var (world2, promo2, pair2) = TopPair(seed: 9);
        var winner2 = pair2.Single(f => f.Id == winner.Id);
        winner2.PersonaTag = "babyface";
        int before2 = winner2.Intangibles.Popularity.Value;
        RunHeadlinedBy(world2, promo2, pair2[0], pair2[1], WeightClass.Lightweight);

        int babyfaceGain = winner2.Intangibles.Popularity.Value - before2;
        Assert.True(plainGain > 0, "winning should always grow popularity a little");
        Assert.Equal(plainGain * 2, babyfaceGain);
    }

    [Fact]
    public void ALongLosingSkid_ErodesAStar_OffTheMarquee()
    {
        var (world, promo, pair) = TopPair(seed: 13);
        var star = pair[0];
        var beater = pair[1];
        star.Intangibles.Popularity = 78;                 // a genuine headline draw
        int start = star.Intangibles.Popularity.Value;

        // A single loss barely dents a big name.
        ApplyDecisionLoss(world, promo, star, beater);
        int afterOne = star.Intangibles.Popularity.Value;
        Assert.True(start - afterOne <= 4, $"one loss shouldn't tank a star: dropped {start - afterOne}");

        // But losing for years compounds — they slide well off the marquee.
        for (int i = 0; i < 11; i++) ApplyDecisionLoss(world, promo, star, beater);
        Assert.True(star.Intangibles.Popularity.Value < 40,
            $"a long skid should erode a star's draw: {star.Intangibles.Popularity.Value}");
    }

    private static void ApplyDecisionLoss(World world, Promotion promo, Fighter loser, Fighter winner)
    {
        var fight = new Fight
        {
            Id = world.NewFightId(), RedCorner = winner.Id, BlueCorner = loser.Id,
            WeightClass = loser.WeightClass, ScheduledRounds = 3,
            Outcome = new FightOutcome
            {
                Winner = winner.Id, Loser = loser.Id, Method = FightMethod.UnanimousDecision,
                EndRound = 3, EndTimeSeconds = 900,
            },
        };
        var ev = new Event { Id = world.NewEventId(), Promotion = promo.Id, Name = "L", Date = world.CurrentDate };
        ev.Card.Add(fight);
        ResultsProcessor.Apply(world, ev, fight);
    }

    [Fact]
    public void PersonaAndDesignedFlag_SurviveSaveLoad()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 3 });
        var tagged = world.Fighters.Values.First(f => !f.Designed);
        tagged.PersonaTag = "menace";
        var designed = world.Fighters.Values.First(f => f.Designed);

        string path = Path.Combine(Path.GetTempPath(), $"persona-{Guid.NewGuid():N}.mmasave");
        try
        {
            MmaManager.Persistence.SaveGame.Save(world, path);
            var loaded = MmaManager.Persistence.SaveGame.Load(path);

            Assert.Equal("menace", loaded.Get(tagged.Id)!.PersonaTag);
            Assert.True(loaded.Get(designed.Id)!.Designed);
            Assert.False(loaded.Get(tagged.Id)!.Designed);
        }
        finally { File.Delete(path); }
    }
}
