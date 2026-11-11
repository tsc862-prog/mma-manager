using System.Collections;
using System.Reflection;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Rng;
using MmaManager.Persistence;

namespace MmaManager.Tests;

/// <summary>
/// The invariant behind save/load determinism: every entity collection on <see cref="World"/>
/// enumerates in ascending id order, a pure function of the set of ids present, never of the
/// history of adds and removes or of whether the world was loaded. The sim iterates these
/// collections while it draws from shared RNG streams, so their order feeds rolls.
/// </summary>
public class EntityOrderTests
{
    // ---- the collection type itself ----------------------------------------

    [Fact]
    public void EntityMap_RandomAddsAndRemoves_AlwaysEnumerateInIdOrder_AndLookUpCorrectly()
    {
        var rng = new SimRandom(0x1D_0DE5UL);
        var map = new EntityMap<FighterId, string>();
        var reference = new SortedDictionary<int, string>();
        int next = 0;
        for (int step = 0; step < 5_000; step++)
        {
            int op = rng.Range(0, 9);
            if (op <= 4 || reference.Count == 0)
            {
                // Mostly fresh ids (the real allocator), now and then an old id coming back (a reload's Add order).
                int id = op == 0 && next > 3 ? rng.Range(1, next) : ++next;
                if (reference.ContainsKey(id)) continue;
                map[new FighterId(id)] = $"f{id}";
                reference[id] = $"f{id}";
            }
            else if (op <= 7)
            {
                int victim = reference.Keys.ElementAt(rng.Range(0, reference.Count - 1));
                Assert.True(map.Remove(new FighterId(victim)));
                reference.Remove(victim);
            }
            else
            {
                int id = reference.Keys.ElementAt(rng.Range(0, reference.Count - 1));
                map[new FighterId(id)] = $"g{id}";   // replace in place
                reference[id] = $"g{id}";
            }

            Assert.Equal(reference.Keys, map.Keys.Select(k => k.Value));
            Assert.Equal(reference.Values, map.Values);
        }
        foreach (var (id, v) in reference)
        {
            Assert.Equal(v, map.GetValueOrDefault(new FighterId(id)));
            Assert.True(map.TryGetValue(new FighterId(id), out var got) && got == v);
        }
        Assert.Null(map.GetValueOrDefault(new FighterId(next + 1)));
        Assert.Throws<ArgumentException>(() => map.Add(new FighterId(reference.Keys.First()), "dup"));
    }

    [Fact]
    public void EntityMap_ThroughTheNonGenericDictionaryInterface_StaysInIdOrder()
    {
        var map = new EntityMap<EventId, string>();
        IDictionary d = map;
        d.Add(new EventId(3), "c");
        d[new EventId(1)] = "a";
        d.Add(new EventId(2), "b");
        d.Remove(new EventId(1));
        d[new EventId(4)] = "d";
        Assert.Equal(new[] { 2, 3, 4 }, map.Keys.Select(k => k.Value));
        Assert.Equal(new object[] { new EventId(2), new EventId(3), new EventId(4) }, d.Keys.Cast<object>());
    }

    // ---- the guard -----------------------------------------------------------

    /// <summary>
    /// No id-keyed plain <see cref="Dictionary{TKey,TValue}"/> may sit on the model unless it is on the
    /// allow-list, each entry with the reason its order cannot drift. A new world collection must be an
    /// <see cref="EntityMap{TKey,TValue}"/>; a new per-entity map that the sim REMOVES from must be one too.
    /// </summary>
    [Fact]
    public void Model_HasNoIdKeyedPlainDictionary_OutsideTheAllowList()
    {
        var allowed = new Dictionary<string, string>
        {
            // Two entries (red, blue), written once when the fight is simulated; never removed from.
            ["FightOutcome.Stats"] = "write-once per fight",
            // Accumulate per city; the sim only adds and updates (no Remove anywhere in Core.Sim).
            ["Fighter.CityReputation"] = "add/update only",
            ["Promotion.PopularityByCity"] = "add/update only",
        };

        var found = new List<string>();
        foreach (var t in typeof(World).Assembly.GetTypes())
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var pt = p.PropertyType;
                if (!pt.IsGenericType || pt.GetGenericTypeDefinition() != typeof(Dictionary<,>)) continue;
                if (!typeof(IEntityId).IsAssignableFrom(pt.GetGenericArguments()[0])) continue;
                found.Add($"{t.Name}.{p.Name}");
            }

