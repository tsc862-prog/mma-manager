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

        // The 0-10 fighter doesn't exist: winless careers end long before ten bouts. Nobody active is 0-6 or worse
        // (the winless wall retires a man the week after his fifth loss without a win, signed or not: a deal used
        // to carry a signed 0-5 between the monthly washout rolls, and before the wall one such man stood in 4 of
        // seeds 21-40 after three years).
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

    [Fact]
    public void ASignedWinlessManAtTheWall_DoesNotFightAgain()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 31 });
        new WorldTick().AdvanceWeeks(world, 4);   // the houses have their next cards on the books

        // A signed man on an AI house's card a week or two out, made 0-5 (the loss that made it is behind him).
        var (card, man) = world.Events.Values
            .Where(e => e.Status == EventStatus.Scheduled && e.Promotion != world.PlayerPromotion && world.Get(e.Promotion) is not null
                        && e.Date > world.CurrentDate.AddDays(7) && e.Date <= world.CurrentDate.AddDays(21))
            .OrderBy(e => e.Date).ThenBy(e => e.Id.Value)
            .SelectMany(e => e.Card.Where(x => !x.IsTitleFight).Select(x => (Card: e, Man: world.Get(x.RedCorner)!)))
            .First(p => p.Man is { LocalTalent: false, Status: FighterStatus.Active } m && m.Contract?.Promotion == p.Card.Promotion
                        && m.Contract.FightsRemaining >= 2);
        man.Record.Wins = man.Record.WinsByKo = man.Record.WinsBySubmission = man.Record.WinsByDecision = 0;
        man.Record.Draws = man.Record.NoContests = 0;
        man.Record.Losses = Lifecycle.WinlessWall;
        man.Record.CurrentStreak = -Lifecycle.WinlessWall;
        int bouts = man.BoutHistory.Count;

        // Main: the monthly roll (30% while signed) is the only thing that could stop him, and he fights the card.
        new WorldTick().AdvanceWeeks(world, 4);

        Assert.True(card.Date <= world.CurrentDate);
        Assert.Equal(bouts, man.BoutHistory.Count);   // (main: he fought the card)
        Assert.Equal(FighterStatus.Retired, man.Status);
        Assert.Null(man.Contract);
        Assert.DoesNotContain(card.Card, x => x.RedCorner == man.Id || x.BlueCorner == man.Id);
        Assert.NotEqual(EventStatus.Scheduled, card.Status);   // the card ran (or was called off) without him
    }

    [Fact]
    public void ThePlayersWinlessManAtTheWall_QuitsAndThePlayerIsTold()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 31 });
        var man = world.RosterOf(world.PlayerPromotion).OrderBy(f => f.Id.Value).First(f => !f.LocalTalent);
        man.Record.Wins = man.Record.WinsByKo = man.Record.WinsBySubmission = man.Record.WinsByDecision = 0;
        man.Record.Draws = man.Record.NoContests = 0;
        man.Record.Losses = Lifecycle.WinlessWall;

        new WorldTick().AdvanceWeeks(world, 1);

        Assert.Equal(FighterStatus.Retired, man.Status);
        Assert.Contains(world.Mail, m => m.Subject == $"{man.FullName} has quit the sport" && m.Body.Contains("0-5"));
    }

    [Fact]
    public void AWinlessManShortOfTheWall_IsLeftToTheMonthlyRoll()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 31 });
        var man = world.Fighters.Values.OrderBy(f => f.Id.Value)
            .First(f => f is { LocalTalent: false, Status: FighterStatus.Active, Contract: not null } && f.Contract.Promotion != world.PlayerPromotion);
        man.Record.Wins = man.Record.WinsByKo = man.Record.WinsBySubmission = man.Record.WinsByDecision = 0;
        man.Record.Draws = man.Record.NoContests = 0;
        man.Record.Losses = Lifecycle.WinlessWall - 1;

        Lifecycle.RetireTheWinless(world, world.CurrentDate);
        Assert.NotEqual(FighterStatus.Retired, man.Status);
        man.Record.Losses = Lifecycle.WinlessWall;
        Assert.True(Lifecycle.RetireTheWinless(world, world.CurrentDate) >= 1);
        Assert.Equal(FighterStatus.Retired, man.Status);
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
        // Nobody can carry it (a buyer must be out of the red), so it closes rather
        // than being sold at the ceiling (Acquisitions.TryRescue; see BigMoneyTests).
        int rosterBefore = world.RosterOf(victim.Id).Count();
        Assert.True(rosterBefore > 0);

        // Held there week by week, at half each house's own debt ceiling: far enough under water that no week's
        // gates and television (a flagship's best week is a few million) float it back into the black, and far
        // enough above the ceiling that the month's turn does not close it too. (At -1, set once, a giant filling
        // its rooms (RoomFit) was back in the black inside five weeks and bought the victim.)
        var tick = new WorldTick();
        for (int w = 0; w < 5; w++)   // crosses a month boundary
        {
            foreach (var p in world.Promotions.Values.Where(p => p.Id != victim.Id && !p.Defunct))
                p.Balance = new Money(MmaManager.Core.Sim.Org.Solvency.CeilingFor(p, world) / 2);
            tick.AdvanceWeeks(world, 1);
        }
        Assert.Single(world.Promotions.Values, p => p.Defunct);   // the victim alone
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
