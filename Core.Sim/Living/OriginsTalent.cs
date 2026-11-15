using MmaManager.Core.Model;
using MmaManager.Core.Sim.Engine;
using MmaManager.Core.Sim.Generation;
using MmaManager.Core.Sim.Generation.Origins;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Living;

/// <summary>
/// The talent of a sport that has just been born (Origins mode): single-discipline specialists. A
/// generated man is still built by <see cref="FighterGenerator"/> (name, body, tendencies, potential,
/// hometown), then his skills are re-cut around ONE art with the pioneers' own discipline profiles
/// (<see cref="OriginsPioneers.SkillsOf"/>): the boxer can box and nothing else, the judoka throws and
/// holds. Which art is a hash of his id and his country, never a draw.
///
/// Only an Origins world ever calls this, and every roll it makes is on a stream keyed to the Origins
/// build or to the month (never the week's shared stream), so today's mode is untouched.
/// </summary>
public static class OriginsTalent
{
    /// <summary>The arts a country's men come from in 1993, most common first (repeats weight the pick).</summary>
    private static readonly Dictionary<string, OriginsDiscipline[]> ArtsByCountry = new(StringComparer.Ordinal)
    {
        ["USA"] = new[] { OriginsDiscipline.FreestyleWrestling, OriginsDiscipline.FreestyleWrestling, OriginsDiscipline.Boxing, OriginsDiscipline.Boxing,
                          OriginsDiscipline.Karate, OriginsDiscipline.Kickboxing, OriginsDiscipline.Brawler, OriginsDiscipline.ProWrestling,
                          OriginsDiscipline.Judo, OriginsDiscipline.JiuJitsu },
        ["JPN"] = new[] { OriginsDiscipline.ProWrestling, OriginsDiscipline.ProWrestling, OriginsDiscipline.Judo, OriginsDiscipline.Judo,
                          OriginsDiscipline.Karate, OriginsDiscipline.Kickboxing, OriginsDiscipline.FreestyleWrestling, OriginsDiscipline.Sumo,
                          OriginsDiscipline.Boxing },
        ["BRA"] = new[] { OriginsDiscipline.JiuJitsu, OriginsDiscipline.JiuJitsu, OriginsDiscipline.JiuJitsu, OriginsDiscipline.LutaLivre,
                          OriginsDiscipline.LutaLivre, OriginsDiscipline.Capoeira, OriginsDiscipline.Boxing, OriginsDiscipline.Judo,
                          OriginsDiscipline.Brawler, OriginsDiscipline.Kickboxing },
        ["NED"] = new[] { OriginsDiscipline.Kickboxing, OriginsDiscipline.Kickboxing, OriginsDiscipline.Kickboxing, OriginsDiscipline.Judo },
        ["RUS"] = new[] { OriginsDiscipline.Sambo, OriginsDiscipline.Sambo, OriginsDiscipline.GrecoRomanWrestling, OriginsDiscipline.Judo, OriginsDiscipline.Boxing },
        ["GEO"] = new[] { OriginsDiscipline.Judo, OriginsDiscipline.Sambo, OriginsDiscipline.GrecoRomanWrestling },
        ["KOR"] = new[] { OriginsDiscipline.Taekwondo, OriginsDiscipline.Taekwondo, OriginsDiscipline.Ssireum, OriginsDiscipline.Judo },
        ["THA"] = new[] { OriginsDiscipline.MuayThai },
        ["CUB"] = new[] { OriginsDiscipline.Boxing, OriginsDiscipline.Boxing, OriginsDiscipline.FreestyleWrestling },
        ["MEX"] = new[] { OriginsDiscipline.Boxing, OriginsDiscipline.Boxing, OriginsDiscipline.ProWrestling },
        ["CAN"] = new[] { OriginsDiscipline.Judo, OriginsDiscipline.FreestyleWrestling, OriginsDiscipline.Boxing, OriginsDiscipline.Karate },
        ["GBR"] = new[] { OriginsDiscipline.Boxing, OriginsDiscipline.Boxing, OriginsDiscipline.Brawler, OriginsDiscipline.Judo, OriginsDiscipline.Kickboxing },
        ["IRL"] = new[] { OriginsDiscipline.Boxing, OriginsDiscipline.Brawler },
        ["FRA"] = new[] { OriginsDiscipline.Judo, OriginsDiscipline.Judo, OriginsDiscipline.Kickboxing, OriginsDiscipline.Boxing },
        ["UKR"] = new[] { OriginsDiscipline.Sambo, OriginsDiscipline.FreestyleWrestling, OriginsDiscipline.Boxing },
        ["IRI"] = new[] { OriginsDiscipline.FreestyleWrestling },
        ["SAM"] = new[] { OriginsDiscipline.Sumo, OriginsDiscipline.Brawler },
    };

