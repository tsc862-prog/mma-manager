using System.Collections.ObjectModel;
using System.Windows.Media;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Flavor;
using MmaManager.Wpf.Art;
using MmaManager.Wpf.Mvvm;

namespace MmaManager.Wpf.ViewModels;

/// <summary>One event in the master list.</summary>
public sealed class EventListRow
{
    public EventListRow(Event e, World w)
    {
        Id = e.Id.Value;
        Name = e.Name;
        DateText = e.Date.ToString("d MMM yyyy");
        Date = e.Date;
        Bouts = e.Card.Count;
        var promo = w.Get(e.Promotion);
        Org = promo?.Abbreviation ?? "LOCAL";   // nobody's show — the club circuit
        OrgLogo = promo is null ? null : LogoFactory.ForPromotion(promo);
        IsMine = e.Promotion == w.PlayerPromotion;
        IsScheduled = e.Status == EventStatus.Scheduled;

        var rated = e.Card.Where(f => f.Outcome is not null).ToList();
        Stars = rated.Count == 0 ? "" : MatchRating.Stars((int)rated.Average(f => (double)MatchRating.Of(f)));
    }

    public int Id { get; }
    public string Name { get; }
    public string DateText { get; }
    /// <summary>Raw date behind <see cref="DateText"/> so the column sorts chronologically.</summary>
    public DateOnly Date { get; }
    public int Bouts { get; }
    public string Org { get; }
    public ImageSource? OrgLogo { get; }
    public bool IsMine { get; }
    public bool IsScheduled { get; }
    public string Stars { get; }
}

/// <summary>One bout on the event page, in billing order.</summary>
public sealed class EventBoutRow
{
    public EventBoutRow(Fight f, Event e, World w, string slot)
    {
        Slot = slot;
        IsMainEvent = slot == "Main event";
        IsTitle = f.IsTitleFight;
        Division = FighterRow.Pretty(f.WeightClass.ToString());
        var red = w.Get(f.RedCorner);
        var blue = w.Get(f.BlueCorner);
        RedName = red?.FullName ?? "?";
        BlueName = blue?.FullName ?? "?";
        RedId = red?.Id.Value ?? 0;
        BlueId = blue?.Id.Value ?? 0;
        RedPortrait = red is null ? null : Portraits.For(red, w);
        BluePortrait = blue is null ? null : Portraits.For(blue, w);

        if (f.Outcome is not { } o)
        {
            ResultLine = $"Scheduled · {f.ScheduledRounds} rounds";
            return;
        }

        Stars = MatchRating.StarsFor(f);
        RedWon = o.Winner == f.RedCorner;
        BlueWon = o.Winner == f.BlueCorner;
        if (o.Winner is not { } win)
            ResultLine = o.Method == FightMethod.NoContest ? "No Contest" : "Draw";
        else
        {
            string method = FighterRow.Pretty(o.Method.ToString()).ToLowerInvariant() switch
            {
                "knockout" => "KO",
                "technical knockout" => "TKO",
                "submission tap" => "submission",
                var m => m,
            };
            ResultLine = $"{w.Get(win)?.FullName ?? "?"} wins by {method} — R{o.EndRound} {o.EndTimeSeconds / 60}:{o.EndTimeSeconds % 60:00}";

            // A belt changing hands on this very night is the headline of headlines.
            if (f.IsTitleFight && f.Title is { } tid && w.Get(tid) is { } title)
                NewChampion = title.Lineage.Any(r => r.Champion == win && r.WonOn == e.Date);
        }
    }

    public string Slot { get; }
    public bool IsMainEvent { get; }
    public bool IsTitle { get; }
    public string Division { get; }
    public string RedName { get; }
    public string BlueName { get; }
    public int RedId { get; }
    public int BlueId { get; }
    public ImageSource? RedPortrait { get; }
    public ImageSource? BluePortrait { get; }
    public bool RedWon { get; }
    public bool BlueWon { get; }
    public string ResultLine { get; } = "";
    public string Stars { get; } = "";
    public bool NewChampion { get; }
}

/// <summary>
/// The Events screen: every card in the world on the left, and a full EVENT PAGE
/// on the right — the poster, the venue and the box office, and the card bout by
/// bout with results. Your own shows filterable into a browsable history.
/// </summary>
public sealed class EventsViewModel : ObservableObject
{
    private World? _world;

    public ObservableCollection<EventListRow> Events { get; } = new();
    public ObservableCollection<EventBoutRow> Card { get; } = new();

