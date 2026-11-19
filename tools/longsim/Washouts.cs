using MmaManager.Core.Model;
using MmaManager.Core.Sim.Org;

namespace LongSim;

/// <summary>
/// WINLESS WASHOUTS (reporting only, for <c>compare</c>): the career rule says a winless man past the fair start
/// (0-3) quits long before he grinds out a dreadful record. This follows every active non-local man at the
/// WINLESS FLOOR (no wins, 3+ bouts) after each tick, read-only:
/// <list type="bullet">
/// <item>Year-end snapshots: active non-local men at 0-4, 0-5, 0-6, 0-7+, signed vs free agent; for the 0-5+,
/// why he is still there (fights left on the deal, already on a card, hurt).</item>
/// <item>Every monthly washout roll a floor man faced and survived, signed or free (or skipped: hurt or
/// suspended, the roll only reads Active men), and the days he stayed active past each loss.</item>
/// <item>Every bout booked for a winless man with 4 / 5+ losses, the week it first appeared on a card: a
/// house's card (by tier, his own house or not) or the local circuit; and the bouts such men actually fought.</item>
/// <item>The churn the rule feeds: active count, free-agent pool, debuts and retirements per year, and
/// completed promotion cards that ran short of the six-bout minimum.</item>
/// </list>
/// </summary>
public sealed class Washouts
{
    public const int Buckets = 4;   // 0-4, 0-5, 0-6, 0-7+
    static int Bucket(int losses) => Math.Min(losses, 7) - 4;

    // Year-end: [year-1][bucket][signed 1 / free 0]
    public readonly List<int[,]> Grid = new();
    public readonly List<(int Losses, bool Signed, int Left, bool Booked, bool Hurt, int Age, int FloorDays)> FivePlus = new();
    public readonly List<(int Active, int Free, int Debuts, int Retired, int ShortCards, int Cards)> Years = new();

    // Monthly rolls faced by a floor man: survived signed / survived free / skipped (not Active) / retired that month.
    public int RollSurvivedSigned, RollSurvivedFree, RollSkippedHurt, RollQuitSigned, RollQuitFree;
    public int RollSurvivedSigned5, RollSurvivedFree5, RollSkippedHurt5;   // ...the same, for men already at 0-5+
    // Losses at the floor: the loss that took a man to N losses (N >= 4), signed (deal has fights left) or not.
    public readonly Dictionary<int, (int Signed, int Free)> FloorLosses = new();
    // Days active after a floor loss before he quit (or the run ended), for the loss that took him to 0-4 / 0-5+.
    public readonly List<int> DaysAfter4 = new(), DaysAfter5 = new();
    public int Escaped4, Escaped5;           // won a bout from 0-4 / 0-5+ (left the floor the other way)
    // Bookings of a winless man with 4 / 5+ losses: by card kind.
    public readonly Dictionary<string, int> Booked4 = new(), Booked5 = new();
    public int Fought5;                      // bouts actually fought by a man entering at 0-5+
    public int Fought5Won;

    sealed class Track
    {
        public int Losses;
        public DateOnly LastLoss;
        public DateOnly FloorSince;
    }

    private readonly Dictionary<FighterId, Track> _floor = new();
    private readonly HashSet<FightId> _seenFight = new();
    private readonly HashSet<FighterId> _known = new();
    private bool _first = true;
    private int _debuts, _retired, _shortCards, _cards;
    private readonly HashSet<EventId> _countedCard = new();
    private Dictionary<FighterId, (bool Active, bool Signed)> _pre = new();

    /// <summary>The pre-tick state of the floor men (whether each was active, and whether he was signed).</summary>
    public void BeforeWeek(World world)
    {
        if (_first)
        {
            foreach (var f in world.Fighters.Values) _known.Add(f.Id);
            foreach (var ev in world.Events.Values) foreach (var x in ev.Card) _seenFight.Add(x.Id);
            foreach (var ev in world.Events.Values) _countedCard.Add(ev.Id);
            RememberActive(world);
            _first = false;
        }
        _pre = new();
        foreach (var id in _floor.Keys)
            if (world.Get(id) is { } f)
                _pre[id] = (f.Status == FighterStatus.Active, f.Contract is { FightsRemaining: > 0 });
    }

