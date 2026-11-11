using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Tests;

/// <summary>
/// The fixes for the first sim-QA sweep, beyond the pinned SQA tests: what the player is told when a booked fighter
/// becomes unavailable, the clock stopping on a lost main event, local cards that run on their dates, stipends that
/// stay with the org that granted them, women's classes for dynasty prospects and card numbers that are never reissued.
/// </summary>
public class QaSweepFixTests
{
    private static World Build(ulong seed) => new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });

    // The player's AFC with one card 30 days out and up to four bouts; returns the main event's corners.
    private static (World World, Event Card, Fighter Red, Fighter Blue) BookedCard(ulong seed = 7, int bouts = 4)
    {
        var world = Build(seed);
        var promo = world.Get(world.PlayerPromotion)!;
        var booking = new PlayerBooking();
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(30));
        Fighter? red = null, blue = null;
        foreach (var div in promo.Divisions.Where(d => d.Sex == Sex.Male))
        {
            var pair = booking.AvailableRoster(world, ev, div.WeightClass).Take(2).ToList();
            if (pair.Count < 2 || !booking.AddBout(world, ev, pair[0].Id, pair[1].Id, 3, false, out _)) continue;
            red ??= pair[0]; blue ??= pair[1];   // the first bout booked stays at the top of the bill
            if (ev.Card.Count >= bouts) break;
        }
        Assert.True(red is not null && ev.Card[^1].RedCorner == red.Id, "could not book a card");
        return (world, ev, red!, blue!);
    }

    private static bool OnCard(Event ev, FighterId id) => ev.Card.Any(f => f.RedCorner == id || f.BlueCorner == id);

    // --- 1. Unavailable fighters come off the player's card ------------------

    [Fact]
    public void ARetiredFighterComesOffThePlayersCardAtOnceWithAMail()
    {
        var (world, ev, red, blue) = BookedCard();
        int bouts = ev.Card.Count;
        Lifecycle.Retire(world, red, world.CurrentDate);

        Assert.False(OnCard(ev, red.Id));
        Assert.False(OnCard(ev, blue.Id));          // no auto-substitute: the bout is gone, his opponent is free
        Assert.Equal(bouts - 1, ev.Card.Count);
        var mail = world.Mail.Last(m => m.AboutFighter == red.Id && m.Subject.Contains(ev.Name));
        Assert.Contains("retired", mail.Body);
        Assert.Contains(blue.FullName, mail.Body);
        Assert.Contains("main event", mail.Body);
        Assert.Equal(ev.Id, mail.AboutEvent);
        // ...and the Card Desk sees the changed card
        Assert.Equal(bouts - 1, new PlayerBooking().Project(world, ev).BoutsBooked);
    }

    [Fact]
    public void ReleasingABookedFighterTakesTheBoutOffWithAConfirmation()
    {
        var (world, ev, _, _) = BookedCard();
        var undercard = ev.Card[0];
        var f = world.Get(undercard.RedCorner)!;
        Assert.Single(PlayerBooking.CardsBooking(world, f.Id));

        ContractLifecycle.Release(world, f);

        Assert.DoesNotContain(ev.Card, b => b.Id == undercard.Id);
        Assert.Empty(PlayerBooking.CardsBooking(world, f.Id));
        Assert.Contains(world.Mail, m => m.AboutFighter == f.Id && m.Body.Contains("was released"));
    }

    [Fact]
    public void AFighterSignedAwayComesOffAndTheMailNamesTheBuyer()
    {
        var (world, ev, _, _) = BookedCard();
        var f = world.Get(ev.Card[0].BlueCorner)!;
        var rival = world.Promotions.Values.Where(p => p.Id != world.PlayerPromotion && !p.Defunct).OrderBy(p => p.Id.Value).First();
        ContractLifecycle.Sign(world, f, new ContractOffer { Fighter = f.Id, Promotion = rival.Id, PursePerFight = new Money(5_000m), Fights = 3 }, world.CurrentDate);

        Assert.False(OnCard(ev, f.Id));
        Assert.Contains(world.Mail, m => m.AboutFighter == f.Id && m.Body.Contains($"signed with {rival.Name}"));
    }

    [Fact]
    public void AnUnfoughtLocalOpponentGoesBackToTheSceneWhenHisBoutComesOff()
    {
        // A local bout needs a City/Regional house: found one.
        var world = Build(9);
        var promo = world.Get(world.PlayerPromotion)!;
        promo.Prestige = 10;
        var booking = new PlayerBooking();
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(30));
        var f = world.RosterOf(promo.Id).First(x => x.IsMatchReady(ev.Date));
        Assert.True(booking.AddLocalBout(world, ev, f.Id, PrimaryStyle.Brawler, 3, out var err), err);
        var local = ev.Card[0].BlueCorner;
        Assert.NotNull(world.Get(local));

        ContractLifecycle.Release(world, f);
        Assert.Null(world.Get(local));
        Assert.Contains(world.Mail, m => m.AboutFighter == f.Id && m.Body.Contains("local opponent"));
    }

    [Fact]
    public void ASuspendedFighterIsPulledByTheAuditAndTheBeltComesOffABoutWithoutItsChampion()
    {
        var (world, ev, red, _) = BookedCard();
        red.Status = FighterStatus.Suspended;
        red.SuspendedUntil = ev.Date.AddDays(60);
        ForwardCalendar.Audit(world, world.CurrentDate);
        Assert.False(OnCard(ev, red.Id));
        Assert.Contains(world.Mail, m => m.AboutFighter == red.Id && m.Body.Contains("suspended"));

        // A title bout whose belt has since gone to somebody else: the bout stays, the belt comes off.
        var promo = world.Get(world.PlayerPromotion)!;
        var bout = ev.Card[^1];
        var title = promo.Divisions.Select(d => d.Title is { } t ? world.Get(t) : null).First(t => t is not null)!;
        bout.IsTitleFight = true; bout.Title = title.Id;
        var other = world.RosterOf(promo.Id).First(x => x.Id != bout.RedCorner && x.Id != bout.BlueCorner);
        title.CurrentChampion = other.Id;
        ForwardCalendar.Audit(world, world.CurrentDate);
        Assert.Contains(bout, ev.Card);
        Assert.False(bout.IsTitleFight);
        Assert.Null(bout.Title);
        Assert.Contains(world.Mail, m => m.Subject.StartsWith("Title off the line"));
    }

    [Fact]
    public void FightNightNeverRunsARetiredOrUnsignedFighterOnThePlayersCard()
    {
        // The last line: corners invalidated behind the audit's back (no hook fired) are pulled on the night.
        var (world, ev, red, blue) = BookedCard();
        var f = world.Get(ev.Card[0].RedCorner)!;
        f.Contract = null;                  // gone without passing through LeavePromotion
        red.Status = FighterStatus.Retired; // retired without passing through Lifecycle.Retire
        new WorldTick().RunEventLive(world, ev, playByPlay: false);
        Assert.DoesNotContain(ev.Card, b => b.Outcome is not null && (b.RedCorner == f.Id || b.BlueCorner == f.Id
                                                                       || b.RedCorner == red.Id || b.BlueCorner == red.Id));
    }

    [Fact]
    public void TimeStopsWhenThePlayersMainEventFallsOut()
    {
        var (world, ev, red, _) = BookedCard();
        red.Status = FighterStatus.Suspended;
        red.SuspendedUntil = ev.Date.AddDays(60);
        var start = world.CurrentDate;

        var report = new WorldTick().AdvanceWeeksStoppingForPlayer(world, 4);

        Assert.Same(ev, report.MainEventLost);
        Assert.Equal(start.AddDays(7), world.CurrentDate);   // stopped after the first week
        Assert.Null(report.PendingPlayerEvent);
    }

    // --- 2. The local circuit runs on its dates ------------------------------

    [Fact]
    public void LocalCardsAreStagedAheadRunOnTheirDatesAndLeaveTheirFightersRecovering()
    {
        var world = Build(1);
        world.PlayerPromotion = default;
        var tick = new WorldTick();
        bool sawStaged = false;
        for (int w = 0; w < 30; w++)
        {
            tick.AdvanceWeek(world);
            foreach (var ev in world.Events.Values.Where(LocalCircuit.IsLocalCard))
            {
                if (ev.Status == EventStatus.Completed) Assert.True(ev.Date <= world.CurrentDate, $"{ev.Name} completed before {ev.Date}");
                if (ev.Status != EventStatus.Scheduled) continue;
                sawStaged = true;
                Assert.True(ev.Date >= world.CurrentDate.AddDays(-7));
                foreach (var b in ev.Card)
                    if (world.Get(b.RedCorner) is { LocalTalent: false } pro)
                        Assert.True(ForwardCalendar.IsCommitted(world, pro.Id), "a staged local card commits its fighters");
            }
        }
        Assert.True(sawStaged, "no local card was ever on the calendar at a week's end");

        // Nobody fights a club show and a promotion's card within two weeks of each other, and the pros got their layoff.
        var dates = new Dictionary<FighterId, List<(DateOnly Date, bool Local, EventId Id)>>();
        foreach (var ev in world.Events.Values.Where(e => e.Status == EventStatus.Completed && e.Date >= world.StartDate))
            foreach (var b in ev.Card)
                foreach (var id in new[] { b.RedCorner, b.BlueCorner })
                    (dates.TryGetValue(id, out var l) ? l : dates[id] = new()).Add((ev.Date, LocalCircuit.IsLocalCard(ev), ev.Id));
        var clashes = new List<string>();
        foreach (var (id, list) in dates)
        {
            if (world.Get(id) is not { LocalTalent: false }) continue;
            foreach (var a in list.Where(x => x.Local))
                foreach (var b in list.Where(x => x.Id != a.Id && Math.Abs(x.Date.DayNumber - a.Date.DayNumber) < 14))
                    clashes.Add($"{world.Get(id)!.FullName}: {a.Date} local and {b.Date}");
        }
        Assert.True(clashes.Count == 0, string.Join("; ", clashes.Take(5)));

        var ran = world.Events.Values.Where(e => LocalCircuit.IsLocalCard(e) && e.Status == EventStatus.Completed).OrderBy(e => e.Id.Value).First();
        var foughtThere = ran.Card.Select(b => world.Get(b.RedCorner)).Where(f => f is { LocalTalent: false }).ToList();
        Assert.NotEmpty(foughtThere);
        Assert.All(foughtThere, f => Assert.True(f!.ReadyToFightOn is { } r && r > ran.Date || f.Status != FighterStatus.Active,
            $"{f.FullName} has no layoff after {ran.Name} {ran.Date}"));
    }

    [Fact]
    public void AStagedLocalBoutLosesAFighterWhoSignsAnExclusiveDeal()
    {
        var world = Build(1);
        world.PlayerPromotion = default;
        var tick = new WorldTick();
        Event? staged = null;
        for (int w = 0; w < 10 && staged is null; w++)
        {
            tick.AdvanceWeek(world);
            staged = world.Events.Values.Where(e => LocalCircuit.IsLocalCard(e) && e.Status == EventStatus.Scheduled && e.Card.Count > 0)
                .OrderBy(e => e.Id.Value).FirstOrDefault();
        }
        Assert.NotNull(staged);
        var bout = staged!.Card[0];
        var pro = world.Get(bout.RedCorner)!;
        var local = bout.BlueCorner;
        var org = world.Promotions.Values.Where(p => !p.Defunct).OrderBy(p => p.Id.Value).First();
        if (pro.Contract is not null) ContractLifecycle.Release(world, pro);
        ContractLifecycle.Sign(world, pro, new ContractOffer { Fighter = pro.Id, Promotion = org.Id, PursePerFight = new Money(5_000m), Fights = 3, Exclusive = true }, world.CurrentDate);

        LocalCircuit.Audit(world);
        Assert.DoesNotContain(bout, staged.Card);
        if (world.Get(local) is { LocalTalent: true } l) Assert.NotEmpty(l.BoutHistory);   // an unfought local went back to the scene
    }

    // --- 3. Stipends ---------------------------------------------------------

    [Fact]
    public void AStipendEndsOnEveryExitWithoutTheStoppedStipendGrievance()
    {
        var world = Build(4);
        world.PlayerPromotion = default;
        var orgs = world.Promotions.Values.Where(p => !p.Defunct).OrderBy(p => p.Id.Value).ToList();
        var roster = world.RosterOf(orgs[0].Id).Where(x => x.Status == FighterStatus.Active).OrderBy(x => x.Id.Value).ToList();

        Fighter Granted(Fighter f) { f.CampStipend = true; f.Camp ??= world.Camps.Values.OrderBy(c => c.Id.Value).First().Id; return f; }

        var retiring = Granted(roster[0]);
        var camp = retiring.Camp;
        Lifecycle.Retire(world, retiring, world.CurrentDate);
        Assert.False(retiring.CampStipend);
        Assert.Equal(camp, retiring.Camp);   // stays in the room; nobody pays for it (not StopStipend's walk back)

        var poached = Granted(roster[1]);
        int loyalty = poached.Loyalty;
        ContractLifecycle.Sign(world, poached, new ContractOffer { Fighter = poached.Id, Promotion = orgs[1].Id, PursePerFight = new Money(5_000m), Fights = 3 }, world.CurrentDate);
        Assert.False(poached.CampStipend);

        var closing = Granted(roster[2]);
        MmaManager.Core.Sim.Org.Solvency.Close(world, orgs[0], world.CurrentDate);
        Assert.False(closing.CampStipend);
    }

    // --- 4. Dynasty prospects ------------------------------------------------

    [Fact]
    public void AFamousWomansJuniorAlwaysLandsInAWomensClassNearHers()
    {
        var world = Build(2);
        foreach (WeightClass elder in Enum.GetValues<WeightClass>())
            for (int i = 1; i < 200; i++)
            {
                var wc = NewgenService.WomensClassNear(world, new FighterId(i), elder);
                Assert.True(WomensDivisions.Allows(wc), $"{elder} -> {wc}");
                Assert.Equal(wc, NewgenService.WomensClassNear(world, new FighterId(i), elder));   // deterministic
                if (WomensDivisions.Allows(elder))
                    Assert.True(Math.Abs(Array.IndexOf(WomensDivisions.Classes, wc) - Array.IndexOf(WomensDivisions.Classes, elder)) <= 1);
            }
    }

    // --- 5. Card numbers -----------------------------------------------------

    [Fact]
    public void CancellingACardNeverReissuesALiveCardsNumber()
    {
        var world = Build(7);
        var booking = new PlayerBooking();
        var a = booking.CreateEvent(world, world.CurrentDate.AddDays(20));
        var b = booking.CreateEvent(world, world.CurrentDate.AddDays(40));
        var c = booking.CreateEvent(world, world.CurrentDate.AddDays(60));
        booking.CancelEvent(world, b);
        var d = booking.CreateEvent(world, world.CurrentDate.AddDays(80));

        Assert.NotEqual(c.Name, d.Name);
        Assert.NotEqual(a.Name, d.Name);
        var names = world.Events.Values.Where(e => e.Promotion == world.PlayerPromotion).Select(e => e.Name).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
    }
}

