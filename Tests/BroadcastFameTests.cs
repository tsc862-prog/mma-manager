using System.Reflection;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Geography;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Tests;

/// <summary>
/// Remote city fame (Geography.BroadcastFame): a televised main card and a belt make a man heard of where the
/// promotion is known, scaled by the screen, the slot and the belt's prestige, and never past the remote ceiling,
/// so home and the cities he fought in stay hottest.
/// </summary>
public class BroadcastFameTests
{
    private static int _ids = 900_000;

    private static (World W, Promotion P) World()
    {
        var w = new WorldBuilder().Build(new WorldBuilder.Options { Seed = 4 });
        var p = w.Promotions.Values.First(x => x.Abbreviation == "AFC");
        p.MediaDeals.Clear();
        p.Stream = null;
        return (w, p);
    }

    private static void OnScreen(Promotion p, MediaKind kind, string partner) =>
        p.MediaDeals.Add(new MediaDeal { Kind = kind, Partner = partner });

    private static Fighter Fresh(World w, int pop, int skip = 0)
    {
        var f = w.Fighters.Values.Where(x => x.Status == FighterStatus.Active && !x.LocalTalent && x.HomeCity != null)
            .OrderBy(x => x.Id.Value).Skip(skip).First();
        f.CityReputation.Clear();
        f.Intangibles.Popularity = pop;
        return f;
    }

    private static Fight Bout(Fighter red, Fighter blue) => new()
    {
        Id = new FightId(++_ids), RedCorner = red.Id, BlueCorner = blue.Id,
        Outcome = new FightOutcome { Winner = red.Id, Loser = blue.Id, Method = FightMethod.UnanimousDecision },
    };

    /// <summary>A card of <paramref name="bouts"/> (main event last) with a main card of <paramref name="mainCard"/>.</summary>
    private static Event Card(World w, Promotion p, int mainCard, params Fight[] bouts)
    {
        var ev = new Event { Id = new EventId(++_ids), Promotion = p.Id, Name = "Test card", Date = w.CurrentDate, MainCardFights = mainCard };
        ev.Card.AddRange(bouts);
        return ev;
    }

    private static int Sum(Fighter f) => f.CityReputation.Values.Sum(r => r.Value);

    [Fact]
    public void BiggerScreen_ReachesFurther_AndHigher()
    {
        var (w, p) = World();
        var footprints = new Dictionary<MediaKind, (int Cities, int Abroad, double Cap)>();
        foreach (var (kind, partner) in new[] { (MediaKind.SpecialtyCable, "Combat One"), (MediaKind.CableSports, "Vertex Sports"),
                                                 (MediaKind.PrimeBroadcast, "NBS"), (MediaKind.MajorStream, "Arena+") })
        {
            p.MediaDeals.Clear();
            OnScreen(p, kind, partner);
            var ev = Card(w, p, 1);
            var s = BroadcastFame.ScreenFor(p, ev)!.Value;
            Assert.Equal(kind, s.Kind);
            var fp = BroadcastFame.Footprint(w, p, s.HomeFloor, s.AbroadFloor);
            footprints[kind] = (fp.Count, fp.Count(c => w.Get(c.City)!.Region != p.BaseRegion), s.Cap);
        }
        // A niche channel stays in the home region; a worldwide streamer goes wherever the brand is known.
        Assert.Equal(0, footprints[MediaKind.SpecialtyCable].Abroad);
        Assert.True(footprints[MediaKind.MajorStream].Abroad > footprints[MediaKind.CableSports].Abroad);
        Assert.True(footprints[MediaKind.MajorStream].Cities > footprints[MediaKind.SpecialtyCable].Cities);
        // The bigger the audience, the higher the most a stranger city can learn.
        Assert.True(footprints[MediaKind.SpecialtyCable].Cap < footprints[MediaKind.CableSports].Cap);
        Assert.True(footprints[MediaKind.CableSports].Cap < footprints[MediaKind.PrimeBroadcast].Cap);
        Assert.All(footprints.Values, v => Assert.True(v.Cap <= BroadcastFame.RemoteCeiling));
    }

