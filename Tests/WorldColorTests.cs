using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Flavor;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Org;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Tests;

/// <summary>Match ratings: how good a fight was, from its recorded action.</summary>
public class MatchRatingTests
{
    private static Fight Bout(int strikes, int knockdowns, int subs, FightMethod method, int round = 3) => new()
    {
        Id = new FightId(1),
        RedCorner = new FighterId(1),
        BlueCorner = new FighterId(2),
        Outcome = new FightOutcome
        {
            Winner = new FighterId(1),
            Loser = new FighterId(2),
            Method = method,
            EndRound = round,
            Stats = { [new FighterId(1)] = new FightStatline
            {
                SignificantStrikesLanded = strikes,
                KnockdownsScored = knockdowns,
                SubmissionAttempts = subs,
            } },
        },
    };

    [Fact]
    public void AWar_OutratesADullDecision()
    {
        int war = MatchRating.Of(Bout(strikes: 90, knockdowns: 2, subs: 2, FightMethod.Knockout, round: 3));
        int dull = MatchRating.Of(Bout(strikes: 25, knockdowns: 0, subs: 0, FightMethod.UnanimousDecision));
        Assert.True(war > dull, $"war {war} should outrate dull decision {dull}");
        Assert.True(war >= 68, $"a late KO war should be a 4-5 star fight (got {war})");
    }

    [Fact]
    public void Stars_ShowOnlyEarnedGlyphs()
    {
        // Filled stars only — outline padding made every rating read like five.
        foreach (int r in new[] { 0, 31, 49, 67, 84, 100 })
            Assert.InRange(MatchRating.Stars(r).Length, 1, 5);
        Assert.Equal("★★★★★", MatchRating.Stars(95));
        Assert.Equal("★", MatchRating.Stars(10));
        Assert.Equal("★★★", MatchRating.Stars(55));
        Assert.DoesNotContain('☆', MatchRating.Stars(10));
    }
}

/// <summary>The local circuit: idle fighters keep their careers alive off-screen.</summary>
public class LocalCircuitTests
{
    [Fact]
    public void FreeAgents_FightLocals_AndTheCapableMostlyWin()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 21 });
        // Everyone who can pick up an off-screen local fight: free agents AND
        // non-exclusive contracted fighters (the small halls sign non-exclusive).
        var pool = world.Fighters.Values.Where(f => f.Status == FighterStatus.Active && !f.LocalTalent
            && (f.Contract is null || f.Contract is { IsExclusive: false })).ToList();
        var capable = pool.Where(f => MmaManager.Core.Sim.Ability.FighterAbility.CurrentAbility(f) >= 48).ToList();
        int winsBefore = pool.Sum(f => f.Record.Wins);
        int lossesBefore = pool.Sum(f => f.Record.Losses);
        int capWinsBefore = capable.Sum(f => f.Record.Wins);
        int capLossesBefore = capable.Sum(f => f.Record.Losses);

        int fights = 0;
        for (int m = 1; m <= 6; m++)
        {
            // staged in the monthly pass, fought when the clock reaches the card's date
            LocalCircuit.RunMonthly(world, new DateOnly(2026, m, 28),
                new SimRandom(SimRandom.Mix(world.RngSeed, (ulong)m)));
            fights += LocalCircuit.RunDue(world, new DateOnly(2026, m, 28));
        }

        int winsAdded = pool.Sum(f => f.Record.Wins) - winsBefore;
        int lossesAdded = pool.Sum(f => f.Record.Losses) - lossesBefore;
        Assert.True(fights > 0, "the local circuit should produce fights");
        // Most bouts are pro-vs-local (one pool result each); the occasional
        // pro-vs-pro bout lands a win AND a loss inside the pool.
        Assert.InRange(winsAdded + lossesAdded, fights, fights * 2);
        Assert.True(winsAdded > lossesAdded,
            $"even the leftover pool should edge local talent ({winsAdded}W-{lossesAdded}L)");

        // The circuit's whole point now: every bout on a record is a real,
        // listed fight — club shows included. No mystery numbers.
        Assert.All(pool, f => Assert.Equal(f.Record.TotalBouts, f.BoutHistory.Count));
        var localEvents = world.Events.Values.Where(e => e.Promotion == default(PromotionId)).ToList();
        Assert.NotEmpty(localEvents);
        Assert.All(localEvents, e => Assert.Equal(EventStatus.Completed, e.Status));
        Assert.All(localEvents.SelectMany(e => e.Card), f => Assert.NotNull(f.Outcome));

        // A genuinely capable pro barely ever loses on the local scene.
        int capWins = capable.Sum(f => f.Record.Wins) - capWinsBefore;
        int capLosses = capable.Sum(f => f.Record.Losses) - capLossesBefore;
        Assert.True(capWins >= capLosses * 5,
            $"capable pros should dominate locals ({capWins}W-{capLosses}L)");
    }

    [Fact]
    public void ExclusiveContracts_KeepFightersOffTheLocalScene()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 22 });
        var signed = world.Fighters.Values.Where(f => f.Contract is { IsExclusive: true }).ToList();
        int before = signed.Sum(f => f.Record.TotalBouts);

        for (int m = 1; m <= 6; m++)
        {
            LocalCircuit.RunMonthly(world, new DateOnly(2026, m, 28),
                new SimRandom(SimRandom.Mix(world.RngSeed, (ulong)m)));
            LocalCircuit.RunDue(world, new DateOnly(2026, m, 28));
        }

        Assert.Equal(before, signed.Sum(f => f.Record.TotalBouts));
    }

    [Fact]
    public void NonExclusiveIdleFighter_Moonlights()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 23 });
        var f = world.RosterOf(world.PlayerPromotion).First(x => x.Status == FighterStatus.Active);
        f.Contract!.IsExclusive = false;
        int before = f.Record.TotalBouts;

        for (int m = 1; m <= 12; m++)
        {
            LocalCircuit.RunMonthly(world, new DateOnly(2026, m, 28),
                new SimRandom(SimRandom.Mix(world.RngSeed, (ulong)(100 + m))));
            LocalCircuit.RunDue(world, new DateOnly(2026, m, 28));
        }

        Assert.True(f.Record.TotalBouts > before, "a non-exclusive idle fighter should take local fights");
    }
}

