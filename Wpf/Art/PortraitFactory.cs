using System.Windows;
using System.Windows.Media;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Wpf.Art;

/// <summary>
/// A parametric vector portrait generator. Each fighter gets a deterministic
/// face built from their id (stable) plus a few attributes (nationality → skin,
/// age → greying/wrinkles, weight class → face width, style/record → cauliflower
/// ears / scars). Rendered softly — radial-gradient skin shading and feathered
/// hair/beard edges rather than hard-clipped shapes — so faces read organically
/// instead of like flat masks. Frozen <see cref="DrawingImage"/>, cached by id + age.
/// </summary>
public static class PortraitFactory
{
    private static readonly Dictionary<long, ImageSource> Cache = new();
    private static readonly Color White = Color.FromRgb(255, 255, 255);
    private static readonly Color Black = Color.FromRgb(0, 0, 0);

    public static ImageSource ForFighter(Fighter f, int age)
    {
        long key = (long)f.Id.Value << 8 | (uint)Math.Clamp(age, 0, 120);
        if (Cache.TryGetValue(key, out var cached)) return cached;

        var group = new DrawingGroup();
        using (var ctx = group.Open())
            new Face(f, age).Draw(ctx);
        var image = new DrawingImage(group);
        image.Freeze();
        Cache[key] = image;
        return image;
    }

    private sealed class Face
    {
        private readonly SimRandom _r;
        private const double Cx = 100;
        private double _cy, _fw, _fh, _jaw;
        private Geometry _faceGeo = Geometry.Empty;

        private Color _skin, _shadow, _deep, _hi, _lip, _lipDark, _iris, _hair, _hairHi, _hairDark;
        private int _hairStyle, _facial, _age;
        private bool _cauliflower, _crooked, _scarred;
        private PrimaryStyle _style;

        public Face(Fighter f, int age)
        {
            _r = new SimRandom(SimRandom.Mix(0xFACE_5EEDUL, (ulong)f.Id.Value));
            _age = age;
            _style = f.PrimaryStyle;

            _skin = Pick(SkinTones(f.Nationality));
            _shadow = Shade(_skin, 0.9);
            _deep = Shade(_skin, 0.76);
            _hi = Lighten(_skin, 0.13);
            _lip = Mix(_skin, Rgb(0xB5615A), 0.55);
            _lipDark = Shade(_lip, 0.82);
            _iris = Pick(EyeColors);

            var baseHair = Pick(HairColors);
            double grey = age >= 46 ? 0.7 : age >= 40 ? 0.45 : age >= 35 ? 0.25 : age >= 30 ? 0.08 : 0;
            grey += _r.NextDouble() * 0.12;
            _hair = Mix(baseHair, Rgb(0xB9B9B9), Math.Min(0.85, grey));
            _hairHi = Lighten(_hair, 0.18);
            _hairDark = Shade(_hair, 0.7);

            double wc = (int)f.WeightClass / 8.0;
            _fw = 56 + wc * 12 + _r.Range(-3, 3);
            _fh = 74 + wc * 4 + _r.Range(-3, 3);
            _jaw = 0.6 + wc * 0.2 + _r.NextDouble() * 0.1;
            _cy = 96;

            _hairStyle = WeightedHair(_r, age);
            _facial = WeightedFacial(_r);

            bool grappler = f.PrimaryStyle is PrimaryStyle.Wrestler or PrimaryStyle.Grappler
                or PrimaryStyle.BrazilianJiuJitsu or PrimaryStyle.ClinchFighter;
            int bouts = f.Record.TotalBouts;
            _cauliflower = grappler && _r.Chance(0.3 + Math.Min(0.35, bouts * 0.01));
            _crooked = _r.Chance(0.15 + (f.PrimaryStyle == PrimaryStyle.Brawler ? 0.22 : 0) + Math.Min(0.18, bouts * 0.004));
            _scarred = _r.Chance(0.12 + (f.PrimaryStyle == PrimaryStyle.Brawler ? 0.25 : 0) + Math.Min(0.25, bouts * 0.007));

            _faceGeo = FaceSilhouette();
        }

