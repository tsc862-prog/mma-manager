using System.Windows;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Org;
using MmaManager.Wpf.ViewModels;

namespace MmaManager.Wpf;

/// <summary>
/// The new-game setup dialog: found a brand-new promotion (the zero-to-hero
/// start) or take over one of the established organizations. Produces the
/// <see cref="WorldBuilder.Options"/> for the world it described in
/// <see cref="Result"/> when the player hits Start.
/// </summary>
public partial class NewGameWindow : Window
{
    /// <summary>How big a founded promotion starts — bankroll, reach, and how deep the hiring market runs.</summary>
    public sealed record ScalePreset(string Name, string Blurb, decimal Balance, int Prestige, int TalentPool, WeightClass[] Divisions)
    {
        public override string ToString() => Name;
    }

    // TalentPool is the hiring market generated INTO the org's own divisions —
    // roughly 30 candidates per division, which is what makes the free-agent
    // screen read like a real regional scene (the old 14/20/32 presets predated
    // that tuning and left a startup picking from a nearly empty shelf).
    public static ScalePreset[] Scales { get; } =
    {
        new("Shoestring", "Two divisions, $100,000, and an empty gym — hire every fighter yourself. The hardest road, the best story.",
            100_000m, 8, 60, new[] { WeightClass.Lightweight, WeightClass.Welterweight }),
        new("Regional startup", "A modest $250,000 bankroll and three empty divisions — build your roster from the free-agent market.",
            250_000m, 15, 90, WorldBuilder.FoundingSpec.DefaultDivisions),
        new("Funded challenger", "Serious backers, $1,000,000, five divisions to fill — arrive just below the established players.",
            1_000_000m, 28, 140, new[] { WeightClass.Featherweight, WeightClass.Lightweight, WeightClass.Welterweight, WeightClass.Middleweight, WeightClass.LightHeavyweight }),
    };

    public sealed record TakeoverRow(WorldBuilder.PromotionSpec Spec, int Index)
    {
        public string Title => $"{Spec.Name} ({Spec.Abbreviation})";
        public string Detail => $"{FighterRow.Pretty(Spec.Region.ToString())} · prestige {Spec.Prestige} · {Spec.Balance}";

        // The owner you would answer to: the designed owner stamped onto this house at world build
        // (StaffService.SeedOwners matches by abbreviation, the same key used here).
        private DesignedOwner? Owner => DesignedStaff.Owners.FirstOrDefault(o => o.Org == Spec.Abbreviation);
        public string OwnerName => Owner?.Name ?? "";
        public bool HasOwner => OwnerName.Length > 0;
        public PersonCard? OwnerCard => Owner is { } o
            ? new PersonCard
            {
                Name = o.Name,
                Kicker = "YOU WOULD ANSWER TO",
                Note = o.Note,
            }
            : null;
        public string Lore => Spec.Lore ?? "";
        public bool HasLore => Lore.Length > 0;
    }

    /// <summary>Set when the dialog closes with Start; null on cancel.</summary>
    public WorldBuilder.Options? Result { get; private set; }

    /// <summary>One-line description of the chosen start, for the status line.</summary>
    public string Summary { get; private set; } = "";

    public NewGameWindow()
    {
        InitializeComponent();

        foreach (var region in Enum.GetValues<Region>())
            RegionBox.Items.Add(FighterRow.Pretty(region.ToString()));
        RegionBox.SelectedIndex = 0;   // fills CityBox through OnRegionChanged

        foreach (var scale in Scales) ScaleBox.Items.Add(scale);
        ScaleBox.SelectedIndex = 1;

        foreach (var (spec, i) in WorldBuilder.Options.DefaultPromotions.Select((s, i) => (s, i)))
            TakeoverBox.Items.Add(new TakeoverRow(spec, i));
        TakeoverBox.SelectedIndex = 0;

        foreach (var house in OriginsStart.Houses) HouseBox.Items.Add(house);
        HouseBox.SelectedIndex = 0;
        RingsideFaces.ItemsSource = OriginsStart.Houses;

        SeedBox.Text = Random.Shared.Next(1, 1_000_000).ToString();
    }

