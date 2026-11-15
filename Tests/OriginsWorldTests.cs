using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Engine;
using MmaManager.Core.Sim.Generation;
using MmaManager.Core.Sim.Generation.Origins;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Org;
using MmaManager.Persistence;

namespace MmaManager.Tests;

/// <summary>One AI-run Origins world, a year on (shared: building and running it is the slow part).</summary>
public sealed class OriginsYearFixture
{
    public World World { get; }
    public World DayOne { get; }

    public OriginsYearFixture()
    {
        DayOne = OriginsWorldTests.AiWorld(11);
        World = OriginsWorldTests.AiWorld(11);
        new WorldTick().AdvanceWeeks(World, 52);
    }
}

/// <summary>
/// Origins phase 1: the bare 1993 world. Three promotions with their house rules and formats, the
/// authored pioneers, one open division per house, the nights each house runs, the scene gate, and
/// the guarantees that today's mode never sees any of it.
/// </summary>
public class OriginsWorldTests : IClassFixture<OriginsYearFixture>
{
    private readonly OriginsYearFixture _year;
    public OriginsWorldTests(OriginsYearFixture year) => _year = year;

    internal static World AiWorld(ulong seed)
    {
        var w = new WorldBuilder().Build(WorldBuilder.Options.Origins(seed));
        if (w.Get(w.PlayerPromotion) is { } p) p.IsPlayerControlled = false;
        w.PlayerPromotion = default;
        return w;
    }

    private static Promotion House(World w, string abbr) => w.Promotions.Values.Single(p => p.Abbreviation == abbr);
    private static Fighter Pioneer(World w, string first, string last) =>
        w.Fighters.Values.Single(f => f.Designed && f.FirstName == first && f.LastName == last);

