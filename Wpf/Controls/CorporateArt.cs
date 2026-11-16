using System.Collections.Concurrent;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MmaManager.Wpf.Art;

namespace MmaManager.Wpf.Controls;

/// <summary>
/// The one way the screens draw the people who are not fighters (owners, founders, named staff) and the
/// brands that pay the bills (media partners, sponsors). Reads <see cref="PeopleLibrary"/> and
/// <see cref="BrandLibrary"/> by the EXACT game name and only reframes what they return for the UI:
/// a portrait is cropped to the head and shoulders for a round chip; a logo is trimmed to its drawn edge so
/// a 32 px row shows the badge, not a 512 px square of empty canvas. Nothing here decides anything.
///
/// XAML uses the <c>PersonFace</c> and <c>BrandMark</c> styles in App.xaml (Content = the name), which bind
/// through <see cref="FaceOf"/>, <see cref="LogoOf"/> and <see cref="HasLogo"/>.
/// </summary>
public static class CorporateArt
{
    private static readonly ConcurrentDictionary<string, ImageSource> Faces = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, ImageSource?> Logos = new(StringComparer.Ordinal);

    /// <summary>Round-chip art for a person: the portrait cropped to head and shoulders, or the library's initials disc.</summary>
    public static ImageSource Face(string? name)
    {
        string key = name ?? "";
        return Faces.GetOrAdd(key, k =>
            PeopleLibrary.PortraitOrNull(k) is BitmapSource src && Crop(src, 0.19, 0.02, 0.62) is { } head
                ? head
                : PeopleLibrary.Portrait(k, 128));
    }

    /// <summary>True when this exact name has a real portrait (not the initials fallback).</summary>
    public static bool HasFace(string? name) => PeopleLibrary.Has(name);

    /// <summary>The partner's logo trimmed to its drawn edge, or null when there is no logo (draw the name instead).</summary>
    public static ImageSource? Logo(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        return Logos.GetOrAdd(name, k => BrandLibrary.LogoOrNull(k) is BitmapSource src ? Trim(src) : null);
    }

    /// <summary>True when this exact partner name has a real logo.</summary>
    public static bool HasBrand(string? name) => Logo(name) is not null;

    // A square window (fractions of the source width) of a square portrait.
    private static ImageSource? Crop(BitmapSource src, double x, double y, double size)
    {
        try
        {
            int w = src.PixelWidth, h = src.PixelHeight;
            int s = (int)Math.Round(Math.Min(w, h) * size);
            var rect = new Int32Rect((int)Math.Round(w * x), (int)Math.Round(h * y), s, s);
            if (rect.X + rect.Width > w || rect.Y + rect.Height > h) return src;
            var c = new CroppedBitmap(src, rect);
            c.Freeze();
            return c;
        }
        catch { return null; }
    }

    // Cut away the transparent margin around a logo (alpha below a whisper), keeping a 2 px breath.
    private static ImageSource Trim(BitmapSource src)
    {
        try
        {
            var bgra = src.Format == PixelFormats.Bgra32 || src.Format == PixelFormats.Pbgra32
                ? src : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
            int w = bgra.PixelWidth, h = bgra.PixelHeight, stride = w * 4;
            var px = new byte[stride * h];
            bgra.CopyPixels(px, stride, 0);
            int minX = w, minY = h, maxX = -1, maxY = -1;
            for (int y = 0; y < h; y++)
            {
                int row = y * stride;
                for (int x = 0; x < w; x++)
                {
                    if (px[row + x * 4 + 3] <= 10) continue;
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
            if (maxX < 0) return src;
            minX = Math.Max(0, minX - 2); minY = Math.Max(0, minY - 2);
            maxX = Math.Min(w - 1, maxX + 2); maxY = Math.Min(h - 1, maxY + 2);
            var c = new CroppedBitmap(src, new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1));
            c.Freeze();
            return c;
        }
        catch { return src; }
    }

    // --- Converters for the shared styles -------------------------------------------------------------

    /// <summary>name -> round-chip face (never null).</summary>
    public static readonly IValueConverter FaceOf = new Fn(v => Face(v as string));

    /// <summary>name -> trimmed logo, or null.</summary>
    public static readonly IValueConverter LogoOf = new Fn(v => Logo(v as string));

    /// <summary>name -> true when a real logo exists (the BrandMark style shows the name tile otherwise).</summary>
    public static readonly IValueConverter HasLogo = new Fn(v => HasBrand(v as string));

    private sealed class Fn : IValueConverter
    {
        private readonly Func<object?, object?> _f;
        public Fn(Func<object?, object?> f) => _f = f;
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => _f(value);
        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
