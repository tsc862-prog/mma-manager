using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;

namespace MmaManager.Tests;

public class BookingTests
{
    private static World BuildLeague(ulong seed = 1, int roster = 120) =>
        new LeagueBuilder().Build(new LeagueBuilder.Options { Seed = seed, RosterSize = roster });

    [Fact]
    public void LeagueBuilder_SignsRosterAndSeedsDivisions()
    {
        var world = BuildLeague();
        var promo = world.Get(world.PlayerPromotion)!;

        Assert.Equal(9, promo.Divisions.Count);
        Assert.All(world.RosterOf(promo.Id), f => Assert.NotNull(f.Contract));
        Assert.Equal(world.Fighters.Count, world.RosterOf(promo.Id).Count()); // everyone signed

        foreach (var div in promo.Divisions)
        {
            var champ = world.Get(div.Title!.Value)!.CurrentChampion;
            Assert.NotNull(champ);
            // Champion is tracked on the title, never inside the contender list.
            Assert.DoesNotContain(champ!.Value, div.Rankings);
        }
    }

    [Fact]
    public void Matchmaker_NeverDoubleBooksOrBooksAChampionOnTheUndercard()
    {
        var world = BuildLeague();
        var promo = world.Get(world.PlayerPromotion)!;
        var champions = promo.Divisions
            .Select(d => world.Get(d.Title!.Value)!.CurrentChampion!.Value)
            .ToHashSet();

        var card = new Matchmaker().BuildCard(world, promo.Id, targetFights: 9);

        var seen = new HashSet<FighterId>();
        foreach (var m in card)
        {
            Assert.True(seen.Add(m.Red), "a fighter was booked twice");
            Assert.True(seen.Add(m.Blue), "a fighter was booked twice");
            Assert.NotEqual(m.Red, m.Blue);
            if (!m.IsTitle)
            {
                Assert.DoesNotContain(m.Red, champions);
                Assert.DoesNotContain(m.Blue, champions);
            }
        }
    }

    [Fact]
    public void RunEvent_UpdatesRecordsAndBoutHistory()
    {
        var world = BuildLeague();
        var promoId = world.PlayerPromotion;
        var events = new EventService();

        // Book first so we can snapshot records before the card is simulated —
        // fighters start with a generated backstory record, so we check the delta.
        var card = new Matchmaker().BuildCard(world, promoId, targetFights: 8);
        var ev = events.CreateEvent(world, promoId, "Test Card", world.CurrentDate, card);
        var boutsBefore = ev.Card
            .SelectMany(f => new[] { f.RedCorner, f.BlueCorner })
            .Distinct()
            .ToDictionary(id => id, id => world.Get(id)!.Record.TotalBouts);

        events.RunEvent(world, ev);

        Assert.Equal(EventStatus.Completed, ev.Status);
        Assert.NotNull(ev.Financials);

        foreach (var fight in ev.Card)
        {
            Assert.NotNull(fight.Outcome);
            var red = world.Get(fight.RedCorner)!;
            var blue = world.Get(fight.BlueCorner)!;
            Assert.Contains(fight.Id, red.BoutHistory);
            Assert.Contains(fight.Id, blue.BoutHistory);
            // Exactly one bout's worth of results landed on each corner.
            Assert.Equal(boutsBefore[fight.RedCorner] + 1, red.Record.TotalBouts);
            Assert.Equal(boutsBefore[fight.BlueCorner] + 1, blue.Record.TotalBouts);
        }
    }

    [Fact]
    public void TitleFight_Upset_SwapsTheBeltAndLineage()
    {
        var world = BuildLeague(seed: 5);
        var promo = world.Get(world.PlayerPromotion)!;
        var div = promo.Divisions.First(d => d.Rankings.Count > 0);
        var title = world.Get(div.Title!.Value)!;

        var champ = title.CurrentChampion!.Value;
        var challenger = div.Rankings[0];

        // Force a title fight the challenger wins (draw an outcome directly).
        var fight = new Fight
        {
            Id = world.NewFightId(),
            RedCorner = champ,
            BlueCorner = challenger,
            WeightClass = div.WeightClass,
            IsTitleFight = true,
            Title = title.Id,
            ScheduledRounds = 5,
            Outcome = new FightOutcome
            {
                Winner = challenger,
                Loser = champ,
                Method = FightMethod.Knockout,
                EndRound = 2,
                EndTimeSeconds = 130,
            },
        };
        var ev = new Event { Id = world.NewEventId(), Promotion = promo.Id, Name = "Belt", Date = world.CurrentDate };
        ev.Card.Add(fight);

        ResultsProcessor.Apply(world, ev, fight);

        Assert.Equal(challenger, title.CurrentChampion);          // belt changed hands
        Assert.DoesNotContain(challenger, div.Rankings);          // new champ leaves the rankings
        Assert.Contains(champ, div.Rankings);                     // ex-champ enters the rankings
        Assert.Equal(2, title.Lineage.Count);                     // two reigns now
        Assert.NotNull(title.Lineage[0].LostOn);                  // old reign closed out
    }

    // Leave only one belt contestable, so we know which title bout will headline.
    private static Division IsolateBeltInDivision(World world, Promotion promo, Func<Division, bool> keep)
    {
        Division? kept = null;
        foreach (var d in promo.Divisions)
        {
            if (d.Title is not { } tid || world.Get(tid) is not { CurrentChampion: { } cid } || world.Get(cid) is not { } champ)
                continue;
            if (keep(d)) { kept = d; continue; }
            champ.Status = FighterStatus.Injured;   // unavailable → no defence, but the belt stays filled
        }
        return kept!;
    }

    private static void MakeWorthy(Fighter f) { f.Status = FighterStatus.Active; f.CurrentInjury = null; f.Record.Wins = 12; f.Record.Losses = 2; f.Record.CurrentStreak = 3; }

