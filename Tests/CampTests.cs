using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Tests;

/// <summary>
/// Fight camps, phase 1 (see CAMPS-DESIGN.md): the designed rooms open at world
/// build in real map cities, most fighters sort into geographically/stylistically
/// coherent camps (elite rooms fill with the best talent first), development runs
/// through the room's growth multiplier, newgens find a gym near home, and the
/// whole layer round-trips through the save file.
/// </summary>
public class CampTests
{
    [Fact]
    public void WorldBuild_OpensTheDesignedCamps_InRealCities()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });

        Assert.True(world.Camps.Count >= 12, $"the designed camps should open (got {world.Camps.Count})");
        Assert.All(world.Camps.Values, c => Assert.NotNull(world.Get(c.City)));
        Assert.All(world.Camps.Values, c => Assert.True(c.CoachQuality is > 0 and <= 100));
    }

    [Fact]
    public void Fighters_SortIntoCoherentRooms_AndEliteCampsStayWithinCapacity()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
        var active = world.Fighters.Values
            .Where(f => f.Status != FighterStatus.Retired && !f.LocalTalent).ToList();

        // Most of the sport trains somewhere, and nobody trains at an over-full room.
        double affiliated = active.Count(f => f.Camp is not null) / (double)active.Count;
        Assert.InRange(affiliated, 0.45, 0.90);
        foreach (var camp in world.Camps.Values)
            Assert.True(active.Count(f => f.Camp == camp.Id) <= camp.Capacity,
                $"{camp.Name} is over capacity");

        // Coherence: the SORT only ever puts a fighter in a room on their own
        // continent. Authored placements are deliberate exceptions — Okonjo's
        // wrestling really was learned on an American scholarship — so they're
        // out of scope here.
        var authored = Core.Sim.Generation.DesignedRoster.CampHomes.Keys.ToHashSet();
        foreach (var f in active.Where(f => f.Camp is not null && !(f.Designed && authored.Contains(f.LastName))))
        {
            var city = world.Get(world.Get(f.Camp!.Value)!.City)!;
            Assert.True(city.Region == f.Nationality || city.Country == f.Country,
                $"{f.FullName} ({f.Nationality}) trains at a camp in {city.Region}");
        }
    }

    [Fact]
    public void EliteRooms_AreNotAllStars_LowerNamesShareTheMats()
    {
        // Gyms take people, not rankings: the biggest earners likely get the
        // spots, but it's normal for lower names to be part of a big camp.
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
        var elite = world.Camps.Values.Where(c => c.CoachQuality >= 80).ToList();
        Assert.NotEmpty(elite);

        bool mixed = elite.Any(c =>
        {
            var abilities = world.Fighters.Values
                .Where(f => f.Camp == c.Id && f.Status != FighterStatus.Retired)
                .Select(f => Core.Sim.Ability.FighterAbility.CurrentAbility(f)).ToList();
            return abilities.Count >= 6 && abilities.Max() - abilities.Min() >= 15;
        });
        Assert.True(mixed, "at least one elite room should span stars down to lesser names");
    }

    [Fact]
    public void TheRoomDrivesDevelopment_SoloTrainingStunts()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
        var elite = world.Camps.Values.OrderByDescending(c => c.CoachQuality).First();
        var f = world.Fighters.Values.First(x => x.Status == FighterStatus.Active);

        f.Camp = null;
        Assert.Equal(Camps.UnaffiliatedMultiplier, Camps.GrowthMultiplier(world, f));

        f.Camp = elite.Id;
        double atElite = Camps.GrowthMultiplier(world, f);
        Assert.True(atElite > 1.10, $"an elite room should be a real edge (got {atElite:F2})");
        Assert.True(atElite > Camps.UnaffiliatedMultiplier + 0.2, "the gap between solo and elite should matter");
    }

    [Fact]
    public void Newgens_FindARoomNearHome()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 11 });
        var rng = new SimRandom(5);
        var intake = NewgenService.ForWorld(world).GenerateIntake(world, 60, rng);

        var joined = intake.Where(f => f.Camp is not null).ToList();
        Assert.True(joined.Count > intake.Count / 3, $"most newgens should find a gym ({joined.Count}/{intake.Count})");
        foreach (var f in joined)
        {
            var city = world.Get(world.Get(f.Camp!.Value)!.City)!;
            Assert.True(city.Region == f.Nationality || city.Country == f.Country,
                "a newgen joins a room on their own continent");
        }
    }

    // --- Phase 2: the people --------------------------------------------------

    [Fact]
    public void AStarRetiring_WhereNoEliteRoomExists_OpensTheirOwnGym()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
        var rng = new SimRandom(1);

        // A beloved star from a country with no elite camp hangs up the gloves.
        var star = world.Fighters.Values.First(f => f.Status == FighterStatus.Active
            && !world.Camps.Values.Any(c => c.CoachQuality >= 70 && world.Get(c.City)?.Country == f.Country));
        star.Intangibles.Popularity = 75;
        int before = world.Camps.Count;

        Lifecycle.Retire(world, star, world.CurrentDate);
        for (int i = 0; i < 10 && world.Camps.Count == before; i++)   // founding is a 60% roll
            Camps.OnRetirement(world, star, world.CurrentDate, rng);

        var gym = world.Camps.Values.FirstOrDefault(c => c.HeadCoach == star.Id);
        Assert.NotNull(gym);
        Assert.Equal($"Team {star.LastName}", gym!.Name);
        Assert.Equal(star.PrimaryStyle, gym.Specialty);
        Assert.Contains(world.News, n => n.Headline.Contains($"opens Team {star.LastName}"));
    }

    [Fact]
    public void AHeadCoach_TakesTheRoomsBestProspect_AndTheProtegeGrowsFaster()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
        var rng = new SimRandom(2);
        var camp = world.Camps.Values.OrderByDescending(c => c.CoachQuality).First();

        // Install a retired legend at the head of the room, with a young
        // high-ceiling prospect on the mats (a newgen is 18-22 by construction).
        var coach = world.Fighters.Values.First(f => f.Status == FighterStatus.Retired || f.Camp != camp.Id);
        coach.Status = FighterStatus.Retired;
        camp.HeadCoach = coach.Id;
        var prospect = NewgenService.ForWorld(world).GenerateIntake(world, 1, new SimRandom(9))[0];
        prospect.Camp = camp.Id;
        prospect.PotentialAbility = 80;

        double before = Camps.GrowthMultiplier(world, prospect);
        for (int month = 0; month < 12; month++)   // taking a protégé is a monthly roll
            Camps.RunMonthly(world, world.CurrentDate.AddDays(month * 30), rng);

        var bond = world.Relationships.FirstOrDefault(r => r.Kind == RelationshipKind.Mentor && r.A == coach.Id);
        Assert.NotNull(bond);
        Assert.Equal(camp.Id, world.Fighters[bond!.B].Camp);
        if (bond.B == prospect.Id)
            Assert.Equal(before + Camps.MentorBonus, Camps.GrowthMultiplier(world, prospect), 3);
    }

    [Fact]
    public void Teammates_AreNeverBookedAgainstEachOther_OnTheUndercard()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 7, UseDesignedRoster = false });
        var promo = world.Promotions.Values.First(p => p.Id != world.PlayerPromotion && !p.Defunct);

        // Put the whole roster in ONE room, then build a card: no undercard bout
        // should pair two of them... which means no undercard at all.
        var camp = world.Camps.Values.First();
        foreach (var f in world.RosterOf(promo.Id)) f.Camp = camp.Id;

        var card = new Matchmaker().BuildCard(world, promo.Id);

        Assert.All(card.Where(m => !m.IsTitle), m =>
            Assert.True(world.Fighters[m.Red].Camp != world.Fighters[m.Blue].Camp,
                "campmates fought on the undercard"));
    }

    // --- Phase 3: the player's money -------------------------------------------

    [Fact]
    public void AStipend_MovesAFighterToABetterRoom_AndTheBillArrives()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
        var promo = world.Promotions.Values.First(p => !p.Defunct);
        var f = world.RosterOf(promo.Id).First(x => x.Status == FighterStatus.Active
            && (x.Camp is null || world.Get(x.Camp.Value)!.CoachQuality < 60));
        int loyaltyBefore = f.Loyalty;
        double growthBefore = Camps.GrowthMultiplier(world, f);

        var room = Camps.PayStipend(world, promo, f);

        Assert.NotNull(room);
        Assert.True(f.CampStipend);
        Assert.True(f.Loyalty > loyaltyBefore, "they remember who paid for their corner");
        Assert.True(Camps.GrowthMultiplier(world, f) > growthBefore, "the better room should grow them faster");

        decimal bankBefore = promo.Balance.Amount;
        Camps.ChargeMonthly(world, world.CurrentDate);
        Assert.True(promo.Balance.Amount < bankBefore, "the fee lands on the org's books");
        Assert.Contains(promo.Ledger, e => e.Category == FinanceCategory.CampFees);

        Camps.StopStipend(world, f);
        Assert.False(f.CampStipend);
    }

    [Fact]
    public void ATeamDeal_HalvesStipends_AndBillsTheRetainer()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
        var promo = world.Promotions.Values.First(p => !p.Defunct);
        var f = world.RosterOf(promo.Id).First(x => x.Status == FighterStatus.Active
            && (x.Camp is null || world.Get(x.Camp.Value)!.CoachQuality < 60));
        Assert.NotNull(Camps.PayStipend(world, promo, f));
        var room = world.Get(f.Camp!.Value)!;

        // Without the deal: full fee. With it: half — plus the retainer.
        Camps.ChargeMonthly(world, world.CurrentDate);
        decimal fullFee = -promo.Ledger.Last(e => e.Category == FinanceCategory.CampFees
            && e.Description.Contains(f.FullName)).Amount.Amount;

        promo.SponsoredCamp = room.Id;
        Camps.ChargeMonthly(world, world.CurrentDate.AddMonths(1));
        decimal halfFee = -promo.Ledger.Last(e => e.Category == FinanceCategory.CampFees
            && e.Description.Contains(f.FullName)).Amount.Amount;

        Assert.Equal(fullFee / 2, halfFee);
        Assert.Contains(promo.Ledger, e => e.Description.Contains("Team deal"));
    }

    [Fact]
    public void ACampReport_CostsMoney_AndMailsTheIntel()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
        var player = world.Get(world.PlayerPromotion)!;
        var camp = world.Camps.Values.OrderByDescending(c => c.Reputation).First();
        decimal bankBefore = player.Balance.Amount;

        Assert.True(Camps.BuyReport(world, player, camp, world.CurrentDate));

        Assert.True(player.Balance.Amount < bankBefore);
        var mail = world.Mail.Last(m => m.Subject.Contains("Camp report"));
        Assert.Contains(camp.Name, mail.Subject);
        Assert.Contains("pros in the room", mail.Body);
    }

    // The fog on the paid camp report: its ONE reveal is each man's exact current ability ("looks a 62").
    // The order of the room and the room-to-grow line are what anybody could say from outside.
    private static void SetAllRatings(Fighter f, int v)
    {
        foreach (object block in new object[] { f.Striking, f.Grappling, f.Physical, f.Mental })
            foreach (var p in block.GetType().GetProperties())
                if (p.PropertyType == typeof(Rating) && p.CanWrite) p.SetValue(block, (Rating)v);
    }

    private static string WithoutTheReveal(string body) =>
        System.Text.RegularExpressions.Regex.Replace(body, @"looks a \d+", "looks a ?");

    [Fact]
    public void ACampReport_OrdersAndWordsTheRoom_OffPublicFacts_AndStillRevealsTheExactAbility()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
        var player = world.Get(world.PlayerPromotion)!;
        var camp = world.Camps.Values.OrderByDescending(c => c.Reputation).ThenBy(c => c.Id.Value).First();
        var members = world.Fighters.Values
            .Where(f => f.Camp == camp.Id && f.Status == FighterStatus.Active).OrderBy(f => f.Id.Value).ToList();
        Assert.True(members.Count >= 4, $"{camp.Name} has {members.Count} pros");

        Assert.True(Camps.BuyReport(world, player, camp, world.CurrentDate));
        string before = world.Mail.Last(m => m.Subject.Contains("Camp report")).Body;

        // Turn the truth upside down inside the room: the better half become the worse and the ceilings run the
        // other way. Nothing public moves (record, age, rankings, camp). Everybody lands in one revealed band
        // (Strong), since the band of the number the report shows is itself part of the paid reveal.
        var trueOrderBefore = members.OrderByDescending(Core.Sim.Ability.FighterAbility.CurrentAbility).ThenBy(f => f.Id.Value)
            .Select(f => f.Id).ToList();
        var betterHalf = trueOrderBefore.Take(members.Count / 2).ToHashSet();
        foreach (var f in members)
        {
            bool wasGood = betterHalf.Contains(f.Id);
            SetAllRatings(f, wasGood ? 66 : 78);
            f.PotentialAbility = wasGood ? 67 : 99;   // the old top now has no room left, the old bottom plenty
        }
        var trueOrderAfter = members.OrderByDescending(Core.Sim.Ability.FighterAbility.CurrentAbility).ThenBy(f => f.Id.Value)
            .Select(f => f.Id).ToList();
        Assert.NotEqual(trueOrderBefore, trueOrderAfter);

        player.Balance = new Money(player.Balance.Amount + 1_000_000m);
        Assert.True(Camps.BuyReport(world, player, camp, world.CurrentDate));
        string after = world.Mail.Last(m => m.Subject.Contains("Camp report")).Body;

        // Same order, same words: only the paid-for numbers changed.
        Assert.Equal(WithoutTheReveal(before), WithoutTheReveal(after));
        Assert.NotEqual(before, after);

        // The reveal stays: every man's exact current ability is on his line.
        var lines = after.Split('\n').Where(l => l.StartsWith("•")).ToList();
        Assert.Equal(members.Count, lines.Count);
        foreach (var f in members)
        {
            var line = Assert.Single(lines, l => l.Contains(f.FullName + " ("));
            Assert.Contains($"looks a {Core.Sim.Ability.FighterAbility.CurrentAbility(f)},", line);
        }
    }

    [Fact]
    public void TwoFighters_WithTheSamePublicPicture_GetTheSameGrowthLine_WhateverTheirCeiling()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
        var date = world.CurrentDate;
        // Every age and record the world offers; within one revealed band (Strong: 65-79) the ability shown and
        // the hidden ceiling are both turned from one end to the other, and the line must not move.
        var sample = world.Fighters.Values.Where(f => f.Status == FighterStatus.Active)
            .OrderBy(f => f.Id.Value).Take(300).ToList();
        var ages = new HashSet<int>();
        foreach (var f in sample)
        {
            SetAllRatings(f, 66); f.PotentialAbility = 67;
            int lowShown = Core.Sim.Ability.FighterAbility.CurrentAbility(f);
            string low = Camps.GrowthRead(f, date);

            SetAllRatings(f, 78); f.PotentialAbility = 99;
            int highShown = Core.Sim.Ability.FighterAbility.CurrentAbility(f);
            Assert.Equal(Core.Sim.Scouting.ScoutReport.Band(lowShown), Core.Sim.Scouting.ScoutReport.Band(highShown));
            Assert.True(highShown > lowShown);

            Assert.Equal(low, Camps.GrowthRead(f, date));
            ages.Add(f.AgeOn(date));
        }
        Assert.True(ages.Min() <= 22 && ages.Max() >= 32, "the sample should span young and old");

        // And it reads age, not the ceiling: a kid and a veteran with the same record and the same number are
        // told apart.
        var kid = sample.First(f => f.AgeOn(date) <= 21);
        var vet = sample.First(f => f.AgeOn(date) >= 33);
        Assert.NotEqual(Camps.GrowthRead(kid, date), Camps.GrowthRead(vet, date));
    }

    [Fact]
    public void ACampReport_ReadsTheRoomInPublicOrder()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
        var rank = Camps.PublicRanks(world);
        var room = world.Fighters.Values.Where(f => f.Status == FighterStatus.Active)
            .OrderBy(f => f.Id.Value).Take(400).ToList();
        var ordered = Camps.ReportOrder(room, rank).ToList();

        // Ranked men lead, best place first; the unranked follow by record.
        int firstUnranked = ordered.FindIndex(f => !rank.ContainsKey(f.Id));
        Assert.True(firstUnranked > 0, "the sample should hold ranked men");
        for (int i = 1; i < firstUnranked; i++)
            Assert.True(rank[ordered[i - 1].Id] <= rank[ordered[i].Id]);
        Assert.All(ordered.Skip(firstUnranked), f => Assert.False(rank.ContainsKey(f.Id)));
        for (int i = firstUnranked + 1; i < ordered.Count; i++)
            Assert.True(ordered[i - 1].Record.Wins >= ordered[i].Record.Wins);

        // Iteration order never matters.
        var reversed = room.AsEnumerable().Reverse().ToList();
        Assert.Equal(ordered.Select(f => f.Id), Camps.ReportOrder(reversed, rank).Select(f => f.Id));
    }

    [Fact]
    public void TheCampGrapevine_ClearsTheFog()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
        var mine = world.RosterOf(world.PlayerPromotion).FirstOrDefault(f => f.Camp is not null);
        Assert.NotNull(mine);

        // A stranger who shares the room with one of ours is half-seen already.
        var stranger = world.Fighters.Values.First(f =>
            f.Camp == mine!.Camp && f.Id != mine.Id && f.Contract?.Promotion != world.PlayerPromotion
            && f.Status == FighterStatus.Active);
        Assert.Equal(0.5, Camps.FamiliarityBonus(world, stranger));

        // A famous room puts its members on everyone's tape — the AI misjudges less.
        var famous = world.Camps.Values.First(c => c.Reputation >= 70);
        var nobody = world.Fighters.Values.First(f => f.Record.TotalBouts <= 3 && f.Camp is null
            && f.Status == FighterStatus.Active);
        var rival = world.Promotions.Values.First(p => p.Id != world.PlayerPromotion && !p.Defunct);
        int truth = Core.Sim.Ability.FighterAbility.CurrentAbility(nobody);

        int errSolo = Math.Abs(Core.Sim.Scouting.ScoutingService.PerceivedAbility(world, rival, nobody) - truth);
        nobody.Camp = famous.Id;
        int errFamous = Math.Abs(Core.Sim.Scouting.ScoutingService.PerceivedAbility(world, rival, nobody) - truth);
        Assert.True(errFamous <= errSolo, $"a famous camp should narrow the guesswork ({errSolo} -> {errFamous})");
    }

    // --- Phase 4: gym wars -------------------------------------------------------

    [Fact]
    public void TwoRoomsWhoseFightersKeepFeuding_AreAtWar()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
        var camps = world.Camps.Values.Take(2).ToList();
        var aSide = world.Fighters.Values.Where(f => f.Status == FighterStatus.Active).Take(4).ToList();
        aSide[0].Camp = camps[0].Id; aSide[1].Camp = camps[0].Id;
        aSide[2].Camp = camps[1].Id; aSide[3].Camp = camps[1].Id;

        world.Rivalries.Add(new Rivalry { A = aSide[0].Id, B = aSide[2].Id, Intensity = 60, Since = world.CurrentDate });
        Assert.False(Camps.AtWar(world, camps[0].Id, camps[1].Id));   // one feud is personal

        world.Rivalries.Add(new Rivalry { A = aSide[1].Id, B = aSide[3].Id, Intensity = 50, Since = world.CurrentDate });
        Assert.True(Camps.AtWar(world, camps[0].Id, camps[1].Id));    // two is a gym war
    }

    [Fact]
    public void Camps_RoundTrip_ThroughTheSave()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5 });
        var someCamp = world.Camps.Values.First();
        var member = world.Fighters.Values.First(f => f.Camp == someCamp.Id);
        member.CampStipend = true;
        world.Get(world.PlayerPromotion)!.SponsoredCamp = someCamp.Id;

        var path = Path.Combine(Path.GetTempPath(), $"camps-{Guid.NewGuid():N}.db");
        try
        {
            MmaManager.Persistence.SaveGame.Save(world, path);
            var loaded = MmaManager.Persistence.SaveGame.Load(path);

            Assert.Equal(world.Camps.Count, loaded.Camps.Count);
            var camp = loaded.Camps[someCamp.Id];
            Assert.Equal(someCamp.Name, camp.Name);
            Assert.Equal(someCamp.Specialty, camp.Specialty);
            Assert.Equal(someCamp.CoachQuality, camp.CoachQuality);
            Assert.Equal(someCamp.MonthlyFee, camp.MonthlyFee);
            Assert.Equal(someCamp.Id, loaded.Fighters[member.Id].Camp);
            Assert.True(loaded.Fighters[member.Id].CampStipend);
            Assert.Equal(someCamp.Id, loaded.Get(loaded.PlayerPromotion)!.SponsoredCamp);
        }
        finally { File.Delete(path); }
    }
}