    public void AfterWeek(World world, DateOnly before)
    {
        var date = world.CurrentDate;
        bool monthly = date.Month != before.Month;

        // Debuts (any new non-local man) and retirements (non-local, active or hurt last week, retired now).
        foreach (var f in world.Fighters.Values)
        {
            if (_known.Add(f.Id) && !f.LocalTalent) _debuts++;
        }

        // Bookings first seen this week.
        foreach (var ev in world.Events.Values)
        {
            if (ev.Status != EventStatus.Scheduled && ev.Status != EventStatus.Completed) continue;
            foreach (var x in ev.Card)
            {
                if (!_seenFight.Add(x.Id)) continue;
                foreach (var id in new[] { x.RedCorner, x.BlueCorner })
                {
                    if (world.Get(id) is not { LocalTalent: false } f || f.Record.Wins > 0) continue;
                    // A completed card first seen now was booked and fought inside this week: read the record before the bout.
                    int losses = f.Record.Losses - (ev.Status == EventStatus.Completed && x.Outcome?.Loser == id ? 1 : 0);
                    if (losses < 4) continue;
                    string kind;
                    if (world.Get(ev.Promotion) is not { } host) kind = "local circuit";
                    else kind = OrgSizing.Label(OrgSizing.SizeOf(host)) + (f.Contract?.Promotion == host.Id ? " (own house)" : " (not his house)");
                    var map = losses >= 5 ? Booked5 : Booked4;
                    map[kind] = map.GetValueOrDefault(kind) + 1;
                }
            }
        }

        // Completed promotion cards this week: short of the six-bout minimum?
        foreach (var ev in world.Events.Values)
        {
            if (ev.Status != EventStatus.Completed || ev.Card.Count == 0) continue;
            if (!_countedCard.Add(ev.Id)) continue;
            if (world.Get(ev.Promotion) is not null)
            {
                _cards++;
                if (ev.Card.Count < 6) _shortCards++;
            }
            foreach (var x in ev.Card)
                if (x.Outcome is { } o)
                    foreach (var id in new[] { x.RedCorner, x.BlueCorner })
                        if (_floor.TryGetValue(id, out var t) && t.Losses >= 5)
                        {
                            Fought5++;
                            if (o.Winner == id) Fought5Won++;
                        }
        }

        // The floor men: losses, exits, and the monthly roll.
        foreach (var (id, t) in _floor.ToList())
        {
            var f = world.Get(id)!;
            _pre.TryGetValue(id, out var pre);
            if (f.Status == FighterStatus.Retired)
            {
                (t.Losses >= 5 ? DaysAfter5 : DaysAfter4).Add(date.DayNumber - t.LastLoss.DayNumber);
                if (monthly && pre.Active) { if (pre.Signed) RollQuitSigned++; else RollQuitFree++; }
                _floor.Remove(id);
                continue;
            }
            if (f.Record.Wins > 0)
            {
                if (t.Losses >= 5) Escaped5++; else Escaped4++;
                _floor.Remove(id);
                continue;
            }
            if (f.Record.Losses > t.Losses)
            {
                for (int n = t.Losses + 1; n <= f.Record.Losses; n++)
                {
                    var c = FloorLosses.GetValueOrDefault(n);
                    FloorLosses[n] = f.Contract is { FightsRemaining: > 0 } ? (c.Signed + 1, c.Free) : (c.Signed, c.Free + 1);
                }
                if (t.Losses >= 4) (t.Losses >= 5 ? DaysAfter5 : DaysAfter4).Add(date.DayNumber - t.LastLoss.DayNumber);
                t.Losses = f.Record.Losses;
                t.LastLoss = date;
            }
            if (monthly)
            {
                bool five = t.Losses >= 5;
                if (f.Status != FighterStatus.Active) { RollSkippedHurt++; if (five) RollSkippedHurt5++; }
                else if (f.Contract is null) { RollSurvivedFree++; if (five) RollSurvivedFree5++; }
                else { RollSurvivedSigned++; if (five) RollSurvivedSigned5++; }
            }
        }
        // ...and the men who came onto the floor this week (or were on it on day one).
        foreach (var f in world.Fighters.Values)
        {
            if (f.LocalTalent || f.Status == FighterStatus.Retired || f.Record.Wins > 0 || f.Record.TotalBouts < 3) continue;
            if (_floor.ContainsKey(f.Id)) continue;
            _floor[f.Id] = new Track { Losses = f.Record.Losses, LastLoss = date, FloorSince = date };
            if (f.Record.Losses >= 4)
            {
                var c = FloorLosses.GetValueOrDefault(f.Record.Losses);
                FloorLosses[f.Record.Losses] = f.Contract is { FightsRemaining: > 0 } ? (c.Signed + 1, c.Free) : (c.Signed, c.Free + 1);
            }
        }
        // Retirements among everyone else (non-local) - the churn line, not the floor.
        foreach (var f in world.Fighters.Values)
            if (!f.LocalTalent && f.Status == FighterStatus.Retired && _activeLast.Contains(f.Id)) _retired++;
        RememberActive(world);
    }

