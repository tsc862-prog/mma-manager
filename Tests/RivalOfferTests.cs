using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Living;
using Xunit.Abstractions;

namespace MmaManager.Tests;

/// <summary>
/// Rival offers for the player's fighters (contracts stage 6, Contracts.OfferMarket):
/// on the last fight of his deal a bigger promotion may put real paper in front of
/// him; the owner matches, sits down with him, or lets him go — and if he is let go
/// he signs there the day his exclusive re-sign window closes.
/// </summary>
public class RivalOfferTests
{
    private readonly ITestOutputHelper _out;
    public RivalOfferTests(ITestOutputHelper output) => _out = output;

    // --- When an offer comes ----------------------------------------------------------

    [Fact]
    public void AnOffer_IsLodgedOnlyForThePlayersFighters_OnTheLastFightOfTheDeal()
    {
        var world = FoundedWorld(48);
        var day = world.CurrentDate;
        var f = Star(world, loyalty: 20);
        Assert.True(PoachingService.HasSuitor(world, f), "precondition: a bigger promotion wants him");

        f.Contract!.FightsRemaining = 2;
        Assert.Null(OfferMarket.Call(world, f, day));   // two fights left: not yet

        // In his window the player has him to himself: nobody calls.
        var g = Star(world, loyalty: 20);
        g.Contract!.FightsRemaining = 0;
        DealWindow.Open(world, g, day);
        Assert.Null(OfferMarket.Call(world, g, day));

        // AI promotions' fighters on their last fight are the AI market's business.
        foreach (var x in world.Fighters.Values.Where(x => x.Status == FighterStatus.Active
                     && x.Contract is { } c && c.Promotion != world.PlayerPromotion).Take(300))
            x.Contract!.FightsRemaining = 1;

        f.Contract.FightsRemaining = 1;
        RivalOffer? made = null;
        for (int w = 1; w <= 26 && made is null; w++)
        {
            world.CurrentDate = day.AddDays(7 * w);
            OfferMarket.RunWeekly(world, world.CurrentDate);
            made = OfferMarket.LiveFor(world, f.Id);
        }
        Assert.NotNull(made);
        Assert.All(world.RivalOffers, o => Assert.Equal(world.PlayerPromotion, o.Holder));
        Assert.DoesNotContain(world.RivalOffers, o => o.Fighter == g.Id);
        Assert.NotEqual(world.PlayerPromotion, made.From);
        Assert.Equal(RivalOfferState.Pending, made.State);
        Assert.Equal(made.Made.AddDays(OfferMarket.AnswerDays), made.Deadline);
        Assert.True(made.Purse.Amount > 0 && made.Fights > 0);
        Assert.Contains(world.Decisions, d => d.Kind == DecisionKind.RivalOffer && d.Fighter == f.Id && d.Promotion == made.From);
        Assert.Contains(world.Mail, m => m.Category == MailCategory.FighterRequest && m.AboutFighter == f.Id
                                         && m.Subject == $"{f.FullName} has an offer from {world.Get(made.From)!.Abbreviation}");

        // One call per deal: the weeks run on, no second offer.
        for (int w = 1; w <= 8; w++) OfferMarket.RunWeekly(world, world.CurrentDate.AddDays(7 * w));
        Assert.Single(world.RivalOffers, o => o.Fighter == f.Id);
    }

    [Fact]
    public void Offers_ComeTwoToFiveTimesAYear_ForATypicalRoster()
    {
        // Three seeds, two years each, a script-built roster of ~20 (see MeasureFounded).
        var perYear = new List<double>();
        foreach (ulong seed in new ulong[] { 3, 7, 11 })
        {
            var (made, roster) = MeasureFounded(seed, years: 2);
            perYear.Add(made.Count / 2.0);
            int worstYear = made.Count == 0 ? 0 : made.Max(d => made.Count(x => x >= d && x.DayNumber - d.DayNumber < 365));
            _out.WriteLine($"seed {seed}: {made.Count} offers in 2 years · roster {roster:0.0} · most in any 365 days {worstYear}");
            Assert.True(worstYear <= OfferMarket.MaxPerYear, $"seed {seed}: {worstYear} offers inside one year");
        }
        _out.WriteLine($"mean {perYear.Average():0.00} a year");
        Assert.InRange(perYear.Average(), 2.0, 5.0);
    }

    // --- Match ---------------------------------------------------------------------------

