using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Scouting;
using MmaManager.Persistence;

namespace MmaManager.Tests;

/// <summary>
/// The Origins observer start: the player owns no promotion (<see cref="World.HasPlayer"/> false), all three
/// day-one houses are the AI's, the world advances exactly as an all-AI world does, he has no inbox, every
/// booking mutator refuses him, and he reads fighters through the public (least informed) read.
/// </summary>
public class OriginsObserverTests
{
    private static World Observer(ulong seed) => new WorldBuilder().Build(WorldBuilder.Options.OriginsObserver(seed));

    // A cheap per-week fingerprint (the full graph diff runs at the checkpoints).
    private static string Digest(World w) => string.Join("|",
        w.CurrentDate.ToString("yyyy-MM-dd"),
        w.Events.Count, w.Events.Values.Count(e => e.Status == EventStatus.Completed),
        w.Events.Values.Sum(e => e.Card.Count),
        w.Fighters.Count, w.Fighters.Values.Sum(f => f.Record.Wins),
        string.Join(",", w.Promotions.Values.OrderBy(p => p.Id.Value).Select(p => $"{p.Id.Value}:{p.Balance.Amount}:{p.Defunct}")),
        w.News.Count, w.Rivalries.Count, w.Era.Chapters.Count);

    // ---- day one ------------------------------------------------------------------

    [Fact]
    public void ObserverBuild_FlagsNobody_StaffsAndBooksAllThree()
    {
        var w = Observer(7);
        Assert.False(w.HasPlayer);
        Assert.Equal(default, w.PlayerPromotion);
        Assert.Equal(WorldMode.Origins, w.Mode);
        Assert.Equal(3, w.Promotions.Count);
        Assert.All(w.Promotions.Values, p => Assert.False(p.IsPlayerControlled));
        foreach (var p in w.Promotions.Values)
            Assert.True(p.Staff.Count > 0, $"{p.Abbreviation} has no front office");
        // Day one books every opening night inside the lead window (the player start leaves his own house's to him).
        Assert.NotEmpty(w.Events.Values);
        Assert.All(w.Events.Values, e => Assert.Equal(EventStatus.Scheduled, e.Status));
        var cruStart = new WorldBuilder().Build(WorldBuilder.Options.Origins(7));
        Assert.True(w.Events.Count >= cruStart.Events.Count);
        Assert.DoesNotContain(cruStart.Events.Values, e => e.Promotion == cruStart.PlayerPromotion);
        Assert.DoesNotContain(w.Events.Values, e => e.Promotion == default);
        Assert.Empty(w.Mail);

        // ...and every house's opening night goes on the books on time, with nobody to wait for.
        var tick = new WorldTick();
        while (w.CurrentDate < w.StartDate.AddDays(7 * 14))
            tick.AdvanceWeek(w);
        foreach (var p in w.Promotions.Values)
            Assert.Contains(w.Events.Values, e => e.Promotion == p.Id && e.Status != EventStatus.Cancelled);
        Assert.DoesNotContain(w.Events.Values, e => e.Promotion == default);
    }

    [Fact]
    public void ObserverBuild_SameSeedTwice_IsTheSameWorld()
    {
        var diffs = PersistenceGraph.Diff(Observer(21), Observer(21));
        Assert.True(diffs.Count == 0, "Same seed, different observer worlds:\n" + PersistenceGraph.Describe(diffs));
    }

    [Fact]
    public void ObserverBuild_IsTheNobodysHouseBuild_AndThePlayerStartIsUntouched()
    {
        // The explicit flag builds exactly what an out-of-range house index always built (nobody flagged).
        var nobody = new WorldBuilder().Build(WorldBuilder.Options.Origins(9, playerIndex: -1));
        var diffs = PersistenceGraph.Diff(nobody, Observer(9));
        Assert.True(diffs.Count == 0, "Observer differs from the no-house build:\n" + PersistenceGraph.Describe(diffs));

        // A player start still runs his house (and differs from the observer world by it).
        var cru = new WorldBuilder().Build(WorldBuilder.Options.Origins(9));
        Assert.True(cru.HasPlayer);
        Assert.Equal("CRU", cru.Get(cru.PlayerPromotion)!.Abbreviation);
        Assert.True(cru.Get(cru.PlayerPromotion)!.IsPlayerControlled);
        Assert.Single(cru.Promotions.Values, p => p.IsPlayerControlled);
        Assert.NotEmpty(PersistenceGraph.Diff(cru, Observer(9)));
    }

    [Fact]
    public void ObserverOption_IsRefusedForToday()
    {
        Assert.Throws<NotSupportedException>(() => new WorldBuilder().Build(new WorldBuilder.Options { Seed = 1, Observer = true }));
    }

    // ---- five years of watching -----------------------------------------------------

