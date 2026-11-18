using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Generation.Origins;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Scouting;

namespace MmaManager.Tests;

/// <summary>
/// The player's founding options in an Origins world: recurring open tryouts on the AI houses' rule (an option he
/// calls), the choice of divisions at founding (suggested: what his purse can staff), and a weight split that keeps
/// the bouts already booked on announced nights (for every house).
/// </summary>
public class PlayerFoundingOptionsTests
{
    private static World Observer(ulong seed) => new WorldBuilder().Build(WorldBuilder.Options.OriginsObserver(seed));

    private static FoundingCity FirstOpen(World w) => PlayerFounding.Choices(w, w.CurrentDate).Cities.First(c => c.Open);

    private static Promotion FoundOk(World w, PlayerFoundingSpec spec)
    {
        var promo = PlayerFounding.Found(w, w.CurrentDate, spec, out var why);
        Assert.True(promo is not null, why);
        return promo!;
    }

    private static int Lines(Promotion p, string description) =>
        p.Ledger.Count(e => e.Category == FinanceCategory.Other && e.Description == description);

    // ---- 2. recurring tryouts ------------------------------------------------------------------

    [Fact]
    public void RecurringTryout_OnTheAiRule_OnlyWhenShortAndSpaced()
    {
        var w = Observer(9);
        var promo = FoundOk(w, new PlayerFoundingSpec("Lantern Street Open", "LSO", FirstOpen(w).Id));
        var freeOn = w.CurrentDate;
        Assert.True(PlayerFounding.TryoutOptions(w, freeOn).Free);
        var first = PlayerFounding.HoldTryout(w, freeOn, out var why);
        Assert.Null(why);
        Assert.Equal(PlayerFounding.TryoutTurnout, first.Count);
        Assert.Equal(freeOn, PlayerFounding.TryoutHeldOn(promo));

        // Inside the spacing: no tryout, and he is told when the next one could be.
        var soon = PlayerFounding.TryoutOptions(w, freeOn.AddDays(7));
        Assert.False(soon.Available);
        Assert.False(soon.Free);
        Assert.Equal(freeOn.AddDays(OriginsFounding.TryoutEveryDays), soon.NextPossibleOn);
        Assert.Contains("one free open tryout", soon.Refusal);
        Assert.Empty(PlayerFounding.HoldTryout(w, freeOn.AddDays(7), out why));
        Assert.Equal(soon.Refusal, why);

        // Half a year on, still short (six men, a night needs ten): the AI houses' tryout is his to call.
        new WorldTick().AdvanceWeeks(w, 26);
        var on = w.CurrentDate;
        Assert.True(on >= freeOn.AddDays(OriginsFounding.TryoutEveryDays));
        Assert.True(OriginsFounding.ShortHanded(w, promo, on, out int before));
        int target = OriginsFounding.TryoutTarget(promo);
        var offer = PlayerFounding.TryoutOptions(w, on);
        Assert.True(offer.Available, offer.Refusal);
        Assert.False(offer.Free);
        Assert.Equal(target - before, offer.Turnout);
        Assert.Null(offer.NextPossibleOn);

        decimal bank = promo.Balance.Amount;
        var men = PlayerFounding.HoldTryout(w, on, out why);
        Assert.Null(why);
        Assert.NotEmpty(men);
        Assert.Equal(target, OriginsFounding.OnTheBooks(w, promo));   // filled to the signing target, as an AI house is
        var home = w.Get(w.Get(promo.HomeCity!.Value)!.Country)!.Code;
        foreach (var m in men)
        {
            var f = w.Get(m.Fighter)!;
            Assert.Equal(promo.Id, f.Contract!.Promotion);
            Assert.Equal(promo.Id, f.DiscoveredBy);
            Assert.Equal(home, f.OriginCountryCode);
            Assert.Equal(Core.Sim.Contracts.Negotiator.OriginsPurseFloor, f.Contract.PursePerFight.Amount);
            Assert.Equal(Core.Sim.Contracts.Negotiator.WantsExclusivity(promo), f.Contract.IsExclusive);   // the house's own terms, as the AI's
            Assert.Contains(f.Accolades, a => a.Title == $"Signed at {promo.Name}'s open tryout");      // the intake deducts them
            Assert.Equal(ScoutingService.PerceivedAbility(w, promo, f), m.ScoutedRead);                  // the fog, never the truth
        }
        Assert.Equal(bank, promo.Balance.Amount);   // free
        // The free tryout's mark stays as it was (saves read it); the recurring one carries the AI houses' mark.
        Assert.Equal(1, Lines(promo, PlayerFounding.TryoutLedgerLine));
        Assert.Equal(1, Lines(promo, OriginsFounding.TryoutLedgerLine));
        Assert.Equal(freeOn, PlayerFounding.TryoutHeldOn(promo));
        Assert.Equal(on, PlayerFounding.LastTryoutOn(promo));

        // Spaced again from this one.
        var again = PlayerFounding.TryoutOptions(w, on.AddDays(1));
        Assert.False(again.Available);
        Assert.Equal(on.AddDays(OriginsFounding.TryoutEveryDays), again.NextPossibleOn);

        // Past the spacing with a full house: not short of men, so no tryout, and no date to wait for.
        var later = on.AddDays(OriginsFounding.TryoutEveryDays);
        Assert.False(OriginsFounding.ShortHanded(w, promo, later, out _));
        var full = PlayerFounding.TryoutOptions(w, later);
        Assert.False(full.Available);
        Assert.Contains("short of men", full.Refusal);
        Assert.Null(full.NextPossibleOn);
        Assert.Empty(PlayerFounding.HoldTryout(w, later, out why));
        Assert.NotNull(why);
    }

