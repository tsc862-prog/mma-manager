using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Engine;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Tests;

/// <summary>
/// Three correctness bugs the origins-observer arm found: the streak after an overturned bout on a one-night
/// bracket, a cancelled night handing its name to the next one, and the retired open-weight belt read as a
/// house's current belt.
/// </summary>
public class OriginsQaFindingsTests
{
    private static Promotion House(World w, string abbr) => w.Promotions.Values.Single(p => p.Abbreviation == abbr);

    // ---- 1. the streak after an overturn -----------------------------------------------------------

    [Fact]
    public void AnOverturnedEarlyRound_LeavesTheStreakTheLaterRoundsThatNightSay()
    {
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var w = OriginsWorldTests.AiWorld(seed);
            var cru = House(w, "CRU");
            var ev = OriginsShows.ScheduleTournament(w, cru.Id, w.CurrentDate.AddDays(14));
            if (ev is null || !OriginsShows.RunTournament(w, ev, new FightEngine())) continue;

            // The night's winner: he won every bout he fought on the card, the final last.
            var final = ev.Card[^1];
            if (final.Outcome is not { Winner: { } winId }) continue;
            var champ = w.Get(winId)!;
            var his = champ.BoutHistory.Where(id => ev.Card.Any(f => f.Id == id)).ToList();
            if (his.Count < 2 || his[^1] != champ.BoutHistory[^1]) continue;
            var fights = his.Select(id => ev.Card.Single(f => f.Id == id)).ToList();
            if (fights.Any(f => f.Outcome?.Winner != champ.Id || f.OverturnedNc)) continue;

            // His first-round win is overturned after the night (the failed test lands at AfterEvent).
            var first = fights[0];
            var beaten = w.Get(first.Outcome!.Loser!.Value)!;
            Storylines.Overturn(w, first, ev.Date);

            Assert.True(first.OverturnedNc);
            Assert.Equal(his.Count - 1, champ.Record.CurrentStreak);   // the rounds after it, all won (was 0)
            Assert.Equal(0, beaten.Record.CurrentStreak);              // his last bout: the record ends on the NC
            return;
        }
        Assert.Fail("no seed in 1..12 ran a bracket with a multi-round winner: the test never ran its checks");
    }

    [Fact]
    public void AnOverturnOfAMansLatestBout_StillResetsHisStreak()
    {
        // Today's mode: the overturn lands right after the card, so the bout is the latest on both records.
        var w = OriginsWorldTests.AiWorld(2);
        var sfa = House(w, "SFA");
        var men = w.RosterOf(sfa.Id).Where(f => f.Status == FighterStatus.Active).OrderBy(f => f.Id.Value).Take(2).ToList();
        var ev = new EventService().CreateEvent(w, sfa.Id, "SFA test night", w.CurrentDate,
            new[] { new Matchup(men[0].Id, men[1].Id, Division.OpenClass, 1, false, null) });
        new EventService().RunEvent(w, ev);
        var fight = ev.Card.Single(f => f.RedCorner == men[0].Id);
        if (fight.Outcome is not { Winner: { } winId, Loser: { } loseId }) return;   // a draw: nothing to overturn

        Storylines.Overturn(w, fight, ev.Date);
        Assert.Equal(0, w.Get(winId)!.Record.CurrentStreak);
        Assert.Equal(0, w.Get(loseId)!.Record.CurrentStreak);
    }

    // ---- 2. a cancelled night keeps its number -------------------------------------------------------

    [Fact]
    public void ACancelledNight_DoesNotHandItsNameToTheNextOne()
    {
        var w = OriginsWorldTests.AiWorld(3);

        // A card house (the path the player's founded houses book through).
        var sfa = House(w, "SFA");
        var card = OriginsShows.SuggestCard(w, sfa.Id, w.CurrentDate.AddDays(21));
        Assert.True(card.Count >= 2);
        var first = OriginsShows.ScheduleCard(w, sfa.Id, w.CurrentDate.AddDays(21), card)!;
        first.Status = EventStatus.Cancelled;   // fell apart on fight night
        var next = OriginsShows.ScheduleCard(w, sfa.Id, w.CurrentDate.AddDays(35), card)!;
        Assert.NotEqual(first.Name, next.Name);

        // A bracket house.
        var cru = House(w, "CRU");
        var bracket = OriginsShows.ScheduleTournament(w, cru.Id, w.CurrentDate.AddDays(21))!;
        bracket.Status = EventStatus.Cancelled;   // the field could not be filled
        var again = OriginsShows.ScheduleTournament(w, cru.Id, w.CurrentDate.AddDays(35))!;
        Assert.NotEqual(bracket.Name, again.Name);

        foreach (var house in new[] { sfa, cru })
        {
            var names = w.Events.Values.Where(e => e.Promotion == house.Id).Select(e => e.Name).ToList();
            Assert.Equal(names.Count, names.Distinct().Count());
        }
    }

    // ---- 3. a retired open-weight belt is history, not a current belt -----------------------------

    [Fact]
    public void ARetiredOpenWeightBelt_StaysAsHistory_ButIsNoLongerACurrentBelt()
    {
        var w = OriginsWorldTests.AiWorld(4);
        var sfa = House(w, "SFA");
        var open = w.Get(sfa.Divisions.Single(d => d.Open).Title!.Value)!;
        var holder = w.RosterOf(sfa.Id).OrderBy(f => f.Id.Value).First();
        open.CurrentChampion = holder.Id;
        open.Lineage.Add(new TitleReign { Champion = holder.Id, WonOn = w.CurrentDate });
        Assert.Equal(open.Id, Belts.Current(w, sfa).First().Id);

        OriginsReforms.DrawLines(w, sfa, new[] { WeightClass.Middleweight, WeightClass.Heavyweight }, w.CurrentDate.AddDays(7));

        // History kept: the belt, its place in the house's list, and its one closed reign.
        Assert.Contains(open.Id, w.Titles.Keys);
        Assert.Contains(open.Id, sfa.Titles);
        Assert.Equal(holder.Id, Assert.Single(open.Lineage).Champion);
        // ...but it is retired, and every list of the house's current belts leaves it out.
        Assert.True(Belts.IsRetired(w, open));
        var current = Belts.Current(w, sfa).ToList();
        Assert.DoesNotContain(current, t => t.Id == open.Id);
        Assert.Equal(2, current.Count);
        Assert.All(current, t => Assert.False(Belts.IsRetired(w, t)));
        Assert.Equal(sfa.Divisions.Where(d => d.Title is not null).Select(d => d.Title!.Value).OrderBy(id => id.Value),
                     current.Select(t => t.Id).OrderBy(id => id.Value));
    }
}
