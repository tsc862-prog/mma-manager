using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Generation;
using MmaManager.Core.Sim.Generation.Origins;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Rng;
using MmaManager.Persistence;

namespace MmaManager.Tests;

/// <summary>One AI-run Origins world to the start of 2008 (every Era 3 window has closed), with the fighter ids at which cross-training began and the specialists fell.</summary>
public sealed class OriginsSpreadFixture
{
    public World World { get; }
    public World DayOne { get; }
    public int FirstIdAfterCamp { get; } = int.MaxValue;
    public int FirstIdAfterFall { get; } = int.MaxValue;

    public OriginsSpreadFixture()
    {
        DayOne = OriginsWorldTests.AiWorld(11);
        World = OriginsWorldTests.AiWorld(11);
        var tick = new WorldTick();
        int NextId() => World.Fighters.Keys.Select(k => k.Value).DefaultIfEmpty(0).Max() + 1;
        while (World.CurrentDate.Year < 2008)
        {
            tick.AdvanceWeek(World);
            if (FirstIdAfterCamp == int.MaxValue && World.Era.HasFired(OriginsCamps.FirstCampChapter)) FirstIdAfterCamp = NextId();
            if (FirstIdAfterFall == int.MaxValue && World.Era.HasFired(OriginsCamps.SpecialistsFallChapter)) FirstIdAfterFall = NextId();
        }
    }
}

/// <summary>
/// Origins phase 3: the sport spreads. Scenes wake country by country and grow men and gyms of their own; a
/// second centre rises; rooms learn to teach the whole game and the intake turns from specialists to
/// all-rounders; smaller men arrive as the weight ladder fills; the commissions agree one code and the
/// rankings across promotions are recognised. Today's mode never sees any of it.
/// </summary>
public class OriginsSpreadTests : IClassFixture<OriginsSpreadFixture>
{
    private readonly OriginsSpreadFixture _run;
    public OriginsSpreadTests(OriginsSpreadFixture run) => _run = run;