    private bool _mineOnly;
    public bool MineOnly { get => _mineOnly; set { if (Set(ref _mineOnly, value)) RefreshList(); } }

    private EventListRow? _selected;
    public EventListRow? Selected
    {
        get => _selected;
        set { if (Set(ref _selected, value)) BuildDetail(); }
    }

    // --- The event page -----------------------------------------------------
    private bool _hasDetail;
    public bool HasDetail { get => _hasDetail; private set => Set(ref _hasDetail, value); }

    private ImageSource? _poster;
    public ImageSource? Poster { get => _poster; private set => Set(ref _poster, value); }

    private string _detailTitle = "";
    public string DetailTitle { get => _detailTitle; private set => Set(ref _detailTitle, value); }

    private string _detailSub = "";
    public string DetailSub { get => _detailSub; private set => Set(ref _detailSub, value); }

    private string _finLine = "";
    /// <summary>The box office ("5,204 tickets · gate $X · … · net $Y"), or the scheduled note.</summary>
    public string FinLine { get => _finLine; private set => Set(ref _finLine, value); }

    private bool _finInRed;
    public bool FinInRed { get => _finInRed; private set => Set(ref _finInRed, value); }

    public void Attach(World world)
    {
        _world = world;
        RefreshList();
    }

    /// <summary>Open the page for a specific event (jumps from elsewhere in the shell).</summary>
    public void Select(int eventId)
    {
        if (Events.FirstOrDefault(r => r.Id == eventId) is { } row) Selected = row;
        else if (_world?.Events.Values.FirstOrDefault(e => e.Id.Value == eventId) is { } ev
                 && ev.Promotion != _world.PlayerPromotion && MineOnly)
        {
            MineOnly = false;   // it's off the filtered list — widen and retry
            Selected = Events.FirstOrDefault(r => r.Id == eventId);
        }
    }

    private void RefreshList()
    {
        int? keep = Selected?.Id;
        Events.Clear();
        if (_world is null) return;

        IEnumerable<Event> events = _world.Events.Values;
        if (MineOnly) events = events.Where(e => e.Promotion == _world.PlayerPromotion);
        foreach (var e in events.OrderByDescending(e => e.Date).ThenByDescending(e => e.Id.Value).Take(200))
            Events.Add(new EventListRow(e, _world));

        Selected = Events.FirstOrDefault(r => r.Id == keep) ?? Events.FirstOrDefault();
    }

    private void BuildDetail()
    {
        Card.Clear();
        if (_world is null || Selected is null
            || _world.Events.Values.FirstOrDefault(e => e.Id.Value == Selected.Id) is not { } ev)
        {
            HasDetail = false;
            Poster = null;
            DetailTitle = "";
            DetailSub = "";
            FinLine = "";
            return;
        }

        HasDetail = true;
        Poster = PosterFactory.For(_world, ev);
        DetailTitle = ev.Name;

        string where = ev.VenueId is { } vid && _world.Get(vid) is { } v
            ? $"{v.Name}{(_world.Get(v.City) is { } c ? $", {c.Name}" : "")}"
            : ev.Venue ?? FighterRow.Pretty(ev.Region.ToString());
        DetailSub = $"{ev.Date:dddd, d MMMM yyyy}  ·  {where}  ·  {FighterRow.Pretty(ev.Tier.ToString())}"
                    + $"  ·  {_world.Get(ev.Promotion)?.Name ?? "?"}";

        if (ev.Financials is { } fin)
        {
            var net = fin.NetProfit;
            FinInRed = net.Amount < 0;
            FinLine = $"{fin.Attendance:N0} tickets  ·  gate {fin.GateRevenue}"
                      + (fin.PayPerViewBuys > 0 ? $"  ·  {fin.PayPerViewBuys:N0} PPV buys ({fin.PayPerViewRevenue})" : "")
                      + $"  ·  production {fin.ProductionCost}  ·  payroll {fin.FighterPayroll}  ·  net {net}";
        }
        else
        {
            FinInRed = false;
            FinLine = ev.Status == EventStatus.Cancelled
                ? "Cancelled — the card fell apart before the doors opened."
                : $"Scheduled  ·  {ev.Card.Count} bout{(ev.Card.Count == 1 ? "" : "s")} booked";
        }

        // Billing order: the main event tops the page.
        for (int i = ev.Card.Count - 1; i >= 0; i--)
            Card.Add(new EventBoutRow(ev.Card[i], ev, _world, CardBilling.Slot(i, ev.Card.Count)));
    }
}
