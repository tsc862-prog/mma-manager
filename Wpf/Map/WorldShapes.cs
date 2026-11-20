using System.IO;
using System.Windows;
using System.Windows.Media;
using MmaManager.Core.Model;

namespace MmaManager.Wpf.Map;

/// <summary>One country's land, projected and frozen (built once per run).</summary>
public sealed class LandShape
{
    public required string Code { get; init; }          // Natural Earth ADM0_A3
    public required string Name { get; init; }
    public required Region Region { get; init; }        // Natural Earth continent mapped to the game's regions
    public required StreamGeometry Geometry { get; init; }
    public required Rect Bounds { get; init; }
}

/// <summary>
/// The drawn world: country outlines from Natural Earth 1:110m Admin 0 - Countries
/// (naturalearthdata.com; public domain, see the header of world-countries-110m.txt),
/// put through a Robinson projection and frozen once. Projected space: x right, y DOWN,
/// one unit = the earth's radius. The map is centred on 10°E so the seam runs through the
/// Pacific at 170°W: Samoa and Tonga sit beside Fiji, east of New Zealand, as they do on
/// a Pacific chart. Antarctica is left out; the frame runs from 84°N to 58°S.
/// Nothing here is game data: it is a picture of the real world under the fictional one.
/// </summary>
public static class WorldShapes
{
    public const double CentralMeridian = 10;
    public const double North = 84, South = -58;
    /// <summary>Display longitudes run [West, West+360): anything west of the seam wraps to the far east.</summary>
    public const double West = CentralMeridian - 180;

    private static readonly Lazy<IReadOnlyList<LandShape>> _land = new(Load);
    private static readonly Lazy<Dictionary<string, LandShape>> _byCode = new(() => _land.Value.ToDictionary(l => l.Code));
    private static readonly Lazy<StreamGeometry> _outline = new(BuildOutline);
    private static readonly Lazy<StreamGeometry> _graticule = new(BuildGraticule);

    public static IReadOnlyList<LandShape> Land => _land.Value;
    public static LandShape? ByCode(string code) => _byCode.Value.GetValueOrDefault(code);
    /// <summary>The world's edge (the Robinson lens), cropped to the frame.</summary>
    public static StreamGeometry Outline => _outline.Value;
    /// <summary>Faint lines every 30 degrees.</summary>
    public static StreamGeometry Graticule => _graticule.Value;

