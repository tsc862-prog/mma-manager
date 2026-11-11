namespace MmaManager.Core.Model;

/// <summary>A card on a date. The player books these; AI promotions run their own.</summary>
public sealed class Event
{
    public required EventId Id { get; init; }
    public required PromotionId Promotion { get; init; }
    public required string Name { get; set; }
    public required DateOnly Date { get; init; }
    public string? Venue { get; set; }         // denormalised venue name for display
    public VenueId? VenueId { get; set; }      // the building this card is staged in
    public Region Region { get; set; }
    public EventTier Tier { get; set; } = EventTier.FightNight;
    public EventStatus Status { get; set; } = EventStatus.Scheduled;

    /// <summary>The card in running order; the last bout is the main event.</summary>
    public List<Fight> Card { get; init; } = new();

    /// <summary>
    /// How many fights at the TOP of the running order are the main card — the
    /// broadcast window a media partner airs and regulates. Everything before
    /// them is prelims: cheaper exposure, less fame for the fighters on them.
    /// 0 = no split (the whole card is the show — a club card has no window).
    /// </summary>
    public int MainCardFights { get; set; }

    /// <summary>The main card actually in effect (0 =&gt; the full card).</summary>
    public int EffectiveMainCard => MainCardFights <= 0 ? Card.Count : Math.Min(MainCardFights, Card.Count);

    /// <summary>Is this bout on the prelims (below the broadcast window)?</summary>
    public bool IsPrelim(Fight f) => Card.IndexOf(f) < Card.Count - EffectiveMainCard;

    /// <summary>Filled in once the event has run.</summary>
    public EventFinancials? Financials { get; set; }

    /// <summary>
    /// The press-tour dividend: how much the pre-fight media cycle (an electric
    /// presser, a face-off brawl) lifted this card's box office. Set by the
    /// Living.PressTour roll in fight week, consumed by Financials at settle.
    /// 0 = no tour or a flat one. Persisted.
    /// </summary>
    public double HypeBonus { get; set; }
}

/// <summary>
/// A single booked bout. <see cref="Outcome"/> is null until the fight engine
/// simulates it. Corners are held by id so a Fight is a light, serialisable
/// record of the booking rather than a tangle of object references.
/// </summary>
public sealed class Fight
{
    public required FightId Id { get; init; }
    public required FighterId RedCorner { get; init; }
    public required FighterId BlueCorner { get; init; }

    public WeightClass WeightClass { get; set; }
    public int ScheduledRounds { get; set; } = 3;        // 5 for main events / title fights
    public bool IsTitleFight { get; set; }
    public TitleId? Title { get; set; }
    public int? CatchweightLbs { get; set; }             // non-null => catchweight bout

    /// <summary>Who blew the scale at the weigh-in (fined; a title leaves the line — or the champ's waist — over it).</summary>
    public FighterId? WeightMissBy { get; set; }

    /// <summary>
    /// The commission overturned this result to a No Contest (a failed drug
    /// test after the fact). The original <see cref="Outcome"/> is kept — the
    /// fight happened as it happened — but the records read NC for both.
    /// </summary>
    public bool OverturnedNc { get; set; }

    public FightOutcome? Outcome { get; set; }           // non-null once simulated
}

/// <summary>The result the fight engine produces for a bout.</summary>
public sealed class FightOutcome
{
    public FighterId? Winner { get; init; }              // null => draw or no-contest
    public FighterId? Loser { get; init; }
    public required FightMethod Method { get; init; }
    public int EndRound { get; init; }
    public int EndTimeSeconds { get; init; }

    /// <summary>Per-fighter tale of the tape, keyed by fighter id.</summary>
    public Dictionary<FighterId, FightStatline> Stats { get; init; } = new();

    /// <summary>
    /// Optional round-by-round play-by-play text, present only on a freshly
    /// simulated bout that asked for it; it is not saved. It can NOT be rebuilt
    /// later by re-simulating: the seed is reproducible, but the fighters have
    /// trained, aged and been injured since, so a re-run is a different fight
    /// (about a quarter of a year-old world's bouts come out differently).
    /// </summary>
    public List<string>? PlayByPlay { get; set; }

    /// <summary>
    /// Each COMPLETED round, 10-9s (10-8 on a knockdown or a rout). For a decision
    /// this is the card of a judge who voted for the official winner, so it always
    /// agrees with the result; for a finish it is the noiseless card of the rounds
    /// before the stoppage. Persisted (unlike <see cref="PlayByPlay"/>), because
    /// the sim and the broadcast both read it and a loaded world must behave
    /// as the one that was saved.
    /// </summary>
    public List<RoundScore>? RoundScores { get; set; }
}

/// <summary>One completed round's score on the stored card (see <see cref="FightOutcome.RoundScores"/>).</summary>
public sealed record RoundScore(int Red, int Blue);

/// <summary>Aggregate output stats for one fighter in one bout.</summary>
public sealed class FightStatline
{
    public int SignificantStrikesLanded { get; set; }
    public int SignificantStrikesAttempted { get; set; }
    public int TakedownsLanded { get; set; }
    public int TakedownsAttempted { get; set; }
    public int SubmissionAttempts { get; set; }
    public int KnockdownsScored { get; set; }
    public int ControlTimeSeconds { get; set; }
}

public sealed class EventFinancials
{
    public int Attendance { get; set; }
    public Money GateRevenue { get; set; }
    public long PayPerViewBuys { get; set; }
    public Money PayPerViewRevenue { get; set; }
    public Money ProductionCost { get; set; }
    public Money FighterPayroll { get; set; }

    public Money NetProfit =>
        GateRevenue + PayPerViewRevenue - ProductionCost - FighterPayroll;
}
