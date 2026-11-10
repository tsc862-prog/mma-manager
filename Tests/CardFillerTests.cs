using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;

namespace MmaManager.Tests;

/// <summary>
/// Every card runs at a realistic length. A promotion fleshes out a short card
/// from its own available roster first; a small org that's run dry of contracted
/// talent papers the rest with local-vs-local filler (pure spectacle, no roster
/// impact, nobody "discovered"). The featured bout always stays the main event.
/// </summary>
public class CardFillerTests
{
    // A tiny founded City org — empty roster, so we control exactly who's signed.
    private static World CityOrg(ulong seed) =>
        new WorldBuilder().Build(new WorldBuilder.Options
        {
            Seed = seed,
            Founding = new WorldBuilder.FoundingSpec
            {
                Prestige = 15,   // City tier
                Divisions = new[] { WeightClass.Lightweight, WeightClass.Welterweight },
            },
        });

    private static Fighter Hire(World world)
    {
        var promo = world.Get(world.PlayerPromotion)!;
        var pick = MmaManager.Core.Sim.Contracts.SigningAdvisor.Suggest(world, promo)!;
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
    public void ACityOrg_OutOfRoster_FillsTheUndercardWithLocalPairs()
    {
        var world = CityOrg(seed: 31);
        var a = Hire(world);
        var b = Hire(world);
        var svc = new EventService();

        // Just the one signed-vs-signed main event — nothing else in the tank.
        var ev = svc.CreateEvent(world, world.PlayerPromotion, "City Show", world.CurrentDate,
            new[] { new Matchup(a.Id, b.Id, a.WeightClass, 5, false, null) });
        Assert.Single(ev.Card);

        svc.RunEvent(world, ev);

        // Padded up to a realistic minimum...
        Assert.True(ev.Card.Count >= CardFiller.MinCardBouts,
            $"card should reach the minimum, was {ev.Card.Count}");

        // ...the featured bout is still the main event (last), untouched...
        var main = ev.Card[^1];
        Assert.Equal(a.Id, main.RedCorner);
        Assert.Equal(b.Id, main.BlueCorner);
        Assert.False(world.Get(main.RedCorner)!.LocalTalent);
        Assert.False(world.Get(main.BlueCorner)!.LocalTalent);

        // ...and every prelim was booked as local-vs-local filler (a show-stealing
        // winner may have been DISCOVERED by fight's end — no longer flagged local).
        foreach (var prelim in ev.Card.Take(ev.Card.Count - 1))
            foreach (var id in new[] { prelim.RedCorner, prelim.BlueCorner })
            {
                var l = world.Get(id)!;
                Assert.True(l.LocalTalent || l.Accolades.Any(ac => ac.Title.Contains("Discovered on the local circuit")),
                    $"{l.FullName} should be a filler local or a fresh discovery");
            }
    }

    [Fact]
    public void LocalVsLocalFiller_FadesAway_UnlessSomeoneStealsTheShow()
    {
        var world = CityOrg(seed: 32);
        var a = Hire(world);
        var b = Hire(world);
        var svc = new EventService();
        var ev = svc.CreateEvent(world, world.PlayerPromotion, "City Show", world.CurrentDate,
            new[] { new Matchup(a.Id, b.Id, a.WeightClass, 5, false, null) });

        svc.RunEvent(world, ev);

        // A fighter is never both: discovery strips the local flag.
        Assert.DoesNotContain(world.Fighters.Values,
            f => f.LocalTalent && f.Accolades.Any(ac => ac.Title.Contains("Discovered")));
        // Every filler local fades back to the scene — unless they won a
        // show-stealing (4+ star) fight, which mints a real prospect instead.
        var fillerLocals = ev.Card.Take(ev.Card.Count - 1)
            .SelectMany(fi => new[] { fi.RedCorner, fi.BlueCorner })
            .Select(id => world.Get(id)!);
        Assert.All(fillerLocals, f =>
        {
            if (f.Accolades.Any(ac => ac.Title.Contains("stole the show")))
            {
                Assert.False(f.LocalTalent);
                Assert.Equal(FighterStatus.Active, f.Status);
            }
            else
            {
                Assert.True(f.LocalTalent);
                Assert.Equal(FighterStatus.Retired, f.Status);
            }
        });
        // No undiscovered local lingers in the free-agent pool.
        Assert.DoesNotContain(world.FreeAgents(), f => f.LocalTalent);
    }

    [Fact]
    public void ADeepOrg_FillsFromItsOwnRoster_NeverLocals()
    {
        // A full league is National+ with a deep roster — it never needs locals.
        var world = new LeagueBuilder().Build(new LeagueBuilder.Options { Seed = 33, RosterSize = 120 });
        var promo = world.Get(world.PlayerPromotion)!;
        var svc = new EventService();

        // A deliberately short two-bout card from two divisions.
        var card = new List<Matchup>();
        foreach (var div in promo.Divisions.Take(2))
        {
            var two = world.RosterOf(promo.Id)
                .Where(f => f.WeightClass == div.WeightClass && f.Status == FighterStatus.Active)
                .Take(2).ToList();
            if (two.Count == 2) card.Add(new Matchup(two[0].Id, two[1].Id, div.WeightClass, 3, false, null));
        }
        var ev = svc.CreateEvent(world, promo.Id, "Deep Card", world.CurrentDate, card);

        svc.RunEvent(world, ev);

        Assert.True(ev.Card.Count >= CardFiller.MinCardBouts);
        foreach (var fight in ev.Card)
        {
            Assert.False(world.Get(fight.RedCorner)!.LocalTalent, "a deep org should never fill with locals");
            Assert.False(world.Get(fight.BlueCorner)!.LocalTalent, "a deep org should never fill with locals");
        }
    }

    [Fact]
    public void AFullCard_IsLeftAlone()
    {
        var world = new LeagueBuilder().Build(new LeagueBuilder.Options { Seed = 34, RosterSize = 120 });
        var promoId = world.PlayerPromotion;
        var svc = new EventService();

        var card = new Matchmaker().BuildCard(world, promoId, targetFights: 9);
        var ev = svc.CreateEvent(world, promoId, "Full Card", world.CurrentDate, card);
        int before = ev.Card.Count;
        Assert.True(before >= CardFiller.MinCardBouts);
        var mainEventFighters = (ev.Card[^1].RedCorner, ev.Card[^1].BlueCorner);

        svc.RunEvent(world, ev);

        Assert.Equal(before, ev.Card.Count);   // nothing added
        Assert.Equal(mainEventFighters, (ev.Card[^1].RedCorner, ev.Card[^1].BlueCorner));
    }
}
