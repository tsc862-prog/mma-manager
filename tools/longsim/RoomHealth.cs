using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Org;

namespace LongSim;

/// <summary>
/// The rooms the houses play (compare's VENUES block): per tier at the time of the card (the house's tier SAMPLED the
/// week before), and the AFC on its own: the room, the crowd that wanted in, how often the crowd outgrew the room or
/// left it under 60% full, the people and gate turned away, the rent, the card's net (gate + PPV - production with
/// rent - payroll, from the settled financials: DURABLE), and two diagnostics: whether a bigger room stood in the same
/// city when the crowd outgrew the one picked, and whether the room picked was the one that would have netted most on
/// the night (gate less production and rent; PPV and payroll do not depend on the room).
/// <para>Demand: a card that did not sell out drew exactly its demand (the settled attendance is the uncapped house);
/// a sell-out's demand is re-estimated at week end with <see cref="Financials.Demand"/> (uncapped; popularity has moved
/// a little since the fights), and never below the room. Also the bank of every live org at each year end by its
/// tier then (p10 / median). Observes only; no draws.</para>
/// </summary>
public sealed class RoomHealth
{
    public sealed class Acc
    {
        public int Cards, OverRoom, UnderSixty, BiggerRoomStood, BestRoom;
        public double Room, Demand, Turned;
        public decimal GateTurned, Rent, Net, Gate;
    }

    public readonly Dictionary<string, Acc> By = new();
    /// <summary>Per year: (tier label, bank) of every live org at year end ("AFC" rows are also in their tier).</summary>
    public readonly List<List<(string Who, decimal Bank)>> BankByYear = new();

    private Dictionary<PromotionId, OrgSize> _tier = new();
    private List<Event> _due = new();

    private static IEnumerable<string> Who(Promotion p, OrgSize tier) =>
        p.Abbreviation == "AFC" ? new[] { "AFC", OrgSizing.Label(tier) } : new[] { OrgSizing.Label(tier) };

    public void BeforeWeek(World world)
    {
        _tier = world.Promotions.Values.Where(p => !p.Defunct).ToDictionary(p => p.Id, OrgSizing.SizeOf);
        var horizon = world.CurrentDate.AddDays(8);
        _due = world.Events.Values.Where(e => e.Status == EventStatus.Scheduled && e.Date <= horizon).ToList();
    }

    public void AfterWeek(World world)
    {
        foreach (var ev in _due)
        {
            if (ev.Status != EventStatus.Completed || ev.Card.Count == 0 || ev.Financials is not { } fin) continue;
            if (world.Get(ev.Promotion) is not { } promo || !_tier.TryGetValue(promo.Id, out var tier)) continue;
            if (ev.VenueId is not { } vid || world.Get(vid) is not { } room) continue;
            int cap = room.Capacity;
            int demand = fin.Attendance < cap ? fin.Attendance : Math.Max(cap, Financials.Demand(world, ev));
            int turned = Math.Max(0, demand - fin.Attendance);
            decimal mult = ev.Tier switch { EventTier.Ppv => 1.3m, EventTier.SuperCard => 1.6m, _ => 1.0m };
            decimal prod = ev.Tier switch { EventTier.Ppv => 1.4m, EventTier.SuperCard => 1.8m, _ => 1.0m };
            decimal price = Financials.TicketPrice(world, promo.Prestige.Value);
            decimal RoomNet(Venue v)
            {
                int seats = Math.Min(demand, v.Capacity);
                return seats * price * mult - (10_000m + seats * 12m) * prod - Financials.RentOf(world, v).Amount;
            }
            var city = world.Venues.Values.Where(v => v.City == room.City).ToList();
            decimal best = city.Max(RoomNet);
            foreach (var w in Who(promo, tier))
            {
                if (!By.TryGetValue(w, out var a)) By[w] = a = new Acc();
                a.Cards++;
                a.Room += cap;
                a.Demand += demand;
                a.Turned += turned;
                if (demand > cap)
                {
                    a.OverRoom++;
                    if (city.Any(v => v.Capacity > cap)) a.BiggerRoomStood++;
                }
                if (fin.Attendance < 0.6 * cap) a.UnderSixty++;
                if (RoomNet(room) >= best) a.BestRoom++;
                a.GateTurned += turned * price * mult;
                a.Rent += Financials.RentOf(world, room).Amount;
                a.Net += fin.NetProfit.Amount;
                a.Gate += fin.GateRevenue.Amount;
            }
        }
    }

