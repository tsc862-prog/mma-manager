using System.Windows;
using System.Windows.Media;
using static MmaManager.Wpf.Art.FlatArt;

namespace MmaManager.Wpf.Art;

/// <summary>
/// Draws one character's head in the flat-illustration style, in local
/// coordinates (origin = head centre, chin at +FaceH, crown at -FaceH).
/// Shared by the face-shot and body-shot renderers so both always match.
/// </summary>
internal static class FlatHead
{
    public static void Draw(DrawingContext ctx, CharacterDna d, double cx, double cy, double scale)
    {
        ctx.PushTransform(new TranslateTransform(cx, cy));
        ctx.PushTransform(new ScaleTransform(scale, scale));

        double w = d.FaceW, h = d.FaceH;
        var head = HeadGeo(w, h, d.Jaw, d.Cheek);
        var hairClip = HeadGeo(w * 1.06, h * 1.05, d.Jaw * 1.02, d.Cheek);
        var beardClip = HeadGeo(w * 1.03, h * (d.Facial == 6 ? 1.30 : 1.12), d.Jaw * 1.08, d.Cheek);

        HairBack(ctx, d, w, h);
        Ears(ctx, d, w, h);
        ctx.DrawGeometry(B(d.Skin), null, head);
        FacialHair(ctx, d, w, h, beardClip);
        Mouth(ctx, d, w, h);
        Moustache(ctx, d, w, h);
        Nose(ctx, d, w, h);
        Eyes(ctx, d, w, h);
        Brows(ctx, d, w, h);
        HairFront(ctx, d, w, h, hairClip);
        Wear(ctx, d, w, h);

        ctx.Pop();
        ctx.Pop();
    }

    // The head silhouette: chin -> jaw -> cheek -> temple -> crown, mirrored.
    private static Geometry HeadGeo(double w, double h, double jaw, double cheek)
    {
        double jw = w * jaw;
        return Geo(c =>
        {
            c.BeginFigure(new Point(0, h), true, true);
            c.BezierTo(new Point(jw * 0.55, h * 0.99), new Point(jw * 0.95, h * 0.82), new Point(jw, h * 0.55), true, true);
            c.BezierTo(new Point(jw + (w - jw) * cheek, h * 0.28), new Point(w, h * 0.02), new Point(w, -h * 0.18), true, true);
            c.BezierTo(new Point(w, -h * 0.62), new Point(w * 0.72, -h * 0.94), new Point(0, -h), true, true);
            c.BezierTo(new Point(-w * 0.72, -h * 0.94), new Point(-w, -h * 0.62), new Point(-w, -h * 0.18), true, true);
            c.BezierTo(new Point(-w, h * 0.02), new Point(-(jw + (w - jw) * cheek), h * 0.28), new Point(-jw, h * 0.55), true, true);
            c.BezierTo(new Point(-jw * 0.95, h * 0.82), new Point(-jw * 0.55, h * 0.99), new Point(0, h), true, true);
        });
    }

    // Open path along the jawline only (stroked for the chin-strap beard).
    private static Geometry JawGeo(double w, double h, double jaw, double cheek)
    {
        double jw = w * jaw;
        return Geo(c =>
        {
            c.BeginFigure(new Point(-w, -h * 0.02), false, false);
            c.BezierTo(new Point(-(jw + (w - jw) * cheek), h * 0.28), new Point(-jw, h * 0.4), new Point(-jw, h * 0.55), true, true);
            c.BezierTo(new Point(-jw * 0.95, h * 0.82), new Point(-jw * 0.55, h * 0.99), new Point(0, h), true, true);
            c.BezierTo(new Point(jw * 0.55, h * 0.99), new Point(jw * 0.95, h * 0.82), new Point(jw, h * 0.55), true, true);
            c.BezierTo(new Point(jw, h * 0.4), new Point(jw + (w - jw) * cheek, h * 0.28), new Point(w, -h * 0.02), true, true);
        });
    }

