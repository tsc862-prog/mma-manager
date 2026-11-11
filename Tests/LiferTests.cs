using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Org;
using MmaManager.Persistence;

namespace MmaManager.Tests;

/// <summary>
/// Lifers: a fighter bound to his promotion holds — renewed first, cheaper to
/// keep, deaf to rivals — and breaks only for a reason the player can read.
/// </summary>
public class LiferTests
{
    // --- Who refuses, and why ------------------------------------------------------

    [Fact]
    public void ALifer_RefusesThePoach_ThatTakesTheSameFighterAtLoyalty30()
    {
        var mercWorld = FoundedWorld(48);
        var merc = Star(mercWorld, Disposition.CompanyMan, loyalty: 30);
        var lifeWorld = FoundedWorld(48);
        var lifer = Star(lifeWorld, Disposition.CompanyMan, loyalty: 85);

        Assert.False(Lifers.IsLifer(mercWorld, merc));
        Assert.True(Lifers.IsHolding(lifeWorld, lifer));

        Assert.True(PoachingService.TryPoachFromPlayer(mercWorld, merc, mercWorld.CurrentDate),
            "the same fighter at loyalty 30 takes the bigger stage");
        Assert.False(PoachingService.TryPoachFromPlayer(lifeWorld, lifer, lifeWorld.CurrentDate),
            "a holding lifer says no");
        Assert.Equal(lifeWorld.PlayerPromotion, lifer.Contract!.Promotion);
    }

    [Fact]
    public void ALifersRefusal_IsTheBond_NotThePrice()
    {
        // Same loyalty, so the same price to pry loose — the only difference is
        // that one of them is a company man and the face of the company (and so
        // over the lifer bar). Both are the face: the posters are the same.
        (World, Fighter) Setup(Disposition d)
        {
            var w = FoundedWorld(48);
            w.Get(w.PlayerPromotion)!.Prestige = 10;   // a city hall: the giants' leap sells itself
            // ...and the giants have built their gates, so their purse budgets can reach a loyal price.
            foreach (var big in w.Promotions.Values.Where(x => x.Id != w.PlayerPromotion && x.HomeCity is not null))
                big.PopularityByCity[big.HomeCity!.Value] = 100;
            // A cheap-to-price celebrity draw (modest in the cage, a big name, a
            // short record): the giants can afford even a loyal fighter's price.
            var f = HireOne(w);
            foreach (var a in RaiseAbility(f)) a(42);
            f.Intangibles.Popularity = 76;   // just past the giants' draw floor
            f.Record.CurrentStreak = 1;
            f.Disposition = d;
            f.Loyalty = 65;
            f.Contract!.FightsRemaining = 0;
            w.Get(w.PlayerPromotion)!.FaceOfCompany = f.Id;
            return (w, f);
        }
        var (hireWorld, hired) = Setup(Disposition.Warhorse);
        var (lifeWorld, lifer) = Setup(Disposition.CompanyMan);

        Assert.False(Lifers.IsLifer(hireWorld, hired));
        Assert.True(Lifers.IsHolding(lifeWorld, lifer));
        Assert.True(PoachingService.TryPoachFromPlayer(hireWorld, hired, hireWorld.CurrentDate),
            "at loyalty 65 the money reaches him");
        Assert.False(PoachingService.TryPoachFromPlayer(lifeWorld, lifer, lifeWorld.CurrentDate),
            "the same money doesn't move a lifer");
    }

    [Fact]
    public void TheRefusal_IsReported_OncePerContract()
    {
        var world = FoundedWorld(48);
        var lifer = Star(world, Disposition.CompanyMan, loyalty: 90);
        lifer.Designed = true;   // notable: the refusal makes the news

        Assert.False(PoachingService.TryPoachFromPlayer(world, lifer, world.CurrentDate));
        Assert.False(PoachingService.TryPoachFromPlayer(world, lifer, world.CurrentDate.AddMonths(1)));
        Assert.False(PoachingService.TryPoachFromPlayer(world, lifer, world.CurrentDate.AddMonths(2)));

        Assert.Single(world.News, n => n.Headline.StartsWith($"{lifer.FullName} turns down"));
        Assert.Single(world.Mail, m => m.Subject.StartsWith($"{lifer.FullName} turned down"));

        // A new deal is a new contract: the next approach is news again.
        Renew(world, lifer);
        lifer.Contract!.FightsRemaining = 0;
        Assert.False(PoachingService.TryPoachFromPlayer(world, lifer, world.CurrentDate.AddMonths(3)));
        Assert.Equal(2, world.News.Count(n => n.Headline.StartsWith($"{lifer.FullName} turns down")));
    }

