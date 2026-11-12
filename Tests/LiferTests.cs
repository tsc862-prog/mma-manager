using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Org;
using MmaManager.Persistence;

namespace MmaManager.Tests;

/// <summary>
/// Lifers: a fighter bound to his promotion holds — renewed first, cheaper to
/// keep, deaf to rivals — and breaks only for a reason the player can read.
/// </summary>
public class LiferTests
{
    // --- Who refuses, and why ------------------------------------------------------

    [Fact]
    public void ALifer_RefusesThePoach_ThatTakesTheSameFighterAtLoyalty30()
    {
        var mercWorld = FoundedWorld(48);
        var merc = Star(mercWorld, Disposition.CompanyMan, loyalty: 30);
        var lifeWorld = FoundedWorld(48);
        var lifer = Star(lifeWorld, Disposition.CompanyMan, loyalty: 85);

        Assert.False(Lifers.IsLifer(mercWorld, merc));
        Assert.True(Lifers.IsHolding(lifeWorld, lifer));

        Assert.True(PoachingService.TryPoachFromPlayer(mercWorld, merc, mercWorld.CurrentDate),
            "the same fighter at loyalty 30 takes the bigger stage");
        Assert.False(PoachingService.TryPoachFromPlayer(lifeWorld, lifer, lifeWorld.CurrentDate),
            "a holding lifer says no");
        Assert.Equal(lifeWorld.PlayerPromotion, lifer.Contract!.Promotion);
    }

    [Fact]
    public void ALifersRefusal_IsTheBond_NotThePrice()
    {
        // Same loyalty, so the same price to pry loose — the only difference is
        // that one of them is a company man and the face of the company (and so
        // over the lifer bar). Both are the face: the posters are the same.
        (World, Fighter) Setup(Disposition d)
        {
            var w = FoundedWorld(48);
            w.Get(w.PlayerPromotion)!.Prestige = 10;   // a city hall: the giants' leap sells itself
            // ...and the giants have built their gates, so their purse budgets can reach a loyal price.
            foreach (var big in w.Promotions.Values.Where(x => x.Id != w.PlayerPromotion && x.HomeCity is not null))
                big.PopularityByCity[big.HomeCity!.Value] = 100;
            // A cheap-to-price celebrity draw (modest in the cage, a big name, a
            // short record): the giants can afford even a loyal fighter's price.
            // The ask is built from the public sheet, so the sheet is short too,
            // and the agent is the calmest on the market (the price must sit
            // inside the giants' loyalty reach whoever the advisor would pick).
            var f = HireCalmest(w);
            foreach (var a in RaiseAbility(f)) a(42);
            f.BoutHistory.Clear();
            f.Record.Wins = 2; f.Record.Losses = 1; f.Record.Draws = 0; f.Record.NoContests = 0;
            f.Record.WinsByKo = 1; f.Record.WinsBySubmission = 0; f.Record.WinsByDecision = 1;
            f.Momentum = 0;                       // a name, not a hot streak
            f.Intangibles.Popularity = 76;   // just past the giants' draw floor
            f.Record.CurrentStreak = 1;
            f.Disposition = d;
            f.Loyalty = 65;
            f.Contract!.FightsRemaining = 0;
            w.Get(w.PlayerPromotion)!.FaceOfCompany = f.Id;
            return (w, f);
        }
        var (hireWorld, hired) = Setup(Disposition.Warhorse);
        var (lifeWorld, lifer) = Setup(Disposition.CompanyMan);

        Assert.False(Lifers.IsLifer(hireWorld, hired));
        Assert.True(Lifers.IsHolding(lifeWorld, lifer));
        Assert.True(PoachingService.TryPoachFromPlayer(hireWorld, hired, hireWorld.CurrentDate),
            "at loyalty 65 the money reaches him");
        Assert.False(PoachingService.TryPoachFromPlayer(lifeWorld, lifer, lifeWorld.CurrentDate),
            "the same money doesn't move a lifer");
    }

