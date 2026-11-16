using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MmaManager.Wpf.Art;

/// <summary>What the pipeline knows about a pictured person (from Assets\People\manifest.json).</summary>
/// <param name="Kind">owner | founder | staff</param>
/// <param name="Role">owner | marketing | medical | coach | scouting</param>
/// <param name="Org">The promotion abbreviation they already work for, or null.</param>
public sealed record PersonArt(string Name, string File, string Kind, string Role, string? Org, string Sex, int Age, string Country);

/// <summary>
/// Head-and-shoulders portraits of the people who are not fighters: promotion owners and founders, named staff
/// (marketing, medical, coaching, scouting). Produced by the separate FighterArt pipeline
/// (E:/FighterArt, corporate/ + sync/synccorporate.py) and synced into <c>Assets\People</c>: one 512x512
/// transparent PNG per person plus <c>manifest.json</c>, keyed by the person's EXACT name as the game spells
/// it (DesignedStaff / Origins founders). Lookup is by name only; nothing here decides anything in the sim.
///
/// Never throws and never returns null from <see cref="Portrait"/>: a missing file, a missing folder or an
/// unknown name gets a tidy fallback (initials on a neutral disc) so the game looks right with no pack at all.
/// Images are loaded once, frozen and cached (same pattern as <see cref="PaintedLibrary"/>).
/// </summary>
public static class PeopleLibrary
{
    private static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "Assets", "People");
    private static readonly object Gate = new();
    private static Dictionary<string, PersonArt>? _people;
    private static readonly ConcurrentDictionary<string, ImageSource?> Files = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<(string, int), ImageSource> Fallbacks = new();

    /// <summary>Every name that has a real portrait.</summary>
    public static IReadOnlyCollection<string> Names { get { Load(); return _people!.Keys; } }

    /// <summary>True when a real portrait (not the fallback) exists for this exact name.</summary>
    public static bool Has(string? name) { Load(); return name is not null && _people!.ContainsKey(name) && File.Exists(PathOf(_people[name])); }

    /// <summary>The manifest row for a person (role, org, age, country), or null.</summary>
    public static PersonArt? Info(string? name) { Load(); return name is not null && _people!.TryGetValue(name, out var p) ? p : null; }

    /// <summary>The real portrait, or null when there is none (callers that want to draw their own fallback).</summary>
    public static ImageSource? PortraitOrNull(string? name)
    {
        Load();
        if (name is null || !_people!.TryGetValue(name, out var p)) return null;
        return Files.GetOrAdd(name, _ => LoadFile(PathOf(p)));
    }

    /// <summary>
    /// The portrait for <paramref name="name"/>; when there is none, initials on a neutral disc at
    /// <paramref name="fallbackSize"/> pixels. Always a frozen ImageSource.
    /// </summary>
    public static ImageSource Portrait(string? name, int fallbackSize = 256) =>
        PortraitOrNull(name) ?? Fallbacks.GetOrAdd((name ?? "", fallbackSize), k => Initials(k.Item1, k.Item2));

    private static string PathOf(PersonArt p) => Path.Combine(Dir, p.File);

    private static void Load()
    {
        if (_people is not null) return;
        lock (Gate)
        {
            if (_people is not null) return;
            var map = new Dictionary<string, PersonArt>(StringComparer.Ordinal);
            try
            {
                var mf = Path.Combine(Dir, "manifest.json");
                if (File.Exists(mf))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(mf));
                    if (doc.RootElement.TryGetProperty("people", out var people))
                        foreach (var e in people.EnumerateObject())
                        {
                            var v = e.Value;
                            map[e.Name] = new PersonArt(e.Name, Str(v, "file"), Str(v, "kind"), Str(v, "role"),
                                v.TryGetProperty("org", out var o) && o.ValueKind == JsonValueKind.String ? o.GetString() : null,
                                Str(v, "sex"), v.TryGetProperty("age", out var a) && a.TryGetInt32(out var n) ? n : 0, Str(v, "country"));
                        }
                }
            }
            catch { /* a bad manifest means no portraits, never a crash */ }
            _people = map;
        }
    }

    private static string Str(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

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

    // ------------------------------------------------------------------ fallback: initials on a neutral disc
    private static readonly string[] Honorifics = { "Dr.", "Dr", "Sheikh", "Baron", "Dona", "Big", "Mr.", "Mrs.", "Ms." };
    private static readonly (byte r, byte g, byte b)[] Discs =
    {
        (0x5B, 0x6B, 0x7F), (0x6E, 0x62, 0x78), (0x4F, 0x6E, 0x6A), (0x7A, 0x66, 0x58), (0x5A, 0x6A, 0x86), (0x70, 0x5C, 0x62),
    };

    /// <summary>"Dr. Ruth Nakashima" -> "RN"; "Sheikh Faisal bin Rashid" -> "FR"; "Cher" -> "C".</summary>
    internal static string InitialsOf(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !Honorifics.Contains(w, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (words.Length == 0) return "?";
        var first = words[0];
        var last = words.Length > 1 ? words[^1] : "";
        return (first[..1] + (last.Length > 0 ? last[..1] : "")).ToUpperInvariant();
    }

    private static ImageSource Initials(string name, int size)
    {
        var h = name.Aggregate(17, (a, c) => unchecked(a * 31 + c));
        var (r, g, b) = Discs[(h & 0x7fffffff) % Discs.Length];
        var disc = new SolidColorBrush(Color.FromRgb(r, g, b)); disc.Freeze();
        var txt = new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF0)); txt.Freeze();
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawEllipse(disc, null, new Point(size / 2.0, size / 2.0), size * 0.5, size * 0.5);
            var ft = new FormattedText(InitialsOf(name), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                size * 0.38, txt, 1.0);
            dc.DrawText(ft, new Point((size - ft.Width) / 2, (size - ft.Height) / 2));
        }
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }
}
