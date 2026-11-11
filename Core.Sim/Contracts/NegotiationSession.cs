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
    private readonly decimal _home;           // a lifer's hometown discount at his own promotion's table (else 1)

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
        _home = Lifers.HomeDiscount(world, fighter, promo.Id);

        AgentToughness = FighterValuation.AgentToughness(fighter);
        _concessionRate = 0.45 - AgentToughness * 0.002;   // 0.25 (hard-nosed) .. 0.45 (easygoing)
        Patience = 3 + (_appeal > 0.5 ? 1 : 0);
        Mood = 60 + (int)(_appeal * 25);

        AgentStance =
            AgentToughness >= 70 ? "hard-nosed representation" :
            AgentToughness >= 45 ? "a firm but fair agent" : "an easygoing camp";
        OpeningLine = Lifers.Gendered(fighter, string.Format(
            _home < 1m ? "\"{0} isn't going anywhere, and everybody knows it. Make it fair and he stays — he won't squeeze you.\"" :
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
    public string OpeningLine { get; }

    public string MoodLabel =>
        Mood >= 70 ? "Warm" : Mood >= 50 ? "Businesslike" : Mood >= 30 ? "Testy" : "Insulted";

    public bool WantsSigningBonus => _exp.WantsSigningBonus;
    public int DemandFights => _exp.DesiredFights;

    // --- Their position, at the terms currently on the table -----------------
    // Non-exclusive deals run ~20% cheaper (they keep outside earnings); a
    // champion's clause is a leash they charge extra to wear. Both scale the
    // whole position, so toggling a checkbox visibly moves their number.

    private decimal Factor(bool exclusive, bool clause) =>
        (exclusive ? 1m : Negotiator.NonExclusiveMoneyFactor)
        * (clause ? Negotiator.ChampionsClausePremium : 1m)
        * _home;

    public Money DemandPurse(bool exclusive, bool clause) =>
        new(RoundTo(_exp.AskingPurse.Amount * Factor(exclusive, clause) * (decimal)_demandFrac, 500m));

    public Money DemandWinBonus(bool exclusive, bool clause) =>
        new(RoundTo(_exp.AskingWinBonus.Amount * Factor(exclusive, clause) * (decimal)_demandFrac, 500m));

    public Money DemandSigning(bool exclusive, bool clause) =>
        _exp.WantsSigningBonus
            ? new(RoundTo(DemandPurse(exclusive, clause).Amount * 0.5m, 500m))
            : Money.Zero;

    /// <summary>Their current position drawn up as a signable offer ("meet their terms").</summary>
    public ContractOffer TheirTermsAsOffer(bool exclusive, bool clause) => new()
    {
        Fighter = _fighter.Id,
        Promotion = _promo.Id,
        PursePerFight = DemandPurse(exclusive, clause),
        WinBonus = DemandWinBonus(exclusive, clause),
        SigningBonus = DemandSigning(exclusive, clause),
        Fights = _exp.DesiredFights,
        Exclusive = exclusive,
        ChampionsClause = clause,
        Round = Round,
    };

    /// <summary>Put an offer on the table and hear the room's answer.</summary>
    public TalkResponse Table(ContractOffer offer)
    {
        if (Outcome != TalkOutcome.Open)
            return new(TalkVerdict.WalkOut, "The talks are over.", Array.Empty<TermReaction>());
        Round++;

        // Measure the offer against their CURRENT position (the number they're
        // actually quoting today), not the opening ask — concessions are real.
        // The quoted signing bonus counts on the same per-fight scale as
        // MoneyValue, so meeting their terms exactly is worth exactly 1.0.
        decimal needed = DemandPurse(offer.Exclusive, offer.ChampionsClause).Amount
                       + 0.5m * DemandWinBonus(offer.Exclusive, offer.ChampionsClause).Amount
                       + DemandSigning(offer.Exclusive, offer.ChampionsClause).Amount / Math.Max(1, _exp.DesiredFights);
        decimal offered = Negotiator.MoneyValue(offer);
        double ratio = needed <= 0 ? 2.0 : (double)(offered / needed);

        bool signingOk = !_exp.WantsSigningBonus || offer.SigningBonus.Amount > 0 || _appeal > 0.5;
        bool lengthOk = Math.Abs(offer.Fights - _exp.DesiredFights) <= 1;
        var reactions = React(offer);

        if (ratio >= 0.995 && signingOk && lengthOk)
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

    // --- Per-term reactions ---------------------------------------------------

    private List<TermReaction> React(ContractOffer o)
    {
        var list = new List<TermReaction>();
        var purse = DemandPurse(o.Exclusive, o.ChampionsClause);
        var winB = DemandWinBonus(o.Exclusive, o.ChampionsClause);
        var signing = DemandSigning(o.Exclusive, o.ChampionsClause);

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
            list.Add(o.SigningBonus.Amount <= 0
                ? (_appeal > 0.5
                    ? new("Signing bonus", TermFeel.Close, "expected, but the situation sells itself")
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

        if (o.PpvPoints > 0 || o.PerformanceBonus.Amount > 0)
            list.Add(new("Upside", TermFeel.Pleased, "the PPV/performance money counts toward the deal"));
        if (o.ChampionsClause)
            list.Add(new("Champion's clause", TermFeel.Close, "reads as a leash — it adds ~8% to their price"));
        if (o.Exclusive && !Negotiator.WantsExclusivity(_promo))
            list.Add(new("Exclusivity", TermFeel.Close, "tying them down at your level charges the full rate"));

        return list;
    }

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

    private static decimal RoundTo(decimal v, decimal step) => Math.Round(v / step) * step;
}
