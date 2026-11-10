using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Org;

/// <summary>
/// Brand marketing: a promotion can pour a monthly budget into promoting ITSELF —
/// filling more seats and building its standing so it climbs to the next tier
/// faster. Deliberately SATURATING: money moves the needle less the bigger you
/// already are (a City org saturates its little market cheaply; a global brand has
/// to spend millions to shift a single point). And it can only ACCELERATE a climb
/// the product can sustain — the house is a fraction of a show's quality, so you
/// still can't market a bad card to the summit. A real, scalable place to sink cash.
/// </summary>
public static class OrgMarketing
{
    /// <summary>
    /// Spend needed to reach half the effect. Grows with prestige (a global brand
    /// costs more to market than a local one) but only mildly super-linear, so money
    /// still goes a real distance at the top — a few hundred k/mo actually registers,
    /// and dropping millions genuinely moves the needle.
    /// </summary>
    private static decimal Saturation(Promotion p) => 45_000m + p.Prestige.Value * (decimal)p.Prestige.Value * 220m;

    /// <summary>How hard the campaign is running, 0 (nothing) → ~1 (saturated).</summary>
    public static double Intensity(Promotion p)
    {
        decimal b = p.MarketingBudget.Amount;
        return b <= 0 ? 0 : (double)(b / (b + Saturation(p)));
    }

    /// <summary>Extra house from the campaign (up to +75%) — the gate/money lever.</summary>
    public static double AttendanceBonus(Promotion p) => Intensity(p) * 0.75;

    /// <summary>
    /// Show-quality bump the campaign adds to the climb engine (up to +11). A strong
    /// accelerant, but NOT enough to buy a whole tier on its own — the summit stays
    /// exclusive to promotions whose product genuinely belongs there (or that pour in
    /// truly relentless money on top of a good one).
    /// </summary>
    public static double StandingQualityBonus(Promotion p) => Intensity(p) * 11.0;

    /// <summary>A sensible budget cap so the UI/AI don't pour in money past the point of near-saturation.</summary>
    public static Money SuggestedMax(Promotion p) => new(System.Math.Round(Saturation(p) * 4m / 50_000m) * 50_000m);

    /// <summary>Bill every promotion's monthly brand-marketing budget; trims a spend it can't cover.</summary>
    public static void ChargeMonthly(World world, DateOnly date)
    {
        foreach (var promo in world.Promotions.Values.Where(p => !p.Defunct && p.MarketingBudget.Amount > 0))
        {
            if (promo.Balance.Amount < promo.MarketingBudget.Amount)
                promo.MarketingBudget = new Money(System.Math.Max(0, promo.Balance.Amount * 0.5m));
            if (promo.MarketingBudget.Amount <= 0) continue;
            Finance.Ledger.Expense(promo, date, FinanceCategory.OrgMarketing, promo.MarketingBudget, "Brand marketing campaign");
        }
    }
}
