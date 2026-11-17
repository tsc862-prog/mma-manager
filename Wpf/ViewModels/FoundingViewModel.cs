using System.Collections.ObjectModel;
using System.Windows.Input;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Living;
using MmaManager.Wpf.Mvvm;

namespace MmaManager.Wpf.ViewModels;

/// <summary>A city in the founding list: where, what kind of market, and the sim's reason when a house can't open there.</summary>
public sealed class FoundingCityRow
{
    public FoundingCityRow(FoundingCity c)
    {
        City = c;
        Name = c.Name;
        Line = $"{c.Country}  ·  {FoundingViewModel.MarketWords(c.Market)}";
        Tag = c.Open ? "" : "closed";
    }

    public FoundingCity City { get; }
    public string Name { get; }
    public string Line { get; }
    public string Tag { get; }
    public bool Open => City.Open;
    public string? Refusal => City.Refusal;
}

/// <summary>One choice the era leaves to the founder (a round length, the gloves, ring or cage).</summary>
public sealed record FoundingPick(string Label, int? Minutes = null, GloveType? Gloves = null, Enclosure? Enclosure = null)
{
    public override string ToString() => Label;
}

/// <summary>
/// The FOUND YOUR PROMOTION window's model: name, abbreviation (suggested from the name until he edits it), the
/// home city from the cities the sim allows today (each with what it means), the house rules the era leaves to him
/// in that city, and the bank and prestige he opens with, stated before he commits. Everything is read from
/// <see cref="PlayerFounding"/>; the only write is <see cref="PlayerFounding.Found"/>, and a refusal comes back in
/// the sim's own words, shown inline. Founding asks first (<see cref="Confirm"/>): there is no way back to ringside.
/// </summary>
public sealed class FoundingViewModel : ObservableObject
{
    private readonly World _w;
    private readonly FoundingChoices _choices;

    public FoundingViewModel(World w)
    {
        _w = w;
        var today = w.CurrentDate;
        _choices = PlayerFounding.Choices(w, today);
        // Only countries with a scene: elsewhere every city is refused for the same reason (said once, below the list).
        var sceneCountries = _choices.Cities.Where(c => c.Open).Select(c => c.CountryCode)
            .Concat(OriginsScenes.Scenes(w).Select(s => s.Code)).ToHashSet(StringComparer.Ordinal);
        foreach (var c in _choices.Cities.Where(c => sceneCountries.Contains(c.CountryCode))
                     .OrderByDescending(c => c.Open).ThenByDescending(c => c.Market).ThenBy(c => c.Name, StringComparer.Ordinal))
            Cities.Add(new FoundingCityRow(c));
        int elsewhere = _choices.Cities.Select(c => c.CountryCode).Distinct().Count(k => !sceneCountries.Contains(k));
        ElsewhereLine = $"Every other country ({elsewhere}) has no MMA scene yet: no gyms, no crowd, no fighters to sign. The list grows as the sport spreads.";

        Bank = _choices.Bank.ToString();
        Prestige = _choices.Prestige;
        StartLine = "A newcomer's money and a newcomer's name. Nobody is signed: you get one free open tryout of local men, "
                  + "then the free-agent market at their asking terms.";
        Divisions = _choices.OpenWeight
            ? "One open division with a vacant belt: no weight classes yet."
            : $"The sport's weight lines, each with a vacant belt: {string.Join(", ", _choices.WeightLines.Select(OriginsReforms.Name))}.";
        LateLine = PlayerFounding.LateInYear(today)
            ? $"It's late in {today.Year}: nobody will judge you on what is left of it. The owner's targets start on 1 January {today.Year + 1}."
            : $"The owner's targets run to the end of {today.Year}.";
        Refusal = _choices.Refusal ?? "";

        FoundCommand = new RelayCommand(Found, () => BlockReason.Length == 0);
        SelectedCity = Cities.FirstOrDefault(c => c.Open) ?? Cities.FirstOrDefault();
    }

    // --- the name
    private string _name = "";
    public string Name
    {
        get => _name;
        set
        {
            if (!Set(ref _name, value ?? "")) return;
            if (!_abbreviationEdited) { _abbreviation = PlayerFounding.SuggestAbbreviation(_w, _name); OnPropertyChanged(nameof(Abbreviation)); }
            Error = "";
            Revalidate();
        }
    }

