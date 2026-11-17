using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Generation.Origins;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Scouting;
using MmaManager.Persistence;

namespace MmaManager.Tests;

/// <summary>
/// Founding a promotion mid-game from an Origins observer start (<see cref="PlayerFounding"/>): the choices and
/// their honest refusals, what a newcomer gets in each era, the one free tryout, the first night, the story's
/// credit for the fourth house, determinism and the save.
/// </summary>
public class PlayerFoundingTests
{
    private static World Observer(ulong seed) => new WorldBuilder().Build(WorldBuilder.Options.OriginsObserver(seed));

    /// <summary>The first city he may found in, in id order (a USA/JPN/BRA city on day one).</summary>
    private static FoundingCity FirstOpen(World w) => PlayerFounding.Choices(w, w.CurrentDate).Cities.First(c => c.Open);

    private static PlayerFoundingSpec Spec(World w, string name = "Lantern Street Open", string abbr = "LSO",
        int? minutes = null, GloveType? gloves = null, Enclosure? enclosure = null) =>
        new(name, abbr, FirstOpen(w).Id, minutes, gloves, enclosure);

    private static Promotion FoundOk(World w, PlayerFoundingSpec spec)
    {
        var promo = PlayerFounding.Found(w, w.CurrentDate, spec, out var why);
        Assert.True(promo is not null, why);
        return promo!;
    }

    // ---- the choices --------------------------------------------------------------

    [Fact]
    public void Choices_DayOne_TheAiRule_WithHonestReasons()
    {
        var w = Observer(3);
        var choices = PlayerFounding.Choices(w, w.CurrentDate);
        Assert.True(choices.CanFound);
        Assert.Equal(w.Cities.Count, choices.Cities.Count);

        // The open cities are exactly the AI newcomer's.
        var ai = OriginsFounding.FoundableCities(w, w.CurrentDate).Select(c => c.Id).ToList();
        Assert.Equal(ai, choices.Cities.Where(c => c.Open).Select(c => c.Id).ToList());
        Assert.All(choices.Cities.Where(c => c.Open), c => Assert.Contains(c.CountryCode, new[] { "USA", "JPN", "BRA" }));

        // Every closed city says why.
        Assert.All(choices.Cities.Where(c => !c.Open), c => Assert.False(string.IsNullOrWhiteSpace(c.Refusal)));
        var cru = w.Promotions.Values.Single(p => p.Abbreviation == "CRU");
        Assert.Contains("Crucible", choices.Cities.Single(c => c.Id == cru.HomeCity).Refusal);
        Assert.Contains(choices.Cities, c => c.CountryCode == "GBR" && c.Refusal!.Contains("no MMA scene"));

        // 1993: one long round (he picks its length), bare or optional gloves, ring or cage, one open division.
        Assert.False(choices.RoundsAndJudges);
        Assert.Equal(new[] { 15, 20 }, choices.SingleRoundMinutes);
        Assert.Equal(new[] { GloveType.Bare, GloveType.Optional }, choices.Gloves);
        Assert.Equal(new[] { Enclosure.Cage, Enclosure.Ring }, choices.Enclosures);
        Assert.True(choices.OpenWeight);
        Assert.Equal(OriginsFounding.NewcomerBank(PlayerFounding.BankSteps), choices.Bank.Amount);   // (125k: 50k and three steps of 25k, the AI newcomers' median)
        Assert.Equal(5, choices.Prestige);
        Assert.NotEmpty(choices.Notes);
    }

    [Fact]
    public void ReadingTheChoices_ChangesNothing()
    {
        var a = Observer(4);
        var b = Observer(4);
        var tick = new WorldTick();
        for (int week = 0; week < 30; week++)
        {
            PlayerFounding.Choices(a, a.CurrentDate);
            PlayerFounding.TryoutOptions(a, a.CurrentDate);
            Assert.Null(PlayerFounding.HoldTryout(a, a.CurrentDate, out _).FirstOrDefault());
            tick.AdvanceWeek(a);
        }
        new WorldTick().AdvanceWeeks(b, 30);
        var diffs = PersistenceGraph.Diff(b, a);
        Assert.True(diffs.Count == 0, "Reading the founding choices moved the world:\n" + PersistenceGraph.Describe(diffs));
        Assert.False(a.HasPlayer);
        Assert.Empty(a.Mail);
    }

    // ---- refusals -------------------------------------------------------------------

