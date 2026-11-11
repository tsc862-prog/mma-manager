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

        bool pulledNews = false;   // read as it happens: the feed caps at 1,000 items
        for (int w = 0; w < 26; w++)
        {
            tick.AdvanceWeek(world); Snap();
            pulledNews |= world.News.Any(n => n.Category == NewsCategory.Withdrawal && n.Headline.Contains(newYear.Name)
                && (n.Headline.Contains("Sato") || n.Headline.Contains("Kang")));
        }

        // The dome show ran on its date with Sato and Kang on top, unless one of
        // them was pulled in fight week (a 3% roll per corner on the week's shared
        // stream, which is what this seed draws since main's local-promotions
        // change), in which case the withdrawal is news and the belt co-main
        // headlines instead.
        Assert.Equal(EventStatus.Completed, newYear.Status);
        Assert.Equal(date, newYear.Date);
        Assert.NotNull(newYear.Card[^1].Outcome);
        var (sato, kang) = announced[newYear.Id];
        bool ranAsAnnounced = (newYear.Card[^1].RedCorner, newYear.Card[^1].BlueCorner) == (sato, kang);
        if (!ranAsAnnounced)
        {
            Assert.DoesNotContain(newYear.Card, f => (f.RedCorner == sato && f.BlueCorner == kang) || (f.RedCorner == kang && f.BlueCorner == sato));
            Assert.True(pulledNews, "a headliner pulled from the dome show is news");
            Assert.True(newYear.Card[^1].IsTitleFight, "the belt co-main takes over the top of the bill");
        }

        // And across half a year: whenever both announced headliners are still on
        // the card on fight night, their bout IS the main event (nothing may
        // reshuffle a standing headliner), and most announced mains do happen.
        var ran = announced.Keys.Select(id => world.Events[id]).Where(e => e.Status == EventStatus.Completed).ToList();
        Assert.True(ran.Count > 100, $"only {ran.Count} announced AI cards ran in 26 weeks");
        int asAnnounced = 0;
        foreach (var e in ran)
        {
            var (r, b) = announced[e.Id];
            var bout = e.Card.FirstOrDefault(f => (f.RedCorner == r && f.BlueCorner == b) || (f.RedCorner == b && f.BlueCorner == r));
            if (bout is null) continue;   // a headliner left; the card was rebuilt around it
            Assert.True(ReferenceEquals(bout, e.Card[^1]), $"{e.Name}: the announced main event was moved down the card");
            asAnnounced++;
        }
        Assert.True(asAnnounced >= ran.Count * 0.8, $"{asAnnounced}/{ran.Count} main events ran as announced");
    }

    [Fact]
    public void AFeudHeadlinerOverABeltCoMain_StaysOnTopToFightNight()
    {
        // The shape AnnounceCard builds when bad blood outranks the belt: a
        // non-title main event above a title co-main. Losing an undercard bout,
        // and the run itself, must not promote the belt over it.
        var world = Build();
        var ev = AiScheduled(world).First(e => e.Card.Count >= 2 && e.Card[^1].IsTitleFight
                                               && e.Date.DayNumber - world.CurrentDate.DayNumber <= ForwardCalendar.UndercardFillDays
                                               && OrgSizing.SizeOf(world.Get(e.Promotion)!) >= OrgSize.Continental);
        var belt = ev.Card[^1];
        var feud = ev.Card.Take(ev.Card.Count - 1).Last(f => !f.IsTitleFight);
        ev.Card.Remove(feud);
        ev.Card.Add(feud);   // feud on top, belt as co-main
        feud.ScheduledRounds = world.Get(ev.Promotion)!.Rules.TitleRounds;
        Assert.Same(belt, ev.Card[^2]);

        // An undercard corner leaves: the bout goes, the headliner doesn't move.
        var under = ev.Card.First(f => !ReferenceEquals(f, feud) && !ReferenceEquals(f, belt));
        world.Get(under.RedCorner)!.Contract = null;
        ForwardCalendar.Audit(world, world.CurrentDate);
        Assert.Same(feud, ev.Card[^1]);

        // Fight night: unless a feud corner pulled out, the feud is still the main event.
        new WorldTick().RunEventLive(world, ev, playByPlay: false);
        if (ev.Card.Contains(feud)) Assert.Same(feud, ev.Card[^1]);
    }

    [Fact]
    public void AnEmptiedCard_IsRebuilt_NotLeftDeadOrCancelled()
    {
        // Everything on an announced card falls through weeks out: the house
        // books it again from whoever is free (only a card still empty on fight
        // night collapses, with the standing hit and a headline).
        var world = Build();
        var ev = AiScheduled(world).First(e => e.Date.DayNumber - world.CurrentDate.DayNumber >= 21
                                               && OrgSizing.SizeOf(world.Get(e.Promotion)!) >= OrgSize.National);
        ev.Card.Clear();
        new WorldTick().AdvanceWeek(world);
        Assert.Equal(EventStatus.Scheduled, ev.Status);
        Assert.NotEmpty(ev.Card);
        Assert.Equal(world.Get(ev.Promotion)!.Rules.TitleRounds, ev.Card[^1].ScheduledRounds);
        var v = Violations(world);
        Assert.True(v.Count == 0, string.Join(Environment.NewLine, v));
    }

    [Fact]
    public void ASuperfightIsNeverPaddedWithFightersCommittedToALaterCard()
    {
        var world = Build();
        var tick = new WorldTick();
        for (int w = 0; w < 3; w++) tick.AdvanceWeek(world);
        // An announced card two weeks out, and a superfight-style one-bout card today.
        var later = AiScheduled(world).First(e => e.Date == world.CurrentDate.AddDays(14) && e.Card.Count >= 2
                                                  && world.RosterOf(e.Promotion).Count() > 40);
        var promo = world.Get(later.Promotion)!;
        var committed = ForwardCalendar.Committed(world);
        var pair = world.RosterOf(promo.Id)
            .Where(f => f.IsMatchReady(world.CurrentDate) && !committed.Contains(f.Id)
                        && !world.Titles.Values.Any(t => t.CurrentChampion == f.Id))
            .GroupBy(f => (f.WeightClass, f.Sex)).First(g => g.Count() >= 2).Take(2).ToList();
        var super = new EventService().CreateEvent(world, promo.Id, $"{Storylines.SuperfightPrefix}: {pair[0].LastName} vs {pair[1].LastName}",
            world.CurrentDate, new[] { new Matchup(pair[0].Id, pair[1].Id, pair[0].WeightClass, 5, false, null) });
        var laterFighters = later.Card.SelectMany(f => new[] { f.RedCorner, f.BlueCorner }).ToHashSet();

        tick.RunEventLive(world, super, playByPlay: false);

        Assert.True(super.Card.Count >= CardFiller.MinCardBouts, "the super-card is padded to a real card");
        Assert.DoesNotContain(super.Card, f => laterFighters.Contains(f.RedCorner) || laterFighters.Contains(f.BlueCorner));
        Assert.DoesNotContain(super.Card.Take(super.Card.Count - 1),
            f => world.Titles.Values.Any(t => t.CurrentChampion == f.RedCorner || t.CurrentChampion == f.BlueCorner));
    }

    [Fact]
    public void AWorldSavedMidCalendar_RunsTheSameCardsAsOneNeverSaved()
    {
        var straight = Build(seed: 6);
        var split = Build(seed: 6);
        var tick = new WorldTick();
        tick.AdvanceWeeks(straight, 3);
        tick.AdvanceWeeks(split, 3);
        Assert.True(AiScheduled(split).Count() > 10, "announced cards are pending at the save");

        string path = Path.Combine(Path.GetTempPath(), $"mma_calendar_{Guid.NewGuid():N}.db");
        World resumed;
        try { MmaManager.Persistence.SaveGame.Save(split, path); resumed = MmaManager.Persistence.SaveGame.Load(path); }
        finally { File.Delete(path); }

        new WorldTick().AdvanceWeeks(straight, 8);
        new WorldTick().AdvanceWeeks(resumed, 8);
        var diffs = PersistenceGraph.Diff(straight, resumed, max: 40);
        Assert.True(diffs.Count == 0, "a world saved with announced cards diverged:\n" + PersistenceGraph.Describe(diffs));
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
        Assert.Null(ForwardCalendar.AuthoredBookingOn(world, clash));
        // The fight that can't happen is called off in the news, and nobody is
        // substituted into it: the card is headlined by something else entirely.
        Assert.DoesNotContain(world.News, n => n.Headline.StartsWith("GFC sets Dudaev vs Al-Suwaidi"));
        var off = Assert.Single(world.News, n => n.Headline.StartsWith("Off: Dudaev vs Al-Suwaidi"));
        Assert.Contains("Khalid Al-Suwaidi is no longer an active fighter", off.Body);
    }

    [Fact]
    public void EveryAuthoredBooking_ResolvesToDesignedFightersOfItsPromotion()
    {
        var world = Build();
        foreach (var row in DesignedBookings.All)
        {
            var promo = world.Promotions.Values.FirstOrDefault(p => p.Abbreviation == row.Org);
            Assert.True(promo is not null, $"{row.Org} isn't a promotion");
            foreach (var name in new[] { row.Red, row.Blue }.OfType<string>())
            {
                var f = world.Fighters.Values.FirstOrDefault(x => x.Designed && $"{x.FirstName} {x.LastName}" == name);
                Assert.True(f is not null, $"{name} isn't a designed fighter");
                Assert.Equal(promo!.Id, f!.Contract?.Promotion);
                Assert.Equal(row.Wc, f.WeightClass);
                Assert.Equal(row.Sex, f.Sex);
            }
            // A flagship row names the org's real flagship.
            if (row.EventName is { } show) Assert.Equal(show, SignatureEvents.NameOf(promo!));
            Assert.InRange(row.Order, 0, 1);
        }
    }

    [Fact]
    public void AnAuthoredNumberedCard_IsAnnouncedOnTheTablesSchedule()
    {
        // Quispe vs Huanca: CDC, ~day 21, announced 21 days ahead. Friends refuse
        // each other on an ordinary card; an authored booking is the exception.
        var world = Build();
        var cdc = world.Promotions.Values.First(p => p.Abbreviation == "CDC");
        var (night, rows) = ForwardCalendar.BookedNights(world, cdc).Single();
        Assert.True(Math.Abs(night.DayNumber - world.StartDate.AddDays(21).DayNumber) <= 14);
        Assert.True(ForwardCalendar.IsShowWeek(cdc, night));
        var tick = new WorldTick();
        while (world.CurrentDate < night && !AiScheduled(world).Any(e => e.Promotion == cdc.Id && e.Date == night)) tick.AdvanceWeek(world);
        var card = AiScheduled(world).First(e => e.Promotion == cdc.Id && e.Date == night);
        var names = new[] { card.Card[^1].RedCorner, card.Card[^1].BlueCorner }.Select(id => world.Get(id)!.LastName).ToList();
        Assert.Contains("Quispe", names);
        Assert.Contains("Huanca", names);
        Assert.NotNull(ForwardCalendar.AuthoredBookingOn(world, card));
    }

    [Fact]
    public void RivalCardsNear_ListsTheClash_AndTheRealPenalty()
    {
        var world = Build();
        var player = world.PlayerPromotion;
        var date = AiScheduled(world).First().Date;
        var (rivals, penalty) = ForwardCalendar.RivalCardsNear(world, player, date);
        Assert.NotEmpty(rivals);
        Assert.All(rivals, r =>
        {
            Assert.NotEqual(player, r.Card.Promotion.Id);
            Assert.InRange(Math.Abs(r.Card.Date.DayNumber - date.DayNumber), 0, Financials.ClashWindowDays);
            // Charged exactly when the box office's rule counts it: an AI card
            // dated after the show is listed but not charged.
            Assert.Equal(r.Card.Date <= date, r.Charged);
        });
        Assert.Equal(Financials.ClashPenalty, penalty);
        Assert.Equal(Financials.ClashingCards(world, player, date).Count > 0 ? Financials.ClashPenalty : 0, penalty);
        // Narrowed to a region, only that region's cards.
        var asia = ForwardCalendar.RivalCardsNear(world, player, date, Region.Asia).Rivals;
        Assert.All(asia, r => Assert.Equal(Region.Asia, r.Card.Region));
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

    [Fact]
    public void APromotionFoundedMidGame_RunsAtTheOldCadence_AndBooksTheRoomItsDrawFits()
    {
        // Stand a startup up the way WorldEvents does (a shoestring City house
        // stocked from the free-agent market) a few months into the game.
        var world = Build(seed: 4);
        world.PlayerPromotion = default;
        var tick = new WorldTick();
        tick.AdvanceWeeks(world, 10);
        var founded = world.CurrentDate;
        var promo = new Promotion
        {
            Id = world.NewPromotionId(), Name = "Test Startup", Abbreviation = "TST",
            BaseRegion = Region.Europe, Balance = new Money(200_000m), Prestige = 8, Reputation = 50,
        };
        promo.TierClearance = OrgSizing.SizeOf(promo);
        world.Promotions[promo.Id] = promo;
        var classes = new[] { WeightClass.Lightweight, WeightClass.Welterweight };
        var signed = new List<Fighter>();
        var rng = new MmaManager.Core.Sim.Rng.SimRandom(42);
        foreach (var wc in classes)
            foreach (var fa in world.FreeAgents().Where(f => f.WeightClass == wc && f.Sex == Sex.Male && f.Status == FighterStatus.Active)
                         .OrderBy(f => f.Id.Value).Take(12).ToList())
            {
                fa.Contract = PromotionSetup.SignContract(world, promo, founded, fa, rng);
                signed.Add(fa);
            }
        PromotionSetup.ConfigureDivisions(world, promo, signed, classes, founded, 15, null, Sex.Male);
        Assert.Equal(OrgSize.City, OrgSizing.SizeOf(promo));

        // The old same-day booker's rule: one card on each of the org's show
        // weeks from the first tick after founding (it has the roster to fill them).
        int ShowWeeks(int weeks) => Enumerable.Range(1, weeks).Count(w => ForwardCalendar.IsShowWeek(promo, founded.AddDays(7 * w)));
        int Ran(int weeks) => world.Events.Values.Count(e => e.Promotion == promo.Id && e.Status == EventStatus.Completed
                                                             && e.Date <= founded.AddDays(7 * weeks));
        tick.AdvanceWeeks(world, 26);
        if (!promo.Defunct) Assert.InRange(Ran(26), ShowWeeks(26) - 1, ShowWeeks(26) + 1);
        tick.AdvanceWeeks(world, 26);
        if (!promo.Defunct) Assert.InRange(Ran(52), ShowWeeks(52) - 1, ShowWeeks(52) + 1);

        var cards = world.Events.Values.Where(e => e.Promotion == promo.Id && e.Status == EventStatus.Completed).OrderBy(e => e.Date).ToList();
        Assert.NotEmpty(cards);
        // No card before the first tick after founding (the old booker's earliest).
        Assert.True(cards[0].Date >= founded.AddDays(7));
        // Each card ran in the room its draw fitted on the night, not the one
        // guessed when it was announced: the same pick the old same-day booker made.
        foreach (var e in cards)
        {
            var room = world.Get(e.VenueId!.Value)!;
            Assert.True(e.Financials!.Attendance <= room.Capacity);
        }
    }

    [Fact]
    public void TheFlagship_PlaysTheRoomItsCardFills_NotTheClubAcrossTown()
    {
        // The old fight-night fit used PickVenueIn's estimate, which leaves out the
        // headliner's pull and the brand campaign: the AFC sold out 4,200 seats in
        // Las Vegas with ~21,000 wanting in and a 21,200 arena in the same town.
        var world = Build(seed: 8);
        world.PlayerPromotion = default;
        var start = world.CurrentDate;
        new WorldTick().AdvanceWeeks(world, 13);
        var afc = world.Promotions.Values.OrderByDescending(p => p.Prestige.Value).ThenBy(p => p.Id.Value).First();
        var cards = world.Events.Values
            .Where(e => e.Promotion == afc.Id && e.Status == EventStatus.Completed && e.Date > start
                        && !SignatureEvents.IsSignature(world, e) && !Storylines.IsSuperfight(e))
            .ToList();
        Assert.NotEmpty(cards);
        foreach (var e in cards)
        {
            var room = world.Get(e.VenueId!.Value)!;
            // No bigger room in town that this card would have filled (demand is
            // re-read after the night, when fame has moved a little: allow 10%).
            int demand = Financials.Demand(world, e);
            var missed = world.Venues.Values.Where(v => v.City == room.City && v.Capacity > room.Capacity)
                .FirstOrDefault(v => demand >= v.Capacity * (Financials.MoveUpFill + 0.10));
            Assert.True(missed is null,
                $"{e.Name}: {room.Capacity:N0} seats for a demand of {demand:N0} ({missed?.Capacity:N0} stood empty)");
        }
    }

    [Fact]
    public void BestRoom_MovesUpOnlyToARoomTheCardNearlyFills_AndNeverBelowTheEstimate()
    {
        var world = Build(seed: 8);
        world.PlayerPromotion = default;
        new WorldTick().AdvanceWeeks(world, 1);   // the calendar announces on the tick
        var afc = world.Promotions.Values.OrderByDescending(p => p.Prestige.Value).ThenBy(p => p.Id.Value).First();
        var ev = world.Events.Values.Where(e => e.Promotion == afc.Id && e.Status == EventStatus.Scheduled && e.Card.Count > 0 && e.VenueId is not null)
            .OrderBy(e => e.Date).ThenBy(e => e.Id.Value).First();
        var city = world.Get(ev.VenueId!.Value)!.City;
        var rooms = world.Venues.Values.Where(v => v.City == city).OrderBy(v => v.Capacity).ToList();
        Assert.True(rooms.Count >= 2);

        // Never below the fanbase estimate's room; above it only into a room the
        // card's own draw fills to MoveUpFill.
        var estimate = MmaManager.Core.Sim.Geography.WorldMap.PickVenueIn(world, afc, city)!;
        var pick = Financials.BestRoom(world, ev, city)!;
        Assert.True(pick.Capacity >= estimate.Capacity);
        if (pick.Id != estimate.Id)
            Assert.True(Financials.Demand(world, ev) >= pick.Capacity * Financials.MoveUpFill);

        // A nobody (no brand, no names) draws a few hundred: it stays in the
        // estimate's room.
        var nobody = new Promotion
        {
            Id = world.NewPromotionId(), Name = "Nobody", Abbreviation = "NBD",
            BaseRegion = world.Get(city)!.Region, Prestige = 1, Reputation = 1,
        };
        world.Promotions[nobody.Id] = nobody;
        var small = new Event { Id = world.NewEventId(), Promotion = nobody.Id, Name = "Nobody 1", Date = ev.Date, Region = nobody.BaseRegion, VenueId = rooms[^1].Id };
        Assert.Equal(MmaManager.Core.Sim.Geography.WorldMap.PickVenueIn(world, nobody, city)!.Id, Financials.BestRoom(world, small, city)!.Id);
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