    [Fact]
    public void TryoutOffer_ShortOfMen_ReadsApartFromTheSpacing()
    {
        var w = Observer(9);
        var promo = FoundOk(w, new PlayerFoundingSpec("Lantern Street Open", "LSO", FirstOpen(w).Id));
        var freeOn = w.CurrentDate;

        // The free founding tryout reflects the same test: an empty new house is short of men.
        var free = PlayerFounding.TryoutOptions(w, freeOn);
        Assert.True(free.Free);
        Assert.Equal(OriginsFounding.ShortHanded(w, promo, freeOn, out _), free.ShortOfMen);
        Assert.True(free.ShortOfMen);
        PlayerFounding.HoldTryout(w, freeOn, out var why);
        Assert.Null(why);

        // Short and blocked by the spacing: still short, not on offer, and the date he can next call one.
        var soonOn = freeOn.AddDays(7);
        Assert.True(OriginsFounding.ShortHanded(w, promo, soonOn, out _));
        var soon = PlayerFounding.TryoutOptions(w, soonOn);
        Assert.True(soon.ShortOfMen);
        Assert.False(soon.Available);
        Assert.Equal(freeOn.AddDays(OriginsFounding.TryoutEveryDays), soon.NextPossibleOn);
        Assert.True(soon.Turnout > 0);

        // Short and allowed: on offer, and still short.
        new WorldTick().AdvanceWeeks(w, 26);
        var on = w.CurrentDate;
        Assert.True(OriginsFounding.ShortHanded(w, promo, on, out _));
        var offer = PlayerFounding.TryoutOptions(w, on);
        Assert.True(offer.Available, offer.Refusal);
        Assert.True(offer.ShortOfMen);
        Assert.Null(offer.NextPossibleOn);

        // Filled to the target and past the spacing: not short.
        PlayerFounding.HoldTryout(w, on, out why);
        Assert.Null(why);
        var later = on.AddDays(OriginsFounding.TryoutEveryDays);
        Assert.False(OriginsFounding.ShortHanded(w, promo, later, out _));
        var full = PlayerFounding.TryoutOptions(w, later);
        Assert.False(full.ShortOfMen);
        Assert.False(full.Available);
        Assert.Null(full.NextPossibleOn);
        // Inside the spacing with a full house: blocked by the date, but not short either.
        var fullSoon = PlayerFounding.TryoutOptions(w, on.AddDays(1));
        Assert.Equal(OriginsFounding.ShortHanded(w, promo, on.AddDays(1), out _), fullSoon.ShortOfMen);
        Assert.False(fullSoon.ShortOfMen);
        Assert.NotNull(fullSoon.NextPossibleOn);
    }

    [Fact]
    public void TryoutOffer_ShortOfMen_FalseWithNoHouse()
    {
        var w = Observer(9);
        Assert.False(PlayerFounding.TryoutOptions(w, w.CurrentDate).ShortOfMen);
    }