    private static readonly OriginsDiscipline[] DefaultArts =
    {
        OriginsDiscipline.Boxing, OriginsDiscipline.Judo, OriginsDiscipline.FreestyleWrestling, OriginsDiscipline.Karate,
        OriginsDiscipline.Kickboxing, OriginsDiscipline.Brawler,
    };

    /// <summary>
    /// Where the 1993 talent pool comes from: the three day-one scenes most of all, then the fight nations
    /// whose specialists travel. (Country codes; repeats weight the pick.)
    /// </summary>
    private static readonly string[] PoolCountries =
    {
        "USA", "USA", "USA", "USA", "USA", "USA", "USA", "USA", "USA",
        "JPN", "JPN", "JPN", "JPN", "JPN", "JPN", "JPN",
        "BRA", "BRA", "BRA", "BRA", "BRA", "BRA", "BRA",
        "NED", "RUS", "RUS", "KOR", "THA", "CUB", "MEX", "CAN", "GBR", "FRA", "GEO", "UKR",
    };

    /// <summary>The art this man comes from: a hash of his id over his country's arts.</summary>
    public static OriginsDiscipline DisciplineFor(World world, FighterId id, string? countryCode)
    {
        var arts = countryCode is not null && ArtsByCountry.TryGetValue(countryCode, out var a) ? a : DefaultArts;
        ulong h = SimRandom.Mix(world.RngSeed, (ulong)id.Value ^ 0x0A27_1993UL);
        return arts[(int)(h % (ulong)arts.Length)];
    }

    /// <summary>
    /// One specialist, added to the world as a free agent (0-0: his sport starts today). Country, age and
    /// level come from <paramref name="rng"/> (an Origins-only stream); the art is a hash of the id.
    /// </summary>
    public static Fighter Create(World world, FighterGenerator gen, DateOnly on, SimRandom rng, string countryCode,
        int age, int overall)
    {
        var id = world.NewFighterId();
        var discipline = DisciplineFor(world, id, countryCode);
        var region = Geography.WorldAtlas.ByCode(countryCode)?.Continent ?? Region.NorthAmerica;
        var f = gen.Generate(id, on, new GenOptions
        {
            // The big-man arts are big men; everyone else's size is the generator's.
            WeightClass = discipline is OriginsDiscipline.Sumo or OriginsDiscipline.Ssireum ? WeightClass.Heavyweight : null,
            Style = OriginsPioneers.StyleOf(discipline),
            Nationality = region,
            CountryCode = countryCode,
            Sex = Sex.Male,
            Age = age,
            Overall = overall,
            WithRecord = false,
        });
        Specialise(world, f, discipline, overall);
        gen.AssignHometown(world, f, countryCode);
        gen.EnsureUniqueName(world, f);
        world.Fighters[id] = f;
        return f;
    }

