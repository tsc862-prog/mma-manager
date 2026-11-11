using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using MmaManager.Core.Model;
using MmaManager.Wpf.Controls;
using MmaManager.Wpf.Mvvm;

namespace MmaManager.Wpf.ViewModels;

/// <summary>A choice in one of the Fighters filters (label + what it matches).</summary>
public sealed class FilterOption
{
    public required string Label { get; init; }
    public string Key { get; init; } = "";
    public override string ToString() => Label;
}

/// <summary>
/// The Fighters app: the whole world's fighter database. Search and filters run
/// over EVERY active fighter (not a pre-cut top list); the best matches by the
/// player's scouted read are shown (never true ability), id breaking ties, and
/// the screen says how many are shown of how many match. Cards by default, the
/// sortable table one toggle away. The scouted read for every fighter is worked
/// out once per world refresh — and only while the app is actually in use — so
/// typing in the search box only filters a prepared index.
/// </summary>
public sealed class FightersViewModel : ObservableObject
{
    /// <summary>How many matches are drawn at once (cards and table rows).</summary>
    public const int ShowLimit = 240;

    public const string AnyDivision = "", AnyPromotion = "", Anywhere = "";
    private const string FreeAgents = "fa", Mine = "mine";

    private sealed record Entry(Fighter F, int Scout, string Haystack, int DivisionKey, string Division,
        int? Org, Region Region, int Country);

    private World? _world;
    private List<Entry> _index = new();
    private Dictionary<FighterId, Event> _booked = new();
    private bool _dirty = true;
    private bool _watched;
    private IReadOnlyDictionary<FightId, (Fight, Event)> _fightIndex = new Dictionary<FightId, (Fight, Event)>();

    /// <summary>The card wall (best matches first).</summary>
    public ObservableCollection<object> Cards { get { Watch(); return _cards; } }
    private readonly ObservableCollection<object> _cards = new();

    /// <summary>The table's rows: the same matches, sortable by any column.</summary>
    public ObservableCollection<FighterRow> Rows { get { Watch(); return _rows; } }
    private readonly ObservableCollection<FighterRow> _rows = new();

    public ObservableCollection<FilterOption> DivisionOptions { get; } = new();
    public ObservableCollection<FilterOption> PromotionOptions { get; } = new();
    public ObservableCollection<FilterOption> FromOptions { get; } = new();