    private bool _abbreviationEdited;
    private string _abbreviation = "";
    public string Abbreviation
    {
        get => _abbreviation;
        set
        {
            string v = (value ?? "").ToUpperInvariant();
            if (!Set(ref _abbreviation, v)) return;
            // Typing his own letters stops the suggestion following the name; clearing the box hands it back.
            _abbreviationEdited = v.Length > 0 && v != PlayerFounding.SuggestAbbreviation(_w, _name);
            Error = "";
            Revalidate();
        }
    }

    // --- the city
    public ObservableCollection<FoundingCityRow> Cities { get; } = new();
    public string ElsewhereLine { get; }

    private FoundingCityRow? _city;
    public FoundingCityRow? SelectedCity
    {
        get => _city;
        set
        {
            if (!Set(ref _city, value)) return;
            DescribeCity();
            BuildPicks();
            Error = "";
            OnPropertyChanged(string.Empty);
            Revalidate();
        }
    }

    public string CityTitle { get; private set; } = "";
    public string CityMarket { get; private set; } = "";
    public string CityNeighbours { get; private set; } = "";
    public string CityScene { get; private set; } = "";
    public string CityRefusal { get; private set; } = "";
    public bool CityClosed => CityRefusal.Length > 0;
    public ObservableCollection<string> RuleNotes { get; } = new();

    private void DescribeCity()
    {
        RuleNotes.Clear();
        if (_city is null) { CityTitle = CityMarket = CityNeighbours = CityScene = CityRefusal = ""; return; }
        var c = _city.City;
        CityTitle = $"{c.Name}, {c.Country}";
        CityMarket = $"{Capital(MarketWords(c.Market))} (market {c.Market}), and {InterestWords(c.MmaInterest)} (interest {c.MmaInterest}).";
        var here = _w.Promotions.Values
            .Where(p => !p.Defunct && p.HomeCity is { } hc && _w.Get(hc) is { } pc && _w.Get(pc.Country)?.Code == c.CountryCode)
            .OrderBy(p => p.Id.Value)
            .Select(p => $"{p.Name} ({_w.Get(p.HomeCity!.Value)!.Name})").ToList();
        CityNeighbours = here.Count == 0
            ? $"No house runs in {c.Country} yet: you would be the first."
            : $"Already in {c.Country}: {string.Join(", ", here)}.";
        var scene = OriginsScenes.Scenes(_w).FirstOrDefault(s => s.Code == c.CountryCode);
        CityScene = scene is null ? ""
            : $"The scene in {c.Country}: {scene.Gyms} gym{(scene.Gyms == 1 ? "" : "s")}, {scene.Men} active fighter{(scene.Men == 1 ? "" : "s")}"
              + (scene.SecondCentre ? ". It is the sport's second centre." : ".");
        CityRefusal = c.Refusal ?? "";
        foreach (var n in c.Rules.Notes) RuleNotes.Add(n);
    }

    // --- the rules the era leaves to him here
    public ObservableCollection<FoundingPick> MinuteChoices { get; } = new();
    public ObservableCollection<FoundingPick> GloveChoices { get; } = new();
    public ObservableCollection<FoundingPick> EnclosureChoices { get; } = new();
    public bool HasMinuteChoice => MinuteChoices.Count > 1;
    public bool HasGloveChoice => GloveChoices.Count > 1;
    public bool HasEnclosureChoice => EnclosureChoices.Count > 1;
    public FoundingPick? Minutes { get; set; }
    public FoundingPick? Gloves { get; set; }
    public FoundingPick? Enclosure { get; set; }
    /// <summary>What the era decides for him in this city, in one line.</summary>
    public string FixedLine { get; private set; } = "";

    private void BuildPicks()
    {
        MinuteChoices.Clear(); GloveChoices.Clear(); EnclosureChoices.Clear();
        if (_city is null) return;
        var r = _city.City.Rules;
        foreach (int m in r.SingleRoundMinutes) MinuteChoices.Add(new FoundingPick($"One {m}-minute round, no judges", Minutes: m));
        foreach (var g in r.Gloves) GloveChoices.Add(new FoundingPick(GloveWords(g), Gloves: g));
        foreach (var e in r.Enclosures) EnclosureChoices.Add(new FoundingPick(HouseRules.EnclosureLine(e), Enclosure: e));
        Minutes = MinuteChoices.FirstOrDefault();
        Gloves = GloveChoices.FirstOrDefault();
        Enclosure = EnclosureChoices.FirstOrDefault();

        var fixedBits = new List<string>();
        if (r.SingleRoundMinutes.Count == 0)
            fixedBits.Add(r.StandardRounds == r.TitleRounds
                ? $"{r.StandardRounds} rounds of five minutes, with judges"
                : $"{r.StandardRounds} rounds of five minutes ({r.TitleRounds} for a title), with judges");
        else if (r.SingleRoundMinutes.Count == 1) fixedBits.Add($"one {r.SingleRoundMinutes[0]}-minute round, no judges");
        if (r.Gloves.Count == 1) fixedBits.Add(GloveWords(r.Gloves[0]).ToLowerInvariant());
        if (r.Enclosures.Count == 1) fixedBits.Add(HouseRules.EnclosureLine(r.Enclosures[0]).ToLowerInvariant());
        FixedLine = fixedBits.Count == 0 ? "" : "Set by the sport here: " + string.Join("; ", fixedBits) + ".";
    }

