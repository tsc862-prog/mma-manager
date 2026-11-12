using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Org;

namespace LongSim;

/// <summary>
/// ladders &lt;seed&gt; &lt;years&gt; [ABBR ...]: run one all-AI world and print the
/// contender ladders (champion + top 10) of the named promotions' divisions, or
/// of the #1 org, the biggest women's division and the largest Regional org when
/// none are named — each fighter with his record, streak and his last three
/// results (opponent and that opponent's current record). Reporting only.
/// </summary>
public static class Ladders
{
    public static void Run(string[] args)
    {
        ulong seed = args.Length > 1 ? ulong.Parse(args[1]) : 7;
        int years = args.Length > 2 ? int.Parse(args[2]) : 2;
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });
        world.PlayerPromotion = default;
        new WorldTick().AdvanceWeeks(world, 52 * years);

        var fights = new Dictionary<FightId, (Event Ev, Fight F)>();
        foreach (var e in world.Events.Values) foreach (var fi in e.Card) fights[fi.Id] = (e, fi);

        var picks = new List<(Promotion P, Division D)>();
        var named = args.Skip(3).ToHashSet();
        var live = world.Promotions.Values.Where(p => !p.Defunct).ToList();
        if (named.Count > 0)
            foreach (var p in live.Where(p => named.Contains(p.Abbreviation)))
                picks.AddRange(p.Divisions.Select(d => (p, d)));
        else
        {
            var top = live.OrderByDescending(p => p.Prestige.Value).ThenBy(p => p.Id.Value).First();
            picks.Add((top, top.Divisions.FirstOrDefault(d => d.WeightClass == WeightClass.Lightweight && d.Sex == Sex.Male) ?? top.Divisions[0]));
            var women = live.SelectMany(p => p.Divisions.Where(d => d.Sex == Sex.Female).Select(d => (p, d)))
                .OrderByDescending(x => x.p.Prestige.Value).ThenBy(x => x.p.Id.Value).FirstOrDefault();
            if (women.p is not null) picks.Add(women);
            var regional = live.Where(p => OrgSizing.SizeOf(p) == OrgSize.Regional).OrderByDescending(p => world.RosterOf(p.Id).Count()).ThenBy(p => p.Id.Value).FirstOrDefault();
            if (regional is not null) picks.Add((regional, regional.Divisions.OrderByDescending(d => d.Rankings.Count).First()));
        }

        Console.WriteLine($"=== LADDERS — seed {seed}, after {years} years ({world.CurrentDate:yyyy-MM-dd}) ===");
        foreach (var (p, d) in picks)
        {
            Console.WriteLine();
            Console.WriteLine($"--- {p.Abbreviation} ({OrgSizing.Label(OrgSizing.SizeOf(p))}, prestige {p.Prestige.Value}) · {(d.Sex == Sex.Female ? "Women's " : "")}{d.WeightClass} ---");
            var champ = d.Title is { } tid ? world.Get(tid)?.CurrentChampion : null;
            if (champ is { } c && world.Get(c) is { } cf) Line("  C", cf);
            int i = 0;
            foreach (var id in d.Rankings.Take(10)) if (world.Get(id) is { } f) Line($"{++i,3}", f);
        }

        void Line(string tag, Fighter f)
        {
            var last = new List<string>();
            for (int k = f.BoutHistory.Count - 1; k >= 0 && last.Count < 3; k--)
            {
                if (!fights.TryGetValue(f.BoutHistory[k], out var x) || x.F.Outcome is not { } o) continue;
                var opp = world.Get(x.F.RedCorner == f.Id ? x.F.BlueCorner : x.F.RedCorner);
                string res = o.Winner == f.Id ? "W" : o.Loser == f.Id ? "L" : "D";
                string where = world.Get(x.Ev.Promotion)?.Abbreviation ?? "club";
                last.Add($"{res} {opp?.LastName ?? "?"} ({opp?.Record.Wins}-{opp?.Record.Losses}, {where}{(x.F.IsTitleFight ? ", title" : "")})");
            }
            Console.WriteLine($"  {tag} {f.FullName,-34} {f.Record.Wins,2}-{f.Record.Losses,-2} streak {f.Record.CurrentStreak,3} · {string.Join(" · ", last)}");
        }
    }
}
