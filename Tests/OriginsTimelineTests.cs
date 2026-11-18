using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Generation.Origins;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Org;
using MmaManager.Persistence;

namespace MmaManager.Tests;

/// <summary>One AI-run Origins world, ten years on (to the end of 2002: every Era 1-2 window has closed).</summary>
public sealed class OriginsDecadeFixture
{
    public World World { get; }

    public OriginsDecadeFixture()
    {
        World = OriginsWorldTests.AiWorld(11);
        var tick = new WorldTick();
        while (World.CurrentDate.Year < 2003) tick.AdvanceWeek(World);
    }
}

/// <summary>
/// Origins phase 2: the timeline engine. The chapters fire in order, inside their windows, for whoever made
/// them happen; Eras 1-2 change the world (rivals, the first star, the Backlash's bans and licences, rounds
/// and judges, gloves, the first weight split); the story reads back; and today's mode never sees any of it.
/// </summary>
public class OriginsTimelineTests : IClassFixture<OriginsDecadeFixture>
{
    private readonly OriginsDecadeFixture _decade;
    public OriginsTimelineTests(OriginsDecadeFixture decade) => _decade = decade;

    private static Promotion House(World w, string abbr) => w.Promotions.Values.Single(p => p.Abbreviation == abbr);
    private static DateOnly Month(int m) => new DateOnly(1993, 1, 1).AddMonths(m);

    /// <summary>Mark chapters as fired (a test shortcut through the record the engine itself writes).</summary>
    private static void Fired(World w, DateOnly on, params string[] keys)
    {
        foreach (var k in keys)
        {
            Era.Fire(w, k, on);
            foreach (var u in OriginsChapters.ByKey(k).Unlocks)
                if (Era.TryFromChapterUnlock(u, out var cap)) Era.Unlock(w, cap);
        }
    }

    // ---- today's mode -----------------------------------------------------------------

