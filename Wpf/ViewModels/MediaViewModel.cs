using System.Collections.ObjectModel;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Finance;
using MmaManager.Wpf.Mvvm;

namespace MmaManager.Wpf.ViewModels;

/// <summary>One rung of the media ladder, judged for the player's promotion.</summary>
public sealed class RungRow
{
    public RungRow(MediaService.RungView r, Promotion promo)
    {
        Label = r.Label;
        var deal = promo.MediaDeals.FirstOrDefault(d => d.Kind == r.Kind);
        Signed = deal is not null;
        Qualified = r.Qualified;
        Status = deal is not null ? $"ON AIR — {deal.Partner}"
            : promo.Offers.Any(o => o.Media == r.Kind) ? "offer on the table"
            : r.Qualified ? "qualified — waiting on a call"
            : r.Gate;
        Fee = r.Kind == MediaKind.PpvCarrier ? "50–60% of buys" : $"~{r.Fee}/ev";
        Terms = r.Terms;
        // The networks on this rung, drawn as their marks (a name tile when one has no logo). The SIM is
        // the source: RungView has no structured partner list yet, so the names are read from where the
        // sim spells them today — the terms text, " · networks: A (tag), B (tag) — terms and money vary
        // by house". (Ask: world-sim to expose RungView.Partners, then read that instead.) If the text
        // can't be read, no marks are shown and the full terms stay as they were.
        int cut = Terms.IndexOf(NetworksMark, StringComparison.Ordinal);
        Partners = PartnersIn(Terms, cut)
            .Select(n => new RungPartner(n, deal?.Partner == n))
            .OrderByDescending(p => p.OnAir).ToList();
        ShortTerms = Partners.Count > 0 && cut > 0 ? Terms[..cut] : Terms;
    }

    private const string NetworksMark = " · networks: ";
    private const string NetworksEnd = " — terms and money vary by house";

    // "Combat One (standard), Octagon TV (flagship)" -> ["Combat One", "Octagon TV"], in the sim's order.
    private static List<string> PartnersIn(string terms, int at)
    {
        if (at < 0) return new List<string>();
        string list = terms[(at + NetworksMark.Length)..];
        int end = list.IndexOf(NetworksEnd, StringComparison.Ordinal);
        if (end >= 0) list = list[..end];
        return list.Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.EndsWith(')') && p.LastIndexOf(" (", StringComparison.Ordinal) is > 0 and var open ? p[..open] : p)
            .Where(n => n.Length > 0).ToList();
    }
    public string Label { get; }
    public bool Signed { get; }
    public bool Qualified { get; }
    public string Status { get; }
    public string Fee { get; }
    public string Terms { get; }
    /// <summary>The terms without the list of network names (the marks show those).</summary>
    public string ShortTerms { get; }
    public IReadOnlyList<RungPartner> Partners { get; }
    public bool HasPartners => Partners.Count > 0;
}

/// <summary>A network on a rung of the ladder (its exact name), and whether it airs us.</summary>
public sealed record RungPartner(string Name, bool OnAir);

/// <summary>
/// The Media desk: who airs the promotion (one deal per lane — linear TV, a
/// streaming home, the PPV pipe), the offers on the table, the in-house
/// platform's books, and the ladder of screens still to climb. Networks
/// pitch once — sign them here or watch them move on.
/// </summary>
public sealed class MediaViewModel : ObservableObject
{
    private World? _world;

    public ObservableCollection<MediaRow> Deals { get; } = new();
    public ObservableCollection<OfferRow> Offers { get; } = new();
    public ObservableCollection<RungRow> Ladder { get; } = new();

    private bool _showDealTable;
    /// <summary>Our deals as the dense sortable table instead of logo rows (kept for the session).</summary>
    public bool ShowDealTable { get => _showDealTable; set => Set(ref _showDealTable, value); }

    /// <summary>The desk's one-line brief (today's lanes, or what a 1993 promoter can actually sell).</summary>
    public string Headline { get; private set; } = Today;
    private const string Today = "One deal per screen: a linear TV home, a streaming home, and the pay-per-view pipe. Networks pitch once — sign them or watch them move on.";

    /// <summary>The screens the sport hasn't got yet (an Origins world), in plain words; empty today.</summary>
    public string NotYetLine { get; private set; } = "";
    public bool HasNotYet => NotYetLine.Length > 0;

    private bool _hasStream;
    public bool HasStream { get => _hasStream; private set => Set(ref _hasStream, value); }

    private string _streamHeadline = "";
    public string StreamHeadline { get => _streamHeadline; private set => Set(ref _streamHeadline, value); }

    private string _streamDetail = "";
    public string StreamDetail { get => _streamDetail; private set => Set(ref _streamDetail, value); }

    private bool _canLaunchStream;
    public bool CanLaunchStream { get => _canLaunchStream; private set => Set(ref _canLaunchStream, value); }

