namespace MmaManager.Core.Model;

/// <summary>When in a fighter's contract a rival's offer arrives.</summary>
public enum RivalOfferKind
{
    /// <summary>On the last fight of his deal with the player: the offer he takes when he is free, unless the player keeps him.</summary>
    EndOfDeal,

    /// <summary>
    /// For a free agent the player is courting (open talks): the deal an AI promotion would
    /// have signed him on that day, lodged with a short deadline instead. The player has until
    /// then to beat it at the table, or to put a final offer in writing.
    /// </summary>
    FreeAgent,
}

/// <summary>Where a rival's offer stands.</summary>
public enum RivalOfferState
{
    /// <summary>On the owner's desk: match it, sit down with him, or let him go.</summary>
    Pending,

    /// <summary>The owner let him go (or let the deadline pass): he signs with the rival the day his re-sign window closes, unless the owner re-signs him first.</summary>
    LetGo,

    /// <summary>The owner matched it and re-signed him on the spot.</summary>
    Matched,

    /// <summary>The owner re-signed him another way (at the table) before the offer could be taken.</summary>
    Beaten,

    /// <summary>He signed with the rival on these terms.</summary>
    Signed,

    /// <summary>The offer fell away: he retired or left another way, or the rival no longer had the room or the money.</summary>
    Withdrawn,
}

/// <summary>
/// A real offer from a rival promotion for one of the player's fighters, or for a free agent
/// the player is courting (World.RivalOffers,
/// id order). Lodged by the sim (Core.Sim Contracts.OfferMarket) and resolved by it; the
/// player's answers and the negotiating table only read it. Every term here is the deal the
/// rival will actually sign him on: no bluffs. What the rival read of him, and how it priced
/// the bid, are never stored.
/// </summary>
public sealed class RivalOffer
{
    public required int Id { get; init; }
    public required FighterId Fighter { get; init; }

    /// <summary>The promotion making the offer.</summary>
    public required PromotionId From { get; init; }

    /// <summary>
    /// The player's side of the offer: for <see cref="RivalOfferKind.EndOfDeal"/>, the promotion
    /// he is signed to; for <see cref="RivalOfferKind.FreeAgent"/>, the promotion courting him
    /// (the one whose open talks made the rival lodge an offer instead of signing him on the spot).
    /// </summary>
    public required PromotionId Holder { get; init; }

    public RivalOfferKind Kind { get; init; }

    /// <summary>The rival's terms (raised at most once, by <see cref="Improvements"/>, on a free agent).</summary>
    public Money Purse { get; set; }
    public Money WinBonus { get; set; }
    public Money SigningBonus { get; set; }
    public int Fights { get; init; }
    public bool Exclusive { get; init; }

    public required DateOnly Made { get; init; }

    /// <summary>The owner's answer is due by this day; extended to the end of his re-sign window once it opens.</summary>
    public DateOnly Deadline { get; set; }

    public RivalOfferState State { get; set; } = RivalOfferState.Pending;

    /// <summary>The day it left Pending/LetGo for good (Matched, Beaten, Signed, Withdrawn); null while live.</summary>
    public DateOnly? Resolved { get; set; }

    /// <summary>How many times the rival has raised its paper (0 or 1: a free agent's offer only, answering a final offer).</summary>
    public int Improvements { get; set; }

    /// <summary>
    /// The player's final offer, in writing (a free agent's offer only): he decides between it
    /// and the rival's paper on the next weekly tick, after the rival has had its one chance to
    /// improve. Null when none is lodged.
    /// </summary>
    public ContractOffer? Final { get; set; }

    /// <summary>Still in play: on the desk, or let go and waiting for his window to close.</summary>
    public bool Live => State is RivalOfferState.Pending or RivalOfferState.LetGo;
}
