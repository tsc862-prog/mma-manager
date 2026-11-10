using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Org;
using Standing = MmaManager.Core.Sim.Org.Standing;

namespace MmaManager.Tests;

/// <summary>
/// The progression layer: the within-tier climb meter, one-time milestones, and
/// the talent treadmill (bigger orgs poaching the player's expiring stars).
/// </summary>
public class ProgressionTests
{
    // --- Tier progress meter -------------------------------------------------

    [Fact]
    public void TierProgress_ReadsHalfwayBetweenTierFloors()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options
        {
            Seed = 5,
            Founding = new WorldBuilder.FoundingSpec { Prestige = 38 },   // National band is 32–45
        });
        var promo = world.Get(world.PlayerPromotion)!;
        var p = OrgSizing.Progress(promo);

        Assert.Equal(OrgSize.National, p.Tier);
        Assert.Equal(OrgSize.Continental, p.Next);
        Assert.InRange(p.Percent, 40, 55);   // 38 of 32→45 ≈ 46%
    }

    [Fact]
    public void TierProgress_MovesSmoothlyWithFractionalPrestige()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options
        {
            Seed = 5,
            Founding = new WorldBuilder.FoundingSpec { Prestige = 32 },
        });
        var promo = world.Get(world.PlayerPromotion)!;
        int at32 = OrgSizing.Progress(promo).Percent;

        Standing.Apply(world, promo, 0.6, world.CurrentDate, "sliver");   // 32.00 → 32.60, same tier
        Assert.Equal(32, promo.Prestige.Value);                          // whole number unchanged
        Assert.True(OrgSizing.Progress(promo).Percent > at32, "the bar should creep even within a whole point");
    }

    [Fact]
    public void TopTier_ShowsProgressTowardTheCeiling()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5 });
        var afc = world.Promotions.Values.OrderByDescending(p => p.Prestige.Value).First();
        afc.Prestige = 85;   // International (70–100)
        var p = OrgSizing.Progress(afc);
        Assert.Equal(OrgSize.International, p.Tier);
        Assert.Null(p.Next);
        Assert.InRange(p.Percent, 45, 55);   // 85 of 70→100 = 50%
    }

    // --- Milestones ----------------------------------------------------------

    [Fact]
    public void FirstShow_FiresOnce_WithNewsAndMail()
    {
        var world = FoundedWorld(seed: 41);
        var promo = world.Get(world.PlayerPromotion)!;
        var mine = HireOne(world);
        var booking = new PlayerBooking();

        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(7));
        booking.AddLocalBout(world, ev, mine.Id, PrimaryStyle.Striker, 3, out _);
        new EventService().RunEvent(world, ev);

        Assert.Contains("first-show", promo.Milestones);
        Assert.Contains(world.Mail, m => m.Subject.Contains("First show"));
        Assert.Contains(world.News, n => n.Headline.Contains("First show"));

        // A second show doesn't re-fire it.
        int mailBefore = world.Mail.Count(m => m.Subject.Contains("First show"));
        var ev2 = booking.CreateEvent(world, world.CurrentDate.AddDays(14));
        booking.AddLocalBout(world, ev2, mine.Id, PrimaryStyle.Striker, 3, out _);
        new EventService().RunEvent(world, ev2);
        Assert.Equal(mailBefore, world.Mail.Count(m => m.Subject.Contains("First show")));
    }

    [Fact]
    public void Milestones_ArePlayerOnly()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
        var ai = world.Promotions.Values.First(p => p.Id != world.PlayerPromotion);

        Milestones.Award(world, ai, world.CurrentDate, "first-show", "First show", "body");

        Assert.Empty(ai.Milestones);
    }

    // --- Talent treadmill ----------------------------------------------------

    [Fact]
    public void ABiggerOrg_PoachesTheExpiringStar_WhoWasNotReSigned()
    {
        var world = FoundedWorld(seed: 42);
        var player = world.Get(world.PlayerPromotion)!;
        player.Prestige = 20;   // Regional — the AFC/VMMA tower over us

        // A genuine, affordable prospect on a deal that's about to run out.
        var star = HireOne(world);
        star.PotentialAbility = 90;
        foreach (var s in RaiseAbility(star)) s(82);   // a genuine star — good enough for the giants
        star.Intangibles.Popularity = 60;
        star.Loyalty = 0;                     // a mercenary — isolates the fit test from loyalty
        star.Contract!.FightsRemaining = 0;   // free agency now

        bool poached = PoachingService.TryPoachFromPlayer(world, star, world.CurrentDate);

        Assert.True(poached, "a bigger org should take an unprotected star");
        Assert.NotEqual(player.Id, star.Contract!.Promotion);
        Assert.True(OrgSizing.SizeOf(world.Get(star.Contract.Promotion)!) > OrgSizing.SizeOf(player));
        Assert.Contains(world.Mail, m => m.Subject.Contains($"We lost {star.FullName}"));
    }

    [Fact]
    public void APoachedChampion_VacatesTheBelt()
    {
        var world = FoundedWorld(seed: 43);
        var player = world.Get(world.PlayerPromotion)!;
        player.Prestige = 20;
        var star = HireOne(world);
        star.PotentialAbility = 90;
        foreach (var s in RaiseAbility(star)) s(82);   // a genuine star — good enough for the giants
        star.Intangibles.Popularity = 60;
        star.Loyalty = 0;                     // a mercenary — isolates the belt-vacating test from loyalty
        star.Record.CurrentStreak = 3;        // in form — the pre-history could have dealt them a skid
        star.Contract!.FightsRemaining = 0;

        // Make them the champion of their division's belt.
        var title = player.Titles.Select(world.Get).First(t => t!.WeightClass == star.WeightClass)!;
        title.CurrentChampion = star.Id;
        title.Lineage.Add(new TitleReign { Champion = star.Id, WonOn = world.CurrentDate.AddMonths(-2) });

        Assert.True(PoachingService.TryPoachFromPlayer(world, star, world.CurrentDate));
        Assert.Null(title.CurrentChampion);   // belt vacated on the way out
        Assert.Contains(world.News, n => n.Headline.Contains("vacates"));
    }

    [Fact]
    public void OverTwoYears_BiggerAiOrgsPoachStars_FromSmallerOnes()
    {
        // Player is AFC (the top org): it's never poached (nobody's bigger) and never
        // auto-acquires (excluded as a suitor), so every "signs X away from Y" here is
        // one AI org skimming an expiring star off a smaller AI org.
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9 });
        new WorldTick().AdvanceWeeks(world, 104);

        int poaches = world.News.Count(n => n.Headline.Contains(" signs ") && n.Headline.Contains(" away from "));
        Assert.True(poaches > 0, $"bigger AI orgs should poach expiring stars off smaller ones over time (saw {poaches})");
    }

    [Fact]
    public void ALoyalFighter_ResistsAPoach_ThatAMercenaryWouldTake()
    {
        // Same star, same suitor — the only difference is loyalty.
        Fighter Setup(World w)
        {
            var player = w.Get(w.PlayerPromotion)!;
            player.Prestige = 20;
            var s = HireOne(w);
            foreach (var a in RaiseAbility(s)) a(72);
            // Ability alone isn't enough under fog — suitors scout the RÉSUMÉ,
            // and the founding pool skews to unproven kids. Give the star a
            // record the tape can back up (ON A WIN STREAK — nobody chases a
            // fighter in free-fall, whatever their loyalty).
            s.Record.Wins = 14; s.Record.Losses = 2; s.Record.WinsByKo = 7; s.Record.WinsByDecision = 7;
            s.Record.CurrentStreak = 4;
            s.Intangibles.Popularity = 55;
            s.Contract!.FightsRemaining = 0;
            return s;
        }

        var mercWorld = FoundedWorld(seed: 48);
        var merc = Setup(mercWorld);
        merc.Loyalty = 0;                       // no attachment — will jump for the bigger stage

        var loyalWorld = FoundedWorld(seed: 48);
        var loyal = Setup(loyalWorld);
        loyal.Loyalty = 100;                    // a lifer — won't be pried loose for a normal offer

        Assert.True(PoachingService.TryPoachFromPlayer(mercWorld, merc, mercWorld.CurrentDate),
            "a mercenary should take the bigger stage");
        Assert.False(PoachingService.TryPoachFromPlayer(loyalWorld, loyal, loyalWorld.CurrentDate),
            "a career-loyal fighter shouldn't be poached by a normal offer");
    }

    [Fact]
    public void NeglectDecay_SoursTheBenched_ButSparesTheRecovering()
    {
        // Two identically-idle stars: one healed months ago and left on the shelf
        // (genuine neglect), one still in age-scaled recovery (not the org's fault).
        static Fighter Idle(World w, DateOnly signed, DateOnly? readyOn, int loyalty)
        {
            var f = w.Fighters.Values.First(x => x.Contract is not null && x.Status == FighterStatus.Active);
            w.Fighters.Remove(f.Id);                 // isolate: only the survivor gets decayed
            var g = w.Fighters.Values.First(x => x.Contract is not null && x.Status == FighterStatus.Active && x.Id != f.Id);
            g.Contract = new Contract { Promotion = g.Contract!.Promotion, SignedOn = signed, FightsRemaining = 6 };
            g.ReadyToFightOn = readyOn;
            g.Loyalty = loyalty;
            return g;
        }

        // Day 0 is the save's start — pre-history bouts all predate it, so the
        // neglect clock runs from availability (readyOn/signing), as designed.
        var benchedWorld = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5, UseDesignedRoster = false });
        var day0 = benchedWorld.StartDate;
        var probe = day0.AddDays(200);               // 200 days on from the reference point

        var benched = Idle(benchedWorld, signed: day0, readyOn: day0, loyalty: 60);   // healed at day0, unused since
        WorldTick.DecayNeglectedLoyalty(benchedWorld, probe);
        Assert.True(benched.Loyalty < 60, "a star healed long ago and left benched should sour");

        var healingWorld = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5, UseDesignedRoster = false });
        var healing = Idle(healingWorld, signed: day0, readyOn: probe.AddDays(60), loyalty: 60);  // still recovering
        WorldTick.DecayNeglectedLoyalty(healingWorld, probe);
        Assert.Equal(60, healing.Loyalty);           // can't be "benched" while they physically can't fight
    }

    [Fact]
    public void TheTopOrg_KeepsItsStars_NobodyIsBigger()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 44, UseDesignedRoster = false });
        var afc = world.Promotions.Values.OrderByDescending(p => p.Prestige.Value).First();
        // Pretend the player IS the top org.
        world.PlayerPromotion = afc.Id;
        var star = world.RosterOf(afc.Id).OrderByDescending(FighterAbilityOf).First();
        star.Contract!.FightsRemaining = 0;

        Assert.False(PoachingService.TryPoachFromPlayer(world, star, world.CurrentDate),
            "nobody out-sizes the top org, so its stars are safe");
    }

    [Fact]
    public void FringeRoster_IsNotWorthPoaching()
    {
        var world = FoundedWorld(seed: 45);
        var player = world.Get(world.PlayerPromotion)!;
        player.Prestige = 20;
        var scrub = HireOne(world);
        foreach (var s in RaiseAbility(scrub)) s(30);   // clearly fringe
        scrub.Intangibles.Popularity = 10;
        scrub.Contract!.FightsRemaining = 0;

        Assert.False(PoachingService.TryPoachFromPlayer(world, scrub, world.CurrentDate));
    }

    [Fact]
    public void AGenuineDraw_IsPursued_EvenIfModestInTheCage()
    {
        var world = FoundedWorld(seed: 46);
        var player = world.Get(world.PlayerPromotion)!;
        player.Prestige = 20;
        var celeb = HireOne(world);
        foreach (var s in RaiseAbility(celeb)) s(42);   // mediocre in the cage...
        celeb.Intangibles.Popularity = 82;              // ...but a genuine draw who sells tickets
        celeb.Loyalty = 0;                              // a mercenary — isolates the draw test from loyalty
        celeb.Record.CurrentStreak = 1;                 // not in free-fall — nobody chases a skid
        celeb.Contract!.FightsRemaining = 0;

        // Orgs go after names, not just ability — a big enough draw gets chased up.
        Assert.True(PoachingService.TryPoachFromPlayer(world, celeb, world.CurrentDate),
            "a genuine draw should be pursued by a bigger stage even if modest in the cage");
    }

    [Fact]
    public void AModestLocalDraw_IsBeneathTheGiants()
    {
        var world = FoundedWorld(seed: 47);
        var player = world.Get(world.PlayerPromotion)!;
        player.Prestige = 20;   // Regional — only National+ orgs could poach
        var f = HireOne(world);
        foreach (var s in RaiseAbility(f)) s(41);   // not competitive at a bigger stage
        f.Intangibles.Popularity = 50;              // a modest regional draw, not a national name
        f.Record.Wins = 10; f.Record.Losses = 5;    // a public record — the AI reads him accurately, not by fog luck
        f.Contract!.FightsRemaining = 0;

        // Popularity is tier-scaled: a 50-pop name doesn't move the needle for the
        // giants (National draw floor 54, and up), so they leave them be.
        Assert.False(PoachingService.TryPoachFromPlayer(world, f, world.CurrentDate),
            "a modest local draw isn't worth a bigger org's roster spot");
    }

    // --- helpers -------------------------------------------------------------

    private static World FoundedWorld(ulong seed) =>
        new WorldBuilder().Build(new WorldBuilder.Options
        {
            Seed = seed,
            Founding = new WorldBuilder.FoundingSpec
            {
                Prestige = 18,
                Divisions = new[] { WeightClass.Lightweight, WeightClass.Welterweight },
            },
        });

    private static Fighter HireOne(World world)
    {
        var promo = world.Get(world.PlayerPromotion)!;
        var pick = SigningAdvisor.Suggest(world, promo)!;
        var exp = new FighterValuation().Evaluate(world, pick.Fighter);
        ContractLifecycle.Sign(world, pick.Fighter, new ContractOffer
        {
            Fighter = pick.Fighter.Id, Promotion = promo.Id,
            PursePerFight = exp.AskingPurse, WinBonus = exp.AskingWinBonus, Fights = exp.DesiredFights,
        }, world.CurrentDate);
        return pick.Fighter;
    }

    private static int FighterAbilityOf(Fighter f) => MmaManager.Core.Sim.Ability.FighterAbility.CurrentAbility(f);

    // Push a fighter's core skills to a value so CurrentAbility clears the poach bar.
    private static IEnumerable<Action<int>> RaiseAbility(Fighter f) => new Action<int>[]
    {
        v => f.Striking.PunchPower = v, v => f.Striking.PunchTechnique = v, v => f.Striking.StrikingDefense = v,
        v => f.Striking.Footwork = v, v => f.Striking.KickTechnique = v, v => f.Grappling.TakedownOffense = v,
        v => f.Grappling.TakedownDefense = v, v => f.Grappling.TopControl = v, v => f.Grappling.Scrambling = v,
        v => f.Grappling.SubmissionOffense = v, v => f.Grappling.SubmissionDefense = v,
        v => f.Physical.Cardio = v, v => f.Physical.Speed = v, v => f.Physical.Chin = v, v => f.Physical.Strength = v,
        v => f.Mental.FightIQ = v, v => f.Mental.Composure = v, v => f.Mental.Heart = v,
    };
}
