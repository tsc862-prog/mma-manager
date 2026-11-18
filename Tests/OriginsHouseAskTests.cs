using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Tests;

/// <summary>
/// What a man asks in an Origins world, house by house (world/origins-asks).
///
/// A: the sport's price level must not depend on which house the player runs. Every Origins ask is a share of the
/// best purse cap among live houses (<see cref="FighterValuation.OriginsMarketCeiling"/>). An AI founder's cap follows
/// the crowd it has seen; the player's kept his hall's, so a world where he ran the Crucible held the ceiling up at the
/// Crucible's 4,000-seat hall and every man in the sport asked more than in the same world with the AI running it. The
/// market now reads every house the same way (<see cref="Negotiator.MarketPurseCap"/>); the player's own budget is
/// still his hall's (<see cref="Negotiator.PurseCap"/>), and nothing caps what he offers.
///
/// B: a man asks a smaller house less (<see cref="Negotiator.HouseTier"/>): the same share of a purse between that
/// house's cap and the best one, nearer the best the more of a name he is. Late in a 20-year world the giants' caps
/// had grown to ~4,250 while a full 1,200-seat gym house stayed at 750, and every journeyman priced off the giants.
/// </summary>
public class OriginsHouseAskTests
{
    private static Promotion House(World w, string abbr) => w.Promotions.Values.Single(p => p.Abbreviation == abbr);

    private static Venue Authored(World w, Promotion p) =>
        w.Venues.Values.Single(v => v.City == p.HomeCity && v.Name == MmaManager.Core.Sim.Generation.Origins.OriginsPromotions.All
            .Single(s => s.Abbreviation == p.Abbreviation).HomeVenue);

    /// <summary>Every completed night of the house was in its hall and drew this crowd (seats left over: the crowd is what it saw).</summary>
    private static void Drew(World w, Promotion p, int crowd)
    {
        var hall = Authored(w, p);
        foreach (var e in w.Events.Values.Where(e => e.Promotion == p.Id && e.Status == EventStatus.Completed && e.Financials is not null))
        {
            e.VenueId = hall.Id; e.Venue = hall.Name;
            e.Financials!.Attendance = crowd;
        }
    }

    /// <summary>A year on, with the Crucible drawing a third of its hall (its market cap well under its hall's).</summary>
    private static (World W, Promotion Cru) ThinCrucible()
    {
        var w = OriginsWorldTests.AiWorld(11);
        new WorldTick().AdvanceWeeks(w, 52);
        var cru = House(w, "CRU");
        cru.Prestige = cru.Prestige.Value + 15;   // the sport's biggest house (its hall's cap is the top one)
        Drew(w, cru, Authored(w, cru).Capacity / 3);
        return (w, cru);
    }

    private static List<Fighter> Free(World w) =>
        w.FreeAgents().Where(f => f.Status == FighterStatus.Active).OrderBy(f => f.Id.Value).ToList();

    [Fact]
    public void TheCeiling_AndEveryFreeAgentsAsk_AreTheSame_WhetherThePlayerOrTheAiRunsTheCrucible()
    {
        var (w, cru) = ThinCrucible();
        var val = new FighterValuation();
        var free = Free(w);
        Assert.NotEmpty(free);
        var sfa = House(w, "SFA");

        decimal aiCeiling = FighterValuation.OriginsMarketCeiling(w);
        decimal aiCap = Negotiator.PurseCap(w, cru);
        var aiAsks = free.Select(f => val.Evaluate(w, f).AskingPurse.Amount).ToList();
        var aiQuotes = free.Select(f => AskingQuote.For(w, sfa, f, val.Evaluate(w, f)).Purse.Amount).ToList();

        // The player takes the Crucible; nothing else in the world changes.
        w.PlayerPromotion = cru.Id;
        cru.IsPlayerControlled = true;
        decimal hisCap = Negotiator.PurseCap(w, cru);
        Assert.Equal(aiCap, Negotiator.MarketPurseCap(w, cru));                  // the market reads his house as the AI's
        Assert.Equal(aiCeiling, FighterValuation.OriginsMarketCeiling(w));
        Assert.Equal(aiAsks, free.Select(f => val.Evaluate(w, f).AskingPurse.Amount).ToList());
        Assert.Equal(aiQuotes, free.Select(f => AskingQuote.For(w, sfa, f, val.Evaluate(w, f)).Purse.Amount).ToList());

        // His own budget is still his hall's, over the crowd-read cap and over the ceiling: that cap in the maximum is
        // what used to put a premium on every man's ask in the sport.
        Assert.True(hisCap > aiCap, $"his cap {hisCap}, the market's read of his house {aiCap}");
        Assert.True(hisCap > aiCeiling, $"his cap {hisCap}, the ceiling {aiCeiling}");
    }

