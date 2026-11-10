using System.Windows;
using System.Windows.Media;
using MmaManager.Core.Model;
using static MmaManager.Wpf.Art.FlatArt;

namespace MmaManager.Wpf.Art;

/// <summary>
/// The flat-illustration character generator: a face shot (200×200 card) and a
/// matching full-body shot (200×300 card) for any fighter, drawn procedurally
/// from <see cref="CharacterDna"/>. Deterministic per fighter id, so portraits
/// are stable across sessions; the combination space (skin × head shape × hair ×
/// facial hair × features × gear) is in the millions, so no two rosters look
/// stamped out. Frozen and cached by id + age.
/// </summary>
public static class CharacterFactory
{
    private static readonly Dictionary<long, ImageSource> FaceCache = new();
    private static readonly Dictionary<long, ImageSource> BodyCache = new();

    public static ImageSource Face(Fighter f, int age) =>
        Cached(FaceCache, f, age, d => Render(200, 200, ctx => DrawFaceShot(ctx, d)));

    public static ImageSource Body(Fighter f, int age) =>
        Cached(BodyCache, f, age, d => Render(200, 300, ctx => DrawBodyShot(ctx, d)));

    private static ImageSource Cached(Dictionary<long, ImageSource> cache, Fighter f, int age,
        Func<CharacterDna, ImageSource> render)
    {
        long key = (long)f.Id.Value << 8 | (uint)Math.Clamp(age, 0, 120);
        if (cache.TryGetValue(key, out var hit)) return hit;
        var img = render(CharacterDna.For(f, age));
        cache[key] = img;
        return img;
    }

    private static ImageSource Render(int cw, int ch, Action<DrawingContext> draw)
    {
        var dg = new DrawingGroup();
        using (var ctx = dg.Open())
        {
            var card = new RectangleGeometry(new Rect(0, 0, cw, ch), 26, 26);
            card.Freeze();
            ctx.PushClip(card);
            draw(ctx);
            ctx.Pop();
        }
        var img = new DrawingImage(dg);
        img.Freeze();
        return img;
    }

    // ------------------------------------------------------------ face shot --
    private static void DrawFaceShot(DrawingContext ctx, CharacterDna d)
    {
        ctx.DrawRectangle(B(d.Bg), null, new Rect(0, 0, 200, 200));

        const double cx = 100, cy = 90;
        double nw = d.FaceW * (d.Female ? 0.28 : 0.32) + d.Build * 1.5;   // neck half-width
        double sh = (d.Female ? 62 : 70) + d.Build * 8;                   // shoulder half-width

        // neck + chin shadow
        ctx.DrawRectangle(B(d.Skin), null, new Rect(cx - nw, cy + d.FaceH * 0.62, nw * 2, 200 - (cy + d.FaceH * 0.62)));
        ctx.DrawRectangle(B(d.SkinShadow), null, new Rect(cx - nw, cy + d.FaceH * 0.62, nw * 2, d.FaceH * 0.30));

        // shoulders — skin first (shows for tank tops), then the outfit
        var bust = BustGeo(cx, sh);
        if (d.Tank)
        {
            ctx.DrawGeometry(B(d.Skin), null, bust);
            ctx.DrawGeometry(B(d.Outfit), null, Geo(c =>
            {
                c.BeginFigure(new Point(cx - sh * 0.78, 200), true, true);
                c.BezierTo(new Point(cx - sh * 0.72, 186), new Point(cx - sh * 0.5, 180), new Point(cx - nw - 8, 176), true, true);
                c.LineTo(new Point(cx - nw - 2, 190), true, true);
                c.LineTo(new Point(cx + nw + 2, 190), true, true);
                c.LineTo(new Point(cx + nw + 8, 176), true, true);
                c.BezierTo(new Point(cx + sh * 0.5, 180), new Point(cx + sh * 0.72, 186), new Point(cx + sh * 0.78, 200), true, true);
            }));
        }
        else
        {
            ctx.DrawGeometry(B(d.Outfit), null, bust);
            ctx.DrawEllipse(B(d.OutfitDark), null, new Point(cx, 168), nw + 7, 7);   // collar
            ctx.DrawEllipse(B(d.Skin), null, new Point(cx, 165), nw + 3, 6);
        }

        FlatHead.Draw(ctx, d, cx, cy, 1.0);
    }