/// <summary>The early filter: failure quits, so records self-select.</summary>
public class WashoutTests
{
    [Fact]
    public void NobodyGrindsOutADreadfulRecord_TheyQuitFirst()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 31 });
        new WorldTick().AdvanceWeeks(world, 156);   // three years of the filter at work

        var active = world.Fighters.Values
            .Where(f => f.Status == FighterStatus.Active && !f.LocalTalent).ToList();

        // The 0-10 fighter doesn't exist: winless careers end long before ten bouts.
        Assert.DoesNotContain(active, f => f.Record.Wins == 0 && f.Record.Losses >= 6);

        // Deep losing careers are RARE, not extinct — real regional MMA carries
        // some 4-12 journeymen; it's the winless who don't exist. Cap the share.
        int hopeless = active.Count(f => f.Record.TotalBouts >= 10
            && f.Record.Wins / (double)f.Record.TotalBouts < 0.3);
        // (~2.5% cap: a signed journeyman rides out his deal for the guaranteed
        // money — the washout reckons with him when it ends and nobody calls.)
        Assert.True(hopeless <= Math.Max(3, active.Count / 40),
            $"the washout should keep hopeless records rare ({hopeless} of {active.Count})");

        // And the base of the pyramid stays wide: plenty of green fighters working their way in.
        int green = active.Count(f => f.Record.TotalBouts <= 4);
        Assert.True(green >= active.Count / 6, $"the world should carry a wide green base ({green} of {active.Count})");
    }
}

