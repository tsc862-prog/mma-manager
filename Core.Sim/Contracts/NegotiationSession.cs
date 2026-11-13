using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Contracts;

/// <summary>How the whole negotiation ended (or hasn't yet).</summary>
public enum TalkOutcome { Open, Signed, TheyWalked, YouWalked }

/// <summary>The agent's answer to one tabled offer.</summary>
public enum TalkVerdict { Accept, Counter, WalkOut }

/// <summary>How one term of the offer landed.</summary>
public enum TermFeel { Pleased, Close, Sore }

/// <summary>The agent's read on a single lever of the deal ("Purse", Sore, "way off — they're asking $12,000").</summary>
public sealed record TermReaction(string Term, TermFeel Feel, string Note);

/// <summary>One round's answer: the verdict, the agent's line, and how each term landed.</summary>
public sealed record TalkResponse(TalkVerdict Verdict, string Line, IReadOnlyList<TermReaction> Reactions);

/// <summary>
/// A rival's offer as the table discloses it: the promotion, its stage, the terms, the
/// deadline, and whether it has already come up once. Never its walk-away number or its read.
/// </summary>
public sealed record RivalSlip(
    PromotionId Promotion, string Name, string Abbreviation, string Tier, RivalOfferKind Kind,
    Money Purse, Money WinBonus, Money SigningBonus, int Fights, bool Exclusive,
    DateOnly Deadline, int DaysLeft, bool Improved);

/// <summary>
/// The negotiating table as the UI reads it (<see cref="NegotiationSession.View"/>): his
/// position on the terms asked about, the reasons behind it in words, the rival's paper
/// (<see cref="Rival"/>, null when there is none), his leaning between the two, the final
/// offer in writing (<see cref="Final"/>), and the answer to the last offer tabled. Contracts
/// stage 9 adds the promises the owner could put on the slip (<see cref="PromiseOptions"/>:
/// allowed or why not, what each is worth to him, how much it takes off his purse), the
/// promises already on the books for him (<see cref="Promises"/>), whether the deal he is on
/// carries a matching right (<see cref="MatchingRightHeld"/>), and what granting one on this
/// slip adds to his purse (<see cref="MatchingRightCost"/>).
/// </summary>
public sealed record NegotiationView(
    FighterId Fighter, string Name, string Stance, string OpeningLine, string Mood, int Patience, int Round,
    TalkOutcome Outcome, bool Resumed, ContractOffer Position, IReadOnlyList<string> Reasons,
    RivalSlip? Rival, Leaning? Leaning, ContractOffer? Final, TalkResponse? Last,
    IReadOnlyList<PromiseOption> PromiseOptions, IReadOnlyList<PromiseStatus> Promises,
    bool MatchingRightHeld, Money MatchingRightCost);

/// <summary>
/// A sit-down with a fighter's agent: stateful, multi-round talks. The agent
/// opens at the full ask and CONCEDES toward a floor as the player bargains in
/// good faith — how fast depends on the agent's toughness, and how low the
/// floor sits depends on how much the fighter already likes the org (the same
/// appeal the AI's <see cref="Negotiator"/> uses, so both sides of the market
/// price identically). Lowball offers burn mood and patience instead of moving
/// the number; run either out and the agent walks. Deterministic throughout —
/// same fighter, same talks.
/// </summary>
public sealed class NegotiationSession
{
    private readonly World _world;
    private readonly Fighter _fighter;
    private readonly Promotion _promo;
    private readonly ContractExpectations _exp;

    private readonly double _appeal;          // 0..1 — how much they like the org already
    private readonly double _floorFrac;       // the fraction of the ask they'll never go under
    private readonly double _concessionRate;  // how much of the gap they give per credible round
    private double _demandFrac = 1.0;         // where their number sits now, as a fraction of the ask
    private readonly decimal _home;           // his personal discount at this table: a lifer's hometown rate or a discovered local's gratitude (Negotiator.TableDiscount; else 1)
    private readonly Money? _liveFloor;       // a renewal of a live deal never goes under its show money (see Negotiator.LivePurseFloor)
    private Money? _rivalFloor;               // a rival's live offer is his walk-away: the match purse (OfferMarket.MatchTerms; set by Resume)
    private string? _rivalName;
    private RivalOffer? _offer;               // the live rival offer the table reads (set by Resume)
    private decimal? _rivalScore;             // the rival's paper by his lights (OfferMarket.Score): a deal here must be worth at least this
    private readonly decimal _distrust;       // this promotion broke its word to him: his number is higher (Promises.Distrust; else 1)