    // Every non-local man not retired after the last tick: a retirement is one of them retired now.
    private readonly HashSet<FighterId> _activeLast = new();
    private void RememberActive(World world)
    {
        _activeLast.Clear();
        foreach (var f in world.Fighters.Values)
            if (!f.LocalTalent && f.Status != FighterStatus.Retired) _activeLast.Add(f.Id);
    }

    public void YearEnd(World world)
    {
        var date = world.CurrentDate;
        var grid = new int[Buckets, 2];
        int active = 0, free = 0;
        var booked = new HashSet<FighterId>();
        foreach (var ev in world.Events.Values)
            if (ev.Status == EventStatus.Scheduled)
                foreach (var x in ev.Card) { booked.Add(x.RedCorner); booked.Add(x.BlueCorner); }
        foreach (var f in world.Fighters.Values)
        {
            if (f.LocalTalent || f.Status == FighterStatus.Retired) continue;
            if (f.Status == FighterStatus.Active) { active++; if (f.Contract is null) free++; }
            if (f.Status != FighterStatus.Active || f.Record.Wins > 0 || f.Record.Losses < 4) continue;
            bool signed = f.Contract is not null;
            grid[Bucket(f.Record.Losses), signed ? 1 : 0]++;
            if (f.Record.Losses >= 5)
                FivePlus.Add((f.Record.Losses, signed, f.Contract?.FightsRemaining ?? 0, booked.Contains(f.Id),
                    false, f.AgeOn(date), _floor.TryGetValue(f.Id, out var t) ? date.DayNumber - t.FloorSince.DayNumber : -1));
        }
        // Hurt winless 0-5+ men are not Active, so the grid above leaves them out: list them apart.
        foreach (var f in world.Fighters.Values)
            if (!f.LocalTalent && f.Status is FighterStatus.Injured or FighterStatus.Suspended && f.Record.Wins == 0 && f.Record.Losses >= 5)
                FivePlus.Add((f.Record.Losses, f.Contract is not null, f.Contract?.FightsRemaining ?? 0, booked.Contains(f.Id),
                    true, f.AgeOn(date), _floor.TryGetValue(f.Id, out var t) ? date.DayNumber - t.FloorSince.DayNumber : -1));
        Grid.Add(grid);
        Years.Add((active, free, _debuts, _retired, _shortCards, _cards));
        _debuts = _retired = _shortCards = _cards = 0;
    }

    /// <summary>At the end of the run: the days the still-active floor men have stood since their last loss.</summary>
    public void Close(World world)
    {
        foreach (var (_, t) in _floor)
            (t.Losses >= 5 ? DaysAfter5 : t.Losses >= 4 ? DaysAfter4 : null)?.Add(world.CurrentDate.DayNumber - t.LastLoss.DayNumber);
    }

