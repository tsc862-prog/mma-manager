using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;

namespace MmaManager.Tests;

/// <summary>
/// The asking price every screen quotes is the number the negotiating room opens
/// at, and the reason it gives is the real one.
/// </summary>
public class AskingQuoteTests
{
    private static World Founded() =>
        new WorldBuilder().Build(new WorldBuilder.Options
        {
            Seed = 48,
            Founding = new WorldBuilder.FoundingSpec
            {
                Prestige = 20,
                StartingBalance = new Money(2_000_000m),
                Divisions = new[] { WeightClass.Lightweight, WeightClass.Welterweight },
            },
        });

    private static Money RoomOpens(World w, Promotion promo, Fighter f, ContractExpectations exp) =>
        new NegotiationSession(w, f, promo, exp).DemandPurse(Negotiator.WantsExclusivity(promo), clause: false);

    private static Fighter FreeAgent(World w) =>
        w.FreeAgents().Where(f => f.Status == FighterStatus.Active && f.Record.TotalBouts > 6)
         .OrderBy(f => f.Id.Value).First();

    private static Fighter SignToPlayer(World w, Fighter f, decimal purse, int fights)
    {
        ContractLifecycle.Sign(w, f, new ContractOffer
        {
            Fighter = f.Id, Promotion = w.PlayerPromotion,
            PursePerFight = new Money(purse), Fights = fights,
        }, w.CurrentDate);
        return f;
    }

    [Fact]
    public void AFreeAgent_AtASmallHall_IsQuotedTheRoomsNumber_NonExclusive()
    {
        var w = Founded();
        var promo = w.Get(w.PlayerPromotion)!;
        var f = FreeAgent(w);
        var exp = new FighterValuation().Evaluate(w, f);

        var q = AskingQuote.For(w, promo, f, exp);
        Assert.Equal(RoomOpens(w, promo, f, exp), q.Purse);
        Assert.Equal(exp.AskingPurse, q.MarketRate);
        Assert.Contains("non-exclusive deal", q.Reasons);
        Assert.DoesNotContain(q.Reasons, r => r.Contains("loyal"));
    }

    [Fact]
    public void AHoldingLifer_IsQuotedHisHometownRate_AndToldSo()
    {
        var w = Founded();
        var promo = w.Get(w.PlayerPromotion)!;
        var f = SignToPlayer(w, FreeAgent(w), purse: 500m, fights: 3);
        f.Disposition = Disposition.CompanyMan;
        f.Loyalty = 90;
        f.Record.CurrentStreak = 1;
        Assert.True(Lifers.HomeDiscount(w, f, promo.Id) < 1m, Lifers.BondOf(w, f).Grievance?.Reason);
        var exp = new FighterValuation().Evaluate(w, f);

        var q = AskingQuote.For(w, promo, f, exp);
        Assert.Equal(RoomOpens(w, promo, f, exp), q.Purse);
        Assert.Contains(q.Reasons, r => r.StartsWith("loyal to you"));
        Assert.True(q.Purse.Amount < exp.AskingPurse.Amount * Negotiator.NonExclusiveMoneyFactor);
    }

    [Fact]
    public void ALiveDeal_FloorsTheQuote_AtItsShowMoney()
    {
        var w = Founded();
        var promo = w.Get(w.PlayerPromotion)!;
        var f = FreeAgent(w);
        var exp0 = new FighterValuation().Evaluate(w, f);
        SignToPlayer(w, f, purse: exp0.AskingPurse.Amount * 3m, fights: 3);
        f.Record.CurrentStreak = 1;
        var exp = new FighterValuation().Evaluate(w, f);

        var q = AskingQuote.For(w, promo, f, exp);
        Assert.Equal(RoomOpens(w, promo, f, exp), q.Purse);
        Assert.Equal(f.Contract!.PursePerFight, q.Purse);
        Assert.Contains(q.Reasons, r => r.StartsWith("won't take less than"));
    }

    [Fact]
    public void ADiscoveredProspect_IsQuotedWhatTheRoomOpensAt()
    {
        var w = Founded();
        var promo = w.Get(w.PlayerPromotion)!;
        var f = w.FreeAgents().Where(x => x.Status == FighterStatus.Active && x.Record.TotalBouts <= 5)
                 .OrderBy(x => x.Id.Value).First();
        f.DiscoveredBy = promo.Id;
        var exp = new FighterValuation().Evaluate(w, f);

        var q = AskingQuote.For(w, promo, f, exp);
        Assert.Equal(RoomOpens(w, promo, f, exp), q.Purse);
        // The discoverer's gratitude reaches the room: he opens at the grateful rate.
        decimal grateful = Math.Ceiling(exp.AskingPurse.Amount
                                      * (Negotiator.WantsExclusivity(promo) ? 1m : Negotiator.NonExclusiveMoneyFactor)
                                      * Negotiator.GratitudeFactor(f, promo.Id) / 500m) * 500m;
        Assert.Equal(grateful, q.Purse.Amount);
        Assert.Contains(q.Reasons, r => r.StartsWith("you discovered him"));
    }