    [Fact]
    public void ThePlayersOwnOffer_IsNotHeldToTheMarketsReadOfHisHouse()
    {
        var (w, cru) = ThinCrucible();
        w.PlayerPromotion = cru.Id;
        cru.IsPlayerControlled = true;
        decimal market = Negotiator.MarketPurseCap(w, cru);
        decimal his = Negotiator.PurseCap(w, cru);
        Assert.True(his > market);

        // A named man whose ask is over the market's read of the Crucible: the player offers his ask, at his own budget's
        // reach, and the man signs. Nothing trims the offer to the market's read.
        var val = new FighterValuation();
        var neg = new Negotiator();
        var named = Free(w).Select(f => (F: f, Exp: val.Evaluate(w, f)))
            .Where(x => x.Exp.AskingPurse.Amount > market && x.Exp.AskingPurse.Amount <= his)
            .OrderByDescending(x => x.Exp.AskingPurse.Amount).ThenBy(x => x.F.Id.Value).FirstOrDefault();
        var who = named.F ?? Free(w).OrderByDescending(f => val.Evaluate(w, f).AskingPurse.Amount).ThenBy(f => f.Id.Value).First();
        var exp = val.Evaluate(w, who);
        var offer = new ContractOffer
        {
            Fighter = who.Id, Promotion = cru.Id, PursePerFight = new Money(Math.Max(exp.AskingPurse.Amount, market + 500m)),
            WinBonus = exp.AskingWinBonus, Fights = exp.DesiredFights, Exclusive = Negotiator.WantsExclusivity(cru),
            SigningBonus = exp.WantsSigningBonus ? new Money(exp.AskingPurse.Amount) : Money.Zero,
        };
        Assert.True(offer.PursePerFight.Amount > market);
        Assert.Equal(NegotiationVerdict.Accept, neg.Evaluate(w, who, offer, exp).Verdict);
        ContractLifecycle.Sign(w, who, offer, w.CurrentDate);
        Assert.Equal(offer.PursePerFight, who.Contract!.PursePerFight);
    }

    /// <summary>
    /// The late-era world in miniature: a giant in a stadium (the best purse in the sport several times a small house's),
    /// a small house with a full room at its own small cap, and a free market of known journeymen (some fame, no belts).
    /// </summary>
    private static (World W, Promotion Small, Promotion Giant) LateEra()
    {
        var w = OriginsWorldTests.AiWorld(11);
        var giant = House(w, "SFA");
        giant.Prestige = 60;
        OriginsShows.HomeVenue(w, giant)!.Capacity = 20_000;
        var small = House(w, "DLB");
        // The small house's men walk (deals done); it must find a roster on the market.
        foreach (var f in w.RosterOf(small.Id).ToList()) f.Contract = null;
        // Known journeymen: a little fame, nothing more (each asks a sixth or so of the best purse).
        foreach (var f in w.FreeAgents()) f.Intangibles.Popularity = 38;
        return (w, small, giant);
    }

    [Fact]
    public void ALateEraSmallHouseWithAFullRoom_SignsEnoughMenOffTheMarketToHoldItsRoster()
    {
        var (w, small, _) = LateEra();
        var val = new FighterValuation();
        decimal cap = Negotiator.PurseCap(w, small);
        decimal ceiling = FighterValuation.OriginsMarketCeiling(w);
        Assert.True(ceiling >= cap * 4, $"ceiling {ceiling}, small house {cap}");
        int need = OriginsShows.MinRoster(small);

        // On the open-market price (a share of the giant's purse) hardly anyone fits the small house's purse.
        var pool = w.FreeAgents().Where(f => f.Status == FighterStatus.Active && small.Divisions.Any(d => d.Matches(f))).ToList();
        int fitsOpenMarket = pool.Count(f => val.Evaluate(w, f).AskingPurse.Amount * Negotiator.NonExclusiveMoneyFactor <= cap);
        Assert.True(fitsOpenMarket < need, $"{fitsOpenMarket} of {pool.Count} fit the open-market price");

        // At its own table the journeymen ask what a house its size pays: one month's market fills its night.
        FreeAgencyService.RunMonthly(w, w.CurrentDate);
        var men = w.RosterOf(small.Id).ToList();
        Assert.True(men.Count >= need, $"{men.Count} men signed, a night needs {need}");
        Assert.All(men, f => Assert.True(f.Contract!.PursePerFight.Amount <= cap, $"{f.Contract.PursePerFight} over the cap {cap}"));
    }

