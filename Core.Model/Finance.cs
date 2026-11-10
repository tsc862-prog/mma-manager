namespace MmaManager.Core.Model;

/// <summary>
/// What a piece of money was for. Income categories are positive on the ledger,
/// expense categories negative, so the finance screen can total each bucket and
/// build a monthly profit-and-loss without re-deriving anything.
/// </summary>
public enum FinanceCategory
{
    // Income
    EventGate,
    EventPpv,
    Sponsorship,
    Broadcast,        // media rights fees (linear TV / streaming licensing), paid per event aired
    Streaming,        // in-house streaming service subscriber income (monthly)
    Merchandise,

    // Expense
    FighterPurse,
    SigningBonus,
    ContractBuyout,
    Production,
    Overhead,
    RetentionBonus,
    FighterMarketing,
    OrgMarketing,
    CampFees,
    StreamingOps,     // running the in-house streaming platform
    Acquisition,      // buying a rival promotion outright (roster, brand and all)
    Fines,            // commission fines — a press-conference brawl has a price

    Other,
}

/// <summary>
/// A single dated money movement on a promotion's books. Amount is signed —
/// positive for income, negative for expense — so the running balance is just the
/// sum of every entry on top of the promotion's starting cash.
/// </summary>
public sealed record LedgerEntry(DateOnly Date, FinanceCategory Category, Money Amount, string Description)
{
    public bool IsIncome => Amount.Amount >= 0;
}

/// <summary>
/// The rungs of the media ladder, roughly smallest reach to biggest. Each kind
/// occupies a LANE (<see cref="MediaLanes"/>): one linear TV deal, one streaming
/// home, and one pay-per-view carrier can coexist — but a promotion can't sign
/// two networks for the same screen.
/// </summary>
public enum MediaKind
{
    InHouseStream,    // your own service (launch it yourself; airs everything, money is monthly subs)
    SpecialtyCable,   // a niche cable channel (a Big Ten Network) — small rights fee per event
    CableSports,      // a national cable sports network (an FS1) — real rights money
    PrimeBroadcast,   // over-the-air prime time (a Fox/CBS) — the big stage, the strictest partner
    MajorStream,      // a global streaming giant (a Netflix) — huge per-event licensing, wants spectacles
    PpvCarrier,       // the pay-per-view pipe — no fee, it ENABLES selling PPVs (carrier keeps its cut)
}

/// <summary>The three screens a promotion can occupy at once — one deal per lane.</summary>
public enum MediaLane { Linear, Streaming, PayPerView }

public static class MediaLanes
{
    public static MediaLane LaneOf(MediaKind kind) => kind switch
    {
        MediaKind.SpecialtyCable or MediaKind.CableSports or MediaKind.PrimeBroadcast => MediaLane.Linear,
        MediaKind.InHouseStream or MediaKind.MajorStream => MediaLane.Streaming,
        _ => MediaLane.PayPerView,
    };
}

/// <summary>
/// A condition a partner writes into a deal. Sponsors and networks alike buy a
/// PRODUCT, and these are the product specs — checked when a card settles (or,
/// for cadence, monthly). Threshold's meaning depends on the kind; kinds that
/// are pure flags ignore it.
/// </summary>
public enum DealRequirementKind
{
    MinCardQuality,     // the card must average at least this MatchRating (0-100)
    MinLocalFighters,   // at least N fighters on the card from the host region (hometown names sell local product)
    StandardRules,      // house rules must be orthodox: 5-minute rounds, 3+ round bouts (flag)
    MinStarPower,       // the main event must feature a name with at least this popularity
    MinAttendance,      // the house must be at least this big
    TitleFight,         // a belt must be on the line somewhere on the card (flag)
    MinMainCard,        // the broadcast window: at least N main-card fights
    MaxMainCard,        // ...and at most N (the window is finite)
    EventCadence,       // media only: no more than N days between events (a network buys CONTENT)
}

public sealed record DealRequirement(DealRequirementKind Kind, int Threshold);

/// <summary>
/// A sponsorship: a brand that pays PER EVENT for its logo on the canvas —
/// no show, no check. The term counts down in events, so an idle promotion's
/// sponsor simply never pays out (and never expires — the money waits for
/// fight night). Missing the partner's requirements halves the check and earns
/// a strike; two strikes and they walk.
/// </summary>
public sealed class SponsorDeal
{
    public required string Partner { get; set; }

    /// <summary>The check per event staged, scaled up on PPV/SuperCard nights.</summary>
    public Money FeePerEvent { get; set; }

    public int EventsRemaining { get; set; }
    public List<DealRequirement> Requirements { get; init; } = new();
    public int Strikes { get; set; }
    public DateOnly SignedOn { get; set; }
}

/// <summary>
/// A media rights deal: a platform that pays per event AIRED and writes real
/// constraints into the contract — a broadcast window (min/max main-card
/// fights), quality and star floors, and a content cadence. Strikes accumulate
/// on misses; a partner that's had enough drops the promotion mid-term.
/// The in-house streaming platform is NOT one of these (see
/// <see cref="StreamingPlatform"/>) — you can't strike out with yourself.
/// </summary>
public sealed class MediaDeal
{
    public required MediaKind Kind { get; init; }
    public required string Partner { get; set; }

    /// <summary>Rights fee per event aired (zero for the PPV carrier — its value is the buys pipe).</summary>
    public Money RightsFeePerEvent { get; set; }

    public int MonthsRemaining { get; set; }
    public List<DealRequirement> Requirements { get; init; } = new();
    public int Strikes { get; set; }
    public DateOnly SignedOn { get; set; }

    /// <summary>Last card this partner aired — the cadence clock.</summary>
    public DateOnly? LastAiredOn { get; set; }
}

/// <summary>
/// A deal on the table — a sponsor or network that has approached the
/// promotion. Sits in the promotion's offer list until accepted, declined, or
/// it goes stale. Media offers carry a kind; sponsor offers don't.
/// </summary>
public sealed class DealOffer
{
    /// <summary>Null =&gt; a sponsorship offer; set =&gt; a media deal of this kind.</summary>
    public MediaKind? Media { get; init; }

    public required string Partner { get; init; }

    /// <summary>Per-event money (sponsor fee or rights fee).</summary>
    public Money FeePerEvent { get; init; }

    /// <summary>Sponsor terms are in events; media terms in months.</summary>
    public int Term { get; init; }

    public List<DealRequirement> Requirements { get; init; } = new();
    public DateOnly OfferedOn { get; init; }
    public DateOnly ExpiresOn { get; init; }
}

/// <summary>
/// The promotion's own streaming service (a Fight Pass): launched with real
/// money, bleeding a monthly run cost, and earning per-subscriber income that
/// grows with every card aired and decays when the promotion goes dark. The
/// early-game bet a City org can make on itself — viable on either a war chest
/// or a following, painful on neither.
/// </summary>
public sealed class StreamingPlatform
{
    public required string Name { get; set; }
    public DateOnly LaunchedOn { get; init; }
    public int Subscribers { get; set; }
}
