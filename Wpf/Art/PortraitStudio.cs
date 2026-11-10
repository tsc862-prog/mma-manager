using System.Windows;
using System.Windows.Media;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Wpf.Art;

/// <summary>
/// Experimental portrait renderer used to preview several art styles side by side
/// so a direction can be chosen. Shares one <see cref="FaceModel"/> (with more
/// human proportions than the current cartoon look) and renders it in different
/// treatments. Not wired into the app yet — see <c>--stylegallery</c>.
/// </summary>
public static class PortraitStudio
{
    public enum Style
    {
        RealisticSoft, RealisticDetailed, Painterly, Noir, Sepia,
        Comic, InkLine, PencilSketch, FlatMinimal, LowPoly,
    }

    public static ImageSource Render(Fighter f, int age, Style style)
    {
        var m = new FaceModel(f, age);
        // Palette-only styles reuse the realistic renderer on a transformed palette.
        switch (style)
        {
            case Style.Noir: m.Recolor(Grayscale); break;
            case Style.Sepia: m.Recolor(SepiaTone); break;
        }

        var group = new DrawingGroup();
        using (var ctx = group.Open())
        {
            switch (style)
            {
                case Style.RealisticSoft: Realistic(ctx, m, depth: 0.7, texture: false, warm: false); break;
                case Style.RealisticDetailed: Realistic(ctx, m, depth: 1.0, texture: true, warm: false); break;
                case Style.Painterly: Realistic(ctx, m, depth: 1.1, texture: true, warm: true); break;
                case Style.Noir: Realistic(ctx, m, depth: 1.3, texture: true, warm: false); break;
                case Style.Sepia: Realistic(ctx, m, depth: 0.9, texture: true, warm: false); break;
                case Style.Comic: Comic(ctx, m); break;
                case Style.InkLine: Sketch(ctx, m, Rgb(0xF6F3EC), Rgb(0x2A2622), pencil: false); break;
                case Style.PencilSketch: Sketch(ctx, m, Rgb(0xEEE6D6), Rgb(0x4A3B2A), pencil: true); break;
                case Style.FlatMinimal: Flat(ctx, m); break;
                case Style.LowPoly: LowPoly(ctx, m); break;
            }
        }
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    // ================================================================ styles ==

    // Soft airbrushed realism: radial-gradient skin, layered feathered shadows.
    private static void Realistic(DrawingContext ctx, FaceModel m, double depth, bool texture, bool warm)
    {
        Backdrop(ctx, m, warm);
        Body(ctx, m);
        if (m.HasBackHair) ctx.DrawEllipse(Radial(m.Hair, m.HairDark), null, m.BackHair, m.BackHairRx, m.BackHairRy);
        Ears(ctx, m, soft: true);

        var skin = new RadialGradientBrush { GradientOrigin = new Point(0.5, 0.38), Center = new Point(0.5, 0.42), RadiusX = 0.62, RadiusY = 0.66 };
        skin.GradientStops.Add(new GradientStop(m.SkinHi, 0));
        skin.GradientStops.Add(new GradientStop(m.Skin, 0.6));
        skin.GradientStops.Add(new GradientStop(m.SkinShadow, 0.9));
        skin.GradientStops.Add(new GradientStop(m.SkinDeep, 1));
        skin.Freeze();
        ctx.DrawGeometry(skin, P(m.SkinDeep, 0.6), m.Face);

        ctx.PushClip(m.Face);
        // core shadows: cheek hollows, jaw, temples, brow, nose, under-lip
        double a = 0.32 * depth;
        foreach (int s in new[] { -1, 1 })
        {
            ctx.DrawEllipse(Soft(m.SkinDeep, a), null, new Point(m.Cx + s * m.Fw * 0.58, m.Cy + m.Fh * 0.14), m.Fw * 0.26, m.Fh * 0.22); // cheek hollow
            ctx.DrawEllipse(Soft(m.SkinDeep, a * 1.1), null, new Point(m.Cx + s * m.Fw * 0.7, m.Cy + m.Fh * 0.3), m.Fw * 0.28, m.Fh * 0.22);   // jaw
            ctx.DrawEllipse(Soft(m.SkinDeep, a * 0.7), null, new Point(m.Cx + s * m.Fw * 0.72, m.Cy - m.Fh * 0.28), m.Fw * 0.22, m.Fh * 0.2);   // temple
        }
        ctx.DrawEllipse(Soft(m.SkinDeep, a), null, new Point(m.Cx, m.Cy - m.Fh * 0.16), m.Fw * 0.42, m.Fh * 0.08);       // brow shadow
        ctx.DrawEllipse(Soft(m.SkinDeep, a * 0.9), null, new Point(m.Cx, m.Cy + m.Fh * 0.24), m.Fw * 0.18, m.Fh * 0.08);  // under nose
        ctx.DrawEllipse(Soft(m.SkinDeep, a * 0.8), null, new Point(m.Cx, m.Cy + m.Fh * 0.5), m.Fw * 0.34, m.Fh * 0.1);    // under lip / chin
        // highlights
        ctx.DrawEllipse(Soft(m.SkinHi, 0.5), null, new Point(m.Cx, m.Cy - m.Fh * 0.4), m.Fw * 0.36, m.Fh * 0.18);
        ctx.DrawEllipse(Soft(m.SkinHi, 0.45), null, new Point(m.Cx, m.Cy + m.Fh * 0.02), m.Fw * 0.1, m.Fh * 0.24);
        foreach (int s in new[] { -1, 1 })
        {
            ctx.DrawEllipse(Soft(m.SkinHi, 0.32), null, new Point(m.Cx + s * m.Fw * 0.4, m.Cy + m.Fh * 0.02), m.Fw * 0.18, m.Fh * 0.12);
            ctx.DrawEllipse(Soft(warm ? Rgb(0xC66) : Rgb(0xB86B60), 0.12), null, new Point(m.Cx + s * m.Fw * 0.44, m.Cy + m.Fh * 0.16), m.Fw * 0.16, m.Fh * 0.1);
        }
        if (texture) SkinTexture(ctx, m);
        ctx.Pop();

        FacialHairSoft(ctx, m);
        BrowsFilled(ctx, m);
        EyesRealistic(ctx, m);
        NoseSoft(ctx, m);
        MouthSoft(ctx, m);
        FrontHairSoft(ctx, m);
        Wear(ctx, m);
    }

    private static void Comic(DrawingContext ctx, FaceModel m)
    {
        Backdrop(ctx, m, false);
        Body(ctx, m);
        if (m.HasBackHair) ctx.DrawGeometry(B(m.Hair), Ink(m, 2.4), new EllipseGeometry(m.BackHair, m.BackHairRx, m.BackHairRy));
        Ears(ctx, m, soft: false);
        ctx.DrawGeometry(B(m.Skin), Ink(m, 2.4), m.Face);
        ctx.PushClip(m.Face);
        // hard cel shadow on one side + jaw
        var sh = B(WithA(m.SkinDeep, 0.5));
        ctx.DrawGeometry(sh, null, Geo(c =>
        {
            c.BeginFigure(new Point(m.Cx + m.Fw * 0.2, m.Cy - m.Fh * 0.6), true, true);
            c.LineTo(new Point(m.Cx + m.Fw, m.Cy - m.Fh * 0.4), false, true);
            c.LineTo(new Point(m.Cx + m.Fw, m.Cy + m.Fh * 0.4), false, true);
            c.LineTo(new Point(m.Cx + m.Fw * 0.1, m.Cy + m.Fh * 0.62), false, true);
            c.BezierTo(new Point(m.Cx + m.Fw * 0.4, m.Cy), new Point(m.Cx + m.Fw * 0.35, m.Cy - m.Fh * 0.3), new Point(m.Cx + m.Fw * 0.2, m.Cy - m.Fh * 0.6), false, true);
        }));
        ctx.Pop();
        FacialHairFlat(ctx, m, true);
        BrowsFilled(ctx, m);
        EyesComic(ctx, m);
        NoseInk(ctx, m);
        MouthInk(ctx, m);
        FrontHairFlat(ctx, m, true);
    }

    private static void Sketch(DrawingContext ctx, FaceModel m, Color paper, Color ink, bool pencil)
    {
        ctx.DrawRoundedRectangle(B(paper), null, new Rect(0, 0, 200, 200), 26, 26);
        var pen = new Pen(B(WithA(ink, pencil ? 0.7 : 0.9)), pencil ? 1.0 : 1.4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        // contour
        ctx.DrawGeometry(null, pen, m.Face);
        if (m.HasBackHair) ctx.DrawGeometry(null, pen, new EllipseGeometry(m.BackHair, m.BackHairRx, m.BackHairRy));
        // hatching shadow on the right + jaw
        ctx.PushClip(m.Face);
        var hp = new Pen(B(WithA(ink, pencil ? 0.28 : 0.4)), 0.8);
        hp.Freeze();
        Hatch(ctx, hp, new Rect(m.Cx + m.Fw * 0.2, m.Cy - m.Fh * 0.4, m.Fw * 0.9, m.Fh * 1.0), 3.2);
        Hatch(ctx, hp, new Rect(m.Cx - m.Fw, m.Cy + m.Fh * 0.28, m.Fw * 2, m.Fh * 0.34), 3.6);
        ctx.Pop();
        // features as line work
        FrontHairLines(ctx, m, pen);
        BrowsLines(ctx, m, pen);
        EyesLine(ctx, m, pen);
        NoseLine(ctx, m, pen);
        MouthLine(ctx, m, pen);
        if (m.Facial != 0) FacialHairLines(ctx, m, pen);
        Ears(ctx, m, soft: false, lineOnly: pen);
    }

    private static void Flat(DrawingContext ctx, FaceModel m)
    {
        ctx.DrawRoundedRectangle(B(m.BackA), null, new Rect(0, 0, 200, 200), 26, 26);
        Body(ctx, m, flat: true);
        if (m.HasBackHair) ctx.DrawEllipse(B(m.Hair), null, m.BackHair, m.BackHairRx, m.BackHairRy);
        ctx.DrawEllipse(B(m.Skin), null, new Point(m.Cx + m.Fw, m.Cy + 4), 6, 11);
        ctx.DrawEllipse(B(m.Skin), null, new Point(m.Cx - m.Fw, m.Cy + 4), 6, 11);
        ctx.DrawGeometry(B(m.Skin), null, m.Face);
        // single flat side shadow
        ctx.PushClip(m.Face);
        ctx.DrawRectangle(B(WithA(m.SkinShadow, 0.5)), null, new Rect(m.Cx + m.Fw * 0.3, 0, m.Fw, 200));
        ctx.Pop();
        if (m.Facial >= 3) FacialHairFlat(ctx, m, false);
        else if (m.Facial != 0) FacialHairFlat(ctx, m, false);
        BrowsFilled(ctx, m);
        // simple flat eyes
        foreach (int s in new[] { -1, 1 })
        {
            var e = new Point(m.Cx + s * m.EyeDx, m.EyeY);
            ctx.DrawEllipse(B(Rgb(0x2A2622)), null, e, m.Ew * 0.5, m.Ew * 0.5);
        }
        // simple nose + mouth
        ctx.DrawLine(P(m.SkinDeep, 1.6), new Point(m.Cx, m.Cy + m.Fh * 0.02), new Point(m.Cx, m.NoseTipY));
        ctx.DrawLine(P(m.LipDark, 2.4), new Point(m.Cx - m.Fw * 0.22, m.MouthY), new Point(m.Cx + m.Fw * 0.22, m.MouthY));
        FrontHairFlat(ctx, m, false);
    }

    private static void LowPoly(DrawingContext ctx, FaceModel m)
    {
        Backdrop(ctx, m, false);
        Body(ctx, m, flat: true);
        ctx.PushClip(m.Face);
        var rng = m.Rng;
        // Triangulate the face bounds with skin tones shaded by vertical position.
        int cols = 7, rows = 9;
        var pts = new Point[(cols + 1) * (rows + 1)];
        double x0 = m.Cx - m.Fw * 1.1, y0 = m.Cy - m.Fh * 0.7, w = m.Fw * 2.2, h = m.Fh * 1.5;
        for (int r = 0; r <= rows; r++)
            for (int col = 0; col <= cols; col++)
            {
                double jitter = (r == 0 || r == rows || col == 0 || col == cols) ? 0 : 6;
                pts[r * (cols + 1) + col] = new Point(
                    x0 + w * col / cols + (rng.NextDouble() - 0.5) * jitter,
                    y0 + h * r / rows + (rng.NextDouble() - 0.5) * jitter);
            }
        for (int r = 0; r < rows; r++)
            for (int col = 0; col < cols; col++)
            {
                var a = pts[r * (cols + 1) + col];
                var b = pts[r * (cols + 1) + col + 1];
                var cc = pts[(r + 1) * (cols + 1) + col];
                var d = pts[(r + 1) * (cols + 1) + col + 1];
                Tri(ctx, m, a, b, cc, rng);
                Tri(ctx, m, b, d, cc, rng);
            }
        ctx.Pop();
        ctx.DrawGeometry(null, P(m.SkinDeep, 0.6), m.Face);
        // minimal features so it still reads as a face
        BrowsFilled(ctx, m);
        foreach (int s in new[] { -1, 1 })
            ctx.DrawEllipse(B(Rgb(0x201A16)), null, new Point(m.Cx + s * m.EyeDx, m.EyeY), m.Ew * 0.42, m.Ew * 0.42);
        ctx.DrawLine(P(m.LipDark, 2), new Point(m.Cx - m.Fw * 0.2, m.MouthY), new Point(m.Cx + m.Fw * 0.2, m.MouthY));
        FrontHairFlat(ctx, m, false);
    }

    private static void Tri(DrawingContext ctx, FaceModel m, Point a, Point b, Point c, SimRandom rng)
    {
        double ny = (a.Y + b.Y + c.Y) / 3;
        double t = Math.Clamp((ny - (m.Cy - m.Fh * 0.6)) / (m.Fh * 1.2), 0, 1);
        var col = Mix(m.SkinHi, m.SkinDeep, t * 0.9 + (rng.NextDouble() - 0.5) * 0.12);
        ctx.DrawGeometry(B(col), null, Geo(g =>
        {
            g.BeginFigure(a, true, true);
            g.LineTo(b, false, true);
            g.LineTo(c, false, true);
        }));
    }

    // ============================================================== features ==

    private static void Backdrop(DrawingContext ctx, FaceModel m, bool warm)
    {
        var a = warm ? Mix(m.BackA, Rgb(0xF0D6C0), 0.4) : m.BackA;
        var b = warm ? Mix(m.BackB, Rgb(0x8A6E60), 0.3) : m.BackB;
        var rg = new RadialGradientBrush(a, b) { GradientOrigin = new Point(0.5, 0.4), Center = new Point(0.5, 0.42), RadiusX = 0.85, RadiusY = 0.85 };
        rg.Freeze();
        ctx.DrawRoundedRectangle(rg, null, new Rect(0, 0, 200, 200), 26, 26);
    }

    private static void Body(DrawingContext ctx, FaceModel m, bool flat = false)
    {
        var shirt = m.Shirt;
        Brush fill = flat ? B(shirt) : Frozen(new LinearGradientBrush(Lighten(shirt, 0.07), Shade(shirt, 0.82), 90));
        var body = Geo(c =>
        {
            c.BeginFigure(new Point(28, 200), true, true);
            c.BezierTo(new Point(32, 166), new Point(58, 154), new Point(m.Cx, 152), false, true);
            c.BezierTo(new Point(142, 154), new Point(168, 166), new Point(172, 200), false, true);
        });
        ctx.DrawGeometry(fill, null, body);
        var neckTop = m.Cy + m.Fh * 0.4;
        ctx.DrawGeometry(flat ? B(m.Skin) : Frozen(new LinearGradientBrush(m.SkinDeep, m.Skin, 0) { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) }), null, Geo(c =>
        {
            c.BeginFigure(new Point(m.Cx - 21, neckTop), true, true);
            c.LineTo(new Point(m.Cx - 23, 156), false, true);
            c.LineTo(new Point(m.Cx + 23, 156), false, true);
            c.LineTo(new Point(m.Cx + 21, neckTop), false, true);
        }));
        if (!flat) ctx.DrawEllipse(Soft(m.SkinDeep, 0.5), null, new Point(m.Cx, m.Cy + m.Fh * 0.5), m.Fw * 0.55, m.Fh * 0.16);
    }

    private static void Ears(DrawingContext ctx, FaceModel m, bool soft, Pen? lineOnly = null)
    {
        foreach (int s in new[] { -1, 1 })
        {
            var p = new Point(m.Cx + s * (m.Fw - 3), m.Cy + 4);
            if (lineOnly is not null) { ctx.DrawEllipse(null, lineOnly, p, m.Cauliflower ? 8 : 6, m.Cauliflower ? 12 : 11); continue; }
            if (m.Cauliflower)
            {
                var cc = Mix(m.Skin, Rgb(0xB0524A), 0.35);
                ctx.DrawEllipse(soft ? Radial(Lighten(cc, 0.1), Shade(cc, 0.85)) : B(cc), null, p, 9, 13);
                ctx.DrawEllipse(Soft(Shade(cc, 0.8), 0.6), null, p, 5, 7);
            }
            else
            {
                ctx.DrawEllipse(soft ? Radial(m.SkinHi, m.SkinShadow) : B(m.Skin), soft ? null : P(m.SkinDeep, 1), p, 6, 11);
                if (soft) ctx.DrawEllipse(Soft(m.SkinDeep, 0.5), null, new Point(p.X + s, p.Y), 3, 5);
            }
        }
    }

    private static void SkinTexture(DrawingContext ctx, FaceModel m)
    {
        var rng = m.Rng;
        for (int i = 0; i < 40; i++)
        {
            double x = m.Cx + (rng.NextDouble() - 0.5) * m.Fw * 1.6;
            double y = m.Cy + (rng.NextDouble() - 0.3) * m.Fh * 1.2;
            ctx.DrawEllipse(Soft(rng.NextDouble() < 0.5 ? m.SkinHi : m.SkinDeep, 0.06), null, new Point(x, y), 3, 3);
        }
    }

    private static void BrowsFilled(DrawingContext ctx, FaceModel m)
    {
        double y = m.BrowY, dx = m.EyeDx;
        foreach (int s in new[] { -1, 1 })
        {
            double xi = m.Cx + s * (dx - m.Fw * 0.18), xo = m.Cx + s * (dx + m.Fw * 0.2);
            ctx.DrawGeometry(B(m.HairDark), null, Geo(c =>
            {
                c.BeginFigure(new Point(xi, y + 1.4), true, true);
                c.BezierTo(new Point(m.Cx + s * dx, y - 3), new Point(m.Cx + s * (dx + m.Fw * 0.08), y - 2.6), new Point(xo, y + 0.6), false, true);
                c.BezierTo(new Point(m.Cx + s * (dx + m.Fw * 0.04), y + 1), new Point(m.Cx + s * dx, y + 1.5), new Point(xi, y + 1.4), false, true);
            }));
        }
    }

    private static void EyesRealistic(DrawingContext ctx, FaceModel m)
    {
        double eh = m.Ew * 0.5, gaze = (m.Rng.NextDouble() - 0.5) * 1.4;
        foreach (int s in new[] { -1, 1 })
        {
            var e = new Point(m.Cx + s * m.EyeDx, m.EyeY);
            ctx.DrawEllipse(Soft(m.SkinDeep, 0.28), null, new Point(e.X, e.Y + eh * 0.6), m.Ew * 1.3, eh * 1.8);
            var eye = Almond(e, m.Ew, eh);
            ctx.DrawGeometry(B(Rgb(0xEDE8DF)), null, eye);
            ctx.PushClip(eye);
            var ic = new Point(e.X + gaze, e.Y + eh * 0.1);
            double ir = eh * 1.05;
            ctx.DrawEllipse(Radial(Lighten(m.Iris, 0.25), Shade(m.Iris, 0.6)), P(Shade(m.Iris, 0.5), 0.6), ic, ir, ir);
            ctx.DrawEllipse(B(Rgb(0x140D08)), null, ic, ir * 0.5, ir * 0.5);
            ctx.DrawEllipse(B(WithA(White, 0.9)), null, new Point(ic.X - 1.2, ic.Y - 1.4), ir * 0.24, ir * 0.24);
            ctx.DrawGeometry(Soft(m.SkinDeep, 0.55), null, Almond(new Point(e.X, e.Y - eh * 0.55), m.Ew, eh * 1.15));
            ctx.Pop();
            ctx.DrawGeometry(null, P(Rgb(0x1A1512), 1.4), Geo(c =>
            {
                c.BeginFigure(new Point(e.X - m.Ew, e.Y), false, false);
                c.QuadraticBezierTo(new Point(e.X, e.Y - eh * 1.1), new Point(e.X + m.Ew, e.Y), true, false);
            }));
            ctx.DrawLine(P(WithA(m.SkinDeep, 0.5), 0.8), new Point(e.X - m.Ew * 0.7, e.Y + eh * 0.8), new Point(e.X + m.Ew * 0.7, e.Y + eh * 0.8));
        }
    }

    private static void EyesComic(DrawingContext ctx, FaceModel m)
    {
        double eh = m.Ew * 0.5;
        foreach (int s in new[] { -1, 1 })
        {
            var e = new Point(m.Cx + s * m.EyeDx, m.EyeY);
            var eye = Almond(e, m.Ew, eh);
            ctx.DrawGeometry(B(White), Ink(m, 1.6), eye);
            ctx.DrawEllipse(B(Shade(m.Iris, 0.8)), null, e, eh * 0.95, eh * 0.95);
            ctx.DrawEllipse(B(Black), null, e, eh * 0.5, eh * 0.5);
            ctx.DrawEllipse(B(White), null, new Point(e.X - 1.2, e.Y - 1.4), eh * 0.22, eh * 0.22);
        }
    }

    private static void EyesLine(DrawingContext ctx, FaceModel m, Pen pen)
    {
        double eh = m.Ew * 0.5;
        foreach (int s in new[] { -1, 1 })
        {
            var e = new Point(m.Cx + s * m.EyeDx, m.EyeY);
            ctx.DrawGeometry(null, pen, Almond(e, m.Ew, eh));
            ctx.DrawEllipse(null, pen, e, eh * 0.7, eh * 0.7);
            ctx.DrawEllipse(B(pen.Brush is SolidColorBrush sb ? sb.Color : Black), null, e, eh * 0.32, eh * 0.32);
        }
    }

    private static void NoseSoft(DrawingContext ctx, FaceModel m)
    {
        double tip = m.NoseTipY, nw = m.Fw * 0.16, bend = m.Crooked ? (m.Rng.NextDouble() < 0.5 ? 3 : -3) : 0;
        ctx.DrawEllipse(Soft(m.SkinDeep, 0.4), null, new Point(m.Cx - nw * 0.7 + bend, m.Cy + m.Fh * 0.06), nw * 0.7, m.Fh * 0.16);
        ctx.DrawEllipse(Soft(m.SkinHi, 0.5), null, new Point(m.Cx + bend * 0.4, tip - 2), nw * 0.6, m.Fh * 0.06);
        ctx.DrawEllipse(Soft(m.SkinDeep, 0.45), null, new Point(m.Cx + bend * 0.4, tip + 1), nw * 1.1, m.Fh * 0.07);
        foreach (int s in new[] { -1, 1 })
            ctx.DrawEllipse(B(Shade(m.SkinDeep, 0.7)), null, new Point(m.Cx + s * nw * 0.55 + bend * 0.3, tip + 0.5), 1.4, 1);
    }

    private static void NoseInk(DrawingContext ctx, FaceModel m)
    {
        double tip = m.NoseTipY, nw = m.Fw * 0.16;
        ctx.DrawGeometry(null, Ink(m, 1.4), Geo(c =>
        {
            c.BeginFigure(new Point(m.Cx - nw * 0.3, m.Cy + m.Fh * 0.02), false, false);
            c.QuadraticBezierTo(new Point(m.Cx - nw, tip), new Point(m.Cx, tip + 1), true, false);
            c.QuadraticBezierTo(new Point(m.Cx + nw, tip), new Point(m.Cx + nw * 0.3, m.Cy + m.Fh * 0.02), true, false);
        }));
    }

    private static void NoseLine(DrawingContext ctx, FaceModel m, Pen pen) => NoseInkPen(ctx, m, pen);
    private static void NoseInkPen(DrawingContext ctx, FaceModel m, Pen pen)
    {
        double tip = m.NoseTipY, nw = m.Fw * 0.16;
        ctx.DrawGeometry(null, pen, Geo(c =>
        {
            c.BeginFigure(new Point(m.Cx - nw * 0.4, tip - m.Fh * 0.1), false, false);
            c.LineTo(new Point(m.Cx - nw * 0.7, tip), false, false);
            c.QuadraticBezierTo(new Point(m.Cx, tip + 2), new Point(m.Cx + nw * 0.7, tip), true, false);
        }));
    }

    private static void MouthSoft(DrawingContext ctx, FaceModel m)
    {
        double y = m.MouthY, w = m.Fw * 0.32;
        ctx.DrawGeometry(B(m.LipDark), null, Geo(c =>
        {
            c.BeginFigure(new Point(m.Cx - w, y), true, true);
            c.QuadraticBezierTo(new Point(m.Cx - w * 0.5, y - 2.6), new Point(m.Cx, y - 1.2), false, true);
            c.QuadraticBezierTo(new Point(m.Cx + w * 0.5, y - 2.6), new Point(m.Cx + w, y), false, true);
            c.QuadraticBezierTo(new Point(m.Cx, y + 1), new Point(m.Cx - w, y), false, true);
        }));
        ctx.DrawGeometry(B(m.Lip), null, Geo(c =>
        {
            c.BeginFigure(new Point(m.Cx - w * 0.9, y + 0.5), true, true);
            c.QuadraticBezierTo(new Point(m.Cx, y + 5), new Point(m.Cx + w * 0.9, y + 0.5), false, true);
            c.QuadraticBezierTo(new Point(m.Cx, y + 2.2), new Point(m.Cx - w * 0.9, y + 0.5), false, true);
        }));
        ctx.DrawLine(P(WithA(m.LipDark, 0.9), 1.1), new Point(m.Cx - w, y), new Point(m.Cx + w, y));
    }

    private static void MouthInk(DrawingContext ctx, FaceModel m)
    {
        double y = m.MouthY, w = m.Fw * 0.3;
        ctx.DrawGeometry(B(m.Lip), Ink(m, 1.4), Geo(c =>
        {
            c.BeginFigure(new Point(m.Cx - w, y), true, true);
            c.QuadraticBezierTo(new Point(m.Cx, y - 2.4), new Point(m.Cx + w, y), false, true);
            c.QuadraticBezierTo(new Point(m.Cx, y + 4), new Point(m.Cx - w, y), false, true);
        }));
    }

    private static void MouthLine(DrawingContext ctx, FaceModel m, Pen pen)
    {
        double y = m.MouthY, w = m.Fw * 0.3;
        ctx.DrawGeometry(null, pen, Geo(c =>
        {
            c.BeginFigure(new Point(m.Cx - w, y), false, false);
            c.QuadraticBezierTo(new Point(m.Cx, y + 2.6), new Point(m.Cx + w, y), true, false);
        }));
    }

    // hair --------------------------------------------------------------------
    private static Geometry HairCap(FaceModel m)
    {
        var mask = Geo(c =>
        {
            c.BeginFigure(new Point(0, 0), true, true);
            c.LineTo(new Point(200, 0), false, true);
            c.LineTo(new Point(200, m.HairlineY - 6), false, true);
            c.QuadraticBezierTo(new Point(m.Cx, m.HairlineY + 8), new Point(0, m.HairlineY - 6), false, true);
        });
        var r = new CombinedGeometry(GeometryCombineMode.Intersect, m.Face, mask);
        r.Freeze();
        return r;
    }

    private static void FrontHairSoft(DrawingContext ctx, FaceModel m)
    {
        if (m.HairStyle == 0) { ctx.PushClip(m.Face); ctx.DrawEllipse(Soft(White, 0.14), null, new Point(m.Cx - m.Fw * 0.2, m.Cy - m.Fh * 0.4), m.Fw * 0.3, m.Fh * 0.2); ctx.Pop(); return; }
        var region = HairCap(m);
        ctx.DrawGeometry(Frozen(new LinearGradientBrush(m.HairHi, m.HairDark, 90)), null, region);
        ctx.PushClip(region);
        ctx.DrawEllipse(Soft(m.HairHi, 0.5), null, new Point(m.Cx - m.Fw * 0.2, m.HairlineY - m.Fh * 0.18), m.Fw * 0.3, m.Fh * 0.14);
        ctx.Pop();
    }

    private static void FrontHairFlat(DrawingContext ctx, FaceModel m, bool ink)
    {
        if (m.HairStyle == 0) return;
        var region = HairCap(m);
        ctx.DrawGeometry(B(m.Hair), ink ? Ink(m, 2) : null, region);
    }

    private static void FrontHairLines(DrawingContext ctx, FaceModel m, Pen pen)
    {
        if (m.HairStyle == 0) return;
        var region = HairCap(m);
        ctx.DrawGeometry(null, pen, region);
        ctx.PushClip(region);
        for (int i = 0; i < 14; i++)
        {
            double x = m.Cx + (m.Rng.NextDouble() - 0.5) * m.Fw * 1.6;
            ctx.DrawLine(new Pen(pen.Brush, 0.6), new Point(x, m.Cy - m.Fh * 0.55), new Point(x + (m.Rng.NextDouble() - 0.5) * 8, m.HairlineY));
        }
        ctx.Pop();
    }

    // facial hair -------------------------------------------------------------
    private static void FacialHairSoft(DrawingContext ctx, FaceModel m)
    {
        if (m.Facial == 0) return;
        ctx.PushClip(m.Face);
        switch (m.Facial)
        {
            case 1: ctx.DrawEllipse(FadeDown(m.HairDark, 0.34), null, new Point(m.Cx, m.Cy + m.Fh * 0.42), m.Fw * 0.92, m.Fh * 0.44); break;
            case 2: ctx.DrawEllipse(FadeDown(m.HairDark, 0.9), null, new Point(m.Cx, m.Cy + m.Fh * 0.32), m.Fw * 0.22, m.Fh * 0.06); break;
            case 3:
                ctx.DrawEllipse(FadeDown(m.HairDark, 0.95), null, new Point(m.Cx, m.Cy + m.Fh * 0.46), m.Fw * 0.26, m.Fh * 0.2);
                ctx.DrawEllipse(FadeDown(m.HairDark, 0.85), null, new Point(m.Cx, m.Cy + m.Fh * 0.32), m.Fw * 0.2, m.Fh * 0.06);
                break;
            default:
                ctx.DrawEllipse(FadeDown(m.HairDark, 1.0), null, new Point(m.Cx, m.Cy + m.Fh * 0.42), m.Fw * 0.92, m.Fh * 0.46);
                ctx.DrawEllipse(FadeDown(m.Hair, 0.55), null, new Point(m.Cx, m.Cy + m.Fh * 0.5), m.Fw * 0.66, m.Fh * 0.28);
                ctx.DrawEllipse(FadeDown(m.HairDark, 0.8), null, new Point(m.Cx, m.Cy + m.Fh * 0.32), m.Fw * 0.22, m.Fh * 0.06);
                break;
        }
        ctx.Pop();
    }

    private static void FacialHairFlat(DrawingContext ctx, FaceModel m, bool ink)
    {
        if (m.Facial == 0) return;
        double top = m.Facial == 3 ? m.Cy + m.Fh * 0.3 : m.Cy + m.Fh * 0.1;
        double width = m.Facial == 3 ? m.Fw * 0.3 : m.Fw * 0.95;
        var region = new CombinedGeometry(GeometryCombineMode.Intersect, m.Face, new RectangleGeometry(new Rect(m.Cx - width, top, width * 2, 120)));
        region.Freeze();
        ctx.DrawGeometry(B(m.HairDark), ink ? Ink(m, 1.6) : null, region);
    }

    private static void FacialHairLines(DrawingContext ctx, FaceModel m, Pen pen)
    {
        double top = m.Facial == 3 ? m.Cy + m.Fh * 0.32 : m.Cy + m.Fh * 0.14;
        double width = m.Facial == 3 ? m.Fw * 0.28 : m.Fw * 0.9;
        var region = new CombinedGeometry(GeometryCombineMode.Intersect, m.Face, new RectangleGeometry(new Rect(m.Cx - width, top, width * 2, 120)));
        region.Freeze();
        ctx.PushClip(region);
        for (int i = 0; i < 80; i++)
        {
            double x = m.Cx + (m.Rng.NextDouble() - 0.5) * width * 2;
            double y = top + m.Rng.NextDouble() * m.Fh * 0.5;
            ctx.DrawLine(new Pen(pen.Brush, 0.5), new Point(x, y), new Point(x + (m.Rng.NextDouble() - 0.5) * 2, y + 3));
        }
        ctx.Pop();
    }

    private static void BrowsLines(DrawingContext ctx, FaceModel m, Pen pen)
    {
        double y = m.BrowY, dx = m.EyeDx;
        foreach (int s in new[] { -1, 1 })
            ctx.DrawLine(new Pen(pen.Brush, 2), new Point(m.Cx + s * (dx - m.Fw * 0.18), y), new Point(m.Cx + s * (dx + m.Fw * 0.2), y - 1.5));
    }

    private static void Wear(DrawingContext ctx, FaceModel m)
    {
        if (m.Age < 34 && !m.Scarred) return;
        ctx.PushClip(m.Face);
        if (m.Age >= 34)
        {
            var line = P(WithA(m.SkinDeep, 0.3), 1);
            double fy = m.Cy - m.Fh * 0.32;
            for (int i = 0; i < (m.Age >= 44 ? 3 : 2); i++)
                ctx.DrawGeometry(null, line, Geo(c =>
                {
                    c.BeginFigure(new Point(m.Cx - m.Fw * 0.32, fy + i * 5), false, false);
                    c.QuadraticBezierTo(new Point(m.Cx, fy + i * 5 - 2.4), new Point(m.Cx + m.Fw * 0.32, fy + i * 5), true, false);
                }));
        }
        ctx.Pop();
        if (m.Scarred)
            ctx.DrawLine(P(WithA(Lighten(m.Skin, 0.22), 0.8), 1.3), new Point(m.Cx + m.Fw * 0.4, m.Cy - m.Fh * 0.22), new Point(m.Cx + m.Fw * 0.4, m.Cy - m.Fh * 0.08));
    }

    private static void Hatch(DrawingContext ctx, Pen pen, Rect r, double gap)
    {
        for (double d = -r.Height; d < r.Width; d += gap)
            ctx.DrawLine(pen, new Point(r.X + d, r.Y), new Point(r.X + d + r.Height, r.Y + r.Height));
    }

    // ============================================================ FaceModel ==
    private sealed class FaceModel
    {
        public readonly SimRandom Rng;
        public double Cx = 100, Cy, Fw, Fh, Jaw;
        public Geometry Face = Geometry.Empty;
        public double EyeY, EyeDx, Ew, BrowY, NoseTipY, MouthY, HairlineY;
        public Point BackHair; public double BackHairRx, BackHairRy; public bool HasBackHair;
        public int HairStyle, Facial, Age;
        public bool Cauliflower, Crooked, Scarred;
        public Color Skin, SkinShadow, SkinDeep, SkinHi, Lip, LipDark, Iris, Hair, HairHi, HairDark, Shirt, BackA, BackB;

        public FaceModel(Fighter f, int age)
        {
            Rng = new SimRandom(SimRandom.Mix(0xF00D_5EEDUL, (ulong)f.Id.Value));
            Age = age;

            Skin = Pick(Rng, SkinTones(f.Nationality));
            SkinShadow = Shade(Skin, 0.9); SkinDeep = Shade(Skin, 0.76); SkinHi = Lighten(Skin, 0.12);
            Lip = Mix(Skin, Rgb(0xB0605A), 0.5); LipDark = Shade(Lip, 0.82);
            Iris = Pick(Rng, EyeColors);
            var bh = Pick(Rng, HairColors);
            double grey = age >= 46 ? 0.65 : age >= 40 ? 0.42 : age >= 35 ? 0.22 : age >= 30 ? 0.07 : 0;
            Hair = Mix(bh, Rgb(0xB9B9B9), Math.Min(0.85, grey + Rng.NextDouble() * 0.1));
            HairHi = Lighten(Hair, 0.18); HairDark = Shade(Hair, 0.68);
            Shirt = Pick(Rng, ShirtColors);
            (BackA, BackB) = Backd(f.PrimaryStyle);

            double wc = (int)f.WeightClass / 8.0;
            Fw = 56 + wc * 12 + (Rng.NextDouble() - 0.5) * 6;
            Fh = 78 + wc * 4 + (Rng.NextDouble() - 0.5) * 6;
            Jaw = 0.6 + wc * 0.2 + Rng.NextDouble() * 0.1;
            Cy = 100;
            Face = Silhouette();

            // more human feature proportions (smaller eyes, positioned near mid-face)
            EyeY = Cy - Fh * 0.03; EyeDx = Fw * 0.4; Ew = Fw * 0.19;
            BrowY = Cy - Fh * 0.15;
            NoseTipY = Cy + Fh * 0.2;
            MouthY = Cy + Fh * 0.42;

            HairStyle = WeightedHair(Rng, age);
            Facial = WeightedFacial(Rng);
            HairlineY = Cy - Fh * (HairStyle == 3 ? 0.44 : HairStyle == 1 ? 0.32 : 0.37);
            HasBackHair = HairStyle is 2 or 4 or 5 or 7;
            BackHair = new Point(Cx, HairStyle == 5 ? Cy - Fh * 0.3 : Cy - Fh * 0.4);
            BackHairRx = HairStyle == 5 ? Fw * 1.16 : Fw * (HairStyle == 4 ? 1.05 : 0.98);
            BackHairRy = HairStyle == 5 ? Fh * 0.94 : Fh * 0.56;

            bool grap = f.PrimaryStyle is PrimaryStyle.Wrestler or PrimaryStyle.Grappler or PrimaryStyle.BrazilianJiuJitsu or PrimaryStyle.ClinchFighter;
            int bouts = f.Record.TotalBouts;
            Cauliflower = grap && Rng.Chance(0.3 + Math.Min(0.35, bouts * 0.01));
            Crooked = Rng.Chance(0.15 + (f.PrimaryStyle == PrimaryStyle.Brawler ? 0.22 : 0) + Math.Min(0.18, bouts * 0.004));
            Scarred = Rng.Chance(0.12 + (f.PrimaryStyle == PrimaryStyle.Brawler ? 0.25 : 0) + Math.Min(0.25, bouts * 0.007));
        }

        public void Recolor(Func<Color, Color> t)
        {
            Skin = t(Skin); SkinShadow = t(SkinShadow); SkinDeep = t(SkinDeep); SkinHi = t(SkinHi);
            Lip = t(Lip); LipDark = t(LipDark); Iris = t(Iris);
            Hair = t(Hair); HairHi = t(HairHi); HairDark = t(HairDark);
            Shirt = t(Shirt); BackA = t(BackA); BackB = t(BackB);
        }

        private Geometry Silhouette() => Geo(c =>
        {
            double top = Cy - Fh * 0.62, chin = Cy + Fh * 0.62;
            c.BeginFigure(new Point(Cx, top), true, true);
            c.BezierTo(new Point(Cx + Fw * 0.72, top + 2), new Point(Cx + Fw, Cy - Fh * 0.28), new Point(Cx + Fw, Cy - Fh * 0.02), false, true);
            c.BezierTo(new Point(Cx + Fw, Cy + Fh * 0.18), new Point(Cx + Fw * Jaw, Cy + Fh * 0.36), new Point(Cx + Fw * (Jaw * 0.72), Cy + Fh * 0.5), false, true);
            c.BezierTo(new Point(Cx + Fw * 0.32, chin - 2), new Point(Cx + Fw * 0.16, chin), new Point(Cx, chin), false, true);
            c.BezierTo(new Point(Cx - Fw * 0.16, chin), new Point(Cx - Fw * 0.32, chin - 2), new Point(Cx - Fw * (Jaw * 0.72), Cy + Fh * 0.5), false, true);
            c.BezierTo(new Point(Cx - Fw * Jaw, Cy + Fh * 0.36), new Point(Cx - Fw, Cy + Fh * 0.18), new Point(Cx - Fw, Cy - Fh * 0.02), false, true);
            c.BezierTo(new Point(Cx - Fw, Cy - Fh * 0.28), new Point(Cx - Fw * 0.72, top + 2), new Point(Cx, top), false, true);
        });
    }

    // ================================================================ colour ==
    private static Color Grayscale(Color c)
    {
        byte v = (byte)Math.Clamp(0.3 * c.R + 0.59 * c.G + 0.11 * c.B, 0, 255);
        return Color.FromArgb(c.A, v, v, v);
    }

    private static Color SepiaTone(Color c)
    {
        double r = 0.393 * c.R + 0.769 * c.G + 0.189 * c.B;
        double g = 0.349 * c.R + 0.686 * c.G + 0.168 * c.B;
        double b = 0.272 * c.R + 0.534 * c.G + 0.131 * c.B;
        return Color.FromArgb(c.A, Cl(r), Cl(g), Cl(b));
        static byte Cl(double x) => (byte)Math.Clamp(x, 0, 255);
    }

    // ================================================================ helpers ==
    private static readonly Color White = Color.FromRgb(255, 255, 255);
    private static readonly Color Black = Color.FromRgb(0, 0, 0);

    private static Pen Ink(FaceModel m, double w)
    {
        var p = new Pen(B(Rgb(0x241F1B)), w) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        p.Freeze();
        return p;
    }

    private static Geometry Almond(Point c, double ew, double eh) => Geo(g =>
    {
        g.BeginFigure(new Point(c.X - ew, c.Y), true, true);
        g.QuadraticBezierTo(new Point(c.X, c.Y - eh * 1.1), new Point(c.X + ew, c.Y), false, true);
        g.QuadraticBezierTo(new Point(c.X, c.Y + eh), new Point(c.X - ew, c.Y), false, true);
    });

    private static Geometry Geo(Action<StreamGeometryContext> build)
    {
        var g = new StreamGeometry();
        using (var c = g.Open()) build(c);
        g.Freeze();
        return g;
    }

    private static RadialGradientBrush Soft(Color c, double alpha)
    {
        var b = new RadialGradientBrush();
        b.GradientStops.Add(new GradientStop(WithA(c, alpha), 0));
        b.GradientStops.Add(new GradientStop(WithA(c, alpha * 0.6), 0.6));
        b.GradientStops.Add(new GradientStop(WithA(c, 0), 1));
        b.Freeze();
        return b;
    }

    private static LinearGradientBrush FadeDown(Color c, double bottomAlpha)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        b.GradientStops.Add(new GradientStop(WithA(c, 0), 0));
        b.GradientStops.Add(new GradientStop(WithA(c, bottomAlpha * 0.5), 0.45));
        b.GradientStops.Add(new GradientStop(WithA(c, bottomAlpha), 1));
        b.Freeze();
        return b;
    }