    public static void Print(List<Washouts> seeds)
    {
        if (seeds.Count == 0) return;
        int years = seeds.Min(s => s.Grid.Count);
        Console.WriteLine();
        Console.WriteLine("  --- WINLESS WASHOUTS (SAMPLED after every tick; active non-local men with no wins) ---");
        Console.WriteLine("  year-end, summed over seeds: signed / free agent at 0-4 · 0-5 · 0-6 · 0-7+  (seeds with any 0-5+ / any 0-6+)");
        for (int y = 0; y < years; y++)
        {
            string cells = string.Join(" · ", Enumerable.Range(0, Buckets).Select(b =>
                $"{(b == 3 ? "0-7+" : $"0-{b + 4}")} {seeds.Sum(s => s.Grid[y][b, 1])}/{seeds.Sum(s => s.Grid[y][b, 0])}"));
            int any5 = seeds.Count(s => Enumerable.Range(1, 3).Any(b => s.Grid[y][b, 0] + s.Grid[y][b, 1] > 0));
            int any6 = seeds.Count(s => Enumerable.Range(2, 2).Any(b => s.Grid[y][b, 0] + s.Grid[y][b, 1] > 0));
            Console.WriteLine($"  Y{y + 1}: {cells}  ({any5}/{seeds.Count} · {any6}/{seeds.Count})");
        }
        var five = seeds.SelectMany(s => s.FivePlus).ToList();
        Console.WriteLine($"  0-5+ men at year-ends (incl. hurt): {five.Count} · signed {five.Count(x => x.Signed)} (fights left on the deal: " +
            $"{string.Join(" ", five.Where(x => x.Signed).GroupBy(x => x.Left).OrderBy(g => g.Key).Select(g => $"{g.Key}:{g.Count()}"))}) · " +
            $"already on a scheduled card {five.Count(x => x.Booked)} · hurt/suspended {five.Count(x => x.Hurt)} · " +
            $"age <=22 {five.Count(x => x.Age <= 22)} · days on the floor median {Median(five.Select(x => x.FloorDays))}");
        int S(Func<Washouts, int> f) => seeds.Sum(f);
        Console.WriteLine($"  monthly rolls a floor man faced: survived signed {S(s => s.RollSurvivedSigned)} · survived free {S(s => s.RollSurvivedFree)} · " +
            $"skipped hurt/suspended {S(s => s.RollSkippedHurt)} · quit that month signed {S(s => s.RollQuitSigned)} / free {S(s => s.RollQuitFree)}");
        Console.WriteLine($"    ...by men already at 0-5+: survived signed {S(s => s.RollSurvivedSigned5)} · survived free {S(s => s.RollSurvivedFree5)} · skipped hurt {S(s => s.RollSkippedHurt5)}");
        var losses = seeds.SelectMany(s => s.FloorLosses).GroupBy(kv => kv.Key).OrderBy(g => g.Key).ToList();
        Console.WriteLine("  winless losses taken (the loss that made 0-N; deal with fights left / not): " +
            string.Join(" · ", losses.Select(g => $"0-{g.Key} {g.Sum(kv => kv.Value.Signed)}/{g.Sum(kv => kv.Value.Free)}")));
        var d4 = seeds.SelectMany(s => s.DaysAfter4).ToList();
        var d5 = seeds.SelectMany(s => s.DaysAfter5).ToList();
        Console.WriteLine($"  days active after a loss at 0-4: n {d4.Count} median {Median(d4)} p90 {Pct(d4, 0.9)} · at 0-5+: n {d5.Count} median {Median(d5)} p90 {Pct(d5, 0.9)}");
        Console.WriteLine($"  escaped the floor with a win: from 0-4 {S(s => s.Escaped4)} · from 0-5+ {S(s => s.Escaped5)}; bouts fought entering at 0-5+ {S(s => s.Fought5)} (won {S(s => s.Fought5Won)})");
        void Map(string label, Func<Washouts, Dictionary<string, int>> pick)
        {
            var all = seeds.SelectMany(s => pick(s)).GroupBy(kv => kv.Key).Select(g => (g.Key, N: g.Sum(kv => kv.Value))).OrderByDescending(x => x.N).ToList();
            Console.WriteLine($"  {label}: {all.Sum(x => x.N)} ({string.Join(" · ", all.Select(x => $"{x.Key} {x.N}"))})");
        }
        Map("bouts booked for a winless man at 0-4", s => s.Booked4);
        Map("bouts booked for a winless man at 0-5+", s => s.Booked5);
        Console.WriteLine("  churn per year, mean over seeds: active · free agents · debuts · retirements · completed promotion cards (short of 6 bouts)");
        for (int y = 0; y < years; y++)
        {
            double A(Func<(int Active, int Free, int Debuts, int Retired, int ShortCards, int Cards), int> f) => seeds.Average(s => f(s.Years[y]));
            Console.WriteLine($"  Y{y + 1}: {A(x => x.Active),6:0.0} · {A(x => x.Free),5:0.0} · {A(x => x.Debuts),5:0.0} · {A(x => x.Retired),5:0.0} · {A(x => x.Cards),5:0.0} ({A(x => x.ShortCards):0.00})");
        }
    }

    static int Median(IEnumerable<int> xs) => Pct(xs.ToList(), 0.5);
    static int Pct(List<int> xs, double q)
    {
        if (xs.Count == 0) return 0;
        var s = xs.OrderBy(x => x).ToList();
        return s[Math.Min(s.Count - 1, (int)(q * s.Count))];
    }
}