    [Fact]
    public void NoScreen_NoTravel_ExceptForBelts()
    {
        var (w, p) = World();
        var red = Fresh(w, 90); var blue = Fresh(w, 90, 1);
        BroadcastFame.AfterCard(w, p, Card(w, p, 1, Bout(red, blue)));
        Assert.Empty(red.CityReputation);
        Assert.Empty(blue.CityReputation);
    }

    [Fact]
    public void WorldwideStreamer_SpreadsAStarThinly_AbroadAndTheNicheChannelDoesNot()
    {
        var (w, p) = World();
        OnScreen(p, MediaKind.MajorStream, "Arena+");
        var star = Fresh(w, 91); var foe = Fresh(w, 60, 1);
        for (int i = 0; i < 30; i++) BroadcastFame.AfterCard(w, p, Card(w, p, 1, Bout(star, foe)));
        var abroad = star.CityReputation.Where(kv => w.Get(kv.Key)!.Region != p.BaseRegion).ToList();
        Assert.NotEmpty(abroad);
        // "heard of / a few fans" abroad, never "known"
        Assert.All(star.CityReputation.Values, v => Assert.InRange(v.Value, BroadcastFame.MinWrite, BroadcastFame.RemoteCeiling));
        Assert.Contains(star.CityReputation.Values, v => v.Value >= 10);

        var (w2, p2) = World();
        OnScreen(p2, MediaKind.SpecialtyCable, "Combat One");
        var star2 = Fresh(w2, 91); var foe2 = Fresh(w2, 60, 1);
        for (int i = 0; i < 30; i++) BroadcastFame.AfterCard(w2, p2, Card(w2, p2, 1, Bout(star2, foe2)));
        Assert.NotEmpty(star2.CityReputation);
        Assert.All(star2.CityReputation.Keys, c => Assert.Equal(p2.BaseRegion, w2.Get(c)!.Region));
        Assert.True(star2.CityReputation.Values.Max(v => v.Value) < star.CityReputation.Values.Max(v => v.Value));
    }

    [Fact]
    public void FamousMen_TravelFurther_ThanUnknownsOnTheSameScreen()
    {
        var (w, p) = World();
        OnScreen(p, MediaKind.PrimeBroadcast, "NBS");
        var star = Fresh(w, 90); var nobody = Fresh(w, 15, 1);
        for (int i = 0; i < 20; i++) BroadcastFame.AfterCard(w, p, Card(w, p, 1, Bout(star, nobody)));
        Assert.True(Sum(star) > 3 * Sum(nobody));
    }

    [Fact]
    public void CardPosition_MainEventMost_MainCardSome_PrelimsNothing()
    {
        var (w, p) = World();
        OnScreen(p, MediaKind.PrimeBroadcast, "NBS");
        var f = Enumerable.Range(0, 8).Select(i => Fresh(w, 80, i)).ToArray();
        // running order: prelim, prelim, main-card opener, co-main, main event; main card = 3
        var prelim = Bout(f[0], f[1]); var opener = Bout(f[2], f[3]); var coMain = Bout(f[4], f[5]); var main = Bout(f[6], f[7]);
        var ev = Card(w, p, 3, prelim, opener, coMain, main);
        Assert.Equal(0, BroadcastFame.PositionShare(ev, 0));
        Assert.Equal(1.0, BroadcastFame.PositionShare(ev, 3));
        for (int i = 0; i < 40; i++) BroadcastFame.AfterCard(w, p, ev);

        Assert.Empty(f[0].CityReputation);
        Assert.Empty(f[1].CityReputation);
        Assert.True(Sum(f[6]) > Sum(f[4]));
        Assert.True(Sum(f[4]) > Sum(f[2]));
        Assert.True(Sum(f[2]) > 0);
    }