    // A hair "cap": everything above a curved hairline, meant to be clipped to
    // the (slightly expanded) head. sweep shifts the hairline's low point
    // sideways for parted styles.
    private static Geometry CapGeo(double w, double h, double centerY, double sideY, double sweep = 0)
    {
        return Geo(c =>
        {
            c.BeginFigure(new Point(-w * 1.12, sideY), true, true);
            c.BezierTo(new Point(-w * 0.55 + sweep, sideY), new Point(-w * 0.3 + sweep, centerY), new Point(sweep, centerY), true, true);
            c.BezierTo(new Point(w * 0.3 + sweep, centerY), new Point(w * 0.55 + sweep, sideY), new Point(w * 1.12, sideY), true, true);
            c.LineTo(new Point(w * 1.12, -h * 1.35), true, true);
            c.LineTo(new Point(-w * 1.12, -h * 1.35), true, true);
        });
    }

    // ------------------------------------------------------------------ hair --
    private static void HairBack(DrawingContext ctx, CharacterDna d, double w, double h)
    {
        var hair = B(d.Hair);
        if (d.Female)
        {
            switch (d.HairStyle)
            {
                case 1:   // ponytail: swoop falling behind one shoulder
                    ctx.DrawGeometry(null, P(d.Hair, w * 0.34), Geo(c =>
                    {
                        c.BeginFigure(new Point(w * 0.3, -h * 0.75), false, false);
                        c.BezierTo(new Point(w * 1.05, -h * 0.45), new Point(w * 1.1, h * 0.3), new Point(w * 0.85, h * 1.15), true, true);
                    }));
                    ctx.DrawEllipse(B(d.HairDark), null, new Point(w * 0.72, -h * 0.28), w * 0.1, w * 0.16);   // tie
                    break;
                case 2:   // bob: rounded mass crown -> jaw
                    ctx.DrawRoundedRectangle(hair, null, new Rect(-w * 1.18, -h * 1.02, w * 2.36, h * 1.62), w * 0.6, w * 0.6);
                    break;
                case 3:   // long straight: panel falling past the chin
                    ctx.DrawRoundedRectangle(hair, null, new Rect(-w * 1.15, -h * 0.95, w * 2.3, h * 2.45), w * 0.5, w * 0.5);
                    break;
                case 4:   // braids: narrower panel + rope lines
                    ctx.DrawRoundedRectangle(hair, null, new Rect(-w * 1.12, -h * 0.9, w * 2.24, h * 2.3), w * 0.42, w * 0.42);
                    for (int i = -1; i <= 1; i += 2)
                        for (int j = 0; j < 3; j++)
                            ctx.DrawLine(P(WithA(d.HairDark, 0.8), 2.2),
                                new Point(i * (w * 0.82 + j * w * 0.11), h * (0.15 + j * 0.06)),
                                new Point(i * (w * 0.86 + j * w * 0.11), h * 1.25));
                    break;
                case 6:   // afro
                    ctx.DrawEllipse(hair, null, new Point(0, -h * 0.42), w * 1.42, w * 1.34);
                    break;
                case 8:   // curly bob: blobby cluster around the head
                    for (int i = 0; i < 7; i++)
                    {
                        double a = Math.PI * (0.12 + i * 0.127);
                        ctx.DrawEllipse(hair, null,
                            new Point(Math.Cos(a) * w * 1.02, -h * 0.35 - Math.Sin(a) * h * 0.62), w * 0.42, w * 0.42);
                    }
                    break;
            }
        }
        else
        {
            switch (d.HairStyle)
            {
                case 5:   // afro
                    ctx.DrawEllipse(hair, null, new Point(0, -h * 0.5), w * 1.3, w * 1.22);
                    break;
                case 11:  // long: panel to the shoulders
                    ctx.DrawRoundedRectangle(hair, null, new Rect(-w * 1.12, -h * 0.85, w * 2.24, h * 2.0), w * 0.45, w * 0.45);
                    break;
            }
        }
    }