    [Fact]
    public void VacantBelt_IsContested_AsTheMainEvent()
    {
        var world = BuildLeague(seed: 8);
        var promo = world.Get(world.PlayerPromotion)!;
        var div = promo.Divisions.First(d => d.Rankings.Count >= 2);
        var title = world.Get(div.Title!.Value)!;
        title.CurrentChampion = null;                        // the belt is vacant

        // Only this division can produce a title bout, so it must headline.
        IsolateBeltInDivision(world, promo, d => d == div);
        var top = div.Rankings.Take(2).ToList();

        var card = new Matchmaker().BuildCard(world, promo.Id, targetFights: 8);

        var main = card[^1];
        Assert.True(main.IsTitle, "a vacant belt should be contested as the main event");
        Assert.Equal(title.Id, main.Title);
        Assert.Contains(main.Red, top);
        Assert.Contains(main.Blue, top);                     // top two contenders fight for it
    }

    [Fact]
    public void TitleChallenger_IsNotAnImmediateRematch()
    {
        var world = BuildLeague(seed: 11);
        var promo = world.Get(world.PlayerPromotion)!;
        var div = promo.Divisions.First(d => d.Rankings.Count >= 2);
        var title = world.Get(div.Title!.Value)!;
        var champ = world.Get(title.CurrentChampion!.Value)!;
        var r0 = world.Get(div.Rankings[0])!;                // #1 contender
        var r1 = world.Get(div.Rankings[1])!;                // #2 contender
        MakeWorthy(champ); MakeWorthy(r0); MakeWorthy(r1);
        IsolateBeltInDivision(world, promo, d => d == div);

        // The champ just beat the #1 contender — no instant rematch owed.
        var past = new Event { Id = world.NewEventId(), Promotion = promo.Id, Name = "Prev", Date = world.CurrentDate.AddDays(-30), Status = EventStatus.Completed };
        past.Card.Add(new Fight
        {
            Id = world.NewFightId(), RedCorner = champ.Id, BlueCorner = r0.Id, WeightClass = div.WeightClass,
            IsTitleFight = true, Title = title.Id, ScheduledRounds = 5,
            Outcome = new FightOutcome { Winner = champ.Id, Loser = r0.Id, Method = FightMethod.UnanimousDecision, EndRound = 5, EndTimeSeconds = 300 },
        });
        world.Events[past.Id] = past;

        var main = new Matchmaker().BuildCard(world, promo.Id, targetFights: 8)[^1];

        Assert.True(main.IsTitle);
        Assert.Equal(champ.Id, main.Red);
        Assert.Equal(r1.Id, main.Blue);                      // the fresh contender, not the rematch
    }

    [Fact]
    public void TitleChallenger_MustBeWorthy_ALosingContenderIsPassedOver()
    {
        var world = BuildLeague(seed: 14);
        var promo = world.Get(world.PlayerPromotion)!;
        var div = promo.Divisions.First(d => d.Rankings.Count >= 2);
        var title = world.Get(div.Title!.Value)!;
        var champ = world.Get(title.CurrentChampion!.Value)!;
        var r0 = world.Get(div.Rankings[0])!;
        var r1 = world.Get(div.Rankings[1])!;
        MakeWorthy(champ); MakeWorthy(r1);
        IsolateBeltInDivision(world, promo, d => d == div);

        // The #1-ranked contender is on a cold streak with a losing record.
        r0.Status = FighterStatus.Active; r0.CurrentInjury = null;
        r0.Record.Wins = 4; r0.Record.Losses = 11; r0.Record.CurrentStreak = -3;

        var main = new Matchmaker().BuildCard(world, promo.Id, targetFights: 8)[^1];

        Assert.True(main.IsTitle);
        Assert.Equal(r1.Id, main.Blue);                      // the deserving contender gets the shot
    }

    [Fact]
    public void RunningACard_MovesThePromotionBalance()
    {
        var world = BuildLeague();
        var promo = world.Get(world.PlayerPromotion)!;
        var before = promo.Balance;
        new EventService().BookAndRun(world, promo.Id, "Money", world.CurrentDate, new Matchmaker());
        Assert.NotEqual(before, promo.Balance);
    }

    [Fact]
    public void Ranking_RewardsBeatingTopOpposition()
    {
        var world = new LeagueBuilder().Build(new LeagueBuilder.Options { Seed = 15, RosterSize = 90 });
        var promo = world.Get(world.PlayerPromotion)!;
        var div = promo.Divisions.First(d => d.Rankings.Count >= 4);
        var champ = world.Get(div.Title!.Value)!.CurrentChampion!.Value;
        var underdog = div.Rankings[^1];                 // starts at the bottom of the rankings

        Assert.NotEqual(div.Rankings[0], underdog);

        // A marquee, non-title win over the champion.
        var fight = new Fight
        {
            Id = world.NewFightId(),
            RedCorner = underdog,
            BlueCorner = champ,
            WeightClass = div.WeightClass,
            ScheduledRounds = 3,
            Outcome = new FightOutcome
            {
                Winner = underdog,
                Loser = champ,
                Method = FightMethod.Knockout,
                EndRound = 1,
                EndTimeSeconds = 90,
            },
        };
        var ev = new Event { Id = world.NewEventId(), Promotion = promo.Id, Name = "Upset", Date = world.CurrentDate };
        ev.Card.Add(fight);
        world.Events[ev.Id] = ev;   // recompute scans the world's events

        ResultsProcessor.Apply(world, ev, fight);
        RankingService.RecomputePromotion(world, promo, world.CurrentDate);

        // Beating the champion vaults the underdog to #1 contender.
        Assert.Equal(underdog, div.Rankings[0]);
    }
}
