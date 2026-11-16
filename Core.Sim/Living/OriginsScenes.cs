using MmaManager.Core.Model;
using MmaManager.Core.Sim.Generation.Origins;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Living;

/// <summary>One national scene of an Origins world, ready to show: when it woke, by which chapter, and what it holds now.</summary>
public sealed record SceneView(
    string Code, string Name, Region Continent, DateOnly WokeOn, string? Chapter, bool SecondCentre,
    int Houses, int Gyms, int CrossTrainingGyms, int Men, double TalentShare);

/// <summary>
/// The sport spreading country by country (Origins phase 3). A scene is dormant until its chapter wakes it
/// (<see cref="OriginsChapters.SceneOrder"/>; the first house to take a night there IS the chapter,
/// <see cref="OriginsTimeline.ContestedScene"/>); from then on:
/// <list type="bullet">
/// <item>it grows men of its own (<see cref="OriginsTalent.Intake"/>), its share of the sport's intake set by
/// the depth of its fight culture (the atlas weight, square-rooted) and ramping up over its first
/// <see cref="RampYears"/> years (<see cref="TalentWeight"/>): a young scene is a handful of gyms, not a nation;</item>
/// <item>gyms open there: its first the month after it wakes, more as its men outgrow the mats
/// (<see cref="RunMonthly"/>), teaching the country's own art until the sport learns to cross-train;</item>
/// <item>new houses may open there (<see cref="OriginsFounding"/>: any city with a scene).</item>
/// </list>
/// And one scene becomes the sport's SECOND CENTRE when its chapter fires (<see cref="SecondCentreChapter"/>):
/// a fight-mad market (its cities' interest rises for good, so its gates are bigger), houses that keep their
/// own house rules when the commissions unify everyone else's, and a ring rather than a fence.
/// Today's mode never comes here. Keyed rolls only (seed, date, salt): nothing on a shared stream.
/// </summary>
public static class OriginsScenes
{
    public const string SecondCentreChapter = "second-centre-arenas";

    /// <summary>The second centre when nobody outside North America made the chapter happen (the chapter's own words: Tokyo).</summary>
    public const string DefaultSecondCentre = "JPN";

    /// <summary>How much the second centre's market warms, for good (interest points, base and current).</summary>
    public const int SecondCentreInterest = 15;

    /// <summary>The highest a second centre's interest is taken.</summary>
    public const int SecondCentreInterestCap = 85;

    /// <summary>A scene's talent ramps from a quarter of its depth to all of it over this many years.</summary>
    public const double RampYears = 5.0;

    /// <summary>A scene opens another gym when its men outnumber its mats by this many per gym (to <see cref="MaxGyms"/>).</summary>
    public const int MenPerGym = 15;

    public const int MaxGyms = 8;

    /// <summary>A crowded scene's monthly chance of a new gym opening.</summary>
    public const double NewGymChance = 0.15;

    private const ulong GymSalt = 0x0219_6E50_0007UL;

    // ------------------------------------------------------------------ reading the map

    /// <summary>The chapter that wakes each country (by code), from the table.</summary>
    private static readonly Dictionary<string, string> WakingChapter = OriginsChapters.SceneOrder
        .GroupBy(x => x.Country.ToUpperInvariant()).ToDictionary(g => g.Key, g => g.First().Chapter, StringComparer.Ordinal);

    /// <summary>
    /// The day a country's scene woke: the world's first day for a day-one scene, the day its chapter fired
    /// otherwise. Null while it sleeps (and in today's mode, where nothing ever slept: use <see cref="Era.AllowsScene"/>).
    /// </summary>
    public static DateOnly? WokeOn(World world, string countryCode)
    {
        if (world.Mode != WorldMode.Origins) return null;
        string code = countryCode.ToUpperInvariant();
        if (!world.Era.IsUnlocked(Capability.Scene(code))) return null;
        if (WakingChapter.TryGetValue(code, out var key) && world.Era.Chapter(key) is { } rec) return rec.FiredOn;
        return world.StartDate;
    }

    /// <summary>
    /// A scene's weight in the month's intake: the depth of its fight culture (the square root of the atlas
    /// weight: the United States 5.5, Brazil 4.9, Russia 4.5, Sweden 1.7) times its ramp, a quarter in its
    /// first month rising to all of it after <see cref="RampYears"/>. Zero for a dormant scene.
    /// </summary>
    public static double TalentWeight(World world, string countryCode, DateOnly date)
    {
        if (WokeOn(world, countryCode) is not { } woke) return 0;
        double depth = Math.Sqrt(Math.Max(1, Geography.WorldAtlas.ByCode(countryCode.ToUpperInvariant())?.Weight ?? 1));
        double years = Math.Max(0, date.DayNumber - woke.DayNumber) / 365.25;
        return depth * (0.25 + 0.75 * Math.Min(1.0, years / RampYears));
    }

    /// <summary>The sport's second centre (a country code), once its chapter has fired; null before (and in today's mode).</summary>
    public static string? SecondCentre(World world)
    {
        if (world.Mode != WorldMode.Origins || world.Era.Chapter(SecondCentreChapter) is not { } rec) return null;
        if (rec.City is { } c && world.Get(c) is { } city && city.Region != Region.NorthAmerica && world.Get(city.Country) is { } k)
            return k.Code;
        return DefaultSecondCentre;
    }