    // ---- the build ---------------------------------------------------------------

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(77UL)]
    public void TheWorldBuilds_ThreeHousesWithTheirRules(ulong seed)
    {
        var w = new WorldBuilder().Build(WorldBuilder.Options.Origins(seed, playerIndex: 1));
        Assert.Equal(WorldMode.Origins, w.Mode);
        Assert.Equal(new DateOnly(1993, 1, 1), w.StartDate);
        Assert.Equal(new DateOnly(1993, 1, 1), w.CurrentDate);
        Assert.Equal(new[] { "CRU", "SFA", "DLB" }, w.Promotions.Values.OrderBy(p => p.Id.Value).Select(p => p.Abbreviation));
        Assert.Equal("SFA", w.Get(w.PlayerPromotion)!.Abbreviation);

        foreach (var spec in OriginsPromotions.All)
        {
            var p = House(w, spec.Abbreviation);
            Assert.Equal(spec.Name, p.Name);
            Assert.Equal(spec.Prestige, p.Prestige.Value);
            Assert.Equal(spec.Balance, p.Balance);
            Assert.Equal(spec.Founder.Name, p.OwnerName);
            Assert.Equal(spec.Lore, p.Lore);
            Assert.Equal(spec.ToFightRules(), p.Rules.Fight);
            Assert.True(p.Rules.Fight!.OpenWeight);
            Assert.Equal(spec.HomeCity, w.Get(p.HomeCity!.Value)!.Name);
            Assert.Contains(w.Venues.Values, v => v.Name == spec.HomeVenue && v.City == p.HomeCity);
            var d = Assert.Single(p.Divisions);
            Assert.True(d.Open);
            Assert.Equal(Sex.Male, d.Sex);
        }
        Assert.Equal(ShowFormat.OneNightTournament, House(w, "CRU").Rules.ShowFormat);
        Assert.Equal(8, House(w, "CRU").Rules.TournamentSize);
        Assert.Equal(ShowFormat.Card, House(w, "SFA").Rules.ShowFormat);
        Assert.Equal(ShowFormat.GymChallenge, House(w, "DLB").Rules.ShowFormat);
        Assert.NotNull(House(w, "CRU").Divisions[0].Title);
        Assert.NotNull(House(w, "SFA").Divisions[0].Title);
        Assert.Null(House(w, "DLB").Divisions[0].Title);   // the garage settles arguments, not belts
        Assert.All(w.Titles.Values, t => Assert.Null(t.CurrentChampion));   // nobody has won anything yet

        // Day one has exactly the documented capabilities.
        Assert.Equal(OriginsWorldBuilder.DayOneUnlocks.Select(c => c.Id).OrderBy(x => x, StringComparer.Ordinal), w.Era.Unlocked);
        Assert.Empty(w.Era.Chapters);
    }

    [Fact]
    public void EveryPioneer_IsThereWithHisAuthoredFacts()
    {
        var w = _year.DayOne;
        Assert.Equal(OriginsPioneers.All.Count, w.Fighters.Values.Count(f => f.Designed));
        foreach (var row in OriginsPioneers.All)
        {
            var d = row.Fighter;
            var f = Pioneer(w, d.First, d.Last);
            Assert.Equal(d.Nickname, f.Nickname);
            Assert.Equal(d.Bio, f.Bio);
            Assert.Equal(row.WeightLbs, f.NaturalWeightLbs);
            Assert.Equal(row.HeightCm, f.HeightCm);
            Assert.Equal(d.Age, f.AgeOn(w.StartDate));
            Assert.Equal(d.Wins, f.Record.Wins);
            Assert.Equal(d.Losses, f.Record.Losses);
            Assert.Equal(d.Country, f.OriginCountryCode);
            Assert.Equal(OriginsPioneers.StyleOf(row.Discipline), f.PrimaryStyle);
            Assert.Equal(d.Org, f.Contract is { } c ? w.Get(c.Promotion)!.Abbreviation : null);
            foreach (var (skill, value) in row.Skills)
                Assert.Equal(value, MmaManager.Core.Sim.Engine.FightLab.Attributes.Single(a => a.Name == skill.ToString()).Get(f));
        }

        // Spot checks, as the bios have them.
        var island = Pioneer(w, "Lemalu", "Tofaeono");
        Assert.Equal(370, island.NaturalWeightLbs);
        Assert.Equal("CRU", w.Get(island.Contract!.Promotion)!.Abbreviation);
        var baker = Pioneer(w, "Odair", "Quintela");
        Assert.Equal("Quintela Academia", w.Get(baker.Camp!.Value)!.Name);
        Assert.Equal(Disposition.CompanyMan, baker.Disposition);
        var dike = Pioneer(w, "Joost", "Zandvliet");
        Assert.Null(dike.Contract);
        Assert.Equal("Zuidwal Gym", w.Get(dike.Camp!.Value)!.Name);
        var cassio = Pioneer(w, "Cassio", "Vilela");
        Assert.Equal("Quintela Academia", w.Get(cassio.Camp!.Value)!.Name);
        Assert.Equal(Disposition.Hothead, Pioneer(w, "Marcelo", "Vilela").Disposition);

        // The day-one stories.
        var tatu = Pioneer(w, "Heitor", "Paranhos");
        Assert.Contains(w.Rivalries, r => r.Intensity == 80
            && ((r.A == baker.Id && r.B == tatu.Id) || (r.A == tatu.Id && r.B == baker.Id)));
        var hand = Pioneer(w, "Tetsuya", "Hagiwara");
        var rope = Pioneer(w, "Goro", "Kawabata");
        Assert.Contains(w.Relationships, r => r.Kind == RelationshipKind.Mentor && r.A == rope.Id && r.B == hand.Id);
        Assert.Contains(w.Relationships, r => r.Kind == RelationshipKind.Family && r.IsBetween(cassio.Id, Pioneer(w, "Marcelo", "Vilela").Id));
        Assert.Equal(OriginsPioneers.Gyms.Select(g => g.Name).OrderBy(x => x), w.Camps.Values.Select(c => c.Name).OrderBy(x => x));
    }

    [Fact]
    public void TheSupportingPool_IsZeroAndZeroSpecialists()
    {
        var w = _year.DayOne;
        var pool = w.Fighters.Values.Where(f => !f.Designed).ToList();
        Assert.Equal(OriginsWorldBuilder.SupportingPool, pool.Count);
        Assert.All(pool, f =>
        {
            Assert.Equal(0, f.Record.TotalBouts);
            Assert.Equal(Sex.Male, f.Sex);
            Assert.InRange(f.AgeOn(w.StartDate), 21, 35);
        });
        // Specialists: the art's craft far above the skills it never teaches (a striker can't wrestle).
        double gap = pool.Average(f =>
        {
            var v = MmaManager.Core.Sim.Engine.FightLab.Attributes.Take(15).Select(a => a.Get(f)).OrderBy(x => x).ToList();
            return v[^3..].Average() - v[..3].Average();
        });
        Assert.True(gap > 30, $"technical skills spread only {gap:0.0} points: these are not specialists");
        Assert.True(pool.Count(f => f.OriginCountryCode is "USA" or "JPN" or "BRA") > pool.Count / 2);
    }

    [Fact]
    public void NothingTheSportHasNotGot_ExistsOnDayOne()
    {
        var w = _year.DayOne;
        Assert.DoesNotContain(w.Fighters.Values, f => f.Sex == Sex.Female || f.LocalTalent);
        Assert.All(w.Promotions.Values, p =>
        {
            Assert.All(p.Divisions, d => Assert.True(d.Open));
            Assert.Empty(p.MediaDeals);
            Assert.Null(p.Stream);
        });
        Assert.Empty(w.Events.Values.Where(e => e.Status == EventStatus.Completed));   // no back-dated history
        Assert.DoesNotContain(w.Fighters.Values, f => DesignedRoster.All.Any(d => d.First == f.FirstName && d.Last == f.LastName && f.Designed));
        Assert.All(w.Fighters.Values, f => Assert.Empty(f.BoutHistory));
        foreach (var wc in Enum.GetValues<WeightClass>()) Assert.False(Era.AllowsDivision(w, wc, Sex.Male));
        Assert.False(Era.Allows(w, Capability.NewPromotions));
        Assert.False(Era.Allows(w, Capability.WomensDivisions));
    }

    [Fact]
    public void TodaysWorld_HasNoOriginsContent()
    {
        var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 11 });
        Assert.Equal(WorldMode.Modern, w.Mode);
        Assert.Equal(new DateOnly(2026, 1, 1), w.StartDate);
        Assert.Empty(w.Era.Unlocked);
        var origins = OriginsPromotions.All.Select(s => s.Abbreviation).ToHashSet();
        Assert.DoesNotContain(w.Promotions.Values, p => origins.Contains(p.Abbreviation));
        Assert.DoesNotContain(w.Fighters.Values, f => OriginsPioneers.All.Any(o => o.Fighter.First == f.FirstName && o.Fighter.Last == f.LastName));
        Assert.All(w.Promotions.Values, p =>
        {
            Assert.Equal(ShowFormat.Card, p.Rules.ShowFormat);
            Assert.Null(p.Rules.Fight);
            Assert.DoesNotContain(p.Divisions, d => d.Open);
        });
        Assert.DoesNotContain(w.Events.Values, e => e.Format != EventFormat.Card || e.Card.Any(f => f.OpenWeight));
    }

    // ---- a year of it -------------------------------------------------------------

    [Fact]
    public void AYear_EachHouseRunsNightsOfItsOwnFormat()
    {
        var w = _year.World;
        foreach (var p in w.Promotions.Values)
        {
            Assert.False(p.Defunct, $"{p.Abbreviation} folded in its first year");
            var nights = w.Events.Values.Where(e => e.Promotion == p.Id && e.Status == EventStatus.Completed).ToList();
            Assert.True(nights.Count >= 2, $"{p.Abbreviation} ran {nights.Count} nights in a year");
            foreach (var ev in nights)
            {
                Assert.Equal(p.Rules.ShowFormat == ShowFormat.OneNightTournament ? EventFormat.Tournament : EventFormat.Card, ev.Format);
                Assert.All(ev.Card, f =>
                {
                    Assert.True(f.OpenWeight);
                    Assert.Null(f.WeightMissBy);
                    Assert.Equal(p.Rules.Fight!.Rounds, f.ScheduledRounds);
                    Assert.NotNull(f.Outcome);
                });
                // No judges: nothing goes to the cards.
                if (!p.Rules.Fight!.Judges)
                    Assert.DoesNotContain(ev.Card, f => f.Outcome!.Method is FightMethod.UnanimousDecision or FightMethod.SplitDecision or FightMethod.MajorityDecision);
                Assert.Equal(OriginsShows.HomeVenue(w, p)!.Id, ev.VenueId);
            }
        }
        // The garage never sets two men of one gym against each other.
        foreach (var ev in w.Events.Values.Where(e => e.Promotion == House(w, "DLB").Id && e.Status == EventStatus.Completed))
            Assert.All(ev.Card, f =>
            {
                var a = w.Get(f.RedCorner)!; var b = w.Get(f.BlueCorner)!;
                Assert.False(a.Camp is not null && a.Camp == b.Camp, $"{ev.Name}: {a.FullName} and {b.FullName} share a gym");
            });
        // No club shows, no filler, no startups, no new capabilities.
        Assert.DoesNotContain(w.Events.Values, e => e.Promotion.Value == 0);
        Assert.DoesNotContain(w.Fighters.Values, f => f.LocalTalent || f.Sex == Sex.Female);
        Assert.Equal(3, w.Promotions.Count);
        Assert.Equal(OriginsWorldBuilder.DayOneUnlocks.Count, w.Era.Unlocked.Count);
        Assert.All(w.Promotions.Values, p => Assert.All(p.Divisions, d => Assert.True(d.Open)));
        // New men turned pro, all from a country with a scene.
        var newcomers = w.Fighters.Values.Where(f => f.Id.Value > _year.DayOne.Fighters.Keys.Max(k => k.Value)).ToList();
        Assert.NotEmpty(newcomers);
        Assert.All(newcomers, f => Assert.Contains(f.OriginCountryCode, new[] { "USA", "JPN", "BRA" }));
    }

    [Fact]
    public void ACrucibleNight_CrownsAChampionOrDeclaresNone()
    {
        var w = _year.World;
        var cru = House(w, "CRU");
        var title = w.Get(cru.Divisions[0].Title!.Value)!;
        var nights = w.Events.Values.Where(e => e.Promotion == cru.Id && e.Status == EventStatus.Completed).OrderBy(e => e.Date).ToList();
        Assert.NotEmpty(nights);
        foreach (var ev in nights)
        {
            Assert.Equal(EventFormat.Tournament, ev.Format);
            Assert.InRange(ev.Card.Count, 4, 8);   // at most seven bouts (an alternate's bout makes no eighth); walkovers fight fewer
            var crowned = title.Lineage.Any(r => r.WonOn == ev.Date);
            var noWinner = w.News.Any(n => n.Date == ev.Date && n.Promotion == cru.Id
                && (n.Headline.StartsWith("No winner in") || n.Headline.EndsWith("ends without a champion")));
            var repeat = w.News.Any(n => n.Date == ev.Date && n.Promotion == cru.Id && n.Category == NewsCategory.TitleDefense);
            Assert.True(crowned ^ (noWinner || repeat), $"{ev.Name}: crowned {crowned}, no winner {noWinner}, kept {repeat}");
            Assert.NotNull(ev.Financials);
        }
        if (title.CurrentChampion is { } champ)
            Assert.Equal(champ, title.Lineage[^1].Champion);
    }

    [Fact]
    public void TheSceneGate_KeepsShowsAndTalentAtHome()
    {
        var w = AiWorld(19);   // its own world: the intake below adds men
        var london = w.Cities.Values.First(c => c.Name == "London");
        var tokyo = w.Cities.Values.First(c => c.Name == "Tokyo");
        foreach (var p in w.Promotions.Values) Assert.False(OrgReach.CanBookIn(w, p, london.Id));
        Assert.True(OrgReach.CanBookIn(w, House(w, "SFA"), tokyo.Id));
        Assert.All(OriginsTalent.Intake(w, 25, w.StartDate.AddMonths(1)), f => Assert.Contains(f.OriginCountryCode, new[] { "USA", "JPN", "BRA" }));

        // Today's mode: no scene gate at all.
        var modern = new World();
        Assert.True(Era.AllowsScene(modern, "GBR"));
    }

    [Fact]
    public void ThePlayer_BooksABracketAndItRunsOnItsNight()
    {
        var w = new WorldBuilder().Build(WorldBuilder.Options.Origins(5, playerIndex: 0));
        var cru = w.Get(w.PlayerPromotion)!;
        Assert.Equal("CRU", cru.Abbreviation);
        var night = OriginsShows.ScheduleTournament(w, cru.Id, w.CurrentDate.AddDays(21));
        Assert.NotNull(night);
        Assert.Equal(EventFormat.Tournament, night!.Format);
        Assert.Empty(night.Card);
        Assert.Null(OriginsShows.ScheduleTournament(w, House(w, "SFA").Id, w.CurrentDate.AddDays(21)));   // not a bracket house

        var tick = new WorldTick();
        for (int i = 0; i < 6 && w.CurrentDate < night.Date; i++) tick.AdvanceWeeksStoppingForPlayer(w, 1);
        Assert.Equal(EventStatus.Scheduled, night.Status);   // the player's own night waits for him
        tick.RunEventLive(w, night);
        Assert.Equal(EventStatus.Completed, night.Status);
        Assert.InRange(night.Card.Count, 4, 8);
        Assert.All(night.Card, f => Assert.True(f.OpenWeight));

        // A card for the Association's player, booked through the same API the AI uses.
        var s = new WorldBuilder().Build(WorldBuilder.Options.Origins(5, playerIndex: 1));
        var card = OriginsShows.SuggestCard(s, s.PlayerPromotion, s.CurrentDate.AddDays(28));
        Assert.True(card.Count >= 3);
        Assert.Contains(card, m => m.IsTitle);   // the vacant belt goes up on the first night
        var ev = OriginsShows.ScheduleCard(s, s.PlayerPromotion, s.CurrentDate.AddDays(28), card);
        Assert.NotNull(ev);
        Assert.All(ev!.Card, f => Assert.True(f.OpenWeight));
    }

    // ---- determinism and the save ---------------------------------------------------

    [Fact]
    public void SameSeedTwice_GivesTheSameWorld()
    {
        var a = AiWorld(13);
        var b = AiWorld(13);
        new WorldTick().AdvanceWeeks(a, 16);
        new WorldTick().AdvanceWeeks(b, 16);
        Assert.Contains(a.Events.Values, e => e.Format == EventFormat.Tournament && e.Status == EventStatus.Completed);
        var diffs = PersistenceGraph.Diff(a, b);
        Assert.True(diffs.Count == 0, "Same seed, different worlds:\n" + PersistenceGraph.Describe(diffs));
    }

    [Fact]
    public void SaveLoadRunOn_EqualsNeverSaved_AcrossATournamentNight()
    {
        var world = AiWorld(17);
        var tick = new WorldTick();
        // Run until the Crucible has fought one bracket and announced the next (saved mid-year with a
        // tournament's results on the books and an empty bracket night still to come).
        int guard = 0;
        while (!(world.Events.Values.Any(e => e.Format == EventFormat.Tournament && e.Status == EventStatus.Completed)
                 && world.Events.Values.Any(e => e.Format == EventFormat.Tournament && e.Status == EventStatus.Scheduled))
               && guard++ < 40)
            tick.AdvanceWeek(world);
        Assert.Contains(world.Events.Values, e => e.Format == EventFormat.Tournament && e.Status == EventStatus.Completed && e.Card.Count > 0);
        Assert.Contains(world.Events.Values, e => e.Format == EventFormat.Tournament && e.Status == EventStatus.Scheduled);

        string path = Path.Combine(Path.GetTempPath(), $"mma_origins_{Guid.NewGuid():N}.db");
        try
        {
            SaveGame.Save(world, path);
            var loaded = SaveGame.Load(path);
            var diffs = PersistenceGraph.Diff(world, loaded);
            Assert.True(diffs.Count == 0, "Changed across the save:\n" + PersistenceGraph.Describe(diffs));

            new WorldTick().AdvanceWeeks(world, 8);    // across the announced bracket night
            new WorldTick().AdvanceWeeks(loaded, 8);
            diffs = PersistenceGraph.Diff(world, loaded);
            Assert.True(diffs.Count == 0, "Saved and never-saved worlds ran apart:\n" + PersistenceGraph.Describe(diffs));
        }
        finally { File.Delete(path); }
    }

    // ---- the chapter table's vocabulary --------------------------------------------

    [Fact]
    public void EveryChapterUnlock_MapsToARealCapability()
    {
        foreach (var chapter in OriginsChapters.All)
            foreach (var unlock in chapter.Unlocks)
            {
                Assert.True(Era.TryFromChapterUnlock(unlock, out var cap), $"{chapter.Key}: '{unlock}' maps to no capability");
                Assert.True(Capability.TryParse(cap.Id, out _), $"{chapter.Key}: '{unlock}' -> '{cap.Id}' does not parse back");
            }
        foreach (var unlock in OriginsCapabilities.All)
            Assert.True(Era.TryFromChapterUnlock(unlock, out _), unlock);

        Assert.True(Era.TryFromChapterUnlock("Media:PpvCarrier", out var ppv));
        Assert.Equal(Capability.PpvCarrier, ppv);
        Assert.True(Era.TryFromChapterUnlock("Sponsor:EnergyDrink", out var drink));
        Assert.Equal(Capability.SponsorEnergyDrink, drink);
        Assert.True(Era.TryFromChapterUnlock("WeightClass:Lightweight", out var lw));
        Assert.Equal(Capability.Class(WeightClass.Lightweight), lw);
        Assert.True(Era.TryFromChapterUnlock("CountryScene:bra", out var bra));
        Assert.Equal(Capability.Scene("BRA"), bra);
        Assert.True(Era.TryFromChapterUnlock("Rounds", out var rounds));
        Assert.Equal(Capability.Rounds, rounds);
        foreach (var bad in new[] { "Media:Telegraph", "Sponsor:", "CountryScene:XXX", "Sponsor:Lager", "Openweight", "Media:4" })
            Assert.False(Era.TryFromChapterUnlock(bad, out _), bad);
    }
}
