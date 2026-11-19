using MmaManager.Core.Model;
using MmaManager.Core.Sim.Contracts;
using MmaManager.Core.Sim.Living;

namespace LongSim;

/// <summary>
/// ORIGINS STAFFING (AI runs): how full the AI houses' rosters are against what they want, where their men come from
/// and where they go, why the monthly free-agent pass and the expiry pass fail to sign or keep a man, and how many
/// nights run short or never go on the books. By era and by house size (the house's purse cap that week).
/// Read-only: the sim's own counts arrive through <see cref="TickProbe"/> ("staff|..." keys, Origins only), the rest is
/// a weekly diff of who is under contract where. Never a failure.
/// </summary>
public static partial class OriginsMode
{
    /// <summary>Collects the sim's "staff|kind|promo|reason" counts between weeks.</summary>
    private sealed class StaffProbe : ITickProbe
    {
        public readonly Dictionary<string, int> Counts = new(StringComparer.Ordinal);
        public void Enter(string phase) { }
        public void Exit(string phase) { }
        public void Count(string helper)
        {
            if (helper.StartsWith("staff|", StringComparison.Ordinal)) Counts[helper] = Counts.GetValueOrDefault(helper) + 1;
        }
    }

    private sealed class StaffStats
    {
        public long HouseWeeks, Wanted, Held, Bookable, UnderTwoThirds, Under, Distressed;
        public decimal CapSum;
        public readonly Dictionary<string, long> Flow = new(StringComparer.Ordinal);   // join:..., leave:..., fa:..., exp:..., tryout:..., show:..., night:...
        public readonly List<decimal> SignPurse = new(), RenewPurse = new(), TryoutPurse = new(), MovePurse = new();
        public void Add(string key, long n = 1) => Flow[key] = Flow.GetValueOrDefault(key) + n;
        public long Get(string key) => Flow.GetValueOrDefault(key);
    }

    /// <summary>One AI run's staffing record, by (era, size) and (era, kind).</summary>
    private sealed class Staffing
    {
        public readonly Dictionary<(string Era, string Size), StaffStats> BySize = new();
        public readonly Dictionary<(string Era, string Kind), StaffStats> ByKind = new();
        public readonly StaffProbe Probe = new();
        public Dictionary<FighterId, (int Promo, DateOnly Signed, int Left, bool Out)> Last = new();
        public HashSet<FighterId> Known = new();
        public readonly HashSet<(int Promo, int Week)> NoFieldWeeks = new();
        public int Week;
    }

    public static readonly string[] StaffEras = { "1993-1999", "2000-2005", "2006-2009", "2010+" };
    public static readonly string[] StaffSizes = { "cap<=750", "cap 1000-1500", "cap 1750-2750", "cap 3000+" };

    private static string EraOf(int year) => year <= 1999 ? StaffEras[0] : year <= 2005 ? StaffEras[1] : year <= 2009 ? StaffEras[2] : StaffEras[3];
    private static string SizeOf(decimal cap) => cap <= 750m ? StaffSizes[0] : cap <= 1500m ? StaffSizes[1] : cap <= 2750m ? StaffSizes[2] : StaffSizes[3];