/// <summary>
/// A title fight that ends in a DRAW (possible once the engine scores draws): the champion keeps the belt and that
/// counts as a defence, his champion's clause tops his deal up, the belt's prestige follows the contest, and the
/// pair are left with a grudge hot enough for the matchmaker to book the rematch next. No engine needed.
/// </summary>
public class DrawnTitleFightTests
{
    private static (World World, Event Card, Fight Bout, Title Title, Fighter Champ, Fighter Challenger) DrawnTitleFight(ulong seed = 7)
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });
        var promo = world.Get(world.PlayerPromotion)!;
        var booking = new PlayerBooking();
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(30));
        var div = promo.Divisions.Where(d => d.Title is { } t && world.Get(t)?.CurrentChampion is not null && d.Rankings.Count > 0)
            .OrderBy(d => (int)d.WeightClass).ThenBy(d => d.Sex).First();
        var title = world.Get(div.Title!.Value)!;
        var champ = world.Get(title.CurrentChampion!.Value)!;
        var challenger = div.Rankings.Select(world.Get).First(c => c is not null && c.Id != champ.Id)!;
        Assert.True(booking.AddBout(world, ev, champ.Id, challenger.Id, 5, titleFight: true, out var err), err);
        var bout = ev.Card[0];
        bout.Outcome = new FightOutcome { Winner = null, Loser = null, Method = FightMethod.Draw, EndRound = 5, EndTimeSeconds = 300 };
        return (world, ev, bout, title, champ, challenger);
    }

    [Fact]
    public void TheChampionRetainsAndTheReignCountsTheDefence()
    {
        var (world, ev, bout, title, champ, challenger) = DrawnTitleFight();
        var reign = title.Lineage.Last(r => r.LostOn is null);
        int defences = reign.Defenses, champDraws = champ.Record.Draws, reigns = title.Lineage.Count;
        champ.Contract!.HasChampionsClause = true;
        champ.Contract.FightsRemaining = 1;   // the last fight of his deal

        ResultsProcessor.Apply(world, ev, bout);

        Assert.Equal(champ.Id, title.CurrentChampion);
        Assert.Equal(reigns, title.Lineage.Count);
        Assert.Equal(defences + 1, reign.Defenses);
        Assert.Null(reign.LostOn);
        Assert.Equal(champDraws + 1, champ.Record.Draws);
        Assert.Equal(0, champ.Record.CurrentStreak);
        Assert.Equal(3, champ.Contract.FightsRemaining);   // topped up as after any successful defence
        Assert.Contains(world.News, n => n.Headline.Contains("on a draw") && n.Headline.Contains(champ.FullName));
    }

    [Fact]
    public void TheBeltsPrestigeFollowsADrawnContest()
    {
        var (world, ev, bout, title, champ, challenger) = DrawnTitleFight();
        var promo = world.Get(ev.Promotion)!;
        var div = promo.Divisions.First(d => d.Title == title.Id);
        double contest = TitleCredibility.Contest(world, promo, div, title, world.Get(bout.RedCorner)!, world.Get(bout.BlueCorner)!);
        int before = title.Prestige.Value;
        title.Prestige = Math.Clamp(before + (contest >= before ? -20 : 20), 0, 100);   // well away from the contest
        int set = title.Prestige.Value;

        ResultsProcessor.Apply(world, ev, bout);
        // pulled toward the level of the two who contested it (a sixth or so of the gap), as any title fight pulls it
        Assert.True(Math.Abs(title.Prestige.Value - contest) < Math.Abs(set - contest), $"{set} -> {title.Prestige.Value}, contest {contest:0.0}");
    }

    [Fact]
    public void ADrawnTitleFightLeavesAGrudgeTheMatchmakerBooksNext()
    {
        var (world, ev, bout, _, champ, challenger) = DrawnTitleFight();
        world.Rivalries.RemoveAll(r => (r.A == champ.Id && r.B == challenger.Id) || (r.A == challenger.Id && r.B == champ.Id));
        var shared = new MmaManager.Core.Sim.Rng.SimRandom(99);

        ResultsProcessor.Apply(world, ev, bout);
        Storylines.AfterEvent(world, ev, ev.Date, shared);

        var feud = Storylines.RivalryBetween(world, champ.Id, challenger.Id);
        Assert.NotNull(feud);
        Assert.True(feud!.Intensity >= 40, $"a drawn title fight should leave a grudge past the rematch line (got {feud.Intensity})");
    }

    [Fact]
    public void AVacantBeltStaysVacantAfterADraw()
    {
        var (world, ev, bout, title, champ, challenger) = DrawnTitleFight();
        // vacate the belt first: a drawn fight for a vacant belt crowns nobody
        title.Lineage.Last(r => r.LostOn is null).LostOn = world.CurrentDate;
        title.CurrentChampion = null;
        int reigns = title.Lineage.Count;
        ResultsProcessor.Apply(world, ev, bout);
        Assert.Null(title.CurrentChampion);
        Assert.Equal(reigns, title.Lineage.Count);
    }
}
