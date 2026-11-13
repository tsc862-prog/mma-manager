using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Tests;

/// <summary>
/// The player's deals really end: the last fight closes the deal, a 30-day
/// exclusive re-sign window follows (nobody else may sign him, nobody may book
/// him), and then he is a free agent. Talks are remembered between sit-downs.
/// </summary>
public class DealWindowTests
{
    // --- The deal ends at the last fight -------------------------------------------

    [Fact]
    public void TheDeal_EndsAtTheLastFight_AndTheWindowOpensThatNight()
    {
        var world = FoundedWorld(48);
        var (a, b) = Pair(world);
        a.Contract!.FightsRemaining = 1;   // his last fight
        b.Contract!.FightsRemaining = 3;
        var booking = new PlayerBooking();
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(7));
        Assert.True(booking.AddBout(world, ev, a.Id, b.Id, 3, false, out var err), err);

        new WorldTick().RunEventLive(world, ev, playByPlay: false);

        Assert.Equal(world.PlayerPromotion, a.Contract?.Promotion);   // still ours, out of contract
        Assert.Equal(0, a.Contract!.FightsRemaining);
        Assert.Equal(ev.Date.AddDays(DealWindow.WindowDays), a.Contract.WindowEndsOn);
        Assert.True(DealWindow.InWindow(world, a));
        Assert.Contains(world.Mail, m => m.Category == MailCategory.ContractAlert && m.AboutFighter == a.Id
                                         && m.Subject.Contains("re-sign"));
        // The other corner still has fights on his deal: no window.
        Assert.Equal(2, b.Contract!.FightsRemaining);
        Assert.Null(b.Contract.WindowEndsOn);
        Assert.False(DealWindow.InWindow(world, b));
    }

    // --- Hands off: the market and the booker -------------------------------------------

    [Fact]
    public void InTheWindow_NoRivalSignsOrPoachesHim_AndNobodyBooksHim()
    {
        var world = FoundedWorld(48);
        var f = Star(world, Disposition.CompanyMan, loyalty: 30);
        Assert.True(PoachingService.HasSuitor(world, f), "precondition: a bigger promotion wants him");
        DealWindow.Open(world, f, world.CurrentDate);
        var end = f.Contract!.WindowEndsOn!.Value;

        // The monthly expiry pass, free agency and three weeks of the living world:
        // he stays ours, with no new deal (a poach at expiry would have taken him).
        ContractLifecycle.ProcessExpiries(world, world.CurrentDate);
        FreeAgencyService.RunMonthly(world, world.CurrentDate);
        new WorldTick().AdvanceWeeks(world, 3);
        Assert.True(world.CurrentDate < end);
        Assert.Equal(world.PlayerPromotion, f.Contract?.Promotion);
        Assert.Equal(0, f.Contract!.FightsRemaining);
        Assert.DoesNotContain(world.BiddingWars, w => w.Fighter == f.Id);

        // Not on anyone's card, and the player can't book him either.
        Assert.False(ForwardCalendar.IsCommitted(world, f.Id));
        var booking = new PlayerBooking();
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(14));
        var opp = HireOne(world);
        opp.WeightClass = f.WeightClass; opp.Sex = f.Sex;
        Assert.False(booking.AddBout(world, ev, f.Id, opp.Id, 3, false, out var err));
        Assert.Contains("deal is up", err);
        Assert.False(booking.AddLocalBout(world, ev, f.Id, PrimaryStyle.Striker, 3, out _));
        Assert.DoesNotContain(booking.AvailableRoster(world, ev, f.WeightClass, f.Sex), x => x.Id == f.Id);
    }

    [Fact]
    public void ABoutAlreadyOnTheCard_ComesOffWhenTheWindowOpens()
    {
        var world = FoundedWorld(48);
        var (a, b) = Pair(world);
        var booking = new PlayerBooking();
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(21));
        Assert.True(booking.AddBout(world, ev, a.Id, b.Id, 3, false, out var err), err);
        a.Contract!.FightsRemaining = 0;   // (a deal that reached zero some other way)

        DealWindow.RunWeekly(world, world.CurrentDate);   // the safety net opens his window

        Assert.NotNull(a.Contract.WindowEndsOn);
        Assert.DoesNotContain(ev.Card, x => x.RedCorner == a.Id || x.BlueCorner == a.Id);
        Assert.Contains(world.Mail, m => m.Subject == $"{a.FullName} is off {ev.Name}");
    }

    // --- The player re-signs inside it ---------------------------------------------------

    [Fact]
    public void ThePlayer_CanReSignHim_InTheWindow_AndHeIsBookableAgain()
    {
        var world = FoundedWorld(48);
        var f = Star(world, Disposition.Warhorse, loyalty: 40);
        DealWindow.Open(world, f, world.CurrentDate);
        world.CurrentDate = world.CurrentDate.AddDays(14);

        var promo = world.Get(world.PlayerPromotion)!;
        var exp = new FighterValuation().Evaluate(world, f);
        var talks = NegotiationSession.Resume(world, f, promo, exp);
        var offer = talks.TheirTermsAsOffer(Negotiator.WantsExclusivity(promo), clause: false);
        Assert.Equal(TalkVerdict.Accept, talks.Table(offer).Verdict);
        int loyalty = f.Loyalty;
        ContractLifecycle.Sign(world, f, offer, world.CurrentDate);

        Assert.True(f.Contract!.FightsRemaining > 0);
        Assert.Null(f.Contract.WindowEndsOn);
        Assert.False(DealWindow.InWindow(world, f));
        Assert.Equal(loyalty + ContractLifecycle.RenewalLoyalty, f.Loyalty);   // a worked deal, renewed

        var opp = HireOne(world);
        opp.WeightClass = f.WeightClass; opp.Sex = f.Sex; opp.ReadyToFightOn = null;
        f.ReadyToFightOn = null;
        var booking = new PlayerBooking();
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(14));
        Assert.True(booking.AddBout(world, ev, f.Id, opp.Id, 3, false, out var err), err);

        // His window is closed for good: the weekly pass leaves the new deal alone.
        DealWindow.RunWeekly(world, world.CurrentDate.AddDays(30));
        Assert.Equal(world.PlayerPromotion, f.Contract.Promotion);
    }

    // --- ...or he walks onto the open market ------------------------------------------------

    [Fact]
    public void WhenTheWindowCloses_HeIsAFreeAgent_AfterALastWeekWarning()
    {
        var world = FoundedWorld(48);
        var f = Star(world, Disposition.Warhorse, loyalty: 40);
        var day0 = world.CurrentDate;
        DealWindow.Open(world, f, day0);
        var end = f.Contract!.WindowEndsOn!.Value;

        DealWindow.RunWeekly(world, end.AddDays(-8));
        Assert.False(f.Contract.WindowWarned);
        DealWindow.RunWeekly(world, end.AddDays(-3));
        Assert.True(f.Contract.WindowWarned);
        Assert.Single(world.Mail, m => m.Subject == $"Last week to re-sign {f.FullName}");
        DealWindow.RunWeekly(world, end.AddDays(-1));
        Assert.Equal(world.PlayerPromotion, f.Contract?.Promotion);   // still ours on the last day before it closes

        DealWindow.RunWeekly(world, end);
        Assert.Null(f.Contract);
        Assert.Contains(f, world.FreeAgents());
        Assert.Contains(world.Mail, m => m.Subject == $"{f.FullName} is a free agent");
        Assert.DoesNotContain(world.Get(world.PlayerPromotion)!.Divisions, d => d.Rankings.Contains(f.Id));
    }

    [Fact]
    public void AChampion_KeepsTheBeltThroughTheWindow_AndItIsVacantIfHeWalks()
    {
        var world = FoundedWorld(48);
        var promo = world.Get(world.PlayerPromotion)!;
        var f = Star(world, Disposition.Warhorse, loyalty: 40);
        var div = promo.Divisions.First(d => d.Matches(f));
        PromotionSetup.CommissionTitle(world, promo, div.WeightClass, div.Sex, world.CurrentDate);
        var belt = world.Get(div.Title!.Value)!;
        belt.CurrentChampion = f.Id;
        belt.Lineage.Add(new TitleReign { Champion = f.Id, WonOn = world.CurrentDate });

        DealWindow.Open(world, f, world.CurrentDate);
        var end = f.Contract!.WindowEndsOn!.Value;
        Assert.Contains(world.Mail, m => m.AboutFighter == f.Id && m.Body.Contains("keeps the belt"));
        ContractLifecycle.ProcessExpiries(world, world.CurrentDate);
        DealWindow.RunWeekly(world, end.AddDays(-1));
        Assert.Equal(f.Id, belt.CurrentChampion);

        DealWindow.RunWeekly(world, end);
        Assert.Null(belt.CurrentChampion);
        Assert.Equal(end, belt.Lineage[^1].LostOn);
    }

    // --- A lifer in his window ---------------------------------------------------------

    [Fact]
    public void ALiferInHisWindow_WantsToStay_AtHisHometownRate_AndLeavesAsTheHousesDecisionIfItLapses()
    {
        var world = FoundedWorld(48);
        var lifer = Star(world, Disposition.CompanyMan, loyalty: 90);
        lifer.Designed = true;
        Assert.True(Lifers.IsHolding(world, lifer), "precondition: a holding lifer");
        DealWindow.Open(world, lifer, world.CurrentDate);
        var end = lifer.Contract!.WindowEndsOn!.Value;

        // He is told about as staying, the room quotes the hometown number, and
        // the AI side neither renews nor poaches him (no refusal story either).
        Assert.Contains(world.Mail, m => m.AboutFighter == lifer.Id && m.Body.Contains("hometown rate"));
        var promo = world.Get(world.PlayerPromotion)!;
        var exp = new FighterValuation().Evaluate(world, lifer);
        Assert.True(NegotiationSession.Resume(world, lifer, promo, exp).DemandPurse(true, false).Amount < exp.AskingPurse.Amount);
        ContractLifecycle.ProcessExpiries(world, world.CurrentDate);
        Assert.Equal(0, lifer.Contract.FightsRemaining);
        Assert.DoesNotContain(world.News, n => n.Headline.StartsWith($"{lifer.FullName} turns down"));

        // Let it lapse: the house let him go — a lifer's departure, told as such.
        DealWindow.RunWeekly(world, end);
        Assert.Null(lifer.Contract);
        Assert.Contains(world.News, n => n.AboutFighter == lifer.Id && n.Headline.StartsWith($"{lifer.FullName} leaves")
                                         && n.Headline.Contains("let his deal run out"));
        Assert.Contains(world.Mail, m => m.Subject == $"{lifer.FullName} has left us");
    }

    [Fact]
    public void TheRealMonthlyPass_SendsNoRivalStoryOrCirclingMail_ForFightersInTheirWindow()
    {
        var world = FoundedWorld(48);
        var tick = new WorldTick();
        // Stop on the last week of a month, so the next tick runs the monthly block.
        while (world.CurrentDate.AddDays(7).Month == world.CurrentDate.Month) tick.AdvanceWeek(world);
        var lifer = Star(world, Disposition.CompanyMan, loyalty: 90);
        var hired = Star(world, Disposition.Warhorse, loyalty: 30);
        Assert.True(Lifers.IsHolding(world, lifer), "precondition: a holding lifer");
        Assert.True(PoachingService.HasSuitor(world, hired), "precondition: a rival wants the other one");
        DealWindow.Open(world, lifer, world.CurrentDate);
        DealWindow.Open(world, hired, world.CurrentDate);

        var month = world.CurrentDate.Month;
        tick.AdvanceWeek(world);   // OfferMarket, Lifers.RunMonthly, ProcessExpiries, free agency, MonthlyReview...
        Assert.NotEqual(month, world.CurrentDate.Month);

        foreach (var f in new[] { lifer, hired })
        {
            Assert.Equal(world.PlayerPromotion, f.Contract?.Promotion);
            Assert.DoesNotContain(world.News, n => n.Headline.StartsWith($"{f.FullName} turns down"));
            Assert.DoesNotContain(world.Mail, m => m.Subject.StartsWith($"{f.FullName} turned down"));
            Assert.DoesNotContain(world.Mail, m => m.Subject.EndsWith($"is circling {f.FullName}"));
            Assert.False(f.Contract!.PoachWarned);
        }
    }

    // --- A walk-out inside the window -----------------------------------------------

    [Theory]
    [InlineData(2, 23)]    // a full cool-off still ends well inside the window
    [InlineData(9, 27)]    // capped: the door reopens 3 days before the window ends
    [InlineData(26, 27)]   // capped, and still after today
    public void AWalkOutInTheWindow_ReopensTheDoorBeforeTheWindowEnds(int day, int reopensOn)
    {
        var (world, f, day0, end) = WalkOutOnDay(day);
        Assert.Equal(day0.AddDays(reopensOn), NegotiationSession.SouredUntil(world, f.Id, world.PlayerPromotion));
        Assert.True(day0.AddDays(reopensOn) < end);
        Assert.False(DealWindow.TalksClosed(world, f));
        Assert.DoesNotContain(world.Mail, m => m.Subject == $"{f.FullName} will test the market");
    }

    [Fact]
    public void AWalkOutInTheLastThreeDays_ClosesTalks_AndHeTestsTheMarket()
    {
        var (world, f, _, end) = WalkOutOnDay(28);
        Assert.True(DealWindow.TalksClosed(world, f));
        Assert.True(NegotiationSession.SouredUntil(world, f.Id, world.PlayerPromotion) >= end);
        Assert.Contains(world.Mail, m => m.Subject == $"{f.FullName} will test the market" && m.Body.Contains("Talks are closed"));
        DealWindow.RunWeekly(world, end);
        Assert.Null(f.Contract);   // a free agent
    }

    // A star in his window from day0, who walks out of talks on day0 + day.
    private static (World W, Fighter F, DateOnly Day0, DateOnly End) WalkOutOnDay(int day)
    {
        var world = FoundedWorld(48);
        var f = Star(world, Disposition.Warhorse, loyalty: 40);
        var day0 = world.CurrentDate;
        DealWindow.Open(world, f, day0);
        world.CurrentDate = day0.AddDays(day);
        var promo = world.Get(world.PlayerPromotion)!;
        var talks = NegotiationSession.Resume(world, f, promo, new FighterValuation().Evaluate(world, f));
        for (int i = 0; i < 8 && talks.Outcome == TalkOutcome.Open; i++)
        {
            var low = talks.TheirTermsAsOffer(true, false);
            low.PursePerFight = new Money(Math.Round(low.PursePerFight.Amount * 0.3m));
            talks.Table(low);
        }
        Assert.Equal(TalkOutcome.TheyWalked, talks.Outcome);
        talks.Remember(world.CurrentDate);
        return (world, f, day0, f.Contract!.WindowEndsOn!.Value);
    }

    // --- The AI world is untouched ---------------------------------------------------

    [Fact]
    public void AnAiPromotionsDeal_StillResolvesInTheMonthlyPass_WithNoWindow()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 11 });
        var org = world.Promotions.Values.First(p => p.Id != world.PlayerPromotion && !p.Defunct && world.RosterOf(p.Id).Any());
        var f = world.RosterOf(org.Id).Where(x => x.Status == FighterStatus.Active && !world.Titles.Values.Any(t => t.CurrentChampion == x.Id))
            .OrderBy(x => x.Id.Value).First();
        f.Contract!.FightsRemaining = 0;
        DealWindow.RunWeekly(world, world.CurrentDate);
        Assert.Null(f.Contract.WindowEndsOn);
        Assert.False(DealWindow.InWindow(world, f));
        ContractLifecycle.ProcessExpiries(world, world.CurrentDate);
        Assert.True(f.Status == FighterStatus.Retired || f.Contract is null || f.Contract.FightsRemaining > 0,
            "renewed, walked, poached or retired — resolved either way");
    }

    // --- Talks remembered ----------------------------------------------------------------

    [Fact]
    public void Talks_PickUpWhereTheyLeftOff_AndAWalkOutClosesTheDoor()
    {
        var world = FoundedWorld(48);
        var promo = world.Get(world.PlayerPromotion)!;
        var f = world.FreeAgents().Where(x => x.Status == FighterStatus.Active).OrderBy(x => x.Id.Value).First();
        var exp = new FighterValuation().Evaluate(world, f);
        bool excl = Negotiator.WantsExclusivity(promo);

        // A fair-but-short offer moves his number down; then the player leaves.
        var first = NegotiationSession.Resume(world, f, promo, exp);
        Assert.False(first.Resumed);
        var fresh = first.DemandPurse(excl, false);
        var offer = first.TheirTermsAsOffer(excl, false);
        offer.PursePerFight = new Money(Math.Round(offer.PursePerFight.Amount * 0.85m));
        Assert.Equal(TalkVerdict.Counter, first.Table(offer).Verdict);
        var moved = first.DemandPurse(excl, false);
        Assert.True(moved.Amount <= fresh.Amount);
        first.WalkAway();
        first.Remember(world.CurrentDate);

        // A week later he sits down at the same number, mood and patience — not a reset.
        world.CurrentDate = world.CurrentDate.AddDays(7);
        var again = NegotiationSession.Resume(world, f, promo, exp);
        Assert.True(again.Resumed);
        Assert.Equal(moved, again.DemandPurse(excl, false));
        Assert.Equal(first.Patience, again.Patience);
        Assert.Equal(first.Mood, again.Mood);
        Assert.Equal(first.Round, again.Round);

        // Lowball him until he walks: the door is closed for the cool-off, then talks start over.
        for (int i = 0; i < 6 && again.Outcome == TalkOutcome.Open; i++)
        {
            var low = again.TheirTermsAsOffer(excl, false);
            low.PursePerFight = new Money(Math.Round(low.PursePerFight.Amount * 0.3m));
            again.Table(low);
        }
        Assert.Equal(TalkOutcome.TheyWalked, again.Outcome);
        again.Remember(world.CurrentDate);
        Assert.Equal(world.CurrentDate.AddDays(NegotiationSession.CoolOffDays), NegotiationSession.SouredUntil(world, f.Id, promo.Id));
        world.CurrentDate = world.CurrentDate.AddDays(NegotiationSession.CoolOffDays);
        Assert.Null(NegotiationSession.SouredUntil(world, f.Id, promo.Id));
        var over = NegotiationSession.Resume(world, f, promo, exp);
        Assert.False(over.Resumed);
        Assert.Equal(fresh, over.DemandPurse(excl, false));

        // A signature forgets the talks.
        var deal = over.TheirTermsAsOffer(excl, false);
        Assert.Equal(TalkVerdict.Accept, over.Table(deal).Verdict);
        over.Remember(world.CurrentDate);
        Assert.DoesNotContain(world.Talks, t => t.Fighter == f.Id);
    }

    [Fact]
    public void Talks_GoneColdForTwoMonths_AreForgotten()
    {
        var world = FoundedWorld(48);
        var promo = world.Get(world.PlayerPromotion)!;
        var f = world.FreeAgents().Where(x => x.Status == FighterStatus.Active).OrderBy(x => x.Id.Value).First();
        var exp = new FighterValuation().Evaluate(world, f);
        var s = NegotiationSession.Resume(world, f, promo, exp);
        var offer = s.TheirTermsAsOffer(true, false);
        offer.PursePerFight = new Money(Math.Round(offer.PursePerFight.Amount * 0.85m));
        s.Table(offer);
        s.WalkAway();
        s.Remember(world.CurrentDate);
        Assert.Single(world.Talks);

        world.CurrentDate = world.CurrentDate.AddDays(DealWindow.TalksMemoryDays + 1);
        Assert.False(NegotiationSession.Resume(world, f, promo, exp).Resumed);
        DealWindow.RunWeekly(world, world.CurrentDate);
        Assert.Empty(world.Talks);
    }

    // --- helpers ------------------------------------------------------------------------

    private static World FoundedWorld(ulong seed) =>
        new WorldBuilder().Build(new WorldBuilder.Options
        {
            Seed = seed,
            Founding = new WorldBuilder.FoundingSpec
            {
                Prestige = 20,
                StartingBalance = new Money(2_000_000m),
                Divisions = new[] { WeightClass.Lightweight, WeightClass.Welterweight },
            },
        });

    // Two of the player's fighters who can meet: same class and sex, fit to fight.
    private static (Fighter A, Fighter B) Pair(World world)
    {
        var a = HireOne(world);
        var b = HireOne(world);
        b.WeightClass = a.WeightClass; b.Sex = a.Sex;
        a.ReadyToFightOn = null; b.ReadyToFightOn = null;
        a.Status = FighterStatus.Active; b.Status = FighterStatus.Active;
        a.CurrentInjury = null; b.CurrentInjury = null;
        return (a, b);
    }

    // A proven, in-form, popular fighter on the player's roster whose deal has just run out.
    private static Fighter Star(World world, Disposition d, int loyalty)
    {
        var f = HireOne(world);
        foreach (var a in RaiseAbility(f)) a(72);
        f.Record.Wins = 14; f.Record.Losses = 2; f.Record.WinsByKo = 7; f.Record.WinsByDecision = 7;
        f.Record.CurrentStreak = 4;
        f.Intangibles.Popularity = 55;
        f.Disposition = d;
        f.Loyalty = loyalty;
        f.Contract!.FightsRemaining = 0;
        return f;
    }

    private static Fighter HireOne(World world)
    {
        var promo = world.Get(world.PlayerPromotion)!;
        var pick = SigningAdvisor.Suggest(world, promo)!;
        var exp = new FighterValuation().Evaluate(world, pick.Fighter);
        ContractLifecycle.Sign(world, pick.Fighter, new ContractOffer
        {
            Fighter = pick.Fighter.Id, Promotion = promo.Id,
            PursePerFight = exp.AskingPurse, WinBonus = exp.AskingWinBonus, Fights = exp.DesiredFights,
        }, world.CurrentDate);
        return pick.Fighter;
    }

    private static IEnumerable<Action<int>> RaiseAbility(Fighter f) => new Action<int>[]
    {
        v => f.Striking.PunchPower = v, v => f.Striking.PunchTechnique = v, v => f.Striking.StrikingDefense = v,
        v => f.Striking.Footwork = v, v => f.Striking.KickTechnique = v, v => f.Grappling.TakedownOffense = v,
        v => f.Grappling.TakedownDefense = v, v => f.Grappling.TopControl = v, v => f.Grappling.Scrambling = v,
        v => f.Grappling.SubmissionOffense = v, v => f.Grappling.SubmissionDefense = v,
        v => f.Physical.Cardio = v, v => f.Physical.Speed = v, v => f.Physical.Chin = v, v => f.Physical.Strength = v,
        v => f.Mental.FightIQ = v, v => f.Mental.Composure = v, v => f.Mental.Heart = v,
    };
}
