using System.Collections.ObjectModel;
using System.Windows.Media;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Org;
using MmaManager.Wpf.Art;
using MmaManager.Wpf.Mvvm;

namespace MmaManager.Wpf.ViewModels;

/// <summary>One promotion in the Organizations master list.</summary>
public sealed class OrgListRow
{
    public OrgListRow(Promotion p, World w, int rank)
    {
        Id = p.Id.Value;
        Logo = LogoFactory.ForPromotion(p);
        Name = p.Name;
        Abbr = p.Abbreviation;
        Tier = OrgSizing.Label(OrgSizing.SizeOf(p));
        Prestige = p.Prestige.Value;
        Region = FighterRow.Pretty(p.BaseRegion.ToString());
        IsPlayer = p.IsPlayerControlled;
        Rank = rank;
    }

    public int Id { get; }
    public ImageSource Logo { get; }
    public string Name { get; }
    public string Abbr { get; }
    public string Tier { get; }
    public int Prestige { get; }
    public string Region { get; }
    public bool IsPlayer { get; }
    public int Rank { get; }
}

/// <summary>One revenue relationship on an org's page (TV, PPV, sponsor, or its own stream).</summary>
public sealed class OrgDealRow
{
    public required string Glyph { get; init; }
    public required string Kind { get; init; }
    public required string Partner { get; init; }
    public required string Terms { get; init; }
}

/// <summary>A marquee name on an org's page.</summary>
public sealed class OrgTopFighter
{
    public required int FighterId { get; init; }
    public required ImageSource Portrait { get; init; }
    public required string Name { get; init; }
    public required string Detail { get; init; }
    public required bool IsChampion { get; init; }
}

/// <summary>
/// The Organizations app: every promotion in the sport on the left, and a full
/// ORG PAGE on the right — level and the climb, the books and the media deals,
/// the belts, the marquee names, and the recent shows.
/// </summary>
public sealed class OrgsViewModel : ObservableObject
{
    private World? _world;

    public ObservableCollection<OrgListRow> Orgs { get; } = new();

    private OrgListRow? _selected;
    public OrgListRow? Selected
    {
        get => _selected;
        set { if (Set(ref _selected, value)) BuildDetail(); }
    }

    // --- The org page -------------------------------------------------------
    private bool _hasDetail;
    public bool HasDetail { get => _hasDetail; private set => Set(ref _hasDetail, value); }

    private ImageSource? _logo;
    public ImageSource? Logo { get => _logo; private set => Set(ref _logo, value); }

    private string _name = "";
    public string Name { get => _name; private set => Set(ref _name, value); }

    private string _tagline = "";
    /// <summary>"International promotion · HQ Las Vegas · #1 of 21 in the world".</summary>
    public string Tagline { get => _tagline; private set => Set(ref _tagline, value); }

    private int _prestige, _reputation, _tierProgress;
    public int Prestige { get => _prestige; private set => Set(ref _prestige, value); }
    public int Reputation { get => _reputation; private set => Set(ref _reputation, value); }
    public int TierProgress { get => _tierProgress; private set => Set(ref _tierProgress, value); }

    private string _tierLine = "";
    /// <summary>The LEVEL, spelled out ("National · 46% to Continental").</summary>
    public string TierLine { get => _tierLine; private set => Set(ref _tierLine, value); }

    private string _balanceText = "";
    public string BalanceText { get => _balanceText; private set => Set(ref _balanceText, value); }

    private string _story = "";
    public string Story { get => _story; private set => Set(ref _story, value); }

    private string _rosterLine = "";
    public string RosterLine { get => _rosterLine; private set => Set(ref _rosterLine, value); }

    private string _rulesLine = "";
    public string RulesLine { get => _rulesLine; private set => Set(ref _rulesLine, value); }

    public ObservableCollection<OrgDealRow> Deals { get; } = new();

    private bool _hasDeals;
    public bool HasDeals { get => _hasDeals; private set => Set(ref _hasDeals, value); }

    public ObservableCollection<BeltRow> Belts { get; } = new();
    public ObservableCollection<OrgTopFighter> TopFighters { get; } = new();
    public ObservableCollection<EventListRow> RecentEvents { get; } = new();

    public void Attach(World world)
    {
        _world = world;
        int? keep = Selected?.Id;
        Orgs.Clear();

        int rank = 0;
        foreach (var p in world.Promotions.Values.Where(p => !p.Defunct).OrderByDescending(p => p.Prestige.Value))
            Orgs.Add(new OrgListRow(p, world, ++rank));

        Selected = Orgs.FirstOrDefault(o => o.Id == keep)
                   ?? Orgs.FirstOrDefault(o => o.IsPlayer)
                   ?? Orgs.FirstOrDefault();
    }