    /// <summary>Is this house based in the sport's second centre?</summary>
    public static bool InSecondCentre(World world, Promotion p) =>
        SecondCentre(world) is { } code && p.HomeCity is { } hc && world.Get(hc) is { } city
        && world.Get(city.Country)?.Code == code;

    /// <summary>
    /// Every awake scene, in the order it woke (ties on code): who runs houses there, its gyms, its men, and
    /// its share of the month's new men. Read-only; empty in today's mode.
    /// </summary>
    public static IReadOnlyList<SceneView> Scenes(World world)
    {
        if (world.Mode != WorldMode.Origins) return Array.Empty<SceneView>();
        var date = world.CurrentDate;
        var codes = Era.Scenes(world).ToList();
        double total = codes.Sum(c => TalentWeight(world, c, date));
        string? centre = SecondCentre(world);
        var list = new List<SceneView>();
        foreach (var code in codes)
        {
            if (Geography.WorldMap.CountryByCode(world, code) is not { } country) continue;
            var cities = world.Cities.Values.Where(c => c.Country == country.Id).Select(c => c.Id).ToHashSet();
            var gyms = world.Camps.Values.Where(c => cities.Contains(c.City)).ToList();
            list.Add(new SceneView(
                code, country.Name, country.Continent, WokeOn(world, code) ?? world.StartDate,
                WakingChapter.GetValueOrDefault(code), code == centre,
                world.Promotions.Values.Count(p => !p.Defunct && p.HomeCity is { } hc && cities.Contains(hc)),
                gyms.Count, gyms.Count(g => OriginsCamps.CrossTrains(world, g)),
                world.Fighters.Values.Count(f => f.Status == FighterStatus.Active && f.Country == country.Id),
                total <= 0 ? 0 : TalentWeight(world, code, date) / total));
        }
        return list.OrderBy(s => s.WokeOn).ThenBy(s => s.Code, StringComparer.Ordinal).ToList();
    }

    // ------------------------------------------------------------------ the month

    /// <summary>
    /// The month's scenes (Origins; called from the timeline's monthly block): a woken scene with no gym opens
    /// its first; a scene whose men outnumber its mats may open another.
    /// </summary>
    public static void RunMonthly(World world, DateOnly date)
    {
        if (world.Mode != WorldMode.Origins) return;
        int month = OriginsTimeline.Month(world, date);
        foreach (var code in Era.Scenes(world).ToList())
        {
            if (Geography.WorldMap.CountryByCode(world, code) is not { } country) continue;
            var cities = world.Cities.Values.Where(c => c.Country == country.Id).Select(c => c.Id).ToHashSet();
            if (cities.Count == 0) continue;
            int gyms = world.Camps.Values.Count(c => cities.Contains(c.City));
            if (gyms == 0) { OpenGym(world, country, date); continue; }
            if (gyms >= MaxGyms) continue;
            int men = world.Fighters.Values.Count(f => f.Status == FighterStatus.Active && f.Country == country.Id);
            if (men <= MenPerGym * gyms) continue;
            if (OriginsTimeline.Roll(world, GymSalt, OriginsTimeline.KeyHash(code), (ulong)month) < NewGymChance)
                OpenGym(world, country, date);
        }
    }

    /// <summary>
    /// A new gym in a scene: in its biggest city without one (else its biggest), teaching the country's own
    /// art, or every art once the sport cross-trains (half the new rooms at first, most once the specialists
    /// have fallen). An honest local room: middling coaching, no name yet, a modest fee.
    /// </summary>
    internal static Camp? OpenGym(World world, Country country, DateOnly date)
    {
        var cities = world.Cities.Values.Where(c => c.Country == country.Id)
            .OrderByDescending(c => c.Market).ThenBy(c => c.Id.Value).ToList();
        if (cities.Count == 0) return null;
        var withGym = world.Camps.Values.Select(c => c.City).ToHashSet();
        var city = cities.FirstOrDefault(c => !withGym.Contains(c.Id)) ?? cities[0];
        int existing = world.Camps.Values.Count(c => world.Get(c.City)?.Country == country.Id);
        ulong h = SimRandom.Mix(world.RngSeed, (ulong)date.DayNumber ^ GymSalt ^ OriginsTimeline.KeyHash(country.Code));

        double crossShare = !world.Era.HasFired(OriginsCamps.FirstCampChapter) ? 0
            : world.Era.HasFired(OriginsCamps.SpecialistsFallChapter) ? 0.8 : 0.5;
        bool cross = crossShare > 0 && (h % 1000) / 1000.0 < crossShare;
        var art = OriginsTalent.ArtOf(country.Code, existing);
        var style = cross ? PrimaryStyle.WellRounded : OriginsPioneers.StyleOf(art);

        string name = $"{city.Name} {(cross ? CrossNames[(int)((h >> 10) % (ulong)CrossNames.Length)] : RoomName(art))}";
        if (world.Camps.Values.Any(c => c.Name == name)) name = $"{name} {RomanSuffix(existing)}";

        var camp = new Camp
        {
            Id = world.NewCampId(),
            Name = name,
            City = city.Id,
            Specialty = style,
            CoachQuality = 44 + (int)((h >> 20) % 14),
            Reputation = 12 + (int)((h >> 28) % 10),
            Capacity = 30,
            MonthlyFee = new Money(300m + (h >> 36) % 4 * 100m),
        };
        world.Camps[camp.Id] = camp;
        News.Post(world, date, NewsCategory.General, $"{name} opens in {city.Name}",
            cross
                ? $"A new room in {country.Name} teaches striking, takedowns and the ground on one timetable."
                : $"{country.Name}'s {ArtWords(art)} have a room of their own in the new sport.", default);
        return camp;
    }

