using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MmaManager.Core.Model;

namespace MmaManager.Wpf.Art;

/// <summary>
/// The AI-painted fighter pack (Assets/Painted): one fictional person per identity,
/// rendered at up to three life stages (young / prime / veteran) with a matching face
/// shot and full-body shot per stage. Metadata comes from manifest.json (written by
/// the FighterArt project, E:/FighterArt/sync/syncassets.py): sex, ethnicity bucket, natural-weight band, fighting
/// style (null = wildcard), tags (glam/menace/...), and WorldAtlas culture keys.
/// Image filenames are opaque pointers. Packs without a manifest fall back to the
/// legacy token-encoded filename format ({sex}_{eth}_{id}_{min}_{max}_{style}_{stage}_{face|body}.png).
///
/// Assignment is deterministic by fighter id. Sex is a hard filter; when the world
/// is attached, the fighter's COUNTRY culture picks the ethnicity buckets (a Samoan
/// draws from the poly pool, an Iranian from mena) with culture-exact identities
/// preferred; otherwise the coarse continental Region decides. The pool then prefers
/// weight + exact fighting style, relaxing style to its cluster, then any style,
/// then ethnicity — weight is only dropped when no identity of the fighter's sex
/// covers it (a right-sized portrait is reused before a wrong-sized one is ever
/// picked), so a fighter always gets *someone* of their own sex before falling
/// back to drawn art.
/// </summary>
public static class PaintedLibrary
{
    private sealed class Identity
    {
        public string Key = "";
        public string Ethnicity = "";
        public char Sex = 'm';
        public int? WeightMin;
        public int? WeightMax;
        public string Style = "wl";
        public string[] Cultures = Array.Empty<string>();
        public readonly Dictionary<(char stage, bool body), string> Images = new();
    }

    private static readonly Dictionary<string, Identity> Identities = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Tags = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, ImageSource> ImageCache = new();
    private static readonly Dictionary<(int id, char stage, bool body), ImageSource?> FighterCache = new();
    private static readonly Dictionary<int, string?> PersonaCache = new();
    private static readonly object Gate = new();
    private static bool _loaded;

    private static readonly string[] GrappleCluster = { "wr", "gr", "bjj" };
    private static readonly string[] StrikeCluster = { "st", "br", "kb", "cl" };

    public static bool HasFaces
    {
        get { Load(); return Identities.Count > 0; }
    }

    /// <summary>
    /// The persona tag baked into the fighter's assigned portrait identity
    /// (glam · adonis · menace · inked · babyface), or null. Deterministic —
    /// same assignment the face/body lookups use.
    /// </summary>
    public static string? PersonaFor(Fighter f)
    {
        Load();
        if (Tags.Count == 0 || Identities.Count == 0) return null;
        if (PersonaCache.TryGetValue(f.Id.Value, out var cached)) return cached;
        var tag = Choose(f) is { } id && Tags.TryGetValue(id.Key, out var t) ? t : null;
        PersonaCache[f.Id.Value] = tag;
        return tag;
    }

    /// <summary>Whether the fighter's assigned identity is rendered at all three life stages (diagnostics).</summary>
    public static bool HasTriptychFor(Fighter f)
    {
        Load();
        return Choose(f) is { } i && HasAllStages(i);
    }

    /// <summary>The fighter's painted head shot for their current age, or null if none matches.</summary>
    public static ImageSource? Face(Fighter f, int age) => Image(f, age, body: false);

    /// <summary>The matching full-body shot (same person as the face), or null.</summary>
    public static ImageSource? Body(Fighter f, int age) => Image(f, age, body: true);

    /// <summary>
    /// The file behind the fighter's head shot for their current age (same choice as
    /// <see cref="Face(Fighter,int)"/>), or null — so a caller can decode it off the UI thread.
    /// </summary>
    public static string? FacePath(Fighter f, int age)
    {
        Load();
        if (Identities.Count == 0) return null;
        return Choose(f) is { } chosen ? Resolve(chosen, StageFor(age), false) : null;
    }

    /// <summary>
    /// Decode a portrait file at a small pixel width, frozen. Touches no shared state,
    /// so it is safe on a worker thread (card walls decode their faces in parallel).
    /// </summary>
    public static ImageSource DecodeThumb(string path, int decodeWidth)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.DecodePixelWidth = decodeWidth;
        bmp.UriSource = new Uri(path);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private static ImageSource? Image(Fighter f, int age, bool body)
    {
        Load();
        if (Identities.Count == 0) return null;

        var stage = StageFor(age);
        var cacheKey = (f.Id.Value, stage, body);
        if (FighterCache.TryGetValue(cacheKey, out var cached)) return cached;

        var chosen = Choose(f);
        ImageSource? img = null;
        if (chosen is not null)
        {
            var path = Resolve(chosen, stage, body);
            if (path is not null) img = LoadImage(path);
        }
        FighterCache[cacheKey] = img;
        return img;
    }

