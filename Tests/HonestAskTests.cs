using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Living;

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
        var before = live.ToDictionary(f => f.Id, f => Snapshot(Valuation.Evaluate(world, f)));
        var readBefore = live.ToDictionary(f => f.Id, f => PublicStanding.Read(world, f));

        foreach (var f in world.Fighters.Values)
        {
            int was = FighterAbility.CurrentAbility(f);
            SetAllRatings(f, was >= 50 ? 20 : 95);   // flip the truth
            f.PotentialAbility = was >= 50 ? 21 : 99;
        }
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
}
