namespace MmaManager.Core.Model;

/// <summary>The kinds of call an owner gets asked to make.</summary>
public enum DecisionKind
{
    /// <summary>A ranked fighter publicly asks for the next title shot — jump the queue or tell them to earn it.</summary>
    TitleShotRequest,

    /// <summary>A sponsor asks for a home-region fighter featured on the next main card — deliver for a bonus, fail for a strike.</summary>
    SponsorShowcase,

    /// <summary>The board wants an expensive, fading veteran off the books — the locker room will notice either way.</summary>
    VeteranCut,

    /// <summary>A struggling rival promotion is quietly for sale — buy it (roster, brand and all) or let the market decide.</summary>
    AcquisitionOffer,

    /// <summary>A bigger promotion has put a real offer in front of one of our fighters on his last fight — match it, sit down with him, or let him go (see World.RivalOffers).</summary>
    RivalOffer,
}

public enum DecisionState
{
    Pending,    // on the owner's desk, awaiting a call
    Accepted,   // said yes to something that resolves LATER (e.g. a showcase judged at the next event)
}

/// <summary>
/// A two-way call sitting on the owner's desk: someone wants something, both
/// answers have consequences, and ignoring it past the deadline is an answer
/// too. Kept small and flat — the KIND plus a fighter/sponsor reference is
/// enough to reconstruct the stakes; <see cref="Detail"/> is the display text.
/// </summary>
public sealed class Decision
{
    public required int Id { get; init; }
    public required DecisionKind Kind { get; init; }
    public required DateOnly Created { get; init; }
    public required DateOnly Deadline { get; init; }
    public FighterId? Fighter { get; init; }
    public string Detail { get; init; } = "";
    public string Sponsor { get; init; } = "";

    /// <summary>The promotion at stake (an acquisition target). Null for fighter/sponsor calls.</summary>
    public PromotionId? Promotion { get; init; }

    /// <summary>What saying yes costs (an acquisition's asking price). Zero when money isn't the question.</summary>
    public Money Price { get; init; }

    public DecisionState State { get; set; } = DecisionState.Pending;
}