    private static World? _world;
    private static Dictionary<int, string>? _countryCodes;

    /// <summary>
    /// Give the library the loaded world so portrait choice can use the fighter's
    /// COUNTRY culture (new geography: a Samoan draws poly identities, an Iranian
    /// mena) instead of only the coarse continental region. Optional — everything
    /// degrades to region buckets when never called.
    /// </summary>
    public static void AttachWorld(World w)
    {
        if (ReferenceEquals(_world, w)) return;
        lock (Gate)
        {
            _world = w;
            _countryCodes = w.Countries.Values.ToDictionary(c => c.Id.Value, c => c.Code);
            FighterCache.Clear();
            PersonaCache.Clear();
        }
    }

    private static string? CultureOf(Fighter f) =>
        _countryCodes is not null && _countryCodes.TryGetValue(f.Country.Value, out var code)
            ? Core.Sim.Geography.WorldAtlas.ByCode(code)?.Culture
            : null;

    // Culture -> ethnicity buckets. Cultures not listed (anglo, european ones,
    // melting pots) fall through to the continental region mapping.
    private static readonly Dictionary<string, string[]> CultureBuckets = new(StringComparer.Ordinal)
    {
        ["arabic"] = new[] { "mena" },
        ["persian"] = new[] { "mena" },
        ["turkic"] = new[] { "mena" },
        ["israeli"] = new[] { "mena", "white" },
        ["polynesian"] = new[] { "poly" },
        ["centralasian"] = new[] { "asian" },
        ["mongolian"] = new[] { "asian" },
        ["japanese"] = new[] { "asian" },
        ["korean"] = new[] { "asian" },
        ["chinese"] = new[] { "asian" },
        ["thai"] = new[] { "asian" },
        ["vietnamese"] = new[] { "asian" },
        ["filipino"] = new[] { "asian" },
        ["indonesian"] = new[] { "asian" },
        ["indian"] = new[] { "asian" },
        ["latino"] = new[] { "latin" },
        ["brazilian"] = new[] { "latin", "black" },
        ["westafrican"] = new[] { "black" },
        ["eastafrican"] = new[] { "black" },
        ["centralafrican"] = new[] { "black" },
        ["southernafrican"] = new[] { "black", "white" },
    };

    private static Identity? Choose(Fighter f)
    {
        var sex = f.Sex == Sex.Female ? 'f' : 'm';
        var mine = Identities.Values.Where(i => i.Sex == sex).ToList();
        if (mine.Count == 0) return null;

        var culture = CultureOf(f);
        var eths = culture is not null && CultureBuckets.TryGetValue(culture, out var cb)
            ? cb
            : Buckets(f.Nationality);
        var lbs = f.NaturalWeightLbs;
        var style = StyleToken(f.PrimaryStyle);
        var cluster = ClusterOf(style);

        // Designed headliners prefer identities rendered at all three life stages
        // (the triptych packs) so the world's stars visibly age — but only when a
        // triptych of the RIGHT LOOK exists. An Irish champion in a Thai body ages
        // worse than a single-stage Irishman doesn't.
        if (f.Designed)
        {
            var trips = mine.Where(i => HasAllStages(i)
                && eths.Contains(i.Ethnicity) && MatchesWeight(i, lbs)).ToList();
            if (trips.Count > 0) mine = trips;
        }

        // Tightest match first: culture-exact identities (a Dagestani prefers a
        // Dagestani-featured portrait), then ethnicity + weight + exact style,
        // relax style to cluster, any style, then ethnicity. Weight is held as
        // long as anything in-range exists — reusing a right-sized portrait
        // beats handing a heavyweight a flyweight's body — and is only dropped
        // when no identity of this sex covers the weight at all.
        var tiers = new Func<Identity, bool>[]
        {
            i => culture is not null && i.Cultures.Contains(culture) && MatchesWeight(i, lbs) && i.Style == style,
            i => culture is not null && i.Cultures.Contains(culture) && MatchesWeight(i, lbs) && cluster.Contains(i.Style),
            i => eths.Contains(i.Ethnicity) && MatchesWeight(i, lbs) && i.Style == style,
            i => eths.Contains(i.Ethnicity) && MatchesWeight(i, lbs) && cluster.Contains(i.Style),
            i => eths.Contains(i.Ethnicity) && MatchesWeight(i, lbs),
            i => MatchesWeight(i, lbs),
            i => eths.Contains(i.Ethnicity),
            _ => true,
        };

        foreach (var fits in tiers)
        {
            var pool = mine.Where(fits).ToList();
            if (pool.Count == 0) continue;
            pool = PreferSpecific(pool, lbs);
            pool.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            return pool[(int)((uint)f.Id.Value % (uint)pool.Count)];
        }
        return null;
    }

