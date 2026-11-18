using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Tests;

/// <summary>
/// The house tier reaches every Origins price path (world/origins-tier-reach, finishing PR #88). #88 made a man ask a
/// smaller house less (<see cref="Negotiator.HouseTier"/> through <see cref="Negotiator.TableFactor"/>) and quoted the
/// Origins table on the 250 step, but several paths still priced at the untiered open-market ask or on the 500 step:
/// a distressed house's budget test (free agency and renewals), a founded house's founding roster, the AI's opening
/// offer's own rounding (a mid house's tier ask of 1,100 was offered at 1,500), the room's rival and guaranteed-money
/// figures, a rival's paper and walk-away, a short-notice stand-in's price and a lifer's "underpaid" bar. Today's mode is
/// untouched on every one of them.
/// </summary>
public class OriginsTierReachTests
{
    private static Promotion House(World w, string abbr) => w.Promotions.Values.Single(p => p.Abbreviation == abbr);

    /// <summary>A giant in a stadium (the sport's best purse far above everyone's), the free market's men known journeymen.</summary>
    private static (World W, Promotion Giant) LateEra()
    {
        var w = OriginsWorldTests.AiWorld(11);
        var giant = House(w, "SFA");
        giant.Prestige = 60;
        OriginsShows.HomeVenue(w, giant)!.Capacity = 20_000;
        foreach (var f in w.FreeAgents()) f.Intangibles.Popularity = 38;
        return (w, giant);
    }

    private static List<Fighter> Free(World w) =>
        w.FreeAgents().Where(f => f.Status == FighterStatus.Active).OrderBy(f => f.Id.Value).ToList();

    [Fact]
    public void ADistressedHouse_SignsAManWhoseTierPriceFits_ThoughHisOpenMarketAskDoesNot()
    {
        var (w, giant) = LateEra();
        // The giant is the player's (the AI pass skips it), so the market is the mid house's to shop.
        w.PlayerPromotion = giant.Id;
        giant.IsPlayerControlled = true;
        var mid = House(w, "CRU");
        foreach (var f in w.RosterOf(mid.Id).ToList()) f.Contract = null;   // its men walk: it needs bodies
        mid.Balance = new Money(-1m);                                       // in the red: distress 1
        int distress = MmaManager.Core.Sim.Org.Solvency.Distress(mid, w);
        Assert.Equal(1, distress);
        // A purse budget a distressed house can still pay someone from (a 750 house's 35% reaches nobody at all).
        for (int pr = mid.Prestige.Value; pr <= 60 && Negotiator.PurseCap(w, mid) * MmaManager.Core.Sim.Org.Solvency.DistressedPurseFactor(distress) < 750m; pr++)
            mid.Prestige = pr;
        decimal budget = Negotiator.PurseCap(w, mid) * MmaManager.Core.Sim.Org.Solvency.DistressedPurseFactor(distress);
        decimal ceiling = FighterValuation.OriginsMarketCeiling(w);
        Assert.True(budget >= 750m && Negotiator.MarketPurseCap(w, mid) < ceiling / 2, $"budget {budget}, cap {Negotiator.PurseCap(w, mid)}, ceiling {ceiling}");

        var val = new FighterValuation();
        var asks = Free(w).ToDictionary(f => f.Id, f => val.Evaluate(w, f).AskingPurse.Amount);
        // Men the open-market test would turn away, whom his ask at this house brings within the budget.
        int reachable = Free(w).Count(f => mid.Divisions.Any(d => d.Matches(f)) && asks[f.Id] > budget
                                           && Negotiator.AffordAsk(w, mid, f, val.Evaluate(w, f)) <= budget);
        Assert.True(reachable > 0, "no man the tier brings within a distressed budget");

        FreeAgencyService.RunMonthly(w, w.CurrentDate);
        var signed = w.RosterOf(mid.Id).Where(f => asks.ContainsKey(f.Id)).ToList();
        Assert.Contains(signed, f => asks[f.Id] > budget);
        Assert.All(signed, f => Assert.True(f.Contract!.PursePerFight.Amount <= Negotiator.PurseCap(w, mid)));
    }