    [Fact]
    public void FiveYears_StoppingForPlayer_IsPlainAdvance_WeekForWeek()
    {
        var plain = Observer(5);
        var ui = Observer(5);
        var quiet = Observer(5);
        var tickPlain = new WorldTick();
        var tickUi = new WorldTick();
        var tickQuiet = new WorldTick();
        for (int week = 1; week <= 52 * 5; week++)
        {
            tickPlain.AdvanceWeeks(plain, 1);
            var r = tickUi.AdvanceWeeksStoppingForPlayer(ui, 1);
            tickQuiet.AdvanceWeek(quiet, runPlayerEvents: false);
            Assert.Null(r.PendingPlayerEvent);
            Assert.Null(r.MainEventLost);
            Assert.Null(r.OfferNeedsYou);
            string d = Digest(plain);
            Assert.Equal(d, Digest(ui));
            Assert.Equal(d, Digest(quiet));
            Assert.False(ui.HasPlayer);
            Assert.Empty(ui.Mail);
            Assert.DoesNotContain(ui.Events.Values, e => e.Promotion == default);

            if (week % 52 == 0)
            {
                var diffs = PersistenceGraph.Diff(plain, ui);
                Assert.True(diffs.Count == 0, $"Week {week}: the UI advance ran apart:\n" + PersistenceGraph.Describe(diffs));
                diffs = PersistenceGraph.Diff(plain, quiet);
                Assert.True(diffs.Count == 0, $"Week {week}: runPlayerEvents:false ran apart:\n" + PersistenceGraph.Describe(diffs));
            }
        }
        // The sport went on without him.
        Assert.True(plain.Events.Values.Count(e => e.Status == EventStatus.Completed) > 20);
    }

    [Fact]
    public void ManyWeeksAtOnce_NeverStopsForAPlayer()
    {
        var a = Observer(3);
        var b = Observer(3);
        var report = new WorldTick().AdvanceWeeksStoppingForPlayer(a, 40);
        new WorldTick().AdvanceWeeks(b, 40);
        Assert.Equal(b.CurrentDate, a.CurrentDate);
        Assert.Null(report.PendingPlayerEvent);
        var diffs = PersistenceGraph.Diff(b, a);
        Assert.True(diffs.Count == 0, PersistenceGraph.Describe(diffs));
    }

    // ---- no inbox -----------------------------------------------------------------

    [Fact]
    public void MailPost_IsANoOp_WithNoPlayer_AndStillDelivers_WithOne()
    {
        var w = Observer(4);
        Mail.Post(w, w.CurrentDate, MailCategory.BoardNote, "Nobody", "Hello", "Is anyone there?");
        Assert.Empty(w.Mail);

        var p = new WorldBuilder().Build(WorldBuilder.Options.Origins(4));
        int before = p.Mail.Count;
        Mail.Post(p, p.CurrentDate, MailCategory.BoardNote, "Somebody", "Hello there", "Welcome.");
        Assert.Equal(before + 1, p.Mail.Count);
    }

    // ---- booking refusals -----------------------------------------------------------

    [Fact]
    public void Booking_RefusesEverything_WithNoHouse()
    {
        var w = Observer(6);
        var booking = new PlayerBooking();
        int events = w.Events.Count;
        var date = w.CurrentDate.AddDays(28);

        Assert.Null(booking.TryCreateEvent(w, date, out var why));
        Assert.Equal(PlayerBooking.NoHouseText, why);
        var thrown = Assert.Throws<InvalidOperationException>(() => booking.CreateEvent(w, date));
        Assert.Equal(PlayerBooking.NoHouseText, thrown.Message);

        var sfa = w.Promotions.Values.Single(p => p.Abbreviation == "SFA");
        Assert.Null(OriginsShows.ScheduleTournament(w, w.PlayerPromotion, date, out why));   // what the screen calls
        Assert.Equal(PlayerBooking.NoHouseText, why);
        var card = OriginsShows.SuggestCard(w, sfa.Id, date);
        Assert.Null(OriginsShows.ScheduleCard(w, w.PlayerPromotion, date, card));
        Assert.Equal(events, w.Events.Count);
        Assert.DoesNotContain(w.Events.Values, e => e.Promotion == default);

        // An AI card is nobody's to edit.
        var ai = w.Events.Values.Where(e => e.Status == EventStatus.Scheduled && e.Card.Count >= 2)
            .OrderBy(e => e.Id.Value).First();
        var before = ai.Card.Select(f => (f.Id, f.RedCorner, f.BlueCorner)).ToList();
        var bout = ai.Card[0];
        var spare = w.Fighters.Values.OrderBy(f => f.Id.Value).First(f => f.Id != bout.RedCorner && f.Id != bout.BlueCorner);

        Assert.False(booking.AddBout(w, ai, ai.Card[0].RedCorner, ai.Card[1].BlueCorner, 3, false, out why));
        Assert.Equal(PlayerBooking.NoHouseText, why);
        Assert.False(booking.AddLocalBout(w, ai, bout.RedCorner, PrimaryStyle.Striker, 3, out why));
        Assert.Equal(PlayerBooking.NoHouseText, why);
        Assert.False(booking.ReplaceFighter(w, ai, bout.Id, bout.RedCorner, spare.Id, out why));
        Assert.Equal(PlayerBooking.NoHouseText, why);
        Assert.False(booking.MoveBout(w, ai, 0, ai.Card.Count - 1, out why));
        Assert.Equal(PlayerBooking.NoHouseText, why);
        Assert.False(booking.SetMainCardSize(w, ai, 1, out why));
        Assert.Equal(PlayerBooking.NoHouseText, why);
        if (ai.VenueId is { } room)
        {
            Assert.False(booking.SetVenue(w, ai, room, out why));
            Assert.Equal(PlayerBooking.NoHouseText, why);
        }
        booking.RemoveBout(w, ai, bout.Id);
        booking.CancelEvent(w, ai);
        Assert.True(w.Events.ContainsKey(ai.Id));
        Assert.Equal(before, ai.Card.Select(f => (f.Id, f.RedCorner, f.BlueCorner)).ToList());

        // The read-only desk answers for nobody's cards.
        Assert.Empty(booking.UpcomingEvents(w));
        Assert.Empty(PlayerBooking.CardsBooking(w, bout.RedCorner));
        Assert.Empty(booking.VenueOptions(w, ai));
        Assert.Equal(0, booking.MainCardOnTheNight(w, ai));
        Assert.Empty(booking.Suggest(w, ai, BoutSlot.Prelim, 3));
    }

