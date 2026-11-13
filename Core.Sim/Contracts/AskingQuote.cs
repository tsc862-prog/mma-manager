using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Contracts;

/// <summary>
/// The purse a fighter would open at across THIS promotion's negotiating table,
/// and why it differs from his open-market rate, in plain words. Every
/// player-facing "asking" figure (the market list, the negotiating table panel,
/// the fighter profile, the scout's pick) reads this, so they all quote the
/// number the room (<see cref="NegotiationSession.DemandPurse"/>) opens at:
/// the open-market ask, scaled by the deal's terms (a non-exclusive deal comes
/// cheaper, a champion's clause costs extra) and his personal discount — a
/// lifer's hometown rate or a discovered local's gratitude, the bigger of the
/// two (<see cref="Negotiator.TableDiscount"/>) — and never under a live deal's show money
/// (<see cref="Negotiator.LivePurseFloor"/>). Reads only; changes nothing.
/// </summary>
/// <param name="Purse">What he opens at, per fight, across this table.</param>
/// <param name="MarketRate">His open-market (exclusive) asking purse.</param>
/// <param name="Reasons">Why <paramref name="Purse"/> differs from the market rate, plain words; empty when it doesn't.</param>
public sealed record AskingQuote(Money Purse, Money MarketRate, IReadOnlyList<string> Reasons)
{
    /// <summary>The reasons as one clause ("loyal to you: asks less to stay; non-exclusive deal"), or "".</summary>
    public string Why => string.Join("; ", Reasons);

    /// <summary>
    /// The opening ask at <paramref name="promo"/>'s table on the terms the room
    /// starts from (its usual exclusivity, no champion's clause), or on the given
    /// terms when the player toggles them.
    /// </summary>
    public static AskingQuote For(World world, Promotion promo, Fighter f, ContractExpectations exp,
        bool? exclusive = null, bool clause = false)
    {
        bool excl = exclusive ?? Negotiator.WantsExclusivity(promo);
        decimal home = Lifers.HomeDiscount(world, f, promo.Id);
        decimal grateful = Negotiator.GratitudeFactor(f, promo.Id);
        decimal personal = Negotiator.TableDiscount(world, f, promo.Id);
        // The same factor and rounding as NegotiationSession.DemandPurse at its opening position.
        decimal distrust = Promises.Distrust(world, f.Id, promo.Id, world.CurrentDate);
        decimal factor = (excl ? 1m : Negotiator.NonExclusiveMoneyFactor)
                       * (clause ? Negotiator.ChampionsClausePremium : 1m)
                       * personal * distrust;
        decimal discounted = NegotiationSession.UpTo500(exp.AskingPurse.Amount * factor);
        var floor = Negotiator.LivePurseFloor(f, promo.Id);
        decimal purse = Math.Max(floor?.Amount ?? 0m, discounted);

        var reasons = new List<string>();
        // The one discount that applies (they don't stack): name that one.
        if (personal < 1m && home <= grateful) reasons.Add(Lifers.Gendered(f, "loyal to you: he asks less to stay"));
        else if (personal < 1m) reasons.Add(Lifers.Gendered(f, "you discovered him: he asks less from you"));
        if (!excl) reasons.Add("non-exclusive deal");
        if (clause) reasons.Add("the champion's clause adds ~8%");
        if (distrust > 1m) reasons.Add(Lifers.Gendered(f, "you broke your word to him: he asks more"));
        if (floor is { } fl && fl.Amount > discounted)
            reasons.Add(Lifers.Gendered(f, $"won't take less than his current {fl}"));
        return new AskingQuote(new Money(purse), exp.AskingPurse, reasons);
    }
}