    /// <summary>Open a specific promotion's page (jumps from anywhere in the shell).</summary>
    public void Select(int promotionId)
    {
        if (Orgs.FirstOrDefault(o => o.Id == promotionId) is { } row) Selected = row;
    }

    private void BuildDetail()
    {
        Deals.Clear(); Belts.Clear(); TopFighters.Clear(); RecentEvents.Clear();
        if (_world is null || Selected is null || _world.Get(new PromotionId(Selected.Id)) is not { } p)
        {
            HasDetail = false;
            return;
        }

        HasDetail = true;
        Logo = LogoFactory.ForPromotion(p);
        Name = p.Name;

        string hq = p.HomeCity is { } hc && _world.Get(hc) is { } city ? city.Name : FighterRow.Pretty(p.BaseRegion.ToString());
        Tagline = $"{OrgSizing.Label(OrgSizing.SizeOf(p))} promotion  ·  HQ {hq}  ·  #{Selected.Rank} of {Orgs.Count} in the world";

        Prestige = p.Prestige.Value;
        Reputation = p.Reputation.Value;
        var standing = OrgSizing.Progress(p);
        TierProgress = standing.Percent;
        TierLine = standing.Next is { } next
            ? $"LEVEL: {OrgSizing.Label(standing.Tier)}  ·  {standing.Percent}% of the way to {OrgSizing.Label(next)}"
            : $"LEVEL: {OrgSizing.Label(standing.Tier)}  ·  the top of the sport";
        BalanceText = p.Balance.ToString();
        Story = Core.Sim.Flavor.Biographer.Promotion(_world, p);
        RulesLine = $"House rules: {p.Rules.RoundMinutes}-minute rounds  ·  {p.Rules.StandardRounds}-round bouts  ·  {p.Rules.TitleRounds}-round title fights";

        var roster = _world.RosterOf(p.Id).Where(f => f.Status != FighterStatus.Retired).ToList();
        RosterLine = $"{roster.Count} fighters under contract  ·  {p.Divisions.Count} divisions";

        // Money & media: TV and PPV rights, sponsors, and their own platform.
        foreach (var d in p.MediaDeals.OrderByDescending(d => d.RightsFeePerEvent.Amount))
            Deals.Add(new OrgDealRow
            {
                Glyph = "📺", Kind = FighterRow.Pretty(d.Kind.ToString()), Partner = d.Partner,
                Terms = $"{d.RightsFeePerEvent} / event  ·  {d.MonthsRemaining} months left",
            });
        foreach (var d in p.SponsorDeals.OrderByDescending(d => d.FeePerEvent.Amount))
            Deals.Add(new OrgDealRow
            {
                Glyph = "💼", Kind = "Sponsorship", Partner = d.Partner,
                Terms = $"{d.FeePerEvent} / event  ·  {d.EventsRemaining} events left",
            });
        if (p.Stream is { } st)
            Deals.Add(new OrgDealRow
            {
                Glyph = "📡", Kind = "Streaming platform", Partner = st.Name,
                Terms = $"{st.Subscribers:N0} subscribers  ·  launched {st.LaunchedOn:MMM yyyy}",
            });
        HasDeals = Deals.Count > 0;

        // The belts.
        foreach (var d in p.Divisions.OrderBy(d => d.Sex).ThenByDescending(d => d.WeightClass))
            if (d.Title is { } tid && _world.Get(tid) is { } title)
                Belts.Add(new BeltRow(title, d, _world));

        // The marquee: best names on the books.
        foreach (var f in roster.OrderByDescending(FighterAbility.CurrentAbility).Take(6))
            TopFighters.Add(new OrgTopFighter
            {
                FighterId = f.Id.Value,
                Portrait = Portraits.For(f, _world),
                Name = f.FullName,
                Detail = $"{FighterRow.DivisionLabel(f.WeightClass, f.Sex)}  ·  {f.Record}  ·  Sct {FighterRow.Scouted(_world, f)}",
                IsChampion = _world.Titles.Values.Any(t => t.CurrentChampion == f.Id),
            });

        // The recent shows (click through to the event pages).
        foreach (var ev in _world.Events.Values
                     .Where(e => e.Promotion == p.Id && e.Status == EventStatus.Completed)
                     .OrderByDescending(e => e.Date).Take(8))
            RecentEvents.Add(new EventListRow(ev, _world));
    }
}
