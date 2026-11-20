using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MmaManager.Core.Model;

namespace MmaManager.Wpf.Map;

/// <summary>
/// The drawn world map: Natural Earth land on a dark sea (Robinson, see <see cref="WorldShapes"/>),
/// each country faintly tinted by the view model, and every city a glow whose size and colour follow
/// the active layer - a heat map. Hot cities are labelled (greedy placement around the dot, nothing
/// overlaps); the rest name themselves on hover. Hover a city or a country for the hover card; click a
/// city, a country or a region's name to drill.
///
/// Three retained layers, each redrawn only when its inputs change: land (size, zoom, tints), cities
/// (size, zoom, the spots), and a thin hover overlay (the ring and the country highlight). The land's
/// shapes are built and frozen once per run; zooming is a transform, never a rebuild.
/// It reads only what the view model hands it, and writes nothing but the hover back.
/// </summary>
public sealed class WorldMapCanvas : FrameworkElement
{
    // Bottom to top: land, the hovered country's highlight, the cities, the hovered city's ring.
    private readonly DrawingVisual _land = new(), _landHover = new(), _cities = new(), _overlay = new();
    private UIElement? _card;
    private Matrix _m = Matrix.Identity;
    private Size _laidOut;
    private bool _pressed;
    private Point? _mouse;
    private object? _pressTarget;

    private readonly List<(MapSpot Spot, Point At, double Core)> _dots = new();
    private readonly List<(Rect Rect, MapSpot Spot)> _labelHits = new();
    private readonly List<(Rect Rect, MapRegionLabel Label)> _regionHits = new();

    // Palette: a dark sea under slightly lighter land, coasts a touch lighter still.
    private static readonly Brush Sea = Frozen(Color.FromRgb(0x0B, 0x11, 0x17));
    private static readonly Color LandColor = Color.FromRgb(0x22, 0x29, 0x32);
    private static readonly Color Coast = Color.FromRgb(0x36, 0x40, 0x4C);
    private static readonly Color GraticuleColor = Color.FromRgb(0x15, 0x1E, 0x28);
    private static readonly Color Edge = Color.FromRgb(0x2A, 0x34, 0x40);
    private static readonly Brush Gold = Frozen(Color.FromRgb(0xE8, 0xB2, 0x3A));
    private static readonly Brush QuietFill = Frozen(Color.FromRgb(0x5A, 0x64, 0x70));
    private static readonly Brush LabelInk = Frozen(Color.FromRgb(0xE6, 0xE9, 0xEC));
    private static readonly Brush LabelQuiet = Frozen(Color.FromRgb(0x8A, 0x92, 0x9B));
    private static readonly Brush RegionInk = Frozen(Color.FromRgb(0x7D, 0x88, 0x95));
    private static readonly Brush Halo = Frozen(Color.FromArgb(0xD8, 0x0B, 0x11, 0x17));
    private static readonly Brush HoverLand = Frozen(Color.FromArgb(0x16, 0xFF, 0xFF, 0xFF));
    private static readonly Brush DotEdge = Frozen(Color.FromArgb(0xC0, 0x0B, 0x11, 0x17));

    public WorldMapCanvas()
    {
        AddVisualChild(_land);
        AddVisualChild(_landHover);
        AddVisualChild(_cities);
        AddVisualChild(_overlay);
        ClipToBounds = true;
        Focusable = false;
    }

    // --- Inputs ---------------------------------------------------------------------------------

    public static readonly DependencyProperty SpotsProperty = DependencyProperty.Register(nameof(Spots), typeof(IReadOnlyList<MapSpot>), typeof(WorldMapCanvas),
        new PropertyMetadata(null, (d, _) => ((WorldMapCanvas)d).Redraw(cities: true)));
    public IReadOnlyList<MapSpot>? Spots { get => (IReadOnlyList<MapSpot>?)GetValue(SpotsProperty); set => SetValue(SpotsProperty, value); }

    public static readonly DependencyProperty RegionLabelsProperty = DependencyProperty.Register(nameof(RegionLabels), typeof(IReadOnlyList<MapRegionLabel>), typeof(WorldMapCanvas),
        new PropertyMetadata(null, (d, _) => ((WorldMapCanvas)d).Redraw(cities: true)));
    public IReadOnlyList<MapRegionLabel>? RegionLabels { get => (IReadOnlyList<MapRegionLabel>?)GetValue(RegionLabelsProperty); set => SetValue(RegionLabelsProperty, value); }

