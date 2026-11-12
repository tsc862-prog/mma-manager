using System.Collections.Concurrent;
using System.Text;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Org;

namespace LongSim;

/// <summary>
/// asks &lt;seeds&gt; &lt;years&gt; [--csv PATH]: the price of every fighter in the world, by the
/// stage he fights on and by his true ability band, at day one and after each
/// year. Prints the live valuation beside the PRE-v45 formula (true ability x
/// resume, kept here verbatim as <see cref="LegacyAsk"/>) on the SAME world, so
/// the level and the pyramid of prices can be compared like with like; plus
/// the contract purses actually being paid, and how much of true ability the
/// ask still tracks (correlation by experience bucket). The harness may read
/// true ability: it is measuring, not deciding.
/// </summary>
public static class Asks
{
    private sealed record Row(int Snap, ulong Seed, Fighter F, int Ca, string Tier, int Decisive, decimal Legacy, decimal Live,
        decimal Paid, PublicStanding.Facts Pub, int Age, string Sheet);

    public static void Run(string[] args)
    {
        int seeds = args.Length > 1 ? int.Parse(args[1]) : 4;
        int years = args.Length > 2 ? int.Parse(args[2]) : 5;
        string? csv = null;
        for (int i = 3; i < args.Length - 1; i++) if (args[i] == "--csv") csv = args[i + 1];

        var rows = new ConcurrentBag<Row>();
        Parallel.ForEach(Enumerable.Range(1, seeds).Select(i => (ulong)i), seed =>
        {
            var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });
            world.PlayerPromotion = default;
            var tick = new WorldTick();
            Snapshot(world, seed, 0, rows);
            for (int y = 1; y <= years; y++)
            {
                tick.AdvanceWeeks(world, 52);
                Snapshot(world, seed, y, rows);
            }
        });

        var all = rows.ToList();
        Console.WriteLine($"=== ASKS — {seeds} seeds x {years} years · legacy = pre-v45 formula (true ability x resume) on the same world · live = this build ===");
        foreach (int snap in new[] { 0, years }.Distinct())
        {
            var at = all.Where(r => r.Snap == snap).ToList();
            Console.WriteLine();
            Console.WriteLine($"--- {(snap == 0 ? "DAY ONE" : $"YEAR {snap}")} · {at.Count} active fighters ---");
            Table("by stage (fighter's org tier), ESTABLISHED (8+ decisive bouts)", at.Where(r => r.Decisive >= 8), r => r.Tier, TierOrder);
            Table("by stage, ALL", at, r => r.Tier, TierOrder);
            Table("by TRUE ability band, ESTABLISHED", at.Where(r => r.Decisive >= 8), r => Band(r.Ca), BandOrder);
            Table("by TRUE ability band, ALL", at, r => Band(r.Ca), BandOrder);
            Table("by experience (decisive bouts)", at, r => Exp(r.Decisive), ExpOrder);
            Console.WriteLine("  purses being PAID on live contracts, by stage (p50 / p90 / mean):");
            foreach (var g in at.Where(r => r.F.Contract is not null).GroupBy(r => r.Tier).OrderBy(g => TierOrder(g.Key)))
            {
                var p = g.Select(r => r.Paid).OrderBy(x => x).ToList();
                Console.WriteLine($"    {g.Key,-14} n={p.Count,5}  {Pct(p, 50),9:N0} {Pct(p, 90),9:N0}  mean {p.Average(),9:N0}");
            }
            Console.WriteLine("  how much of TRUE ability the ask tracks (Pearson r of ask vs true ability; of reputation vs true ability):");
            foreach (var g in at.GroupBy(r => Exp(r.Decisive)).OrderBy(g => ExpOrder(g.Key)))
                Console.WriteLine($"    {g.Key,-8} n={g.Count(),5}  legacy r={Corr(g.Select(r => ((double)r.Ca, (double)r.Legacy))),5:0.00}  live r={Corr(g.Select(r => ((double)r.Ca, (double)r.Live))),5:0.00}  rep r={Corr(g.Select(r => ((double)r.Ca, r.Pub.Reputation))),5:0.00}");
        }

        if (csv is not null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("snap,seed,id,ca,pa,w,l,d,streak,fin,pop,mom,disp,age,tier,contract_stage,stage,results,honours,proof,rep,share,bouts_read,idle,legacy,live,paid,champ");
            foreach (var r in all.OrderBy(r => r.Seed).ThenBy(r => r.Snap).ThenBy(r => r.F.Id.Value))
            {
                var f = r.F; var p = r.Pub;
                sb.AppendLine(string.Join(",", r.Snap, r.Seed, f.Id.Value, r.Ca, r.Sheet, r.Age,
                    r.Tier, p.ContractStage, p.Stage.ToString("0.00"), p.Results.ToString("0.00"), p.Honours.ToString("0.00"),
                    p.Proof.ToString("0.00"), p.Reputation.ToString("0.00"), p.WinShare.ToString("0.000"), p.BoutsRead, p.DaysIdle,
                    r.Legacy, r.Live, r.Paid, p.Champion ? 1 : 0));
            }
            File.WriteAllText(csv, sb.ToString());
            Console.WriteLine($"\nwrote {all.Count} rows to {csv}");
        }
    }

    // The fighter's sheet as of the snapshot (the Fighter object keeps changing after it).
    private static string Sheet(Fighter f) => string.Join(",", f.PotentialAbility.Value, f.Record.Wins, f.Record.Losses,
        f.Record.Draws, f.Record.CurrentStreak, f.Record.WinsByKo + f.Record.WinsBySubmission, f.Intangibles.Popularity.Value,
        f.Momentum, (int)f.Disposition);

    private static void Snapshot(World world, ulong seed, int snap, ConcurrentBag<Row> rows)
    {
        var val = new FighterValuation();
        foreach (var f in world.Fighters.Values.Where(f => f.Status == FighterStatus.Active && !f.LocalTalent).OrderBy(f => f.Id.Value))
        {
            var org = f.Contract is { } c ? world.Get(c.Promotion) : null;
            string tier = org is null ? "FA" : OrgSizing.Label(OrgSizing.SizeOf(org));
            rows.Add(new Row(snap, seed, f, FighterAbility.CurrentAbility(f), tier, f.Record.Wins + f.Record.Losses,
                LegacyAsk(world, f), val.Evaluate(world, f).AskingPurse.Amount, f.Contract?.PursePerFight.Amount ?? 0m,
                PublicStanding.Read(world, f), f.AgeOn(world.CurrentDate), Sheet(f)));
        }
    }

    // The asking purse exactly as main computed it before fog-honest asks (true
    // ability x resume, discovered-local floor off true potential).
    public static decimal LegacyAsk(World world, Fighter f)
    {
        int ability = FighterAbility.CurrentAbility(f);
        int pop = f.Intangibles.Popularity;
        int momentum = Math.Clamp(f.Momentum, -100, 100);
        bool champion = world.Titles.Values.Any(t => t.CurrentChampion == f.Id);
        int toughness = FighterValuation.AgentToughness(f);
        decimal purse = 2_500m + Math.Max(0, ability - 25) * 350m + Math.Max(0, ability - 50) * 1_800m;
        int decisive = f.Record.Wins + f.Record.Losses;
        double experience = Math.Min(1.0, (decisive + f.Record.Draws * 0.5) / 12.0);
        double winPct = decisive == 0 ? 0.5 : f.Record.Wins / (double)decisive;
        double form = winPct >= 0.5 ? 1.0 : Math.Max(0.35, Math.Pow(winPct / 0.5, 1.2));
        purse *= (decimal)((0.15 + 0.85 * experience) * form);
        purse += Math.Max(0, pop - 30) * 600m;
        purse += Math.Max(0, momentum) * 150m;
        if (champion) purse += 30_000m;
        purse *= 1m + toughness / 100m * 0.25m;
        if (Franchise.IsFace(world, f)) purse *= 1.20m;
        if (f.Contract is null && f.Record.CurrentStreak <= -2)
            purse *= f.Record.CurrentStreak <= -4 ? 0.5m : 0.7m;
        if (f.Record.TotalBouts <= 4 && f.Accolades.Any(a => a.Title.StartsWith("Discovered on the local circuit")))
            purse = Math.Max(purse, 5_000m + Math.Max(0, f.PotentialAbility.Value - 55) * 150m);
        decimal medal = 0m;
        foreach (var a in f.Accolades)
        {
            if (a.Title.EndsWith("— gold")) medal = Math.Max(medal, 12_000m);
            else if (a.Title.EndsWith("— silver")) medal = Math.Max(medal, 7_000m);
            else if (a.Title.EndsWith("— bronze")) medal = Math.Max(medal, 4_000m);
        }
        purse += medal;
        return Math.Max(1_500m, Math.Round(purse / 500m) * 500m);
    }

    private static void Table(string title, IEnumerable<Row> rows, Func<Row, string> key, Func<string, int> order)
    {
        Console.WriteLine($"  {title}:   n · legacy p10/p50/p90/mean · live p10/p50/p90/mean · live/legacy mean");
        foreach (var g in rows.GroupBy(key).OrderBy(g => order(g.Key)))
        {
            var a = g.Select(r => r.Legacy).OrderBy(x => x).ToList();
            var b = g.Select(r => r.Live).OrderBy(x => x).ToList();
            Console.WriteLine($"    {g.Key,-14} {a.Count,5} · {Pct(a, 10),8:N0} {Pct(a, 50),8:N0} {Pct(a, 90),9:N0} {a.Average(),9:N0} · "
                + $"{Pct(b, 10),8:N0} {Pct(b, 50),8:N0} {Pct(b, 90),9:N0} {b.Average(),9:N0} · {b.Average() / Math.Max(1m, a.Average()),5:0.00}");
        }
    }

    private static decimal Pct(List<decimal> sorted, int p) =>
        sorted.Count == 0 ? 0 : sorted[Math.Clamp((int)Math.Round(p / 100.0 * (sorted.Count - 1)), 0, sorted.Count - 1)];

    private static double Corr(IEnumerable<(double X, double Y)> pts)
    {
        var l = pts.ToList();
        if (l.Count < 3) return 0;
        double mx = l.Average(p => p.X), my = l.Average(p => p.Y);
        double sxy = l.Sum(p => (p.X - mx) * (p.Y - my)), sxx = l.Sum(p => (p.X - mx) * (p.X - mx)), syy = l.Sum(p => (p.Y - my) * (p.Y - my));
        return sxx <= 0 || syy <= 0 ? 0 : sxy / Math.Sqrt(sxx * syy);
    }

    private static readonly string[] Tiers = { "FA", "City", "Regional", "National", "Continental", "International" };
    private static int TierOrder(string t) => Array.IndexOf(Tiers, t);
    private static string Band(int ca) => ca < 40 ? "<40" : ca < 50 ? "40-49" : ca < 60 ? "50-59" : ca < 70 ? "60-69" : ca < 80 ? "70-79" : "80+";
    private static int BandOrder(string b) => Array.IndexOf(new[] { "<40", "40-49", "50-59", "60-69", "70-79", "80+" }, b);
    private static string Exp(int d) => d == 0 ? "0" : d <= 3 ? "1-3" : d <= 7 ? "4-7" : d <= 15 ? "8-15" : "16+";
    private static int ExpOrder(string e) => Array.IndexOf(new[] { "0", "1-3", "4-7", "8-15", "16+" }, e);
}
