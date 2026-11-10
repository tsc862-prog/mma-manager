using MmaManager.Core.Model;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Core.Sim.Org;

/// <summary>
/// Companies can die. A promotion that sinks past the debt ceiling closes its
/// doors: every fighter hits the open market at once, the belts are vacated,
/// booked cards are cancelled, and the org goes <see cref="Promotion.Defunct"/> —
/// out of every running system but kept in the world so history resolves.
/// The player is warned on the way down and goes bankrupt by the same rule:
/// that's the game over.
/// </summary>
public static class Solvency
{
    /// <summary>A small club's closing line — the floor of the sliding scale.</summary>
    public const decimal DebtCeiling = -300_000m;

    /// <summary>
    /// How deep a promotion can go before it folds — it scales with stature. A
    /// regional club is done at a few hundred grand in the red; an established
    /// international brand has the backing to weather millions, so a big org
    /// almost never dies on the books (a sustained, years-long bleed would still
    /// do it). Keeps bankruptcy a real threat where it belongs: the small and
    /// the newly founded.
    /// </summary>
    public static decimal DebtCeilingFor(Promotion promo) => OrgSizing.SizeOf(promo) switch
    {
        OrgSize.International => -8_000_000m,
        OrgSize.Continental => -3_000_000m,
        OrgSize.National => -1_000_000m,
        OrgSize.Regional => -400_000m,
        _ => DebtCeiling,   // City
    };

    /// <summary>
    /// How financially distressed a promotion is: 0 = healthy (build freely),
    /// 1 = in the red (stop shopping for names — rebuild with cheap developable
    /// talent), 2 = past half its debt ceiling (emergency — bare-minimum bodies
    /// only). Drives the AI's roster discipline so nobody signs into the grave.
    /// </summary>
    public static int Distress(Promotion promo) =>
        promo.Balance.Amount >= 0 ? 0
        : promo.Balance.Amount <= DebtCeilingFor(promo) * 0.5m ? 2 : 1;

    /// <summary>
    /// What fraction of the org's normal purse cap a distressed front office will
    /// still commit to one fighter — deep bargain territory, so the roster is
    /// rebuilt from prospects rather than names while the books recover.
    /// </summary>
    public static decimal DistressedPurseFactor(int distress) => distress >= 2 ? 0.15m : 0.35m;

    public static void RunMonthly(World world, DateOnly date)
    {
        foreach (var promo in world.Promotions.Values.Where(p => !p.Defunct).ToList())
        {
            bool isPlayer = promo.Id == world.PlayerPromotion;
            decimal ceiling = DebtCeilingFor(promo);

            if (promo.Balance.Amount > ceiling)
            {
                // The player gets fair warning on the way down.
                if (isPlayer && promo.Balance.Amount < 0)
                    Mail.Post(world, date, MailCategory.BoardNote, "The Accountant",
                        promo.Balance.Amount < ceiling / 2 ? "BANKRUPTCY IMMINENT" : "We are in the red",
                        $"The books stand at {promo.Balance}. If we hit {new Money(ceiling)}, the doors close for good.");
                continue;
            }

            Close(world, promo, date);
        }
    }

    /// <summary>Fold the promotion: release everyone, vacate the belts, cancel the cards.</summary>
    public static void Close(World world, Promotion promo, DateOnly date)
    {
        PromotionLadder.OnSeatFreed(world, OrgSizing.SizeOf(promo));   // a death makes room at its level
        promo.Defunct = true;

        int released = 0;
        foreach (var f in world.RosterOf(promo.Id).ToList())
        {
            f.Contract = null;   // the company is broke — nobody gets a payout
            released++;
        }
        foreach (var d in promo.Divisions) d.Rankings.Clear();   // no roster, no contenders

        foreach (var tid in promo.Titles)
        {
            if (world.Get(tid) is not { } title) continue;
            var reign = title.Lineage.LastOrDefault(r => r.LostOn is null);
            if (reign is not null) reign.LostOn = date;
            title.CurrentChampion = null;
        }

        foreach (var ev in world.Events.Values.Where(e => e.Promotion == promo.Id && e.Status == EventStatus.Scheduled))
            ev.Status = EventStatus.Cancelled;

        promo.SponsorDeals.Clear();
        promo.MediaDeals.Clear();
        promo.Offers.Clear();
        promo.Stream = null;

        News.Post(world, date, NewsCategory.Finance,
            $"{promo.Name} closes its doors",
            $"Insolvent and out of road — {released} fighters hit the open market overnight.", promo.Id);

        Mail.Post(world, date, MailCategory.BoardNote, "The Accountant",
            promo.Id == world.PlayerPromotion
                ? $"It's over — {promo.Name} is bankrupt"
                : $"{promo.Name} has folded",
            promo.Id == world.PlayerPromotion
                ? "The debt ceiling is breached and the doors are closed. The story ends here — start a new game."
                : $"{released} of their fighters just hit free agency. If we move fast, there are bargains in that wreckage.");
    }
}
