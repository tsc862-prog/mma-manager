using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using System.Windows.Media;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Geography;
using MmaManager.Core.Sim.Org;
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

/// <summary>A place (region / country / city) as a heat-mapped, drillable card.</summary>
public sealed class PlaceCard
{
    public required string Name { get; init; }
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

/// <summary>
/// The world at a glance, drill-down style: start on the seven continental regions
/// (heat-mapped by MMA interest), click into a region to see its countries, then a
/// country to see its individual fight cities. Each card colour-keys the local
/// interest and lists the promotions based there. A breadcrumb walks back up.
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

    public ObservableCollection<PlaceCard> Places { get; } = new();
    public ObservableCollection<Crumb> Breadcrumb { get; } = new();

    public ICommand DrillCommand { get; }
    public ICommand NavigateCommand { get; }

    private string _summary = "";
    public string Summary { get => _summary; private set => Set(ref _summary, value); }

    private string _levelHint = "";
    public string LevelHint { get => _levelHint; private set => Set(ref _levelHint, value); }

    public WorldMapViewModel()
    {
        DrillCommand = new RelayCommand<PlaceCard>(Drill);
        NavigateCommand = new RelayCommand<Crumb>(NavigateTo);
    }

    public void Attach(World world)
    {
        _world = world;
        _level = PlaceLevel.World;
        _region = default;
        _country = null;
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

    private void Rebuild()
    {
        if (_world is null) return;
        Places.Clear();
        switch (_level)
        {
            case PlaceLevel.World: BuildRegions(_world); break;
            case PlaceLevel.Region: BuildCountries(_world, _region); break;
            case PlaceLevel.Country: BuildCities(_world, _country!.Value); break;
        }
        BuildBreadcrumb(_world);
    }

    // --- World: the seven continental regions --------------------------------
    private void BuildRegions(World world)
    {
        foreach (var region in Layout)
        {
            int interest = MmaInterest.InRegion(world, region);
            var orgs = OrgsInRegion(world, region);
            int cities = world.Cities.Values.Count(c => c.Region == region);
            bool playerHere = world.Get(world.PlayerPromotion)?.BaseRegion == region;

            var card = new PlaceCard
            {
                Name = Pretty(region),
                Interest = interest,
                HeatBrush = Heat(interest),
                TextBrush = TextOn(interest),
                IsPlayerHere = playerHere,
                Drillable = cities > 0,
                DrillTo = PlaceLevel.Region,
                Region = region,
                OrgSummary = $"{Count(orgs.Count, "promotion")}  ·  {Count(cities, "city", "cities")}",
                CitiesLine = TopCities(world.Cities.Values.Where(c => c.Region == region)),
            };
            foreach (var p in orgs) card.Orgs.Add(new MapOrgRow(p, world));
            Places.Add(card);
        }

        int active = world.Promotions.Values.Count(p => !p.Defunct);
        Summary = $"{active} active promotions across {world.Cities.Count} cities";
        LevelHint = "Click a region to see its countries";
    }

    // --- Region: its countries with a fight scene ----------------------------
    private void BuildCountries(World world, Region region)
    {
        var countries = world.Cities.Values
            .Where(c => c.Region == region)
            .Select(c => c.Country)
            .Distinct()
            .Select(id => world.Get(id))
            .Where(c => c is not null)
            .Select(c => c!)
            .OrderByDescending(c => MmaInterest.InCountry(world, c.Id))
            .ToList();

        foreach (var country in countries)
        {
            int interest = MmaInterest.InCountry(world, country.Id);
            var cities = world.Cities.Values.Where(c => c.Country == country.Id).ToList();
            var orgs = OrgsWhere(world, p => p.HomeCity is { } hc && world.Get(hc)?.Country == country.Id);

            var card = new PlaceCard
            {
                Name = country.Name,
                Interest = interest,
                HeatBrush = Heat(interest),
                TextBrush = TextOn(interest),
                Drillable = cities.Count > 0,
                DrillTo = PlaceLevel.Country,
                Region = region,
                Country = country.Id,
                OrgSummary = $"{Count(orgs.Count, "promotion")}  ·  {Count(cities.Count, "city", "cities")}",
                CitiesLine = TopCities(cities),
            };
            foreach (var p in orgs) card.Orgs.Add(new MapOrgRow(p, world));
            Places.Add(card);
        }

        Summary = $"{Pretty(region)} — {Count(countries.Count, "country", "countries")} with a fight scene";
        LevelHint = "Click a country to see its cities";
    }

    // --- Country: its individual fight cities (the leaves) -------------------
    private void BuildCities(World world, CountryId countryId)
    {
        var cities = world.Cities.Values
            .Where(c => c.Country == countryId)
            .OrderByDescending(c => c.MmaInterest)
            .ToList();

        foreach (var city in cities)
        {
            var orgs = OrgsWhere(world, p => p.HomeCity == city.Id);
            int locals = world.Fighters.Values.Count(f => f.HomeCity == city.Id && f.Status != FighterStatus.Retired);
            bool playerHere = world.Get(world.PlayerPromotion)?.HomeCity == city.Id;

            var card = new PlaceCard
            {
                Name = city.Name,
                Interest = city.MmaInterest,
                HeatBrush = Heat(city.MmaInterest),
                TextBrush = TextOn(city.MmaInterest),
                IsPlayerHere = playerHere,
                Drillable = false,
                OrgSummary = $"Market {city.Market}  ·  {Count(orgs.Count, "promotion")}",
                CitiesHeader = "FROM THIS CITY",
                CitiesLine = locals == 0 ? "no ranked fighters call it home" : $"{Count(locals, "fighter")} call it home",
            };
            foreach (var p in orgs) card.Orgs.Add(new MapOrgRow(p, world));
            Places.Add(card);
        }

        var country = world.Get(countryId);
        Summary = $"{country?.Name} — {Count(cities.Count, "fight city", "fight cities")}";
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

    // --- Helpers -------------------------------------------------------------
    private static System.Collections.Generic.List<Promotion> OrgsInRegion(World world, Region region) =>
        world.Promotions.Values.Where(p => !p.Defunct && p.BaseRegion == region)
            .OrderByDescending(p => p.Prestige.Value).ToList();

    private static System.Collections.Generic.List<Promotion> OrgsWhere(World world, Func<Promotion, bool> pred) =>
        world.Promotions.Values.Where(p => !p.Defunct && pred(p))
            .OrderByDescending(p => p.Prestige.Value).ToList();

    private static string TopCities(System.Collections.Generic.IEnumerable<City> cities) =>
        string.Join("   ·   ", cities.OrderByDescending(c => c.MmaInterest).Take(3).Select(c => $"{c.Name} {c.MmaInterest}"));

    private static string Count(int n, string singular, string? plural = null) =>
        n == 1 ? $"1 {singular}" : $"{n} {plural ?? singular + "s"}";

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

    private static string Pretty(Region r) => r switch
    {
        Region.NorthAmerica => "North America",
        Region.SouthAmerica => "South America",
        Region.MiddleEast => "Middle East",
        _ => r.ToString(),
    };
}
