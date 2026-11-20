using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Geography;
using MmaManager.Core.Sim.Org;
using MmaManager.Wpf.Art;
using MmaManager.Wpf.Map;
using MmaManager.Wpf.Mvvm;

namespace MmaManager.Wpf.ViewModels;

/// <summary>One promotion listed on a place card.</summary>
public sealed class MapOrgRow
{
    public MapOrgRow(Promotion p, World w)
    {
        Abbrev = p.Abbreviation;
        Name = p.Name;
        Tier = OrgSizing.Label(OrgSizing.SizeOf(p));
        Prestige = p.Prestige.Value;
        IsPlayer = p.Id == w.PlayerPromotion;
    }

    public string Abbrev { get; }
    public string Name { get; }
    public string Tier { get; }
    public int Prestige { get; }
    public bool IsPlayer { get; }
    public string Detail => $"{Tier} · prestige {Prestige}";
}

/// <summary>The three tiers you can drill through on the world overview.</summary>
public enum PlaceLevel { World, Region, Country }

/// <summary>
/// What the map is coloured by: the sport's appetite (MMA interest), how well the player's own
/// promotion is known, or how well one fighter is known. Fame layers read the sim's
/// <see cref="Reputation"/> (cities are the truth; regions and countries are its market-weighted
/// roll-up), nothing else.
/// </summary>
public enum MapLayer { Interest, MyPromotion, Fighter }

/// <summary>A place (region / country / city) as a heat-mapped, drillable card.</summary>
public sealed class PlaceCard
{
    public required string Name { get; init; }
    /// <summary>The number in the card's heat strip: MMA interest, or fame on a fame layer (0-100).</summary>
    public required int Interest { get; init; }
    public required Brush HeatBrush { get; init; }
    public required Brush TextBrush { get; init; }
    public double HeatBarWidth => Interest;          // 0–100 maps straight to a 0–100px bar
    public string OrgSummary { get; init; } = "";
    public string CitiesLine { get; init; } = "";
    public string CitiesHeader { get; init; } = "HOTTEST CITIES";
    public bool IsPlayerHere { get; init; }
    public bool Drillable { get; init; }
    public string Chevron => Drillable ? "›" : "";
    public ObservableCollection<MapOrgRow> Orgs { get; } = new();
    /// <summary>The fame layer's subject calls this place home (his home town is in it).</summary>
    public bool IsHome { get; init; }
    /// <summary>What the strip's number means on hover.</summary>
    public string ValueTip { get; init; } = "";

    // Drill payload — what this card opens into.
    public PlaceLevel DrillTo { get; init; }
    public Region Region { get; init; }
    public CountryId? Country { get; init; }
}

/// <summary>One hop in the breadcrumb trail back up the hierarchy.</summary>
public sealed class Crumb
{
    public required string Label { get; init; }
    public required PlaceLevel Level { get; init; }
    public Region Region { get; init; }
    public CountryId? Country { get; init; }
    public bool IsLast { get; set; }
}

/// <summary>A city where the subject's name carries, as a clickable chip (opens its country).</summary>
public sealed class HotSpot
{
    public required string City { get; init; }
    public required string Where { get; init; }
    public required int Fame { get; init; }
    public required Brush HeatBrush { get; init; }
    public required Brush InkBrush { get; init; }
    public string Word => ViewModels.Fame.PlaceWord(Fame);
    public bool IsHome { get; init; }
    public Region Region { get; init; }
    public CountryId Country { get; init; }
}

/// <summary>A stop on the fame legend.</summary>
public sealed class LegendStop
{
    public required string Word { get; init; }
    public required Brush Brush { get; init; }
}

/// <summary>
/// The world at a glance, drill-down style: start on the seven continental regions, click into
/// a region to see its countries, then a country to see its individual fight cities. Each card
/// colour-keys the chosen layer: MMA interest, where the player's promotion is known, or where one
/// fighter is known (a heat map of fame, his face and home town on it). A breadcrumb walks back up.
/// Holds ids only (the subject fighter, the country drilled into) so a refresh rebuilds in place.
/// </summary>
public sealed class WorldMapViewModel : ObservableObject
{
    private static readonly Region[] Layout =
    {
        Region.NorthAmerica, Region.Europe, Region.Asia, Region.MiddleEast,
        Region.SouthAmerica, Region.Africa, Region.Oceania,
    };

    private World? _world;
    private PlaceLevel _level = PlaceLevel.World;
    private Region _region;
    private CountryId? _country;
    private MapLayer _layer = MapLayer.Interest;
    private int? _fighterId;

    public ObservableCollection<PlaceCard> Places { get; } = new();
    public ObservableCollection<Crumb> Breadcrumb { get; } = new();
    public ObservableCollection<HotSpot> HotSpots { get; } = new();

    /// <summary>The fame legend, cold to hot (drawn with the same ramp the cards use).</summary>
    public IReadOnlyList<LegendStop> FameLegend { get; } = ViewModels.Fame.LegendStops
        .Select(s => new LegendStop { Word = s.Word, Brush = ViewModels.Fame.Heat(s.Fame) }).ToList();

