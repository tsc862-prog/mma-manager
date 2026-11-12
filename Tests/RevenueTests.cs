using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Finance;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Tests;

/// <summary>
/// The commercial model: sponsors and media partners pay PER EVENT against
/// contract terms, the in-house platform earns monthly subs, and an idle
/// promotion earns nothing at all — income is a consequence of running shows.
/// </summary>
public class RevenueTests
{
    private static Event SettledEvent(World world, Promotion promo, int fights = 6, int attendance = 2_000,
        DateOnly? date = null, bool fought = true, EventTier tier = EventTier.FightNight)
    {
        var ev = new Event
        {
            Id = world.NewEventId(),
            Promotion = promo.Id,
            Name = "Test Night",
            Date = date ?? world.CurrentDate,
            Region = promo.BaseRegion,
            Status = EventStatus.Completed,
            Tier = tier,
        };
        for (int i = 0; i < fights; i++)
        {
            var red = new FighterId(900_000 + i * 2);
            var blue = new FighterId(900_001 + i * 2);
            ev.Card.Add(new Fight
            {
                Id = world.NewFightId(), RedCorner = red, BlueCorner = blue,
                Outcome = fought ? new FightOutcome { Winner = red, Loser = blue, Method = FightMethod.UnanimousDecision, EndRound = 3, EndTimeSeconds = 300 } : null,
            });
        }
        ev.Financials = new EventFinancials { Attendance = attendance };
        world.Events[ev.Id] = ev;
        return ev;
    }