    [Fact]
    public void RecurringTryout_IsNeverHeldForHim()
    {
        var w = Observer(10);
        var promo = FoundOk(w, new PlayerFoundingSpec("Lantern Street Open", "LSO", FirstOpen(w).Id));
        PlayerFounding.HoldTryout(w, w.CurrentDate, out _);
        new WorldTick().AdvanceWeeks(w, 60);   // well past the spacing, short of men the whole time
        Assert.Equal(0, Lines(promo, OriginsFounding.TryoutLedgerLine));
        Assert.True(PlayerFounding.TryoutOptions(w, w.CurrentDate).Available);
    }

    // ---- 3. the divisions he opens with ---------------------------------------------------------

    /// <summary>A world in which the sport has drawn every men's line of the ladder (rounds and gloves too).</summary>
    private static World AllLinesDrawn(ulong seed)
    {
        var w = Observer(seed);
        Era.Unlock(w, Capability.Rounds);
        Era.Unlock(w, Capability.Gloves);
        foreach (var chapter in OriginsReforms.Ladder)
            foreach (var wc in OriginsReforms.LinesOf(chapter)) Era.Unlock(w, Capability.Class(wc));
        return w;
    }

    [Fact]
    public void Choices_SuggestWhatHisPurseCanStaff_AndFoundingDefaultsToIt()
    {
        var w = AllLinesDrawn(7);
        var choices = PlayerFounding.Choices(w, w.CurrentDate);
        var drawn = choices.WeightLines;
        Assert.Equal(OriginsReforms.Ladder.Sum(c => OriginsReforms.LinesOf(c).Count), drawn.Count);
        Assert.All(choices.Cities.Where(c => !c.Open), c => Assert.Empty(c.SuggestedLines));
        var city = choices.Cities.First(c => c.Open);
        Assert.Equal(PlayerFounding.SuggestedDivisions(w, city.Id), city.SuggestedLines);
        Assert.True(OriginsReforms.LinesOf(OriginsReforms.Ladder[0]).All(city.SuggestedLines.Contains));
        Assert.True(city.SuggestedLines.Count < drawn.Count, $"suggested {city.SuggestedLines.Count} of {drawn.Count}: a new house's purse staffs a few lines, not the ladder");

        // Omitted: the suggestion, not every line.
        var promo = FoundOk(w, new PlayerFoundingSpec("Lantern Street Open", "LSO", city.Id));
        var opened = promo.Divisions.Where(d => !d.Open).Select(d => d.WeightClass).OrderBy(x => x).ToList();
        Assert.Equal(city.SuggestedLines.OrderBy(x => x), opened);
        // ...which is the AI newcomer's own rule read off the house as it stands.
        Assert.Equal(OriginsReforms.LinesItCanStaff(w, promo, drawn).OrderBy(x => x), opened);
        Assert.All(promo.Divisions, d => Assert.Null(w.Get(d.Title!.Value)!.CurrentChampion));
    }

    [Fact]
    public void Found_OpensTheLinesHePicks_AndRefusesASetItCannotTake()
    {
        var w = AllLinesDrawn(8);
        var city = FirstOpen(w).Id;
        PlayerFoundingSpec With(params WeightClass[] lines) => new("Harbour Lights Fighting", "HLF", city, Divisions: lines);
        string Refuse(PlayerFoundingSpec s)
        {
            Assert.Null(PlayerFounding.Found(w, w.CurrentDate, s, out var why));
            Assert.False(string.IsNullOrWhiteSpace(why));
            return why!;
        }

        Assert.Contains("first split", Refuse(With(WeightClass.Heavyweight, WeightClass.Lightweight)));
        Assert.Contains("first split", Refuse(With()));
        Assert.Contains("not a weight class", Refuse(With(WeightClass.Heavyweight, WeightClass.Middleweight, (WeightClass)99)));
        Assert.Contains("not a line the sport has drawn", Refuse(With(WeightClass.Heavyweight, WeightClass.Middleweight, WeightClass.Strawweight)));
        Assert.False(w.HasPlayer);

        // Every line he can name, a gap in the ladder included.
        var pick = new[] { WeightClass.Middleweight, WeightClass.Heavyweight, WeightClass.Featherweight, WeightClass.Heavyweight };
        var promo = FoundOk(w, With(pick));
        Assert.Equal(new[] { WeightClass.Featherweight, WeightClass.Middleweight, WeightClass.Heavyweight },
            promo.Divisions.Select(d => d.WeightClass).OrderBy(x => x));
        Assert.All(promo.Divisions, d => Assert.False(d.Open));
        // The bands still cover every man: the lightest takes everyone under its line, the next everyone above it.
        Assert.Equal(WeightClass.Strawweight, promo.Divisions.Single(d => d.WeightClass == WeightClass.Featherweight).Lightest);
        Assert.Equal(WeightClass.Featherweight + 1, promo.Divisions.Single(d => d.WeightClass == WeightClass.Middleweight).Lightest);
    }

