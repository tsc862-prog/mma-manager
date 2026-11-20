using MmaManager.Core.Model;
using MmaManager.Core.Sim.Geography;
using MmaManager.Core.Sim.Org;

namespace LongSim;

/// <summary>
/// Where fighters are known (compare's FAME SPREAD block). At the end of the run, for active pros by popularity band
/// (70+, 50-69): how many cities know them at all / at "a few fans" (10+) / "known" (25+) / "popular" (45+), how many
/// regions (market-weighted, Reputation.FighterInRegion) at 5+ and 10+; fame at home, in the cities they have fought in
/// (DURABLE: bout history -> event -> host city), and in cities they have never fought in nor live in, split home
/// region (where in-person fights echo) and abroad (only television and belts reach there). The size of every
/// fighter's city-fame dictionary, retired men included. Per week, for cards that ran: the city-fame term of the
/// box-office draw (host-city fame / 4, +12 hometown) of the better-drawing main-event corner, read the week BEFORE
/// the card (so it is what the gate saw), by the house's tier then. And the small houses' home pull: for live City
/// and Regional orgs at the end, the roster share from the house's HQ city and their mean fame there.
/// Observes only; no draws.
/// </summary>
public sealed class FameSpread
{
    public sealed class Band
    {
        public int N;
        public double Known, Fans, Knownish, Popular, Regions5, Regions10, Home, Fought, UnfoughtHomeRegion, UnfoughtAbroad;
        public int AbroadMax, AbroadAt20;
        public int AbroadCities;
    }

    public readonly Band Stars = new(), Names = new();
    public readonly List<int> DictSizes = new();
    public readonly Dictionary<string, (int Cards, double Local, int Hometown)> DrawBy = new();
    public int SmallRoster, SmallFromHq; public double SmallHqFame;

    private readonly Dictionary<EventId, (string Tier, double Local, bool Home)> _pending = new();

    public void BeforeWeek(World world)
    {
        _pending.Clear();
        var horizon = world.CurrentDate.AddDays(8);
        foreach (var ev in world.Events.Values)
        {
            if (ev.Status != EventStatus.Scheduled || ev.Date > horizon || ev.Card.Count == 0) continue;
            if (world.Get(ev.Promotion) is not { } p || p.Defunct) continue;
            var host = WorldMap.CityOf(world, ev);
            var main = ev.Card[^1];
            double best = 0; bool home = false;
            foreach (var id in new[] { main.RedCorner, main.BlueCorner })
            {
                if (world.Get(id) is not { } f || host is not { } h) continue;
                bool hm = f.HomeCity == h;
                double term = Reputation.FighterInCity(f, h) / 4 + (hm ? 12 : 0);
                if (term > best) { best = term; home = hm; }
            }
            _pending[ev.Id] = (p.Abbreviation == "AFC" ? "AFC" : OrgSizing.Label(OrgSizing.SizeOf(p)), best, home);
        }
    }

    public void AfterWeek(World world)
    {
        foreach (var (id, row) in _pending)
        {
            if (world.Get(id) is not { Status: EventStatus.Completed } ev || ev.Financials is null) continue;
            var cur = DrawBy.GetValueOrDefault(row.Tier);
            DrawBy[row.Tier] = (cur.Cards + 1, cur.Local + row.Local, cur.Hometown + (row.Home ? 1 : 0));
        }
    }