/// <summary>Companies can die — and so can yours.</summary>
public class SolvencyTests
{
    [Fact]
    public void AnInsolventAiPromotion_ClosesItsDoors()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 31 });
        var victim = world.Promotions.Values.First(p => p.Id != world.PlayerPromotion);
        victim.Balance = new Money(-12_000_000m);   // past even a top brand's deep debt ceiling
        int rosterBefore = world.RosterOf(victim.Id).Count();
        Assert.True(rosterBefore > 0);

        new WorldTick().AdvanceWeeks(world, 5);   // crosses a month boundary
        var closedOn = world.CurrentDate;

        Assert.True(victim.Defunct);
        Assert.Empty(world.RosterOf(victim.Id));
        Assert.All(victim.Titles, tid => Assert.Null(world.Get(tid)!.CurrentChampion));
        Assert.DoesNotContain(world.Events.Values, e => e.Promotion == victim.Id && e.Status == EventStatus.Scheduled);
        Assert.Contains(world.News, n => n.Headline.Contains("closes its doors"));

        // ...and the world keeps ticking without them.
        new WorldTick().AdvanceWeeks(world, 8);
        Assert.DoesNotContain(world.Events.Values,
            e => e.Promotion == victim.Id && e.Date > closedOn && e.Status != EventStatus.Cancelled);
    }

    [Fact]
    public void ThePlayer_CanGoBankrupt()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 32 });
        var player = world.Get(world.PlayerPromotion)!;
        player.Balance = new Money(-12_000_000m);   // deep enough to fold a promotion of any size

        new WorldTick().AdvanceWeeks(world, 5);

        Assert.True(player.Defunct);
        Assert.Contains(world.Mail, m => m.Subject.Contains("It's over"));
    }

    [Fact]
    public void DefunctAndAccolades_SurviveSaveLoad()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 33 });
        var promo = world.Promotions.Values.First(p => p.Id != world.PlayerPromotion);
        Solvency.Close(world, promo, world.CurrentDate);
        var f = world.Fighters.Values.First();
        f.Accolades.Add(new Accolade(world.CurrentDate, "2026 Fighter of the Year"));

        string path = Path.Combine(Path.GetTempPath(), $"solvency-{Guid.NewGuid():N}.mmasave");
        try
        {
            MmaManager.Persistence.SaveGame.Save(world, path);
            var loaded = MmaManager.Persistence.SaveGame.Load(path);
            Assert.True(loaded.Get(promo.Id)!.Defunct);
            Assert.Equal("2026 Fighter of the Year", Assert.Single(loaded.Get(f.Id)!.Accolades).Title);
        }
        finally { File.Delete(path); }
    }
}

/// <summary>The wider combat-sports calendar (Olympics, world championships).</summary>
public class SpecialCircuitTests
{
    [Fact]
    public void WorldWrestlingChampionships_CrownMedalists_EveryAugust()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 41 });
        var rng = new SimRandom(7);

        SpecialCircuit.RunMonthly(world, new DateOnly(2026, 8, 28), rng);

        Assert.Contains(world.News, n => n.Headline.Contains("World Wrestling Championships gold"));
        var gold = world.Fighters.Values.First(f => f.Accolades.Any(a => a.Title.Contains("World Wrestling Championships — gold")));
        Assert.Equal(PrimaryStyle.Wrestler, gold.PrimaryStyle);
    }

    [Fact]
    public void Olympics_OnlyHappenEveryFourYears()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 42 });
        var rng = new SimRandom(7);

        SpecialCircuit.RunMonthly(world, new DateOnly(2027, 7, 28), rng);
        Assert.DoesNotContain(world.News, n => n.Headline.Contains("Olympic"));

        SpecialCircuit.RunMonthly(world, new DateOnly(2028, 7, 28), rng);
        Assert.Contains(world.News, n => n.Headline.Contains("Olympic Games (combat sports) gold"));
    }
}

/// <summary>End-of-year honours land as news and permanent profile accolades.</summary>
public class YearlyAwardsTests
{
    [Fact]
    public void ARunYear_ProducesTheHonoursList()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 51 });
        new WorldTick().AdvanceWeeks(world, 54);   // through Jan 1

        Assert.Contains(world.News, n => n.Headline.StartsWith("Fight of the Year:"));
        Assert.Contains(world.News, n => n.Headline.StartsWith("Fighter of the Year:"));
        Assert.Contains(world.News, n => n.Headline.StartsWith("Event of the Year:"));
        // Award winners carry it on their profile.
        Assert.Contains(world.Fighters.Values, f => f.Accolades.Any(a => a.Title.Contains("Fight of the Year")));
        Assert.Contains(world.Fighters.Values, f => f.Accolades.Any(a => a.Title.Contains("Fighter of the Year")));
    }
}
