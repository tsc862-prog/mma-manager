using MmaManager.Core.Model;
using MmaManager.Core.Sim.Org;

namespace LongSim;

/// <summary>
/// The world at each year end (compare's BY YEAR block, printed every fifth year and the last): live houses per tier
/// (SAMPLED at year end), bankruptcies and acquisitions so far (DURABLE: the buyers' Acquisition ledger lines; a dead
/// house whose name no buyer's ledger carries went bankrupt), startups founded and alive, the AFC's bank and the
/// pooled Continental banks, poaching from Continental and National houses inside each window (SAMPLED weekly by
/// <see cref="MoneyFlows"/>), and International purses (every live International roster contract at year end).
/// Observes only; no draws.
/// </summary>
public sealed class YearMarks
{
    public sealed class Mark
    {
        public int Year;
        public Dictionary<OrgSize, int> Tiers = new();
        public int Bankruptcies, Acquisitions, Founded, FoundedAlive;
        public Dictionary<string, int> Buyers = new();
        public decimal AfcBank;
        public List<decimal> ContinentalBanks = new(), IntlPurses = new();
        public int PoachedFromContinental, PoachedFromNational, PoachedFromContinentalToAfc;
    }

    public readonly List<Mark> Marks = new();

    public void YearEnd(World world, int year, HashSet<PromotionId> dayOne, HashSet<PromotionId> deadAtStart, DateOnly start, MoneyFlows flows)
    {
        var m = new Mark { Year = year };
        foreach (var p in world.Promotions.Values.Where(p => !p.Defunct))
            m.Tiers[OrgSizing.SizeOf(p)] = m.Tiers.GetValueOrDefault(OrgSizing.SizeOf(p)) + 1;
        var bought = new HashSet<string>();
        foreach (var p in world.Promotions.Values.OrderBy(p => p.Id.Value))
            foreach (var e in p.Ledger)
                if (e.Category == FinanceCategory.Acquisition && e.Amount.Amount < 0 && e.Date > start)
                {
                    m.Acquisitions++;
                    m.Buyers[p.Abbreviation] = m.Buyers.GetValueOrDefault(p.Abbreviation) + 1;
                    bought.Add(e.Description.Replace("Acquisition of ", ""));
                }
        m.Bankruptcies = world.Promotions.Values.Count(p => p.Defunct && !deadAtStart.Contains(p.Id) && !bought.Contains(p.Name));
        var startups = world.Promotions.Values.Where(p => !dayOne.Contains(p.Id)).ToList();
        m.Founded = startups.Count;
        m.FoundedAlive = startups.Count(p => !p.Defunct);
        m.AfcBank = world.Promotions.Values.FirstOrDefault(p => p.Abbreviation == "AFC")?.Balance.Amount ?? 0m;
        foreach (var p in world.Promotions.Values.Where(p => !p.Defunct))
        {
            var tier = OrgSizing.SizeOf(p);
            if (tier == OrgSize.Continental) m.ContinentalBanks.Add(p.Balance.Amount);
            if (tier == OrgSize.International)
                foreach (var f in world.RosterOf(p.Id))
                    if (f.Contract is { } c) m.IntlPurses.Add(c.PursePerFight.Amount);
        }
        foreach (var kv in flows.Poaches)
        {
            if (kv.Key.From == OrgSize.Continental) { m.PoachedFromContinental += kv.Value; if (kv.Key.To == "AFC") m.PoachedFromContinentalToAfc += kv.Value; }
            if (kv.Key.From == OrgSize.National) m.PoachedFromNational += kv.Value;
        }
        Marks.Add(m);
    }

    public static void Print(IReadOnlyList<YearMarks> seeds)
    {
        if (seeds.Count == 0) return;
        int ys = seeds.Max(s => s.Marks.Count);
        var at = Enumerable.Range(1, ys).Where(y => y % 5 == 0 || y == ys).ToList();
        Console.WriteLine();
        Console.WriteLine("  --- BY YEAR (year end; tiers SAMPLED at year end; bankruptcies/acquisitions DURABLE, cumulative since day one; poaching per seed-year inside the window since the previous mark; purses at year end) ---");
        Console.WriteLine($"    {"year",4} | {"Intl",5} {"Cont",5} {"Natl",5} {"Reg",5} {"City",5} | {"bankr",5} {"acq",4} {"founded",7} {"alive",5} | {"AFC bank med",12} {"Cont bank med",13} {"Cont p10",8} | {"poach C",7} {"C->AFC",6} {"poach N",7} | {"Intl purse mean/med",19} | buyers");
        int prev = 0;
        foreach (var y in at)
        {
            var ms = seeds.Where(s => s.Marks.Count >= y).Select(s => s.Marks[y - 1]).ToList();
            var before = prev == 0 ? null : seeds.Where(s => s.Marks.Count >= y).Select(s => s.Marks[prev - 1]).ToList();
            double win = Math.Max(1, (y - prev)) * ms.Count;
            double T(OrgSize t) => ms.Average(m => m.Tiers.GetValueOrDefault(t));
            int Poach(Func<Mark, int> f) => ms.Sum(f) - (before?.Sum(f) ?? 0);
            var cont = ms.SelectMany(m => m.ContinentalBanks).ToList();
            var purses = ms.SelectMany(m => m.IntlPurses).ToList();
            var buyers = ms.SelectMany(m => m.Buyers).GroupBy(kv => kv.Key).Select(g => (g.Key, N: g.Sum(kv => kv.Value)))
                .OrderByDescending(x => x.N).ThenBy(x => x.Key, StringComparer.Ordinal).Take(5);
            Console.WriteLine($"    {"Y" + y,4} | {T(OrgSize.International),5:0.00} {T(OrgSize.Continental),5:0.00} {T(OrgSize.National),5:0.00} {T(OrgSize.Regional),5:0.00} {T(OrgSize.City),5:0.00} | "
                + $"{ms.Sum(m => m.Bankruptcies),5} {ms.Sum(m => m.Acquisitions),4} {ms.Sum(m => m.Founded),7} {ms.Sum(m => m.FoundedAlive),5} | "
                + $"{Pct(ms.Select(m => m.AfcBank).ToList(), 0.5) / 1_000_000m,12:0.0} {Pct(cont, 0.5) / 1_000_000m,13:0.0} {Pct(cont, 0.1) / 1_000_000m,8:0.0} | "
                + $"{Poach(m => m.PoachedFromContinental) / win,7:0.0} {Poach(m => m.PoachedFromContinentalToAfc) / win,6:0.0} {Poach(m => m.PoachedFromNational) / win,7:0.0} | "
                + $"{(purses.Count == 0 ? 0 : purses.Average()),9:N0}/{Pct(purses, 0.5),9:N0} | {string.Join(", ", buyers.Select(b => $"{b.Key} x{b.N}"))}");
            prev = y;
        }
    }

    private static decimal Pct(List<decimal> xs, double q)
    {
        if (xs.Count == 0) return 0m;
        var v = xs.OrderBy(x => x).ToList();
        return v[Math.Clamp((int)Math.Round(q * (v.Count - 1)), 0, v.Count - 1)];
    }
}
