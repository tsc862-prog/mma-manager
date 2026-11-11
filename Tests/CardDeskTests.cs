using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Scouting;

namespace MmaManager.Tests;

/// <summary>
/// The Card Desk's sim surface (<see cref="PlayerBooking"/>): order and split,
/// slot-keeping replacements, the room, the filler preview, the all-in
/// projection and fogged suggestions. Everything read-only must leave the world
/// untouched; everything that advises the player must read through the scouts.
/// </summary>
public class CardDeskTests
{
    private static World Build(ulong seed) => new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });

    // A division of the player's with at least `need` fighters ready by the date.
    private static List<Fighter> ReadyPool(World world, Event ev, int need)
    {
        var promo = world.Get(world.PlayerPromotion)!;
        foreach (var d in promo.Divisions.OrderBy(d => d.Sex).ThenBy(d => d.WeightClass))
        {
            var pool = world.RosterOf(promo.Id)
                .Where(f => d.Matches(f) && f.IsMatchReady(ev.Date))
                .OrderBy(f => f.Id.Value).ToList();
            if (pool.Count >= need) return pool;
        }
        throw new InvalidOperationException("no division deep enough");
    }

    // Book `bouts` non-title bouts from one deep division.
    private static List<Fighter> BookCard(World world, PlayerBooking booking, Event ev, int bouts)
    {
        var pool = ReadyPool(world, ev, bouts * 2 + 2);
        for (int i = 0; i < bouts; i++)
            Assert.True(booking.AddBout(world, ev, pool[2 * i].Id, pool[2 * i + 1].Id, 3, false, out var err), err);
        return pool;
    }

    // --- 1. Fog -------------------------------------------------------------

    [Fact]
    public void Appraise_SameScoutedRead_SameAppraisal_WhateverTheTruth()
    {
        var world = Build(11);
        var advisor = new MatchmakingAdvisor();
        var promo = world.Get(world.PlayerPromotion)!;
        var div = promo.Divisions.First(d => world.RosterOf(promo.Id).Count(d.Matches) >= 2);
        var red = world.RosterOf(promo.Id).First(div.Matches);
        int Read(Fighter f) => ScoutingService.PerceivedAbility(world, promo, f);

        // Two unranked fighters in red's division whom our scouts read the same
        // but who are NOT the same fighter underneath.
        var unranked = world.Fighters.Values
            .Where(f => f.Id != red.Id && div.Matches(f) && !div.Rankings.Contains(f.Id) && !f.LocalTalent
                        && (div.Title is not { } t || world.Get(t)?.CurrentChampion != f.Id))
            .OrderBy(f => f.Id.Value).ToList();
        var pair = unranked
            .SelectMany(x => unranked.Where(y => y.Id.Value > x.Id.Value && Read(y) == Read(x)
                && FighterAbility.CurrentAbility(y) != FighterAbility.CurrentAbility(x)).Select(y => (x, y)))
            .First();
        pair.y.Intangibles.Popularity = pair.x.Intangibles.Popularity;   // the public facts match too

        var a = advisor.Appraise(world, red.Id, pair.x.Id);
        var b = advisor.Appraise(world, red.Id, pair.y.Id);
        Assert.Equal(a.Competitiveness, b.Competitiveness);
        Assert.Equal(a.Appeal, b.Appeal);
        Assert.Equal(a.Stakes, b.Stakes);
        Assert.Equal(a.Summary, b.Summary);
    }

    [Fact]
    public void Appraise_FollowsTheReadNotTheTruth()
    {
        var world = Build(11);
        var promo = world.Get(world.PlayerPromotion)!;
        var f = world.Fighters.Values.First(x => !x.LocalTalent && x.Record.TotalBouts < 5 && x.Contract?.Promotion != promo.Id
                                                 && ScoutingService.PerceivedAbility(world, promo, x) != FighterAbility.CurrentAbility(x));
        Assert.Equal(ScoutingService.PerceivedAbility(world, promo, f), MatchmakingAdvisor.ScoutedRead(world, f));
    }

    [Fact]
    public void OneToWatch_DoesNotReadTruePotential()
    {
        var world = Build(11);
        var promo = world.Get(world.PlayerPromotion)!;
        var date = world.CurrentDate;
        // Make a young riser our scouts rate, and a week worth printing.
        var riser = world.Fighters.Values
            .Where(f => f.Status == FighterStatus.Active && !f.LocalTalent && f.AgeOn(date) <= 24)
            .OrderByDescending(f => ScoutingService.PerceivedAbility(world, promo, f) + 2 * (25 - f.AgeOn(date)))
            .ThenBy(f => f.Id.Value).First();
        Assert.True(ScoutingService.PerceivedAbility(world, promo, riser) + 2 * (25 - riser.AgeOn(date)) >= 64);
        riser.Record.CurrentStreak = 5;
        News.Post(world, date, NewsCategory.Signing, "Story one", "x", promo.Id);
        News.Post(world, date, NewsCategory.Signing, "Story two", "x", promo.Id);

        string? Watch()
        {
            world.Mail.RemoveAll(m => m.From == "MMA Weekly");
            Digest.Weekly(world, date);
            return world.Mail.Single(m => m.From == "MMA Weekly").Body
                .Split('\n').FirstOrDefault(l => l.StartsWith("ONE TO WATCH"));
        }

        var before = Watch();
        Assert.NotNull(before);
        foreach (var f in world.Fighters.Values) f.PotentialAbility = 1;   // nobody can see this
        Assert.Equal(before, Watch());
    }

    // --- 2. Order and split ------------------------------------------------

    [Fact]
    public void MoveBout_ReordersTheCard_LastIsTheMainEvent()
    {
        var world = Build(11);
        var booking = new PlayerBooking();
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(60));
        BookCard(world, booking, ev, 3);
        var first = ev.Card[0].Id;

        Assert.True(booking.MoveBout(world, ev, first, ev.Card.Count - 1, out var err), err);
        Assert.Equal(first, ev.Card[^1].Id);
        Assert.Equal(3, ev.Card.Count);

        Assert.True(booking.MoveBout(world, ev, 2, 0, out err), err);   // by index, back down
        Assert.Equal(first, ev.Card[0].Id);

        Assert.False(booking.MoveBout(world, ev, first, 3, out err));
        Assert.NotNull(err);
        Assert.False(booking.MoveBout(world, ev, new FightId(-5), 0, out err));
        Assert.NotNull(err);
    }

    [Fact]
    public void CardEdits_RefuseCardsThatArentYoursOrAreClosed()
    {
        var world = Build(11);
        var booking = new PlayerBooking();
        var theirs = world.Events.Values.First(e => e.Promotion != world.PlayerPromotion && e.Card.Count > 1);
        Assert.False(booking.MoveBout(world, theirs, theirs.Card[0].Id, 1, out var err));
        Assert.NotNull(err);
        Assert.False(booking.SetMainCardSize(world, theirs, 4, out err));

        var mine = booking.CreateEvent(world, world.CurrentDate.AddDays(60));
        BookCard(world, booking, mine, 2);
        mine.Status = EventStatus.Completed;
        Assert.False(booking.MoveBout(world, mine, 0, 1, out err));
        Assert.False(booking.SetMainCardSize(world, mine, 2, out err));
    }

    [Fact]
    public void SetMainCardSize_ValidatesAndFeedsTheSplit()
    {
        var world = Build(11);
        var booking = new PlayerBooking();
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(60));
        BookCard(world, booking, ev, 6);

        Assert.True(booking.SetMainCardSize(world, ev, 4, out var err), err);
        Assert.Equal(4, ev.MainCardFights);
        Assert.True(ev.IsPrelim(ev.Card[0]));
        Assert.False(ev.IsPrelim(ev.Card[^1]));
        Assert.Equal(4, booking.MainCardOnTheNight(world, ev));

        Assert.True(booking.SetMainCardSize(world, ev, 0, out err), err);   // auto
        Assert.Equal(0, ev.MainCardFights);

        Assert.False(booking.SetMainCardSize(world, ev, -1, out err));
        Assert.False(booking.SetMainCardSize(world, ev, PlayerBooking.MaxMainCardSize + 1, out err));
        Assert.Equal(0, ev.MainCardFights);
    }

    // --- 6. Replacements keep the slot --------------------------------------

    [Fact]
    public void ReplaceFighter_KeepsSlotIdAndRounds()
    {
        var world = Build(11);
        var booking = new PlayerBooking();
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(60));
        var pool = BookCard(world, booking, ev, 3);
        var target = ev.Card[1];
        target.ScheduledRounds = 5;
        var spare = pool[^1];

        Assert.True(booking.ReplaceFighter(world, ev, target.Id, target.BlueCorner, spare.Id, out var err), err);
        var now = ev.Card[1];
        Assert.Equal(target.Id, now.Id);
        Assert.Equal(target.RedCorner, now.RedCorner);
        Assert.Equal(spare.Id, now.BlueCorner);
        Assert.Equal(5, now.ScheduledRounds);
        Assert.Equal(3, ev.Card.Count);

        // Refusals: already on the card, someone not in the bout, the other corner.
        Assert.False(booking.ReplaceFighter(world, ev, now.Id, now.BlueCorner, ev.Card[0].RedCorner, out err));
        Assert.False(booking.ReplaceFighter(world, ev, now.Id, ev.Card[0].RedCorner, pool[^2].Id, out err));
        Assert.False(booking.ReplaceFighter(world, ev, now.Id, now.BlueCorner, now.RedCorner, out err));
        // Wrong weight class.
        var other = world.RosterOf(world.PlayerPromotion).First(f => f.WeightClass != now.WeightClass && f.IsMatchReady(ev.Date));
        Assert.False(booking.ReplaceFighter(world, ev, now.Id, now.BlueCorner, other.Id, out err));
        // Not ours.
        var outsider = world.Fighters.Values.First(f => f.Contract is { } c && c.Promotion != world.PlayerPromotion
                                                        && f.WeightClass == now.WeightClass && f.Sex == spare.Sex);
        Assert.False(booking.ReplaceFighter(world, ev, now.Id, now.BlueCorner, outsider.Id, out err));
        Assert.Equal(spare.Id, ev.Card[1].BlueCorner);
    }

    [Fact]
    public void ReplaceFighter_KeepsTheBeltOnlyWhileTheChampionIsIn()
    {
        var world = Build(11);
        var booking = new PlayerBooking();
        var promo = world.Get(world.PlayerPromotion)!;
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(90));
        var (div, champ) = promo.Divisions
            .Select(d => (d, c: d.Title is { } t && world.Get(t)?.CurrentChampion is { } cid ? world.Get(cid) : null))
            .First(x => x.c is not null && x.c.IsMatchReady(ev.Date) && x.c.Contract?.Promotion == promo.Id
                        && world.RosterOf(promo.Id).Count(f => x.d.Matches(f) && f.IsMatchReady(ev.Date)) >= 4);
        // Ranked challengers first, so the first swap is to a ranked one.
        var others = world.RosterOf(promo.Id)
            .Where(f => div.Matches(f) && f.Id != champ!.Id && f.IsMatchReady(ev.Date))
            .OrderBy(f => div.Rankings.Contains(f.Id) ? 0 : 1).ThenBy(f => f.Id.Value).ToList();
        Assert.True(div.Rankings.Contains(others[1].Id), "need two ranked challengers");

        Assert.True(booking.AddBout(world, ev, champ!.Id, others[0].Id, 5, true, out var err), err);
        var bout = ev.Card[0];

        Assert.True(booking.ReplaceFighter(world, ev, bout.Id, others[0].Id, others[1].Id, out err, out bool kept, out var note), err);
        Assert.True(kept);
        Assert.Null(note);   // a ranked challenger: nothing to say
        Assert.True(ev.Card[0].IsTitleFight);
        Assert.Equal(div.Title, ev.Card[0].Title);

        // An unranked challenger keeps the belt on (as AddBout allows) but the desk says so.
        if (others.FirstOrDefault(f => !div.Rankings.Contains(f.Id)) is { } unranked)
        {
            Assert.True(booking.ReplaceFighter(world, ev, bout.Id, others[1].Id, unranked.Id, out err, out kept, out note), err);
            Assert.True(kept);
            Assert.Contains("unranked", note);
            Assert.True(booking.ReplaceFighter(world, ev, bout.Id, unranked.Id, others[1].Id, out err), err);
        }

        Assert.True(booking.ReplaceFighter(world, ev, bout.Id, champ.Id, others[2].Id, out err, out kept, out note), err);
        Assert.False(kept);
        Assert.Contains("comes off", note);
        Assert.False(ev.Card[0].IsTitleFight);
        Assert.Null(ev.Card[0].Title);
        Assert.Equal(5, ev.Card[0].ScheduledRounds);
    }

    // --- 4. The room -----------------------------------------------------------

    [Fact]
    public void VenueOptions_ListTheCitysRooms_WithTheSimsPickMarked()
    {
        var world = Build(11);
        var booking = new PlayerBooking();
        var promo = world.Get(world.PlayerPromotion)!;
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(60));
        BookCard(world, booking, ev, 2);
        var city = world.Get(ev.VenueId!.Value)!.City;

        var options = booking.VenueOptions(world, ev);
        Assert.Equal(world.Venues.Values.Count(v => v.City == city), options.Count);
        // The sim's pick is the AI calendar's picker: the room closest to this
        // card's demand.
        Assert.Equal(Financials.BestRoom(world, ev, world.Venues.Values.Where(v => v.City == city))!.Id,
            Assert.Single(options, o => o.SimPick).Venue);
        var pick = options.Single(o => o.SimPick);
        Assert.All(options, o => Assert.True(Math.Abs(pick.Capacity - pick.Demand) <= Math.Abs(o.Capacity - o.Demand)));
        Assert.Equal(ev.VenueId, Assert.Single(options, o => o.Selected).Venue);
        Assert.All(options, o => Assert.True(o.ProjectedAttendance <= o.Capacity));
        Assert.All(options, o => Assert.Equal(Math.Min(o.Demand, o.Capacity), o.ProjectedAttendance));
        Assert.Single(options.Select(o => o.Demand).Distinct());

        // Pick the biggest room: the card moves there and projects its house.
        var big = options.OrderByDescending(o => o.Capacity).First();
        Assert.True(booking.SetVenue(world, ev, big.Venue, out var err), err);
        Assert.Equal(big.Venue, ev.VenueId);
        Assert.Equal(big.Name, ev.Venue);
        Assert.Equal(big.ProjectedAttendance, booking.Project(world, ev).Attendance);

        Assert.False(booking.SetVenue(world, ev, new VenueId(-1), out err));
    }

    [Fact]
    public void SetVenue_RefusesACityBeyondReach()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options
        {
            Seed = 21,
            Founding = new WorldBuilder.FoundingSpec { Prestige = 10, Divisions = new[] { WeightClass.Lightweight } },
        });
        var booking = new PlayerBooking();
        var promo = world.Get(world.PlayerPromotion)!;
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(30));
        var far = world.Venues.Values.OrderBy(v => v.Id.Value)
            .First(v => !MmaManager.Core.Sim.Org.OrgReach.CanBookIn(world, promo, v.City));
        var before = ev.VenueId;
        Assert.False(booking.SetVenue(world, ev, far.Id, out var err));
        Assert.NotNull(err);
        Assert.Equal(before, ev.VenueId);
    }

    // --- 5. Filler preview ----------------------------------------------------

    [Fact]
    public void PreviewPadding_ShowsWhatFightNightAdds_WithoutAddingIt()
    {
        var world = Build(11);
        var booking = new PlayerBooking();
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(60));
        BookCard(world, booking, ev, 2);
        int fighters = world.Fighters.Count;
        var cardBefore = ev.Card.Select(f => f.Id).ToList();

        var preview = booking.PreviewPadding(world, ev);
        Assert.Equal(cardBefore, ev.Card.Select(f => f.Id).ToList());   // nothing added
        Assert.Equal(fighters, world.Fighters.Count);                    // nobody minted
        Assert.Equal(2, preview.BoutsBefore);
        Assert.Equal(CardFiller.MinCardBouts, preview.BoutsAfter);
        Assert.True(preview.WouldPad);
        Assert.Equal(preview.Bouts.Aggregate(Money.Zero, (s, b) => s + b.Cost), preview.Cost);

        // Fight night pads exactly as previewed.
        CardFiller.PadToMinimum(world, ev);
        Assert.Equal(preview.BoutsAfter, ev.Card.Count);
        var added = ev.Card.Where(f => !cardBefore.Contains(f.Id)).Reverse().ToList();   // inserted at the front, newest first
        Assert.Equal(preview.Bouts.Count, added.Count);
        for (int i = 0; i < added.Count; i++)
        {
            if (preview.Bouts[i].LocalFiller) continue;
            Assert.Equal(preview.Bouts[i].Red, added[i].RedCorner);
            Assert.Equal(preview.Bouts[i].Blue, added[i].BlueCorner);
        }
    }

    [Fact]
    public void PreviewPadding_FullCard_AddsNothing()
    {
        var world = Build(11);
        var booking = new PlayerBooking();
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(60));
        BookCard(world, booking, ev, CardFiller.MinCardBouts);
        var preview = booking.PreviewPadding(world, ev);
        Assert.False(preview.WouldPad);
        Assert.Equal(Money.Zero, preview.Cost);
    }

    // --- 3. The all-in projection ----------------------------------------------

    [Fact]
    public void Project_AgreesWithTheBoxOfficeEstimate_AndTouchesNothing()
    {
        var world = Build(11);
        var booking = new PlayerBooking();
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(60));
        BookCard(world, booking, ev, CardFiller.MinCardBouts);   // no padding, so payroll lines up
        int fighters = world.Fighters.Count, events = world.Events.Count;

        var p = booking.Project(world, ev);
        var est = Financials.Estimate(world, ev);
        Assert.Equal(est.Attendance, p.Attendance);
        Assert.Equal(est.GateRevenue, p.Gate);
        Assert.Equal(est.PayPerViewRevenue, p.Ppv);
        Assert.Equal(est.ProductionCost, p.Production + p.Rent);
        Assert.Equal(est.FighterPayroll, p.Purses + p.WinBonusExpected + p.PpvPoints);
        Assert.True(p.WinBonusMin.Amount <= p.WinBonusMax.Amount);
        Assert.True(p.NetLow.Amount <= p.Net.Amount && p.Net.Amount <= p.NetHigh.Amount);
        Assert.Equal(p.Gate + p.Ppv + p.MediaRights + p.Sponsors + p.Merch, p.Income);

        var promo = world.Get(world.PlayerPromotion)!;
        Assert.Equal(promo.SponsorDeals.Count + promo.MediaDeals.Count(d => d.Kind != MediaKind.PpvCarrier || ev.Tier >= EventTier.Ppv),
            p.Partners.Count);

        Assert.Equal(fighters, world.Fighters.Count);
        Assert.Equal(events, world.Events.Count);
        Assert.Equal(p.Net, booking.Project(world, ev).Net);   // deterministic
    }

    [Fact]
    public void Project_FlagsAMissedTerm_HalvesThatFee_AndSaysHowFarOff()
    {
        var world = Build(11);
        var booking = new PlayerBooking();
        var promo = world.Get(world.PlayerPromotion)!;
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(60));
        BookCard(world, booking, ev, 3);
        promo.SponsorDeals.Add(new SponsorDeal
        {
            Partner = "Test Brand",
            FeePerEvent = new Money(10_000m),
            EventsRemaining = 4,
            Strikes = 1,
            Requirements =
            {
                new DealRequirement(DealRequirementKind.TitleFight, 0),
                new DealRequirement(DealRequirementKind.MinAttendance, 9_999_999),
                new DealRequirement(DealRequirementKind.MinCardQuality, 50),
            },
        });

        var p = booking.Project(world, ev);
        var brand = Assert.Single(p.Partners, x => x.Partner == "Test Brand");
        Assert.True(brand.AtRisk);
        Assert.True(brand.WouldWalk);   // strike 2 of 2
        Assert.Equal(brand.FullFee * 0.5m, brand.ExpectedFee);
        Assert.Equal(TermStatus.Missed, brand.Terms.Single(t => t.Requirement.Kind == DealRequirementKind.TitleFight).Status);
        var house = brand.Terms.Single(t => t.Requirement.Kind == DealRequirementKind.MinAttendance);
        Assert.Equal(TermStatus.Missed, house.Status);
        Assert.Contains("short", house.Gap);
        Assert.Equal(TermStatus.JudgedOnTheNight, brand.Terms.Single(t => t.Requirement.Kind == DealRequirementKind.MinCardQuality).Status);
        Assert.Contains(p.Risks, r => r.Kind == BookingRiskKind.PartnerTermMissed && r.Partner == "Test Brand");
    }

    [Fact]
    public void Project_NamesTheRisks()
    {
        var world = Build(11);
        var booking = new PlayerBooking();
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(60));
        BookCard(world, booking, ev, 2);
        var main = ev.Card[^1];
        var lastDeal = world.Get(main.RedCorner)!;
        lastDeal.Contract!.FightsRemaining = 1;
        var hurt = world.Get(main.BlueCorner)!;
        hurt.CurrentInjury = new Injury
        {
            Severity = InjurySeverity.Minor, Description = "a broken hand",
            OccurredOn = world.CurrentDate, ExpectedReturn = ev.Date.AddDays(30),
        };

        // A rival's card two days EARLIER: public, and it clashes. (An AI card
        // announced for a date after yours is listed in RivalCardsNear but not
        // charged: before the forward calendar an AI card didn't exist until
        // its own night, so it never cut a card dated before it.)
        var rival = world.Promotions.Values.First(x => x.Id != world.PlayerPromotion && !x.Defunct);
        var clash = new Event
        {
            Id = world.NewEventId(), Promotion = rival.Id, Name = "Rival Night", Date = ev.Date.AddDays(-2),
            Status = EventStatus.Scheduled,
        };
        world.Events[clash.Id] = clash;
        // And an AI card announced for two days AFTER: listed for the booker,
        // but not charged (and so no "costs 8%" risk is claimed for it).
        var later = new Event
        {
            Id = world.NewEventId(), Promotion = rival.Id, Name = "Rival Night Two", Date = ev.Date.AddDays(2),
            Status = EventStatus.Scheduled,
        };
        world.Events[later.Id] = later;

        var p = booking.Project(world, ev);
        Assert.DoesNotContain(later, p.ClashingCards);
        Assert.DoesNotContain(p.Risks, r => r.Kind == BookingRiskKind.RivalCardClash && r.OtherEvent == later.Id);
        Assert.Contains(p.NearbyRivalCards, r => r.Card.Event.Id == later.Id && !r.Charged);
        Assert.Contains(p.NearbyRivalCards, r => r.Card.Event.Id == clash.Id && r.Charged);
        Assert.Contains(p.Risks, r => r.Kind == BookingRiskKind.LastFightOnDeal && r.Fighter == lastDeal.Id);
        Assert.Contains(p.Risks, r => r.Kind == BookingRiskKind.NotMatchReady && r.Fighter == hurt.Id && r.Text.Contains("broken hand"));
        Assert.Contains(p.Risks, r => r.Kind == BookingRiskKind.RivalCardClash && r.OtherEvent == clash.Id);
        Assert.Contains(clash, p.ClashingCards);
        Assert.True(p.Clash);
        Assert.Contains(p.Risks, r => r.Kind == BookingRiskKind.CardPadded);
        Assert.All(p.Risks, r => Assert.False(string.IsNullOrWhiteSpace(r.Text)));
    }

    [Fact]
    public void Project_EmptyCard_SaysSo()
    {
        var world = Build(11);
        var booking = new PlayerBooking();
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(60));
        var p = booking.Project(world, ev);
        Assert.Contains(p.Risks, r => r.Kind == BookingRiskKind.EmptyCard);
        Assert.Equal(0, p.BoutsOnTheNight);
    }

    // --- 7. Suggestions ---------------------------------------------------------

    [Fact]
    public void Suggest_OffersBookablePairs_WithReasons_Deterministically()
    {
        var world = Build(11);
        var booking = new PlayerBooking();
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(60));
        BookCard(world, booking, ev, 1);

        foreach (BoutSlot slot in Enum.GetValues<BoutSlot>())
        {
            var list = booking.Suggest(world, ev, slot, 8);
            Assert.InRange(list.Count, 1, 8);
            static object Key(BoutProposal p) => (p.Red, p.Blue, p.Score, p.Reason, p.TitleFight, p.Rounds, p.Appraisal.Appeal);
            Assert.Equal(list.Select(Key), booking.Suggest(world, ev, slot, 8).Select(Key));
            Assert.Equal(list.OrderByDescending(p => p.Score).Select(p => p.Score), list.Select(p => p.Score));
            foreach (var p in list)
            {
                Assert.False(string.IsNullOrWhiteSpace(p.Reason));
                var r = world.Get(p.Red)!;
                var b = world.Get(p.Blue)!;
                Assert.Equal(world.PlayerPromotion, r.Contract!.Promotion);
                Assert.Equal(world.PlayerPromotion, b.Contract!.Promotion);
                Assert.Equal(r.WeightClass, b.WeightClass);
                Assert.Equal(r.Sex, b.Sex);
                Assert.True(r.IsMatchReady(ev.Date) && b.IsMatchReady(ev.Date));
                Assert.DoesNotContain(ev.Card, f => f.RedCorner == p.Red || f.BlueCorner == p.Red
                                                    || f.RedCorner == p.Blue || f.BlueCorner == p.Blue);
                if (slot == BoutSlot.Prelim) Assert.False(p.TitleFight);
            }
        }

        // A suggestion books as offered.
        var top = booking.Suggest(world, ev, BoutSlot.MainCard, 1)[0];
        Assert.True(booking.AddBout(world, ev, top.Red, top.Blue, top.Rounds, top.TitleFight, out var err), err);
    }

    [Fact]
    public void Suggest_MainEvent_PutsADueTitleFightOnTop()
    {
        var world = Build(11);
        var booking = new PlayerBooking();
        var promo = world.Get(world.PlayerPromotion)!;
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(90));
        Assert.Contains(promo.Divisions, d => d.Title is { } t && world.Get(t)?.CurrentChampion is { } c
                                              && world.Get(c)!.IsMatchReady(ev.Date) && world.Get(c)!.Contract?.Promotion == promo.Id);

        var main = booking.Suggest(world, ev, BoutSlot.MainEvent, 5);
        Assert.True(main[0].TitleFight, main[0].Reason);
        Assert.Equal(promo.Rules.TitleRounds, main[0].Rounds);
        Assert.Contains("title", main[0].Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(booking.Suggest(world, ev, BoutSlot.Prelim, 50), p => p.TitleFight);
    }

    // --- Review fixes --------------------------------------------------------

    [Fact]
    public void ReadOnlyHelpers_AnswerForYourCardsOnly()
    {
        var world = Build(11);
        var booking = new PlayerBooking();
        // A rival's short card (one bout), so padding would have names to leak.
        var rival = world.Promotions.Values.Where(x => x.Id != world.PlayerPromotion && !x.Defunct)
            .OrderByDescending(x => world.RosterOf(x.Id).Count()).ThenBy(x => x.Id.Value).First();
        var pair = world.RosterOf(rival.Id).GroupBy(f => (f.WeightClass, f.Sex)).First(g => g.Count() >= 2).Take(2).ToList();
        var theirs = new EventService().CreateEvent(world, rival.Id, "Rival Night", world.CurrentDate.AddDays(30),
            new[] { new Matchup(pair[0].Id, pair[1].Id, pair[0].WeightClass, 3, false, null) });
        Assert.True(CardFiller.Preview(world, theirs).WouldPad);   // the sim knows; the player's desk won't say

        var p = booking.Project(world, theirs);
        Assert.Empty(p.Partners);
        Assert.Empty(p.Risks);
        Assert.Equal(Money.Zero, p.Purses);
        Assert.Equal(0, p.Attendance);
        Assert.False(booking.PreviewPadding(world, theirs).WouldPad);
        Assert.Empty(booking.VenueOptions(world, theirs));
        Assert.Equal(0, booking.MainCardOnTheNight(world, theirs));
        Assert.Empty(booking.Suggest(world, theirs, BoutSlot.MainCard, 5));
    }

    [Fact]
    public void Padding_OnYourCard_PicksByTheScoutsRead_AndTheNightMatchesThePreview()
    {
        var world = Build(11);
        var booking = new PlayerBooking();
        var promo = world.Get(world.PlayerPromotion)!;
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(60));
        BookCard(world, booking, ev, 1);
        var preview = booking.PreviewPadding(world, ev);
        var first = preview.Bouts.First(b => !b.LocalFiller);

        // The first padded pair is the top two of its division by OUR read (id breaks ties).
        var r = world.Get(first.Red!.Value)!;
        var group = world.RosterOf(promo.Id)
            .Where(f => f.WeightClass == r.WeightClass && f.Sex == r.Sex && f.IsMatchReady(ev.Date)
                        && !ev.Card.Any(b => b.RedCorner == f.Id || b.BlueCorner == f.Id)
                        // champions only fight for the belt, and nobody committed to
                        // another scheduled card pads this one
                        && !world.Titles.Values.Any(t => t.CurrentChampion == f.Id)
                        && !MmaManager.Core.Sim.Living.ForwardCalendar.IsCommitted(world, f.Id, ev.Id))
            .OrderByDescending(f => ScoutingService.PerceivedAbility(world, promo, f)).ThenBy(f => f.Id.Value)
            .ToList();
        Assert.Equal(new[] { group[0].Id, group[1].Id }, new[] { first.Red!.Value, first.Blue!.Value });

        AssertNightMatchesPreview(world, ev, preview);
    }

    [Fact]
    public void Padding_SkipsYourChampionAndYourNextCardsFighters_InThePreviewAndOnTheNight()
    {
        var world = Build(11);
        var booking = new PlayerBooking();
        var promo = world.Get(world.PlayerPromotion)!;
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(60));
        BookCard(world, booking, ev, 1);

        // Find a division where the top two by OUR read are free to pad, then
        // make one of them the champion and book the other on our NEXT card.
        Fighter[] TopTwo()
        {
            foreach (var d in promo.Divisions.OrderBy(d => d.Sex).ThenBy(d => d.WeightClass))
            {
                var pool = world.RosterOf(promo.Id)
                    .Where(f => d.Matches(f) && f.IsMatchReady(ev.Date) && !ForwardCalendar.IsCommitted(world, f.Id)
                                && !world.Titles.Values.Any(t => t.CurrentChampion == f.Id))
                    .OrderByDescending(f => ScoutingService.PerceivedAbility(world, promo, f)).ThenBy(f => f.Id.Value)
                    .ToList();
                if (pool.Count >= 6) return pool.Take(3).ToArray();
            }
            throw new InvalidOperationException("no division deep enough");
        }
        var top = TopTwo();
        var div = promo.Divisions.First(d => d.Matches(top[0]));
        var belt = world.Get(div.Title!.Value)!;
        belt.CurrentChampion = top[0].Id;
        var next = booking.CreateEvent(world, ev.Date.AddDays(28));
        Assert.True(booking.AddBout(world, next, top[1].Id, top[2].Id, 3, false, out var err), err);

        var preview = booking.PreviewPadding(world, ev);
        var padded = preview.Bouts.Where(b => !b.LocalFiller).SelectMany(b => new[] { b.Red!.Value, b.Blue!.Value }).ToHashSet();
        Assert.DoesNotContain(top[0].Id, padded);   // the champion only fights for the belt
        Assert.DoesNotContain(top[1].Id, padded);   // already committed to the next card
        Assert.DoesNotContain(top[2].Id, padded);
        AssertNightMatchesPreview(world, ev, preview);
    }

    [Fact]
    public void Padding_SmallPromotion_LocalPairs_NightMatchesThePreview()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options
        {
            Seed = 21,
            Founding = new WorldBuilder.FoundingSpec { Prestige = 15, Divisions = new[] { WeightClass.Lightweight, WeightClass.Welterweight } },
        });
        var booking = new PlayerBooking();
        var a = SignFreeAgent(world, f => f.WeightClass == WeightClass.Lightweight && f.Sex == Sex.Male);
        var b = SignFreeAgent(world, f => f.WeightClass == WeightClass.Lightweight && f.Sex == Sex.Male && f.Id != a.Id);
        SignFreeAgent(world, f => f.WeightClass == WeightClass.Welterweight && f.Sex == Sex.Male);
        SignFreeAgent(world, f => f.WeightClass == WeightClass.Welterweight && f.Sex == Sex.Male
                                  && f.Contract?.Promotion != world.PlayerPromotion);
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(30));
        Assert.True(booking.AddBout(world, ev, a.Id, b.Id, 3, false, out var err), err);

        var preview = booking.PreviewPadding(world, ev);
        Assert.True(preview.LocalPairs > 0, "a small org runs out of roster and pads with locals");
        Assert.Contains(preview.Bouts, x => !x.LocalFiller);
        AssertNightMatchesPreview(world, ev, preview);
    }

    private static void AssertNightMatchesPreview(World world, Event ev, FillerPreview preview)
    {
        var before = ev.Card.Select(f => f.Id).ToList();
        CardFiller.PadToMinimum(world, ev);
        Assert.Equal(preview.BoutsAfter, ev.Card.Count);
        var added = ev.Card.Where(f => !before.Contains(f.Id)).Reverse().ToList();   // inserted at the front, newest first
        Assert.Equal(preview.Bouts.Count, added.Count);
        for (int i = 0; i < added.Count; i++)
        {
            var want = preview.Bouts[i];
            Assert.Equal(want.WeightClass, added[i].WeightClass);
            if (want.LocalFiller)
            {
                Assert.True(world.Get(added[i].RedCorner)!.LocalTalent);
                Assert.True(world.Get(added[i].BlueCorner)!.LocalTalent);
            }
            else
            {
                Assert.Equal(want.Red, added[i].RedCorner);
                Assert.Equal(want.Blue, added[i].BlueCorner);
            }
        }
    }

    private static Fighter SignFreeAgent(World world, Func<Fighter, bool> pick)
    {
        var promo = world.Get(world.PlayerPromotion)!;
        var f = world.FreeAgents().Where(x => !x.LocalTalent && x.IsMatchReady(world.CurrentDate.AddDays(30)) && pick(x))
            .OrderBy(x => x.Id.Value).First();
        var exp = new MmaManager.Core.Sim.Contracts.FighterValuation().Evaluate(world, f);
        MmaManager.Core.Sim.Contracts.ContractLifecycle.Sign(world, f, new ContractOffer
        {
            Fighter = f.Id, Promotion = promo.Id,
            PursePerFight = exp.AskingPurse, WinBonus = exp.AskingWinBonus, Fights = exp.DesiredFights,
        }, world.CurrentDate);
        return f;
    }

    [Fact]
    public void Terms_TooCloseToCall_AreJudgedOnTheNight_AndWidenTheRange()
    {
        var world = Build(11);
        var booking = new PlayerBooking();
        var promo = world.Get(world.PlayerPromotion)!;
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(60));
        BookCard(world, booking, ev, 3);
        var p0 = booking.Project(world, ev);
        int star = Math.Max(world.Get(ev.Card[^1].RedCorner)!.Intangibles.Popularity.Value,
                            world.Get(ev.Card[^1].BlueCorner)!.Intangibles.Popularity.Value);
        promo.SponsorDeals.Add(new SponsorDeal
        {
            Partner = "Close Call Co.",
            FeePerEvent = new Money(20_000m),
            EventsRemaining = 4,
            Requirements =
            {
                new DealRequirement(DealRequirementKind.MinAttendance, p0.Attendance + 1),   // one short: a coin-flip
                new DealRequirement(DealRequirementKind.MinStarPower, star + 2),
            },
        });

        var p = booking.Project(world, ev);
        var deal = Assert.Single(p.Partners, x => x.Partner == "Close Call Co.");
        Assert.All(deal.Terms, t => Assert.Equal(TermStatus.JudgedOnTheNight, t.Status));
        Assert.Contains("too close to call", deal.Terms[0].Gap);
        Assert.False(deal.AtRisk);
        Assert.Equal(deal.FullFee, deal.ExpectedFee);
        Assert.True(p.PartnerFeesAtStakeOnTheNight.Amount >= deal.FullFee.Amount * 0.5m);
        Assert.True(p.NetLow.Amount < p.Net.Amount && p.Net.Amount < p.NetHigh.Amount);
    }

    [Fact]
    public void NetRange_IsABound_NoBonusAtBest_EveryBonusAndFillerCeilingAtWorst()
    {
        var world = Build(11);
        var booking = new PlayerBooking();
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(60));
        BookCard(world, booking, ev, 2);   // short, so the padding counts too
        var p = booking.Project(world, ev);
        Assert.True(p.Padding.WouldPad);
        var fixedCosts = p.Production + p.Rent + p.Purses + p.PpvPoints;
        Assert.Equal(p.Income - fixedCosts - p.FillerFloor, p.NetHigh);
        Assert.Equal(p.Income - p.PartnerFeesAtStakeOnTheNight - fixedCosts - p.WinBonusMax - p.FightOfTheNight - p.FillerCeiling, p.NetLow);
        Assert.True(p.FillerFloor.Amount <= p.Filler.Amount && p.Filler.Amount <= p.FillerCeiling.Amount);
        Assert.True(p.NetLow.Amount <= p.Net.Amount && p.Net.Amount <= p.NetHigh.Amount);
    }

    [Fact]
    public void Cadence_StrikesBeforeTheCard_CountTowardWalking()
    {
        var world = Build(11);
        var booking = new PlayerBooking();
        var promo = world.Get(world.PlayerPromotion)!;
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(75));
        BookCard(world, booking, ev, 2);
        promo.MediaDeals.Add(new MediaDeal
        {
            Kind = MediaKind.SpecialtyCable,
            Partner = "Quiet Channel",
            RightsFeePerEvent = new Money(50_000m),
            MonthsRemaining = 12,
            Strikes = 1,
            SignedOn = world.CurrentDate.AddDays(-200),
            LastAiredOn = world.CurrentDate.AddDays(-40),
            Requirements =
            {
                new DealRequirement(DealRequirementKind.EventCadence, 45),
                new DealRequirement(DealRequirementKind.TitleFight, 0),   // missed on this card
            },
        });

        var p = booking.Project(world, ev);
        var deal = Assert.Single(p.Partners, x => x.Partner == "Quiet Channel");
        Assert.True(deal.PendingStrikes >= 1, "75 days dark against a 45-day clause strikes before the card");
        Assert.True(deal.AtRisk);
        // Without the cadence strike this miss would be strike 2 of 3; with it, the deal ends.
        Assert.True(deal.WouldWalk);
        Assert.Equal(deal.Strikes + deal.PendingStrikes >= MmaManager.Core.Sim.Finance.MediaService.StrikeLimit, deal.GoneBeforeCard);
        Assert.Equal(deal.GoneBeforeCard ? Money.Zero : deal.FullFee * 0.5m, deal.ExpectedFee);
        Assert.Contains(p.Risks, r => r.Kind == BookingRiskKind.CadenceBroken && r.Partner == "Quiet Channel");

        // Fully served deal with no strikes and nothing missed: the cadence strike alone doesn't walk it.
        var clean = promo.MediaDeals.Single(d => d.Partner == "Quiet Channel");
        clean.Strikes = 0;
        clean.Requirements.RemoveAll(r => r.Kind == DealRequirementKind.TitleFight);
        var q = booking.Project(world, ev).Partners.Single(x => x.Partner == "Quiet Channel");
        Assert.Equal(q.PendingStrikes >= MmaManager.Core.Sim.Finance.MediaService.StrikeLimit, q.WouldWalk);
    }

    [Fact]
    public void ReplaceFighter_ReleasesAnUnfoughtLocal()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options
        {
            Seed = 21,
            Founding = new WorldBuilder.FoundingSpec { Prestige = 15, Divisions = new[] { WeightClass.Lightweight } },
        });
        var booking = new PlayerBooking();
        var a = SignFreeAgent(world, f => f.WeightClass == WeightClass.Lightweight && f.Sex == Sex.Male);
        var b = SignFreeAgent(world, f => f.WeightClass == WeightClass.Lightweight && f.Sex == Sex.Male && f.Id != a.Id);
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(30));
        Assert.True(booking.AddLocalBout(world, ev, a.Id, PrimaryStyle.Striker, 3, out var err), err);
        var bout = Assert.Single(ev.Card);
        var local = bout.BlueCorner;
        Assert.True(world.Get(local)!.LocalTalent);

        Assert.True(booking.ReplaceFighter(world, ev, bout.Id, local, b.Id, out err), err);
        Assert.Null(world.Get(local));   // back into the scene
        Assert.Equal(b.Id, ev.Card[0].BlueCorner);
    }

    [Fact]
    public void PickLists_AreOrderedByTheScoutsRead()
    {
        var world = Build(11);
        var booking = new PlayerBooking();
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(60));
        var pool = ReadyPool(world, ev, 4);
        var f0 = pool[0];

        var roster = booking.AvailableRoster(world, ev, f0.WeightClass, f0.Sex);
        Assert.Equal(roster.OrderByDescending(f => MatchmakingAdvisor.ScoutedRead(world, f)).ThenBy(f => f.Id.Value).Select(f => f.Id),
                     roster.Select(f => f.Id));
        var opponents = booking.EligibleOpponents(world, ev, f0.Id);
        Assert.Equal(opponents.OrderByDescending(f => MatchmakingAdvisor.ScoutedRead(world, f)).ThenBy(f => f.Id.Value).Select(f => f.Id),
                     opponents.Select(f => f.Id));
    }

    [Fact]
    public void PulloutShortlist_IsRankedByTheScoutsRead()
    {
        var world = Build(11);
        var promo = world.Get(world.PlayerPromotion)!;
        var booking = new PlayerBooking();
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(60));
        BookCard(world, booking, ev, 1);
        var fight = ev.Card[0];
        var booked = new HashSet<FighterId>(world.Events.Values.Where(e => e.Status == EventStatus.Scheduled)
            .SelectMany(e => e.Card).SelectMany(f => new[] { f.RedCorner, f.BlueCorner }));

        var list = Storylines.PlayerShortlist(world, ev, fight, booked, fight.BlueCorner);
        Assert.InRange(list.Count, 1, 3);
        Assert.Equal(list.OrderByDescending(f => ScoutingService.PerceivedAbility(world, promo, f)).ThenBy(f => f.Id.Value).Select(f => f.Id),
                     list.Select(f => f.Id));
        // Nobody left out reads higher than the last name on the list.
        int floor = ScoutingService.PerceivedAbility(world, promo, list[^1]);
        var stay = world.Get(fight.BlueCorner)!;
        var div = promo.Divisions.First(d => d.Matches(stay));
        var candidates = world.RosterOf(promo.Id).Concat(world.FreeAgents().Where(f => f.Record.TotalBouts >= 3))
            .Where(c => c.Status == FighterStatus.Active && !c.LocalTalent && c.Id != stay.Id && !booked.Contains(c.Id)
                        && c.IsMatchReady(ev.Date) && div.Matches(c) && world.BiddingWars.All(w => w.Fighter != c.Id))
            .Where(c => !list.Any(l => l.Id == c.Id));
        Assert.All(candidates, c => Assert.True(ScoutingService.PerceivedAbility(world, promo, c) <= floor));
    }

    [Fact]
    public void Suggest_TwoFightersWeReadTheSame_ScoreTheSame_WhateverTheTruth()
    {
        foreach (ulong seed in new ulong[] { 11, 13, 17, 19, 23, 29 })
        {
            var world = Build(seed);
            var booking = new PlayerBooking();
            var promo = world.Get(world.PlayerPromotion)!;
            var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(60));
            var city = MmaManager.Core.Sim.Geography.WorldMap.CityOf(world, ev)!.Value;
            var champs = promo.Divisions.Where(d => d.Title is not null).Select(d => world.Get(d.Title!.Value)!.CurrentChampion).ToHashSet();
            int Read(Fighter f) => ScoutingService.PerceivedAbility(world, promo, f);

            foreach (var div in promo.Divisions)
            {
                // Plain unranked bodies: no belt, no hometown pull, no camp or feud ties.
                var plain = world.RosterOf(promo.Id)
                    .Where(f => div.Matches(f) && f.IsMatchReady(ev.Date) && !div.Rankings.Contains(f.Id) && !champs.Contains(f.Id)
                                && f.HomeCity != city && MmaManager.Core.Sim.Geography.Reputation.FighterInCity(f, city) < 40)
                    .OrderBy(f => f.Id.Value).ToList();
                bool Clean(Fighter p, Fighter q) =>
                    (p.Camp is null || p.Camp != q.Camp) && !Bonds.WontFight(world, p.Id, q.Id)
                    && Storylines.RivalryBetween(world, p.Id, q.Id) is null && !p.BoutHistory.Intersect(q.BoutHistory).Any();

                foreach (var red in plain)
                    foreach (var x in plain.Where(x => x.Id != red.Id && Clean(red, x)))
                        foreach (var y in plain.Where(y => y.Id.Value > x.Id.Value && y.Id != red.Id && Clean(red, y)
                                                           && Read(y) == Read(x)
                                                           && FighterAbility.CurrentAbility(y) != FighterAbility.CurrentAbility(x)))
                        {
                            y.Intangibles.Popularity = x.Intangibles.Popularity;   // same public facts
                            var all = booking.Suggest(world, ev, BoutSlot.MainCard, 100_000);
                            var px = all.Single(p => (p.Red == red.Id && p.Blue == x.Id) || (p.Red == x.Id && p.Blue == red.Id));
                            var py = all.Single(p => (p.Red == red.Id && p.Blue == y.Id) || (p.Red == y.Id && p.Blue == red.Id));
                            Assert.Equal(px.Score, py.Score);
                            Assert.Equal(px.Appraisal.Competitiveness, py.Appraisal.Competitiveness);
                            Assert.Equal(px.Appraisal.Appeal, py.Appraisal.Appeal);
                            return;
                        }
            }
        }
        Assert.Fail("no seed had two same-read, different-truth unranked fighters to compare");
    }
}
