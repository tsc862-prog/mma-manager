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
/// One promise on the slip (a title shot, a fight at home, so many fights in the year):
/// a toggle the player writes in, and what the session says about it on these terms —
/// whether it can be made (and if not, why, in his words), what it is worth to him, and
/// how much it takes off his number. Held for the life of the window; only its words are
/// refreshed from each <see cref="NegotiationView"/>.
/// </summary>
public sealed class SlipPromise : ObservableObject
{
    private readonly Action _changed;
    public SlipPromise(PromiseKind kind, string label, Action changed)
    {
        Kind = kind;
        Label = label;
        _changed = changed;
    }

    public PromiseKind Kind { get; }
    public string Label { get; }

    private bool _isOn;
    /// <summary>Written on the slip (toggling it re-quotes his number).</summary>
    public bool IsOn { get => _isOn; set { if (Set(ref _isOn, value)) _changed(); } }

    private bool _allowed = true;
    /// <summary>The promise can be made at all (off and greyed out when not — never hidden).</summary>
    public bool Allowed { get => _allowed; private set => Set(ref _allowed, value); }

    private string _note = "";
    /// <summary>What it is worth to him, or why it can't be offered (his words, in quotes).</summary>
    public string Note { get => _note; private set => Set(ref _note, value); }

    private string _effect = "";
    /// <summary>What it does to his number on these terms ("takes $2,500 off his number").</summary>
    public string Effect { get => _effect; private set => Set(ref _effect, value); }
    public bool HasEffect => _effect.Length > 0;

    /// <summary>For the activity promise: the 2/3/4 picker (empty for the others).</summary>
    public ObservableCollection<SlipPick> Picks { get; } = new();
    public bool HasPicks => Picks.Count > 0;

    internal void Update(bool allowed, string note, string effect)
    {
        Allowed = allowed;
        Note = note;
        Effect = effect;
        OnPropertyChanged(nameof(HasEffect));
        // A promise that can't be made comes off the slip (quietly: the caller is refreshing).
        if (!allowed && _isOn) { _isOn = false; OnPropertyChanged(nameof(IsOn)); }
    }
}

/// <summary>One choice on the activity picker ("3" fights in the year).</summary>
public sealed class SlipPick : ObservableObject
{
    private readonly Action<SlipPick> _picked;
    public SlipPick(int fights, Action<SlipPick> picked) { Fights = fights; _picked = picked; }
    public int Fights { get; }
    public string Text => Fights.ToString();

    private bool _isChecked;
    public bool IsChecked { get => _isChecked; set { if (Set(ref _isChecked, value) && value) _picked(this); } }
    internal void SetQuietly(bool on) { _isChecked = on; OnPropertyChanged(nameof(IsChecked)); }