    // --- Atlas code (the game's) -> Natural Earth ADM0_A3 --------------------------------------
    // Explicit, never guessed: the atlas uses sporting codes (GER, NED, RSA) and two of them mean a
    // different country in Natural Earth (atlas BRN is Bahrain; NE BRN is Brunei). Countries too small
    // for the 1:110m set (Singapore, Bahrain, Samoa, Tonga) map to null: their cities still show.
    public static readonly IReadOnlyDictionary<string, string?> AtlasToLand = new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["USA"] = "USA", ["CAN"] = "CAN", ["MEX"] = "MEX", ["CUB"] = "CUB", ["JAM"] = "JAM", ["DOM"] = "DOM",
        ["PUR"] = "PRI", ["CRC"] = "CRI", ["PAN"] = "PAN", ["GUA"] = "GTM", ["HON"] = "HND", ["HAI"] = "HTI",
        ["BRA"] = "BRA", ["ARG"] = "ARG", ["CHI"] = "CHL", ["PER"] = "PER", ["COL"] = "COL", ["ECU"] = "ECU",
        ["VEN"] = "VEN", ["URU"] = "URY", ["PAR"] = "PRY", ["BOL"] = "BOL",
        ["GBR"] = "GBR", ["IRL"] = "IRL", ["FRA"] = "FRA", ["GER"] = "DEU", ["NED"] = "NLD", ["BEL"] = "BEL",
        ["ESP"] = "ESP", ["POR"] = "PRT", ["ITA"] = "ITA", ["SWE"] = "SWE", ["NOR"] = "NOR", ["DEN"] = "DNK",
        ["FIN"] = "FIN", ["ISL"] = "ISL", ["POL"] = "POL", ["CZE"] = "CZE", ["SVK"] = "SVK", ["AUT"] = "AUT",
        ["SUI"] = "CHE", ["HUN"] = "HUN", ["ROU"] = "ROU", ["BUL"] = "BGR", ["SRB"] = "SRB", ["CRO"] = "HRV",
        ["BIH"] = "BIH", ["ALB"] = "ALB", ["MKD"] = "MKD", ["GRE"] = "GRC", ["UKR"] = "UKR", ["BLR"] = "BLR",
        ["LTU"] = "LTU", ["LAT"] = "LVA", ["EST"] = "EST", ["MDA"] = "MDA", ["RUS"] = "RUS", ["GEO"] = "GEO",
        ["ARM"] = "ARM", ["AZE"] = "AZE",
        ["JPN"] = "JPN", ["KOR"] = "KOR", ["CHN"] = "CHN", ["TPE"] = "TWN", ["MGL"] = "MNG", ["THA"] = "THA",
        ["VIE"] = "VNM", ["PHI"] = "PHL", ["INA"] = "IDN", ["MAS"] = "MYS", ["SGP"] = null, ["IND"] = "IND",
        ["PAK"] = "PAK", ["BAN"] = "BGD", ["SRI"] = "LKA", ["NEP"] = "NPL", ["KAZ"] = "KAZ", ["KGZ"] = "KGZ",
        ["UZB"] = "UZB", ["TJK"] = "TJK", ["TKM"] = "TKM", ["MYA"] = "MMR", ["CAM"] = "KHM", ["LAO"] = "LAO",
        ["AUS"] = "AUS", ["NZL"] = "NZL", ["FIJ"] = "FJI", ["SAM"] = null, ["TGA"] = null, ["PNG"] = "PNG",
        ["RSA"] = "ZAF", ["NGA"] = "NGA", ["GHA"] = "GHA", ["SEN"] = "SEN", ["CMR"] = "CMR", ["CIV"] = "CIV",
        ["MLI"] = "MLI", ["COD"] = "COD", ["CGO"] = "COG", ["ANG"] = "AGO", ["KEN"] = "KEN", ["UGA"] = "UGA",
        ["TAN"] = "TZA", ["ETH"] = "ETH", ["ZIM"] = "ZWE", ["ZAM"] = "ZMB", ["BOT"] = "BWA", ["NAM"] = "NAM",
        ["MOZ"] = "MOZ", ["MAR"] = "MAR", ["ALG"] = "DZA", ["TUN"] = "TUN", ["EGY"] = "EGY",
        ["UAE"] = "ARE", ["KSA"] = "SAU", ["QAT"] = "QAT", ["BRN"] = null, ["KUW"] = "KWT", ["OMA"] = "OMN",
        ["JOR"] = "JOR", ["LIB"] = "LBN", ["IRQ"] = "IRQ", ["IRI"] = "IRN", ["AFG"] = "AFG", ["ISR"] = "ISR",
        ["TUR"] = "TUR", ["YEM"] = "YEM",
    };

    /// <summary>Natural Earth code -> atlas code (only the atlas's countries).</summary>
    public static readonly IReadOnlyDictionary<string, string> LandToAtlas =
        AtlasToLand.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Value!, kv => kv.Key, StringComparer.Ordinal);

    // --- Projection -----------------------------------------------------------------------------

    // Robinson's table (every 5 degrees of latitude): parallel length and distance from the equator.
    private static readonly double[] RX =
        { 1.0000, 0.9986, 0.9954, 0.9900, 0.9822, 0.9730, 0.9600, 0.9427, 0.9216, 0.8962, 0.8679, 0.8350, 0.7986, 0.7597, 0.7186, 0.6732, 0.6213, 0.5722, 0.5322 };
    private static readonly double[] RY =
        { 0.0000, 0.0620, 0.1240, 0.1860, 0.2480, 0.3100, 0.3720, 0.4340, 0.4958, 0.5571, 0.6176, 0.6769, 0.7346, 0.7903, 0.8435, 0.8936, 0.9394, 0.9761, 1.0000 };

    /// <summary>A longitude moved into the display range [West, West+360).</summary>
    public static double DisplayLon(double lon)
    {
        double d = (lon - West) % 360;
        if (d < 0) d += 360;
        return West + d;
    }

    /// <summary>Robinson, with <paramref name="displayLon"/> already in display range (or deliberately past it).</summary>
    public static Point Project(double lat, double displayLon)
    {
        double a = Math.Min(Math.Abs(lat), 90) / 5.0;
        int i = Math.Min((int)a, RX.Length - 2);
        double u = a - i;
        double px = RX[i] + (RX[i + 1] - RX[i]) * u;
        double py = RY[i] + (RY[i + 1] - RY[i]) * u;
        double x = 0.8487 * px * (displayLon - CentralMeridian) * Math.PI / 180;
        double y = 1.3523 * py * Math.Sign(lat);
        return new Point(x, -y);
    }

    /// <summary>A real-world point (any longitude) on the map.</summary>
    public static Point ProjectGeo(double lat, double lon) => Project(lat, DisplayLon(lon));

    /// <summary>The whole frame in projected units.</summary>
    public static Rect WorldRect
    {
        get
        {
            var tl = Project(North, West);
            var tr = Project(0, West + 360);
            var bl = Project(South, West);
            return new Rect(new Point(Project(0, West).X, tl.Y), new Point(tr.X, bl.Y));
        }
    }

    /// <summary>The projected bounds of a lat/lon box in display longitudes (edges sampled: Robinson bends them).</summary>
    public static Rect BoxRect(double west, double east, double south, double north)
    {
        var r = Rect.Empty;
        for (int k = 0; k <= 8; k++)
        {
            double lat = south + (north - south) * k / 8, lon = west + (east - west) * k / 8;
            r.Union(Project(lat, west)); r.Union(Project(lat, east));
            r.Union(Project(south, lon)); r.Union(Project(north, lon));
        }
        return r;
    }

    // --- Loading --------------------------------------------------------------------------------

    private static IReadOnlyList<LandShape> Load()
    {
        var list = new List<LandShape>();
        using var s = typeof(WorldShapes).Assembly.GetManifestResourceStream("MmaManager.Wpf.Map.world-countries-110m.txt");
        if (s is null) return list;      // no land: the map still draws its cities on the sea
        using var reader = new StreamReader(s);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var parts = line.Split(';');
            if (parts.Length < 4) continue;
            var geo = new StreamGeometry { FillRule = FillRule.Nonzero };
            using (var ctx = geo.Open())
                foreach (var ring in parts[3].Split('|', StringSplitOptions.RemoveEmptyEntries))
                    AddRing(ctx, ring);
            geo.Freeze();
            list.Add(new LandShape
            {
                Code = parts[0], Region = RegionOf(parts[1]), Name = parts[2],
                Geometry = geo, Bounds = geo.Bounds,
            });
        }
        return list;
    }

    private static void AddRing(StreamGeometryContext ctx, string ring)
    {
        var pts = ring.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lons = new double[pts.Length];
        var lats = new double[pts.Length];
        for (int i = 0; i < pts.Length; i++)
        {
            int c = pts[i].IndexOf(',');
            lons[i] = int.Parse(pts[i].AsSpan(0, c)) / 10.0;
            lats[i] = int.Parse(pts[i].AsSpan(c + 1)) / 10.0;
        }
        // Keep the ring in one piece: unwrap across the seam, then shift the whole ring so its middle is on the map.
        double prev = DisplayLon(lons[0]);
        lons[0] = prev;
        for (int i = 1; i < lons.Length; i++)
        {
            double d = DisplayLon(lons[i]);
            while (d - prev > 180) d -= 360;
            while (prev - d > 180) d += 360;
            lons[i] = prev = d;
        }
        double mid = (lons.Min() + lons.Max()) / 2;
        double shift = DisplayLon(mid) - mid;
        var first = Project(lats[0], lons[0] + shift);
        ctx.BeginFigure(first, isFilled: true, isClosed: true);
        var rest = new List<Point>(lons.Length);
        for (int i = 1; i < lons.Length; i++) rest.Add(Project(lats[i], lons[i] + shift));
        ctx.PolyLineTo(rest, isStroked: true, isSmoothJoin: true);
    }

    private static Region RegionOf(string code) => code switch
    {
        "NA" => Region.NorthAmerica,
        "SA" => Region.SouthAmerica,
        "EU" => Region.Europe,
        "AF" => Region.Africa,
        "OC" => Region.Oceania,
        "ME" => Region.MiddleEast,
        _ => Region.Asia,
    };

    private static StreamGeometry BuildOutline()
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(Project(North, West), true, true);
            var pts = new List<Point>();
            for (double lat = North; lat >= South; lat -= 1) pts.Add(Project(lat, West));
            for (double lon = West; lon <= West + 360; lon += 2) pts.Add(Project(South, lon));
            for (double lat = South; lat <= North; lat += 1) pts.Add(Project(lat, West + 360));
            for (double lon = West + 360; lon >= West; lon -= 2) pts.Add(Project(North, lon));
            ctx.PolyLineTo(pts, true, true);
        }
        g.Freeze();
        return g;
    }

    private static StreamGeometry BuildGraticule()
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            for (int lat = -30; lat <= 60; lat += 30)
            {
                ctx.BeginFigure(Project(lat, West), false, false);
                ctx.PolyLineTo(Enumerable.Range(1, 180).Select(k => Project(lat, West + 2 * k)).ToList(), true, true);
            }
            for (double lon = -150; lon <= 180; lon += 30)
            {
                double d = DisplayLon(lon);
                ctx.BeginFigure(Project(North, d), false, false);
                ctx.PolyLineTo(Enumerable.Range(1, 142).Select(k => Project(North - k, d)).ToList(), true, true);
            }
        }
        g.Freeze();
        return g;
    }
}