    public NegotiationSession(World world, Fighter fighter, Promotion promo, ContractExpectations exp)
    {
        _world = world;
        _fighter = fighter;
        _promo = promo;
        _exp = exp;

        // Appeal doesn't read the money terms, only who's offering — compute once.
        var rep = new ContractOffer { Fighter = fighter.Id, Promotion = promo.Id };
        _appeal = Negotiator.Appeal(world, fighter, rep, exp);
        _floorFrac = 1.0 - _appeal * 0.25;    // a dream situation is worth ~25% off the ask
        _home = Negotiator.TableDiscount(world, fighter, promo.Id);
        _liveFloor = Negotiator.LivePurseFloor(fighter, promo.Id);
        _distrust = Promises.Distrust(world, fighter.Id, promo.Id, world.CurrentDate);

        AgentToughness = FighterValuation.AgentToughness(fighter);
        _concessionRate = 0.45 - AgentToughness * 0.002;   // 0.25 (hard-nosed) .. 0.45 (easygoing)
        Patience = 3 + (_appeal > 0.5 ? 1 : 0) - (_distrust > 1m ? 1 : 0);   // a broken promise: less patience
        Mood = 60 + (int)(_appeal * 25);

        AgentStance =
            AgentToughness >= 70 ? "hard-nosed representation" :
            AgentToughness >= 45 ? "a firm but fair agent" : "an easygoing camp";
        OpeningLine = Lifers.Gendered(fighter, string.Format(
            Lifers.HomeDiscount(world, fighter, promo.Id) < 1m ? "\"I'm not going anywhere, and everybody knows it. Make it fair and I'll sign — I won't squeeze you.\"" :
            AgentToughness >= 70 ? "\"Let's not waste anyone's time — you know what {1} is worth, and so do we.\"" :
            AgentToughness >= 45 ? "\"We're open to the right deal. Put real numbers in front of us and {0} signs.\"" :
            "\"{0} likes what you're building over there. Make it fair and we're done by lunch.\"",
            _fighter.FirstName, _fighter.LastName));
    }

    public int Round { get; private set; }
    public int Patience { get; private set; }
    public int Mood { get; private set; }
    public TalkOutcome Outcome { get; private set; } = TalkOutcome.Open;

    public int AgentToughness { get; }
    /// <summary>Who you're dealing with, for the room's flavor text.</summary>
    public string AgentStance { get; }
    /// <summary>What the agent says as you sit down.</summary>
    public string OpeningLine { get; private set; }

    /// <summary>These talks picked up where an earlier sit-down left off (<see cref="Resume"/>).</summary>
    public bool Resumed { get; private set; }

    // --- Talks remembered (World.Talks) ---------------------------------------

    /// <summary>How long a walk-out closes the door before he will sit down again.</summary>
    public const int CoolOffDays = 21;

    /// <summary>
    /// Sit down with him: a fresh session, unless he remembers talks with this
    /// promotion from the last <see cref="DealWindow.TalksMemoryDays"/> days — then
    /// his number, mood, patience and round are where they were left, so leaving
    /// the table and coming back is not a free reset. After a walk-out has cooled
    /// off (<see cref="SouredUntil"/>), talks start over. No RNG.
    /// </summary>
    public static NegotiationSession Resume(World world, Fighter fighter, Promotion promo, ContractExpectations exp)
    {
        var session = new NegotiationSession(world, fighter, promo, exp);
        // A rival's paper in his pocket (OfferMarket): he won't take less than what
        // beats it, and he says so as he sits down. (Its terms are the offer's own;
        // nothing about how the rival read him.) A dead rival's paper is no paper.
        if (OfferMarket.LiveFor(world, fighter.Id) is { } rival && rival.Holder == promo.Id
            && world.Get(rival.From) is { Defunct: false } from)
        {
            session._offer = rival;
            var match = OfferMarket.MatchTerms(world, rival);
            session._rivalFloor = match.PursePerFight;
            session._rivalName = from.Abbreviation;
            // With a matching right the bar is the exact match (worth at least that to him),
            // not the rival's paper by his lights (contracts stage 9).
            session._rivalScore = OfferMarket.Score(world, fighter,
                OfferMarket.HasMatchingRight(world, rival) ? match : OfferMarket.AsContractOffer(rival), exp);
            session.OpeningLine = Lifers.Gendered(fighter, rival.Kind == RivalOfferKind.FreeAgent
                ? $"\"Before we start: {session._rivalName} has offered {fighter.FirstName} {OfferTerms(rival)}."
                  + (rival.Improvements > 0 ? " They've already come up once." : "") + " Beat that and he's yours.\""
                : $"\"{session._rivalName} has {fighter.FirstName} at {rival.Purse} a fight for {rival.Fights} fights. Beat that and we stay.\"");
        }
        if (Memory(world, fighter.Id, promo.Id) is not { } m || m.SouredUntil is not null
            || world.CurrentDate.DayNumber - m.LastTalked.DayNumber > DealWindow.TalksMemoryDays)
        {
            if (session._offer is not null) session.Patience++;   // with paper in his pocket he'll hear you out
            return session;
        }
        session._demandFrac = Math.Max(session._floorFrac, Math.Min(1.0, m.DemandFrac));
        session.Mood = m.Mood;
        session.Patience = m.Patience + (session._offer is { } o && o.Made > m.LastTalked ? 1 : 0);   // (an offer since: he'll hear you out)
        session.Round = m.Round;
        session.Resumed = true;
        if (session._rivalFloor is null)
            session.OpeningLine = Lifers.Gendered(fighter,
                $"\"We were here {Ago(world.CurrentDate.DayNumber - m.LastTalked.DayNumber)}. The number hasn't moved — let's finish it.\"");
        return session;
    }

