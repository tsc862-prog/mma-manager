using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Org;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Tests;

/// <summary>
/// The big-money era: market-share dilution, org acquisitions, bidding wars,
/// overturned results, camp withdrawals with short-notice saves, and press
/// tours that move the box office.
/// </summary>
public class BigMoneyTests
{
    private static World Build(ulong seed = 5) =>
        new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });

    // --- Market share --------------------------------------------------------

    [Fact]
    public void ACrowdedBand_ThinsEveryonesSlice_ButNeverPastTheFloor()
    {
        var world = Build();
        var promo = world.Promotions.Values.First(p => !p.Defunct);
        foreach (var p in world.Promotions.Values.Where(p => !p.Defunct))
            p.Prestige = 40;   // everyone operates National — a genuinely packed band

        double crowded = MarketShare.Factor(world, promo);
        Assert.True(crowded < 1.0, "a packed band should dilute");
        Assert.True(crowded >= 0.8, "the drag is a headwind, never a cliff");

        foreach (var p in world.Promotions.Values.Where(p => p.Id != promo.Id))
            p.Prestige = 25;   // the band empties out
        Assert.Equal(1.0, MarketShare.Factor(world, promo));
    }

    // --- Acquisitions --------------------------------------------------------

    [Fact]
    public void AnAcquisition_TransfersTheBest_RetiresTheBelts_AndClosesTheBooks()
    {
        var world = Build();
        var buyer = world.Promotions.Values.First(p => p.Abbreviation == "AFC");
        var target = world.Promotions.Values.First(p => !p.Defunct && p.Id != buyer.Id
            && p.Titles.Any(t => world.Get(t)?.CurrentChampion is not null)
            && OrgSizing.SizeOf(p) >= OrgSize.National);
        var champ = world.Get(world.Get(target.Titles
            .First(t => world.Get(t)?.CurrentChampion is not null))!.CurrentChampion!.Value)!;
        var scheduled = world.Events.Values.Count(e => e.Promotion == target.Id && e.Status == EventStatus.Scheduled);
        decimal buyerBankBefore = buyer.Balance.Amount;

        var price = Acquisitions.AskingPrice(world, target);
        Assert.True(price.Amount > 0);
        Acquisitions.Execute(world, buyer, target, world.CurrentDate, price);

        Assert.True(target.Defunct);
        Assert.Equal(world.CurrentDate, world.LastAcquisitionOn);
        Assert.True(buyer.Balance.Amount < buyerBankBefore, "the cheque was real");
        Assert.Equal(buyer.Id, champ.Contract?.Promotion);                        // the champion came over
        Assert.All(target.Titles, t => Assert.Null(world.Get(t)!.CurrentChampion));   // the belts retired
        Assert.DoesNotContain(world.Events.Values,
            e => e.Promotion == target.Id && e.Status == EventStatus.Scheduled);  // the calendar cleared
        Assert.Contains(world.News, n => n.Headline.Contains("acquires"));
    }

    [Fact]
    public void TheFlagship_IsNeverQuietlyForSale_HoweverBadTheYear()
    {
        var world = Build();
        var flagship = world.Promotions.Values.Where(p => !p.Defunct)
            .OrderByDescending(p => p.Prestige.Value).ThenBy(p => p.Id.Value).First();

        // Drown it: deep in the red and bleeding hard over the trailing quarter.
        flagship.Balance = new Money(-5_000_000m);
        Core.Sim.Finance.Ledger.Expense(flagship, world.CurrentDate.AddDays(-30),
            FinanceCategory.Other, new Money(9_000_000m), "a catastrophic quarter");

        Assert.False(Acquisitions.IsForSale(world, flagship),
            "the sport's #1 brand doesn't get bought out of a cash dip");

        // The same books at the SECOND-biggest brand are absolutely for sale.
        var rival = world.Promotions.Values.Where(p => !p.Defunct && p.Id != flagship.Id)
            .OrderByDescending(p => p.Prestige.Value).First();
        rival.Balance = new Money(-5_000_000m);
        Core.Sim.Finance.Ledger.Expense(rival, world.CurrentDate.AddDays(-30),
            FinanceCategory.Other, new Money(9_000_000m), "a catastrophic quarter");
        Assert.True(Acquisitions.IsForSale(world, rival));
    }

    [Fact]
    public void ABadQuarterIsNotADeathSentence_TheLedgerDecides()
    {
        var world = Build();
        var org = world.Promotions.Values.Where(p => !p.Defunct
                && OrgSizing.SizeOf(p) >= OrgSize.National)
            .OrderByDescending(p => p.Prestige.Value).Skip(1).First();

        // Thin on cash but RECOVERING — a lumpy giant mid-rebound, not a corpse.
        org.Balance = new Money(100_000m);
        Core.Sim.Finance.Ledger.Income(org, world.CurrentDate.AddDays(-20),
            FinanceCategory.EventGate, new Money(4_000_000m), "a huge night");
        Assert.False(Acquisitions.IsForSale(world, org),
            "a thin bank that's climbing is not a sale — the ledger says which way it's going");
    }

    [Fact]
    public void TheBroker_CallsThePlayerFirst_AndYesClosesTheDeal()
    {
        var world = Build();
        var player = world.Get(world.PlayerPromotion)!;
        var flagship = world.Promotions.Values.Where(p => !p.Defunct)
            .OrderByDescending(p => p.Prestige.Value).ThenBy(p => p.Id.Value).First();
        var target = world.Promotions.Values.First(p => !p.Defunct && p.Id != player.Id && p.Id != flagship.Id
            && OrgSizing.SizeOf(p) >= OrgSize.National && OrgSizing.SizeOf(p) <= OrgSizing.SizeOf(player));
        // Genuinely dying: in the red AND still bleeding across the trailing quarter.
        target.Balance = new Money(-500_000m);
        Core.Sim.Finance.Ledger.Expense(target, world.CurrentDate.AddDays(-30),
            FinanceCategory.Other, new Money(2_000_000m), "the quarter that killed them");
        player.Balance = new Money(500_000_000m);

        // Hunt for the month the broker picks up the phone.
        for (ulong s = 1; s < 300 && !world.Decisions.Any(d => d.Kind == DecisionKind.AcquisitionOffer); s++)
            Acquisitions.RunMonthly(world, world.CurrentDate, new SimRandom(s));
        var call = world.Decisions.FirstOrDefault(d => d.Kind == DecisionKind.AcquisitionOffer);
        Assert.NotNull(call);
        Assert.Equal(target.Id, call!.Promotion);

        Decisions.Accept(world, call, world.CurrentDate);
        Assert.True(target.Defunct);
        Assert.Empty(world.Decisions);
        Assert.Contains(player.Ledger, e => e.Category == FinanceCategory.Acquisition);
    }

    // --- Bidding wars --------------------------------------------------------

    [Fact]
    public void ABiddingWar_Escalates_ResolvesAtWarPrices_AndBlocksTheQuietMarket()
    {
        var world = Build();
        // A star hits free agency.
        var star = world.Fighters.Values.First(f => f.Status == FighterStatus.Active
            && !f.LocalTalent && f.Record.TotalBouts >= 6 && f.Contract is not null);
        star.Contract = null;
        star.Intangibles.Popularity = 80;

        for (ulong s = 1; s < 200 && world.BiddingWars.Count == 0; s++)
            BiddingWars.RunWeekly(world, world.CurrentDate, new SimRandom(SimRandom.Mix(0xB1D5, s)));
        var war = Assert.Single(world.BiddingWars);
        Assert.Equal(star.Id, war.Fighter);
        Assert.True(war.Suitors.Count >= 2, "it takes two rooms to make a war");
        var opening = war.CurrentBid;

        // The war owns the fighter: the quiet monthly market must not sign them.
        FreeAgencyService.RunMonthly(world, world.CurrentDate);
        Assert.Null(star.Contract);

        // Rounds escalate until OUR war resolves at the inflated number (fresh
        // wars over other stars may start meanwhile — that's the feature working).
        for (int round = 0; round < 5 && world.BiddingWars.Contains(war); round++)
        {
            world.CurrentDate = war.NextRoundOn;
            BiddingWars.RunWeekly(world, world.CurrentDate, new SimRandom(1000 + (ulong)round));
        }
        Assert.DoesNotContain(world.BiddingWars, w => w.Fighter == star.Id);
        if (star.Contract is { } deal)   // resolved as a signing (not a collapse)
        {
            Assert.True(deal.PursePerFight.Amount > opening.Amount,
                $"the winner pays war prices ({deal.PursePerFight} vs opening {opening})");
            Assert.True(deal.SigningBonus.Amount > 0, "the winner's curse includes the bonus cheque");
        }
    }

    [Fact]
    public void AnySignature_EndsTheWarOnTheSpot()
    {
        var world = Build();
        var star = world.Fighters.Values.First(f => f.Status == FighterStatus.Active && !f.LocalTalent && f.Contract is null)
                   ?? world.Fighters.Values.First(f => f.Status == FighterStatus.Active && !f.LocalTalent);
        star.Contract = null;
        var war = new BiddingWar { Fighter = star.Id, CurrentBid = new Money(50_000m), NextRoundOn = world.CurrentDate.AddDays(14) };
        world.BiddingWars.Add(war);

        ContractLifecycle.Sign(world, star, new ContractOffer
        {
            Fighter = star.Id, Promotion = world.PlayerPromotion, PursePerFight = new Money(60_000m), Fights = 4,
        }, world.CurrentDate);

        Assert.Empty(world.BiddingWars);   // the swoop ends the soap opera
    }

    // --- Scandals ------------------------------------------------------------

    [Fact]
    public void AnOverturnedResult_ReadsNcForBoth_AndTheRecordsAgree()
    {
        var world = Build();
        var ev = world.Events.Values.First(e => e.Status == EventStatus.Completed
            && e.Card.Any(f => f.Outcome is { Winner: not null, Method: FightMethod.Knockout }));
        var fight = ev.Card.First(f => f.Outcome is { Winner: not null, Method: FightMethod.Knockout });
        var winner = world.Get(fight.Outcome!.Winner!.Value)!;
        var loser = world.Get(fight.Outcome!.Loser!.Value)!;
        int wins = winner.Record.Wins, kos = winner.Record.WinsByKo, losses = loser.Record.Losses;
        int winnerBouts = winner.Record.TotalBouts, loserBouts = loser.Record.TotalBouts;

        Storylines.Overturn(world, fight, world.CurrentDate);

        Assert.True(fight.OverturnedNc);
        Assert.Equal(wins - 1, winner.Record.Wins);
        Assert.Equal(kos - 1, winner.Record.WinsByKo);
        Assert.Equal(losses - 1, loser.Record.Losses);
        Assert.Equal(0, winner.Record.CurrentStreak);
        // The bout still HAPPENED — total bouts hold, so the fight-log invariant survives.
        Assert.Equal(winnerBouts, winner.Record.TotalBouts);
        Assert.Equal(loserBouts, loser.Record.TotalBouts);
    }

    // --- Camp withdrawals & short-notice saves -------------------------------

    [Fact]
    public void ACampInjury_WeeksOut_GetsTheBoutSaved_OrScrapped_NeverGhosted()
    {
        var world = Build(seed: 9);
        world.PlayerPromotion = default;   // AI card — the matchmaker saves its own bouts

        // A card booked three weeks out (the announced-in-advance kind — player
        // events and superfight cards live in this window).
        var promo = world.Promotions.Values.First(p => !p.Defunct && world.RosterOf(p.Id).Count() >= 8);
        var div = promo.Divisions.First(d => world.RosterOf(promo.Id)
            .Count(f => d.Matches(f) && f.Status == FighterStatus.Active) >= 4);
        var four = world.RosterOf(promo.Id)
            .Where(f => div.Matches(f) && f.Status == FighterStatus.Active).Take(4).ToList();
        var ev = new EventService().CreateEvent(world, promo.Id, "Announced Card",
            world.CurrentDate.AddDays(21), new[]
            {
                new Matchup(four[0].Id, four[1].Id, div.WeightClass, 3, false, null),
                new Matchup(four[2].Id, four[3].Id, div.WeightClass, 3, false, null),
            });
        var before = ev.Card.Select(f => f.Id).ToList();

        // Hunt for the week the scale of bad luck tips. An AI card rolls on its
        // own keyed stream (world seed, card, date) — not the week's shared one —
        // so the hunt turns the world seed rather than the stream handed in.
        bool changed = false;
        ulong seed = world.RngSeed;
        for (ulong s = 1; s < 400 && !changed; s++)
        {
            world.RngSeed = SimRandom.Mix(0xCA3B, s);
            Storylines.RollCampWithdrawals(world, world.CurrentDate, new SimRandom(SimRandom.Mix(0xCA3B, s)));
            changed = !ev.Card.Select(f => f.Id).SequenceEqual(before);
        }
        world.RngSeed = seed;
        Assert.True(changed, "the hunt should find a withdrawal");

        // Every bout on the card is either intact, replaced (new fight id, one
        // original corner), or gone — and every corner still booked is healthy.
        foreach (var fight in ev.Card)
            foreach (var corner in new[] { fight.RedCorner, fight.BlueCorner })
                Assert.True(world.Get(corner)!.Status == FighterStatus.Active,
                    "an injured fighter must never remain booked");
        Assert.Contains(world.News, n => n.Category == NewsCategory.Withdrawal);
    }

    // --- Fog-of-war pricing --------------------------------------------------

    [Fact]
    public void TheMarket_PaysForWhatItHasSeen()
    {
        var world = Build();
        var valuation = new FighterValuation();
        var f = world.Fighters.Values.First(x => x.Status == FighterStatus.Active && !x.LocalTalent);

        Money AskAt(int wins, int losses)
        {
            f.Record.Wins = wins; f.Record.Losses = losses;
            f.Record.Draws = 0; f.Record.NoContests = 0; f.Record.CurrentStreak = 0;
            f.Momentum = 0;
            return valuation.Evaluate(world, f).AskingPurse;
        }

        var fresh = AskAt(0, 0);
        var oneLoss = AskAt(0, 1);
        var proven = AskAt(8, 2);
        Assert.True(oneLoss.Amount <= fresh.Amount, $"an 0-1 ({oneLoss}) must never out-ask an 0-0 ({fresh})");
        Assert.True(proven.Amount >= fresh.Amount, "proof is what gets paid for");

        // A podium is public proof the fog can't hide — the ask carries it.
        var before = AskAt(2, 1);
        f.Accolades.Add(new Accolade(world.CurrentDate, $"{world.CurrentDate.Year} Olympic Games — gold"));
        var medalled = valuation.Evaluate(world, f).AskingPurse;
        Assert.True(medalled.Amount > before.Amount, "an Olympic gold should raise the ask");
        f.Accolades.RemoveAt(f.Accolades.Count - 1);
    }

    [Fact]
    public void ADiscoveredLocal_DealsCheapWithTheirDiscoverer_AndFullPriceWithEveryoneElse()
    {
        var world = Build();
        var f = world.Fighters.Values.First(x => x.Status == FighterStatus.Active && !x.LocalTalent);
        var discoverer = world.Promotions.Values.First(p => !p.Defunct);
        var stranger = world.Promotions.Values.First(p => !p.Defunct && p.Id != discoverer.Id);
        f.Contract = null;
        f.DiscoveredBy = discoverer.Id;
        f.Record.Wins = 1; f.Record.Losses = 0; f.Record.Draws = 0; f.Record.NoContests = 0;

        Assert.True(Negotiator.GratitudeFactor(f, discoverer.Id) < 1m, "gratitude discounts the discoverer");
        Assert.Equal(1m, Negotiator.GratitudeFactor(f, stranger.Id));

        // The thank-you fades once the career is their own story.
        f.Record.Wins = 6;
        Assert.Equal(1m, Negotiator.GratitudeFactor(f, discoverer.Id));
    }

    // --- Commissioning belts -------------------------------------------------

    [Fact]
    public void CommissioningABelt_CreatesDivisionAndVacantTitle_AndBillsTheHouse()
    {
        var world = Build();
        var promo = world.Get(world.PlayerPromotion)!;
        // A division (weight class + sex) the org doesn't run yet.
        var (wc, sex) = Enum.GetValues<WeightClass>()
            .SelectMany(w => new[] { (w, Sex.Male), (w, Sex.Female) })
            .First(x => promo.Divisions.All(d => d.WeightClass != x.Item1 || d.Sex != x.Item2));

        var title = PromotionSetup.CommissionTitle(world, promo, wc, sex, world.CurrentDate);

        var div = promo.Divisions.First(d => d.WeightClass == wc && d.Sex == sex);
        Assert.Equal(title.Id, div.Title);
        Assert.Null(title.CurrentChampion);                          // vacant until somebody takes it
        Assert.Equal(promo.Prestige.Value, title.Prestige.Value);    // born with the org's name
        Assert.Contains(promo.Ledger, e => e.Description.StartsWith("Commissioned the"));

        // Commissioning again is a no-op returning the same belt, not a duplicate.
        Assert.Equal(title.Id, PromotionSetup.CommissionTitle(world, promo, wc, sex, world.CurrentDate).Id);
        Assert.Single(promo.Divisions, d => d.WeightClass == wc && d.Sex == sex);
    }

    // --- Press tours ---------------------------------------------------------

    [Fact]
    public void AHotPresser_PutsPeopleInSeats()
    {
        var world = Build();
        var promo = world.Promotions.Values.First(p => !p.Defunct);
        var div = promo.Divisions.First();
        var pair = world.RosterOf(promo.Id)
            .Where(f => f.Status == FighterStatus.Active && div.Matches(f)).Take(2).ToList();
        var ev = new EventService().CreateEvent(world, promo.Id, "Hype Night", world.CurrentDate.AddDays(21),
            new[] { new Matchup(pair[0].Id, pair[1].Id, div.WeightClass, 3, false, null) });

        ev.VenueId = null;   // no capacity ceiling — measure the raw pull
        var flat = Financials.Compute(world, ev);
        ev.HypeBonus = 0.15;
        var hyped = Financials.Compute(world, ev);
        Assert.True(hyped.Attendance > flat.Attendance, "hype fills the room");
    }

    [Fact]
    public void ThePressTour_LightsUpATitleCard_InsideTheWindow()
    {
        var world = Build();
        var promo = world.Promotions.Values.First(p => p.Prestige.Value >= 15
            && p.Divisions.Any(d => d.Title is { } t && world.Get(t)?.CurrentChampion is not null));
        var div = promo.Divisions.First(d => d.Title is { } t && world.Get(t)?.CurrentChampion is not null);
        var belt = world.Get(div.Title!.Value)!;
        var champ = world.Get(belt.CurrentChampion!.Value)!;
        var chall = world.RosterOf(promo.Id)
            .First(f => div.Matches(f) && f.Id != champ.Id && f.Status == FighterStatus.Active);
        var ev = new EventService().CreateEvent(world, promo.Id, "Tour Stop", world.CurrentDate.AddDays(15),
            new[] { new Matchup(champ.Id, chall.Id, div.WeightClass, 5, true, belt.Id) });

        for (ulong s = 1; s < 300 && ev.HypeBonus == 0; s++)
            PressTour.RunWeekly(world, world.CurrentDate, new SimRandom(SimRandom.Mix(0x9E55, s)));
        Assert.True(ev.HypeBonus > 0, "some seed should light the presser up");
        Assert.True(ev.HypeBonus <= 0.20, "the dividend is bounded");

        // And outside the window, nothing rolls.
        var late = new EventService().CreateEvent(world, promo.Id, "Too Close", world.CurrentDate.AddDays(5),
            new[] { new Matchup(chall.Id, champ.Id, div.WeightClass, 5, true, belt.Id) });
        for (ulong s = 1; s < 50; s++)
            PressTour.RunWeekly(world, world.CurrentDate, new SimRandom(SimRandom.Mix(0x9E55, s)));
        Assert.Equal(0, late.HypeBonus);
    }
}