    [Fact]
    public void AFoundedSmallHouse_PricesItsFoundingRosterAtItsTier()
    {
        var (w, _) = LateEra();
        decimal ceiling = FighterValuation.OriginsMarketCeiling(w);
        var before = Free(w).Select(f => f.Id).ToHashSet();
        var house = OriginsFounding.Found(w, w.CurrentDate, null);
        Assert.NotNull(house);
        decimal cap = Negotiator.PurseCap(w, house!);
        Assert.True(cap * 2 < ceiling, $"cap {cap}, ceiling {ceiling}");
        bool excl = Negotiator.WantsExclusivity(house!);

        var val = new FighterValuation();
        var market = w.RosterOf(house!.Id).Where(f => before.Contains(f.Id)).ToList();   // signed off the market (not tryout men)
        Assert.NotEmpty(market);
        foreach (var f in market)
        {
            var exp = val.Evaluate(w, f);
            decimal factor = (excl ? 1m : Negotiator.NonExclusiveMoneyFactor) * Negotiator.TableFactor(w, f, house.Id, exp);
            Assert.Equal(NegotiationSession.UpToStep(w, exp.AskingPurse.Amount * factor), f.Contract!.PursePerFight.Amount);
            Assert.Equal(NegotiationSession.UpToStep(w, exp.AskingWinBonus.Amount * factor), f.Contract.WinBonus.Amount);
            Assert.True(f.Contract.PursePerFight.Amount <= cap);
            Assert.Equal(0m, f.Contract.PursePerFight.Amount % NegotiationSession.OriginsStep);
        }
        // At least one of them the open-market price kept out on day one.
        Assert.Contains(market, f => val.Evaluate(w, f).AskingPurse.Amount > cap);
    }

    [Fact]
    public void TheAisOpeningOffer_InOrigins_LandsOnThe250Step_AndIsAccepted()
    {
        var (w, giant) = LateEra();
        var val = new FighterValuation();
        var neg = new Negotiator();
        bool sawQuarter = false;
        foreach (var p in w.Promotions.Values.Where(p => !p.Defunct && p.Id != giant.Id).OrderBy(p => p.Id.Value))
        foreach (var f in Free(w).Where(f => p.Divisions.Any(d => d.Matches(f))).Take(60))
        {
            var exp = val.Evaluate(w, f, out var market);
            var offer = neg.AiOpeningOffer(w, p, f, exp, market: market);
            decimal ask = Negotiator.TableAsk(w, p, f, exp, Negotiator.WantsExclusivity(p), market);
            decimal cap = Negotiator.PurseCap(w, p);
            decimal purse = offer.PursePerFight.Amount;
            // On the 250 step, unless the step would cross the cap (then his ask to the dollar).
            Assert.True(purse % 250m == 0m || purse == ask, $"{p.Abbreviation} offered {purse} (ask {ask}, cap {cap})");
            if (ask <= cap) Assert.True(purse >= ask && purse - ask < 250m, $"{p.Abbreviation} offered {purse} for an ask of {ask}");
            if (purse % 500m != 0m && purse < NegotiationSession.UpTo500(ask)
                && neg.Evaluate(w, f, offer, exp, market).Verdict == NegotiationVerdict.Accept)
                sawQuarter = true;
        }
        Assert.True(sawQuarter, "no opening offer landed between two 500 steps and was accepted");
    }

    [Fact]
    public void ThePlayersTable_WithARivalsPaper_QuotesOnThe250Step_AndSignsAtWhatItQuotes()
    {
        var (w, giant) = LateEra();
        var mid = House(w, "CRU");
        w.PlayerPromotion = mid.Id;
        mid.IsPlayerControlled = true;
        bool excl = Negotiator.WantsExclusivity(mid);
        var val = new FighterValuation();
        var neg = new Negotiator();

        // A journeyman with the giant's opening offer in his hand.
        var f = Free(w).First(x => mid.Divisions.Any(d => d.Matches(x)) && giant.Divisions.Any(d => d.Matches(x))
                                   && val.Evaluate(w, x).AskingPurse.Amount > Negotiator.OriginsPurseFloor * 2);
        var exp = val.Evaluate(w, f);
        var paper = neg.AiOpeningOffer(w, giant, f, exp);
        w.RivalOffers.Add(new RivalOffer
        {
            Id = 1, Fighter = f.Id, From = giant.Id, Holder = mid.Id, Kind = RivalOfferKind.FreeAgent,
            Purse = paper.PursePerFight, WinBonus = paper.WinBonus, SigningBonus = paper.SigningBonus,
            Fights = paper.Fights, Exclusive = paper.Exclusive, Made = w.CurrentDate, Deadline = w.CurrentDate.AddDays(14),
        });

        var room = NegotiationSession.Resume(w, f, mid, exp);
        Assert.NotNull(room.RivalOffer);
        Assert.Equal(NegotiationSession.OriginsStep, room.TableStep);
        var his = room.TheirTermsAsOffer(excl, clause: false);
        Assert.Equal(0m, his.PursePerFight.Amount % NegotiationSession.OriginsStep);
        Assert.Equal(room.DemandPurse(excl, clause: false), his.PursePerFight);
        Assert.Equal(his.PursePerFight, room.View(excl, false).Position.PursePerFight);
        Assert.True(OfferMarket.Score(w, f, his, exp) >= OfferMarket.Score(w, f, OfferMarket.AsContractOffer(w.RivalOffers[0]), exp));

        // Bonus-heavy paper: the room names the guaranteed money it needs on the 250 step.
        var thin = room.TheirTermsAsOffer(excl, clause: false);
        thin.PursePerFight = new Money(Negotiator.OriginsPurseFloor);
        thin.WinBonus = new Money(his.PursePerFight.Amount * 4);
        var reply = room.Table(thin);
        var g = reply.Reactions.FirstOrDefault(r => r.Term == "Guaranteed money");
        Assert.NotNull(g);
        var amount = System.Text.RegularExpressions.Regex.Match(g!.Note, @"need \D*([\d,]+)").Groups[1].Value.Replace(",", "");
        Assert.True(decimal.TryParse(amount, out var need) && need % NegotiationSession.OriginsStep == 0m, g.Note);

        // Quoted X, offers X, signs at X.
        var fresh = NegotiationSession.Resume(w, f, mid, exp);
        var meet = fresh.TheirTermsAsOffer(excl, clause: false);
        Assert.Equal(his.PursePerFight, meet.PursePerFight);
        Assert.Equal(TalkVerdict.Accept, fresh.Table(meet).Verdict);
        ContractLifecycle.Sign(w, f, meet, w.CurrentDate);
        Assert.Equal(his.PursePerFight, f.Contract!.PursePerFight);
    }

