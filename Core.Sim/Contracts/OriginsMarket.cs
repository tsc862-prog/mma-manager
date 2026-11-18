using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Contracts;

/// <summary>
/// One read of an Origins market (performance): the sport's ceiling (<see cref="FighterValuation.OriginsMarketCeiling"/>)
/// and every live house's cap as the market reads it (<see cref="Negotiator.MarketPurseCap"/>), taken in one go. Each cap
/// walks the event calendar, so a valuation that reads the ceiling and a table that then weighs the house's tier
/// (<see cref="Negotiator.HouseTier"/>) would otherwise walk it once per house, twice over.
///
/// It is a value handed DOWN a call chain, never stored: <see cref="FighterValuation.Evaluate(World, Fighter, out OriginsMarket?)"/>
/// returns it with the expectations, and the caller hands both to the table calls it makes for that same man straight
/// after (<see cref="Negotiator.AiOpeningOffer"/>, <see cref="Negotiator.Evaluate"/>), with nothing in the world changed
/// in between. Those calls then read exactly the numbers they would have computed themselves. A caller that changes the
/// world (a signing) must take a new read for the next man.
/// </summary>
public sealed class OriginsMarket
{
    private readonly Dictionary<PromotionId, decimal> _caps;

    private OriginsMarket(decimal ceiling, Dictionary<PromotionId, decimal> caps) { Ceiling = ceiling; _caps = caps; }

    /// <summary>The sport's best purse cap among live houses: <see cref="FighterValuation.OriginsMarketCeiling"/>.</summary>
    public decimal Ceiling { get; }

    /// <summary>Read the market as it stands (the same houses, order and rule as <see cref="FighterValuation.OriginsMarketCeiling"/>).</summary>
    public static OriginsMarket Read(World world)
    {
        var caps = new Dictionary<PromotionId, decimal>();
        decimal best = 0m;
        foreach (var p in world.Promotions.Values)
        {
            if (p.Defunct) continue;
            decimal cap = Negotiator.MarketPurseCap(world, p);
            caps[p.Id] = cap;
            best = Math.Max(best, cap);
        }
        return new OriginsMarket(best, caps);
    }

    /// <summary>The house's market cap from this read (<see cref="Negotiator.MarketPurseCap"/>; read fresh for a house not in it).</summary>
    public decimal CapOf(World world, Promotion p) => _caps.TryGetValue(p.Id, out var c) ? c : Negotiator.MarketPurseCap(world, p);
}