        public void Draw(DrawingContext ctx)
        {
            Background(ctx);
            Body(ctx);
            HairBack(ctx);
            Ears(ctx);
            FaceBase(ctx);
            FacialHair(ctx);
            Brows(ctx);
            Eyes(ctx);
            Nose(ctx);
            Mouth(ctx);
            HairFront(ctx);
            Wear(ctx);
        }

        // --- backdrop + body ------------------------------------------------
        private void Background(DrawingContext ctx)
        {
            var (a, b) = Backdrop(_style);
            var rg = new RadialGradientBrush(a, b) { GradientOrigin = new Point(0.5, 0.4), Center = new Point(0.5, 0.42), RadiusX = 0.85, RadiusY = 0.85 };
            rg.Freeze();
            ctx.DrawRoundedRectangle(rg, null, new Rect(0, 0, 200, 200), 26, 26);
        }

        private void Body(DrawingContext ctx)
        {
            var shirt = Pick(ShirtColors);
            var grad = new LinearGradientBrush(Lighten(shirt, 0.07), Shade(shirt, 0.82), 90);
            grad.Freeze();
            ctx.DrawGeometry(grad, null, Geo(c =>
            {
                c.BeginFigure(new Point(28, 200), true, true);
                c.BezierTo(new Point(32, 166), new Point(58, 154), new Point(Cx, 152), false, true);
                c.BezierTo(new Point(142, 154), new Point(168, 166), new Point(172, 200), false, true);
            }));

            double neckTop = _cy + _fh * 0.4;
            var neck = new LinearGradientBrush(_deep, _skin, 0) { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            neck.Freeze();
            ctx.DrawGeometry(neck, null, Geo(c =>
            {
                c.BeginFigure(new Point(Cx - 21, neckTop), true, true);
                c.LineTo(new Point(Cx - 23, 156), false, true);
                c.LineTo(new Point(Cx + 23, 156), false, true);
                c.LineTo(new Point(Cx + 21, neckTop), false, true);
            }));
            // soft shadow where the jaw meets the neck
            ctx.DrawEllipse(Soft(_deep, 0.5), null, new Point(Cx, _cy + _fh * 0.5), _fw * 0.55, _fh * 0.16);
        }

        // --- ears -----------------------------------------------------------
        private void Ears(DrawingContext ctx)
        {
            foreach (int s in new[] { -1, 1 })
            {
                double ex = Cx + s * (_fw - 3), ey = _cy + 4;
                if (_cauliflower)
                {
                    var cc = Mix(_skin, Rgb(0xB0524A), 0.35);
                    ctx.DrawEllipse(Radial(Lighten(cc, 0.1), Shade(cc, 0.85)), null, new Point(ex, ey), 9, 13);
                    ctx.DrawEllipse(Soft(Shade(cc, 0.8), 0.6), null, new Point(ex, ey), 5, 7);
                }
                else
                {
                    ctx.DrawEllipse(Radial(_hi, _shadow), null, new Point(ex, ey), 7, 12);
                    ctx.DrawEllipse(Soft(_deep, 0.5), null, new Point(ex + s, ey), 3.5, 6);
                }
            }
        }

        // --- face base + soft shading --------------------------------------
        private Geometry FaceSilhouette() => Geo(c =>
        {
            double top = _cy - _fh * 0.62, chin = _cy + _fh * 0.62;
            c.BeginFigure(new Point(Cx, top), true, true);
            c.BezierTo(new Point(Cx + _fw * 0.72, top + 2), new Point(Cx + _fw, _cy - _fh * 0.28), new Point(Cx + _fw, _cy - _fh * 0.02), false, true);
            c.BezierTo(new Point(Cx + _fw, _cy + _fh * 0.18), new Point(Cx + _fw * _jaw, _cy + _fh * 0.36), new Point(Cx + _fw * (_jaw * 0.72), _cy + _fh * 0.5), false, true);
            c.BezierTo(new Point(Cx + _fw * 0.32, chin - 2), new Point(Cx + _fw * 0.16, chin), new Point(Cx, chin), false, true);
            c.BezierTo(new Point(Cx - _fw * 0.16, chin), new Point(Cx - _fw * 0.32, chin - 2), new Point(Cx - _fw * (_jaw * 0.72), _cy + _fh * 0.5), false, true);
            c.BezierTo(new Point(Cx - _fw * _jaw, _cy + _fh * 0.36), new Point(Cx - _fw, _cy + _fh * 0.18), new Point(Cx - _fw, _cy - _fh * 0.02), false, true);
            c.BezierTo(new Point(Cx - _fw, _cy - _fh * 0.28), new Point(Cx - _fw * 0.72, top + 2), new Point(Cx, top), false, true);
        });

        private void FaceBase(DrawingContext ctx)
        {
            // Rounded 3-D shading from a single radial gradient (lit from the forehead).
            var skin = new RadialGradientBrush
            {
                GradientOrigin = new Point(0.5, 0.4),
                Center = new Point(0.5, 0.42),
                RadiusX = 0.62,
                RadiusY = 0.66,
            };
            skin.GradientStops.Add(new GradientStop(_hi, 0));
            skin.GradientStops.Add(new GradientStop(_skin, 0.62));
            skin.GradientStops.Add(new GradientStop(_shadow, 0.92));
            skin.GradientStops.Add(new GradientStop(_deep, 1));
            skin.Freeze();
            ctx.DrawGeometry(skin, P(_deep, 0.8), _faceGeo);

            ctx.PushClip(_faceGeo);
            // soft contour: cheekbones, jaw, temples — all feathered blobs
            foreach (int s in new[] { -1, 1 })
            {
                ctx.DrawEllipse(Soft(_deep, 0.35), null, new Point(Cx + s * _fw * 0.7, _cy + _fh * 0.28), _fw * 0.32, _fh * 0.26); // jaw
                ctx.DrawEllipse(Soft(_deep, 0.22), null, new Point(Cx + s * _fw * 0.78, _cy - _fh * 0.3), _fw * 0.22, _fh * 0.2);  // temple
            }
            ctx.DrawEllipse(Soft(_deep, 0.28), null, new Point(Cx, _cy + _fh * 0.56), _fw * 0.4, _fh * 0.14);                     // under chin
            // highlights: forehead, nose bridge, cheeks
            ctx.DrawEllipse(Soft(_hi, 0.5), null, new Point(Cx, _cy - _fh * 0.42), _fw * 0.4, _fh * 0.2);
            ctx.DrawEllipse(Soft(_hi, 0.45), null, new Point(Cx, _cy + _fh * 0.02), _fw * 0.12, _fh * 0.26);
            foreach (int s in new[] { -1, 1 })
            {
                ctx.DrawEllipse(Soft(_hi, 0.3), null, new Point(Cx + s * _fw * 0.42, _cy + _fh * 0.06), _fw * 0.2, _fh * 0.14);
                ctx.DrawEllipse(Soft(Rgb(0xC8695E), 0.14), null, new Point(Cx + s * _fw * 0.48, _cy + _fh * 0.18), _fw * 0.18, _fh * 0.12); // blush
            }
            ctx.Pop();
        }

        // --- brows (clean filled shape) ------------------------------------
        private void Brows(DrawingContext ctx)
        {
            double y = _cy - _fh * 0.15, dx = _fw * 0.44;
            foreach (int s in new[] { -1, 1 })
            {
                double xi = Cx + s * (dx - _fw * 0.2), xo = Cx + s * (dx + _fw * 0.22);
                ctx.DrawGeometry(B(_hairDark), null, Geo(c =>
                {
                    c.BeginFigure(new Point(xi, y + 1.5), true, true);
                    c.BezierTo(new Point(Cx + s * dx, y - 3.5), new Point(Cx + s * (dx + _fw * 0.1), y - 3), new Point(xo, y + 0.5), false, true);
                    c.BezierTo(new Point(Cx + s * (dx + _fw * 0.05), y + 1), new Point(Cx + s * dx, y + 1.5), new Point(xi, y + 1.5), false, true);
                }));
            }
        }

        // --- eyes -----------------------------------------------------------
        private void Eyes(DrawingContext ctx)
        {
            double y = _cy - _fh * 0.02, dx = _fw * 0.42, ew = _fw * 0.25, eh = ew * 0.52;
            double gaze = R(-1, 1);
            foreach (int s in new[] { -1, 1 })
            {
                var cE = new Point(Cx + s * dx, y);
                ctx.DrawEllipse(Soft(_deep, 0.3), null, cE, ew * 1.4, eh * 1.9);          // socket
                var eye = Almond(cE, ew, eh);
                ctx.DrawGeometry(B(Rgb(0xF4F1EB)), null, eye);
                ctx.PushClip(eye);
                var ic = new Point(cE.X + gaze, cE.Y + eh * 0.12);
                double ir = eh * 1.15;
                ctx.DrawEllipse(Radial(Lighten(_iris, 0.28), Shade(_iris, 0.62)), P(Shade(_iris, 0.5), 0.7), ic, ir, ir);
                ctx.DrawEllipse(B(Rgb(0x120C08)), null, ic, ir * 0.46, ir * 0.46);
                ctx.DrawEllipse(B(WithA(White, 0.9)), null, new Point(ic.X - 1.4, ic.Y - 1.7), ir * 0.22, ir * 0.22);
                ctx.DrawGeometry(Soft(_deep, 0.5), null, Almond(new Point(cE.X, cE.Y - eh * 0.5), ew, eh * 1.1)); // lid shadow
                ctx.Pop();
                // upper lash line
                ctx.DrawGeometry(null, P(Rgb(0x1A1512), 1.6), Geo(c =>
                {
                    c.BeginFigure(new Point(cE.X - ew, cE.Y), false, false);
                    c.QuadraticBezierTo(new Point(cE.X, cE.Y - eh * 1.15), new Point(cE.X + ew, cE.Y), true, false);
                }));
                ctx.DrawLine(P(WithA(_deep, 0.5), 0.9), new Point(cE.X - ew * 0.7, cE.Y + eh * 0.7), new Point(cE.X + ew * 0.7, cE.Y + eh * 0.7));
            }
        }

        private Geometry Almond(Point c, double ew, double eh) => Geo(g =>
        {
            g.BeginFigure(new Point(c.X - ew, c.Y), true, true);
            g.QuadraticBezierTo(new Point(c.X, c.Y - eh * 1.15), new Point(c.X + ew, c.Y), false, true);
            g.QuadraticBezierTo(new Point(c.X, c.Y + eh * 1.05), new Point(c.X - ew, c.Y), false, true);
        });

        // --- nose -----------------------------------------------------------
        private void Nose(DrawingContext ctx)
        {
            double topY = _cy - _fh * 0.06, tipY = _cy + _fh * 0.2, nw = _fw * 0.18;
            double bend = _crooked ? R(2, 4) * (_r.Chance(0.5) ? 1 : -1) : 0;
            ctx.DrawEllipse(Soft(_deep, 0.4), null, new Point(Cx - nw * 0.7 + bend, _cy + _fh * 0.05), nw * 0.7, _fh * 0.16); // side shadow
            ctx.DrawEllipse(Soft(_hi, 0.5), null, new Point(Cx + bend * 0.4, tipY - 2), nw * 0.6, _fh * 0.07);                 // tip highlight
            ctx.DrawEllipse(Soft(_deep, 0.45), null, new Point(Cx + bend * 0.4, tipY + 1), nw * 1.1, _fh * 0.08);             // under-tip
            foreach (int s in new[] { -1, 1 })
                ctx.DrawEllipse(B(Shade(_deep, 0.7)), null, new Point(Cx + s * nw * 0.55 + bend * 0.3, tipY + 0.5), 1.5, 1.1); // nostrils
        }

        // --- mouth ----------------------------------------------------------
        private void Mouth(DrawingContext ctx)
        {
            double y = _cy + _fh * 0.42, w = _fw * 0.38;
            ctx.DrawEllipse(Soft(_deep, 0.28), null, new Point(Cx, y - 5), w * 0.3, 3); // philtrum shadow
            ctx.DrawGeometry(B(_lipDark), null, Geo(c =>
            {
                c.BeginFigure(new Point(Cx - w, y), true, true);
                c.QuadraticBezierTo(new Point(Cx - w * 0.5, y - 3), new Point(Cx, y - 1.4), false, true);
                c.QuadraticBezierTo(new Point(Cx + w * 0.5, y - 3), new Point(Cx + w, y), false, true);
                c.QuadraticBezierTo(new Point(Cx, y + 1), new Point(Cx - w, y), false, true);
            }));
            ctx.DrawGeometry(B(_lip), null, Geo(c =>
            {
                c.BeginFigure(new Point(Cx - w * 0.9, y + 0.5), true, true);
                c.QuadraticBezierTo(new Point(Cx, y + 6), new Point(Cx + w * 0.9, y + 0.5), false, true);
                c.QuadraticBezierTo(new Point(Cx, y + 2.5), new Point(Cx - w * 0.9, y + 0.5), false, true);
            }));
            ctx.DrawLine(P(WithA(_lipDark, 0.9), 1.2), new Point(Cx - w, y), new Point(Cx + w, y));
            ctx.DrawEllipse(Soft(White, 0.22), null, new Point(Cx, y + 3.4), w * 0.4, 1.6);
        }

        // --- hair -----------------------------------------------------------
        private void HairBack(DrawingContext ctx)
        {
            if (_hairStyle is 0 or 1 or 3 or 6) return;
            if (_hairStyle == 5)
            {
                ctx.DrawEllipse(Radial(_hair, _hairDark), null, new Point(Cx, _cy - _fh * 0.3), _fw * 1.16, _fh * 0.94);
                return;
            }
            double spread = _hairStyle == 4 ? 1.05 : 0.98;
            ctx.DrawEllipse(Radial(_hair, _hairDark), null, new Point(Cx, _cy - _fh * 0.4), _fw * spread, _fh * 0.56 * spread);
        }

        private void HairFront(DrawingContext ctx)
        {
            if (_hairStyle == 0) { Bald(ctx); return; }
            double crown = _cy - _fh * 0.62;
            if (_hairStyle == 6) { Mohawk(ctx, crown); return; }
            if (_hairStyle == 5) { Afro(ctx); return; }

            double hairlineY = _cy - _fh * (_hairStyle == 3 ? 0.44 : _hairStyle == 1 ? 0.32 : 0.36);
            // Cap = top of the face above a *curved* hairline (lower at centre — natural),
            // clipped to the face so it never sticks out past the temples.
            var mask = Geo(c =>
            {
                c.BeginFigure(new Point(0, 0), true, true);
                c.LineTo(new Point(200, 0), false, true);
                c.LineTo(new Point(200, hairlineY - 6), false, true);
                c.QuadraticBezierTo(new Point(Cx, hairlineY + 8), new Point(0, hairlineY - 6), false, true);
            });
            var region = new CombinedGeometry(GeometryCombineMode.Intersect, _faceGeo, mask);
            region.Freeze();

            var grad = new LinearGradientBrush(_hairHi, _hairDark, 90);
            grad.Freeze();
            ctx.DrawGeometry(grad, null, region);
            // soft sheen
            ctx.PushClip(region);
            ctx.DrawEllipse(Soft(_hairHi, 0.5), null, new Point(Cx - _fw * 0.2, crown + _fh * 0.12), _fw * 0.3, _fh * 0.16);
            ctx.Pop();

            if (_hairStyle is 2 or 4 or 7)
                foreach (int s in new[] { -1, 1 })   // soft sideburns
                    ctx.DrawGeometry(B(_hair), null, new CombinedGeometry(GeometryCombineMode.Intersect, _faceGeo,
                        new RectangleGeometry(new Rect(Cx + s * _fw * 0.7, hairlineY, _fw * 0.2, _fh * 0.28))));

            if (_hairStyle == 7)
                ctx.DrawEllipse(Radial(_hair, _hairDark), null, new Point(Cx, crown - 2), 9, 9);
        }

        private void Bald(DrawingContext ctx)
        {
            ctx.PushClip(_faceGeo);
            ctx.DrawEllipse(Soft(White, 0.16), null, new Point(Cx - _fw * 0.18, _cy - _fh * 0.4), _fw * 0.32, _fh * 0.2);
            ctx.Pop();
        }

        private void Mohawk(DrawingContext ctx, double crown)
        {
            var strip = Geo(c =>
            {
                c.BeginFigure(new Point(Cx - 9, _cy - _fh * 0.35), true, true);
                c.BezierTo(new Point(Cx - 12, crown - 10), new Point(Cx - 5, crown - 14), new Point(Cx, crown - 14), false, true);
                c.BezierTo(new Point(Cx + 5, crown - 14), new Point(Cx + 12, crown - 10), new Point(Cx + 9, _cy - _fh * 0.35), false, true);
            });
            ctx.DrawGeometry(new LinearGradientBrush(_hairHi, _hairDark, 90) { }, null, strip);
        }

        private void Afro(DrawingContext ctx)
        {
            var afro = new EllipseGeometry(new Point(Cx, _cy - _fh * 0.3), _fw * 1.16, _fh * 0.94);
            var visible = new CombinedGeometry(GeometryCombineMode.Exclude, afro, _faceGeo);
            visible.Freeze();
            ctx.PushClip(visible);
            for (int i = 0; i < 90; i++)
            {
                double ang = R(0, Math.PI * 2), rad = Math.Sqrt(R(0, 1));
                var p = new Point(Cx + Math.Cos(ang) * _fw * 1.1 * rad, (_cy - _fh * 0.3) + Math.Sin(ang) * _fh * 0.9 * rad);
                ctx.DrawEllipse(Soft(_r.Chance(0.5) ? _hairHi : _hairDark, 0.5), null, p, R(4, 6), R(4, 6));
            }
            ctx.Pop();
        }

        // --- facial hair (soft, feathered — no mask edge) ------------------
        private void FacialHair(DrawingContext ctx)
        {
            if (_facial == 0) return;

            if (_facial == 2) // mustache
            {
                ctx.PushClip(_faceGeo);
                ctx.DrawEllipse(FadeDown(_hairDark, 0.0, 0.9), null, new Point(Cx, _cy + _fh * 0.34), _fw * 0.24, _fh * 0.07);
                ctx.Pop();
                return;
            }

            // Region shapes fade in from the top via a vertical alpha gradient, so the
            // upper edge blends into the skin instead of forming a hard mask line.
            ctx.PushClip(_faceGeo);
            if (_facial == 1) // stubble: faint shadow over the lower face
            {
                ctx.DrawEllipse(FadeDown(_hairDark, 0.0, 0.34), null, new Point(Cx, _cy + _fh * 0.42), _fw * 0.95, _fh * 0.44);
            }
            else if (_facial == 3) // goatee
            {
                ctx.DrawEllipse(FadeDown(_hairDark, 0.0, 0.95), null, new Point(Cx, _cy + _fh * 0.46), _fw * 0.28, _fh * 0.2);
                ctx.DrawEllipse(FadeDown(_hairDark, 0.0, 0.85), null, new Point(Cx, _cy + _fh * 0.34), _fw * 0.22, _fh * 0.06); // mustache
            }
            else // full beard
            {
                ctx.DrawEllipse(FadeDown(_hairDark, 0.0, 1.0), null, new Point(Cx, _cy + _fh * 0.42), _fw * 0.95, _fh * 0.46);
                ctx.DrawEllipse(FadeDown(_hair, 0.0, 0.6), null, new Point(Cx, _cy + _fh * 0.5), _fw * 0.7, _fh * 0.28); // lighter core
                ctx.DrawEllipse(FadeDown(_hairDark, 0.0, 0.8), null, new Point(Cx, _cy + _fh * 0.32), _fw * 0.24, _fh * 0.07); // mustache
            }
            ctx.Pop();
        }

        // --- wear -----------------------------------------------------------
        private void Wear(DrawingContext ctx)
        {
            ctx.PushClip(_faceGeo);
            if (_age >= 34)
            {
                var line = P(WithA(_deep, 0.3), 1);
                double fy = _cy - _fh * 0.32;
                for (int i = 0; i < (_age >= 44 ? 3 : 2); i++)
                    ctx.DrawGeometry(null, line, Geo(c =>
                    {
                        c.BeginFigure(new Point(Cx - _fw * 0.34, fy + i * 5), false, false);
                        c.QuadraticBezierTo(new Point(Cx, fy + i * 5 - 2.5), new Point(Cx + _fw * 0.34, fy + i * 5), true, false);
                    }));
                foreach (int s in new[] { -1, 1 })
                    ctx.DrawGeometry(null, line, Geo(c =>
                    {
                        c.BeginFigure(new Point(Cx + s * _fw * 0.24, _cy + _fh * 0.2), false, false);
                        c.QuadraticBezierTo(new Point(Cx + s * _fw * 0.36, _cy + _fh * 0.3), new Point(Cx + s * _fw * 0.32, _cy + _fh * 0.44), true, false);
                    }));
            }
            ctx.Pop();

            if (_scarred)
            {
                var scar = P(WithA(Lighten(_skin, 0.22), 0.8), 1.3);
                int s = _r.Chance(0.5) ? -1 : 1;
                if (_r.Chance(0.5))
                    ctx.DrawLine(scar, new Point(Cx + s * _fw * 0.4, _cy - _fh * 0.22), new Point(Cx + s * _fw * 0.4, _cy - _fh * 0.1));
                else
                    ctx.DrawLine(scar, new Point(Cx + s * _fw * 0.48, _cy + _fh * 0.06), new Point(Cx + s * _fw * 0.6, _cy + _fh * 0.22));
            }
        }

        // --- helpers --------------------------------------------------------
        private double R(double lo, double hi) => lo + _r.NextDouble() * (hi - lo);
        private T Pick<T>(T[] a) => a[_r.Next(0, a.Length)];

        // Radial blob that fades to transparent at its edge (for soft shading).
        private static RadialGradientBrush Soft(Color c, double alpha)
        {
            var b = new RadialGradientBrush();
            b.GradientStops.Add(new GradientStop(WithA(c, alpha), 0));
            b.GradientStops.Add(new GradientStop(WithA(c, alpha * 0.6), 0.6));
            b.GradientStops.Add(new GradientStop(WithA(c, 0), 1));
            b.Freeze();
            return b;
        }

        // Vertical gradient (transparent at top → opaque below) for feathered hair/beard.
        private static LinearGradientBrush FadeDown(Color c, double topAlpha, double bottomAlpha)
        {
            var b = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
            b.GradientStops.Add(new GradientStop(WithA(c, topAlpha), 0));
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
    }

    // ---------------------------------------------------------------- static --
    private static Geometry Geo(Action<StreamGeometryContext> build)
    {
        var g = new StreamGeometry();
        using (var c = g.Open()) build(c);
        g.Freeze();
        return g;
    }

    private static int WeightedHair(SimRandom rng, int age)
    {
        double bald = 0.8 + Math.Max(0, age - 32) * 0.06;
        double recede = 0.9 + Math.Max(0, age - 30) * 0.05;
        return rng.WeightedIndex(stackalloc double[] { bald, 1.5, 1.7, recede, 1.2, 0.6, 0.3, 0.45 });
    }

    private static int WeightedFacial(SimRandom rng) =>
        rng.WeightedIndex(stackalloc double[] { 2.2, 1.5, 0.5, 1.0, 1.4 });

    private static Color[] SkinTones(Region region) => region switch
    {
        Region.Africa => new[] { Rgb(0x6E4B2C), Rgb(0x57402A), Rgb(0x7E5636), Rgb(0x4A3320) },
        Region.Asia => new[] { Rgb(0xE3B98E), Rgb(0xD9AA80), Rgb(0xCE9A70), Rgb(0xEDC89F) },
        Region.SouthAmerica => new[] { Rgb(0xCB9E72), Rgb(0xBC8E62), Rgb(0xAD7A4E), Rgb(0xDBB086) },
        Region.MiddleEast => new[] { Rgb(0xD6A97D), Rgb(0xC5966E), Rgb(0xB48258) },
        Region.Oceania => new[] { Rgb(0xDDAD82), Rgb(0xC48E60), Rgb(0xA06F48) },
        Region.Europe => new[] { Rgb(0xF2CBA4), Rgb(0xEBBD90), Rgb(0xE0AB7E), Rgb(0xF6D6B2) },
        _ => new[] { Rgb(0xEFC4A4), Rgb(0xDBAB82), Rgb(0xB47E56), Rgb(0x90603C) },
    };

    private static readonly Color[] HairColors =
    {
        Rgb(0x1E1712), Rgb(0x30231A), Rgb(0x4C3424), Rgb(0x6F4C2C),
        Rgb(0xA9772F), Rgb(0xCBA44D), Rgb(0x7C3020), Rgb(0x3C2C1C),
    };

    private static readonly Color[] EyeColors =
    {
        Rgb(0x5C3B20), Rgb(0x3C2B1A), Rgb(0x4C6943), Rgb(0x3C5C7A), Rgb(0x6C5438), Rgb(0x2C1E12),
    };

    private static readonly Color[] ShirtColors =
    {
        Rgb(0x2C3038), Rgb(0x35302C), Rgb(0x233038), Rgb(0x322838), Rgb(0x2A2A2E),
    };

    private static (Color a, Color b) Backdrop(PrimaryStyle style) => style switch
    {
        PrimaryStyle.Striker or PrimaryStyle.Kickboxer or PrimaryStyle.Brawler => (Rgb(0xF6EAE6), Rgb(0xCBA79E)),
        PrimaryStyle.Wrestler or PrimaryStyle.Grappler or PrimaryStyle.ClinchFighter => (Rgb(0xE9F0F6), Rgb(0xA9BFD2)),
        PrimaryStyle.BrazilianJiuJitsu => (Rgb(0xEAF3EB), Rgb(0xAECBB2)),
        _ => (Rgb(0xF0F0F3), Rgb(0xBFBFC8)),
    };

    private static Color Rgb(int hex) => Color.FromRgb((byte)(hex >> 16), (byte)(hex >> 8), (byte)hex);
    private static Color Mix(Color a, Color b, double t) =>
        Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
    private static Color Shade(Color c, double f) => Color.FromRgb((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));
    private static Color Lighten(Color c, double t) => Mix(c, White, t);
    private static Color WithA(Color c, double a) => Color.FromArgb((byte)(Math.Clamp(a, 0, 1) * 255), c.R, c.G, c.B);

    private static SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    private static Pen P(Color c, double w)
    {
        var p = new Pen(B(c), w) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        p.Freeze();
        return p;
    }
}
