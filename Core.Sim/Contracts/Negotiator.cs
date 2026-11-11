using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Contracts;

/// <summary>
/// Turns a contract offer into the fighter's answer — accept, counter, or reject —
/// by weighing the money against what they're asking for, discounted by how much
/// they like the promotion (prestige/title path, loyalty, fighting near home).
/// Deterministic and used by both the player-facing negotiation UI and the AI's
/// free-agency bidding, so both sides play by the same rules.
/// </summary>
public sealed class Negotiator
{
    private const decimal PpvPointValue = 800m;   // rough per-fight worth of one PPV point
    private const int Patience = 2;               // counters before walking away
    public const decimal NonExclusiveMoneyFactor = 0.80m;   // a non-exclusive deal comes ~20% cheaper
    public const decimal ChampionsClausePremium = 1.08m;    // a champion's clause is a leash they charge to wear

    public NegotiationResponse Evaluate(World world, Fighter f, ContractOffer offer, ContractExpectations exp)
    {
        decimal offered = MoneyValue(offer);
        // A non-exclusive deal is cheaper: the fighter keeps their outside earnings
        // (they can fight elsewhere / moonlight), so they'll take less from this org.
        // A champion's clause runs the other way — it costs a premium to include.
        // And GRATITUDE is real: a discovered local deals cheap with the org that
        // gave them the shot (everyone else pays the hometown-hype price), and
        // a LIFER takes less to stay where he belongs (see Lifers).
        decimal asked = AskMoney(exp) * (offer.Exclusive ? 1m : NonExclusiveMoneyFactor)
                                      * (offer.ChampionsClause ? ChampionsClausePremium : 1m)
                                      * GratitudeFactor(f, offer.Promotion)
                                      * Lifers.HomeDiscount(world, f, offer.Promotion);
        double ratio = asked <= 0 ? 2.0 : (double)(offered / asked);

        double appeal = Appeal(world, f, offer, exp);
        double threshold = 1.0 - appeal * 0.25;    // a dream situation is worth ~25% off the ask
        bool signingOk = !exp.WantsSigningBonus || offer.SigningBonus.Amount > 0 || appeal > 0.5;
        bool lengthOk = Math.Abs(offer.Fights - exp.DesiredFights) <= 1;

        if (ratio >= threshold && signingOk && lengthOk)
            return new(NegotiationVerdict.Accept, null, "Happy with the deal.");

        // Lowball, or they've run out of patience → walk.
        if (ratio < 0.7 || offer.Round >= Patience)
            return new(NegotiationVerdict.Reject, null, RejectReason(ratio, signingOk, lengthOk));

        return new(NegotiationVerdict.Counter, BuildCounter(offer, exp, threshold), CounterReason(ratio, signingOk, lengthOk));
    }

    /// <summary>
    /// Would the fighter leave their current org for this poacher's offer? Same
    /// money test as <see cref="Evaluate"/>, but their asking price is inflated by
    /// how LOYAL they are — at loyalty 100 a rival has to roughly double the money
    /// to pry them loose (a lot more expensive, never impossible). The bigger stage
    /// still tempts (appeal), so a giant can always outbid a small org eventually.
    /// </summary>
    /// <summary>
    /// A rival's bid to poach a fighter off their current org. The rival will
    /// OVERPAY to land a coveted target — up to what its own gate bears — because
    /// loyalty is a price to leave: trivial for a fresh signing, but for a fighter
    /// who's spent years with an org it balloons to ~5-10x the normal ask. A bigger
    /// STEP UP (more tiers) is its own lure, so it eases the premium toward the low
    /// end. Returns the offer if the rival can pay enough to actually move them,
    /// else null (too loyal, or the rival's budget can't reach the price).
    /// </summary>
    public ContractOffer? PoachOffer(World world, Promotion suitor, Fighter f, ContractExpectations exp)
    {
        double leavePrice = LoyaltyLeavePrice(world, suitor, f);
        decimal cap = PurseCap(world, suitor);
        bool exclusive = WantsExclusivity(suitor);
        decimal askFactor = exclusive ? 1m : NonExclusiveMoneyFactor;

        // The rival stretches toward the loyalty-inflated ask (discounted if it's
        // only buying a non-exclusive slice of them), but never past its cap.
        decimal purse = RoundTo(Math.Min(exp.AskingPurse.Amount * (decimal)leavePrice * askFactor, cap), 500m);
        decimal winBonus = RoundTo(Math.Min(exp.AskingWinBonus.Amount, purse * 0.75m), 500m);
        decimal signing = exp.WantsSigningBonus && suitor.Balance.Amount > 250_000m
            ? RoundTo(purse * 0.5m, 500m) : 0m;
        var offer = new ContractOffer
        {
            Fighter = f.Id, Promotion = suitor.Id,
            PursePerFight = new Money(purse), WinBonus = new Money(winBonus),
            Fights = exp.DesiredFights, SigningBonus = new Money(signing),
            Exclusive = exclusive, Round = 0,
        };

        decimal asked = AskMoney(exp) * (decimal)leavePrice * askFactor;
        double ratio = asked <= 0 ? 2.0 : (double)(MoneyValue(offer) / asked);
        double bar = 0.70 - Appeal(world, f, offer, exp) * 0.15;   // the bigger stage tempts
        return ratio >= bar ? offer : null;
    }

