using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Generation.Origins;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Tests;

/// <summary>
/// Origins follow-ups from the UI work: the Desk's replacements at open-weight and banded houses, a bracket
/// house's rounds when it becomes a card house, the split's words matching its lines, the refusal for a house
/// with no belt, the "is this class fought" helper, and the Story screen's player-facing gap notes.
/// </summary>
public class OriginsFollowupTests
{
    private static World Player(ulong seed, int index) => new WorldBuilder().Build(WorldBuilder.Options.Origins(seed, index));
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

    // The sport up to the first split: rounds, judges and gloves are the sport's, the split is open.
    private static void ReadyToSplit(World w)
    {
        w.CurrentDate = Month(60);
        Fired(w, Month(2), "crucible-first-night", "first-star");
        Fired(w, Month(34), "press-turns");
        Fired(w, Month(50), "rounds-and-judges", "gloves-adopted");
    }

    // A clean slate for the player's own cards (no other booking holds his men), and men ready to fight.
    private static List<Fighter> FreeMen(World w, DateOnly date)
    {
        foreach (var e in w.Events.Values.Where(e => e.Promotion == w.PlayerPromotion).ToList()) w.Events.Remove(e.Id);
        return w.RosterOf(w.PlayerPromotion)
            .Where(f => f.Sex == Sex.Male && f.IsMatchReady(date) && !Core.Sim.Contracts.DealWindow.OutOfDeal(w, f))
            .OrderBy(f => f.Id.Value).ToList();
    }

    // ---- 1. replacements -------------------------------------------------------------------------

    [Fact]
    public void ReplaceFighter_AtAnOpenWeightHouse_TakesAnyMan_AndTheBoutStaysOpen()
    {
        var w = Player(5, 1);   // SFA: a card house at open weight
        var sfa = w.Get(w.PlayerPromotion)!;
        Assert.True(sfa.Rules.Fight!.OpenWeight);
        var booking = new PlayerBooking();
        var date = w.CurrentDate.AddDays(35);
        var men = FreeMen(w, date);
        var ev = booking.CreateEvent(w, date);

        // Two men of one class in the bout, a man of another class to come in.
        var red = men[0];
        var blue = men.First(f => f.Id != red.Id);
        var heavier = men.First(f => f.Id != red.Id && f.Id != blue.Id && f.WeightClass != blue.WeightClass);
        Assert.True(booking.AddBout(w, ev, red.Id, blue.Id, 1, false, out var err), err);
        var bout = ev.Card.Single();
        Assert.True(bout.OpenWeight);
        Assert.Equal(Division.OpenClass, bout.WeightClass);

        Assert.True(booking.ReplaceFighter(w, ev, bout.Id, blue.Id, heavier.Id, out err), err);
        var now = ev.Card.Single();
        Assert.Equal(bout.Id, now.Id);
        Assert.Equal(heavier.Id, now.BlueCorner);
        Assert.True(now.OpenWeight);   // still an open-weight bout
        Assert.Equal(Division.OpenClass, now.WeightClass);
    }

    [Fact]
    public void ReplaceFighter_AtABandedHouse_TakesAnyManOfTheBand_AndRefusesAnotherBand()
    {
        var w = Player(5, 1);
        var sfa = w.Get(w.PlayerPromotion)!;
        ReadyToSplit(w);
        Assert.True(OriginsReforms.TryAdopt(w, sfa, OriginsReforms.Ladder[0], w.CurrentDate, out var refused), refused);
        var heavy = sfa.Divisions.Single(d => d.WeightClass == WeightClass.Heavyweight);
        Assert.Equal(WeightClass.LightHeavyweight, heavy.Lightest);

        var booking = new PlayerBooking();
        var date = w.CurrentDate.AddDays(35);
        var men = FreeMen(w, date);
        Assert.True(men.Count >= 4);
        // Natural weights for the test: a heavyweight and a light heavyweight in the bout (both of the heavy
        // band), another light heavyweight to come in, and a middleweight who belongs to the other band.
        men[0].WeightClass = WeightClass.Heavyweight;
        men[1].WeightClass = WeightClass.LightHeavyweight;
        men[2].WeightClass = WeightClass.LightHeavyweight;
        men[3].WeightClass = WeightClass.Middleweight;
        var ev = booking.CreateEvent(w, date);
        Assert.True(booking.AddBout(w, ev, men[0].Id, men[1].Id, 3, false, out var err), err);
        var bout = ev.Card.Single();
        Assert.Equal(WeightClass.Heavyweight, bout.WeightClass);   // filed under the band's top class

        // A light heavyweight replaces the heavyweight: his natural class isn't the bout's, his band is.
        Assert.True(booking.ReplaceFighter(w, ev, bout.Id, men[0].Id, men[2].Id, out err), err);
        Assert.Equal(men[2].Id, ev.Card.Single().RedCorner);
        Assert.Equal(WeightClass.Heavyweight, ev.Card.Single().WeightClass);
        Assert.False(ev.Card.Single().OpenWeight);

        // A middleweight is in the light band: refused, and told why.
        Assert.False(booking.ReplaceFighter(w, ev, bout.Id, men[1].Id, men[3].Id, out err));
        Assert.Equal("The replacement must be in the bout's division.", err);
    }