    private static Geometry BustGeo(double cx, double sh) => Geo(c =>
    {
        c.BeginFigure(new Point(cx - sh, 200), true, true);
        c.BezierTo(new Point(cx - sh, 176), new Point(cx - sh * 0.7, 161), new Point(cx - sh * 0.4, 158), true, true);
        c.LineTo(new Point(cx + sh * 0.4, 158), true, true);
        c.BezierTo(new Point(cx + sh * 0.7, 161), new Point(cx + sh, 176), new Point(cx + sh, 200), true, true);
    });

    // ------------------------------------------------------------ body shot --
    private static void DrawBodyShot(DrawingContext ctx, CharacterDna d)
    {
        ctx.DrawRectangle(B(d.Bg), null, new Rect(0, 0, 200, 300));
        ctx.DrawEllipse(B(WithA(Rgb(0x000000), 0.10)), null, new Point(100, 289), 56, 8);

        const double cx = 100, headY = 56, headScale = 0.58;
        double sh = ((d.Female ? 22 : 26) + d.Build * 6);                 // shoulder half-width
        double waist = d.Build == 3 ? sh * 0.95 : sh * (d.Female ? 0.58 : 0.66);
        double armT = (d.Female ? 8.5 : 10) + d.Build * 2.5;
        double legT = (d.Female ? 11 : 13) + d.Build * 3;
        double nw = d.FaceW * headScale * 0.34 + d.Build;

        var skin = B(d.Skin);
        var skinPen = P(d.Skin, armT);
        double chin = headY + d.FaceH * headScale;

        // neck
        ctx.DrawRectangle(skin, null, new Rect(cx - nw, chin - 6, nw * 2, 108 - chin + 6));
        ctx.DrawRectangle(B(d.SkinShadow), null, new Rect(cx - nw, chin - 6, nw * 2, 8));

        // arms (behind the torso edge, gloves drawn last)
        foreach (int s in new[] { -1, 1 })
        {
            ctx.DrawGeometry(null, skinPen, Geo(c =>
            {
                c.BeginFigure(new Point(cx + s * sh * 0.78, 112), false, false);
                c.BezierTo(new Point(cx + s * (sh + 7), 128), new Point(cx + s * (sh + 9), 142), new Point(cx + s * (sh + 6), 152), true, true);
                c.BezierTo(new Point(cx + s * (sh + 2), 164), new Point(cx + s * (sh * 0.85), 172), new Point(cx + s * (sh * 0.8), 182), true, true);
            }));
        }

        // torso
        ctx.DrawGeometry(skin, null, Geo(c =>
        {
            c.BeginFigure(new Point(cx - sh, 120), true, true);
            c.BezierTo(new Point(cx - sh, 106), new Point(cx - sh * 0.62, 99), new Point(cx - sh * 0.35, 98), true, true);
            c.LineTo(new Point(cx + sh * 0.35, 98), true, true);
            c.BezierTo(new Point(cx + sh * 0.62, 99), new Point(cx + sh, 106), new Point(cx + sh, 120), true, true);
            if (d.Build == 3)   // heavy: carry the width down with a slight belly
            {
                c.BezierTo(new Point(cx + sh * 1.0, 146), new Point(cx + waist * 1.08, 160), new Point(cx + waist, 176), true, true);
                c.LineTo(new Point(cx - waist, 176), true, true);
                c.BezierTo(new Point(cx - waist * 1.08, 160), new Point(cx - sh * 1.0, 146), new Point(cx - sh, 120), true, true);
            }
            else                // tapered
            {
                c.BezierTo(new Point(cx + sh * 0.94, 140), new Point(cx + waist * 1.15, 156), new Point(cx + waist, 176), true, true);
                c.LineTo(new Point(cx - waist, 176), true, true);
                c.BezierTo(new Point(cx - waist * 1.15, 156), new Point(cx - sh * 0.94, 140), new Point(cx - sh, 120), true, true);
            }
        }));

        // muscle definition (lean/athletic males only, kept subtle)
        if (!d.Female && d.Build <= 2 && d.Muscle > 0.45)
        {
            var mp = P(WithA(d.SkinLine, 0.30), 1.8);
            foreach (int s in new[] { -1, 1 })
                ctx.DrawGeometry(null, mp, Geo(c =>
                {
                    c.BeginFigure(new Point(cx + s * sh * 0.55, 126), false, false);
                    c.QuadraticBezierTo(new Point(cx + s * sh * 0.26, 132), new Point(cx + s * sh * 0.06, 128), true, true);
                }));
            if (d.Muscle > 0.65)
            {
                ctx.DrawLine(mp, new Point(cx, 136), new Point(cx, 166));
                ctx.DrawLine(mp, new Point(cx - 7, 144), new Point(cx + 7, 144));
                ctx.DrawLine(mp, new Point(cx - 6, 155), new Point(cx + 6, 155));
            }
        }

        // sports bra
        if (d.Female)
        {
            ctx.DrawGeometry(B(d.Outfit), null, Geo(c =>
            {
                c.BeginFigure(new Point(cx - sh * 0.98, 118), true, true);
                c.LineTo(new Point(cx + sh * 0.98, 118), true, true);
                c.BezierTo(new Point(cx + sh * 0.95, 132), new Point(cx + sh * 0.7, 140), new Point(cx, 140), true, true);
                c.BezierTo(new Point(cx - sh * 0.7, 140), new Point(cx - sh * 0.95, 132), new Point(cx - sh * 0.98, 118), true, true);
            }));
            foreach (int s in new[] { -1, 1 })
                ctx.DrawLine(P(d.Outfit, 5), new Point(cx + s * sh * 0.45, 120), new Point(cx + s * nw * 1.4, 100));
        }

        // legs under the trunks
        double hip = waist * 0.5;
        double shortsEnd = d.Female ? 202 : 212;
        foreach (int s in new[] { -1, 1 })
        {
            ctx.DrawGeometry(null, P(d.Skin, legT), Geo(c =>
            {
                c.BeginFigure(new Point(cx + s * hip, 196), false, false);
                c.BezierTo(new Point(cx + s * (hip + 3), 220), new Point(cx + s * (hip + 2), 236), new Point(cx + s * (hip + 1), 250), true, true);
            }));
            ctx.DrawGeometry(null, P(d.Skin, legT * 0.72), Geo(c =>
            {
                c.BeginFigure(new Point(cx + s * (hip + 1), 248), false, false);
                c.BezierTo(new Point(cx + s * (hip + 3), 262), new Point(cx + s * (hip + 1), 274), new Point(cx + s * hip, 284), true, true);
            }));
            ctx.DrawEllipse(skin, null, new Point(cx + s * (hip + 3), 287), 10, 4.5);
        }

        // trunks
        ctx.DrawGeometry(B(d.ShortsMain), null, Geo(c =>
        {
            c.BeginFigure(new Point(cx - waist, 172), true, true);
            c.LineTo(new Point(cx + waist, 172), true, true);
            c.LineTo(new Point(cx + waist + 7, shortsEnd - 4), true, true);
            c.LineTo(new Point(cx + waist * 0.28, shortsEnd), true, true);
            c.LineTo(new Point(cx, shortsEnd - 14), true, true);
            c.LineTo(new Point(cx - waist * 0.28, shortsEnd), true, true);
            c.LineTo(new Point(cx - waist - 7, shortsEnd - 4), true, true);
        }));
        ctx.DrawRectangle(B(d.ShortsAccent), null, new Rect(cx - waist, 170, waist * 2, 7));
        foreach (int s in new[] { -1, 1 })
            ctx.DrawLine(P(d.ShortsAccent, 3.6),
                new Point(cx + s * (waist + 1), 180), new Point(cx + s * (waist + 6), shortsEnd - 7));

        // gloves
        foreach (int s in new[] { -1, 1 })
        {
            var g = new Point(cx + s * sh * 0.8, 190);
            ctx.DrawEllipse(B(Shade(d.Glove, 0.85)), null, new Point(g.X, g.Y - 8), armT * 0.55, 6);   // wrist wrap
            ctx.DrawEllipse(B(d.Glove), null, g, armT * 0.72, armT * 0.78);
            ctx.DrawEllipse(B(Shade(d.Glove, 0.8)), null, new Point(g.X - s * armT * 0.5, g.Y + 2), armT * 0.28, armT * 0.34);
        }

        FlatHead.Draw(ctx, d, cx, headY, headScale);
    }
}
