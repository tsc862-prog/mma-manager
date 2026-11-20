using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Finance;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Scouting;

namespace MmaManager.Tests;

/// <summary>
/// An AI house on television books the top of its card for its network (today's mode). A media deal's star clause
/// ("a main-eventer people know") is judged on the better-known man of the bout that runs LAST; before this rule set
/// the matchmaker never looked at it (the headliner was the longest-idle belt or the strongest pairing) and 81% of
/// the flagship's media strikes were that clause (longsim, 40 seeds). Four of these tests fail on main: the
/// longest-idle belt headlined over a belt whose bout clears the clause; the strongest non-title pairing headlined
/// over a strong pairing with a star in it; a card close to its date kept a main event under the clause with a
/// main-card bout above it; the same with the clause on a PPV carrier's night. The rest guard what must not
/// change: a belt whose champion is overdue stays on top, nobody clears the clause (the usual order), no clause, a co-main pick, a star on the prelims, a main event
/// that clears it, the player's house, the 1993 world.
/// Every scene is built two years past the generated history, so only the bouts staged here are recent.
/// </summary>
public class HeadlinerStarClauseTests
{
    private sealed record Belt(Division D, Title T, Fighter Champ, List<Fighter> Ranked, Fighter Spare);
    private sealed record Scene(World W, Promotion P, Belt A, Belt B);

    private const int Clause = 70;

    private static void Ready(Fighter f, int pop)
    {
        f.Status = FighterStatus.Active;
        f.CurrentInjury = null;
        f.ReadyToFightOn = null;
        f.SuspendedUntil = null;
        f.Camp = null;
        f.Record.Wins = 12; f.Record.Losses = 2; f.Record.CurrentStreak = 2;
        if (f.Contract is { } c) c.FightsRemaining = Math.Max(c.FightsRemaining, 3);
        f.Intangibles.Popularity = pop;
    }

