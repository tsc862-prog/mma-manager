using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Flavor;

namespace MmaManager.Tests;

/// <summary>
/// The poster line: <see cref="EventHype"/> reads the main event's live story —
/// feuds, rematches, bonds, belts, debuts — and picks the one that sells the card.
/// </summary>
public class EventHypeTests
{
    private static (World world, Event ev, Fighter red, Fighter blue) Stage(ulong seed = 9)
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed, UseDesignedRoster = false });
        var promo = world.Promotions.Values.First(p => !p.Defunct);
        // A pair with NO shared past — pre-history pairs divisionmates, and a prior
        // meeting would trip the rematch/trilogy lines instead of the story under test.
        var group = world.RosterOf(promo.Id)
            .Where(f => f.Status == FighterStatus.Active && f.Sex == Sex.Male)
            .GroupBy(f => f.WeightClass).First(g => g.Count() >= 2).ToList();
        List<Fighter>? picked = null;
        for (int i = 0; picked is null && i < group.Count - 1; i++)
            for (int j = i + 1; j < group.Count; j++)
                if (EventHype.Meetings(world, group[i].Id, group[j].Id) == 0)
                {
                    picked = new List<Fighter> { group[i], group[j] };
                    break;
                }
        Assert.NotNull(picked);
        var pair = picked!;
        var ev = new Event { Id = world.NewEventId(), Promotion = promo.Id, Name = "Test Night", Date = world.CurrentDate };
        ev.Card.Add(new Fight { Id = world.NewFightId(), RedCorner = pair[0].Id, BlueCorner = pair[1].Id, WeightClass = pair[0].WeightClass });
        world.Events[ev.Id] = ev;
        // Different camps + no bond, so only the story under test applies.
        pair[0].Camp = null;
        pair[1].Camp = null;
        return (world, ev, pair[0], pair[1]);
    }

    [Fact]
    public void AFirstShow_IsTheDebut_ButOnlyForTheLittleGuys()
    {
        var (world, ev, _, _) = Stage();

        // An established org's first card of the save is NOT a debut…
        Assert.NotEqual("WHERE IT ALL BEGINS", EventHype.Tagline(world, ev));

        // …and every established hall now has a past, so even the little guys
        // aren't debutants. A genuinely NEW startup's first card still is.
        var startup = new Promotion
        {
            Id = world.NewPromotionId(),
            Name = "Garage Fight Series",
            Abbreviation = "GFS",
            BaseRegion = Region.NorthAmerica,
            Prestige = 8,
        };
        world.Promotions[startup.Id] = startup;
        var debut = new Event { Id = world.NewEventId(), Promotion = startup.Id, Name = "Debut", Date = world.CurrentDate };
        world.Events[debut.Id] = debut;
        Assert.Equal("WHERE IT ALL BEGINS", EventHype.Tagline(world, debut));
    }

    private static void MarkHistory(World world, Event ev)
    {
        // A prior completed show so the debut line stops applying.
        var done = new Event { Id = world.NewEventId(), Promotion = ev.Promotion, Name = "Past", Date = world.CurrentDate.AddMonths(-2), Status = EventStatus.Completed };
        world.Events[done.Id] = done;
    }

    [Fact]
    public void AFeud_SellsAsBadBlood_AndARematchAsTheRematch()
    {
        var (world, ev, red, blue) = Stage();
        MarkHistory(world, ev);

        world.Rivalries.Add(new Rivalry { A = red.Id, B = blue.Id, Intensity = 85, Since = world.CurrentDate });
        Assert.Equal("BAD BLOOD", EventHype.Tagline(world, ev));

        // They've met once before → the rematch (feud still live, heat below war).
        world.Rivalries[^1].Intensity = 50;
        var past = new Event { Id = world.NewEventId(), Promotion = ev.Promotion, Name = "First Meeting", Date = world.CurrentDate.AddMonths(-6), Status = EventStatus.Completed };
        past.Card.Add(new Fight
        {
            Id = world.NewFightId(), RedCorner = red.Id, BlueCorner = blue.Id, WeightClass = red.WeightClass,
            Outcome = new FightOutcome { Winner = red.Id, Method = FightMethod.UnanimousDecision, EndRound = 3, EndTimeSeconds = 300 },
        });
        world.Events[past.Id] = past;
        Assert.Equal("THE REMATCH", EventHype.Tagline(world, ev));
    }

    [Fact]
    public void FriendsAndFamily_MakeTheHardestPosters()
    {
        var (world, ev, red, blue) = Stage();
        MarkHistory(world, ev);

        world.Relationships.Add(new Relationship { A = red.Id, B = blue.Id, Kind = RelationshipKind.Friend, Strength = 60, Since = world.CurrentDate });
        Assert.Equal("BUSINESS, NOT FRIENDSHIP", EventHype.Tagline(world, ev));

        world.Relationships[^1] = new Relationship { A = red.Id, B = blue.Id, Kind = RelationshipKind.Family, Strength = 80, Since = world.CurrentDate };
        Assert.Equal("BLOOD AGAINST BLOOD", EventHype.Tagline(world, ev));
    }

    [Fact]
    public void ATitleFight_SellsTheGold_AndTeammatesColliding_SellTheDrama()
    {
        var (world, ev, red, blue) = Stage();
        MarkHistory(world, ev);
        var title = world.Titles.Values.First(t => t.Promotion == ev.Promotion);
        ev.Card[^1].IsTitleFight = true;
        ev.Card[^1].Title = title.Id;
        // The line names the ACTUAL belt, not a generic "for the gold".
        Assert.Equal($"FOR THE {EventHype.BeltName(world, ev.Card[^1])} TITLE", EventHype.Tagline(world, ev));
        Assert.Contains("WEIGHT", EventHype.Tagline(world, ev));   // a real weight class is in there

        var camp = world.Camps.Values.First();
        red.Camp = camp.Id;
        blue.Camp = camp.Id;
        Assert.Equal("TEAMMATES COLLIDE", EventHype.Tagline(world, ev));
    }

    [Fact]
    public void AnEmptyCard_OnAnEstablishedOrg_HasNoLine()
    {
        var (world, ev, _, _) = Stage();
        MarkHistory(world, ev);
        ev.Card.Clear();
        Assert.Null(EventHype.Tagline(world, ev));
    }
}