    // Cards or Table — remembered for the whole session.
    private static bool s_showTable;
    public bool ShowTable
    {
        get => s_showTable;
        set
        {
            if (s_showTable == value) return;
            s_showTable = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowCards));
        }
    }
    public bool ShowCards { get => !ShowTable; set => ShowTable = !value; }

    private string _search = "";
    /// <summary>Name search (first, last or nickname; accents and case ignored; every word must match).</summary>
    public string Search { get => _search; set { if (Set(ref _search, value ?? "")) { OnPropertyChanged(nameof(HasSearch)); Refilter(); } } }
    public bool HasSearch => _search.Length > 0;

    private FilterOption? _division;
    public FilterOption? Division { get => _division; set { if (Set(ref _division, value)) Refilter(); } }

    private FilterOption? _promotion;
    public FilterOption? Promotion { get => _promotion; set { if (Set(ref _promotion, value)) Refilter(); } }

    private FilterOption? _from;
    public FilterOption? From { get => _from; set { if (Set(ref _from, value)) Refilter(); } }

    private bool _shortlistOnly;
    public bool ShortlistOnly { get => _shortlistOnly; set { if (Set(ref _shortlistOnly, value)) Refilter(); } }

    public System.Windows.Input.ICommand ClearCommand { get; }

    public FightersViewModel()
    {
        ClearCommand = new RelayCommand(() =>
        {
            _search = ""; OnPropertyChanged(nameof(Search)); OnPropertyChanged(nameof(HasSearch));
            _division = DivisionOptions.FirstOrDefault(); OnPropertyChanged(nameof(Division));
            _promotion = PromotionOptions.FirstOrDefault(); OnPropertyChanged(nameof(Promotion));
            _from = FromOptions.FirstOrDefault(); OnPropertyChanged(nameof(From));
            _shortlistOnly = false; OnPropertyChanged(nameof(ShortlistOnly));
            Refilter();
        });
    }

    /// <summary>Pick the "my promotion" filter (harness + other screens' shortcuts).</summary>
    public void ShowMyPromotion() => Promotion = PromotionOptions.FirstOrDefault(o => o.Key == Mine);

    private string _countLine = "";
    /// <summary>"Showing the best 240 of 1,912 matches" — always says what is cut.</summary>
    public string CountLine { get => _countLine; private set => Set(ref _countLine, value); }

    private bool _isCapped;
    public bool IsCapped { get => _isCapped; private set => Set(ref _isCapped, value); }

    private bool _noMatches;
    public bool NoMatches { get => _noMatches; private set => Set(ref _noMatches, value); }

    private int _matchCount;
    public int MatchCount { get => _matchCount; private set => Set(ref _matchCount, value); }

    private int _selectedFighterId;
    /// <summary>The fighter last picked or opened — an id, so it survives refreshes.</summary>
    public int SelectedFighterId
    {
        get => _selectedFighterId;
        set
        {
            if (!Set(ref _selectedFighterId, value)) return;
            var row = _rows.FirstOrDefault(r => r.Id == value);
            if (!ReferenceEquals(row, _selectedRow)) { _selectedRow = row; OnPropertyChanged(nameof(SelectedRow)); }
        }
    }
    private FighterRow? _selectedRow;
    public FighterRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (ReferenceEquals(_selectedRow, value)) return;
            _selectedRow = value;
            OnPropertyChanged();
            if (value is not null) SelectedFighterId = value.Id;
        }
    }

    /// <summary>Milliseconds the last index build and the last filter took (the harness prints them).</summary>
    public long LastIndexMs { get; private set; }
    public long LastFilterMs { get; private set; }

    public void Attach(World world, IReadOnlyDictionary<FightId, (Fight, Event)> fightIndex)
    {
        bool newWorld = !ReferenceEquals(world, _world);
        _world = world;
        _fightIndex = fightIndex;
        _dirty = true;
        if (newWorld) BuildOptions(world);
        // Nobody has looked at this app yet: the scouting index waits until someone does.
        if (_watched) Rebuild();
    }

    /// <summary>The shortlist changed somewhere: redraw the stars and the shortlist filter.</summary>
    public void ShortlistChanged() { if (_watched && !_dirty) Refilter(); }

    private void Watch()
    {
        if (_watched) return;
        _watched = true;
        if (_world is not null && _dirty) Rebuild();
    }

    private void BuildOptions(World world)
    {
        DivisionOptions.Clear();
        DivisionOptions.Add(new FilterOption { Label = "Every division", Key = AnyDivision });
        foreach (var sex in new[] { Sex.Male, Sex.Female })
            foreach (var wc in Enum.GetValues<WeightClass>())
                if (world.Fighters.Values.Any(f => f.Sex == sex && f.WeightClass == wc && f.Status != FighterStatus.Retired))
                    DivisionOptions.Add(new FilterOption { Label = FighterRow.DivisionLabel(wc, sex), Key = $"{(int)sex}:{(int)wc}" });
        _division = DivisionOptions[0];
        OnPropertyChanged(nameof(Division));

        PromotionOptions.Clear();
        PromotionOptions.Add(new FilterOption { Label = "Any promotion", Key = AnyPromotion });
        PromotionOptions.Add(new FilterOption { Label = "Free agents only", Key = FreeAgents });
        if (world.Get(world.PlayerPromotion) is { } mine)
            PromotionOptions.Add(new FilterOption { Label = $"My promotion ({mine.Abbreviation})", Key = Mine });
        foreach (var p in world.Promotions.Values.Where(p => !p.Defunct && p.Id != world.PlayerPromotion)
                     .OrderByDescending(p => p.Prestige.Value).ThenBy(p => p.Id.Value))
            PromotionOptions.Add(new FilterOption { Label = $"{p.Abbreviation} — {p.Name}", Key = p.Id.Value.ToString(CultureInfo.InvariantCulture) });
        _promotion = PromotionOptions[0];
        OnPropertyChanged(nameof(Promotion));

        FromOptions.Clear();
        FromOptions.Add(new FilterOption { Label = "Anywhere", Key = Anywhere });
        var withFighters = world.Fighters.Values.Where(f => f.Status != FighterStatus.Retired).Select(f => f.Country).ToHashSet();
        foreach (var region in Enum.GetValues<Region>())
        {
            var countries = world.Countries.Values.Where(c => c.Continent == region && withFighters.Contains(c.Id))
                .OrderBy(c => c.Name, StringComparer.CurrentCulture).ToList();
            if (countries.Count == 0) continue;
            FromOptions.Add(new FilterOption { Label = FighterRow.Pretty(region.ToString()), Key = "r" + (int)region });
            foreach (var c in countries)
                FromOptions.Add(new FilterOption { Label = "      " + c.Name, Key = "c" + c.Id.Value });
        }
        _from = FromOptions[0];
        OnPropertyChanged(nameof(From));
    }

    /// <summary>Work out the scouted read and a search key for every active fighter in the world.</summary>
    private void Rebuild()
    {
        if (_world is null) return;
        var world = _world;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        _booked = new Dictionary<FighterId, Event>();
        foreach (var ev in world.Events.Values.Where(e => e.Status == EventStatus.Scheduled).OrderBy(e => e.Date).ThenBy(e => e.Id.Value))
            foreach (var fight in ev.Card) { _booked.TryAdd(fight.RedCorner, ev); _booked.TryAdd(fight.BlueCorner, ev); }

        _index = world.Fighters.Values
            .Where(f => f.Status != FighterStatus.Retired)
            .Select(f => new Entry(f, FighterRow.Scouted(world, f),
                Fold($"{f.FirstName} {f.LastName} {f.Nickname}"),
                (int)f.Sex * 100 + (int)f.WeightClass, $"{(int)f.Sex}:{(int)f.WeightClass}",
                f.Contract?.Promotion.Value,
                world.Get(f.Country)?.Continent ?? f.Nationality, f.Country.Value))
            // Best first by YOUR scout's read, never the truth; id breaks ties.
            .OrderByDescending(e => e.Scout).ThenBy(e => e.F.Id.Value)
            .ToList();
        _dirty = false;
        LastIndexMs = clock.ElapsedMilliseconds;
        Refilter();
    }

    /// <summary>Lower-case, accents stripped: "Relámpago" finds "relampago".</summary>
    private static string Fold(string s)
    {
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (char c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    private void Refilter()
    {
        if (_world is null || !_watched) return;
        if (_dirty) { Rebuild(); return; }
        var world = _world;
        var clock = System.Diagnostics.Stopwatch.StartNew();

        string[] words = Fold(_search).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string div = _division?.Key ?? AnyDivision;
        string org = _promotion?.Key ?? AnyPromotion;
        string from = _from?.Key ?? Anywhere;
        int playerOrg = world.PlayerPromotion.Value;
        int orgId = org.Length > 0 && char.IsDigit(org[0]) ? int.Parse(org, CultureInfo.InvariantCulture) : 0;
        var shortlist = _shortlistOnly ? world.Shortlist.ToHashSet() : null;

        bool Keep(Entry e)
        {
            if (div.Length > 0 && e.Division != div) return false;
            if (org == FreeAgents && e.Org is not null) return false;
            if (org == Mine && e.Org != playerOrg) return false;
            if (orgId != 0 && e.Org != orgId) return false;
            if (from.Length > 0)
            {
                if (from[0] == 'r' && (int)e.Region != int.Parse(from.AsSpan(1), CultureInfo.InvariantCulture)) return false;
                if (from[0] == 'c' && e.Country != int.Parse(from.AsSpan(1), CultureInfo.InvariantCulture)) return false;
            }
            if (shortlist is not null && !shortlist.Contains(e.F.Id)) return false;
            foreach (var w in words) if (!e.Haystack.Contains(w, StringComparison.Ordinal)) return false;
            return true;
        }

        var matches = new List<Entry>(ShowLimit);
        int total = 0;
        foreach (var e in _index)   // already in scouted order
        {
            if (!Keep(e)) continue;
            total++;
            if (matches.Count < ShowLimit) matches.Add(e);
        }

        _cards.Clear();
        _rows.Clear();
        foreach (var e in matches)
        {
            _cards.Add(new FighterCardModel(e.F, world, _booked.GetValueOrDefault(e.F.Id)) { IsSelected = e.F.Id.Value == _selectedFighterId });
            _rows.Add(new FighterRow(e.F, world, _fightIndex));
        }

        MatchCount = total;
        IsCapped = total > matches.Count;
        NoMatches = total == 0;
        bool filtered = words.Length > 0 || div.Length > 0 || org.Length > 0 || from.Length > 0 || _shortlistOnly;
        string noun = filtered ? (total == 1 ? "match" : "matches") : "active fighters in the world";
        CountLine = total == 0 ? "No fighter matches."
            : IsCapped ? $"Showing the best {matches.Count} of {total:N0} {noun}, by your scout's read. Search a name or narrow the filters to reach anyone."
            : filtered ? $"{total:N0} {noun}, best first by your scout's read."
            : $"All {total:N0} {noun}, best first by your scout's read.";

        _selectedRow = _rows.FirstOrDefault(r => r.Id == _selectedFighterId);
        OnPropertyChanged(nameof(SelectedRow));
        LastFilterMs = clock.ElapsedMilliseconds;
    }
}
