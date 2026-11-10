using MmaManager.Core.Model;
using MmaManager.Core.Sim.Finance;

namespace MmaManager.Core.Sim.Org;

/// <summary>
/// Fighter marketing: a paid promo campaign the player runs on a specific fighter
/// to build them into a draw. While the push is on, the fighter's popularity climbs
/// month over month (faster with a strong Marketing department behind it, and less
/// the more famous they already are), and the campaign is billed each month. It's a
/// deliberate money sink — turning a talented unknown into a name that sells tickets.
/// </summary>
public static class MarketingService
{
    /// <summary>Monthly cost of an active push — a bigger star costs more to keep in the spotlight.</summary>
    public static Money MonthlyCost(Fighter f) =>
        new(8_000m + f.Intangibles.Popularity.Value * 450m);

    /// <summary>Popularity gained this month from an active push, amplified by the marketing dept.</summary>
    public static int MonthlyGain(Promotion promo, Fighter f)
    {
        double baseGain = Math.Max(1.0, (100 - f.Intangibles.Popularity.Value) * 0.06);   // diminishing near the top
        double amplified = baseGain * (1.0 + StaffService.MarketingBonus(promo) * 2.0);
        return Math.Max(1, (int)Math.Round(amplified));
    }

    /// <summary>
    /// Run every active push for one month: grow the fighter and bill the promotion.
    /// A campaign the org can no longer afford is quietly switched off.
    /// </summary>
    public static void RunMonthly(World world, DateOnly date)
    {
        foreach (var promo in world.Promotions.Values.Where(p => !p.Defunct))
        {
            foreach (var f in world.RosterOf(promo.Id).Where(f => f.MarketingPush && f.Status != FighterStatus.Retired))
            {
                var cost = MonthlyCost(f);
                if (promo.Balance.Amount < cost.Amount) { f.MarketingPush = false; continue; }
                Ledger.Expense(promo, date, FinanceCategory.FighterMarketing, cost, $"Marketing push — {f.FullName}");
                int gain = MonthlyGain(promo, f);
                // A campaign can only build fame up to the promotion's reach — you can't
                // market a fighter past the ceiling of the stage they fight on.
                int cap = Math.Max(f.Intangibles.Popularity.Value, OrgSizing.FameCeiling(OrgSizing.SizeOf(promo)));
                f.Intangibles.Popularity = Math.Min(cap, f.Intangibles.Popularity.Value + gain);
            }
        }
    }
}
