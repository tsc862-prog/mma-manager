using System.Collections.Concurrent;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Finance;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Org;

namespace LongSim;

/// <summary>
/// standin &lt;fromSeed&gt; &lt;toSeed&gt; &lt;years&gt; &lt;pickYear&gt;: can a house that plays the streaming lane the way a
/// player who wants a streaming giant would (no platform of its own, so its lane is open) land one? At the start of
/// <c>pickYear</c> the weakest house that a streaming giant would sign (Continental, prestige 50+, no giant yet; lowest
/// prestige, then lowest id) becomes the stand-in: its own platform is shut and kept shut (the harness nulls it every
/// week; the house's monthly relaunch still pays the $50k launch fee, a small cost the stand-in carries). From then on
/// the ordinary market treats it as any open-lane house (the monthly lottery and a freed giant's shop-around), and it
/// takes any giant that pitches (as a player who wants one would). Reported per seed and pooled: giants free on the pick
/// day, weeks until a giant is free, until one pitches the stand-in, until it signs one. Harness only; draws nothing of
/// its own (the world's own streams run as ever).
/// </summary>
public static class StandIn
{
    private sealed record Row(ulong Seed, string Who, int FreeAtPick, int? WeeksToFree, int? WeeksToPitch, int? WeeksToSign,
        int Weeks, int QualifiedWeeks, double MeanFree);

    public static void Run(string[] args)
    {
        ulong from = ulong.Parse(args[1]), to = ulong.Parse(args[2]);
        int years = int.Parse(args[3]), pickYear = int.Parse(args[4]);
        var rows = new ConcurrentBag<Row>();
        Parallel.ForEach(Enumerable.Range((int)from, (int)(to - from + 1)).Select(i => (ulong)i),
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) },
            seed => { if (One(seed, years, pickYear) is { } r) rows.Add(r); });

        var all = rows.OrderBy(r => r.Seed).ToList();
        Console.WriteLine($"=== STANDIN seeds {from}-{to} · {years} years · stand-in picked at the start of year {pickYear} (a Continental house, prestige 50+, no platform of its own from then on) ===");
        foreach (var r in all)
            Console.WriteLine($"  seed {r.Seed,3} {r.Who,-6} free at pick {r.FreeAtPick} · weeks to a free giant {W(r.WeeksToFree)} · to a pitch {W(r.WeeksToPitch)} · to a signing {W(r.WeeksToSign)}"
                + $" · weeks watched {r.Weeks} (qualified {r.QualifiedWeeks}) · giants free mean {r.MeanFree:0.00}");
        int n = Math.Max(1, all.Count);
        string Share(Func<Row, int?> f, int weeks) => $"{100.0 * all.Count(r => f(r) is { } w && w <= weeks) / n:0}%";
        string Med(Func<Row, int?> f)
        {
            var v = all.Select(f).Where(x => x is not null).Select(x => x!.Value).OrderBy(x => x).ToList();
            return v.Count == 0 ? "-" : v[v.Count / 2].ToString();
        }
        Console.WriteLine($"  seeds {all.Count} · a giant free on the pick day {100.0 * all.Count(r => r.FreeAtPick > 0) / n:0}% · giants free mean over the watch {all.Select(r => r.MeanFree).DefaultIfEmpty(0).Average():0.00} of 4");
        Console.WriteLine($"  a giant free within 1y {Share(r => r.WeeksToFree, 52)} · 3y {Share(r => r.WeeksToFree, 156)} · ever {Share(r => r.WeeksToFree, int.MaxValue)} (median weeks {Med(r => r.WeeksToFree)})");
        Console.WriteLine($"  pitched by a giant within 1y {Share(r => r.WeeksToPitch, 52)} · 3y {Share(r => r.WeeksToPitch, 156)} · ever {Share(r => r.WeeksToPitch, int.MaxValue)} (median weeks {Med(r => r.WeeksToPitch)})");
        Console.WriteLine($"  signed a giant within 1y {Share(r => r.WeeksToSign, 52)} · 3y {Share(r => r.WeeksToSign, 156)} · ever {Share(r => r.WeeksToSign, int.MaxValue)} (median weeks {Med(r => r.WeeksToSign)})");
    }

    private static string W(int? w) => w is { } x ? x.ToString() : "never";

    private static Row? One(ulong seed, int years, int pickYear)
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });
        world.PlayerPromotion = default;
        var tick = new WorldTick();
        int pickWeek = 52 * (pickYear - 1);
        for (int w = 0; w < pickWeek; w++) tick.AdvanceWeek(world);

        bool Qualifies(Promotion p) => MediaService.LadderView(world, p).First(r => r.Kind == MediaKind.MajorStream).Qualified;
        var standIn = world.Promotions.Values
            .Where(p => !p.Defunct && OrgSizing.SizeOf(p) == OrgSize.Continental && Qualifies(p)
                && !p.MediaDeals.Any(d => d.Kind == MediaKind.MajorStream))
            .OrderBy(p => p.PrestigeHundredths).ThenBy(p => p.Id.Value).FirstOrDefault();
        if (standIn is null) return null;
        int Free() => MediaHealth.Giants.Count(g => MediaService.PartnerFree(world, g));
        int freeAtPick = Free();
        standIn.Stream = null;
        var courted = standIn.CourtedBy.Where(g => MediaHealth.Giants.Contains(g)).ToHashSet();
        int? toFree = freeAtPick > 0 ? 0 : null, toPitch = null, toSign = null;
        int weeks = 0, qualified = 0, freeSum = 0;
        for (int w = pickWeek; w < 52 * years && !standIn.Defunct; w++)
        {
            tick.AdvanceWeek(world);
            weeks++;
            standIn.Stream = null;   // the stand-in runs no platform of its own
            if (Qualifies(standIn)) qualified++;
            int free = Free();
            freeSum += free;
            if (toFree is null && free > 0) toFree = weeks;
            if (toPitch is null && (standIn.Offers.Any(o => o.Media == MediaKind.MajorStream)
                    || standIn.CourtedBy.Any(g => MediaHealth.Giants.Contains(g) && !courted.Contains(g))
                    || standIn.MediaDeals.Any(d => d.Kind == MediaKind.MajorStream)))
                toPitch = weeks;
            if (toSign is null && standIn.MediaDeals.Any(d => d.Kind == MediaKind.MajorStream)) toSign = weeks;
        }
        return new Row(seed, standIn.Abbreviation, freeAtPick, toFree, toPitch, toSign, weeks, qualified, weeks == 0 ? 0 : freeSum / (double)weeks);
    }
}
