using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Org;

namespace LongSim;

/// <summary>
/// Where the names go and what the sold houses looked like (compare's TALENT FLOW block). Weekly: every active pro with
/// popularity 50+ whose contract house changed this week, by how he came (from free agency, or straight from another
/// house: a poach, a buyout or an acquisition's roster) and the tier of the house he joined (tier SAMPLED the week
/// before). Every 4 weeks: each live National house's bank, roster size, mean roster popularity and mean true current
/// ability (reporting only; never fed back). At the end, for every house bought while National (DURABLE: the buyer's
/// "Acquisition of" ledger line), those samples 24, 12 and 0-3 months before the sale, against all National houses'
/// samples over the run. Observes only; no draws.
/// </summary>
public sealed class TalentFlow
{
    public readonly Dictionary<(string How, string Tier), int> Moves = new();
    public readonly Dictionary<(string How, string Tier), double> MovePop = new();
    private Dictionary<FighterId, PromotionId?> _house = new();
    private Dictionary<PromotionId, OrgSize> _tier = new();
    private int _week;

    private readonly record struct Sample(DateOnly On, decimal Bank, int Roster, double Pop, double Ca);
    private readonly Dictionary<PromotionId, List<Sample>> _national = new();

    /// <summary>Per window ("24m", "12m", "0-3m", "all National"): n, bank sum ($M), roster sum, pop sum, CA sum.</summary>
    public readonly Dictionary<string, (int N, double Bank, double Roster, double Pop, double Ca)> Sold = new();

    public void BeforeWeek(World world)
    {
        _tier = world.Promotions.Values.Where(p => !p.Defunct).ToDictionary(p => p.Id, OrgSizing.SizeOf);
        _house = new();
        foreach (var f in world.Fighters.Values)
            if (f.Status != FighterStatus.Retired && !f.LocalTalent && f.Intangibles.Popularity.Value >= 50)
                _house[f.Id] = f.Contract?.Promotion;
    }

    public void AfterWeek(World world)
    {
        foreach (var (id, before) in _house)
        {
            if (world.Get(id) is not { } f || f.Contract?.Promotion is not { } now || now == before) continue;
            string how = before is null ? "from FA" : "from a house";
            string tier = world.Get(now) is { Abbreviation: "AFC" } ? "AFC"
                : _tier.TryGetValue(now, out var t) ? OrgSizing.Label(t) : "new house";
            Moves[(how, tier)] = Moves.GetValueOrDefault((how, tier)) + 1;
            MovePop[(how, tier)] = MovePop.GetValueOrDefault((how, tier)) + f.Intangibles.Popularity.Value;
        }
        if (++_week % 4 != 0) return;
        foreach (var p in world.Promotions.Values)
        {
            if (p.Defunct || OrgSizing.SizeOf(p) != OrgSize.National) continue;
            var roster = world.RosterOf(p.Id).Where(f => !f.LocalTalent).ToList();
            if (!_national.TryGetValue(p.Id, out var l)) _national[p.Id] = l = new();
            l.Add(new Sample(world.CurrentDate, p.Balance.Amount, roster.Count,
                roster.Count == 0 ? 0 : roster.Average(f => (double)f.Intangibles.Popularity.Value),
                roster.Count == 0 ? 0 : roster.Average(f => (double)FighterAbility.CurrentAbility(f))));
        }
    }

    private void Add(string key, Sample s)
    {
        var c = Sold.GetValueOrDefault(key);
        Sold[key] = (c.N + 1, c.Bank + (double)s.Bank / 1e6, c.Roster + s.Roster, c.Pop + s.Pop, c.Ca + s.Ca);
    }

    public void End(World world)
    {
        foreach (var l in _national.Values) foreach (var s in l) Add("all National", s);
        var soldOn = new Dictionary<string, DateOnly>(StringComparer.Ordinal);
        foreach (var p in world.Promotions.Values.OrderBy(p => p.Id.Value))
            foreach (var e in p.Ledger)
                if (e.Category == FinanceCategory.Acquisition && e.Amount.Amount < 0 && e.Description.StartsWith("Acquisition of ", StringComparison.Ordinal))
                    soldOn.TryAdd(e.Description["Acquisition of ".Length..], e.Date);
        foreach (var p in world.Promotions.Values.OrderBy(p => p.Id.Value))
        {
            if (!soldOn.TryGetValue(p.Name, out var on) || !_national.TryGetValue(p.Id, out var l) || l.Count == 0) continue;
            // bought while National: its last sample within 3 months of the sale
            var last = l[^1];
            if (on.DayNumber - last.On.DayNumber > 91) continue;
            Add("0-3m", last);
            foreach (var (key, days) in new[] { ("12m", 365), ("24m", 730) })
            {
                var near = l.Where(s => Math.Abs(on.DayNumber - days - s.On.DayNumber) <= 31).ToList();
                if (near.Count > 0) Add(key, near[0]);
            }
        }
    }

    public static void Print(List<TalentFlow> runs)
    {
        if (runs.Count == 0) return;
        Console.WriteLine();
        Console.WriteLine("  --- TALENT FLOW (weekly: pros with pop 50+ changing house, by how and by the joining house's tier the week before) ---");
        foreach (var how in new[] { "from FA", "from a house" })
        {
            int tot = runs.Sum(r => r.Moves.Where(kv => kv.Key.How == how).Sum(kv => kv.Value));
            var parts = new List<string>();
            foreach (var t in new[] { "AFC", "International", "Continental", "National", "Regional", "City", "new house" })
            {
                int n = runs.Sum(r => r.Moves.GetValueOrDefault((how, t)));
                if (n == 0) continue;
                double pop = runs.Sum(r => r.MovePop.GetValueOrDefault((how, t))) / n;
                parts.Add($"{t} {n} ({100.0 * n / Math.Max(1, tot):0.0}%, pop {pop:0.0})");
            }
            Console.WriteLine($"  {how,-13} total {tot,6}: {string.Join(" · ", parts)}");
        }
        Console.WriteLine("  National houses later bought (samples near 24m / 12m / 0-3m before the sale) vs all National samples: n · bank $M · roster · mean pop · mean true CA");
        foreach (var key in new[] { "24m", "12m", "0-3m", "all National" })
        {
            var (n, bank, roster, pop, ca) = (runs.Sum(r => r.Sold.GetValueOrDefault(key).N), runs.Sum(r => r.Sold.GetValueOrDefault(key).Bank),
                runs.Sum(r => r.Sold.GetValueOrDefault(key).Roster), runs.Sum(r => r.Sold.GetValueOrDefault(key).Pop), runs.Sum(r => r.Sold.GetValueOrDefault(key).Ca));
            if (n == 0) { Console.WriteLine($"    {key,-12} none"); continue; }
            Console.WriteLine($"    {key,-12} n {n,6} · bank {bank / n,7:0.00} · roster {roster / n,5:0.0} · pop {pop / n,5:0.0} · CA {ca / n,5:0.0}");
        }
    }
}
