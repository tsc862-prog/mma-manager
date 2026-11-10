using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Flavor;

namespace MmaManager.Wpf.Art;

/// <summary>
/// The second wave of poster templates — fifteen looks built under the house
/// rules the first wave earned the hard way: fighters FILL the frame (waist-up
/// crops), crops live at panel edges or under opaque fades, small text rides
/// plates or contrast, accent never sits on accent, gradients never pass
/// through transparent white, and the tagline engine names real belts.
/// </summary>
public static partial class PosterFactory
{
    // Serif kit for the editorial looks (Georgia ships with Windows).
    private static FormattedText Serif(string text, double size, Brush brush, bool italic = false) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Georgia, Times New Roman"), italic ? FontStyles.Italic : FontStyles.Normal,
                FontWeights.Bold, FontStretches.Normal), size, brush, 1.0);

    private static void TextSerif(DrawingContext ctx, string text, double size, Brush brush, double centerX, double top, bool italic = false)
    {
        var ft = Serif(text, size, brush, italic);
        ft.MaxTextWidth = W - 48;
        ft.MaxLineCount = 1;
        ft.Trimming = TextTrimming.CharacterEllipsis;
        ft.TextAlignment = TextAlignment.Center;
        ctx.DrawText(ft, new Point(centerX - (W - 48) / 2, top));
    }

    // "BrazilianJiuJitsu" → "BRAZILIAN JIU JITSU" for the tape rows.
    private static string PrettySplit(string enumName)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < enumName.Length; i++)
        {
            if (i > 0 && char.IsUpper(enumName[i])) sb.Append(' ');
            sb.Append(char.ToUpperInvariant(enumName[i]));
        }
        return sb.ToString();
    }

    // ============ NEON — the fight-week billboard ============
    private static void Neon(DrawingContext ctx, World world, Event ev, Promotion? promo, Fighter? red, Fighter? blue, Palette pal)
    {
        ctx.DrawRectangle(Frozen(Rgb(0x07070C)), null, new Rect(0, 0, W, H));
        // A neon frame: soft glow pass under a hot core line.
        ctx.DrawRoundedRectangle(null, new Pen(Frozen(WithA(pal.Accent, 0x2E)), 12), new Rect(22, 22, W - 44, H - 44), 8, 8);
        ctx.DrawRoundedRectangle(null, new Pen(Frozen(WithA(pal.Accent, 0xE0)), 2.2), new Rect(22, 22, W - 44, H - 44), 8, 8);

        if (promo is not null) ctx.DrawImage(LogoFactory.ForPromotion(promo), new Rect(W / 2 - 33, 44, 66, 66));
        Text(ctx, ev.Name.ToUpperInvariant(), 34, White, W / 2, 120);
        TaglineRow(ctx, world, ev, pal, 176);

        if (red is not null && blue is not null)
        {
            ctx.PushClip(new RectangleGeometry(new Rect(24, 0, W - 48, 800)));
            Body(ctx, world, red, W * 0.29, 236, 600, mirror: false);
            Body(ctx, world, blue, W * 0.71, 236, 600, mirror: true);
            Fade(ctx, 470, 800, 0x07070C);
            ctx.Pop();
            // Neon names: a wide glow pass beneath the hot line of type.
            var glow = Format($"{red.LastName.ToUpperInvariant()}  ·  {blue.LastName.ToUpperInvariant()}", 40, Frozen(WithA(pal.Accent, 0x3C)));
            glow.TextAlignment = TextAlignment.Center; glow.MaxTextWidth = W - 40; glow.MaxLineCount = 1;
            for (int dx = -2; dx <= 2; dx += 2)
                ctx.DrawText(glow, new Point(W / 2 - (W - 40) / 2 + dx, 620));
            Text(ctx, $"{red.LastName.ToUpperInvariant()}  ·  {blue.LastName.ToUpperInvariant()}", 40, Frozen(pal.Accent), W / 2, 620);
        }
        else Teaser(ctx, pal);
        VenueStrip(ctx, world, ev, pal);
    }

    // ============ BROADSHEET — the fight pages ============
    private static void Broadsheet(DrawingContext ctx, World world, Event ev, Promotion? promo, Fighter? red, Fighter? blue, Palette pal)
    {
        var paper = Frozen(Rgb(0xF1EEE4));
        var ink = Frozen(Rgb(0x17171A));
        ctx.DrawRectangle(paper, null, new Rect(0, 0, W, H));
        TextSerif(ctx, "THE FIGHT PAGES", 15, ink, W / 2, 30);
        ctx.DrawRectangle(ink, null, new Rect(36, 58, W - 72, 3));
        ctx.DrawRectangle(ink, null, new Rect(36, 64, W - 72, 1));
        TextSerif(ctx, ev.Name.ToUpperInvariant(), 44, ink, W / 2, 84);
        if (EventHype.Tagline(world, ev) is { } tag)
            TextSerif(ctx, "“" + tag + "”", 17, ink, W / 2, 148, italic: true);

        if (red is not null && blue is not null)
        {
            // Two photo boxes with hairline frames and serif captions — newsprint.
            foreach (var (f, x, mirror) in new[] { (red, 52.0, false), (blue, W / 2 + 26, true) })
            {
                var box = new Rect(x, 196, W / 2 - 78, 380);
                ctx.PushClip(new RectangleGeometry(box));
                Body(ctx, world, f, box.X + box.Width / 2, 206, 640, mirror);
                ctx.Pop();
                ctx.DrawRectangle(null, new Pen(ink, 2), box);
                TextSerif(ctx, f.LastName.ToUpperInvariant(), 24, ink, box.X + box.Width / 2, 588);
                TextSerif(ctx, f.Record.ToString(), 14, ink, box.X + box.Width / 2, 622);
            }
            TextSerif(ctx, "VERSUS", 16, ink, W / 2, 400);
        }
        else TextSerif(ctx, "CARD TO BE ANNOUNCED", 20, ink, W / 2, 380);

        ctx.DrawRectangle(ink, null, new Rect(36, 700, W - 72, 1));
        TextSerif(ctx, ev.Date.ToString("dddd, MMMM d, yyyy", CultureInfo.InvariantCulture) + "  —  " + Where(world, ev), 14, ink, W / 2, 716);
    }

    // ============ BRUTALIST — the Swiss block ============
    private static void Brutalist(DrawingContext ctx, World world, Event ev, Promotion? promo, Fighter? red, Fighter? blue, Palette pal)
    {
        ctx.DrawRectangle(Frozen(pal.Paper), null, new Rect(0, 0, W, H * 0.45));
        ctx.DrawRectangle(Frozen(Rgb(0x111114)), null, new Rect(0, H * 0.45, W, H * 0.55));

        Text(ctx, ev.Name.ToUpperInvariant(), 20, InkBrush, W / 2, 14);
        if (red is not null && blue is not null)
        {
            // Names as enormous flat blocks — each in its OWN band, red's in ink
            // on the colour field, blue's in colour on the ink, the fighters
            // between them so nothing hides behind anything.
            FitLine(ctx, red.LastName.ToUpperInvariant(), 44, 88, new Palette(Rgb(0x111114), Rgb(0x111114), pal.Paper));
            FitLine(ctx, blue.LastName.ToUpperInvariant(), 620, 88, pal);

            // The fighters puncture the boundary in two hard panels — waist-up, big.
            foreach (var (f, x, mirror) in new[] { (red, 62.0, false), (blue, W - 62 - 220, true) })
            {
                var box = new Rect(x, 190, 220, 380);
                ctx.PushClip(new RectangleGeometry(box));
                Body(ctx, world, f, box.X + 110, 198, 580, mirror);
                ctx.Pop();
                ctx.DrawRectangle(null, new Pen(Frozen(pal.Accent), 3), box);
            }
            Text(ctx, "VS", 30, White, W / 2, 344);   // rides the seam — white reads on both fields
        }
        else Text(ctx, "CARD TO BE ANNOUNCED", 22, Frozen(pal.Paper), W / 2, 500, spaced: true);

        if (EventHype.Tagline(world, ev) is { } tag)
            Text(ctx, tag, 14, Frozen(pal.Paper), W / 2, 588, spaced: true);
        Text(ctx, ev.Date.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture).ToUpperInvariant()
            + "   ·   " + Where(world, ev).ToUpperInvariant(), 13, Muted, W / 2, 756, spaced: true);
    }

    // ============ VINTAGE — the old boxing bill ============
    private static void Vintage(DrawingContext ctx, World world, Event ev, Promotion? promo, Fighter? red, Fighter? blue, Palette pal)
    {
        var cream = Frozen(Rgb(0xEBE2C9));
        var ink = Frozen(Rgb(0x2A241C));
        ctx.DrawRectangle(cream, null, new Rect(0, 0, W, H));
        ctx.DrawRectangle(null, new Pen(ink, 4), new Rect(20, 20, W - 40, H - 40));
        ctx.DrawRectangle(null, new Pen(ink, 1.2), new Rect(30, 30, W - 60, H - 60));

        TextSerif(ctx, (world.Get(ev.Promotion)?.Name ?? "").ToUpperInvariant() + " PROUDLY PRESENTS", 13, ink, W / 2, 52);
        TextSerif(ctx, ev.Name.ToUpperInvariant(), 40, ink, W / 2, 76);
        TextSerif(ctx, "◆   ◆   ◆", 12, ink, W / 2, 132);
        if (red is not null && blue is not null && ev.Card[^1].IsTitleFight)
            TextSerif(ctx, $"FOR THE {EventHype.BeltName(world, ev.Card[^1])} CHAMPIONSHIP OF THE WORLD", 14, ink, W / 2, 154);
        else if (EventHype.Tagline(world, ev) is { } tag)
            TextSerif(ctx, tag, 14, ink, W / 2, 154);

        if (red is not null && blue is not null)
        {
            TextSerif(ctx, red.LastName.ToUpperInvariant(), 42, ink, W / 2, 190);
            TextSerif(ctx, "— VERSUS —", 15, ink, W / 2, 250);
            TextSerif(ctx, blue.LastName.ToUpperInvariant(), 42, ink, W / 2, 276);

            foreach (var (f, x, mirror) in new[] { (red, 70.0, false), (blue, W - 70 - 190, true) })
            {
                var box = new Rect(x, 356, 190, 330);
                ctx.PushClip(new RectangleGeometry(box));
                Body(ctx, world, f, box.X + 95, 364, 470, mirror);
                ctx.Pop();
                ctx.DrawRectangle(null, new Pen(ink, 2), box);
            }
            TextSerif(ctx, $"{red.Record}        {blue.Record}", 15, ink, W / 2, 700);
        }
        else TextSerif(ctx, "FULL CARD TO BE ANNOUNCED", 18, ink, W / 2, 400);

        TextSerif(ctx, ev.Date.ToString("dddd MMMM d, yyyy", CultureInfo.InvariantCulture).ToUpperInvariant()
            + "  ·  " + Where(world, ev).ToUpperInvariant(), 12, ink, W / 2, 742);
    }

    // ============ CHAMPION — the defence portrait ============
    private static void Champion(DrawingContext ctx, World world, Event ev, Promotion? promo, Fighter? red, Fighter? blue, Palette pal)
    {
        ctx.DrawRectangle(new LinearGradientBrush(Rgb(0x111014), Rgb(0x070608), 90).Frozen(), null, new Rect(0, 0, W, H));
        ctx.DrawRectangle(new RadialGradientBrush(WithA(Rgb(0xD8B24A), 0x30), WithA(Rgb(0xD8B24A), 0x00))
        {
            Center = new Point(0.5, 0.62), GradientOrigin = new Point(0.5, 0.62), RadiusX = 0.7, RadiusY = 0.5,
        }.Frozen(), null, new Rect(0, 0, W, H));

        // Which corner holds the belt? The champion carries the sheet alone.
        var main = ev.Card.Count > 0 ? ev.Card[^1] : null;
        bool title = main?.IsTitleFight == true;
        Fighter? champ = null, challenger = null;
        if (main is not null && red is not null && blue is not null)
        {
            var beltHolder = main.Title is { } tid ? world.Get(tid)?.CurrentChampion : null;
            (champ, challenger) = beltHolder == blue.Id ? (blue, red) : (red, blue);
        }

        if (promo is not null) ctx.DrawImage(LogoFactory.ForPromotion(promo), new Rect(W / 2 - 32, 26, 64, 64));
        Text(ctx, ev.Name.ToUpperInvariant(), 28, White, W / 2, 96);
        Text(ctx, title ? "THE CHAMPION DEFENDS" : "THE HEADLINER", 13, Frozen(Rgb(0xD8B24A)), W / 2, 140, spaced: true);

        if (champ is not null && challenger is not null)
        {
            Body(ctx, world, champ, W / 2, 176, 660, mirror: false);
            Fade(ctx, 500, 800, 0x080709);
            Text(ctx, champ.LastName.ToUpperInvariant(), 52, White, W / 2, 578);
            Text(ctx, title
                ? $"{champ.Record}   ·   {EventHype.BeltName(world, main!)} CHAMPION"
                : champ.Record.ToString(), 14,
                Frozen(Rgb(0xD8B24A)), W / 2, 644, spaced: true);

            // The challenger: a small corner panel, name plate under it.
            var box = new Rect(30, 560, 128, 128);
            FacePanel(ctx, world, challenger, box.X, box.Y, box.Width, mirror: false);
            ctx.DrawRectangle(null, new Pen(Frozen(WithA(Rgb(0xD8B24A), 0x99)), 1.4), box);
            Text(ctx, "THE CHALLENGER", 10, Muted, box.X + 64, 694, spaced: true);
            Text(ctx, challenger.LastName.ToUpperInvariant(), 16, White, box.X + 64, 708);
        }
        else Teaser(ctx, pal);
        VenueStrip(ctx, world, ev, pal);
    }

    // ============ SLASH — faces on the diagonal ============
    private static void Slash(DrawingContext ctx, World world, Event ev, Promotion? promo, Fighter? red, Fighter? blue, Palette pal)
    {
        ctx.DrawRectangle(Frozen(Rgb(0x0B0B10)), null, new Rect(0, 0, W, H));
        if (red is not null && blue is not null)
        {
            // Red owns the top-left triangle, blue the bottom-right, split by a blade.
            var topLeft = Poly((0, 0), (W, 0), (0, 620));
            var bottomRight = Poly((W, 180), (W, 800), (0, 800));
            ctx.PushClip(topLeft);
            ctx.DrawImage(Portraits.For(red, world), new Rect(-20, 60, 420, 420));
            Fade(ctx, 330, 620, 0x0B0B10);
            ctx.Pop();
            ctx.PushClip(bottomRight);
            ctx.PushTransform(new ScaleTransform(-1, 1, W - 20 - 210, 0));
            ctx.DrawImage(Portraits.For(blue, world), new Rect(W - 20 - 420, 320, 420, 420));
            ctx.Pop();
            Fade(ctx, 660, 800, 0x0B0B10);
            ctx.Pop();
            // The slash itself.
            ctx.DrawGeometry(Frozen(WithA(pal.Accent, 0xD8)), null, Poly((W, 168), (W, 196), (0, 636), (0, 608)));
            // Names ride their own corners, records under.
            Text(ctx, red.LastName.ToUpperInvariant(), 40, White, W * 0.70, 92);
            Text(ctx, red.Record.ToString(), 14, Muted, W * 0.70, 146);
            Text(ctx, blue.LastName.ToUpperInvariant(), 40, White, W * 0.30, 596);
            Text(ctx, blue.Record.ToString(), 14, Muted, W * 0.30, 650);
        }
        else Teaser(ctx, pal);
        if (promo is not null) ctx.DrawImage(LogoFactory.ForPromotion(promo), new Rect(24, 22, 52, 52));
        Text(ctx, ev.Name.ToUpperInvariant(), 24, White, W / 2, 700);
        TaglineRow(ctx, world, ev, pal, red is null ? 340 : 348);
        VenueStrip(ctx, world, ev, pal);
    }

    // ============ RING LIGHTS — under the cones ============
    private static void RingLights(DrawingContext ctx, World world, Event ev, Promotion? promo, Fighter? red, Fighter? blue, Palette pal)
    {
        ctx.DrawRectangle(Frozen(Rgb(0x08090B)), null, new Rect(0, 0, W, H));
        // Three light cones from the truss.
        foreach (double cx in new[] { W * 0.27, W * 0.5, W * 0.73 })
            ctx.DrawGeometry(Frozen(Color.FromArgb(0x16, 0xE8, 0xE4, 0xD0)), null,
                Poly((cx - 18, 0), (cx + 18, 0), (cx + 150, 560), (cx - 150, 560)));

        if (promo is not null) ctx.DrawImage(LogoFactory.ForPromotion(promo), new Rect(W / 2 - 30, 24, 60, 60));
        Text(ctx, ev.Name.ToUpperInvariant(), 30, White, W / 2, 94);
        TaglineRow(ctx, world, ev, pal, 148);

        if (red is not null && blue is not null)
        {
            Body(ctx, world, red, W * 0.27, 216, 600, mirror: false);
            Body(ctx, world, blue, W * 0.73, 216, 600, mirror: true);
            Fade(ctx, 460, 800, 0x08090B);
            Marquee(ctx, red, blue, pal, 580);
        }
        else Teaser(ctx, pal);
        VenueStrip(ctx, world, ev, pal);
    }

    // ============ BLACKOUT — the fight speaks for itself ============
    private static void Blackout(DrawingContext ctx, World world, Event ev, Promotion? promo, Fighter? red, Fighter? blue, Palette pal)
    {
        ctx.DrawRectangle(Frozen(Rgb(0x050506)), null, new Rect(0, 0, W, H));
        var gold = Frozen(pal.Accent);
        ctx.DrawRectangle(gold, null, new Rect(W / 2 - 22, 300, 44, 2));
        Text(ctx, (world.Get(ev.Promotion)?.Abbreviation ?? "").ToUpperInvariant(), 15, Muted, W / 2, 324, spaced: true);
        Text(ctx, ev.Name.ToUpperInvariant(), 30, White, W / 2, 350);
        if (EventHype.Tagline(world, ev) is { } tag)
            Text(ctx, tag, 13, gold, W / 2, 402, spaced: true);
        if (red is not null && blue is not null)
            Text(ctx, $"{red.LastName.ToUpperInvariant()}  VS  {blue.LastName.ToUpperInvariant()}", 24, White, W / 2, 436);
        Text(ctx, ev.Date.ToString("MM.dd.yyyy", CultureInfo.InvariantCulture), 15, gold, W / 2, 486, spaced: true);
        ctx.DrawRectangle(gold, null, new Rect(W / 2 - 22, 522, 44, 2));
        Text(ctx, Where(world, ev).ToUpperInvariant(), 11, Muted, W / 2, 764, spaced: true);
    }

    // ============ CORNERS — red corner, blue corner ============
    private static void Corners(DrawingContext ctx, World world, Event ev, Promotion? promo, Fighter? red, Fighter? blue, Palette pal)
    {
        ctx.DrawRectangle(new LinearGradientBrush(Rgb(0x461A1E), Rgb(0x180A0B), 90).Frozen(), null, new Rect(0, 0, W / 2, H));
        ctx.DrawRectangle(new LinearGradientBrush(Rgb(0x18294A), Rgb(0x090E1A), 90).Frozen(), null, new Rect(W / 2, 0, W / 2, H));
        ctx.DrawRectangle(White, null, new Rect(W / 2 - 1, 0, 2, H));

        Text(ctx, "RED CORNER", 12, Frozen(Rgb(0xE08A8A)), W * 0.25, 34, spaced: true);
        Text(ctx, "BLUE CORNER", 12, Frozen(Rgb(0x8AA6E0)), W * 0.75, 34, spaced: true);

        if (red is not null && blue is not null)
        {
            ctx.PushClip(new RectangleGeometry(new Rect(0, 0, W / 2, 800)));
            Body(ctx, world, red, W * 0.25, 118, 640, mirror: false);
            Fade(ctx, 420, 800, 0x120809);
            ctx.Pop();
            ctx.PushClip(new RectangleGeometry(new Rect(W / 2, 0, W / 2, 800)));
            Body(ctx, world, blue, W * 0.75, 118, 640, mirror: true);
            Fade(ctx, 420, 800, 0x080B12);
            ctx.Pop();

            Text(ctx, red.LastName.ToUpperInvariant(), 38, White, W * 0.25, 570);
            Text(ctx, red.Record.ToString(), 15, Muted, W * 0.25, 622);
            Text(ctx, blue.LastName.ToUpperInvariant(), 38, White, W * 0.75, 570);
            Text(ctx, blue.Record.ToString(), 15, Muted, W * 0.75, 622);
        }
        else Teaser(ctx, pal);

        Text(ctx, ev.Name.ToUpperInvariant(), 26, White, W / 2, 668);
        TaglineRow(ctx, world, ev, pal, red is null ? 340 : 62);
        VenueStrip(ctx, world, ev, pal);
    }

    // ============ TAPE — the tale of the tape ============
    private static void Tape(DrawingContext ctx, World world, Event ev, Promotion? promo, Fighter? red, Fighter? blue, Palette pal)
    {
        ctx.DrawRectangle(new LinearGradientBrush(Rgb(0x101216), Rgb(0x08090B), 90).Frozen(), null, new Rect(0, 0, W, H));
        if (promo is not null) ctx.DrawImage(LogoFactory.ForPromotion(promo), new Rect(W / 2 - 30, 22, 60, 60));
        Text(ctx, ev.Name.ToUpperInvariant(), 28, White, W / 2, 90);
        TaglineRow(ctx, world, ev, pal, 140);

        if (red is not null && blue is not null)
        {
            ctx.PushClip(new RectangleGeometry(new Rect(0, 0, 205, 800)));
            Body(ctx, world, red, 102, 206, 620, mirror: false);
            Fade(ctx, 560, 800, 0x090A0C);
            ctx.Pop();
            ctx.PushClip(new RectangleGeometry(new Rect(W - 205, 0, 205, 800)));
            Body(ctx, world, blue, W - 102, 206, 620, mirror: true);
            Fade(ctx, 560, 800, 0x090A0C);
            ctx.Pop();

            // The tape itself: an ink plate of receipts between them — label
            // centred, red's number on the left edge, blue's on the right.
            var plateRect = new Rect(206, 232, W - 412, 368);
            ctx.DrawRoundedRectangle(Frozen(Color.FromArgb(0xDA, 0x0C, 0x0D, 0x11)), new Pen(Frozen(WithA(pal.Accent, 0x66)), 1), plateRect, 4, 4);
            Text(ctx, "TALE OF THE TAPE", 12, Frozen(pal.Accent), W / 2, 250, spaced: true);
            (string R, string Label, string B)[] rows =
            {
                (red.Record.ToString(), "RECORD", blue.Record.ToString()),
                (red.AgeOn(world.CurrentDate).ToString(), "AGE", blue.AgeOn(world.CurrentDate).ToString()),
                ($"{red.HeightCm} CM", "HEIGHT", $"{blue.HeightCm} CM"),
                ($"{red.ReachCm} CM", "REACH", $"{blue.ReachCm} CM"),
                (PrettySplit(red.PrimaryStyle.ToString()), "STYLE", PrettySplit(blue.PrimaryStyle.ToString())),
            };
            for (int i = 0; i < rows.Length; i++)
            {
                double y = 288 + i * 60;
                Text(ctx, rows[i].Label, 11, Muted, W / 2, y, spaced: true);
                var l = Format(rows[i].R, 15, White);
                l.TextAlignment = TextAlignment.Left; l.MaxTextWidth = plateRect.Width / 2 - 20;
                l.MaxLineCount = 1; l.Trimming = TextTrimming.CharacterEllipsis;
                ctx.DrawText(l, new Point(plateRect.X + 14, y + 18));
                var r = Format(rows[i].B, 15, White);
                r.TextAlignment = TextAlignment.Right; r.MaxTextWidth = plateRect.Width / 2 - 20;
                r.MaxLineCount = 1; r.Trimming = TextTrimming.CharacterEllipsis;
                ctx.DrawText(r, new Point(plateRect.Right - 14 - r.MaxTextWidth, y + 18));
            }

            Text(ctx, $"{red.LastName.ToUpperInvariant()}  VS  {blue.LastName.ToUpperInvariant()}", 34, White, W / 2, 636);
        }
        else Teaser(ctx, pal);
        VenueStrip(ctx, world, ev, pal);
    }

    // ============ HOMETOWN — the city carries the bill ============
    private static void Hometown(DrawingContext ctx, World world, Event ev, Promotion? promo, Fighter? red, Fighter? blue, Palette pal)
    {
        ctx.DrawRectangle(new LinearGradientBrush(Rgb(0x0E1013), Rgb(0x07080A), 90).Frozen(), null, new Rect(0, 0, W, H));
        string city = ev.VenueId is { } vid && world.Get(vid) is { } venue
            ? world.Get(venue.City)?.Name ?? "FIGHT NIGHT" : "FIGHT NIGHT";
        FitLine(ctx, city.ToUpperInvariant(), 44, 96, pal);
        Text(ctx, ev.Name.ToUpperInvariant(), 20, White, W / 2, 152, spaced: true);

        if (red is not null && blue is not null)
        {
            Body(ctx, world, red, W * 0.28, 226, 610, mirror: false);
            Body(ctx, world, blue, W * 0.72, 226, 610, mirror: true);
            Fade(ctx, 470, 800, 0x07080A);
            Marquee(ctx, red, blue, pal, 584);
        }
        else Teaser(ctx, pal);

        if (promo is not null) ctx.DrawImage(LogoFactory.ForPromotion(promo), new Rect(24, 24, 50, 50));
        TaglineRow(ctx, world, ev, pal, red is null ? 340 : 186);
        VenueStrip(ctx, world, ev, pal);
    }

    // ============ COUNTDOWN — the date is the hero ============
    private static void Countdown(DrawingContext ctx, World world, Event ev, Promotion? promo, Fighter? red, Fighter? blue, Palette pal)
    {
        ctx.DrawRectangle(Frozen(Rgb(0x0A0B0E)), null, new Rect(0, 0, W, H));
        if (red is not null && blue is not null)
        {
            ctx.PushClip(new RectangleGeometry(new Rect(0, 0, 216, 800)));
            Body(ctx, world, red, 108, 150, 660, mirror: false);
            Fade(ctx, 520, 800, 0x0A0B0E);
            ctx.Pop();
            ctx.PushClip(new RectangleGeometry(new Rect(W - 216, 0, 216, 800)));
            Body(ctx, world, blue, W - 108, 150, 660, mirror: true);
            Fade(ctx, 520, 800, 0x0A0B0E);
            ctx.Pop();
        }

        if (promo is not null) ctx.DrawImage(LogoFactory.ForPromotion(promo), new Rect(W / 2 - 30, 40, 60, 60));
        Text(ctx, ev.Name.ToUpperInvariant(), 24, White, W / 2, 116, spaced: false);

        // The date, enormous, dead centre.
        Text(ctx, ev.Date.ToString("MMM", CultureInfo.InvariantCulture).ToUpperInvariant(), 44, Frozen(pal.Accent), W / 2, 240, spaced: true);
        var day = Format(ev.Date.Day.ToString("00"), 190, White);
        day.TextAlignment = TextAlignment.Center; day.MaxTextWidth = W - 40; day.MaxLineCount = 1;
        ctx.DrawText(day, new Point(W / 2 - (W - 40) / 2, 288));
        Text(ctx, ev.Date.ToString("dddd", CultureInfo.InvariantCulture).ToUpperInvariant(), 17, Frozen(pal.Accent), W / 2, 534, spaced: true);

        if (red is not null && blue is not null)
            Text(ctx, $"{red.LastName.ToUpperInvariant()}  VS  {blue.LastName.ToUpperInvariant()}", 30, White, W / 2, 596);
        TaglineRow(ctx, world, ev, pal, 652);
        VenueStrip(ctx, world, ev, pal);
    }

    // ============ DECO — the golden age ============
    private static void Deco(DrawingContext ctx, World world, Event ev, Promotion? promo, Fighter? red, Fighter? blue, Palette pal)
    {
        ctx.DrawRectangle(Frozen(Rgb(0x0C0B09)), null, new Rect(0, 0, W, H));
        var gold = Frozen(Rgb(0xC9A94F));
        // The fan: thin rays rising from the bottom centre.
        for (int i = -6; i <= 6; i++)
            ctx.DrawGeometry(Frozen(WithA(Rgb(0xC9A94F), 0x20)), null,
                Poly((W / 2, 820), (W / 2 + i * 90 - 7, -60), (W / 2 + i * 90 + 7, -60)));
        ctx.DrawRectangle(null, new Pen(gold, 2.4), new Rect(18, 18, W - 36, H - 36));
        ctx.DrawRectangle(null, new Pen(Frozen(WithA(Rgb(0xC9A94F), 0x70)), 0.8), new Rect(26, 26, W - 52, H - 52));

        Text(ctx, (world.Get(ev.Promotion)?.Abbreviation ?? "").ToUpperInvariant() + "  PRESENTS", 13, gold, W / 2, 46, spaced: true);
        Text(ctx, ev.Name.ToUpperInvariant(), 36, White, W / 2, 70);
        if (EventHype.Tagline(world, ev) is { } tag) Text(ctx, tag, 13, gold, W / 2, 126, spaced: true);

        if (red is not null && blue is not null)
        {
            ctx.PushClip(new RectangleGeometry(new Rect(28, 0, W - 56, 800)));
            Body(ctx, world, red, W * 0.30, 210, 600, mirror: false);
            Body(ctx, world, blue, W * 0.70, 210, 600, mirror: true);
            Fade(ctx, 450, 800, 0x0C0B09);
            ctx.Pop();
            Text(ctx, red.LastName.ToUpperInvariant(), 44, gold, W / 2, 566);
            Text(ctx, "· VERSUS ·", 14, White, W / 2, 626, spaced: true);
            Text(ctx, blue.LastName.ToUpperInvariant(), 44, gold, W / 2, 648);
        }
        else Teaser(ctx, pal);
        Text(ctx, ev.Date.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture).ToUpperInvariant()
            + "   ·   " + Where(world, ev).ToUpperInvariant(), 13, gold, W / 2, 744, spaced: true);
    }

    // ============ BOLT — the feud, torn down the middle ============
    private static void Bolt(DrawingContext ctx, World world, Event ev, Promotion? promo, Fighter? red, Fighter? blue, Palette pal)
    {
        ctx.DrawRectangle(new LinearGradientBrush(Rgb(0x2A0E12), Rgb(0x0B0709), 90).Frozen(), null, new Rect(0, 0, W / 2, H));
        ctx.DrawRectangle(new LinearGradientBrush(Rgb(0x101828), Rgb(0x070A10), 90).Frozen(), null, new Rect(W / 2, 0, W / 2, H));

        if (red is not null && blue is not null)
        {
            FacePanel(ctx, world, red, 0, 170, 300, mirror: false);
            FacePanel(ctx, world, blue, W / 2, 170, 300, mirror: true);
            Fade(ctx, 380, 560, 0x0B0709);
        }

        // The bolt: a jagged tear where the seam should be — a constant-width
        // zigzag band (left edge down, right edge back up).
        ctx.DrawGeometry(Frozen(WithA(pal.Accent, 0xE6)), null, Poly(
            (W / 2 + 2, 0), (W / 2 - 30, 260), (W / 2 + 22, 540), (W / 2 - 12, 800),
            (W / 2 + 10, 800), (W / 2 + 44, 540), (W / 2 - 8, 260), (W / 2 + 24, 0)));

        if (promo is not null) ctx.DrawImage(LogoFactory.ForPromotion(promo), new Rect(W / 2 - 30, 26, 60, 60));
        Text(ctx, ev.Name.ToUpperInvariant(), 28, White, W / 2, 96, spaced: false);
        TaglineRow(ctx, world, ev, pal, 132);

        if (red is not null && blue is not null)
        {
            Text(ctx, red.LastName.ToUpperInvariant(), 40, White, W * 0.25, 528);
            Text(ctx, red.Record.ToString(), 15, Muted, W * 0.25, 584);
            Text(ctx, blue.LastName.ToUpperInvariant(), 40, White, W * 0.75, 528);
            Text(ctx, blue.Record.ToString(), 15, Muted, W * 0.75, 584);
            if (EventHype.Meetings(world, red.Id, blue.Id) is > 0 and var met)
                Text(ctx, $"THEY HAVE MET {met} TIME{(met > 1 ? "S" : "")} BEFORE", 13, Frozen(pal.Glow), W / 2, 664, spaced: true);
        }
        else Teaser(ctx, pal);
        VenueStrip(ctx, world, ev, pal);
    }

    // ============ BELT — the prize in gold leaf ============
    // A drawn belt can't sit next to photographic fighters without looking like
    // clip-art (tried twice) — so the prize is NAMED instead: the belt's own
    // title in giant gold-leaf lettering, the fighters standing under it.
    private static void Belt(DrawingContext ctx, World world, Event ev, Promotion? promo, Fighter? red, Fighter? blue, Palette pal)
    {
        ctx.DrawRectangle(new LinearGradientBrush(Rgb(0x100F12), Rgb(0x070608), 90).Frozen(), null, new Rect(0, 0, W, H));
        ctx.DrawRectangle(new RadialGradientBrush(WithA(Rgb(0xE8B23A), 0x2E), WithA(Rgb(0xE8B23A), 0x00))
        {
            Center = new Point(0.5, 0.28), GradientOrigin = new Point(0.5, 0.28), RadiusX = 0.75, RadiusY = 0.45,
        }.Frozen(), null, new Rect(0, 0, W, H));
        var gold = Frozen(Rgb(0xD8B24A));

        if (promo is not null) ctx.DrawImage(LogoFactory.ForPromotion(promo), new Rect(W / 2 - 27, 22, 54, 54));
        Text(ctx, ev.Name.ToUpperInvariant(), 26, White, W / 2, 84);
        Text(ctx, "FOR THE WORLD CHAMPIONSHIP", 13, gold, W / 2, 128, spaced: true);

        // The belt's name, edge to edge in gold leaf.
        string beltName = ev.Card.Count > 0 && ev.Card[^1].IsTitleFight
            ? EventHype.BeltName(world, ev.Card[^1])
            : ev.Card.Count > 0 ? EventHype.PrettyWeight(ev.Card[^1].WeightClass) : "CHAMPIONSHIP";
        GoldLine(ctx, beltName, 156, 108);

        if (red is not null && blue is not null)
        {
            Body(ctx, world, red, W * 0.27, 300, 580, mirror: false);
            Body(ctx, world, blue, W * 0.73, 300, 580, mirror: true);
            Fade(ctx, 540, 800, 0x080709);
            Text(ctx, $"{red.LastName.ToUpperInvariant()}  VS  {blue.LastName.ToUpperInvariant()}", 36, White, W / 2, 646);
            Text(ctx, $"{red.Record}   ·   {blue.Record}", 14, gold, W / 2, 698, spaced: true);
        }
        else Teaser(ctx, pal);
        VenueStrip(ctx, world, ev, pal);
    }

    // A line of type scaled to the sheet width and filled with gold leaf —
    // light catching the top of the letters, deepening toward the base.
    private static void GoldLine(DrawingContext ctx, string line, double top, double cap)
    {
        var probe = Format(line, 100, White);
        double size = Math.Min(cap, 100 * (W - 56) / Math.Max(1, probe.Width));
        var leaf = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        leaf.GradientStops.Add(new GradientStop(Rgb(0xF6E8B8), 0));
        leaf.GradientStops.Add(new GradientStop(Rgb(0xD8B24A), 0.45));
        leaf.GradientStops.Add(new GradientStop(Rgb(0x8A6D25), 1));
        var ft = Format(line, size, leaf.Frozen());
        var origin = new Point(W / 2 - ft.Width / 2, top + (cap - size) / 2);
        ctx.DrawText(ft, origin);
        ctx.DrawGeometry(null, new Pen(Frozen(WithA(Rgb(0xF2E2AC), 0x99)), 0.9), ft.BuildGeometry(origin));
    }
}
