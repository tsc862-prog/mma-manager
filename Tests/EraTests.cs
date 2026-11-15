using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Finance;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Rng;
using MmaManager.Persistence;

namespace MmaManager.Tests;

/// <summary>
/// Origins phase 0: the world mode, the saved era state and the capability gate.
/// Today's mode must answer yes to everything without touching an RNG stream; an
/// Origins world with nothing unlocked must hold every gated system back quietly.
/// </summary>
public class EraTests
{
    private static World Build(WorldMode mode, ulong seed = 7, DateOnly? start = null) =>
        new WorldBuilder().Build(new WorldBuilder.Options
        {
            Seed = seed, Mode = mode, StartDate = start ?? new WorldBuilder.Options().StartDate,
        });

    /// <summary>Every capability the enum can express, keyed kinds with a sample key.</summary>
    private static IEnumerable<Capability> EveryCapability() =>
        Enum.GetValues<CapabilityKind>().SelectMany(k => k switch
        {
            CapabilityKind.WeightClass => Enum.GetValues<WeightClass>().Select(Capability.Class),
            CapabilityKind.CountryScene => new[] { Capability.Scene("BRA"), Capability.Scene("jpn") },
            _ => new[] { new Capability(k) },
        });

    /// <summary>Did <paramref name="act"/> draw from the stream? (Compares the next value against an untouched twin.)</summary>
    private static bool Draws(Action<SimRandom> act, ulong seed = 0xE7A)
    {
        var used = new SimRandom(seed);
        var twin = new SimRandom(seed);
        act(used);
        return used.NextULong() != twin.NextULong();
    }

    // ---- today's mode ------------------------------------------------------

    [Fact]
    public void ModernMode_AllowsEverything()
    {
        var world = new World();   // Modern is the default; nothing unlocked, nothing needed
        Assert.Equal(WorldMode.Modern, world.Mode);
        foreach (var cap in EveryCapability())
            Assert.True(Era.Allows(world, cap), cap.Id);
        foreach (var kind in Enum.GetValues<MediaKind>())
            Assert.True(Era.AllowsMedia(world, kind), kind.ToString());
        foreach (var wc in Enum.GetValues<WeightClass>())
        {
            Assert.True(Era.AllowsDivision(world, wc, Sex.Male));
            Assert.True(Era.AllowsDivision(world, wc, Sex.Female));
        }
        Assert.True(Era.AllowsScene(world, "USA"));
    }

    [Fact]
    public void ModernWorld_StillHasItsWomenMediaAndSponsors()
    {
        var world = Build(WorldMode.Modern);
        Assert.Empty(world.Era.Chapters);
        Assert.Empty(world.Era.Unlocked);
        Assert.Contains(world.Promotions.Values, p => p.Divisions.Any(d => d.Sex == Sex.Female));
        Assert.Contains(world.Promotions.Values, p => p.MediaDeals.Count > 0);
        Assert.Contains(world.Promotions.Values, p => p.Stream is not null);
        Assert.Contains(world.Promotions.Values, p => p.SponsorDeals.Count > 0);
    }

    [Fact]
    public void CapabilityIds_RoundTripThroughParse()
    {
        foreach (var cap in EveryCapability())
        {
            Assert.True(Capability.TryParse(cap.Id, out var back), cap.Id);
            Assert.Equal(cap.Id, back.Id);
        }
        Assert.Equal("WeightClass:Lightweight", Capability.Class(WeightClass.Lightweight).Id);
        Assert.Equal("CountryScene:BRA", Capability.Scene("bra").Id);
        Assert.Equal(Capability.InHouseStream, Capability.Media(MediaKind.InHouseStream));
        Assert.False(Capability.TryParse("NoSuchThing", out _));
    }

    // ---- an origins world with nothing unlocked --------------------------------