    private static void HairFront(DrawingContext ctx, CharacterDna d, double w, double h, Geometry clip)
    {
        var hair = B(d.Hair);
        if (d.Female) { FemaleHairFront(ctx, d, w, h, clip); return; }

        switch (d.HairStyle)
        {
            case 0:   // bald — a whisper of shadow where hair was
                Clipped(ctx, clip, c => c.DrawGeometry(B(WithA(d.HairDark, 0.06)), null, CapGeo(w, h, -h * 0.46, -h * 0.28)));
                break;
            case 1:   // buzz
                Clipped(ctx, clip, c => c.DrawGeometry(B(WithA(d.Hair, 0.65)), null, CapGeo(w, h, -h * 0.42, -h * 0.16)));
                break;
            case 2:   // crew cut
                Clipped(ctx, clip, c => c.DrawGeometry(hair, null, CapGeo(w, h, -h * 0.40, -h * 0.12)));
                break;
            case 3:   // side part
                Clipped(ctx, clip, c => c.DrawGeometry(hair, null, CapGeo(w, h, -h * 0.30, -h * 0.18, -w * 0.42)));
                break;
            case 4:   // curly crop: cap + bumpy crown
                Clipped(ctx, clip, c => c.DrawGeometry(hair, null, CapGeo(w, h, -h * 0.42, -h * 0.14)));
                for (int i = -3; i <= 3; i++)
                    ctx.DrawEllipse(hair, null, new Point(i * w * 0.26, -h * (1.0 - Math.Abs(i) * 0.055)), w * 0.19, w * 0.19);
                break;
            case 5:   // afro: clean forehead edge
                Clipped(ctx, clip, c => c.DrawGeometry(hair, null, CapGeo(w, h, -h * 0.44, -h * 0.22)));
                break;
            case 6:   // cornrows: cap + braid lines to the crown
                Clipped(ctx, clip, c =>
                {
                    c.DrawGeometry(hair, null, CapGeo(w, h, -h * 0.42, -h * 0.14));
                    for (int i = -2; i <= 2; i++)
                        c.DrawGeometry(null, P(WithA(d.HairDark, 0.9), 1.8), Geo(g =>
                        {
                            g.BeginFigure(new Point(i * w * 0.24, -h * 0.44), false, false);
                            g.BezierTo(new Point(i * w * 0.28, -h * 0.7), new Point(i * w * 0.2, -h * 0.9), new Point(i * w * 0.12, -h * 1.02), true, true);
                        }));
                });
                break;
            case 7:   // top knot
                Clipped(ctx, clip, c => c.DrawGeometry(B(WithA(d.Hair, 0.8)), null, CapGeo(w, h, -h * 0.5, -h * 0.3)));
                ctx.DrawEllipse(hair, null, new Point(0, -h * 1.1), w * 0.26, w * 0.2);
                break;
            case 8:   // mohawk: shaved sides + centre strip
                Clipped(ctx, clip, c =>
                {
                    c.DrawGeometry(B(WithA(d.Hair, 0.22)), null, CapGeo(w, h, -h * 0.44, -h * 0.2));
                    c.DrawRectangle(hair, null, new Rect(-w * 0.17, -h * 1.2, w * 0.34, h * 0.85));
                });
                ctx.DrawGeometry(hair, null, Geo(c =>
                {
                    c.BeginFigure(new Point(-w * 0.17, -h * 0.92), true, true);
                    c.LineTo(new Point(-w * 0.1, -h * 1.24), true, true);
                    c.LineTo(new Point(0, -h * 0.98), true, true);
                    c.LineTo(new Point(w * 0.1, -h * 1.26), true, true);
                    c.LineTo(new Point(w * 0.17, -h * 0.92), true, true);
                }));
                break;
            case 9:   // slicked back: cap + volume over the crown
                ctx.DrawEllipse(hair, null, new Point(0, -h * 0.92), w * 0.78, h * 0.24);
                Clipped(ctx, clip, c => c.DrawGeometry(hair, null, CapGeo(w, h, -h * 0.46, -h * 0.24)));
                break;
            case 10:  // receding: widow's peak + bare temples
                Clipped(ctx, clip, c => c.DrawGeometry(hair, null, Geo(g =>
                {
                    g.BeginFigure(new Point(-w * 1.12, -h * 0.18), true, true);
                    g.BezierTo(new Point(-w * 0.72, -h * 0.3), new Point(-w * 0.62, -h * 0.62), new Point(-w * 0.42, -h * 0.6), true, true);
                    g.BezierTo(new Point(-w * 0.2, -h * 0.58), new Point(-w * 0.14, -h * 0.4), new Point(0, -h * 0.4), true, true);
                    g.BezierTo(new Point(w * 0.14, -h * 0.4), new Point(w * 0.2, -h * 0.58), new Point(w * 0.42, -h * 0.6), true, true);
                    g.BezierTo(new Point(w * 0.62, -h * 0.62), new Point(w * 0.72, -h * 0.3), new Point(w * 1.12, -h * 0.18), true, true);
                    g.LineTo(new Point(w * 1.12, -h * 1.35), true, true);
                    g.LineTo(new Point(-w * 1.12, -h * 1.35), true, true);
                })));
                break;
            case 11:  // long: centre-part top over the back panel
                Clipped(ctx, clip, c => c.DrawGeometry(hair, null, CapGeo(w, h, -h * 0.34, -h * 0.42)));
                break;
            case 12:  // flat top
                ctx.DrawGeometry(hair, null, Geo(c =>
                {
                    c.BeginFigure(new Point(-w * 0.88, -h * 0.45), true, true);
                    c.LineTo(new Point(-w * 0.82, -h * 1.28), true, true);
                    c.LineTo(new Point(w * 0.82, -h * 1.28), true, true);
                    c.LineTo(new Point(w * 0.88, -h * 0.45), true, true);
                    c.BezierTo(new Point(w * 0.4, -h * 0.55), new Point(-w * 0.4, -h * 0.55), new Point(-w * 0.88, -h * 0.45), true, true);
                }));
                break;
        }
    }