    /// <summary>The week's staffing record (after the tick). <paramref name="kindOf"/> names a house's kind; <paramref name="foundedThisWeek"/> the houses first seen this week.</summary>
    private static void StaffWeek(World world, Staffing st, Func<Promotion, string> kindOf, HashSet<PromotionId> foundedThisWeek)
    {
        st.Week++;
        string era = EraOf(world.CurrentDate.Year);
        var houses = world.Promotions.Values.Where(p => p.Id != world.PlayerPromotion).OrderBy(p => p.Id.Value).ToList();
        var cap = houses.ToDictionary(p => p.Id.Value, p => Negotiator.PurseCap(world, p));
        StaffStats S(Promotion p) => Bucket(st.BySize, (era, SizeOf(cap[p.Id.Value])));
        StaffStats K(Promotion p) => Bucket(st.ByKind, (era, kindOf(p)));
        var byId = houses.ToDictionary(p => p.Id.Value);

        // The roster against what it wants (live houses).
        foreach (var p in houses.Where(p => !p.Defunct))
        {
            int wanted = OriginsShows.RosterDemand(p);
            int held = world.RosterOf(p.Id).Count(f => f.Status != FighterStatus.Retired && !DealWindow.OutOfDeal(world, f));   // (OriginsFounding.OnTheBooks)
            int bookable = world.RosterOf(p.Id).Count(f => OriginsShows.CanBook(world, p, f, world.CurrentDate.AddDays(7 * OriginsShows.LeadWeeks)));   // (OriginsFounding.NightReady)
            bool distressed = MmaManager.Core.Sim.Org.Solvency.Distress(p, world) > 0;
            foreach (var s in new[] { S(p), K(p) })
            {
                s.HouseWeeks++; s.Wanted += wanted; s.Held += held; s.Bookable += bookable; s.CapSum += cap[p.Id.Value];
                if (held < wanted) s.Under++;
                if (held * 3 < wanted * 2) s.UnderTwoThirds++;
                if (distressed) s.Distressed++;
            }
        }

        // The sim's own counts this week.
        foreach (var (key, n) in st.Probe.Counts.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var parts = key.Split('|');
            if (parts.Length < 4 || !int.TryParse(parts[2], out int pid) || !byId.TryGetValue(pid, out var p)) continue;
            if (parts[1] == "show")
            {
                if (st.NoFieldWeeks.Add((pid, st.Week))) { S(p).Add("show:no-field-week"); K(p).Add("show:no-field-week"); }
                continue;
            }
            S(p).Add(parts[1] + ":" + parts[3], n);
            K(p).Add(parts[1] + ":" + parts[3], n);
        }
        st.Probe.Counts.Clear();

        // Nights this week.
        var weekAgo = world.CurrentDate.AddDays(-7);
        foreach (var e in world.Events.Values.Where(e => e.Date > weekAgo && e.Date <= world.CurrentDate).OrderBy(e => e.Id.Value))
        {
            if (!byId.TryGetValue(e.Promotion.Value, out var p)) continue;
            string k = e.Status == EventStatus.Cancelled ? "night:cancelled"
                : e.Status != EventStatus.Completed ? ""
                : e.Format == EventFormat.Tournament ? "night:bracket"
                : e.Card.Count < OriginsShows.CardBouts(p) ? "night:card-short" : "night:card-full";
            if (k.Length == 0) continue;
            S(p).Add(k); K(p).Add(k);
            if (k == "night:card-short") { S(p).Add("night:bouts-missing", OriginsShows.CardBouts(p) - e.Card.Count); K(p).Add("night:bouts-missing", OriginsShows.CardBouts(p) - e.Card.Count); }
        }

        // Who joined and who left (a weekly diff of who is under contract where).
        var now = new Dictionary<FighterId, (int Promo, DateOnly Signed, int Left, bool Out)>();
        foreach (var f in world.Fighters.Values)
            if (f.Status != FighterStatus.Retired && f.Contract is { } c && byId.ContainsKey(c.Promotion.Value))
                now[f.Id] = (c.Promotion.Value, c.SignedOn, c.FightsRemaining, DealWindow.OutOfDeal(world, f));
        foreach (var (id, cur) in now.OrderBy(kv => kv.Key.Value))
        {
            var p = byId[cur.Promo];
            var f = world.Get(id)!;
            decimal purse = f.Contract!.PursePerFight.Amount;
            if (st.Last.TryGetValue(id, out var was) && was.Promo == cur.Promo)
            {
                if (was.Signed != cur.Signed) { S(p).RenewPurse.Add(purse); K(p).Add("join:renewal-seen"); }
                continue;
            }
            string src;
            if (foundedThisWeek.Contains(p.Id)) src = "founding";
            else if (f.Accolades.Any(a => a.Date > weekAgo && a.Title == $"Signed at {p.Name}'s open tryout")) src = "tryout";
            else if (st.Last.TryGetValue(id, out var from))
            {
                src = "from-house";
                var q = byId[from.Promo];
                string dir = cap[cur.Promo] > cap[from.Promo] ? "moved-up" : "moved-down-or-level";
                S(q).Add("leave:" + dir); K(q).Add("leave:" + dir);
                S(p).MovePurse.Add(purse);
            }
            else if (st.Known.Contains(id)) src = "free-agent";
            else src = "new-other";
            S(p).Add("join:" + src); K(p).Add("join:" + src);
            if (src == "free-agent") S(p).SignPurse.Add(purse);
            if (src == "tryout") S(p).TryoutPurse.Add(purse);
        }
        foreach (var (id, was) in st.Last.OrderBy(kv => kv.Key.Value))
        {
            if (now.TryGetValue(id, out var cur) && cur.Promo == was.Promo) continue;
            if (now.ContainsKey(id)) continue;   // (moved: counted above, on the house he left)
            var p = byId[was.Promo];
            if (p.Defunct) continue;
            var f = world.Get(id);
            string why = f is null || f.Status == FighterStatus.Retired ? "retired"
                : was.Left > 0 && !was.Out ? "released" : "deal-ran-out";
            S(p).Add("leave:" + why); K(p).Add("leave:" + why);
        }
        st.Last = now;
        foreach (var id in world.Fighters.Keys) st.Known.Add(id);
    }

    private static StaffStats Bucket<TKey>(Dictionary<TKey, StaffStats> d, TKey key) where TKey : notnull
    {
        if (!d.TryGetValue(key, out var s)) d[key] = s = new StaffStats();
        return s;
    }

