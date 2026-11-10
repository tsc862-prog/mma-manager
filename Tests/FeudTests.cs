using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Org;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Tests;

/// <summary>
/// Feuds as a real system: the matchmaker books grudge matches on purpose, off-cage
/// drama starts and stokes rivalries between fight nights, bad blood that can't be
/// settled cools off, and an AI org that's maxed its tier HOARDS toward the
/// promotion cushion instead of spending itself into a frozen summit.
/// </summary>
public class FeudTests
{
    [Fact]
    public void Matchmaker_BooksTheGrudgeMatch_OverBetterPairings()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 7, UseDesignedRoster = false });
        var promo = world.Promotions.Values.First(p => p.Id != world.PlayerPromotion && !p.Defunct);

        var champions = new HashSet<FighterId>();
        foreach (var d in promo.Divisions)
            if (d.Title is { } tid && world.Get(tid)?.CurrentChampion is { } cid)
                champions.Add(cid);

        // Two fighters two ability-slots apart: without a feud the matchmaker pairs
        // nearest neighbours first, so this exact pairing wouldn't lead the queue.
        // (Picked below the top of the pool — the #1 contender is booked into the
        // title main event and isn't available for undercard pairings.)
        var division = promo.Divisions.First(d => world.RosterOf(promo.Id)
            .Count(f => d.Matches(f) && f.IsMatchReady(world.CurrentDate) && !champions.Contains(f.Id)) >= 7);
        var pool = world.RosterOf(promo.Id)
            .Where(f => division.Matches(f) && f.IsMatchReady(world.CurrentDate) && !champions.Contains(f.Id))
            .OrderByDescending(Core.Sim.Ability.FighterAbility.CurrentAbility)
            .ToList();
        var (a, b) = (pool[3], pool[5]);
        a.Camp = null;   // isolate the grudge logic — campmates/friends refuse bouts by design
        b.Camp = null;
        world.Rivalries.Add(new Rivalry { A = a.Id, B = b.Id, Intensity = 80, Since = world.CurrentDate });

        var card = new Matchmaker().BuildCard(world, promo.Id);

        Assert.Contains(card, m => (m.Red == a.Id && m.Blue == b.Id) || (m.Red == b.Id && m.Blue == a.Id));
    }

    [Fact]
    public void Matchmaker_GivesTheHeatedRival_TheTitleShot()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 7, UseDesignedRoster = false });
        var promo = world.Promotions.Values.First(p => p.Id != world.PlayerPromotion && !p.Defunct);

        // The headliner is the champion who has gone longest without defending
        // (pre-history gives every champ a real last-fight date), so stage the
        // feud in the division the matchmaker will actually put on top.
        var lastFought = world.Events.Values.Where(e => e.Status == EventStatus.Completed)
            .SelectMany(e => e.Card.SelectMany(f => new[] { (Id: f.RedCorner, e.Date), (Id: f.BlueCorner, e.Date) }))
            .GroupBy(x => x.Id)
            .ToDictionary(g => g.Key, g => g.Max(x => x.Date));
        int Idle(FighterId id) => lastFought.TryGetValue(id, out var d)
            ? world.CurrentDate.DayNumber - d.DayNumber : 3650;
        var division = promo.Divisions
            .Where(d => d.Title is { } tid && world.Get(tid)?.CurrentChampion is { } c
                        && world.Get(c)!.IsMatchReady(world.CurrentDate) && d.Rankings.Count >= 3)
            .OrderByDescending(d => Idle(world.Get(d.Title!.Value)!.CurrentChampion!.Value))
            .ThenBy(d => (int)d.WeightClass)
            .First();
        var champ = world.Get(world.Get(division.Title!.Value)!.CurrentChampion!.Value)!;

        // A worthy contender further down the queue, with real bad blood.
        var rival = division.Rankings.Skip(1).Select(world.Get)
            .First(f => f is { Status: FighterStatus.Active } && f.Id != champ.Id && f.IsMatchReady(world.CurrentDate))!;
        rival.Record.Wins = 20;
        rival.Record.Losses = 2;
        rival.Record.CurrentStreak = 4;
        world.Rivalries.Add(new Rivalry { A = champ.Id, B = rival.Id, Intensity = 90, Since = world.CurrentDate });

        var card = new Matchmaker().BuildCard(world, promo.Id);
        var titleBout = card.FirstOrDefault(m => m.IsTitle && m.WeightClass == division.WeightClass);

        Assert.True(titleBout != default, "the division's belt should headline");
        Assert.True(titleBout.Red == rival.Id || titleBout.Blue == rival.Id,
            "the heated rival should jump the queue for the title shot");
    }

    [Fact]
    public void MonthlyDrama_StartsAndStokesFeuds_BetweenFightNights()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9, UseDesignedRoster = false });
        var rng = new SimRandom(123);

        // Seed one live same-org feud and watch drama stoke it (or new beefs appear).
        var promo = world.Promotions.Values.First(p => !p.Defunct);
        var division = promo.Divisions.First(d => d.Rankings.Count >= 2);
        var (a, b) = (division.Rankings[0], division.Rankings[1]);
        world.Rivalries.Add(new Rivalry { A = a, B = b, Intensity = 40, Since = world.CurrentDate });
        int startCount = world.Rivalries.Count;

        for (int month = 0; month < 100; month++)
            Storylines.MonthlyDrama(world, world.CurrentDate.AddDays(month * 30), rng);

        var seeded = world.Rivalries.FirstOrDefault(r => r.IsBetween(a, b));
        bool flared = seeded is { Intensity: > 40 };
        bool newBeef = world.Rivalries.Count(r => !r.IsBetween(a, b)) > startCount - 1;
        Assert.True(flared || newBeef, "eight years of drama should flare the feud or start a new one");
    }

    [Fact]
    public void CrossOrgFeuds_CoolOff_AndDie()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9, UseDesignedRoster = false });
        var rng = new SimRandom(5);

        // Two fighters on DIFFERENT rosters — the fight can't be made, so it fades.
        var promos = world.Promotions.Values.Where(p => !p.Defunct).Take(2).ToList();
        var a = world.RosterOf(promos[0].Id).First(f => f.Status == FighterStatus.Active);
        var b = world.RosterOf(promos[1].Id).First(f => f.Status == FighterStatus.Active);
        world.Rivalries.Add(new Rivalry { A = a.Id, B = b.Id, Intensity = 25, Since = world.CurrentDate });

        for (int month = 0; month < 12; month++)
            Storylines.MonthlyDrama(world, world.CurrentDate.AddDays(month * 30), rng);

        Assert.Null(world.Rivalries.FirstOrDefault(r => r.IsBetween(a.Id, b.Id)));
    }

    [Fact]
    public void AMaxedTierOrg_CommandsAPartnerPremium_TheCrownDoesNot()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5, UseDesignedRoster = false });
        var promo = world.Promotions.Values.First(p => !p.Defunct);

        // Mid-tier, not pinned (and no funded war chest): standard rates.
        promo.TierClearance = OrgSize.National;
        promo.PrestigeHundredths = 4000;
        promo.Balance = new Money(1_000_000m);
        Assert.Equal(1.0m, OrgPerks.PartnerPremium(promo));

        // Pinned at the National ceiling: partners pay up to lock in the next big thing.
        promo.PrestigeHundredths = OrgSizing.PrestigeCeiling(OrgSize.National) * 100;
        Assert.True(OrgPerks.PartnerPremium(promo) > 1.0m,
            "a ceiling-pinned org should command a partner premium");

        // The reigning International crown has no next stage to sell — no premium.
        promo.TierClearance = OrgSize.International;
        promo.PrestigeHundredths = 7400;
        Assert.Equal(1.0m, OrgPerks.PartnerPremium(promo));
    }

    [Fact]
    public void AnEligibleAiOrg_HoardsForTheLeap_ThenTakesIt()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5, UseDesignedRoster = false });
        var promo = world.Promotions.Values.First(p => p.Id != world.PlayerPromotion && !p.Defunct);
        promo.TierClearance = OrgSize.National;
        promo.PrestigeHundredths = OrgSizing.PrestigeCeiling(OrgSize.National) * 100;   // maxed the tier
        promo.Balance = new Money(9_000_000m);   // rich — but short of the $12M Continental cushion

        // Saving mode: only a token campaign, the rest banked. The old treadmill
        // would have set ~0.42 × surplus (≈ $445k) and frozen the summit forever.
        AiFrontOffice.RunMonthly(world, world.CurrentDate);
        Assert.True(promo.MarketingBudget.Amount <= 150_000m,
            $"an org saving for the leap shouldn't pour out its war chest (budget {promo.MarketingBudget})");

        // The war chest arrives — the board takes the leap.
        promo.Balance = new Money(12_500_000m);
        PromotionLadder.RunMonthly(world, world.CurrentDate);
        Assert.Equal(OrgSize.Continental, promo.TierClearance);
    }

    [Fact]
    public void ARushedTier_PricesTheAiOut_ButNeverThePlayer()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5, UseDesignedRoster = false });
        var promo = world.Promotions.Values.First(p => p.Id != world.PlayerPromotion && !p.Defunct);
        promo.TierClearance = OrgSize.National;
        promo.PrestigeHundredths = OrgSizing.PrestigeCeiling(OrgSize.National) * 100;   // maxed the tier
        promo.Balance = new Money(12_500_000m);   // the base $12M cushion, banked

        // A land-rush into the Continental scene: three rivals bought in within the
        // year, so the market prices the next entrant out — the AI holds and waits.
        foreach (var rival in world.Promotions.Values.Where(p => p.Id != promo.Id && !p.Defunct).Take(3))
        {
            rival.LastClearedTier = OrgSize.Continental;
            rival.LastClearedOn = world.CurrentDate.AddMonths(-4);
        }
        Assert.True(PromotionLadder.PromotionCushion(world, promo) > PromotionLadder.PromotionCushion(promo));

        PromotionLadder.RunMonthly(world, world.CurrentDate);
        Assert.Equal(OrgSize.National, promo.TierClearance);   // priced out — holds and waits

        // The PLAYER is never priced out: eligibility alone opens the door.
        var player = world.Get(world.PlayerPromotion)!;
        player.TierClearance = OrgSize.National;
        player.PrestigeHundredths = OrgSizing.PrestigeCeiling(OrgSize.National) * 100;
        player.Balance = new Money(100_000m);   // broke, even — their call to make
        PromotionLadder.Promote(world, player, world.CurrentDate);
        Assert.Equal(OrgSize.Continental, player.TierClearance);

        // Nobody has to leave: the rush simply AGES out of the market's memory,
        // and the queued AI org walks through the reopened door.
        foreach (var rival in world.Promotions.Values.Where(p => p.Id != promo.Id && p.Id != player.Id))
            rival.LastClearedOn = world.CurrentDate.AddMonths(-40);
        PromotionLadder.RunMonthly(world, world.CurrentDate.AddMonths(1));
        Assert.Equal(OrgSize.Continental, promo.TierClearance);
    }
}
