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

    [Fact]
    public void ADethronedChampionWithDefenses_GetsOneRematch_NotTwo()
    {
        var s = Build();
        var former = s.Ranked[6];   // ranked #7 after the loss: the convention, not the queue, gives him the shot
        s.T.Lineage.Clear();
        s.T.Lineage.Add(new TitleReign { Champion = former.Id, WonOn = s.W.CurrentDate.AddDays(-500), LostOn = s.W.CurrentDate.AddDays(-20), Defenses = 2 });
        s.T.Lineage.Add(new TitleReign { Champion = s.Champ.Id, WonOn = s.W.CurrentDate.AddDays(-20) });
        Bout(s, s.Champ, former, daysAgo: 20);

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
        s.T.Lineage.Add(new TitleReign { Champion = a.Id, WonOn = t0.AddDays(-600), LostOn = t0.AddDays(-200), Defenses = 3 });
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
        var busy = s.Ranked.Take(Matchmaker.TitleShotWindow).ToArray();   // #1-#5 all booked elsewhere
        Assert.Null(TitleBout(s, busy));

        // A champion out past the wait is booked against the best man free on the date (#6): no belt stalls.
        s.W.Events.Remove(s.W.Events.Values.Single(e => e.Name == "Test night 30").Id);
        Bout(s, s.Champ, s.Spare, daysAgo: Matchmaker.ChampionWaitDays + 10);
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