    [Fact]
    public void Refusals_SayWhy_AndChangeNothing()
    {
        var w = Observer(5);
        int promos = w.Promotions.Count;
        var cru = w.Promotions.Values.Single(p => p.Abbreviation == "CRU");
        var open = FirstOpen(w);
        var noScene = w.Cities.Values.OrderBy(c => c.Id.Value).First(c => w.Get(c.Country)!.Code == "GBR");

        string? Refuse(PlayerFoundingSpec s)
        {
            Assert.Null(PlayerFounding.Found(w, w.CurrentDate, s, out var why));
            Assert.False(string.IsNullOrWhiteSpace(why));
            return why;
        }

        Assert.Contains("already", Refuse(new PlayerFoundingSpec(cru.Name, "ZZZ", open.Id)));
        Assert.Contains("already", Refuse(new PlayerFoundingSpec("Brand New Fights", "cru", open.Id)));
        Assert.Contains("Crucible", Refuse(new PlayerFoundingSpec("Brand New Fights", "BNF", cru.HomeCity!.Value)));
        Assert.Contains("no MMA scene", Refuse(new PlayerFoundingSpec("Brand New Fights", "BNF", noScene.Id)));
        Refuse(new PlayerFoundingSpec("", "BNF", open.Id));
        Refuse(new PlayerFoundingSpec("Brand New Fights", "B", open.Id));
        Refuse(new PlayerFoundingSpec("Brand New Fights", "B-F", open.Id));
        // Rules the era does not allow in 1993.
        Assert.Contains("one round", Refuse(Spec(w, minutes: 5)));
        Assert.Contains("one round", Refuse(Spec(w, minutes: 12)));
        Assert.Contains("not the sport's rule yet", Refuse(Spec(w, gloves: GloveType.ThinOpenFinger)));
        Assert.Contains("cage or a ring", Refuse(Spec(w, enclosure: Enclosure.OpenFloor)));

        // A banned city.
        var city = w.Get(open.Id)!;
        w.Era.Bans.Add(new SceneBan { Country = w.Get(city.Country)!.Code, Since = w.CurrentDate, Until = w.CurrentDate.AddYears(1) });
        Assert.Contains("Backlash", Refuse(new PlayerFoundingSpec("Brand New Fights", "BNF", open.Id)));
        Assert.Contains("Backlash", PlayerFounding.Choices(w, w.CurrentDate).Cities.Single(c => c.Id == open.Id).Refusal);
        w.Era.Bans.Clear();

        Assert.Equal(promos, w.Promotions.Count);
        Assert.False(w.HasPlayer);

        // Once he has a house, he cannot found another.
        FoundOk(w, Spec(w));
        Assert.Contains("already run", Refuse(Spec(w, "Second House", "SHX")));
        Assert.False(PlayerFounding.Choices(w, w.CurrentDate).CanFound);

        // Today's sport is founded at the start screen.
        var modern = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 2, GenerateHistory = false });
        var anyCity = modern.Cities.Values.OrderBy(c => c.Id.Value).First();
        Assert.Null(PlayerFounding.Found(modern, modern.CurrentDate, new PlayerFoundingSpec("Brand New Fights", "BNF", anyCity.Id), out var mw));
        Assert.Contains("Origins", mw);
        Assert.False(PlayerFounding.TryoutOptions(modern, modern.CurrentDate).Available);
    }

    [Fact]
    public void Refuses_TheNamesTheStoryKeeps_ForArrivalsAndFounders()
    {
        var w = Observer(15);
        var open = FirstOpen(w).Id;
        var arrivals = OriginsArrivals.All.Where(a => a.Kind == OriginsArrivalKind.Promotion).ToList();
        Assert.Contains(arrivals, a => a.Name == "Bellwether Fighting Championship");
        Assert.Contains(arrivals, a => a.Name == "Seiryu Grand Combat");
        foreach (var a in arrivals)
        {
            // Its name and its abbreviation, in any case: neither may be his (else it would never arrive).
            Assert.Null(PlayerFounding.Found(w, w.CurrentDate, new PlayerFoundingSpec(a.Name.ToUpperInvariant(), "QQX", open), out var why));
            Assert.Contains("still to bring", why);
            string abbr = a.Abbreviation ?? OriginsFounding.Initials(a.Name);
            Assert.Null(PlayerFounding.Found(w, w.CurrentDate, new PlayerFoundingSpec("Quite New Fights", abbr.ToLowerInvariant(), open), out why));
            Assert.Contains("still to bring", why);
            Assert.False(OriginsFounding.Arrived(w, a));
        }
        foreach (var f in OriginsPromotions.All)
        {
            Assert.Null(PlayerFounding.Found(w, w.CurrentDate, new PlayerFoundingSpec(f.Name.ToLowerInvariant(), "QQX", open), out var why));
            Assert.Contains("already", why);
            Assert.Null(PlayerFounding.Found(w, w.CurrentDate, new PlayerFoundingSpec("Quite New Fights", f.Abbreviation.ToLowerInvariant(), open), out why));
            Assert.Contains("already", why);
        }
        Assert.False(w.HasPlayer);
    }

    [Fact]
    public void SuggestAbbreviation_IsAlwaysOneFoundAccepts()
    {
        var w = Observer(16);
        Assert.Equal("LSO", PlayerFounding.SuggestAbbreviation(w, "Lantern Street Open"));
        Assert.Equal("", PlayerFounding.SuggestAbbreviation(w, "   "));
        Assert.NotEqual("SGC", PlayerFounding.SuggestAbbreviation(w, "Seiryu Grand Combat Two"));
        var names = new[]
        {
            "Lantern Street Open", "São Paulo Luta Livre Clube", "Seiryu Grand Combat Two", "Bellwether Fights Club",
            "Crucible Challenge Again", "Ü", "Ångström Åsa Örebro", "東京 Fight Night", "X", "Shinken Fighting Academy",
        };
        int i = 0;
        foreach (var name in names)
        {
            string abbr = PlayerFounding.SuggestAbbreviation(w, name);
            Assert.InRange(abbr.Length, 2, 5);
            Assert.All(abbr, ch => Assert.True(char.IsAsciiLetterOrDigit(ch) && !char.IsLower(ch), $"'{abbr}' for {name}"));
            var fresh = Observer(16);
            var promo = PlayerFounding.Found(fresh, fresh.CurrentDate, new PlayerFoundingSpec($"Test House {i++}", abbr, FirstOpen(fresh).Id), out var why);
            Assert.True(promo is not null, $"{name} -> {abbr}: {why}");
        }
    }

    [Fact]
    public void FoundedLateInTheYear_ObjectivesStartOnFirstJanuary()
    {
        Assert.False(PlayerFounding.LateInYear(new DateOnly(1993, 10, 20)));   // 73 days left
        Assert.True(PlayerFounding.LateInYear(new DateOnly(1993, 10, 25)));    // 68 days left

        var w = Observer(17);
        var tick = new WorldTick();
        while (!PlayerFounding.LateInYear(w.CurrentDate)) tick.AdvanceWeek(w);
        int year = w.CurrentDate.Year + 1;
        var promo = FoundOk(w, Spec(w));
        Assert.Empty(promo.Objectives);
        var letter = Assert.Single(w.Mail, m => m.From == "The Accountant" && m.Subject.EndsWith("open for business"));
        Assert.Contains("1 January", letter.Body);

        while (w.CurrentDate < new DateOnly(year, 2, 1)) tick.AdvanceWeeksStoppingForPlayer(w, 1);
        Assert.Equal(2, promo.Objectives.Count);
        Assert.All(promo.Objectives, o =>
        {
            Assert.Equal(new DateOnly(year, 1, 1), o.Start);
            Assert.Equal(new DateOnly(year, 12, 31), o.Deadline);
            Assert.Equal(ObjectiveStatus.Active, o.Status);
        });
    }

    [Fact]
    public void ReformsAlreadyOpenAtFounding_AreToldOnce()
    {
        const string rounds = OriginsReforms.Rounds;
        var chapter = OriginsChapters.ByKey(rounds);
        int checkedWorlds = 0;
        for (ulong seed = 1; seed <= 10 && checkedWorlds == 0; seed++)
        {
            var w = Observer(seed);
            var tick = new WorldTick();
            // Wait until the rounds reform has been open (and unfired) for over a month.
            DateOnly? openSince = null;
            while (w.CurrentDate.Year < 2001 && !w.Era.HasFired(rounds))
            {
                tick.AdvanceWeek(w);
                if (OriginsTimeline.IsLive(w, chapter, w.CurrentDate)) openSince ??= w.CurrentDate;
                if (openSince is { } since && w.CurrentDate.DayNumber - since.DayNumber > 45) break;
            }
            if (w.Era.HasFired(rounds) || openSince is null) continue;
            checkedWorlds++;
            var promo = FoundOk(w, Spec(w));
            Assert.Contains(OriginsReforms.Options(w, promo, w.CurrentDate), o => o.Chapter == rounds);
            var note = Assert.Single(w.Mail, m => m.Subject == "House rules we could change");
            Assert.Contains("Rounds and judges", note.Body);
            // The monthly desk does not tell it again.
            for (int week = 0; week < 6; week++) tick.AdvanceWeeksStoppingForPlayer(w, 1);
            Assert.DoesNotContain(w.Mail, m => m.Subject == "We could adopt: Rounds and judges");
        }
        Assert.True(checkedWorlds > 0, "no seed had the rounds reform open and unfired for a month");
    }

    // ---- what he gets ------------------------------------------------------------------

    [Fact]
    public void Found_DayOne_GetsANewcomersHouse()
    {
        var w = Observer(6);
        var date = w.CurrentDate;
        var city = w.Get(FirstOpen(w).Id)!;
        var promo = FoundOk(w, Spec(w, minutes: 20, gloves: GloveType.Optional, enclosure: Enclosure.Ring));

        Assert.True(w.HasPlayer);
        Assert.Equal(promo.Id, w.PlayerPromotion);
        Assert.True(promo.IsPlayerControlled);
        Assert.Single(w.Promotions.Values, p => p.IsPlayerControlled);
        Assert.Equal(PlayerFounding.Bank.Amount, promo.Balance.Amount);
        Assert.InRange(promo.Balance.Amount, OriginsFounding.NewcomerBank(0), OriginsFounding.NewcomerBank(OriginsFounding.NewcomerBankSteps));
        Assert.Equal(5, promo.Prestige.Value);
        Assert.Equal(50, promo.Reputation.Value);
        Assert.Equal(city.Id, promo.HomeCity);
        Assert.Equal(city.Region, promo.BaseRegion);
        Assert.Equal(1993, promo.FoundedYear);
        Assert.Equal(ShowFormat.Card, promo.Rules.ShowFormat);
        Assert.Equal(Core.Sim.Org.OrgSizing.SizeOf(promo), promo.TierClearance);
        Assert.Equal(15, promo.PopularityByCity[city.Id].Value);

        // The rules he chose, inside what 1993 allows.
        var r = promo.Rules.Fight!;
        Assert.Equal(1, r.Rounds);
        Assert.Equal(20, r.RoundMinutes);
        Assert.False(r.Judges);
        Assert.Equal(GloveType.Optional, r.Gloves);
        Assert.Equal(Enclosure.Ring, r.Enclosure);
        Assert.True(r.OpenWeight);
        Assert.Equal(20, promo.Rules.RoundMinutes);
        Assert.Equal(1, promo.Rules.StandardRounds);

        // One open division with a vacant belt; nobody signed; empty desks.
        var div = Assert.Single(promo.Divisions);
        Assert.True(div.Open);
        var belt = w.Get(div.Title!.Value)!;
        Assert.Null(belt.CurrentChampion);
        Assert.Equal("LSO Open-weight Championship", belt.Name);
        Assert.Empty(w.RosterOf(promo.Id));
        Assert.Empty(promo.Staff);

        // Objectives for the rest of the year, an inbox that opens with the accountant. (No sponsor will touch
        // the sport on day one: the first backers' brands come later, for every house alike.)
        Assert.Equal(2, promo.Objectives.Count);
        Assert.All(promo.Objectives, o => Assert.Equal(new DateOnly(1993, 12, 31), o.Deadline));
        Assert.All(promo.Objectives, o => Assert.Equal(date, o.Start));
        var letter = Assert.Single(w.Mail);
        Assert.Equal("The Accountant", letter.From);
        Assert.Contains("open tryout", letter.Body);
        Assert.Contains(w.News, n => n.Headline == $"{promo.Name} opens in {city.Name}");
    }

    // ---- phase 3: the unified code and the second centre ---------------------------------

    private static World AfterRulesAndGloves(ulong seed)
    {
        var w = Observer(seed);
        Era.Unlock(w, Capability.Rounds);
        Era.Unlock(w, Capability.Gloves);
        return w;
    }

    [Fact]
    public void UnderTheUnifiedCode_OnlyTheEnclosureIsHis_AsForTheAi()
    {
        var w = AfterRulesAndGloves(18);
        Era.Fire(w, OriginsReforms.Unified, w.CurrentDate);
        var choices = PlayerFounding.Choices(w, w.CurrentDate);
        Assert.True(choices.UnifiedCode);
        Assert.Empty(choices.SingleRoundMinutes);
        Assert.Equal(new[] { GloveType.Modern }, choices.Gloves);
        Assert.Equal(new[] { Enclosure.Cage, Enclosure.Ring }, choices.Enclosures);
        var rules = FirstOpen(w).Rules;
        Assert.Equal((3, 5), (rules.StandardRounds, rules.TitleRounds));
        Assert.Contains(rules.Notes, n => n.Contains("code"));

        Assert.Null(PlayerFounding.Found(w, w.CurrentDate, Spec(w, minutes: 20), out var why));
        Assert.Contains("code", why);
        Assert.Null(PlayerFounding.Found(w, w.CurrentDate, Spec(w, gloves: GloveType.ThinOpenFinger), out why));
        Assert.Contains("code", why);

        var promo = FoundOk(w, Spec(w, gloves: GloveType.Modern, enclosure: Enclosure.Ring));
        Assert.True(OriginsReforms.HasUnified(promo));
        Assert.Equal((3, 5, 5), (promo.Rules.StandardRounds, promo.Rules.TitleRounds, promo.Rules.RoundMinutes));
        Assert.Equal(Enclosure.Ring, promo.Rules.Fight!.Enclosure);
        Assert.Contains("commissions' code", w.Mail.Single(m => m.Subject.EndsWith("open for business")).Body);

        // The AI newcomer of that day is under the same code.
        var twin = AfterRulesAndGloves(18);
        Era.Fire(twin, OriginsReforms.Unified, twin.CurrentDate);
        var ai = OriginsFounding.Found(twin, twin.CurrentDate, null)!;
        Assert.True(OriginsReforms.HasUnified(ai));
        Assert.Equal((ai.Rules.StandardRounds, ai.Rules.TitleRounds), (promo.Rules.StandardRounds, promo.Rules.TitleRounds));
    }

    [Fact]
    public void InTheSecondCentre_ARingIsForced_AndTheCodeDoesNotBind()
    {
        var w = AfterRulesAndGloves(19);
        // The second centre is wherever its chapter put it: here, the country of the first open city outside North America.
        var abroad = PlayerFounding.Choices(w, w.CurrentDate).Cities.First(c => c.Open && c.Region != Region.NorthAmerica);
        Era.Fire(w, OriginsScenes.SecondCentreChapter, w.CurrentDate, city: abroad.Id);
        Era.Fire(w, OriginsReforms.Unified, w.CurrentDate);
        var choices = PlayerFounding.Choices(w, w.CurrentDate);
        Assert.Equal(abroad.CountryCode, choices.SecondCentre);
        var tokyo = choices.Cities.First(c => c.Open && c.CountryCode == choices.SecondCentre);
        Assert.Equal(new[] { Enclosure.Ring }, tokyo.Rules.Enclosures);
        Assert.False(tokyo.Rules.UnifiedCode);
        Assert.Contains(tokyo.Rules.Notes, n => n.Contains("ring"));
        Assert.True(choices.Cities.First(c => c.Open && c.CountryCode != choices.SecondCentre).Rules.UnifiedCode);

        Assert.Null(PlayerFounding.Found(w, w.CurrentDate, new PlayerFoundingSpec("Harbour Lights Fighting", "HLF", tokyo.Id, Enclosure: Enclosure.Cage), out var why));
        Assert.Contains("ring", why);
        var promo = FoundOk(w, new PlayerFoundingSpec("Harbour Lights Fighting", "HLF", tokyo.Id));
        Assert.Equal(Enclosure.Ring, promo.Rules.Fight!.Enclosure);
        Assert.False(OriginsReforms.HasUnified(promo));
        Assert.Equal((3, 3), (promo.Rules.StandardRounds, promo.Rules.TitleRounds));   // its era's rules, its own
        Assert.False(OriginsReforms.BoundByCode(w, promo));
    }

    [Fact]
    public void LicensedBeforeTheCode_TheCodeBindsHim_RebooksHisCard_AndTellsHim()
    {
        var w = Observer(20);
        var promo = FoundOk(w, Spec(w));
        var country = w.Get(w.Get(promo.HomeCity!.Value)!.Country)!.Code;
        w.Era.Sanctions.Add(new Sanction { Country = country, On = w.CurrentDate });
        PlayerFounding.HoldTryout(w, w.CurrentDate, out _);
        var date = w.CurrentDate.AddDays(35);
        var ev = OriginsShows.ScheduleCard(w, promo.Id, date, OriginsShows.SuggestCard(w, promo.Id, date))!;
        Assert.NotNull(ev);
        Assert.All(ev.Card, f => Assert.Equal(1, f.ScheduledRounds));
        Assert.False(OriginsReforms.HasUnified(promo));

        Era.Fire(w, OriginsReforms.Unified, w.CurrentDate);
        Assert.True(OriginsReforms.BoundByCode(w, promo));
        new WorldTick().AdvanceWeeksStoppingForPlayer(w, 1);

        Assert.True(OriginsReforms.HasUnified(promo));
        Assert.All(ev.Card, f => Assert.Equal(f.IsTitleFight ? OriginsReforms.UnifiedTitleRounds : OriginsReforms.CardRounds, f.ScheduledRounds));
        var mail = Assert.Single(w.Mail, m => m.Subject == "The commission's code is now our rulebook");
        Assert.Contains("rebooked", mail.Body);
    }

    [Fact]
    public void TryoutMen_AreOfTheirEra_LikeTheAiNewcomers()
    {
        var w = Observer(21);
        new WorldTick().AdvanceWeeks(w, 52 * 6 + 10);
        var promo = FoundOk(w, Spec(w));
        var home = w.Get(w.Get(promo.HomeCity!.Value)!.Country)!.Code;
        var sizes = OriginsTalent.SizesNow(w);
        var men = PlayerFounding.HoldTryout(w, w.CurrentDate, out _).Select(m => w.Get(m.Fighter)!).ToList();
        Assert.NotEmpty(men);
        Assert.All(men, f =>
        {
            Assert.Equal(home, f.OriginCountryCode);
            Assert.True(sizes.Contains(f.WeightClass) || f.WeightClass == WeightClass.Heavyweight, $"{f.WeightClass} is not a size of the day");
        });

        // The same men an AI newcomer's tryout makes: one shared path (OriginsFounding.TryoutMan over OriginsTalent.Create).
        var twin = Observer(21);
        new WorldTick().AdvanceWeeks(twin, 52 * 6 + 10);
        var house = FoundOk(twin, Spec(twin));
        var rng = new Core.Sim.Rng.SimRandom(42);
        var gen = Core.Sim.Generation.FighterGenerator.ForWorld(twin);
        var aiMan = OriginsFounding.TryoutMan(twin, gen, house, home, twin.CurrentDate, rng)!;
        Assert.Equal(home, aiMan.OriginCountryCode);
        Assert.Contains(aiMan.WeightClass, sizes.Append(WeightClass.Heavyweight));
    }

    [Fact]
    public void Found_OfItsEra_RoundsGlovesAndWeightLines()
    {
        // After rounds and gloves are the sport's: both forced, the enclosure still his.
        var w = Observer(7);
        Era.Unlock(w, Capability.Rounds);
        Era.Unlock(w, Capability.Gloves);
        var choices = PlayerFounding.Choices(w, w.CurrentDate);
        Assert.True(choices.RoundsAndJudges);
        Assert.Empty(choices.SingleRoundMinutes);
        Assert.Equal(new[] { GloveType.ThinOpenFinger }, choices.Gloves);
        Assert.Null(PlayerFounding.Found(w, w.CurrentDate, Spec(w, minutes: 20), out var why));
        Assert.Contains("five minutes", why);
        Assert.Null(PlayerFounding.Found(w, w.CurrentDate, Spec(w, gloves: GloveType.Bare), out why));
        Assert.Contains("sport's rule now", why);
        var promo = FoundOk(w, Spec(w, enclosure: Enclosure.Ring));
        var r = promo.Rules.Fight!;
        Assert.Equal((3, 5, true, GloveType.ThinOpenFinger, Enclosure.Ring), (r.Rounds!.Value, r.RoundMinutes!.Value, r.Judges, r.Gloves, r.Enclosure));
        Assert.Equal((5, 3, 3), (promo.Rules.RoundMinutes, promo.Rules.StandardRounds, promo.Rules.TitleRounds));
        Assert.True(Assert.Single(promo.Divisions).Open);

        // After the first weight split: the drawn lines, each with a vacant belt, no open division.
        var v = Observer(7);
        Era.Unlock(v, Capability.Rounds);
        Era.Unlock(v, Capability.Gloves);
        var lines = OriginsReforms.LinesOf(OriginsReforms.Ladder[0]);
        foreach (var wc in lines) Era.Unlock(v, Capability.Class(wc));
        Assert.Equal(lines, PlayerFounding.Choices(v, v.CurrentDate).WeightLines);
        var split = FoundOk(v, Spec(v));
        Assert.Equal(lines, split.Divisions.Select(d => d.WeightClass).ToList());
        Assert.All(split.Divisions, d =>
        {
            Assert.False(d.Open);
            Assert.Null(v.Get(d.Title!.Value)!.CurrentChampion);
        });
        Assert.False(split.Rules.Fight!.OpenWeight);
    }

    [Fact]
    public void Found_YearsIn_MatchesTheChoicesOfThatDay()
    {
        var w = Observer(8);
        new WorldTick().AdvanceWeeks(w, 52 * 5 - 20);   // mid-year: objectives for the rest of it
        var choices = PlayerFounding.Choices(w, w.CurrentDate);
        Assert.True(choices.CanFound);
        var spec = new PlayerFoundingSpec("Harbour Lights Fighting", "HLF", choices.Cities.First(c => c.Open).Id,
            choices.SingleRoundMinutes.FirstOrDefault() is var m and > 0 ? m : null, choices.Gloves[0], choices.Enclosures[^1]);
        var promo = FoundOk(w, spec);
        Assert.Equal(choices.RoundsAndJudges, promo.Rules.Fight!.Judges);
        Assert.Equal(choices.Gloves[0], promo.Rules.Fight.Gloves);
        Assert.Equal(choices.OpenWeight, promo.Divisions.Any(d => d.Open));
        // (Omitted divisions are the city's suggestion, not every drawn line.)
        Assert.Equal(choices.Cities.First(c => c.Open).SuggestedLines.Count, promo.Divisions.Count(d => !d.Open));
        Assert.Equal(PlayerFounding.Bank, promo.Balance);
        Assert.NotEmpty(promo.SponsorDeals);   // a hometown backer, once the sport has them
        Assert.Equal(2, promo.Objectives.Count(o => o.Status == ObjectiveStatus.Active));
        Assert.All(promo.Objectives, o => Assert.Equal(new DateOnly(w.CurrentDate.Year, 12, 31), o.Deadline));
    }

    // ---- the tryout -------------------------------------------------------------------

    [Fact]
    public void Tryout_OnceOnly_SignsLocalMenOnTheFloor_AsHisScoutsReadThem()
    {
        var w = Observer(9);
        Assert.Equal(PlayerBooking.NoHouseText, PlayerFounding.TryoutOptions(w, w.CurrentDate).Refusal);
        var promo = FoundOk(w, Spec(w));
        var home = w.Get(w.Get(promo.HomeCity!.Value)!.Country)!.Code;

        var offer = PlayerFounding.TryoutOptions(w, w.CurrentDate);
        Assert.True(offer.Available);
        var men = PlayerFounding.HoldTryout(w, w.CurrentDate, out var why);
        Assert.Null(why);
        Assert.Equal(PlayerFounding.TryoutTurnout, men.Count);
        foreach (var m in men)
        {
            var f = w.Get(m.Fighter)!;
            Assert.Equal(promo.Id, f.Contract!.Promotion);
            Assert.Equal(promo.Id, f.DiscoveredBy);
            Assert.Equal(home, f.OriginCountryCode);
            Assert.Equal(Core.Sim.Contracts.Negotiator.OriginsPurseFloor, f.Contract.PursePerFight.Amount);
            Assert.Equal(0, f.Record.TotalBouts);
            Assert.Equal(ScoutingService.PerceivedAbility(w, promo, f), m.ScoutedRead);   // the fog, never the truth
        }
        Assert.Equal(men.Count, w.RosterOf(promo.Id).Count());
        Assert.Equal(PlayerFounding.Bank.Amount, promo.Balance.Amount);   // free
        Assert.Equal(w.CurrentDate, PlayerFounding.TryoutHeldOn(promo));

        // Once.
        Assert.False(PlayerFounding.TryoutOptions(w, w.CurrentDate).Available);
        Assert.Empty(PlayerFounding.HoldTryout(w, w.CurrentDate.AddDays(7), out why));
        Assert.Contains("one free open tryout", why);
        Assert.Equal(men.Count, w.RosterOf(promo.Id).Count());
    }

    // ---- the first night -------------------------------------------------------------------

    [Fact]
    public void FoundedHouse_BooksAndRunsAFirstNight_AndTheTickStopsForIt()
    {
        var w = Observer(10);
        var tick = new WorldTick();
        tick.AdvanceWeeks(w, 6);
        var promo = FoundOk(w, Spec(w));
        PlayerFounding.HoldTryout(w, w.CurrentDate, out _);

        var date = w.CurrentDate.AddDays(28);
        var card = OriginsShows.SuggestCard(w, promo.Id, date);
        Assert.True(card.Count >= 2, $"only {card.Count} bouts");
        var ev = OriginsShows.ScheduleCard(w, w.PlayerPromotion, date, card);
        Assert.NotNull(ev);
        Assert.Equal(promo.Id, ev!.Promotion);
        Assert.NotNull(ev.VenueId);

        var report = tick.AdvanceWeeksStoppingForPlayer(w, 10);
        Assert.Equal(ev.Id, report.PendingPlayerEvent?.Id);
        tick.RunEventLive(w, ev, playByPlay: false);
        Assert.Equal(EventStatus.Completed, ev.Status);
        Assert.All(ev.Card, f => Assert.NotNull(f.Outcome));
        Assert.Contains("first-show", promo.Milestones);

        // His inbox and objectives run on as any player's.
        tick.AdvanceWeeksStoppingForPlayer(w, 8);
        Assert.True(w.Mail.Count > 1);
        Assert.Contains(promo.Objectives, o => o.Kind == ObjectiveKind.RunEvents && o.Status != ObjectiveStatus.Failed);

        // And the AI never books his house's nights.
        Assert.DoesNotContain(w.Events.Values, e => e.Promotion == promo.Id && e.Id != ev.Id);
    }

    // ---- the story ---------------------------------------------------------------------

    [Fact]
    public void FourthHouse_BeforeTheChapterOpens_IsCreditedWhenItDoes()
    {
        var w = Observer(11);
        var promo = FoundOk(w, Spec(w));
        var tick = new WorldTick();
        for (int week = 0; week < 52 * 4 && !w.Era.HasFired("rivals-appear"); week++) tick.AdvanceWeeksStoppingForPlayer(w, 1);
        var rec = w.Era.Chapter("rivals-appear");
        Assert.NotNull(rec);
        Assert.Equal(ChapterVariant.Player, rec!.Variant);
        Assert.Equal(promo.Id, rec.Promotion);
        Assert.Contains(w.Mail, m => m.Subject == OriginsChapters.ByKey("rivals-appear").ByPlayer!.Headline.Replace("{Org}", promo.Name));
    }

    [Fact]
    public void FourthHouse_StillCredited_WhenANewcomerOpensAfterHim()
    {
        var w = Observer(12);
        var promo = FoundOk(w, Spec(w));
        var fifth = OriginsFounding.Found(w, w.CurrentDate, null);   // an AI newcomer opens after him, before the chapter can fire
        Assert.NotNull(fifth);
        Assert.True(fifth!.Id.Value > promo.Id.Value);
        var tick = new WorldTick();
        for (int week = 0; week < 52 * 4 && !w.Era.HasFired("rivals-appear"); week++) tick.AdvanceWeeksStoppingForPlayer(w, 1);
        var rec = w.Era.Chapter("rivals-appear");
        Assert.NotNull(rec);
        Assert.Equal(ChapterVariant.Player, rec!.Variant);
        Assert.Equal(promo.Id, rec.Promotion);
    }

    [Fact]
    public void FourthHouse_WhileTheChapterIsLive_FiresItForThePlayerAtOnce()
    {
        var chapter = OriginsChapters.ByKey("rivals-appear");
        int tried = 0;
        for (ulong seed = 1; seed <= 12 && tried < 2; seed++)
        {
            var w = Observer(seed);
            var tick = new WorldTick();
            while (!OriginsTimeline.IsLive(w, chapter, w.CurrentDate) && !w.Era.HasFired(chapter.Key) && w.CurrentDate.Year < 1997)
                tick.AdvanceWeek(w);
            if (!OriginsTimeline.IsLive(w, chapter, w.CurrentDate) || w.Promotions.Values.Count(p => !p.Defunct) != 3) continue;
            tried++;
            var promo = FoundOk(w, Spec(w));
            var rec = w.Era.Chapter(chapter.Key);
            Assert.NotNull(rec);
            Assert.Equal(w.CurrentDate, rec!.FiredOn);
            Assert.Equal(ChapterVariant.Player, rec.Variant);
            Assert.Equal(promo.Id, rec.Promotion);
            Assert.True(w.Era.IsUnlocked(Capability.NewPromotions));
        }
        Assert.True(tried > 0, "no seed reached the live chapter with three houses");
    }

    // ---- determinism and the save ------------------------------------------------------------

    [Fact]
    public void SameSpecSameDay_SameWorld()
    {
        var a = Observer(13);
        var b = Observer(13);
        new WorldTick().AdvanceWeeks(a, 20);
        new WorldTick().AdvanceWeeks(b, 20);
        FoundOk(a, Spec(a));
        FoundOk(b, Spec(b));
        PlayerFounding.HoldTryout(a, a.CurrentDate, out _);
        PlayerFounding.HoldTryout(b, b.CurrentDate, out _);
        var diffs = PersistenceGraph.Diff(a, b);
        Assert.True(diffs.Count == 0, PersistenceGraph.Describe(diffs));
        new WorldTick().AdvanceWeeksStoppingForPlayer(a, 26);
        new WorldTick().AdvanceWeeksStoppingForPlayer(b, 26);
        diffs = PersistenceGraph.Diff(a, b);
        Assert.True(diffs.Count == 0, "Same founding ran apart:\n" + PersistenceGraph.Describe(diffs));
    }

    [Fact]
    public void SaveLoadFoundRunOn_EqualsFoundWithoutSaving()
    {
        var world = Observer(14);
        new WorldTick().AdvanceWeeks(world, 12);
        string path = Path.Combine(Path.GetTempPath(), $"mma_founding_{Guid.NewGuid():N}.db");
        try
        {
            SaveGame.Save(world, path);
            var loaded = SaveGame.Load(path);
            var spec = Spec(world, enclosure: Enclosure.Ring);
            FoundOk(world, spec);
            FoundOk(loaded, spec);
            PlayerFounding.HoldTryout(world, world.CurrentDate, out _);
            PlayerFounding.HoldTryout(loaded, loaded.CurrentDate, out _);
            new WorldTick().AdvanceWeeksStoppingForPlayer(world, 20);
            new WorldTick().AdvanceWeeksStoppingForPlayer(loaded, 20);
            var diffs = PersistenceGraph.Diff(world, loaded);
            Assert.True(diffs.Count == 0, "Saved-then-founded and never-saved worlds ran apart:\n" + PersistenceGraph.Describe(diffs));

            // ...and the founded house survives its own save (the tryout stays spent).
            SaveGame.Save(loaded, path);
            var again = SaveGame.Load(path);
            Assert.True(again.HasPlayer);
            Assert.True(again.Get(again.PlayerPromotion)!.IsPlayerControlled);
            Assert.False(PlayerFounding.TryoutOptions(again, again.CurrentDate).Available);
            diffs = PersistenceGraph.Diff(loaded, again);
            Assert.True(diffs.Count == 0, PersistenceGraph.Describe(diffs));
        }
        finally { File.Delete(path); }
    }
}
