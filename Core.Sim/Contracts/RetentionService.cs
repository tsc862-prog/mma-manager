using MmaManager.Core.Model;
using MmaManager.Core.Sim.Finance;

namespace MmaManager.Core.Sim.Contracts;

/// <summary>
/// Paying a fighter to stay. A retention bonus is a lump sum that buys LOYALTY —
/// the price a rival must pay to pry them loose — so a promotion can spend to keep
/// the homegrown stars it develops instead of watching the giants poach them every
/// contract cycle. Bigger stars and bigger loyalty jumps cost more, and each bonus
/// buys less than the last (loyalty has diminishing returns as it nears the max),
/// so you can't cheaply buy a lifer overnight — it's a real, ongoing investment.
/// </summary>
public static class RetentionService
{
    /// <summary>How many loyalty points a bonus buys — smaller the more loyal they already are.</summary>
    public static int LoyaltyGain(Fighter f) => Math.Max(3, (int)Math.Round((100 - f.Loyalty) * 0.4));

    /// <summary>
    /// What the next retention bonus costs — scales with the fighter's PUBLIC
    /// standing (his reputation, <see cref="PublicStanding"/>, plus his draw) and
    /// the jump bought. The price is shown to the player, so it must not read
    /// ratings the fog hides.
    /// </summary>
    public static Money Quote(World world, Fighter f)
    {
        int value = (int)Math.Round(PublicStanding.Reputation(world, f)) + f.Intangibles.Popularity.Value;
        decimal cost = value * LoyaltyGain(f) * 180m;
        return new Money(Math.Round(cost / 1_000m) * 1_000m);   // to the nearest $1k
    }

    public static bool CanPay(World world, Promotion promo, Fighter f, out string reason)
    {
        reason = "";
        if (f.Contract?.Promotion != promo.Id) { reason = "Not on your roster."; return false; }
        if (f.Loyalty >= 100) { reason = "Already as loyal as they get."; return false; }
        if (promo.Balance.Amount < Quote(world, f).Amount) { reason = "Can't afford the bonus."; return false; }
        return true;
    }

    /// <summary>Pay the bonus: bank the loyalty, charge the books. False (with a reason) if not allowed.</summary>
    public static bool TryPayBonus(World world, Promotion promo, Fighter f, out string reason)
    {
        if (!CanPay(world, promo, f, out reason)) return false;
        int gain = LoyaltyGain(f);
        var cost = Quote(world, f);
        f.Loyalty = Math.Min(100, f.Loyalty + gain);
        Ledger.Expense(promo, world.CurrentDate, FinanceCategory.RetentionBonus, cost,
            $"Retention bonus — {f.FullName} (+{gain} loyalty)");
        return true;
    }
}
