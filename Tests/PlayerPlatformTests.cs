using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Finance;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Tests;

/// <summary>
/// The player's own streaming platform no longer locks him out of the streaming giants. A giant pitches him on keyed
/// rolls (never the shared stream) once his house clears the rung's ordinary bar; the offer waits on his desk;
/// signing it closes the platform, declining keeps it; and he can shut the platform down himself. The sim never
/// answers for him, and the AI houses' rule (StreamingLaneTests) is untouched.
/// </summary>
public class PlayerPlatformTests
{
    private static readonly string[] Giants = { "Streamflix", "Arena+", "Watchtower", "Nimbus Video" };

    private static World Build(ulong seed = 5) => new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });

    /// <summary>
    /// An AI house on its own platform that clears the giants' bar, handed to the player at CONTINENTAL size (prestige
    /// 60): under the International-only limit the AI court-back keeps, so only the player's rule can bring a call.
    /// </summary>
    private static Promotion PlayersPlatformHouse(World world)
    {
        var p = world.Promotions.Values
            .Where(x => !x.Defunct && x.Id != world.PlayerPromotion && x.Stream is not null
                && MediaService.DealIn(x, MediaLane.Streaming) is null)
            .OrderBy(x => x.Id.Value).First();
        p.PrestigeHundredths = 6_000;
        world.PlayerPromotion = p.Id;
        Assert.True(MediaService.LadderView(world, p).First(r => r.Kind == MediaKind.MajorStream).Qualified);
        Assert.True(MediaService.PlayerOnOwnPlatform(world, p));
        Assert.False(MediaService.CourtableOnPlatform(world, p, world.CurrentDate));   // the AI rule never reaches him
        p.Offers.Clear();
        p.CourtedBy.Clear();
        return p;
    }

    /// <summary>Pin the house's other lanes so the monthly market makes no shared draw for them.</summary>
    private static void QuietOtherLanes(Promotion p)
    {
        p.MediaDeals.Clear();
        p.MediaDeals.Add(new MediaDeal { Kind = MediaKind.PrimeBroadcast, Partner = "NBS", RightsFeePerEvent = new Money(50_000_000m), MonthsRemaining = 99, SignedOn = new DateOnly(2026, 1, 1) });
        p.MediaDeals.Add(new MediaDeal { Kind = MediaKind.PpvCarrier, Partner = "MainCard", MonthsRemaining = 99, SignedOn = new DateOnly(2026, 1, 1) });
    }

    private static DealOffer GiantOffer(World world, string partner, decimal fee, params DealRequirement[] terms) => new()
    {
        Media = MediaKind.MajorStream,
        Partner = partner,
        FeePerEvent = new Money(fee),
        Term = 24,
        Requirements = terms.ToList(),
        OfferedOn = world.CurrentDate,
        ExpiresOn = world.CurrentDate.AddDays(90),
    };

    [Fact]
    public void AGiant_PitchesThePlayerOnHisPlatform_OnKeyedRolls_WithNoSharedDraw()
    {
        var world = Build();
        var house = PlayersPlatformHouse(world);
        QuietOtherLanes(house);
        DealOffer? pitch = null;
        for (int m = 0; m < 24 && pitch is null; m++)
        {
            var used = new SimRandom(77);
            var twin = new SimRandom(77);
            MediaService.RunMonthly(world, house, world.CurrentDate.AddMonths(m), used);
            Assert.True(twin.NextULong() == used.NextULong(), $"month {m} drew on the shared stream");
            pitch = house.Offers.FirstOrDefault(o => o.Media == MediaKind.MajorStream);
        }
        Assert.NotNull(pitch);
        Assert.Contains(pitch!.Partner, Giants);
        Assert.Contains(pitch.Partner, house.CourtedBy);
        Assert.NotNull(house.Stream);                                    // nothing signed or closed for him
        Assert.DoesNotContain(house.MediaDeals, d => d.Kind == MediaKind.MajorStream);
        Assert.Contains(world.Mail, m => m.Subject.Contains(pitch.Partner) && m.Body.Contains("Signing closes")
            && m.Body.Contains("the deal pays about"));
    }

    [Fact]
    public void TheCalls_AreTheSameForTheSameSeedAndDay()
    {
        string? First(World w, Promotion h)
        {
            for (int m = 0; m < 24; m++)
            {
                MediaService.RunMonthly(w, h, w.CurrentDate.AddMonths(m), new SimRandom(1));
                if (h.Offers.FirstOrDefault(o => o.Media == MediaKind.MajorStream) is { } o) return $"{m}:{o.Partner}";
            }
            return null;
        }
        var a = Build(); var ha = PlayersPlatformHouse(a); QuietOtherLanes(ha);
        var b = Build(); var hb = PlayersPlatformHouse(b); QuietOtherLanes(hb);
        var first = First(a, ha);
        Assert.NotNull(first);
        Assert.Equal(first, First(b, hb));
    }

    [Fact]
    public void ThePlayer_IsPitchedEvenForTheLastFreeGiant()
    {
        // The AI court-back holds the last free giant back for open lanes; the player's call does not.
        var world = Build();
        var house = PlayersPlatformHouse(world);
        QuietOtherLanes(house);
        var free = Giants.Where(g => MediaService.PartnerFree(world, g)).ToList();
        var others = world.Promotions.Values.Where(p => !p.Defunct && p.Id != house.Id).OrderBy(p => p.Id.Value).ToList();
        for (int i = 0; i < free.Count - 1; i++)
            others[i].MediaDeals.Add(new MediaDeal { Kind = MediaKind.MajorStream, Partner = free[i], MonthsRemaining = 99, SignedOn = world.CurrentDate });
        string last = free[^1];
        for (int m = 0; m < 24 && !house.Offers.Any(o => o.Media == MediaKind.MajorStream); m++)
            MediaService.RunMonthly(world, house, world.CurrentDate.AddMonths(m), new SimRandom(3));
        Assert.Contains(house.Offers, o => o.Media == MediaKind.MajorStream && o.Partner == last);
    }

    [Fact]
    public void BelowTheRungsBar_HeIsNeverPitched()
    {
        var world = Build();
        var house = PlayersPlatformHouse(world);
        QuietOtherLanes(house);
        house.PrestigeHundredths = 4_500;                               // under the giants' prestige 50
        for (int m = 0; m < 24; m++)
            MediaService.RunMonthly(world, house, world.CurrentDate.AddMonths(m), new SimRandom(3));
        Assert.DoesNotContain(house.Offers, o => o.Media == MediaKind.MajorStream);
        Assert.NotNull(house.Stream);
    }

    [Fact]
    public void Accepting_ClosesThePlatform_AndSignsTheDeal()
    {
        var world = Build();
        var house = PlayersPlatformHouse(world);
        string platform = house.Stream!.Name;
        var offer = GiantOffer(world, "Arena+", 1_000_000m);
        house.Offers.Add(offer);

        var trade = MediaService.TradeFor(world, house, offer);
        Assert.NotNull(trade);
        Assert.Equal(platform, trade!.Value.Platform);
        Assert.Equal(house.Stream.Subscribers, trade.Value.Subscribers);
        Assert.Equal(MediaService.PlatformPerQuarter(house), trade.Value.PlatformPerQuarter);
        Assert.Equal(MediaService.OfferPerQuarter(world, house, offer), trade.Value.OfferPerQuarter);
        Assert.Equal(trade.Value.OfferPerQuarter - trade.Value.PlatformPerQuarter, trade.Value.GainPerQuarter);

        var deal = MediaService.Accept(world, house, offer, world.CurrentDate);

        Assert.Null(house.Stream);
        Assert.Equal("Arena+", deal.Partner);
        Assert.Same(deal, Assert.Single(house.MediaDeals, d => d.Kind == MediaKind.MajorStream));
        Assert.Single(world.News, n => n.Headline == $"{house.Abbreviation} shuts down {platform}");
        Assert.False(MediaService.PlayerOnOwnPlatform(world, house));

        // The deal holds the lane: no platform comes back on its own, and none can be launched over it.
        house.Balance = new Money(50_000_000m);
        for (int m = 1; m <= 3; m++)
            MediaService.RunMonthly(world, house, world.CurrentDate.AddMonths(m), new SimRandom(3));
        Assert.Null(house.Stream);
        Assert.False(MediaService.LaunchStream(world, house, world.CurrentDate));
    }

    [Fact]
    public void Declining_KeepsThePlatform_AndThePartnerHasHadItsPitch()
    {
        var world = Build();
        var house = PlayersPlatformHouse(world);
        QuietOtherLanes(house);
        DealOffer? pitch = null;
        for (int m = 0; m < 24 && pitch is null; m++)
        {
            MediaService.RunMonthly(world, house, world.CurrentDate.AddMonths(m), new SimRandom(3));
            pitch = house.Offers.FirstOrDefault(o => o.Media == MediaKind.MajorStream);
        }
        Assert.NotNull(pitch);
        var stream = house.Stream;

        MediaService.Decline(house, pitch!);

        Assert.DoesNotContain(pitch, house.Offers);
        Assert.Same(stream, house.Stream);
        for (int m = 0; m < 24; m++)
            MediaService.RunMonthly(world, house, world.CurrentDate.AddMonths(30 + m), new SimRandom(3));
        Assert.DoesNotContain(house.Offers, o => o.Partner == pitch!.Partner);   // it asked once
        Assert.NotNull(house.Stream);
    }

    [Fact]
    public void AnOfferOnHisDesk_IsNeverSignedForHim()
    {
        var world = Build();
        var house = PlayersPlatformHouse(world);
        house.Stream!.Subscribers = 0;                                  // a platform losing money: an AI house would sign
        foreach (var g in Giants) house.CourtedBy.Add(g);
        var offer = GiantOffer(world, "Arena+", 5_000_000m);
        house.Offers.Add(offer);
        for (int m = 0; m < 2; m++)
            MediaService.RunMonthly(world, house, world.CurrentDate.AddMonths(m), new SimRandom(3));
        Assert.Contains(offer, house.Offers);
        Assert.NotNull(house.Stream);
        Assert.DoesNotContain(house.MediaDeals, d => d.Kind == MediaKind.MajorStream);
    }

    [Fact]
    public void TheTrade_SaysWhenHisRecordWouldMissTheTerms()
    {
        var world = Build();
        var house = PlayersPlatformHouse(world);
        var easy = MediaService.TradeFor(world, house, GiantOffer(world, "Arena+", 1_000_000m))!.Value;
        var nobody = MediaService.TradeFor(world, house, GiantOffer(world, "Arena+", 1_000_000m,
            new DealRequirement(DealRequirementKind.MinStarPower, 101)))!.Value;
        Assert.True(easy.RecordKeepsTerms);
        Assert.False(nobody.RecordKeepsTerms);
        Assert.Equal(0, nobody.Record.Cleared);
        Assert.Equal(easy.OfferPerQuarter / 2, nobody.OfferPerQuarter);  // every card at half pay
    }

    [Fact]
    public void ShuttingDown_LosesTheSubscribers_StopsTheCosts_AndARelaunchStartsFromNone()
    {
        var world = Build();
        var house = PlayersPlatformHouse(world);
        house.Stream!.Subscribers = 40_000;
        string platform = house.Stream.Name;

        var preview = MediaService.PreviewShutdown(house);
        Assert.NotNull(preview);
        var p = preview!.Value;
        Assert.Equal(platform, p.Platform);
        Assert.Equal(40_000, p.Subscribers);
        Assert.Equal(MediaService.PlatformPerQuarter(house), p.NetPerQuarter);
        Assert.Equal((p.IncomePerMonth.Amount - p.OpsPerMonth.Amount) * 3m, p.NetPerQuarter);
        Assert.Equal(MediaService.StreamLaunchCost, p.RelaunchCost);
        Assert.Equal(house.MediaDeals.Count > 0, p.OtherScreen);
        Assert.Equal(MediaService.Exposure(house), p.ExposureNow);
        Assert.True(p.ExposureAfter <= p.ExposureNow);

        Assert.True(MediaService.ShutDownPlatform(world, house, world.CurrentDate));
        Assert.Null(house.Stream);
        Assert.Contains(world.News, n => n.Headline == $"{house.Abbreviation} shuts down {platform}");
        Assert.Equal(p.ExposureAfter, MediaService.Exposure(house));
        Assert.Equal(p.BrandAfter, MediaService.BrandFactor(house));
        Assert.Null(MediaService.PreviewShutdown(house));
        Assert.False(MediaService.ShutDownPlatform(world, house, world.CurrentDate));

        // The running costs stop: a month with no platform books no streaming line.
        int lines = house.Ledger.Count(e => e.Category is FinanceCategory.Streaming or FinanceCategory.StreamingOps);
        house.Offers.Clear();
        foreach (var g in Giants) house.CourtedBy.Add(g);
        MediaService.RunMonthly(world, house, world.CurrentDate.AddMonths(1), new SimRandom(3));
        Assert.Equal(lines, house.Ledger.Count(e => e.Category is FinanceCategory.Streaming or FinanceCategory.StreamingOps));

        // A relaunch is a new platform: the launch cost again, and no subscribers.
        house.Balance = new Money(10_000_000m);
        Assert.True(MediaService.LaunchStream(world, house, world.CurrentDate));
        Assert.Equal(0, house.Stream!.Subscribers);
    }

    [Fact]
    public void WithoutAPlatform_NothingChanges()
    {
        var world = Build();
        var house = PlayersPlatformHouse(world);
        house.Stream = null;
        Assert.False(MediaService.PlayerOnOwnPlatform(world, house));
        Assert.Null(MediaService.PreviewShutdown(house));
        var offer = GiantOffer(world, "Arena+", 1_000_000m);
        Assert.Null(MediaService.TradeFor(world, house, offer));
        house.Offers.Add(offer);
        int news = world.News.Count(n => n.Headline.Contains("shuts down"));
        MediaService.Accept(world, house, offer, world.CurrentDate);
        Assert.Equal(news, world.News.Count(n => n.Headline.Contains("shuts down")));
        Assert.Single(house.MediaDeals, d => d.Kind == MediaKind.MajorStream);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AGiantSigningElsewhere_TellsThePlayerItsOfferIsOffTheTable(bool onPlatform)
    {
        var world = Build();
        var house = PlayersPlatformHouse(world);
        if (!onPlatform) house.Stream = null;
        var mine = GiantOffer(world, "Arena+", 1_000_000m);
        house.Offers.Add(mine);
        var rival = world.Promotions.Values
            .Where(p => !p.Defunct && p.Id != house.Id && p.Stream is null && MediaService.DealIn(p, MediaLane.Streaming) is null)
            .OrderBy(p => p.Id.Value).First();
        var theirs = GiantOffer(world, "Arena+", 1_000_000m);
        rival.Offers.Add(theirs);
        int mail = world.Mail.Count;

        MediaService.Accept(world, rival, theirs, world.CurrentDate);

        Assert.DoesNotContain(mine, house.Offers);
        var note = Assert.Single(world.Mail.Skip(mail), m => m.Subject == $"Arena+ signs with {rival.Abbreviation}");
        Assert.Contains("their offer is off the table", note.Body);
        Assert.Equal(onPlatform, note.Body.Contains("keeps running"));
        Assert.Equal(onPlatform, house.Stream is not null);
    }

    [Fact]
    public void AGiantSigningElsewhere_SendsNoMailWhenThePlayerHadNoOffer()
    {
        var world = Build();
        var house = PlayersPlatformHouse(world);
        var rival = world.Promotions.Values
            .Where(p => !p.Defunct && p.Id != house.Id && p.Stream is null && MediaService.DealIn(p, MediaLane.Streaming) is null)
            .OrderBy(p => p.Id.Value).First();
        var theirs = GiantOffer(world, "Arena+", 1_000_000m);
        rival.Offers.Add(theirs);
        MediaService.Accept(world, rival, theirs, world.CurrentDate);
        Assert.DoesNotContain(world.Mail, m => m.Subject == $"Arena+ signs with {rival.Abbreviation}");
    }

    [Fact]
    public void WithNoCardInAYear_ThePitchSaysThereIsNoRecordToJudge()
    {
        var world = Build();
        var house = PlayersPlatformHouse(world);
        QuietOtherLanes(house);
        world.CurrentDate = world.CurrentDate.AddYears(2);           // nothing has run since
        DealOffer? pitch = null;
        for (int m = 0; m < 24 && pitch is null; m++)
        {
            world.CurrentDate = world.CurrentDate.AddMonths(1);
            MediaService.RunMonthly(world, house, world.CurrentDate, new SimRandom(3));
            pitch = house.Offers.FirstOrDefault(o => o.Media == MediaKind.MajorStream);
        }
        Assert.NotNull(pitch);
        Assert.Equal(0, MediaService.TradeFor(world, house, pitch!)!.Value.Record.Cards);
        var note = Assert.Single(world.Mail, m => m.Subject.StartsWith($"Media offer: {pitch!.Partner}"));
        Assert.Contains("no recent record to judge", note.Body);
        Assert.DoesNotContain("pays about", note.Body);
    }

    [Fact]
    public void AnAiHouseTakingAGiant_ClosesItsPlatformOnce()
    {
        // The close inside Accept is a no-op for an AI house: its own decision already closed the platform.
        var world = Build();
        var house = world.Promotions.Values
            .Where(x => !x.Defunct && x.Id != world.PlayerPromotion && x.Stream is not null && MediaService.DealIn(x, MediaLane.Streaming) is null)
            .OrderBy(x => x.Id.Value).First();
        house.PrestigeHundredths = Math.Max(house.PrestigeHundredths, 7_200);
        house.Stream!.Subscribers = 1_000;
        string platform = house.Stream.Name;
        foreach (var g in Giants) if (g != "Arena+") house.CourtedBy.Add(g);
        house.Offers.Add(GiantOffer(world, "Arena+", 1_000_000m));
        MediaService.RunMonthly(world, house, world.CurrentDate, new SimRandom(3));
        Assert.Null(house.Stream);
        Assert.Single(world.News, n => n.Headline == $"{house.Abbreviation} shuts down {platform}");
    }
}