    // --- what he opens with
    public string Bank { get; }
    public int Prestige { get; }
    public string StartLine { get; }
    public string Divisions { get; }
    public string LateLine { get; }

    // --- founding
    public string Refusal { get; }

    /// <summary>
    /// Why the doors can't open yet, checked before he is asked anything (empty when they can): the sim says no
    /// house can be founded today, no city has room, the picked city is closed (the sim's own reason), or the name
    /// or abbreviation is missing. The sim's full checks (a name or abbreviation already taken) still run on Found.
    /// </summary>
    public string BlockReason
    {
        get
        {
            if (!_choices.CanFound) return _choices.Refusal ?? "No promotion can be founded today.";
            if (!Cities.Any(c => c.Open)) return "No city has room for a new house today: every scene with a crowd already has one, or its halls are closed.";
            if (_city is null) return "Pick a city for the promotion's home.";
            if (!_city.Open) return _city.Refusal ?? $"{_city.Name} can't take a new house today.";
            if (Name.Trim().Length < 3) return "Give the promotion a name of at least three letters.";
            if (Abbreviation.Trim().Length == 0) return "Give the promotion an abbreviation.";
            return "";
        }
    }
    public bool IsBlocked => BlockReason.Length > 0;

    private void Revalidate()
    {
        OnPropertyChanged(nameof(BlockReason));
        OnPropertyChanged(nameof(IsBlocked));
        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
    }
    public bool CanFound => _choices.CanFound;

    private string _error = "";
    /// <summary>The sim's refusal of the last try, word for word.</summary>
    public string Error { get => _error; set => Set(ref _error, value ?? ""); }

    /// <summary>Asks before the doors open (wired to a dialog by the window; null in harness renders = yes).</summary>
    public Func<string, string, bool>? Confirm { get; set; }

    /// <summary>The house, once founded (the window closes on it).</summary>
    public Promotion? Founded { get; private set; }
    public Action? Done { get; set; }

    public ICommand FoundCommand { get; }

    private void Found()
    {
        if (BlockReason is { Length: > 0 } blocked) { Error = blocked; return; }
        if (_city is null) return;
        var spec = new PlayerFoundingSpec(Name, Abbreviation, _city.City.Id, Minutes?.Minutes, Gloves?.Gloves, Enclosure?.Enclosure);
        string body = $"{Name.Trim()} ({Abbreviation.Trim().ToUpperInvariant()}) opens in {_city.City.Name}, {_city.City.Country}, today, "
                    + $"with {Bank} in the bank, prestige {Prestige} and nobody signed.";
        if (Confirm is { } ask && !ask(body, "This ends watching from ringside: from today you run a promotion, and there is no going back to the seats."))
            return;
        var promo = PlayerFounding.Found(_w, _w.CurrentDate, spec, out var refused);
        if (promo is null) { Error = refused ?? "The promotion could not be founded."; return; }
        Founded = promo;
        Done?.Invoke();
    }

    internal static string MarketWords(int market) => market switch
    {
        >= 72 => "a big market",
        >= 45 => "a mid-sized market",
        _ => "a small market",
    };

    private static string InterestWords(int interest) => interest switch
    {
        >= 70 => "fight-mad",
        >= 50 => "warm to fighting",
        _ => "cool on fighting so far",
    };

    private static string GloveWords(GloveType g) => g switch
    {
        GloveType.Bare => "Bare hands, with the fighter's own tape",
        GloveType.Optional => "Gloves if a man wants them",
        GloveType.ThinOpenFinger => "Thin open-fingered gloves for everyone",
        _ => "Today's small open-fingered gloves",
    };

    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
