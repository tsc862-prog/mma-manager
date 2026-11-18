using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Generation;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Tests;

/// <summary>
/// The persona (the marketable LOOK of a fighter's portrait) is stamped by the sim, once, when the fighter is
/// created, from the art catalog the world was given - never by the UI. Headless worlds (no catalog) carry no
/// personas at all and run exactly as they did before the stamp moved into the sim.
/// </summary>
public class PersonaStampTests
{
    // Pinned on main (bb544d9) BEFORE the stamp moved into the sim: a headless world must not move by a bit.
    // (Re-pinned when the story arcs and owner voices began posting news. News.Count is the LAST thing the digest mixes. Measured at
    // week 12 with the two hooks on and off: Modern news 942 vs 851 (+91), Origins 30 vs 25 (+5); events (3936, 5) and the card totals
    // (26127, 26) are identical. The old pins plus those deltas are the new pins: ...D3FD + 91 = ...D458, ...66A5 + 5 = ...66AA.)
    // (Re-pinned again when houses began deciding on their scouted read instead of true ability (world/matchmaker-fog): today's
    // world books, pads and signs differently from week one, so the Modern pin moves whole. The Origins world is identical but for
    // News.Count, the last thing mixed: +4 signings/retirements the press now reads as names (...66AA + 4 = ...66AE).)
    // (Re-pinned when houses and rooms began deciding on their scouted read of a man's CEILING instead of his hidden potential
    // (world/perceived-potential): the front office's promo push and camp stipend, who a house keeps on a leash, and a head coach's
    // protégé all move in today's world inside twelve weeks, so the Modern pin moves whole. The Origins world is identical.)
    // (Re-pinned when the Origins house tier reached every price path (world/origins-tier-reach): the Origins pin moves through the
    // AI's opening offer on the 250 step alone (men are offered, and sign for, the finer step); every other reached path
    // leaves it, and the Modern pin never moves.)
    private const string ModernNoCatalog12Weeks = "A40EC998243A55B2";
    private const string OriginsNoCatalog12Weeks = "5AF296CA832E8562";

    // Who exists, how famous, how they did, where they are, plus every house's bank and the size of the record.
    private static string Digest(World w)
    {
        unchecked
        {
            long h = 17;
            void Mix(long v) => h = h * 31 + v;
            void MixStr(string? s) { if (s is null) { Mix(-1); return; } foreach (char c in s) Mix(c); Mix(s.Length); }
            Mix(w.CurrentDate.DayNumber);
            foreach (var f in w.Fighters.Values.OrderBy(f => f.Id.Value))
            {
                Mix(f.Id.Value); MixStr(f.PersonaTag); Mix(f.Intangibles.Popularity.Value);
                Mix(f.Record.Wins); Mix(f.Record.Losses); Mix((long)f.Status); Mix(f.Loyalty);
                if (f.Contract is { } c) { Mix(c.Promotion.Value); Mix(c.FightsRemaining); Mix((long)c.PursePerFight.Amount); }
            }
            foreach (var p in w.Promotions.Values.OrderBy(p => p.Id.Value)) { Mix((long)p.Balance.Amount); Mix(p.Defunct ? 1 : 0); }
            Mix(w.Events.Count); Mix(w.Events.Values.Sum(e => e.Card.Count)); Mix(w.News.Count);
            return h.ToString("X");
        }
    }

    private static World Modern(ulong seed)
    {
        var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });
        w.PlayerPromotion = default;   // nobody to wait for
        return w;
    }

    private static World Origins(ulong seed) => new WorldBuilder().Build(WorldBuilder.Options.OriginsObserver(seed));

    // A stand-in for the art pack: no identities (so generation is untouched: Pick finds nothing and the
    // generator rolls exactly as headless), and a look that is a pure function of the fighter and his world.
    private static readonly string?[] Looks = { "babyface", "adonis", null, "menace", "glam" };
    private static string? Look(Fighter f, World w) =>
        w.Countries.ContainsKey(f.Country) ? Looks[f.Id.Value % Looks.Length] : null;
    private static IdentityCatalog Stub() => new(Array.Empty<IdentitySpec>()) { LookOf = Look };

    private static World ModernWithArt(ulong seed)
    {
        var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed, IdentityCatalog = Stub() });
        w.PlayerPromotion = default;
        return w;
    }

    private static World OriginsWithArt(ulong seed) =>
        new WorldBuilder().Build(WorldBuilder.Options.OriginsObserver(seed) with { IdentityCatalog = Stub() });

    private static void AssertEveryoneWearsHisLook(World w)
    {
        foreach (var f in w.Fighters.Values)
            Assert.True(f.PersonaTag == Look(f, w), $"#{f.Id.Value} {f.FullName}: '{f.PersonaTag}' vs '{Look(f, w)}'");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WithACatalog_DayOneAndMidAdvanceArrivals_AreStampedByTheSim(bool origins)
    {
        var w = origins ? OriginsWithArt(11) : ModernWithArt(11);
        Assert.Contains(w.Fighters.Values, f => f.PersonaTag is not null);
        AssertEveryoneWearsHisLook(w);
        int dayOneTop = w.Fighters.Keys.Max(id => id.Value);

        new WorldTick().AdvanceWeeks(w, 12);   // nothing outside the sim touches the world in between
        var arrivals = w.Fighters.Values.Where(f => f.Id.Value > dayOneTop).ToList();
        Assert.NotEmpty(arrivals);
        Assert.Contains(arrivals, f => f.PersonaTag is not null);
        AssertEveryoneWearsHisLook(w);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WithACatalog_TwelveWeeksInOneCall_EqualsTwelveSingleWeeks(bool origins)
    {
        var once = origins ? OriginsWithArt(13) : ModernWithArt(13);
        var weekly = origins ? OriginsWithArt(13) : ModernWithArt(13);
        new WorldTick().AdvanceWeeks(once, 12);
        var tick = new WorldTick();
        for (int i = 0; i < 12; i++) tick.AdvanceWeeks(weekly, 1);

        Assert.Equal(Digest(once), Digest(weekly));
        var diffs = PersistenceGraph.Diff(once, weekly);
        Assert.True(diffs.Count == 0, "One call and twelve single weeks ran apart:\n" + PersistenceGraph.Describe(diffs));
    }

    [Fact]
    public void ACatalogWithNoLooks_MovesNothing()
    {
        // The catalog's presence alone (no identities, no look) is the headless world, bit for bit.
        var w = new WorldBuilder().Build(new WorldBuilder.Options
            { Seed = 11, IdentityCatalog = new IdentityCatalog(Array.Empty<IdentitySpec>()) });
        w.PlayerPromotion = default;
        new WorldTick().AdvanceWeeks(w, 12);
        Assert.Equal(ModernNoCatalog12Weeks, Digest(w));
    }

    [Fact]
    public void Headless_NoCatalog_NoPersonas_AndTheWorldIsUnmoved()
    {
        var modern = Modern(11);
        new WorldTick().AdvanceWeeks(modern, 12);
        Assert.All(modern.Fighters.Values, f => Assert.Null(f.PersonaTag));

        var origins = Origins(11);
        new WorldTick().AdvanceWeeks(origins, 12);
        Assert.All(origins.Fighters.Values, f => Assert.Null(f.PersonaTag));

        Assert.Equal((ModernNoCatalog12Weeks, OriginsNoCatalog12Weeks), (Digest(modern), Digest(origins)));
    }
}
