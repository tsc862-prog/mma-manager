using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MmaManager.Core.Model;

namespace MmaManager.Wpf.Art;

/// <summary>
/// Grey "no portrait yet" placeholder art — one bust (face) + one full figure
/// (body) per weight class × sex, in generic fighter gear. Used for anonymous
/// LOCAL-scene fighters (<see cref="Fighter.LocalTalent"/>) who have no real
/// identity until they are discovered; once discovered, the normal pack portrait
/// takes over automatically. Loaded from disk (Assets\Placeholders) like the
/// painted pack, but kept OUT of the pack's identity pool so it never leaks onto
/// named/world fighters. Returns null when a file is missing so callers can fall
/// through to the pack / drawn fallback.
/// </summary>
public static class PlaceholderLibrary
{
    private static readonly string Dir =
        Path.Combine(AppContext.BaseDirectory, "Assets", "Placeholders");
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new();

    public static ImageSource? Face(Fighter f) => Load(Key(f, body: false));
    public static ImageSource? Body(Fighter f) => Load(Key(f, body: true));

    private static string Key(Fighter f, bool body)
    {
        var sex = f.Sex == Sex.Female ? "f" : "m";
        var wc = f.WeightClass.ToString().ToLowerInvariant();   // e.g. "lightheavyweight"
        return $"{sex}_{wc}_{(body ? "body" : "face")}";
    }

    private static ImageSource? Load(string key) => Cache.GetOrAdd(key, k =>
    {
        var path = Path.Combine(Dir, k + ".png");
        if (!File.Exists(path)) return null;
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(path);
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    });
}
