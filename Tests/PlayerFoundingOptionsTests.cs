using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Generation.Origins;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Scouting;

namespace MmaManager.Tests;

/// <summary>
/// The player's founding options in an Origins world: recurring open tryouts on the AI houses' rule (an option he
/// calls), and the choice of divisions at founding (suggested: what his purse can staff).
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
}