    [Fact]
    public void GratitudeAndTheLiferDiscount_DoNotStack()
    {
        var w = Founded();
        var promo = w.Get(w.PlayerPromotion)!;
        var f = w.FreeAgents().Where(x => x.Status == FighterStatus.Active && x.Record.TotalBouts <= 5)
                 .OrderBy(x => x.Id.Value).First();
        f.DiscoveredBy = promo.Id;
        SignToPlayer(w, f, purse: 500m, fights: 3);
        f.Disposition = Disposition.CompanyMan;
        f.Loyalty = 90;
        f.Record.CurrentStreak = 1;
        Assert.True(Lifers.HomeDiscount(w, f, promo.Id) < 1m, Lifers.BondOf(w, f).Grievance?.Reason);
        Assert.True(Negotiator.GratitudeFactor(f, promo.Id) < 1m);

        // The bigger discount, not the product: never under half his ask.
        Assert.Equal(Math.Min(Negotiator.GratitudeFactor(f, promo.Id), Lifers.HomeDiscount(w, f, promo.Id)),
            Negotiator.TableDiscount(w, f, promo.Id));
        Assert.True(Negotiator.TableDiscount(w, f, promo.Id) >= 0.50m);
        var exp = new FighterValuation().Evaluate(w, f);
        var q = AskingQuote.For(w, promo, f, exp);
        Assert.Equal(RoomOpens(w, promo, f, exp), q.Purse);
        Assert.Single(q.Reasons, r => r.StartsWith("loyal to you") || r.StartsWith("you discovered him"));
    }

    [Fact]
    public void TheFightersAnswer_UsesTheBiggerDiscount_NotTheProduct()
    {
        var w = Founded();
        var promo = w.Get(w.PlayerPromotion)!;
        var f = w.FreeAgents().Where(x => x.Status == FighterStatus.Active && x.Record.TotalBouts <= 5)
                 .OrderBy(x => x.Id.Value).First();
        f.DiscoveredBy = promo.Id;
        SignToPlayer(w, f, purse: 500m, fights: 3);
        f.Disposition = Disposition.CompanyMan;
        f.Loyalty = 90;
        f.Record.CurrentStreak = 1;
        decimal home = Lifers.HomeDiscount(w, f, promo.Id), grateful = Negotiator.GratitudeFactor(f, promo.Id);
        Assert.True(home < 1m && grateful < 1m);
        var exp = new FighterValuation().Evaluate(w, f);
        bool excl = Negotiator.WantsExclusivity(promo);
        decimal terms = excl ? 1m : Negotiator.NonExclusiveMoneyFactor;

        ContractOffer At(decimal personal) => new()
        {
            Fighter = f.Id, Promotion = promo.Id, Exclusive = excl, Fights = exp.DesiredFights,
            PursePerFight = new Money(Math.Ceiling(exp.AskingPurse.Amount * terms * personal)),
            WinBonus = new Money(Math.Ceiling(exp.AskingWinBonus.Amount * terms * personal)),
            SigningBonus = new Money(Math.Ceiling(exp.AskingPurse.Amount * terms * personal)),
        };

        var n = new Negotiator();
        Assert.Equal(NegotiationVerdict.Accept, n.Evaluate(w, f, At(Math.Min(home, grateful)), exp).Verdict);
        Assert.NotEqual(NegotiationVerdict.Accept, n.Evaluate(w, f, At(home * grateful), exp).Verdict);
    }

    [Fact]
    public void TheQuote_FollowsTheTermsOnTheTable()
    {
        var w = Founded();
        var promo = w.Get(w.PlayerPromotion)!;
        var f = FreeAgent(w);
        var exp = new FighterValuation().Evaluate(w, f);
        var room = new NegotiationSession(w, f, promo, exp);

        foreach (var excl in new[] { true, false })
        foreach (var clause in new[] { true, false })
            Assert.Equal(room.DemandPurse(excl, clause), AskingQuote.For(w, promo, f, exp, excl, clause).Purse);
    }
}
