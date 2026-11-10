using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Generation;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Tests;

/// <summary>
/// Fights leave a mark: after every bout a fighter needs a recovery window before
/// they can be booked again (longer after a war or a knockout, longer still for
/// an older fighter or one with a poor recovery engine), and an injury shelves
/// them longer. Matchmaking honours the clock.
/// </summary>
public class RecoveryTests
{
    private static readonly DateOnly Day = new(2026, 6, 1);

    // A free-agent fighter of a chosen age and recovery stat.
    private static Fighter MakeFighter(World world, int id, int age, int recovery)
    {
        var f = new FighterGenerator(world.RngSeed).Generate(new FighterId(id), world.CurrentDate,
            new GenOptions { Age = age, WeightClass = WeightClass.Lightweight });
        f.Physical.Recovery = recovery;
        f.Contract = null;
        world.Fighters[f.Id] = f;
        return f;
    }

    // A completed 3-round draw between two fighters (a draw so neither carries a
    // "took the beating" penalty — isolates age/recovery), then run the post-fight
    // recovery/injury pass.
    private static Event RunDraw(World world, Fighter a, Fighter b, SimRandom rng)
    {
        var ev = new Event { Id = world.NewEventId(), Promotion = default, Name = "Test", Date = world.CurrentDate };
        ev.Card.Add(new Fight
        {
            Id = world.NewFightId(), RedCorner = a.Id, BlueCorner = b.Id, WeightClass = WeightClass.Lightweight,
            ScheduledRounds = 3,
            Outcome = new FightOutcome { Winner = null, Loser = null, Method = FightMethod.SplitDecision, EndRound = 3, EndTimeSeconds = 900 },
        });
        InjuryModel.RollForEvent(world, ev, world.CurrentDate, rng);
        return ev;
    }

    [Fact]
    public void AfterAFight_AFighterNeedsRecoveryBeforeTheyCanBeBookedAgain()
    {
        var world = new World { CurrentDate = Day, RngSeed = 3 };
        var a = MakeFighter(world, 1, age: 27, recovery: 55);
        var b = MakeFighter(world, 2, age: 27, recovery: 55);

        RunDraw(world, a, b, new SimRandom(1));   // a clean night — both walk away healthy
        Assert.Null(a.CurrentInjury);
        Assert.Null(b.CurrentInjury);

        // Not bookable the same day...
        Assert.False(a.IsMatchReady(Day));
        Assert.False(b.IsMatchReady(Day));
        // ...but back within a couple of months (a routine turnaround).
        Assert.True(a.IsMatchReady(Day.AddDays(70)));
        Assert.True(b.IsMatchReady(Day.AddDays(70)));
    }

    [Fact]
    public void OlderAndPoorerRecovery_MeansALongerLayoff()
    {
        // Same fight, same dice — the only difference is who's recovering.
        var w1 = new World { CurrentDate = Day, RngSeed = 3 };
        var young = MakeFighter(w1, 1, age: 24, recovery: 90);   // young, elite engine
        RunDraw(w1, young, MakeFighter(w1, 2, age: 24, recovery: 90), new SimRandom(100));

        var w2 = new World { CurrentDate = Day, RngSeed = 3 };
        var veteran = MakeFighter(w2, 1, age: 39, recovery: 25);  // old, poor engine
        RunDraw(w2, veteran, MakeFighter(w2, 2, age: 39, recovery: 25), new SimRandom(100));

        Assert.NotNull(young.ReadyToFightOn);
        Assert.NotNull(veteran.ReadyToFightOn);
        Assert.True(veteran.ReadyToFightOn > young.ReadyToFightOn,
            $"the veteran should recover slower: vet {veteran.ReadyToFightOn} vs young {young.ReadyToFightOn}");
    }

    [Fact]
    public void TheMatchmaker_WontBookAStillRecoveringFighter()
    {
        var world = new LeagueBuilder().Build(new LeagueBuilder.Options { Seed = 7, RosterSize = 90 });
        var promo = world.Get(world.PlayerPromotion)!;

        // Shelve a whole division into recovery.
        var lws = world.RosterOf(promo.Id).Where(f => f.WeightClass == WeightClass.Lightweight).ToList();
        foreach (var f in lws) f.ReadyToFightOn = world.CurrentDate.AddDays(30);

        var card = new Matchmaker().BuildCard(world, promo.Id, targetFights: 12);

        foreach (var m in card)
        {
            Assert.DoesNotContain(m.Red, lws.Select(f => f.Id));
            Assert.DoesNotContain(m.Blue, lws.Select(f => f.Id));   // none of the recovering fighters got booked
        }
    }
}
