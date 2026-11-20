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

    /// <summary>An AI house on its own platform that a streaming giant would sign, lowest id first.</summary>
    private static Promotion OwnPlatformHouse(World world) =>
        world.Promotions.Values
            .Where(p => !p.Defunct && p.Id != world.PlayerPromotion && p.Stream is not null
                && MediaService.DealIn(p, MediaLane.Streaming) is null
                && MediaService.LadderView(world, p).First(r => r.Kind == MediaKind.MajorStream).Qualified)
            .OrderBy(p => p.Id.Value).First();

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
            pitched = house.Offers.Any(o => o.Media == MediaKind.MajorStream)
                || house.MediaDeals.Any(d => d.Kind == MediaKind.MajorStream);
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
        Assert.Contains(offer, house.Offers);               // left on the table to lapse; never signed
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
        Assert.Equal(1_000_000m * MediaService.RightsPremium(world, house) * cards * 91m / 120m, MediaService.OfferPerQuarter(world, house, offer));
    }

    [Fact]
    public void AFreedGiant_ShopsAroundToAHouseOnItsOwnPlatform()
    {
        // A giant that walks out of one house pitches the most prestigious house that could use it, and a house on its
        // own platform is one of those now. (Revert the ShopAround line and the pitch goes elsewhere or nowhere.)
        var world = Build();
        var house = OwnPlatformHouse(world);
        var holder = world.Promotions.Values.Where(p => p.Id != house.Id && p.MediaDeals.Any(d => d.Kind == MediaKind.MajorStream))
            .OrderBy(p => p.Id.Value).First();
        var deal = holder.MediaDeals.First(d => d.Kind == MediaKind.MajorStream);
        house.PrestigeHundredths = 9_900;                        // the top of the sport
        house.Offers.Clear();
        house.CourtedBy.Clear();
        deal.Strikes = 1;                                        // not renewed with a strike at term end
        deal.MonthsRemaining = 1;
        MediaService.RunMonthly(world, holder, world.CurrentDate, new SimRandom(5));

        Assert.DoesNotContain(deal, holder.MediaDeals);
        Assert.Contains(house.Offers, o => o.Media == MediaKind.MajorStream && o.Partner == deal.Partner);
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
            courted = house.Offers.Any(o => o.Media == MediaKind.MajorStream) || house.MediaDeals.Any(d => d.Kind == MediaKind.MajorStream);
        }
        Assert.True(courted, "no giant called on the house in two years");
    }
}
