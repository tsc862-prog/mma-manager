using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MmaManager.Core.Model;

namespace MmaManager.Wpf.Art;

/// <summary>
/// Assigns each fighter a photographic portrait from the bundled face pool
/// (AI-generated fictional people — see Assets/Faces/ATTRIBUTION.txt). Faces are
/// picked deterministically by fighter id, bucketed by nationality → apparent
/// ethnicity, and reused across the roster (as older sports sims do). Falls back to
/// the drawn vector portrait if the pool is missing.
///
/// A "face" is an <see cref="Identity"/> — one fictional person that can carry several
/// life-stage images (young / prime / veteran) so the portrait ages with the fighter,
/// and an optional natural-weight range (lbs) so e.g. a heavyweight-built face is only
/// assigned to fighters created in that weight range. Filenames drive all of this, with an
/// optional leading sex token (m_/f_, absent = male):
///   {eth}_{id}_{stage}             general face, ages by stage          (white_00_y.png)
///   {eth}_{id}_{min}_{max}_{stage} as above but locked to a lbs range   (white_02_206_x_y.png)
///   f_{eth}_{id}_{stage}           a female face                        (f_white_00_y.png)
///   anything else                  a single-image face, no aging/lock   (legacy *.jpg)
/// where stage ∈ {y,p,v}; min/max are pounds, and 'x' means open-ended
/// (206_x = 206lbs and up, x_150 = up to 150lbs). Sex is a hard filter — a fighter is only
/// ever assigned a face of their own sex (else the drawn fallback).
/// </summary>
public static class FaceLibrary
{
    private sealed class Identity
    {
        public string Key = "";                                     // deterministic sort key
        public string Ethnicity = "";
        public char Sex = 'm';                                      // 'm' / 'f'
        public int? WeightMin;                                      // null = open (no lower bound)
        public int? WeightMax;                                      // null = open (no upper bound)
        public string? Single;                                      // legacy one-image face
        public readonly Dictionary<char, string> Stages = new();    // 'y'/'p'/'v' -> path
    }

    private static readonly Dictionary<string, Identity> Identities = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ImageSource> ImageCache = new();
    private static readonly Dictionary<(int id, char stage), ImageSource?> FighterCache = new();
    private static readonly object Gate = new();
    private static bool _loaded;

    public static bool HasFaces
    {
        get { Load(); return Identities.Count > 0; }
    }

    /// <summary>The fighter's assigned photo for their current age, or null if no pool is bundled.</summary>
    public static ImageSource? For(Fighter f, int age)
    {
        Load();
        if (Identities.Count == 0) return null;

        var stage = StageFor(age);
        var cacheKey = (f.Id.Value, stage);
        if (FighterCache.TryGetValue(cacheKey, out var cached)) return cached;

        var eths = Buckets(f.Nationality);
        var lbs = f.NaturalWeightLbs;
        var sex = f.Sex == Sex.Female ? 'f' : 'm';

        // Sex is a hard constraint — never assign a man's photo to a woman or vice versa.
        // With no faces of the fighter's sex, fall through to the drawn vector portrait.
        var mine = Identities.Values.Where(i => i.Sex == sex).ToList();
        if (mine.Count == 0) { FighterCache[cacheKey] = null; return null; }

        // Within that sex: prefer ethnicity AND natural-weight match (if the face is range-
        // locked); relax ethnicity before weight — reusing a right-sized face beats a
        // wrong-sized one — and drop weight only when nothing of this sex fits it.
        var pool = mine.Where(i => eths.Contains(i.Ethnicity) && MatchesWeight(i, lbs)).ToList();
        if (pool.Count == 0)
            pool = mine.Where(i => MatchesWeight(i, lbs)).ToList();
        if (pool.Count == 0)
            pool = mine.Where(i => eths.Contains(i.Ethnicity)).ToList();
        if (pool.Count == 0)
            pool = mine;
        pool = PreferSpecific(pool, lbs);

        ImageSource? img = null;
        if (pool.Count > 0)
        {
            pool.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            var chosen = pool[(int)((uint)f.Id.Value % (uint)pool.Count)];
            var path = Resolve(chosen, stage);
            if (path is not null) img = LoadImage(path);
        }
        FighterCache[cacheKey] = img;
        return img;
    }

    // Age -> life stage. Bands: 18-26 young, 27-37 prime, 38+ veteran.
    private static char StageFor(int age) => age < 27 ? 'y' : age <= 37 ? 'p' : 'v';

    // A face with no range matches everyone; a range-locked face matches only fighters
    // whose natural weight falls inside it (null bound = open on that side).
    private static bool MatchesWeight(Identity i, int lbs) =>
        (i.WeightMin is null || lbs >= i.WeightMin) && (i.WeightMax is null || lbs <= i.WeightMax);

