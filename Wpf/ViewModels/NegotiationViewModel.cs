using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Media;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Wpf.Art;
using MmaManager.Wpf.Mvvm;

namespace MmaManager.Wpf.ViewModels;

/// <summary>One line of the talks transcript. Tone: "You", "Him", "Good", "Info".</summary>
public sealed record TranscriptRow(string Who, string Line, string Tone);

/// <summary>His read on one term. Feel: "Pleased", "Close", "Sore".</summary>
public sealed record ReactionRow(string Term, string Note, string Feel);

/// <summary>How the table was left, other than a signature at it.</summary>
public enum TableSettlement { None, Matched, LetGo }

/// <summary>
/// THE TABLE: a sit-down between the promoter and the fighter himself (no agent),
/// driven by a <see cref="NegotiationSession"/>. Everything about his side comes
/// from the session's <see cref="NegotiationView"/>: his number on the terms you
/// are writing, why it sits there, his mood and patience, a rival's paper when one
/// is in his pocket (and which way he leans), and your final offer if it is in
/// writing. The player edits a slip, tables it, and reads his answer. With a
/// rival's paper on the table the window also answers it: match it (his deal is
/// ending), put a final offer in writing (a free agent), or let him go.
/// </summary>
public sealed class NegotiationViewModel : ObservableObject
{
    private readonly World _world;
    private readonly Fighter _fighter;
    private readonly NegotiationSession _session;
    private readonly ContractExpectations _exp;
    private NegotiationView _view;

    /// <summary>Set when he accepted at the table — the deal for the caller to sign.</summary>
    public ContractOffer? SignedOffer { get; private set; }

    /// <summary>Set when the rival's offer was answered here (matched, or he was let go). The sim has already acted.</summary>
    public TableSettlement Settled { get; private set; }

    /// <summary>What happened, in a line, when <see cref="Settled"/> is set.</summary>
    public string SettledLine { get; private set; } = "";

    /// <summary>Raised when the room is done with the window (true = deal struck).</summary>
    public event Action<bool>? CloseRequested;

    /// <summary>
    /// Ask before something costly or final (heading, body, detail, confirm label, keep label);
    /// null in harness runs, which go ahead as scripted.
    /// </summary>
    public Func<string, string, string?, string, string, bool>? Confirm { get; set; }

    public NegotiationViewModel(World world, Fighter fighter, Promotion promo, ContractExpectations exp,
        NegotiationSession session, bool renewal)
    {
        _world = world;
        _fighter = fighter;
        _session = session;
        _exp = exp;   // (only for the asking quote's reasons; his side comes from the session's view)
        BondLine = renewal ? AskText.Bond(world, fighter) : "";
        _promoId = promo.Id;

        Name = $"{fighter.FirstName} {fighter.LastName}";
        FirstName = fighter.FirstName;
        Nickname = string.IsNullOrWhiteSpace(fighter.Nickname) ? "" : $"“{fighter.Nickname}”";
        Portrait = Portraits.For(fighter, world);
        Facts = new[]
        {
            FighterRow.DivisionLabel(fighter.WeightClass, fighter.Sex),
            fighter.Record.ToString(),
            $"Scouted {FighterRow.Scouted(world, fighter)}",
            FreeAgentRow.HometownOf(fighter, world),
            Core.Sim.Generation.Personality.Label(fighter.Disposition),
        }.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        PlayerOrg = promo.Name;
        PlayerLogo = LogoFactory.ForPromotion(promo);
        Occasion = renewal ? "RE-SIGNING TALKS" : fighter.Contract is null ? "FREE AGENT" : "CONTRACT TALKS";

        // Seed the slip as an opening BID a notch under his number — the player
        // haggles up from here (or hits "Meet his number" to end it).
        bool exclusive = Negotiator.WantsExclusivity(promo);
        _offerExclusive = exclusive;
        _view = session.View(exclusive, false);
        var his = _view.Position;
        _offerFights = his.Fights;
        _offerPurse = RoundTo(his.PursePerFight.Amount * 0.85m);
        _offerWinBonus = RoundTo(his.WinBonus.Amount * 0.85m);
        _offerSigningBonus = his.SigningBonus.Amount;

        Transcript.Add(new(FirstName, _view.OpeningLine, "Him"));
        HisLine = _view.OpeningLine;

        TableOfferCommand = new RelayCommand(TableOffer, () => CanTalk);
        MeetTermsCommand = new RelayCommand(MeetTerms, () => CanTalk);
        SplitCommand = new RelayCommand(SplitTheDifference, () => CanTalk);
        WalkAwayCommand = new RelayCommand(WalkAway, () => CanTalk);
        PutInWritingCommand = new RelayCommand(PutInWriting, () => CanPutInWriting);
        MatchCommand = new RelayCommand(MatchIt, () => CanMatch);
        LetGoCommand = new RelayCommand(LetHimGo, () => CanLetGo);

        RefreshTable();
    }