    [Fact]
    public void AMansAskAtASmallerHouse_FallsWithTheHouse_AndRisesWithHisName()
    {
        var (w, small, giant) = LateEra();
        var val = new FighterValuation();
        decimal ceiling = FighterValuation.OriginsMarketCeiling(w);
        var mid = House(w, "CRU");
        Assert.True(Negotiator.MarketPurseCap(w, small) < Negotiator.MarketPurseCap(w, mid));
        Assert.Equal(ceiling, Negotiator.MarketPurseCap(w, giant));

        var asks = Free(w).Select(f => (F: f, Exp: val.Evaluate(w, f))).ToList();
        var journeyman = asks.Where(x => x.Exp.AskingPurse.Amount > Negotiator.OriginsPurseFloor * 2 && x.Exp.AskingPurse.Amount < ceiling / 2)
            .OrderBy(x => x.F.Id.Value).First();
        decimal atSmall = Negotiator.HouseTier(w, small, journeyman.Exp), atMid = Negotiator.HouseTier(w, mid, journeyman.Exp);
        Assert.True(atSmall < atMid && atMid < 1m, $"tier {atSmall} at the small house, {atMid} at the middle one");
        Assert.Equal(1m, Negotiator.HouseTier(w, giant, journeyman.Exp));   // the house that pays the best purse pays his full ask
        Assert.True(journeyman.Exp.AskingPurse.Amount * atSmall >= Negotiator.OriginsPurseFloor);   // never under the sport's floor

        // The more of a name, the less he comes down: a man asking the whole ceiling asks it of every house.
        var star = asks.OrderByDescending(x => x.Exp.AskingPurse.Amount).ThenBy(x => x.F.Id.Value).First();
        star.F.Intangibles.Popularity = 100;
        var starExp = val.Evaluate(w, star.F);
        Assert.True(starExp.AskingPurse.Amount >= ceiling - 250m, $"top ask {starExp.AskingPurse} vs ceiling {ceiling}");
        Assert.True(Negotiator.HouseTier(w, small, starExp) > 0.95m);
        // A floor-asker has nothing to come down from.
        var unknown = asks.FirstOrDefault(x => x.Exp.AskingPurse.Amount <= Negotiator.OriginsPurseFloor);
        if (unknown.F is not null) Assert.Equal(1m, Negotiator.HouseTier(w, small, unknown.Exp));

        // The tier is only ever the lower of it and a personal discount, never both (they don't stack).
        var f = journeyman.F;
        Assert.Equal(Math.Min(Negotiator.TableDiscount(w, f, small.Id), atSmall), Negotiator.TableFactor(w, f, small.Id, journeyman.Exp));
    }

