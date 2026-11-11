using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;

namespace MmaManager.Core.Sim.Booking;

/// <summary>
/// Local promotions are LOCAL. The build's tier sort hands each division's
/// talent out by ability alone, so a small hall's roster used to be whoever
/// happened to sit in its ability band — 9-28% of a "local" show's fighters
/// were from its own continent. This pass runs after that sort and lets every
/// City- and Regional-tier promotion trade its outsiders for fighters OF THE
/// SAME LEVEL from its own city, then its own country, then its own continent.
///
/// Every move is an ability-for-ability SWAP (within <see cref="AbilityWindow"/>)
/// with another roster or the unsigned leftovers, so no roster changes size or
/// level, no fighter is generated or altered, and nothing draws from a random
/// stream. A swap is only taken if the other side loses no locality of its own:
/// the Rio house never pays for its carioca with São Paulo's paulistano. The
/// big stages (National and up) never initiate — the giants are international
/// by design, and a cosmopolitan niche like the Gulf's hired guns stays that way.
/// Designed fighters never move.
/// </summary>
internal static class RosterNativity
{
    /// <summary>How far apart (current ability) two fighters may be and still be "the same level".</summary>
    public const int AbilityWindow = 8;

    /// <summary>How local a fighter is to a promotion: 3 its own city, 2 its country, 1 its continent, 0 abroad.</summary>
    public static int Score(Fighter f, City? home, Region region)
    {
        if (home is not null && f.HomeCity == home.Id) return 3;
        if (home is not null && f.Country == home.Country) return 2;
        return f.Nationality == region ? 1 : 0;
    }

    /// <summary>The tiers that build a local roster: City and Regional.</summary>
    public static bool IsLocalStage(int prestige) => prestige < Org.OrgSizing.PrestigeFor(OrgSize.National);

    public static void Apply(WorldBuilder.PromotionSpec[] specs, IReadOnlyList<City?> homes,
        List<Fighter>[] rosters, IEnumerable<Fighter> pool, IEnumerable<WeightClass> classes, Sex sex)
    {
        // Who holds whom right now: a roster index, or -1 for the unsigned leftovers.
        var owner = new Dictionary<FighterId, int>();
        for (int p = 0; p < rosters.Length; p++)
            foreach (var f in rosters[p])
                if (f.Sex == sex) owner[f.Id] = p;
        foreach (var f in pool)
            if (f.Sex == sex && !owner.ContainsKey(f.Id)) owner[f.Id] = -1;

        var byId = pool.Concat(rosters.SelectMany(r => r))
            .Where(f => f.Sex == sex)
            .GroupBy(f => f.Id).ToDictionary(g => g.Key, g => g.First());
        var claimed = new HashSet<FighterId>();   // swapped in somewhere: stays put
        int Local(Fighter f, int org) => org < 0 ? 0 : Score(f, homes[org], specs[org].Region);

        foreach (var wc in classes)
        {
            var inClass = owner.Keys.Select(id => byId[id])
                .Where(f => f.WeightClass == wc && !f.Designed)
                .OrderBy(f => f.Id.Value)
                .ToList();
            var ability = inClass.ToDictionary(f => f.Id, FighterAbility.CurrentAbility);

            // Round robin over the local stages (spec order within a round), one
            // outsider each per round, so two houses on one continent (Kansai and
            // Mekong) share the local talent instead of the first one taking it all.
            // Top of the card first: the belt-holders and contenders the fans
            // actually see are the first to come home.
            var stages = Enumerable.Range(0, specs.Length).Where(p => IsLocalStage(specs[p].Prestige)).ToArray();
            var tried = new HashSet<(int, FighterId)>();
            for (bool progress = true; progress;)
            {
                progress = false;
                foreach (var p in stages)
                {
                    var n = rosters[p]
                        .Where(f => f.Sex == sex && f.WeightClass == wc && !f.Designed && !claimed.Contains(f.Id)
                                    && Local(f, p) < 3 && !tried.Contains((p, f.Id)))
                        .OrderByDescending(f => ability[f.Id]).ThenBy(f => f.Id.Value)
                        .FirstOrDefault();
                    if (n is null) continue;
                    tried.Add((p, n.Id));
                    progress = true;

                    int mine = Local(n, p);
                    int level = ability[n.Id];
                    Fighter? best = null;
                    int bestScore = mine, bestGap = int.MaxValue;
                    foreach (var c in inClass)
                    {
                        int q = owner[c.Id];
                        if (q == p || claimed.Contains(c.Id)) continue;
                        int gap = Math.Abs(ability[c.Id] - level);
                        if (gap > AbilityWindow) continue;
                        int score = Local(c, p);
                        if (score < bestScore || (score == bestScore && (best is null || gap >= bestGap))) continue;
                        if (q >= 0 && Local(n, q) < Local(c, q)) continue;   // the other side keeps its own
                        best = c; bestScore = score; bestGap = gap;
                    }
                    if (best is null) continue;

                    int from = owner[best.Id];
                    rosters[p].Remove(n);
                    rosters[p].Add(best);
                    owner[best.Id] = p;
                    if (from >= 0)
                    {
                        rosters[from].Remove(best);
                        rosters[from].Add(n);
                    }
                    owner[n.Id] = from;
                    claimed.Add(best.Id);
                }
            }
        }
    }
}
