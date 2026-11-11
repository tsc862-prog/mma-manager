using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Living;
using MmaManager.Persistence;

namespace MmaManager.Tests;

/// <summary>
/// Fog-honest asks: a fighter's price is built from what the sport has SEEN —
/// his stages, results, belts, rankings, draw and age — never from his ratings
/// or potential. Two fighters with the same public sheet ask the same; a hidden
/// gem asks like his record; the ask moves when the record does.
/// </summary>
public class HonestAskTests
{
    private static World SeasonedWorld(ulong seed = 42)
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });
        new WorldTick().AdvanceWeeks(world, 20);
        return world;
    }

    private static readonly FighterValuation Valuation = new();

    // Rebuild every ranking from the record. A ranking reads the previous
    // top-five as its "quality opponents", so it is rebuilt until it settles.
    private static void Rerank(World world)
    {
        for (int i = 0; i < 4; i++)
        {
            RankingService.RecomputeAll(world, world.CurrentDate);
            RankingService.RecomputeGlobal(world);
        }
    }

    // Every rating a fighter has (striking, grappling, physical, mental), set to one value.
    private static void SetAllRatings(Fighter f, int v)
    {
        foreach (object block in new object[] { f.Striking, f.Grappling, f.Physical, f.Mental })
            foreach (var p in block.GetType().GetProperties())
                if (p.PropertyType == typeof(Rating) && p.CanWrite) p.SetValue(block, (Rating)v);
    }

    private static string Snapshot(ContractExpectations e) =>
        $"{e.MarketValue}|{e.AskingPurse}|{e.AskingWinBonus}|{e.DesiredFights}|{e.WantsSigningBonus}|"
        + $"{e.Priorities.Money}|{e.Priorities.TitleOpportunity}|{e.Priorities.Loyalty}|{e.Priorities.HomeRegion}";

    [Fact]
    public void NoPathFromTrueAbilityOrPotential_ToAnyValuationOutput()
    {
        // Perturb EVERY fighter's ratings and potential in the world (so an
        // opponent-quality read can't leak either), then every valuation output
        // of every fighter must be exactly what it was.
        var world = SeasonedWorld();
        var live = world.Fighters.Values.Where(f => f.Status != FighterStatus.Retired).OrderBy(f => f.Id.Value).ToList();
        // Rankings are public and the read uses them: rebuild them before AND
        // after the flip, so a ranking channel back to ability would show.
        Rerank(world);
        var before = live.ToDictionary(f => f.Id, f => Snapshot(Valuation.Evaluate(world, f)));
        var readBefore = live.ToDictionary(f => f.Id, f => PublicStanding.Read(world, f));

        foreach (var f in world.Fighters.Values)
        {
            int was = FighterAbility.CurrentAbility(f);
            SetAllRatings(f, was >= 50 ? 20 : 95);   // flip the truth
            f.PotentialAbility = was >= 50 ? 21 : 99;
        }
        Rerank(world);
        Assert.Contains(live, f => FighterAbility.CurrentAbility(f) >= 90);
        Assert.Contains(live, f => FighterAbility.CurrentAbility(f) <= 25);

        foreach (var f in live)
        {
            Assert.Equal(before[f.Id], Snapshot(Valuation.Evaluate(world, f)));
            Assert.Equal(readBefore[f.Id], PublicStanding.Read(world, f));
        }
    }

    [Fact]
    public void TwoFighters_WithTheSamePublicSheet_AskTheSame_WhateverTheyReallyAre()
    {
        var world = SeasonedWorld();
        // Two established free agents on the same weight; give B exactly A's
        // public sheet (record, history, draw, character, age, home) and a
        // completely different body of talent.
        var a = world.FreeAgents().Where(f => f.Status == FighterStatus.Active && f.Record.TotalBouts >= 6)
            .OrderBy(f => f.Id.Value).First();
        var twin = Twin(a);
        SetAllRatings(a, 30); a.PotentialAbility = 31;
        SetAllRatings(twin, 88); twin.PotentialAbility = 95;
        Assert.True(FighterAbility.CurrentAbility(twin) - FighterAbility.CurrentAbility(a) >= 40);

        Assert.Equal(Snapshot(Valuation.Evaluate(world, a)), Snapshot(Valuation.Evaluate(world, twin)));
    }

    // A copy of A's public sheet and nothing else: the same record and the same
    // bouts on it, draw, character, birthday and seat in the world (his id, so
    // he is read against the same events, titles and rankings) — none of his
    // ratings.
    private static Fighter Twin(Fighter a)
    {
        var t = new Fighter
        {
            Id = a.Id, FirstName = "Twin", LastName = a.LastName, BirthDate = a.BirthDate,
            WeightClass = a.WeightClass, Sex = a.Sex, Nationality = a.Nationality, Disposition = a.Disposition,
            Momentum = a.Momentum, HomeCity = a.HomeCity, Country = a.Country,
        };
        t.Intangibles.Popularity = a.Intangibles.Popularity.Value;
        t.Intangibles.Charisma = a.Intangibles.Charisma.Value;
        t.Temperament.Ego = a.Temperament.Ego.Value;
        t.Temperament.Ambition = a.Temperament.Ambition.Value;
        t.Record.Wins = a.Record.Wins; t.Record.Losses = a.Record.Losses; t.Record.Draws = a.Record.Draws;
        t.Record.NoContests = a.Record.NoContests; t.Record.CurrentStreak = a.Record.CurrentStreak;
        t.Record.WinsByKo = a.Record.WinsByKo; t.Record.WinsBySubmission = a.Record.WinsBySubmission;
        t.Record.WinsByDecision = a.Record.WinsByDecision;
        t.BoutHistory.AddRange(a.BoutHistory);
        t.Accolades.AddRange(a.Accolades);
        return t;
    }

    [Fact]
    public void AHiddenGem_AsksLikeHisRecord()
    {
        var world = SeasonedWorld();
        // Unproven free agents: a couple of bouts at most, no name and no hype
        // (a hot streak and a following are public, and priced).
        var green = world.FreeAgents()
            .Where(f => f.Status == FighterStatus.Active && f.Record.Wins + f.Record.Losses <= 2
                        && f.Intangibles.Popularity.Value < 30 && f.Momentum <= 20)
            .OrderBy(f => f.Id.Value).Take(20).ToList();
        Assert.NotEmpty(green);
        foreach (var f in green)
        {
            decimal ordinary = Valuation.Evaluate(world, f).AskingPurse.Amount;
            SetAllRatings(f, 92); f.PotentialAbility = 97;   // secretly a world-beater
            decimal gem = Valuation.Evaluate(world, f).AskingPurse.Amount;
            Assert.Equal(ordinary, gem);
            Assert.True(gem <= 6_000m, $"{f.FullName} ({f.Record}) asks {gem}: an unproven kid fights for exposure money");
        }
    }

    [Fact]
    public void TheAsk_MovesWithTheRecord()
    {
        var world = SeasonedWorld();
        var f = world.Fighters.Values
            .Where(x => x.Status == FighterStatus.Active && x.Contract is not null
                        && x.Record.Wins + x.Record.Losses is >= 3 and <= 8 && x.Record.Losses >= 1
                        && !world.Titles.Values.Any(t => t.CurrentChampion == x.Id))
            .OrderBy(x => x.Id.Value).First();
        decimal start = Valuation.Evaluate(world, f).AskingPurse.Amount;

        // He keeps winning: the sheet gets longer and better.
        f.Record.Wins += 8;
        f.Record.CurrentStreak = 8;
        decimal winning = Valuation.Evaluate(world, f).AskingPurse.Amount;
        Assert.True(winning > start, $"8 more wins should raise the ask ({start} -> {winning})");

        // ...and wins a belt.
        var title = world.Titles.Values.Where(t => world.Get(t.Promotion) is { Defunct: false })
            .OrderBy(t => t.Id.Value).First();
        title.CurrentChampion = f.Id;
        decimal champ = Valuation.Evaluate(world, f).AskingPurse.Amount;
        Assert.True(champ > winning, $"a belt should raise the ask ({winning} -> {champ})");
        title.CurrentChampion = null;

        // A losing run takes it back down.
        f.Record.Losses += 12;
        f.Record.CurrentStreak = -4;
        decimal losing = Valuation.Evaluate(world, f).AskingPurse.Amount;
        Assert.True(losing < winning, $"12 more losses should lower the ask ({winning} -> {losing})");
    }

    [Fact]
    public void AStageIsPublic_TheSameSheetOnABiggerStage_AsksMore()
    {
        var world = SeasonedWorld();
        var f = world.Fighters.Values
            .Where(x => x.Status == FighterStatus.Active && x.Contract is not null && x.Record.Wins + x.Record.Losses >= 8)
            .OrderBy(x => x.Id.Value).First();
        var small = world.Promotions.Values.Where(p => !p.Defunct).OrderBy(p => p.Prestige.Value).ThenBy(p => p.Id.Value).First();
        var big = world.Promotions.Values.Where(p => !p.Defunct).OrderByDescending(p => p.Prestige.Value).ThenBy(p => p.Id.Value).First();
        var c = f.Contract!;
        f.Contract = new Contract { Promotion = small.Id, SignedOn = c.SignedOn, PursePerFight = c.PursePerFight, FightsRemaining = 2 };
        double lo = PublicStanding.Reputation(world, f);
        f.Contract = new Contract { Promotion = big.Id, SignedOn = c.SignedOn, PursePerFight = c.PursePerFight, FightsRemaining = 2 };
        double hi = PublicStanding.Reputation(world, f);
        f.Contract = c;
        Assert.True(hi > lo + 5, $"signed to {big.Abbreviation} he reads {hi:0.0}, to {small.Abbreviation} {lo:0.0}");
    }

    [Fact]
    public void SelfWorth_LeansByCharacter_AndIsStable()
    {
        var world = SeasonedWorld();
        var f = world.Fighters.Values.Where(x => x.Status == FighterStatus.Active).OrderBy(x => x.Id.Value).First();
        f.Disposition = Disposition.Mercenary;
        decimal merc = FighterValuation.SelfWorth(f);
        f.Disposition = Disposition.CompanyMan;
        decimal company = FighterValuation.SelfWorth(f);
        Assert.True(merc > company);
        Assert.Equal(company, FighterValuation.SelfWorth(f));
        Assert.InRange(company, 0.85m, 1.15m);
        Assert.InRange(merc, 0.85m, 1.15m);
    }

    [Fact]
    public void AReplacedBout_CountsOnTheRecord_AndAReloadPricesTheSame()
    {
        // A card is booked and the public read looked at (so any index is warm),
        // then a fighter is swapped out: the bout in that slot is a NEW Fight
        // object under the same id. After the card runs, the new bout must be
        // on the replacement's public record — and a saved-and-reloaded world
        // must price every fighter on the card exactly as the live one does.
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 11 });
        var booking = new PlayerBooking();
        var promo = world.Get(world.PlayerPromotion)!;
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(30));
        var div = promo.Divisions.OrderBy(d => d.Sex).ThenBy(d => d.WeightClass)
            .First(d => world.RosterOf(promo.Id).Count(f => d.Matches(f) && f.IsMatchReady(ev.Date)) >= 5);
        var pool = world.RosterOf(promo.Id).Where(f => div.Matches(f) && f.IsMatchReady(ev.Date)).OrderBy(f => f.Id.Value).ToList();
        Assert.True(booking.AddBout(world, ev, pool[0].Id, pool[1].Id, 3, false, out var err), err);
        Assert.True(booking.AddBout(world, ev, pool[2].Id, pool[3].Id, 3, false, out err), err);
        foreach (var f in world.Fighters.Values.Take(200)) PublicStanding.Read(world, f);   // warm the index

        var slot = ev.Card[0];
        var spare = pool[4];
        Assert.True(booking.ReplaceFighter(world, ev, slot.Id, slot.BlueCorner, spare.Id, out err), err);
        foreach (var f in world.Fighters.Values.Take(200)) PublicStanding.Read(world, f);

        new EventService().RunEvent(world, ev);
        var bouts = PublicStanding.RecentPublicBouts(world, spare, PublicStanding.RecentBouts);
        Assert.Contains(bouts, b => b.Date == ev.Date && (b.Won || b.Lost || b.Opponent == slot.RedCorner));
        Assert.Equal(ev.Card.First(x => x.Id == slot.Id).Outcome is { Winner: not null } ? 1 : 0,
            bouts.Count(b => b.Date == ev.Date && (b.Won || b.Lost)));

        string path = Path.Combine(Path.GetTempPath(), $"mma_honest_{Guid.NewGuid():N}.db");
        World loaded;
        try { SaveGame.Save(world, path); loaded = SaveGame.Load(path); }
        finally { File.Delete(path); }
        foreach (var id in ev.Card.SelectMany(x => new[] { x.RedCorner, x.BlueCorner }).Distinct())
            Assert.Equal(Snapshot(Valuation.Evaluate(world, world.Get(id)!)), Snapshot(Valuation.Evaluate(loaded, loaded.Get(id)!)));
    }

    [Fact]
    public void TheQuotedFigure_IsWhatTheRoomAsks_AndMeetingItSigns()
    {
        // The ask the player is quoted, the number the room opens at, and what
        // it takes to sign must agree to the dollar: tabling exactly the quoted
        // terms is accepted, never countered as a lowball.
        var world = SeasonedWorld();
        var promo = world.Get(world.PlayerPromotion)!;
        var fas = world.FreeAgents().Where(f => f.Status == FighterStatus.Active).OrderBy(f => f.Id.Value).Take(60).ToList();
        Assert.NotEmpty(fas);
        foreach (var f in fas)
        {
            var exp = Valuation.Evaluate(world, f);
            foreach (bool excl in new[] { true, false })
            {
                var quote = AskingQuote.For(world, promo, f, exp, excl);
                var room = new NegotiationSession(world, f, promo, exp);
                Assert.Equal(quote.Purse, room.DemandPurse(excl, false));
                Assert.True(quote.Purse.Amount >= exp.AskingPurse.Amount * (excl ? 1m : Negotiator.NonExclusiveMoneyFactor)
                                                  * Negotiator.TableDiscount(world, f, promo.Id),
                    "the quote never rounds below what he wants");
                var terms = room.TheirTermsAsOffer(excl, false);
                Assert.Equal(quote.Purse, terms.PursePerFight);
                Assert.Equal(TalkVerdict.Accept, room.Table(terms).Verdict);
            }
        }
    }

    [Fact]
    public void AnAiOpeningOffer_RoundsUpToTheAsk_ButNeverPastTheCap()
    {
        var world = SeasonedWorld();
        var negotiator = new Negotiator();
        foreach (var promo in world.Promotions.Values.Where(p => !p.Defunct).OrderBy(p => p.Id.Value))
        {
            decimal cap = Negotiator.PurseCap(world, promo);
            foreach (var f in world.FreeAgents().OrderBy(f => f.Id.Value).Take(40))
            {
                var exp = Valuation.Evaluate(world, f);
                foreach (decimal stretch in new[] { 1m, Lifers.RenewalStretch })
                {
                    var offer = negotiator.AiOpeningOffer(world, promo, f, exp, stretch);
                    Assert.True(offer.PursePerFight.Amount <= cap * stretch, $"{promo.Abbreviation} offered {offer.PursePerFight} over its cap {cap * stretch}");
                    decimal ask = exp.AskingPurse.Amount * (Negotiator.WantsExclusivity(promo) ? 1m : Negotiator.NonExclusiveMoneyFactor)
                                * Negotiator.TableDiscount(world, f, promo.Id);
                    if (ask <= cap * stretch - 500m) Assert.True(offer.PursePerFight.Amount >= ask);
                }
            }
        }
    }
}