    /// <summary>The day a walk-out stops closing the door on this promotion, while it still does; else null.</summary>
    public static DateOnly? SouredUntil(World world, FighterId fighter, PromotionId promo) =>
        Memory(world, fighter, promo) is { SouredUntil: { } until } && world.CurrentDate < until ? until : null;

    /// <summary>
    /// The room has closed: write what he remembers. A signature forgets the talks;
    /// his walk-out closes the door for <see cref="CoolOffDays"/> days (after which
    /// talks start over); otherwise his number, mood and patience are kept for the
    /// next sit-down. Talks where nothing was tabled leave nothing to remember.
    /// </summary>
    public void Remember(DateOnly on)
    {
        var existing = Memory(_world, _fighter.Id, _promo.Id);
        if (Outcome == TalkOutcome.Signed)
        {
            if (existing is not null) _world.Talks.Remove(existing);
            return;
        }
        if (Outcome != TalkOutcome.TheyWalked && Round == 0) return;   // sat down and left: nothing changed

        var m = existing ?? new TalksMemory { Fighter = _fighter.Id, Promotion = _promo.Id };
        if (existing is null) _world.Talks.Add(m);
        m.DemandFrac = _demandFrac;
        m.Mood = Mood;
        m.Patience = Patience;
        m.Round = Round;
        m.LastTalked = on;
        m.SouredUntil = Outcome == TalkOutcome.TheyWalked ? CoolOffEnd(on) : null;

        // In his own re-sign window a walk-out this late shuts the door for good:
        // he will test the market. Say so now, not in a week.
        if (Outcome == TalkOutcome.TheyWalked && _promo.Id == _world.PlayerPromotion && DealWindow.TalksClosed(_world, _fighter))
            Living.Mail.Post(_world, on, MailCategory.ContractAlert, "Matchmaking",
                $"{_fighter.FullName} will test the market",
                Lifers.Gendered(_fighter, $"{_fighter.FullName} walked out of our talks with too little of his window left to cool off. "
                    + $"Talks are closed: on {_fighter.Contract!.WindowEndsOn:d MMMM} he's a free agent and anyone can have him — us included."),
                aboutFighter: _fighter.Id);
    }

    /// <summary>
    /// When a walk-out on <paramref name="on"/> stops closing the door:
    /// <see cref="CoolOffDays"/> later — except in the fighter's own re-sign window
    /// with this promotion (<see cref="DealWindow"/>), where the door reopens at
    /// least <see cref="WindowReopenDays"/> days before the window ends, so a
    /// mid-window walk-out still leaves time to re-sign. A walk-out inside those
    /// last days keeps the full cool-off: talks are closed and he tests the market
    /// (<see cref="DealWindow.TalksClosed"/>).
    /// </summary>
    private DateOnly CoolOffEnd(DateOnly on)
    {
        var until = on.AddDays(CoolOffDays);
        if (DealWindow.InWindow(_world, _fighter) && _fighter.Contract!.Promotion == _promo.Id
            && _fighter.Contract.WindowEndsOn is { } end && on < end.AddDays(-WindowReopenDays))
        {
            var reopen = end.AddDays(-WindowReopenDays);
            if (reopen < until) until = reopen;
            if (until <= on) until = on.AddDays(1);   // never before tomorrow
        }
        return until;
    }

    /// <summary>Inside a re-sign window, a walk-out's door reopens at least this many days before the window ends.</summary>
    public const int WindowReopenDays = 3;

    private static TalksMemory? Memory(World world, FighterId fighter, PromotionId promo) =>
        world.Talks.FirstOrDefault(t => t.Fighter == fighter && t.Promotion == promo);

    private static string Ago(int days) =>
        days <= 1 ? "yesterday" : days < 14 ? $"{days} days ago" : $"{days / 7} weeks ago";

    public string MoodLabel =>
        Mood >= 70 ? "Warm" : Mood >= 50 ? "Businesslike" : Mood >= 30 ? "Testy" : "Insulted";

    public bool WantsSigningBonus => _exp.WantsSigningBonus;
    public int DemandFights => _exp.DesiredFights;

    // --- Their position, at the terms currently on the table -----------------
    // Non-exclusive deals run ~20% cheaper (they keep outside earnings); a
    // champion's clause is a leash they charge extra to wear. Both scale the
    // whole position, so toggling a checkbox visibly moves their number.

    // The terms on the slip that move his number: exclusivity, the leash terms (champion's
    // clause, matching right) and the promises (contracts stage 9).
    private readonly record struct Slip(bool Exclusive, bool Clause, bool Match = false, int Fights = 0, bool Home = false, bool Title = false)
    {
        public static Slip Of(ContractOffer o) =>
            new(o.Exclusive, o.ChampionsClause, o.MatchingRight, o.PromiseFights, o.PromiseHomeShow, o.PromiseTitleShot);
        public bool HasPromises => Fights > 0 || Home || Title;
    }