    [Fact]
    public void ReplaceFighter_InTodaysMode_StillKeepsToTheClass()
    {
        var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 11 });
        var booking = new PlayerBooking();
        var ev = booking.CreateEvent(w, w.CurrentDate.AddDays(60));
        var promo = w.Get(w.PlayerPromotion)!;
        var div = promo.Divisions.Where(d => d.Sex == Sex.Male)
            .First(d => w.RosterOf(promo.Id).Count(f => d.Matches(f) && f.IsMatchReady(ev.Date)) >= 2);
        var pair = w.RosterOf(promo.Id).Where(f => div.Matches(f) && f.IsMatchReady(ev.Date)).OrderBy(f => f.Id.Value).Take(2).ToList();
        Assert.True(booking.AddBout(w, ev, pair[0].Id, pair[1].Id, 3, false, out var err), err);
        var other = w.RosterOf(promo.Id).OrderBy(f => f.Id.Value)
            .First(f => f.Sex == Sex.Male && f.WeightClass != div.WeightClass && f.IsMatchReady(ev.Date)
                        && !Core.Sim.Contracts.DealWindow.OutOfDeal(w, f));
        Assert.False(booking.ReplaceFighter(w, ev, ev.Card[0].Id, pair[1].Id, other.Id, out err));
        Assert.Equal("The replacement must be in the bout's weight class.", err);
        Assert.False(ev.Card[0].OpenWeight);
    }

    // ---- 2. a bracket house becomes a card house ---------------------------------------------------

    [Fact]
    public void ABracketHouse_ThatSplits_TakesTheCardStandardRounds()
    {
        var w = OriginsWorldTests.AiWorld(6);
        var cru = House(w, "CRU");
        ReadyToSplit(w);
        Assert.True(OriginsReforms.TryAdopt(w, cru, OriginsReforms.Rounds, w.CurrentDate, out var why), why);
        Assert.Equal(OriginsReforms.BracketRounds, cru.Rules.Fight!.Rounds);   // a bracket man fights three times a night
        Assert.Equal(OriginsReforms.BracketRounds, cru.Rules.StandardRounds);
        if (!OriginsReforms.HasGloves(cru))
            Assert.True(OriginsReforms.TryAdopt(w, cru, OriginsReforms.Gloves, w.CurrentDate, out why), why);

        var option = OriginsReforms.Options(w, cru, w.CurrentDate).Single(o => o.Chapter == OriginsReforms.Ladder[0]);
        Assert.Contains($"over {OriginsReforms.CardRounds} rounds", option.Detail);
        Assert.True(OriginsReforms.TryAdopt(w, cru, OriginsReforms.Ladder[0], w.CurrentDate, out why), why);

        Assert.Equal(ShowFormat.Card, cru.Rules.ShowFormat);
        Assert.Equal(OriginsReforms.CardRounds, cru.Rules.Fight!.Rounds);
        Assert.Equal(OriginsReforms.CardRounds, cru.Rules.StandardRounds);
        Assert.Equal(OriginsReforms.CardRounds, cru.Rules.TitleRounds);
        Assert.True(cru.Rules.Fight.Judges);
        Assert.True(OriginsReforms.HasRounds(cru));
        Assert.Contains(w.News, n => n.Headline.Contains("ends its one-night brackets") && n.Body.Contains($"{OriginsReforms.CardRounds} rounds"));

        // A card house that splits keeps its rounds as they are.
        var sfa = House(w, "SFA");
        int before = sfa.Rules.StandardRounds;
        Assert.True(OriginsReforms.TryAdopt(w, sfa, OriginsReforms.Ladder[0], w.CurrentDate, out why), why);
        Assert.Equal(before, sfa.Rules.StandardRounds);
    }

    // ---- 3. the split's words match its lines ------------------------------------------------------

    [Fact]
    public void TheSplitsWords_MatchTheLinesItDraws()
    {
        var w = OriginsWorldTests.AiWorld(6);
        var sfa = House(w, "SFA");
        ReadyToSplit(w);
        var first = OriginsReforms.Options(w, sfa, w.CurrentDate).Single(o => o.Chapter == OriginsReforms.Ladder[0]);
        int line = WeightClasses.LimitLbs(WeightClass.Middleweight);
        Assert.Contains($"Men over {line} pounds fight at heavyweight, everyone else at middleweight", first.Detail);
        Assert.DoesNotContain("two hundred", first.Detail);
        Assert.DoesNotContain("bracket", first.Detail);   // a card house has none to give up

        Assert.True(OriginsReforms.Adopt(w, sfa, OriginsReforms.Ladder[0], w.CurrentDate));
        // The lines drawn are the lines described: the heavy band starts one class over the light band's top.
        var heavy = sfa.Divisions.Single(d => d.WeightClass == WeightClass.Heavyweight);
        Assert.Equal(WeightClass.Middleweight + 1, heavy.Lightest);

        // The next chapter's bands, lightest first, each with its top.
        var next = OriginsReforms.BandsAfter(sfa, OriginsReforms.Ladder[1]);
        Assert.Equal(new[] { WeightClass.Welterweight, WeightClass.Middleweight, WeightClass.LightHeavyweight, WeightClass.Heavyweight },
            next.Select(b => b.Class));
        Fired(w, Month(62), OriginsReforms.Ladder[0]);
        w.CurrentDate = Month(OriginsChapters.ByKey(OriginsReforms.Ladder[1]).EarliestMonth + 1);
        var middle = OriginsReforms.Options(w, sfa, w.CurrentDate).Single(o => o.Chapter == OriginsReforms.Ladder[1]);
        Assert.Contains("welterweight up to 170 pounds, middleweight 171 to 185 pounds, light heavyweight 186 to 205 pounds, heavyweight over 205 pounds", middle.Detail);
    }

    // ---- 4. a house with no belt is told so ---------------------------------------------------------

    [Fact]
    public void ABracketHouseWithNoBelt_IsToldThereIsNoBeltToSplit()
    {
        var w = Player(5, 0);   // CRU
        var cru = w.Get(w.PlayerPromotion)!;
        ReadyToSplit(w);
        Assert.True(OriginsReforms.TryAdopt(w, cru, OriginsReforms.Rounds, w.CurrentDate, out var why), why);
        if (!OriginsReforms.HasGloves(cru))
            Assert.True(OriginsReforms.TryAdopt(w, cru, OriginsReforms.Gloves, w.CurrentDate, out why), why);
        foreach (var d in cru.Divisions) d.Title = null;

        var refusal = OriginsReforms.Refusal(w, cru, OriginsReforms.Ladder[0], w.CurrentDate);
        Assert.NotNull(refusal);
        Assert.Contains("no men's belt to split", refusal);
        Assert.DoesNotContain("nobody is asking", refusal);
        Assert.False(OriginsReforms.TryAdopt(w, cru, OriginsReforms.Ladder[0], w.CurrentDate, out why));
        Assert.Equal(refusal, why);
    }

    // ---- 5. is this class fought? -------------------------------------------------------------------

    [Fact]
    public void ClassFought_CountsADrawnLine_BeforeAnyChampion()
    {
        var modern = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 11 });
        foreach (var wc in Enum.GetValues<WeightClass>())
            Assert.True(Era.ClassFought(modern, wc, Sex.Male));

        var w = OriginsWorldTests.AiWorld(6);
        var sfa = House(w, "SFA");
        ReadyToSplit(w);
        foreach (var wc in Enum.GetValues<WeightClass>()) Assert.False(Era.ClassFought(w, wc, Sex.Male));   // open weight: no lines

        Assert.True(OriginsReforms.Adopt(w, sfa, OriginsReforms.Ladder[0], w.CurrentDate));
        Assert.False(Era.AllowsDivision(w, WeightClass.Middleweight, Sex.Male));   // nobody crowned yet: the meaning is unchanged
        Assert.True(Era.ClassFought(w, WeightClass.Middleweight, Sex.Male));
        Assert.True(Era.ClassFought(w, WeightClass.Heavyweight, Sex.Male));
        Assert.False(Era.ClassFought(w, WeightClass.LightHeavyweight, Sex.Male));   // inside the heavy band, not a line
        Assert.False(Era.ClassFought(w, WeightClass.Middleweight, Sex.Female));

        sfa.Defunct = true;   // a closed house fights nowhere
        Assert.False(Era.ClassFought(w, WeightClass.Middleweight, Sex.Male));
    }

    // ---- 6. the Story screen's notes ---------------------------------------------------------------

    [Fact]
    public void EveryGap_HasAPlayerNote_WithNoTalkOfPhasesOrBuilds()
    {
        Assert.Equal(OriginsStory.Gaps.Keys.OrderBy(k => k, StringComparer.Ordinal),
            OriginsStory.PlayerNotes.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var (key, note) in OriginsStory.PlayerNotes)
        {
            Assert.NotNull(OriginsChapters.ByKey(key));
            Assert.Equal(note, OriginsStory.PlayerNote(key));
            Assert.False(string.IsNullOrWhiteSpace(note));
            foreach (var word in new[] { "phase", "build", "Origins", "mode", "(" })
                Assert.DoesNotContain(word, note, StringComparison.OrdinalIgnoreCase);
        }
        Assert.Null(OriginsStory.PlayerNote("rounds-and-judges"));   // a chapter that does what it says
    }
}