    public ICommand DrillCommand { get; }
    public ICommand NavigateCommand { get; }
    public ICommand HotSpotCommand { get; }
    public ICommand BackToInterestCommand { get; }
    public ICommand OpenSubjectCommand { get; }

    private string _summary = "";
    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    private string _levelHint = "";
    public string LevelHint { get => _levelHint; private set => Set(ref _levelHint, value); }

    public WorldMapViewModel()
    {
        DrillCommand = new RelayCommand<PlaceCard>(Drill);
        NavigateCommand = new RelayCommand<Crumb>(NavigateTo);
        HotSpotCommand = new RelayCommand<HotSpot>(GoToSpot);
        BackToInterestCommand = new RelayCommand(() => SetLayer(MapLayer.Interest));
        OpenSubjectCommand = new RelayCommand(OpenSubject);
        MapSpotCommand = new RelayCommand<MapSpot>(OpenSpot);
        MapCountryCommand = new RelayCommand<string>(OpenLand);
        MapRegionCommand = new RelayCommand<Region>(r => { _level = PlaceLevel.Region; _region = r; Rebuild(); });
    }

    // --- Map or list -------------------------------------------------------------------------

    // The drawn map leads; the place cards are its detail (a rail beside the zoomed map) and the List
    // view. A viewing preference, so it is kept across a new world too.
    private bool _showMap = true;
    public bool ShowMap { get => _showMap; set { if (value && !_showMap) { _showMap = true; Rebuild(); } } }
    public bool ShowList { get => !_showMap; set { if (value && _showMap) { _showMap = false; Rebuild(); } } }
    /// <summary>Map view, drilled in: the region's countries (or the country's cities) as cards beside the map.</summary>
    public bool ShowRail => _showMap && _level != PlaceLevel.World && Places.Count > 0;
    public string RailHeader => _level == PlaceLevel.Country ? "ITS FIGHT CITIES" : "COUNTRIES WITH A FIGHT SCENE";

    public ICommand MapSpotCommand { get; }
    public ICommand MapCountryCommand { get; }
    public ICommand MapRegionCommand { get; }

    public IReadOnlyList<MapSpot> MapSpots { get; private set; } = Array.Empty<MapSpot>();
    public IReadOnlyList<MapRegionLabel> MapRegions { get; private set; } = Array.Empty<MapRegionLabel>();
    public IReadOnlyDictionary<string, Color> MapFills { get; private set; } = new Dictionary<string, Color>();
    public MapFocus? MapFocus { get; private set; }
    public string? MapFocusCountry { get; private set; }

    // --- Hover card (the map writes the hover here; nothing else reads it) --------------------

    private MapSpot? _hoverSpot;
    private string? _hoverCountry;
    public MapSpot? HoverSpot { get => _hoverSpot; set { if (!ReferenceEquals(_hoverSpot, value)) { _hoverSpot = value; HoverRead(); } } }
    public string? HoverCountry { get => _hoverCountry; set { if (_hoverCountry != value) { _hoverCountry = value; HoverRead(); } } }

    public bool HasHover { get; private set; }
    public string HoverTitle { get; private set; } = "";
    public string HoverWhere { get; private set; } = "";
    public string HoverValue { get; private set; } = "";
    public Brush HoverValueBrush { get; private set; } = Brushes.Transparent;
    public string HoverSecond { get; private set; } = "";
    public string HoverOrgs { get; private set; } = "";
    public string HoverHint { get; private set; } = "";
    public bool HoverIsHome { get; private set; }
    public string HoverHomeText { get; private set; } = "";

    private static readonly string[] HoverProps =
    {
        nameof(HasHover), nameof(HoverTitle), nameof(HoverWhere), nameof(HoverValue), nameof(HoverValueBrush),
        nameof(HoverSecond), nameof(HoverOrgs), nameof(HoverHint), nameof(HoverIsHome), nameof(HoverHomeText),
        nameof(HoverSpot), nameof(HoverCountry),
    };

    // --- Layers ------------------------------------------------------------------------------

    public MapLayer Layer => _layer;
    public bool IsInterestLayer { get => _layer == MapLayer.Interest; set { if (value) SetLayer(MapLayer.Interest); } }
    public bool IsPromotionLayer { get => _layer == MapLayer.MyPromotion; set { if (value) SetLayer(MapLayer.MyPromotion); } }
    public bool IsFighterLayer { get => _layer == MapLayer.Fighter; set { if (value) SetLayer(MapLayer.Fighter); } }
    public bool IsFameLayer => _layer != MapLayer.Interest;

    /// <summary>The player runs a house (not watching from ringside): "where am I known" makes sense.</summary>
    public bool CanShowPromotion => PlayerPromo is not null;
    /// <summary>A fighter has been put on the map (from a profile): his layer is offered.</summary>
    public bool CanShowFighter => SubjectFighter is not null;
    /// <summary>The fighter segment's label ("Kvaratskhelia").</summary>
    public string FighterLayerLabel => SubjectFighter is { } f ? f.LastName : "A fighter";

