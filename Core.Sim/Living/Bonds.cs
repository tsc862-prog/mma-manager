using MmaManager.Core.Model;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Living;

/// <summary>
/// The warm side of the relationship graph (the cold side is <see cref="Storylines"/>'
/// rivalries): friendships form between rostermates and deepen with time together,
/// family ties arrive with newgen dynasties, and mentors come with the camps layer.
/// Bonds push back on the matchmaker (friends don't fight on undercards), pull in
/// free agency (fighters drift toward their people), and BREAK dramatically — a
/// title fight between friends cuts deep, and a broken friendship makes the best
/// rivalries in the sport.
/// </summary>
public static class Bonds
{
    /// <summary>The bond between two fighters, if any.</summary>
    public static Relationship? Between(World world, FighterId a, FighterId b) =>
        world.Relationships.FirstOrDefault(r => r.IsBetween(a, b));

    /// <summary>
    /// Would these two rather not be booked against each other? Family never wants
    /// it; friends refuse while the friendship is real. Only a belt overrides.
    /// </summary>
    public static bool WontFight(World world, FighterId a, FighterId b) =>
        Between(world, a, b) is { } r
        && (r.Kind is RelationshipKind.Family or RelationshipKind.Mentor || r.Strength >= 30);

    /// <summary>
    /// Monthly: friendships form and deepen. The CAMP is the primary channel —
    /// people who share a room every day bond hardest — with the shared roster
    /// (fight weeks, bus rides) a slower second.
    /// </summary>
    public static void RunMonthly(World world, DateOnly date, SimRandom rng)
    {
        // Friendships deepen with time together: fastest on shared mats, slower
        // when they merely share an employer (capped short of family-deep).
        foreach (var r in world.Relationships)
        {
            if (r.Kind != RelationshipKind.Friend || r.Strength >= 95) continue;
            var a = world.Get(r.A);
            var b = world.Get(r.B);
            if (a is null || b is null) continue;
            if (a.Camp is { } room && a.Camp == b.Camp) r.Strength = Math.Min(95, r.Strength + 2);
            else if (a.Contract?.Promotion is { } pa && pa == b.Contract?.Promotion) r.Strength += 1;
        }

        // The camp channel: two fighters sharing a room click — training partners
        // become friends far faster than colleagues do.
        var camps = world.Camps.Values.ToList();
        for (int attempt = 0; attempt < 2 && camps.Count > 0; attempt++)
        {
            var camp = camps[rng.Range(0, camps.Count - 1)];
            var mates = world.Fighters.Values
                .Where(f => f.Camp == camp.Id && f.Status == FighterStatus.Active).ToList();
            TryBefriend(world, date, rng, mates, baseChance: 0.50, strength: 50);
        }

        // The roster channel: a couple of rolls a month, world-wide.
        var promos = world.Promotions.Values.Where(p => !p.Defunct).ToList();
        for (int attempt = 0; attempt < 2 && promos.Count > 0; attempt++)
        {
            var promo = promos[rng.Range(0, promos.Count - 1)];
            var roster = world.RosterOf(promo.Id).Where(f => f.Status == FighterStatus.Active).ToList();
            TryBefriend(world, date, rng, roster, baseChance: 0.30, strength: 45);
        }
    }

    private static void TryBefriend(World world, DateOnly date, SimRandom rng,
        List<Fighter> pool, double baseChance, int strength)
    {
        if (pool.Count < 2) return;
        var x = pool[rng.Range(0, pool.Count - 1)];
        var y = pool[rng.Range(0, pool.Count - 1)];
        if (x.Id == y.Id || x.Sex != y.Sex) return;
        if (Between(world, x.Id, y.Id) is not null) return;
        if (Storylines.RivalryBetween(world, x.Id, y.Id) is not null) return;
        if (FriendCount(world, x.Id) >= 3 || FriendCount(world, y.Id) >= 3) return;

        double chance = baseChance
            + (x.Country == y.Country ? 0.25 : 0)
            + (x.WeightClass == y.WeightClass ? 0.10 : 0);
        if (!rng.Chance(chance)) return;

        world.Relationships.Add(new Relationship
        {
            A = x.Id, B = y.Id, Kind = RelationshipKind.Friend,
            Strength = strength, Since = date,
            Note = FriendNotes[rng.Range(0, FriendNotes.Length - 1)],
        });
    }

    /// <summary>
    /// After a card: a bout between bonded fighters leaves a mark. Family survives
    /// anything (blood is blood, but it stings). A friendship takes real damage —
    /// and when it breaks, it breaks into the hottest kind of rivalry.
    /// </summary>
    public static void AfterEvent(World world, Event ev, DateOnly date)
    {
        foreach (var fight in ev.Card)
        {
            if (fight.Outcome is null) continue;
            if (Between(world, fight.RedCorner, fight.BlueCorner) is not { } bond) continue;
            var a = world.Get(fight.RedCorner);
            var b = world.Get(fight.BlueCorner);
            if (a is null || b is null) continue;

            if (bond.Kind == RelationshipKind.Family)
            {
                bond.Strength = Math.Max(30, bond.Strength - 10);
                News.Post(world, date, NewsCategory.Rivalry,
                    $"Blood against blood: {a.FullName} vs {b.FullName}",
                    "Family on both sides of the cage — nobody in that locker room enjoyed it.", ev.Promotion, a.Id);
                continue;
            }
            if (bond.Kind != RelationshipKind.Friend) continue;

            bond.Strength -= 35;
            if (bond.Strength < 20)
            {
                // The friendship is over — and nothing burns hotter than a broken one.
                world.Relationships.Remove(bond);
                if (Storylines.RivalryBetween(world, a.Id, b.Id) is null)
                    world.Rivalries.Add(new Rivalry { A = a.Id, B = b.Id, Intensity = 40, Since = date });
                News.Post(world, date, NewsCategory.Rivalry,
                    $"The friendship is over: {a.FullName} and {b.FullName}",
                    "They came up together. After that fight, they don't speak.", ev.Promotion, a.Id);
            }
            else
            {
                News.Post(world, date, NewsCategory.Rivalry,
                    $"Friends forced to fight: {a.FullName} vs {b.FullName}",
                    "Business in the cage, but something between them took a hit.", ev.Promotion, a.Id);
            }
        }
    }

    private static int FriendCount(World world, FighterId id) =>
        world.Relationships.Count(r => r.Kind == RelationshipKind.Friend && r.Involves(id));

    private static readonly string[] FriendNotes =
    {
        "training partners",
        "came up together on the local circuit",
        "cornered each other since the amateurs",
        "road roommates turned best friends",
    };
}