    private bool _allowed = true;
    public bool Allowed { get => _allowed; internal set => Set(ref _allowed, value); }
}

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
    /// Ask before something costly or final (heading, body, detail, confirm label, keep label).
    /// The window wires it to <see cref="MessageWindow.Ask"/> when it takes this view model; a
    /// view model driven without the window (the render harness, before it builds one) has none
    /// and goes ahead as scripted.
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

        // The promises he could be given (each one re-quotes his number when toggled).
        string home = world.Get(fighter.Country)?.Name is { Length: > 0 } c ? $"A fight at home in {c}" : "A fight at home";
        TitlePromise = new SlipPromise(PromiseKind.TitleShot, "A title shot", RefreshTable);
        HomePromise = new SlipPromise(PromiseKind.HomeShow, home, RefreshTable);
        ActivityPromise = new SlipPromise(PromiseKind.Activity, "Fights in the year", RefreshTable);
        for (int n = Promises.MinActivity; n <= Promises.MaxActivity; n++)
            ActivityPromise.Picks.Add(new SlipPick(n, PickActivity));
        (ActivityPromise.Picks.FirstOrDefault(p => p.Fights == 3) ?? ActivityPromise.Picks[0]).SetQuietly(true);
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
        MatchCommand = new RelayCommand(MatchIt, () => CanAffordMatch);
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
    /// <summary>For the window's he/she text (<see cref="Pronouns"/>).</summary>
    public bool IsWoman => _fighter.Sex == Sex.Female;
    private string G(string text) => Pronouns.For(IsWoman, text);
    public ImageSource? Portrait { get; }
    /// <summary>Public facts as chips: division, record, the scout's read, hometown, who he is.</summary>
    public IReadOnlyList<string> Facts { get; }
    /// <summary>The kicker over his name ("FREE AGENT", "RE-SIGNING TALKS").</summary>
    public string Occasion { get; }
    /// <summary>One of your fighters' bond with you, at re-signing talks ("" otherwise).</summary>
    public string BondLine { get; }

    /// <summary>How he bargains, in words about him (the session's <see cref="NegotiationView.Stance"/>).</summary>
    public string Stance => _view.Stance;
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

    // --- Promises and the matching right, on the slip --------------------------------
    public SlipPromise TitlePromise { get; }
    public SlipPromise HomePromise { get; }
    public SlipPromise ActivityPromise { get; }

    /// <summary>Fights promised in the year (0 when the activity promise is off).</summary>
    private int PromisedFights => ActivityPromise.IsOn ? ActivityPromise.Picks.FirstOrDefault(p => p.IsChecked)?.Fights ?? 0 : 0;

    private void PickActivity(SlipPick pick)
    {
        foreach (var p in ActivityPromise.Picks) if (!ReferenceEquals(p, pick)) p.SetQuietly(false);
        if (!ActivityPromise.IsOn) ActivityPromise.IsOn = true;   // picking a number writes the promise in
        else RefreshTable();
    }

    private bool _offerMatchingRight;
    /// <summary>A matching right on the slip: if a rival bids when this deal ends, you keep him by matching their terms.</summary>
    public bool OfferMatchingRight { get => _offerMatchingRight; set { if (Set(ref _offerMatchingRight, value)) RefreshTable(); } }

    /// <summary>What the right adds to his number on these terms.</summary>
    public string MatchingRightEffect => _view.MatchingRightCost.Amount > 0
        ? G($"adds {_view.MatchingRightCost} to his number")
        : G("won't move his number on these terms");

    /// <summary>His current deal with you already carries one ("" otherwise).</summary>
    public string MatchingRightHeld => _view.MatchingRightHeld
        ? G("His current deal with you carries one. It is used up when he re-signs, unless this slip carries one too.")
        : "";

    /// <summary>
    /// One line when a broken promise is costing you at this table: he remembers yours to him,
    /// or word is out that you broke one this past year (both read off the sim, never worked out here).
    /// </summary>
    public string WaryLine
    {
        get
        {
            var today = _world.CurrentDate;
            var lines = new List<string>();
            if (Promises.BrokenBy(_world, _fighter.Id, _promoId, today) > 0)
                lines.Add(G("You broke your word to him: a promise from you counts for nothing with him for now."));
            if (Promises.Wariness(_world, _promoId, today) > 1m)
                lines.Add("Word is out that you broke a promise this past year: every fighter asks a little more at your table and counts your promises for less.");
            return string.Join(" ", lines);
        }
    }
    public bool HasWaryLine => WaryLine.Length > 0;

    /// <summary>Promises already on the books for him (open ones), in a line ("" when none).</summary>
    public string OpenPromisesLine => _view.Promises.Where(p => p.State == PromiseState.Open).ToList() is { Count: > 0 } open
        ? "Already promised: " + string.Join("; ", open.Select(p => $"{p.What} ({p.StatusText.ToLowerInvariant()}{(p.Progress.Length > 0 ? ", " + p.Progress : "")})"))
        : "";
    public bool HasOpenPromises => OpenPromisesLine.Length > 0;

    // The terms the session prices: exclusivity, the leash terms and the promises (money is ignored).
    private ContractOffer SlipTerms() => new()
    {
        Fighter = _fighter.Id, Promotion = _promoId,
        Fights = Math.Max(1, OfferFights),
        Exclusive = OfferExclusive, ChampionsClause = OfferChampionsClause, MatchingRight = OfferMatchingRight,
        PromiseFights = PromisedFights, PromiseHomeShow = HomePromise.IsOn, PromiseTitleShot = TitlePromise.IsOn,
    };

    // Each promise's words and price, from the session's options on this slip.
    private void RefreshPromises()
    {
        var opts = _view.PromiseOptions;
        void Fill(SlipPromise sp, PromiseOption? o)
        {
            if (o is null) { sp.Update(false, "", ""); return; }
            string note = o.Allowed ? Sentence(o.Worth) : $"“{o.Why}”";
            string effect = !o.Allowed ? ""
                : o.PurseOff.Amount > 0 ? G($"takes {o.PurseOff} off his number")
                : G("won't move his number");
            sp.Update(o.Allowed, note, effect);
        }
        Fill(TitlePromise, opts.FirstOrDefault(o => o.Kind == PromiseKind.TitleShot));
        Fill(HomePromise, opts.FirstOrDefault(o => o.Kind == PromiseKind.HomeShow));

        var acts = opts.Where(o => o.Kind == PromiseKind.Activity).ToList();
        foreach (var p in ActivityPromise.Picks)
            p.Allowed = acts.FirstOrDefault(o => o.Fights == p.Fights)?.Allowed == true;
        // The picked number must be one he'd take; else the nearest he would.
        var picked = ActivityPromise.Picks.FirstOrDefault(p => p.IsChecked);
        if (picked is null || !picked.Allowed)
        {
            var next = ActivityPromise.Picks.LastOrDefault(p => p.Allowed && p.Fights <= (picked?.Fights ?? 3))
                       ?? ActivityPromise.Picks.FirstOrDefault(p => p.Allowed);
            foreach (var p in ActivityPromise.Picks) p.SetQuietly(ReferenceEquals(p, next));
            picked = next;
        }
        var act = picked is null ? acts.FirstOrDefault() : acts.FirstOrDefault(o => o.Fights == picked.Fights);
        Fill(ActivityPromise, act is null ? null : act with { Allowed = acts.Any(o => o.Allowed) && picked is not null });
    }

    private static string Sentence(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..] + (s.EndsWith('.') ? "" : ".");

    // His number on the terms you are writing — pencilled in the slip's margin.
    public string HisPurse => _view.Position.PursePerFight.ToString();
    public string HisWinBonus => _view.Position.WinBonus.ToString();
    public string HisSigning => _view.Position.SigningBonus.Amount > 0 ? G($"he wants {_view.Position.SigningBonus}") : "doesn't need one";
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
    public string RivalExclusive => Rival is { } r ? (r.Exclusive ? "Exclusive" : G("Non-exclusive — he keeps outside fights")) : "";
    public bool RivalImproved => Rival?.Improved == true;
    public bool IsFreeAgentOffer => Rival?.Kind == RivalOfferKind.FreeAgent;

    /// <summary>The kicker on the rival's paper.</summary>
    public string RivalKicker => Rival?.Kind == RivalOfferKind.FreeAgent ? "THEIR OFFER — TO SIGN NOW" : G("THEIR OFFER — FOR WHEN HIS DEAL IS UP");

    /// <summary>The deadline in his terms ("Signs it after 14 Mar — 9 days").</summary>
    public string RivalDeadline
    {
        get
        {
            if (Rival is not { } r) return "";
            string days = r.DaysLeft == 0 ? "today" : r.DaysLeft == 1 ? "1 day" : $"{r.DaysLeft} days";
            bool letGo = _session.RivalOffer?.State == RivalOfferState.LetGo;
            if (r.Kind == RivalOfferKind.FreeAgent)
                return G(letGo
                    ? $"You let him go: {r.Abbreviation} signs him when the week turns. Until then you can still sign him here"
                    : $"He signs it after {r.Deadline:d MMM} — {days} left");
            if (letGo)
                return G("You let him go: he signs it when his re-sign window closes, unless you keep him first");
            return G($"Answer by {r.Deadline:d MMM} — {days} left. He signs it when his deal is up");
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
    public string FinalNote => G(Rival?.Improved == true
        ? $"In his hands. {RivalAbbr} has already come back once and won't go again."
        : $"In his hands. {RivalAbbr} gets one chance to come back; you'll hear within the week.");

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

    /// <summary>
    /// The bank can cover a match (<see cref="OfferMarket.Match"/> refuses when the balance is
    /// under the signing bonus: overdrawn, even a $0 bonus). The button is off when it can't,
    /// so a click never fails (each failed match posts the board another note).
    /// </summary>
    public bool CanAffordMatch => CanMatch && _session.RivalOffer is { } o && _world.Get(_promoId) is { } p
                                  && p.Balance.Amount >= OfferMarket.MatchTerms(_world, o).SigningBonus.Amount;

    /// <summary>Why "Match it" is off, in terms of the bank ("" when it isn't).</summary>
    public string MatchBlocked
    {
        get
        {
            if (!CanMatch || CanAffordMatch || _session.RivalOffer is not { } o || _world.Get(_promoId) is not { } p) return "";
            var bonus = OfferMarket.MatchTerms(_world, o).SigningBonus;
            return p.Balance.Amount < 0
                ? $"Matching is off while the bank is overdrawn by {new Money(-p.Balance.Amount)}: the books can't sign a new deal until the balance is back above zero."
                : $"Matching means {bonus} up front and the bank holds {p.Balance}. Find the money before the deadline, or beat their paper at the table.";
        }
    }

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
        MatchingRight = OfferMatchingRight,
        PromiseFights = PromisedFights,
        PromiseHomeShow = HomePromise.IsOn,
        PromiseTitleShot = TitlePromise.IsOn,
        Round = _view.Round,
    };

    private string Describe(ContractOffer offer) =>
        $"{offer.PursePerFight} a fight, {offer.WinBonus} to win, {offer.SigningBonus} up front, {offer.Fights} fights"
        + (offer.PpvPoints > 0 ? $", {offer.PpvPoints} PPV points" : "")
        + (offer.PerformanceBonus.Amount > 0 ? $", {offer.PerformanceBonus} performance bonus" : "")
        + (offer.Exclusive ? ", exclusive" : ", non-exclusive")
        + (offer.ChampionsClause ? ", champion's clause" : "")
        + (offer.MatchingRight ? ", a matching right" : "")
        + (PromisedLine(offer) is { Length: > 0 } p ? $". You promise {p}" : "") + ".";

    /// <summary>The promises on a slip in a phrase ("a title shot and 3 fights in the year"; "" when none).</summary>
    private string PromisedLine(ContractOffer o)
    {
        var parts = new List<string>();
        if (o.PromiseTitleShot) parts.Add("a title shot");
        if (o.PromiseHomeShow) parts.Add(HomePromise.Label.Replace("A fight", "a fight"));
        if (o.PromiseFights > 0) parts.Add($"{o.PromiseFights} fights in the year");
        return parts.Count switch
        {
            0 => "",
            1 => parts[0],
            _ => string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1],
        };
    }

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
            Transcript.Add(new("", G($"{FirstName} got up and left. The talks are dead until he cools off."), "Info"));
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
        // Binding: he may sign it next week. Ask first, and say what it commits.
        var guaranteed = new Money(offer.PursePerFight.Amount * offer.Fights + offer.SigningBonus.Amount);
        if (Confirm is { } ask && !ask($"Put your offer to {Name} in writing?",
                G($"It's binding: if he takes it over {RivalAbbr}'s paper, he signs it next week at {offer.PursePerFight} a fight for {offer.Fights} fights"
                  + (offer.SigningBonus.Amount > 0 ? $", {offer.SigningBonus} up front." : ".")),
                $"That commits {guaranteed} guaranteed over the deal" + (offer.SigningBonus.Amount > 0 ? $", {offer.SigningBonus} of it on signing." : ".")
                + (PromisedLine(offer) is { Length: > 0 } promised ? G($" It also promises him {promised}: break that and he takes it hard, in public.") : ""),
                "Put it in writing", "Not yet"))
            return;
        var result = _session.PutInWriting(offer);
        string abbr = RivalAbbr.Length > 0 ? RivalAbbr : "the other side";
        switch (result)
        {
            case FinalOfferResult.Lodged:
                Transcript.Add(new("You", "In writing: " + Describe(offer), "You"));
                Transcript.Add(new("", G($"{FirstName} folded the paper into his pocket. He'll take it to {abbr}; you'll hear within the week."), "Info"));
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
        if (!CanAffordMatch) { Say(MatchBlocked, CardTone.Bad); return; }
        if (Confirm is { } ask && !ask($"Match {RivalAbbr} for {Name}?",
                G($"He re-signs now: {cost}."),
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
            Say(MatchBlocked.Length > 0 ? MatchBlocked : $"The match didn't go through. {RivalAbbr}'s offer stands until the deadline.", CardTone.Bad);
            RefreshTable();
        }
    }

    /// <summary>"Let him go": answer the rival's paper with no (<see cref="OfferMarket.LetGo"/>).</summary>
    private void LetHimGo()
    {
        if (_session.RivalOffer is not { } o) return;
        bool fa = o.Kind == RivalOfferKind.FreeAgent;
        string body = G(fa
            ? $"You stop courting him, and {RivalAbbr} signs him on its paper when the week turns."
            : $"He fights out his deal with you. His re-sign window still opens, but the day it closes unsigned he signs with {RivalAbbr}.");
        if (Confirm is { } ask && !ask($"Let {Name} go?", body,
                fa ? "Any offer you put in writing goes with it." : "The call on your Desk goes with it.",
                G("Let him go"), "Keep talking"))
            return;
        OfferMarket.LetGo(_world, o, _world.CurrentDate);
        _session.WalkAway();
        Settle(TableSettlement.LetGo, G(fa
            ? $"You let {Name} go: {RivalAbbr} signs him this week."
            : $"You let {Name} go: he signs with {RivalAbbr} when his window closes, unless you re-sign him first."));
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
        Status = G(line);
    }

    // --- Refresh ------------------------------------------------------------------------

    private void RefreshTable()
    {
        // (Called by the promise toggles too, which exist only once the constructor has made them.)
        if (TitlePromise is null) return;
        _view = _session.View(SlipTerms());
        RefreshPromises();
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
        nameof(CanPutInWriting), nameof(PutInWritingText), nameof(CanMatch), nameof(MatchText), nameof(CanAffordMatch), nameof(MatchBlocked), nameof(CanLetGo),
        nameof(MatchingRightEffect), nameof(MatchingRightHeld), nameof(WaryLine), nameof(HasWaryLine), nameof(OpenPromisesLine), nameof(HasOpenPromises),
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
}
