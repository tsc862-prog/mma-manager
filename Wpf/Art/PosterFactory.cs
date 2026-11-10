using System.Globalization;
using System.Windows;
using System.Windows.Media;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Flavor;

namespace MmaManager.Wpf.Art;

/// <summary>
/// Auto-generated fight posters with real variety: several layout TEMPLATES
/// (the arena shaft, the corner split, the face-off closeup, the street bill),
/// each dressed in the org's SIGNATURE PALETTE, picked deterministically per
/// event — so every card gets its own look, a City hall's poster feels like a
/// photocopied street bill while a PPV feels like a premium one-sheet, and the
/// same event always renders the same poster. The story line comes from
/// <see cref="EventHype"/>; the where/when from the booking.
/// </summary>
public static partial class PosterFactory
{
    private const double W = 600, H = 800;

    public enum Template
    {
        Arena, Split, Closeup, Street, OneSheet, Gold, Marquee, DoubleBill, TripleBill, Stacked,
        // The second wave (PosterFactory.Templates2.cs):
        Neon, Broadsheet, Brutalist, Vintage, Champion, Slash, RingLights, Blackout,
        Corners, Tape, Hometown, Countdown, Deco, Bolt, Belt,
    }

    public static ImageSource For(World world, Event ev, Template? force = null)
    {
        var promo = world.Get(ev.Promotion);
        var main = ev.Card.Count > 0 ? ev.Card[^1] : null;
        var red = main is not null ? world.Get(main.RedCorner) : null;
        var blue = main is not null ? world.Get(main.BlueCorner) : null;
        var pal = PaletteFor(promo);
        var template = force ?? Pick(world, ev, promo);

        var group = new DrawingGroup();
        using (var ctx = group.Open())
        {
            switch (template)
            {
                case Template.Street: Street(ctx, world, ev, promo, red, blue, pal); break;
                case Template.Split: Split(ctx, world, ev, promo, red, blue, pal); break;
                case Template.Closeup: Closeup(ctx, world, ev, promo, red, blue, pal); break;
                case Template.OneSheet: OneSheet(ctx, world, ev, promo, red, blue, pal); break;
                case Template.Gold: Gold(ctx, world, ev, promo, red, blue, pal); break;
                case Template.Marquee: Marquee(ctx, world, ev, promo, red, blue, pal); break;
                case Template.DoubleBill: DoubleBill(ctx, world, ev, promo, pal); break;
                case Template.TripleBill: TripleBill(ctx, world, ev, promo, pal); break;
                case Template.Stacked: Stacked(ctx, world, ev, promo, pal); break;
                case Template.Neon: Neon(ctx, world, ev, promo, red, blue, pal); break;
                case Template.Broadsheet: Broadsheet(ctx, world, ev, promo, red, blue, pal); break;
                case Template.Brutalist: Brutalist(ctx, world, ev, promo, red, blue, pal); break;
                case Template.Vintage: Vintage(ctx, world, ev, promo, red, blue, pal); break;
                case Template.Champion: Champion(ctx, world, ev, promo, red, blue, pal); break;
                case Template.Slash: Slash(ctx, world, ev, promo, red, blue, pal); break;
                case Template.RingLights: RingLights(ctx, world, ev, promo, red, blue, pal); break;
                case Template.Blackout: Blackout(ctx, world, ev, promo, red, blue, pal); break;
                case Template.Corners: Corners(ctx, world, ev, promo, red, blue, pal); break;
                case Template.Tape: Tape(ctx, world, ev, promo, red, blue, pal); break;
                case Template.Hometown: Hometown(ctx, world, ev, promo, red, blue, pal); break;
                case Template.Countdown: Countdown(ctx, world, ev, promo, red, blue, pal); break;
                case Template.Deco: Deco(ctx, world, ev, promo, red, blue, pal); break;
                case Template.Bolt: Bolt(ctx, world, ev, promo, red, blue, pal); break;
                case Template.Belt: Belt(ctx, world, ev, promo, red, blue, pal); break;
                default: Arena(ctx, world, ev, promo, red, blue, pal); break;
            }
        }
        group.ClipGeometry = new RectangleGeometry(new Rect(0, 0, W, H));
        group.Freeze();
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    // The look is the event's, forever: hashed off the event id, steered by tier
    // (a City hall bills the street or its scrappier cousins; a PPV gets the
    // premium treatments; title fights unlock the championship looks).
    private static Template Pick(World world, Event ev, Promotion? promo)
    {
        uint h = Hash((uint)ev.Id.Value);
        bool titleFight = ev.Card.Count > 0 && ev.Card[^1].IsTitleFight;
        bool hasCoMain = ev.Card.Count >= 2;
        bool hasThree = ev.Card.Count >= 3;
        if (promo is not null && Core.Sim.Org.OrgSizing.SizeOf(promo) == OrgSize.City)
            return (h % 5) switch   // small halls: photocopy energy, never black tie
            {
                0 or 1 => Template.Street,          // the street bill still leads
                2 => Template.Vintage,
                3 => Template.Hometown,
                _ => Template.Brutalist,
            };
        if (ev.Tier >= EventTier.Ppv)   // the premium looks — gold for the belts
            return titleFight && h % 2 == 0
                 ? (h % 3) switch { 0 => Template.Gold, 1 => Template.Champion, _ => Template.Belt }
                 : (h % 7) switch
                 {
                     0 => Template.OneSheet,
                     1 => Template.Marquee,
                     2 when hasThree => Template.TripleBill,    // a stacked card sells the depth
                     2 when hasCoMain => Template.DoubleBill,
                     3 when hasCoMain => Template.Stacked,      // the interlocked two-fight one-sheet
                     4 => Template.Deco,
                     5 => Template.Blackout,
                     6 => Template.Countdown,
                     _ => Template.Closeup,
                 };
        return (h % 13) switch
        {
            0 => Template.Arena, 1 => Template.Split, 2 => Template.OneSheet,
            3 when hasThree => Template.TripleBill,
            3 when hasCoMain => Template.DoubleBill, 3 => Template.Marquee,
            4 => Template.Neon,
            5 => Template.Slash,
            6 => Template.RingLights,
            7 => Template.Corners,
            8 => Template.Tape,
            9 => Template.Bolt,
            10 => Template.Broadsheet,
            11 => Template.Countdown,
            _ => Template.Closeup,
        };
    }

    // ============ TEMPLATE: the arena shaft (the classic one-sheet) ============
    private static void Arena(DrawingContext ctx, World world, Event ev, Promotion? promo, Fighter? red, Fighter? blue, Palette pal)
    {
        ctx.DrawRectangle(new LinearGradientBrush(Rgb(0x14161B), Rgb(0x08090C), 90).Frozen(), null, new Rect(0, 0, W, H));
        ctx.DrawRectangle(new RadialGradientBrush(WithA(pal.Glow, 0x58), WithA(pal.Glow, 0x00))
        {
            Center = new Point(0.5, 0.30), GradientOrigin = new Point(0.5, 0.30), RadiusX = 0.62, RadiusY = 0.5,
        }.Frozen(), null, new Rect(0, 0, W, H));

        if (red is not null && blue is not null)
        {
            // Waist-up and big, starting BELOW the tagline band — the plate and
            // the faces each own their own zone, nothing overlaps.
            Body(ctx, world, red, W * 0.27, 256, 620, mirror: false);
            Body(ctx, world, blue, W * 0.73, 256, 620, mirror: true);
            var fade = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            fade.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0x09, 0x0A, 0x0D), 0));
            fade.GradientStops.Add(new GradientStop(Color.FromArgb(0xD8, 0x09, 0x0A, 0x0D), 0.45));
            fade.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0x09, 0x0A, 0x0D), 1));
            ctx.DrawRectangle(fade.Frozen(), null, new Rect(0, 440, W, H - 440));
            Marquee(ctx, red, blue, pal, 588);
        }
        else Teaser(ctx, pal);

        Masthead(ctx, ev, promo, pal);
        TaglineRow(ctx, world, ev, pal, red is null ? 340 : 202);
        VenueStrip(ctx, world, ev, pal);
    }

    // ============ TEMPLATE: the corner split (red corner vs blue corner) ============
    private static void Split(DrawingContext ctx, World world, Event ev, Promotion? promo, Fighter? red, Fighter? blue, Palette pal)
    {
        // Two tinted halves under a hard diagonal — the oldest fight-poster trick.
        // The blade is MIRROR-SYMMETRIC about the centre (0.58 top → 0.42 bottom),
        // so the composition pivots on the middle instead of drifting sideways.
        ctx.DrawRectangle(new LinearGradientBrush(Rgb(0x3A1216), Rgb(0x0B0708), new Point(0, 0), new Point(0.7, 1)).Frozen(),
            null, new Rect(0, 0, W, H));
        ctx.DrawGeometry(new LinearGradientBrush(Rgb(0x101B2E), Rgb(0x070A10), new Point(1, 0), new Point(0.3, 1)).Frozen(),
            null, Poly((W * 0.58, 0), (W, 0), (W, H), (W * 0.42, H)));
        ctx.DrawGeometry(Frozen(WithA(pal.Accent, 0xCC)), null, Poly((W * 0.565, 0), (W * 0.595, 0), (W * 0.435, H), (W * 0.405, H)));

        if (red is not null && blue is not null)
        {
            // The fighters OWN their halves, starting BELOW the tagline band —
            // waist-up poster crops, nothing under the plate.
            Body(ctx, world, red, W * 0.26, 254, 640, mirror: false);
            Body(ctx, world, blue, W * 0.74, 254, 640, mirror: true);
            BottomFade(ctx, 0.58);
            // Names anchor their own corners, receipts underneath, VS on the blade.
            Text(ctx, red.LastName.ToUpperInvariant(), 46, White, W * 0.27, 612);
            Text(ctx, blue.LastName.ToUpperInvariant(), 46, White, W * 0.73, 612);
            Text(ctx, red.Record.ToString(), 17, Muted, W * 0.27, 674);
            Text(ctx, blue.Record.ToString(), 17, Muted, W * 0.73, 674);
            Text(ctx, "VS", 24, White, W * 0.452, 640);   // sits ON the blade's path — white so the blade can't eat it
        }
        else Teaser(ctx, pal);

        Masthead(ctx, ev, promo, pal);
        TaglineRow(ctx, world, ev, pal, red is null ? 340 : 208);
        VenueStrip(ctx, world, ev, pal);
    }

    // ============ TEMPLATE: the face-off closeup (nose to nose) ============
    private static void Closeup(DrawingContext ctx, World world, Event ev, Promotion? promo, Fighter? red, Fighter? blue, Palette pal)
    {
        ctx.DrawRectangle(new LinearGradientBrush(Rgb(0x0D0E12), Rgb(0x060708), 90).Frozen(), null, new Rect(0, 0, W, H));

        if (red is not null && blue is not null)
        {
            // Two HUGE faces, each CLIPPED to its own half-panel — the photo fills
            // the panel edge-to-edge and the crop reads as deliberate, instead of
            // limbs falling off the side of the poster. They meet at the seam.
            const double faceTop = 196, face = 404, half = W / 2;
            var rf = Portraits.For(red, world);
            var bf = Portraits.For(blue, world);

            ctx.PushClip(new RectangleGeometry(new Rect(0, faceTop, half, face)));
            ctx.DrawImage(rf, new Rect(half / 2 - face / 2, faceTop, face, face));     // head centred in its panel
            ctx.Pop();

            ctx.PushClip(new RectangleGeometry(new Rect(half, faceTop, half, face)));
            ctx.PushTransform(new ScaleTransform(-1, 1, half + half / 2, 0));          // mirrored within the panel
            ctx.DrawImage(bf, new Rect(half + half / 2 - face / 2, faceTop, face, face));
            ctx.Pop();
            ctx.Pop();
            // A colour wash + centre seam so the two halves read as one image.
            ctx.DrawRectangle(new LinearGradientBrush(WithA(Rgb(0x8A1E24), 0x30), WithA(Rgb(0x1C3A6E), 0x30), 0).Frozen(),
                null, new Rect(0, faceTop, W, face));
            ctx.DrawRectangle(Frozen(WithA(pal.Accent, 0xB0)), null, new Rect(W / 2 - 1.5, faceTop, 3, face));
            ctx.DrawRectangle(new LinearGradientBrush(Color.FromArgb(0x00, 0x06, 0x07, 0x08), Color.FromArgb(0xF5, 0x06, 0x07, 0x08), 90).Frozen(),
                null, new Rect(0, faceTop + face - 90, W, H - (faceTop + face - 90)));

            // The staredown sells itself — one hard line of type, then the receipts.
            TaglineRow(ctx, world, ev, pal, 612);
            Text(ctx, $"{red.LastName.ToUpperInvariant()}  VS  {blue.LastName.ToUpperInvariant()}", 44, White, W / 2, 642);
            Text(ctx, $"{red.Record}     ·     {blue.Record}", 15, Muted, W / 2, 704);
        }
        else
        {
            Teaser(ctx, pal);
            TaglineRow(ctx, world, ev, pal, 340);
        }

        Masthead(ctx, ev, promo, pal);
        VenueStrip(ctx, world, ev, pal);
    }

    // ============ TEMPLATE: the street bill (small-hall photocopy energy) ============
    private static void Street(DrawingContext ctx, World world, Event ev, Promotion? promo, Fighter? red, Fighter? blue, Palette pal)
    {
        // Poster paper in the org colour, dressed with cheap diagonal stripes.
        ctx.DrawRectangle(Frozen(pal.Paper), null, new Rect(0, 0, W, H));
        for (int i = -1; i < 12; i++)
            ctx.DrawGeometry(Frozen(WithA(Colors.Black, 0x14)), null,
                Poly((i * 80, 0), (i * 80 + 26, 0), (i * 80 - 100 + 26, H), (i * 80 - 100, H)));
        ctx.DrawRectangle(null, new Pen(Frozen(Ink), 5), new Rect(16, 16, W - 32, H - 32));

        if (promo is not null)
            ctx.DrawImage(LogoFactory.ForPromotion(promo), new Rect(W / 2 - 34, 34, 68, 68));
        Text(ctx, ((promo?.Name ?? "").ToUpperInvariant() + " PRESENTS"), 14, InkBrush, W / 2, 112, spaced: true);
        Text(ctx, ev.Name.ToUpperInvariant(), 44, InkBrush, W / 2, 132);

        if (EventHype.Tagline(world, ev) is { } tagline)
        {
            // The tagline goes on a slapped-on ink banner, slightly askew.
            var ft = Format(tagline, 22, Frozen(pal.Paper));
            ctx.PushTransform(new RotateTransform(-2.5, W / 2, 218));
            ctx.DrawRectangle(Frozen(Ink), null, new Rect(W / 2 - ft.Width / 2 - 18, 204, ft.Width + 36, ft.Height + 12));
            ctx.DrawText(ft, new Point(W / 2 - ft.Width / 2, 210));
            ctx.Pop();
        }

        if (red is not null && blue is not null)
        {
            // Waist-up and HUGE — the bill is the fighters. Torsos crop cleanly
            // AT the names band, shoulders shoulder past the stripes.
            ctx.PushClip(new RectangleGeometry(new Rect(21, 0, W - 42, 660)));
            Body(ctx, world, red, W * 0.27, 244, 640, mirror: false);
            Body(ctx, world, blue, W * 0.73, 244, 640, mirror: true);
            ctx.Pop();
            // The marquee is a full-width ink band — pure street printing.
            ctx.DrawRectangle(Frozen(Ink), null, new Rect(16, 660, W - 32, 62));
            Text(ctx, $"{red.LastName.ToUpperInvariant()}  ·  VS  ·  {blue.LastName.ToUpperInvariant()}", 30,
                Frozen(pal.Paper), W / 2, 672);
        }
        else
        {
            Text(ctx, "ONE NIGHT ONLY", 26, InkBrush, W / 2, 380, spaced: true);
            Text(ctx, "CARD TO BE ANNOUNCED", 18, InkBrush, W / 2, 430);
        }

        string city = Where(world, ev);
        ctx.DrawRectangle(Frozen(Ink), null, new Rect(48, 742, W - 96, 2));
        Text(ctx, ev.Date.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture).ToUpperInvariant()
            + (city.Length > 0 ? "  ·  " + city.ToUpperInvariant() : ""), 14, InkBrush, W / 2, 754, spaced: true);
    }

    // ============ TEMPLATE: the one-sheet (paper, ghost names, centre column) ============
    private static void OneSheet(DrawingContext ctx, World world, Event ev, Promotion? promo, Fighter? red, Fighter? blue, Palette pal)
    {
        // Washed paper ground — the fighters carry the colour.
        ctx.DrawRectangle(new LinearGradientBrush(Rgb(0xEDECE7), Rgb(0xD9D8D2), 90).Frozen(), null, new Rect(0, 0, W, H));
        ctx.DrawRectangle(new RadialGradientBrush(WithA(Colors.White, 0x66), WithA(Colors.White, 0x00))
        {
            Center = new Point(0.5, 0.35), GradientOrigin = new Point(0.5, 0.35), RadiusX = 0.75, RadiusY = 0.6,
        }.Frozen(), null, new Rect(0, 0, W, H));

        const double colW = 128, colX = (W - colW) / 2, bandTop = 648, bandH = 74;

        if (red is not null && blue is not null)
        {
            // Giant ghost surnames running down each fighter's side.
            GhostName(ctx, red.LastName.ToUpperInvariant(), 34);
            GhostName(ctx, blue.LastName.ToUpperInvariant(), W - 34 - 118);
        }

        // The centre column: the org's mark on ink, "MAIN EVENT" stacked in gold.
        ctx.DrawRectangle(Frozen(Rgb(0x15140F)), null, new Rect(colX, 0, colW, H));
        if (promo is not null)
            ctx.DrawImage(LogoFactory.ForPromotion(promo), new Rect(W / 2 - 40, 18, 80, 80));
        StackLetters(ctx, "MAIN", 118, pal);
        StackLetters(ctx, "EVENT", 356, pal);

        if (red is not null && blue is not null)
        {
            // Waist-up crops, each clipped EXACTLY at the column edge — the
            // fighters read as tucked cleanly behind the column, never sliced
            // halfway across it.
            ctx.PushClip(new RectangleGeometry(new Rect(0, 0, colX, bandTop)));
            Body(ctx, world, red, W * 0.22, 96, 880, mirror: false);
            ctx.Pop();
            ctx.PushClip(new RectangleGeometry(new Rect(colX + colW, 0, W - colX - colW, bandTop)));
            Body(ctx, world, blue, W * 0.78, 96, 880, mirror: true);
            ctx.Pop();
        }

        // The paper band interrupts the column: eyebrow + the marquee in ink.
        ctx.DrawRectangle(Frozen(Rgb(0xEDECE7)), null, new Rect(0, bandTop, W, bandH));
        string eyebrow = ev.Card.Count > 0 && ev.Card[^1].IsTitleFight
            ? $"{EventHype.BeltName(world, ev.Card[^1])} TITLE BOUT"
            : EventHype.Tagline(world, ev) ?? "THE MAIN EVENT";
        Text(ctx, eyebrow, 12, pal.DeepAccentBrush, W / 2, bandTop + 8, spaced: true);
        if (red is not null && blue is not null)
            Text(ctx, $"{red.LastName.ToUpperInvariant()}  VS  {blue.LastName.ToUpperInvariant()}", 34,
                Frozen(Rgb(0x15140F)), W / 2, bandTop + 24);
        else
            Text(ctx, ev.Name.ToUpperInvariant(), 34, Frozen(Rgb(0x15140F)), W / 2, bandTop + 24);

        // The column carries the date home.
        Text(ctx, ev.Date.ToString("MMM d", CultureInfo.InvariantCulture).ToUpperInvariant(), 26, pal.AccentBrush, W / 2, 736);
        Text(ctx, ev.Date.ToString("dddd", CultureInfo.InvariantCulture).ToUpperInvariant(), 13, pal.AccentBrush, W / 2, 768, spaced: true);
    }

    // ============ TEMPLATE: the championship gold (black tie for the belts) ============
    private static void Gold(DrawingContext ctx, World world, Event ev, Promotion? promo, Fighter? red, Fighter? blue, Palette pal)
    {
        // Rich black, a floor of gold light, and double hairline frames — the
        // restraint IS the luxury.
        ctx.DrawRectangle(Frozen(Rgb(0x0A0A0C)), null, new Rect(0, 0, W, H));
        ctx.DrawRectangle(new RadialGradientBrush(WithA(Rgb(0xE8B23A), 0x38), WithA(Rgb(0xE8B23A), 0x00))
        {
            Center = new Point(0.5, 0.78), GradientOrigin = new Point(0.5, 0.78), RadiusX = 0.7, RadiusY = 0.45,
        }.Frozen(), null, new Rect(0, 0, W, H));
        var gold = Frozen(Rgb(0xD8B24A));
        ctx.DrawRectangle(null, new Pen(gold, 1.6), new Rect(16, 16, W - 32, H - 32));
        ctx.DrawRectangle(null, new Pen(Frozen(WithA(Rgb(0xD8B24A), 0x66)), 0.8), new Rect(24, 24, W - 48, H - 48));

        if (promo is not null)
            ctx.DrawImage(LogoFactory.ForPromotion(promo), new Rect(W / 2 - 33, 40, 66, 66));
        Text(ctx, ev.Name.ToUpperInvariant(), 28, White, W / 2, 118);
        string eyebrow = ev.Card.Count > 0 && ev.Card[^1].IsTitleFight
            ? $"FOR THE {EventHype.BeltName(world, ev.Card[^1])} CHAMPIONSHIP"
            : EventHype.Tagline(world, ev) ?? "A NIGHT OF CHAMPIONSHIP FIGHTING";
        Text(ctx, eyebrow, 12, Frozen(Rgb(0xD8B24A)), W / 2, 162, spaced: true);
        ctx.DrawRectangle(gold, null, new Rect(W / 2 - 30, 186, 60, 1.2));

        if (red is not null && blue is not null)
        {
            // Waist-up crops inside the frame, meeting at a gold hairline seam.
            ctx.PushClip(new RectangleGeometry(new Rect(25, 208, W / 2 - 25, 430)));
            Body(ctx, world, red, W * 0.27, 216, 760, mirror: false);
            ctx.Pop();
            ctx.PushClip(new RectangleGeometry(new Rect(W / 2, 208, W / 2 - 25, 430)));
            Body(ctx, world, blue, W * 0.73, 216, 760, mirror: true);
            ctx.Pop();
            ctx.DrawRectangle(gold, null, new Rect(W / 2 - 0.8, 208, 1.6, 430));
            ctx.DrawRectangle(new LinearGradientBrush(Color.FromArgb(0x00, 0x0A, 0x0A, 0x0C), Color.FromArgb(0xEE, 0x0A, 0x0A, 0x0C), 90).Frozen(),
                null, new Rect(25, 560, W - 50, 80));

            Text(ctx, $"{red.LastName.ToUpperInvariant()}  ·  {blue.LastName.ToUpperInvariant()}", 36, White, W / 2, 654);
            Text(ctx, $"{red.Record}   VS   {blue.Record}", 13, Frozen(Rgb(0xD8B24A)), W / 2, 702, spaced: true);
        }
        else Text(ctx, "CARD TO BE ANNOUNCED", 20, Muted, W / 2, 420, spaced: true);

        Text(ctx, ev.Date.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture).ToUpperInvariant()
            + "   ·   " + Where(world, ev).ToUpperInvariant(), 12, Muted, W / 2, 748, spaced: true);
    }

    // ============ TEMPLATE: the marquee (type IS the poster) ============
    private static void Marquee(DrawingContext ctx, World world, Event ev, Promotion? promo, Fighter? red, Fighter? blue, Palette pal)
    {
        ctx.DrawRectangle(new LinearGradientBrush(Rgb(0x101116), Rgb(0x07080A), 90).Frozen(), null, new Rect(0, 0, W, H));
        ctx.DrawRectangle(new RadialGradientBrush(WithA(pal.Glow, 0x30), WithA(pal.Glow, 0x00))
        {
            Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5), RadiusX = 0.8, RadiusY = 0.6,
        }.Frozen(), null, new Rect(0, 0, W, H));

        if (red is not null && blue is not null)
        {
            // The names ARE the artwork: one filled behind the fighters, one
            // riding in front — giant, edge to edge.
            FitName(ctx, red.LastName.ToUpperInvariant(), 96, filled: true, pal);

            // The bodies dissolve INTO the dark before the clip line, so there's
            // no visible crop edge — they emerge from the poster, not a scissor.
            ctx.PushClip(new RectangleGeometry(new Rect(0, 0, W, 640)));
            Body(ctx, world, red, W * 0.32, 150, 760, mirror: false);
            Body(ctx, world, blue, W * 0.68, 170, 760, mirror: true);
            var dissolve = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
            dissolve.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0x07, 0x08, 0x0A), 0));
            dissolve.GradientStops.Add(new GradientStop(Color.FromArgb(0xE8, 0x07, 0x08, 0x0A), 0.82));
            dissolve.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0x07, 0x08, 0x0A), 1));
            ctx.DrawRectangle(dissolve.Frozen(), null, new Rect(0, 430, W, 210));
            ctx.Pop();
            ctx.DrawRectangle(Frozen(Color.FromArgb(0xFF, 0x07, 0x08, 0x0A)), null, new Rect(0, 640, W, H - 640));

            FitName(ctx, blue.LastName.ToUpperInvariant(), 508, filled: false, pal);
        }
        else Teaser(ctx, pal);

        if (promo is not null)
            ctx.DrawImage(LogoFactory.ForPromotion(promo), new Rect(30, 28, 54, 54));
        Text(ctx, ev.Name.ToUpperInvariant(), 32, White, W / 2, 682);
        if (EventHype.Tagline(world, ev) is { } tag)
            Text(ctx, tag, 13, pal.AccentBrush, W / 2, 36, spaced: true);
        VenueStrip(ctx, world, ev, pal);
    }

    // ============ TEMPLATE: the double bill (main AND co-main, two decks) ============
    private static void DoubleBill(DrawingContext ctx, World world, Event ev, Promotion? promo, Palette pal)
    {
        // A FLAT ground on purpose: the decks' dissolves melt into it invisibly
        // (a gradient ground leaves banding at every deck edge).
        ctx.DrawRectangle(Frozen(Rgb(0x0A0B0E)), null, new Rect(0, 0, W, H));

        if (promo is not null)
            ctx.DrawImage(LogoFactory.ForPromotion(promo), new Rect(30, 24, 54, 54));
        Text(ctx, ev.Name.ToUpperInvariant(), 32, White, W / 2, 24);
        if (EventHype.Tagline(world, ev) is { } tag)
            Text(ctx, tag, 12, pal.AccentBrush, W / 2, 66, spaced: true);

        var main = ev.Card.Count > 0 ? ev.Card[^1] : null;
        var co = ev.Card.Count > 1 ? ev.Card[^2] : null;

        // Two decks: the main event owns the sheet, the co-main rides beneath it.
        Deck(ctx, world, main, pal, "MAIN EVENT", top: 100, height: 368, bodyH: 480, nameCap: 88);
        ctx.DrawRectangle(Frozen(WithA(pal.Accent, 0x66)), null, new Rect(150, 480, W - 300, 1));
        Deck(ctx, world, co, pal, "CO-MAIN EVENT", top: 492, height: 240, bodyH: 320, nameCap: 50);

        VenueStrip(ctx, world, ev, pal);
    }

    // One billing deck: eyebrow, the two fighters dissolving into the dark, and
    // the marquee name line laid over their waists (fill + outline, legible).
    private static void Deck(DrawingContext ctx, World world, Fight? fight, Palette pal,
        string label, double top, double height, double bodyH, double nameCap)
    {
        Text(ctx, label, 12, pal.AccentBrush, W / 2, top, spaced: true);
        if (fight is null) { Text(ctx, "TO BE ANNOUNCED", 16, Muted, W / 2, top + height / 2); return; }
        var red = world.Get(fight.RedCorner);
        var blue = world.Get(fight.BlueCorner);
        if (red is null || blue is null) return;

        double bottom = top + height;
        double dissolveH = Math.Min(130, height * 0.5);
        ctx.PushClip(new RectangleGeometry(new Rect(0, top + 18, W, height - 18)));
        Body(ctx, world, red, W * 0.31, top + 24, bodyH, mirror: false);
        Body(ctx, world, blue, W * 0.69, top + 24, bodyH, mirror: true);
        // NOTE: the zero-alpha stop must be the TARGET colour at alpha 0 — WPF's
        // Colors.Transparent is transparent WHITE, and interpolating through it
        // paints a milky grey band across the poster.
        var dissolve = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        dissolve.GradientStops.Add(new GradientStop(Color.FromArgb(0x00, 0x0A, 0x0B, 0x0E), 0));
        dissolve.GradientStops.Add(new GradientStop(Color.FromArgb(0xE0, 0x0A, 0x0B, 0x0E), 0.80));
        dissolve.GradientStops.Add(new GradientStop(Color.FromArgb(0xFF, 0x0A, 0x0B, 0x0E), 1));   // melts into the flat ground
        ctx.DrawRectangle(dissolve.Frozen(), null, new Rect(0, bottom - dissolveH, W, dissolveH));
        ctx.Pop();

        FitLine(ctx, $"{red.LastName.ToUpperInvariant()} VS {blue.LastName.ToUpperInvariant()}",
            bottom - nameCap - 12, nameCap, pal);
    }

    // ============ TEMPLATE: the stacked (two fights INTERLOCKED, PPV-style) ============
    private static void Stacked(DrawingContext ctx, World world, Event ev, Promotion? promo, Palette pal)
    {
        // Rich black under fine gold ornament rings — the ceremony poster.
        ctx.DrawRectangle(Frozen(Rgb(0x0B0A08)), null, new Rect(0, 0, W, H));
        var ornament = Frozen(WithA(Rgb(0xC9A94F), 0x26));
        ctx.DrawEllipse(null, new Pen(ornament, 22), new Point(W / 2, 226), 268, 268);
        ctx.DrawEllipse(null, new Pen(Frozen(WithA(Rgb(0xC9A94F), 0x3A)), 1.6), new Point(W / 2, 226), 236, 236);
        ctx.DrawEllipse(null, new Pen(Frozen(WithA(Rgb(0xC9A94F), 0x1E)), 16), new Point(W / 2, 640), 210, 210);

        Text(ctx, ev.Name.ToUpperInvariant(), 30, Frozen(Rgb(0xD8B24A)), W / 2, 20);

        var main = ev.Card.Count > 0 ? ev.Card[^1] : null;
        var co = ev.Card.Count > 1 ? ev.Card[^2] : null;
        var gold = Frozen(Rgb(0xD8B24A));

        // The MAIN EVENT pair: two big face panels meeting at the centre.
        if (main is not null && world.Get(main.RedCorner) is { } mr && world.Get(main.BlueCorner) is { } mb)
        {
            FacePanel(ctx, world, mr, x: 0, top: 78, size: 300, mirror: false);
            FacePanel(ctx, world, mb, x: W / 2, top: 78, size: 300, mirror: true);
            Fade(ctx, 300, 378, 0x0B0A08);
        }

        // The CO-MAIN pair tucks up underneath them, inset toward the centre —
        // the four heads interlock instead of sitting in separate boxes.
        if (co is not null && world.Get(co.RedCorner) is { } cr && world.Get(co.BlueCorner) is { } cb)
        {
            FacePanel(ctx, world, cr, x: 34, top: 452, size: 252, mirror: false);
            FacePanel(ctx, world, cb, x: W - 34 - 252, top: 452, size: 252, mirror: true);
            Fade(ctx, 568, 704, 0x0B0A08);
        }

        // The championship block THREADS BETWEEN the two pairs, laid over the seam.
        if (main is not null && world.Get(main.RedCorner) is { } r2 && world.Get(main.BlueCorner) is { } b2)
        {
            string eyebrow = main.IsTitleFight
                ? $"WORLD {EventHype.BeltName(world, main)} CHAMPIONSHIP"
                : EventHype.Tagline(world, ev) ?? "THE MAIN EVENT";
            Text(ctx, eyebrow, 13, gold, W / 2, 384, spaced: true);
            Text(ctx, r2.LastName.ToUpperInvariant(), 34, gold, W / 2, 404);
            Text(ctx, "VS  " + b2.LastName.ToUpperInvariant(), 34, gold, W / 2, 444);
        }
        if (co is not null && world.Get(co.RedCorner) is { } r3 && world.Get(co.BlueCorner) is { } b3)
        {
            Text(ctx, $"{EventHype.BeltName(world, co)} BOUT", 12, gold, W / 2, 636, spaced: true);
            Text(ctx, $"{r3.LastName.ToUpperInvariant()}  VS  {b3.LastName.ToUpperInvariant()}", 27, gold, W / 2, 656);
        }
        if (main is null) Text(ctx, "CARD TO BE ANNOUNCED", 20, Muted, W / 2, 400, spaced: true);

        // The where and when, centre bottom like the ceremony demands.
        Text(ctx, ev.Date.ToString("MMM d", CultureInfo.InvariantCulture).ToUpperInvariant()
            + "  ·  " + ev.Date.ToString("ddd", CultureInfo.InvariantCulture).ToUpperInvariant(), 24, gold, W / 2, 706);
        Text(ctx, Where(world, ev).ToUpperInvariant(), 13, Muted, W / 2, 748, spaced: true);
    }

    // A face in its own square panel (over-or-under rule: crops live at panel edges).
    private static void FacePanel(DrawingContext ctx, World world, Fighter f, double x, double top, double size, bool mirror)
    {
        ctx.PushClip(new RectangleGeometry(new Rect(x, top, size, size)));
        if (mirror)
        {
            ctx.PushTransform(new ScaleTransform(-1, 1, x + size / 2, 0));
            ctx.DrawImage(Portraits.For(f, world), new Rect(x, top, size, size));
            ctx.Pop();
        }
        else
        {
            ctx.DrawImage(Portraits.For(f, world), new Rect(x, top, size, size));
        }
        ctx.Pop();
    }

    // A straight fade band into a flat colour (target colour at alpha 0 — never Colors.Transparent).
    private static void Fade(DrawingContext ctx, double top, double bottom, int rgb)
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        brush.GradientStops.Add(new GradientStop(WithA(Rgb(rgb), 0x00), 0));
        brush.GradientStops.Add(new GradientStop(WithA(Rgb(rgb), 0xFF), 1));
        ctx.DrawRectangle(brush.Frozen(), null, new Rect(0, top, W, bottom - top));
    }

    // ============ TEMPLATE: the triple bill (main, co-main, featured bout) ============
    private static void TripleBill(DrawingContext ctx, World world, Event ev, Promotion? promo, Palette pal)
    {
        ctx.DrawRectangle(Frozen(Rgb(0x0A0B0E)), null, new Rect(0, 0, W, H));

        if (promo is not null)
            ctx.DrawImage(LogoFactory.ForPromotion(promo), new Rect(28, 20, 50, 50));
        Text(ctx, ev.Name.ToUpperInvariant(), 30, White, W / 2, 20);
        if (EventHype.Tagline(world, ev) is { } tag)
            Text(ctx, tag, 11, pal.AccentBrush, W / 2, 60, spaced: true);

        var main = ev.Card.Count > 0 ? ev.Card[^1] : null;
        var co = ev.Card.Count > 1 ? ev.Card[^2] : null;
        var featured = ev.Card.Count > 2 ? ev.Card[^3] : null;

        // Three decks, each smaller than the one above — the billing IS the layout.
        Deck(ctx, world, main, pal, "MAIN EVENT", top: 88, height: 258, bodyH: 350, nameCap: 60);
        ctx.DrawRectangle(Frozen(WithA(pal.Accent, 0x66)), null, new Rect(170, 352, W - 340, 1));
        Deck(ctx, world, co, pal, "CO-MAIN EVENT", top: 362, height: 182, bodyH: 250, nameCap: 42);
        ctx.DrawRectangle(Frozen(WithA(pal.Accent, 0x66)), null, new Rect(170, 550, W - 340, 1));
        Deck(ctx, world, featured, pal, "FEATURED BOUT", top: 560, height: 168, bodyH: 226, nameCap: 38);

        VenueStrip(ctx, world, ev, pal);
    }

    // A full line ("RED VS BLUE") scaled to the sheet width — translucent fill
    // under a bright outline, the double bill's marquee signature.
    private static void FitLine(DrawingContext ctx, string line, double top, double cap, Palette pal)
    {
        var probe = Format(line, 100, White);
        double size = Math.Min(cap, 100 * (W - 48) / Math.Max(1, probe.Width));
        var ft = Format(line, size, Frozen(WithA(pal.Accent, 0x62)));
        var origin = new Point(W / 2 - ft.Width / 2, top + (cap - size) / 2);
        ctx.DrawText(ft, origin);
        ctx.DrawGeometry(null, new Pen(Frozen(WithA(pal.Accent, 0xE6)), 1.5), ft.BuildGeometry(origin));
    }

    // A name scaled to span the full sheet width. Behind the fighters: a quiet
    // ghost fill. In front of them: a translucent fill UNDER a bright outline —
    // layered over the image but still legible at a glance.
    private static void FitName(DrawingContext ctx, string name, double top, bool filled, Palette pal)
    {
        var probe = Format(name, 100, White);
        double size = Math.Min(170, 100 * (W - 44) / Math.Max(1, probe.Width));
        var origin = new Point(0, 0);
        if (filled)
        {
            var ft = Format(name, size, Frozen(WithA(pal.Accent, 0x2C)));
            ctx.DrawText(ft, new Point(W / 2 - ft.Width / 2, top));
        }
        else
        {
            var ft = Format(name, size, Frozen(WithA(pal.Accent, 0x62)));
            origin = new Point(W / 2 - ft.Width / 2, top);
            ctx.DrawText(ft, origin);
            ctx.DrawGeometry(null, new Pen(Frozen(WithA(pal.Accent, 0xE6)), 1.7), ft.BuildGeometry(origin));
        }
    }

    // A surname in enormous pale letters running top-to-bottom behind its fighter.
    private static void GhostName(DrawingContext ctx, string name, double x)
    {
        var ft = new FormattedText(name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Bahnschrift Condensed, Segoe UI"), FontStyles.Normal, FontWeights.Black, FontStretches.Condensed),
            118, Frozen(WithA(Rgb(0xB9B8B0), 0x9A)), 1.0);
        ctx.PushTransform(new RotateTransform(90, x, 24));
        ctx.DrawText(ft, new Point(x, 24 - 118));
        ctx.Pop();
    }

    // "M A I N" stacked down the centre column, one upright letter per line.
    private static void StackLetters(DrawingContext ctx, string word, double top, Palette pal)
    {
        for (int i = 0; i < word.Length; i++)
        {
            var ft = Format(word[i].ToString(), 46, pal.AccentBrush);
            ctx.DrawText(ft, new Point(W / 2 - ft.Width / 2, top + i * 54));
        }
    }

    // ============ shared furniture ============
    private static void Masthead(DrawingContext ctx, Event ev, Promotion? promo, Palette pal)
    {
        if (promo is not null)
            ctx.DrawImage(LogoFactory.ForPromotion(promo), new Rect(W / 2 - 42, 26, 84, 84));
        Text(ctx, (promo?.Abbreviation ?? "").ToUpperInvariant() + " PRESENTS", 16, White, W / 2, 122, spaced: true);
        Text(ctx, ev.Name.ToUpperInvariant(), 36, White, W / 2, 148);
    }

    private static void TaglineRow(DrawingContext ctx, World world, Event ev, Palette pal, double y)
    {
        if (EventHype.Tagline(world, ev) is not { } tagline) return;
        // The story line rides on an INK PLATE so it stays legible over anything —
        // a same-colour blade, a bright glow, a fighter's shoulder.
        var ft = Format(tagline, 24, pal.AccentBrush);
        ctx.DrawRoundedRectangle(Frozen(Color.FromArgb(0xD2, 0x0B, 0x0C, 0x10)), null,
            new Rect(W / 2 - ft.Width / 2 - 16, y - 7, ft.Width + 32, ft.Height + 14), 3, 3);
        ctx.DrawRectangle(Frozen(WithA(pal.Accent, 0x88)), null, new Rect(W / 2 - ft.Width / 2 - 56, y + ft.Height / 2 - 1, 32, 2));
        ctx.DrawRectangle(Frozen(WithA(pal.Accent, 0x88)), null, new Rect(W / 2 + ft.Width / 2 + 24, y + ft.Height / 2 - 1, 32, 2));
        ctx.DrawText(ft, new Point(W / 2 - ft.Width / 2, y));
    }

    private static void Marquee(DrawingContext ctx, Fighter red, Fighter blue, Palette pal, double y)
    {
        Text(ctx, red.LastName.ToUpperInvariant(), 46, White, W / 2, y);
        Text(ctx, "VS", 20, pal.AccentBrush, W / 2, y + 54);
        Text(ctx, blue.LastName.ToUpperInvariant(), 46, White, W / 2, y + 80);
    }

    private static void Teaser(DrawingContext ctx, Palette pal) =>
        Text(ctx, "CARD TO BE ANNOUNCED", 22, Muted, W / 2, 420);

    private static void VenueStrip(DrawingContext ctx, World world, Event ev, Palette pal)
    {
        string city = Where(world, ev);
        ctx.DrawRectangle(Frozen(WithA(pal.Accent, 0x88)), null, new Rect(60, 743, W - 120, 1));
        Text(ctx, ev.Date.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture).ToUpperInvariant()
            + (city.Length > 0 ? "   ·   " + city.ToUpperInvariant() : ""), 15, White, W / 2, 756, spaced: true);
    }

    private static string Where(World world, Event ev) =>
        ev.VenueId is { } vid && world.Get(vid) is { } venue
            ? $"{venue.Name}  ·  {world.Get(venue.City)?.Name ?? ""}"
            : ev.Venue ?? "";

    private static void Body(DrawingContext ctx, World world, Fighter f, double centerX, double top, double height, bool mirror)
    {
        var body = Portraits.Body(f, world);
        double width = body.Width > 0 && body.Height > 0 ? height * body.Width / body.Height : height * 0.52;
        width = Math.Min(width, height * 0.64);
        var rect = new Rect(centerX - width / 2, top, width, height);
        if (!mirror) { ctx.DrawImage(body, rect); return; }
        ctx.PushTransform(new ScaleTransform(-1, 1, centerX, 0));
        ctx.DrawImage(body, rect);
        ctx.Pop();
    }

    private static void BottomFade(DrawingContext ctx, double from) =>
        ctx.DrawRectangle(new LinearGradientBrush(Color.FromArgb(0x00, 0x07, 0x08, 0x0B), Color.FromArgb(0xF0, 0x07, 0x08, 0x0B), 90).Frozen(),
            null, new Rect(0, H * from, W, H * (1 - from)));

    // ============ palettes: the org's colours dress the poster ============
    private sealed record Palette(Color Accent, Color Glow, Color Paper)
    {
        public Brush AccentBrush { get; } = Frozen(Accent);

        /// <summary>The accent darkened for LIGHT grounds (eyebrows on paper).</summary>
        public Brush DeepAccentBrush { get; } = Frozen(Color.FromRgb(
            (byte)(Accent.R * 0.66), (byte)(Accent.G * 0.66), (byte)(Accent.B * 0.66)));
    }

    /// <summary>The org's signature accent colour — the same one that dresses its
    /// posters — so the shell chrome can wear the player promotion's colours.</summary>
    public static Color AccentFor(Promotion? promo) => PaletteFor(promo).Accent;

    private static Palette PaletteFor(Promotion? promo) => promo?.Abbreviation switch
    {
        "AFC" => new(Rgb(0xE8B23A), Rgb(0xE8D9B0), Rgb(0xE8B23A)),   // apex gold
        "VMMA" => new(Rgb(0x9FB6D9), Rgb(0xB9CBE8), Rgb(0x2C4A7C)),  // continental silver-blue
        "ICF" => new(Rgb(0xD94F4F), Rgb(0xE8A9A0), Rgb(0xB33939)),   // rising-sun red
        "CK" => new(Rgb(0x59C26E), Rgb(0xB9E8C4), Rgb(0x1E7A38)),    // Brazil green
        "GFC" => new(Rgb(0xC9A94F), Rgb(0xE8DCB0), Rgb(0x244A3A)),   // gulf gold on green
        "IFA" => new(Rgb(0xB8443C), Rgb(0xD98F84), Rgb(0x8F2F2A)),   // hard-school crimson
        null => new(Rgb(0xE8B23A), Rgb(0xE8D9B0), Rgb(0xC9B037)),
        // Everyone else draws a signature look from a curated, poster-safe set
        // (fight posters live in steel, rust, olive and oxblood — never bubblegum),
        // hashed off the name so it's stable forever.
        _ => Stock[Hash((uint)promo!.Abbreviation.GetHashCode2()) % (uint)Stock.Length],
    };

    private static readonly Palette[] Stock =
    {
        new(Rgb(0x7FA6C9), Rgb(0xB9CFE3), Rgb(0x3E5F7E)),   // cold steel blue
        new(Rgb(0xD97B4F), Rgb(0xE8B99C), Rgb(0x9C4B2E)),   // rust
        new(Rgb(0xB9C46A), Rgb(0xD9DFA9), Rgb(0x5F6B33)),   // army olive
        new(Rgb(0x6FC4B4), Rgb(0xB0E3D9), Rgb(0x2E6B60)),   // deep teal
        new(Rgb(0xE0A23C), Rgb(0xEDCB93), Rgb(0xA8701F)),   // amber
        new(Rgb(0xC9606B), Rgb(0xE3AAB1), Rgb(0x77303C)),   // oxblood
        new(Rgb(0x9C93C9), Rgb(0xC8C2E3), Rgb(0x4E4778)),   // storm violet
        new(Rgb(0x8FBF6E), Rgb(0xC5DFB2), Rgb(0x47702F)),   // pine
    };

    // ============ drawing kit ============
    private static readonly Brush White = Frozen(Rgb(0xF2F3F5));
    private static readonly Brush Muted = Frozen(Rgb(0x9AA4AF));
    private static readonly Color Ink = Rgb(0x141216);
    private static readonly Brush InkBrush = Frozen(Rgb(0x141216));

    private static Color Rgb(int rgb) => Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    private static Color WithA(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
    private static T Frozen<T>(this T brush) where T : Brush { brush.Freeze(); return brush; }

    // Deterministic string hash (GetHashCode is randomised per process).
    private static int GetHashCode2(this string s)
    {
        unchecked { int h = 23; foreach (char ch in s) h = h * 31 + ch; return h; }
    }

    private static uint Hash(uint x)
    {
        unchecked { x ^= x >> 16; x *= 2246822519u; x ^= x >> 13; x *= 3266489917u; x ^= x >> 16; return x; }
    }

    private static Geometry Poly(params (double X, double Y)[] pts)
    {
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(pts[0].X, pts[0].Y), true, true);
            for (int i = 1; i < pts.Length; i++) g.LineTo(new Point(pts[i].X, pts[i].Y), false, false);
        }
        geo.Freeze();
        return geo;
    }

    private static FormattedText Format(string text, double size, Brush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Bahnschrift Condensed, Segoe UI"), FontStyles.Normal, FontWeights.Black, FontStretches.Condensed),
            size, brush, 1.0);

    private static void Text(DrawingContext ctx, string text, double size, Brush brush, double centerX, double top, bool spaced = false)
    {
        if (spaced) text = string.Join(" ", text.ToCharArray());   // cheap tracking for small caps lines
        var ft = Format(text, size, brush);
        ft.MaxTextWidth = W - 40;
        ft.MaxLineCount = 1;
        ft.Trimming = TextTrimming.CharacterEllipsis;
        ft.TextAlignment = TextAlignment.Center;
        ctx.DrawText(ft, new Point(centerX - (W - 40) / 2, top));
    }
}