    private readonly PromotionId _promoId;

    public ICommand TableOfferCommand { get; }
    public ICommand MeetTermsCommand { get; }
    public ICommand SplitCommand { get; }
    public ICommand WalkAwayCommand { get; }
    public ICommand PutInWritingCommand { get; }
    public ICommand MatchCommand { get; }
    public ICommand LetGoCommand { get; }

    // --- Him ------------------------------------------------------------------------
    public string Name { get; }
    public string FirstName { get; }
    public string Nickname { get; }
    public ImageSource? Portrait { get; }
    /// <summary>Public facts as chips: division, record, the scout's read, hometown, who he is.</summary>
    public IReadOnlyList<string> Facts { get; }
    /// <summary>The kicker over his name ("FREE AGENT", "RE-SIGNING TALKS").</summary>
    public string Occasion { get; }
    /// <summary>One of your fighters' bond with you, at re-signing talks ("" otherwise).</summary>
    public string BondLine { get; }

    /// <summary>How he bargains, in his own terms (the session's stance, said about him).</summary>
    public string Stance => HisStance(_view.Stance);
    public string MoodLabel => _view.Mood;
    public CardTone MoodTone => _view.Mood switch
    {
        "Warm" => CardTone.Good, "Businesslike" => CardTone.Neutral, "Testy" => CardTone.Warn, _ => CardTone.Bad,
    };
    /// <summary>Rounds of goodwill left, as dots (true = a round still in hand); at least four drawn.</summary>
    public IReadOnlyList<bool> PatienceDots => Enumerable.Range(0, Math.Max(4, _view.Patience))
        .Select(i => i < Math.Max(0, _view.Patience)).ToList();
    /// <summary>What drives his number, in words (from the session).</summary>
    public IReadOnlyList<string> Reasons => _view.Reasons;

    private string _askWhy = "";
    /// <summary>Why his opening number on these terms isn't the market rate ("" when it is).</summary>
    public string AskWhy { get => _askWhy; private set => Set(ref _askWhy, value); }

    private string _hisLine = "";
    /// <summary>The last thing he said, shown large.</summary>
    public string HisLine { get => _hisLine; private set => Set(ref _hisLine, value); }

    public ObservableCollection<TranscriptRow> Transcript { get; } = new();
    public ObservableCollection<ReactionRow> Reactions { get; } = new();
    public bool HasReactions => Reactions.Count > 0;
    /// <summary>More said than his opening line: the transcript is worth showing.</summary>
    public bool HasHistory => Transcript.Count > 1;

    public bool CanTalk => _view.Outcome == TalkOutcome.Open && Settled == TableSettlement.None;

    // --- Your slip ---------------------------------------------------------------------
    public string PlayerOrg { get; }
    public ImageSource? PlayerLogo { get; }
    /// <summary>The slip's heading: who the paper is from and to.</summary>
    public string SlipTitle => $"{PlayerOrg} to {Name}";
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

