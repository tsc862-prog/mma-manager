using System.Collections.ObjectModel;
using MmaManager.Core.Model;
using MmaManager.Wpf.Mvvm;

namespace MmaManager.Wpf.ViewModels;

/// <summary>One headline in the news feed.</summary>
public sealed class NewsRow
{
    public NewsRow(NewsItem n, bool aboutPlayer)
    {
        When = n.Date.ToString("ddd d MMM yyyy");
        Category = FighterRow.Pretty(n.Category.ToString());
        Headline = n.Headline;
        Body = n.Body;
        AboutPlayer = aboutPlayer;
        FighterLinkId = n.AboutFighter?.Value ?? 0;
        EventLinkId = n.AboutEvent?.Value ?? 0;
    }
    public string When { get; }
    public string Category { get; }
    public string Headline { get; }
    public string Body { get; }
    public bool AboutPlayer { get; }

    /// <summary>Stories link out when the sim stamped who/what they're about.</summary>
    public int FighterLinkId { get; }
    public int EventLinkId { get; }
    public bool HasFighterLink => FighterLinkId != 0;
    public bool HasEventLink => EventLinkId != 0;
}

/// <summary>The world news feed, newest first, filterable by category and by "about my promotion".</summary>
public sealed class NewsViewModel : ObservableObject
{
    private readonly List<NewsRow> _all = new();

    public ObservableCollection<NewsRow> Items { get; } = new();
    public ObservableCollection<string> Categories { get; } = new() { "All stories" };

    private string _selectedCategory = "All stories";
    public string SelectedCategory
    {
        get => _selectedCategory;
        set { if (Set(ref _selectedCategory, value)) Refilter(); }
    }

    private bool _mineOnly;
    public bool MineOnly { get => _mineOnly; set { if (Set(ref _mineOnly, value)) Refilter(); } }

    private string _orgName = "";

    /// <summary>The browser chrome's address line — the site "navigates" as you filter.</summary>
    public string Address =>
        "https://fightwire.net/" + (MineOnly
            ? "search?q=" + Uri.EscapeDataString(_orgName.ToLowerInvariant())
            : SelectedCategory == "All stories" ? "latest"
            : SelectedCategory.ToLowerInvariant().Replace(" ", "-"));

    public void Attach(World world)
    {
        _all.Clear();
        var player = world.PlayerPromotion;
        _orgName = world.Get(player)?.Name ?? "";
        foreach (var n in Enumerable.Reverse(world.News).Take(300))
            _all.Add(new NewsRow(n, n.Promotion == player));

        // Offer only categories that actually have stories.
        var keep = SelectedCategory;
        Categories.Clear();
        Categories.Add("All stories");
        foreach (var c in _all.Select(r => r.Category).Distinct().OrderBy(c => c))
            Categories.Add(c);
        _selectedCategory = Categories.Contains(keep) ? keep : "All stories";
        OnPropertyChanged(nameof(SelectedCategory));

        Refilter();
    }

    private void Refilter()
    {
        Items.Clear();
        foreach (var r in _all)
        {
            if (MineOnly && !r.AboutPlayer) continue;
            if (SelectedCategory != "All stories" && r.Category != SelectedCategory) continue;
            Items.Add(r);
        }
        OnPropertyChanged(nameof(Address));
    }
}