    private static void FemaleHairFront(DrawingContext ctx, CharacterDna d, double w, double h, Geometry clip)
    {
        var hair = B(d.Hair);
        switch (d.HairStyle)
        {
            case 0:   // bun: slick cap + top bun
                Clipped(ctx, clip, c => c.DrawGeometry(hair, null, CapGeo(w, h, -h * 0.38, -h * 0.1)));
                ctx.DrawEllipse(hair, null, new Point(0, -h * 1.08), w * 0.32, w * 0.26);
                break;
            case 1:   // ponytail: slick cap
                Clipped(ctx, clip, c => c.DrawGeometry(hair, null, CapGeo(w, h, -h * 0.40, -h * 0.1)));
                break;
            case 2:   // bob: full fringe + curtains over the face edges
                Clipped(ctx, clip, c => c.DrawGeometry(hair, null, CapGeo(w, h, -h * 0.28, -h * 0.05)));
                SideCurtains(ctx, d, w, h, 0.62);
                break;
            case 3:   // long: centre part + curtains
                Clipped(ctx, clip, c => c.DrawGeometry(hair, null, CapGeo(w, h, -h * 0.32, -h * 0.4)));
                SideCurtains(ctx, d, w, h, 1.0);
                break;
            case 4:   // braids: centre part
                Clipped(ctx, clip, c => c.DrawGeometry(hair, null, CapGeo(w, h, -h * 0.34, -h * 0.42)));
                break;
            case 5:   // pixie: crew-ish cap + side sweep
                Clipped(ctx, clip, c => c.DrawGeometry(hair, null, CapGeo(w, h, -h * 0.3, -h * 0.12, -w * 0.35)));
                ctx.DrawEllipse(hair, null, new Point(0, -h * 0.9), w * 0.8, h * 0.22);
                break;
            case 6:   // afro
                Clipped(ctx, clip, c => c.DrawGeometry(hair, null, CapGeo(w, h, -h * 0.44, -h * 0.22)));
                break;
            case 7:   // cornrow bun: braid lines + low bun
                Clipped(ctx, clip, c =>
                {
                    c.DrawGeometry(hair, null, CapGeo(w, h, -h * 0.42, -h * 0.12));
                    for (int i = -2; i <= 2; i++)
                        c.DrawGeometry(null, P(WithA(d.HairDark, 0.9), 1.8), Geo(g =>
                        {
                            g.BeginFigure(new Point(i * w * 0.24, -h * 0.44), false, false);
                            g.BezierTo(new Point(i * w * 0.28, -h * 0.7), new Point(i * w * 0.2, -h * 0.9), new Point(i * w * 0.12, -h * 1.02), true, true);
                        }));
                });
                ctx.DrawEllipse(hair, null, new Point(0, -h * 1.04), w * 0.3, w * 0.24);
                break;
            case 8:   // curly bob: clean forehead edge over the blob mass
                Clipped(ctx, clip, c => c.DrawGeometry(hair, null, CapGeo(w, h, -h * 0.36, -h * 0.1)));
                break;
        }
    }