    [Fact]
    public void Matching_ReSignsHimNow_OnTermsThatBeatTheRival_AndClearsTheOffer()
    {
        var (world, f, o) = WithOffer(48);
        var desk = world.Decisions.Single(d => d.Kind == DecisionKind.RivalOffer && d.Fighter == f.Id);
        var exp = new FighterValuation().Evaluate(world, f);
        var terms = OfferMarket.MatchTerms(world, o);
        Assert.Equal(terms.PursePerFight, desk.Price);
        Assert.True(OfferMarket.Score(world, f, terms, exp) >= OfferMarket.Score(world, f, OfferMarket.AsContractOffer(o), exp),
            "the match beats the rival's paper by his own lights");

        Decisions.Accept(world, desk, world.CurrentDate);

        Assert.Equal(RivalOfferState.Matched, o.State);
        Assert.Equal(world.CurrentDate, o.Resolved);
        Assert.Null(OfferMarket.LiveFor(world, f.Id));
        Assert.DoesNotContain(world.Decisions, d => d.Kind == DecisionKind.RivalOffer);
        Assert.Equal(world.PlayerPromotion, f.Contract!.Promotion);
        Assert.Equal(o.Fights, f.Contract.FightsRemaining);
        Assert.Equal(terms.PursePerFight, f.Contract.PursePerFight);

        // Nothing is left to take him at the end of this deal.
        for (int w = 1; w <= 10; w++) OfferMarket.RunWeekly(world, world.CurrentDate.AddDays(7 * w));
        Assert.Single(world.RivalOffers);
        Assert.Equal(RivalOfferState.Matched, o.State);
    }

    // --- Open the table ----------------------------------------------------------------------

    [Fact]
    public void AtTheTable_HeWontGoUnderWhatBeatsTheRival_AndSigningThereSettlesTheOffer()
    {
        var (world, f, o) = WithOffer(48);
        var promo = world.Get(world.PlayerPromotion)!;
        bool excl = Negotiator.WantsExclusivity(promo);
        var exp = new FighterValuation().Evaluate(world, f);
        var match = OfferMarket.MatchTerms(world, o).PursePerFight;

        var room = NegotiationSession.Resume(world, f, promo, exp);
        Assert.Contains(world.Get(o.From)!.Abbreviation, room.OpeningLine);
        Assert.True(room.DemandPurse(excl, false).Amount >= match.Amount);
        var shortOffer = room.TheirTermsAsOffer(excl, false);
        shortOffer.PursePerFight = new Money(match.Amount - 500m);
        Assert.NotEqual(TalkVerdict.Accept, room.Table(shortOffer).Verdict);

        var again = NegotiationSession.Resume(world, f, promo, exp);
        var full = again.TheirTermsAsOffer(excl, false);
        Assert.Equal(TalkVerdict.Accept, again.Table(full).Verdict);
        ContractLifecycle.Sign(world, f, full, world.CurrentDate);

        Assert.Equal(RivalOfferState.Beaten, o.State);
        Assert.Null(OfferMarket.LiveFor(world, f.Id));
        Assert.DoesNotContain(world.Decisions, d => d.Kind == DecisionKind.RivalOffer);
    }

    // --- Let him go --------------------------------------------------------------------------

    [Fact]
    public void LetGo_HeFightsOutHisDeal_TheWindowIsStillOurs_ThenHeSignsTheRivalsPaper()
    {
        var (world, f, o) = WithOffer(48);
        var desk = world.Decisions.Single(d => d.Kind == DecisionKind.RivalOffer && d.Fighter == f.Id);
        Decisions.Decline(world, desk, world.CurrentDate);
        Assert.Equal(RivalOfferState.LetGo, o.State);
        Assert.DoesNotContain(world.Decisions, d => d.Kind == DecisionKind.RivalOffer);

        // His last fight is fought: the deal is up and his window opens — ours alone.
        f.Contract!.FightsRemaining = 0;
        var day = world.CurrentDate.AddDays(14);
        world.CurrentDate = day;
        DealWindow.Open(world, f, day);
        var end = f.Contract.WindowEndsOn!.Value;
        Assert.Contains(world.Mail, m => m.Subject.StartsWith($"{f.FullName}'s deal is up") && m.Body.Contains(world.Get(o.From)!.Name));

        for (var d = day.AddDays(7); d < end; d = d.AddDays(7))
        {
            world.CurrentDate = d;
            FreeAgencyService.RunMonthly(world, d);
            OfferMarket.RunWeekly(world, d);
            DealWindow.RunWeekly(world, d);
            Assert.Equal(world.PlayerPromotion, f.Contract!.Promotion);
            Assert.True(o.Live);
        }
        Assert.Equal(end, o.Deadline);   // the offer stands to the window's end

        // The window closes unsigned: he signs the rival's paper, not the open market.
        world.CurrentDate = end;
        OfferMarket.RunWeekly(world, end);
        DealWindow.RunWeekly(world, end);
        Assert.Equal(o.From, f.Contract!.Promotion);
        Assert.Equal(o.Purse, f.Contract.PursePerFight);
        Assert.Equal(o.Fights, f.Contract.FightsRemaining);
        Assert.Equal(RivalOfferState.Signed, o.State);
        Assert.Contains(world.Mail, m => m.Subject == $"We lost {f.FullName} to {world.Get(o.From)!.Abbreviation}");
    }