    // His number on the terms you are writing — pencilled in the slip's margin.
    public string HisPurse => _view.Position.PursePerFight.ToString();
    public string HisWinBonus => _view.Position.WinBonus.ToString();
    public string HisSigning => _view.Position.SigningBonus.Amount > 0 ? $"he wants {_view.Position.SigningBonus}" : "doesn't need one";
    public string HisFights => $"wants {_view.Position.Fights}";

    private string _offerTotalLine = "";
    public string OfferTotalLine { get => _offerTotalLine; private set => Set(ref _offerTotalLine, value); }

    // --- The rival's paper ------------------------------------------------------------
    private RivalSlip? Rival => _view.Rival;
    public bool HasRival => Rival is not null && Settled == TableSettlement.None;
    public bool NoRival => !HasRival;
    public string RivalName => Rival?.Name ?? "";
    public string RivalAbbr => Rival?.Abbreviation ?? "";
    public string RivalTier => Rival?.Tier ?? "";
    public ImageSource? RivalLogo => Rival is { } r ? LogoFactory.ForAbbreviation(r.Abbreviation) : null;
    public string RivalPurse => Rival?.Purse.ToString() ?? "";
    public string RivalWinBonus => Rival is { } r && r.WinBonus.Amount > 0 ? r.WinBonus.ToString() : "none";
    public string RivalSigning => Rival is { } r && r.SigningBonus.Amount > 0 ? r.SigningBonus.ToString() : "none";
    public string RivalFights => Rival is { } r ? $"{r.Fights}" : "";
    public string RivalExclusive => Rival is { } r ? (r.Exclusive ? "Exclusive" : "Non-exclusive — he keeps outside fights") : "";
    public bool RivalImproved => Rival?.Improved == true;
    public bool IsFreeAgentOffer => Rival?.Kind == RivalOfferKind.FreeAgent;

    /// <summary>The kicker on the rival's paper.</summary>
    public string RivalKicker => Rival?.Kind == RivalOfferKind.FreeAgent ? "THEIR OFFER — TO SIGN NOW" : "THEIR OFFER — FOR WHEN HIS DEAL IS UP";

    /// <summary>The deadline in his terms ("Signs it after 14 Mar — 9 days").</summary>
    public string RivalDeadline
    {
        get
        {
            if (Rival is not { } r) return "";
            string days = r.DaysLeft == 0 ? "today" : r.DaysLeft == 1 ? "1 day" : $"{r.DaysLeft} days";
            if (r.Kind == RivalOfferKind.FreeAgent)
                return $"He signs it after {r.Deadline:d MMM} — {days} left";
            if (_session.RivalOffer?.State == RivalOfferState.LetGo)
                return $"You let him go: he signs it when his re-sign window closes, unless you keep him first";
            return $"Answer by {r.Deadline:d MMM} — {days} left. He signs it when his deal is up";
        }
    }

    // His leaning, as a three-way marker and his reasons.
    public bool HasLeaning => _view.Leaning is not null && HasRival;
    public bool LeansTheirs => _view.Leaning?.Side == LeaningSide.Theirs;
    public bool LeansTorn => _view.Leaning?.Side == LeaningSide.Torn;
    public bool LeansYours => _view.Leaning?.Side == LeaningSide.Yours;
    public string LeaningWords => _view.Leaning?.Words ?? "";

    // --- Your final offer, in writing --------------------------------------------------
    public bool HasFinal => _view.Final is not null && HasRival;
    public string FinalLine => _view.Final is { } o
        ? $"{o.PursePerFight} a fight for {o.Fights} fights"
          + (o.WinBonus.Amount > 0 ? $", {o.WinBonus} to win" : "")
          + (o.SigningBonus.Amount > 0 ? $", {o.SigningBonus} to sign" : "")
          + (o.Exclusive ? "" : ", non-exclusive")
        : "";
    public string FinalNote => Rival?.Improved == true
        ? $"In his hands. {RivalAbbr} has already come back once and won't go again."
        : $"In his hands. {RivalAbbr} gets one chance to come back; you'll hear within the week.";

