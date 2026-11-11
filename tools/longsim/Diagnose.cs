using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Finance;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Org;

namespace LongSim;

/// <summary>
/// `diagnose &lt;seed&gt; &lt;years&gt; ABBR [ABBR ...]`: a forensic trace of named orgs.
/// Weekly: media deals signed, struck (with the clauses missed) and dropped
/// (with the network's reason), every acquisition and every National+ death in
/// the world. Per card: tier, room vs house, gate, PPV, purses. Quarterly: the
/// full ledger by line, roster, committed purses, marketing budget, distress.
/// Read-only: it only observes the world the normal tick produces.
/// </summary>
public static class Diagnose
{
    public static void Detailed(ulong seed, int years, string[] abbrevs)
    {
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });
        world.PlayerPromotion = default;
        var tick = new WorldTick();
        var watch = abbrevs
            .Select(a => world.Promotions.Values.FirstOrDefault(p => p.Abbreviation == a))
            .Where(p => p is not null).Select(p => p!).ToList();

        Console.WriteLine($"=== DIAGNOSIS (detailed) — seed {seed}, {years} years: {string.Join(", ", watch.Select(p => p.Abbreviation))} ===");
        foreach (var p in watch)
        {
            Console.WriteLine($"{p.Abbreviation} day one: prestige {p.Prestige.Value} {OrgSizing.Label(OrgSizing.SizeOf(p))} · bank {p.Balance} · roster {world.RosterOf(p.Id).Count()} · " +
                $"committed purses {world.RosterOf(p.Id).Sum(f => f.Contract!.PursePerFight.Amount):C0} · marketing {p.MarketingBudget} · ceiling {Solvency.DebtCeilingFor(p):C0}");
            foreach (var d in p.MediaDeals) Console.WriteLine("    media " + Deal(d));
            if (p.Stream is { } s) Console.WriteLine($"    stream {s.Name} {s.Subscribers:N0} subs");
        }
        Console.WriteLine();

        var mediaSnap = watch.ToDictionary(p => p.Id, p => Snap(p));
        var seenEvents = new HashSet<EventId>(world.Events.Values.Where(e => e.Financials is not null).Select(e => e.Id));
        var ledgerAt = watch.ToDictionary(p => p.Id, p => p.Ledger.Count);
        var allLedgerAt = world.Promotions.Values.ToDictionary(p => p.Id, p => p.Ledger.Count);
        var defunct = world.Promotions.Values.Where(p => p.Defunct).Select(p => p.Id).ToHashSet();
        var tierOf = world.Promotions.Values.ToDictionary(p => p.Id, p => OrgSizing.SizeOf(p));
        NewsItem? lastNews = world.News.Count > 0 ? world.News[^1] : null;

        for (int week = 1; week <= years * 52; week++)
        {
            tick.AdvanceWeeks(world, 1);
            var date = world.CurrentDate;

            // News since last week (the cap is 1000; a week never trims past our marker in practice).
            var fresh = new List<NewsItem>();
            int from = 0;
            if (lastNews is not null)
                for (int i = world.News.Count - 1; i >= 0; i--)
                    if (ReferenceEquals(world.News[i], lastNews)) { from = i + 1; break; }
            for (int i = from; i < world.News.Count; i++) fresh.Add(world.News[i]);
            if (world.News.Count > 0) lastNews = world.News[^1];

            // Cards that settled this week.
            foreach (var ev in world.Events.Values.Where(e => e.Financials is not null && !seenEvents.Contains(e.Id))
                         .OrderBy(e => e.Date).ThenBy(e => e.Id.Value).ToList())
            {
                seenEvents.Add(ev.Id);
                if (watch.All(p => p.Id != ev.Promotion)) continue;
                var fin = ev.Financials!;
                int cap = ev.VenueId is { } vid ? world.Get(vid)?.Capacity ?? 0 : 0;
                var main = ev.Card.Count > 0 ? ev.Card[^1] : null;
                int popA = main is null ? 0 : world.Get(main.RedCorner)?.Intangibles.Popularity.Value ?? 0;
                int popB = main is null ? 0 : world.Get(main.BlueCorner)?.Intangibles.Popularity.Value ?? 0;
                double rating = ev.Card.Where(f => f.Outcome is not null).Select(f => (double)MmaManager.Core.Sim.Flavor.MatchRating.Of(f)).DefaultIfEmpty(0).Average();
                Console.WriteLine($"  [{ev.Date:yyyy-MM-dd}] {world.Get(ev.Promotion)!.Abbreviation} card {ev.Tier,-10} room {cap,6:N0} house {fin.Attendance,6:N0} " +
                    $"gate {fin.GateRevenue.Amount,11:C0} ppv {fin.PayPerViewRevenue.Amount,11:C0} purses {fin.FighterPayroll.Amount,11:C0} prod {fin.ProductionCost.Amount,9:C0} · " +
                    $"{ev.Card.Count} bouts, title {(ev.Card.Any(f => f.IsTitleFight) ? "Y" : "n")}, main pops {popA}/{popB}, avg rating {rating:0}");
            }

            // Media: signings, strikes (with the clauses missed), drops (with why).
            foreach (var p in watch)
            {
                var now = Snap(p);
                var before = mediaSnap[p.Id];
                foreach (var d in p.MediaDeals)
                {
                    var old = before.FirstOrDefault(b => b.Partner == d.Partner && b.Kind == d.Kind);
                    if (old.Partner is null)
                        Console.WriteLine($"  [{date:yyyy-MM-dd}] {p.Abbreviation} MEDIA SIGNED {Deal(d)}");
                    else if (d.Strikes > old.Strikes)
                        Console.WriteLine($"  [{date:yyyy-MM-dd}] {p.Abbreviation} MEDIA STRIKE {d.Partner} ({d.Kind}) now {d.Strikes}: {Missed(world, p, d)}");
                    else if (d.Strikes < old.Strikes)
                        Console.WriteLine($"  [{date:yyyy-MM-dd}] {p.Abbreviation} MEDIA strikes reset {d.Partner} {old.Strikes}->{d.Strikes}");
                }
                foreach (var b in before.Where(b => !p.MediaDeals.Any(d => d.Partner == b.Partner && d.Kind == b.Kind)))
                {
                    var why = fresh.FirstOrDefault(n => n.Headline == $"{b.Partner} drops {p.Abbreviation}")?.Body ?? "(replaced or org gone)";
                    Console.WriteLine($"  [{date:yyyy-MM-dd}] {p.Abbreviation} MEDIA LOST {b.Partner} ({b.Kind}, {b.Fee:C0}/ev, strikes {b.Strikes}): {why}");
                }
                mediaSnap[p.Id] = now;
            }

            // Acquisitions and deaths anywhere that matter at National+.
            foreach (var p in world.Promotions.Values.OrderBy(p => p.Id.Value))
            {
                int at = allLedgerAt.GetValueOrDefault(p.Id);
                for (int i = at; i < p.Ledger.Count; i++)
                    if (p.Ledger[i].Category == FinanceCategory.Acquisition)
                        Console.WriteLine($"  [{p.Ledger[i].Date:yyyy-MM-dd}] ACQUISITION {p.Abbreviation} ({OrgSizing.Label(OrgSizing.SizeOf(p))}, bank now {p.Balance}): {p.Ledger[i].Description} {p.Ledger[i].Amount.Amount:C0}");
                allLedgerAt[p.Id] = p.Ledger.Count;
                if (p.Defunct && defunct.Add(p.Id))
                    Console.WriteLine($"  [{date:yyyy-MM-dd}] GONE {p.Abbreviation} (was {OrgSizing.Label(tierOf.GetValueOrDefault(p.Id))}, bank {p.Balance})");
                if (!p.Defunct) tierOf[p.Id] = OrgSizing.SizeOf(p);
            }
            foreach (var n in fresh.Where(n => n.Headline.Contains(" acquires ")))
                Console.WriteLine($"  [{n.Date:yyyy-MM-dd}] NEWS {n.Headline}: {n.Body}");

            if (week % 13 != 0) continue;

            Console.WriteLine($"--- Q{week / 13} ({date:yyyy-MM}) ---");
            foreach (var p in watch)
            {
                var lines = p.Ledger.Skip(ledgerAt[p.Id]).ToList();
                ledgerAt[p.Id] = p.Ledger.Count;
                var byCat = lines.GroupBy(l => l.Category).OrderBy(g => g.Key)
                    .Select(g => (Cat: g.Key, Amt: g.Sum(l => l.Amount.Amount), N: g.Count())).ToList();
                decimal inc = byCat.Where(c => c.Amt > 0).Sum(c => c.Amt), exp = byCat.Where(c => c.Amt < 0).Sum(c => c.Amt);
                var roster = world.RosterOf(p.Id).ToList();
                var evs = world.Events.Values.Where(e => e.Promotion == p.Id && e.Financials is not null
                    && date.DayNumber - e.Date.DayNumber < 91).ToList();
                double house = evs.Select(e => (double)e.Financials!.Attendance).DefaultIfEmpty(0).Average();
                double room = evs.Select(e => (double)(e.VenueId is { } v ? world.Get(v)?.Capacity ?? 0 : 0)).DefaultIfEmpty(0).Average();
                Console.WriteLine($"  {p.Abbreviation,-5} {(p.Defunct ? "*** DEFUNCT *** " : "")}bank {p.Balance.Amount,14:C0} pr {p.Prestige.Value} {OrgSizing.Label(OrgSizing.SizeOf(p))} distress {Solvency.Distress(p)} " +
                    $"roster {roster.Count} (active {roster.Count(f => f.Status == FighterStatus.Active)}) committed {roster.Sum(f => f.Contract?.PursePerFight.Amount ?? 0):C0}/fight-cycle " +
                    $"mkt budget {p.MarketingBudget} · {evs.Count} cards, avg room {room:N0} house {house:N0} · net {inc + exp:C0}");
                Console.WriteLine("        " + string.Join(" | ", byCat.Select(c => $"{c.Cat} {c.Amt:C0} ({c.N})")));
                Console.WriteLine("        media: " + (p.MediaDeals.Count == 0 ? "none" : string.Join(" · ", p.MediaDeals.Select(Deal)))
                    + (p.Stream is { } s ? $" · stream {s.Subscribers:N0} subs" : ""));
            }
            Console.WriteLine();
        }
    }

    private readonly record struct DealSnap(string Partner, MediaKind Kind, int Strikes, decimal Fee);

    private static List<DealSnap> Snap(Promotion p) =>
        p.MediaDeals.Select(d => new DealSnap(d.Partner, d.Kind, d.Strikes, d.RightsFeePerEvent.Amount)).ToList();

    private static string Deal(MediaDeal d) =>
        $"{d.Kind} {d.Partner} {d.RightsFeePerEvent.Amount:C0}/ev {d.MonthsRemaining}mo strikes {d.Strikes} [{string.Join("; ", d.Requirements.Select(r => $"{r.Kind} {r.Threshold}"))}]";

    // The clauses the org's most recent settled card missed for this deal
    // (a cadence strike shows as no card-clause misses).
    private static string Missed(World world, Promotion p, MediaDeal d)
    {
        var last = world.Events.Values.Where(e => e.Promotion == p.Id && e.Financials is not null)
            .OrderByDescending(e => e.Date).ThenByDescending(e => e.Id.Value).FirstOrDefault();
        if (last is null) return "cadence";
        var missed = DealCompliance.Audit(world, last, d.Requirements).Where(c => !c.Met).Select(c => c.Text).ToList();
        return missed.Count == 0 ? $"cadence (last card {last.Date:yyyy-MM-dd})" : $"{last.Name} ({last.Tier}): {string.Join("; ", missed)}";
    }
}
