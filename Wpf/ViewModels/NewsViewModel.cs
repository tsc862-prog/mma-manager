using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using MmaManager.Core.Model;
using MmaManager.Wpf.Art;
using MmaManager.Wpf.Controls;
using MmaManager.Wpf.Mvvm;

namespace MmaManager.Wpf.ViewModels;

/// <summary>
/// FightWire, the news app on the promoter's desktop. A lead story with a big
/// picture, then the wire as story cards grouped by day (or by kind), each with
/// the face or logo it is about and links that follow the story to its people,
/// promotion and event. Filters: everything / my promotion / my fighters, a kind,
/// a division. Reads the sim's news feed only (the sim keeps the newest 1,000).
/// </summary>
public sealed class NewsViewModel : ObservableObject
{
    /// <summary>The sim keeps this many stories; older ones roll off (Core.Sim.Living.News).</summary>
    public const int WireCapacity = 1000;

    public const string Everything = "Everything", MyPromotion = "My promotion", MyFighters = "My fighters";
    public const string AllKinds = "All stories", Titles = "Title fights", Callouts = "Callouts & rivalries",
        Signings = "Signings & careers", Results = "Results & awards", Business = "Business";

    /// <summary>Set by the shell: follow a story to its people, promotion and event.</summary>
    public ICommand? OpenFighterCommand { get; set; }
    public ICommand? OpenOrgCommand { get; set; }
    public ICommand? OpenEventCommand { get; set; }

    /// <summary>The wall: the lead (a <see cref="CardBanner"/>), day/kind headings and story cards.</summary>
    public ObservableCollection<object> Items { get; } = new();

    public IReadOnlyList<string> KindOptions { get; } = new[] { AllKinds, Titles, Callouts, Signings, Results, Business };
    public ObservableCollection<FilterOption> DivisionOptions { get; } = new();

    private string _who = Everything;
    public string Who { get => _who; set { if (Set(ref _who, value ?? Everything)) { RaiseWho(); Refilter(); } } }
    public bool WhoEverything { get => Who == Everything; set { if (value) Who = Everything; } }
    public bool WhoMine { get => Who == MyPromotion; set { if (value) Who = MyPromotion; } }
    public bool WhoMyFighters { get => Who == MyFighters; set { if (value) Who = MyFighters; } }
    private void RaiseWho() { OnPropertyChanged(nameof(WhoEverything)); OnPropertyChanged(nameof(WhoMine)); OnPropertyChanged(nameof(WhoMyFighters)); }

    private string _kind = AllKinds;
    public string Kind { get => _kind; set { if (Set(ref _kind, value ?? AllKinds)) Refilter(); } }

    private FilterOption? _division;
    public FilterOption? Division { get => _division; set { if (Set(ref _division, value)) Refilter(); } }

    private bool _byKind;
    /// <summary>Group the wire by kind of story instead of by day.</summary>
    public bool ByKind { get => _byKind; set { if (Set(ref _byKind, value)) { OnPropertyChanged(nameof(ByDay)); Refilter(); } } }
    public bool ByDay { get => !_byKind; set => ByKind = !value; }

    private string _dateLine = "";
    public string DateLine { get => _dateLine; private set => Set(ref _dateLine, value); }
    private string _countLine = "";
    public string CountLine { get => _countLine; private set => Set(ref _countLine, value); }
    private bool _isEmpty;
    public bool IsEmpty { get => _isEmpty; private set => Set(ref _isEmpty, value); }
    private string _emptyMessage = "";
    public string EmptyMessage { get => _emptyMessage; private set => Set(ref _emptyMessage, value); }

    public ICommand ShowEverythingCommand { get; }

    private World? _world;
    private sealed record Story(NewsItem N, int Order, string KindName, string Division, bool AboutMine, bool AboutMyFighter);
    private List<Story> _all = new();

    public NewsViewModel()
    {
        ShowEverythingCommand = new RelayCommand(() =>
        {
            _who = Everything; RaiseWho(); OnPropertyChanged(nameof(Who));
            _kind = AllKinds; OnPropertyChanged(nameof(Kind));
            _division = DivisionOptions.FirstOrDefault(); OnPropertyChanged(nameof(Division));
            Refilter();
        });
    }