    public void End(World world)
    {
        // fight -> host city, once
        var hostOf = new Dictionary<FightId, CityId>();
        foreach (var ev in world.Events.Values)
        {
            if (ev.Status != EventStatus.Completed || WorldMap.CityOf(world, ev) is not { } h) continue;
            foreach (var f in ev.Card) hostOf[f.Id] = h;
        }
        var cities = world.Cities.Values.OrderBy(c => c.Id.Value).ToList();
        foreach (var f in world.Fighters.Values)
        {
            DictSizes.Add(f.CityReputation.Count);
            if (f.Status == FighterStatus.Retired || f.LocalTalent) continue;
            int pop = f.Intangibles.Popularity.Value;
            var band = pop >= 70 ? Stars : pop >= 50 ? Names : null;
            if (band is null) continue;
            var fought = f.BoutHistory.Where(hostOf.ContainsKey).Select(b => hostOf[b]).ToHashSet();
            var homeRegion = f.HomeCity is { } hc && world.Get(hc) is { } hcity ? hcity.Region : (Region?)null;
            band.N++;
            int known = 0, fans = 0, kn = 0, popu = 0;
            double foughtSum = 0; int foughtN = 0;
            double uhr = 0; int uhrN = 0; double ua = 0; int uaN = 0;
            foreach (var c in cities)
            {
                int v = Reputation.FighterInCity(f, c.Id);
                if (v > 0) known++;
                if (v >= 10) fans++;
                if (v >= 25) kn++;
                if (v >= 45) popu++;
                if (fought.Contains(c.Id)) { foughtSum += v; foughtN++; }
                else if (c.Id != f.HomeCity)
                {
                    if (c.Region == homeRegion) { uhr += v; uhrN++; }
                    else
                    {
                        ua += v; uaN++;
                        band.AbroadMax = Math.Max(band.AbroadMax, v);
                        if (v >= 20) band.AbroadAt20++;
                        if (v > 0) band.AbroadCities++;
                    }
                }
            }
            band.Known += known; band.Fans += fans; band.Knownish += kn; band.Popular += popu;
            band.Regions5 += Enum.GetValues<Region>().Count(r => Reputation.FighterInRegion(world, f, r) >= 5);
            band.Regions10 += Enum.GetValues<Region>().Count(r => Reputation.FighterInRegion(world, f, r) >= 10);
            band.Home += f.HomeCity is { } h2 ? Reputation.FighterInCity(f, h2) : 0;
            band.Fought += foughtN == 0 ? 0 : foughtSum / foughtN;
            band.UnfoughtHomeRegion += uhrN == 0 ? 0 : uhr / uhrN;
            band.UnfoughtAbroad += uaN == 0 ? 0 : ua / uaN;
        }
        foreach (var p in world.Promotions.Values)
        {
            if (p.Defunct || OrgSizing.SizeOf(p) > OrgSize.Regional || p.HomeCity is not { } hq) continue;
            foreach (var f in world.RosterOf(p.Id))
            {
                SmallRoster++;
                if (f.HomeCity == hq) SmallFromHq++;
                SmallHqFame += Reputation.FighterInCity(f, hq);
            }
        }
    }

    public static void Print(List<FameSpread> runs)
    {
        if (runs.Count == 0) return;
        Console.WriteLine();
        Console.WriteLine("  --- FAME SPREAD (end of run; active pros by popularity; city fame 0-100; DURABLE: CityReputation, bout history) ---");
        foreach (var (label, pick) in new (string, Func<FameSpread, Band>)[] { ("pop 70+", r => r.Stars), ("pop 50-69", r => r.Names) })
        {
            int n = runs.Sum(r => pick(r).N);
            if (n == 0) { Console.WriteLine($"  {label}: none"); continue; }
            double A(Func<Band, double> g) => runs.Sum(r => g(pick(r))) / n;
            Console.WriteLine($"  {label,-9} n={n,5} · cities known {A(b => b.Known),5:0.0} · 10+ {A(b => b.Fans),5:0.0} · 25+ {A(b => b.Knownish),5:0.0} · 45+ {A(b => b.Popular),5:0.0} · regions 5+ {A(b => b.Regions5),4:0.00} 10+ {A(b => b.Regions10),4:0.00}");
            Console.WriteLine($"  {"",-9}         fame: home {A(b => b.Home),5:0.0} · fought-in cities {A(b => b.Fought),5:0.0} · never-fought home region {A(b => b.UnfoughtHomeRegion),5:0.0} · never-fought abroad {A(b => b.UnfoughtAbroad),5:0.0} (abroad cities known {A(b => b.AbroadCities),4:0.0}/fighter, at 20+ {A(b => b.AbroadAt20),4:0.0}/fighter, max {runs.Max(r => pick(r).AbroadMax)})");
        }
        var sizes = runs.SelectMany(r => r.DictSizes).OrderBy(x => x).ToList();
        if (sizes.Count > 0)
            Console.WriteLine($"  CityReputation entries per fighter (all fighters incl. retired): mean {sizes.Average():0.0} · p50 {sizes[sizes.Count / 2]} · p90 {sizes[(int)(sizes.Count * 0.9)]} · max {sizes[^1]} · total/seed {sizes.Sum() / (double)runs.Count:N0} over {sizes.Count / (double)runs.Count:N0} fighters/seed");
        Console.WriteLine("  main-event city-fame draw term (host fame/4 + 12 hometown, the better corner, read the week before the card), by house tier then:");
        foreach (var t in new[] { "AFC", "International", "Continental", "National", "Regional", "City", "Local" })
        {
            int c = runs.Sum(r => r.DrawBy.GetValueOrDefault(t).Cards);
            if (c == 0) continue;
            Console.WriteLine($"    {t,-14} cards {c,7} · mean term {runs.Sum(r => r.DrawBy.GetValueOrDefault(t).Local) / c,5:0.00} · hometown headliner {100.0 * runs.Sum(r => r.DrawBy.GetValueOrDefault(t).Hometown) / c,5:0.0}%");
        }
        int sr = runs.Sum(r => r.SmallRoster);
        if (sr > 0)
            Console.WriteLine($"  small houses (City/Regional, live at end): roster {sr} · from the HQ city {100.0 * runs.Sum(r => r.SmallFromHq) / sr:0.0}% · mean HQ-city fame {runs.Sum(r => r.SmallHqFame) / sr:0.0}");
    }
}
