using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Engine;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Tests;

/// <summary>
/// Origins phase 1, the review's fixes: open belts go vacant (never handed down), the bracket's field
/// obeys the bookability rule, tournament nights can't be hand-booked, a title-shot promise at a
/// bracket house is a place in the bracket, honest crowning news, and the player's bracket booking
/// refuses with a reason.
/// </summary>
public class OriginsReviewTests
{
    private static World Player(ulong seed, int index) => new WorldBuilder().Build(WorldBuilder.Options.Origins(seed, index));
    private static Promotion House(World w, string abbr) => w.Promotions.Values.Single(p => p.Abbreviation == abbr);

    private static Title BeltOf(World w, Promotion p) => w.Get(p.Divisions.Single().Title!.Value)!;

    private static void Hold(World w, Title belt, Fighter f, DateOnly on)
    {
        belt.CurrentChampion = f.Id;
        belt.Lineage.Add(new TitleReign { Champion = f.Id, WonOn = on });
    }

    // ---- 1. an open belt goes vacant ------------------------------------------------

    [Theory]
    [InlineData("CRU")]
    [InlineData("SFA")]
    public void ARetiringChampionsOpenBelt_GoesVacant_NotToTheNextMan(string abbr)
    {
        var w = OriginsWorldTests.AiWorld(3);
        var house = House(w, abbr);
        var belt = BeltOf(w, house);
        var champ = w.RosterOf(house.Id).OrderBy(f => f.Id.Value).First();
        Hold(w, belt, champ, w.CurrentDate);
        Assert.NotEmpty(house.Divisions[0].Rankings);   // there IS a contender who would have been handed it

        Lifecycle.RelinquishTitles(w, champ, w.CurrentDate.AddDays(10));
        Assert.Null(belt.CurrentChampion);
        Assert.Single(belt.Lineage);
        Assert.True(belt.Lineage[0].Vacated);
        Assert.Equal(w.CurrentDate.AddDays(10), belt.Lineage[0].LostOn);
    }

    [Fact]
    public void AYear_EveryCrucibleReignBeginsOnABracketNight_AndEverySfaReignInATitleFight()
    {
        var w = OriginsWorldTests.AiWorld(4);
        new WorldTick().AdvanceWeeks(w, 104);
        var cru = House(w, "CRU");
        var nights = w.Events.Values.Where(e => e.Promotion == cru.Id && e.Status == EventStatus.Completed && e.Format == EventFormat.Tournament)
            .Select(e => e.Date).ToHashSet();
        Assert.All(BeltOf(w, cru).Lineage, r => Assert.Contains(r.WonOn, nights));
        Assert.True(BeltOf(w, cru).Lineage.Count <= nights.Count, "more Crucible champions than brackets");

        var sfa = House(w, "SFA");
        var sfaBelt = BeltOf(w, sfa);
        var titleNights = w.Events.Values.Where(e => e.Promotion == sfa.Id && e.Status == EventStatus.Completed
                && e.Card.Any(f => f.IsTitleFight && f.Title == sfaBelt.Id))
            .Select(e => e.Date).ToHashSet();
        Assert.All(sfaBelt.Lineage, r => Assert.Contains(r.WonOn, titleNights));
    }

    // ---- 2. the bracket's field --------------------------------------------------------

    [Fact]
    public void TheField_LeavesOutSpentDealsAndMenBookedElsewhere()
    {
        var w = OriginsWorldTests.AiWorld(5);
        var cru = House(w, "CRU");
        // Deep enough that the bracket fills even with three men out of it.
        foreach (var fa in w.FreeAgents().Where(f => f.Status == FighterStatus.Active).OrderBy(f => f.Id.Value).Take(4).ToList())
            ContractLifecycle.Sign(w, fa, new ContractOffer
            {
                Fighter = fa.Id, Promotion = cru.Id, PursePerFight = new Money(1_000m), WinBonus = new Money(500m), Fights = 4, Exclusive = false,
            }, w.CurrentDate);
        var roster = w.RosterOf(cru.Id).OrderBy(f => f.Id.Value).ToList();
        Assert.True(roster.Count >= 11);
        var spent = roster[0];
        spent.Contract!.FightsRemaining = 0;
        var booked = roster[1];
        var other = roster[2];
        var date = w.CurrentDate.AddDays(14);
        var card = new EventService().CreateEvent(w, cru.Id, "CRU side card", date.AddDays(7),
            new[] { new Matchup(booked.Id, other.Id, Division.OpenClass, 1, false, null) });

        var ev = OriginsShows.ScheduleTournament(w, cru.Id, date) ?? throw new InvalidOperationException("no bracket night");
        // (An AI house: book it by hand through the same constructor the AI uses.)
        var result = new TournamentService().RunOneNight(w, ev, 8, Money.Zero, wc: null, rules: null);
        Assert.NotNull(result);
        var drawn = ev.Card.SelectMany(f => new[] { f.RedCorner, f.BlueCorner }).ToHashSet();
        Assert.DoesNotContain(spent.Id, drawn);
        Assert.DoesNotContain(booked.Id, drawn);
        Assert.DoesNotContain(other.Id, drawn);
        Assert.Equal(EventStatus.Scheduled, card.Status);
    }

    // ---- 3. no hand-booking a bracket ----------------------------------------------------