    [Fact]
    public void ARivalsWalkAway_InOrigins_IsHisAskAtTheRival_OnThe250Step()
    {
        var (w, giant) = LateEra();
        var mid = House(w, "CRU");
        var small = House(w, "DLB");
        var val = new FighterValuation();
        var f = Free(w).First(x => small.Divisions.Any(d => d.Matches(x)) && val.Evaluate(w, x).AskingPurse.Amount > Negotiator.OriginsPurseFloor * 2);
        var exp = val.Evaluate(w, f);
        var o = new RivalOffer
        {
            Id = 1, Fighter = f.Id, From = small.Id, Holder = mid.Id, Kind = RivalOfferKind.FreeAgent,
            Purse = new Money(Negotiator.OriginsPurseFloor), Fights = 3, Exclusive = Negotiator.WantsExclusivity(small),
            Made = w.CurrentDate, Deadline = w.CurrentDate.AddDays(14),
        };
        decimal walk = OfferMarket.CeilingPurse(w, small, o, exp);
        decimal ask = Negotiator.TableAsk(w, small, f, exp, o.Exclusive);
        Assert.Equal(0m, walk % NegotiationSession.OriginsStep);
        Assert.True(walk <= Math.Max(o.Purse.Amount, ask), $"walk-away {walk}, his ask at the rival {ask}");
        Assert.Equal(Math.Max(o.Purse.Amount, Math.Floor(Math.Min(Negotiator.PurseCap(w, small), ask) / 250m) * 250m), walk);
    }

    [Fact]
    public void TodaysMode_EveryReachedPath_IsAsBefore()
    {
        var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 3 });
        Assert.Equal(500m, NegotiationSession.Step(w));
        var val = new FighterValuation();
        var neg = new Negotiator();
        foreach (var p in w.Promotions.Values.Where(p => !p.Defunct).OrderBy(p => p.Id.Value).Take(8))
        foreach (var f in w.FreeAgents().OrderBy(f => f.Id.Value).Take(15))
        {
            var exp = val.Evaluate(w, f);
            bool excl = Negotiator.WantsExclusivity(p);
            // The distress test reads the open-market ask, as before.
            Assert.Equal(exp.AskingPurse.Amount, Negotiator.AffordAsk(w, p, f, exp));
            // The opening offer pitches the same ask on the 500 step.
            decimal ask = exp.AskingPurse.Amount * (excl ? 1m : Negotiator.NonExclusiveMoneyFactor) * Negotiator.TableDiscount(w, f, p.Id);
            Assert.Equal(ask, Negotiator.TableAsk(w, p, f, exp, excl));
            decimal cap = Negotiator.PurseCap(w, p);
            decimal main = ask > cap ? Math.Floor(cap / 500m) * 500m : Math.Ceiling(ask / 500m) * 500m is var up && up <= cap ? up : ask;
            Assert.Equal(main, neg.AiOpeningOffer(w, p, f, exp).PursePerFight.Amount);
            Assert.Equal(Math.Round(ask / 500m) * 500m, Negotiator.EffectiveAskingPurse(w, p, f, exp).Amount);
            // A rival's walk-away: his open-market ask on the 500 step.
            var o = new RivalOffer
            {
                Id = 1, Fighter = f.Id, From = p.Id, Holder = w.PlayerPromotion, Purse = new Money(500m), Exclusive = excl,
                Made = w.CurrentDate,
            };
            decimal oldAsk = exp.AskingPurse.Amount * (excl ? 1m : Negotiator.NonExclusiveMoneyFactor);
            Assert.Equal(Math.Max(500m, Math.Floor(Math.Min(cap, oldAsk) / 500m) * 500m), OfferMarket.CeilingPurse(w, p, o, exp));
            Assert.Equal(1m, Negotiator.HouseTier(w, p, exp));
        }
    }
}
