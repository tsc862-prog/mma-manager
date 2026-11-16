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
        // The scenes that wake later (the chapters' own words: Sweden's wrestlers and kickboxers, Poland's
        // strikers and grapplers, rugby men with wrestlers' hands, the Philippines' boxers, the Andes' gyms).
        ["SWE"] = new[] { OriginsDiscipline.GrecoRomanWrestling, OriginsDiscipline.GrecoRomanWrestling, OriginsDiscipline.Kickboxing, OriginsDiscipline.Boxing },
        ["POL"] = new[] { OriginsDiscipline.Boxing, OriginsDiscipline.Kickboxing, OriginsDiscipline.Judo, OriginsDiscipline.FreestyleWrestling, OriginsDiscipline.GrecoRomanWrestling },
        ["AUS"] = new[] { OriginsDiscipline.Boxing, OriginsDiscipline.Brawler, OriginsDiscipline.Kickboxing, OriginsDiscipline.FreestyleWrestling, OriginsDiscipline.JiuJitsu },
        ["NZL"] = new[] { OriginsDiscipline.Brawler, OriginsDiscipline.Kickboxing, OriginsDiscipline.FreestyleWrestling, OriginsDiscipline.Boxing },
        ["ARG"] = new[] { OriginsDiscipline.Boxing, OriginsDiscipline.Boxing, OriginsDiscipline.FreestyleWrestling, OriginsDiscipline.JiuJitsu, OriginsDiscipline.Judo },
        ["CHN"] = new[] { OriginsDiscipline.Kickboxing, OriginsDiscipline.Kickboxing, OriginsDiscipline.FreestyleWrestling, OriginsDiscipline.Judo },
        ["PHI"] = new[] { OriginsDiscipline.Boxing, OriginsDiscipline.Boxing, OriginsDiscipline.Kickboxing, OriginsDiscipline.Brawler },
        ["SGP"] = new[] { OriginsDiscipline.MuayThai, OriginsDiscipline.Kickboxing, OriginsDiscipline.JiuJitsu, OriginsDiscipline.Boxing },
        ["TUR"] = new[] { OriginsDiscipline.FreestyleWrestling, OriginsDiscipline.FreestyleWrestling, OriginsDiscipline.Kickboxing, OriginsDiscipline.Boxing },
        ["RSA"] = new[] { OriginsDiscipline.Boxing, OriginsDiscipline.Kickboxing, OriginsDiscipline.FreestyleWrestling, OriginsDiscipline.Brawler },
        ["COL"] = new[] { OriginsDiscipline.Boxing, OriginsDiscipline.Boxing, OriginsDiscipline.Kickboxing, OriginsDiscipline.FreestyleWrestling },
        ["UAE"] = new[] { OriginsDiscipline.JiuJitsu, OriginsDiscipline.FreestyleWrestling, OriginsDiscipline.Boxing },
        ["NGA"] = new[] { OriginsDiscipline.FreestyleWrestling, OriginsDiscipline.FreestyleWrestling, OriginsDiscipline.Boxing, OriginsDiscipline.Brawler },
        ["IND"] = new[] { OriginsDiscipline.FreestyleWrestling, OriginsDiscipline.FreestyleWrestling, OriginsDiscipline.Boxing, OriginsDiscipline.Kickboxing },
        ["CHI"] = new[] { OriginsDiscipline.Boxing, OriginsDiscipline.FreestyleWrestling, OriginsDiscipline.Kickboxing },
        ["KSA"] = new[] { OriginsDiscipline.FreestyleWrestling, OriginsDiscipline.Boxing, OriginsDiscipline.JiuJitsu },
        ["QAT"] = new[] { OriginsDiscipline.JiuJitsu, OriginsDiscipline.Boxing, OriginsDiscipline.Kickboxing },
        ["KEN"] = new[] { OriginsDiscipline.Boxing, OriginsDiscipline.Boxing, OriginsDiscipline.FreestyleWrestling },
        ["GHA"] = new[] { OriginsDiscipline.Boxing, OriginsDiscipline.Boxing, OriginsDiscipline.FreestyleWrestling },
        ["MAR"] = new[] { OriginsDiscipline.Kickboxing, OriginsDiscipline.Boxing, OriginsDiscipline.FreestyleWrestling },
        ["SEN"] = new[] { OriginsDiscipline.FreestyleWrestling, OriginsDiscipline.FreestyleWrestling, OriginsDiscipline.Boxing },
        ["PER"] = new[] { OriginsDiscipline.Boxing, OriginsDiscipline.FreestyleWrestling, OriginsDiscipline.JiuJitsu },
    };

    /// <summary>
    /// A country's n-th art, cycling through its distinct arts in order of how common they are (its first is
    /// the art most of its men come from): the n-th gym a scene opens teaches this.
    /// </summary>
    public static OriginsDiscipline ArtOf(string? countryCode, int n)
    {
        var arts = (countryCode is not null && ArtsByCountry.TryGetValue(countryCode, out var a) ? a : DefaultArts).Distinct().ToArray();
        return arts[Math.Abs(n) % arts.Length];
    }

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

    /// <summary>
    /// The natural size of the men who answered the call in 1993: nobody cut weight for a sport with no
    /// classes, and the small men mostly stayed in boxing and judo. Lightweight and up, weighted to the
    /// middle and heavy end (repeats weight the pick); a hash of the id, never a draw.
    /// </summary>
    private static readonly WeightClass[] Sizes =
    {
        WeightClass.Featherweight,
        WeightClass.Lightweight, WeightClass.Lightweight, WeightClass.Lightweight,
        WeightClass.Welterweight, WeightClass.Welterweight, WeightClass.Welterweight, WeightClass.Welterweight,
        WeightClass.Middleweight, WeightClass.Middleweight, WeightClass.Middleweight, WeightClass.Middleweight, WeightClass.Middleweight,
        WeightClass.LightHeavyweight, WeightClass.LightHeavyweight, WeightClass.LightHeavyweight, WeightClass.LightHeavyweight,
        WeightClass.Heavyweight, WeightClass.Heavyweight, WeightClass.Heavyweight, WeightClass.Heavyweight,
    };

    /// <summary>
    /// The small men the sport draws once it has weight classes for them: when a lower line's chapter is open
    /// (live or fired), men of that size who used to stay in boxing and judo start turning professional
    /// (repeats weight the pick, added to <see cref="Sizes"/>). Day one has none of these open.
    /// </summary>
    private static readonly (string Chapter, WeightClass[] Adds)[] SmallerMen =
    {
        ("weight-ladder-lower", new[] { WeightClass.Lightweight, WeightClass.Lightweight, WeightClass.Featherweight, WeightClass.Featherweight, WeightClass.Featherweight }),
        ("weight-ladder-bantam", new[] { WeightClass.Bantamweight, WeightClass.Bantamweight, WeightClass.Bantamweight, WeightClass.Bantamweight }),
        ("weight-ladder-flyweight", new[] { WeightClass.Flyweight, WeightClass.Flyweight, WeightClass.Flyweight, WeightClass.Flyweight }),
    };

    /// <summary>
    /// A new man's natural class: a hash of his id over the sizes the sport draws today (<see cref="Sizes"/>,
    /// plus <see cref="SmallerMen"/> for every lower weight line whose chapter is open). On day one, and until
    /// the lightweight chapter opens, exactly the 1993 table.
    /// </summary>
    public static WeightClass NaturalClass(World world, FighterId id)
    {
        var sizes = SizesNow(world);
        return sizes[(int)(SimRandom.Mix(world.RngSeed, (ulong)id.Value ^ 0x5_12E_1993UL) % (ulong)sizes.Count)];
    }

    /// <summary>The sizes the sport draws its new men from today (read-only; the 1993 table until a lower line opens).</summary>
    public static IReadOnlyList<WeightClass> SizesNow(World world)
    {
        if (world.Mode != WorldMode.Origins) return Sizes;
        List<WeightClass>? sizes = null;
        foreach (var (chapter, adds) in SmallerMen)
            if (OriginsTimeline.IsOpen(world, chapter, world.CurrentDate))
                (sizes ??= new List<WeightClass>(Sizes)).AddRange(adds);
        return sizes is null ? Sizes : sizes;
    }

    /// <summary>
    /// A generated man finds a room: about half train in one of the existing gyms of their own country
    /// whose craft is their own (a judoka at the judo club, a wrestler at the wrestling room); the rest
    /// train where their art lives, outside the sport's rooms. A hash of the id, never a draw.
    /// The authored pioneers keep the rooms their stories give them (or none).
    /// </summary>
    public static void FindGym(World world, Fighter f)
    {
        if (f.Camp is not null || f.Designed) return;
        if (SimRandom.Mix(world.RngSeed, (ulong)f.Id.Value ^ 0x6_1A_1993UL) % 2 == 0) return;
        // (Once rooms cross-train, a room that teaches everything takes a man of any art.)
        f.Camp = world.Camps.Values
            .Where(c => (c.Specialty == f.PrimaryStyle || OriginsCamps.CrossTrains(world, c)) && world.Get(c.City)?.Country == f.Country)
            .OrderByDescending(c => c.Reputation).ThenBy(c => c.Id.Value)
            .Select(c => (CampId?)c.Id).FirstOrDefault();
    }

    /// <summary>The art this man comes from: a hash of his id over his country's arts.</summary>
    public static OriginsDiscipline DisciplineFor(World world, FighterId id, string? countryCode)
    {
        var arts = countryCode is not null && ArtsByCountry.TryGetValue(countryCode, out var a) ? a : DefaultArts;
        ulong h = SimRandom.Mix(world.RngSeed, (ulong)id.Value ^ 0x0A27_1993UL);
        return arts[(int)(h % (ulong)arts.Length)];
    }

    /// <summary>
    /// One specialist, added to the world as a free agent (0-0: his sport starts today). Country, age and
    /// level are the caller's (rolled on an Origins-only stream); the art and the size are hashes of the id.
    /// </summary>
    public static Fighter Create(World world, FighterGenerator gen, DateOnly on, string countryCode, int age, int overall)
    {
        var id = world.NewFighterId();
        var discipline = DisciplineFor(world, id, countryCode);
        // Once the sport has cross-training rooms, a share of the new men arrive knowing a second art (a hash of the id).
        OriginsDiscipline? second = CrossTrainedRoll(world, id, on) ? SecondArtFor(world, id, discipline) : null;
        // (A man who trained in two rooms is not a street brawler any more: his hands were taught in a boxing gym.)
        if (second is not null && discipline == OriginsDiscipline.Brawler) discipline = OriginsDiscipline.Boxing;
        var region = Geography.WorldAtlas.ByCode(countryCode)?.Continent ?? Region.NorthAmerica;
        var f = gen.Generate(id, on, new GenOptions
        {
            // The big-man arts are big men; everyone else is the size of a fighting man of his day (a hash of his id).
            WeightClass = discipline is OriginsDiscipline.Sumo or OriginsDiscipline.Ssireum ? WeightClass.Heavyweight : NaturalClass(world, id),
            Style = second is null ? OriginsPioneers.StyleOf(discipline) : PrimaryStyle.WellRounded,
            Nationality = region,
            CountryCode = countryCode,
            Sex = Sex.Male,
            Age = age,
            Overall = overall,
            WithRecord = false,
        });
        Specialise(world, f, discipline, overall, second);
        gen.AssignHometown(world, f, countryCode);
        gen.EnsureUniqueName(world, f);
        world.Fighters[id] = f;
        Personas.Stamp(world, f);   // his portrait's look (no catalog: nothing)
        return f;
    }

    /// <summary>
    /// Re-cut a fighter's skills around one art: the pioneers' discipline profile around his level, with a
    /// little personal variation (a hash of his id per skill, ±4) so two judoka of one level are not twins.
    /// Sumo men carry their bulk (natural weight of a sumo wrestler).
    /// </summary>
    public static void Specialise(World world, Fighter f, OriginsDiscipline discipline, int overall, OriginsDiscipline? second = null)
    {
        var row = new DesignedFighter(f.FirstName, f.LastName, f.Nickname, f.Nationality, f.WeightClass,
            OriginsPioneers.StyleOf(discipline), f.AgeOn(world.CurrentDate), overall, 0, 0);
        var skills = OriginsPioneers.SkillsOf(new OriginsPioneer(row, discipline, f.HeightCm, (int)(f.NaturalWeightLbs / 2.20462), ""));
        // A cross-trained man knows his second art nearly as well as a man who only did that, and his first a
        // little less than a man who only did his (the hours went somewhere): each skill is the better of the
        // two arts' profiles, the second a few points down, then the whole spread pulled down to keep the level.
        IReadOnlyDictionary<SkillId, int>? other = second is { } s2
            ? OriginsPioneers.SkillsOf(new OriginsPioneer(row with { Style = OriginsPioneers.StyleOf(s2) }, s2, f.HeightCm, (int)(f.NaturalWeightLbs / 2.20462), ""))
            : null;
        foreach (var (skill, value) in skills)
        {
            int v = value;
            if (other is not null && Technical.Contains(skill))
                v = Math.Max(value, other[skill] - SecondArtGap) - CrossTrainedLevelCost;
            int jitter = (int)(SimRandom.Mix(world.RngSeed, (ulong)f.Id.Value * 31UL + (ulong)skill) % 9) - 4;
            SetSkill(f, skill, Math.Clamp(v + jitter, 1, 95));
        }
        if (discipline is OriginsDiscipline.Sumo && f.NaturalWeightLbs < 300)
            f.NaturalWeightLbs = 300 + (int)(SimRandom.Mix(world.RngSeed, (ulong)f.Id.Value ^ 0x5_0B0UL) % 60);
    }

    // ------------------------------------------------------------------ specialists and all-rounders

    /// <summary>The striking half of the game (the seven stand-up skills).</summary>
    public static readonly SkillId[] StrikingSkills =
    {
        SkillId.PunchPower, SkillId.PunchTechnique, SkillId.KickPower, SkillId.KickTechnique, SkillId.ClinchStriking,
        SkillId.StrikingDefense, SkillId.Footwork,
    };

    /// <summary>The grappling half of the game (wrestling and the ground: eight skills).</summary>
    public static readonly SkillId[] GrapplingSkills =
    {
        SkillId.TakedownOffense, SkillId.TakedownDefense, SkillId.TopControl, SkillId.BottomGame,
        SkillId.SubmissionOffense, SkillId.SubmissionDefense, SkillId.Scrambling, SkillId.ClinchControl,
    };

    private static readonly SkillId[] Technical = StrikingSkills.Concat(GrapplingSkills).ToArray();

    /// <summary>How far below its own profile a cross-trained man knows his second art.</summary>
    public const int SecondArtGap = 6;

    /// <summary>What knowing two arts costs a man in each one (the same hours, split): every technical skill this many points down.</summary>
    public const int CrossTrainedLevelCost = 3;

    /// <summary>A half of the game is TAUGHT when its mean sits no more than this far below the man's own level (body and head).</summary>
    public const int TaughtMargin = 16;

    /// <summary>The mean of the striking half (true skills).</summary>
    /// <remarks>
    /// WORLD-LEVEL DETECTOR: reads true skills. Not for display on a fighter, and not for any decision made on an
    /// org's behalf (the fog); a screen shows <see cref="ListedAsAllRounder"/> instead.
    /// </remarks>
    public static double StrikingMean(Fighter f) =>
        (f.Striking.PunchPower + f.Striking.PunchTechnique + f.Striking.KickPower + f.Striking.KickTechnique
         + f.Striking.ClinchStriking + f.Striking.StrikingDefense + f.Striking.Footwork) / 7.0;

    /// <summary>The mean of the grappling half (true skills; a world-level detector, not for display: see <see cref="StrikingMean"/>).</summary>
    public static double GrapplingMean(Fighter f) =>
        (f.Grappling.TakedownOffense + f.Grappling.TakedownDefense + f.Grappling.TopControl + f.Grappling.BottomGame
         + f.Grappling.SubmissionOffense + f.Grappling.SubmissionDefense + f.Grappling.Scrambling + f.Grappling.ClinchControl) / 8.0;

    /// <summary>The man's own level, apart from his craft: the mean of his body (six) and his head (four). True skills: a world-level detector, not for display.</summary>
    public static double BaseLevel(Fighter f) =>
        (f.Physical.Strength + f.Physical.Cardio + f.Physical.Speed + f.Physical.Chin + f.Physical.Recovery + f.Physical.Flexibility
         + f.Mental.FightIQ + f.Mental.Composure + f.Mental.Heart + f.Mental.Discipline) / 10.0;

    /// <summary>
    /// Does this man know more than one art: is each half of the game (striking, grappling) taught, its mean no
    /// more than <see cref="TaughtMargin"/> below his own level (<see cref="BaseLevel"/>)? A specialist is lost in
    /// one half (every 1993 pioneer is one; a brawler is lost in both). Read off his skills today, so a
    /// specialist who learns the other half becomes an all-rounder.
    /// WORLD-LEVEL DETECTOR: reads true skills. Not for display on a fighter, and not for any decision made on an
    /// org's behalf (the fog); a screen shows <see cref="ListedAsAllRounder"/> instead.
    /// </summary>
    public static bool IsAllRounder(Fighter f)
    {
        double floor = BaseLevel(f) - TaughtMargin;
        return StrikingMean(f) >= floor && GrapplingMean(f) >= floor;
    }

    /// <summary>
    /// What the public knows: is this man LISTED as knowing more than one art (his style is
    /// <see cref="PrimaryStyle.WellRounded"/>: he arrived cross-trained, or his story says so)? The read for a
    /// fighter's screen; a man who learned the other half on the mats shows it in his record, not here.
    /// </summary>
    public static bool ListedAsAllRounder(Fighter f) => f.PrimaryStyle == PrimaryStyle.WellRounded;

    private const ulong CrossSalt = 0x0219_C405_5000UL, SecondArtSalt = 0x0219_5EC0_0D00UL;

    /// <summary>
    /// The share of new men who arrive knowing a second art, on a date: none before the first famous
    /// cross-training camp; then 10% rising 7 points a year to 45%; once the specialists have fallen,
    /// half rising 7 points a year to 85%. Zero in today's mode (never asked there).
    /// </summary>
    public static double CrossTrainedShare(World world, DateOnly on)
    {
        if (world.Mode != WorldMode.Origins || world.Era.Chapter(OriginsCamps.FirstCampChapter) is not { } camp || camp.FiredOn > on) return 0;
        if (world.Era.Chapter(OriginsCamps.SpecialistsFallChapter) is { } fall && fall.FiredOn <= on)
            return Math.Min(0.85, 0.50 + 0.07 * YearsBetween(fall.FiredOn, on));
        return Math.Min(0.45, 0.10 + 0.07 * YearsBetween(camp.FiredOn, on));
    }

    private static double YearsBetween(DateOnly from, DateOnly to) => Math.Max(0, to.DayNumber - from.DayNumber) / 365.25;

    /// <summary>Does this new man arrive cross-trained? A hash of his id against the day's share.</summary>
    private static bool CrossTrainedRoll(World world, FighterId id, DateOnly on)
    {
        double share = CrossTrainedShare(world, on);
        if (share <= 0) return false;
        ulong h = SimRandom.Mix(world.RngSeed, (ulong)id.Value ^ CrossSalt);
        return (h >> 11) * (1.0 / (1UL << 53)) < share;
    }

    /// <summary>The second art a cross-trained man took up: from the other half of the game than his first (a hash of his id).</summary>
    private static OriginsDiscipline SecondArtFor(World world, FighterId id, OriginsDiscipline first)
    {
        OriginsDiscipline[] options = OriginsPioneers.StyleOf(first) switch
        {
            PrimaryStyle.Striker or PrimaryStyle.Kickboxer or PrimaryStyle.Brawler =>
                new[] { OriginsDiscipline.FreestyleWrestling, OriginsDiscipline.FreestyleWrestling, OriginsDiscipline.JiuJitsu, OriginsDiscipline.Judo, OriginsDiscipline.Sambo },
            _ => new[] { OriginsDiscipline.Boxing, OriginsDiscipline.Boxing, OriginsDiscipline.Kickboxing, OriginsDiscipline.MuayThai },
        };
        return options[(int)(SimRandom.Mix(world.RngSeed, (ulong)id.Value ^ SecondArtSalt) % (ulong)options.Length)];
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
            made.Add(Create(world, gen, on, country, age, overall));
        }
        return made;
    }

    /// <summary>The size of the sport the intake keeps up (about the day-one world: three houses and their market).</summary>
    public const int TargetActives = 110;

    /// <summary>
    /// How many new men turn pro this month in an Origins world: those who left are replaced, and a sport
    /// that has shrunk below <see cref="TargetActives"/> (men also leave by routes the month doesn't count)
    /// draws a few more, up to four, from the gyms. Phase 3's scenes will grow the target.
    /// </summary>
    public static int MonthlyIntake(int leavers, int actives) => MonthlyIntake(leavers, actives, TargetActives);

    /// <summary>As above, against a target of its own (a sport with more houses needs more men).</summary>
    public static int MonthlyIntake(int leavers, int actives, int target) =>
        leavers + Math.Clamp((target - actives) / 6, 0, 4);

    /// <summary>The houses' total demand for men on day one (three houses of a dozen), which <see cref="TargetActives"/> was tuned to.</summary>
    public const int DayOneDemand = 36;

    /// <summary>
    /// The size the sport keeps up in this world: the day-one target, plus 1.3 men for every man the houses
    /// want past day one's demand (a new house, a house split into divisions). A sport of more houses needs
    /// more men, or the new ones starve the old.
    /// </summary>
    public static int TargetFor(World world) =>
        TargetActives + (int)(1.3 * Math.Max(0, world.Promotions.Values.Where(p => !p.Defunct).Sum(OriginsShows.RosterDemand) - DayOneDemand));

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
        // A scene's share of the new men: the depth of its fight culture, ramping up over its first years.
        var weights = scenes.Select(code => OriginsScenes.TalentWeight(world, code, date)).ToArray();

        var rng = new SimRandom(SimRandom.Mix(world.RngSeed, (ulong)date.DayNumber ^ 0x0219_17A6_E000UL));
        var gen = FighterGenerator.ForWorld(world);
        for (int i = 0; i < count; i++)
        {
            string country = scenes[rng.WeightedIndex(weights)];
            var f = Create(world, gen, date, country, rng.Range(19, 26), rng.GaussianInt(42, 8, 26, 64));
            FindGym(world, f);
            made.Add(f);
        }
        return made;
    }
}
