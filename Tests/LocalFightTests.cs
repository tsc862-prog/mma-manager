using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Org;

namespace MmaManager.Tests;

/// <summary>
/// Bookable local talent: a small org pads its cards with easy hometown
/// opponents to build prospects — the local retires after the bout unless they
/// pull the upset or steal the show, which mints a real prospect the host org
/// moves to sign (a letter to the player; AI hosts offer a deal on the spot).
/// </summary>
public class LocalFightTests
{
    private static World FoundedWorld(ulong seed) =>
        new WorldBuilder().Build(new WorldBuilder.Options
        {
            Seed = seed,
            Founding = new WorldBuilder.FoundingSpec
            {
                Prestige = 15,
                Divisions = new[] { WeightClass.Lightweight, WeightClass.Welterweight },
            },
        });

    private static Fighter Hire(World world, Sex? sex = null)
    {
        var promo = world.Get(world.PlayerPromotion)!;
        var pick = MmaManager.Core.Sim.Contracts.SigningAdvisor.Suggest(world, promo,
            f => sex is null || f.Sex == sex ? MmaManager.Core.Sim.Scouting.ScoutingService.PerceivedAbility(world, promo, f) : -1000)!;
        var exp = new MmaManager.Core.Sim.Contracts.FighterValuation().Evaluate(world, pick.Fighter);
        MmaManager.Core.Sim.Contracts.ContractLifecycle.Sign(world, pick.Fighter, new ContractOffer
        {
            Fighter = pick.Fighter.Id,
            Promotion = promo.Id,
            PursePerFight = exp.AskingPurse,
            WinBonus = exp.AskingWinBonus,
            Fights = exp.DesiredFights,
        }, world.CurrentDate);
        return pick.Fighter;
    }

    [Fact]
    public void AWomansLocalFight_IsAgainstAWoman()
    {
        var world = FoundedWorld(seed: 21);
        var promo = world.Get(world.PlayerPromotion)!;
        var woman = world.FreeAgents().First(f => f.Sex == Sex.Female && f.Status == FighterStatus.Active
                                                  && promo.Divisions.Any(d => d.Matches(f)));
        var exp = new MmaManager.Core.Sim.Contracts.FighterValuation().Evaluate(world, woman);
        MmaManager.Core.Sim.Contracts.ContractLifecycle.Sign(world, woman, new ContractOffer
        {
            Fighter = woman.Id, Promotion = promo.Id,
            PursePerFight = exp.AskingPurse, WinBonus = exp.AskingWinBonus, Fights = exp.DesiredFights,
        }, world.CurrentDate);
        var booking = new PlayerBooking();
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(14));