    private decimal Factor(Slip s) =>
        (s.Exclusive ? 1m : Negotiator.NonExclusiveMoneyFactor)
        * (s.Clause ? Negotiator.ChampionsClausePremium : 1m)
        * (s.Match ? Negotiator.MatchingRightPremium : 1m)
        * _home * _distrust * PromiseFactor(s);

    // What the promises on the slip take off his number: the same appeal the Negotiator
    // reads (Promises.AppealOf), turned into money the way the floor is (a quarter of it).
    private decimal PromiseFactor(Slip s)
    {
        if (!s.HasPromises) return 1m;
        var rep = new ContractOffer
        {
            Fighter = _fighter.Id, Promotion = _promo.Id,
            PromiseFights = s.Fights, PromiseHomeShow = s.Home, PromiseTitleShot = s.Title,
        };
        double with = Negotiator.Appeal(_world, _fighter, rep, _exp);
        return (decimal)((1.0 - with * 0.25) / (1.0 - _appeal * 0.25));
    }

    public Money DemandPurse(bool exclusive, bool clause) => DemandPurse(new Slip(exclusive, clause));

    /// <summary>His number on the terms of this slip: its exclusivity, leash terms and promises (its money is ignored).</summary>
    public Money DemandPurse(ContractOffer terms) => DemandPurse(Slip.Of(terms));

    private Money DemandPurse(Slip s)
    {
        decimal p = Math.Max(Math.Max(_liveFloor?.Amount ?? 0m, _rivalFloor?.Amount ?? 0m),
            UpTo500(_exp.AskingPurse.Amount * Factor(s) * (decimal)_demandFrac));
        return new(_rivalScore is { } target ? BeatRival(p, target, s) : p);
    }

    // With a rival's paper in his pocket, his number on his own terms (his win bonus,
    // his signing bonus, his length) must be worth more to him than the rival's whole
    // deal: raise the purse, on the $500 step, until it is. (Score is linear in the
    // purse: one closed-form step, then the rounding is checked.)
    private decimal BeatRival(decimal purse, decimal target, Slip terms)
    {
        decimal ScoreAt(decimal p) => OfferMarket.Score(_world, _fighter, AtPurse(p, terms), _exp);
        decimal s = ScoreAt(purse);
        if (s >= target) return purse;
        decimal per500 = ScoreAt(purse + 500m) - s;
        if (per500 > 0) purse += Math.Max(0m, Math.Floor((target - s) / per500) * 500m);
        for (int i = 0; i < 40 && ScoreAt(purse) < target; i++) purse += 500m;
        return purse;
    }

    // His terms at a given purse (what TheirTermsAsOffer tables, with that purse).
    private ContractOffer AtPurse(decimal purse, Slip s) => new()
    {
        Fighter = _fighter.Id, Promotion = _promo.Id,
        PursePerFight = new Money(purse),
        WinBonus = DemandWinBonus(s),
        SigningBonus = _exp.WantsSigningBonus ? new Money(UpTo500(purse * 0.5m)) : Money.Zero,
        Fights = _exp.DesiredFights, Exclusive = s.Exclusive, ChampionsClause = s.Clause,
        MatchingRight = s.Match, PromiseFights = s.Fights, PromiseHomeShow = s.Home, PromiseTitleShot = s.Title,
    };

    public Money DemandWinBonus(bool exclusive, bool clause) => DemandWinBonus(new Slip(exclusive, clause));

    /// <summary>His win bonus on the terms of this slip (see <see cref="DemandPurse(ContractOffer)"/>).</summary>
    public Money DemandWinBonus(ContractOffer terms) => DemandWinBonus(Slip.Of(terms));

    private Money DemandWinBonus(Slip s) =>
        new(UpTo500(_exp.AskingWinBonus.Amount * Factor(s) * (decimal)_demandFrac));

    public Money DemandSigning(bool exclusive, bool clause) => DemandSigning(new Slip(exclusive, clause));

    /// <summary>His signing bonus on the terms of this slip (see <see cref="DemandPurse(ContractOffer)"/>).</summary>
    public Money DemandSigning(ContractOffer terms) => DemandSigning(Slip.Of(terms));

    private Money DemandSigning(Slip s) =>
        _exp.WantsSigningBonus
            ? new(UpTo500(DemandPurse(s).Amount * 0.5m))
            : Money.Zero;

    /// <summary>Their current position drawn up as a signable offer ("meet their terms").</summary>
    public ContractOffer TheirTermsAsOffer(bool exclusive, bool clause) => TheirTermsAsOffer(new Slip(exclusive, clause));

    /// <summary>
    /// Their current position on the terms of this slip — its exclusivity, leash terms and
    /// promises carried over — drawn up as a signable offer (its money is ignored).
    /// </summary>
    public ContractOffer TheirTermsAsOffer(ContractOffer terms) => TheirTermsAsOffer(Slip.Of(terms));