    /// <summary>Land colour per Natural Earth code (missing = plain land).</summary>
    public static readonly DependencyProperty CountryFillsProperty = DependencyProperty.Register(nameof(CountryFills), typeof(IReadOnlyDictionary<string, Color>), typeof(WorldMapCanvas),
        new PropertyMetadata(null, (d, _) => ((WorldMapCanvas)d).Redraw(land: true)));
    public IReadOnlyDictionary<string, Color>? CountryFills { get => (IReadOnlyDictionary<string, Color>?)GetValue(CountryFillsProperty); set => SetValue(CountryFillsProperty, value); }

    /// <summary>The country drilled into (Natural Earth code): outlined in gold.</summary>
    public static readonly DependencyProperty FocusCountryProperty = DependencyProperty.Register(nameof(FocusCountry), typeof(string), typeof(WorldMapCanvas),
        new PropertyMetadata(null, (d, _) => ((WorldMapCanvas)d).Redraw(land: true)));
    public string? FocusCountry { get => (string?)GetValue(FocusCountryProperty); set => SetValue(FocusCountryProperty, value); }

    /// <summary>The box to zoom to; null = the whole world.</summary>
    public static readonly DependencyProperty FocusProperty = DependencyProperty.Register(nameof(Focus), typeof(MapFocus), typeof(WorldMapCanvas),
        new PropertyMetadata(null, (d, _) => ((WorldMapCanvas)d).Redraw(land: true, cities: true, transform: true)));
    public MapFocus? Focus { get => (MapFocus?)GetValue(FocusProperty); set => SetValue(FocusProperty, value); }

    public static readonly DependencyProperty HoverSpotProperty = DependencyProperty.Register(nameof(HoverSpot), typeof(MapSpot), typeof(WorldMapCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((WorldMapCanvas)d).HoverChanged()));
    public MapSpot? HoverSpot { get => (MapSpot?)GetValue(HoverSpotProperty); set => SetValue(HoverSpotProperty, value); }

    /// <summary>The land under the pointer (Natural Earth code) when no city is.</summary>
    public static readonly DependencyProperty HoverCountryProperty = DependencyProperty.Register(nameof(HoverCountry), typeof(string), typeof(WorldMapCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((WorldMapCanvas)d).HoverChanged()));
    public string? HoverCountry { get => (string?)GetValue(HoverCountryProperty); set => SetValue(HoverCountryProperty, value); }

    public static readonly DependencyProperty SpotCommandProperty = DependencyProperty.Register(nameof(SpotCommand), typeof(ICommand), typeof(WorldMapCanvas));
    public ICommand? SpotCommand { get => (ICommand?)GetValue(SpotCommandProperty); set => SetValue(SpotCommandProperty, value); }

    public static readonly DependencyProperty CountryCommandProperty = DependencyProperty.Register(nameof(CountryCommand), typeof(ICommand), typeof(WorldMapCanvas));
    public ICommand? CountryCommand { get => (ICommand?)GetValue(CountryCommandProperty); set => SetValue(CountryCommandProperty, value); }

    public static readonly DependencyProperty RegionCommandProperty = DependencyProperty.Register(nameof(RegionCommand), typeof(ICommand), typeof(WorldMapCanvas));
    public ICommand? RegionCommand { get => (ICommand?)GetValue(RegionCommandProperty); set => SetValue(RegionCommandProperty, value); }

    /// <summary>The hover card (bound to the view model); placed beside whatever is hovered, flipped to stay inside.</summary>
    public UIElement? HoverCard
    {
        get => _card;
        set
        {
            if (ReferenceEquals(_card, value)) return;
            if (_card is not null) { RemoveVisualChild(_card); RemoveLogicalChild(_card); }
            _card = value;
            if (_card is not null) { AddLogicalChild(_card); AddVisualChild(_card); _card.IsHitTestVisible = false; }
            InvalidateMeasure();
        }
    }

