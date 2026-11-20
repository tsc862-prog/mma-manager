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
/// The trade a streaming giant's offer puts to the player while his own platform runs, in the desk's words:
/// what the platform nets a quarter, what the deal should pay a quarter on his last year of cards, the
/// difference, what signing closes, and (when his record says he'd miss the deal's terms) why. Every number
/// is the sim's (<see cref="MediaService.TradeFor"/>); nothing here is computed on the side.
/// </summary>
public sealed class PlatformTradeView
{
    public PlatformTradeView(MediaService.PlatformTrade t, DealOffer offer)
    {
        Platform = t.Platform;
        Partner = offer.Partner;
        PlatformNet = MediaMoney.Signed(t.PlatformPerQuarter);
        PlatformUp = t.PlatformPerQuarter >= 0;
        OfferPays = MediaMoney.Plain(t.OfferPerQuarter);
        Gain = MediaMoney.Signed(t.GainPerQuarter);
        GainUp = t.GainPerQuarter >= 0;
        var rec = t.Record;
        Lede = rec.Cards == 0
            ? $"{Platform} nets {MediaMoney.Plain(t.PlatformPerQuarter)} a quarter. You haven't run a card in the last year, so there's no record to say what {Partner} would pay."
            : $"On your last {Plural(rec.Cards, "card")}, {Partner} would pay about {OfferPays} a quarter. "
              + (GainUp ? $"That's {MediaMoney.Plain(t.GainPerQuarter)} more than {Platform} brings in."
                        : $"That's {MediaMoney.Plain(-t.GainPerQuarter)} less than {Platform} brings in.");
        CloseLine = $"Signing closes {Platform}. Its {t.Subscribers:N0} subscribers go, and you can't relaunch while the deal holds the streaming lane.";

        // Why his record says he'd miss the terms (the bar the sim holds an AI house to), in plain words.
        var lines = new List<string>();
        int? bar = offer.Requirements.FirstOrDefault(r => r.Kind == DealRequirementKind.MinStarPower)?.Threshold;
        int? cadence = offer.Requirements.FirstOrDefault(r => r.Kind == DealRequirementKind.EventCadence)?.Threshold;
        if (!t.RecordKeepsTerms)
        {
            if (rec.Cards == 0)
                lines.Add("No card in the last year: there's nothing to show you can keep their terms.");
            else
            {
                int missed = rec.Cards - rec.Cleared;
                if (bar is { } b && missed > 0)
                    lines.Add($"{missed} of your last {Plural(rec.Cards, "main event")} would have missed their star clause (a headliner at popularity {b}+).");
                if (!rec.KeepsCadence && cadence is { } c)
                    lines.Add($"You run a card about every {Math.Round((double)rec.Days / rec.Cards):0} days; they want one at least every {c}.");
            }
            lines.Add($"Every card that misses is paid at half and earns a strike. At {MediaService.StrikeLimit} strikes they walk.");
        }
        Warning = string.Join(" ", lines);
    }

    private static string Plural(int n, string what) => n == 1 ? $"1 {what}" : $"{n} {what}s";

    public string Platform { get; }
    public string Partner { get; }
    /// <summary>What the platform nets a quarter (signed).</summary>
    public string PlatformNet { get; }
    public bool PlatformUp { get; }
    /// <summary>What the deal should pay a quarter on his recent record.</summary>
    public string OfferPays { get; }
    /// <summary>The difference, signed: what signing is worth a quarter.</summary>
    public string Gain { get; }
    public bool GainUp { get; }
    public string Lede { get; }
    public string CloseLine { get; }
    /// <summary>Empty when his record keeps the terms.</summary>
    public string Warning { get; }
    public bool HasWarning => Warning.Length > 0;
}