    // Width buckets: class-band art, half-range art (≤170 / 206+), open-ended.
    // Mirrors IdentityCatalog — keep the values in step.
    private const int SpecificWidth = 40;
    private const int HalfRangeWidth = 80;
    private const int MinPool = 6;

    private static int BandWidth(Identity i) => (i.WeightMax ?? 265) - (i.WeightMin ?? 106);

    /// <summary>
    /// Of the identities that fit, prefer the most weight-SPECIFIC band (a 250 lbs
    /// heavyweight takes a 206-265 body over a generic 171-and-up one) — but never
    /// shrink the choice below <see cref="MinPool"/> faces, so a division with only
    /// a couple of precise images doesn't put the same face on everyone. Tiers that
    /// don't constrain weight (nothing in range at all) pass through untouched.
    /// </summary>
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

    private static bool HasAllStages(Identity i) =>
        i.Images.ContainsKey(('y', false)) && i.Images.ContainsKey(('p', false)) && i.Images.ContainsKey(('v', false));

    /// <summary>
    /// The art pack as a portrait-first generation catalog: every identity that
    /// carries a name culture becomes a spec the sim can build a fighter to fit,
    /// so generated fighters always have an exact portrait. Null if no pack.
    /// </summary>
    public static Core.Sim.Generation.IdentityCatalog? BuildCatalog()
    {
        Load();
        if (Identities.Count == 0) return null;

        var specs = new List<Core.Sim.Generation.IdentitySpec>();
        foreach (var id in Identities.Values)
        {
            var culture = id.Cultures.FirstOrDefault();
            if (string.IsNullOrEmpty(culture)) continue;   // need a culture to drive name + nationality
            specs.Add(new Core.Sim.Generation.IdentitySpec(
                id.Sex == 'f' ? Sex.Female : Sex.Male,
                culture,
                id.WeightMin ?? 106,
                id.WeightMax ?? 265,
                StyleFromCode(id.Style),
                HasAllStages(id)));
        }
        return specs.Count > 0 ? new Core.Sim.Generation.IdentityCatalog(specs) : null;
    }

    private static PrimaryStyle? StyleFromCode(string code) => code switch
    {
        "wr" => PrimaryStyle.Wrestler,
        "gr" => PrimaryStyle.Grappler,
        "bjj" => PrimaryStyle.BrazilianJiuJitsu,
        "st" => PrimaryStyle.Striker,
        "kb" => PrimaryStyle.Kickboxer,
        "br" => PrimaryStyle.Brawler,
        "cl" => PrimaryStyle.ClinchFighter,
        "wl" => PrimaryStyle.WellRounded,
        _ => null,   // "x" wildcard / unknown — the generator rolls the style
    };

    // Age -> life stage. Pack bands: 18-25 young, 26-35 prime, 36+ veteran
    // (the identities are painted at ~21 / ~30 / ~40).
    private static char StageFor(int age) => age <= 25 ? 'y' : age <= 35 ? 'p' : 'v';

    private static string StyleToken(PrimaryStyle s) => s switch
    {
        PrimaryStyle.Striker => "st",
        PrimaryStyle.Kickboxer => "kb",
        PrimaryStyle.Wrestler => "wr",
        PrimaryStyle.BrazilianJiuJitsu => "bjj",
        PrimaryStyle.Grappler => "gr",
        PrimaryStyle.Brawler => "br",
        PrimaryStyle.ClinchFighter => "cl",
        _ => "wl",
    };

    private static string[] ClusterOf(string style) =>
        GrappleCluster.Contains(style) ? GrappleCluster :
        StrikeCluster.Contains(style) ? StrikeCluster :
        new[] { "wl" };

    private static bool MatchesWeight(Identity i, int lbs) =>
        (i.WeightMin is null || lbs >= i.WeightMin) && (i.WeightMax is null || lbs <= i.WeightMax);

    private static int? ParseBound(string s) => int.TryParse(s, out var v) ? v : null;

    // Requested stage if present, else the nearest available one.
    private static string? Resolve(Identity id, char stage, bool body)
    {
        foreach (var s in Nearest(stage))
            if (id.Images.TryGetValue((s, body), out var p)) return p;
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
        Region.Africa => new[] { "black", "mena" },          // North Africa is arabic-culture
        Region.Asia => new[] { "asian" },
        Region.Europe => new[] { "white" },
        Region.Oceania => new[] { "white", "poly" },
        Region.MiddleEast => new[] { "mena", "white" },
        Region.SouthAmerica => new[] { "latin" },
        _ => new[] { "white", "black", "asian", "latin" },   // North America
    };