    [Fact]
    public void Build_SeedsTheCommercialSlate_ToMatchEachTier()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options
        {
            Seed = 5,
            Founding = new WorldBuilder.FoundingSpec { Prestige = 15 },
        });

        foreach (var p in world.Promotions.Values.Where(p => p.Id != world.PlayerPromotion))
        {
            Assert.NotEmpty(p.SponsorDeals);
            var size = MmaManager.Core.Sim.Org.OrgSizing.SizeOf(p);
            bool onScreen = MediaService.DealIn(p, MediaLane.Linear) is not null
                || MediaService.DealIn(p, MediaLane.Streaming) is not null || p.Stream is not null;
            if (size >= OrgSize.National)
                Assert.True(onScreen, $"{p.Abbreviation} ({size}) should open with a media presence");
            if (size >= OrgSize.Continental)
                Assert.True(MediaService.HasPpvCarrier(p), $"{p.Abbreviation} ({size}) should open with a PPV pipe");
            if (size <= OrgSize.City)
                Assert.False(onScreen, "a City outfit hasn't earned a screen yet");
        }

        // The founding: one friendly local backer, an offer to grow into, no media.
        var founded = world.Get(world.PlayerPromotion)!;
        Assert.Single(founded.SponsorDeals);
        Assert.Empty(founded.MediaDeals);
        Assert.Contains(founded.Offers, o => o.Media is null);
    }

    [Fact]
    public void AnIdlePromotion_EarnsNothing()
    {
        // The zombie check: no shows, no sponsor checks, no rights fees, no merch.
        var world = new WorldBuilder().Build(new WorldBuilder.Options
        {
            Seed = 5,
            Founding = new WorldBuilder.FoundingSpec(),
        });
        var promo = world.Get(world.PlayerPromotion)!;
        decimal before = promo.Balance.Amount;

        for (int m = 0; m < 6; m++)
            RevenueService.CollectMonthly(world, world.CurrentDate.AddMonths(m));

        Assert.Equal(before, promo.Balance.Amount);
        Assert.DoesNotContain(promo.Ledger, e => e.Category == FinanceCategory.Sponsorship);
        Assert.DoesNotContain(promo.Ledger, e => e.Category == FinanceCategory.Merchandise && e.Amount.Amount > 0);
    }

    [Fact]
    public void FightNight_PaysSponsors_AndTheMerchTable()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5, Founding = new WorldBuilder.FoundingSpec() });
        var promo = world.Get(world.PlayerPromotion)!;
        promo.SponsorDeals.Clear();
        promo.SponsorDeals.Add(new SponsorDeal
        {
            Partner = "Test Backer", FeePerEvent = new Money(10_000m), EventsRemaining = 4, SignedOn = world.CurrentDate,
        });
        var ev = SettledEvent(world, promo);

        decimal before = promo.Balance.Amount;
        RevenueService.SettleEvent(world, promo, ev);

        Assert.Contains(promo.Ledger, e => e.Category == FinanceCategory.Sponsorship && e.Amount.Amount == 10_000m);
        Assert.Contains(promo.Ledger, e => e.Category == FinanceCategory.Merchandise && e.Description.Contains("merch table"));
        Assert.True(promo.Balance.Amount > before);
        Assert.Equal(3, promo.SponsorDeals[0].EventsRemaining);
    }

    [Fact]
    public void MissedTerms_HalveTheCheck_AndTwoStrikesLoseTheSponsor()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5, Founding = new WorldBuilder.FoundingSpec() });
        var promo = world.Get(world.PlayerPromotion)!;
        promo.SponsorDeals.Clear();
        var deal = new SponsorDeal
        {
            Partner = "Demanding Corp", FeePerEvent = new Money(10_000m), EventsRemaining = 9, SignedOn = world.CurrentDate,
            Requirements = { new DealRequirement(DealRequirementKind.MinAttendance, 50_000) },   // never met
        };
        promo.SponsorDeals.Add(deal);

        RevenueService.SettleEvent(world, promo, SettledEvent(world, promo));
        Assert.Contains(promo.Ledger, e => e.Category == FinanceCategory.Sponsorship && e.Amount.Amount == 5_000m);
        Assert.Equal(1, deal.Strikes);
        Assert.Contains(world.Mail, m => m.Subject.Contains("Demanding Corp is unhappy"));

        RevenueService.SettleEvent(world, promo, SettledEvent(world, promo));
        Assert.Empty(promo.SponsorDeals);   // struck out
        Assert.Contains(world.News, n => n.Headline.Contains("Demanding Corp drops"));
    }

    [Fact]
    public void ABrokenBroadcastWindow_StrikesTheMediaDeal()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5, Founding = new WorldBuilder.FoundingSpec() });
        var promo = world.Get(world.PlayerPromotion)!;
        var deal = new MediaDeal
        {
            Kind = MediaKind.SpecialtyCable, Partner = "Combat One",
            RightsFeePerEvent = new Money(30_000m), MonthsRemaining = 18, SignedOn = world.CurrentDate,
            Requirements =
            {
                new DealRequirement(DealRequirementKind.MinMainCard, 3),
                new DealRequirement(DealRequirementKind.MaxMainCard, 5),
            },
        };
        promo.MediaDeals.Add(deal);

        // A 6-fight card with no split: the whole card is "main card" — over the window.
        var ev = SettledEvent(world, promo, fights: 6);
        RevenueService.SettleEvent(world, promo, ev);

        Assert.Contains(promo.Ledger, e => e.Category == FinanceCategory.Broadcast && e.Amount.Amount == 15_000m);
        Assert.Equal(1, deal.Strikes);

        // The same card split to a 4-fight window airs clean at full rights,
        // and a clean card earns the strike back.
        var ok = SettledEvent(world, promo, fights: 6);
        ok.MainCardFights = 4;
        RevenueService.SettleEvent(world, promo, ok);
        Assert.Contains(promo.Ledger, e => e.Category == FinanceCategory.Broadcast && e.Amount.Amount == 30_000m);
        Assert.Equal(0, deal.Strikes);
    }

    [Fact]
    public void Strikes_AreJudgedOnTheRunOfCards_NotForever()
    {
        // The cliff this replaced: three misses at any distance ended the deal,
        // so a partner whose floor an org met most nights still walked within
        // months. Now miss-clean-miss-clean never reaches the limit, while
        // three misses in a row still do.
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5, Founding = new WorldBuilder.FoundingSpec() });
        var promo = world.Get(world.PlayerPromotion)!;
        var deal = new MediaDeal
        {
            Kind = MediaKind.SpecialtyCable, Partner = "Combat One",
            RightsFeePerEvent = new Money(30_000m), MonthsRemaining = 18, SignedOn = world.CurrentDate,
            Requirements = { new DealRequirement(DealRequirementKind.MaxMainCard, 5) },
        };
        promo.MediaDeals.Add(deal);

        Event Card(bool clean)
        {
            var ev = SettledEvent(world, promo, fights: 6);
            if (clean) ev.MainCardFights = 4;
            return ev;
        }

        for (int i = 0; i < 5; i++)
        {
            RevenueService.SettleEvent(world, promo, Card(clean: false));
            RevenueService.SettleEvent(world, promo, Card(clean: true));
        }
        Assert.Contains(deal, promo.MediaDeals);
        Assert.Equal(0, deal.Strikes);

        for (int i = 0; i < MediaService.StrikeLimit; i++)
            RevenueService.SettleEvent(world, promo, Card(clean: false));
        Assert.DoesNotContain(deal, promo.MediaDeals);
    }

    private static (World World, Promotion Promo, MediaDeal Deal) CadenceDeal(int signedDaysAgo, int strikes = 1,
        MediaKind kind = MediaKind.SpecialtyCable, string partner = "Combat One")
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5, Founding = new WorldBuilder.FoundingSpec() });
        var promo = world.Get(world.PlayerPromotion)!;
        var deal = new MediaDeal
        {
            Kind = kind, Partner = partner,
            RightsFeePerEvent = new Money(30_000m), MonthsRemaining = 18,
            SignedOn = world.CurrentDate.AddDays(-signedDaysAgo), Strikes = strikes,
            LastAiredOn = world.CurrentDate.AddDays(-5),   // the monthly check's clock reset: NOT an airing
            Requirements =
            {
                new DealRequirement(DealRequirementKind.MaxMainCard, 20),
                new DealRequirement(DealRequirementKind.EventCadence, 100),
            },
        };
        promo.MediaDeals.Add(deal);
        return (world, promo, deal);
    }

    [Fact]
    public void ALateCleanCard_DoesNotClearAStrike()
    {
        // 130 days since the deal's last card against a 100-day clause: the card is
        // clean on every card term, but it is the late card that caused the cadence
        // strike, so it can't wipe it (strike, clear, strike, clear forever).
        var (world, promo, deal) = CadenceDeal(signedDaysAgo: 130);
        int mail = world.Mail.Count;
        RevenueService.SettleEvent(world, promo, SettledEvent(world, promo));
        Assert.Equal(1, deal.Strikes);
        Assert.Equal(mail, world.Mail.Count);
    }

    [Fact]
    public void AnOnTimeCleanCard_ClearsOneStrike_WithOneMail()
    {
        var (world, promo, deal) = CadenceDeal(signedDaysAgo: 60, strikes: 2);
        int mail = world.Mail.Count;
        RevenueService.SettleEvent(world, promo, SettledEvent(world, promo));
        Assert.Equal(1, deal.Strikes);
        Assert.Equal(mail + 1, world.Mail.Count);

        // The next card, a week later, is on cadence from THIS card: one more off.
        var second = SettledEvent(world, promo, date: world.CurrentDate.AddDays(7));
        second.Name = "Test Night 2";   // (identical unread mail is de-duplicated)
        RevenueService.SettleEvent(world, promo, second);
        Assert.Equal(0, deal.Strikes);
        Assert.Equal(mail + 2, world.Mail.Count);
    }

    [Fact]
    public void APpvCarrier_IsNotClearedByANonPpvNight_AndACardWithNothingFoughtProvesNothing()
    {
        var (world, promo, carrier) = CadenceDeal(signedDaysAgo: 10, kind: MediaKind.PpvCarrier, partner: "MainCard");
        RevenueService.SettleEvent(world, promo, SettledEvent(world, promo));                       // a fight night
        Assert.Equal(1, carrier.Strikes);
        RevenueService.SettleEvent(world, promo, SettledEvent(world, promo, tier: EventTier.Ppv));  // the carrier's night
        Assert.Equal(0, carrier.Strikes);

        var (w2, p2, deal) = CadenceDeal(signedDaysAgo: 10);
        RevenueService.SettleEvent(w2, p2, SettledEvent(w2, p2, fought: false));
        Assert.Equal(1, deal.Strikes);
    }

    [Fact]
    public void GoingDark_StrikesTheCadenceClause_UntilTheNetworkWalks()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5, Founding = new WorldBuilder.FoundingSpec() });
        var promo = world.Get(world.PlayerPromotion)!;
        var deal = new MediaDeal
        {
            Kind = MediaKind.SpecialtyCable, Partner = "Combat One",
            RightsFeePerEvent = new Money(30_000m), MonthsRemaining = 18,
            SignedOn = world.CurrentDate.AddDays(-400), LastAiredOn = world.CurrentDate.AddDays(-400),
            Requirements = { new DealRequirement(DealRequirementKind.EventCadence, 100) },
        };
        promo.MediaDeals.Add(deal);
        var rng = new SimRandom(1);

        MediaService.RunMonthly(world, promo, world.CurrentDate, rng);
        Assert.Equal(1, deal.Strikes);

        // Dark past the window again and again: three strikes and it's over.
        deal.LastAiredOn = world.CurrentDate.AddDays(-200);
        MediaService.RunMonthly(world, promo, world.CurrentDate, rng);
        deal.LastAiredOn = world.CurrentDate.AddDays(-200);
        MediaService.RunMonthly(world, promo, world.CurrentDate, rng);

        Assert.Empty(promo.MediaDeals);
        Assert.Contains(world.News, n => n.Headline.Contains("Combat One drops"));
    }

    [Fact]
    public void TheInHouseStream_CostsToLaunch_AndFollowsTheProduct()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5, Founding = new WorldBuilder.FoundingSpec() });
        var promo = world.Get(world.PlayerPromotion)!;
        decimal before = promo.Balance.Amount;

        Assert.True(MediaService.LaunchStream(world, promo, world.CurrentDate));
        Assert.NotNull(promo.Stream);
        Assert.Equal(before - MediaService.StreamLaunchCost.Amount, promo.Balance.Amount);
        Assert.False(MediaService.LaunchStream(world, promo, world.CurrentDate));   // one platform only

        // An ACTIVE promotion's platform gains subscribers month over month...
        SettledEvent(world, promo);   // a recent completed card = a living org
        var rng = new SimRandom(1);
        MediaService.RunMonthly(world, promo, world.CurrentDate, rng);
        int active = promo.Stream!.Subscribers;
        Assert.True(active > 0, "an active org's platform should find subscribers");
        Assert.Contains(promo.Ledger, e => e.Category == FinanceCategory.Streaming);
        Assert.Contains(promo.Ledger, e => e.Category == FinanceCategory.StreamingOps);

        // ...and a dark promotion's platform bleeds them.
        world.CurrentDate = world.CurrentDate.AddDays(200);   // the card ages out of the activity window
        MediaService.RunMonthly(world, promo, world.CurrentDate, rng);
        Assert.True(promo.Stream.Subscribers < active, "a dark org's subscribers should melt away");
    }

    [Fact]
    public void MainCardSplit_DefaultsToTheDealWindow_WhenTheCardRuns()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 13, UseDesignedRoster = false });
        var promo = world.Promotions.Values.OrderByDescending(p => p.Prestige.Value).First();
        Assert.NotNull(MediaService.BroadcastWindow(promo));   // a big org's deals define a window

        var ev = new EventService().BookAndRun(world, promo.Id, "Windowed", world.CurrentDate, new Matchmaker());

        Assert.True(ev.MainCardFights > 0, "an unsplit card should fit itself to the deal's window");
        Assert.True(ev.EffectiveMainCard <= MediaService.BroadcastWindow(promo));
        Assert.True(ev.Card.Count >= ev.EffectiveMainCard);
    }

    [Fact]
    public void ANetwork_PitchesOnce_AndReturnsOnlyWhenBackOnTheMarket()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5, Founding = new WorldBuilder.FoundingSpec() });
        var promo = world.Get(world.PlayerPromotion)!;
        promo.Prestige = 35;   // National — cable and specialty both qualified
        var rng = new SimRandom(1);

        // The seeded world sells most of the dial — free a channel (the churn
        // that happens at renewals anyway) so a pitch can arrive.
        // (A rung the org actually reaches — a starless National can't use a freed prime slot.)
        var seededDeal = world.Promotions.Values.Where(p => p.Id != promo.Id)
            .SelectMany(p => p.MediaDeals.Select(d => (Promo: p, Deal: d)))
            .First(x => x.Deal.Kind == MediaKind.CableSports);
        seededDeal.Promo.MediaDeals.Remove(seededDeal.Deal);

        // Run the market until an offer lands, then PASS on it.
        for (int m = 0; m < 12 && !promo.Offers.Any(o => o.Media is not null); m++)
            MediaService.RunMonthly(world, promo, world.CurrentDate.AddMonths(m), rng);
        var offer = promo.Offers.First(o => o.Media is not null);
        string partner = offer.Partner;
        Assert.Contains(partner, promo.CourtedBy);
        promo.Offers.Remove(offer);   // the player passes

        // They don't call again — however long the market runs.
        for (int m = 0; m < 24; m++)
            MediaService.RunMonthly(world, promo, world.CurrentDate.AddMonths(m), rng);
        Assert.DoesNotContain(promo.Offers, o => o.Partner == partner);

        // The partner signs elsewhere, then that deal dies (goes dark past the
        // cadence until dropped): they re-enter the market and can pitch again.
        var rival = world.Promotions.Values.First(p => p.Id != promo.Id && !p.Defunct);
        rival.MediaDeals.Add(new MediaDeal
        {
            Kind = offer.Media!.Value, Partner = partner, MonthsRemaining = 24,
            SignedOn = world.CurrentDate, LastAiredOn = world.CurrentDate.AddDays(-999),
            Requirements = { new DealRequirement(DealRequirementKind.EventCadence, 60) },
        });
        for (int i = 0; i < MediaService.StrikeLimit; i++)
        {
            MediaService.RunMonthly(world, rival, world.CurrentDate, rng);
            rival.MediaDeals.ForEach(d => d.LastAiredOn = world.CurrentDate.AddDays(-999));
        }
        Assert.DoesNotContain(rival.MediaDeals, d => d.Partner == partner);
        Assert.DoesNotContain(partner, promo.CourtedBy);   // free to court everyone afresh
    }

    [Fact]
    public void ABusyNetwork_IsOffTheMarket()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5, Founding = new WorldBuilder.FoundingSpec() });
        var promo = world.Get(world.PlayerPromotion)!;
        promo.Prestige = 35;   // National: qualified for every linear rung's attention
        var rng = new SimRandom(1);

        // Every linear partner in the world signs with someone else.
        var rivals = world.Promotions.Values.Where(p => p.Id != promo.Id && !p.Defunct).ToList();
        var linearPartners = new[]
        {
            "Combat One", "The Fight Channel", "Octagon TV", "Ring Report",
            "PrimeSport", "Vertex Sports", "Global Combat Network", "AllSport 24",
            "NBS", "United Broadcasting", "Star Network", "Meridian One",
        };
        foreach (var (name, i) in linearPartners.Select((n, i) => (n, i)))
        {
            var holder = rivals[i % rivals.Count];
            holder.MediaDeals.RemoveAll(d => d.Partner == name);
            holder.MediaDeals.Add(new MediaDeal
            {
                Kind = MediaKind.SpecialtyCable, Partner = name,
                MonthsRemaining = 999, SignedOn = world.CurrentDate, LastAiredOn = world.CurrentDate,
            });
        }

        for (int m = 0; m < 12; m++)
            MediaService.RunMonthly(world, promo, world.CurrentDate.AddMonths(m), rng);
        Assert.DoesNotContain(promo.Offers, o => o.Media is not null
            && MediaLanes.LaneOf(o.Media.Value) == MediaLane.Linear);   // the whole dial is spoken for
    }

    [Fact]
    public void Newgens_TurnPro_AtZeroAndZero()
    {
        // A record with no fights behind it is a lie the profile can't cash:
        // only the initial build rolls backstory records (HistoryBuilder makes
        // them REAL); a mid-sim debutant's story starts in the cage.
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5, UseDesignedRoster = false });
        var intake = MmaManager.Core.Sim.Living.NewgenService.ForWorld(world)
            .GenerateIntake(world, 12, new SimRandom(3));

        Assert.All(intake, f => Assert.Equal(0, f.Record.TotalBouts));
        Assert.All(intake, f => Assert.Empty(f.BoutHistory));
    }

    [Fact]
    public void Networks_HavePersonalities_NotPaletteSwaps()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5, Founding = new WorldBuilder.FoundingSpec() });
        var promo = world.Get(world.PlayerPromotion)!;
        promo.Prestige = 40;

        // Same rung, different houses: the flagship pays more and demands more,
        // the budget channel pays less and asks less.
        Assert.Equal("flagship", MediaService.TagOf("PrimeSport"));
        Assert.Equal("budget", MediaService.TagOf("AllSport 24"));

        MediaDeal Sign(string partner)
        {
            promo.MediaDeals.Clear();
            promo.Offers.Clear();
            promo.CourtedBy.Clear();
            var offer = new DealOffer { Media = MediaKind.CableSports, Partner = partner, FeePerEvent = Money.Zero, Term = 24 };
            promo.Offers.Add(offer);
            return MediaService.Accept(world, promo, offer, world.CurrentDate);
        }

        // Fees & terms come from the OFFER, which MakeOffer builds per profile —
        // test the profile plumbing directly through renewal (which re-prices
        // off the profile) and the reach scaling.
        var flagship = Sign("PrimeSport");
        flagship.MonthsRemaining = 24;
        double flagshipExposure = MediaService.Exposure(promo);
        var budget = Sign("AllSport 24");
        double budgetExposure = MediaService.Exposure(promo);
        Assert.True(flagshipExposure > budgetExposure, "the flagship carries further than the budget channel");

        // The carrier's personality is its cut: premium pipe 60%, budget 50%.
        promo.MediaDeals.Clear();
        promo.MediaDeals.Add(new MediaDeal { Kind = MediaKind.PpvCarrier, Partner = "PrimeView" });
        Assert.Equal(0.60m, MediaService.PpvShare(promo));
        promo.MediaDeals.Clear();
        promo.MediaDeals.Add(new MediaDeal { Kind = MediaKind.PpvCarrier, Partner = "BoxSeat" });
        Assert.Equal(0.50m, MediaService.PpvShare(promo));
        promo.MediaDeals.Clear();
        Assert.Equal(0.55m, MediaService.PpvShare(promo));   // no carrier — legacy flat share
    }

    [Fact]
    public void Exposure_ScalesWithTheLadder()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5, Founding = new WorldBuilder.FoundingSpec() });
        var promo = world.Get(world.PlayerPromotion)!;

        Assert.Equal(1.0, MediaService.Exposure(promo));
        Assert.Equal(0.75, MediaService.BrandFactor(promo));   // no screen: word of mouth only

        promo.Stream = new StreamingPlatform { Name = "NWF+", LaunchedOn = world.CurrentDate };
        Assert.True(MediaService.Exposure(promo) > 1.0);

        promo.MediaDeals.Add(new MediaDeal { Kind = MediaKind.PrimeBroadcast, Partner = "NBS" });
        Assert.Equal(1.5, MediaService.Exposure(promo));
        Assert.True(MediaService.FameCeilingBump(promo) >= 6);
        Assert.Equal(1.25, MediaService.BrandFactor(promo));
    }
}