    // Hair falling along both sides of the face (bob/long); len is in face-heights.
    private static void SideCurtains(DrawingContext ctx, CharacterDna d, double w, double h, double len)
    {
        var hair = B(d.Hair);
        foreach (int s in new[] { -1, 1 })
            ctx.DrawGeometry(hair, null, Geo(c =>
            {
                c.BeginFigure(new Point(s * w * 0.6, -h * 0.72), true, true);
                c.BezierTo(new Point(s * w * 1.05, -h * 0.55), new Point(s * w * 1.18, -h * 0.1), new Point(s * w * 1.12, h * len * 0.55), true, true);
                c.BezierTo(new Point(s * w * 1.1, h * len * 0.8), new Point(s * w * 0.95, h * len * 0.82), new Point(s * w * 0.78, h * len * 0.78), true, true);
                c.BezierTo(new Point(s * w * 0.84, h * 0.1), new Point(s * w * 0.8, -h * 0.3), new Point(s * w * 0.6, -h * 0.72), true, true);
            }));
    }

    // ----------------------------------------------------------- facial hair --
    private static void FacialHair(DrawingContext ctx, CharacterDna d, double w, double h, Geometry clip)
    {
        if (d.Facial is 0 or 4) return;
        var hair = B(d.Hair);

        // 1 stubble, 3 full, 6 heavy, 7 vandyke(base=goatee), 2 goatee, 5 chinstrap
        if (d.Facial is 1 or 3 or 6)
        {
            // Jaw-hugging: the top boundary passes below the mouth so the lips
            // stay on skin (moustache fills the gap above for full beards).
            double side = d.Facial == 6 ? h * 0.08 : h * 0.16;
            double dip = d.Facial == 6 ? h * 0.62 : h * 0.66;
            var brush = d.Facial == 1 ? B(WithA(d.Hair, 0.32)) : hair;
            Clipped(ctx, clip, c => c.DrawGeometry(brush, null, Geo(g =>
            {
                g.BeginFigure(new Point(-w * 1.08, side), true, true);
                g.BezierTo(new Point(-w * 0.60, h * 0.34), new Point(-w * 0.40, dip), new Point(0, dip), true, true);
                g.BezierTo(new Point(w * 0.40, dip), new Point(w * 0.60, h * 0.34), new Point(w * 1.08, side), true, true);
                g.LineTo(new Point(w * 1.08, h * 1.4), true, true);
                g.LineTo(new Point(-w * 1.08, h * 1.4), true, true);
            })));
        }
        else if (d.Facial is 2 or 7)
        {
            Clipped(ctx, clip, c =>
            {
                c.DrawEllipse(hair, null, new Point(0, h * 0.84), w * d.Jaw * 0.42, h * 0.24);
                c.DrawRectangle(hair, null, new Rect(-w * 0.05, h * 0.63, w * 0.10, h * 0.08));   // soul patch
            });
        }
        else if (d.Facial == 5)
        {
            Clipped(ctx, clip, c => c.DrawGeometry(null, P(d.Hair, w * 0.13), JawGeo(w * 0.99, h * 0.99, d.Jaw, d.Cheek)));
        }
    }

    private static void Moustache(DrawingContext ctx, CharacterDna d, double w, double h)
    {
        if (d.Facial is not (3 or 4 or 6 or 7)) return;
        ctx.DrawGeometry(null, P(d.Hair, h * 0.08), Geo(c =>
        {
            c.BeginFigure(new Point(-w * 0.30, h * 0.52), false, false);
            c.QuadraticBezierTo(new Point(0, h * 0.44), new Point(w * 0.30, h * 0.52), true, true);
        }));
    }