    private string _streamPitch = "";
    public string StreamPitch { get => _streamPitch; private set => Set(ref _streamPitch, value); }

    public System.Windows.Input.ICommand AcceptOfferCommand { get; }
    public System.Windows.Input.ICommand DeclineOfferCommand { get; }
    public System.Windows.Input.ICommand LaunchStreamCommand { get; }

    public MediaViewModel()
    {
        AcceptOfferCommand = new RelayCommand<OfferRow>(row =>
        {
            if (_world?.Get(_world.PlayerPromotion) is not { } promo || row.Offer.Media is null) return;
            MediaService.Accept(_world, promo, row.Offer, _world.CurrentDate);
            Refresh();
        });
        DeclineOfferCommand = new RelayCommand<OfferRow>(row =>
        {
            if (_world?.Get(_world.PlayerPromotion) is not { } promo) return;
            promo.Offers.Remove(row.Offer);
            Refresh();
        });
        LaunchStreamCommand = new RelayCommand(() =>
        {
            if (_world?.Get(_world.PlayerPromotion) is not { } promo) return;
            MediaService.LaunchStream(_world, promo, _world.CurrentDate);
            Refresh();
        });
    }

    private static string Join(IReadOnlyList<string> items) => items.Count switch
    {
        0 => "",
        1 => items[0],
        _ => string.Join(", ", items.Take(items.Count - 1)) + " or " + items[^1],
    };

    public void Attach(World world)
    {
        _world = world;
        Refresh();
    }

    private void Refresh()
    {
        Deals.Clear(); Offers.Clear(); Ladder.Clear();
        if (_world?.Get(_world.PlayerPromotion) is not { } promo) return;

        if (promo.Stream is { } stream) Deals.Add(new MediaRow(stream));
        foreach (var d in promo.MediaDeals.OrderByDescending(d => d.RightsFeePerEvent.Amount))
            Deals.Add(new MediaRow(d, promo));

        foreach (var o in promo.Offers.Where(o => o.Media is not null).OrderBy(o => o.ExpiresOn))
            Offers.Add(new OfferRow(o));

        // Only the screens the sport has (every one, today); the rest are named once, as not there yet.
        var missing = new List<string>();
        foreach (var rung in MediaService.LadderView(_world, promo))
            if (Core.Sim.Living.Era.AllowsMedia(_world, rung.Kind)) Ladder.Add(new RungRow(rung, promo));
            else missing.Add(rung.Label);
        bool streams = Core.Sim.Living.Era.AllowsMedia(_world, MediaKind.InHouseStream);
        if (!streams && !missing.Contains(MediaService.Label(MediaKind.InHouseStream))) missing.Add(MediaService.Label(MediaKind.InHouseStream));
        NotYetLine = missing.Count == 0 ? ""
            : $"The sport hasn't got {Join(missing.Select(m => m.ToLowerInvariant()).ToList())} yet. When it does, the offers start here.";
        Headline = _world.Mode == WorldMode.Modern ? Today
            : Ladder.Count == 0 ? "No screen carries the sport yet. A night is sold to the people in the room."
            : $"What the sport has for now: {Join(Ladder.Select(r => r.Label).ToList())}. Carriers pitch once — sign them or watch them move on.";
        OnPropertyChanged(nameof(Headline)); OnPropertyChanged(nameof(NotYetLine)); OnPropertyChanged(nameof(HasNotYet));

        HasStream = promo.Stream is not null;
        if (promo.Stream is { } s)
        {
            var (income, ops) = MediaService.StreamMonthly(s);
            StreamHeadline = $"{s.Name} — {s.Subscribers:N0} subscribers";
            StreamDetail = $"{income}/mo subscriptions − {ops}/mo operations = net {income - ops}/mo. "
                + "Subscribers follow the product: they grow with every card aired and melt away when we go dark.";
        }
        else { StreamHeadline = ""; StreamDetail = ""; }

        CanLaunchStream = promo.Stream is null && streams
            && MediaService.LaneOpen(promo, MediaKind.InHouseStream)
            && promo.Balance.Amount >= MediaService.StreamLaunchCost.Amount;
        StreamPitch = promo.Stream is not null ? ""
            : !streams ? $"{promo.Abbreviation}+ will have to wait: nobody streams fights in {_world.CurrentDate.Year}. Your nights sell in the room and, if a carrier takes them, on pay-per-view."
            : MediaService.LaneOpen(promo, MediaKind.InHouseStream)
                ? $"Launch {promo.Abbreviation}+ for {MediaService.StreamLaunchCost} (plus monthly operations): our own service, " +
                  "every card aired, no network in between. Viable on a war chest or a following — painful on neither."
                : "The streaming lane is taken — a licensing deal occupies it.";
    }
}