    protected override IEnumerator LogicalChildren => _card is null ? Enumerable.Empty<object>().GetEnumerator() : new object[] { _card }.GetEnumerator();
    protected override int VisualChildrenCount => _card is null ? 4 : 5;
    protected override Visual GetVisualChild(int index) => index switch
    {
        0 => _land, 1 => _landHover, 2 => _cities, 3 => _overlay,
        _ => _card ?? throw new ArgumentOutOfRangeException(nameof(index)),
    };

    // --- Layout ---------------------------------------------------------------------------------

    protected override Size MeasureOverride(Size available)
    {
        _card?.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return new Size(double.IsInfinity(available.Width) ? 900 : available.Width,
                        double.IsInfinity(available.Height) ? 460 : available.Height);
    }

    protected override Size ArrangeOverride(Size final)
    {
        if (final != _laidOut)
        {
            _laidOut = final;
            Redraw(land: true, cities: true, transform: true);
        }
        if (_card is not null)
        {
            var size = _card.DesiredSize;
            var a = Anchor();
            double x = 0, y = 0;
            if (a is { } at)
            {
                x = at.X + 18; y = at.Y + 14;
                if (x + size.Width > final.Width - 6) x = at.X - 18 - size.Width;
                if (y + size.Height > final.Height - 6) y = at.Y - 14 - size.Height;
                x = Math.Max(6, x); y = Math.Max(6, y);
            }
            _card.Arrange(new Rect(new Point(x, y), size));
        }
        return final;
    }

    private Point? Anchor()
    {
        if (HoverSpot is { } s) return ScreenOf(s);
        if (HoverCountry is null) return null;
        if (_mouse is { } m) return m;
        return WorldShapes.ByCode(HoverCountry) is { } shape ? _m.Transform(new Point(shape.Bounds.X + shape.Bounds.Width / 2, shape.Bounds.Y + shape.Bounds.Height / 2)) : null;
    }

    private bool IsWorld => Focus is null;

    private void Redraw(bool land = false, bool cities = false, bool transform = false)
    {
        if (_laidOut.Width < 20 || _laidOut.Height < 20) return;
        if (transform) ComputeTransform();
        if (land) DrawLand();
        if (cities) DrawCities();
        DrawOverlay();
        InvalidateArrange();
    }

    private void ComputeTransform()
    {
        Rect target = Focus is { } f ? WorldShapes.BoxRect(f.West, f.East, f.South, f.North) : WorldShapes.WorldRect;
        double pad = IsWorld ? 4 : 0;
        double s = Math.Min((_laidOut.Width - 2 * pad) / target.Width, (_laidOut.Height - 2 * pad) / target.Height);
        double ox = _laidOut.Width / 2 - s * (target.X + target.Width / 2);
        double oy = _laidOut.Height / 2 - s * (target.Y + target.Height / 2);
        _m = new Matrix(s, 0, 0, s, ox, oy);
    }

    private double Scale => _m.M11;
    private Point ScreenOf(MapSpot s) => _m.Transform(WorldShapes.ProjectGeo(s.Lat, s.Lon));

    // --- Land -----------------------------------------------------------------------------------

    private void DrawLand()
    {
        using var dc = _land.RenderOpen();
        double s = Scale;
        if (!IsWorld)
        {
            // Zoomed: the sea fills the frame (rounded like the app's panels) and the land is clipped to it.
            var frame = new RectangleGeometry(new Rect(_laidOut), 8, 8);
            frame.Freeze();
            dc.DrawGeometry(Sea, new Pen(Frozen(Edge), 1), frame);
            dc.PushClip(frame);
        }
        dc.PushTransform(new MatrixTransform(_m));
        if (IsWorld)
        {
            dc.DrawGeometry(Sea, null, WorldShapes.Outline);
            dc.PushClip(WorldShapes.Outline);
        }
        dc.DrawGeometry(null, new Pen(Frozen(GraticuleColor), 1 / s), WorldShapes.Graticule);
        var fills = CountryFills;
        var cache = new Dictionary<Color, Brush>();
        var coast = new Pen(Frozen(Coast), 0.7 / s) { LineJoin = PenLineJoin.Round };
        coast.Freeze();
        foreach (var shape in WorldShapes.Land)
        {
            var c = fills is not null && fills.TryGetValue(shape.Code, out var fc) ? fc : LandColor;
            if (!cache.TryGetValue(c, out var b)) cache[c] = b = Frozen(c);
            dc.DrawGeometry(b, coast, shape.Geometry);
        }
        if (FocusCountry is { } fcode && WorldShapes.ByCode(fcode) is { } focus)
            dc.DrawGeometry(null, new Pen(Gold, 1.6 / s) { LineJoin = PenLineJoin.Round }, focus.Geometry);
        if (IsWorld)
        {
            dc.Pop();
            dc.DrawGeometry(null, new Pen(Frozen(Edge), 1.2 / s), WorldShapes.Outline);
        }
        dc.Pop();
        if (!IsWorld) dc.Pop();
    }