    private static RadialGradientBrush Radial(Color inner, Color outer)
    {
        var b = new RadialGradientBrush(inner, outer) { GradientOrigin = new Point(0.4, 0.35), Center = new Point(0.5, 0.45), RadiusX = 0.7, RadiusY = 0.7 };
        b.Freeze();
        return b;
    }

    private static Brush Frozen(Brush b) { b.Freeze(); return b; }

    private static int WeightedHair(SimRandom rng, int age)
    {
        double bald = 0.8 + Math.Max(0, age - 32) * 0.06, recede = 0.9 + Math.Max(0, age - 30) * 0.05;
        return rng.WeightedIndex(stackalloc double[] { bald, 1.5, 1.7, recede, 1.2, 0.5, 0.3, 0.45 });
    }
    private static int WeightedFacial(SimRandom rng) => rng.WeightedIndex(stackalloc double[] { 2.2, 1.5, 0.5, 1.0, 1.4 });

    private static Color[] SkinTones(Region region) => region switch
    {
        Region.Africa => new[] { Rgb(0x6E4B2C), Rgb(0x57402A), Rgb(0x7E5636), Rgb(0x4A3320) },
        Region.Asia => new[] { Rgb(0xE0B489), Rgb(0xD5A67C), Rgb(0xCA976D), Rgb(0xE9C49B) },
        Region.SouthAmerica => new[] { Rgb(0xC79A6E), Rgb(0xB78A60), Rgb(0xA8764C), Rgb(0xD6AB82) },
        Region.MiddleEast => new[] { Rgb(0xD2A579), Rgb(0xC1926A), Rgb(0xB07E54) },
        Region.Oceania => new[] { Rgb(0xD9A97E), Rgb(0xC08A5C), Rgb(0x9C6B44) },
        Region.Europe => new[] { Rgb(0xEDC6A0), Rgb(0xE5B88C), Rgb(0xDBA97A), Rgb(0xF0D0AC) },
        _ => new[] { Rgb(0xE9C0A0), Rgb(0xD6A67E), Rgb(0xB07A54), Rgb(0x8C5C3A) },
    };