    private static void Load()
    {
        if (_loaded) return;
        lock (Gate)
        {
            if (_loaded) return;
            var dir = Path.Combine(AppContext.BaseDirectory, "Assets", "Painted");
            if (Directory.Exists(dir))
            {
                if (!LoadManifest(dir))
                {
                    // legacy bundle: metadata token-encoded in the filenames
                    foreach (var path in Directory.EnumerateFiles(dir, "*.png"))
                        Register(path);
                    LoadTags(Path.Combine(dir, "tags.json"));
                }
            }
            _loaded = true;
        }
    }

    // manifest.json (version 1): the metadata source of truth, written by
    // E:/FighterArt/sync/syncassets.py. Image filenames are opaque pointers relative
    // to the same folder. Returns false (caller falls back to filename parsing)
    // when absent or unreadable.
    private static bool LoadManifest(string dir)
    {
        var path = Path.Combine(dir, "manifest.json");
        if (!File.Exists(path)) return false;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("fighters", out var fighters)) return false;
            foreach (var fp in fighters.EnumerateObject())
            {
                var e = fp.Value;
                var id = new Identity
                {
                    Key = fp.Name,
                    Sex = e.GetProperty("sex").GetString() == "f" ? 'f' : 'm',
                    Ethnicity = e.GetProperty("eth").GetString() ?? "",
                    Style = e.TryGetProperty("style", out var st) && st.ValueKind == System.Text.Json.JsonValueKind.String
                        ? st.GetString()! : "x",              // null style = wildcard
                };
                if (e.TryGetProperty("weight", out var w))
                {
                    if (w.TryGetProperty("min", out var lo) && lo.ValueKind == System.Text.Json.JsonValueKind.Number)
                        id.WeightMin = lo.GetInt32();
                    if (w.TryGetProperty("max", out var hi) && hi.ValueKind == System.Text.Json.JsonValueKind.Number)
                        id.WeightMax = hi.GetInt32();
                }
                if (e.TryGetProperty("cultures", out var cu) && cu.ValueKind == System.Text.Json.JsonValueKind.Array)
                    id.Cultures = cu.EnumerateArray()
                        .Select(x => x.GetString() ?? "").Where(s => s.Length > 0).ToArray();
                if (e.TryGetProperty("images", out var imgs) && imgs.ValueKind == System.Text.Json.JsonValueKind.Object)
                    foreach (var stage in imgs.EnumerateObject())
                    {
                        if (stage.Name.Length != 1 || !"ypv".Contains(stage.Name[0])) continue;
                        foreach (var kind in stage.Value.EnumerateObject())
                        {
                            if (kind.Name is not ("face" or "body")) continue;
                            var file = Path.Combine(dir, kind.Value.GetString() ?? "");
                            if (File.Exists(file))
                                id.Images[(stage.Name[0], kind.Name == "body")] = file;
                        }
                    }
                if (id.Images.Count == 0) continue;           // never offer an imageless identity
                Identities[id.Key] = id;
                if (e.TryGetProperty("tags", out var tg) && tg.ValueKind == System.Text.Json.JsonValueKind.Array
                    && tg.GetArrayLength() > 0)
                    Tags[id.Key] = tg[0].GetString() ?? "";
            }
            return Identities.Count > 0;
        }
        catch (Exception)
        {
            Identities.Clear();
            Tags.Clear();
            return false;   // corrupt manifest -> legacy filename parsing
        }
    }

    // tags.json: identity base -> ["tag"], shipped by syncassets. A fighter has
    // at most one tag; a malformed file just means no personas.
    private static void LoadTags(string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            var map = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(path));
            if (map is null) return;
            foreach (var (key, tags) in map)
                if (tags is { Length: > 0 })
                    Tags[key] = tags[0];
        }
        catch (Exception) { /* art metadata is optional — never take the app down */ }
    }

    // {sex}_{eth}_{id}_{min}_{max}_{style}_{stage}_{face|body} — exactly 8 tokens;
    // anything else is ignored so stray files can't corrupt the pool.
    private static void Register(string path)
    {
        var t = Path.GetFileNameWithoutExtension(path).Split('_');
        if (t.Length != 8) return;
        if (t[0] is not ("m" or "f")) return;
        if (t[6].Length != 1 || !"ypv".Contains(t[6][0])) return;
        if (t[7] is not ("face" or "body")) return;

        var key = $"{t[0]}_{t[1]}_{t[2]}_{t[3]}_{t[4]}_{t[5]}";
        if (!Identities.TryGetValue(key, out var identity))
            Identities[key] = identity = new Identity
            {
                Key = key,
                Sex = t[0][0],
                Ethnicity = t[1],
                WeightMin = ParseBound(t[3]),
                WeightMax = ParseBound(t[4]),
                Style = t[5],
            };
        identity.Images[(t[6][0], t[7] == "body")] = path;
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