    /// <summary>Put the map into fame mode for one fighter (from his profile), at the world view.</summary>
    public void ShowFighter(int fighterId)
    {
        _fighterId = fighterId;
        _level = PlaceLevel.World;
        _country = null;
        SetLayer(MapLayer.Fighter, force: true);
    }

    /// <summary>Put the map into "where my promotion is known" mode, at the world view.</summary>
    public void ShowPromotion()
    {
        _level = PlaceLevel.World;
        _country = null;
        SetLayer(MapLayer.MyPromotion, force: true);
    }

    private void SetLayer(MapLayer layer, bool force = false)
    {
        if (layer == MapLayer.Fighter && SubjectFighter is null) layer = MapLayer.Interest;
        if (layer == MapLayer.MyPromotion && PlayerPromo is null) layer = MapLayer.Interest;
        if (layer == _layer && !force) return;
        _layer = layer;
        Rebuild();
    }

    private Fighter? SubjectFighter => _world is not null && _fighterId is { } id ? _world.Get(new FighterId(id)) : null;
    private Promotion? PlayerPromo => _world?.Get(_world.PlayerPromotion) is { Defunct: false } p ? p : null;

    // --- The subject banner (fame layers) ----------------------------------------------------

    public string SubjectKicker { get; private set; } = "";
    public string SubjectName { get; private set; } = "";
    public ImageSource? SubjectPicture { get; private set; }
    public bool SubjectPictureIsLogo { get; private set; }
    /// <summary>Where the name carries, or (known nowhere) a human line on why not yet.</summary>
    public string SubjectLine { get; private set; } = "";
    public bool SubjectIsNobody { get; private set; }
    public string SubjectHome { get; private set; } = "";
    public bool HasSubjectHome => SubjectHome.Length > 0;
    /// <summary>The fighter's popularity for his draw meter (fighter layer only).</summary>
    public int SubjectDraw { get; private set; }
    public bool ShowSubjectDraw => _layer == MapLayer.Fighter;
    public string OpenSubjectText => _layer == MapLayer.Fighter ? "Open profile ›" : "Open HQ ›";
    public bool HasHotSpots => HotSpots.Count > 0;
    public string HotSpotsHeader { get; private set; } = "";

    private void OpenSubject()
    {
        if (_layer == MapLayer.Fighter && _fighterId is { } id) AppCommands.OpenFighter.Execute(id);
        else if (_layer == MapLayer.MyPromotion && PlayerPromo is { } p) AppCommands.OpenOrg.Execute(p.Id.Value);
    }

    // --- Lifecycle -----------------------------------------------------------------------------

    public void Attach(World world)
    {
        bool fresh = !ReferenceEquals(world, _world);
        _world = world;
        if (fresh)
        {
            // A new world (load, new game): back to the plain map at the top.
            _level = PlaceLevel.World;
            _region = default;
            _country = null;
            _fighterId = null;
            _layer = MapLayer.Interest;
        }
        // A refresh keeps the layer, the subject (by id) and where the player had drilled to.
        if (_layer == MapLayer.Fighter && SubjectFighter is null) _layer = MapLayer.Interest;
        if (_layer == MapLayer.MyPromotion && PlayerPromo is null) _layer = MapLayer.Interest;
        if (_level == PlaceLevel.Country && (_country is not { } cid || world.Get(cid) is null)) _level = PlaceLevel.World;
        Rebuild();
    }

    private void Drill(PlaceCard? card)
    {
        if (card is null || !card.Drillable || _world is null) return;
        if (card.DrillTo == PlaceLevel.Region) { _level = PlaceLevel.Region; _region = card.Region; }
        else if (card.DrillTo == PlaceLevel.Country) { _level = PlaceLevel.Country; _country = card.Country; }
        Rebuild();
    }

    private void NavigateTo(Crumb? crumb)
    {
        if (crumb is null || _world is null) return;
        _level = crumb.Level;
        if (crumb.Level == PlaceLevel.Region) _region = crumb.Region;
        Rebuild();
    }

    private void GoToSpot(HotSpot? spot)
    {
        if (spot is null || _world is null) return;
        _level = PlaceLevel.Country;
        _region = spot.Region;
        _country = spot.Country;
        Rebuild();
    }

    private void Rebuild()
    {
        if (_world is null) return;
        Places.Clear();
        BuildSubject(_world);
        switch (_level)
        {
            case PlaceLevel.World: BuildRegions(_world); break;
            case PlaceLevel.Region: BuildCountries(_world, _region); break;
            case PlaceLevel.Country: BuildCities(_world, _country!.Value); break;
        }
        BuildBreadcrumb(_world);
        BuildMap(_world);
        _hoverSpot = null;      // the spots were rebuilt: an old hover points at nothing
        _hoverCountry = null;
        HoverClear();
        if (_showMap) LevelHint = _level switch
        {
            PlaceLevel.World => "Click a region's name or its land to zoom in · click a city to open its country",
            PlaceLevel.Region => "Click a country or a city to see its cities",
            _ => "",
        };
        // Everything that hangs off the layer (segments, banner, legend, the map) reads again.
        OnPropertyChanged(string.Empty);
    }

    // --- The drawn map ---------------------------------------------------------------------------