    // ---- the public read --------------------------------------------------------------

    [Fact]
    public void PublicRead_IsStable_FogsTheUnseen_AndIgnoresWhoHoldsHim()
    {
        var w = Observer(8);
        var unseen = w.Fighters.Values
            .Where(f => f.Record.TotalBouts == 0 && Camps.PublicExposure(w, f) == 0)
            .OrderBy(f => f.Id.Value).ToList();
        Assert.True(unseen.Count >= 40, $"only {unseen.Count} unseen men");

        int exact = 0;
        double absError = 0;
        foreach (var f in unseen)
        {
            int read = ScoutingService.PublicRead(w, f);
            Assert.Equal(read, ScoutingService.PublicRead(w, f));                     // stable, no stream
            int truth = FighterAbility.CurrentAbility(f);
            Assert.InRange(read, Math.Max(1, truth - 10), Math.Min(100, truth + 10));  // the error model's budget
            if (read == truth) exact++;
            absError += Math.Abs(read - truth);
        }
        // A 0-0 man is a total unknown to an outsider: the full error, spread across the budget.
        Assert.True(exact < unseen.Count * 0.2, $"{exact} of {unseen.Count} unseen men read exactly");
        double mean = absError / unseen.Count;
        Assert.InRange(mean, 3.0, 7.0);

        // Nobody's scouts behind it: signing him somewhere does not sharpen the public read.
        var free = unseen.First(f => f.Contract is null);
        int was = ScoutingService.PublicRead(w, free);
        var house = w.Promotions.Values.OrderBy(p => p.Id.Value).First();
        free.Contract = new Contract { Promotion = house.Id, SignedOn = w.CurrentDate, FightsRemaining = 3, PursePerFight = new Money(500), WinBonus = new Money(250) };
        Assert.Equal(was, ScoutingService.PublicRead(w, free));
    }

    // ---- save and load ------------------------------------------------------------------

    [Fact]
    public void SaveLoadRunOn_EqualsNeverSaved_ForAnObserver()
    {
        var world = Observer(17);
        new WorldTick().AdvanceWeeks(world, 30);
        string path = Path.Combine(Path.GetTempPath(), $"mma_observer_{Guid.NewGuid():N}.db");
        try
        {
            SaveGame.Save(world, path);
            var loaded = SaveGame.Load(path);
            Assert.False(loaded.HasPlayer);
            Assert.All(loaded.Promotions.Values, p => Assert.False(p.IsPlayerControlled));
            var diffs = PersistenceGraph.Diff(world, loaded);
            Assert.True(diffs.Count == 0, "Changed across the save:\n" + PersistenceGraph.Describe(diffs));

            new WorldTick().AdvanceWeeksStoppingForPlayer(world, 10);
            new WorldTick().AdvanceWeeksStoppingForPlayer(loaded, 10);
            diffs = PersistenceGraph.Diff(world, loaded);
            Assert.True(diffs.Count == 0, "Saved and never-saved worlds ran apart:\n" + PersistenceGraph.Describe(diffs));
            Assert.Empty(loaded.Mail);
        }
        finally { File.Delete(path); }
    }
}