    // What it costs to pry a fighter loose, as a multiple of their normal ask. Near
    // 1x for a fresh signing; climbs (quadratically, so late in a tenure) toward a
    // ceiling of 5-10x at max loyalty — 10x for a lateral-ish one-tier move, easing
    // to ~5x for a giant multi-tier leap that sells itself.
    private static double LoyaltyLeavePrice(World world, Promotion suitor, Fighter f)
    {
        var current = f.Contract is { } c ? world.Get(c.Promotion) : null;
        int stepUp = current is null
            ? 1 : Math.Max(1, (int)Org.OrgSizing.SizeOf(suitor) - (int)Org.OrgSizing.SizeOf(current));
        double ceiling = Math.Clamp(10.0 - (stepUp - 1) * 1.5, 5.0, 10.0);
        return 1.0 + Math.Pow(f.Loyalty / 100.0, 2.0) * (ceiling - 1.0);
    }

    /// <summary>
    /// A promotion's opening offer — pitched at the fighter's ask but capped at
    /// what the org's box office can actually carry. Fighters who outgrow the
    /// budget walk to a bigger promotion (or the open market): the talent ladder.
    /// <paramref name="capStretch"/> is how far past that cap the org will reach —
    /// 1 for business as usual; a solvent org renewing a lifer stretches
    /// (<see cref="Lifers.RenewalStretch"/>), the way a bidding war does.
    /// </summary>
    public ContractOffer AiOpeningOffer(World world, Promotion promo, Fighter f, ContractExpectations exp, decimal capStretch = 1m)
    {
        decimal cap = PurseCap(world, promo) * capStretch;
        // A non-exclusive deal is pitched at the fighter's DISCOUNTED ask — the org
        // is only buying part of their dance card, so it pays part of the price.
        // The org that discovered them pitches at the gratitude rate.
        bool exclusive = WantsExclusivity(promo);
        decimal ask = exp.AskingPurse.Amount * (exclusive ? 1m : NonExclusiveMoneyFactor)
                                             * GratitudeFactor(f, promo.Id)
                                             * Lifers.HomeDiscount(world, f, promo.Id);
        var purse = new Money(RoundTo(Math.Min(ask, cap), 500m));
        var winBonus = new Money(Math.Min(exp.AskingWinBonus.Amount, RoundTo(purse.Amount * 0.75m, 500m)));

        var signing = exp.WantsSigningBonus && promo.Balance.Amount > 250_000m
            ? new Money(RoundTo(purse.Amount * 0.5m, 500m))
            : Money.Zero;

        return new ContractOffer
        {
            Fighter = f.Id,
            Promotion = promo.Id,
            PursePerFight = purse,
            WinBonus = winBonus,
            Fights = exp.DesiredFights,
            SigningBonus = signing,
            Exclusive = exclusive,
            Round = 0,
        };
    }