    // ---------------------------------------------------------------- features --
    private static void Eyes(DrawingContext ctx, CharacterDna d, double w, double h)
    {
        double ey = -h * 0.05, ex = w * 0.42;
        double rx = w * 0.155;
        var whiteB = B(Rgb(0xFAF7F2));
        var irisB = B(d.Eye);
        var pupilB = B(Rgb(0x1C1410));

        foreach (int s in new[] { -1, 1 })
        {
            var c = new Point(s * ex, ey);
            switch (d.EyeShape)
            {
                case 1:   // round
                    ctx.DrawEllipse(whiteB, null, c, rx * 0.92, rx * 0.8);
                    ctx.DrawEllipse(irisB, null, c, rx * 0.5, rx * 0.5);
                    ctx.DrawEllipse(pupilB, null, c, rx * 0.24, rx * 0.24);
                    break;
                case 2:   // narrow
                    ctx.DrawEllipse(whiteB, null, c, rx, rx * 0.42);
                    ctx.DrawEllipse(irisB, null, c, rx * 0.4, rx * 0.4);
                    ctx.DrawEllipse(pupilB, null, c, rx * 0.2, rx * 0.2);
                    ctx.DrawGeometry(null, P(d.SkinLine, 1.8), Geo(g =>
                    {
                        g.BeginFigure(new Point(c.X - rx, c.Y - rx * 0.34), false, false);
                        g.QuadraticBezierTo(new Point(c.X, c.Y - rx * 0.75), new Point(c.X + rx, c.Y - rx * 0.3), true, true);
                    }));
                    break;
                case 3:   // heavy-lid
                    ctx.DrawEllipse(whiteB, null, c, rx, rx * 0.66);
                    ctx.DrawEllipse(irisB, null, new Point(c.X, c.Y + rx * 0.06), rx * 0.46, rx * 0.46);
                    ctx.DrawEllipse(pupilB, null, new Point(c.X, c.Y + rx * 0.06), rx * 0.22, rx * 0.22);
                    ctx.DrawRectangle(B(d.Skin), null, new Rect(c.X - rx * 1.05, c.Y - rx * 0.75, rx * 2.1, rx * 0.52));
                    break;
                default:  // almond
                    ctx.DrawEllipse(whiteB, null, c, rx, rx * 0.62);
                    ctx.DrawEllipse(irisB, null, c, rx * 0.46, rx * 0.46);
                    ctx.DrawEllipse(pupilB, null, c, rx * 0.22, rx * 0.22);
                    break;
            }
            ctx.DrawEllipse(B(WithA(White, 0.85)), null, new Point(c.X - rx * 0.14, c.Y - rx * 0.16), rx * 0.09, rx * 0.09);

            if (d.Female)   // lashes at the outer corner
                for (int i = 0; i < 2; i++)
                    ctx.DrawLine(P(Rgb(0x1C1410), 1.6),
                        new Point(c.X + s * (rx * 0.72 + i * rx * 0.2), c.Y - rx * (0.4 - i * 0.12)),
                        new Point(c.X + s * (rx * 1.05 + i * rx * 0.22), c.Y - rx * (0.66 - i * 0.1)));
        }
    }

    private static void Brows(DrawingContext ctx, CharacterDna d, double w, double h)
    {
        double by = -h * 0.22, ex = w * 0.42;
        double bw = w * 0.36, bh = d.Brow switch { 2 => h * 0.075, 3 => h * 0.028, _ => h * 0.05 };
        var brush = B(Shade(d.HairDark, 0.8));

        foreach (int s in new[] { -1, 1 })
        {
            if (d.Brow == 4)   // arched stroke
            {
                ctx.DrawGeometry(null, P(Shade(d.HairDark, 0.8), h * 0.04), Geo(c =>
                {
                    c.BeginFigure(new Point(s * (ex - bw / 2), by + h * 0.02), false, false);
                    c.QuadraticBezierTo(new Point(s * ex, by - h * 0.045), new Point(s * (ex + bw / 2), by + h * 0.01), true, true);
                }));
                continue;
            }
            double tilt = d.Brow == 1 ? -s * 9 : 0;   // angled = stern, inner ends lower
            ctx.PushTransform(new RotateTransform(tilt, s * ex, by));
            ctx.DrawRoundedRectangle(brush, null, new Rect(s * ex - bw / 2, by - bh / 2, bw, bh), bh / 2, bh / 2);
            ctx.Pop();
        }
    }