    private ContractOffer TheirTermsAsOffer(Slip s) => new()
    {
        Fighter = _fighter.Id,
        Promotion = _promo.Id,
        PursePerFight = DemandPurse(s),
        WinBonus = DemandWinBonus(s),
        SigningBonus = DemandSigning(s),
        Fights = _exp.DesiredFights,
        Exclusive = s.Exclusive,
        ChampionsClause = s.Clause,
        MatchingRight = s.Match,
        PromiseFights = s.Fights,
        PromiseHomeShow = s.Home,
        PromiseTitleShot = s.Title,
        Round = Round,
    };

    /// <summary>Put an offer on the table and hear the room's answer.</summary>
    public TalkResponse Table(ContractOffer offer) => Last = TableCore(offer);

    /// <summary>The room's answer to the last offer tabled in this sit-down; null before the first.</summary>
    public TalkResponse? Last { get; private set; }

    private TalkResponse TableCore(ContractOffer offer)
    {
        if (Outcome != TalkOutcome.Open)
            return new(TalkVerdict.WalkOut, "The talks are over.", Array.Empty<TermReaction>());
        // A promise that can't be kept is refused before anything else (it costs no round).
        if (Promises.FirstRefusal(_world, _promo, _fighter, offer) is { } no)
            return new(TalkVerdict.Counter, $"\"Take that promise off the paper. {no.Why}\"",
                new[] { new TermReaction(PromiseTerm(no.Kind), TermFeel.Sore, no.Why) });
        Round++;

        // Measure the offer against their CURRENT position (the number they're
        // actually quoting today), not the opening ask — concessions are real.
        // The quoted signing bonus counts on the same per-fight scale as
        // MoneyValue, so meeting their terms exactly is worth exactly 1.0.
        var slip = Slip.Of(offer);
        decimal needed = DemandPurse(slip).Amount
                       + 0.5m * DemandWinBonus(slip).Amount
                       + DemandSigning(slip).Amount / Math.Max(1, _exp.DesiredFights);
        decimal offered = Negotiator.MoneyValue(_world, offer);
        double ratio = needed <= 0 ? 2.0 : (double)(offered / needed);

        bool signingOk = SigningOk(offer);
        bool lengthOk = Math.Abs(offer.Fights - _exp.DesiredFights) <= 1;
        // A live deal is never torn up for less show money (unless he's skidding).
        bool payOk = _liveFloor is not { } floor || offer.PursePerFight.Amount >= floor.Amount;
        // A rival's paper is live: the purse may not go under the match price, and the
        // whole deal must be worth more to him than theirs (a thin win bonus, a short
        // signing bonus or a different length can't undercut it).
        bool beatsRival = (_rivalFloor is not { } rf || offer.PursePerFight.Amount >= rf.Amount)
                          && (_rivalScore is not { } rs || OfferMarket.Score(_world, _fighter, offer, _exp) >= rs);
        bool guaranteedOk = GuaranteedOk(offer);
        var reactions = React(offer);

        if (ratio >= 0.995 && signingOk && lengthOk && payOk && beatsRival && guaranteedOk)
        {
            Outcome = TalkOutcome.Signed;
            return new(TalkVerdict.Accept, Line(AcceptLines), reactions);
        }

        if (ratio < 0.70)
        {
            // A lowball doesn't move their number — it just sours the room.
            Mood -= 25;
            Patience -= 2;
        }
        else
        {
            // Good-faith haggling: they give back a slice of the gap, plus a
            // little drift for time spent at the table. Tough agents give less.
            double target = Math.Clamp(_demandFrac * ratio, _floorFrac, _demandFrac);
            _demandFrac = Math.Max(_floorFrac, _demandFrac - (_demandFrac - target) * _concessionRate - 0.02);
            Mood -= 4;
            Patience -= 1;
        }

        if (Patience <= 0 || Mood <= 15)
        {
            Outcome = TalkOutcome.TheyWalked;
            return new(TalkVerdict.WalkOut, Line(WalkLines), reactions);
        }

        string line = ratio < 0.70 ? Line(LowballLines)
                    : !payOk ? $"\"{_fighter.FirstName} is signed at {_liveFloor} a fight. We're not tearing that up for less.\""
                    : !beatsRival ? $"\"{_rivalName} has real paper on the table. {_fighter.FirstName} stays for a deal worth more to him than theirs — on these terms that's {DemandPurse(slip)} a fight or better, with the bonuses.\""
                    : !guaranteedOk ? $"\"Bonuses are nice. Guaranteed money pays the rent — {_fighter.FirstName} needs at least {GuaranteedNeeded(offer)} a fight he can count on.\""
                    : !signingOk ? "\"Close on the money — but no signing bonus, no signature.\""
                    : !lengthOk ? $"\"The money can work, but {_fighter.FirstName} wants a {_exp.DesiredFights}-fight deal.\""
                    : ratio >= 0.90 ? Line(CloseLines) : Line(MidLines);
        return new(TalkVerdict.Counter, line, reactions);
    }