    [Fact]
    public void LetGo_IfTheRivalNoLongerHasTheMoney_HeWalksOntoTheOpenMarket()
    {
        var (world, f, o) = WithOffer(48);
        OfferMarket.LetGo(world, o, world.CurrentDate);
        f.Contract!.FightsRemaining = 0;
        DealWindow.Open(world, f, world.CurrentDate);
        var end = f.Contract.WindowEndsOn!.Value;

        world.Get(o.From)!.Balance = new Money(-1m);   // in the red: a bleeding org doesn't sign stars
        world.CurrentDate = end;
        OfferMarket.RunWeekly(world, end);
        DealWindow.RunWeekly(world, end);

        Assert.Equal(RivalOfferState.Withdrawn, o.State);
        Assert.Null(f.Contract);   // a free agent
        Assert.Contains(world.Mail, m => m.Subject == $"{world.Get(o.From)!.Abbreviation} pulled its offer for {f.FullName}");
    }

    [Fact]
    public void Silence_PastTheDeadline_IsLettingHimGo_AndTheLastWeekStopsTheClock()
    {
        var (world, f, o) = WithOffer(48);
        Assert.False(OfferMarket.TopBilled(world, f, o.Holder), "precondition: an ordinary member of the roster");

        Assert.Null(OfferMarket.RunWeekly(world, o.Made.AddDays(7)));          // two weeks left: the agenda, no stop
        Assert.Same(o, OfferMarket.RunWeekly(world, o.Made.AddDays(14)));       // the final week: time stops
        Assert.Contains(world.Mail, m => m.Subject.StartsWith("Last week to answer"));
        Assert.Null(OfferMarket.RunWeekly(world, o.Made.AddDays(21)));          // the deadline day itself: still pending
        Assert.Equal(RivalOfferState.Pending, o.State);

        OfferMarket.RunWeekly(world, o.Made.AddDays(28));
        Assert.Equal(RivalOfferState.LetGo, o.State);
        Assert.DoesNotContain(world.Decisions, d => d.Kind == DecisionKind.RivalOffer);
        Assert.Contains(world.Mail, m => m.Subject == $"No answer on {f.FullName}: he'll sign with {world.Get(o.From)!.Abbreviation}");
    }

    [Fact]
    public void AnOfferForAChampion_StopsTheClockTheWeekItArrives()
    {
        var world = FoundedWorld(48);
        var promo = world.Get(world.PlayerPromotion)!;
        var f = Star(world, loyalty: 20);
        var div = promo.Divisions.First();
        if (div.Title is null)
        {
            var t = new Title { Id = world.NewTitleId(), Promotion = promo.Id, WeightClass = div.WeightClass, Name = "Test Belt" };
            world.Titles[t.Id] = t;
            div.Title = t.Id;
        }
        f.WeightClass = div.WeightClass; f.Sex = div.Sex;
        world.Get(div.Title!.Value)!.CurrentChampion = f.Id;
        f.Contract!.FightsRemaining = 1;
        Assert.True(OfferMarket.TopBilled(world, f, promo.Id));

        RivalOffer? stop = null;
        for (int w = 1; w <= 26 && OfferMarket.LiveFor(world, f.Id) is null; w++)
        {
            world.CurrentDate = world.CurrentDate.AddDays(7);
            stop = OfferMarket.RunWeekly(world, world.CurrentDate);
        }
        var o = OfferMarket.LiveFor(world, f.Id);
        Assert.NotNull(o);
        Assert.Same(o, stop);
    }

    // --- A lifer says no ------------------------------------------------------------------------

