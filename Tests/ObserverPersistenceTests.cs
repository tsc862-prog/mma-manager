using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Living;
using MmaManager.Persistence;

namespace MmaManager.Tests;

/// <summary>
/// Origins worlds with no player house (the observer start), a house founded mid-game with its free tryout,
/// and an AI house's open tryout: each must survive save and load, and a loaded world must keep behaving
/// like the one that was never saved.
/// </summary>
public class ObserverPersistenceTests
{
    private static World RoundTrip(World w)
    {
        string path = Path.Combine(Path.GetTempPath(), $"mma_obs_{Guid.NewGuid():N}.db");
        try { SaveGame.Save(w, path); return SaveGame.Load(path); }
        finally { File.Delete(path); }
    }

    private static void AssertSame(World expected, World actual, string what)
    {
        var diffs = PersistenceGraph.Diff(expected, actual);
        Assert.True(diffs.Count == 0, what + ":\n" + PersistenceGraph.Describe(diffs));
    }

    /// <summary>Advance both worlds the same way and check they are still one world.</summary>
    private static void SoakBoth(World never, World loaded, int weeks)
    {
        new WorldTick().AdvanceWeeks(never, weeks);
        new WorldTick().AdvanceWeeks(loaded, weeks);
        AssertSame(never, loaded, $"Loaded world drifted from the never-saved one after {weeks} more weeks");
    }

    private static Promotion FoundHouse(World w)
    {
        for (int i = 0; i < 30; i++)
        {
            var city = PlayerFounding.Choices(w, w.CurrentDate).Cities.First(c => c.Open);
            var spec = new PlayerFoundingSpec("Lantern Street Open", "LSO", city.Id, null, null, null);
            var promo = PlayerFounding.Found(w, w.CurrentDate, spec, out _);
            if (promo is not null) return promo;
            new WorldTick().AdvanceWeeks(w, 1);
        }
        throw new Xunit.Sdk.XunitException("the observer could not found a house in 30 weeks");
    }

    [Fact]
    public void ObserverWorld_NoPlayerHouse_RoundTripsAndKeepsBehaving()
    {
        var never = new WorldBuilder().Build(WorldBuilder.Options.OriginsObserver(4));
        new WorldTick().AdvanceWeeks(never, 104);
        Assert.Equal(default, never.PlayerPromotion);

        var loaded = RoundTrip(never);
        Assert.Equal(default, loaded.PlayerPromotion);
        AssertSame(never, loaded, "Observer world changed across the save");
        Assert.True(PlayerFounding.Choices(loaded, loaded.CurrentDate).CanFound == PlayerFounding.Choices(never, never.CurrentDate).CanFound);

        SoakBoth(never, loaded, 40);
    }

    [Fact]
    public void FoundedHouse_AndItsFreeTryout_RoundTrip()
    {
        var never = new WorldBuilder().Build(WorldBuilder.Options.OriginsObserver(4));
        new WorldTick().AdvanceWeeks(never, 104);
        var promo = FoundHouse(never);
        var men = PlayerFounding.HoldTryout(never, never.CurrentDate, out var why);
        Assert.True(men.Count > 0, why);

        var loaded = RoundTrip(never);
        AssertSame(never, loaded, "Founded world changed across the save");

        var lp = loaded.Get(loaded.PlayerPromotion)!;
        Assert.Equal(promo.Id, lp.Id);
        Assert.True(lp.IsPlayerControlled);
        Assert.Equal(never.CurrentDate, PlayerFounding.TryoutHeldOn(lp));
        Assert.Contains(lp.Ledger, e => e.Description == PlayerFounding.TryoutLedgerLine && e.Amount.Amount == 0m);
        foreach (var m in men)
        {
            var f = loaded.Get(m.Fighter)!;
            Assert.Contains(f.Accolades, a => a.Title == $"Signed at {lp.Name}'s open tryout");
        }
        var a = PlayerFounding.TryoutOptions(never, never.CurrentDate);
        var b = PlayerFounding.TryoutOptions(loaded, loaded.CurrentDate);
        Assert.Equal(a, b);
        Assert.False(b.Available);

        SoakBoth(never, loaded, 40);
    }

    [Fact]
    public void AiHouseTryout_LedgerLineAndAccolades_SurviveAndFeedTheSameIntakeDeduction()
    {
        var never = OriginsWorldTests.AiWorld(11);
        var tick = new WorldTick();
        // Force one: the Southern house's men are all hurt for months, so none could fight its next night.
        var sfa = never.Promotions.Values.Single(p => p.Abbreviation == "SFA");
        foreach (var f in never.RosterOf(sfa.Id))
        {
            f.Status = FighterStatus.Injured;
            f.ReadyToFightOn = never.CurrentDate.AddDays(150);
        }
        OriginsFounding.RunTryouts(never, never.CurrentDate);
        Assert.Contains(sfa.Ledger, e => e.Description == OriginsFounding.TryoutLedgerLine);
        Assert.Contains(never.Fighters.Values, f => f.Accolades.Any(a => a.Title == $"Signed at {sfa.Name}'s open tryout"));

        var loaded = RoundTrip(never);
        AssertSame(never, loaded, "Tryout world changed across the save");
        var houses = never.Promotions.Values.Where(p => p.Ledger.Any(e => e.Description == OriginsFounding.TryoutLedgerLine)).ToList();
        foreach (var h in houses)
            Assert.Contains(loaded.Get(h.Id)!.Ledger, e => e.Description == OriginsFounding.TryoutLedgerLine && e.Amount.Amount == 0m);
        Assert.Equal(
            never.Fighters.Values.Count(f => f.Accolades.Any(a => a.Title.EndsWith("open tryout"))),
            loaded.Fighters.Values.Count(f => f.Accolades.Any(a => a.Title.EndsWith("open tryout"))));

        // The deduction at each coming month tick matches while both advance week by week.
        var tl = new WorldTick();
        int seen = 0;
        for (int k = 0; k < 52; k++)
        {
            tick.AdvanceWeek(never);
            tl.AdvanceWeek(loaded);
            int d = OriginsTalent.TryoutDeduction(never, never.CurrentDate);
            Assert.Equal(d, OriginsTalent.TryoutDeduction(loaded, loaded.CurrentDate));
            seen += d;
        }
        Assert.True(seen > 0, "the deduction never fired, so the comparison proved nothing");
        AssertSame(never, loaded, "Loaded tryout world drifted after a further year");
    }
}
