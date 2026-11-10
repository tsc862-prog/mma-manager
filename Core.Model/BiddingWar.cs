namespace MmaManager.Core.Model;

/// <summary>
/// A public bidding war over a marquee free agent: multiple big orgs escalate
/// against each other in rounds until one remains (or the money stops making
/// sense) — the sport's favourite soap opera, and how superstars get overpaid.
/// The fighter is off the normal free-agency board while the war runs. The
/// PLAYER never bids through this object — they can swoop any time by signing
/// the fighter through normal negotiation at war prices, which ends the war.
/// </summary>
public sealed class BiddingWar
{
    public required FighterId Fighter { get; init; }

    /// <summary>Orgs still in the running (dropping out as the number climbs).</summary>
    public List<PromotionId> Suitors { get; init; } = new();

    /// <summary>The bid on the table — each round pushes it up.</summary>
    public Money CurrentBid { get; set; }

    public int Round { get; set; } = 1;

    /// <summary>When the next escalation (or the resolution) happens.</summary>
    public DateOnly NextRoundOn { get; set; }
}