    /// <summary>
    /// Re-cut a fighter's skills around one art: the pioneers' discipline profile around his level, with a
    /// little personal variation (a hash of his id per skill, ±4) so two judoka of one level are not twins.
    /// Sumo men carry their bulk (natural weight of a sumo wrestler).
    /// </summary>
    public static void Specialise(World world, Fighter f, OriginsDiscipline discipline, int overall)
    {
        var row = new DesignedFighter(f.FirstName, f.LastName, f.Nickname, f.Nationality, f.WeightClass,
            OriginsPioneers.StyleOf(discipline), f.AgeOn(world.CurrentDate), overall, 0, 0);
        var skills = OriginsPioneers.SkillsOf(new OriginsPioneer(row, discipline, f.HeightCm, (int)(f.NaturalWeightLbs / 2.20462), ""));
        foreach (var (skill, value) in skills)
        {
            int jitter = (int)(SimRandom.Mix(world.RngSeed, (ulong)f.Id.Value * 31UL + (ulong)skill) % 9) - 4;
            SetSkill(f, skill, Math.Clamp(value + jitter, 1, 95));
        }
        if (discipline is OriginsDiscipline.Sumo && f.NaturalWeightLbs < 300)
            f.NaturalWeightLbs = 300 + (int)(SimRandom.Mix(world.RngSeed, (ulong)f.Id.Value ^ 0x5_0B0UL) % 60);
    }

    /// <summary>Set one skill by id (the lab's accessor table, so the two never disagree on a name).</summary>
    public static void SetSkill(Fighter f, SkillId skill, int value)
    {
        string name = skill.ToString();
        foreach (var a in FightLab.Attributes)
            if (a.Name == name) { a.Set(f, value); return; }
        throw new InvalidOperationException($"No attribute accessor for {name}");
    }

    /// <summary>
    /// The day-one supporting pool around the pioneers: <paramref name="count"/> specialists, 0-0, mostly from
    /// the three scenes, aged for 1993 (21-35) and mostly unremarkable, with a few real talents.
    /// </summary>
    public static List<Fighter> SeedPool(World world, FighterGenerator gen, DateOnly on, SimRandom rng, int count)
    {
        var made = new List<Fighter>(count);
        for (int i = 0; i < count; i++)
        {
            string country = PoolCountries[rng.Next(0, PoolCountries.Length)];
            int age = rng.Range(21, 35);
            int overall = rng.GaussianInt(50, 8, 32, 70);
            made.Add(Create(world, gen, on, rng, country, age, overall));
        }
        return made;
    }

    /// <summary>
    /// How many new men turn pro this month in an Origins world: those who left are replaced, plus one more
    /// while the open market is thin (a third of the sport unsigned is plenty for three promotions).
    /// </summary>
    public static int MonthlyIntake(int leavers, int actives, int unsigned) =>
        leavers + (unsigned > actives / 3 ? 0 : 1);

    /// <summary>
    /// The month's intake of new men in an Origins world: young specialists from the countries that HAVE a
    /// scene (a country with no scene grows no talent of its own), on a stream keyed to the month — so the
    /// week's shared stream is never touched. Each joins a gym near home when there is one.
    /// </summary>
    public static IReadOnlyList<Fighter> Intake(World world, int count, DateOnly date)
    {
        var made = new List<Fighter>();
        if (count <= 0) return made;
        var scenes = Era.Scenes(world)
            .Where(code => Geography.WorldMap.CountryByCode(world, code) is not null)
            .ToList();   // SortedSet order: deterministic
        if (scenes.Count == 0) return made;

        var rng = new SimRandom(SimRandom.Mix(world.RngSeed, (ulong)date.DayNumber ^ 0x0219_17A6_E000UL));
        var gen = FighterGenerator.ForWorld(world);
        for (int i = 0; i < count; i++)
        {
            string country = scenes[rng.Next(0, scenes.Count)];
            var f = Create(world, gen, date, rng, country, rng.Range(19, 26), rng.GaussianInt(42, 8, 26, 64));
            Camps.JoinLocalCamp(world, f, rng);
            made.Add(f);
        }
        return made;
    }
}