    public void YearEnd(World world)
    {
        var rows = new List<(string, decimal)>();
        foreach (var p in world.Promotions.Values.Where(p => !p.Defunct).OrderBy(p => p.Id.Value))
            foreach (var w in Who(p, OrgSizing.SizeOf(p))) rows.Add((w, p.Balance.Amount));
        BankByYear.Add(rows);
    }

    private static readonly string[] Order = { "AFC", "International", "Continental", "National", "Regional", "City" };

    private static IEnumerable<string> Whos(IEnumerable<string> keys)
    {
        var have = keys.ToHashSet();
        return Order.Where(have.Contains).Concat(have.Where(k => !Order.Contains(k)).OrderBy(k => k, StringComparer.Ordinal));
    }

    public static void Print(IReadOnlyList<RoomHealth> seeds)
    {
        if (seeds.Count == 0) return;
        Console.WriteLine();
        Console.WriteLine("  --- VENUES (per completed card, by the house's tier the week before (SAMPLED weekly); room/attendance/gate/rent/net DURABLE from the settled card;"
            + " demand = attendance unless sold out, then re-estimated uncapped at week end; 'best room' = the room in that city that nets most on the night's demand) ---");
        Console.WriteLine($"    {"who",-14} {"cards",6} | {"room",7} {"demand",7} {"over",6} {"<60%",6} | {"turned",7} {"gate lost",10} | {"rent",8} {"gate",10} {"net",10} | {"bigger room stood",17} {"best room",9}");
        foreach (var w in Whos(seeds.SelectMany(s => s.By.Keys)))
        {
            var acc = seeds.Select(s => s.By.GetValueOrDefault(w)).Where(a => a is not null).Select(a => a!).ToList();
            int n = acc.Sum(a => a.Cards);
            if (n == 0) continue;
            int over = acc.Sum(a => a.OverRoom);
            Console.WriteLine($"    {w,-14} {n,6} | {acc.Sum(a => a.Room) / n,7:N0} {acc.Sum(a => a.Demand) / n,7:N0} {100.0 * over / n,5:0.0}% {100.0 * acc.Sum(a => a.UnderSixty) / n,5:0.0}% | "
                + $"{acc.Sum(a => a.Turned) / n,7:N0} {acc.Sum(a => a.GateTurned) / n,10:N0} | {acc.Sum(a => a.Rent) / n,8:N0} {acc.Sum(a => a.Gate) / n,10:N0} {acc.Sum(a => a.Net) / n,10:N0} | "
                + $"{(over == 0 ? "-" : $"{100.0 * acc.Sum(a => a.BiggerRoomStood) / over:0.0}% of over"),17} {100.0 * acc.Sum(a => a.BestRoom) / n,8:0.0}%");
        }
        int ys = seeds.Max(s => s.BankByYear.Count);
        if (ys == 0) return;
        Console.WriteLine("  bank of live orgs at the final year end by tier then ($M; p10 / median / mean; n):");
        var last = seeds.Where(s => s.BankByYear.Count == ys).SelectMany(s => s.BankByYear[ys - 1]).ToList();
        foreach (var w in Whos(last.Select(r => r.Who)))
        {
            var v = last.Where(r => r.Who == w).Select(r => r.Bank).OrderBy(x => x).ToList();
            decimal Q(double q) => v[Math.Clamp((int)Math.Round(q * (v.Count - 1)), 0, v.Count - 1)] / 1_000_000m;
            Console.WriteLine($"    {w,-14} p10 {Q(0.1),7:0.00} · median {Q(0.5),7:0.00} · mean {v.Average() / 1_000_000m,7:0.00} · n {v.Count}");
        }
    }
}