    /// <summary>
    /// Whether an org ties its signings down. The small halls can't afford to — a
    /// City/Regional org deals non-exclusive (cheaper purses, no monthly retainer,
    /// fighters moonlight on the local circuit); the bigger stages demand exclusivity
    /// because their names ARE the product and a rival card cheapens them.
    /// </summary>
    public static bool WantsExclusivity(Promotion promo) =>
        Org.OrgSizing.SizeOf(promo) >= OrgSize.National;

    /// <summary>
    /// The discovered local's thank-you: a real discount for the org that gave
    /// them the shot, while the career is still young. The open market pays the
    /// hometown-hype price; the discoverer pays about half.
    /// </summary>
    public static decimal GratitudeFactor(Fighter f, PromotionId promo) =>
        f.DiscoveredBy == promo && f.Record.TotalBouts <= 5 ? 0.55m : 1m;

    /// <summary>
    /// The purse a fighter actually asks OF THIS ORG. A small hall deals
    /// non-exclusive, and the fighter takes ~20% less for keeping their outside
    /// earnings — so the raw <see cref="ContractExpectations.AskingPurse"/> is
    /// not the number a City/Regional owner will ever pay. Every player-facing
    /// "ask" should quote THIS, so the market list, the negotiating table and
    /// the deal that gets signed all tell the same story.
    /// </summary>
    public static Money EffectiveAskingPurse(Promotion promo, Fighter f, ContractExpectations exp) =>
        new(RoundTo(exp.AskingPurse.Amount
            * (WantsExclusivity(promo) ? 1m : NonExclusiveMoneyFactor)
            * GratitudeFactor(f, promo.Id), 500m));

    /// <summary>
    /// <see cref="EffectiveAskingPurse(Promotion, Fighter, ContractExpectations)"/>
    /// with the lifer's hometown discount folded in — the number his OWN
    /// promotion will actually be quoted at the table.
    /// </summary>
    public static Money EffectiveAskingPurse(World world, Promotion promo, Fighter f, ContractExpectations exp) =>
        new(RoundTo(exp.AskingPurse.Amount
            * (WantsExclusivity(promo) ? 1m : NonExclusiveMoneyFactor)
            * GratitudeFactor(f, promo.Id)
            * Lifers.HomeDiscount(world, f, promo.Id), 500m));

    /// <summary>
    /// The biggest per-fight purse a promotion's economics support, derived from
    /// its expected box office: what one card grosses in its usual building,
    /// less production, spread across a full card's purses. A small-hall org
    /// pays small-hall money; a fighter who outgrows the budget must move up.
    /// </summary>
    public static decimal PurseCap(World world, Promotion promo)
    {
        int prestige = promo.Prestige.Value;
        int homePop = promo.HomeCity is { } hq
            ? Geography.Reputation.OrgInCity(promo, hq)
            : Geography.Reputation.OrgInRegion(world, promo, promo.BaseRegion);
        decimal attendance = 2_500m + prestige * 110m + 35m * 35m + homePop * 40m;
        var venue = Geography.WorldMap.PickVenue(world, promo);
        if (venue is not null) attendance = Math.Min(attendance, venue.Capacity);

        decimal gate = attendance * (55m + prestige * 1.4m);
        decimal production = 10_000m + attendance * 12m + (venue?.Rent.Amount ?? 0m);
        // A full card is ~16 purses + 8 win bonuses at 0.75 → ~22 purse-units.
        return Math.Max(2_000m, Math.Round((gate - production) * 0.9m / 22m / 500m) * 500m);
    }

    /// <summary>One offer's per-fight worth, all levers blended — shared by AI bidding and the player's negotiating room.</summary>
    public static decimal MoneyValue(ContractOffer o)
    {
        decimal signingPerFight = o.Fights > 0 ? o.SigningBonus.Amount / o.Fights : o.SigningBonus.Amount;
        return o.PursePerFight.Amount + 0.5m * o.WinBonus.Amount + signingPerFight
             + 0.25m * o.PerformanceBonus.Amount + o.PpvPoints * PpvPointValue;
    }