    [Fact]
    public void TodaysMode_NeverRunsTheTimeline()
    {
        var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 3 });
        OriginsTimeline.RunWeekly(w, w.CurrentDate.AddDays(7));
        new WorldTick().AdvanceWeeks(w, 6);
        Assert.Empty(w.Era.Chapters);
        Assert.Empty(w.Era.Unlocked);
        Assert.Empty(w.Era.Bans);
        Assert.Empty(w.Era.Sanctions);
        Assert.False(OriginsTimeline.Contested(w, Capability.SpecialtyCable));
        Assert.False(OriginsTimeline.ContestedScene(w, "NED"));
        Assert.All(w.Promotions.Values, p =>
        {
            Assert.Empty(OriginsReforms.Options(w, p, w.CurrentDate));
            Assert.All(p.Divisions, d => Assert.Null(d.Lightest));
        });
        Assert.All(w.Cities.Keys, c => Assert.False(Era.Banned(w, c, w.CurrentDate)));
        Assert.Empty(OriginsStory.Coming(w));
    }

    // ---- the table ----------------------------------------------------------------------

    [Fact]
    public void EveryUnlock_Parses_AndEveryChapterTheEngineCannotDetectSaysWhy()
    {
        foreach (var c in OriginsChapters.All)
            foreach (var u in c.Unlocks)
                Assert.True(Era.TryFromChapterUnlock(u, out _), $"{c.Key}: '{u}'");

        // A chapter whose condition waits for a later phase fires at its deadline: the gap is named honestly.
        var detected = new HashSet<string>
        {
            "Date", "SignSponsor", "SignMedia", "HoldEventInNewCountry", "CrownChampionInClass", "HoldEventAbove",
            "PromotionsAtLeast", "PromotionsOnTheMapAtLeast", "PromotionReachesTier", "FighterFameAtLeast", "UpsetOfStar",
            "BothFightersFameAtLeast", "CampReputationAtLeast", "SanctionedRegionsAtLeast", "ContinentsWithPromotionsAtLeast",
            "ChaptersFired", "AdoptRules", "SanctionedByCommission", "AllRoundersBeatSpecialists",
        };
        foreach (var c in OriginsChapters.All)
        {
            var cond = c.Structured.Condition;
            bool waits = !detected.Contains(cond.Split(':')[0]) || cond == "SignMedia:ForeignNetwork"
                         || c.Key.StartsWith("womens-", StringComparison.Ordinal);
            if (waits) Assert.True(OriginsStory.Gaps.ContainsKey(c.Key), $"{c.Key} can only fire at its deadline but names no gap");
        }
        Assert.All(OriginsStory.Gaps.Keys, k => OriginsChapters.ByKey(k));
    }

    // ---- a decade of it -------------------------------------------------------------------

    [Fact]
    public void ADecade_EveryEraOneAndTwoChapterFired_InOrder_InsideItsWindow()
    {
        var w = _decade.World;
        foreach (var c in OriginsChapters.All.Where(c => c.Era <= 2))
            Assert.True(w.Era.HasFired(c.Key), $"{c.Key} never fired");
        foreach (var r in w.Era.Chapters)
        {
            var c = OriginsChapters.ByKey(r.Key);
            Assert.True(r.FiredOn >= Month(c.EarliestMonth), $"{r.Key} fired {r.FiredOn} before its window");
            Assert.True(r.FiredOn <= Month(c.LatestMonth + 1).AddDays(7), $"{r.Key} fired {r.FiredOn} after its deadline");
            foreach (var req in c.Requires)
                Assert.True(w.Era.Chapter(req)!.FiredOn < r.FiredOn, $"{r.Key} fired before {req}");
            foreach (var u in c.Unlocks)
            {
                Era.TryFromChapterUnlock(u, out var cap);
                Assert.True(w.Era.IsUnlocked(cap), $"{r.Key} fired but {u} is locked");
            }
            // The variant matches who did it: no player here, so a named house is a rival.
            if (r.Variant == ChapterVariant.Rival) Assert.NotNull(r.Promotion);
            Assert.NotEqual(ChapterVariant.Player, r.Variant);
        }
        Assert.Equal(w.Era.Chapters.Count, w.Era.Chapters.Select(r => r.Key).Distinct().Count());
    }

    [Fact]
    public void ADecade_TheRivalsArrived_AndTheFirstStarIsAStar()
    {
        var w = _decade.World;
        var rivals = w.Era.Chapter("rivals-appear")!;
        Assert.NotNull(rivals.Promotion);
        Assert.DoesNotContain(OriginsPromotions.All, s => s.Abbreviation == w.Get(rivals.Promotion!.Value)!.Abbreviation);
        Assert.True(w.Promotions.Count > 3);
        // Every house founded after day one is of its era: open weight, or the lines the sport had drawn.
        foreach (var p in w.Promotions.Values.Where(p => p.Id.Value > 3))
            Assert.Equal(p.Divisions.Any(d => d.Open), p.Rules.Fight!.OpenWeight);   // open weight exactly when it runs no lines
        // A split house (any house, founder or not) is no longer open weight, and its booked bouts say which division they are in.
        foreach (var p in w.Promotions.Values.Where(p => p.Divisions.Any(d => !d.Open)))
        {
            Assert.False(p.Rules.Fight!.OpenWeight);
            foreach (var e in w.Events.Values.Where(e => e.Promotion == p.Id && e.Status == EventStatus.Scheduled))
                Assert.All(e.Card, f => { Assert.False(f.OpenWeight); Assert.Contains(p.Divisions, d => d.WeightClass == f.WeightClass); });
        }

        var star = w.Era.Chapter("first-star")!;
        Assert.NotNull(star.Fighter);
        Assert.True(w.Get(star.Fighter!.Value)!.StarPower);
    }

    [Fact]
    public void ADecade_TheBacklashClosedHalls_LeftOneOpen_AndNoNightWasFoughtInAClosedOne()
    {
        var w = _decade.World;
        var bans = w.Era.Bans;
        Assert.NotEmpty(bans);
        Assert.All(bans, b => Assert.Equal("USA", b.Country));
        var usa = w.Countries.Values.Single(c => c.Code == "USA");
        Assert.True(bans.Select(b => b.Area).Distinct().Count() < usa.Areas.Count, "every region of the United States was closed");
        Assert.Equal(w.Era.Chapter("bans-spread")!.FiredOn.AddDays(1), bans.Min(b => b.Since));

        foreach (var e in w.Events.Values.Where(e => e.Status == EventStatus.Completed && e.VenueId is not null))
        {
            var city = w.Get(w.Get(e.VenueId!.Value)!.City)!;
            Assert.False(bans.Any(b => b.Country == "USA" && w.Get(city.Country)!.Code == "USA" && b.Area == city.Area && b.ActiveOn(e.Date)),
                $"{e.Name} was fought in {city.Name} on {e.Date} under a ban");
        }
        // A licence came, and lifted a ban or opened a region.
        Assert.NotEmpty(w.Era.Sanctions);
        var first = w.Era.Chapter("first-sanctioned-state")!;
        Assert.Contains(w.Era.Sanctions, s => s.On <= first.FiredOn);
    }

    [Fact]
    public void ADecade_HousesTookUpRoundsGlovesAndWeightClasses_WithHonestBands()
    {
        var w = _decade.World;
        var cru = House(w, "CRU");
        // The bracket house of the long round has changed its rules (as somebody had to by the deadline's sport).
        Assert.Contains(w.Promotions.Values, p => OriginsReforms.HasRounds(p) && p.Rules.Fight is { Rounds: > 1 } && p.Abbreviation is "CRU" or "DLB");
        Assert.True(w.Era.IsUnlocked(Capability.Gloves));

        // Every split house: men's lines in weight order, each band starting one class above the line below.
        var split = w.Promotions.Values.Where(p => p.Divisions.Any(d => !d.Open)).ToList();
        Assert.NotEmpty(split);
        foreach (var p in split)
        {
            Assert.DoesNotContain(p.Divisions, d => d.Open);
            var lines = p.Divisions.Where(d => d.Sex == Sex.Male).OrderBy(d => d.WeightClass).ToList();
            WeightClass? below = null;
            foreach (var d in lines)
            {
                var expected = below is { } b ? b + 1 : WeightClass.Strawweight;
                Assert.Equal(expected == d.WeightClass ? null : expected, d.Lightest);
                Assert.NotNull(d.Title);
                below = d.WeightClass;
            }
            // Every man of the house belongs to exactly one division, and nobody holds a belt outside his band.
            foreach (var f in w.RosterOf(p.Id).Where(f => f.Status == FighterStatus.Active))
                Assert.Single(lines, d => d.Matches(f));
            foreach (var d in lines)
                if (w.Get(d.Title!.Value)!.CurrentChampion is { } champ) Assert.True(d.Matches(w.Get(champ)!), $"{p.Abbreviation} {d.WeightClass}/{d.Lightest} champion {w.Get(champ)!.FullName} is a {w.Get(champ)!.WeightClass}, title {w.Get(d.Title!.Value)!.Name} lineage {string.Join(",", w.Get(d.Title!.Value)!.Lineage.Select(r => r.WonOn))}");
        }
        // Every bout on the books at a split house is inside one of its divisions as they stand today, and a belt bout
        // is inside the belt's division. The one exception is the grandfathered card (PR #80: bouts already on an
        // announced card when the house draws new lines are fought as booked): a bout the newest lines put across two
        // divisions may stay, but only without a belt, and only on a night already announced when the lines were drawn
        // (within OriginsShows.LeadWeeks of that day, the furthest ahead a house announces a night). (Before #80 such bouts came off their cards; this test still said so
        // and passed on main only because no split there landed inside the last card's horizon.)
        foreach (var p in split)
        {
            var drawn = w.News.Where(n => n.Promotion == p.Id && n.Headline.StartsWith($"{p.Name} draws ", StringComparison.Ordinal))
                .Select(n => n.Date).DefaultIfEmpty(DateOnly.MinValue).Max();
            foreach (var e in w.Events.Values.Where(e => e.Promotion == p.Id && e.Status == EventStatus.Scheduled))
                foreach (var f in e.Card)
                {
                    var a = w.Get(f.RedCorner)!; var b2 = w.Get(f.BlueCorner)!;
                    if (p.Divisions.Any(d => d.Matches(a) && d.Matches(b2) && (f.Title is null || d.Title == f.Title))) continue;
                    string what = $"{p.Abbreviation} {e.Name} {e.Date}: {a.WeightClass} vs {b2.WeightClass} filed {f.WeightClass}, title {f.Title}, lines drawn {drawn}";
                    Assert.True(f.Title is null && !f.IsTitleFight, what);                                  // no belt outside its division
                    Assert.True(drawn != DateOnly.MinValue && e.Date >= drawn && e.Date <= drawn.AddDays(7 * OriginsShows.LeadWeeks), what);   // grandfathered
                }
        }
        _ = cru;
    }

    [Fact]
    public void TheStory_ReadsBackEveryChapter_WithEverySlotFilled_AndKeepsTheFutureVague()
    {
        var w = _decade.World;
        var story = OriginsStory.SoFar(w);
        Assert.Equal(w.Era.Chapters.Count, story.Count);
        foreach (var v in story)
        {
            Assert.DoesNotContain("{", v.Headline + v.Body + v.Digest);
            Assert.False(string.IsNullOrWhiteSpace(v.Title));
            Assert.InRange(v.Era, 1, 6);
            Assert.Equal(OriginsChapters.ByKey(v.Key).Unlocks.Length, v.Unlocked.Count);
            Assert.DoesNotContain(v.Unlocked, u => u.Contains(':'));
        }
        Assert.Equal(story.Select(s => s.Date).OrderBy(d => d), story.Select(s => s.Date));
        var coming = OriginsStory.Coming(w);
        Assert.NotEmpty(coming);
        Assert.All(coming, c =>
        {
            Assert.Contains(c.When, new[] { "any time now", "in the next year or two", "further off" });
            Assert.False(w.Era.HasFired(c.Key));
        });
        Assert.True(OriginsStory.CurrentEra(w).Era >= 2);
    }

    // ---- determinism and the save --------------------------------------------------------

    [Fact]
    public void SameSeedTwice_GivesTheSameHistory()
    {
        var a = OriginsWorldTests.AiWorld(13);
        var b = OriginsWorldTests.AiWorld(13);
        var tick = new WorldTick();
        while (a.CurrentDate < new DateOnly(1998, 6, 1)) tick.AdvanceWeek(a);
        while (b.CurrentDate < new DateOnly(1998, 6, 1)) tick.AdvanceWeek(b);
        Assert.True(a.Era.Chapters.Count >= 8);
        var diffs = PersistenceGraph.Diff(a, b);
        Assert.True(diffs.Count == 0, "Same seed, different history:\n" + PersistenceGraph.Describe(diffs));
    }

    [Fact]
    public void SaveLoadRunOn_EqualsNeverSaved_InTheMiddleOfTheBacklash()
    {
        var world = OriginsWorldTests.AiWorld(17);
        var tick = new WorldTick();
        while (!(world.Era.Bans.Count > 0 && world.Era.HasFired("first-weight-split")) && world.CurrentDate.Year < 2003)
            tick.AdvanceWeek(world);
        Assert.NotEmpty(world.Era.Bans);

        string path = Path.Combine(Path.GetTempPath(), $"mma_timeline_{Guid.NewGuid():N}.db");
        try
        {
            SaveGame.Save(world, path);
            var loaded = SaveGame.Load(path);
            var diffs = PersistenceGraph.Diff(world, loaded);
            Assert.True(diffs.Count == 0, "Changed across the save:\n" + PersistenceGraph.Describe(diffs));

            new WorldTick().AdvanceWeeks(world, 30);
            new WorldTick().AdvanceWeeks(loaded, 30);
            diffs = PersistenceGraph.Diff(world, loaded);
            Assert.True(diffs.Count == 0, "Saved and never-saved worlds ran apart:\n" + PersistenceGraph.Describe(diffs));
        }
        finally { File.Delete(path); }
    }

    // ---- the player's own path into history -----------------------------------------------

    [Fact]
    public void ThePlayer_AdoptsRoundsFirst_AndTheChapterIsHis()
    {
        var w = new WorldBuilder().Build(WorldBuilder.Options.Origins(5, playerIndex: 2));   // the Garage
        var dlb = w.Get(w.PlayerPromotion)!;
        Assert.Equal("DLB", dlb.Abbreviation);
        w.CurrentDate = Month(40);
        Assert.Empty(OriginsReforms.Options(w, dlb, w.CurrentDate));             // the press has not turned: nobody asks for rounds yet
        Assert.False(OriginsReforms.Adopt(w, dlb, OriginsReforms.Rounds, w.CurrentDate));

        Fired(w, Month(2), "crucible-first-night", "first-star");
        Fired(w, Month(39), "press-turns");
        Assert.Contains(OriginsReforms.Options(w, dlb, w.CurrentDate), o => o.Chapter == OriginsReforms.Rounds);
        Assert.True(OriginsReforms.Adopt(w, dlb, OriginsReforms.Rounds, w.CurrentDate));

        var r = w.Era.Chapter(OriginsReforms.Rounds)!;
        Assert.Equal(ChapterVariant.Player, r.Variant);
        Assert.Equal(dlb.Id, r.Promotion);
        Assert.True(w.Era.IsUnlocked(Capability.Rounds) && w.Era.IsUnlocked(Capability.Judges));
        Assert.Equal(3, dlb.Rules.Fight!.Rounds);
        Assert.True(dlb.Rules.Fight.Judges);
        Assert.Contains("Duelo Livre", OriginsStory.View(w, r)!.Headline);
        Assert.Contains(w.Mail, m => m.Subject == OriginsStory.View(w, r)!.Headline);
        Assert.Empty(OriginsReforms.Options(w, dlb, w.CurrentDate).Where(o => o.Chapter == OriginsReforms.Rounds));
    }

    [Fact]
    public void TheFirstSplit_RetiresTheOpenBeltHonestly_AndCutsHeavyAndLight()
    {
        var w = OriginsWorldTests.AiWorld(6);
        var sfa = House(w, "SFA");
        w.CurrentDate = Month(60);
        Fired(w, Month(2), "crucible-first-night", "first-star");
        Fired(w, Month(34), "press-turns");
        Fired(w, Month(50), "rounds-and-judges", "gloves-adopted");

        // The open belt has a champion: the lightest man on the roster.
        var open = sfa.Divisions.Single(d => d.Open);
        var belt = w.Get(open.Title!.Value)!;
        var champ = w.RosterOf(sfa.Id).OrderBy(f => f.WeightClass).ThenBy(f => f.Id.Value).First();
        belt.CurrentChampion = champ.Id;
        belt.Lineage.Add(new TitleReign { Champion = champ.Id, WonOn = Month(20) });
        // A man promised a shot at the open belt.
        var hopeful = w.RosterOf(sfa.Id).Where(f => f.Id != champ.Id).OrderByDescending(f => f.WeightClass).ThenBy(f => f.Id.Value).First();
        var promise = new Promise
        {
            Id = w.NewPromiseId(), Fighter = hopeful.Id, Promotion = sfa.Id, Kind = PromiseKind.TitleShot,
            Title = belt.Id, Made = Month(55), Deadline = Month(70),
        };
        w.Promises.Add(promise);
        Assert.True(sfa.Rules.Fight!.OpenWeight);

        Assert.Contains(OriginsReforms.Options(w, sfa, w.CurrentDate), o => o.Chapter == "first-weight-split");
        Assert.True(OriginsReforms.Adopt(w, sfa, "first-weight-split", w.CurrentDate));
        Assert.False(sfa.Rules.Fight!.OpenWeight);   // a split house fights inside its divisions and weighs in
        // The promise follows the man's weight to the belt of his new division, same deadline.
        Assert.True(promise.Live);
        Assert.Equal(sfa.Divisions.Single(d => d.Matches(hopeful)).Title, promise.Title);
        Assert.Equal(Month(70), promise.Deadline);

        Assert.DoesNotContain(sfa.Divisions, d => d.Open);
        var mw = sfa.Divisions.Single(d => d.WeightClass == WeightClass.Middleweight);
        var hw = sfa.Divisions.Single(d => d.WeightClass == WeightClass.Heavyweight);
        Assert.Equal(WeightClass.Strawweight, mw.Lightest);
        Assert.Equal(WeightClass.LightHeavyweight, hw.Lightest);
        Assert.Null(w.Get(mw.Title!.Value)!.CurrentChampion);
        Assert.Null(w.Get(hw.Title!.Value)!.CurrentChampion);
        // The old reign ended without a defeat, and the news says so; he starts at the top of his band.
        Assert.True(belt.Lineage[^1].Vacated);
        Assert.Equal(w.CurrentDate, belt.Lineage[^1].LostOn);
        Assert.Null(belt.CurrentChampion);
        Assert.Contains(w.News, n => n.Headline.Contains("retires its open-weight belt") && n.Body.Contains(champ.FullName));
        var band = champ.WeightClass >= WeightClass.LightHeavyweight ? hw : mw;
        Assert.Equal(champ.Id, band.Rankings[0]);
        // The chapter waits for a champion to be crowned in a class (it was not handed over).
        Assert.False(w.Era.HasFired("first-weight-split"));
        Assert.All(w.RosterOf(sfa.Id), f => Assert.Single(sfa.Divisions, d => d.Matches(f)));
    }

    [Fact]
    public void ABan_ClosesTheHomeRoom_TheHouseGoesIntoExile_AndAnAnnouncedCardMoves()
    {
        var w = OriginsWorldTests.AiWorld(8);
        var cru = House(w, "CRU");
        var la = cru.HomeCity!.Value;
        w.CurrentDate = Month(40);
        // A night already on the books at home.
        var ev = new EventService().CreateEvent(w, cru.Id, "CRU test night", w.CurrentDate.AddDays(20),
            Array.Empty<Matchup>(), la);
        Assert.Equal(la, w.Get(ev.VenueId!.Value)!.City);

        OriginsBacklash.BanWave(w, w.CurrentDate, first: true);
        Assert.False(Era.Banned(w, la, w.CurrentDate));                 // tonight's cards were fought before the news
        Assert.True(Era.Banned(w, la, w.CurrentDate.AddDays(1)));
        w.CurrentDate = w.CurrentDate.AddDays(1);
        Assert.False(OrgReach.CanBookIn(w, cru, la));
        var usa = w.Countries.Values.Single(c => c.Code == "USA");
        Assert.Contains(Enumerable.Range(0, usa.Areas.Count), a => !w.Era.Bans.Any(b => b.Area == a));   // one region stays open

        var exile = OriginsShows.NightVenue(w, cru, w.CurrentDate.AddDays(30), 99);
        Assert.NotNull(exile);
        Assert.False(Era.Banned(w, exile!.City, w.CurrentDate.AddDays(30)));
        Assert.True(ev.Status == EventStatus.Cancelled || !Era.Banned(w, w.Get(ev.VenueId!.Value)!.City, ev.Date));
        Assert.Contains(w.News, n => n.Headline.StartsWith("Banned in the West"));
    }

    [Fact]
    public void AContestedScene_HostsTheFirstNight_ButGrowsNoTalentUntilItWakes()
    {
        var w = new WorldBuilder().Build(WorldBuilder.Options.Origins(4, playerIndex: 1));   // the Association
        var sfa = w.Get(w.PlayerPromotion)!;
        w.CurrentDate = Month(16);
        Assert.False(OriginsTimeline.ContestedScene(w, "CAN"));                // the first star has not come
        Fired(w, Month(1), "crucible-first-night", "shinken-and-duelo-open");
        Fired(w, Month(8), "first-star");
        Assert.True(OriginsTimeline.ContestedScene(w, "CAN"));
        Assert.False(Era.AllowsScene(w, "CAN"));
        Assert.DoesNotContain("CAN", Era.Scenes(w));
        var toronto = w.Cities.Values.Single(c => c.Name == "Toronto");
        Assert.True(Era.AllowsSceneIn(w, toronto.Id));

        var card = OriginsShows.SuggestCard(w, sfa.Id, w.CurrentDate.AddDays(14));
        var ev = OriginsShows.ScheduleCard(w, sfa.Id, w.CurrentDate.AddDays(14), card, city: toronto.Id);
        Assert.NotNull(ev);
        Assert.Equal(toronto.Id, w.Get(ev!.VenueId!.Value)!.City);
        var tick = new WorldTick();
        while (ev.Status == EventStatus.Scheduled && w.CurrentDate < ev.Date) tick.AdvanceWeeksStoppingForPlayer(w, 1);
        if (ev.Status == EventStatus.Scheduled) tick.RunEventLive(w, ev, playByPlay: false);
        OriginsTimeline.RunWeekly(w, w.CurrentDate.AddDays(7));

        var r = w.Era.Chapter("scene-low-countries-canada")!;
        Assert.Equal(ChapterVariant.Player, r.Variant);
        Assert.Equal(toronto.Id, r.City);
        Assert.True(Era.AllowsScene(w, "CAN") && Era.AllowsScene(w, "NED"));
        Assert.Contains("Toronto", OriginsStory.View(w, r)!.Headline + OriginsStory.View(w, r)!.Body);
    }

    // ---- PR #59 review fixes ---------------------------------------------------------------

    [Fact]
    public void ALicensedRegion_IsNeverBannedAgain_AndALicenceBeatsAnyBan()
    {
        var w = OriginsWorldTests.AiWorld(8);
        var cru = House(w, "CRU");
        var la = cru.HomeCity!.Value;
        int area = w.Get(la)!.Area!.Value;
        w.CurrentDate = Month(40);
        OriginsBacklash.BanWave(w, w.CurrentDate, first: true);
        Assert.True(Era.Banned(w, la, w.CurrentDate.AddDays(1)));

        w.CurrentDate = Month(50);
        OriginsBacklash.LicenceAtFounding(w, cru, w.CurrentDate);       // a licence for CRU's home region
        Assert.False(Era.Banned(w, la, w.CurrentDate));

        // The second wave never closes it again, and even a ban written over it would not hold.
        OriginsBacklash.BanWave(w, Month(52), first: false);
        Assert.DoesNotContain(w.Era.Bans, b => b.Area == area && b.Since > Month(50));
        w.Era.Bans.Add(new SceneBan { Country = "USA", Area = area, Since = Month(53), Until = Month(90) });
        Assert.False(Era.Banned(w, la, Month(60)));

    }

    [Fact]
    public void EachBanLapse_IsAnnouncedExactlyOnce()
    {
        var w = OriginsWorldTests.AiWorld(9);
        // Lapses on every day of a month, whatever weekday the ticks fall on.
        var start = new DateOnly(1996, 3, 1);
        for (int d = 0; d < 31; d++)
            w.Era.Bans.Add(new SceneBan { Country = "USA", Area = d % 4, Since = start.AddDays(-400), Until = start.AddDays(d) });
        int before = w.News.Count(n => n.Headline.StartsWith("The ban lapses"));
        var tick = new DateOnly(1996, 1, 5);
        for (int week = 0; week < 20; week++, tick = tick.AddDays(7))
            if (tick.AddDays(-7).Month != tick.Month) { w.CurrentDate = tick; OriginsBacklash.RunMonthly(w, tick); }
        Assert.Equal(31, w.News.Count(n => n.Headline.StartsWith("The ban lapses")) - before);
    }

    [Fact]
    public void Distress_ReachesItsDeepestTier_AtAnOriginsHousesOwnCeiling()
    {
        var w = OriginsWorldTests.AiWorld(2);
        var dlb = House(w, "DLB");
        dlb.Balance = new Money(-40_000m);                               // past half of an Origins City house's 75,000
        Assert.Equal(2, Solvency.Distress(dlb, w));
        Assert.Equal(1, Solvency.Distress(dlb));                          // (today's scale, unchanged)
        Assert.Equal(-75_000m, Solvency.CeilingFor(dlb, w));
        var modern = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 2 });
        var m = modern.Promotions.Values.First();
        m.Balance = new Money(-40_000m);
        Assert.Equal(Solvency.Distress(m), Solvency.Distress(m, modern));
    }

    [Fact]
    public void TheBoomCanReachItsFourteenHouses_BeforeItsDeadline()
    {
        var w = OriginsWorldTests.AiWorld(3);
        Assert.True(OriginsFounding.Ceiling(w) < 14);
        Fired(w, Month(140), "breakout-tv-moment");
        Assert.True(OriginsFounding.Ceiling(w) >= 14);
        Assert.True(OriginsChapters.ByKey("boom-new-promotions").Structured.Value <= OriginsFounding.Ceiling(w));
    }

    [Fact]
    public void ASignedDeal_CarriesAnEraName_AndSoDoesItsChapter()
    {
        var w = OriginsWorldTests.AiWorld(4);
        var sfa = House(w, "SFA");
        w.CurrentDate = Month(60);
        sfa.Prestige = 30;                                              // regional, and past the specialty bar
        sfa.TierClearance = OrgSizing.SizeOf(sfa);
        Era.Unlock(w, Capability.SpecialtyCable);
        var rng = new MmaManager.Core.Sim.Rng.SimRandom(5);
        for (int m = 0; m < 24 && sfa.MediaDeals.Count == 0; m++) Core.Sim.Finance.MediaService.RunMonthly(w, sfa, w.CurrentDate, rng);
        var deal = Assert.Single(sfa.MediaDeals);
        Assert.Equal("Nocturne Cable", deal.Partner);
        var doer = OriginsTimeline.Detect(w, OriginsChapters.ByKey("late-night-cable"), w.CurrentDate);
        Assert.Equal("Nocturne Cable", doer?.Partner);

        // Sponsors: every deal a house signs in its first years names an era brand.
        var dlb = House(w, "DLB");
        dlb.SponsorDeals.Clear();
        for (int m = 0; m < 30; m++)
        {
            w.CurrentDate = Month(3 + m);
            Core.Sim.Finance.Sponsorships.RunMonthly(w, dlb, w.CurrentDate, rng);
        }
        Assert.NotEmpty(dlb.SponsorDeals);
        Assert.All(dlb.SponsorDeals, d => Assert.Contains(OriginsBroadcasters.Sponsors, s => s.Name == d.Partner));
    }

    [Fact]
    public void ThePlayer_HearsWhatHeCanAdopt_AndIsToldWhyNot()
    {
        var w = new WorldBuilder().Build(WorldBuilder.Options.Origins(5, playerIndex: 2));   // the Garage
        var dlb = w.Get(w.PlayerPromotion)!;
        w.CurrentDate = Month(40);
        Assert.Equal("The sport isn't ready for that yet: nobody is asking for it.", OriginsReforms.Refusal(w, dlb, OriginsReforms.Rounds, w.CurrentDate));
        Fired(w, Month(2), "crucible-first-night", "first-star");
        Fired(w, Month(39), "press-turns");
        Assert.Null(OriginsReforms.Refusal(w, dlb, OriginsReforms.Rounds, w.CurrentDate));
        Assert.NotNull(OriginsReforms.Refusal(w, dlb, "first-weight-split", w.CurrentDate));

        // The desk hears of it once, the month it opened.
        OriginsReforms.RunMonthly(w, new DateOnly(1996, 4, 5));
        OriginsReforms.RunMonthly(w, new DateOnly(1996, 5, 3));
        Assert.Single(w.Mail, m => m.Subject == "We could adopt: Rounds and judges");

        Assert.True(OriginsReforms.TryAdopt(w, dlb, OriginsReforms.Rounds, w.CurrentDate, out var none));
        Assert.Null(none);
        Assert.False(OriginsReforms.TryAdopt(w, dlb, OriginsReforms.Rounds, w.CurrentDate, out var why));
        Assert.Equal("You already fight in rounds with judges.", why);
    }

    [Fact]
    public void APlayersNight_IsRefused_WhenEveryHallIsBanned()
    {
        var w = new WorldBuilder().Build(WorldBuilder.Options.Origins(4, playerIndex: 0));
        w.CurrentDate = Month(40);
        foreach (var code in Era.Scenes(w).ToList())
            w.Era.Bans.Add(new SceneBan { Country = code, Since = Month(39), Until = Month(90) });
        var ev = new PlayerBooking().TryCreateEvent(w, Month(41), out var refused);
        Assert.Null(ev);
        Assert.Contains("closed by a ban", refused);
        Assert.Throws<InvalidOperationException>(() => new PlayerBooking().CreateEvent(w, Month(41)));
        Assert.DoesNotContain(w.Events.Values, e => e.VenueId is null && e.Promotion == w.PlayerPromotion);
    }

    // ---- the Origins screens' sim asks (PR #58) ---------------------------------------------

    [Fact]
    public void ABracketNight_DrawsThePreviewedField_AndSavesItsBracket()
    {
        var w = new WorldBuilder().Build(WorldBuilder.Options.Origins(5, playerIndex: 0));
        var cru = w.Get(w.PlayerPromotion)!;
        var date = w.CurrentDate.AddDays(21);
        var preview = TournamentService.PreviewField(w, cru, date, except: null);
        Assert.True(preview.Full);
        Assert.Equal(8, preview.Seeds.Count);
        Assert.All(preview.Seeds, f => Assert.Equal(Sex.Male, f.Sex));
        Assert.DoesNotContain(preview.Alternate, preview.Seeds);

        var night = OriginsShows.ScheduleTournament(w, cru.Id, date)!;
        var again = TournamentService.PreviewField(w, cru, date, except: night.Id);
        Assert.Equal(preview.Seeds.Select(f => f.Id), again.Seeds.Select(f => f.Id));
        var tick = new WorldTick();
        while (w.CurrentDate < night.Date) tick.AdvanceWeeksStoppingForPlayer(w, 1);
        if (night.Status == EventStatus.Scheduled) tick.RunEventLive(w, night, playByPlay: false);

        // The bracket as it ran: round 1 seats the previewed seeds; every bout carries its round.
        Assert.NotEmpty(night.Bracket);
        var first = night.Bracket.Where(s => s.Round == 1).OrderBy(s => s.Slot).ToList();
        Assert.Equal(4, first.Count);
        Assert.Equal(again.Seeds.Select(f => f.Id).OrderBy(i => i.Value),
            first.SelectMany(s => new[] { s.Red, s.Blue }).OfType<FighterId>().OrderBy(i => i.Value));
        Assert.Equal(again.Alternate?.Id, night.BracketAlternate);
        Assert.All(night.Card, f => Assert.NotNull(f.BracketRound));
        Assert.All(night.Bracket.Where(s => s.Fight is not null),
            s => Assert.Equal(s.Round, night.Card.Single(f => f.Id == s.Fight).BracketRound));
        Assert.Equal(3, night.Bracket.Max(s => s.Round));                 // eight men: three rounds
        Assert.Single(night.Bracket, s => s.Round == 3);                 // one final, however many walkovers came before it
    }

    [Fact]
    public void OriginsObjectives_FitEachHousesCadence()
    {
        foreach (var (index, abbr, shows) in new[] { (0, "CRU", 3), (1, "SFA", 4), (2, "DLB", 5) })
        {
            var w = new WorldBuilder().Build(WorldBuilder.Options.Origins(3, playerIndex: index));
            var p = w.Get(w.PlayerPromotion)!;
            Assert.Equal(abbr, p.Abbreviation);
            var goal = p.Objectives.Single(o => o.Kind == ObjectiveKind.RunEvents);
            Assert.Equal(shows, goal.Target);
            Assert.Contains($"{shows} shows", goal.Description);
        }
        // Today's mode still asks for six.
        var modern = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 3 });
        Assert.Equal(6, modern.Get(modern.PlayerPromotion)!.Objectives.Single(o => o.Kind == ObjectiveKind.RunEvents).Target);
    }

    [Fact]
    public void TheMediaLadder_NamesTheEraBroadcasters_InAnOriginsWorld()
    {
        var w = new WorldBuilder().Build(WorldBuilder.Options.Origins(3));
        var view = Core.Sim.Finance.MediaService.LadderView(w, w.Get(w.PlayerPromotion)!);
        var today = new[] { "MainCard", "PrimeView", "BoxSeat", "Combat One", "PrimeSport", "Streamflix" };
        Assert.All(view, r => Assert.DoesNotContain(today, n => r.Terms.Contains(n)));
        Assert.Contains(view, r => r.Kind == MediaKind.PpvCarrier && r.Terms.Contains("Keystone Event Network"));
        Assert.Contains(view, r => r.Kind == MediaKind.CableSports && r.Gate.StartsWith("Not in this era yet"));
    }

    [Fact]
    public void APlayersCard_DefaultsToTheHousesOwnRoom()
    {
        var w = new WorldBuilder().Build(WorldBuilder.Options.Origins(4, playerIndex: 2));
        var dlb = w.Get(w.PlayerPromotion)!;
        var ev = new PlayerBooking().CreateEvent(w, w.CurrentDate.AddDays(21), city: dlb.HomeCity);
        Assert.Equal(OriginsShows.HomeVenue(w, dlb)!.Id, ev.VenueId);
        var none = new PlayerBooking().CreateEvent(w, w.CurrentDate.AddDays(28));
        Assert.Equal(OriginsShows.HomeVenue(w, dlb)!.Id, none.VenueId);
    }
}