    // Width buckets: class-band art, half-range art, open-ended. Mirrors
    // PaintedLibrary/IdentityCatalog — keep the values in step.
    private const int SpecificWidth = 40;
    private const int HalfRangeWidth = 80;
    private const int MinPool = 6;

    private static int BandWidth(Identity i) => (i.WeightMax ?? 265) - (i.WeightMin ?? 106);

    // Of the faces that fit, prefer the most weight-specific range — but never
    // shrink the choice below MinPool faces. Pools with nothing in range (the
    // ethnicity-only / anyone fallbacks) pass through untouched.
    private static List<Identity> PreferSpecific(List<Identity> pool, int lbs)
    {
        var inRange = pool.Where(i => MatchesWeight(i, lbs)).ToList();
        if (inRange.Count == 0) return pool;
        foreach (int cap in new[] { SpecificWidth, HalfRangeWidth })
        {
            var tight = inRange.Where(i => BandWidth(i) <= cap).ToList();
            if (tight.Count >= MinPool) return tight;
        }
        return inRange;
    }

    // A weight bound: a pounds value, or 'x' (and unparseable) meaning open-ended.
    private static int? ParseBound(string s) =>
        int.TryParse(s, out var v) ? v : null;

    // Requested stage if present, else the nearest available one.
    private static string? Resolve(Identity id, char stage)
    {
        if (id.Single is not null) return id.Single;
        foreach (var s in Nearest(stage))
            if (id.Stages.TryGetValue(s, out var p)) return p;
        return null;
    }

    private static char[] Nearest(char stage) => stage switch
    {
        'y' => new[] { 'y', 'p', 'v' },
        'v' => new[] { 'v', 'p', 'y' },
        _   => new[] { 'p', 'y', 'v' },
    };

    // Which ethnicity buckets a region draws from (melting-pot regions mix).
    private static string[] Buckets(Region region) => region switch
    {
        Region.Africa => new[] { "black" },
        Region.Asia => new[] { "asian" },
        Region.Europe => new[] { "white" },
        Region.Oceania => new[] { "white" },
        Region.MiddleEast => new[] { "white" },
        Region.SouthAmerica => new[] { "white", "black" },
        _ => new[] { "white", "black", "asian" },   // North America
    };

    private static void Load()
    {
        if (_loaded) return;
        lock (Gate)
        {
            if (_loaded) return;
            var dir = Path.Combine(AppContext.BaseDirectory, "Assets", "Faces");
            if (Directory.Exists(dir))
                foreach (var path in Directory.EnumerateFiles(dir, "*.*")
                             .Where(p => p.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                                      || p.EndsWith(".png", StringComparison.OrdinalIgnoreCase)))
                    Register(path);
            _loaded = true;
        }
    }

    // Parse a filename into the identity pool (see class summary for the shapes).
    private static void Register(string path)
    {
        var all = Path.GetFileNameWithoutExtension(path).Split('_');
        if (all.Length == 0) return;

        // optional leading sex token (m_/f_); absent => male
        char sex = 'm';
        var tokens = all;
        if (all[0] is "m" or "f") { sex = all[0][0]; tokens = all[1..]; }
        if (tokens.Length == 0) return;

        var eth = tokens[0];
        var last = tokens[^1];
        bool staged = tokens.Length >= 3 && last.Length == 1 && "ypv".Contains(last[0]);

        if (staged)
        {
            var stage = last[0];
            var key = $"{sex}_{eth}_{tokens[1]}";
            if (!Identities.TryGetValue(key, out var identity))
                Identities[key] = identity = new Identity { Key = key, Ethnicity = eth, Sex = sex };
            // optional weight lock: {eth}_{id}_{min}_{max}_{stage}, each bound a lbs value or 'x' (open)
            if (tokens.Length == 5)
            {
                identity.WeightMin = ParseBound(tokens[2]);
                identity.WeightMax = ParseBound(tokens[3]);
            }
            identity.Stages[stage] = path;
        }
        else
        {
            // legacy / non-staged file: its own single-image, unrestricted face
            var key = "single_" + Path.GetFileNameWithoutExtension(path);
            Identities[key] = new Identity { Key = key, Ethnicity = eth, Sex = sex, Single = path };
        }
    }

    private static ImageSource LoadImage(string path)
    {
        if (ImageCache.TryGetValue(path, out var img)) return img;
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;      // load now so the file isn't locked
        bmp.UriSource = new Uri(path);
        bmp.EndInit();
        bmp.Freeze();
        ImageCache[path] = bmp;
        return bmp;
    }
}