    private static void Nose(DrawingContext ctx, CharacterDna d, double w, double h)
    {
        var shadow = B(d.SkinShadow);
        var line = d.SkinLine;
        switch (d.Nose)
        {
            case 1:   // wedge
                ctx.DrawGeometry(shadow, null, Geo(c =>
                {
                    c.BeginFigure(new Point(0, h * 0.04), true, true);
                    c.LineTo(new Point(-w * 0.10, h * 0.30), true, true);
                    c.QuadraticBezierTo(new Point(0, h * 0.36), new Point(w * 0.10, h * 0.30), true, true);
                }));
                break;
            case 2:   // button
                ctx.DrawEllipse(shadow, null, new Point(0, h * 0.27), w * 0.09, h * 0.055);
                break;
            case 3:   // wide
                ctx.DrawRoundedRectangle(shadow, null, new Rect(-w * 0.14, h * 0.21, w * 0.28, h * 0.1), w * 0.07, h * 0.05);
                ctx.DrawEllipse(B(line), null, new Point(-w * 0.08, h * 0.275), w * 0.025, w * 0.025);
                ctx.DrawEllipse(B(line), null, new Point(w * 0.08, h * 0.275), w * 0.025, w * 0.025);
                break;
            case 4:   // strong: bridge line + wedge
                ctx.DrawLine(P(line, 2.8), new Point(-w * 0.025, -h * 0.02), new Point(-w * 0.045, h * 0.22));
                ctx.DrawGeometry(shadow, null, Geo(c =>
                {
                    c.BeginFigure(new Point(-w * 0.045, h * 0.06), true, true);
                    c.LineTo(new Point(-w * 0.12, h * 0.31), true, true);
                    c.QuadraticBezierTo(new Point(0, h * 0.375), new Point(w * 0.12, h * 0.31), true, true);
                }));
                break;
            default:  // simple curve + nostril hook
                ctx.DrawGeometry(null, P(line, 3.0), Geo(c =>
                {
                    c.BeginFigure(new Point(-w * 0.03, h * 0.04), false, false);
                    c.BezierTo(new Point(-w * 0.05, h * 0.14), new Point(-w * 0.055, h * 0.2), new Point(-w * 0.05, h * 0.26), true, true);
                    c.QuadraticBezierTo(new Point(-w * 0.03, h * 0.31), new Point(w * 0.05, h * 0.29), true, true);
                }));
                break;
        }
    }

    private static void Mouth(DrawingContext ctx, CharacterDna d, double w, double h)
    {
        double my = h * 0.55, mw = w * 0.24;
        var line = d.SkinLine;
        switch (d.Mouth)
        {
            case 1:   // smile
                ctx.DrawGeometry(null, P(line, 3.2), Geo(c =>
                {
                    c.BeginFigure(new Point(-mw, my - h * 0.008), false, false);
                    c.QuadraticBezierTo(new Point(0, my + h * 0.05), new Point(mw, my - h * 0.008), true, true);
                }));
                break;
            case 2:   // open smile
                ctx.DrawGeometry(B(Rgb(0x50241E)), null, Geo(c =>
                {
                    c.BeginFigure(new Point(-mw * 1.1, my - h * 0.01), true, true);
                    c.QuadraticBezierTo(new Point(0, my + h * 0.005), new Point(mw * 1.1, my - h * 0.01), true, true);
                    c.QuadraticBezierTo(new Point(0, my + h * 0.10), new Point(-mw * 1.1, my - h * 0.01), true, true);
                }));
                ctx.DrawRectangle(B(Rgb(0xF5F1E8)), null, new Rect(-mw * 0.7, my - h * 0.005, mw * 1.4, h * 0.022));
                break;
            case 3:   // smirk
                ctx.DrawGeometry(null, P(line, 3.2), Geo(c =>
                {
                    c.BeginFigure(new Point(-mw, my + h * 0.01), false, false);
                    c.QuadraticBezierTo(new Point(mw * 0.2, my + h * 0.035), new Point(mw, my - h * 0.02), true, true);
                }));
                break;
            case 4:   // slight frown
                ctx.DrawGeometry(null, P(line, 3.2), Geo(c =>
                {
                    c.BeginFigure(new Point(-mw, my + h * 0.015), false, false);
                    c.QuadraticBezierTo(new Point(0, my - h * 0.02), new Point(mw, my + h * 0.015), true, true);
                }));
                break;
            case 5:   // full lips
                ctx.DrawEllipse(B(d.Lip), null, new Point(0, my + h * 0.008), mw * 1.05, h * 0.042);
                ctx.DrawGeometry(null, P(Shade(d.Lip, 0.7), 1.6), Geo(c =>
                {
                    c.BeginFigure(new Point(-mw * 0.95, my), false, false);
                    c.QuadraticBezierTo(new Point(0, my + h * 0.012), new Point(mw * 0.95, my), true, true);
                }));
                break;
            default:  // neutral
                ctx.DrawGeometry(null, P(line, 3.2), Geo(c =>
                {
                    c.BeginFigure(new Point(-mw, my), false, false);
                    c.QuadraticBezierTo(new Point(0, my + h * 0.018), new Point(mw, my), true, true);
                }));
                break;
        }
    }