    [Fact]
    public void ATournamentNight_RefusesEveryCardMutator()
    {
        var w = Player(6, 0);
        var cru = w.Get(w.PlayerPromotion)!;
        var night = OriginsShows.ScheduleTournament(w, cru.Id, w.CurrentDate.AddDays(28))!;
        var men = w.RosterOf(cru.Id).OrderBy(f => f.Id.Value).ToList();
        var booking = new PlayerBooking();

        Assert.False(booking.AddBout(w, night, men[0].Id, men[1].Id, 1, false, out var why));
        Assert.Contains("tournament", why);
        Assert.False(booking.AddLocalBout(w, night, men[0].Id, PrimaryStyle.Brawler, 1, out why));
        Assert.Contains("tournament", why);
        Assert.False(booking.SetMainCardSize(w, night, 2, out why));
        Assert.Contains("tournament", why);
        Assert.False(booking.MoveBout(w, night, 0, 0, out why));
        Assert.Contains("tournament", why);
        Assert.Empty(night.Card);
    }

    // ---- 4. a title-shot promise at a bracket house ------------------------------------------

    [Fact]
    public void ATitleShotPromiseAtABracketHouse_IsAPlaceInTheBracket()
    {
        var w = Player(7, 0);
        var cru = w.Get(w.PlayerPromotion)!;
        var signee = w.FreeAgents().Where(f => f.Status == FighterStatus.Active).OrderBy(f => f.Id.Value).First();
        Assert.Null(Promises.Refusal(w, cru, signee, PromiseKind.TitleShot, 0, 4));
        ContractLifecycle.Sign(w, signee, new ContractOffer
        {
            Fighter = signee.Id, Promotion = cru.Id, PursePerFight = new Money(1_000m), WinBonus = new Money(500m),
            Fights = 4, Exclusive = false, PromiseTitleShot = true,
        }, w.CurrentDate);
        var promise = Assert.Single(w.Promises, p => p.Fighter == signee.Id);
        Assert.Equal("a place in the next tournament", Promises.What(w, promise));

        var night = OriginsShows.ScheduleTournament(w, cru.Id, w.CurrentDate.AddDays(21))!;
        var tick = new WorldTick();
        while (w.CurrentDate < night.Date) tick.AdvanceWeeksStoppingForPlayer(w, 1);
        tick.RunEventLive(w, night);
        Assert.Contains(night.Card, f => f.RedCorner == signee.Id || f.BlueCorner == signee.Id);   // promised men get in first
        Promises.RunMonthly(w, w.CurrentDate.AddDays(1));
        Assert.Equal(PromiseState.Kept, promise.State);

        // The garage has no belt: still refused there.
        var d = Player(7, 2);
        var dlb = d.Get(d.PlayerPromotion)!;
        Assert.NotNull(Promises.Refusal(d, dlb, d.RosterOf(dlb.Id).First(), PromiseKind.TitleShot, 0, 4));
    }

    // ---- 5. honest crowning news --------------------------------------------------------------

    [Fact]
    public void AWinnerWhenTheHolderSatOut_IsCrownedWithoutADefeatInTheNews()
    {
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var w = OriginsWorldTests.AiWorld(seed);
            var cru = House(w, "CRU");
            var belt = BeltOf(w, cru);
            var holder = w.RosterOf(cru.Id).OrderBy(f => f.Id.Value).First();
            Hold(w, belt, holder, w.CurrentDate);
            holder.Contract!.FightsRemaining = 0;   // he can't be drawn
            var ev = OriginsShows.ScheduleTournament(w, cru.Id, w.CurrentDate.AddDays(14));
            if (ev is null) continue;
            Assert.True(OriginsShows.RunTournament(w, ev, new FightEngine()));
            if (belt.CurrentChampion is not { } champId || champId == holder.Id) continue;   // no champion this time: next seed

            var champ = w.Get(champId)!;
            Assert.DoesNotContain(ev.Card, f => f.RedCorner == holder.Id || f.BlueCorner == holder.Id);
            Assert.True(belt.Lineage[0].Vacated);
            Assert.Equal(ev.Date, belt.Lineage[0].LostOn);
            var head = OriginsShows.NotInFieldHeadline(champ, ev, belt, holder);
            Assert.Contains(w.News, n => n.Headline == head);
            Assert.Contains("was not in the field", head);
            Assert.DoesNotContain(w.News, n => n.Date == ev.Date && n.Category == NewsCategory.TitleChange && n.Headline.Contains("defeat"));
            return;
        }
        Assert.Fail("no seed in 1..12 crowned a new champion: the test never ran its checks");
    }

    // ---- 10. the player's bracket booking -------------------------------------------------------

    [Fact]
    public void ScheduleTournament_RefusesWithAReason_WhenTheBracketCantBeFilled()
    {
        var w = Player(8, 0);
        var cru = w.Get(w.PlayerPromotion)!;
        foreach (var f in w.RosterOf(cru.Id).OrderBy(f => f.Id.Value).Take(5)) f.Contract!.FightsRemaining = 0;
        Assert.Null(OriginsShows.ScheduleTournament(w, cru.Id, w.CurrentDate.AddDays(21), out var why));
        Assert.Contains("of the 8 men", why);
        Assert.Null(OriginsShows.ScheduleTournament(w, House(w, "SFA").Id, w.CurrentDate.AddDays(21), out why));
        Assert.Contains("doesn't run one-night tournaments", why);
        Assert.Null(OriginsShows.ScheduleTournament(w, cru.Id, w.CurrentDate, out why));
        Assert.NotNull(why);
    }
}