/// <summary>What shutting the player's platform down costs, for the inline confirmation (<see cref="MediaService.PreviewShutdown"/>).</summary>
public sealed class ShutdownView
{
    public ShutdownView(MediaService.ShutdownPreview p, Promotion promo)
    {
        Platform = p.Platform;
        Subscribers = $"{p.Subscribers:N0}";
        NetPerQuarter = MediaMoney.Signed(p.NetPerQuarter);
        NetUp = p.NetPerQuarter >= 0;
        Relaunch = p.RelaunchCost.ToString();
        Lede = $"{Platform} goes dark today and every subscriber goes with it. Its running costs stop this month.";
        var others = promo.MediaDeals.Select(d => d.Partner).Distinct().ToList();
        Reach = p.OtherScreen
            ? $"Your cards still air on {string.Join(" and ", others)}, so that's the reach you keep."
            : "With no other screen, your fights would reach only the people in the room.";
        RelaunchLine = $"Coming back means paying {Relaunch} again and starting from zero subscribers.";
    }

    public string Platform { get; }
    /// <summary>The confirming button says exactly what it does.</summary>
    public string ConfirmLabel => $"Shut down {Platform}";
    public string Subscribers { get; }
    public string NetPerQuarter { get; }
    public bool NetUp { get; }
    public string Relaunch { get; }
    public string Lede { get; }
    public string Reach { get; }
    public string RelaunchLine { get; }
}

