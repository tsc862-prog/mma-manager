using System.Windows.Media;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Geography;

namespace MmaManager.Wpf.ViewModels;

/// <summary>
/// Fame and drawing power in words and colour: the ONE place the app turns popularity and
/// city fame into something read at a glance. Popularity and city fame are public facts
/// (fog of war never hides who sells tickets), so anything here may be shown for anyone.
/// Every number is read straight off the sim (<see cref="Fighter.Intangibles"/> and
/// <see cref="Reputation"/>); nothing is computed in parallel.
/// </summary>
public static class Fame
{
    // --- Drawing power (a fighter's one popularity number) ---------------------------------

    /// <summary>Popularity in words (who sells tickets) - the fight sheet's vocabulary.</summary>
    public static string DrawWord(int pop) => pop switch
    {
        >= 75 => "a star",
        >= 60 => "draws a crowd",
        >= 45 => "a known name",
        >= 30 => "a following",
        _ => "little draw yet",
    };

    /// <summary>Filled bars on the draw meter (0-5), one per draw word above the floor.</summary>
    public static int DrawLevel(int pop) => pop switch
    {
        >= 75 => 5,
        >= 60 => 4,
        >= 45 => 3,
        >= 30 => 2,
        >= 15 => 1,
        _ => 0,
    };

    /// <summary>Below a following there is nothing to shout about: cards stay quiet.</summary>
    public const int DrawShownFrom = 30;

    // --- Fame in a place (a city, or a region/country rolled up by the sim) ---------------

    /// <summary>How well known a name is in one place.</summary>
    public static string PlaceWord(int fame) => fame switch
    {
        >= 70 => "a household name",
        >= 45 => "popular",
        >= 25 => "known",
        >= 10 => "a few fans",
        > 0 => "barely heard of",
        _ => "unknown",
    };

    /// <summary>The legend's stops: (fame, word) from cold to hot.</summary>
    public static readonly (int Fame, string Word)[] LegendStops =
    {
        (0, "unknown"), (5, "heard of"), (18, "a few fans"), (35, "known"), (55, "popular"), (80, "household name"),
    };

    // Ember ramp: grey (nobody) -> dark ember -> ember -> orange -> gold glow.
    private static readonly Color[] Ramp =
    {
        Color.FromRgb(0x4A, 0x31, 0x26),
        Color.FromRgb(0x8C, 0x42, 0x24),
        Color.FromRgb(0xC9, 0x63, 0x2A),
        Color.FromRgb(0xF2, 0xB3, 0x3D),
    };
    private static readonly Color Nobody = Color.FromRgb(0x2A, 0x2F, 0x37);
    private static readonly Dictionary<int, Brush> HeatCache = new();

    /// <summary>
    /// Where a fame number sits on the heat ramp (0..1). Bent so the low end is visible: most
    /// names are a few points deep in most places, and a flat ramp drew that as nothing.
    /// </summary>
    public static double HeatT(int fame) => fame <= 0 ? 0 : Math.Pow(Math.Clamp(fame / 75.0, 0, 1), 0.6);

    /// <summary>The fame heat colour (frozen, cached per value).</summary>
    public static Brush Heat(int fame)
    {
        fame = Math.Clamp(fame, 0, 100);
        if (HeatCache.TryGetValue(fame, out var hit)) return hit;
        Color c;
        if (fame <= 0) c = Nobody;
        else
        {
            double t = HeatT(fame) * (Ramp.Length - 1);
            int i = Math.Min((int)t, Ramp.Length - 2);
            double u = t - i;
            byte L(byte a, byte b) => (byte)Math.Round(a + (b - a) * u);
            c = Color.FromRgb(L(Ramp[i].R, Ramp[i + 1].R), L(Ramp[i].G, Ramp[i + 1].G), L(Ramp[i].B, Ramp[i + 1].B));
        }
        var brush = new SolidColorBrush(c);
        brush.Freeze();
        return HeatCache[fame] = brush;
    }