        var unexpected = found.Where(n => !allowed.ContainsKey(n)).ToList();
        Assert.True(unexpected.Count == 0,
            "Id-keyed plain Dictionary on the model (enumeration order would depend on removal history; use EntityMap): "
            + string.Join(", ", unexpected));

        // And every world-level entity collection is an EntityMap.
        foreach (var p in typeof(World).GetProperties())
            if (p.PropertyType.IsGenericType && typeof(IDictionary).IsAssignableFrom(p.PropertyType)
                && typeof(IEntityId).IsAssignableFrom(p.PropertyType.GetGenericArguments()[0]))
                Assert.Equal(typeof(EntityMap<,>), p.PropertyType.GetGenericTypeDefinition());
    }

    // ---- through the real sim paths -----------------------------------------

    /// <summary>Every id-keyed collection on the world, by name, as ids in enumeration order.</summary>
    private static Dictionary<string, List<int>> KeyOrders(World world)
    {
        var orders = new Dictionary<string, List<int>>();
        foreach (var p in typeof(World).GetProperties())
            if (p.GetValue(world) is IDictionary d && p.PropertyType.IsGenericType
                && typeof(IEntityId).IsAssignableFrom(p.PropertyType.GetGenericArguments()[0]))
                orders[p.Name] = d.Keys.Cast<IEntityId>().Select(k => k.Value).ToList();
        return orders;
    }

    private static void AssertIdOrdered(World world, string when)
    {
        var orders = KeyOrders(world);
        Assert.Equal(8, orders.Count);   // Fighters, Promotions, Titles, Events, Countries, Cities, Venues, Camps
        foreach (var (name, ids) in orders)
            for (int i = 1; i < ids.Count; i++)
                Assert.True(ids[i - 1] < ids[i], $"{name} enumerates out of id order {when}: {ids[i - 1]} before {ids[i]}");
    }

    private static World PlayerStartWorld(ulong seed, int hires)
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options
        {
            Seed = seed,
            Founding = new WorldBuilder.FoundingSpec
            {
                Prestige = 15,
                Divisions = new[] { WeightClass.Lightweight, WeightClass.Welterweight },
            },
        });
        new WorldTick().AdvanceWeeks(world, 4);
        var promo = world.Get(world.PlayerPromotion)!;
        for (int i = 0; i < hires; i++)
        {
            var pick = SigningAdvisor.Suggest(world, promo);
            if (pick is null) break;
            var exp = new FighterValuation().Evaluate(world, pick.Fighter);
            ContractLifecycle.Sign(world, pick.Fighter, new ContractOffer
            {
                Fighter = pick.Fighter.Id, Promotion = promo.Id,
                PursePerFight = exp.AskingPurse, WinBonus = exp.AskingWinBonus, Fights = exp.DesiredFights,
            }, world.CurrentDate);
        }
        return world;
    }

    private static World RoundTrip(World w)
    {
        string path = Path.Combine(Path.GetTempPath(), $"mma_idorder_{Guid.NewGuid():N}.db");
        try { SaveGame.Save(w, path); return SaveGame.Load(path); }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// Random player traffic (book events at any date, add local bouts that mint fighters, drop bouts
    /// and cancel events at any position, i.e. remove non-newest events and fighters) interleaved with
    /// world weeks (which add fighters, events, titles, promotions). After every step every collection
    /// is in id order, and a save/load keeps exactly that order.
    /// </summary>
    [Theory]
    [InlineData(31UL)]
    [InlineData(32UL)]
    public void PlayerTraffic_KeepsEveryCollectionInIdOrder_AndALoadAgrees(ulong seed)
    {
        var world = PlayerStartWorld(seed, hires: 6);
        var booking = new PlayerBooking();
        var rng = new SimRandom(SimRandom.Mix(seed, 0x0D0E_12UL));
        var styles = new[] { PrimaryStyle.Striker, PrimaryStyle.Wrestler, PrimaryStyle.Grappler, PrimaryStyle.Brawler };
        int removedEvents = 0, removedFighters = 0;

        AssertIdOrdered(world, "after the build");
        for (int step = 0; step < 70; step++)
        {
            var mine = world.Events.Values
                .Where(e => e.Promotion == world.PlayerPromotion && e.Status == EventStatus.Scheduled)
                .ToList();
            int op = rng.Range(0, 9);
            int fightersBefore = world.Fighters.Count, eventsBefore = world.Events.Count;
            if (op <= 3 || mine.Count == 0)
            {
                var ev = booking.CreateEvent(world, world.CurrentDate.AddDays(rng.Range(10, 120)));
                var roster = world.RosterOf(world.PlayerPromotion).OrderBy(f => f.Id.Value).ToList();
                for (int b = rng.Range(0, 2); b > 0 && roster.Count > 0; b--)
                    booking.AddLocalBout(world, ev, roster[rng.Range(0, roster.Count - 1)].Id,
                        styles[rng.Range(0, styles.Length - 1)], 3, out _);
            }
            else if (op <= 5)
            {
                booking.CancelEvent(world, mine[rng.Range(0, mine.Count - 1)]);   // any position, not just the newest
            }
            else if (op <= 7)
            {
                var ev = mine[rng.Range(0, mine.Count - 1)];
                if (ev.Card.Count > 0) booking.RemoveBout(world, ev, ev.Card[rng.Range(0, ev.Card.Count - 1)].Id);
            }
            else
            {
                new WorldTick().AdvanceWeeks(world, 1);
            }
            if (op is >= 4 and <= 7)
            {
                removedEvents += eventsBefore - world.Events.Count;
                removedFighters += fightersBefore - world.Fighters.Count;
            }
            AssertIdOrdered(world, $"after step {step} (op {op})");
        }
        Assert.True(removedEvents > 0 && removedFighters > 0,
            $"the walk must exercise removals (events {removedEvents}, fighters {removedFighters})");

        var loaded = RoundTrip(world);
        AssertIdOrdered(loaded, "after a load");
        var live = KeyOrders(world);
        foreach (var (name, ids) in KeyOrders(loaded)) Assert.Equal(live[name], ids);
    }

    /// <summary>
    /// Remove a NON-newest event and a NON-newest fighter early (the cancel path frees a slot in the
    /// middle of both collections), then run a year in the never-saved world and in a saved-and-loaded
    /// copy: the two must be identical, enumeration order included.
    /// </summary>
    [Fact]
    public void RemovingAnOldEventAndFighter_ThenAYear_LoadedWorldMatchesNeverSaved()
    {
        World Setup()
        {
            var w = PlayerStartWorld(23, hires: 3);
            var roster = w.RosterOf(w.PlayerPromotion).OrderBy(f => f.Id.Value).ToList();
            var booking = new PlayerBooking();
            var old = booking.CreateEvent(w, w.CurrentDate.AddDays(14));
            Assert.True(booking.AddLocalBout(w, old, roster[0].Id, PrimaryStyle.Grappler, 3, out var err), err);
            var oldLocal = old.Card[0].BlueCorner;
            var later = booking.CreateEvent(w, w.CurrentDate.AddDays(28));
            Assert.True(booking.AddLocalBout(w, later, roster[1].Id, PrimaryStyle.Striker, 3, out err), err);

            booking.CancelEvent(w, old);
            Assert.Null(w.Get(old.Id));
            Assert.Null(w.Get(oldLocal));
            Assert.True(w.Events.Keys.Max(k => k.Value) > old.Id.Value, "the removed event must not be the newest");
            Assert.True(w.Fighters.Keys.Max(k => k.Value) > oldLocal.Value, "the removed fighter must not be the newest");
            return w;
        }

        var straight = Setup();
        var resumed = RoundTrip(Setup());
        new WorldTick().AdvanceWeeks(straight, 52);
        new WorldTick().AdvanceWeeks(resumed, 52);

        AssertIdOrdered(straight, "after a year");
        var diffs = PersistenceGraph.Diff(straight, resumed, max: 40);
        Assert.True(diffs.Count == 0,
            "After removing an old event and fighter, a loaded world diverged from a never-saved one:\n" + PersistenceGraph.Describe(diffs));
    }
}
