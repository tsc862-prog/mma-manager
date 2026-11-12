using MmaManager.Core.Model;
using MmaManager.Core.Sim.Org;

namespace LongSim;

/// <summary>
/// What the money at the top buys, per seed (compare's MONEY FLOWS block):
/// who buys whom (DURABLE: Acquisition ledger lines), bidding wars and who wins
/// them at what purse, fighters signed straight off another org's roster (a
/// poach: SAMPLED weekly, contract promotion changing org-to-org with the old
/// org still alive), purses at International tier and the flagship's bank by year.
/// Observes only; no draws.
/// </summary>
public sealed class MoneyFlows
{
    public readonly List<(string Buyer, OrgSize BuyerTier, string Desc, decimal Price)> Acquisitions = new();
    public int WarsStarted;
    public readonly List<(string Winner, OrgSize Tier, decimal Purse)> WarsWon = new();
    public readonly Dictionary<(OrgSize From, string To), int> Poaches = new();
    public readonly List<decimal> InternationalPurses = new();
    public readonly List<decimal> AfcBankByYear = new();
    public int Years;
    public int AfcTvLost, TvLost;

    private readonly Dictionary<PromotionId, HashSet<string>> _deals = new();

    private readonly Dictionary<FighterId, PromotionId?> _contract = new();
    private readonly HashSet<FighterId> _wars = new();

    public void Week(World world)
    {
        // Wars: new ones, and the ones that closed with a signing this week.
        var now = world.BiddingWars.Select(w => w.Fighter).ToHashSet();
        foreach (var id in now) if (_wars.Add(id)) WarsStarted++;
        foreach (var id in _wars.Where(id => !now.Contains(id)).ToList())
        {
            _wars.Remove(id);
            if (world.Get(id)?.Contract is { } c && world.CurrentDate.DayNumber - c.SignedOn.DayNumber <= 7
                && world.Get(c.Promotion) is { } w)
                WarsWon.Add((w.Abbreviation, OrgSizing.SizeOf(w), c.PursePerFight.Amount));
        }

        // TV deals lost: a partner gone from a live org's slate (dropped, not renewed, or replaced).
        foreach (var p in world.Promotions.Values)
        {
            if (p.Defunct) continue;
            var cur = p.MediaDeals.Select(d => d.Partner).ToHashSet();
            if (_deals.TryGetValue(p.Id, out var before))
            {
                int gone = before.Count(x => !cur.Contains(x));
                TvLost += gone;
                if (p.Abbreviation == "AFC") AfcTvLost += gone;
            }
            _deals[p.Id] = cur;
        }

        // Poaches: org-to-org moves with the old org still in business.
        foreach (var f in world.Fighters.Values)
        {
            var cur = f.Contract?.Promotion;
            if (_contract.TryGetValue(f.Id, out var prev) && prev is { } from && cur is { } to && from != to
                && world.Get(from) is { Defunct: false } old && world.Get(to) is { } dest)
            {
                var key = (OrgSizing.SizeOf(old), dest.Abbreviation);
                Poaches[key] = Poaches.GetValueOrDefault(key) + 1;
            }
            _contract[f.Id] = cur;
        }
    }

    public void YearEnd(World world)
    {
        Years++;
        var afc = world.Promotions.Values.FirstOrDefault(p => p.Abbreviation == "AFC");
        AfcBankByYear.Add(afc?.Balance.Amount ?? 0m);
    }

    public void End(World world, DateOnly start)
    {
        foreach (var p in world.Promotions.Values)
            foreach (var e in p.Ledger)
                if (e.Category == FinanceCategory.Acquisition && e.Amount.Amount < 0 && e.Date > start)
                    Acquisitions.Add((p.Abbreviation, p.Defunct ? OrgSize.City : OrgSizing.SizeOf(p), e.Description, -e.Amount.Amount));
        foreach (var p in world.Promotions.Values.Where(p => !p.Defunct && OrgSizing.SizeOf(p) == OrgSize.International))
            foreach (var f in world.RosterOf(p.Id))
                if (f.Contract is { } c) InternationalPurses.Add(c.PursePerFight.Amount);
    }

    public static void Print(IReadOnlyList<MoneyFlows> seeds)
    {
        if (seeds.Count == 0) return;
        double years = seeds.Sum(s => s.Years);
        Console.WriteLine();
        Console.WriteLine("  --- MONEY FLOWS (acquisitions DURABLE from ledgers; wars and poaches SAMPLED weekly; purses at the end) ---");
        var acq = seeds.SelectMany(s => s.Acquisitions).ToList();
        Console.WriteLine($"  acquisitions: {acq.Count} ({acq.Count / years:0.00}/seed-yr) · buyers: "
            + string.Join(", ", acq.GroupBy(a => a.Buyer).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => $"{g.Key} x{g.Count()}"))
            + (acq.Count > 0 ? $" · mean price {acq.Average(a => a.Price):C0}" : ""));
        var won = seeds.SelectMany(s => s.WarsWon).ToList();
        int started = seeds.Sum(s => s.WarsStarted);
        Console.WriteLine($"  bidding wars: {started / years:0.00} started/seed-yr, {won.Count / years:0.00} won with a signing · mean winning purse "
            + (won.Count > 0 ? $"{won.Average(w => w.Purse):C0} (median {Median(won.Select(w => w.Purse)):C0})" : "-")
            + " · winners: " + string.Join(", ", won.GroupBy(w => w.Winner).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Take(8).Select(g => $"{g.Key} x{g.Count()}")));
        var poach = seeds.SelectMany(s => s.Poaches).ToList();
        foreach (var from in new[] { OrgSize.Continental, OrgSize.National })
        {
            var rows = poach.Where(p => p.Key.From == from).ToList();
            int total = rows.Sum(r => r.Value);
            Console.WriteLine($"  poached from {OrgSizing.Label(from)} orgs: {total / years:0.00}/seed-yr · to: "
                + string.Join(", ", rows.GroupBy(r => r.Key.To).Select(g => (g.Key, N: g.Sum(r => r.Value))).OrderByDescending(x => x.N).ThenBy(x => x.Key).Take(6).Select(x => $"{x.Key} {x.N / years:0.00}")));
        }
        Console.WriteLine($"  media deals lost (partner gone from a live org's slate): all orgs {seeds.Sum(s => s.TvLost) / years:0.00}/seed-yr · AFC {seeds.Sum(s => s.AfcTvLost) / years:0.00}/seed-yr");
        var purses = seeds.SelectMany(s => s.InternationalPurses).ToList();
        if (purses.Count > 0)
            Console.WriteLine($"  International purses at the end: n={purses.Count} mean {purses.Average():C0} median {Median(purses):C0} p90 {Pct(purses, 0.9):C0}");
        int ys = seeds.Max(s => s.AfcBankByYear.Count);
        Console.WriteLine("  AFC bank at year end (median over seeds, $M): " + string.Join(" ", Enumerable.Range(0, ys)
            .Select(y => $"Y{y + 1} {Median(seeds.Where(s => s.AfcBankByYear.Count > y).Select(s => s.AfcBankByYear[y])) / 1_000_000m:0.0}")));
    }

    private static decimal Median(IEnumerable<decimal> xs) => Pct(xs.ToList(), 0.5);
    private static decimal Pct(List<decimal> xs, double q)
    {
        if (xs.Count == 0) return 0m;
        var v = xs.OrderBy(x => x).ToList();
        return v[Math.Clamp((int)Math.Round(q * (v.Count - 1)), 0, v.Count - 1)];
    }
}
