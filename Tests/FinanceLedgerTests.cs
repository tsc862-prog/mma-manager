using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Finance;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Tests;

public class FinanceLedgerTests
{
    [Fact]
    public void MonthlyOverhead_ChargesEveryPromotion()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 4 });
        var before = world.Promotions.Values.ToDictionary(p => p.Id, p => p.Balance.Amount);

        Ledger.ChargeMonthlyOverhead(world, world.CurrentDate);

        foreach (var p in world.Promotions.Values)
        {
            Assert.True(p.Balance.Amount < before[p.Id], "overhead should reduce the balance");
            var entry = p.Ledger.Last();
            Assert.Equal(FinanceCategory.Overhead, entry.Category);
            Assert.True(entry.Amount.Amount < 0);
        }
    }

    [Fact]
    public void EventSettlement_RecordsCategorisedEntriesNettingToProfit()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 6 });
        var promo = world.Promotions.Values.OrderByDescending(p => p.Prestige.Value).First();
        decimal before = promo.Balance.Amount;

        var ev = new EventService().BookAndRun(world, promo.Id, "Test Card", world.CurrentDate, new Matchmaker());
        decimal net = ev.Financials!.NetProfit.Amount;

        var settlement = promo.Ledger
            .Where(e => e.Date == world.CurrentDate && e.Category is
                FinanceCategory.EventGate or FinanceCategory.EventPpv or
                FinanceCategory.Production or FinanceCategory.FighterPurse)
            .ToList();

        Assert.NotEmpty(settlement);
        Assert.Equal(net, settlement.Sum(e => e.Amount.Amount));

        // Fight night also settles the commercial side — sponsor checks, media
        // rights, the merch table — as its own categorised entries on top.
        decimal commercial = promo.Ledger
            .Where(e => e.Date == world.CurrentDate && e.Category is
                FinanceCategory.Sponsorship or FinanceCategory.Broadcast or FinanceCategory.Merchandise)
            .Sum(e => e.Amount.Amount);
        Assert.True(commercial > 0, "a big org's card should earn sponsor and rights money");
        Assert.Equal(before + net + commercial, promo.Balance.Amount);
    }

    [Fact]
    public void Ledger_AccountsForEveryBalanceMovement()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 8 });
        var start = world.Promotions.Values.ToDictionary(p => p.Id, p => p.Balance.Amount);

        new WorldTick().AdvanceWeeks(world, 26);

        // Reconcile the orgs whose starting cash we captured. (Promotions founded
        // mid-run start from their own founding balance — a separate starting point,
        // not a ledger movement — so they're out of scope for this snapshot check.)
        foreach (var p in world.Promotions.Values.Where(p => start.ContainsKey(p.Id)))
        {
            decimal ledgerSum = p.Ledger.Sum(e => e.Amount.Amount);
            Assert.Equal(p.Balance.Amount - start[p.Id], ledgerSum);
        }
    }
}