    /// <summary>The ask on the same per-fight scale as <see cref="MoneyValue"/>.</summary>
    public static decimal AskMoney(ContractExpectations e) =>
        e.AskingPurse.Amount + 0.5m * e.AskingWinBonus.Amount;

    /// <summary>How much the fighter likes this org regardless of money (title path, loyalty, home, bonds), 0..1.</summary>
    public static double Appeal(World world, Fighter f, ContractOffer offer, ContractExpectations exp)
    {
        var promo = world.Get(offer.Promotion);
        double prestige = (promo?.Prestige.Value ?? 40) / 100.0;
        double title = prestige * (exp.Priorities.TitleOpportunity / 100.0);
        double loyalty = (f.Contract?.Promotion == offer.Promotion ? 1.0 : 0.0) * (exp.Priorities.Loyalty / 100.0);
        // "Fighting at home" is strongest in your own country; the same continent still counts for half.
        double homeMatch = promo is null ? 0.0
            : promo.HomeCity is { } hq && world.Get(hq)?.Country == f.Country ? 1.0
            : promo.BaseRegion == f.Nationality ? 0.5
            : 0.0;
        double home = homeMatch * (exp.Priorities.HomeRegion / 100.0);
        // Your people pull you in: a friend or family member already on the roster
        // makes the org home turf — fighters sign where their circle trains. An org
        // that SPONSORS your camp is already part of the family.
        double bonds = world.Relationships.Any(r => r.Involves(f.Id)
                && world.Get(r.Other(f.Id))?.Contract?.Promotion == offer.Promotion)
            || (promo?.SponsoredCamp is { } sc && f.Camp == sc) ? 1.0 : 0.0;
        return Math.Clamp(title * 0.45 + loyalty * 0.3 + home * 0.15 + bonds * 0.1, 0, 1);
    }

    private ContractOffer BuildCounter(ContractOffer offer, ContractExpectations exp, double threshold)
    {
        int fights = exp.DesiredFights;
        decimal winBonus = exp.AskingWinBonus.Amount;
        decimal signing = exp.WantsSigningBonus
            ? Math.Max(offer.SigningBonus.Amount, RoundTo(exp.AskingPurse.Amount * 0.5m, 500m))
            : offer.SigningBonus.Amount;

        decimal targetMoney = (decimal)threshold * AskMoney(exp) * (offer.Exclusive ? 1m : NonExclusiveMoneyFactor)
                            * (offer.ChampionsClause ? ChampionsClausePremium : 1m);
        decimal signingPerFight = fights > 0 ? signing / fights : signing;
        decimal purse = targetMoney - 0.5m * winBonus - signingPerFight
                      - 0.25m * offer.PerformanceBonus.Amount - offer.PpvPoints * PpvPointValue;
        purse = Math.Max(exp.AskingPurse.Amount * 0.6m, RoundTo(purse, 500m));

        return new ContractOffer
        {
            Fighter = offer.Fighter,
            Promotion = offer.Promotion,
            PursePerFight = new Money(purse),
            WinBonus = new Money(winBonus),
            Fights = fights,
            SigningBonus = new Money(signing),
            PpvPoints = offer.PpvPoints,
            PerformanceBonus = offer.PerformanceBonus,
            Exclusive = offer.Exclusive,
            ChampionsClause = offer.ChampionsClause,
            Round = offer.Round + 1,
        };
    }

    private static string RejectReason(double ratio, bool signingOk, bool lengthOk) =>
        ratio < 0.7 ? "That offer is well short of my worth."
        : !signingOk ? "No signing bonus, no deal."
        : !lengthOk ? "The length doesn't work for me."
        : "We're too far apart.";

    private static string CounterReason(double ratio, bool signingOk, bool lengthOk) =>
        !signingOk ? "Close, but I need a signing bonus."
        : !lengthOk ? "Close on money — let's fix the length."
        : "Bump the purse and we have a deal.";

    private static decimal RoundTo(decimal v, decimal step) => Math.Round(v / step) * step;
}