    [Fact]
    public void AHoldingLifer_RefusesTheRivalInPublic_AndNoOfferIsLodged()
    {
        var world = FoundedWorld(48);
        var lifer = Star(world, loyalty: 92, Disposition.CompanyMan);
        Backdate(lifer, days: 365);
        lifer.Contract!.FightsRemaining = 1;
        lifer.Contract.PursePerFight = new FighterValuation().Evaluate(world, lifer).AskingPurse;   // paid his worth
        HireOne(world);
        BookOnNextCard(world, lifer);   // his last fight is booked (not benched: no grievance)
        Assert.True(Lifers.IsHolding(world, lifer), $"precondition: a holding lifer ({Lifers.GrievanceOf(world, lifer, world.CurrentDate)?.Reason})");
        Assert.NotNull(PoachingService.BestSuitor(world, world.Get(world.PlayerPromotion)!, lifer));

        Assert.Null(OfferMarket.Call(world, lifer, world.CurrentDate));

        Assert.Empty(world.RivalOffers);
        Assert.DoesNotContain(world.Decisions, d => d.Kind == DecisionKind.RivalOffer);
        Assert.Equal(Contract.RefusalTold, lifer.Contract!.BondNoticeOn);
        Assert.Contains(world.News, n => n.Headline.StartsWith($"{lifer.FullName} turns down"));
    }

    // --- Fog of war ------------------------------------------------------------------------------

    [Fact]
    public void TheRivalsPrice_ReadsWhatTheMarketHasSeen_NotTrueAbility()
    {
        var (world, f, o) = WithOffer(48);
        var suitor = world.Get(o.From)!;
        var bid = OfferMarket.Bid(world, suitor, f)!;
        var match = OfferMarket.MatchTerms(world, o);
        var leaning = OfferMarket.LeaningOf(world, o);

        // A far better fighter than anyone has seen: the price does not move.
        foreach (var a in RaiseAbility(f)) a(95);
        f.PotentialAbility = 99;
        var bid2 = OfferMarket.Bid(world, suitor, f)!;
        Assert.Equal(bid.PursePerFight, bid2.PursePerFight);
        Assert.Equal(bid.WinBonus, bid2.WinBonus);
        Assert.Equal(bid.SigningBonus, bid2.SigningBonus);
        Assert.Equal(bid.Fights, bid2.Fights);
        Assert.Equal(match.PursePerFight, OfferMarket.MatchTerms(world, o).PursePerFight);
        Assert.Equal(leaning, OfferMarket.LeaningOf(world, o));

        // A public fact the whole market can see — his fame — does.
        f.Intangibles.Popularity = 85;
        Assert.True(OfferMarket.Bid(world, suitor, f)!.PursePerFight.Amount > bid.PursePerFight.Amount);
    }

    // --- The AI world -----------------------------------------------------------------------------