    [Fact]
    public void OriginsWorld_NothingUnlocked_BlocksEveryGatedSystemQuietly()
    {
        var world = Build(WorldMode.Origins);
        Assert.Equal(WorldMode.Origins, world.Mode);
        foreach (var cap in EveryCapability())
            Assert.False(Era.Allows(world, cap), cap.Id);

        // Built without screens, brands or women's divisions.
        Assert.All(world.Promotions.Values, p =>
        {
            Assert.Empty(p.MediaDeals);
            Assert.Null(p.Stream);
            Assert.Empty(p.SponsorDeals);
            Assert.DoesNotContain(p.Divisions, d => d.Sex == Sex.Female);
        });

        var top = world.Promotions.Values.OrderByDescending(p => p.Prestige.Value).ThenBy(p => p.Id.Value).First();
        top.Balance = new Money(50_000_000m);
        var date = world.CurrentDate;

        // Media: no network calls, no platform of its own (by the AI or by hand).
        var rng = new SimRandom(11);
        for (int m = 0; m < 24; m++)
        {
            MediaService.RunMonthly(world, top, date.AddMonths(m), rng);
            Sponsorships.RunMonthly(world, top, date.AddMonths(m), rng);
        }
        Assert.DoesNotContain(top.Offers, o => o.Media is not null);
        Assert.Empty(top.MediaDeals);
        Assert.Null(top.Stream);
        Assert.False(MediaService.LaunchStream(world, top, date));
        Assert.Null(top.Stream);
        Assert.Equal(50_000_000m, top.Balance.Amount);   // nothing charged
        Assert.Empty(top.SponsorDeals);
        Assert.DoesNotContain(top.Offers, o => o.Media is null);
        Assert.All(MediaService.LadderView(world, top), r => Assert.False(r.Qualified));

        // Startups and crossovers: on their scheduled months, nothing happens and nothing is drawn.
        int promotions = world.Promotions.Count, fighters = world.Fighters.Count;
        Assert.False(Draws(r => WorldEvents.RunMonthly(world, world.StartDate.AddMonths(6), r)));    // the crossover month
        Assert.False(Draws(r => WorldEvents.RunMonthly(world, world.StartDate.AddMonths(14), r)));   // the startup month
        Assert.Equal(promotions, world.Promotions.Count);
        Assert.Equal(fighters, world.Fighters.Count);

        // Superfights and acquisitions: blocked before their first draw.
        Assert.False(Draws(r => Storylines.MaybeSuperfight(world, date, r)));
        Assert.False(Draws(r => Acquisitions.RunMonthly(world, date, r)));
        Assert.All(world.Promotions.Values, p => Assert.False(Acquisitions.TryRescue(world, p, date)));

        // Belts: no division the sport hasn't split out, and no women's belt before women's MMA.
        int titles = world.Titles.Count;
        int divisions = top.Divisions.Count;
        Assert.Null(PromotionSetup.CommissionTitle(world, top, WeightClass.Bantamweight, Sex.Female, date));
        Assert.Null(PromotionSetup.CommissionTitle(world, top, WeightClass.Lightweight, Sex.Male, date));
        Assert.Equal(titles, world.Titles.Count);
        Assert.Equal(divisions, top.Divisions.Count);
        Assert.Equal(50_000_000m, top.Balance.Amount);
    }

    [Fact]
    public void OriginsWorld_UnlockingOpensExactlyThatCapability()
    {
        var world = new World { Mode = WorldMode.Origins };
        Assert.True(Era.Unlock(world, Capability.Class(WeightClass.Lightweight)));
        Assert.False(Era.Unlock(world, Capability.Class(WeightClass.Lightweight)));   // once

        Assert.True(Era.AllowsDivision(world, WeightClass.Lightweight, Sex.Male));
        Assert.False(Era.AllowsDivision(world, WeightClass.Lightweight, Sex.Female));   // women's MMA hasn't arrived
        Assert.False(Era.AllowsDivision(world, WeightClass.Welterweight, Sex.Male));

        Era.Unlock(world, Capability.WomensDivisions);
        Assert.True(Era.AllowsDivision(world, WeightClass.Lightweight, Sex.Female));

        Era.Unlock(world, Capability.Scene("bra"));
        Assert.True(Era.AllowsScene(world, "BRA"));
        Assert.False(Era.AllowsScene(world, "JPN"));

        Era.Unlock(world, Capability.CableSports);
        Assert.True(Era.AllowsMedia(world, MediaKind.CableSports));
        Assert.False(Era.AllowsMedia(world, MediaKind.PrimeBroadcast));

        var on = new DateOnly(1995, 3, 1);
        Assert.True(Era.Fire(world, "rounds-and-judges", on, by: new PromotionId(2), note: "first"));
        Assert.False(Era.Fire(world, "rounds-and-judges", on.AddDays(9)));   // a chapter fires once
        Assert.Equal(on, world.Era.Chapter("rounds-and-judges")!.FiredOn);
        Assert.Equal(new PromotionId(2), world.Era.Chapter("rounds-and-judges")!.Promotion);
    }

    [Fact]
    public void OriginsWorld_CommissionsABeltOnceTheClassIsSplitOut()
    {
        var world = Build(WorldMode.Origins);
        var promo = world.Promotions.Values.OrderBy(p => p.Id.Value).First();
        promo.Balance = new Money(1_000_000m);
        // An Origins build runs no women's divisions, so this belt can only come from the commission.
        Assert.Null(PromotionSetup.CommissionTitle(world, promo, WeightClass.Strawweight, Sex.Female, world.CurrentDate));
        Era.Unlock(world, Capability.Class(WeightClass.Strawweight));
        Assert.Null(PromotionSetup.CommissionTitle(world, promo, WeightClass.Strawweight, Sex.Female, world.CurrentDate));
        Era.Unlock(world, Capability.WomensDivisions);
        var title = PromotionSetup.CommissionTitle(world, promo, WeightClass.Strawweight, Sex.Female, world.CurrentDate);
        Assert.NotNull(title);
        Assert.Contains(promo.Divisions, d => d.WeightClass == WeightClass.Strawweight && d.Sex == Sex.Female && d.Title == title!.Id);
    }