    private static Promotion House(World w, string abbr) => w.Promotions.Values.Single(p => p.Abbreviation == abbr);
    private static DateOnly Month(int m) => new DateOnly(1993, 1, 1).AddMonths(m);

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
    public void TodaysMode_EveryRoomTeachesEverything_AndNothingHereRuns()
    {
        var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 4 });
        Assert.All(w.Camps.Values, c => Assert.True(OriginsCamps.CrossTrains(w, c)));
        Assert.All(w.Fighters.Values, f => Assert.False(OriginsCamps.TrainsOneArt(w, f)));
        Assert.Empty(OriginsScenes.Scenes(w));
        Assert.Null(OriginsScenes.SecondCentre(w));
        Assert.Null(OriginsScenes.WokeOn(w, "USA"));
        Assert.True(OriginsReforms.RankingsRecognised(w));
        Assert.Equal(0, OriginsTalent.CrossTrainedShare(w, w.CurrentDate));
        Assert.Empty(OriginsCamps.Options(w, w.Get(w.PlayerPromotion)!, w.CurrentDate));
        Assert.NotNull(OriginsCamps.Refusal(w, w.Get(w.PlayerPromotion)!, w.Camps.Keys.First(), w.CurrentDate));
        Assert.False(OriginsReforms.BoundByCode(w, w.Get(w.PlayerPromotion)!));
        Assert.NotEmpty(w.PoundForPound);   // the sport's lists are published as they always were
    }

    // ---- day one --------------------------------------------------------------------

    [Fact]
    public void DayOne_IsASportOfSpecialists_InOneArtRooms()
    {
        var w = _run.DayOne;
        var men = w.Fighters.Values.Where(f => f.Status == FighterStatus.Active).ToList();
        Assert.True(men.Count(OriginsTalent.IsAllRounder) <= men.Count / 20, $"{men.Count(OriginsTalent.IsAllRounder)} of {men.Count} all-rounders on day one");
        Assert.DoesNotContain(men, f => !f.Designed && f.PrimaryStyle == PrimaryStyle.WellRounded);
        Assert.All(w.Camps.Values, c => Assert.False(OriginsCamps.CrossTrains(w, c)));
        Assert.All(men, f => Assert.True(OriginsCamps.TrainsOneArt(w, f)));
        Assert.Empty(w.PoundForPound);            // no list of the sport's best before the code
        Assert.Empty(w.GlobalRankings.Values.SelectMany(l => l));
        Assert.Equal(3, OriginsScenes.Scenes(w).Count);
        Assert.All(OriginsScenes.Scenes(w), s => Assert.Equal(w.StartDate, s.WokeOn));
    }

    // ---- fifteen years of it ---------------------------------------------------------------

    [Fact]
    public void FifteenYears_TheScenesWoke_EachGrewAGym_AndTheIntakeFollowedThem()
    {
        var w = _run.World;
        var scenes = OriginsScenes.Scenes(w);
        Assert.True(scenes.Count >= 15, $"only {scenes.Count} scenes awake by 2008");
        foreach (var s in scenes)
        {
            Assert.True(s.Gyms >= 1, $"{s.Code} woke {s.WokeOn} and has no gym");
            if (s.Chapter is { } key) Assert.Equal(w.Era.Chapter(key)!.FiredOn, s.WokeOn);
        }
        Assert.Equal(1.0, scenes.Sum(s => s.TalentShare), 3);
        // A scene that woke grows men of its own: the later scenes have produced some.
        Assert.Contains(scenes, s => s.Chapter is not null && s.Men > 0);
    }

    [Fact]
    public void FifteenYears_ASecondCentreRose_WithAWarmerMarket_AndItsOwnRules()
    {
        var w = _run.World;
        Assert.True(w.Era.HasFired(OriginsScenes.SecondCentreChapter));
        string code = OriginsScenes.SecondCentre(w)!;
        Assert.NotNull(code);
        var country = Core.Sim.Geography.WorldMap.CountryByCode(w, code)!;
        var before = Core.Sim.Geography.WorldMap.CountryByCode(_run.DayOne, code)!;
        Assert.True(country.MmaInterestBase > before.MmaInterestBase, $"{code} base {before.MmaInterestBase} -> {country.MmaInterestBase}");
        foreach (var p in w.Promotions.Values.Where(p => !p.Defunct && OriginsScenes.InSecondCentre(w, p)))
        {
            Assert.False(OriginsReforms.BoundByCode(w, p));
            Assert.Equal(0, OriginsReforms.Chance(w, p, OriginsReforms.Unified, w.CurrentDate));
        }
    }

    [Fact]
    public void FifteenYears_RoomsLearnedTheWholeGame_AndTheIntakeTurnedToAllRounders()
    {
        var w = _run.World;
        Assert.True(w.Era.HasFired(OriginsCamps.FirstCampChapter));
        Assert.True(w.Era.HasFired(OriginsCamps.SpecialistsFallChapter));
        Assert.Contains(w.Camps.Values, c => c.Name == "Fenwick Street Combat Club" && OriginsCamps.CrossTrains(w, c));
        Assert.True(w.Camps.Values.Count(c => OriginsCamps.CrossTrains(w, c)) >= w.Camps.Count / 2);

        var generated = w.Fighters.Values.Where(f => !f.Designed).ToList();
        var beforeCamp = generated.Where(f => f.Id.Value < _run.FirstIdAfterCamp).ToList();
        var afterFall = generated.Where(f => f.Id.Value >= _run.FirstIdAfterFall).ToList();
        Assert.DoesNotContain(beforeCamp, f => f.PrimaryStyle == PrimaryStyle.WellRounded);
        Assert.True(afterFall.Count >= 20, $"only {afterFall.Count} new men after the fall");
        double share = afterFall.Count(f => f.PrimaryStyle == PrimaryStyle.WellRounded) / (double)afterFall.Count;
        Assert.True(share >= 0.4, $"{share:P0} of the men after the fall arrived cross-trained");
        // And they are all-rounders by the one test the sport uses (their skills, not their label).
        var crossTrained = afterFall.Where(f => f.PrimaryStyle == PrimaryStyle.WellRounded).ToList();
        Assert.True(crossTrained.Count(OriginsTalent.IsAllRounder) >= crossTrained.Count * 9 / 10);
    }

    [Fact]
    public void FifteenYears_TheCodeBindsTheLicensed_AndTheRankingsArePublished()
    {
        var w = _run.World;
        Assert.True(w.Era.HasFired(OriginsReforms.Unified));
        Assert.True(OriginsReforms.RankingsRecognised(w));
        Assert.NotEmpty(w.PoundForPound);
        foreach (var p in w.Promotions.Values.Where(p => OriginsReforms.BoundByCode(w, p)))
        {
            Assert.True(OriginsReforms.HasUnified(p), $"{p.Abbreviation} is licensed but not under the code");
            Assert.Equal(OriginsReforms.UnifiedTitleRounds, p.Rules.TitleRounds);
        }
        Assert.Contains(w.Promotions.Values, p => !p.Defunct && OriginsReforms.BoundByCode(w, p));
    }

    [Fact]
    public void FifteenYears_SmallerMenArrived_OnceTheLowerLinesOpened()
    {
        var w = _run.World;
        Assert.True(OriginsTimeline.IsOpen(w, "weight-ladder-bantam", w.CurrentDate));
        Assert.Contains(w.Fighters.Values, f => !f.Designed && f.WeightClass == WeightClass.Bantamweight);
        Assert.Contains(WeightClass.Bantamweight, OriginsTalent.SizesNow(w));
        Assert.DoesNotContain(WeightClass.Bantamweight, OriginsTalent.SizesNow(_run.DayOne));
    }

    // ---- the mechanics, one at a time ----------------------------------------------------------

    [Fact]
    public void AScene_RampsUpOverItsFirstYears_AndOpensAGymTheMonthAfterItWakes()
    {
        var w = OriginsWorldTests.AiWorld(3);
        Assert.Equal(0, OriginsScenes.TalentWeight(w, "SWE", w.CurrentDate));
        Assert.Null(OriginsScenes.WokeOn(w, "SWE"));

        var woke = Month(80);
        w.CurrentDate = woke;
        Fired(w, Month(2), "crucible-first-night", "first-star");
        Fired(w, Month(30), "press-turns");
        Fired(w, Month(50), "scene-britain-mexico");
        Fired(w, woke, "scene-russia-australia-korea-sweden");
        Assert.Equal(woke, OriginsScenes.WokeOn(w, "SWE"));
        double fresh = OriginsScenes.TalentWeight(w, "SWE", woke);
        double grown = OriginsScenes.TalentWeight(w, "SWE", woke.AddYears(6));
        Assert.Equal(grown / 4, fresh, 6);
        Assert.True(OriginsScenes.TalentWeight(w, "USA", woke) > grown);   // a nation's fight culture outweighs a young scene's

        var sweden = Core.Sim.Geography.WorldMap.CountryByCode(w, "SWE")!;
        Assert.DoesNotContain(w.Camps.Values, c => w.Get(c.City)!.Country == sweden.Id);
        OriginsScenes.RunMonthly(w, woke.AddDays(7));
        var gym = Assert.Single(w.Camps.Values, c => w.Get(c.City)!.Country == sweden.Id);
        Assert.NotEqual(PrimaryStyle.WellRounded, gym.Specialty);   // nobody cross-trains yet: it teaches Sweden's art
    }

    [Fact]
    public void AOneArtRoom_GrowsAManInHisOwnHalfOnly_AndAWholeGameRoomGrowsBoth()
    {
        var w = OriginsWorldTests.AiWorld(5);
        var gen = FighterGenerator.ForWorld(w);
        var a = OriginsTalent.Create(w, gen, w.CurrentDate, "USA", 19, 40);
        var b = OriginsTalent.Create(w, gen, w.CurrentDate, "USA", 19, 40);
        foreach (var f in new[] { a, b })
        {
            f.PotentialAbility = 90;
            OriginsTalent.Specialise(w, f, OriginsDiscipline.Boxing, 40);   // a boxer: nothing on the ground
        }
        double grapA = OriginsTalent.GrapplingMean(a), grapB = OriginsTalent.GrapplingMean(b);
        var rngA = new SimRandom(77); var rngB = new SimRandom(77);
        var day = w.CurrentDate;
        for (int m = 0; m < 24; m++)
        {
            day = day.AddMonths(1);
            FighterDevelopment.Advance(a, day, rngA, 1.0, oneArt: true);
            FighterDevelopment.Advance(b, day, rngB, 1.0, oneArt: false);
        }
        Assert.Equal(grapA, OriginsTalent.GrapplingMean(a));            // a boxing room never taught him to wrestle
        Assert.True(OriginsTalent.GrapplingMean(b) > grapB + 3);        // a whole-game room did
    }

    [Fact]
    public void TheCrossTrainedIntake_StartsWithTheFirstCamp_AndGrowsAfterTheFall()
    {
        var w = OriginsWorldTests.AiWorld(8);
        Assert.Equal(0, OriginsTalent.CrossTrainedShare(w, Month(100)));
        Fired(w, Month(70), OriginsCamps.FirstCampChapter);
        Assert.Equal(0.10, OriginsTalent.CrossTrainedShare(w, Month(70)), 6);
        Assert.Equal(0.45, OriginsTalent.CrossTrainedShare(w, Month(70 + 120)), 6);
        Fired(w, Month(100), OriginsCamps.SpecialistsFallChapter);
        Assert.Equal(0.50, OriginsTalent.CrossTrainedShare(w, Month(100)), 6);
        Assert.Equal(0.85, OriginsTalent.CrossTrainedShare(w, Month(100 + 120)), 6);

        // Men made after the fall: about half cross-trained, and those are all-rounders without being
        // much better than a specialist made at the same level.
        w.CurrentDate = Month(101);
        var gen = FighterGenerator.ForWorld(w);
        var made = Enumerable.Range(0, 200).Select(_ => OriginsTalent.Create(w, gen, w.CurrentDate, "USA", 22, 45)).ToList();
        var cross = made.Where(f => f.PrimaryStyle == PrimaryStyle.WellRounded).ToList();
        var specialists = made.Except(cross).ToList();
        Assert.InRange(cross.Count, 70, 130);
        Assert.True(cross.Count(OriginsTalent.IsAllRounder) >= cross.Count * 9 / 10);
        Assert.True(specialists.Count(OriginsTalent.IsAllRounder) <= specialists.Count / 10);
        double caCross = cross.Average(f => Core.Sim.Ability.FighterAbility.CurrentAbility(f));
        double caSpec = specialists.Average(f => Core.Sim.Ability.FighterAbility.CurrentAbility(f));
        Assert.InRange(caCross - caSpec, -2, 8);
    }

    [Fact]
    public void ThePlayer_CanPayForHisRoomToCrossTrain_OnceTheSportKnowsHow_AndIsToldWhyNot()
    {
        var w = new WorldBuilder().Build(WorldBuilder.Options.Origins(9, playerIndex: 1));   // the Association, Tokyo
        var sfa = w.Get(w.PlayerPromotion)!;
        var man = w.RosterOf(sfa.Id).First(f => f.Camp is not null);
        var room = w.Get(man.Camp!.Value)!;

        w.CurrentDate = Month(60);
        Assert.Empty(OriginsCamps.Options(w, sfa, w.CurrentDate));
        Assert.StartsWith("Nobody teaches that way yet", OriginsCamps.Refusal(w, sfa, room.Id, w.CurrentDate));
        Assert.False(OriginsCamps.TryFund(w, sfa, room.Id, w.CurrentDate, out _));

        Fired(w, Month(2), "crucible-first-night", "first-star");
        Fired(w, Month(20), "style-upset");
        w.CurrentDate = Month(70);   // the first famous camp's window is open: the chapter is live
        Assert.True(OriginsTimeline.IsLive(w, OriginsChapters.ByKey(OriginsCamps.FirstCampChapter), w.CurrentDate));
        var option = Assert.Single(OriginsCamps.Options(w, sfa, w.CurrentDate), o => o.Camp == room.Id);
        decimal bank = sfa.Balance.Amount;
        Assert.True(OriginsCamps.TryFund(w, sfa, room.Id, w.CurrentDate, out var refused), refused);
        Assert.Equal(PrimaryStyle.WellRounded, room.Specialty);
        Assert.True(OriginsCamps.CrossTrains(w, room));
        Assert.False(OriginsCamps.TrainsOneArt(w, man));
        Assert.Equal(bank - option.Cost.Amount, sfa.Balance.Amount);
        Assert.EndsWith("already teaches striking, wrestling and the ground.", OriginsCamps.Refusal(w, sfa, room.Id, w.CurrentDate));

        // A room none of his men train in is not his to fund.
        var other = w.Camps.Values.First(c => c.Specialty != PrimaryStyle.WellRounded && !w.RosterOf(sfa.Id).Any(f => f.Camp == c.Id));
        Assert.StartsWith("None of your men train at", OriginsCamps.Refusal(w, sfa, other.Id, w.CurrentDate));
    }

    [Fact]
    public void TheFamousRoom_IsCreditedToTheHouseWithMostOfItsMen()
    {
        var w = new WorldBuilder().Build(WorldBuilder.Options.Origins(9, playerIndex: 1));
        var sfa = w.Get(w.PlayerPromotion)!;
        var man = w.RosterOf(sfa.Id).First(f => f.Camp is not null);
        var room = w.Get(man.Camp!.Value)!;
        Fired(w, Month(2), "crucible-first-night", "first-star");
        Fired(w, Month(20), "style-upset");
        w.CurrentDate = Month(70);
        room.Reputation = 50;
        Assert.True(OriginsCamps.TryFund(w, sfa, room.Id, w.CurrentDate, out _));   // +6: 56, over the chapter's 55
        OriginsTimeline.RunWeekly(w, w.CurrentDate.AddDays(7));
        var rec = w.Era.Chapter(OriginsCamps.FirstCampChapter)!;
        Assert.Equal(sfa.Id, rec.Promotion);
        Assert.Equal(ChapterVariant.Player, rec.Variant);
        Assert.Equal(room.Name, rec.Note);
    }

    [Fact]
    public void TheCode_BindsALicensedHouse_IsOfferedToTheRest_AndGivesATitleFiveRounds()
    {
        var w = new WorldBuilder().Build(WorldBuilder.Options.Origins(12, playerIndex: 0));   // the Crucible, Los Angeles
        var cru = w.Get(w.PlayerPromotion)!;
        var dlb = House(w, "DLB");
        w.CurrentDate = Month(160);
        Assert.Equal("There is no one code yet: every house and every commission still writes its own rules.",
            OriginsReforms.Refusal(w, dlb, OriginsReforms.Unified, w.CurrentDate));

        var la = w.Get(cru.HomeCity!.Value)!;
        w.Era.Sanctions.Add(new Sanction { Country = "USA", Area = la.Area, On = Month(120) });
        Fired(w, Month(158), OriginsReforms.Unified);
        Assert.True(OriginsReforms.BoundByCode(w, cru));
        Assert.False(OriginsReforms.BoundByCode(w, dlb));   // Rio has no licence: nobody binds the Garage

        OriginsReforms.UnifiedRulesBegin(w, w.CurrentDate);
        Assert.True(OriginsReforms.HasUnified(cru));
        Assert.Equal(ShowFormat.Card, cru.Rules.ShowFormat);   // the bracket gives way to cards
        Assert.Null(cru.Rules.Fight!.Rounds);
        Assert.Equal(OriginsReforms.UnifiedTitleRounds, cru.Rules.TitleRounds);
        Assert.Contains(w.Mail, m => m.Subject == "The commission's code is now our rulebook");
        Assert.Empty(OriginsReforms.Options(w, cru, w.CurrentDate).Where(o => o.Chapter == OriginsReforms.Unified));

        Assert.Contains(OriginsReforms.Options(w, dlb, w.CurrentDate), o => o.Chapter == OriginsReforms.Unified);
        Assert.True(OriginsReforms.TryAdopt(w, dlb, OriginsReforms.Unified, w.CurrentDate, out _));
        Assert.True(OriginsReforms.HasUnified(dlb));
        Assert.Equal("You already fight under the commissions' code.", OriginsReforms.Refusal(w, dlb, OriginsReforms.Unified, w.CurrentDate));

        // A title bout under the code is booked for five rounds; any other bout for three.
        var div = cru.Divisions.First(d => d.Title is not null);
        var men = w.RosterOf(cru.Id).Where(f => div.Matches(f)).Take(2).ToList();
        div.Rankings.Clear();
        div.Rankings.AddRange(men.Select(f => f.Id));
        var card = OriginsShows.SuggestCard(w, cru.Id, w.CurrentDate.AddDays(28));
        Assert.Contains(card, m => m.IsTitle && m.Rounds == OriginsReforms.UnifiedTitleRounds);
        Assert.All(card.Where(m => !m.IsTitle), m => Assert.Equal(OriginsReforms.CardRounds, m.Rounds));
    }

    [Fact]
    public void TheRankings_AreRecognisedOnlyOnceThereIsOneCode()
    {
        var w = OriginsWorldTests.AiWorld(2);
        new WorldTick().AdvanceWeeks(w, 60);
        RankingService.RecomputeGlobal(w);
        Assert.Empty(w.PoundForPound);
        Fired(w, w.CurrentDate, OriginsReforms.Unified);
        RankingService.RecomputeGlobal(w);
        Assert.NotEmpty(w.PoundForPound);
    }

    [Fact]
    public void TheSecondCentre_IsWhereTheArenaNightWas_OrTokyo_AndItsHousesFightInARing()
    {
        var w = OriginsWorldTests.AiWorld(4);
        var seoul = w.Cities.Values.First(c => c.Name == "Seoul");
        Era.Fire(w, OriginsScenes.SecondCentreChapter, Month(100), city: seoul.Id);
        Assert.Equal("KOR", OriginsScenes.SecondCentre(w));

        var t = OriginsWorldTests.AiWorld(4);
        var la = t.Cities.Values.First(c => c.Name == "Los Angeles");
        Era.Fire(t, OriginsScenes.SecondCentreChapter, Month(100), city: la.Id);   // North America is the first centre, not the second
        Assert.Equal("JPN", OriginsScenes.SecondCentre(t));

        var sfa = House(t, "SFA");
        if (sfa.Rules.Fight is { } r) sfa.Rules.Fight = r with { Enclosure = Enclosure.Cage };
        var tokyo = t.Get(sfa.HomeCity!.Value)!;
        int before = tokyo.MmaInterestBase;
        OriginsScenes.SecondCentreBegins(t, Month(100));
        Assert.Equal(Math.Max(before, Math.Min(OriginsScenes.SecondCentreInterestCap, before + OriginsScenes.SecondCentreInterest)), tokyo.MmaInterestBase);
        Assert.True(tokyo.MmaInterestBase > before || before >= OriginsScenes.SecondCentreInterestCap);
        Assert.Equal(Enclosure.Ring, sfa.Rules.Fight!.Enclosure);
        Assert.True(OriginsScenes.InSecondCentre(t, sfa));
    }

    [Fact]
    public void SaveLoadRunOn_EqualsNeverSaved_AsTheRoomsLearnTheWholeGame()
    {
        var world = OriginsWorldTests.AiWorld(17);
        var tick = new WorldTick();
        while (!(world.Era.HasFired(OriginsCamps.FirstCampChapter) && world.Camps.Values.Count(c => OriginsCamps.CrossTrains(world, c)) >= 3)
               && world.CurrentDate.Year < 2005)
            tick.AdvanceWeek(world);
        Assert.True(world.Era.HasFired(OriginsCamps.FirstCampChapter));

        string path = Path.Combine(Path.GetTempPath(), $"mma_spread_{Guid.NewGuid():N}.db");
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
}