    [Fact]
    public void BeforeTheFirstSplit_HePicksNoLines()
    {
        var w = Observer(11);
        var city = FirstOpen(w);
        Assert.Empty(city.SuggestedLines);
        Assert.Null(PlayerFounding.Found(w, w.CurrentDate,
            new PlayerFoundingSpec("Harbour Lights Fighting", "HLF", city.Id, Divisions: new[] { WeightClass.Heavyweight }), out var why));
        Assert.Contains("no weight classes yet", why);
        var promo = FoundOk(w, new PlayerFoundingSpec("Harbour Lights Fighting", "HLF", city.Id, Divisions: Array.Empty<WeightClass>()));
        Assert.True(Assert.Single(promo.Divisions).Open);
    }

    // ---- 4. a weight split keeps the bouts already booked ----------------------------------------

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

    private static void ReadyToSplit(World w)
    {
        w.CurrentDate = Month(60);
        Fired(w, Month(2), "crucible-first-night", "first-star");
        Fired(w, Month(34), "press-turns");
        Fired(w, Month(50), "rounds-and-judges", "gloves-adopted");
    }

    [Fact]
    public void ASplit_KeepsTheBoutsOnAnAnnouncedCard_AndNoRetiredBeltIsWon()
    {
        var w = new WorldBuilder().Build(WorldBuilder.Options.Origins(5, 1));   // the player runs SFA, a card house at open weight
        var sfa = w.Get(w.PlayerPromotion)!;
        ReadyToSplit(w);
        foreach (var e in w.Events.Values.Where(e => e.Promotion == sfa.Id).ToList()) w.Events.Remove(e.Id);
        var date = w.CurrentDate.AddDays(35);
        var men = w.RosterOf(sfa.Id)
            .Where(f => f.Sex == Sex.Male && f.IsMatchReady(date) && !Core.Sim.Contracts.DealWindow.OutOfDeal(w, f))
            .OrderBy(f => f.Id.Value).Take(4).ToList();
        Assert.Equal(4, men.Count);
        men[0].WeightClass = WeightClass.Heavyweight;   // the open-weight belt bout crosses the new line...
        men[1].WeightClass = WeightClass.Middleweight;
        men[2].WeightClass = WeightClass.Welterweight;  // ...and so does an ordinary bout
        men[3].WeightClass = WeightClass.LightHeavyweight;
        var booking = new PlayerBooking();
        var ev = booking.CreateEvent(w, date);
        Assert.True(booking.AddBout(w, ev, men[2].Id, men[3].Id, 3, false, out var err), err);
        Assert.True(booking.AddBout(w, ev, men[0].Id, men[1].Id, 3, true, out err), err);
        var openBelt = sfa.Divisions.Single(d => d.Open).Title!.Value;
        Assert.Contains(ev.Card, f => f.Title == openBelt);

        Assert.True(OriginsReforms.TryAdopt(w, sfa, OriginsReforms.Ladder[0], w.CurrentDate, out var refused), refused);

        // Both bouts stay, fought as booked (open weight); the retired belt's bout goes on without it.
        Assert.Equal(2, ev.Card.Count);
        Assert.All(ev.Card, f => Assert.True(f.OpenWeight));
        Assert.All(ev.Card, f => Assert.False(f.IsTitleFight));
        Assert.All(ev.Card, f => Assert.Null(f.Title));
        var mail = Assert.Single(w.Mail, m => m.Subject == "The new weight lines and the bouts on our cards");
        Assert.Contains(men[0].FullName, mail.Body);
        Assert.DoesNotContain(w.Mail, m => m.Subject == "The new weight lines took bouts off our cards");

        // The night runs with both bouts, and no belt is won on it.
        var reignsBefore = w.Titles.Values.Sum(t => t.Lineage.Count);
        w.CurrentDate = ev.Date;
        new WorldTick().RunEventLive(w, ev, playByPlay: false);
        Assert.Equal(EventStatus.Completed, ev.Status);
        Assert.Equal(2, ev.Card.Count(f => f.Outcome is not null));
        Assert.Equal(reignsBefore, w.Titles.Values.Sum(t => t.Lineage.Count));
        Assert.Null(w.Get(openBelt)!.CurrentChampion);

        // The new lines bind from the next night: a new bout across them is refused.
        var next = booking.CreateEvent(w, ev.Date.AddDays(40));
        Assert.False(booking.AddBout(w, next, men[0].Id, men[1].Id, 3, false, out err));
    }

