namespace MmaManager.Core.Model;

/// <summary>
/// How much a fighter cares about each thing when weighing a contract offer.
/// Each is a 0–100 weight; the negotiator scores offers against these so two
/// fighters with the same asking price can still want very different deals.
/// </summary>
public sealed record OfferPriorities
{
    /// <summary>Cares about the money — purse, bonuses, guarantees.</summary>
    public required int Money { get; init; }

    /// <summary>Cares about a path to the belt — prestige and ranking of the promotion.</summary>
    public required int TitleOpportunity { get; init; }

    /// <summary>Cares about staying put / rewarding the promotion that developed them.</summary>
    public required int Loyalty { get; init; }

    /// <summary>Cares about fighting in or near their home region.</summary>
    public required int HomeRegion { get; init; }
}

/// <summary>
/// What a fighter is looking for in their next deal — the target a scouted player
/// negotiates toward and the yardstick the negotiator measures offers against.
/// Produced deterministically by the valuation model from the fighter's ability,
/// draw, résumé, age and standing, so the same fighter always wants the same thing.
/// </summary>
public sealed record ContractExpectations
{
    /// <summary>Headline 0–100 "worth" for the UI (ability + draw + résumé, minus age).</summary>
    public required int MarketValue { get; init; }

    /// <summary>Desired guaranteed show money per fight.</summary>
    public required Money AskingPurse { get; init; }

    /// <summary>Desired win bonus.</summary>
    public required Money AskingWinBonus { get; init; }

    /// <summary>Preferred contract length in fights.</summary>
    public required int DesiredFights { get; init; }

    /// <summary>Whether they expect an up-front signing bonus to sign.</summary>
    public required bool WantsSigningBonus { get; init; }

    /// <summary>What they weigh when comparing offers.</summary>
    public required OfferPriorities Priorities { get; init; }
}

/// <summary>
/// A proposed deal on the table — the full set of levers a promotion is offering a
/// fighter, plus the negotiation round it belongs to. Both the player and the AI
/// build these; the <c>Negotiator</c> turns them into an accept/counter/reject.
/// </summary>
public sealed class ContractOffer
{
    public required FighterId Fighter { get; init; }
    public required PromotionId Promotion { get; init; }

    public Money PursePerFight { get; set; }
    public Money WinBonus { get; set; }
    public int Fights { get; set; } = 4;
    public Money SigningBonus { get; set; }
    public int PpvPoints { get; set; }
    public Money PerformanceBonus { get; set; }
    public bool Exclusive { get; set; } = true;
    public bool ChampionsClause { get; set; }

    /// <summary>0 = opening offer; increments each counter.</summary>
    public int Round { get; set; }

    /// <summary>Turn this offer into a signed deal as of <paramref name="on"/>.</summary>
    public Contract ToContract(DateOnly on) => new()
    {
        Promotion = Promotion,
        SignedOn = on,
        Type = ContractType.PerFight,
        PursePerFight = PursePerFight,
        WinBonus = WinBonus,
        FightsRemaining = Fights,
        IsExclusive = Exclusive,
        HasChampionsClause = ChampionsClause,
        SigningBonus = SigningBonus,
        PpvPoints = PpvPoints,
        PerformanceBonus = PerformanceBonus,
        Guaranteed = true,
    };
}

public enum NegotiationVerdict { Accept, Counter, Reject }

/// <summary>The fighter's answer to an offer; <see cref="Counter"/> is set only when countering.</summary>
public sealed record NegotiationResponse(NegotiationVerdict Verdict, ContractOffer? Counter, string Summary);