    private static readonly Color[] HairColors = { Rgb(0x201811), Rgb(0x30231A), Rgb(0x4C3424), Rgb(0x6F4C2C), Rgb(0xA9772F), Rgb(0xC9A44D), Rgb(0x7C3020), Rgb(0x3C2C1C) };
    private static readonly Color[] EyeColors = { Rgb(0x5C3B20), Rgb(0x3C2B1A), Rgb(0x4C6943), Rgb(0x3C5C7A), Rgb(0x6C5438), Rgb(0x2C1E12) };
    private static readonly Color[] ShirtColors = { Rgb(0x2C3038), Rgb(0x35302C), Rgb(0x233038), Rgb(0x322838), Rgb(0x2A2A2E) };

    private static (Color, Color) Backd(PrimaryStyle s) => s switch
    {
        PrimaryStyle.Striker or PrimaryStyle.Kickboxer or PrimaryStyle.Brawler => (Rgb(0xEFE2DD), Rgb(0xC2A198)),
        PrimaryStyle.Wrestler or PrimaryStyle.Grappler or PrimaryStyle.ClinchFighter => (Rgb(0xE4EBF1), Rgb(0xA6BACC)),
        PrimaryStyle.BrazilianJiuJitsu => (Rgb(0xE6EFE7), Rgb(0xAAC6AE)),
        _ => (Rgb(0xECECEF), Rgb(0xBCBCC5)),
    };

    private static Color Rgb(int hex) => Color.FromRgb((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex);
    private static Color Mix(Color a, Color b, double t) { t = Math.Clamp(t, 0, 1); return Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t)); }
    private static Color Shade(Color c, double f) => Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));
    private static Color Lighten(Color c, double t) => Mix(c, White, t);
    private static Color WithA(Color c, double a) => Color.FromArgb((byte)(Math.Clamp(a, 0, 1) * 255), c.R, c.G, c.B);
    private static SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    private static Pen P(Color c, double w) { var p = new Pen(B(c), w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }; p.Freeze(); return p; }
    private static T Pick<T>(SimRandom rng, T[] a) => a[rng.Next(0, a.Length)];
}