    [Fact]
    public void RemoteFame_HasACeiling_AndNeverLowersWhatACityKnows()
    {
        var (w, p) = World();
        OnScreen(p, MediaKind.PrimeBroadcast, "United Broadcasting");   // the flagship: the biggest cap there is
        var star = Fresh(w, 100); var foe = Fresh(w, 100, 1);
        var foughtIn = p.PopularityByCity.OrderByDescending(kv => kv.Value.Value).ThenBy(kv => kv.Key.Value).First().Key;
        star.CityReputation[foughtIn] = 60;
        for (int i = 0; i < 200; i++) BroadcastFame.AfterCard(w, p, Card(w, p, 1, Bout(star, foe)));

        Assert.Equal(60, Reputation.FighterInCity(star, foughtIn));
        Assert.All(star.CityReputation.Where(kv => kv.Key != foughtIn), kv => Assert.True(kv.Value.Value <= BroadcastFame.RemoteCeiling));
    }

    [Fact]
    public void Home_AndCitiesFoughtIn_StayHottest()
    {
        var (w, p) = World();
        OnScreen(p, MediaKind.MajorStream, "Streamflix");
        OnScreen(p, MediaKind.PrimeBroadcast, "United Broadcasting");
        var star = Fresh(w, 92); var foe = Fresh(w, 70, 1);
        var home = star.HomeCity!.Value;
        // a modest in-person career: a handful of fights at home and one city abroad
        var abroad = w.Cities.Values.Where(c => c.Region != w.Get(home)!.Region).OrderBy(c => c.Id.Value).First().Id;
        for (int i = 0; i < 6; i++) Reputation.BumpFighter(w, star, home, 6);
        for (int i = 0; i < 2; i++) Reputation.BumpFighter(w, star, abroad, 4);
        // ...then years on network television with belts on the line
        var belt = w.Titles.Values.Where(t => t.Promotion == p.Id).OrderBy(t => t.Id.Value).First();
        belt.Prestige = 90;
        for (int i = 0; i < 60; i++)
        {
            var bout = Bout(star, foe);
            bout.IsTitleFight = true; bout.Title = belt.Id;
            belt.CurrentChampion = star.Id;
            BroadcastFame.AfterCard(w, p, Card(w, p, 1, bout));
        }
        int homeFame = Reputation.FighterInCity(star, home);
        int abroadFame = Reputation.FighterInCity(star, abroad);
        var stranger = star.CityReputation.Where(kv => kv.Key != home && kv.Key != abroad && w.Get(kv.Key)!.Region != w.Get(home)!.Region)
            .Select(kv => kv.Value.Value).DefaultIfEmpty(0).Max();
        Assert.True(homeFame > stranger + 5, $"home {homeFame} vs a city he never fought in {stranger}");
        Assert.True(abroadFame >= stranger, $"fought-in {abroadFame} vs {stranger}");
        Assert.True(stranger >= 10, "a champion on worldwide TV should have a few fans abroad");
    }

    [Fact]
    public void BeltLift_ScalesWithPrestige_AndReachesBeyondTheHostCity_WithoutTelevision()
    {
        var (w, p) = World();   // no screen at all: only the belt travels
        var belt = w.Titles.Values.Where(t => t.Promotion == p.Id).OrderBy(t => t.Id.Value).First();
        int Lifted(int prestige, int skip)
        {
            var champ = Fresh(w, 50, skip); var foe = Fresh(w, 50, skip + 1);
            belt.Prestige = prestige;
            belt.CurrentChampion = champ.Id;
            var bout = Bout(champ, foe);
            bout.IsTitleFight = true; bout.Title = belt.Id;
            BroadcastFame.AfterCard(w, p, Card(w, p, 1, bout));
            Assert.Empty(foe.CityReputation);   // the loser's name does not travel on a belt
            Assert.All(champ.CityReputation.Values, v => Assert.True(v.Value <= BroadcastFame.RemoteCeiling));
            return champ.CityReputation.Count(kv => kv.Value.Value > 0) * 1000 + Sum(champ);
        }
        int storied = Lifted(85, 0), trinket = Lifted(25, 2);
        Assert.True(storied > trinket, $"prestige 85 lifted {storied}, prestige 25 lifted {trinket}");
        Assert.True(storied / 1000 > 5, "a storied belt is news in many cities");
    }