    private static readonly Brush DarkInk = Frozen(Color.FromRgb(0x1D, 0x21, 0x27));
    private static readonly Brush LightInk = Frozen(Color.FromRgb(0xF4, 0xF5, 0xF6));
    private static readonly Brush DimInk = Frozen(Color.FromRgb(0x9A, 0xA4, 0xAF));

    /// <summary>Readable ink on a heat colour (dark on the gold end, light elsewhere, dim on nobody).</summary>
    public static Brush InkOn(int fame) => fame <= 0 ? DimInk : HeatT(fame) >= 0.82 ? DarkInk : LightInk;

    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    // --- Reading a fighter's or a promotion's map -------------------------------------------

    /// <summary>The cities where a name is known, hottest first (ties by city id, so the order is stable).</summary>
    public static List<(City City, int Fame)> KnownCities(World w, Fighter f) =>
        w.Cities.Values.Select(c => (c, Reputation.FighterInCity(f, c.Id)))
            .Where(t => t.Item2 > 0).OrderByDescending(t => t.Item2).ThenBy(t => t.c.Id.Value).ToList();

    public static List<(City City, int Fame)> KnownCities(World w, Promotion p) =>
        w.Cities.Values.Select(c => (c, Reputation.OrgInCity(p, c.Id)))
            .Where(t => t.Item2 > 0).OrderByDescending(t => t.Item2).ThenBy(t => t.c.Id.Value).ToList();

    /// <summary>"Tbilisi, Georgia" (the country only when there is one on file).</summary>
    public static string CityLabel(World w, City c) => w.Get(c.Country) is { } k ? $"{c.Name}, {k.Name}" : c.Name;

    /// <summary>
    /// One line on where a name carries: "Best known in Lagos, Nigeria (a household name, 100) · known in 2 more
    /// cities · a few fans in 6 more, across 2 regions". Empty when the name is known nowhere (the caller
    /// says that in its own words).
    /// </summary>
    public static string ReachLine(World w, List<(City City, int Fame)> known)
    {
        if (known.Count == 0) return "";
        var (top, fame) = known[0];
        var rest = known.Skip(1).ToList();
        int knownMore = rest.Count(k => k.Fame >= 25), fansMore = rest.Count(k => k.Fame is >= 10 and < 25), heardMore = rest.Count(k => k.Fame < 10);
        int regions = known.Where(k => k.Fame >= 10).Select(k => k.City.Region).Distinct().Count();
        if (fame < 10)
            return $"Barely heard of yet: a little word around {CityLabel(w, top)} ({fame})"
                   + (rest.Count > 0 ? $" and {(rest.Count == 1 ? "1 other city" : $"{rest.Count} other cities")}" : "");
        var parts = new List<string> { $"Best known in {CityLabel(w, top)} ({PlaceWord(fame)}, {fame})" };
        string Cities(int n) => n == 1 ? "1 more city" : $"{n} more cities";
        if (knownMore > 0) parts.Add($"known in {Cities(knownMore)}");
        if (fansMore > 0) parts.Add($"a few fans in {(knownMore > 0 ? $"{fansMore} more" : Cities(fansMore))}");
        if (heardMore > 0) parts.Add($"heard of in {(knownMore + fansMore > 0 ? $"{heardMore} more" : Cities(heardMore))}");
        return string.Join(" · ", parts) + (regions > 1 ? $", across {regions} regions" : "");
    }

    /// <summary>The human line for a fighter nobody has heard of yet (his/her by sex).</summary>
    public static string NobodyLine(Fighter f) =>
        $"Nobody outside {(f.Sex == Sex.Female ? "her" : "his")} gym knows the name yet. Wins where people are watching are how a name travels.";

    /// <summary>The human line for a promotion nobody has heard of yet.</summary>
    public static string NobodyLine(Promotion p) =>
        $"Nobody knows {p.Abbreviation} yet. Put on a card in a city and the word starts there; every show grows the name a little.";

    public static string PrettyRegion(Region r) => r switch
    {
        Region.NorthAmerica => "North America",
        Region.SouthAmerica => "South America",
        Region.MiddleEast => "Middle East",
        _ => r.ToString(),
    };
}
