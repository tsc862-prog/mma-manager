using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Finance;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Tests;

/// <summary>
/// A media offer past its <see cref="DealOffer.ExpiresOn"/> is dead even before the month turn's prune takes it off
/// the table: it cannot be signed (Accept returns null and changes nothing) and the pending view leaves it out. It is
/// live through its expiry day itself, the same rule the prune has always used.
/// </summary>
public class MediaOfferExpiryTests
{
    private static World Build(ulong seed = 5) => new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });

    /// <summary>The player on an AI house with its own platform and no streaming deal: the case where signing closes something.</summary>
    private static Promotion PlatformHouse(World world)
    {
        var p = world.Promotions.Values
            .Where(x => !x.Defunct && x.Id != world.PlayerPromotion && x.Stream is not null
                && MediaService.DealIn(x, MediaLane.Streaming) is null)
            .OrderBy(x => x.Id.Value).First();
        world.PlayerPromotion = p.Id;
        p.Offers.Clear();
        return p;
    }

    private static DealOffer Offer(string partner, DateOnly offered, int days, MediaKind kind = MediaKind.MajorStream) => new()
    {
        Media = kind,
        Partner = partner,
        FeePerEvent = new Money(1_000_000m),
        Term = 24,
        Requirements = new List<DealRequirement>(),
        OfferedOn = offered,
        ExpiresOn = offered.AddDays(days),
    };

    [Fact]
    public void ALapsedOffer_CannotBeSigned_AndNothingChanges()
    {
        var world = Build();
        var house = PlatformHouse(world);
        var offer = Offer("Arena+", world.CurrentDate.AddDays(-100), 90);   // lapsed ten days ago, month not yet turned
        house.Offers.Add(offer);
        var stream = house.Stream;
        int deals = house.MediaDeals.Count;
        var elsewhere = world.Promotions.Values.Where(p => p.Id != house.Id).Sum(p => p.Offers.Count);

        Assert.Null(MediaService.Accept(world, house, offer, world.CurrentDate));

        Assert.Contains(offer, house.Offers);                        // left for the prune, not half-signed
        Assert.Equal(deals, house.MediaDeals.Count);
        Assert.Same(stream, house.Stream);                           // the platform it would have closed keeps running
        Assert.Equal(elsewhere, world.Promotions.Values.Where(p => p.Id != house.Id).Sum(p => p.Offers.Count));
    }

    [Fact]
    public void AnOffer_IsLiveThroughItsExpiryDay()
    {
        var world = Build();
        var house = PlatformHouse(world);
        var offer = Offer("Arena+", world.CurrentDate.AddDays(-90), 90);   // expires today
        house.Offers.Add(offer);

        Assert.True(MediaService.IsLive(offer, world.CurrentDate));
        Assert.False(MediaService.IsLive(offer, world.CurrentDate.AddDays(1)));
        var deal = MediaService.Accept(world, house, offer, world.CurrentDate);
        Assert.NotNull(deal);
        Assert.Equal("Arena+", deal!.Partner);
        Assert.DoesNotContain(offer, house.Offers);
    }

    [Fact]
    public void PendingOffers_LeavesOutLapsedAndSponsorOffers()
    {
        var world = Build();
        var house = PlatformHouse(world);
        var today = world.CurrentDate;
        var live = Offer("Arena+", today.AddDays(-10), 90);
        var lapsed = Offer("Streamflix", today.AddDays(-95), 90);
        var sponsor = new DealOffer
        {
            Partner = "Some Brand", FeePerEvent = new Money(10_000m), Term = 4,
            Requirements = new List<DealRequirement>(), OfferedOn = today, ExpiresOn = today.AddDays(60),
        };
        house.Offers.AddRange(new[] { lapsed, live, sponsor });

        Assert.Equal(new[] { live }, MediaService.PendingOffers(house, today).ToArray());
        Assert.Equal(new[] { lapsed, live }, MediaService.PendingOffers(house, today.AddDays(-6)).ToArray());   // both open then
    }

    [Fact]
    public void TheMonthlyPrune_StillDropsLapsedOffers_AndKeepsOnesDueToday()
    {
        var world = Build();
        var house = PlatformHouse(world);
        var today = world.CurrentDate;
        var lapsed = Offer("Streamflix", today.AddDays(-91), 90);
        var dueToday = Offer("Arena+", today.AddDays(-90), 90);
        house.Offers.AddRange(new[] { lapsed, dueToday });

        MediaService.RunMonthly(world, house, today, new SimRandom(3));

        Assert.DoesNotContain(lapsed, house.Offers);
        Assert.Contains(dueToday, house.Offers);   // the player's house: the sim never answers for him
    }
}