    [Fact]
    public void ALaterSplit_KeepsABeltBoutOnlyWhenBothMenStayInItsDivision_AtAnAiHouse()
    {
        var w = OriginsWorldTests.AiWorld(4);
        var sfa = w.Promotions.Values.Single(p => p.Abbreviation == "SFA");
        OriginsReforms.DrawLines(w, sfa, OriginsReforms.LinesOf(OriginsReforms.Ladder[0]), w.CurrentDate);
        foreach (var e in w.Events.Values.Where(e => e.Promotion == sfa.Id && e.Status == EventStatus.Scheduled).ToList()) w.Events.Remove(e.Id);
        var heavy = sfa.Divisions.Single(d => d.WeightClass == WeightClass.Heavyweight);
        var men = w.RosterOf(sfa.Id).Where(f => f.Sex == Sex.Male).OrderBy(f => f.Id.Value).Take(4).ToList();
        Assert.Equal(4, men.Count);
        men[0].WeightClass = WeightClass.Heavyweight;
        men[1].WeightClass = WeightClass.Heavyweight;
        men[2].WeightClass = WeightClass.Heavyweight;
        men[3].WeightClass = WeightClass.LightHeavyweight;   // in the heavy band now, his own division after the next split
        var date = w.CurrentDate.AddDays(28);
        var ev = OriginsShows.ScheduleCard(w, sfa.Id, date, new[]
        {
            new Matchup(men[2].Id, men[3].Id, WeightClass.Heavyweight, 3, true, heavy.Title),
            new Matchup(men[0].Id, men[1].Id, WeightClass.Heavyweight, 3, true, heavy.Title),
        })!;
        Assert.NotNull(ev);
        Assert.Equal(2, ev.Card.Count(f => f.IsTitleFight));

        OriginsReforms.DrawLines(w, sfa, OriginsReforms.LinesOf(OriginsReforms.Ladder[1]), w.CurrentDate);

        Assert.Equal(2, ev.Card.Count);   // nothing taken off
        var kept = ev.Card.Single(f => f.RedCorner == men[0].Id);
        Assert.True(kept.IsTitleFight);
        Assert.Equal(heavy.Title, kept.Title);
        var crossed = ev.Card.Single(f => f.RedCorner == men[2].Id);
        Assert.False(crossed.IsTitleFight);
        Assert.Null(crossed.Title);
        Assert.Equal(WeightClass.Heavyweight, crossed.WeightClass);   // as booked
    }

    // ---- review follow-ups: a founding house's tryouts; the kept bout's size term ----------------