    public static string KindOf(NewsCategory c) => c switch
    {
        NewsCategory.TitleChange or NewsCategory.TitleDefense => Titles,
        NewsCategory.Callout or NewsCategory.Rivalry => Callouts,
        NewsCategory.Signing or NewsCategory.Debut or NewsCategory.Retirement => Signings,
        NewsCategory.Result or NewsCategory.Award or NewsCategory.Milestone or NewsCategory.Suspension or NewsCategory.Withdrawal => Results,
        _ => Business,
    };

    private static string IconOf(string kind) => kind switch
    {
        Titles => "Crown",
        Callouts => "Bell",
        Signings => "Pen",
        Results => "Trophy",
        _ => "Money",
    };

    public void Attach(World world)
    {
        bool newWorld = !ReferenceEquals(world, _world);
        _world = world;
        var player = world.PlayerPromotion;
        var mine = world.RosterOf(player).Select(f => f.Id).ToHashSet();
        _all = new List<Story>(world.News.Count);
        int order = 0;
        foreach (var n in Enumerable.Reverse(world.News))
        {
            var f = n.AboutFighter is { } fid ? world.Get(fid) : null;
            _all.Add(new Story(n, order++, KindOf(n.Category),
                f is null ? "" : $"{(int)f.Sex}:{(int)f.WeightClass}",
                n.Promotion == player, f is not null && mine.Contains(f.Id)));
        }
        if (newWorld || DivisionOptions.Count == 0)
        {
            DivisionOptions.Clear();
            DivisionOptions.Add(new FilterOption { Label = "Every division", Key = "" });
            foreach (var sex in new[] { Sex.Male, Sex.Female })
                foreach (var wc in Enum.GetValues<WeightClass>())
                    if (world.Fighters.Values.Any(x => x.Sex == sex && x.WeightClass == wc))
                        DivisionOptions.Add(new FilterOption { Label = FighterRow.DivisionLabel(wc, sex), Key = $"{(int)sex}:{(int)wc}" });
            _division = DivisionOptions[0];
            OnPropertyChanged(nameof(Division));
        }
        DateLine = world.CurrentDate.ToString("dddd d MMMM yyyy");
        Refilter();
    }

    private void Refilter()
    {
        if (_world is null) return;
        var world = _world;
        string div = _division?.Key ?? "";
        var shown = _all.Where(s =>
            (Who != MyPromotion || s.AboutMine) &&
            (Who != MyFighters || s.AboutMyFighter) &&
            (Kind == AllKinds || s.KindName == Kind) &&
            (div.Length == 0 || s.Division == div)).ToList();

        Items.Clear();
        IsEmpty = shown.Count == 0;
        EmptyMessage = Who == MyFighters ? "Nothing on the wire about your fighters with these filters yet. Win something and they'll write about it."
            : Who == MyPromotion ? "The wire hasn't written about your promotion with these filters yet. Put on a show and give them a reason."
            : "No stories match these filters.";

        string full = world.News.Count >= WireCapacity
            ? $"The wire keeps the newest {WireCapacity:N0} stories; older ones roll off as new ones land."
            : $"The wire keeps the newest {WireCapacity:N0} stories.";
        CountLine = shown.Count == _all.Count ? $"{shown.Count:N0} stories. {full}" : $"{shown.Count:N0} of {_all.Count:N0} stories. {full}";
        if (shown.Count == 0) return;

        // The lead: the biggest recent story (a belt changing hands, your promotion, someone talking).
        var recent = shown.Take(30).ToList();
        var lead = recent.OrderByDescending(s => Weight(s)).ThenBy(s => s.Order).First();
        Items.Add(new CardBanner { Content = Card(world, lead, big: true), Key = "lead|" + KeyOf(lead.N) });

        var rest = shown.Where(s => !ReferenceEquals(s, lead));
        if (ByKind)
        {
            foreach (var g in rest.GroupBy(s => s.KindName).OrderBy(g => Array.IndexOf(KindOptions.ToArray(), g.Key)))
            {
                var list = g.ToList();
                Items.Add(new CardGroup { Title = g.Key, Detail = $"{list.Count} stor{(list.Count == 1 ? "y" : "ies")}, newest first" });
                foreach (var s in list) Items.Add(Card(world, s, big: false));
            }
        }
        else
        {
            foreach (var g in rest.GroupBy(s => s.N.Date))
            {
                var list = g.ToList();
                Items.Add(new CardGroup { Title = DayTitle(world.CurrentDate, g.Key), Detail = $"{list.Count} stor{(list.Count == 1 ? "y" : "ies")}" });
                foreach (var s in list) Items.Add(Card(world, s, big: false));
            }
        }
    }