    /// <summary>The player leaves the table — no hard feelings, the door stays open.</summary>
    public void WalkAway()
    {
        if (Outcome == TalkOutcome.Open) Outcome = TalkOutcome.YouWalked;
    }

    // --- A rival's paper on the table (OfferMarket) ----------------------------------

    /// <summary>The live rival offer this table reads (its holder or courting side is this promotion), if any.</summary>
    public RivalOffer? RivalOffer => _offer;

    /// <summary>
    /// "Put it in writing" (a courted free agent with a rival's offer in his hand): the
    /// owner's best offer, lodged with the sim (<see cref="OfferMarket.LodgeFinal"/>). He
    /// takes it over the rival's paper if it is worth more to him (<see cref="OfferMarket.Score"/>)
    /// — even under his asking number — after the rival has had one chance to come back,
    /// on the next weekly tick. It must still be a deal he'd sign: his length (give or take
    /// one), a real signing bonus if he wants one, enough guaranteed money. Counts as a
    /// round (the talks are remembered); the room stays open.
    /// </summary>
    public FinalOfferResult PutInWriting(ContractOffer offer)
    {
        if (Outcome != TalkOutcome.Open || _offer is not { Kind: RivalOfferKind.FreeAgent }) return FinalOfferResult.NoOffer;
        if (Math.Abs(offer.Fights - _exp.DesiredFights) > 1 || !SigningOk(offer) || !GuaranteedOk(offer)
            || Promises.FirstRefusal(_world, _promo, _fighter, offer) is not null)
            return FinalOfferResult.OffTerms;
        var result = OfferMarket.LodgeFinal(_world, _fighter, _promo, offer);
        if (result == FinalOfferResult.Lodged) Round++;
        return result;
    }

    /// <summary>
    /// Everything the table shows, in one read model (the only thing the UI needs): his
    /// position on the given terms, why it sits where it does, the rival's disclosed paper,
    /// which way he leans and the final offer in writing. Public facts and his own wants
    /// only — never the rival's read of him, its walk-away number, nor anyone's ability.
    /// </summary>
    public NegotiationView View(bool exclusive, bool clause) =>
        View(new ContractOffer { Fighter = _fighter.Id, Promotion = _promo.Id, Exclusive = exclusive, ChampionsClause = clause });

    /// <summary>
    /// <see cref="View(bool, bool)"/> on the terms of this slip: its exclusivity, leash terms
    /// and promises (its money is ignored). The promise options are priced against this slip.
    /// </summary>
    public NegotiationView View(ContractOffer terms)
    {
        var onSlip = Slip.Of(terms);
        var reasons = new List<string>();
        var pri = _exp.Priorities;
        var wants = new (int W, string Why)[]
        {
            (pri.Money, "the money"), (pri.TitleOpportunity, "a path to a belt"),
            (pri.Loyalty, "loyalty to the people who backed him"), (pri.HomeRegion, "fighting near home"),
        };
        reasons.Add($"What he weighs most: {string.Join(" and ", wants.OrderByDescending(x => x.W).ThenBy(x => x.Why, StringComparer.Ordinal).Take(2).Select(x => x.Why))}.");
        reasons.Add($"He wants a {_exp.DesiredFights}-fight deal" + (_exp.WantsSigningBonus ? " and a signing bonus." : "."));
        if (_home < 1m) reasons.Add("He gives this house a personal discount.");
        if (_liveFloor is { } lf) reasons.Add($"He's signed at {lf} a fight and won't tear that up for less.");
        if (_offer is not null)
            reasons.Add(OfferMarket.HasMatchingRight(_world, _offer)
                ? $"{_rivalName} has real paper on the table, but we hold a matching right: matching their terms exactly is enough."
                : $"{_rivalName} has real paper on the table: a deal here must be worth more to him than theirs.");
        if (_distrust > 1m)
            reasons.Add(Lifers.Gendered(_fighter, "You broke your word to him: he asks more, has less patience, and won't count a promise from you."));

        RivalSlip? slip = null;
        Leaning? lean = null;
        if (_offer is { } o && _world.Get(o.From) is { } rival)
        {
            slip = new RivalSlip(rival.Id, rival.Name, rival.Abbreviation, Org.OrgSizing.Label(Org.OrgSizing.SizeOf(rival)), o.Kind,
                o.Purse, o.WinBonus, o.SigningBonus, o.Fights, o.Exclusive, o.Deadline,
                Math.Max(0, o.Deadline.DayNumber - _world.CurrentDate.DayNumber), o.Improvements > 0);
            lean = OfferMarket.LeaningOf(_world, o);
        }
        // The promises the owner could add (each priced against this slip without it), and the matching right.
        var options = new List<PromiseOption>();
        void Option(PromiseKind kind, int fights, Slip with, Slip without)
        {
            var probe = new ContractOffer
            {
                Fighter = _fighter.Id, Promotion = _promo.Id, Fights = _exp.DesiredFights,
                PromiseFights = kind == PromiseKind.Activity ? fights : 0,
                PromiseHomeShow = kind == PromiseKind.HomeShow, PromiseTitleShot = kind == PromiseKind.TitleShot,
            };
            string? no = Promises.FirstRefusal(_world, _promo, _fighter, probe)?.Why;
            decimal off = no is null ? Math.Max(0m, DemandPurse(without).Amount - DemandPurse(with).Amount) : 0m;
            options.Add(new PromiseOption(kind, fights, no is null, no ?? "",
                _distrust > 1m ? Lifers.Gendered(_fighter, "a promise from you is worth nothing to him now")
                    : Promises.WorthWords(_world, _promo, _fighter, kind, fights, _exp),
                new Money(off)));
        }
        Option(PromiseKind.TitleShot, 0, onSlip with { Title = true }, onSlip with { Title = false });
        Option(PromiseKind.HomeShow, 0, onSlip with { Home = true }, onSlip with { Home = false });
        for (int n = Promises.MinActivity; n <= Promises.MaxActivity; n++)
            Option(PromiseKind.Activity, n, onSlip with { Fights = n }, onSlip with { Fights = 0 });
        var rightCost = new Money(Math.Max(0m,
            DemandPurse(onSlip with { Match = true }).Amount - DemandPurse(onSlip with { Match = false }).Amount));

        return new NegotiationView(
            _fighter.Id, _fighter.FullName, AgentStance, OpeningLine, MoodLabel, Patience, Round, Outcome, Resumed,
            TheirTermsAsOffer(onSlip), reasons, slip, lean, _offer?.Final, Last,
            options, Promises.For(_world, _fighter.Id),
            _fighter.Contract is { MatchingRight: true } held && held.Promotion == _promo.Id, rightCost);
    }

