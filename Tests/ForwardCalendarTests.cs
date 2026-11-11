using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Org;

namespace MmaManager.Tests;

/// <summary>
/// The world's clock: AI promotions announce their shows weeks ahead with a
/// chosen main event, fill the undercard as the date closes in, and run the
/// card on the night — and everything that can happen to a booked fight in
/// between resolves cleanly, with no departed, retired or double-booked
/// fighter ever on a card.
/// </summary>
public class ForwardCalendarTests
{
    private static World Build(ulong seed = 5) =>
        new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });

    private static IEnumerable<Promotion> AiOrgs(World world) =>
        world.Promotions.Values.Where(p => p.Id != world.PlayerPromotion && !p.Defunct).OrderBy(p => p.Id.Value);

    private static IEnumerable<Event> AiScheduled(World world) =>
        world.Events.Values
            .Where(e => e.Status == EventStatus.Scheduled && e.Promotion != world.PlayerPromotion && world.Get(e.Promotion) is not null)
            .OrderBy(e => e.Date).ThenBy(e => e.Id.Value);

    /// <summary>
    /// The calendar invariants. Every corner of every scheduled AI bout (local
    /// filler aside) is an existing, unretired fighter signed to the promotion
    /// running the card; nobody is booked twice anywhere on the calendar; a
    /// belt is only on a bout its holder (if any) is in. Returns the violations.
    /// </summary>
    internal static List<string> Violations(World world)
    {
        var bad = new List<string>();
        var where = new Dictionary<FighterId, string>();
        foreach (var ev in world.Events.Values.Where(e => e.Status == EventStatus.Scheduled).OrderBy(e => e.Id.Value))
        {
            bool ai = ev.Promotion != world.PlayerPromotion && world.Get(ev.Promotion) is not null;
            foreach (var fight in ev.Card)
            {
                foreach (var id in new[] { fight.RedCorner, fight.BlueCorner })
                {
                    var f = world.Get(id);
                    if (where.TryGetValue(id, out var other))
                        bad.Add($"{f?.FullName ?? id.ToString()} is booked on {other} AND {ev.Name} ({ev.Date})");
                    else where[id] = $"{ev.Name} ({ev.Date})";
                    if (!ai) continue;
                    if (f is null) { bad.Add($"{ev.Name}: fighter {id.Value} doesn't exist"); continue; }
                    if (f.LocalTalent) continue;
                    if (f.Status == FighterStatus.Retired) bad.Add($"{ev.Name}: {f.FullName} is retired");
                    if (f.Contract?.Promotion != ev.Promotion)
                        bad.Add($"{ev.Name}: {f.FullName} is signed to " +
                                $"{(f.Contract is { } c ? world.Get(c.Promotion)?.Abbreviation : "nobody")}");
                }
                if (ai && fight.IsTitleFight && fight.Title is { } tid && world.Get(tid) is { } belt
                    && belt.CurrentChampion is { } holder && holder != fight.RedCorner && holder != fight.BlueCorner)
                    bad.Add($"{ev.Name}: the {belt.Name} is on a bout its champion isn't in");
            }
        }
        return bad;
    }

    // --- day one -----------------------------------------------------------------

    [Fact]
    public void DayOne_EveryAiPromotionHasItsNextCardOnTheBooks()
    {
        var world = Build();
        var start = world.CurrentDate;

        foreach (var promo in AiOrgs(world))
        {
            var next = AiScheduled(world).FirstOrDefault(e => e.Promotion == promo.Id && e.Date > start);
            Assert.True(next is not null, $"{promo.Abbreviation} opens with nothing on the calendar");
            Assert.NotEmpty(next!.Card);
            // Announced inside the org's lead window, on one of its show weeks.
            Assert.True(next.Date.DayNumber - start.DayNumber <= 7 * ForwardCalendar.LeadWeeks(OrgSizing.SizeOf(promo))
                        || SignatureEvents.IsSignature(world, next));
            Assert.True(ForwardCalendar.IsShowWeek(promo, next.Date));
        }
        var v = Violations(world);
        Assert.True(v.Count == 0, string.Join("\n", v));
    }

    [Fact]
    public void DayOne_FlagshipsInsideTwoMonths_AreAnnounced_WithTheirAuthoredMainEvent()
    {
        var world = Build();
        var start = world.CurrentDate;
        var icf = world.Promotions.Values.First(p => p.Abbreviation == "ICF");

        // The Emperor's New Year (January) and Carnaval de Combate (February) are
        // already on the books, with a named main event.
        var flagships = AiScheduled(world).Where(e => SignatureEvents.IsSignature(world, e)).ToList();
        Assert.Contains(flagships, e => e.Name.StartsWith("ICF Emperor's New Year"));
        Assert.Contains(flagships, e => e.Name.StartsWith("RCS Carnaval de Combate"));
        Assert.All(flagships, e =>
        {
            Assert.True(e.Date.DayNumber - start.DayNumber <= 7 * ForwardCalendar.SignatureLeadWeeks);
            Assert.NotEmpty(e.Card);
        });

        // The lore's fight headlines the dome show: Sato defends against Kang.
        var newYear = flagships.First(e => e.Promotion == icf.Id);
        var main = newYear.Card[^1];
        var corners = new[] { world.Get(main.RedCorner)!.LastName, world.Get(main.BlueCorner)!.LastName };
        Assert.Contains("Sato", corners);
        Assert.Contains("Kang", corners);
        Assert.True(main.IsTitleFight, "Sato is the champion: the belt is on the line");
        Assert.Equal(icf.HomeCity, world.Get(newYear.VenueId!.Value)!.City);   // the tradition goes home
    }

    [Fact]
    public void DayOne_Prologue_ReportsTheRealCalendar()
    {
        var world = Build();
        var announced = world.News.Where(n => n.Headline.Contains(" sets ") && n.Headline.Contains(" vs ")).ToList();
        Assert.NotEmpty(announced);
        Assert.Contains(announced, n => n.Headline.Contains("Sato vs Kang") && n.Headline.Contains("Emperor's New Year"));

        // Every announcement names a real scheduled card: its promotion, its two
        // headliners, its date and its room — nothing invented.
        foreach (var n in announced)
        {
            Assert.True(n.Date <= world.CurrentDate, "nothing is reported before it happened");
            var match = AiScheduled(world).FirstOrDefault(e =>
                world.Get(e.Promotion) is { } p && n.Headline.StartsWith(p.Abbreviation + " sets ")
                && n.Headline.Contains($"{world.Get(e.Card[^1].RedCorner)!.LastName} vs {world.Get(e.Card[^1].BlueCorner)!.LastName}")
                && n.Headline.Contains(e.Date.ToString("d MMMM"))
                && (e.Venue is null || n.Headline.EndsWith(e.Venue)));
            Assert.True(match is not null, $"no scheduled card behind \"{n.Headline}\"");
        }
    }

    // --- an announced card runs as announced ------------------------------------

    [Fact]
    public void AnnouncedCards_RunOnTheirDate_WithTheirAnnouncedMainEvent()
    {
        var world = Build();
        var tick = new WorldTick();
        var announced = new Dictionary<EventId, (FighterId, FighterId)>();
        void Snap()
        {
            foreach (var e in AiScheduled(world).Where(e => e.Card.Count > 0 && !announced.ContainsKey(e.Id)))
                announced[e.Id] = (e.Card[^1].RedCorner, e.Card[^1].BlueCorner);
        }
        Snap();
        var newYear = AiScheduled(world).First(e => e.Name.StartsWith("ICF Emperor's New Year"));
        var date = newYear.Date;

        for (int w = 0; w < 26; w++) { tick.AdvanceWeek(world); Snap(); }

        // The dome show ran on its date with Sato and Kang on top.
        Assert.Equal(EventStatus.Completed, newYear.Status);
        Assert.Equal(date, newYear.Date);
        Assert.Equal(announced[newYear.Id], (newYear.Card[^1].RedCorner, newYear.Card[^1].BlueCorner));
        Assert.NotNull(newYear.Card[^1].Outcome);

        // And across half a year, when nothing intervenes, a card is what it said
        // it would be: the great majority of announced main events happen.
        var ran = announced.Keys.Select(id => world.Events[id]).Where(e => e.Status == EventStatus.Completed).ToList();
        Assert.True(ran.Count > 100, $"only {ran.Count} announced AI cards ran in 26 weeks");
        int asAnnounced = ran.Count(e => (e.Card[^1].RedCorner, e.Card[^1].BlueCorner) == announced[e.Id]);
        Assert.True(asAnnounced >= ran.Count * 0.8, $"{asAnnounced}/{ran.Count} main events ran as announced");
    }

    [Fact]
    public void TheUndercard_IsFilledInCloseToTheDate()
    {
        var world = Build();
        var far = AiScheduled(world).First(e => e.Date.DayNumber - world.CurrentDate.DayNumber > ForwardCalendar.UndercardFillDays
                                                 && !SignatureEvents.IsSignature(world, e));
        Assert.InRange(far.Card.Count, 1, 2);   // the headliners only

        var tick = new WorldTick();
        while (far.Date.DayNumber - world.CurrentDate.DayNumber > ForwardCalendar.UndercardFillDays) tick.AdvanceWeek(world);
        Assert.True(far.Card.Count > 2, "the undercard goes in inside the fill window");
        Assert.True(far.Card.Count <= ForwardCalendar.CardSize(world.Get(far.Promotion)!));
    }

    // --- things happen between announcement and fight night ---------------------

    // A big house's scheduled card at least two weeks out (a deep roster, so a
    // short-notice replacement is always there to be found).
    private static (Event Ev, Fight Main) CardWeeksOut(World world, int minDays = 15)
    {
        var ev = AiScheduled(world).First(e => e.Date.DayNumber - world.CurrentDate.DayNumber >= minDays && e.Card.Count >= 2
                                               && OrgSizing.SizeOf(world.Get(e.Promotion)!) >= OrgSize.Continental);
        return (ev, ev.Card[^1]);
    }

    private static void AssertGoneAndClean(World world, Event ev, FighterId gone)
    {
        Assert.DoesNotContain(ev.Card, f => f.RedCorner == gone || f.BlueCorner == gone);
        var v = Violations(world);
        Assert.True(v.Count == 0, string.Join("\n", v));
    }

    [Fact]
    public void AHeadlinerInjuredInCamp_IsReplacedOrTheBoutComesOff()
    {
        var world = Build();
        var (ev, main) = CardWeeksOut(world);
        var hurt = world.Get(main.BlueCorner)!;
        hurt.Status = FighterStatus.Injured;
        hurt.CurrentInjury = new Injury
        {
            Severity = InjurySeverity.Moderate, Description = "a torn hamstring",
            OccurredOn = world.CurrentDate, ExpectedReturn = ev.Date.AddDays(30),
        };

        ForwardCalendar.Audit(world, world.CurrentDate);

        AssertGoneAndClean(world, ev, hurt.Id);
        Assert.NotEmpty(ev.Card);
        // Saved on short notice: the other man still has a fight.
        Assert.Contains(ev.Card, f => f.RedCorner == main.RedCorner || f.BlueCorner == main.RedCorner);
    }

    [Fact]
    public void AHeadlinerWhoSignsElsewhere_ComesOffTheCard_AndIsReplaced()
    {
        var world = Build();
        var (ev, main) = CardWeeksOut(world);
        var mover = world.Get(main.BlueCorner)!;
        var rival = AiOrgs(world).First(p => p.Id != ev.Promotion);
        ContractLifecycle.Sign(world, mover, new ContractOffer
        {
            Fighter = mover.Id, Promotion = rival.Id, PursePerFight = new Money(50_000m), Fights = 4,
        }, world.CurrentDate);

        ForwardCalendar.Audit(world, world.CurrentDate);

        AssertGoneAndClean(world, ev, mover.Id);
        var stayed = ev.Card.FirstOrDefault(f => f.RedCorner == main.RedCorner || f.BlueCorner == main.RedCorner);
        Assert.NotNull(stayed);
        var sub = world.Get(stayed!.RedCorner == main.RedCorner ? stayed.BlueCorner : stayed.RedCorner)!;
        Assert.Equal(ev.Promotion, sub.Contract?.Promotion);   // the stand-in is signed to the show
    }

    [Fact]
    public void AFighterWhoLeavesInFightWeek_HasTheBoutScrapped_NotSaved()
    {
        var world = Build();
        var tick = new WorldTick();
        // Walk to the week before some AI card.
        var ev = AiScheduled(world).First(e => e.Date.DayNumber - world.CurrentDate.DayNumber >= 21 && e.Card.Count >= 2);
        while (ev.Date.DayNumber - world.CurrentDate.DayNumber > 7) tick.AdvanceWeek(world);
        Assume(ev.Status == EventStatus.Scheduled && ev.Card.Count >= 2);

        var bout = ev.Card[0];
        var leaver = world.Get(bout.RedCorner)!;
        var opponent = bout.BlueCorner;
        ContractLifecycle.Release(world, leaver, dentReputation: false);

        int before = ev.Card.Count;
        ForwardCalendar.Audit(world, world.CurrentDate);

        AssertGoneAndClean(world, ev, leaver.Id);
        Assert.Equal(before - 1, ev.Card.Count);   // scrapped, not saved
        Assert.DoesNotContain(ev.Card, f => f.RedCorner == opponent || f.BlueCorner == opponent);
    }

    [Fact]
    public void ARetiredHeadliner_ComesOffTheCard()
    {
        var world = Build();
        var (ev, main) = CardWeeksOut(world);
        var old = world.Get(main.RedCorner)!;
        Lifecycle.Retire(world, old, world.CurrentDate);

        ForwardCalendar.Audit(world, world.CurrentDate);

        AssertGoneAndClean(world, ev, old.Id);
        // Whatever headlines now is a proper main event: title length.
        Assert.Equal(world.Get(ev.Promotion)!.Rules.TitleRounds, ev.Card[^1].ScheduledRounds);
    }

    [Fact]
    public void AStrippedChampion_TakesTheBeltOffTheAnnouncedBout()
    {
        var world = Build();
        var ev = AiScheduled(world).First(e => e.Card.Count > 0 && e.Card[^1].IsTitleFight
                                               && world.Get(e.Card[^1].Title!.Value)!.CurrentChampion is not null);
        var main = ev.Card[^1];
        var belt = world.Get(main.Title!.Value)!;
        // Stripped (as on a scale elsewhere) and the belt goes to someone else.
        var heir = world.Get(belt.Promotion)!.Divisions.First(d => d.Title == belt.Id).Rankings
            .First(id => id != main.RedCorner && id != main.BlueCorner);
        belt.CurrentChampion = heir;

        ForwardCalendar.Audit(world, world.CurrentDate);

        Assert.False(main.IsTitleFight);
        Assert.Null(main.Title);
        var v = Violations(world);
        Assert.True(v.Count == 0, string.Join("\n", v));
    }

    [Fact]
    public void APromotionThatFolds_CancelsItsAnnouncedCards_WithNews()
    {
        var world = Build();
        var promo = AiOrgs(world).First(p => AiScheduled(world).Count(e => e.Promotion == p.Id) >= 1);
        var cards = AiScheduled(world).Where(e => e.Promotion == promo.Id).ToList();

        Solvency.Close(world, promo, world.CurrentDate);

        Assert.All(cards, e => Assert.Equal(EventStatus.Cancelled, e.Status));
        Assert.Contains(world.News, n => n.Headline == $"{promo.Name} closes its doors" && n.Body.Contains("off the calendar"));
        var v = Violations(world);
        Assert.True(v.Count == 0, string.Join("\n", v));

        // And the next tick books nothing for the dead house.
        new WorldTick().AdvanceWeek(world);
        Assert.DoesNotContain(AiScheduled(world), e => e.Promotion == promo.Id);
    }

    [Fact]
    public void AFighterCanNeverBeOnTwoCards_TheLaterBookingLosesHim()
    {
        var world = Build();
        var cards = AiScheduled(world).Where(e => e.Card.Count >= 2).ToList();
        var first = cards.First(e => e.Date.DayNumber - world.CurrentDate.DayNumber >= 15);
        var later = cards.First(e => e.Date > first.Date && e.Promotion == first.Promotion);
        // Force a double-booking: the later card's co-main "also" has the first card's main-eventer.
        var doubled = first.Card[^1].RedCorner;
        var victim = later.Card[0];
        later.Card[0] = new Fight
        {
            Id = world.NewFightId(), RedCorner = doubled, BlueCorner = victim.BlueCorner,
            WeightClass = victim.WeightClass, ScheduledRounds = victim.ScheduledRounds,
        };

        ForwardCalendar.Audit(world, world.CurrentDate);

        Assert.Contains(first.Card, f => f.RedCorner == doubled || f.BlueCorner == doubled);
        Assert.DoesNotContain(later.Card, f => f.RedCorner == doubled || f.BlueCorner == doubled);
        var v = Violations(world);
        Assert.True(v.Count == 0, string.Join("\n", v));
    }

    [Fact]
    public void TheRunTimeGuard_ScrapsAnyBoutWithADepartedCorner()
    {
        var world = Build();
        var (ev, main) = CardWeeksOut(world, minDays: 7);
        var gone = world.Get(main.RedCorner)!;
        // Leave without the audit ever seeing it, then run the card on the spot.
        gone.Contract = null;
        new WorldTick().RunEventLive(world, ev, playByPlay: false);

        Assert.Equal(EventStatus.Completed, ev.Status);
        Assert.DoesNotContain(ev.Card, f => f.RedCorner == gone.Id || f.BlueCorner == gone.Id);
        // Everyone who fought was signed to the show (or was the local filler).
        Assert.All(ev.Card.SelectMany(f => new[] { f.RedCorner, f.BlueCorner }), id =>
        {
            var c = world.Get(id)!;
            if (!c.LocalTalent) Assert.Equal(ev.Promotion, c.Contract?.Promotion);
        });
    }

    // --- the designed-bookings table --------------------------------------------

    [Fact]
    public void DesertClash_IsHeadlinedByDudaevVsAlSuwaidi_AndTheyAreHeldBackForIt()
    {
        var world = Build();
        var gfc = world.Promotions.Values.First(p => p.Abbreviation == "GFC");
        var dudaev = world.Fighters.Values.First(f => f.Designed && f.LastName == "Dudaev");
        var suwaidi = world.Fighters.Values.First(f => f.Designed && f.LastName == "Al-Suwaidi");
        var tick = new WorldTick();

        Event? clash = null;
        for (int w = 0; w < 10 && clash is null; w++)
        {
            // Until the flagship is announced, neither man is booked on an ordinary GFC night.
            foreach (var e in AiScheduled(world).Where(e => e.Promotion == gfc.Id && !SignatureEvents.IsSignature(world, e)
                                                            && e.Date.Month <= 3 && e.Date.Year == world.StartDate.Year))
                Assert.DoesNotContain(e.Card, f => f.RedCorner == suwaidi.Id || f.BlueCorner == suwaidi.Id
                                                || f.RedCorner == dudaev.Id || f.BlueCorner == dudaev.Id);
            tick.AdvanceWeek(world);
            clash = AiScheduled(world).FirstOrDefault(e => e.Name.StartsWith("GFC Desert Clash"));
        }
        Assert.NotNull(clash);
        var main = clash!.Card[^1];
        Assert.Equal(new[] { dudaev.Id, suwaidi.Id }.OrderBy(i => i.Value), new[] { main.RedCorner, main.BlueCorner }.OrderBy(i => i.Value));
        Assert.Contains(world.News, n => n.Headline.StartsWith("GFC sets Dudaev vs Al-Suwaidi for the Desert Clash")
                                         && n.Body.Contains("Gulf's son"));
    }

    [Fact]
    public void AnAuthoredBookingThatCantHappen_FallsBackCleanly()
    {
        var world = Build();
        var suwaidi = world.Fighters.Values.First(f => f.Designed && f.LastName == "Al-Suwaidi");
        Lifecycle.Retire(world, suwaidi, world.CurrentDate);   // the prodigy walks away before it's made
        var tick = new WorldTick();

        Event? clash = null;
        for (int w = 0; w < 10 && clash is null; w++)
        {
            tick.AdvanceWeek(world);
            clash = AiScheduled(world).FirstOrDefault(e => e.Name.StartsWith("GFC Desert Clash"));
        }
        Assert.NotNull(clash);
        Assert.NotEmpty(clash!.Card);   // the flagship still goes ahead, headlined by the ordinary choice
        Assert.DoesNotContain(clash.Card, f => f.RedCorner == suwaidi.Id || f.BlueCorner == suwaidi.Id);
        Assert.DoesNotContain(world.News, n => n.Headline.Contains("Dudaev vs Al-Suwaidi"));
        Assert.Null(ForwardCalendar.AuthoredBookingOn(world, clash));
    }

    // --- the feed ----------------------------------------------------------------

    [Fact]
    public void Announcements_OnlyReachTheFeedWhenTheyMeritIt()
    {
        var world = Build();
        world.PlayerPromotion = default;
        var tick = new WorldTick();
        int posted = 0, announcements = 0;
        var small = new List<string>();
        for (int w = 0; w < 52; w++)
        {
            var last = world.News.Count > 0 ? world.News[^1] : null;
            tick.AdvanceWeek(world);
            int from = last is null ? 0 : world.News.FindLastIndex(n => ReferenceEquals(n, last)) + 1;
            for (int i = from; i < world.News.Count; i++)
            {
                posted++;
                var n = world.News[i];
                if (!(n.Headline.Contains(" sets ") && n.Headline.Contains(" vs ") && n.AboutEvent is { } id)) continue;
                announcements++;
                var ev = world.Events[id];
                // A club's monthly night never makes the feed — only flagships,
                // feud headliners from National up, title cards from Continental up.
                if (!SignatureEvents.IsSignature(world, ev) && ForwardCalendar.AuthoredBookingOn(world, ev) is null
                    && OrgSizing.SizeOf(world.Get(ev.Promotion)!) < OrgSize.National)
                    small.Add(n.Headline);
            }
        }
        Assert.True(small.Count == 0, "small-house announcements in the feed:\n" + string.Join("\n", small));
        Assert.InRange(announcements, 20, 200);
        Assert.True(announcements < posted * 0.08, $"{announcements} of {posted} headlines were announcements");
        Assert.True(world.News.Count <= 1000);
    }

    [Fact]
    public void TheDigest_ListsTheComingCards_WithTheirMainEvents()
    {
        var world = Build();
        var tick = new WorldTick();
        MailMessage? digest = null;
        for (int w = 0; w < 8 && digest is null; w++)
        {
            tick.AdvanceWeek(world);
            digest = world.Mail.LastOrDefault(m => m.Subject.StartsWith("MMA Weekly") && m.Body.Contains("ON THE CALENDAR"));
        }
        Assert.NotNull(digest);
        var lines = digest!.Body.Split('\n').SkipWhile(l => !l.StartsWith("ON THE CALENDAR")).Skip(1)
            .TakeWhile(l => l.TrimStart().StartsWith("•")).ToList();
        Assert.NotEmpty(lines);
        Assert.All(lines, l => Assert.Contains(" vs ", l));
    }

    // --- cadence ------------------------------------------------------------------

    [Fact]
    public void EachPromotion_RunsItsShowsAtTheSameCadence()
    {
        var world = Build(seed: 3);
        world.PlayerPromotion = default;
        var start = world.CurrentDate;
        var sizeAtStart = AiOrgs(world).ToDictionary(p => p.Id, OrgSizing.SizeOf);
        new WorldTick().AdvanceWeeks(world, 52);

        int checkedOrgs = 0;
        foreach (var promo in world.Promotions.Values.Where(p => !p.Defunct && sizeAtStart.ContainsKey(p.Id)))
        {
            // Only orgs that stayed on one rung all year (a tier move changes the cadence).
            if (OrgSizing.SizeOf(promo) != sizeAtStart[promo.Id]) continue;
            int shows = world.Events.Values.Count(e => e.Promotion == promo.Id && e.Status == EventStatus.Completed
                                                       && e.Date > start && !Storylines.IsSuperfight(e));
            int expected = 52 / ForwardCalendar.CadenceWeeks(promo);
            Assert.True(Math.Abs(shows - expected) <= 2, $"{promo.Abbreviation}: {shows} shows, expected ~{expected}");
            checkedOrgs++;
        }
        Assert.True(checkedOrgs >= 10);
    }

    // --- the player ----------------------------------------------------------------

    [Fact]
    public void ThePlayer_CannotBookAFighterCommittedToAnAiCard_AndTheCalendarReadsTheWholeSport()
    {
        var world = Build();
        var aiCard = AiScheduled(world).First(e => e.Card.Count > 0);
        var committed = aiCard.Card[^1].RedCorner;
        Assert.True(ForwardCalendar.IsCommitted(world, committed));
        Assert.Equal(aiCard.Id, ForwardCalendar.CommittedTo(world, committed)!.Id);

        // The read API: every promotion's next shows, flagships flagged, main events named.
        var upcoming = ForwardCalendar.Upcoming(world, world.CurrentDate, world.CurrentDate.AddDays(60));
        Assert.Contains(upcoming, c => c.IsSignature && c.MainEvent is not null);
        Assert.Equal(upcoming.OrderBy(c => c.Date).Select(c => c.Date), upcoming.Select(c => c.Date));
        var icf = world.Promotions.Values.First(p => p.Abbreviation == "ICF");
        Assert.All(ForwardCalendar.Upcoming(world, promotion: icf.Id), c => Assert.Equal(icf.Id, c.Promotion.Id));
        Assert.All(ForwardCalendar.Upcoming(world, region: Region.Asia), c => Assert.Equal(Region.Asia, c.Region));

        // Player-side booking counts the AI calendar: a fighter on an announced
        // AI card can't be booked onto one of the player's.
        var player = world.Get(world.PlayerPromotion)!;
        var pair = world.RosterOf(player.Id).Where(x => x.IsMatchReady(world.CurrentDate.AddDays(30)))
            .GroupBy(x => (x.WeightClass, x.Sex)).First(g => g.Count() >= 3).Take(3).ToList();
        aiCard.Card.Insert(0, new Fight
        {
            Id = world.NewFightId(), RedCorner = pair[0].Id, BlueCorner = pair[2].Id, WeightClass = pair[0].WeightClass,
        });
        var booking = new PlayerBooking();
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(30));
        Assert.False(booking.AddBout(world, ev, pair[0].Id, pair[1].Id, 3, false, out var err));
        Assert.Contains("booked", err);
    }

    // --- the long run ------------------------------------------------------------

    [Fact]
    public void OverThreeYears_NoScheduledBoutEverHoldsADepartedRetiredOrDoubleBookedFighter()
    {
        var world = Build(seed: 8);
        var tick = new WorldTick();
        var problems = new List<string>();
        for (int w = 0; w < 52 * 3 && problems.Count == 0; w++)
        {
            tick.AdvanceWeek(world);
            foreach (var p in Violations(world)) problems.Add($"{world.CurrentDate}: {p}");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems.Take(20)));
        // And the clock kept running: the calendar is never empty.
        Assert.True(AiScheduled(world).Count() >= AiOrgs(world).Count() / 2);
    }

    private static void Assume(bool condition)
    {
        if (!condition) throw new InvalidOperationException("test premise not met for this seed");
    }
}