    [Fact]
    public void AnAllAiWorld_NeverSeesAnOffer()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5 });
        world.PlayerPromotion = default;
        var tick = new WorldTick();
        for (int w = 0; w < 20; w++)
            Assert.Null(tick.AdvanceWeek(world).OfferNeedsYou);
        Assert.Empty(world.RivalOffers);
        Assert.DoesNotContain(world.Decisions, d => d.Kind == DecisionKind.RivalOffer);
    }

    // --- Helpers ----------------------------------------------------------------------------------

    private static (World World, Fighter F, RivalOffer O) WithOffer(ulong seed)
    {
        var world = FoundedWorld(seed);
        var f = Star(world, loyalty: 20);
        f.Contract!.FightsRemaining = 1;
        var o = OfferMarket.Call(world, f, world.CurrentDate);
        Assert.NotNull(o);
        return (world, f, o!);
    }

    /// <summary>
    /// A founded promotion (prestige 12, three divisions) run for <paramref name="years"/>
    /// years under the player's deal rules by a plain scripted owner, as tools/longsim's
    /// shoestring does: the script alone builds the roster (5 deep, from the free agents
    /// it can afford), re-signs in the window when his number fits, and lets every rival
    /// offer go. Returns the dates offers were made and the mean roster.
    /// </summary>
    private static (List<DateOnly> Made, double Roster) MeasureFounded(ulong seed, int years)
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options
        {
            Seed = seed,
            Founding = new WorldBuilder.FoundingSpec
            {
                Prestige = 12,
                StartingBalance = new Money(3_000_000m),
                Divisions = new[] { WeightClass.Lightweight, WeightClass.Welterweight, WeightClass.Middleweight },
            },
        });
        var org = world.Get(world.PlayerPromotion)!;
        world.PlayerPromotion = default;   // the world books its cards (the founded org stands in for the player)
        DealWindow.HarnessPlayerRules(world, org.Id);
        FreeAgencyService.HarnessHandsOff = org.Id;   // thread-static: the script alone builds the roster
        try
        {
            var tick = new WorldTick();
            var valuation = new FighterValuation();
            var made = new Dictionary<(FighterId, DateOnly), DateOnly>();
            double rosterSum = 0;
            int months = years * 13;
            for (int m = 0; m < months; m++)
            {
                decimal maxAsk = 3_000m + org.Prestige.Value * 300m;
                foreach (var o in world.RivalOffers.Where(o => o.State == RivalOfferState.Pending).ToList())
                    OfferMarket.LetGo(world, o, world.CurrentDate);
                foreach (var f in world.RosterOf(org.Id).Where(f => DealWindow.InWindow(world, f)).OrderBy(f => f.Id.Value).ToList())
                {
                    var s = NegotiationSession.Resume(world, f, org, valuation.Evaluate(world, f));
                    var offer = s.TheirTermsAsOffer(Negotiator.WantsExclusivity(org), clause: false);
                    if (offer.PursePerFight.Amount > Math.Max(maxAsk, f.Contract!.PursePerFight.Amount * 1.5m)) continue;
                    if (s.Table(offer).Verdict == TalkVerdict.Accept) ContractLifecycle.Sign(world, f, offer, world.CurrentDate);
                }
                for (int h = 0; h < 3; h++)
                {
                    var thin = org.Divisions.Where(d => world.RosterOf(org.Id).Count(f => d.Matches(f) && f.Status == FighterStatus.Active) < 5).ToList();
                    if (thin.Count == 0) break;
                    var pick = world.FreeAgents()
                        .Where(f => f.Status == FighterStatus.Active && thin.Any(d => d.Matches(f)))
                        .Select(f => (Fighter: f, Exp: valuation.Evaluate(world, f)))
                        .Where(x => x.Exp.AskingPurse.Amount <= maxAsk)
                        .OrderByDescending(x => FighterAbility.CurrentAbility(x.Fighter)).ThenBy(x => x.Fighter.Id.Value)
                        .FirstOrDefault();
                    if (pick.Fighter is null) break;
                    ContractLifecycle.Sign(world, pick.Fighter, new ContractOffer
                    {
                        Fighter = pick.Fighter.Id, Promotion = org.Id,
                        PursePerFight = pick.Exp.AskingPurse, WinBonus = pick.Exp.AskingWinBonus, Fights = pick.Exp.DesiredFights,
                    }, world.CurrentDate);
                }
                for (int w = 0; w < 4; w++)
                {
                    tick.AdvanceWeek(world);
                    foreach (var o in world.RivalOffers.Where(o => o.Holder == org.Id)) made.TryAdd((o.Fighter, o.Made), o.Made);
                }
                rosterSum += world.RosterOf(org.Id).Count(f => f.Status == FighterStatus.Active);
            }
            return (made.Values.OrderBy(d => d).ToList(), rosterSum / months);
        }
        finally { FreeAgencyService.HarnessHandsOff = null; }
    }

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

    // A proven, in-form, popular fighter on the player's roster.
    private static Fighter Star(World world, int loyalty, Disposition d = Disposition.Warhorse)
    {
        var f = HireOne(world);
        foreach (var a in RaiseAbility(f)) a(72);
        f.Record.Wins = 14; f.Record.Losses = 2; f.Record.WinsByKo = 7; f.Record.WinsByDecision = 7;
        f.Record.CurrentStreak = 4;
        f.Intangibles.Popularity = 55;
        f.Disposition = d;
        f.Loyalty = loyalty;
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

    private static void BookOnNextCard(World world, Fighter f)
    {
        var ev = new Event
        {
            Id = world.NewEventId(), Promotion = world.PlayerPromotion, Name = "Next",
            Date = world.CurrentDate.AddDays(21), Status = EventStatus.Scheduled,
        };
        var opponent = world.RosterOf(world.PlayerPromotion).First(x => x.Id != f.Id);
        ev.Card.Add(new Fight { Id = world.NewFightId(), RedCorner = f.Id, BlueCorner = opponent.Id, WeightClass = f.WeightClass });
        world.Events[ev.Id] = ev;
    }

    private static void Backdate(Fighter f, int days)
    {
        var c = f.Contract!;
        f.Contract = new Contract
        {
            Promotion = c.Promotion, SignedOn = c.SignedOn.AddDays(-days), PursePerFight = c.PursePerFight,
            WinBonus = c.WinBonus, FightsRemaining = c.FightsRemaining, IsExclusive = c.IsExclusive,
        };
        f.ReadyToFightOn = null;
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