    [Fact]
    public void NewChampion_LiftsMore_ThanADefense()
    {
        var (w, p) = World();
        var belt = w.Titles.Values.Where(t => t.Promotion == p.Id).OrderBy(t => t.Id.Value).First();
        belt.Prestige = 70;
        var newKing = Fresh(w, 50, 0); var keeper = Fresh(w, 50, 2);
        int Run(Fighter champ, int foeSkip, bool crowned)
        {
            var foe = Fresh(w, 50, foeSkip);
            belt.CurrentChampion = champ.Id;
            belt.Lineage.Add(new TitleReign { Champion = champ.Id, WonOn = crowned ? w.CurrentDate : w.CurrentDate.AddDays(-400) });
            var bout = Bout(champ, foe);
            bout.IsTitleFight = true; bout.Title = belt.Id;
            BroadcastFame.AfterCard(w, p, Card(w, p, 1, bout));
            belt.Lineage.RemoveAt(belt.Lineage.Count - 1);
            return Sum(champ);
        }
        Assert.True(Run(newKing, 1, true) > Run(keeper, 3, false));
    }

    [Fact]
    public void LocalTalent_IsNotBroadcast()
    {
        var (w, p) = World();
        OnScreen(p, MediaKind.PrimeBroadcast, "NBS");
        var pro = Fresh(w, 80); var local = Fresh(w, 30, 1);
        local.LocalTalent = true;
        BroadcastFame.AfterCard(w, p, Card(w, p, 1, Bout(pro, local)));
        Assert.Empty(local.CityReputation);
        Assert.NotEmpty(pro.CityReputation);
    }

    [Fact]
    public void BroadcastFame_DrawsNothing_AndIsDeterministic()
    {
        // No randomness of any kind: no method takes or makes a SimRandom, so no shared stream can gain a draw.
        var t = typeof(BroadcastFame);
        foreach (var m in t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            Assert.DoesNotContain(m.GetParameters(), pa => pa.ParameterType == typeof(SimRandom));
        Assert.DoesNotContain(t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic), fi => fi.FieldType == typeof(SimRandom));

        Dictionary<CityId, Rating> Once()
        {
            var (w, p) = World();
            OnScreen(p, MediaKind.MajorStream, "Arena+");
            var a = Fresh(w, 85); var b = Fresh(w, 55, 1);
            for (int i = 0; i < 10; i++) BroadcastFame.AfterCard(w, p, Card(w, p, 1, Bout(a, b)));
            return a.CityReputation;
        }
        var x = Once(); var y = Once();
        Assert.Equal(x.OrderBy(kv => kv.Key.Value), y.OrderBy(kv => kv.Key.Value));
    }

    [Fact]
    public void RunningACard_AppliesBroadcastFame_ThroughSettle()
    {
        var (w, p) = World();
        OnScreen(p, MediaKind.MajorStream, "Arena+");
        var star = Fresh(w, 90); var foe = Fresh(w, 60, 1);
        var ev = Card(w, p, 1, Bout(star, foe));
        ev.Region = p.BaseRegion;
        new EventService().Settle(w, ev);
        Assert.Contains(star.CityReputation.Keys, c => w.Get(c)!.Region != p.BaseRegion);
    }

    [Fact]
    public void Origins_DayOne_HasNoScreens_SoOnlyBeltsTravel()
    {
        // The 1993 world gets the same rule: its houses start with no television (the era opens screens later), so on
        // day one nothing is broadcast; a belt still makes its champion known where the small house is.
        var w = new WorldBuilder().Build(WorldBuilder.Options.OriginsObserver(3));
        Assert.Equal(WorldMode.Origins, w.Mode);
        foreach (var p in w.Promotions.Values.Where(p => !p.Defunct).OrderBy(p => p.Id.Value))
            Assert.Null(BroadcastFame.ScreenFor(p, new Event { Id = new EventId(++_ids), Promotion = p.Id, Name = "x", Date = w.CurrentDate }));
    }
}