    [Fact]
    public void TodaysMode_HasNoHouseTier_AndTheMarketReadIsThePurseCap()
    {
        var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 3 });
        var val = new FighterValuation();
        foreach (var p in w.Promotions.Values.OrderBy(p => p.Id.Value))
        {
            Assert.Equal(Negotiator.PurseCap(w, p), Negotiator.MarketPurseCap(w, p));
            foreach (var f in w.FreeAgents().OrderBy(f => f.Id.Value).Take(10))
            {
                var exp = val.Evaluate(w, f);
                Assert.Equal(1m, Negotiator.HouseTier(w, p, exp));
                Assert.Equal(Negotiator.TableDiscount(w, f, p.Id), Negotiator.TableFactor(w, f, p.Id, exp));
            }
        }
    }

    [Fact]
    public void TheMarketReadHandedDown_GivesExactlyWhatEachTableCallWouldComputeItself()
    {
        var (w, small, _) = LateEra();
        var val = new FighterValuation();
        var neg = new Negotiator();
        foreach (var f in Free(w).Take(40))
        {
            var exp = val.Evaluate(w, f, out var market);
            Assert.NotNull(market);
            Assert.Equal(FighterValuation.OriginsMarketCeiling(w), market!.Ceiling);
            Assert.Equal(val.Evaluate(w, f), exp);
            foreach (var p in w.Promotions.Values.Where(p => !p.Defunct).OrderBy(p => p.Id.Value))
            {
                Assert.Equal(Negotiator.MarketPurseCap(w, p), market.CapOf(w, p));
                Assert.Equal(Negotiator.HouseTier(w, p, exp), Negotiator.HouseTier(w, p, exp, market));
                var a = neg.AiOpeningOffer(w, p, f, exp);
                var b = neg.AiOpeningOffer(w, p, f, exp, market: market);
                Assert.Equal(a.PursePerFight, b.PursePerFight);
                Assert.Equal(a.WinBonus, b.WinBonus);
                Assert.Equal(neg.Evaluate(w, f, a, exp).Verdict, neg.Evaluate(w, f, a, exp, market).Verdict);
            }
        }
        // Today's mode takes no read at all.
        var modern = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 3 });
        val.Evaluate(modern, modern.FreeAgents().OrderBy(f => f.Id.Value).First(), out var none);
        Assert.Null(none);
    }

    [Fact]
    public void TheOriginsTable_QuotesOnThe250Step_SoAManAHouseCanPayIsQuotedWithinItsPurse()
    {
        // The player runs a small house whose purse cap is 750, in a late-era market.
        var (w, small, _) = LateEra();
        for (int pr = 1; pr <= 60 && Negotiator.PurseCap(w, small) != 750m; pr++) small.Prestige = pr;
        Assert.Equal(750m, Negotiator.PurseCap(w, small));
        w.PlayerPromotion = small.Id;
        small.IsPlayerControlled = true;
        decimal cap = Negotiator.PurseCap(w, small);
        bool excl = Negotiator.WantsExclusivity(small);
        var val = new FighterValuation();

        // A man whose ask at this table works out between the floor and the cap (516, say): find one by his fame.
        Fighter? who = null; ContractExpectations? exp = null; decimal raw = 0m;
        foreach (var f in Free(w).Where(f => small.Divisions.Any(d => d.Matches(f))))
        {
            for (int pop = 30; pop <= 90 && who is null; pop++)
            {
                f.Intangibles.Popularity = pop;
                var e = val.Evaluate(w, f);
                decimal r = e.AskingPurse.Amount * (excl ? 1m : Negotiator.NonExclusiveMoneyFactor) * Negotiator.TableFactor(w, f, small.Id, e);
                if (r > Negotiator.OriginsPurseFloor && r <= cap - 100m) { who = f; exp = e; raw = r; }
            }
            if (who is not null) break;
            f.Intangibles.Popularity = 38;
        }
        Assert.NotNull(who);
        Assert.True(NegotiationSession.UpTo500(raw) > cap, $"his ask here {raw}: the old step quoted {NegotiationSession.UpTo500(raw)}");

        // The quote, the room and the deal: all within the house's purse, and he signs at it.
        var quote = AskingQuote.For(w, small, who!, exp!);
        Assert.True(quote.Purse.Amount <= cap, $"quoted {quote.Purse} at a {cap} house (his ask here {raw})");
        Assert.True(quote.Purse.Amount >= raw);
        var session = new NegotiationSession(w, who!, small, exp!);
        Assert.Equal(quote.Purse, session.DemandPurse(excl, clause: false));
        var offer = session.TheirTermsAsOffer(excl, clause: false);
        Assert.Equal(quote.Purse, offer.PursePerFight);
        Assert.Equal(NegotiationVerdict.Accept, new Negotiator().Evaluate(w, who!, offer, exp!).Verdict);
        ContractLifecycle.Sign(w, who!, offer, w.CurrentDate);
        Assert.Equal(small.Id, who!.Contract!.Promotion);
        Assert.Equal(quote.Purse, who.Contract.PursePerFight);
    }

    [Fact]
    public void TodaysTable_KeepsThe500Step()
    {
        var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 3 });
        foreach (var v in new[] { 1m, 250m, 499m, 500m, 516m, 750m, 1_001m, 12_345m })
            Assert.Equal(NegotiationSession.UpTo500(v), NegotiationSession.UpToStep(w, v));
        var val = new FighterValuation();
        var promo = w.Get(w.PlayerPromotion)!;
        foreach (var f in w.FreeAgents().OrderBy(f => f.Id.Value).Take(60))
        {
            var exp = val.Evaluate(w, f);
            var q = AskingQuote.For(w, promo, f, exp);
            Assert.Equal(0m, q.Purse.Amount % 500m);
            Assert.Equal(q.Purse, new NegotiationSession(w, f, promo, exp).DemandPurse(Negotiator.WantsExclusivity(promo), clause: false));
        }
    }
}