    private static void Ears(DrawingContext ctx, CharacterDna d, double w, double h)
    {
        double ey = -h * 0.02;
        foreach (int s in new[] { -1, 1 })
        {
            var c = new Point(s * w * 0.98, ey);
            ctx.DrawEllipse(B(d.Skin), null, c, w * 0.115, h * 0.155);
            if (d.Cauliflower)
            {
                ctx.DrawEllipse(B(d.SkinLine), null, new Point(c.X + s * w * 0.01, c.Y), w * 0.07, h * 0.09);
                ctx.DrawEllipse(B(d.SkinShadow), null, new Point(c.X - s * w * 0.015, c.Y - h * 0.03), w * 0.045, h * 0.05);
            }
            else
            {
                ctx.DrawEllipse(B(d.SkinShadow), null, new Point(c.X + s * w * 0.015, c.Y + h * 0.01), w * 0.055, h * 0.08);
            }
            if (d.Earring)
                ctx.DrawEllipse(B(Rgb(0xE0B93E)), null, new Point(c.X + s * w * 0.02, c.Y + h * 0.14), w * 0.035, w * 0.035);
        }
    }

    // Age wrinkles, scars, blush — the last flat details on top.
    private static void Wear(DrawingContext ctx, CharacterDna d, double w, double h)
    {
        if (d.Blush && !d.Wrinkles)
        {
            var blush = B(WithA(Rgb(0xD97B6C), 0.18));
            ctx.DrawEllipse(blush, null, new Point(-w * 0.55, h * 0.24), w * 0.15, h * 0.07);
            ctx.DrawEllipse(blush, null, new Point(w * 0.55, h * 0.24), w * 0.15, h * 0.07);
        }
        if (d.Wrinkles)
        {
            var wr = P(WithA(d.SkinLine, 0.45), 1.6);
            ctx.DrawGeometry(null, wr, Geo(c =>
            {
                c.BeginFigure(new Point(-w * 0.3, -h * 0.5), false, false);
                c.QuadraticBezierTo(new Point(0, -h * 0.55), new Point(w * 0.3, -h * 0.5), true, true);
            }));
            foreach (int s in new[] { -1, 1 })
                ctx.DrawGeometry(null, wr, Geo(c =>
                {
                    c.BeginFigure(new Point(s * w * 0.32, h * 0.28), false, false);
                    c.QuadraticBezierTo(new Point(s * w * 0.38, h * 0.4), new Point(s * w * 0.3, h * 0.48), true, true);
                }));
        }
        if (d.Scar)
            ctx.DrawLine(P(Lighten(d.Skin, 0.4), 2.2),
                new Point(w * 0.32, -h * 0.32), new Point(w * 0.5, -h * 0.22));
    }

    private static void Clipped(DrawingContext ctx, Geometry clip, Action<DrawingContext> draw)
    {
        ctx.PushClip(clip);
        draw(ctx);
        ctx.Pop();
    }
}
