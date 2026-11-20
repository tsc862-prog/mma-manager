using System.Windows.Media;
using MmaManager.Core.Model;

namespace MmaManager.Wpf.Map;

/// <summary>One city on the drawn map: where it is, and how hot it glows on the active layer.</summary>
public sealed class MapSpot
{
    public required int CityId { get; init; }
    public required string Name { get; init; }
    public required string CountryName { get; init; }
    public required CountryId Country { get; init; }
    public required Region Region { get; init; }
    public required double Lat { get; init; }
    public required double Lon { get; init; }
    /// <summary>The layer's number here (0-100).</summary>
    public required int Value { get; init; }
    /// <summary>0..1: how big and bright the glow is.</summary>
    public required double Heat { get; init; }
    public required Color Color { get; init; }
    /// <summary>False: a quiet grey dot (nobody here knows the name), no glow.</summary>
    public required bool Lit { get; init; }
    /// <summary>The subject's home town (fighter) or home city (promotion).</summary>
    public bool IsHome { get; init; }
    /// <summary>The player's promotion is based here (shown on the interest layer).</summary>
    public bool IsMine { get; init; }
    /// <summary>Labelled on the whole-world view (the hot ones, home, your base); the rest show on hover.</summary>
    public bool AlwaysLabel { get; init; }
    /// <summary>The label's order of precedence when labels compete for room (higher first).</summary>
    public double LabelRank { get; init; }
    // Hover card text.
    public string ValueWord { get; init; } = "";
    public string ValueLine { get; init; } = "";
    public IReadOnlyList<string> Orgs { get; init; } = Array.Empty<string>();
    public bool HasMyOrg { get; init; }
}

/// <summary>A region's name across its land on the whole-world view (click it to zoom in).</summary>
public sealed class MapRegionLabel
{
    public required Region Region { get; init; }
    public required string Name { get; init; }
    public required int Value { get; init; }
    public required Color Ink { get; init; }
    public required double Lat { get; init; }
    public required double Lon { get; init; }
}

/// <summary>A lat/lon box the map zooms to (display longitudes, see <see cref="WorldShapes.DisplayLon"/>).</summary>
public sealed record MapFocus(double West, double East, double South, double North);
