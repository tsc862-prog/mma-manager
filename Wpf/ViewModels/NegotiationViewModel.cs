using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Wpf.Art;
using MmaManager.Wpf.Mvvm;

namespace MmaManager.Wpf.ViewModels;

/// <summary>One line of the talks transcript. Tone: "You", "Agent", "Info".</summary>
public sealed record TranscriptRow(string Who, string Line, string Tone);

/// <summary>The agent's read on one term. Feel: "Pleased", "Close", "Sore".</summary>
public sealed record ReactionRow(string Term, string Note, string Feel);

/// <summary>
/// The negotiating room: a modal sit-down with a fighter's agent driven by a
/// <see cref="NegotiationSession"/>. The player edits an offer against the
/// agent's live position, tables it, and reads the room — per-term reactions,
/// mood, patience and a running transcript — until someone signs or walks.
/// </summary>
public sealed class NegotiationViewModel : ObservableObject
{
    private readonly World _world;
    private readonly Fighter _fighter;
    private readonly NegotiationSession _session;

    /// <summary>Set when the agent accepted — the deal for the caller to sign.</summary>
    public ContractOffer? SignedOffer { get; private set; }

    /// <summary>Raised when the room is done with the window (true = deal struck).</summary>
    public event Action<bool>? CloseRequested;

    public NegotiationViewModel(World world, Fighter fighter, Promotion promo, ContractExpectations exp,
        NegotiationSession session, bool renewal)
    {
        _world = world;
        _fighter = fighter;
        _session = session;

        Name = fighter.FullName;
        Portrait = Portraits.For(fighter, world);
        Sub = $"Scouted {FighterRow.Scouted(world, fighter)} · {FighterRow.DivisionLabel(fighter.WeightClass, fighter.Sex)}"
            + $" · {fighter.Record} · {FreeAgentRow.HometownOf(fighter, world)}"
            + (renewal ? " · renewal talks" : " · free agent");
        StanceLine = $"Across the table: {session.AgentStance}.";

        // Seed the form as an opening BID a notch under their position — the
        // player haggles up from here (or hits "Meet their terms" to end it).
        bool exclusive = Negotiator.WantsExclusivity(promo);
        _offerExclusive = exclusive;
        _offerFights = session.DemandFights;
        _offerPurse = RoundTo(session.DemandPurse(exclusive, false).Amount * 0.85m);
        _offerWinBonus = RoundTo(session.DemandWinBonus(exclusive, false).Amount * 0.85m);
        _offerSigningBonus = session.WantsSigningBonus ? session.DemandSigning(exclusive, false).Amount : 0m;

        Transcript.Add(new("Agent", session.OpeningLine, "Agent"));

        TableOfferCommand = new RelayCommand(TableOffer, () => CanTalk);
        MeetTermsCommand = new RelayCommand(MeetTerms, () => CanTalk);
        SplitCommand = new RelayCommand(SplitTheDifference, () => CanTalk);
        WalkAwayCommand = new RelayCommand(WalkAway, () => CanTalk);

        RefreshTable();
    }

    public ICommand TableOfferCommand { get; }
    public ICommand MeetTermsCommand { get; }
    public ICommand SplitCommand { get; }
    public ICommand WalkAwayCommand { get; }

    public string Name { get; }
    public string Sub { get; }
    public string StanceLine { get; }
    public ImageSource? Portrait { get; }

    public ObservableCollection<TranscriptRow> Transcript { get; } = new();
    public ObservableCollection<ReactionRow> Reactions { get; } = new();

    public bool CanTalk => _session.Outcome == TalkOutcome.Open;

    // --- The room's temperature ----------------------------------------------
    public int Mood => Math.Max(0, _session.Mood);
    public string MoodLabel => _session.MoodLabel;
    /// <summary>Rounds of goodwill left, drawn as dots ("● ● ○").</summary>
    public string PatienceText => string.Join(" ",
        Enumerable.Repeat("●", Math.Max(0, _session.Patience))
            .Concat(Enumerable.Repeat("○", Math.Max(0, 4 - Math.Max(0, _session.Patience)))));

    // --- Your offer -------------------------------------------------------------
    private decimal _offerPurse, _offerWinBonus, _offerSigningBonus, _offerPerfBonus;
    private int _offerFights, _offerPpvPoints;
    private bool _offerExclusive, _offerChampionsClause;

    public decimal OfferPurse { get => _offerPurse; set { if (Set(ref _offerPurse, value)) RefreshTotals(); } }
    public decimal OfferWinBonus { get => _offerWinBonus; set { if (Set(ref _offerWinBonus, value)) RefreshTotals(); } }
    public decimal OfferSigningBonus { get => _offerSigningBonus; set { if (Set(ref _offerSigningBonus, value)) RefreshTotals(); } }
    public decimal OfferPerfBonus { get => _offerPerfBonus; set { if (Set(ref _offerPerfBonus, value)) RefreshTotals(); } }
    public int OfferFights { get => _offerFights; set { if (Set(ref _offerFights, value)) RefreshTotals(); } }
    public int OfferPpvPoints { get => _offerPpvPoints; set { if (Set(ref _offerPpvPoints, value)) RefreshTotals(); } }
    public bool OfferExclusive { get => _offerExclusive; set { if (Set(ref _offerExclusive, value)) RefreshTable(); } }
    public bool OfferChampionsClause { get => _offerChampionsClause; set { if (Set(ref _offerChampionsClause, value)) RefreshTable(); } }