internal static class MediaMoney
{
    public static string Plain(decimal d) => new Money(Math.Round(d)).ToString();
    /// <summary>"+$1,200" / "−$300" (a real minus sign).</summary>
    public static string Signed(decimal d) => (d < 0 ? "−" : "+") + new Money(Math.Abs(Math.Round(d))).ToString();
}

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
    /// <summary>Opens the shutdown confirmation on the platform's panel (nothing closes yet).</summary>
    public System.Windows.Input.ICommand AskShutdownCommand { get; }
    public System.Windows.Input.ICommand CancelShutdownCommand { get; }
    /// <summary>Shuts the platform down (only from the confirmation).</summary>
    public System.Windows.Input.ICommand ShutdownCommand { get; }

    /// <summary>Ask before something costly (heading, body, detail, confirm label, keep label); null = go ahead (harness runs).</summary>
    public Func<string, string, string?, string, string, bool>? Confirm { get; set; }
    /// <summary>Something here moved the money or the screens: the whole app refreshes (the desk with it).</summary>
    public Action? OnChanged { get; set; }
    /// <summary>A line for the shell's result strip.</summary>
    public Action<string>? Said { get; set; }

    private ShutdownView? _shutdown;
    /// <summary>The shutdown confirmation, when it's open; null otherwise.</summary>
    public ShutdownView? Shutdown { get => _shutdown; private set { if (Set(ref _shutdown, value)) OnPropertyChanged(nameof(ShowShutdown)); } }
    public bool ShowShutdown => _shutdown is not null;

    private string _shutdownLabel = "";
    /// <summary>"Shut down {platform}…" — the quiet action on the platform's panel.</summary>
    public string ShutdownLabel { get => _shutdownLabel; private set => Set(ref _shutdownLabel, value); }

    public MediaViewModel()
    {
        AcceptOfferCommand = new RelayCommand<OfferRow>(row =>
        {
            if (_world?.Get(_world.PlayerPromotion) is not { } promo || row.Offer.Media is null) return;
            if (!promo.Offers.Contains(row.Offer)) { Refresh(); return; }   // lapsed or answered elsewhere since the row was drawn
            // A streaming offer while his own platform runs: signing closes it. Say so once, with the sim's numbers.
            if (Confirm is not null && SignQuestion(row.Offer) is { } q
                && !Confirm(q.Heading, q.Body, q.Detail, q.Confirm, q.Keep)) return;
            string? closed = promo.Stream?.Name;
            var deal = MediaService.Accept(_world, promo, row.Offer, _world.CurrentDate);
            Said?.Invoke(closed is not null && promo.Stream is null
                ? $"Signed with {deal.Partner}. {closed} is shut down; your cards stream on {deal.Partner} from the next one."
                : $"Signed with {deal.Partner}: {MediaService.Label(deal.Kind)} for {deal.MonthsRemaining} months.");
            Changed();
        });
        DeclineOfferCommand = new RelayCommand<OfferRow>(row =>
        {
            if (_world?.Get(_world.PlayerPromotion) is not { } promo) return;
            bool trade = MediaService.TradeFor(_world, promo, row.Offer) is not null;
            MediaService.Decline(promo, row.Offer);
            Said?.Invoke(trade && promo.Stream is { } s
                ? $"Passed on {row.Offer.Partner}. {s.Name} keeps running."
                : $"Passed on {row.Offer.Partner}.");
            Changed();
        });
        LaunchStreamCommand = new RelayCommand(() =>
        {
            if (_world?.Get(_world.PlayerPromotion) is not { } promo) return;
            if (MediaService.LaunchStream(_world, promo, _world.CurrentDate))
                Said?.Invoke($"{promo.Stream?.Name} is live: every card airs on it from now on.");
            Changed();
        });
        AskShutdownCommand = new RelayCommand(() =>
        {
            if (_world?.Get(_world.PlayerPromotion) is not { } promo) return;
            Shutdown = MediaService.PreviewShutdown(promo) is { } p ? new ShutdownView(p, promo) : null;
        });
        CancelShutdownCommand = new RelayCommand(() => Shutdown = null);
        ShutdownCommand = new RelayCommand(() =>
        {
            if (_world?.Get(_world.PlayerPromotion) is not { } promo) return;
            string? name = promo.Stream?.Name;
            Shutdown = null;
            if (MediaService.ShutDownPlatform(_world, promo, _world.CurrentDate))
                Said?.Invoke($"{name} is shut down. The subscribers are gone and the running costs stop this month.");
            Changed();
        });
    }

    /// <summary>
    /// The one question before signing a streaming offer that closes his own platform (heading, body, detail, the
    /// confirming and the keeping button); null for any other offer, which signs without asking.
    /// </summary>
    public (string Heading, string Body, string Detail, string Confirm, string Keep)? SignQuestion(DealOffer offer)
    {
        if (_world?.Get(_world.PlayerPromotion) is not { } promo || MediaService.TradeFor(_world, promo, offer) is not { } t) return null;
        var view = new PlatformTradeView(t, offer);
        string body = $"{t.Platform} goes dark the day you sign, and its {t.Subscribers:N0} subscribers go with it. "
            + $"{offer.Partner} pays {offer.FeePerEvent} an event for {offer.Term} months.";
        string detail = $"{t.Platform} nets {view.PlatformNet} a quarter now; on your last year of cards the deal pays about {view.OfferPays} ({view.Gain} a quarter). "
            + "You can't relaunch a platform while the deal holds the streaming lane."
            + (view.HasWarning ? "\n\n" + view.Warning : "");
        return ($"Sign with {offer.Partner} and close {t.Platform}?", body, detail, $"Sign and close {t.Platform}", $"Keep {t.Platform}");
    }

    private void Changed()
    {
        if (OnChanged is { } all) all();   // the whole app (the desk refreshes with it)
        else Refresh();
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
            Offers.Add(new OfferRow(o, MediaService.TradeFor(_world, promo, o) is { } t ? new PlatformTradeView(t, o) : null));

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
        ShutdownLabel = promo.Stream is { } live ? $"Shut down {live.Name}…" : "";
        // An open confirmation is re-read from the sim (a week may have moved the numbers), or closed with the platform.
        if (Shutdown is not null)
            Shutdown = MediaService.PreviewShutdown(promo) is { } preview ? new ShutdownView(preview, promo) : null;

        CanLaunchStream = promo.Stream is null && streams
            && MediaService.LaneOpen(promo, MediaKind.InHouseStream)
            && promo.Balance.Amount >= MediaService.StreamLaunchCost.Amount;
        StreamPitch = promo.Stream is not null ? ""
            : !streams ? $"{promo.Abbreviation}+ will have to wait: nobody streams fights in {_world.CurrentDate.Year}. Your nights sell in the room and, if a carrier takes them, on pay-per-view."
            : MediaService.LaneOpen(promo, MediaKind.InHouseStream)
                ? $"Launch {promo.Abbreviation}+ for {MediaService.StreamLaunchCost} (plus monthly operations): our own service, " +
                  "every card aired, no network in between. Viable on a war chest or a following — painful on neither."
                : MediaService.DealIn(promo, MediaLane.Streaming) is { } holder
                    ? $"{holder.Partner} holds our streaming rights for {holder.MonthsRemaining} more months. A platform of our own can come back when that deal ends."
                    : "The streaming lane is taken — a licensing deal occupies it.";
    }
}