    // ---- the schedule runs from the start date ---------------------------------

    /// <summary>The months (since the start) on which the scheduled beats drew from the stream.</summary>
    private static List<int> BeatMonths(World world, int months)
    {
        var hit = new List<int>();
        for (int m = 0; m <= months; m++)
        {
            var date = world.StartDate.AddMonths(m);
            if (Draws(r => WorldEvents.RunMonthly(world, date, r), seed: (ulong)(1000 + m))) hit.Add(m);
        }
        return hit;
    }

    [Fact]
    public void A1993Start_GetsItsScheduledBeatsAtTheSameOffsets()
    {
        var modern2026 = Build(WorldMode.Modern, seed: 3);
        var modern1993 = Build(WorldMode.Modern, seed: 3, start: new DateOnly(1993, 1, 1));
        Assert.Equal(new DateOnly(1993, 1, 1), modern1993.StartDate);

        Assert.Equal(0, WorldEvents.Epoch(modern2026, new DateOnly(2026, 1, 1)));
        Assert.Equal(0, WorldEvents.Epoch(modern1993, new DateOnly(1993, 1, 31)));
        Assert.Equal(14, WorldEvents.Epoch(modern1993, new DateOnly(1994, 3, 1)));
        // A world with no start date of its own keeps the old January 2026 anchor.
        Assert.Equal(6, WorldEvents.Epoch(new World(), new DateOnly(2026, 7, 1)));

        var beats2026 = BeatMonths(modern2026, 26);
        var beats1993 = BeatMonths(modern1993, 26);
        Assert.Equal(beats2026, beats1993);
        Assert.Contains(6, beats1993);    // the first crossover, six months in
        Assert.Contains(14, beats1993);   // the first startup window
        Assert.Contains(24, beats1993);   // and the crossover after it, 18 months on

        // The first crossover lands in mid-1993 in the 1993 world, as it lands in mid-2026 in today's.
        static Fighter? Crossover(World w) => w.Fighters.Values.FirstOrDefault(f => f.FirstName == "Bruno" && f.LastName == "Castellano");
        Assert.NotNull(Crossover(modern1993));
        Assert.Equal(new DateOnly(1993, 7, 1), Crossover(modern1993)!.Accolades[0].Date);
        Assert.Equal(new DateOnly(2026, 7, 1), Crossover(modern2026)!.Accolades[0].Date);
    }

    // ---- determinism and the save ------------------------------------------------

    [Theory]
    [InlineData(WorldMode.Modern)]
    [InlineData(WorldMode.Origins)]
    public void SameSeedTwice_GivesTheSameWorld(WorldMode mode)
    {
        var a = Build(mode, seed: 21);
        var b = Build(mode, seed: 21);
        new WorldTick().AdvanceWeeks(a, 10);
        new WorldTick().AdvanceWeeks(b, 10);
        var diffs = PersistenceGraph.Diff(a, b);
        Assert.True(diffs.Count == 0, "Same seed, different worlds:\n" + PersistenceGraph.Describe(diffs));
    }

    [Fact]
    public void ModeAndEraState_SurviveTheSave()
    {
        var world = Build(WorldMode.Origins, seed: 4);
        Era.Unlock(world, Capability.Class(WeightClass.Heavyweight));
        Era.Unlock(world, Capability.Scene("USA"));
        Era.Unlock(world, Capability.PpvCarrier);
        Era.Fire(world, "the-spectacle", world.StartDate, by: world.Promotions.Keys.First());
        Era.Fire(world, "first-star", world.StartDate.AddDays(40), fighter: world.Fighters.Keys.First(), note: "a note");

        string path = Path.Combine(Path.GetTempPath(), $"mma_era_{Guid.NewGuid():N}.db");
        try
        {
            SaveGame.Save(world, path);
            var loaded = SaveGame.Load(path);
            Assert.Equal(WorldMode.Origins, loaded.Mode);
            Assert.Equal(world.Era.Unlocked, loaded.Era.Unlocked);
            Assert.Equal(world.Era.Chapters.Select(c => (c.Key, c.FiredOn, c.Promotion, c.Fighter, c.Note)),
                         loaded.Era.Chapters.Select(c => (c.Key, c.FiredOn, c.Promotion, c.Fighter, c.Note)));
            Assert.True(Era.AllowsDivision(loaded, WeightClass.Heavyweight, Sex.Male));
            Assert.False(Era.AllowsDivision(loaded, WeightClass.Lightweight, Sex.Male));
            var diffs = PersistenceGraph.Diff(world, loaded);
            Assert.True(diffs.Count == 0, "World changed across the save:\n" + PersistenceGraph.Describe(diffs));
        }
        finally { File.Delete(path); }
    }
}