    private static string OfferTerms(RivalOffer o) =>
        $"{o.Purse} a fight for {o.Fights} fights"
        + (o.WinBonus.Amount > 0 ? $", {o.WinBonus} to win" : "")
        + (o.SigningBonus.Amount > 0 ? $", {o.SigningBonus} to sign" : "");

    // Whatever the upside, the guaranteed money (purse plus signing bonus spread
    // over the deal) must cover a real share of what they're quoting as
    // guaranteed today — the promotion decides whether he ever fights on a PPV,
    // for a bonus, or against someone he can beat.
    private decimal GuaranteedFloor(ContractOffer o) =>
        (DemandPurse(Slip.Of(o)).Amount
         + DemandSigning(Slip.Of(o)).Amount / Math.Max(1, _exp.DesiredFights))
        * Negotiator.GuaranteedFloorFraction;

    private bool GuaranteedOk(ContractOffer o) => Negotiator.GuaranteedPerFight(o) >= GuaranteedFloor(o);

    // The floor as the room says it: rounded up to the next $500.
    private Money GuaranteedNeeded(ContractOffer o) => new(Math.Ceiling(GuaranteedFloor(o) / 500m) * 500m);

    // "Wants a signing bonus" means a real one: at least a quarter of the bonus he quoted.
    private bool SigningOk(ContractOffer o) =>
        !_exp.WantsSigningBonus || _appeal > 0.5
        || (o.SigningBonus.Amount > 0
            && o.SigningBonus.Amount >= DemandSigning(Slip.Of(o)).Amount * Negotiator.SigningBonusMinFraction);

    // --- Per-term reactions ---------------------------------------------------