    // Each region's zoomed view (display longitudes: Samoa and Tonga sit past 180, beside Fiji).
    private static readonly Dictionary<Region, MapFocus> RegionBoxes = new()
    {
        [Region.NorthAmerica] = new(-128, -56, 10, 58),
        [Region.SouthAmerica] = new(-92, -30, -56, 14),
        [Region.Europe] = new(-14, 48, 35, 66),
        [Region.MiddleEast] = new(24, 72, 10, 45),
        [Region.Asia] = new(64, 150, -10, 50),
        [Region.Africa] = new(-24, 56, -37, 38),
        [Region.Oceania] = new(108, 192, -50, -2),
    };

    // Where each region's name sits on the whole-world view: over open land, clear of its cities.
    private static readonly Dictionary<Region, (double Lat, double Lon)> RegionNameAt = new()
    {
        [Region.NorthAmerica] = (54, -102),
        [Region.SouthAmerica] = (-8, -60),
        [Region.Europe] = (63, 40),
        [Region.MiddleEast] = (33, 54),
        [Region.Asia] = (46, 100),
        [Region.Africa] = (2, 21),
        [Region.Oceania] = (-23, 132),
    };

    private static readonly Color PlainLand = Color.FromRgb(0x22, 0x29, 0x32);
    private static readonly Color DimLand = Color.FromRgb(0x1A, 0x1F, 0x26);