    [Fact]
    public void AFoundingHouse_HasNoFreeTryout_ButTheRecurringOneWhenShort()
    {
        var w = new WorldBuilder().Build(WorldBuilder.Options.Origins(5, 1));   // the player runs SFA from day one
        var sfa = w.Get(w.PlayerPromotion)!;
        Assert.False(PlayerFounding.FoundedByPlayer(sfa));
        int roster = w.RosterOf(sfa.Id).Count();

        // A full house: no free founding tryout, and no recurring one (not short of men); no date to wait for.
        var offer = PlayerFounding.TryoutOptions(w, w.CurrentDate);
        Assert.False(offer.Available);
        Assert.False(offer.Free);
        Assert.Null(offer.NextPossibleOn);
        Assert.Contains("free founding tryout is for a house you found yourself", offer.Refusal);
        Assert.Empty(PlayerFounding.HoldTryout(w, w.CurrentDate, out var why));
        Assert.Equal(offer.Refusal, why);
        Assert.Equal(roster, w.RosterOf(sfa.Id).Count());

        // Short of men: the AI houses' tryout is his, at once (no tryout on his ledger, so no spacing).
        foreach (var f in w.RosterOf(sfa.Id).OrderBy(f => f.Id.Value).Skip(3).ToList()) f.Status = FighterStatus.Retired;
        var date = w.CurrentDate;
        Assert.True(OriginsFounding.ShortHanded(w, sfa, date, out int before));
        var shortOffer = PlayerFounding.TryoutOptions(w, date);
        Assert.True(shortOffer.Available, shortOffer.Refusal);
        Assert.False(shortOffer.Free);
        Assert.Null(shortOffer.NextPossibleOn);
        Assert.Equal(OriginsFounding.TryoutTarget(sfa) - before, shortOffer.Turnout);
        var men = PlayerFounding.HoldTryout(w, date, out why);
        Assert.Null(why);
        Assert.NotEmpty(men);
        Assert.Equal(OriginsFounding.TryoutTarget(sfa), OriginsFounding.OnTheBooks(w, sfa));
        Assert.Equal(1, Lines(sfa, OriginsFounding.TryoutLedgerLine));
        Assert.Equal(0, Lines(sfa, PlayerFounding.TryoutLedgerLine));
        Assert.Null(PlayerFounding.TryoutHeldOn(sfa));

        // Spaced from that tryout, though no free one was ever on his ledger.
        var after = PlayerFounding.TryoutOptions(w, date.AddDays(1));
        Assert.False(after.Available);
        Assert.False(after.Free);
        Assert.Equal(date.AddDays(OriginsFounding.TryoutEveryDays), after.NextPossibleOn);
    }

    [Fact]
    public void AHouseHeFounded_IsTheOnlyKindWithTheFreeTryout()
    {
        var w = Observer(12);
        var promo = FoundOk(w, new PlayerFoundingSpec("Lantern Street Open", "LSO", FirstOpen(w).Id));
        Assert.True(PlayerFounding.FoundedByPlayer(promo));
        var offer = PlayerFounding.TryoutOptions(w, w.CurrentDate);
        Assert.True(offer.Available);
        Assert.True(offer.Free);
        Assert.Equal(PlayerFounding.TryoutTurnout, offer.Turnout);
    }

    [Fact]
    public void AKeptOpenWeightBout_IsFoughtWithTheSizeTerm_UnderTheHousesCustomRules()
    {
        var w = new WorldBuilder().Build(WorldBuilder.Options.Origins(5, 1));
        var sfa = w.Get(w.PlayerPromotion)!;
        ReadyToSplit(w);
        foreach (var e in w.Events.Values.Where(e => e.Promotion == sfa.Id).ToList()) w.Events.Remove(e.Id);
        var date = w.CurrentDate.AddDays(35);
        var men = w.RosterOf(sfa.Id)
            .Where(f => f.Sex == Sex.Male && f.IsMatchReady(date) && !Core.Sim.Contracts.DealWindow.OutOfDeal(w, f))
            .OrderBy(f => f.Id.Value).Take(2).ToList();
        men[0].WeightClass = WeightClass.Heavyweight;
        men[1].WeightClass = WeightClass.Middleweight;
        var booking = new PlayerBooking();
        var ev = booking.CreateEvent(w, date);
        Assert.True(booking.AddBout(w, ev, men[0].Id, men[1].Id, 3, false, out var err), err);
        Assert.True(OriginsReforms.TryAdopt(w, sfa, OriginsReforms.Ladder[0], w.CurrentDate, out var refused), refused);

        var bout = Assert.Single(ev.Card);
        Assert.True(bout.OpenWeight);                      // kept, as booked
        var rules = sfa.Rules.Fight!;
        Assert.False(rules.OpenWeight);                    // the house's own bouts weigh in now...
        Assert.False(rules.IsModern);                      // ...under its own (custom) rules: the engine's custom path
        // The engine's size term is rules.OpenWeight || BandedSizeTerm: on here only through the bout's own flag.
        Assert.True(Core.Sim.Booking.EventService.SizeTermFor(w, sfa, bout));
    }
}