    [Fact]
    public void TheRefusal_IsReported_OncePerContract()
    {
        var world = FoundedWorld(48);
        var lifer = Star(world, Disposition.CompanyMan, loyalty: 90);
        lifer.Designed = true;   // notable: the refusal makes the news
        Backdate(lifer, days: 365);   // a deal that has run a while (a fresh one is too soon after the last story)
        HireOne(world);
        BookOnNextCard(world, lifer);  // ...and he's been kept busy and paid his worth: nothing to complain about
        lifer.Contract!.PursePerFight = new FighterValuation().Evaluate(world, lifer).AskingPurse;
        Assert.True(Lifers.IsHolding(world, lifer), Lifers.BondOf(world, lifer).Grievance?.Reason);

        Assert.False(PoachingService.TryPoachFromPlayer(world, lifer, world.CurrentDate));
        Assert.False(PoachingService.TryPoachFromPlayer(world, lifer, world.CurrentDate.AddMonths(1)));
        Assert.False(PoachingService.TryPoachFromPlayer(world, lifer, world.CurrentDate.AddMonths(2)));

        Assert.Single(world.News, n => n.Headline.StartsWith($"{lifer.FullName} turns down"));
        Assert.Single(world.Mail, m => m.Subject.StartsWith($"{lifer.FullName} turned down"));

        // A new deal is a new contract: the next approach is news again.
        // A new deal is a new contract — but a story only once the deal has run
        // half a year, so a string of short deals can't spam the feed.
        Renew(world, lifer);
        lifer.Contract!.FightsRemaining = 0;
        Assert.False(PoachingService.TryPoachFromPlayer(world, lifer, world.CurrentDate.AddMonths(3)));
        Assert.Single(world.News, n => n.Headline.StartsWith($"{lifer.FullName} turns down"));
        Renew(world, lifer);
        lifer.Contract!.FightsRemaining = 0;
        Assert.False(PoachingService.TryPoachFromPlayer(world, lifer, world.CurrentDate.AddMonths(7)));
        Assert.Equal(2, world.News.Count(n => n.Headline.StartsWith($"{lifer.FullName} turns down")));
        Assert.Contains(world.Mail, m => m.From == lifer.FullName && m.Subject.StartsWith($"{lifer.FullName} turned down"));
    }

    // --- His promotion fights to keep him ----------------------------------------------

