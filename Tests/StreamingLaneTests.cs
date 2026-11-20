using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Finance;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Tests;

/// <summary>
/// An AI house on its own streaming platform can be courted back by a streaming giant (MajorStream), weighs the
/// offer against what its platform nets, and once it signs it stays on the deal for the term. The player's house
/// and the 1993 world are untouched, and the shared monthly stream makes no new draw for it.
/// </summary>
public class StreamingLaneTests
{
    private static readonly string[] Giants = { "Streamflix", "Arena+", "Watchtower", "Nimbus Video" };

    private static World Build(ulong seed = 5) => new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });

    /// <summary>AI houses on their own platform that a streaming giant would sign, lowest id first.</summary>
    private static IEnumerable<Promotion> PlatformHouses(World world) =>
        world.Promotions.Values
            .Where(p => !p.Defunct && p.Id != world.PlayerPromotion && p.Stream is not null
                && MediaService.DealIn(p, MediaLane.Streaming) is null
                && MediaService.LadderView(world, p).First(r => r.Kind == MediaKind.MajorStream).Qualified)
            .OrderBy(p => p.Id.Value);

    /// <summary>The first of them, lifted to the top of the sport (International) so a giant may court it back.</summary>
    private static Promotion OwnPlatformHouse(World world)
    {
        var p = PlatformHouses(world).First();
        p.PrestigeHundredths = Math.Max(p.PrestigeHundredths, 7_200);
        return p;
    }

    /// <summary>Pin the house's other lanes so the monthly market makes no draw for them (top linear rung, a PPV pipe, long terms, no clauses).</summary>
    private static void QuietOtherLanes(Promotion p)
    {
        p.MediaDeals.Clear();
        p.Offers.Clear();
        p.MediaDeals.Add(new MediaDeal { Kind = MediaKind.PrimeBroadcast, Partner = "NBS", RightsFeePerEvent = new Money(50_000_000m), MonthsRemaining = 99, SignedOn = new DateOnly(2026, 1, 1) });
        p.MediaDeals.Add(new MediaDeal { Kind = MediaKind.PpvCarrier, Partner = "MainCard", MonthsRemaining = 99, SignedOn = new DateOnly(2026, 1, 1) });
    }

    private static DealOffer GiantOffer(World world, string partner, decimal fee) => new()
    {
        Media = MediaKind.MajorStream,
        Partner = partner,
        FeePerEvent = new Money(fee),
        Term = 24,
        Requirements = new List<DealRequirement>(),
        OfferedOn = world.CurrentDate,
        ExpiresOn = world.CurrentDate.AddDays(90),
    };

    /// <summary>Only <paramref name="partner"/> could pitch: everyone else has already called.</summary>
    private static void OnlyThisGiant(Promotion p, string partner)
    {
        foreach (var g in Giants) if (g != partner) p.CourtedBy.Add(g);
    }

    [Fact]
    public void AHouseOnItsOwnPlatform_IsCourtedBackByAStreamingGiant()
    {
        var world = Build();
        var house = OwnPlatformHouse(world);
        Assert.True(MediaService.OnOwnPlatform(world, house));
        house.CourtedBy.Clear();
        var rng = new SimRandom(11);
        bool pitched = false;
        for (int m = 0; m < 12 && !pitched; m++)
        {
            MediaService.RunMonthly(world, house, world.CurrentDate.AddMonths(m), rng);
            pitched = house.CourtedBy.Overlaps(Giants);   // a pitch is remembered, whether taken or turned down
        }
        Assert.True(pitched, "no streaming giant called in a year");
    }

    [Fact]
    public void TheHouse_TakesAnOfferWorthMoreThanItsPlatform_AndClosesThePlatform()
    {
        var world = Build();
        var house = OwnPlatformHouse(world);
        house.Stream!.Subscribers = 1_000;                  // a platform netting next to nothing
        OnlyThisGiant(house, "Arena+");
        var offer = GiantOffer(world, "Arena+", 1_000_000m);
        house.Offers.Add(offer);
        Assert.True(MediaService.OfferPerQuarter(world, house, offer) > MediaService.PlatformPerQuarter(house));

        MediaService.RunMonthly(world, house, world.CurrentDate, new SimRandom(3));

        Assert.Null(house.Stream);
        var deal = Assert.Single(house.MediaDeals, d => d.Kind == MediaKind.MajorStream);
        Assert.Equal("Arena+", deal.Partner);
        Assert.False(MediaService.OnOwnPlatform(world, house));
    }

    [Fact]
    public void TheHouse_RefusesAnOfferWorthLessThanItsPlatform()
    {
        var world = Build();
        var house = OwnPlatformHouse(world);
        house.Stream!.Subscribers = 5_000_000;              // a platform netting ~$80M a quarter
        OnlyThisGiant(house, "Watchtower");
        var offer = GiantOffer(world, "Watchtower", 100_000m);
        house.Offers.Add(offer);
        Assert.True(MediaService.OfferPerQuarter(world, house, offer) < MediaService.PlatformPerQuarter(house));

        MediaService.RunMonthly(world, house, world.CurrentDate, new SimRandom(3));

        Assert.NotNull(house.Stream);
        Assert.DoesNotContain(house.MediaDeals, d => d.Kind == MediaKind.MajorStream);
        Assert.DoesNotContain(offer, house.Offers);         // answered at once: turned down, not left to slip in later
    }

    [Fact]
    public void TakingOneGiant_TurnsTheOthersDown_AndNoneSlipsInLater()
    {
        var world = Build();
        var house = OwnPlatformHouse(world);
        house.Stream!.Subscribers = 1_000;
        foreach (var g in Giants) house.CourtedBy.Add(g);   // no new calls: only the two offers below
        var strict = WithTerms(GiantOffer(world, "Streamflix", 3_000_000m), new DealRequirement(DealRequirementKind.MinStarPower, 101));
        var easy = GiantOffer(world, "Watchtower", 1_000_000m);
        house.Offers.Add(strict);
        house.Offers.Add(easy);
        var rng = new SimRandom(3);
        MediaService.RunMonthly(world, house, world.CurrentDate, rng);

        var deal = Assert.Single(house.MediaDeals, d => d.Kind == MediaKind.MajorStream);
        Assert.Equal("Watchtower", deal.Partner);                 // the richer offer's star clause was out of reach
        Assert.DoesNotContain(house.Offers, o => o.Media == MediaKind.MajorStream);
        for (int m = 1; m <= 3; m++)
        {
            MediaService.RunMonthly(world, house, world.CurrentDate.AddMonths(m), rng);
            Assert.Same(deal, Assert.Single(house.MediaDeals, d => d.Kind == MediaKind.MajorStream));
        }
    }

    [Fact]
    public void AfterSigning_TheHouseStaysOnTheDeal_AndRelaunchesOnlyWhenTheLaneIsEmpty()
    {
        var world = Build();
        var house = OwnPlatformHouse(world);
        house.Stream!.Subscribers = 1_000;
        house.Balance = new Money(50_000_000m);
        OnlyThisGiant(house, "Arena+");
        house.Offers.Add(GiantOffer(world, "Arena+", 1_000_000m));
        var rng = new SimRandom(3);
        MediaService.RunMonthly(world, house, world.CurrentDate, rng);
        var deal = Assert.Single(house.MediaDeals, d => d.Kind == MediaKind.MajorStream);

        // Months on the deal (inside its 120-day cadence, rich enough to relaunch): no platform comes back.
        for (int m = 1; m <= 3; m++)
        {
            MediaService.RunMonthly(world, house, world.CurrentDate.AddMonths(m), rng);
            Assert.Null(house.Stream);
            Assert.Contains(deal, house.MediaDeals);
        }

        // The deal gone (and the giants all called already), the house has no streaming income at all: it builds its own again.
        house.MediaDeals.Remove(deal);
        foreach (var g in Giants) house.CourtedBy.Add(g);
        MediaService.RunMonthly(world, house, world.CurrentDate.AddMonths(4), rng);
        Assert.NotNull(house.Stream);
    }

    [Fact]
    public void ThePlayersHouse_OnItsOwnPlatform_IsNeitherCourtedNorDecidedFor()
    {
        var world = Build();
        var house = OwnPlatformHouse(world);
        world.PlayerPromotion = house.Id;
        Assert.False(MediaService.OnOwnPlatform(world, house));
        house.CourtedBy.Clear();
        house.Stream!.Subscribers = 1_000;
        var rng = new SimRandom(11);
        for (int m = 0; m < 24; m++)
            MediaService.RunMonthly(world, house, world.CurrentDate.AddMonths(m), rng);
        Assert.DoesNotContain(house.Offers, o => o.Media == MediaKind.MajorStream);
        Assert.NotNull(house.Stream);

        // An offer on his desk is his to answer: the month never signs it for him.
        house.Offers.Add(GiantOffer(world, "Arena+", 5_000_000m));
        MediaService.RunMonthly(world, house, world.CurrentDate.AddMonths(25), rng);
        Assert.NotNull(house.Stream);
        Assert.DoesNotContain(house.MediaDeals, d => d.Kind == MediaKind.MajorStream);
    }

    private static DealOffer WithTerms(DealOffer o, params DealRequirement[] terms) => new()
    {
        Media = o.Media, Partner = o.Partner, FeePerEvent = o.FeePerEvent, Term = o.Term,
        Requirements = terms.ToList(), OfferedOn = o.OfferedOn, ExpiresOn = o.ExpiresOn,
    };

    /// <summary>Subscribers at which the platform nets just under (or, one more, just over) the given quarterly sum.</summary>
    private static int SubscribersNetting(Promotion house, decimal perQuarter)
    {
        // Net per quarter = 3 x (subs x 6 - (6,000 + subs x 0.6)) = 16.2 x subs - 18,000.
        int subs = (int)Math.Floor((perQuarter + 18_000m) / 16.2m);
        house.Stream!.Subscribers = subs;
        if (MediaService.PlatformPerQuarter(house) >= perQuarter) subs--;
        return subs;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TheDecision_TurnsOnTheQuarterlyNet_AtTheBoundary(bool justUnder)
    {
        var world = Build();
        var house = OwnPlatformHouse(world);
        var offer = GiantOffer(world, "Arena+", 1_000_000m);
        decimal value = MediaService.OfferPerQuarter(world, house, offer);
        Assert.True(value > 1_000_000m);
        int subs = SubscribersNetting(house, value);
        house.Stream!.Subscribers = justUnder ? subs : subs + 1;
        decimal net = MediaService.PlatformPerQuarter(house);
        Assert.True(justUnder ? net < value && value - net < 20m : net > value && net - value < 20m);
        Assert.Equal(justUnder, MediaService.TakesOverPlatform(world, house, offer));
    }

    [Fact]
    public void TheStarClause_WeighsTheOffer_AndAHouseThatWouldMissItMostlyRefuses()
    {
        var world = Build();
        var house = OwnPlatformHouse(world);
        house.Stream!.Subscribers = 0;                           // the platform loses money: only the terms can say no
        var plain = GiantOffer(world, "Arena+", 1_000_000m);
        var nobody = WithTerms(plain, new DealRequirement(DealRequirementKind.MinStarPower, 101));
        Assert.Equal(0, MediaService.RecordAgainst(world, house, nobody).Cleared);
        Assert.Equal(MediaService.OfferPerQuarter(world, house, plain) / 2, MediaService.OfferPerQuarter(world, house, nobody));
        Assert.True(MediaService.TakesOverPlatform(world, house, plain));
        Assert.False(MediaService.TakesOverPlatform(world, house, nobody));

        // The share is the house's own main events against the clause, popularity as it stands.
        int today = world.CurrentDate.DayNumber;
        var mains = world.Events.Values.Where(e => e.Promotion == house.Id && e.Status == EventStatus.Completed && e.Card.Count > 0
            && e.Date.DayNumber <= today && today - e.Date.DayNumber < 365).Select(e => e.Card[^1]).ToList();
        int Pop(FighterId id) => world.Get(id)?.Intangibles.Popularity.Value ?? 0;
        var pops = mains.Select(b => Math.Max(Pop(b.RedCorner), Pop(b.BlueCorner))).OrderBy(x => x).ToList();
        int mid = pops[pops.Count / 2];
        var some = WithTerms(plain, new DealRequirement(DealRequirementKind.MinStarPower, mid));
        var rec = MediaService.RecordAgainst(world, house, some);
        Assert.Equal(mains.Count, rec.Cards);
        Assert.Equal(pops.Count(x => x >= mid), rec.Cleared);
        Assert.Equal((rec.Cards - rec.Cleared) * MediaService.MissOneIn <= rec.Cards, MediaService.TakesOverPlatform(world, house, some));
    }

    [Fact]
    public void AHouseWithNoCardInAYear_NeverTakesADeal()
    {
        var world = Build();
        var house = OwnPlatformHouse(world);
        house.Stream!.Subscribers = 0;                           // the platform is losing money today
        world.CurrentDate = world.CurrentDate.AddYears(2);       // and nothing has run since
        var offer = GiantOffer(world, "Arena+", 5_000_000m);
        Assert.True(MediaService.PlatformPerQuarter(house) < 0);
        Assert.Equal(0m, MediaService.OfferPerQuarter(world, house, offer));
        Assert.False(MediaService.TakesOverPlatform(world, house, offer));
    }

    [Fact]
    public void AYoungHouse_IsPacedOverTheTimeItHasRun()
    {
        var world = Build();
        var house = OwnPlatformHouse(world);
        var first = world.Events.Values.Where(e => e.Promotion == house.Id && e.Status == EventStatus.Completed && e.Card.Count > 0)
            .Min(e => e.Date);
        world.CurrentDate = first.AddDays(120);                  // as if it had run four months
        var offer = GiantOffer(world, "Arena+", 1_000_000m);
        var rec = MediaService.RecordAgainst(world, house, offer);
        int cards = world.Events.Values.Count(e => e.Promotion == house.Id && e.Status == EventStatus.Completed && e.Card.Count > 0
            && e.Date >= first && e.Date <= world.CurrentDate);
        Assert.Equal(120, rec.Days);
        Assert.Equal(cards, rec.Cards);
        decimal expected = 1_000_000m * MediaService.RightsPremium(world, house) * cards * 91m / 120m;
        Assert.InRange(MediaService.OfferPerQuarter(world, house, offer), expected - 0.01m, expected + 0.01m);
    }

    [Fact]
    public void AFreedGiant_ShopsAroundToAnOpenLane_NotToAHouseOnItsOwnPlatform()
    {
        // A giant that walks out of one house pitches the most prestigious house with an OPEN lane; a house on its own
        // platform is passed over even from the very top. (Put the platform house back in ShopAround and it takes the pitch.)
        var world = Build();
        var platform = OwnPlatformHouse(world);
        var open = world.Promotions.Values.Where(p => !p.Defunct && p.Id != platform.Id && p.Id != world.PlayerPromotion && p.Stream is not null)
            .OrderBy(p => p.Id.Value).First();
        open.Stream = null;                                      // runs no platform: its streaming lane is open
        var holder = world.Promotions.Values.Where(p => p.Id != platform.Id && p.Id != open.Id && p.MediaDeals.Any(d => d.Kind == MediaKind.MajorStream))
            .OrderBy(p => p.Id.Value).First();
        var deal = holder.MediaDeals.First(d => d.Kind == MediaKind.MajorStream);
        platform.PrestigeHundredths = 9_900;                     // the top of the sport
        open.PrestigeHundredths = 9_800;                         // just under it
        foreach (var p in new[] { platform, open }) { p.Offers.Clear(); p.CourtedBy.Clear(); }
        deal.Strikes = 1;                                        // not renewed with a strike at term end
        deal.MonthsRemaining = 1;
        MediaService.RunMonthly(world, holder, world.CurrentDate, new SimRandom(5));

        Assert.DoesNotContain(deal, holder.MediaDeals);
        Assert.Contains(open.Offers, o => o.Media == MediaKind.MajorStream && o.Partner == deal.Partner);
        Assert.DoesNotContain(platform.Offers, o => o.Partner == deal.Partner);
    }

    [Fact]
    public void AContinentalHouseOnItsOwnPlatform_IsNotCourtedBack()
    {
        var world = Build();
        var house = PlatformHouses(world).First();
        house.PrestigeHundredths = 6_000;                        // Continental, over the giants' prestige bar
        Assert.True(MediaService.OnOwnPlatform(world, house));
        Assert.False(MediaService.CourtableOnPlatform(world, house, world.CurrentDate));
        house.CourtedBy.Clear();
        var rng = new SimRandom(11);
        for (int m = 0; m < 24; m++)
            MediaService.RunMonthly(world, house, world.CurrentDate.AddMonths(m), rng);
        Assert.False(house.CourtedBy.Overlaps(Giants));
        Assert.NotNull(house.Stream);
    }

    [Fact]
    public void TheLastFreeGiant_IsLeftForTheOpenLanes()
    {
        var world = Build();
        var house = OwnPlatformHouse(world);
        var others = world.Promotions.Values.Where(p => !p.Defunct && p.Id != house.Id).OrderBy(p => p.Id.Value).ToList();
        var free = Giants.Where(g => MediaService.PartnerFree(world, g)).ToList();
        Assert.True(free.Count >= MediaService.CourtBackMinFree);
        Assert.True(MediaService.CourtableOnPlatform(world, house, world.CurrentDate));
        // Sign all but one free giant elsewhere: the last one is not courted back over a platform.
        for (int i = 0; i < free.Count - 1; i++)
            others[i].MediaDeals.Add(new MediaDeal { Kind = MediaKind.MajorStream, Partner = free[i], MonthsRemaining = 24, SignedOn = world.CurrentDate });
        Assert.Equal(1, Giants.Count(g => MediaService.PartnerFree(world, g)));
        Assert.False(MediaService.CourtableOnPlatform(world, house, world.CurrentDate));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AHouseThatRelaunchedAfterStrikes_IsLeftAloneForTwoYears(bool struck)
    {
        var world = Build();
        var house = OwnPlatformHouse(world);
        var launched = world.CurrentDate.AddDays(30);
        world.CurrentDate = launched.AddDays(30);
        // Its last giant paid it reduced rights for broken terms (or paid in full) shortly before the relaunch.
        Ledger.Income(house, launched.AddDays(-60), FinanceCategory.Broadcast, new Money(500_000m),
            $"Some Night — Arena+ rights{(struck ? " (terms missed)" : "")}");
        house.Stream = new StreamingPlatform { Name = "X+", LaunchedOn = launched, Subscribers = 1_000 };
        Assert.Equal(struck, MediaService.RelaunchedAfterStrikes(world, house));
        Assert.Equal(!struck, MediaService.CourtableOnPlatform(world, house, world.CurrentDate));
        Assert.True(MediaService.CourtableOnPlatform(world, house, launched.AddDays(MediaService.RelaunchSpellDays)));
    }

    [Fact]
    public void AGiantOnThePlayersDesk_IsNeverTakenByACourtBack()
    {
        // The player holds a giant's offer; an International platform house is courtable and every other free giant
        // has already called on it, so the only giant a court-back could pick is the player's. Month after month the
        // keyed roll comes up (about half the months), and the giant must still never pitch the house: taking it would
        // withdraw the player's offer from under him.
        var world = Build();
        var house = OwnPlatformHouse(world);
        var player = world.Promotions.Values.Where(p => !p.Defunct && p.Id != house.Id).OrderBy(p => p.Id.Value).First();
        world.PlayerPromotion = player.Id;
        var free = Giants.Where(g => MediaService.PartnerFree(world, g)).ToList();
        Assert.True(free.Count >= MediaService.CourtBackMinFree + 1);
        string desk = free[0];
        var pending = GiantOffer(world, desk, 2_000_000m);
        player.Offers.Add(pending);
        house.Offers.Clear();
        house.CourtedBy.Clear();
        foreach (var g in Giants.Where(g => g != desk)) house.CourtedBy.Add(g);
        house.Stream!.Subscribers = 1_000;
        Assert.True(MediaService.OnPlayersDesk(world, desk));
        Assert.True(MediaService.CourtableOnPlatform(world, house, world.CurrentDate));

        var rng = new SimRandom(17);
        for (int m = 0; m < 36; m++)
            MediaService.RunMonthly(world, house, world.CurrentDate.AddMonths(m), rng);

        Assert.Contains(pending, player.Offers);
        Assert.DoesNotContain(desk, house.CourtedBy);
        Assert.DoesNotContain(house.MediaDeals, d => d.Partner == desk);
    }

    [Fact]
    public void AGiantOnThePlayersDesk_IsNotCountedFree()
    {
        var world = Build();
        var house = OwnPlatformHouse(world);
        var player = world.Promotions.Values.Where(p => !p.Defunct && p.Id != house.Id).OrderBy(p => p.Id.Value).First();
        world.PlayerPromotion = player.Id;
        var free = Giants.Where(g => MediaService.PartnerFree(world, g)).ToList();
        // Leave exactly two giants free, then put one of them on the player's desk: one is left, so no court-back.
        var others = world.Promotions.Values.Where(p => !p.Defunct && p.Id != house.Id && p.Id != player.Id).OrderBy(p => p.Id.Value).ToList();
        for (int i = 0; i < free.Count - 2; i++)
            others[i].MediaDeals.Add(new MediaDeal { Kind = MediaKind.MajorStream, Partner = free[i], MonthsRemaining = 24, SignedOn = world.CurrentDate });
        Assert.True(MediaService.CourtableOnPlatform(world, house, world.CurrentDate));
        player.Offers.Add(GiantOffer(world, free[^1], 2_000_000m));
        Assert.False(MediaService.CourtableOnPlatform(world, house, world.CurrentDate));
    }

    [Fact]
    public void AnOriginsWorld_KeepsTheOldLane()
    {
        var world = Build();
        var house = OwnPlatformHouse(world);
        world.Mode = WorldMode.Origins;
        Assert.False(MediaService.OnOwnPlatform(world, house));
        house.Stream!.Subscribers = 1_000;
        var offer = GiantOffer(world, "Arena+", 5_000_000m);
        Assert.False(MediaService.TakesOverPlatform(world, house, offer));
    }

    [Fact]
    public void CourtingBack_MakesNoDrawOnTheSharedStream()
    {
        // A house whose other lanes are pinned drew nothing on the shared stream in a month before this change
        // (its streaming lane read as held), and still draws nothing while the giants court it:
        // their calls at a house on its own platform roll on their own keyed hashes. (Once a deal holds the lane
        // the ordinary rules run, renewal coin flip included, so the check stops at the first call.)
        var world = Build();
        var house = OwnPlatformHouse(world);
        QuietOtherLanes(house);
        house.CourtedBy.Clear();
        house.Stream!.Subscribers = 1_000;
        bool courted = false;
        for (int m = 0; m < 24 && !courted; m++)
        {
            var used = new SimRandom(77);
            var twin = new SimRandom(77);
            MediaService.RunMonthly(world, house, world.CurrentDate.AddMonths(m), used);
            Assert.True(twin.NextULong() == used.NextULong(), $"month {m} drew on the shared stream");
            courted = house.CourtedBy.Overlaps(Giants);
        }
        Assert.True(courted, "no giant called on the house in two years");
    }
}
