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
        // The networks on this rung, drawn as their marks. The rung's partner names live inside the sim's
        // Ladder (only spelled out in the terms text), so the logos come from the art manifest's rung
        // category for this kind; the full text stays in the tooltip. (Ask: world-sim to expose the
        // partner names on RungView, then read them from there.)
        var key = r.Kind.ToString();
        Partners = Art.BrandLibrary.Names(Art.BrandKind.Media)
            .Where(n => Art.BrandLibrary.Info(n, Art.BrandKind.Media) is { } b
                        && string.Equals(b.Category.Replace("-", ""), key, StringComparison.OrdinalIgnoreCase))
            .Select(n => new RungPartner(n, deal?.Partner == n))
            .OrderByDescending(p => p.OnAir).ThenBy(p => p.Name, StringComparer.Ordinal).ToList();
        int cut = Terms.IndexOf(" · networks:", StringComparison.Ordinal);
        ShortTerms = Partners.Count > 0 && cut > 0 ? Terms[..cut] : Terms;
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

        foreach (var rung in MediaService.LadderView(_world, promo))
            Ladder.Add(new RungRow(rung, promo));

        HasStream = promo.Stream is not null;
        if (promo.Stream is { } s)
        {
            var (income, ops) = MediaService.StreamMonthly(s);
            StreamHeadline = $"{s.Name} — {s.Subscribers:N0} subscribers";
            StreamDetail = $"{income}/mo subscriptions − {ops}/mo operations = net {income - ops}/mo. "
                + "Subscribers follow the product: they grow with every card aired and melt away when we go dark.";
        }
        else { StreamHeadline = ""; StreamDetail = ""; }

        CanLaunchStream = promo.Stream is null
            && MediaService.LaneOpen(promo, MediaKind.InHouseStream)
            && promo.Balance.Amount >= MediaService.StreamLaunchCost.Amount;
        StreamPitch = promo.Stream is not null ? ""
            : MediaService.LaneOpen(promo, MediaKind.InHouseStream)
                ? $"Launch {promo.Abbreviation}+ for {MediaService.StreamLaunchCost} (plus monthly operations): our own service, " +
                  "every card aired, no network in between. Viable on a war chest or a following — painful on neither."
                : "The streaming lane is taken — a licensing deal occupies it.";
    }
}