    [Fact]
    public void ASolventOrg_RenewsTheLifer_ItUsedToLetWalk()
    {
        // The same IFA fighter, priced past IFA's purse budget: as an ordinary
        // fighter his capped renewal falls short and he walks; as a lifer the
        // org stretches, he takes less to stay, and he's renewed.
        static (World W, Fighter F, Promotion Org) Setup(Disposition d, int loyalty)
        {
            var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 11 });
            w.PlayerPromotion = default;
            var org = w.Promotions.Values.First(p => p.Abbreviation == "IFA");
            var f = w.RosterOf(org.Id)
                .Where(x => x.Status == FighterStatus.Active && !w.Titles.Values.Any(t => t.CurrentChampion == x.Id))
                .OrderBy(x => x.Id.Value).First();
            f.Disposition = d;
            f.Loyalty = loyalty;
            f.Record.Wins = 20; f.Record.Losses = 2; f.Record.WinsByKo = 10; f.Record.WinsByDecision = 10;
            f.Record.CurrentStreak = 2;
            f.Intangibles.Popularity = 66;
            f.Contract!.FightsRemaining = 0;
            return (w, f, org);
        }

        var (ow, ordinary, oOrg) = Setup(Disposition.Warhorse, 50);
        var exp = new FighterValuation().Evaluate(ow, ordinary);
        Assert.True(Negotiator.EffectiveAskingPurse(oOrg, ordinary, exp).Amount > Negotiator.PurseCap(ow, oOrg),
            "precondition: his ask is past IFA's usual budget");
        ContractLifecycle.ProcessExpiries(ow, ow.CurrentDate);
        Assert.NotEqual(oOrg.Id, ordinary.Contract?.Promotion);

        var (lw, lifer, lOrg) = Setup(Disposition.CompanyMan, 90);
        Assert.True(Lifers.IsHolding(lw, lifer));
        ContractLifecycle.ProcessExpiries(lw, lw.CurrentDate);
        Assert.Equal(lOrg.Id, lifer.Contract?.Promotion);
        Assert.True(lifer.Contract!.FightsRemaining > 0, "renewed in place");
    }

    [Fact]
    public void ALifer_AsksLessOfHisOwnPromotion_AndOnlyHisOwn()
    {
        var world = FoundedWorld(48);
        var lifer = Star(world, Disposition.CompanyMan, loyalty: 90);
        var mine = world.Get(world.PlayerPromotion)!;
        var other = world.Promotions.Values.First(p => p.Id != mine.Id && !p.Defunct);

        Assert.True(Lifers.HomeDiscount(world, lifer, mine.Id) < 1m);
        Assert.Equal(1m, Lifers.HomeDiscount(world, lifer, other.Id));

        var exp = new FighterValuation().Evaluate(world, lifer);
        Assert.True(Negotiator.EffectiveAskingPurse(world, mine, lifer, exp).Amount
                    < Negotiator.EffectiveAskingPurse(mine, lifer, exp).Amount);
        var talks = new NegotiationSession(world, lifer, mine, exp);
        Assert.True(talks.DemandPurse(true, false).Amount < exp.AskingPurse.Amount, "the room quotes the hometown number");
    }

    // --- It breaks for a reason ------------------------------------------------------------

    [Fact]
    public void Break_PromotionInTheRed()
    {
        var (world, lifer) = HoldingLifer();
        world.Get(world.PlayerPromotion)!.Balance = new Money(-50_000m);
        AssertBroken(world, lifer, Lifers.GrievanceKind.OrgInTrouble, "in the red");
    }

    [Fact]
    public void TheBond_IsReadable_WhyNotProtected_AndWhatRepairsIt()
    {
        var (world, lifer) = HoldingLifer();
        var holding = Lifers.BondOf(world, lifer);
        Assert.True(holding.IsLifer && holding.IsHolding && holding.KeepsFirstCall);
        Assert.Null(holding.Grievance);
        Assert.Null(holding.Repair);

        lifer.Record.CurrentStreak = -4;
        var hurt = Lifers.BondOf(world, lifer);
        Assert.True(hurt.IsLifer);
        Assert.False(hurt.IsHolding);
        Assert.False(hurt.KeepsFirstCall);
        Assert.Equal(Lifers.GrievanceKind.Skid, hurt.Grievance!.Kind);
        Assert.Equal("get him a win", hurt.Repair);

        lifer.Sex = Sex.Female;
        Assert.Contains("have her wondering whether she needs", Lifers.BondOf(world, lifer).Grievance!.Reason);
    }

    [Fact]
    public void Break_PassedOverForTheTitleShot()
    {
        var (world, lifer) = HoldingLifer();
        var promo = world.Get(world.PlayerPromotion)!;
        var div = promo.Divisions.First(d => d.Matches(lifer));
        var title = world.Get(div.Title!.Value)!;
        var rivals = world.RosterOf(promo.Id).Where(x => x.Id != lifer.Id && div.Matches(x)).Take(2).ToList();
        while (rivals.Count < 2)
        {
            var extra = HireOne(world);
            if (div.Matches(extra)) rivals.Add(extra);
        }
        title.CurrentChampion = rivals[0].Id;
        div.Rankings.Remove(lifer.Id);
        div.Rankings.Insert(0, lifer.Id);   // the #1 contender
        for (int i = 1; i <= 2; i++)
        {
            var ev = new Event
            {
                Id = world.NewEventId(), Promotion = promo.Id, Name = $"Test {i}",
                Date = world.CurrentDate.AddDays(-60 * i), Status = EventStatus.Completed,
            };
            ev.Card.Add(new Fight
            {
                Id = world.NewFightId(), RedCorner = rivals[0].Id, BlueCorner = rivals[1].Id,
                WeightClass = div.WeightClass, IsTitleFight = true, Title = title.Id,
            });
            world.Events[ev.Id] = ev;
        }
        AssertBroken(world, lifer, Lifers.GrievanceKind.PassedOver, "passed over twice");

        // Repair: book him the shot.
        var shot = new Event
        {
            Id = world.NewEventId(), Promotion = promo.Id, Name = "The shot",
            Date = world.CurrentDate.AddDays(30), Status = EventStatus.Scheduled,
        };
        shot.Card.Add(new Fight
        {
            Id = world.NewFightId(), RedCorner = rivals[0].Id, BlueCorner = lifer.Id,
            WeightClass = div.WeightClass, IsTitleFight = true, Title = title.Id,
        });
        world.Events[shot.Id] = shot;
        Assert.True(Lifers.IsHolding(world, lifer), "a booked title shot fixes it");
    }

    [Fact]
    public void Break_BenchedForHalfAYear()
    {
        var (world, lifer) = HoldingLifer();
        Backdate(lifer, days: 200);
        AssertBroken(world, lifer, Lifers.GrievanceKind.Benched, "on the shelf");
    }

    [Fact]
    public void Break_PaidFarBelowWhatTheMarketHasSeen()
    {
        var (world, lifer) = HoldingLifer();
        Backdate(lifer, days: 300);
        lifer.Contract!.PursePerFight = new Money(500m);
        // Booked on a card, so he isn't also "on the shelf" — this is about the money.
        BookOnNextCard(world, lifer);
        AssertBroken(world, lifer, Lifers.GrievanceKind.Underpaid, "barely a third");
    }

    [Fact]
    public void Break_ALosingSkid()
    {
        var (world, lifer) = HoldingLifer();
        lifer.Record.CurrentStreak = -3;
        AssertBroken(world, lifer, Lifers.GrievanceKind.Skid, "3 straight losses");
    }

    [Fact]
    public void Break_ThePostersComeDown()
    {
        var (world, lifer) = HoldingLifer();
        var promo = world.Get(world.PlayerPromotion)!;
        Franchise.SetFace(world, promo, lifer, world.CurrentDate);
        var newFace = world.RosterOf(promo.Id).First(x => x.Id != lifer.Id);

        Franchise.SetFace(world, promo, newFace, world.CurrentDate);

        Assert.False(Lifers.IsLifer(world, lifer), "taking the posters off him breaks the bond");
        Assert.Contains(world.Mail, m => m.Subject == $"We've lost {lifer.FullName}'s loyalty" && m.Body.Contains("posters"));
    }

    [Fact]
    public void Break_TheCampStipendStops_AndARetentionBonusRepairsIt()
    {
        var (world, lifer) = HoldingLifer();
        var promo = world.Get(world.PlayerPromotion)!;
        lifer.CampStipend = true;

        Camps.StopStipend(world, lifer);

        Assert.False(Lifers.IsLifer(world, lifer));
        Assert.Contains(world.Mail, m => m.Subject == $"We've lost {lifer.FullName}'s loyalty" && m.Body.Contains("corner"));

        promo.Balance = new Money(50_000_000m);
        Assert.True(RetentionService.TryPayBonus(world, promo, lifer, out _));
        Assert.True(Lifers.IsHolding(world, lifer), "a retention bonus buys the bond back");
    }

    [Fact]
    public void AStandingGrievance_WearsTheBondDown_AndARepairInTimeRestoresIt()
    {
        var (world, lifer) = HoldingLifer();
        var promo = world.Get(world.PlayerPromotion)!;
        lifer.Loyalty = 85;   // company man: score 95
        promo.Balance = new Money(-10_000m);

        int before = lifer.Loyalty;
        Lifers.RunMonthly(world, world.CurrentDate);
        Assert.True(lifer.Loyalty < before, "a standing grievance costs loyalty every month");
        Assert.False(Lifers.IsHolding(world, lifer));
        Assert.Contains(world.Mail, m => m.Subject == $"{lifer.FullName} is unhappy" && m.Body.Contains("in the red"));

        promo.Balance = new Money(400_000m);   // back in the black
        Assert.True(Lifers.IsHolding(world, lifer), "fixed in time, he holds again");
        int after = lifer.Loyalty;
        Lifers.RunMonthly(world, world.CurrentDate.AddMonths(1));
        Assert.Equal(after, lifer.Loyalty);
    }

    [Fact]
    public void ALongGrievance_BreaksTheBond_AndThePlayerHearsWhy()
    {
        var (world, lifer) = HoldingLifer();
        var promo = world.Get(world.PlayerPromotion)!;
        lifer.Designed = true;
        promo.Balance = new Money(-10_000m);

        for (int m = 0; m < 40 && Lifers.IsLifer(world, lifer); m++)
            Lifers.RunMonthly(world, world.CurrentDate.AddMonths(m));

        Assert.False(Lifers.IsLifer(world, lifer));
        Assert.Contains(world.News, n => n.Headline == $"{lifer.FullName}'s bond with {promo.Abbreviation} is broken"
                                         && n.Body.Contains("in the red"));
        Assert.Contains(world.Mail, m => m.Subject == $"We've lost {lifer.FullName}'s loyalty");
    }

    [Fact]
    public void WhenALiferLeaves_TheHeadlineSaysWhy()
    {
        var (world, lifer) = HoldingLifer();
        var promo = world.Get(world.PlayerPromotion)!;
        lifer.Record.CurrentStreak = -3;   // the reason

        ContractLifecycle.Release(world, lifer);

        // The promotion's decision is the headline; his grievance is the background.
        Assert.Contains(world.News, n => n.Headline.StartsWith($"{lifer.FullName} leaves {promo.Name}")
                                         && n.Headline.Contains("released him") && n.Body.Contains("straight losses"));
        Assert.Contains(world.Mail, m => m.Subject == $"{lifer.FullName} has left us");
    }

    [Fact]
    public void ABenchedLifer_IsBittenOnce_NotTwice()
    {
        var (world, lifer) = HoldingLifer();
        Backdate(lifer, days: 200);   // past both the neglect clock (120) and the Benched grievance (180)
        Assert.Equal(Lifers.GrievanceKind.Benched, Lifers.GrievanceOf(world, lifer, world.CurrentDate)!.Kind);

        int before = lifer.Loyalty;
        Lifers.RunMonthly(world, world.CurrentDate);
        int afterGrievance = lifer.Loyalty;
        WorldTick.DecayNeglectedLoyalty(world, world.CurrentDate);

        Assert.True(afterGrievance < before, "the grievance bites");
        Assert.Equal(afterGrievance, lifer.Loyalty);   // the neglect decay stands aside for him
    }

    [Fact]
    public void DayOne_AuthoredLifers_AreNeitherBenchedNorTornUp()
    {
        // Day-one contracts are signed on the start date (not back-dated), so
        // neither the shelf clock nor the pay clock has started for anyone.
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 3 });
        world.PlayerPromotion = default;
        var lifers = world.Fighters.Values.Where(f => f.Designed && Lifers.IsLifer(world, f)).ToList();
        Assert.NotEmpty(lifers);
        var signed = lifers.ToDictionary(f => f.Id, f => f.Contract!.SignedOn);

        var month1 = world.CurrentDate.AddDays(31);
        foreach (var f in lifers)
            Assert.True(Lifers.GrievanceOf(world, f, month1) is not { Kind: Lifers.GrievanceKind.Benched or Lifers.GrievanceKind.Underpaid },
                $"{f.FullName}: {Lifers.GrievanceOf(world, f, month1)?.Reason}");
        Lifers.RunMonthly(world, month1);
        Assert.All(lifers, f => Assert.Equal(signed[f.Id], f.Contract!.SignedOn));
    }

    // --- Told once: the deal remembers ---------------------------------------------

    [Fact]
    public void AScoreBobbingAcrossTheBar_TellsTheBreakOnce()
    {
        var (world, lifer) = HoldingLifer();
        var promo = world.Get(world.PlayerPromotion)!;
        lifer.Designed = true;
        lifer.Temperament.Ambition = 50;     // a bite of 2 a month
        lifer.Loyalty = 71;                  // company man: score 81
        promo.Balance = new Money(-10_000m); // the grievance that stands throughout

        for (int m = 0; m < 6; m++)
        {
            Lifers.RunMonthly(world, world.CurrentDate.AddMonths(m));   // bites him under the bar (79)
            foreach (var mail in world.Mail) mail.Read = true;            // no help from the inbox's unread de-dupe
            lifer.Loyalty += 2;                                           // a fight and a stipend lift him back to 81
        }

        Assert.Single(world.Mail, x => x.Subject == $"We've lost {lifer.FullName}'s loyalty");
        Assert.Single(world.News, n => n.Headline == $"{lifer.FullName}'s bond with {promo.Abbreviation} is broken");
    }

    [Fact]
    public void BreakRepairBreak_IsToldTwice()
    {
        var (world, lifer) = HoldingLifer();
        var promo = world.Get(world.PlayerPromotion)!;
        lifer.Temperament.Ambition = 50;
        var day = world.CurrentDate;

        void BreakHim(DateOnly on)
        {
            lifer.Loyalty = 71;
            promo.Balance = new Money(-10_000m);
            Lifers.RunMonthly(world, on);
            foreach (var mail in world.Mail) mail.Read = true;
        }

        BreakHim(day);
        Assert.NotNull(lifer.Contract!.BondNoticeOn);

        promo.Balance = new Money(2_000_000m);   // back in the black...
        lifer.Loyalty = 85;                      // ...and a retention bonus later, he holds again
        Lifers.RunMonthly(world, day.AddMonths(1));
        Assert.Null(lifer.Contract!.BondNoticeOn);

        BreakHim(day.AddMonths(2));
        Assert.Equal(2, world.Mail.Count(x => x.Subject == $"We've lost {lifer.FullName}'s loyalty"));
    }

    [Fact]
    public void ARefusedLifer_WhoBecomesPoachable_GetsTheCirclingWarningAMonthFirst()
    {
        var world = FoundedWorld(48);
        var lifer = Star(world, Disposition.CompanyMan, loyalty: 90);
        Backdate(lifer, days: 365);
        HireOne(world);
        BookOnNextCard(world, lifer);
        lifer.Contract!.PursePerFight = new FighterValuation().Evaluate(world, lifer).AskingPurse;
        var day = world.CurrentDate;

        // Month 0: the rival calls, he says no.
        PoachingService.WarnExpiring(world, day);
        Assert.False(PoachingService.TryPoachFromPlayer(world, lifer, day));
        Assert.Equal(Contract.RefusalTold, lifer.Contract!.BondNoticeOn);
        Assert.False(lifer.Contract.PoachWarned, "a refusal is not the circling warning");

        // He sours completely — now any rival's money moves him.
        lifer.Loyalty = 0;
        Assert.False(Lifers.IsLifer(world, lifer));

        // Month 1: the owner is warned, and nobody takes him this month.
        PoachingService.WarnExpiring(world, day.AddMonths(1));
        Assert.False(PoachingService.TryPoachFromPlayer(world, lifer, day.AddMonths(1)));
        Assert.Contains(world.Mail, m => m.Subject.EndsWith($"is circling {lifer.FullName}"));
        Assert.Equal(world.PlayerPromotion, lifer.Contract!.Promotion);

        // Month 2: unsigned and warned — the treadmill takes him.
        PoachingService.WarnExpiring(world, day.AddMonths(2));
        Assert.True(PoachingService.TryPoachFromPlayer(world, lifer, day.AddMonths(2)));
    }

    [Fact]
    public void TheBondNotice_SurvivesASave()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 7 });
        var signed = world.Fighters.Values.Where(f => f.Contract is not null).OrderBy(f => f.Id.Value).Take(2).ToList();
        signed[0].Contract!.BondNoticeOn = Contract.RefusalTold;
        signed[1].Contract!.BondNoticeOn = world.CurrentDate;

        string path = Path.Combine(Path.GetTempPath(), $"mma_bond_{Guid.NewGuid():N}.db");
        try
        {
            SaveGame.Save(world, path);
            var loaded = SaveGame.Load(path);
            Assert.Equal(Contract.RefusalTold, loaded.Get(signed[0].Id)!.Contract!.BondNoticeOn);
            Assert.Equal(world.CurrentDate, loaded.Get(signed[1].Id)!.Contract!.BondNoticeOn);
        }
        finally { File.Delete(path); }
    }

    // --- Mercenaries stay mercenaries --------------------------------------------------

    [Fact]
    public void AMercenary_NeverCountsAsALifer_HoweverLoyal()
    {
        var world = FoundedWorld(48);
        var merc = Star(world, Disposition.Mercenary, loyalty: 100);
        Assert.False(Lifers.IsLifer(world, merc));
        Assert.Equal(1m, Lifers.HomeDiscount(world, merc, world.PlayerPromotion));
    }

    [Fact]
    public void AWorldWithoutLifers_RunsExactlyAsIfTheLayerDidNotExist()
    {
        // Every fighter a mercenary: no lifers can exist, so the whole layer must
        // be inert. Same seed, same run, layer on and layer off — any difference
        // means it leaks into the ordinary treadmill. (No stored hash: the proof
        // stays valid whatever else changes on main.)
        static string Run(bool layerOff)
        {
            var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 21 });
            world.PlayerPromotion = default;
            foreach (var f in world.Fighters.Values) f.Disposition = Disposition.Mercenary;
            Lifers.Disabled = layerOff;
            try { new WorldTick().AdvanceWeeks(world, 26); }
            finally { Lifers.Disabled = false; }
            return Fingerprint(world) + "|" + string.Join(" / ", world.News.Select(n => n.Headline));
        }

        Assert.Equal(Run(layerOff: true), Run(layerOff: false));
    }

    [Fact]
    public void TheSwitch_ReallyTurnsTheLayerOff()
    {
        // Guards the test above: with the layer off, a would-be lifer is not one.
        var (world, lifer) = HoldingLifer();
        Lifers.Disabled = true;
        try { Assert.False(Lifers.IsLifer(world, lifer)); }
        finally { Lifers.Disabled = false; }
        Assert.True(Lifers.IsHolding(world, lifer));
    }

    // --- Determinism ---------------------------------------------------------------------

    [Fact]
    public void SameSeed_SameRefusalsAndBreaks()
    {
        static World Run()
        {
            var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 7 });
            w.PlayerPromotion = default;
            new WorldTick().AdvanceWeeks(w, 60);
            return w;
        }
        var a = Run();
        var b = Run();
        Assert.Equal(LiferStories(a), LiferStories(b));
        Assert.Equal(Fingerprint(a), Fingerprint(b));
    }

    [Fact]
    public void ASaveAndLoad_InTheMiddle_ChangesNoRefusalOrBreak()
    {
        var straight = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 7 });
        straight.PlayerPromotion = default;
        var tick = new WorldTick();
        tick.AdvanceWeeks(straight, 30);

        string path = Path.Combine(Path.GetTempPath(), $"mma_lifers_{Guid.NewGuid():N}.db");
        World reloaded;
        try
        {
            SaveGame.Save(straight, path);
            reloaded = SaveGame.Load(path);
        }
        finally { File.Delete(path); }

        tick.AdvanceWeeks(straight, 30);
        new WorldTick().AdvanceWeeks(reloaded, 30);

        Assert.Equal(LiferStories(straight), LiferStories(reloaded));
        Assert.Equal(Fingerprint(straight), Fingerprint(reloaded));
    }

    // --- helpers -------------------------------------------------------------------------

    private static List<string> LiferStories(World w) => w.News
        .Where(n => n.Headline.Contains(" turns down ") || n.Headline.Contains("'s bond with ")
                    || (n.Category == NewsCategory.Milestone && n.Headline.Contains(" leaves ")))
        .Select(n => $"{n.Date:yyyy-MM-dd} {n.Headline}")
        .ToList();

    // Who is where, on what terms, how loyal — plus every org's bank.
    private static string Fingerprint(World w)
    {
        unchecked
        {
            long h = 17;
            void Mix(long v) => h = h * 31 + v;
            foreach (var f in w.Fighters.Values.OrderBy(f => f.Id.Value))
            {
                Mix(f.Id.Value); Mix(f.Loyalty); Mix((long)f.Status);
                if (f.Contract is { } c)
                {
                    Mix(c.Promotion.Value); Mix(c.FightsRemaining); Mix((long)c.PursePerFight.Amount); Mix(c.SignedOn.DayNumber);
                }
            }
            foreach (var p in w.Promotions.Values.OrderBy(p => p.Id.Value)) { Mix((long)p.Balance.Amount); Mix(p.Defunct ? 1 : 0); }
            return h.ToString("X");
        }
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

    // A company man at loyalty 85 under contract with fights left — holding, nothing wrong.
    private static (World, Fighter) HoldingLifer()
    {
        var world = FoundedWorld(48);
        var f = Star(world, Disposition.CompanyMan, loyalty: 85);
        f.Contract!.FightsRemaining = 3;
        HireOne(world);   // a teammate: an opponent on a card, a new face for the posters
        Assert.True(Lifers.IsHolding(world, f), "precondition: a holding lifer");
        return (world, f);
    }

    private static void AssertBroken(World world, Fighter f, Lifers.GrievanceKind kind, string reasonText)
    {
        Assert.False(Lifers.IsHolding(world, f), "the grievance opens the door");
        var g = Lifers.GrievanceOf(world, f, world.CurrentDate);
        Assert.NotNull(g);
        Assert.Equal(kind, g!.Kind);
        Assert.Contains(reasonText, g.Reason);
        // Only a pay grievance leaves the hometown terms in place — the renewal IS the raise.
        if (kind == Lifers.GrievanceKind.Underpaid) Assert.True(Lifers.HomeDiscount(world, f, world.PlayerPromotion) < 1m);
        else Assert.Equal(1m, Lifers.HomeDiscount(world, f, world.PlayerPromotion));
    }

    // The deal was signed long ago: replace it with an identical one dated back.
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

    private static void Renew(World world, Fighter f)
    {
        var exp = new FighterValuation().Evaluate(world, f);
        ContractLifecycle.Sign(world, f, new ContractOffer
        {
            Fighter = f.Id, Promotion = world.PlayerPromotion,
            PursePerFight = exp.AskingPurse, WinBonus = exp.AskingWinBonus, Fights = exp.DesiredFights,
        }, world.CurrentDate);
    }

    // The free agent in the player's divisions with the easiest-going agent (ties on id), signed at his ask.
    private static Fighter HireCalmest(World world)
    {
        var promo = world.Get(world.PlayerPromotion)!;
        var f = world.FreeAgents()
            .Where(x => x.Status == FighterStatus.Active && promo.Divisions.Any(d => d.Matches(x)))
            .OrderBy(FighterValuation.AgentToughness).ThenBy(x => x.Id.Value).First();
        var exp = new FighterValuation().Evaluate(world, f);
        ContractLifecycle.Sign(world, f, new ContractOffer
        {
            Fighter = f.Id, Promotion = promo.Id,
            PursePerFight = exp.AskingPurse, WinBonus = exp.AskingWinBonus, Fights = exp.DesiredFights,
        }, world.CurrentDate);
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
