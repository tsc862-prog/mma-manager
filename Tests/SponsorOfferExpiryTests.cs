using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Finance;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Tests;

/// <summary>
/// A sponsor offer past its <see cref="DealOffer.ExpiresOn"/> is dead even before the month turn's prune takes it off
/// the table: it cannot be signed (Accept returns null and changes nothing), the pending view leaves it out, and the
/// AI's accept loop passes over it instead of spinning on it. It is live through its expiry day itself, the same rule
/// the prune has always used (the media side's <see cref="MediaOfferExpiryTests"/>).
/// </summary>
public class SponsorOfferExpiryTests
{
    private static World Build(ulong seed = 5) => new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });

    /// <summary>An AI house with its sponsor canvas emptied and its offer table cleared.</summary>
    private static Promotion House(World world, bool player)
    {
        var p = world.Promotions.Values
            .Where(x => !x.Defunct && x.Id != world.PlayerPromotion)
            .OrderBy(x => x.Id.Value).First();
        if (player) world.PlayerPromotion = p.Id;
        p.Offers.Clear();
        p.SponsorDeals.Clear();
        return p;
    }

    private static DealOffer Offer(string partner, DateOnly offered, int days, decimal fee = 10_000m) => new()
    {
        Partner = partner,
        FeePerEvent = new Money(fee),
        Term = 6,
        Requirements = new List<DealRequirement>(),
        OfferedOn = offered,
        ExpiresOn = offered.AddDays(days),
    };

    [Fact]
    public void ALapsedOffer_CannotBeSigned_AndNothingChanges()
    {
        var world = Build();
        var house = House(world, player: true);
        var offer = Offer("Falcon Lager", world.CurrentDate.AddDays(-70), 60);   // lapsed ten days ago, month not yet turned
        house.Offers.Add(offer);
        var balance = house.Balance;

        Assert.Null(Sponsorships.Accept(world, house, offer, world.CurrentDate));

        Assert.Contains(offer, house.Offers);   // left for the prune, not half-signed
        Assert.Empty(house.SponsorDeals);
        Assert.Equal(balance, house.Balance);
    }

    [Fact]
    public void AnOffer_IsLiveThroughItsExpiryDay()
    {
        var world = Build();
        var house = House(world, player: true);
        var offer = Offer("Falcon Lager", world.CurrentDate.AddDays(-60), 60);   // expires today
        house.Offers.Add(offer);

        Assert.Equal(new[] { offer }, Sponsorships.PendingOffers(house, world.CurrentDate).ToArray());
        Assert.Empty(Sponsorships.PendingOffers(house, world.CurrentDate.AddDays(1)));
        var deal = Sponsorships.Accept(world, house, offer, world.CurrentDate);
        Assert.NotNull(deal);
        Assert.Equal("Falcon Lager", deal!.Partner);
        Assert.Equal(6, deal.EventsRemaining);
        Assert.DoesNotContain(offer, house.Offers);
        Assert.Contains(deal, house.SponsorDeals);
    }

    [Fact]
    public void PendingOffers_LeavesOutLapsedAndMediaOffers()
    {
        var world = Build();
        var house = House(world, player: true);
        var today = world.CurrentDate;
        var live = Offer("Falcon Lager", today.AddDays(-10), 60);
        var lapsed = Offer("Apex Fuel", today.AddDays(-65), 60);
        var media = new DealOffer
        {
            Media = MediaKind.MajorStream, Partner = "Arena+", FeePerEvent = new Money(1_000_000m), Term = 24,
            Requirements = new List<DealRequirement>(), OfferedOn = today, ExpiresOn = today.AddDays(90),
        };
        house.Offers.AddRange(new[] { lapsed, live, media });

        Assert.Equal(new[] { live }, Sponsorships.PendingOffers(house, today).ToArray());
        Assert.Equal(new[] { lapsed, live }, Sponsorships.PendingOffers(house, today.AddDays(-6)).ToArray());   // both open then
    }

    [Fact]
    public async Task TheAiAcceptLoop_Terminates_WithALapsedOfferOnTheTable()
    {
        var world = Build();
        var house = House(world, player: false);
        var today = world.CurrentDate;
        // The lapsed one pays best: a loop over the raw list would pick it every pass and never fill a slot.
        var lapsed = Offer("Golden Gate Pawn", today.AddDays(-70), 60, fee: 99_000m);
        var live = Offer("Falcon Lager", today.AddDays(-5), 60);
        house.Offers.AddRange(new[] { lapsed, live });

        var run = Task.Run(() => Sponsorships.AiAccept(world, house, today));
        Assert.Same(run, await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(10))));   // else it spun on the lapsed offer
        await run;

        Assert.Equal(new[] { "Falcon Lager" }, house.SponsorDeals.Select(d => d.Partner).ToArray());
        Assert.Equal(new[] { lapsed }, house.Offers.ToArray());   // the lapsed one waits for the prune
    }

    [Fact]
    public void TheMonthlyPrune_StillDropsLapsedOffers_AndKeepsOnesDueToday()
    {
        var world = Build();
        var house = House(world, player: true);
        var today = world.CurrentDate;
        var lapsed = Offer("Apex Fuel", today.AddDays(-61), 60);
        var dueToday = Offer("Falcon Lager", today.AddDays(-60), 60);
        house.Offers.AddRange(new[] { lapsed, dueToday });

        Sponsorships.RunMonthly(world, house, today, new SimRandom(3));

        Assert.DoesNotContain(lapsed, house.Offers);
        Assert.Contains(dueToday, house.Offers);   // the player's house: the sim never answers for him
    }
}