    /// <summary>The cross-seed ORIGINS STAFFING block (AI runs).</summary>
    private static void StaffingReport(List<Outcome> ai)
    {
        var runs = ai.Where(o => o.Staff is not null).Select(o => o.Staff!).ToList();
        if (runs.Count == 0) return;
        Console.WriteLine($"===== ORIGINS STAFFING ({runs.Count} seeds, AI runs; by era and by the house's purse cap that week; per house-year unless said) =====");
        static string Med(List<decimal> v)
        {
            if (v.Count == 0) return "-";
            var s = v.OrderBy(x => x).ToList();
            return $"{s[s.Count / 4]:0}/{s[s.Count / 2]:0}/{s[s.Count * 3 / 4]:0} (n {s.Count})";
        }
        void Rows<TKey>(string title, IEnumerable<TKey> keys, Func<Staffing, Dictionary<TKey, StaffStats>> pick, bool detail) where TKey : notnull
        {
            Console.WriteLine($"--- {title} ---");
            foreach (var key in keys)
            {
                var all = new StaffStats();
                foreach (var r in runs)
                {
                    if (!pick(r).TryGetValue(key, out var s)) continue;
                    all.HouseWeeks += s.HouseWeeks; all.Wanted += s.Wanted; all.Held += s.Held; all.Bookable += s.Bookable;
                    all.UnderTwoThirds += s.UnderTwoThirds; all.Under += s.Under; all.Distressed += s.Distressed; all.CapSum += s.CapSum;
                    foreach (var (k, n) in s.Flow) all.Add(k, n);
                    all.SignPurse.AddRange(s.SignPurse); all.RenewPurse.AddRange(s.RenewPurse); all.TryoutPurse.AddRange(s.TryoutPurse); all.MovePurse.AddRange(s.MovePurse);
                }
                if (all.HouseWeeks == 0) { Console.WriteLine($"{key}: none"); continue; }
                double hy = all.HouseWeeks / 52.0;
                string Y(string k) => $"{all.Get(k) / hy:0.0}";
                double hw = all.HouseWeeks;
                Console.WriteLine($"{key}: {hy:0} house-years ({hy / runs.Count:0.0}/world); wanted {all.Wanted / hw:0.0} held {all.Held / hw:0.0} ({100.0 * all.Held / Math.Max(1, all.Wanted):0}%) " +
                                  $"fit for the next night {all.Bookable / hw:0.0}; weeks under wanted {100.0 * all.Under / hw:0}%, under 2/3 {100.0 * all.UnderTwoThirds / hw:0}%, distressed {100.0 * all.Distressed / hw:0}%; cap {all.CapSum / (decimal)hw:0}");
                Console.WriteLine($"   in: tryout {Y("join:tryout")}, free agent {Y("join:free-agent")}, founding {Y("join:founding")}, from a house {Y("join:from-house")}, other new {Y("join:new-other")}; " +
                                  $"out: retired {Y("leave:retired")}, moved up {Y("leave:moved-up")}, moved down/level {Y("leave:moved-down-or-level")}, released {Y("leave:released")}, deal ran out {Y("leave:deal-ran-out")}");
                if (!detail) continue;
                Console.WriteLine($"   FA pass: short divisions {Y("fa:div-short")}; signed {Y("fa:signed")}, lodged {Y("fa:lodged")}; refused at the cap {Y("fa:refused-cap")}, refused other {Y("fa:refused-other")}, " +
                                  $"over the distress budget {Y("fa:distress-budget")} (budget under the 500 floor {Y("fa:distress-under-floor")}), not wanted {Y("fa:not-wanted")}, raw gate {Y("fa:raw-gate")}, " +
                                  $"taken by a house ahead {Y("fa:taken")}, bidding war {Y("fa:war")}, offer held {Y("fa:offer-held")}; seats left empty: no free men in the division {Y("fa:missing-no-men")}, tried the pool {Y("fa:missing-pool-tried")}");
                Console.WriteLine($"   expiries: renewed {Y("exp:renewed")}, champion kept {Y("exp:champion-kept")}, lifer kept {Y("exp:lifer-kept")}; walked: refused at the cap {Y("exp:refused-cap")}, refused other {Y("exp:refused-other")}, " +
                                  $"not wanted {Y("exp:not-wanted")}, trimmed {Y("exp:trimmed")}, distress {Y("exp:distress-walked")}, lifer {Y("exp:lifer-walked")}; poached {Y("exp:poached")}, retired at the deal's end {Y("exp:sunset")}");
                Console.WriteLine($"   tryouts held {Y("tryout:held")}, short but spacing blocked (months) {Y("tryout:short-spacing-blocked")}, under target but not short (months) {Y("tryout:under-target-not-short")}; " +
                                  $"nights: card full {Y("night:card-full")}, card short {Y("night:card-short")} ({Y("night:bouts-missing")} bouts missing), bracket {Y("night:bracket")}, cancelled {Y("night:cancelled")}, weeks a night could not be announced {Y("show:no-field-week")}");
                Console.WriteLine($"   purses p25/median/p75: free-agent signing {Med(all.SignPurse)}, renewal {Med(all.RenewPurse)}, from a house {Med(all.MovePurse)}, tryout {Med(all.TryoutPurse)}");
            }
        }
        Rows("by era and house size", StaffEras.SelectMany(e => StaffSizes.Select(s => (e, s))), r => r.BySize, detail: true);
        Rows("by era and kind of house", StaffEras.SelectMany(e => new[] { "founder", "authored", "founded" }.Select(k => (e, k))), r => r.ByKind, detail: false);
    }
}
