using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Org;

namespace MmaManager.Tests;

/// <summary>
/// The money sinks that give the player something to spend on: retention bonuses
/// (buy loyalty so the giants can't poach your stars) and fighter marketing pushes
/// (pay to build a draw). Scouting lives in <see cref="ScoutingTests"/>.
/// </summary>
public class SpendingTests
{
    private static (World w, Promotion p, Fighter f) Setup(ulong seed = 5)
    {
        var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });
        var p = w.Promotions.Values.OrderByDescending(x => x.Prestige.Value).First();
        p.Balance = new Money(10_000_000m);
        var f = w.RosterOf(p.Id).First();
        return (w, p, f);
    }

    // --- Retention bonuses --------------------------------------------------

    [Fact]
    public void ARetentionBonus_BuysLoyalty_AndCostsMoney()
    {
        var (w, p, f) = Setup();
        f.Loyalty = 30;
        decimal bank = p.Balance.Amount;
        var quote = RetentionService.Quote(w, f);
        int expectedGain = RetentionService.LoyaltyGain(f);

        Assert.True(RetentionService.TryPayBonus(w, p, f, out _));

        Assert.Equal(30 + expectedGain, f.Loyalty);
        Assert.Equal(bank - quote.Amount, p.Balance.Amount);
        Assert.Contains(p.Ledger, e => e.Category == FinanceCategory.RetentionBonus);
    }

    [Fact]
    public void RetentionBonuses_BuyLess_TheMoreLoyalTheyAlreadyAre()
    {
        var (_, _, f) = Setup();
        f.Loyalty = 20;
        int lowGain = RetentionService.LoyaltyGain(f);
        f.Loyalty = 85;
        int highGain = RetentionService.LoyaltyGain(f);
        Assert.True(lowGain > highGain, $"a bonus should buy more loyalty low ({lowGain}) than high ({highGain})");
    }

    [Fact]
    public void CantPayToRetain_AFighterWhoIsntYours()
    {
        var (w, p, _) = Setup();
        var outsider = w.Fighters.Values.First(x => x.Contract is { } c && c.Promotion != p.Id);
        Assert.False(RetentionService.TryPayBonus(w, p, outsider, out var reason));
        Assert.False(string.IsNullOrEmpty(reason));
    }

    [Fact]
    public void CantPayARetentionBonus_YouCantAfford()
    {
        var (w, p, f) = Setup();
        f.Loyalty = 20;
        p.Balance = new Money(1_000m);   // broke
        Assert.False(RetentionService.TryPayBonus(w, p, f, out _));
        Assert.Equal(20, f.Loyalty);
    }

    // --- Fighter marketing --------------------------------------------------

    [Fact]
    public void AMarketingPush_GrowsPopularity_AndBillsTheOrg()
    {
        var (w, p, f) = Setup();
        f.Intangibles.Popularity = 30;
        f.MarketingPush = true;
        decimal bank = p.Balance.Amount;

        MarketingService.RunMonthly(w, w.CurrentDate);

        Assert.True(f.Intangibles.Popularity.Value > 30, "an active push should grow popularity");
        Assert.True(p.Balance.Amount < bank, "the push should be billed");
        Assert.Contains(p.Ledger, e => e.Category == FinanceCategory.FighterMarketing);
    }

    [Fact]
    public void NoPush_NoEffect()
    {
        var (w, p, f) = Setup();
        f.Intangibles.Popularity = 30;
        f.MarketingPush = false;
        decimal bank = p.Balance.Amount;

        MarketingService.RunMonthly(w, w.CurrentDate);

        Assert.Equal(30, f.Intangibles.Popularity.Value);
        Assert.Equal(bank, p.Balance.Amount);
    }

    // --- Org marketing + fame ceiling --------------------------------------

    [Fact]
    public void OrgMarketing_MoreMoneyBuysMoreEffect_WithDiminishingReturns()
    {
        var (_, p, _) = Setup();
        p.Prestige = 40;
        p.MarketingBudget = Money.Zero;
        Assert.Equal(0, OrgMarketing.Intensity(p));

        p.MarketingBudget = new Money(500_000m);
        double lo = OrgMarketing.Intensity(p);
        p.MarketingBudget = new Money(5_000_000m);
        double hi = OrgMarketing.Intensity(p);

        Assert.True(hi > lo && lo > 0, "more budget, more intensity");
        Assert.True(hi < 1.0, "it saturates — you can't buy infinite effect");
        Assert.True(OrgMarketing.AttendanceBonus(p) > 0 && OrgMarketing.StandingQualityBonus(p) > 0);
    }

    [Fact]
    public void BiggerBrands_CostMoreToMarketTheSameAmount()
    {
        var (_, small, _) = Setup();
        var (_, big, _) = Setup();
        small.Prestige = 20;
        big.Prestige = 70;
        small.MarketingBudget = big.MarketingBudget = new Money(1_000_000m);
        Assert.True(OrgMarketing.Intensity(small) > OrgMarketing.Intensity(big),
            "the same million does more for a small brand than a global one");
    }

    [Fact]
    public void FameCeiling_RisesWithTheStage_ToppingAt100Only_Globally()
    {
        Assert.True(OrgSizing.FameCeiling(OrgSize.City) < OrgSizing.FameCeiling(OrgSize.Regional));
        Assert.True(OrgSizing.FameCeiling(OrgSize.Regional) < OrgSizing.FameCeiling(OrgSize.National));
        Assert.True(OrgSizing.FameCeiling(OrgSize.National) < OrgSizing.FameCeiling(OrgSize.Continental));
        Assert.True(OrgSizing.FameCeiling(OrgSize.Continental) < 100);
        Assert.Equal(100, OrgSizing.FameCeiling(OrgSize.International));
    }

    [Fact]
    public void AMarketingPush_CantBuildFame_PastTheOrgsReach()
    {
        var (w, p, f) = Setup();
        p.Prestige = 12;                       // a City-level promotion
        f.Intangibles.Popularity = 30;
        f.MarketingPush = true;

        for (int i = 0; i < 40; i++) MarketingService.RunMonthly(w, w.CurrentDate);

        Assert.True(f.Intangibles.Popularity.Value <= OrgSizing.FameCeiling(OrgSize.City),
            $"a City card can't mint a star (pop {f.Intangibles.Popularity.Value})");
    }

    [Fact]
    public void AnUnaffordablePush_SwitchesItselfOff()
    {
        var (w, p, f) = Setup();
        f.MarketingPush = true;
        p.Balance = new Money(500m);   // can't cover a month

        MarketingService.RunMonthly(w, w.CurrentDate);

        Assert.False(f.MarketingPush, "a campaign the org can't afford should quietly end");
    }

    // --- The AI front office's reserve ---------------------------------------

    [Fact]
    public void AnAiFrontOffice_KeepsAQuarterOfRunningCosts_BeforeItMarkets()
    {
        // The flagship used to market down to a $5M floor while spending ~$20M a
        // quarter on purses: no runway at all when income dipped.
        var (w, p, _) = Setup();
        w.PlayerPromotion = default;
        p.Ledger.Clear();
        var day = w.CurrentDate;
        for (int i = 0; i < 6; i++)
            p.Ledger.Add(new LedgerEntry(day.AddDays(-80 + i * 14), FinanceCategory.FighterPurse, new Money(-3_000_000m), "purses"));
        // Discretionary spend and old costs don't raise the bar.
        p.Ledger.Add(new LedgerEntry(day.AddDays(-60), FinanceCategory.OrgMarketing, new Money(-5_000_000m), "campaign"));
        p.Ledger.Add(new LedgerEntry(day.AddDays(-30), FinanceCategory.RetentionBonus, new Money(-700_000m), "bonus"));
        p.Ledger.Insert(0, new LedgerEntry(day.AddDays(-200), FinanceCategory.FighterPurse, new Money(-9_000_000m), "last year"));
        p.Ledger.Sort((a, b) => a.Date.CompareTo(b.Date));

        Assert.Equal(18_000_000m, AiFrontOffice.QuarterRunningCosts(p, day));
        Assert.Equal(18_000_000m * AiFrontOffice.ReserveShare, AiFrontOffice.Buffer(p, day));

        p.Balance = new Money(AiFrontOffice.Buffer(p, day) - 500_000m);   // above the old prestige floor, under the reserve
        Assert.True(p.Balance.Amount > AiFrontOffice.PrestigeFloor(p));
        AiFrontOffice.RunMonthly(w, day);
        Assert.Equal(0m, p.MarketingBudget.Amount);

        p.Balance = new Money(40_000_000m);
        AiFrontOffice.RunMonthly(w, day);
        Assert.True(p.MarketingBudget.Amount > 0, "real surplus above the reserve still funds a campaign");
    }

    [Fact]
    public void ASmallOrgsReserve_IsStillThePrestigeFloor()
    {
        var (w, p, _) = Setup();
        p.Ledger.Clear();
        p.Prestige = 20;
        Assert.Equal(800_000m + 20 * 60_000m, AiFrontOffice.Buffer(p, w.CurrentDate));
    }

    [Fact]
    public void OneOffs_DontRaiseTheReserve_RoutineBuyoutsDo()
    {
        var (w, p, _) = Setup();
        p.Ledger.Clear();
        var day = w.CurrentDate;
        p.Ledger.Add(new LedgerEntry(day.AddDays(-70), FinanceCategory.ContractBuyout, new Money(-400_000m), "Contract buyout — a journeyman"));
        // A takeover: the price and, the same day, the target's cut fighters paid out of it.
        p.Ledger.Add(new LedgerEntry(day.AddDays(-40), FinanceCategory.ContractBuyout, new Money(-2_000_000m), "Contract buyout — X (ABC merger)"));
        p.Ledger.Add(new LedgerEntry(day.AddDays(-40), FinanceCategory.Acquisition, new Money(-9_000_000m), "Acquisition of ABC"));
        p.Ledger.Add(new LedgerEntry(day.AddDays(-20), FinanceCategory.Fines, new Money(-50_000m), "brawl"));
        p.Ledger.Add(new LedgerEntry(day.AddDays(-10), FinanceCategory.Production, new Money(-100_000m), "card"));
        Assert.Equal(500_000m, AiFrontOffice.QuarterRunningCosts(p, day));
    }

    [Fact]
    public void AnOrgSavingForTheLeap_KeepsItsTokenCampaign_UnderTheQuarterReserve()
    {
        // The leap plan is budgeted against the prestige floor as before: a
        // climber whose quarter of costs is above its bank still runs the token
        // campaign instead of going silent and stalling at its tier's ceiling.
        var (w, p, _) = Setup();
        w.PlayerPromotion = default;
        p.TierClearance = OrgSize.Continental;
        p.Prestige = OrgSizing.PrestigeCeiling(OrgSize.Continental) - 1;   // pinned near the ceiling
        Assert.True(PromotionLadder.SavingForTheLeap(p));
        p.Ledger.Clear();
        var day = w.CurrentDate;
        p.Ledger.Add(new LedgerEntry(day.AddDays(-30), FinanceCategory.FighterPurse, new Money(-PromotionLadder.PromotionCushion(p) * 2m), "purses"));
        p.Balance = new Money(PromotionLadder.PromotionCushion(p) * 0.5m);   // filling the chest
        Assert.True(p.Balance.Amount < AiFrontOffice.Buffer(p, day));
        Assert.True(p.Balance.Amount > AiFrontOffice.PrestigeFloor(p) + 1_500_000m);
        AiFrontOffice.RunMonthly(w, day);
        Assert.Equal(30_000m, p.MarketingBudget.Amount);
    }
}
