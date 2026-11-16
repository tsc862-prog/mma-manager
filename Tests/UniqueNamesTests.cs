using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Generation;
using MmaManager.Core.Sim.Generation.Origins;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Org;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Tests;

/// <summary>
/// One person, one name. Every real fighter ever (active or retired) has a full name nobody else carries,
/// at day one and years on, in both modes; a generated person yields to an authored one; the roll that
/// resolves a collision is deterministic and never touches an existing stream.
/// </summary>
public class UniqueNamesTests
{
    private static string Full(Fighter f) => $"{f.FirstName} {f.LastName}";

    private static IEnumerable<string> Duplicates(World w) =>
        w.Fighters.Values.Where(f => !f.LocalTalent)
            .GroupBy(Full, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => $"{g.Key} ids {string.Join(",", g.Select(f => f.Id.Value))}");

    private static World Modern(ulong seed) => new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    public void TodaysMode_NoTwoFightersShareAFullName_DayOneOrYearsOn(ulong seed)
    {
        var w = Modern(seed);
        Assert.Empty(Duplicates(w));
        new WorldTick().AdvanceWeeks(w, 156);   // three years of newgens, tryouts, discoveries
        Assert.Empty(Duplicates(w));
        Assert.True(w.Fighters.Count > 7000);   // (the intake really ran)

        // And nobody shares a name with an AUTHORED staff member or owner.
        var authored = DesignedStaff.Staff.Select(s => s.Name).Concat(DesignedStaff.Owners.Select(o => o.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(w.Fighters.Values, f => !f.LocalTalent && authored.Contains(Full(f)));
    }

    [Theory]
    [InlineData(11UL)]
    [InlineData(12UL)]
    public void Origins_NoTwoFightersShareAFullName_AndEveryPioneerKeepsHis(ulong seed)
    {
        var w = OriginsWorldTests.AiWorld(seed);
        Assert.Empty(Duplicates(w));
        new WorldTick().AdvanceWeeks(w, 156);
        Assert.Empty(Duplicates(w));

        // The pioneers keep their names: nobody generated shares one (the pioneer portrait binding is by exact name).
        foreach (var p in OriginsPioneers.All)
            Assert.Single(w.Fighters.Values, f => f.FirstName == p.Fighter.First && f.LastName == p.Fighter.Last);
    }

    [Fact]
    public void Origins_AGeneratedManYieldsToAnArrivalNotYetInTheWorld()
    {
        // The reserved set covers authored people who have not entered the world yet (arrivals' fighters).
        var w = OriginsWorldTests.AiWorld(11);
        var arrival = OriginsArrivals.All.First(a => a.Pioneer is not null).Pioneer!.Fighter;
        var gen = FighterGenerator.ForWorld(w);
        var f = gen.Generate(w.NewFighterId(), w.CurrentDate, new GenOptions { Sex = Sex.Male });
        f.FirstName = arrival.First; f.LastName = arrival.Last;
        gen.EnsureUniqueName(w, f);
        Assert.NotEqual($"{arrival.First} {arrival.Last}", Full(f));
    }

    [Fact]
    public void ALegendsNameIsReserved_AGeneratedFighterNeverTakesIt()
    {
        var w = Modern(1);
        var legend = DesignedLegends.All[0];
        var gen = FighterGenerator.ForWorld(w);
        var f = gen.Generate(w.NewFighterId(), w.CurrentDate, new GenOptions());
        f.FirstName = legend.First; f.LastName = legend.Last;
        gen.EnsureUniqueName(w, f);
        Assert.NotEqual($"{legend.First} {legend.Last}", Full(f));
    }

    [Fact]
    public void SameSeed_GivesTheSameNames()
    {
        var a = Modern(5); var b = Modern(5);
        new WorldTick().AdvanceWeeks(a, 30); new WorldTick().AdvanceWeeks(b, 30);
        Assert.Equal(
            a.Fighters.OrderBy(kv => kv.Key.Value).Select(kv => $"{kv.Key.Value}:{Full(kv.Value)}|{kv.Value.Nickname}"),
            b.Fighters.OrderBy(kv => kv.Key.Value).Select(kv => $"{kv.Key.Value}:{Full(kv.Value)}|{kv.Value.Nickname}"));
    }

    /// <summary>The ladder as it stood before: 16 tries off the keyed stream, last name first, the first name too from the ninth.</summary>
    private static (string First, string Last, bool Resolved) OldLadder(World w, Fighter f, ulong seed)
    {
        var taken = new HashSet<string>(
            w.Fighters.Values.Where(o => o.Id != f.Id && !o.LocalTalent).Select(o => $"{o.FirstName}|{o.LastName}"),
            StringComparer.OrdinalIgnoreCase);
        string first = f.FirstName, last = f.LastName;
        var culture = f.OriginCountryCode is { } code ? MmaManager.Core.Sim.Geography.WorldAtlas.ByCode(code)?.Culture : null;
        var rng = new SimRandom(SimRandom.Mix(seed, (ulong)f.Id.Value ^ 0x4E11_D3D0UL));
        var (firsts, lasts) = culture is not null ? NameData.Pool(culture, f.Nationality, f.Sex) : NameData.Pool(f.Nationality);
        for (int attempt = 0; attempt < 16 && taken.Contains($"{first}|{last}"); attempt++)
        {
            last = rng.Pick(lasts);
            if (attempt >= 8) first = rng.Pick(firsts);
        }
        return (first, last, !taken.Contains($"{first}|{last}"));
    }

    [Fact]
    public void ACollisionTheOldLadderResolved_ResolvesTheSameWay_AndNobodyElseMoves()
    {
        var w = Modern(1);
        var before = w.Fighters.ToDictionary(kv => kv.Key.Value, kv => Full(kv.Value));
        var gen = FighterGenerator.ForWorld(w);
        var victims = w.Fighters.Values.Where(f => !f.LocalTalent).Take(150).ToList();
        int same = 0;
        foreach (var v in victims)
        {
            var f = gen.Generate(w.NewFighterId(), w.CurrentDate, new GenOptions());
            f.FirstName = v.FirstName; f.LastName = v.LastName;
            var old = OldLadder(w, f, w.RngSeed);
            gen.EnsureUniqueName(w, f);
            Assert.NotEqual(Full(v).ToLowerInvariant(), Full(f).ToLowerInvariant());
            if (old.Resolved) { Assert.Equal((old.First, old.Last), (f.FirstName, f.LastName)); same++; }
            w.Fighters[f.Id] = f;   // and the next collision sees this one
        }
        Assert.True(same > 100);
        foreach (var kv in before) Assert.Equal(kv.Value, Full(w.Fighters[new FighterId(kv.Key)]));   // nobody before moved
        Assert.Empty(Duplicates(w));
    }

    [Fact]
    public void AFullPool_StillResolves_ByDoubleBarrellingThenAGenerationSuffix()
    {
        var w = Modern(1);
        var gen = FighterGenerator.ForWorld(w);
        Fighter Mint()
        {
            var m = gen.Generate(w.NewFighterId(), w.CurrentDate, new GenOptions { Nationality = Region.Oceania, Sex = Sex.Male });
            m.OriginCountryCode = null;   // the regional pool
            return m;
        }
        var (firsts, lasts) = NameData.Pool(Region.Oceania);
        // Every combination in the pool is taken, and every double-barrelled one, so even that stage falls through to the suffix.
        foreach (var fn in firsts)
            foreach (var a in lasts)
                foreach (var b in lasts)
                {
                    var filler = Mint();
                    filler.FirstName = fn; filler.LastName = a == b ? a : $"{a}-{b}";
                    w.Fighters[filler.Id] = filler;
                }
        var f = Mint();
        f.FirstName = firsts[0]; f.LastName = lasts[0];
        gen.EnsureUniqueName(w, f);
        w.Fighters[f.Id] = f;
        Assert.Single(w.Fighters.Values, o => Full(o).Equals(Full(f), StringComparison.OrdinalIgnoreCase));   // (the fillers repeat among themselves; he does not)
        Assert.Matches(@" (II|III|IV|V|VI|VII|VIII|IX|X|\d+)$", f.LastName);
    }

    [Fact]
    public void AFamousNamesJunior_KeepsTheSurname_AndGetsAFirstNameNobodyHas()
    {
        var w = Modern(1);
        var gen = FighterGenerator.ForWorld(w);
        var elder = w.Fighters.Values.First(f => f.Designed && f.LastName == "Volkov");
        for (int i = 0; i < 40; i++)
        {
            var f = gen.Generate(w.NewFighterId(), w.CurrentDate, new GenOptions { Nationality = elder.Nationality, Sex = elder.Sex });
            f.LastName = elder.LastName;
            gen.EnsureUniqueName(w, f, keepSurname: true);
            Assert.Equal(elder.LastName, f.LastName);
            w.Fighters[f.Id] = f;
        }
        Assert.Empty(Duplicates(w));
    }
}