    private void BuildMap(World w)
    {
        var home = HomeCity(w);
        var mine = PlayerPromo;
        var byCity = w.Promotions.Values.Where(p => !p.Defunct && p.HomeCity is not null)
            .GroupBy(p => p.HomeCity!.Value).ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Prestige.Value).ToList());

        // Cities: every one the atlas placed (49 today and in Origins); a city without coordinates is skipped.
        var spots = new List<MapSpot>();
        foreach (var city in w.Cities.Values.OrderBy(c => c.Id.Value))
        {
            if (!CityCoordinates.TryGet(w, city.Id, out var at)) continue;
            int v = InCity(city);
            var orgs = byCity.GetValueOrDefault(city.Id) ?? new List<Promotion>();
            spots.Add(new MapSpot
            {
                CityId = city.Id.Value, Name = city.Name, CountryName = w.Get(city.Country)?.Name ?? "",
                Country = city.Country, Region = city.Region, Lat = at.Lat, Lon = at.Lon,
                Value = v,
                Heat = IsFameLayer ? ViewModels.Fame.HeatT(v) : InterestT(v),
                Color = ((SolidColorBrush)HeatOf(v)).Color,
                Lit = !IsFameLayer || v > 0,
                IsHome = home?.Id == city.Id,
                IsMine = _layer == MapLayer.Interest && mine?.HomeCity == city.Id,
                LabelRank = v + city.Market / 1000.0,
                ValueWord = IsFameLayer ? ViewModels.Fame.PlaceWord(v) : InterestWord(v),
                ValueLine = ValueLineFor(v),
                Orgs = orgs.Select(p => p.Abbreviation).ToList(),
                HasMyOrg = mine is not null && orgs.Any(p => p.Id == mine.Id),
            });
        }
        // Always labelled on the whole world: the hot ones (fame: everywhere with a few fans or more; interest:
        // the dozen most fight-mad), home and your base. The rest name themselves on hover or when zoomed.
        var hot = IsFameLayer
            ? spots.Where(s => s.Value >= 10).OrderByDescending(s => s.Value).Take(14).ToList()
            : spots.OrderByDescending(s => s.Value).ThenBy(s => s.CityId).Take(12).ToList();
        if (IsFameLayer && hot.Count < 4)
            hot = spots.Where(s => s.Value > 0).OrderByDescending(s => s.Value).ThenBy(s => s.CityId).Take(5).ToList();
        var always = hot.Select(s => s.CityId).ToHashSet();
        MapSpots = spots.Select(s => always.Contains(s.CityId) || s.IsHome || s.IsMine ? With(s, alwaysLabel: true) : s).ToList();

        // Region names (whole world only), with the layer's roll-up.
        MapRegions = _level == PlaceLevel.World
            ? Layout.Select(r =>
            {
                int v = InRegion(w, r);
                return new MapRegionLabel
                {
                    Region = r, Name = Pretty(r), Value = v, Ink = ((SolidColorBrush)HeatOf(v)).Color,
                    Lat = RegionNameAt[r].Lat, Lon = RegionNameAt[r].Lon,
                };
            }).ToList()
            : new List<MapRegionLabel>();

        // Land: faint colour from the country roll-up, so there is heat between the dots. Interest: every
        // country the atlas knows; fame: the countries with a fight city (the sim's roll-up is cities only).
        var byCode = w.Countries.Values.GroupBy(c => c.Code).ToDictionary(g => g.Key, g => g.First());
        var fills = new Dictionary<string, Color>();
        foreach (var shape in WorldShapes.Land)
        {
            Country? country = WorldShapes.LandToAtlas.TryGetValue(shape.Code, out var atlas) ? byCode.GetValueOrDefault(atlas) : null;
            var region = country?.Continent ?? shape.Region;
            bool outside = _level != PlaceLevel.World && region != _region;
            Color baseColor = outside ? DimLand : PlainLand;
            if (country is null || outside) { if (outside) fills[shape.Code] = baseColor; continue; }
            int v = InCountry(w, country.Id);
            if (IsFameLayer && v <= 0) continue;
            // A hint, not a fill: the city glows lead. The blend grows with the value raised to 1.5, so a big
            // country at a low value (fame ~20) is barely off plain land, and even the hottest stays well under its glow.
            var heat = ((SolidColorBrush)HeatOf(v)).Color;
            double t = IsFameLayer ? Math.Clamp(v / 100.0, 0, 1) : InterestT(v);
            fills[shape.Code] = Blend(baseColor, heat, (IsFameLayer ? 0.28 : 0.16) * Math.Pow(t, 1.5));
        }
        MapFills = fills;

        MapFocus = _level == PlaceLevel.World ? null : RegionBoxes[_region];
        MapFocusCountry = _level == PlaceLevel.Country && _country is { } cid && w.Get(cid) is { } fc
            ? WorldShapes.AtlasToLand.GetValueOrDefault(fc.Code) : null;
    }

    private static MapSpot With(MapSpot s, bool alwaysLabel) => new()
    {
        CityId = s.CityId, Name = s.Name, CountryName = s.CountryName, Country = s.Country, Region = s.Region,
        Lat = s.Lat, Lon = s.Lon, Value = s.Value, Heat = s.Heat, Color = s.Color, Lit = s.Lit, IsHome = s.IsHome,
        IsMine = s.IsMine, AlwaysLabel = alwaysLabel, LabelRank = s.LabelRank, ValueWord = s.ValueWord,
        ValueLine = s.ValueLine, Orgs = s.Orgs, HasMyOrg = s.HasMyOrg,
    };

    private string ValueLineFor(int v) => _layer switch
    {
        MapLayer.Interest => $"MMA interest {v}  ·  {InterestWord(v)}",
        _ => v > 0 ? $"Fame {v}  ·  {ViewModels.Fame.PlaceWord(v)}" : "Nobody here knows the name yet",
    };

    /// <summary>MMA interest in words, the legend's own ends ("cool" to "fight-mad").</summary>
    private static string InterestWord(int v) => v switch
    {
        >= 75 => "fight-mad",
        >= 60 => "a fight town",
        >= 45 => "warm to it",
        >= 30 => "lukewarm",
        _ => "cool",
    };

    /// <summary>Where interest sits on its ramp (0..1): the same span the card colour uses, so glow and card agree.</summary>
    private static double InterestT(int v) => Math.Clamp((v - 25) / 60.0, 0, 1);

    private static Color Blend(Color a, Color b, double t) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));

    private void OpenSpot(MapSpot? spot)
    {
        if (spot is null || _world is null) return;
        _level = PlaceLevel.Country;
        _region = spot.Region;
        _country = spot.Country;
        Rebuild();
    }

    /// <summary>A click on land: zoom to its region first; once there, a country with a fight city opens its cities.</summary>
    private void OpenLand(string? code)
    {
        if (code is null || _world is null || WorldShapes.ByCode(code) is not { } shape) return;
        var country = WorldShapes.LandToAtlas.TryGetValue(code, out var atlas)
            ? _world.Countries.Values.FirstOrDefault(c => c.Code == atlas) : null;
        var region = country?.Continent ?? shape.Region;
        if (_level == PlaceLevel.World || region != _region)
        {
            _level = PlaceLevel.Region;
            _region = region;
            Rebuild();
            return;
        }
        if (country is not null && _world.Cities.Values.Any(c => c.Country == country.Id))
        {
            _level = PlaceLevel.Country;
            _country = country.Id;
            Rebuild();
        }
    }

    private void HoverClear()
    {
        HasHover = HoverIsHome = false;
        HoverTitle = HoverWhere = HoverValue = HoverSecond = HoverOrgs = HoverHint = HoverHomeText = "";
        HoverValueBrush = Brushes.Transparent;
    }

    /// <summary>The hover card's words for the city or land under the pointer.</summary>
    private void HoverRead()
    {
        HoverClear();
        var w = _world;
        if (w is not null && _hoverSpot is { } s && w.Get(new CityId(s.CityId)) is { } city)
        {
            HasHover = true;
            HoverTitle = s.Name;
            HoverWhere = $"{s.CountryName}  ·  {Pretty(s.Region)}";
            HoverValue = s.ValueLine;
            HoverValueBrush = HeatOf(s.Value);
            HoverSecond = IsFameLayer ? $"MMA interest {city.MmaInterest}  ·  market {city.Market}" : $"Market {city.Market}";
            HoverOrgs = s.Orgs.Count == 0 ? "No promotion based here" : $"Based here: {string.Join(", ", s.Orgs)}";
            HoverIsHome = s.IsHome || s.IsMine;
            HoverHomeText = s.IsMine ? "YOUR BASE" : _layer == MapLayer.Fighter ? "HOME TOWN" : "HOME CITY";
            HoverHint = $"Click to open {s.CountryName}";
        }
        else if (w is not null && _hoverCountry is { } code && WorldShapes.ByCode(code) is { } shape)
        {
            var country = WorldShapes.LandToAtlas.TryGetValue(code, out var atlas) ? w.Countries.Values.FirstOrDefault(c => c.Code == atlas) : null;
            var region = country?.Continent ?? shape.Region;
            int cities = country is null ? 0 : w.Cities.Values.Count(c => c.Country == country.Id);
            HasHover = true;
            HoverTitle = country?.Name ?? shape.Name;
            HoverWhere = Pretty(region);
            if (country is not null && (!IsFameLayer || cities > 0))
            {
                int v = InCountry(w, country.Id);
                HoverValue = ValueLineFor(v);
                HoverValueBrush = HeatOf(v);
            }
            HoverSecond = cities == 0 ? "No fight city on file" : cities == 1 ? "1 fight city" : $"{cities} fight cities";
            if (country is not null)
            {
                var orgs = OrgsWhere(w, p => p.HomeCity is { } hc && w.Get(hc)?.Country == country.Id);
                HoverOrgs = orgs.Count == 0 ? "" : $"Based here: {string.Join(", ", orgs.Take(5).Select(p => p.Abbreviation))}{(orgs.Count > 5 ? $" +{orgs.Count - 5}" : "")}";
            }
            HoverHint = _level == PlaceLevel.World || region != _region ? $"Click to zoom to {Pretty(region)}"
                : cities > 0 ? "Click to see its cities" : "";
        }
        foreach (var p in HoverProps) OnPropertyChanged(p);
    }

    // --- What a layer reads ----------------------------------------------------------------

    private int InRegion(World w, Region r) => _layer switch
    {
        MapLayer.Fighter when SubjectFighter is { } f => Reputation.FighterInRegion(w, f, r),
        MapLayer.MyPromotion when PlayerPromo is { } p => Reputation.OrgInRegion(w, p, r),
        _ => MmaInterest.InRegion(w, r),
    };

    private int InCountry(World w, CountryId c) => _layer switch
    {
        MapLayer.Fighter when SubjectFighter is { } f => Reputation.FighterInCountry(w, f, c),
        MapLayer.MyPromotion when PlayerPromo is { } p => Reputation.OrgInCountry(w, p, c),
        _ => MmaInterest.InCountry(w, c),
    };

    private int InCity(City c) => _layer switch
    {
        MapLayer.Fighter when SubjectFighter is { } f => Reputation.FighterInCity(f, c.Id),
        MapLayer.MyPromotion when PlayerPromo is { } p => Reputation.OrgInCity(p, c.Id),
        _ => c.MmaInterest,
    };

    private Brush HeatOf(int v) => IsFameLayer ? ViewModels.Fame.Heat(v) : Heat(v);
    private Brush InkOf(int v) => IsFameLayer ? ViewModels.Fame.InkOn(v) : TextOn(v);

    /// <summary>The subject's home city (his home town, or the promotion's home city), if any.</summary>
    private City? HomeCity(World w) => _layer switch
    {
        MapLayer.Fighter when SubjectFighter is { HomeCity: { } hc } => w.Get(hc),
        MapLayer.MyPromotion when PlayerPromo is { HomeCity: { } hc } => w.Get(hc),
        _ => null,
    };

    private string ValueTip(string where) => _layer switch
    {
        MapLayer.Fighter => $"How well {SubjectFighter?.FullName} is known in {where} (0-100; a region or country is its cities' average, weighted by market size)",
        MapLayer.MyPromotion => $"How well {PlayerPromo?.Abbreviation} is known in {where} (0-100; a region or country is its cities' average, weighted by market size)",
        _ => $"MMA interest in {where} (0-100): how fight-mad the place is",
    };

    private void BuildSubject(World w)
    {
        HotSpots.Clear();
        SubjectKicker = SubjectName = SubjectLine = SubjectHome = HotSpotsHeader = "";
        SubjectPicture = null;
        SubjectPictureIsLogo = SubjectIsNobody = false;
        SubjectDraw = 0;
        if (!IsFameLayer) return;

        List<(City City, int Fame)> known;
        if (_layer == MapLayer.Fighter && SubjectFighter is { } f)
        {
            known = ViewModels.Fame.KnownCities(w, f);
            SubjectKicker = f.Sex == Sex.Female ? "WHERE SHE'S KNOWN" : "WHERE HE'S KNOWN";
            SubjectName = f.FullName;
            SubjectPicture = Portraits.Thumb(f, w);
            SubjectDraw = f.Intangibles.Popularity.Value;
            SubjectIsNobody = known.Count == 0;
            SubjectLine = SubjectIsNobody ? ViewModels.Fame.NobodyLine(f) : ViewModels.Fame.ReachLine(w, known);
            SubjectHome = HomeCity(w) is { } home ? $"Home town: {ViewModels.Fame.CityLabel(w, home)}" : "";
        }
        else if (PlayerPromo is { } p)
        {
            known = ViewModels.Fame.KnownCities(w, p);
            SubjectKicker = "WHERE YOUR PROMOTION IS KNOWN";
            SubjectName = p.Name;
            SubjectPicture = LogoFactory.ForPromotion(p);
            SubjectPictureIsLogo = true;
            SubjectIsNobody = known.Count == 0;
            SubjectLine = SubjectIsNobody ? ViewModels.Fame.NobodyLine(p) : ViewModels.Fame.ReachLine(w, known);
            SubjectHome = HomeCity(w) is { } home ? $"Home city: {ViewModels.Fame.CityLabel(w, home)}" : "";
        }
        else return;

        var homeId = HomeCity(w)?.Id;
        foreach (var (city, fame) in known.Take(6))
            HotSpots.Add(new HotSpot
            {
                City = city.Name,
                Where = w.Get(city.Country)?.Name is { } cn && cn != city.Name ? cn : "",
                Fame = fame,
                HeatBrush = ViewModels.Fame.Heat(fame),
                InkBrush = ViewModels.Fame.InkOn(fame),
                IsHome = city.Id == homeId,
                Region = city.Region,
                Country = city.Country,
            });
        HotSpotsHeader = HotSpots.Count == 0 ? "" : "HOTTEST CITIES  ·  click one to open its country";
    }

    // --- World: the seven continental regions --------------------------------
    private void BuildRegions(World world)
    {
        var home = HomeCity(world);
        foreach (var region in Layout)
        {
            int value = InRegion(world, region);
            var orgs = OrgsInRegion(world, region);
            var cities = world.Cities.Values.Where(c => c.Region == region).ToList();
            bool playerHere = _layer != MapLayer.Fighter && world.Get(world.PlayerPromotion)?.BaseRegion == region;

            var card = new PlaceCard
            {
                Name = Pretty(region),
                Interest = value,
                HeatBrush = HeatOf(value),
                TextBrush = InkOf(value),
                IsPlayerHere = playerHere,
                IsHome = home?.Region == region,
                Drillable = cities.Count > 0,
                DrillTo = PlaceLevel.Region,
                Region = region,
                ValueTip = ValueTip(Pretty(region)),
                OrgSummary = IsFameLayer ? FameSummary(value, cities)
                    : $"{Count(orgs.Count, "promotion")}  ·  {Count(cities.Count, "city", "cities")}",
                CitiesHeader = IsFameLayer ? "WHERE THE NAME CARRIES" : "HOTTEST CITIES",
                CitiesLine = IsFameLayer ? FameCities(cities) : TopCities(cities),
            };
            foreach (var p in orgs) card.Orgs.Add(new MapOrgRow(p, world));
            Places.Add(card);
        }

        if (IsFameLayer)
        {
            Summary = "";
            LevelHint = "Click a region to see its countries";
            return;
        }
        int active = world.Promotions.Values.Count(p => !p.Defunct);
        Summary = $"{active} active promotions across {world.Cities.Count} cities";
        LevelHint = "Click a region to see its countries";
    }

    // --- Region: its countries with a fight scene ----------------------------
    private void BuildCountries(World world, Region region)
    {
        var home = HomeCity(world);
        var countries = world.Cities.Values
            .Where(c => c.Region == region)
            .Select(c => c.Country)
            .Distinct()
            .Select(id => world.Get(id))
            .Where(c => c is not null)
            .Select(c => c!)
            .Select(c => (c, v: InCountry(world, c.Id)))
            .OrderByDescending(t => t.v).ThenByDescending(t => MmaInterest.InCountry(world, t.c.Id)).ThenBy(t => t.c.Name)
            .ToList();

        foreach (var (country, value) in countries)
        {
            var cities = world.Cities.Values.Where(c => c.Country == country.Id).ToList();
            var orgs = OrgsWhere(world, p => p.HomeCity is { } hc && world.Get(hc)?.Country == country.Id);

            var card = new PlaceCard
            {
                Name = country.Name,
                Interest = value,
                HeatBrush = HeatOf(value),
                TextBrush = InkOf(value),
                IsHome = home?.Country == country.Id,
                Drillable = cities.Count > 0,
                DrillTo = PlaceLevel.Country,
                Region = region,
                Country = country.Id,
                ValueTip = ValueTip(country.Name),
                OrgSummary = IsFameLayer ? FameSummary(value, cities)
                    : $"{Count(orgs.Count, "promotion")}  ·  {Count(cities.Count, "city", "cities")}",
                CitiesHeader = IsFameLayer ? "WHERE THE NAME CARRIES" : "HOTTEST CITIES",
                CitiesLine = IsFameLayer ? FameCities(cities) : TopCities(cities),
            };
            foreach (var p in orgs) card.Orgs.Add(new MapOrgRow(p, world));
            Places.Add(card);
        }

        Summary = IsFameLayer ? "" : $"{Pretty(region)} — {Count(countries.Count, "country", "countries")} with a fight scene";
        LevelHint = "Click a country to see its cities";
    }

    // --- Country: its individual fight cities (the leaves) -------------------
    private void BuildCities(World world, CountryId countryId)
    {
        var home = HomeCity(world);
        var cities = world.Cities.Values
            .Where(c => c.Country == countryId)
            .OrderByDescending(InCity).ThenByDescending(c => c.MmaInterest).ThenBy(c => c.Id.Value)
            .ToList();

        foreach (var city in cities)
        {
            int value = InCity(city);
            var orgs = OrgsWhere(world, p => p.HomeCity == city.Id);
            int locals = world.Fighters.Values.Count(f => f.HomeCity == city.Id && f.Status != FighterStatus.Retired);
            bool playerHere = _layer != MapLayer.Fighter && world.Get(world.PlayerPromotion)?.HomeCity == city.Id;
            bool isHome = home?.Id == city.Id;

            var card = new PlaceCard
            {
                Name = city.Name,
                Interest = value,
                HeatBrush = HeatOf(value),
                TextBrush = InkOf(value),
                IsPlayerHere = playerHere,
                IsHome = isHome,
                Drillable = false,
                ValueTip = ValueTip(city.Name),
                OrgSummary = IsFameLayer
                    ? $"{Cap(ViewModels.Fame.PlaceWord(value))} here  ·  MMA interest {city.MmaInterest}  ·  market {city.Market}"
                    : $"Market {city.Market}  ·  {Count(orgs.Count, "promotion")}",
                CitiesHeader = IsFameLayer && isHome ? "HOME TOWN" : "FROM THIS CITY",
                CitiesLine = locals == 0 ? "no ranked fighters call it home" : $"{Count(locals, "fighter")} call it home",
            };
            foreach (var p in orgs) card.Orgs.Add(new MapOrgRow(p, world));
            Places.Add(card);
        }

        var country = world.Get(countryId);
        Summary = IsFameLayer ? "" : $"{country?.Name} — {Count(cities.Count, "fight city", "fight cities")}";
        LevelHint = "";
    }

    private void BuildBreadcrumb(World world)
    {
        Breadcrumb.Clear();
        Breadcrumb.Add(new Crumb { Label = "World", Level = PlaceLevel.World });
        if (_level is PlaceLevel.Region or PlaceLevel.Country)
            Breadcrumb.Add(new Crumb { Label = Pretty(_region), Level = PlaceLevel.Region, Region = _region });
        if (_level is PlaceLevel.Country && _country is { } cid)
            Breadcrumb.Add(new Crumb { Label = world.Get(cid)?.Name ?? "Country", Level = PlaceLevel.Country, Region = _region, Country = cid });
        Breadcrumb[^1].IsLast = true;
    }

    // --- Fame-layer lines --------------------------------------------------------------------

    /// <summary>"Popular · 4 of 9 cities know the name", or "Nobody here knows the name yet".</summary>
    private string FameSummary(int value, List<City> cities)
    {
        int knowing = cities.Count(c => InCity(c) > 0);
        if (knowing == 0) return "Nobody here knows the name yet";
        // (a name a few cities know can still average out to 0 across a big market: say "barely", never "unknown")
        string word = value == 0 ? "barely heard of" : ViewModels.Fame.PlaceWord(value);
        return $"{Cap(word)} overall  ·  {knowing} of {Count(cities.Count, "city", "cities")} know the name";
    }

    /// <summary>The three cities in a place where the name carries furthest ("Tbilisi 62 · Batumi 30").</summary>
    private string FameCities(List<City> cities)
    {
        var top = cities.Select(c => (c, v: InCity(c))).Where(t => t.v > 0)
            .OrderByDescending(t => t.v).ThenBy(t => t.c.Id.Value).Take(3).ToList();
        return top.Count == 0 ? "—" : string.Join("   ·   ", top.Select(t => $"{t.c.Name} {t.v}"));
    }

    // --- Helpers -------------------------------------------------------------
    private static List<Promotion> OrgsInRegion(World world, Region region) =>
        world.Promotions.Values.Where(p => !p.Defunct && p.BaseRegion == region)
            .OrderByDescending(p => p.Prestige.Value).ToList();

    private static List<Promotion> OrgsWhere(World world, Func<Promotion, bool> pred) =>
        world.Promotions.Values.Where(p => !p.Defunct && pred(p))
            .OrderByDescending(p => p.Prestige.Value).ToList();

    private static string TopCities(IEnumerable<City> cities) =>
        string.Join("   ·   ", cities.OrderByDescending(c => c.MmaInterest).Take(3).Select(c => $"{c.Name} {c.MmaInterest}"));

    private static string Count(int n, string singular, string? plural = null) =>
        n == 1 ? $"1 {singular}" : $"{n} {plural ?? singular + "s"}";

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    // MMA interest → a cold-blue-to-hot-red heat colour.
    private static Brush Heat(int interest)
    {
        double t = Math.Clamp((interest - 25) / 60.0, 0, 1);
        byte Lerp(int a, int b) => (byte)Math.Round(a + (b - a) * t);
        var brush = new SolidColorBrush(Color.FromRgb(Lerp(0x2C, 0xC0), Lerp(0x5F, 0x39), Lerp(0x8A, 0x2B)));
        brush.Freeze();
        return brush;
    }

    private static Brush TextOn(int interest) =>
        interest >= 55 ? Brushes.White : new SolidColorBrush(Color.FromRgb(0xE6, 0xE9, 0xEC));

    private static string Pretty(Region r) => ViewModels.Fame.PrettyRegion(r);
}