    // --- Answering the rival -----------------------------------------------------------
    private bool PendingRival => _session.RivalOffer is { State: RivalOfferState.Pending } && Settled == TableSettlement.None;

    /// <summary>"Put it in writing": a courted free agent with a rival's paper still pending.</summary>
    public bool CanPutInWriting => CanTalk && IsFreeAgentOffer && PendingRival;
    public string PutInWritingText => _view.Final is null ? "Put it in writing" : "Rewrite what's in writing";

    /// <summary>"Match it": his deal with you is ending and a rival has paper for him.</summary>
    public bool CanMatch => Settled == TableSettlement.None && _session.RivalOffer is { Kind: RivalOfferKind.EndOfDeal, Live: true } o
                            && _fighter.Contract?.Promotion == o.Holder;
    public string MatchText => CanMatch && _session.RivalOffer is { } o
        ? $"Match it — {OfferMarket.MatchTerms(_world, o).PursePerFight} a fight" : "Match it";

    /// <summary>"Let him go": answer the rival's paper with no.</summary>
    public bool CanLetGo => PendingRival;

    private string _status = "";
    /// <summary>The answer to the last thing you did with the paperwork, in plain words.</summary>
    public string Status { get => _status; private set => Set(ref _status, value); }
    private CardTone _statusTone = CardTone.Neutral;
    public CardTone StatusTone { get => _statusTone; private set => Set(ref _statusTone, value); }

