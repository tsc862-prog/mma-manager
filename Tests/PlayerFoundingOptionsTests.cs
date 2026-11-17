using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Generation.Origins;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Scouting;

namespace MmaManager.Tests;

/// <summary>
/// The player's founding options in an Origins world: recurring open tryouts on the AI houses' rule (an option he
/// calls).
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
}
