using System.Windows;
using System.Windows.Media;

namespace MmaManager.Wpf.Art;

/// <summary>
/// Shared drawing helpers + the flat-illustration palette used by the
/// <see cref="CharacterFactory"/> renderers. The style is deliberately flat —
/// solid fills, no gradients or outlines — so thousands of generated fighters
/// read as one consistent art set (think mobile-game avatar packs).
/// </summary>
internal static class FlatArt
{
    public static readonly Color White = Color.FromRgb(255, 255, 255);

    public static Color Rgb(int hex) => Color.FromRgb((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex);

    public static Color Mix(Color a, Color b, double t) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    public static Color Shade(Color c, double f) =>
        Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));

    public static Color Lighten(Color c, double t) => Mix(c, White, t);

    public static Color WithA(Color c, double a) =>
        Color.FromArgb((byte)(Math.Clamp(a, 0, 1) * 255), c.R, c.G, c.B);

    public static SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    public static Pen P(Color c, double w)
    {
        var p = new Pen(B(c), w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        p.Freeze();
        return p;
    }

    public static Geometry Geo(Action<StreamGeometryContext> build)
    {
        var g = new StreamGeometry();
        using (var c = g.Open()) build(c);
        g.Freeze();
        return g;
    }

    // ------------------------------------------------------------- palettes --

    /// <summary>Flat skin tones per broad look; regions mix these buckets.</summary>
    public static readonly Color[] SkinDark =
        { Rgb(0x8D5524), Rgb(0x6B4423), Rgb(0x4E2F1B), Rgb(0xA0653A), Rgb(0x3B2314) };
    public static readonly Color[] SkinTan =
        { Rgb(0xC68642), Rgb(0xB87A50), Rgb(0xD29B6C), Rgb(0xAD7A4E) };
    public static readonly Color[] SkinAsian =
        { Rgb(0xF0C8A0), Rgb(0xE0B48C), Rgb(0xD09E6E), Rgb(0xF6D7B0) };
    public static readonly Color[] SkinLight =
        { Rgb(0xF8D3B0), Rgb(0xEDBE96), Rgb(0xFFE0C2), Rgb(0xE8B088), Rgb(0xF2C6A0) };

    public static readonly Color[] HairDarkOnly =
        { Rgb(0x191919), Rgb(0x261B14), Rgb(0x33241A), Rgb(0x1E1E22) };
    public static readonly Color[] HairAll =
        { Rgb(0x191919), Rgb(0x261B14), Rgb(0x33241A), Rgb(0x53381F), Rgb(0x74482C),
          Rgb(0xD9A441), Rgb(0xE8C15A), Rgb(0xB4502A), Rgb(0x7A3520) };

    public static readonly Color[] EyesDark =
        { Rgb(0x2E1E12), Rgb(0x4A2E1A), Rgb(0x5B3A21) };
    public static readonly Color[] EyesAll =
        { Rgb(0x2E1E12), Rgb(0x4A2E1A), Rgb(0x5B3A21), Rgb(0x7A5A2E), Rgb(0x4E7345), Rgb(0x4A7BA6), Rgb(0x6E7B86) };

    /// <summary>Pastel card backgrounds, like avatar-pack tiles.</summary>
    public static readonly Color[] Backgrounds =
        { Rgb(0xE8E4F5), Rgb(0xF5E1E4), Rgb(0xDFEDF5), Rgb(0xE3F0E3),
          Rgb(0xF7EDD8), Rgb(0xEFE7DE), Rgb(0xE9E9EF), Rgb(0xF2E5EF) };

    /// <summary>Shirts / tanks / sports bras for the face shot.</summary>
    public static readonly Color[] Outfits =
        { Rgb(0xC94F4E), Rgb(0x3FA7A0), Rgb(0x35507A), Rgb(0x3A3F47), Rgb(0x5B8C5A),
          Rgb(0x7A5BA6), Rgb(0xD9813F), Rgb(0x4A7BC8), Rgb(0x26282E), Rgb(0xE8E6E0) };

    /// <summary>Fight-trunk main + accent pairs for the body shot.</summary>
    public static readonly (Color main, Color accent)[] Shorts =
    {
        (Rgb(0x26262A), Rgb(0xD9B44A)),   // black / gold
        (Rgb(0xB03A34), Rgb(0xF0EDE6)),   // red / white
        (Rgb(0x2F4E86), Rgb(0xF0EDE6)),   // blue / white
        (Rgb(0x3E4A3A), Rgb(0xB8B29A)),   // olive / sand
        (Rgb(0xF0EDE6), Rgb(0x26262A)),   // white / black
        (Rgb(0x5E2A46), Rgb(0xD08A3E)),   // maroon / orange
        (Rgb(0x1E6E62), Rgb(0xE8E6E0)),   // teal / white
        (Rgb(0x26262A), Rgb(0xB03A34)),   // black / red
    };

    public static readonly Color[] Gloves =
        { Rgb(0x26262A), Rgb(0xB03A34), Rgb(0x3A5FA0), Rgb(0x8A2C26) };
}