    // --- His promotion fights to keep him ----------------------------------------------

    [Fact]
    public void ASolventOrg_RenewsTheLifer_ItUsedToLetWalk()
    {
        // The same IFA fighter, priced past IFA's purse budget: as an ordinary
        // fighter his capped renewal falls short and he walks; as a lifer the
        // org stretches, he takes less to stay, and he's renewed.
        static (World W, Fighter F, Promotion Org) Setup(Disposition d, int loyalty)
        {
            var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 11 });
            w.PlayerPromotion = default;
            var org = w.Promotions.Values.First(p => p.Abbreviation == "IFA");
            var f = w.RosterOf(org.Id)
                .Where(x => x.Status == FighterStatus.Active && !w.Titles.Values.Any(t => t.CurrentChampion == x.Id))
                .OrderBy(x => x.Id.Value).First();
            f.Disposition = d;
            f.Loyalty = loyalty;
            f.Record.Wins = 20; f.Record.Losses = 2; f.Record.WinsByKo = 10; f.Record.WinsByDecision = 10;
            f.Record.CurrentStreak = 2;
            f.Intangibles.Popularity = 66;
            f.Contract!.FightsRemaining = 0;
            return (w, f, org);
        }

        var (ow, ordinary, oOrg) = Setup(Disposition.Warhorse, 50);
        var exp = new FighterValuation().Evaluate(ow, ordinary);
        Assert.True(Negotiator.EffectiveAskingPurse(oOrg, ordinary, exp).Amount > Negotiator.PurseCap(ow, oOrg),
            "precondition: his ask is past IFA's usual budget");
        ContractLifecycle.ProcessExpiries(ow, ow.CurrentDate);
        Assert.NotEqual(oOrg.Id, ordinary.Contract?.Promotion);

        var (lw, lifer, lOrg) = Setup(Disposition.CompanyMan, 90);
        Assert.True(Lifers.IsHolding(lw, lifer));
        ContractLifecycle.ProcessExpiries(lw, lw.CurrentDate);
        Assert.Equal(lOrg.Id, lifer.Contract?.Promotion);
        Assert.True(lifer.Contract!.FightsRemaining > 0, "renewed in place");
    }

    [Fact]
    public void ALifer_AsksLessOfHisOwnPromotion_AndOnlyHisOwn()
    {
        var world = FoundedWorld(48);
        var lifer = Star(world, Disposition.CompanyMan, loyalty: 90);
        var mine = world.Get(world.PlayerPromotion)!;
        var other = world.Promotions.Values.First(p => p.Id != mine.Id && !p.Defunct);

        Assert.True(Lifers.HomeDiscount(world, lifer, mine.Id) < 1m);
        Assert.Equal(1m, Lifers.HomeDiscount(world, lifer, other.Id));

        var exp = new FighterValuation().Evaluate(world, lifer);
        Assert.True(Negotiator.EffectiveAskingPurse(world, mine, lifer, exp).Amount
                    < Negotiator.EffectiveAskingPurse(mine, lifer, exp).Amount);
        var talks = new NegotiationSession(world, lifer, mine, exp);
        Assert.True(talks.DemandPurse(true, false).Amount < exp.AskingPurse.Amount, "the room quotes the hometown number");
    }

    // --- It breaks for a reason ------------------------------------------------------------

    [Fact]
    public void Break_PromotionInTheRed()
    {
        var (world, lifer) = HoldingLifer();
        world.Get(world.PlayerPromotion)!.Balance = new Money(-50_000m);
        AssertBroken(world, lifer, Lifers.GrievanceKind.OrgInTrouble, "in the red");
    }

    [Fact]
    public void Break_PassedOverForTheTitleShot()
    {
        var (world, lifer) = HoldingLifer();
        var promo = world.Get(world.PlayerPromotion)!;
        var div = promo.Divisions.First(d => d.Matches(lifer));
        var title = world.Get(div.Title!.Value)!;
        var rivals = world.RosterOf(promo.Id).Where(x => x.Id != lifer.Id && div.Matches(x)).Take(2).ToList();
        while (rivals.Count < 2)
        {
            var extra = HireOne(world);
            if (div.Matches(extra)) rivals.Add(extra);
        }
        title.CurrentChampion = rivals[0].Id;
        div.Rankings.Remove(lifer.Id);
        div.Rankings.Insert(0, lifer.Id);   // the #1 contender
        for (int i = 1; i <= 2; i++)
        {
            var ev = new Event
            {
                Id = world.NewEventId(), Promotion = promo.Id, Name = $"Test {i}",
                Date = world.CurrentDate.AddDays(-60 * i), Status = EventStatus.Completed,
            };
            ev.Card.Add(new Fight
            {
                Id = world.NewFightId(), RedCorner = rivals[0].Id, BlueCorner = rivals[1].Id,
                WeightClass = div.WeightClass, IsTitleFight = true, Title = title.Id,
            });
            world.Events[ev.Id] = ev;
        }
        AssertBroken(world, lifer, Lifers.GrievanceKind.PassedOver, "passed over twice");

        // Repair: book him the shot.
        var shot = new Event
        {
            Id = world.NewEventId(), Promotion = promo.Id, Name = "The shot",
            Date = world.CurrentDate.AddDays(30), Status = EventStatus.Scheduled,
        };
        shot.Card.Add(new Fight
        {
            Id = world.NewFightId(), RedCorner = rivals[0].Id, BlueCorner = lifer.Id,
            WeightClass = div.WeightClass, IsTitleFight = true, Title = title.Id,
        });
        world.Events[shot.Id] = shot;
        Assert.True(Lifers.IsHolding(world, lifer), "a booked title shot fixes it");
    }

    [Fact]
    public void Break_BenchedForHalfAYear()
    {
        var (world, lifer) = HoldingLifer();
        Backdate(lifer, days: 200);
        AssertBroken(world, lifer, Lifers.GrievanceKind.Benched, "on the shelf");
    }

    [Fact]
    public void Break_PaidFarBelowWhatTheMarketHasSeen()
    {
        var (world, lifer) = HoldingLifer();
        Backdate(lifer, days: 300);
        lifer.Contract!.PursePerFight = new Money(500m);
        // Booked on a card, so he isn't also "on the shelf" — this is about the money.
        BookOnNextCard(world, lifer);
        AssertBroken(world, lifer, Lifers.GrievanceKind.Underpaid, "barely a third");
    }

    [Fact]
    public void Break_ALosingSkid()
    {
        var (world, lifer) = HoldingLifer();
        lifer.Record.CurrentStreak = -3;
        AssertBroken(world, lifer, Lifers.GrievanceKind.Skid, "3 straight losses");
    }

    [Fact]
    public void Break_ThePostersComeDown()
    {
        var (world, lifer) = HoldingLifer();
        var promo = world.Get(world.PlayerPromotion)!;
        Franchise.SetFace(world, promo, lifer, world.CurrentDate);
        var newFace = world.RosterOf(promo.Id).First(x => x.Id != lifer.Id);

        Franchise.SetFace(world, promo, newFace, world.CurrentDate);

        Assert.False(Lifers.IsLifer(world, lifer), "taking the posters off him breaks the bond");
        Assert.Contains(world.Mail, m => m.Subject == $"We've lost {lifer.FullName}'s loyalty" && m.Body.Contains("posters"));
    }

    [Fact]
    public void Break_TheCampStipendStops_AndARetentionBonusRepairsIt()
    {
        var (world, lifer) = HoldingLifer();
        var promo = world.Get(world.PlayerPromotion)!;
        lifer.CampStipend = true;

        Camps.StopStipend(world, lifer);

        Assert.False(Lifers.IsLifer(world, lifer));
        Assert.Contains(world.Mail, m => m.Subject == $"We've lost {lifer.FullName}'s loyalty" && m.Body.Contains("corner"));

        promo.Balance = new Money(50_000_000m);
        Assert.True(RetentionService.TryPayBonus(world, promo, lifer, out _));
        Assert.True(Lifers.IsHolding(world, lifer), "a retention bonus buys the bond back");
    }

    [Fact]
    public void AStandingGrievance_WearsTheBondDown_AndARepairInTimeRestoresIt()
    {
        var (world, lifer) = HoldingLifer();
        var promo = world.Get(world.PlayerPromotion)!;
        lifer.Loyalty = 85;   // company man: score 95
        promo.Balance = new Money(-10_000m);

        int before = lifer.Loyalty;
        Lifers.RunMonthly(world, world.CurrentDate);
        Assert.True(lifer.Loyalty < before, "a standing grievance costs loyalty every month");
        Assert.False(Lifers.IsHolding(world, lifer));
        Assert.Contains(world.Mail, m => m.Subject == $"{lifer.FullName} is unhappy" && m.Body.Contains("in the red"));

        promo.Balance = new Money(400_000m);   // back in the black
        Assert.True(Lifers.IsHolding(world, lifer), "fixed in time, he holds again");
        int after = lifer.Loyalty;
        Lifers.RunMonthly(world, world.CurrentDate.AddMonths(1));
        Assert.Equal(after, lifer.Loyalty);
    }

    [Fact]
    public void ALongGrievance_BreaksTheBond_AndThePlayerHearsWhy()
    {
        var (world, lifer) = HoldingLifer();
        var promo = world.Get(world.PlayerPromotion)!;
        lifer.Designed = true;
        promo.Balance = new Money(-10_000m);

        for (int m = 0; m < 40 && Lifers.IsLifer(world, lifer); m++)
            Lifers.RunMonthly(world, world.CurrentDate.AddMonths(m));

        Assert.False(Lifers.IsLifer(world, lifer));
        Assert.Contains(world.News, n => n.Headline == $"{lifer.FullName}'s bond with {promo.Abbreviation} is broken"
                                         && n.Body.Contains("in the red"));
        Assert.Contains(world.Mail, m => m.Subject == $"We've lost {lifer.FullName}'s loyalty");
    }

    [Fact]
    public void WhenALiferLeaves_TheHeadlineSaysWhy()
    {
        var (world, lifer) = HoldingLifer();
        var promo = world.Get(world.PlayerPromotion)!;
        lifer.Record.CurrentStreak = -3;   // the reason

        ContractLifecycle.Release(world, lifer);

        Assert.Contains(world.News, n => n.Headline.StartsWith($"{lifer.FullName} leaves {promo.Name}")
                                         && n.Headline.Contains("straight losses") && n.Body.Contains("released him"));
        Assert.Contains(world.Mail, m => m.Subject == $"{lifer.FullName} has left us");
    }

    // --- Mercenaries stay mercenaries --------------------------------------------------

    [Fact]
    public void AMercenary_NeverCountsAsALifer_HoweverLoyal()
    {
        var world = FoundedWorld(48);
        var merc = Star(world, Disposition.Mercenary, loyalty: 100);
        Assert.False(Lifers.IsLifer(world, merc));
        Assert.Equal(1m, Lifers.HomeDiscount(world, merc, world.PlayerPromotion));
    }

    [Fact]
    public void AWorldWithoutLifers_RunsExactlyAsItDidBefore()
    {
        // Every fighter a mercenary: no lifers can exist, so the whole layer must
        // be inert. The fingerprint was taken on main before lifers existed
        // (same scenario, same seed) — any drift means the layer leaks into the
        // ordinary treadmill.
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 21 });
        world.PlayerPromotion = default;
        foreach (var f in world.Fighters.Values) f.Disposition = Disposition.Mercenary;
        new WorldTick().AdvanceWeeks(world, 26);

        Assert.Equal(MercenaryWorldFingerprint, Fingerprint(world));
    }

    // Taken on main @ d313337 with the scenario above (see the PR for how).
    private const string MercenaryWorldFingerprint = "D967706736319349";

    // --- Determinism ---------------------------------------------------------------------

    [Fact]
    public void SameSeed_SameRefusalsAndBreaks()
    {
        static World Run()
        {
            var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 7 });
            w.PlayerPromotion = default;
            new WorldTick().AdvanceWeeks(w, 60);
            return w;
        }
        var a = Run();
        var b = Run();
        Assert.Equal(LiferStories(a), LiferStories(b));
        Assert.Equal(Fingerprint(a), Fingerprint(b));
    }

    [Fact]
    public void ASaveAndLoad_InTheMiddle_ChangesNoRefusalOrBreak()
    {
        var straight = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 7 });
        straight.PlayerPromotion = default;
        var tick = new WorldTick();
        tick.AdvanceWeeks(straight, 30);

        string path = Path.Combine(Path.GetTempPath(), $"mma_lifers_{Guid.NewGuid():N}.db");
        World reloaded;
        try
        {
            SaveGame.Save(straight, path);
            reloaded = SaveGame.Load(path);
        }
        finally { File.Delete(path); }

        tick.AdvanceWeeks(straight, 30);
        new WorldTick().AdvanceWeeks(reloaded, 30);

        Assert.Equal(LiferStories(straight), LiferStories(reloaded));
        Assert.Equal(Fingerprint(straight), Fingerprint(reloaded));
    }

    // --- helpers -------------------------------------------------------------------------

    private static List<string> LiferStories(World w) => w.News
        .Where(n => n.Headline.Contains(" turns down ") || n.Headline.Contains("'s bond with ")
                    || (n.Category == NewsCategory.Milestone && n.Headline.Contains(" leaves ")))
        .Select(n => $"{n.Date:yyyy-MM-dd} {n.Headline}")
        .ToList();

    // Who is where, on what terms, how loyal — plus every org's bank.
    private static string Fingerprint(World w)
    {
        unchecked
        {
            long h = 17;
            void Mix(long v) => h = h * 31 + v;
            foreach (var f in w.Fighters.Values.OrderBy(f => f.Id.Value))
            {
                Mix(f.Id.Value); Mix(f.Loyalty); Mix((long)f.Status);
                if (f.Contract is { } c)
                {
                    Mix(c.Promotion.Value); Mix(c.FightsRemaining); Mix((long)c.PursePerFight.Amount); Mix(c.SignedOn.DayNumber);
                }
            }
            foreach (var p in w.Promotions.Values.OrderBy(p => p.Id.Value)) { Mix((long)p.Balance.Amount); Mix(p.Defunct ? 1 : 0); }
            return h.ToString("X");
        }
    }

    private static World FoundedWorld(ulong seed) =>
        new WorldBuilder().Build(new WorldBuilder.Options
        {
            Seed = seed,
            Founding = new WorldBuilder.FoundingSpec
            {
                Prestige = 20,
                StartingBalance = new Money(2_000_000m),
                Divisions = new[] { WeightClass.Lightweight, WeightClass.Welterweight },
            },
        });

    // A proven, in-form, popular fighter on the player's roster whose deal has just run out.
    private static Fighter Star(World world, Disposition d, int loyalty)
    {
        var f = HireOne(world);
        foreach (var a in RaiseAbility(f)) a(72);
        f.Record.Wins = 14; f.Record.Losses = 2; f.Record.WinsByKo = 7; f.Record.WinsByDecision = 7;
        f.Record.CurrentStreak = 4;
        f.Intangibles.Popularity = 55;
        f.Disposition = d;
        f.Loyalty = loyalty;
        f.Contract!.FightsRemaining = 0;
        return f;
    }

    // A company man at loyalty 85 under contract with fights left — holding, nothing wrong.
    private static (World, Fighter) HoldingLifer()
    {
        var world = FoundedWorld(48);
        var f = Star(world, Disposition.CompanyMan, loyalty: 85);
        f.Contract!.FightsRemaining = 3;
        HireOne(world);   // a teammate: an opponent on a card, a new face for the posters
        Assert.True(Lifers.IsHolding(world, f), "precondition: a holding lifer");
        return (world, f);
    }

    private static void AssertBroken(World world, Fighter f, Lifers.GrievanceKind kind, string reasonText)
    {
        Assert.False(Lifers.IsHolding(world, f), "the grievance opens the door");
        var g = Lifers.GrievanceOf(world, f, world.CurrentDate);
        Assert.NotNull(g);
        Assert.Equal(kind, g!.Kind);
        Assert.Contains(reasonText, g.Reason);
        Assert.Equal(1m, Lifers.HomeDiscount(world, f, world.PlayerPromotion));
    }

    // The deal was signed long ago: replace it with an identical one dated back.
    private static void Backdate(Fighter f, int days)
    {
        var c = f.Contract!;
        f.Contract = new Contract
        {
            Promotion = c.Promotion, SignedOn = c.SignedOn.AddDays(-days), PursePerFight = c.PursePerFight,
            WinBonus = c.WinBonus, FightsRemaining = c.FightsRemaining, IsExclusive = c.IsExclusive,
        };
        f.ReadyToFightOn = null;
    }

    private static void BookOnNextCard(World world, Fighter f)
    {
        var ev = new Event
        {
            Id = world.NewEventId(), Promotion = world.PlayerPromotion, Name = "Next",
            Date = world.CurrentDate.AddDays(21), Status = EventStatus.Scheduled,
        };
        var opponent = world.RosterOf(world.PlayerPromotion).First(x => x.Id != f.Id);
        ev.Card.Add(new Fight { Id = world.NewFightId(), RedCorner = f.Id, BlueCorner = opponent.Id, WeightClass = f.WeightClass });
        world.Events[ev.Id] = ev;
    }

    private static void Renew(World world, Fighter f)
    {
        var exp = new FighterValuation().Evaluate(world, f);
        ContractLifecycle.Sign(world, f, new ContractOffer
        {
            Fighter = f.Id, Promotion = world.PlayerPromotion,
            PursePerFight = exp.AskingPurse, WinBonus = exp.AskingWinBonus, Fights = exp.DesiredFights,
        }, world.CurrentDate);
    }

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