        Assert.True(booking.AddLocalBout(world, ev, woman.Id, PrimaryStyle.Striker, 3, out var err), err);
        var local = world.Get(Assert.Single(ev.Card).BlueCorner)!;
        Assert.Equal(Sex.Female, local.Sex);
        Assert.Equal(woman.WeightClass, local.WeightClass);
    }

    [Fact]
    public void ASmallOrg_CanBookALocalFight_ButANationalOrgCannot()
    {
        var world = FoundedWorld(seed: 21);
        var promo = world.Get(world.PlayerPromotion)!;
        var mine = Hire(world);
        var booking = new PlayerBooking();
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(14));

        Assert.True(booking.AddLocalBout(world, ev, mine.Id, PrimaryStyle.Wrestler, 3, out _));

        var fight = Assert.Single(ev.Card);
        var local = world.Get(fight.BlueCorner)!;
        Assert.True(local.LocalTalent);
        Assert.Equal("Local Wrestler", local.FullName);
        Assert.Equal(mine.WeightClass, local.WeightClass);
        Assert.Null(local.Contract);
        // A local, literally: from the host city, in the host country.
        var host = MmaManager.Core.Sim.Geography.WorldMap.CityOf(world, ev)!.Value;
        Assert.Equal(host, local.HomeCity);
        Assert.Equal(world.Get(host)!.Country, local.Country);
        // Cheap: a flat appearance fee in the projection.
        Assert.True(Financials.Estimate(world, ev).FighterPayroll.Amount > 0);

        // Outgrow the scene: no more local fights.
        promo.Prestige = 40;   // National
        Assert.False(booking.AddLocalBout(world, ev, mine.Id, PrimaryStyle.Striker, 3, out var err));
        Assert.Contains("beneath", err);
    }

    [Fact]
    public void LocalsAreEasyWork_AndFadeBackIntoTheScene()
    {
        var world = FoundedWorld(seed: 22);
        // A man, as this test was written against: since the small halls went
        // local the advisor's top pick on this seed became a 19-year-old 0-0
        // strawweight, whose record against female locals is its own question.
        var mine = Hire(world, Sex.Male);
        var booking = new PlayerBooking();
        var events = new EventService();

        int wins = 0;
        const int bouts = 6;
        for (int i = 0; i < bouts; i++)
        {
            var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(7));
            Assert.True(booking.AddLocalBout(world, ev, mine.Id, PrimaryStyle.Brawler, 3, out var err), err);
            var local = world.Get(ev.Card[0].BlueCorner)!;
            events.RunEvent(world, ev);   // pads the undercard to a realistic length; our showcase stays the main event

            // The featured bout is the main event (last) — the filler prelims sit ahead of it.
            var featured = ev.Card[^1];
            if (featured.Outcome?.Winner == mine.Id) wins++;
            // Win or lose, an undiscovered local is gone from circulation.
            Assert.True(local.LocalTalent
                ? local.Status == FighterStatus.Retired
                : local.Status == FighterStatus.Active);
            Assert.DoesNotContain(world.FreeAgents(), f => f.LocalTalent);
        }

        Assert.True(wins >= bouts - 1, $"a signed pro should dominate locals: {wins}/{bouts}");
    }

    [Fact]
    public void TheUpset_MintsARealProspect_WhoWritesHomeToSign()
    {
        var world = FoundedWorld(seed: 23);
        var promo = world.Get(world.PlayerPromotion)!;
        var mine = Hire(world);
        var booking = new PlayerBooking();
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(7));
        Assert.True(booking.AddLocalBout(world, ev, mine.Id, PrimaryStyle.Grappler, 3, out _));

        // Load the dice: this local is a hidden killer, so the upset happens.
        var local = world.Get(ev.Card[0].BlueCorner)!;
        foreach (var s in new Action<int>[]
        {
            v => local.Grappling.SubmissionOffense = v, v => local.Grappling.TakedownOffense = v,
            v => local.Grappling.TopControl = v, v => local.Physical.Strength = v,
            v => local.Physical.Cardio = v, v => local.Physical.Chin = v,
            v => local.Striking.StrikingDefense = v, v => local.Grappling.TakedownDefense = v,
            v => local.Mental.FightIQ = v, v => local.Mental.Heart = v,
            v => local.Grappling.SubmissionDefense = v, v => local.Grappling.BottomGame = v,
        }) s(95);
        // …and gut our fighter's OFFENSE too — with a submission game left intact,
        // even a nerfed pro can catch the local before the upset develops.
        foreach (var s in new Action<int>[]
        {
            v => mine.Physical.Chin = v, v => mine.Grappling.TakedownDefense = v,
            v => mine.Grappling.SubmissionDefense = v, v => mine.Physical.Cardio = v,
            v => mine.Striking.PunchPower = v, v => mine.Striking.KickPower = v,
            v => mine.Striking.PunchTechnique = v, v => mine.Physical.Strength = v,
            v => mine.Mental.Heart = v, v => mine.Grappling.Scrambling = v,
            v => mine.Grappling.SubmissionOffense = v, v => mine.Grappling.TakedownOffense = v,
            v => mine.Grappling.TopControl = v,
        }) s(5);

        new EventService().RunEvent(world, ev);   // pads the undercard; our showcase stays the main event
        Assert.Equal(EventStatus.Completed, ev.Status);
        Assert.Equal(local.Id, ev.Card[^1].Outcome?.Winner);

        // Discovered: real name, real ceiling, the story on the profile, a letter in the inbox.
        Assert.False(local.LocalTalent);
        Assert.NotEqual("Local", local.FirstName);
        Assert.Equal(FighterStatus.Active, local.Status);
        Assert.True(local.PotentialAbility.Value >= 58, $"ceiling {local.PotentialAbility.Value}");
        Assert.Contains(local.Accolades, a => a.Title.Contains("Discovered on the local circuit"));
        Assert.Contains(world.Mail, m => m.Subject.Contains($"wants to sign with {promo.Abbreviation}"));
        Assert.Contains(world.FreeAgents(), f => f.Id == local.Id);
    }

    // Fabricate an all-local bout on the given org's card with a scripted result:
    // either a show-stealing war (KO, knockdowns, big output — 4+ stars) or dull
    // filler (a low-output decision), then land it through the ResultsProcessor.
    private static (Fighter Winner, Fighter Loser) RunLocalPair(World world, Promotion promo, bool exciting)
    {
        var div = promo.Divisions.First(d => d.Sex == Sex.Male);
        var ev = new Event
        {
            Id = world.NewEventId(),
            Promotion = promo.Id,
            Name = "Club Show",
            Date = world.CurrentDate,
        };
        world.Events[ev.Id] = ev;

        var (red, blue) = LocalScene.CreateLocalPair(world, promo, ev, div.WeightClass);
        var fight = new Fight
        {
            Id = world.NewFightId(),
            RedCorner = red.Id,
            BlueCorner = blue.Id,
            WeightClass = div.WeightClass,
        };
        fight.Outcome = new FightOutcome
        {
            Winner = red.Id,
            Loser = blue.Id,
            Method = exciting ? FightMethod.Knockout : FightMethod.UnanimousDecision,
            EndRound = 3,
            Stats =
            {
                [red.Id] = new FightStatline { SignificantStrikesLanded = exciting ? 40 : 8, KnockdownsScored = exciting ? 2 : 0 },
                [blue.Id] = new FightStatline { SignificantStrikesLanded = exciting ? 35 : 6 },
            },
        };
        ev.Card.Add(fight);
        ResultsProcessor.Apply(world, ev, fight);
        return (red, blue);
    }

    // Excitement only opens the door — a promoter has to be in the room (a notice
    // roll). Run show-stealing bouts until one is noticed; unnoticed winners must
    // still fade back to the scene like any other filler local.
    private static (Fighter Winner, Fighter Loser)? RunUntilDiscovered(World world, Promotion promo, int attempts = 120)
    {
        for (int i = 0; i < attempts; i++)
        {
            var (winner, loser) = RunLocalPair(world, promo, exciting: true);
            if (!winner.LocalTalent) return (winner, loser);
            Assert.Equal(FighterStatus.Retired, winner.Status);
        }
        return null;
    }

    [Fact]
    public void AShowStealingLocalWin_MintsAProspect_AndTheAiHostSignsThem()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 25 });
        world.PlayerPromotion = default;
        var promo = world.Promotions.Values.First(p =>
            !p.Defunct && Solvency.Distress(p) == 0 && p.Divisions.Any(d => d.Sex == Sex.Male));
        // World-built rosters sit at (or above) their tier target — open a slot so the host can act.
        var div = promo.Divisions.First(d => d.Sex == Sex.Male);
        int target = OrgSizing.RosterTargetPerDivision(OrgSizing.SizeOf(promo));
        var inDiv = world.RosterOf(promo.Id).Where(f => div.Matches(f)).ToList();
        foreach (var cut in inDiv.Take(Math.Max(0, inDiv.Count - target + 1)))
            MmaManager.Core.Sim.Contracts.ContractLifecycle.Release(world, cut, dentReputation: false);

        var pair = RunUntilDiscovered(world, promo);
        Assert.NotNull(pair);
        var (winner, loser) = pair.Value;

        // Discovered: real name, real ceiling, the story on the profile.
        Assert.False(winner.LocalTalent);
        Assert.NotEqual("Local", winner.FirstName);
        Assert.Equal(FighterStatus.Active, winner.Status);
        Assert.True(winner.PotentialAbility.Value >= 52, $"ceiling {winner.PotentialAbility.Value}");
        Assert.Contains(winner.Accolades, a => a.Title.Contains("stole the show"));
        // The org that gave them the shot signed them on the spot.
        Assert.NotNull(winner.Contract);
        Assert.Equal(promo.Id, winner.Contract!.Promotion);
        // The beaten local fades back into the scene as ever.
        Assert.Equal(FighterStatus.Retired, loser.Status);
    }

    [Fact]
    public void ADullAllLocalBout_StaysFiller_BothFadeBack()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 26 });
        world.PlayerPromotion = default;
        var promo = world.Promotions.Values.First(p =>
            !p.Defunct && p.Divisions.Any(d => d.Sex == Sex.Male));

        var (winner, loser) = RunLocalPair(world, promo, exciting: false);

        Assert.True(winner.LocalTalent);
        Assert.Equal(FighterStatus.Retired, winner.Status);
        Assert.Equal(FighterStatus.Retired, loser.Status);
        Assert.Null(winner.Contract);
    }

    [Fact]
    public void AShowStealerOnThePlayersCard_WritesHomeToSign_NotAutoSigned()
    {
        var world = FoundedWorld(seed: 27);
        var promo = world.Get(world.PlayerPromotion)!;

        var pair = RunUntilDiscovered(world, promo);
        Assert.NotNull(pair);
        var winner = pair.Value.Winner;

        Assert.False(winner.LocalTalent);
        Assert.Contains(winner.Accolades, a => a.Title.Contains("stole the show"));
        // The player decides: a letter in the inbox, the prospect on the open market.
        Assert.Null(winner.Contract);
        Assert.Contains(world.Mail, m => m.Subject.Contains($"wants to sign with {promo.Abbreviation}"));
        Assert.Contains(world.FreeAgents(), f => f.Id == winner.Id);
    }

    [Fact]
    public void CancellingTheCard_ReleasesTheUnfoughtLocal()
    {
        var world = FoundedWorld(seed: 24);
        var mine = Hire(world);
        var booking = new PlayerBooking();
        var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(14));
        Assert.True(booking.AddLocalBout(world, ev, mine.Id, PrimaryStyle.Kickboxer, 3, out _));
        var localId = ev.Card[0].BlueCorner;

        booking.CancelEvent(world, ev);
        Assert.Null(world.Get(localId));
    }
}