    /// <summary>Origins takes the window's room: the other two starts fold away while it is chosen.</summary>
    private void OnModeChanged(object sender, RoutedEventArgs e)
    {
        if (OriginsPanel is null || FoundPanel is null || TakeoverPanel is null) return;
        bool origins = OriginsMode.IsChecked == true;
        OriginsPanel.Visibility = origins ? Visibility.Visible : Visibility.Collapsed;
        FoundPanel.Visibility = origins ? Visibility.Collapsed : Visibility.Visible;
        TakeoverPanel.Visibility = origins ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnHouseChanged(object sender, RoutedEventArgs e)
    {
        if (HouseDetail is null) return;
        HouseDetail.DataContext = HouseBox.SelectedItem;
        // Picking a house is not watching: the ringside tile lets go.
        if (HouseBox.SelectedItem is not null && RingsideTile?.IsChecked == true) RingsideTile.IsChecked = false;
        ShowOriginsDetail();
    }

    /// <summary>Watching from ringside: no house picked, the ringside story in the detail's place.</summary>
    private void OnRingsideChanged(object sender, RoutedEventArgs e)
    {
        if (RingsideTile.IsChecked == true) HouseBox.SelectedIndex = -1;
        else if (HouseBox.SelectedIndex < 0) HouseBox.SelectedIndex = 0;
        ShowOriginsDetail();
    }

    private bool Watching => RingsideTile?.IsChecked == true;

    private void ShowOriginsDetail()
    {
        if (RingsideDetail is null || HouseDetail is null) return;
        RingsideDetail.Visibility = Watching ? Visibility.Visible : Visibility.Collapsed;
        HouseDetail.Visibility = Watching ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Harness hook: show the Origins choice with one house picked.</summary>
    internal void ChooseOrigins(int house)
    {
        OriginsMode.IsChecked = true;
        HouseBox.SelectedIndex = Math.Clamp(house, 0, HouseBox.Items.Count - 1);
    }

    /// <summary>Harness hook: show the Origins choice watching from ringside.</summary>
    internal void ChooseRingside()
    {
        OriginsMode.IsChecked = true;
        RingsideTile.IsChecked = true;
    }

    private void OnScaleChanged(object sender, RoutedEventArgs e)
    {
        if (ScaleBox.SelectedItem is ScalePreset scale && ScaleBlurb is not null)
            ScaleBlurb.Text = scale.Blurb;
    }

    /// <summary>Home town follows home region — the region's fight capital leads the list.</summary>
    private void OnRegionChanged(object sender, RoutedEventArgs e)
    {
        if (CityBox is null || RegionBox.SelectedIndex < 0) return;
        var region = Enum.GetValues<Region>()[RegionBox.SelectedIndex];
        CityBox.Items.Clear();
        foreach (var city in Core.Sim.Geography.WorldAtlas.CitiesIn(region))
            CityBox.Items.Add(city.Anchor ? $"{city.Name}  ·  fight capital" : city.Name);
        if (CityBox.Items.Count > 0) CityBox.SelectedIndex = 0;
    }

    // The combo shows the capital with a tag; the spec wants the bare name.
    private string? ChosenCity =>
        CityBox.SelectedItem is string s && s.Length > 0 ? s.Split("  ·  ")[0] : null;

    private void OnStart(object sender, RoutedEventArgs e)
    {
        if (!ulong.TryParse(SeedBox.Text.Trim(), out var seed) || seed == 0)
        {
            ErrorText.Text = "Seed must be a positive number.";
            return;
        }

        if (FoundMode.IsChecked == true)
        {
            string name = NameBox.Text.Trim();
            string abbrev = AbbrevBox.Text.Trim().ToUpperInvariant();
            if (name.Length < 3) { ErrorText.Text = "Give your promotion a name."; return; }
            if (abbrev.Length < 2) { ErrorText.Text = "Give your promotion a 2–5 letter abbreviation."; return; }

            var region = Enum.GetValues<Region>()[RegionBox.SelectedIndex];
            var scale = (ScalePreset)ScaleBox.SelectedItem;
            string? city = ChosenCity;
            Result = new WorldBuilder.Options
            {
                Seed = seed,
                Founding = new WorldBuilder.FoundingSpec
                {
                    Name = name,
                    Abbreviation = abbrev,
                    Region = region,
                    HomeCityName = city,
                    Prestige = scale.Prestige,
                    StartingBalance = new Money(scale.Balance),
                    TalentPool = scale.TalentPool,
                    Divisions = scale.Divisions,
                },
            };
            Summary = $"You founded {name} — {new Money(scale.Balance)} in the bank, an empty gym, and "
                      + $"{city ?? FighterRow.Pretty(region.ToString())} as home turf. Hire your fighters, book loud, climb.";
        }
        else if (OriginsMode.IsChecked == true && Watching)
        {
            Result = WorldBuilder.Options.OriginsObserver(seed);
            Summary = OriginsStart.RingsideSummary;
        }
        else if (OriginsMode.IsChecked == true)
        {
            var house = (OriginsHouseCard)(HouseBox.SelectedItem ?? HouseBox.Items[0]);
            Result = WorldBuilder.Options.Origins(seed, house.Index);
            Summary = OriginsStart.SummaryFor(house.Spec);
        }
        else
        {
            var row = (TakeoverRow)(TakeoverBox.SelectedItem ?? TakeoverBox.Items[0]);
            Result = new WorldBuilder.Options { Seed = seed, PlayerPromotionIndex = row.Index };
            Summary = $"You take over {row.Spec.Name} ({row.Spec.Abbreviation}) — {row.Spec.Balance} in the bank.";
        }

        DialogResult = true;
    }
}