    // An AI house with two titled men's divisions, each with a champion and five ranked, ready, in-form men plus a
    // spare, everyone at popularity 40; one network deal holding the main event to popularity 70. Belt A's champion
    // last fought <paramref name="idleA"/> days ago (90: inside Matchmaker.ChampionWaitMaxDays), belt B's 30, so A is
    // the longer-idle belt. Every other division's rankings are emptied.
    private static Scene Build(int idleA = 90, ulong seed = 7)
    {
        var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed, UseDesignedRoster = false });
        w.CurrentDate = w.CurrentDate.AddYears(2);
        w.Rivalries.Clear();
        w.Relationships.Clear();
        foreach (var e in w.Events.Values.Where(e => e.Status == EventStatus.Scheduled)) e.Status = EventStatus.Cancelled;   // two years stale
        foreach (var promo in w.Promotions.Values.Where(p => !p.Defunct && p.Id != w.PlayerPromotion).OrderBy(p => p.Id.Value))
        {
            var belts = new List<Belt>();
            foreach (var div in promo.Divisions.Where(d => d.Sex == Sex.Male && d.Title is not null).OrderBy(d => (int)d.WeightClass))
            {
                var title = w.Get(div.Title!.Value)!;
                if (title.CurrentChampion is not { } cid || w.Get(cid) is not { } champ || champ.Contract?.Promotion != promo.Id) continue;
                var men = w.RosterOf(promo.Id).Where(f => div.Matches(f) && f.Id != cid && f.Contract is not null).OrderBy(f => f.Id.Value).ToList();
                if (men.Count < 6) continue;
                foreach (var f in men.Append(champ)) Ready(f, 40);
                var ranked = men.Take(5).ToList();
                div.Rankings.Clear();
                div.Rankings.AddRange(ranked.Select(f => f.Id));
                title.Lineage.Clear();
                title.Lineage.Add(new TitleReign { Champion = cid, WonOn = w.CurrentDate.AddDays(-400), Defenses = 2 });
                belts.Add(new Belt(div, title, champ, ranked, men[5]));
                if (belts.Count == 2) break;
            }
            if (belts.Count < 2) continue;
            foreach (var other in promo.Divisions)
                if (other != belts[0].D && other != belts[1].D) other.Rankings.Clear();
            promo.MediaDeals.Clear();
            promo.MediaDeals.Add(Network(w, MediaKind.PrimeBroadcast, Clause));
            var s = new Scene(w, promo, belts[0], belts[1]);
            Bout(s, belts[0], daysAgo: idleA);
            Bout(s, belts[1], daysAgo: 30);
            return s;
        }
        throw new InvalidOperationException("no AI house with two titled men's divisions");
    }

    private static MediaDeal Network(World w, MediaKind kind, int star) => new()
    {
        Kind = kind,
        Partner = $"Test {kind}",
        MonthsRemaining = 24,
        SignedOn = w.CurrentDate.AddDays(-30),
        Requirements =
        {
            new DealRequirement(DealRequirementKind.MinMainCard, 5),
            new DealRequirement(DealRequirementKind.MaxMainCard, 5),
            new DealRequirement(DealRequirementKind.MinStarPower, star),
        },
    };

    // The champion beat his division's spare so many days ago.
    private static void Bout(Scene s, Belt b, int daysAgo)
    {
        var ev = new Event
        {
            Id = s.W.NewEventId(), Promotion = s.P.Id, Name = $"Test night {daysAgo}", Date = s.W.CurrentDate.AddDays(-daysAgo),
            Status = EventStatus.Completed,
        };
        ev.Card.Add(new Fight
        {
            Id = s.W.NewFightId(), RedCorner = b.Champ.Id, BlueCorner = b.Spare.Id, WeightClass = b.D.WeightClass,
            Outcome = new FightOutcome { Winner = b.Champ.Id, Loser = b.Spare.Id, Method = FightMethod.UnanimousDecision, EndRound = 3 },
        });
        s.W.Events[ev.Id] = ev;
    }

    private static HashSet<FighterId> AllBut(Scene s, IEnumerable<Fighter> keep)
    {
        var k = keep.Select(f => f.Id).ToHashSet();
        return s.W.Fighters.Keys.Where(id => !k.Contains(id)).ToHashSet();
    }

    private static IEnumerable<Fighter> Everyone(Belt b) => b.Ranked.Append(b.Champ).Append(b.Spare);

    private static Matchup? Headliner(Scene s, IEnumerable<Fighter> keep, bool titleLength = true) =>
        new Matchmaker().BestBout(s.W, s.P, s.W.CurrentDate.AddDays(35), AllBut(s, keep), titleLength);

    private static bool Has(Matchup m, Fighter f) => m.Red == f.Id || m.Blue == f.Id;

    // --- Which belt goes on top -------------------------------------------------

    [Fact]
    public void TheBeltWhoseBoutClearsTheClause_HeadlinesOverTheLongerIdleBelt()
    {
        var s = Build();
        s.B.Champ.Intangibles.Popularity = 80;
        var bout = Headliner(s, Everyone(s.A).Concat(Everyone(s.B)));
        Assert.NotNull(bout);
        Assert.True(bout!.Value.IsTitle);
        Assert.Equal(s.B.T.Id, bout.Value.Title);
        Assert.Equal(s.B.Ranked[0].Id, bout.Value.Red == s.B.Champ.Id ? bout.Value.Blue : bout.Value.Red);   // the challenger is the queue's, as before
    }

    [Fact]
    public void AnOverdueBelt_IsNeverPassedOverForTheNetwork()
    {
        var s = Build(idleA: 150);                // A's champion is out past ChampionWaitMaxDays
        s.B.Champ.Intangibles.Popularity = 80;
        var bout = Headliner(s, Everyone(s.A).Concat(Everyone(s.B)));
        Assert.Equal(s.A.T.Id, bout!.Value.Title);
    }

    [Fact]
    public void WhenNoBeltClearsTheClause_TheLongestIdleBeltStillHeadlines()
    {
        var s = Build();
        var bout = Headliner(s, Everyone(s.A).Concat(Everyone(s.B)));
        Assert.Equal(s.A.T.Id, bout!.Value.Title);
    }

    [Fact]
    public void WithoutAStarClause_TheLongestIdleBeltHeadlines_WhateverTheNames()
    {
        var s = Build();
        s.P.MediaDeals.Clear();
        s.B.Champ.Intangibles.Popularity = 80;
        var bout = Headliner(s, Everyone(s.A).Concat(Everyone(s.B)));
        Assert.Equal(s.A.T.Id, bout!.Value.Title);
    }

    // --- A non-title headliner ----------------------------------------------------

    // Belt A's five ranked men alone (both champions busy), ordered by the house's own read, strongest first.
    private static List<Fighter> ByRead(Scene s) =>
        s.A.Ranked.OrderByDescending(f => ScoutingService.PerceivedAbility(s.W, s.P, f)).ThenBy(f => f.Id.Value).ToList();

    [Fact]
    public void ANonTitleHeadliner_IsChosenByTheClauseFirst_AbilitySecond()
    {
        var s = Build();
        var read = ByRead(s);
        var star = read[^1];                     // the weakest of the five by the house's read
        star.Intangibles.Popularity = 85;
        var bout = Headliner(s, s.A.Ranked);
        Assert.NotNull(bout);
        Assert.False(bout!.Value.IsTitle);
        Assert.True(Has(bout.Value, star), "the strongest pairing with the star in it headlines");
        Assert.Equal(s.P.Rules.TitleRounds, bout.Value.Rounds);
    }

    [Fact]
    public void WhenNobodyClearsTheClause_TheStrongestPairingHeadlines()
    {
        var s = Build();
        var read = ByRead(s);
        var bout = Headliner(s, s.A.Ranked);
        var without = Build();
        without.P.MediaDeals.Clear();
        var plain = Headliner(without, without.A.Ranked);
        Assert.Equal(plain, bout);
        Assert.True(Has(bout!.Value, read[0]));
    }

    [Fact]
    public void ACoMain_IsNotChosenForTheNetwork()
    {
        var s = Build();
        var read = ByRead(s);
        read[^1].Intangibles.Popularity = 85;
        var co = Headliner(s, s.A.Ranked, titleLength: false);
        Assert.False(Has(co!.Value, read[^1]));   // only the bout that runs last answers to the clause
    }

    // --- The running order close to the date ------------------------------------

    // A scheduled card 10 days out: belt A's title fight on top (everyone at 40), four bouts from belt B's men
    // below it (bout 1 is a prelim on a five-fight broadcast window), bout k between B.Ranked/spare men.
    private static Event Card(Scene s, int days = 10)
    {
        var ev = new Event { Id = s.W.NewEventId(), Promotion = s.P.Id, Name = "Test card", Date = s.W.CurrentDate.AddDays(days) };
        var b = s.B.Ranked.Append(s.B.Spare).ToList();
        var a = s.A;
        Fight F(Fighter r, Fighter bl, WeightClass wc, int rounds, bool title = false) => new()
        {
            Id = s.W.NewFightId(), RedCorner = r.Id, BlueCorner = bl.Id, WeightClass = wc, ScheduledRounds = rounds,
            IsTitleFight = title, Title = title ? a.T.Id : null,
        };
        // Spare men from A's division for the extra bouts (A.Ranked[1..4]).
        ev.Card.Add(F(a.Ranked[1], a.Ranked[2], a.D.WeightClass, 3));   // prelim
        ev.Card.Add(F(a.Ranked[3], a.Ranked[4], a.D.WeightClass, 3));
        ev.Card.Add(F(b[0], b[1], s.B.D.WeightClass, 3));
        ev.Card.Add(F(b[2], b[3], s.B.D.WeightClass, 3));
        ev.Card.Add(F(b[4], b[5], s.B.D.WeightClass, 3));
        ev.Card.Add(F(a.Champ, a.Ranked[0], a.D.WeightClass, 5, title: true));
        s.W.Events[ev.Id] = ev;
        return ev;
    }

    [Fact]
    public void CloseToTheDate_AMainCardBoutThatClearsTheClause_GoesOnTop()
    {
        var s = Build();
        var ev = Card(s);
        var titleFight = ev.Card[^1];
        var starBout = ev.Card[2];
        s.W.Get(starBout.BlueCorner)!.Intangibles.Popularity = 78;
        ForwardCalendar.RunWeekly(s.W, s.W.CurrentDate, new Matchmaker(), new EventService());
        Assert.Same(starBout, ev.Card[^1]);
        Assert.Equal(s.P.Rules.TitleRounds, starBout.ScheduledRounds);
        Assert.Contains(titleFight, ev.Card);
        Assert.True(titleFight.IsTitleFight);   // the belt is still on the line, as the co-main
        Assert.True(DealCompliance.Meets(s.W, ev, new DealRequirement(DealRequirementKind.MinStarPower, Clause)));
    }

    [Fact]
    public void APpvCarriersClause_CountsOnAPayPerViewNight()
    {
        var s = Build();
        s.P.MediaDeals.Clear();
        s.P.MediaDeals.Add(Network(s.W, MediaKind.PpvCarrier, 55));
        var ev = Card(s);
        var starBout = ev.Card[3];
        s.W.Get(starBout.RedCorner)!.Intangibles.Popularity = 60;
        ForwardCalendar.HeadlineForTheNetwork(s.W, ev);
        Assert.NotSame(starBout, ev.Card[^1]);    // a fight night: the carrier doesn't judge it
        ev.Tier = EventTier.Ppv;
        ForwardCalendar.HeadlineForTheNetwork(s.W, ev);
        Assert.Same(starBout, ev.Card[^1]);
    }

    [Fact]
    public void AStarOnThePrelims_IsNotMovedUp()
    {
        var s = Build();
        var ev = Card(s);
        var before = ev.Card.ToList();
        s.W.Get(ev.Card[0].RedCorner)!.Intangibles.Popularity = 90;   // outside the five-fight window
        ForwardCalendar.HeadlineForTheNetwork(s.W, ev);
        Assert.Equal(before, ev.Card);
    }

    [Fact]
    public void AMainEventThatClearsTheClause_Stays()
    {
        var s = Build();
        var ev = Card(s);
        var before = ev.Card.ToList();
        s.A.Champ.Intangibles.Popularity = 75;                        // clears 70 with room to spare
        s.W.Get(ev.Card[2].RedCorner)!.Intangibles.Popularity = 95;   // a bigger name below changes nothing
        ForwardCalendar.HeadlineForTheNetwork(s.W, ev);
        Assert.Equal(before, ev.Card);
    }

    [Fact]
    public void ThePlayersCard_AndThe1993World_AreLeftAlone()
    {
        var s = Build();
        var ev = Card(s);
        var before = ev.Card.ToList();
        s.W.Get(ev.Card[2].RedCorner)!.Intangibles.Popularity = 90;
        var ai = s.W.PlayerPromotion;
        s.W.PlayerPromotion = s.P.Id;
        ForwardCalendar.HeadlineForTheNetwork(s.W, ev);
        Assert.Equal(before, ev.Card);
        Assert.Null(MediaService.StarBar(s.W, s.P, ppvNight: true));

        s.W.PlayerPromotion = ai;
        s.W.Mode = WorldMode.Origins;
        ForwardCalendar.HeadlineForTheNetwork(s.W, ev);
        Assert.Equal(before, ev.Card);
        Assert.Null(MediaService.StarBar(s.W, s.P, ppvNight: true));
    }
}
