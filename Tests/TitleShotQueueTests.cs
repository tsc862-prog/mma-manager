using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;

namespace MmaManager.Tests;

/// <summary>
/// Who the AI matchmaker hands a title shot to (today's mode). Before this rule set a feud with the champion
/// (heat 40+) was a pass from anywhere in the division, the queue reached as deep as it had to for a free
/// man, and a dethroned champion was never owed his rematch: these tests failed there (the #9 feud got the
/// shot over a ready #2; a #6 got it with the champion fresh off a fight; the former champion was skipped; the
/// #1 contender was booked into an undercard bout while the champion was free to defend).
/// Every scene is built two years past the generated history, so only the bouts staged here are recent.
/// </summary>
public class TitleShotQueueTests
{
    private sealed record Scene(World W, Promotion P, Division D, Title T, Fighter Champ, List<Fighter> Ranked, Fighter Spare);

    // An AI house's titled men's division with nine ranked, ready, in-form men, a spare man outside the
    // rankings, no rivalries, and a champion whose last bout was a win 30 days ago over the spare.
    private static Scene Build(ulong seed = 7)
    {
        var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed, UseDesignedRoster = false });
        w.CurrentDate = w.CurrentDate.AddYears(2);   // the generated history is no longer anybody's last bout
        w.Rivalries.Clear();
        foreach (var promo in w.Promotions.Values.Where(p => !p.Defunct && p.Id != w.PlayerPromotion).OrderBy(p => p.Id.Value))
            foreach (var div in promo.Divisions.Where(d => d.Sex == Sex.Male && d.Title is not null).OrderBy(d => (int)d.WeightClass))
            {
                var title = w.Get(div.Title!.Value)!;
                if (title.CurrentChampion is not { } cid || w.Get(cid) is not { } champ) continue;
                var men = w.RosterOf(promo.Id).Where(f => div.Matches(f) && f.Id != cid).OrderBy(f => f.Id.Value).ToList();
                if (men.Count < 10) continue;
                foreach (var f in men.Append(champ))
                {
                    f.Status = FighterStatus.Active;
                    f.CurrentInjury = null;
                    f.ReadyToFightOn = null;
                    f.SuspendedUntil = null;
                    f.Record.Wins = 12; f.Record.Losses = 2; f.Record.CurrentStreak = 2;   // every man in form (Worthy)
                }
                var ranked = men.Take(9).ToList();
                div.Rankings.Clear();
                div.Rankings.AddRange(ranked.Select(f => f.Id));
                title.Lineage.Clear();
                title.Lineage.Add(new TitleReign { Champion = cid, WonOn = w.CurrentDate.AddDays(-400), Defenses = 2 });
                foreach (var other in promo.Divisions)
                    if (other != div && other.Title is { } ot && w.Get(ot)?.CurrentChampion is null) other.Rankings.Clear();   // no vacant belt queues ahead
                var s = new Scene(w, promo, div, title, champ, ranked, men[9]);
                Bout(s, champ, men[9], daysAgo: 30);
                return s;
            }
        throw new InvalidOperationException("no AI men's division with a champion and ten men");
    }

    // A completed bout the winner took, so many days ago.
    private static void Bout(Scene s, Fighter winner, Fighter loser, int daysAgo)
    {
        var ev = new Event
        {
            Id = s.W.NewEventId(), Promotion = s.P.Id, Name = $"Test night {daysAgo}", Date = s.W.CurrentDate.AddDays(-daysAgo),
            Status = EventStatus.Completed,
        };
        ev.Card.Add(new Fight
        {
            Id = s.W.NewFightId(), RedCorner = winner.Id, BlueCorner = loser.Id, WeightClass = s.D.WeightClass,
            Outcome = new FightOutcome { Winner = winner.Id, Loser = loser.Id, Method = FightMethod.UnanimousDecision, EndRound = 3 },
        });
        s.W.Events[ev.Id] = ev;
    }

    private static void Feud(Scene s, Fighter f, int heat) =>
        s.W.Rivalries.Add(new Rivalry { A = s.Champ.Id, B = f.Id, Intensity = heat, Since = s.W.CurrentDate });

    // The belt bout this house would put on top in 35 days, with everyone outside this division's scene (and the
    // given men) unavailable, so no other belt and no undercard pairing competes.
    private static Matchup? TitleBout(Scene s, params Fighter[] unavailable)
    {
        var keep = s.Ranked.Append(s.Champ).Append(s.Spare).Where(f => !unavailable.Contains(f)).Select(f => f.Id).ToHashSet();
        var exclude = s.W.Fighters.Keys.Where(id => !keep.Contains(id)).ToHashSet();
        var bout = new Matchmaker().BestBout(s.W, s.P, s.W.CurrentDate.AddDays(35), exclude, titleLength: true);
        return bout is { IsTitle: true } b && b.Title == s.T.Id ? b : null;
    }

    private static FighterId Challenger(Matchup m, Scene s) => m.Red == s.Champ.Id ? m.Blue : m.Red;

    [Fact]
    public void AFeudHolderRankedNinth_DoesNotJumpAReadySecond()
    {
        var s = Build();
        Feud(s, s.Ranked[8], 90);
        var bout = TitleBout(s, s.Ranked[0]);   // #1 is busy
        Assert.NotNull(bout);
        Assert.Equal(s.Ranked[1].Id, Challenger(bout!.Value, s));
    }

    [Fact]
    public void AFeudInsideTheWindow_LiftsTheFourthOverTheTopThree()
    {
        var s = Build();
        Feud(s, s.Ranked[3], 45);
        var bout = TitleBout(s);
        Assert.NotNull(bout);
        Assert.Equal(s.Ranked[3].Id, Challenger(bout!.Value, s));
    }

    [Fact]
    public void AFeudJustOutsideTheWindow_DoesNotBeatAReadyFirst()
    {
        var s = Build();
        Feud(s, s.Ranked[Matchmaker.TitleShotWindow], 90);   // #6
        var bout = TitleBout(s);
        Assert.NotNull(bout);
        Assert.Equal(s.Ranked[0].Id, Challenger(bout!.Value, s));
    }

    // The belt changed hands 20 days ago: the scene's champion took it from a man ranked #7 now, whose reign had these defenses.
    private static Fighter Dethroned(Scene s, int defenses)
    {
        var former = s.Ranked[6];
        s.T.Lineage.Clear();
        s.T.Lineage.Add(new TitleReign { Champion = former.Id, WonOn = s.W.CurrentDate.AddDays(-900), LostOn = s.W.CurrentDate.AddDays(-20), Defenses = defenses });
        s.T.Lineage.Add(new TitleReign { Champion = s.Champ.Id, WonOn = s.W.CurrentDate.AddDays(-20) });
        Bout(s, s.Champ, former, daysAgo: 20);
        return former;
    }

    [Fact]
    public void ADethronedChampionWithoutAnEstablishedReign_IsOwedNoRematch()
    {
        var s = Build();
        Dethroned(s, Matchmaker.RematchClauseDefenses - 1);
        var bout = TitleBout(s);
        Assert.NotNull(bout);
        Assert.Equal(s.Ranked[0].Id, Challenger(bout!.Value, s));   // the queue, not the man the champion just beat
        Assert.Null(Matchmaker.OwedRematch(s.T, s.Champ.Id));
    }

    [Fact]
    public void ADethronedChampionWithDefenses_GetsOneRematch_NotTwo()
    {
        var s = Build();
        var former = Dethroned(s, Matchmaker.RematchClauseDefenses);   // ranked #7 after the loss: the convention, not the queue, gives him the shot

        var first = TitleBout(s);
        Assert.NotNull(first);
        Assert.Equal(former.Id, Challenger(first!.Value, s));

        // The rematch is fought and the new champion keeps the belt: that settles it.
        s.T.Lineage[^1].Defenses = 1;
        Bout(s, s.Champ, former, daysAgo: 5);
        var second = TitleBout(s);
        Assert.NotNull(second);
        Assert.NotEqual(former.Id, Challenger(second!.Value, s));
        Assert.Equal(s.Ranked[0].Id, Challenger(second.Value, s));
    }

    [Fact]
    public void ARematchTheFormerChampionWon_OwesTheManHeBeatNothing()
    {
        var s = Build();
        var (a, b) = (s.Ranked[2], s.Champ);
        var t0 = s.W.CurrentDate;
        s.T.Lineage.Clear();
        s.T.Lineage.Add(new TitleReign { Champion = a.Id, WonOn = t0.AddDays(-600), LostOn = t0.AddDays(-200), Defenses = Matchmaker.RematchClauseDefenses });
        s.T.Lineage.Add(new TitleReign { Champion = b.Id, WonOn = t0.AddDays(-200), LostOn = t0.AddDays(-60) });
        s.T.Lineage.Add(new TitleReign { Champion = a.Id, WonOn = t0.AddDays(-60) });
        // a won the rematch: b's reign had no defense, so b is owed nothing.
        Assert.Null(Matchmaker.OwedRematch(s.T, a.Id));
        // and before it, b owed a his one rematch.
        s.T.Lineage.RemoveAt(2);
        s.T.Lineage[1].LostOn = null;
        Assert.Equal(a.Id, Matchmaker.OwedRematch(s.T, b.Id));
    }

    [Fact]
    public void AChampionFreshOffAFight_WaitsRatherThanReachPastTheWindow_ThenTakesTheBestFreeMan()
    {
        var s = Build();
        Assert.True(MmaManager.Core.Sim.Org.OrgSizing.SizeOf(s.P) >= OrgSize.National, "the scene's house is big enough to wait");
        var busy = s.Ranked.Take(Matchmaker.TitleShotWindow).ToArray();   // #1-#5 all booked elsewhere
        Assert.Null(TitleBout(s, busy));

        // A champion out past the wait (under the 120-day neglect line) is booked against the best man free on the date (#6).
        s.W.Events.Remove(s.W.Events.Values.Single(e => e.Name == "Test night 30").Id);
        Bout(s, s.Champ, s.Spare, daysAgo: Matchmaker.ChampionWaitMaxDays + 10);
        var bout = TitleBout(s, busy);
        Assert.NotNull(bout);
        Assert.Equal(s.Ranked[Matchmaker.TitleShotWindow].Id, Challenger(bout!.Value, s));
    }

    // The non-title bouts this house would book in 21 days from the scene's men alone (no camps, no bonds in the way).
    private static IReadOnlyList<Matchup> Undercard(Scene s)
    {
        foreach (var f in s.Ranked.Append(s.Spare)) f.Camp = null;
        s.W.Relationships.Clear();
        var keep = s.Ranked.Append(s.Spare).Append(s.Champ).Select(f => f.Id).ToHashSet();
        var exclude = s.W.Fighters.Keys.Where(id => !keep.Contains(id)).ToHashSet();
        return new Matchmaker().BuildUndercard(s.W, s.P.Id, 10, s.W.CurrentDate.AddDays(21), exclude);
    }

    [Fact]
    public void TheNumberOneContender_IsKeptOffTheUndercard_WhileTheChampionIsFreeToDefend()
    {
        var s = Build();
        bool Booked(IReadOnlyList<Matchup> card, Fighter f) => card.Any(m => m.Red == f.Id || m.Blue == f.Id);

        var held = Undercard(s);
        Assert.NotEmpty(held);
        Assert.False(Booked(held, s.Ranked[0]), "the #1 contender waits for the belt");

        // The champion hurt past the date: nobody to wait for, the #1 stays busy.
        s.Champ.CurrentInjury = new Injury { Severity = InjurySeverity.Minor, Description = "test", OccurredOn = s.W.CurrentDate, ExpectedReturn = s.W.CurrentDate.AddDays(90) };
        s.Champ.Status = FighterStatus.Injured;
        Assert.True(Booked(Undercard(s), s.Ranked[0]), "with the champion out, the #1 fights");
    }

    [Fact]
    public void ABeltFarBackInTheHousesQueue_DoesNotHoldItsNumberOne()
    {
        var s = Build();
        // Every other belt of the house has a free champion who has waited longer than this one (his last bout was
        // two years ago; this champion fought 30 days ago), so this belt queues behind all of them.
        var others = s.P.Divisions.Where(d => d != s.D && d.Title is { } t && s.W.Get(t)?.CurrentChampion is not null && d.Rankings.Count > 0)
            .Select(d => s.W.Get(s.W.Get(d.Title!.Value)!.CurrentChampion!.Value)!).ToList();
        // The undercard is 21 days out; one belt per card, one card per cadence: the belts announced before then.
        int cadence = 7 * MmaManager.Core.Sim.Living.ForwardCalendar.CadenceWeeks(s.P);
        int capacity = (21 + cadence - 1) / cadence;
        Assert.True(others.Count >= capacity, $"the scene's house needs {capacity} other belts with champions to fill the window, has {others.Count}");
        foreach (var c in others) { c.Status = FighterStatus.Active; c.CurrentInjury = null; c.ReadyToFightOn = null; c.SuspendedUntil = null; }
        foreach (var f in s.Ranked.Append(s.Spare)) f.Camp = null;
        s.W.Relationships.Clear();
        var keep = s.Ranked.Append(s.Spare).Append(s.Champ).Concat(others).Select(f => f.Id).ToHashSet();
        var exclude = s.W.Fighters.Keys.Where(id => !keep.Contains(id)).ToHashSet();
        var card = new Matchmaker().BuildUndercard(s.W, s.P.Id, 10, s.W.CurrentDate.AddDays(21), exclude);
        Assert.Contains(card, m => m.Red == s.Ranked[0].Id || m.Blue == s.Ranked[0].Id);
    }

    [Fact]
    public void ASmallHouse_NeverSitsABeltOut()
    {
        // A Regional house runs one card a month: with #1-#5 all busy its champion is booked at once (#6), not kept waiting.
        var s = Build();
        s.P.Prestige = 25;
        Assert.Equal(OrgSize.Regional, MmaManager.Core.Sim.Org.OrgSizing.SizeOf(s.P));
        var bout = TitleBout(s, s.Ranked.Take(Matchmaker.TitleShotWindow).ToArray());
        Assert.NotNull(bout);
        Assert.Equal(s.Ranked[Matchmaker.TitleShotWindow].Id, Challenger(bout!.Value, s));
    }

    [Fact]
    public void AWeakDivision_DoesNotStallTheBelt()
    {
        // #1-#5 free but none in form, #6 the only man in form: nobody busy to wait for, so the belt goes at once
        // (main's fallbacks), even with the champion fresh off a fight.
        var s = Build();
        foreach (var f in s.Ranked.Take(Matchmaker.TitleShotWindow)) { f.Record.Wins = 2; f.Record.Losses = 6; f.Record.CurrentStreak = -2; }
        var bout = TitleBout(s);
        Assert.NotNull(bout);
        Assert.Equal(s.Ranked[Matchmaker.TitleShotWindow].Id, Challenger(bout!.Value, s));
    }

    [Fact]
    public void TheOnlyContender_TheManTheChampionJustBeat_GetsTheBeltDefendedAtOnce()
    {
        var s = Build();
        var a = s.Ranked[0];
        Bout(s, s.Champ, a, daysAgo: 10);   // the champion just beat A
        s.D.Rankings.Clear();
        s.D.Rankings.Add(a.Id);             // a thin division: A is the only ranked man
        var bout = TitleBout(s);
        Assert.NotNull(bout);
        Assert.Equal(a.Id, Challenger(bout!.Value, s));
    }

    [Fact]
    public void OneBusyContender_IsNoReasonToWait()
    {
        // #1 busy, #2-#5 free but out of form, #6 in form: one busy man is not enough to keep the champion waiting.
        var s = Build();
        foreach (var f in s.Ranked.Skip(1).Take(Matchmaker.TitleShotWindow - 1)) { f.Record.Wins = 2; f.Record.Losses = 6; f.Record.CurrentStreak = -2; }
        var bout = TitleBout(s, s.Ranked[0]);
        Assert.NotNull(bout);
        Assert.Equal(s.Ranked[Matchmaker.TitleShotWindow].Id, Challenger(bout!.Value, s));
    }

    [Fact]
    public void TheManTheChampionJustBeat_IsNotHeld_AndTheBeltIsDefendedWithoutAStall()
    {
        // The reviewer's stranding scene: the champion beat A, A stays #1, #2-#5 are out of form. On the old hold A was
        // kept off every card for a belt that would never pick him.
        var s = Build();
        var a = s.Ranked[0];
        Bout(s, s.Champ, a, daysAgo: 10);
        foreach (var f in s.Ranked.Skip(1).Take(Matchmaker.TitleShotWindow - 1)) { f.Record.Wins = 2; f.Record.Losses = 6; f.Record.CurrentStreak = -2; }

        var card = Undercard(s);
        Assert.Contains(card, m => m.Red == a.Id || m.Blue == a.Id);   // A fights on

        var bout = TitleBout(s);
        Assert.NotNull(bout);   // and the belt goes now, to the best fresh man in form (#6)
        Assert.Equal(s.Ranked[Matchmaker.TitleShotWindow].Id, Challenger(bout!.Value, s));
    }

    [Fact]
    public void TheHold_IsForTheManTheBeltWillPick_NotForWhoeverIsFirst()
    {
        // #1 out of form, #2 in form: the belt goes to #2, so #2 is held and #1 fights.
        var s = Build();
        var (first, second) = (s.Ranked[0], s.Ranked[1]);
        first.Record.Wins = 2; first.Record.Losses = 6; first.Record.CurrentStreak = -2;
        var card = Undercard(s);
        Assert.DoesNotContain(card, m => m.Red == second.Id || m.Blue == second.Id);
        Assert.Contains(card, m => m.Red == first.Id || m.Blue == first.Id);
    }

    [Fact]
    public void AFeudTitleHeadliner_NeedsTheRivalInsideTheTopFive()
    {
        var s = Build();
        var date = s.W.CurrentDate.AddDays(35);
        var none = new HashSet<FighterId>();
        foreach (var f in s.Ranked.Append(s.Champ)) f.Camp = null;
        s.W.Relationships.Clear();

        var seventh = s.Ranked[6];
        Assert.Null(MmaManager.Core.Sim.Living.ForwardCalendar.Bookable(s.W, s.P, s.Champ, seventh, date, none, requireWorthy: true));

        var fourth = s.Ranked[3];
        var bout = MmaManager.Core.Sim.Living.ForwardCalendar.Bookable(s.W, s.P, s.Champ, fourth, date, none, requireWorthy: true);
        Assert.NotNull(bout);
        Assert.True(bout!.Value.IsTitle);
        Assert.Equal(s.T.Id, bout.Value.Title);
    }

    [Fact]
    public void AVacantBelt_GoesToTheTwoBestRankedAvailableMen()
    {
        var s = Build();
        s.T.CurrentChampion = null;
        var bout = TitleBout(s, s.Ranked[0]);   // #1 is busy
        Assert.NotNull(bout);
        var pair = new[] { bout!.Value.Red, bout.Value.Blue };
        Assert.Contains(s.Ranked[1].Id, pair);
        Assert.Contains(s.Ranked[2].Id, pair);
    }
}