    // --- Cities ---------------------------------------------------------------------------------

    private double GlowScale => IsWorld ? Math.Clamp(_laidOut.Width / 1500, 0.7, 1.1) : 1.5;

    private double GlowRadius(MapSpot s) => (10 + 30 * Math.Pow(s.Heat, 1.1)) * GlowScale;
    private double CoreRadius(MapSpot s) => s.Lit ? (2.3 + 2.4 * s.Heat) * Math.Sqrt(GlowScale) : 2.4 * Math.Sqrt(GlowScale);

    private void DrawCities()
    {
        _dots.Clear(); _labelHits.Clear(); _regionHits.Clear();
        using var dc = _cities.RenderOpen();
        var spots = Spots;
        if (spots is null) return;
        var at = spots.Select(s => (Spot: s, At: ScreenOf(s))).ToList();

        // Quiet places first (nobody here knows the name): small grey dots, no glow.
        foreach (var (s, p) in at.Where(t => !t.Spot.Lit))
        {
            double r = CoreRadius(s);
            dc.DrawEllipse(QuietFill, new Pen(DotEdge, 1), p, r, r);
            _dots.Add((s, p, r));
        }
        // Glows, coolest first so the hottest sit on top.
        var lit = at.Where(t => t.Spot.Lit).OrderBy(t => t.Spot.Value).ThenBy(t => t.Spot.CityId).ToList();
        foreach (var (s, p) in lit)
        {
            double r = GlowRadius(s);
            dc.DrawEllipse(GlowBrush(s.Color, s.Heat), null, p, r, r);
        }
        foreach (var (s, p) in lit)
        {
            double r = CoreRadius(s);
            dc.DrawEllipse(Frozen(Lighten(s.Color, 0.35 + 0.3 * s.Heat)), new Pen(DotEdge, 1), p, r, r);
            _dots.Add((s, p, r));
        }
        // Home town and your own base: a gold ring.
        foreach (var (s, p) in at.Where(t => t.Spot.IsHome || t.Spot.IsMine))
        {
            double r = CoreRadius(s) + 4;
            dc.DrawEllipse(null, new Pen(Gold, 1.8), p, r, r);
        }

        // Labels. The obstacles: every dot, the region names, and labels already placed.
        var taken = _dots.Select(d => Inflate(new Rect(d.At.X - d.Core, d.At.Y - d.Core, 2 * d.Core, 2 * d.Core), 1)).ToList();
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var face = new Typeface(TextElement.GetFontFamily(this), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        var plain = new Typeface(TextElement.GetFontFamily(this), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var bounds = new Rect(_laidOut);

        if (IsWorld && RegionLabels is { } regions)
            foreach (var r in regions)
            {
                var name = Spaced(r.Name.ToUpperInvariant());
                // (a fame layer's zero is said by the quiet land and grey dots, not printed: "ASIA 0" reads as a verdict)
                string num = r.Value > 0 ? r.Value.ToString(CultureInfo.InvariantCulture) : "";
                var ft = new FormattedText(num.Length > 0 ? $"{name}   {num}" : name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, 10.5, RegionInk, dip);
                if (num.Length > 0) ft.SetForegroundBrush(Frozen(Lighten(r.Ink, 0.25)), name.Length + 3, num.Length);
                var c = _m.Transform(WorldShapes.ProjectGeo(r.Lat, r.Lon));
                var origin = new Point(c.X - ft.Width / 2, c.Y - ft.Height / 2);
                var rect = new Rect(origin, new Size(ft.Width, ft.Height));
                DrawHaloText(dc, ft, origin);
                taken.Add(Inflate(rect, 3));
                _regionHits.Add((Inflate(rect, 4), r));
            }

        var wanted = at.Where(t => IsWorld ? t.Spot.AlwaysLabel : bounds.Contains(t.At))
            .OrderByDescending(t => t.Spot.IsHome).ThenByDescending(t => t.Spot.IsMine)
            .ThenByDescending(t => t.Spot.LabelRank).ThenBy(t => t.Spot.CityId);
        foreach (var (s, p) in wanted)
        {
            double size = IsWorld ? 11.5 : 12.5;
            string value = s.Lit ? s.Value.ToString(CultureInfo.InvariantCulture) : "";
            var ft = new FormattedText(value.Length > 0 ? $"{s.Name} {value}" : s.Name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                face, size, s.Lit || s.IsHome ? LabelInk : LabelQuiet, dip);
            if (value.Length > 0)
            {
                ft.SetForegroundBrush(Frozen(Lighten(s.Color, 0.45)), s.Name.Length + 1, value.Length);
                ft.SetFontTypeface(plain, s.Name.Length + 1, value.Length);
            }
            if (s.IsHome) ft.SetForegroundBrush(Gold, 0, s.Name.Length);
            double core = CoreRadius(s) + (s.IsHome || s.IsMine ? 4 : 0);
            if (Place(p, core, ft.Width, ft.Height, taken, bounds) is { } spot)
            {
                DrawHaloText(dc, ft, spot.Location);
                taken.Add(Inflate(spot, 2));
                _labelHits.Add((Inflate(spot, 2), s));
            }
        }
    }

    /// <summary>The first free spot around a dot for a label (right, left, above, below, the corners, then a step further out).</summary>
    private static Rect? Place(Point p, double r, double w, double h, List<Rect> taken, Rect bounds)
    {
        const double g = 3;
        for (int ring = 0; ring < 2; ring++)
        {
            double far = ring * 10;
            var tries = new[]
            {
                new Point(p.X + r + g + far, p.Y - h / 2),
                new Point(p.X - r - g - w - far, p.Y - h / 2),
                new Point(p.X - w / 2, p.Y - r - g - h - far * 0.6),
                new Point(p.X - w / 2, p.Y + r + g + far * 0.6),
                new Point(p.X + r + far, p.Y - r - h + 2 - far * 0.5),
                new Point(p.X + r + far, p.Y + r - 2 + far * 0.5),
                new Point(p.X - r - w - far, p.Y - r - h + 2 - far * 0.5),
                new Point(p.X - r - w - far, p.Y + r - 2 + far * 0.5),
            };
            foreach (var o in tries)
            {
                var rect = new Rect(o, new Size(w, h));
                if (rect.Left < bounds.Left + 2 || rect.Top < bounds.Top + 2 || rect.Right > bounds.Right - 2 || rect.Bottom > bounds.Bottom - 2) continue;
                if (taken.Any(t => t.IntersectsWith(rect))) continue;
                return rect;
            }
        }
        return null;
    }

    private static void DrawHaloText(DrawingContext dc, FormattedText ft, Point origin)
    {
        var geo = ft.BuildGeometry(origin);
        dc.DrawGeometry(null, new Pen(Halo, 3.2) { LineJoin = PenLineJoin.Round }, geo);
        dc.DrawText(ft, origin);
    }

    // --- Hover ----------------------------------------------------------------------------------

    private void HoverChanged()
    {
        if (_laidOut.Width < 20) return;
        DrawOverlay();
        InvalidateArrange();
    }

    private void DrawOverlay()
    {
        using (var lc = _landHover.RenderOpen())
            if (HoverSpot is null && HoverCountry is { } code && WorldShapes.ByCode(code) is { } shape)
            {
                lc.PushTransform(new MatrixTransform(_m));
                if (IsWorld) lc.PushClip(WorldShapes.Outline);
                lc.DrawGeometry(HoverLand, new Pen(Frozen(Color.FromArgb(0x90, 0xB8, 0xC1, 0xCB)), 1.1 / Scale) { LineJoin = PenLineJoin.Round }, shape.Geometry);
                if (IsWorld) lc.Pop();
                lc.Pop();
            }
        using var dc = _overlay.RenderOpen();
        if (HoverSpot is { } s)
        {
            var p = ScreenOf(s);
            double r = CoreRadius(s) + (s.IsHome || s.IsMine ? 8 : 5);
            dc.DrawEllipse(null, new Pen(LabelInk, 1.6), p, r, r);
        }
    }

    private object? HitAt(Point p)
    {
        MapSpot? best = null;
        double bestD = double.MaxValue;
        foreach (var (s, at, core) in _dots)
        {
            double d = (at - p).Length;
            if (d <= Math.Max(9, core + 5) && d < bestD) { best = s; bestD = d; }
        }
        if (best is not null) return best;
        foreach (var (rect, s) in _labelHits) if (rect.Contains(p)) return s;
        foreach (var (rect, r) in _regionHits) if (rect.Contains(p)) return r;
        if (!_m.HasInverse) return null;
        var inv = _m; inv.Invert();
        var q = inv.Transform(p);
        if (IsWorld && !WorldShapes.Outline.FillContains(q)) return null;
        foreach (var shape in WorldShapes.Land)
            if (shape.Bounds.Contains(q) && shape.Geometry.FillContains(q)) return shape;
        return null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _mouse = e.GetPosition(this);
        var hit = HitAt(_mouse.Value);
        var spot = hit as MapSpot;
        var country = hit is LandShape l ? l.Code : null;
        if (!ReferenceEquals(spot, HoverSpot)) SetCurrentValue(HoverSpotProperty, spot);
        if (country != HoverCountry) SetCurrentValue(HoverCountryProperty, country);
        Cursor = hit is null ? null : Cursors.Hand;
        if (country is not null) InvalidateArrange();      // the card follows the pointer across a country
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _mouse = null;
        _pressed = false;
        SetCurrentValue(HoverSpotProperty, null);
        SetCurrentValue(HoverCountryProperty, null);
        Cursor = null;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        _pressed = true;
        _pressTarget = HitAt(e.GetPosition(this));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_pressed) return;
        _pressed = false;
        var hit = HitAt(e.GetPosition(this));
        // A click is a press and a release on the same thing (a drag off a city does nothing).
        if (hit is null || !Equals(hit, _pressTarget)) return;
        switch (hit)
        {
            case MapSpot s when SpotCommand?.CanExecute(s) == true: SpotCommand.Execute(s); break;
            case MapRegionLabel r when RegionCommand?.CanExecute(r.Region) == true: RegionCommand.Execute(r.Region); break;
            case LandShape l when CountryCommand?.CanExecute(l.Code) == true: CountryCommand.Execute(l.Code); break;
        }
        e.Handled = true;
    }

    // --- Paint helpers --------------------------------------------------------------------------

    private static readonly Dictionary<(Color, int), Brush> GlowCache = new();

    /// <summary>A soft radial glow: bright at the heart, gone at the edge. Hotter places glow denser.</summary>
    private static Brush GlowBrush(Color c, double heat)
    {
        int h = (int)Math.Round(heat * 10);
        if (GlowCache.TryGetValue((c, h), out var hit)) return hit;
        double a = 0.72 + 0.27 * heat;
        var heart = Lighten(c, 0.12);
        var b = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(WithAlpha(heart, a), 0),
                new GradientStop(WithAlpha(c, a * 0.6), 0.3),
                new GradientStop(WithAlpha(c, a * 0.2), 0.65),
                new GradientStop(WithAlpha(c, 0), 1),
            },
        };
        b.Freeze();
        return GlowCache[(c, h)] = b;
    }

    private static Color WithAlpha(Color c, double a) => Color.FromArgb((byte)Math.Round(Math.Clamp(a, 0, 1) * 255), c.R, c.G, c.B);

    private static Color Lighten(Color c, double t) => Color.FromRgb(
        (byte)Math.Round(c.R + (255 - c.R) * t), (byte)Math.Round(c.G + (255 - c.G) * t), (byte)Math.Round(c.B + (255 - c.B) * t));

    private static Rect Inflate(Rect r, double by) { r.Inflate(by, by); return r; }

    private static string Spaced(string s) => string.Join(" ", s.ToCharArray());

    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
}