    // --- Actions --------------------------------------------------------------------------

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
        Round = _view.Round,
    };

    private static string Describe(ContractOffer offer) =>
        $"{offer.PursePerFight} a fight, {offer.WinBonus} to win, {offer.SigningBonus} up front, {offer.Fights} fights"
        + (offer.PpvPoints > 0 ? $", {offer.PpvPoints} PPV points" : "")
        + (offer.PerformanceBonus.Amount > 0 ? $", {offer.PerformanceBonus} performance bonus" : "")
        + (offer.Exclusive ? ", exclusive" : ", non-exclusive")
        + (offer.ChampionsClause ? ", champion's clause" : "") + ".";

    private void TableOffer()
    {
        var offer = BuildOffer();
        Transcript.Add(new("You", Describe(offer), "You"));

        var resp = _session.Table(offer);
        Reactions.Clear();
        foreach (var r in resp.Reactions)
            Reactions.Add(new(r.Term, r.Note, r.Feel.ToString()));
        Transcript.Add(new(FirstName, resp.Line, resp.Verdict == TalkVerdict.Accept ? "Good" : "Him"));
        HisLine = resp.Line;
        Status = "";
        RefreshTable();

        if (resp.Verdict == TalkVerdict.Accept)
        {
            SignedOffer = offer;
            CloseRequested?.Invoke(true);
        }
        else if (resp.Verdict == TalkVerdict.WalkOut)
        {
            Transcript.Add(new("", $"{FirstName} got up and left. The talks are dead until he cools off.", "Info"));
            Say($"{FirstName} walked out. The talks are dead until he cools off.", CardTone.Bad);
        }
    }

    // Load his current number into the slip and table it — a guaranteed close.
    private void MeetTerms()
    {
        var his = _view.Position;
        _offerPurse = his.PursePerFight.Amount;
        _offerWinBonus = his.WinBonus.Amount;
        _offerSigningBonus = his.SigningBonus.Amount;
        _offerFights = his.Fights;
        OnPropertyChanged(nameof(OfferPurse));
        OnPropertyChanged(nameof(OfferWinBonus));
        OnPropertyChanged(nameof(OfferSigningBonus));
        OnPropertyChanged(nameof(OfferFights));
        TableOffer();
    }

    // Halve the gap on every money lever; take his length. Doesn't table —
    // the player still gets to look at it first.
    private void SplitTheDifference()
    {
        var his = _view.Position;
        OfferPurse = RoundTo((OfferPurse + his.PursePerFight.Amount) / 2m);
        OfferWinBonus = RoundTo((OfferWinBonus + his.WinBonus.Amount) / 2m);
        if (his.SigningBonus.Amount > 0)
            OfferSigningBonus = RoundTo(Math.Max(OfferSigningBonus, (OfferSigningBonus + his.SigningBonus.Amount) / 2m));
        OfferFights = his.Fights;
    }

    private void WalkAway()
    {
        _session.WalkAway();
        CloseRequested?.Invoke(false);
    }

    /// <summary>
    /// "Put it in writing": lodge the slip as the final offer against the rival's paper
    /// (<see cref="NegotiationSession.PutInWriting"/>). The sim decides; the window says
    /// what it decided, in plain words.
    /// </summary>
    private void PutInWriting()
    {
        var offer = BuildOffer();
        var result = _session.PutInWriting(offer);
        string abbr = RivalAbbr.Length > 0 ? RivalAbbr : "the other side";
        switch (result)
        {
            case FinalOfferResult.Lodged:
                Transcript.Add(new("You", "In writing: " + Describe(offer), "You"));
                Transcript.Add(new("", $"{FirstName} folded the paper into his pocket. He'll take it to {abbr}; you'll hear within the week.", "Info"));
                Status = "";   // the lodged paper itself says so, on the table
                break;
            case FinalOfferResult.NotEnough:
                Say($"He read it and shook his head: by his lights it doesn't beat {abbr}'s paper, so it isn't lodged.", CardTone.Bad);
                break;
            case FinalOfferResult.CantAfford:
                Say($"The books can't cover the {offer.SigningBonus} signing bonus you wrote down. Nothing was lodged.", CardTone.Bad);
                break;
            case FinalOfferResult.OffTerms:
                Say($"That isn't a deal he'd sign at any money: he wants {_view.Position.Fights} fights (give or take one)"
                    + (_view.Position.SigningBonus.Amount > 0 ? ", a real signing bonus" : "")
                    + " and enough of it guaranteed. Nothing was lodged.", CardTone.Warn);
                break;
            default:
                Say($"There's no paper from {abbr} left to beat. Talk to him at the table instead.", CardTone.Warn);
                break;
        }
        RefreshTable();
    }

    /// <summary>"Match it": re-sign him now on the terms that beat the rival's (<see cref="OfferMarket.Match"/>, the Desk's flow).</summary>
    private void MatchIt()
    {
        if (_session.RivalOffer is not { } o) return;
        var terms = OfferMarket.MatchTerms(_world, o);
        string cost = $"{terms.PursePerFight} a fight for {terms.Fights} fights"
                      + (terms.SigningBonus.Amount > 0 ? $", {terms.SigningBonus} up front" : "");
        var guaranteed = new Money(terms.PursePerFight.Amount * terms.Fights + terms.SigningBonus.Amount);
        if (Confirm is { } ask && !ask($"Match {RivalAbbr} for {Name}?",
                $"He re-signs now: {cost}.",
                $"That commits {guaranteed} guaranteed over the deal" + (terms.SigningBonus.Amount > 0 ? $", {terms.SigningBonus} of it today." : "."),
                "Match it", "Not yet"))
            return;
        if (OfferMarket.Match(_world, o, _world.CurrentDate))
        {
            Settle(TableSettlement.Matched, $"You matched {RivalAbbr}: {Name} re-signed at {cost}.");
            CloseRequested?.Invoke(false);
        }
        else
        {
            Say($"Matching means {terms.SigningBonus} up front and the books say no. {RivalAbbr}'s offer stands until the deadline.", CardTone.Bad);
            RefreshTable();
        }
    }

    /// <summary>"Let him go": answer the rival's paper with no (<see cref="OfferMarket.LetGo"/>).</summary>
    private void LetHimGo()
    {
        if (_session.RivalOffer is not { } o) return;
        bool fa = o.Kind == RivalOfferKind.FreeAgent;
        string body = fa
            ? $"You stop courting him, and {RivalAbbr} signs him on its paper at the next weekly tick."
            : $"He fights out his deal with you. His re-sign window still opens, but the day it closes unsigned he signs with {RivalAbbr}.";
        if (Confirm is { } ask && !ask($"Let {Name} go?", body,
                fa ? "Any offer you put in writing goes with it." : "The call on your Desk goes with it.",
                "Let him go", "Keep talking"))
            return;
        OfferMarket.LetGo(_world, o, _world.CurrentDate);
        _session.WalkAway();
        Settle(TableSettlement.LetGo, fa
            ? $"You let {Name} go: {RivalAbbr} signs him this week."
            : $"You let {Name} go: he signs with {RivalAbbr} when his window closes, unless you re-sign him first.");
        CloseRequested?.Invoke(false);
    }

    private void Settle(TableSettlement how, string line)
    {
        Settled = how;
        SettledLine = line;
        Say(line, how == TableSettlement.Matched ? CardTone.Good : CardTone.Warn);
        RefreshTable();
    }

    private void Say(string line, CardTone tone)
    {
        StatusTone = tone;
        Status = line;
    }

    // --- Refresh ------------------------------------------------------------------------

    private void RefreshTable()
    {
        _view = _session.View(OfferExclusive, OfferChampionsClause);
        var q = AskingQuote.For(_world, _world.Get(_promoId)!, _fighter, _exp,
            OfferExclusive, OfferChampionsClause);
        AskWhy = q.Reasons.Count == 0 ? "" : $"Opened at {q.Purse} a fight: {q.Why} (market rate {q.MarketRate}).";
        foreach (var p in ViewProperties) OnPropertyChanged(p);
        RefreshTotals();
    }

    // Everything read off the session's view (raised together after each refresh).
    private static readonly string[] ViewProperties =
    {
        nameof(Stance), nameof(MoodLabel), nameof(MoodTone), nameof(PatienceDots), nameof(Reasons), nameof(CanTalk),
        nameof(HisPurse), nameof(HisWinBonus), nameof(HisSigning), nameof(HisFights), nameof(HasReactions), nameof(HasHistory),
        nameof(HasRival), nameof(NoRival), nameof(RivalName), nameof(RivalAbbr), nameof(RivalTier), nameof(RivalLogo),
        nameof(RivalPurse), nameof(RivalWinBonus), nameof(RivalSigning), nameof(RivalFights), nameof(RivalExclusive),
        nameof(RivalImproved), nameof(IsFreeAgentOffer), nameof(RivalKicker), nameof(RivalDeadline),
        nameof(HasLeaning), nameof(LeansTheirs), nameof(LeansTorn), nameof(LeansYours), nameof(LeaningWords),
        nameof(HasFinal), nameof(FinalLine), nameof(FinalNote),
        nameof(CanPutInWriting), nameof(PutInWritingText), nameof(CanMatch), nameof(MatchText), nameof(CanLetGo),
    };

    private void RefreshTotals()
    {
        int fights = Math.Max(1, OfferFights);
        var guaranteed = new Money(OfferPurse * fights + OfferSigningBonus);
        var upside = new Money((OfferWinBonus + OfferPerfBonus) * fights);
        OfferTotalLine = $"Commits {guaranteed} guaranteed over {fights} fight{(fights == 1 ? "" : "s")}"
                       + (upside.Amount > 0 ? $"  ·  up to {upside} more in bonuses" : "");
    }

    private static decimal RoundTo(decimal v) => Math.Round(v / 500m) * 500m;

    /// <summary>
    /// The session still describes the old agent ("hard-nosed representation"); at this
    /// table the fighter speaks for himself, so say it about him. Unknown text passes through.
    /// </summary>
    internal static string HisStance(string stance) =>
        stance.Contains("hard", StringComparison.OrdinalIgnoreCase) ? "Drives a hard bargain"
        : stance.Contains("firm", StringComparison.OrdinalIgnoreCase) ? "Firm but fair"
        : stance.Contains("easy", StringComparison.OrdinalIgnoreCase) ? "Easy to deal with"
        : stance;
}
