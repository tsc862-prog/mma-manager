using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Tests;

/// <summary>
/// The relationship graph's warm side: friendships form between rostermates and
/// keep friends off each other's undercards, family dynasties arrive with newgens
/// carrying a famous surname, a title fight between friends damages (and can
/// break) the bond into a rivalry, and bonds pull free agents toward rosters
/// where their people are. Round-trips through the save file.
/// </summary>
public class BondTests
{
    [Fact]
    public void Friendships_Form_OnSharedMatsAndRosters()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9, UseDesignedRoster = false });
        var rng = new SimRandom(7);

        for (int month = 0; month < 60; month++)
            Bonds.RunMonthly(world, world.CurrentDate.AddDays(month * 30), rng);

        var friends = world.Relationships.Where(r => r.Kind == RelationshipKind.Friend).ToList();
        Assert.True(friends.Count > 0, "five years of shared gyms should make some friends");
        Assert.All(friends, r =>
        {
            var a = world.Fighters[r.A];
            var b = world.Fighters[r.B];
            bool sameRoster = a.Contract?.Promotion == b.Contract?.Promotion;
            bool sameCamp = a.Camp is not null && a.Camp == b.Camp;
            Assert.True(sameRoster || sameCamp, "friendships form on shared mats or shared rosters");
        });
        // The camp channel is the primary engine — most bonds come from the room.
        Assert.Contains(friends, r =>
            world.Fighters[r.A].Camp is { } room && world.Fighters[r.B].Camp == room);
    }

    [Fact]
    public void Matchmaker_KeepsFriends_OffTheUndercard()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 7, UseDesignedRoster = false });
        var promo = world.Promotions.Values.First(p => p.Id != world.PlayerPromotion && !p.Defunct);

        // Make every same-division pair in the org fast friends, then build a card:
        // none of those bouts should be booked (title bouts exempt, but a fresh
        // world's champs are excluded from the undercard pool anyway).
        var roster = world.RosterOf(promo.Id).Where(f => f.Status == FighterStatus.Active).ToList();
        foreach (var a in roster)
            foreach (var b in roster.Where(b => b.Id.Value > a.Id.Value && b.WeightClass == a.WeightClass && b.Sex == a.Sex))
                world.Relationships.Add(new Relationship
                {
                    A = a.Id, B = b.Id, Kind = RelationshipKind.Friend, Strength = 60, Since = world.CurrentDate,
                });

        var card = new Matchmaker().BuildCard(world, promo.Id);

        foreach (var m in card.Where(m => !m.IsTitle))
            Assert.False(Bonds.WontFight(world, m.Red, m.Blue),
                "friends shouldn't be booked against each other for an undercard purse");
    }

    [Fact]
    public void ATitleFightBetweenFriends_DamagesTheBond_AndCanBreakIt()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 9, UseDesignedRoster = false });
        var promo = world.Promotions.Values.First(p => !p.Defunct);
        var pair = world.RosterOf(promo.Id).Where(f => f.Status == FighterStatus.Active).Take(2).ToList();
        world.Relationships.Add(new Relationship
        {
            A = pair[0].Id, B = pair[1].Id, Kind = RelationshipKind.Friend, Strength = 50, Since = world.CurrentDate,
        });

        var ev = new Event
        {
            Id = world.NewEventId(), Promotion = promo.Id, Name = "Friends Collide",
            Date = world.CurrentDate, Status = EventStatus.Completed,
        };
        ev.Card.Add(new Fight
        {
            Id = world.NewFightId(), RedCorner = pair[0].Id, BlueCorner = pair[1].Id,
            WeightClass = pair[0].WeightClass, IsTitleFight = true,
            Outcome = new FightOutcome { Winner = pair[0].Id, Method = FightMethod.UnanimousDecision, EndRound = 3, EndTimeSeconds = 300 },
        });

        Bonds.AfterEvent(world, ev, world.CurrentDate);   // 50 -> 15: the friendship breaks
        Assert.Null(Bonds.Between(world, pair[0].Id, pair[1].Id));
        Assert.NotNull(Storylines.RivalryBetween(world, pair[0].Id, pair[1].Id));
        Assert.Contains(world.News, n => n.Headline.Contains("friendship is over"));
    }

    [Fact]
    public void Dynasties_ArriveWithTheNewgens()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 11 });
        var rng = new SimRandom(3);
        var svc = NewgenService.ForWorld(world);

        // The designed world seeds its own authored blood ties (cousins off the
        // same circuit, who don't share a surname); this is about the DYNASTIES
        // the intake makes, so only the new bonds are in scope.
        var before = world.Relationships.ToHashSet();

        // Enough intake that the 7% dynasty roll fires several times.
        svc.GenerateIntake(world, 120, rng);

        var families = world.Relationships
            .Where(r => r.Kind == RelationshipKind.Family && !before.Contains(r)).ToList();
        Assert.True(families.Count > 0, "a big intake should carry at least one famous name");
        foreach (var fam in families)
        {
            var elder = world.Fighters[fam.A];
            var kid = world.Fighters[fam.B];
            Assert.Equal(elder.LastName, kid.LastName);           // the name is the story
            Assert.Equal(elder.Nationality, kid.Nationality);     // same homeland
            Assert.True(kid.Intangibles.Popularity.Value > 0);    // arrives with a spotlight slice
        }
        Assert.Contains(world.News, n => n.Headline.Contains("famous name turns pro"));
    }

    [Fact]
    public void Bonds_RoundTrip_ThroughTheSave()
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 5, UseDesignedRoster = false });
        var pair = world.Fighters.Values.Take(2).ToList();
        world.Relationships.Add(new Relationship
        {
            A = pair[0].Id, B = pair[1].Id, Kind = RelationshipKind.Family,
            Strength = 85, Since = world.CurrentDate, Note = "younger brother of Test",
        });

        var path = Path.Combine(Path.GetTempPath(), $"bonds-{Guid.NewGuid():N}.db");
        try
        {
            MmaManager.Persistence.SaveGame.Save(world, path);
            var loaded = MmaManager.Persistence.SaveGame.Load(path);

            var bond = Assert.Single(loaded.Relationships);
            Assert.Equal(pair[0].Id, bond.A);
            Assert.Equal(RelationshipKind.Family, bond.Kind);
            Assert.Equal(85, bond.Strength);
            Assert.Equal("younger brother of Test", bond.Note);
        }
        finally { File.Delete(path); }
    }
}