    private List<TermReaction> React(ContractOffer o)
    {
        var list = new List<TermReaction>();
        var slip = Slip.Of(o);
        var purse = DemandPurse(slip);
        var winB = DemandWinBonus(slip);
        var signing = DemandSigning(slip);

        list.Add(o.PursePerFight.Amount >= purse.Amount
            ? new("Purse", TermFeel.Pleased, "the show money is there")
            : o.PursePerFight.Amount >= purse.Amount * 0.85m
                ? new("Purse", TermFeel.Close, $"short — they're at {purse}")
                : new("Purse", TermFeel.Sore, $"way off — they're at {purse}"));

        list.Add(o.WinBonus.Amount >= winB.Amount
            ? new("Win bonus", TermFeel.Pleased, "the win money works")
            : o.WinBonus.Amount >= winB.Amount * 0.85m
                ? new("Win bonus", TermFeel.Close, $"they want {winB}")
                : new("Win bonus", TermFeel.Sore, $"thin — they want {winB}"));

        if (_exp.WantsSigningBonus)
            list.Add(!SigningOk(o) || (o.SigningBonus.Amount <= 0 && _appeal > 0.5)
                ? (_appeal > 0.5
                    ? new("Signing bonus", TermFeel.Close, "expected, but the situation sells itself")
                    : o.SigningBonus.Amount > 0
                        ? new("Signing bonus", TermFeel.Sore, $"a token — they're picturing {signing}")
                        : new("Signing bonus", TermFeel.Sore, "no signing bonus, no signature"))
                : o.SigningBonus.Amount >= signing.Amount * 0.85m
                    ? new("Signing bonus", TermFeel.Pleased, "the up-front cheque lands well")
                    : new("Signing bonus", TermFeel.Close, $"light — they're picturing {signing}"));
        else if (o.SigningBonus.Amount > 0)
            list.Add(new("Signing bonus", TermFeel.Pleased, "a sweetener they weren't expecting"));

        int diff = o.Fights - _exp.DesiredFights;
        list.Add(diff == 0
            ? new("Length", TermFeel.Pleased, $"{o.Fights} fights suits them")
            : Math.Abs(diff) == 1
                ? new("Length", TermFeel.Close, $"they'd prefer {_exp.DesiredFights}")
                : new("Length", TermFeel.Sore, $"they want a {_exp.DesiredFights}-fight deal"));

        if (!GuaranteedOk(o))
            list.Add(new("Guaranteed money", TermFeel.Sore,
                $"too much rides on bonuses — they need {GuaranteedNeeded(o)} a fight guaranteed (purse plus signing bonus)"));

        if (o.PpvPoints > 0)
            list.Add(Negotiator.PpvPointValue(_world, _promo.Id) <= 0
                ? new("PPV points", TermFeel.Sore, "worth nothing — you don't sell pay-per-views")
                : o.PpvPoints > Negotiator.MaxPpvPoints
                    ? new("PPV points", TermFeel.Close, $"only {Negotiator.MaxPpvPoints} points can go in a deal")
                    : new("PPV points", TermFeel.Pleased, "counted at what your pay-per-views actually take"));
        if (o.PerformanceBonus.Amount > 0)
            list.Add(new("Performance bonus", TermFeel.Pleased,
                "on top of the house bonus — counted at about one night in eight, never above the show money"));
        if (o.ChampionsClause)
            list.Add(new("Champion's clause", TermFeel.Close, "reads as a leash — it adds ~8% to their price"));
        if (o.MatchingRight)
            list.Add(new("Matching right", TermFeel.Close, "a hold on his next move — it adds ~5% to his price"));
        bool trusted = _distrust <= 1m;
        if (o.PromiseTitleShot) list.Add(PromiseReaction(PromiseKind.TitleShot, 0, trusted));
        if (o.PromiseHomeShow) list.Add(PromiseReaction(PromiseKind.HomeShow, 0, trusted));
        if (o.PromiseFights > 0) list.Add(PromiseReaction(PromiseKind.Activity, o.PromiseFights, trusted));
        if (o.Exclusive && !Negotiator.WantsExclusivity(_promo))
            list.Add(new("Exclusivity", TermFeel.Close, "tying them down at your level charges the full rate"));

        return list;
    }

    private TermReaction PromiseReaction(PromiseKind kind, int fights, bool trusted) =>
        !trusted
            ? new(PromiseTerm(kind), TermFeel.Sore, Lifers.Gendered(_fighter, "you broke your word to him once — a promise from you is worth nothing to him now"))
            : new(PromiseTerm(kind), TermFeel.Pleased,
                Promises.WorthWords(_world, _promo, _fighter, kind, fights, _exp) + " — and it has teeth: break it and he'll take it hard");

    private static string PromiseTerm(PromiseKind kind) => kind switch
    {
        PromiseKind.TitleShot => "Promise: title shot",
        PromiseKind.HomeShow => "Promise: home show",
        _ => "Promise: activity",
    };

    // --- Agent dialogue ---------------------------------------------------------

    private static readonly string[] AcceptLines =
    {
        "\"That's a deal — send the paper over.\"",
        "\"Done. {0} will be there with bells on.\"",
        "\"You've got yourself a fighter.\"",
    };
    private static readonly string[] CloseLines =
    {
        "\"We're close. Stretch a little and it's done.\"",
        "\"Almost there — find a bit more and {0} signs today.\"",
    };
    private static readonly string[] MidLines =
    {
        "\"That's not the number. Come up and we'll talk.\"",
        "\"You're negotiating like it's a favor. It isn't — improve it.\"",
        "\"{0} has other rooms to sit in. Do better.\"",
    };
    private static readonly string[] LowballLines =
    {
        "\"Is that a joke? Don't insult {0} with that again.\"",
        "\"That's fan-night money. One more like that and we're gone.\"",
    };
    private static readonly string[] WalkLines =
    {
        "\"We're done here. Lose our number.\"",
        "\"You had your chance — {0} will sign somewhere serious.\"",
    };

    // Deterministic per-fighter, per-round pick — same talks replay the same way.
    private string Line(string[] pool)
    {
        uint h = (uint)_fighter.Id.Value * 2654435761u + (uint)Round * 40503u + (uint)pool.Length;
        h ^= h >> 15;
        return string.Format(pool[h % (uint)pool.Length], _fighter.FirstName, _fighter.LastName);
    }

    /// <summary>
    /// His number on the $500 step, rounded UP: the room never quotes less than
    /// he actually wants (the AI's opening offer rounds the same way, and
    /// <see cref="AskingQuote"/> quotes exactly this), so meeting the quoted
    /// figure is always meeting his price.
    /// </summary>
    internal static decimal UpTo500(decimal v) => Math.Ceiling(v / 500m) * 500m;
}
