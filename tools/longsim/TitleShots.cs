using MmaManager.Core.Model;
using MmaManager.Core.Sim.Contracts;

namespace LongSim;

/// <summary>
/// Who gets the title shots, as the rankings deliver them (reporting only, for
/// <c>compare</c>). Each week, before the tick, every title fight on a card
/// within the coming week that a reigning champion defends is snapshotted from
/// the challenger's side: his rank in the promotion's division going in (0 =
/// unranked), his record, his current streak and his wins (among his last 8)
/// over fighters ranked or holding a belt at the time of the snapshot. Kept
/// only if the bout was actually fought. Also counts, once a month, the lifers
/// whose standing grievance is "passed over" for a title shot.
/// </summary>
public sealed class TitleShots
{
    public sealed record Shot(int Rank, int Wins, int Losses, int Streak, int WinsOverRanked);

    private readonly Dictionary<FightId, Shot> _pending = new();
    private readonly Dictionary<FightId, Fight> _fights = new();
    private readonly HashSet<EventId> _indexed = new();
    public List<Shot> Shots { get; } = new();
    public int PassedOverLiferMonths { get; private set; }
    public HashSet<FighterId> PassedOverLifers { get; } = new();

    public void BeforeWeek(World world)
    {
        // Fights by id, for completed cards (indexed once).
        foreach (var e in world.Events.Values)
        {
            if (e.Status != EventStatus.Completed || !_indexed.Add(e.Id)) continue;
            foreach (var fi in e.Card) _fights[fi.Id] = fi;
        }
        var horizon = world.CurrentDate.AddDays(7);
        HashSet<FighterId>? rankedNow = null;
        foreach (var e in world.Events.Values)
        {
            if (e.Status != EventStatus.Scheduled || e.Date > horizon || world.Get(e.Promotion) is not { } promo) continue;
            foreach (var fi in e.Card)
            {
                if (!fi.IsTitleFight || fi.Title is not { } tid || world.Get(tid)?.CurrentChampion is not { } champ) continue;
                if (fi.RedCorner != champ && fi.BlueCorner != champ) continue;
                var challenger = world.Get(fi.RedCorner == champ ? fi.BlueCorner : fi.RedCorner);
                if (challenger is null || _pending.ContainsKey(fi.Id)) continue;
                rankedNow ??= RankedOrChampions(world);
                var div = promo.Divisions.FirstOrDefault(d => d.Matches(challenger));
                int rank = div is null ? 0 : div.Rankings.IndexOf(challenger.Id) + 1;
                int overRanked = 0;
                for (int i = challenger.BoutHistory.Count - 1, seen = 0; i >= 0 && seen < 8; i--, seen++)
                    if (_fights.TryGetValue(challenger.BoutHistory[i], out var past) && past.Outcome?.Winner == challenger.Id
                        && rankedNow.Contains(past.RedCorner == challenger.Id ? past.BlueCorner : past.RedCorner))
                        overRanked++;
                _pending[fi.Id] = new Shot(rank, challenger.Record.Wins, challenger.Record.Losses, challenger.Record.CurrentStreak, overRanked);
            }
        }
    }

    public void AfterWeek(World world, bool monthBoundary)
    {
        foreach (var (id, shot) in _pending.ToList())
        {
            var ev = world.Events.Values.FirstOrDefault(e => e.Card.Any(x => x.Id == id));
            if (ev is null || ev.Status == EventStatus.Cancelled) { _pending.Remove(id); continue; }
            if (ev.Status != EventStatus.Completed) continue;
            if (ev.Card.First(x => x.Id == id).Outcome is not null) Shots.Add(shot);
            _pending.Remove(id);
        }
        if (!monthBoundary) return;
        foreach (var f in world.Fighters.Values)
        {
            if (f.Status != FighterStatus.Active || f.Contract is null || !Lifers.IsLifer(world, f)) continue;
            if (Lifers.GrievanceOf(world, f, world.CurrentDate) is { Kind: Lifers.GrievanceKind.PassedOver })
            {
                PassedOverLiferMonths++;
                PassedOverLifers.Add(f.Id);
            }
        }
    }

    private static HashSet<FighterId> RankedOrChampions(World world)
    {
        var set = new HashSet<FighterId>();
        foreach (var t in world.Titles.Values) if (t.CurrentChampion is { } c) set.Add(c);
        foreach (var p in world.Promotions.Values)
            foreach (var d in p.Divisions) set.UnionWith(d.Rankings);
        return set;
    }

    public static void Print(IReadOnlyList<(TitleShots Ts, int Years)> runs)
    {
        var shots = runs.SelectMany(r => r.Ts.Shots).ToList();
        double years = runs.Sum(r => r.Years);
        Console.WriteLine();
        Console.WriteLine("  --- TITLE SHOTS (challengers of a reigning champion, snapshotted the week before; DURABLE: fought bouts only) ---");
        if (shots.Count == 0) { Console.WriteLine("  none"); return; }
        double P(Func<Shot, bool> pred) => 100.0 * shots.Count(pred) / shots.Count;
        var ranked = shots.Where(s => s.Rank > 0).ToList();
        Console.WriteLine($"  title shots: {shots.Count} ({shots.Count / years:0.0}/run-year) · challenger #1 {P(s => s.Rank == 1):0.0}% · top-3 {P(s => s.Rank is >= 1 and <= 3):0.0}% · top-5 {P(s => s.Rank is >= 1 and <= 5):0.0}% · unranked {P(s => s.Rank == 0):0.0}% · mean rank (ranked) {(ranked.Count == 0 ? 0 : ranked.Average(s => s.Rank)):0.0}");
        Console.WriteLine($"  challenger record: wins {shots.Average(s => s.Wins):0.0} · losses {shots.Average(s => s.Losses):0.0} · win share {shots.Average(s => s.Wins / (double)Math.Max(1, s.Wins + s.Losses)):0.000} · on a 2+ win streak {P(s => s.Streak >= 2):0.0}% · on a loss {P(s => s.Streak < 0):0.0}% · wins over ranked/champions in last 8 {shots.Average(s => s.WinsOverRanked):0.00}");
        Console.WriteLine($"  lifers 'passed over' (monthly sample): {runs.Sum(r => r.Ts.PassedOverLiferMonths) / years:0.00} lifer-months/run-year · distinct lifers {runs.Sum(r => r.Ts.PassedOverLifers.Count) / years:0.00}/run-year");
    }
}
