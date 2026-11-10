using MmaManager.Core.Model;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Finance;

/// <summary>
/// The commercial side of the books, orchestrating <see cref="Sponsorships"/>
/// and <see cref="MediaService"/>. Nearly all income is earned on FIGHT NIGHT —
/// sponsors and rights partners pay per event, merchandise sells to the house —
/// so a promotion that goes dark earns almost nothing while its overhead keeps
/// billing. The monthly tick only runs the subscription platform, the deal
/// market (offers, renewals, cadence clocks) and a mail-order merch trickle
/// that dries up without shows to sell. Deterministic.
/// </summary>
public static class RevenueService
{
    /// <summary>Seed a world-build promotion's commercial slate to match its tier.</summary>
    public static void SeedEstablished(World world, Promotion promo, DateOnly on, SimRandom rng)
    {
        Sponsorships.SeedEstablished(world, promo, on, rng);
        MediaService.SeedEstablished(world, promo, on, rng);
    }

    /// <summary>A founded promotion's day one: one local backer, an offer, and empty lanes to earn.</summary>
    public static void SeedFounding(World world, Promotion promo, DateOnly on, SimRandom rng) =>
        Sponsorships.SeedFounding(world, promo, on, rng);

    /// <summary>The monthly commercial tick for every live promotion.</summary>
    public static void CollectMonthly(World world, DateOnly date)
    {
        var rng = new SimRandom(SimRandom.Mix(world.RngSeed,
            (ulong)(date.Year * 12 + date.Month) ^ 0xC0FF_EEDE_A1UL));

        foreach (var promo in world.Promotions.Values.Where(p => !p.Defunct))
        {
            Sponsorships.RunMonthly(world, promo, date, rng);
            MediaService.RunMonthly(world, promo, date, rng);

            // Mail-order merchandise: a trickle that follows the brand while it's
            // ALIVE — prestige sets the ceiling, recent shows set how much of it
            // actually sells. No cards in four months = nothing to wear.
            double activity = Math.Min(1.0, MediaService.EventsInWindow(world, promo, 120) / 3.0);
            Ledger.Income(promo, date, FinanceCategory.Merchandise,
                new Money(promo.Prestige.Value * 1_500m * (decimal)activity), "Merchandise (mail order)");
        }
    }

    /// <summary>
    /// Fight night's commercial settlement: sponsor checks, media rights,
    /// the platform's subscriber bump, and the merch table by the door.
    /// </summary>
    public static void SettleEvent(World world, Promotion promo, Event ev)
    {
        Sponsorships.SettleEvent(world, promo, ev);
        MediaService.SettleEvent(world, promo, ev);

        int attendance = ev.Financials?.Attendance ?? 0;
        if (attendance > 0)
            Ledger.Income(promo, ev.Date, FinanceCategory.Merchandise,
                new Money(attendance * (6m + promo.Prestige.Value * 0.08m)), $"{ev.Name} — merch table");
    }
}
