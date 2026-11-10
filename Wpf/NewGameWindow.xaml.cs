using System.Windows;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
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

        SeedBox.Text = Random.Shared.Next(1, 1_000_000).ToString();
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
        else
        {
            var row = (TakeoverRow)(TakeoverBox.SelectedItem ?? TakeoverBox.Items[0]);
            Result = new WorldBuilder.Options { Seed = seed, PlayerPromotionIndex = row.Index };
            Summary = $"You take over {row.Spec.Name} ({row.Spec.Abbreviation}) — {row.Spec.Balance} in the bank.";
        }

        DialogResult = true;
    }
}