    private static readonly string[] CrossNames = { "Combat Club", "Fight Team", "Mixed Martial Arts", "Fight Academy" };

    private static string RomanSuffix(int n) => n switch { 0 or 1 => "II", 2 => "III", 3 => "IV", 4 => "V", 5 => "VI", 6 => "VII", _ => "VIII" };

    private static string RoomName(OriginsDiscipline art) => art switch
    {
        OriginsDiscipline.Boxing => "Boxing Club",
        OriginsDiscipline.Kickboxing => "Kickboxing Gym",
        OriginsDiscipline.MuayThai => "Muay Thai Camp",
        OriginsDiscipline.Karate => "Karate Dojo",
        OriginsDiscipline.Taekwondo => "Taekwondo Hall",
        OriginsDiscipline.Capoeira => "Capoeira Roda",
        OriginsDiscipline.Judo => "Judo Club",
        OriginsDiscipline.FreestyleWrestling => "Wrestling Club",
        OriginsDiscipline.GrecoRomanWrestling => "Wrestling Hall",
        OriginsDiscipline.Sumo => "Sumo Stable",
        OriginsDiscipline.Ssireum => "Ssireum Hall",
        OriginsDiscipline.JiuJitsu => "Jiu-Jitsu Academy",
        OriginsDiscipline.Sambo => "Sambo Hall",
        OriginsDiscipline.LutaLivre => "Luta Livre School",
        OriginsDiscipline.ProWrestling => "Wrestling Dojo",
        _ => "Fight Club",
    };

    private static string ArtWords(OriginsDiscipline art) => art switch
    {
        OriginsDiscipline.Boxing => "boxers",
        OriginsDiscipline.Kickboxing => "kickboxers",
        OriginsDiscipline.MuayThai => "Thai boxers",
        OriginsDiscipline.Karate => "karateka",
        OriginsDiscipline.Taekwondo => "taekwondo men",
        OriginsDiscipline.Judo => "judoka",
        OriginsDiscipline.FreestyleWrestling or OriginsDiscipline.GrecoRomanWrestling or OriginsDiscipline.ProWrestling => "wrestlers",
        OriginsDiscipline.JiuJitsu => "jiu-jitsu men",
        OriginsDiscipline.Sambo => "sambo men",
        _ => "fighters",
    };

    // ------------------------------------------------------------------ the second centre

    /// <summary>
    /// The second centre begins (the chapter's effect): its cities' and its country's interest rise for good
    /// (base and current, to <see cref="SecondCentreInterestCap"/>), so its gates are bigger; its houses fight in
    /// a ring under their own house rules (they keep them when the commissions unify the rest).
    /// </summary>
    internal static void SecondCentreBegins(World world, DateOnly date)
    {
        if (SecondCentre(world) is not { } code || Geography.WorldMap.CountryByCode(world, code) is not { } country) return;
        country.MmaInterestBase = Math.Max(country.MmaInterestBase, Math.Min(SecondCentreInterestCap, country.MmaInterestBase + SecondCentreInterest));
        country.MmaInterest = Math.Max(country.MmaInterest, Math.Min(SecondCentreInterestCap, country.MmaInterest + SecondCentreInterest));
        foreach (var c in world.Cities.Values.Where(c => c.Country == country.Id).OrderBy(c => c.Id.Value))
        {
            c.MmaInterestBase = Math.Max(c.MmaInterestBase, Math.Min(SecondCentreInterestCap, c.MmaInterestBase + SecondCentreInterest));
            c.MmaInterest = Math.Max(c.MmaInterest, Math.Min(SecondCentreInterestCap, c.MmaInterest + SecondCentreInterest));
        }
        foreach (var p in world.Promotions.Values.Where(p => !p.Defunct && InSecondCentre(world, p)).OrderBy(p => p.Id.Value))
            if (p.Rules.Fight is { } rules && rules.Enclosure != Enclosure.Ring)
                p.Rules.Fight = rules with { Enclosure = Enclosure.Ring };
        News.Post(world, date, NewsCategory.General, $"{country.Name} becomes the sport's second home",
            $"Arenas, entrances and a crowd that bows at the final bell: the sport now has two centres. Its houses in {country.Name} keep their own rules, and their nights will draw as nowhere else.",
            default);
    }
}