    // --- Their position ---------------------------------------------------------
    private string _theirPurse = "", _theirWinBonus = "", _theirSigning = "", _theirFights = "";
    public string TheirPurse { get => _theirPurse; private set => Set(ref _theirPurse, value); }
    public string TheirWinBonus { get => _theirWinBonus; private set => Set(ref _theirWinBonus, value); }
    public string TheirSigning { get => _theirSigning; private set => Set(ref _theirSigning, value); }
    public string TheirFights { get => _theirFights; private set => Set(ref _theirFights, value); }

    private string _offerTotalLine = "";
    public string OfferTotalLine { get => _offerTotalLine; private set => Set(ref _offerTotalLine, value); }

    // --- Actions -----------------------------------------------------------------

    private ContractOffer BuildOffer() => new()
    {
        Fighter = _fighter.Id,
        Promotion = _world.PlayerPromotion,
        PursePerFight = new Money(Math.Max(0, OfferPurse)),
        WinBonus = new Money(Math.Max(0, OfferWinBonus)),
        SigningBonus = new Money(Math.Max(0, OfferSigningBonus)),
        PerformanceBonus = new Money(Math.Max(0, OfferPerfBonus)),
        PpvPoints = Math.Max(0, OfferPpvPoints),
        Fights = Math.Max(1, OfferFights),
        Exclusive = OfferExclusive,
        ChampionsClause = OfferChampionsClause,
        Round = _session.Round,
    };

    private void TableOffer()
    {
        var offer = BuildOffer();
        Transcript.Add(new("You",
            $"Tabled {offer.PursePerFight}/fight + {offer.WinBonus} to win, {offer.SigningBonus} up front, {offer.Fights} fights"
            + (offer.PpvPoints > 0 ? $", {offer.PpvPoints} PPV pts" : "")
            + (offer.Exclusive ? ", exclusive" : ", non-exclusive")
            + (offer.ChampionsClause ? ", champion's clause" : "") + ".", "You"));

        var resp = _session.Table(offer);
        Reactions.Clear();
        foreach (var r in resp.Reactions)
            Reactions.Add(new(r.Term, r.Note, r.Feel.ToString()));
        Transcript.Add(new("Agent", resp.Line, resp.Verdict == TalkVerdict.Accept ? "Good" : "Agent"));
        RefreshTable();

        if (resp.Verdict == TalkVerdict.Accept)
        {
            SignedOffer = offer;
            CloseRequested?.Invoke(true);
        }
        else if (resp.Verdict == TalkVerdict.WalkOut)
        {
            Transcript.Add(new("Info", "The talks are dead — their camp needs time to cool off.", "Info"));
        }
    }

    // Load their current position into the form and table it — a guaranteed close.
    private void MeetTerms()
    {
        var theirs = _session.TheirTermsAsOffer(OfferExclusive, OfferChampionsClause);
        _offerPurse = theirs.PursePerFight.Amount;
        _offerWinBonus = theirs.WinBonus.Amount;
        _offerSigningBonus = theirs.SigningBonus.Amount;
        _offerFights = theirs.Fights;
        OnPropertyChanged(nameof(OfferPurse));
        OnPropertyChanged(nameof(OfferWinBonus));
        OnPropertyChanged(nameof(OfferSigningBonus));
        OnPropertyChanged(nameof(OfferFights));
        TableOffer();
    }

    // Halve the gap on every money lever; take their length. Doesn't table —
    // the player still gets to look at it first.
    private void SplitTheDifference()
    {
        OfferPurse = RoundTo((OfferPurse + _session.DemandPurse(OfferExclusive, OfferChampionsClause).Amount) / 2m);
        OfferWinBonus = RoundTo((OfferWinBonus + _session.DemandWinBonus(OfferExclusive, OfferChampionsClause).Amount) / 2m);
        if (_session.WantsSigningBonus)
            OfferSigningBonus = RoundTo(Math.Max(OfferSigningBonus,
                (OfferSigningBonus + _session.DemandSigning(OfferExclusive, OfferChampionsClause).Amount) / 2m));
        OfferFights = _session.DemandFights;
    }

    private void WalkAway()
    {
        _session.WalkAway();
        CloseRequested?.Invoke(false);
    }

    // --- Refresh ------------------------------------------------------------------

    private void RefreshTable()
    {
        TheirPurse = _session.DemandPurse(OfferExclusive, OfferChampionsClause).ToString();
        TheirWinBonus = _session.DemandWinBonus(OfferExclusive, OfferChampionsClause).ToString();
        TheirSigning = _session.WantsSigningBonus
            ? _session.DemandSigning(OfferExclusive, OfferChampionsClause).ToString()
            : "not needed";
        TheirFights = $"{_session.DemandFights}";
        OnPropertyChanged(nameof(Mood));
        OnPropertyChanged(nameof(MoodLabel));
        OnPropertyChanged(nameof(PatienceText));
        OnPropertyChanged(nameof(CanTalk));
        RefreshTotals();
    }

    private void RefreshTotals()
    {
        int fights = Math.Max(1, OfferFights);
        var guaranteed = new Money(OfferPurse * fights + OfferSigningBonus);
        var upside = new Money((OfferWinBonus + OfferPerfBonus) * fights);
        OfferTotalLine = $"Commits {guaranteed} guaranteed over {fights} fight{(fights == 1 ? "" : "s")}"
                       + (upside.Amount > 0 ? $"  ·  up to {upside} more in bonuses" : "");
    }

    private static decimal RoundTo(decimal v) => Math.Round(v / 500m) * 500m;
}
