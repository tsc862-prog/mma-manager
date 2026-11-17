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

    /// <summary>Generated people who wear a last-resort name: a hyphenated or numbered surname (never wanted, never expected).</summary>
    private static IEnumerable<string> Fallbacks(World w)
    {
        var authoredSurnames = w.Fighters.Values.Where(d => d.Designed).Select(d => d.LastName).ToHashSet();   // (a famous name's junior shares one, hyphen or not)
        return w.Fighters.Values
            .Where(f => !f.Designed && !f.LocalTalent && !authoredSurnames.Contains(f.LastName)
                && (f.LastName.Contains('-') || System.Text.RegularExpressions.Regex.IsMatch(f.LastName, @" (II|III|IV|V|VI|VII|VIII|IX|X|\d+)$")))
            .Select(f => $"{f.Id.Value} {Full(f)}");
    }

    private static World Modern(ulong seed) => new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    public void TodaysMode_NoTwoFightersShareAFullName_DayOneOrYearsOn(ulong seed)
    {
        var w = Modern(seed);
        Assert.Empty(Duplicates(w));
        Assert.Empty(Fallbacks(w));              // day one: the reserve settles everyone, no hyphens or numbers
        new WorldTick().AdvanceWeeks(w, 156);   // three years of newgens, tryouts, discoveries
        Assert.Empty(Duplicates(w));
        Assert.Empty(Fallbacks(w));              // and after three years ("Team {LastName}" camps stay clean)
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

    // ---- the reserve ---------------------------------------------------------------

    /// <summary>People projected per culture at year 25 of a career (male, female): day one plus 25/3 of the growth measured over three years, mean of seeds 1 and 2, retired included.</summary>
    private static readonly Dictionary<string, (int Male, int Female)> Year25 = new(StringComparer.Ordinal)
    {
        ["anglo"] = (2649, 199),
        ["arabic"] = (867, 54),
        ["armenian"] = (47, 3),
        ["balkan"] = (169, 15),
        ["baltic"] = (68, 7),
        ["brazilian"] = (1377, 79),
        ["celtic"] = (113, 5),
        ["centralafrican"] = (106, 5),
        ["centralasian"] = (420, 31),
        ["chinese"] = (224, 20),
        ["czech"] = (55, 17),
        ["dutch"] = (134, 6),
        ["eastafrican"] = (157, 10),
        ["filipino"] = (77, 2),
        ["french"] = (141, 10),
        ["georgian"] = (140, 6),
        ["german"] = (205, 9),
        ["greek"] = (33, 1),
        ["hungarian"] = (18, 1),
        ["iberian"] = (105, 5),
        ["indian"] = (204, 16),
        ["indonesian"] = (97, 5),
        ["israeli"] = (92, 5),
        ["italian"] = (58, 3),
        ["japanese"] = (363, 31),
        ["korean"] = (175, 29),
        ["latino"] = (1661, 102),
        ["mongolian"] = (85, 5),
        ["persian"] = (185, 10),
        ["polish"] = (130, 6),
        ["polynesian"] = (624, 36),
        ["romanian"] = (78, 3),
        ["russian"] = (656, 40),
        ["scandinavian"] = (182, 30),
        ["southernafrican"] = (448, 16),
        ["thai"] = (315, 14),
        ["turkic"] = (203, 12),
        ["vietnamese"] = (31, 1),
        ["westafrican"] = (542, 31),
    };

    [Fact]
    public void EveryCulturesPoolPlusReserve_HoldsTwiceItsYear25Population()
    {
        foreach (var (culture, (male, female)) in Year25)
        {
            var (bf, bl) = NameData.Pool(culture, Region.NorthAmerica, Sex.Male);
            var (ff, _) = NameData.Pool(culture, Region.NorthAmerica, Sex.Female);
            var (rf, rl) = NameReserve.For(culture);
            int lasts = bl.Count + rl.Count;
            Assert.True((bf.Count + rf.Count) * lasts >= 2 * male, $"{culture} men: {(bf.Count + rf.Count) * lasts} pairs for {male} projected");
            Assert.True(ff.Count * lasts >= 2 * female, $"{culture} women: {ff.Count * lasts} pairs for {female} projected");
        }
    }

    [Fact]
    public void TheReserveIsClean_NoRepeats_NoAuthoredSurnames_NoRealStars()
    {
        // Surnames that must never read as family or as a real fighter: every authored person's surname,
        // plus a deny-list of distinctive real fighters' surnames.
        var authored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddLast(string full) { var t = full.Trim().Split(' '); authored.Add(t[^1]); }
        foreach (var d in DesignedRoster.All) authored.Add(d.Last);
        foreach (var d in DesignedLegends.All) authored.Add(d.Last);
        foreach (var p in OriginsPioneers.All) authored.Add(p.Fighter.Last);
        foreach (var a in OriginsArrivals.All) { if (a.Pioneer is { } pp) authored.Add(pp.Fighter.Last); if (a.Lead is { } lead) AddLast(lead); }
        foreach (var s in DesignedStaff.Staff) AddLast(s.Name);
        foreach (var o in DesignedStaff.Owners) AddLast(o.Name);
        foreach (var o in OriginsPromotions.Owners) AddLast(o.Name);
        foreach (var c in WorldEvents.CrossoverNames) AddLast(c);
        string[] realStars =
        {
            "McGregor", "Nurmagomedov", "Adesanya", "Ngannou", "Volkanovski", "Makhachev", "Chimaev", "Emelianenko", "Poirier",
            "Gaethje", "Masvidal", "Cejudo", "Dillashaw", "Cormier", "Lesnar", "Usman", "Whittaker", "Covington", "Holloway",
            "Rousey", "Shevchenko", "Velasquez", "Overeem", "Werdum", "Liddell", "Couture", "Shamrock", "Pettis", "Hendricks",
            "Bisping", "Rockhold", "Weidman", "Woodley", "Cerrone", "Miocic", "Oliveira", "Machida", "Aoki", "Bekele", "Folau",
        };
        foreach (var (culture, (rf, rl)) in NameReserve.All)
        {
            var (bf, bl) = NameData.Pool(culture, Region.NorthAmerica, Sex.Male);
            Assert.Equal(rf.Count, rf.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Equal(rl.Count, rl.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Empty(rf.Intersect(bf, StringComparer.OrdinalIgnoreCase));   // nothing the normal roll can already draw
            Assert.Empty(rl.Intersect(bl, StringComparer.OrdinalIgnoreCase));
            Assert.Empty(rf.Intersect(rl, StringComparer.OrdinalIgnoreCase));   // a first name is not a surname
            var clash = rl.Intersect(authored, StringComparer.OrdinalIgnoreCase).ToList();
            Assert.True(clash.Count == 0, $"{culture}: {string.Join(", ", clash)}");
            var stars = rl.Intersect(realStars, StringComparer.OrdinalIgnoreCase).ToList();
            Assert.True(stars.Count == 0, $"{culture}: {string.Join(", ", stars)}");
            Assert.All(rf.Concat(rl), n => Assert.Matches("^[A-Za-z]+$", n));   // ASCII like the base pools
        }
    }

    [Fact]
    public void TheReserveReallyRuns_AndYieldsToAuthoredNamesInTodaysMode()
    {
        // A saturated anglo pool settles on a reserve name (not a hyphen).
        var w = Modern(1);
        var gen = FighterGenerator.ForWorld(w);
        var (bf, bl) = NameData.Pool("anglo", Region.NorthAmerica, Sex.Male);
        Fighter Mint(string first, string last)
        {
            var m = gen.Generate(w.NewFighterId(), w.CurrentDate, new GenOptions { Nationality = Region.NorthAmerica, CountryCode = "USA", Sex = Sex.Male });
            m.FirstName = first; m.LastName = last;
            return m;
        }
        foreach (var fn in bf) foreach (var ln in bl) { var m = Mint(fn, ln); w.Fighters[m.Id] = m; }
        var f = Mint(bf[0], bl[0]);
        gen.EnsureUniqueName(w, f);
        Assert.DoesNotContain('-', f.LastName);
        Assert.Contains(f.FirstName, bf.Concat(NameReserve.For("anglo").First));

        // And a pioneer's exact name is reserved in today's mode too (his portrait is bound by name in every mode).
        var pioneer = OriginsPioneers.All[0].Fighter;
        var g = Mint("Cody", "Harker");
        g.FirstName = pioneer.First; g.LastName = pioneer.Last;
        gen.EnsureUniqueName(w, g);
        Assert.NotEqual($"{pioneer.First} {pioneer.Last}", Full(g));
    }
}