    private static int Weight(Story s) =>
        (s.N.Category == NewsCategory.TitleChange ? 6 : s.N.Category == NewsCategory.TitleDefense ? 4 : 0)
        + (s.AboutMine ? 3 : 0) + (s.AboutMyFighter ? 2 : 0)
        + (s.N.AboutFighter is not null ? 1 : 0) + (QuoteIn(s.N.Body).Length > 0 ? 1 : 0);

    private static string DayTitle(DateOnly today, DateOnly d) =>
        d == today ? $"Today · {d:dddd d MMMM}"
        : d == today.AddDays(-1) ? $"Yesterday · {d:dddd d MMMM}"
        : d.Year == today.Year ? d.ToString("dddd d MMMM") : d.ToString("dddd d MMMM yyyy");

    private static string KeyOf(NewsItem n) => $"{n.Date.DayNumber}|{n.Category}|{n.Promotion.Value}|{n.AboutFighter?.Value}|{n.Headline}";

    private StoryCardModel Card(World world, Story s, bool big)
    {
        var n = s.N;
        var f = n.AboutFighter is { } fid ? world.Get(fid) : null;
        var org = n.Promotion.Value != 0 ? world.Get(n.Promotion) : null;
        var ev = n.AboutEvent is { } eid && world.Events.TryGetValue(eid, out var e) ? e : null;
        string quote = QuoteIn(n.Body);
        string body = quote.Length > 0 ? n.Body.Replace(quote, "").Trim() : n.Body;
        Func<Action<ImageSource>, ImageSource?> pic = f is not null
            ? (big ? _ => Portraits.For(f, world) : ready => Portraits.Thumb(f, world, ready))
            : org is not null ? _ => LogoFactory.ForPromotion(org) : _ => null;
        return new StoryCardModel(pic)
        {
            PictureIsLogo = f is null,
            HasNoPicture = f is null && org is null,
            Kicker = $"{s.KindName.ToUpperInvariant()}  ·  {n.Date:ddd d MMM}",
            Icon = IconOf(s.KindName),
            Headline = n.Headline,
            Body = body,
            Quote = quote,
            AboutPlayer = s.AboutMine,
            FighterId = f?.Id.Value ?? 0,
            FighterName = f is null ? "" : $"{f.FirstName} {f.LastName}",
            PromotionId = org?.Id.Value ?? 0,
            PromotionLabel = org?.Abbreviation ?? "",
            EventId = ev?.Id.Value ?? 0,
            EventName = ev?.Name ?? "",
            OpenCommand = f is not null ? OpenFighterCommand : ev is not null ? OpenEventCommand : org is not null ? OpenOrgCommand : null,
            OpenParameter = f is not null ? f.Id.Value : ev is not null ? ev.Id.Value : org?.Id.Value,
            Key = KeyOf(n) + (big ? "|lead" : ""),
        };
    }

    /// <summary>The first quoted line in a story body, if somebody said something.</summary>
    private static string QuoteIn(string body)
    {
        int a = body.IndexOf('“');
        if (a < 0) return "";
        int b = body.IndexOf('”', a + 1);
        return b > a ? body.Substring(a, b - a + 1) : "";
    }
}
