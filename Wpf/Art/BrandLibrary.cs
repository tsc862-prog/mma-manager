using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MmaManager.Wpf.Art;

/// <summary>Which family of brand a logo belongs to.</summary>
public enum BrandKind
{
    /// <summary>Search every family (names are unique across them).</summary>
    Any,
    /// <summary>MediaService.Ladder partners (broadcasters, streamers, PPV carriers).</summary>
    Media,
    /// <summary>Sponsorships archetype partners (beer, bank, airline...).</summary>
    Sponsor,
    /// <summary>Origins-era media partners (OriginsBroadcasters.Media).</summary>
    OriginsMedia,
    /// <summary>Origins-era sponsors (OriginsBroadcasters.Sponsors).</summary>
    OriginsSponsor,
}

/// <summary>Manifest row for a logo. Category is the rung / archetype, Tag the partner's personality tag.</summary>
public sealed record BrandArt(string Name, BrandKind Kind, string File, string Category, string? Tag);

/// <summary>
/// Flat code-drawn logos for the media partners and sponsors the game names, produced by the FighterArt
/// pipeline (E:/FighterArt corporate/brandlogos.py, sync/synccorporate.py) into
/// <c>Assets\Brands\{media,sponsors,origins-media,origins-sponsors}</c>: 512x512 transparent PNGs plus a
/// manifest per folder, keyed by the EXACT partner name the game spells (MediaService / Sponsorships /
/// OriginsBroadcasters). Org logos are a different thing (<see cref="LogoFactory"/>).
///
/// Never throws and never returns null from <see cref="Logo"/>: an unknown name or a missing folder gets a small
/// wordmark tile (the name on a muted rounded square) so the game looks right with no pack at all. Loaded once,
/// frozen and cached.
/// </summary>
public static class BrandLibrary
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "Assets", "Brands");
    private static readonly (BrandKind kind, string dir)[] Folders =
    {
        (BrandKind.Media, "media"), (BrandKind.Sponsor, "sponsors"),
        (BrandKind.OriginsMedia, "origins-media"), (BrandKind.OriginsSponsor, "origins-sponsors"),
    };
    private static readonly object Gate = new();
    private static Dictionary<string, BrandArt>? _brands;
    private static readonly ConcurrentDictionary<string, ImageSource?> Files = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<(string, int), ImageSource> Fallbacks = new();

    /// <summary>Every name that has a real logo, optionally limited to one family.</summary>
    public static IReadOnlyList<string> Names(BrandKind kind = BrandKind.Any)
    {
        Load();
        return _brands!.Values.Where(b => kind == BrandKind.Any || b.Kind == kind).Select(b => b.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    /// <summary>True when a real logo file exists for this exact name (and family, when given).</summary>
    public static bool Has(string? name, BrandKind kind = BrandKind.Any) => Find(name, kind) is not null;

    /// <summary>The manifest row (category, tag, family), or null.</summary>
    public static BrandArt? Info(string? name, BrandKind kind = BrandKind.Any) => Find(name, kind);

    /// <summary>The real logo, or null when there is none.</summary>
    public static ImageSource? LogoOrNull(string? name, BrandKind kind = BrandKind.Any)
    {
        var b = Find(name, kind);
        return b is null ? null : Files.GetOrAdd(b.Name, _ => LoadFile(PathOf(b)));
    }

    /// <summary>The logo for <paramref name="name"/>, or a wordmark tile at <paramref name="fallbackSize"/> px. Never null.</summary>
    public static ImageSource Logo(string? name, BrandKind kind = BrandKind.Any, int fallbackSize = 256) =>
        LogoOrNull(name, kind) ?? Fallbacks.GetOrAdd((name ?? "", fallbackSize), k => Wordmark(k.Item1, k.Item2));

    private static BrandArt? Find(string? name, BrandKind kind)
    {
        Load();
        if (name is null || !_brands!.TryGetValue(name, out var b)) return null;
        if (kind != BrandKind.Any && b.Kind != kind) return null;
        return File.Exists(PathOf(b)) ? b : null;
    }

    private static string PathOf(BrandArt b) =>
        Path.Combine(Root, Folders.First(f => f.kind == b.Kind).dir, b.File);

    private static void Load()
    {
        if (_brands is not null) return;
        lock (Gate)
        {
            if (_brands is not null) return;
            var map = new Dictionary<string, BrandArt>(StringComparer.Ordinal);
            foreach (var (kind, dir) in Folders)
            {
                try
                {
                    var mf = Path.Combine(Root, dir, "manifest.json");
                    if (!File.Exists(mf)) continue;
                    using var doc = JsonDocument.Parse(File.ReadAllText(mf));
                    if (!doc.RootElement.TryGetProperty("brands", out var brands)) continue;
                    foreach (var e in brands.EnumerateObject())
                    {
                        var v = e.Value;
                        string S(string k) => v.TryGetProperty(k, out var x) && x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : "";
                        map[e.Name] = new BrandArt(e.Name, kind, S("file"), S("category"),
                            v.TryGetProperty("tag", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null);
                    }
                }
                catch { /* a bad manifest means no logos from that folder, never a crash */ }
            }
            _brands = map;
        }
    }

    private static ImageSource? LoadFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    // ------------------------------------------------------------------ fallback: the name as a small wordmark tile
    private static readonly (byte r, byte g, byte b)[] Tiles =
    {
        (0x2E, 0x3A, 0x52), (0x4A, 0x3A, 0x56), (0x2F, 0x4F, 0x4A), (0x5A, 0x42, 0x38), (0x3C, 0x48, 0x60), (0x55, 0x3A, 0x40),
    };

    private static ImageSource Wordmark(string name, int size)
    {
        var h = name.Aggregate(23, (a, c) => unchecked(a * 31 + c));
        var (r, g, b) = Tiles[(h & 0x7fffffff) % Tiles.Length];
        var bg = new SolidColorBrush(Color.FromRgb(r, g, b)); bg.Freeze();
        var fg = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF0)); fg.Freeze();
        var label = string.IsNullOrWhiteSpace(name) ? "?" : name.Trim();
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRoundedRectangle(bg, null, new Rect(size * 0.03, size * 0.03, size * 0.94, size * 0.94), size * 0.12, size * 0.12);
            var ft = new FormattedText(label.ToUpperInvariant(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                size * 0.125, fg, 1.0)
            { MaxTextWidth = size * 0.78, MaxTextHeight = size * 0.78, TextAlignment = TextAlignment.Center, Trimming = TextTrimming.CharacterEllipsis };
            dc.DrawText(ft, new Point((size - ft.MaxTextWidth) / 2, (size - ft.Height) / 2));
        }
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }
}
