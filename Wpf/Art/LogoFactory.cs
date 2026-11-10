using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MmaManager.Core.Model;

namespace MmaManager.Wpf.Art;

/// <summary>
/// Hand-designed vector logos for the four named starting promotions, plus a
/// generated fallback badge for any other (AI-run or newly created) promotion.
/// Each is a frozen <see cref="DrawingImage"/> so it scales cleanly and needs no
/// asset files. Cached by abbreviation.
/// </summary>
public static class LogoFactory
{
    private static readonly Dictionary<string, ImageSource> Cache = new();

    public static ImageSource ForPromotion(Promotion promo) => ForAbbreviation(promo.Abbreviation);

    public static ImageSource ForAbbreviation(string abbreviation)
    {
        if (Cache.TryGetValue(abbreviation, out var cached)) return cached;
        var image = Build(abbreviation);
        Cache[abbreviation] = image;
        return image;
    }

    private static ImageSource Build(string abbr)
    {
        var group = new DrawingGroup();
        using (var ctx = group.Open())
        {
            switch (abbr)
            {
                case "AFC": Afc(ctx); break;
                case "VMMA": Vmma(ctx); break;
                case "ICF": Icf(ctx); break;
                case "CK": CageKings(ctx); break;
                default: Fallback(ctx, abbr); break;
            }
        }
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    // Apex Fighting Championship — a gold apex on black.
    private static void Afc(DrawingContext ctx)
    {
        Background(ctx, Rgb(0x111317));
        var gold = Frozen(Rgb(0xD4AF37));
        ctx.DrawGeometry(gold, null, Poly((50, 16), (76, 60), (24, 60)));
        ctx.DrawGeometry(Frozen(Rgb(0x111317)), null, Poly((50, 30), (64, 58), (36, 58)));
        Text(ctx, "AFC", 20, gold, 64);
    }

    // Vanguard MMA — a silver chevron on navy.
    private static void Vmma(DrawingContext ctx)
    {
        Background(ctx, Rgb(0x1B2A4A));
        var silver = Frozen(Rgb(0xC7CDD6));
        ctx.DrawGeometry(silver, null, Poly((24, 24), (50, 44), (76, 24), (76, 35), (50, 55), (24, 35)));
        Text(ctx, "VMMA", 15, silver, 66);
    }

    // Iron Circle FC — a red ring on gunmetal.
    private static void Icf(DrawingContext ctx)
    {
        Background(ctx, Rgb(0x24262B));
        var ring = new Pen(Frozen(Rgb(0xC0392B)), 8);
        ring.Freeze();
        ctx.DrawEllipse(null, ring, new Point(50, 40), 24, 24);
        Text(ctx, "ICF", 17, Frozen(Rgb(0xF0F0F0)), 74);
    }

    // Cage Kings — a gold crown on royal purple.
    private static void CageKings(DrawingContext ctx)
    {
        Background(ctx, Rgb(0x3B2A5A));
        var gold = Frozen(Rgb(0xE0B94A));
        ctx.DrawGeometry(gold, null, Poly((28, 56), (28, 36), (37, 46), (50, 30), (63, 46), (72, 36), (72, 56)));
        Text(ctx, "CK", 20, gold, 74);
    }

    // Any other promotion — a badge coloured from the abbreviation.
    private static void Fallback(DrawingContext ctx, string abbr)
    {
        var palette = new[] { Rgb(0x2E4057), Rgb(0x553D5B), Rgb(0x3A5A40), Rgb(0x5B3A29), Rgb(0x2B4C4E) };
        int hash = abbr.Aggregate(7, (h, c) => h * 31 + c);
        Background(ctx, palette[(hash & 0x7FFFFFFF) % palette.Length]);
        Text(ctx, abbr.Length <= 4 ? abbr : abbr[..4], abbr.Length >= 4 ? 15 : 20, Frozen(Rgb(0xF2F2F2)), 58);
    }

    // --- primitives ---------------------------------------------------------

    private static void Background(DrawingContext ctx, Color c) =>
        ctx.DrawRoundedRectangle(Frozen(c), null, new Rect(4, 4, 92, 92), 16, 16);

    private static void Text(DrawingContext ctx, string text, double size, Brush brush, double centerY)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Black, FontStretches.Normal),
            size, brush, 1.0);
        ctx.DrawText(ft, new Point(50 - ft.Width / 2, centerY - ft.Height / 2));
    }

    private static Geometry Poly(params (double x, double y)[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(new Point(pts[0].x, pts[0].y), true, true);
            for (int i = 1; i < pts.Length; i++)
                c.LineTo(new Point(pts[i].x, pts[i].y), true, false);
        }
        g.Freeze();
        return g;
    }

    private static Color Rgb(int hex) => Color.FromRgb((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex);

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
