using System.Text;

namespace LongSim.Check;

public sealed record Sample(string Run, DateOnly Date, string Detail);

/// <summary>
/// Collects invariant violations across runs (thread-safe). A violation is identified by (invariant, run, key): the
/// first sighting is recorded with its date and entities; later sightings of the same key only bump a recurrence
/// count, so a bad row that persists for ten years is one finding, not 120.
/// </summary>
public sealed class ViolationLog
{
    private sealed class Stat
    {
        public string Description = "";
        public long Checks;
        public int Distinct;
        public long Recurrences;
        public HashSet<string> Runs = new();
        public List<Sample> Samples = new();
        public DateOnly? EarliestDate;
    }

    private readonly object _gate = new();
    private readonly Dictionary<string, Stat> _stats = new();
    private readonly HashSet<string> _seen = new();
    private const int KeepSamples = 60;

    public void Declare(string invariant, string description)
    {
        lock (_gate) Get(invariant).Description = description;
    }

    private Stat Get(string inv)
    {
        if (!_stats.TryGetValue(inv, out var s)) _stats[inv] = s = new Stat();
        return s;
    }

    public void Checked(string invariant, long n)
    {
        lock (_gate) Get(invariant).Checks += n;
    }

    public void Report(string invariant, string run, DateOnly date, string key, string detail)
    {
        lock (_gate)
        {
            var s = Get(invariant);
            if (!_seen.Add($"{invariant}|{run}|{key}")) { s.Recurrences++; return; }
            s.Distinct++;
            s.Runs.Add(run);
            if (s.EarliestDate is null || date < s.EarliestDate) s.EarliestDate = date;
            s.Samples.Add(new Sample(run, date, detail));
            if (s.Samples.Count > KeepSamples * 2)
                s.Samples = s.Samples.OrderBy(x => x.Date).ThenBy(x => x.Run, StringComparer.Ordinal).Take(KeepSamples).ToList();
        }
    }

    public int TotalDistinct { get { lock (_gate) return _stats.Values.Sum(s => s.Distinct); } }

    public int Count(string invariant) { lock (_gate) return _stats.TryGetValue(invariant, out var s) ? s.Distinct : 0; }

    /// <summary>Per-invariant table and the first few samples of each invariant that fired.</summary>
    public string Render(int samplesPerInvariant = 4)
    {
        var sb = new StringBuilder();
        lock (_gate)
        {
            sb.AppendLine($"{"invariant",-26} {"checked",10} {"violations",10} {"recur",8} {"runs",5}  description");
            foreach (var (name, s) in _stats.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                sb.AppendLine($"{name,-26} {s.Checks,10:N0} {s.Distinct,10:N0} {s.Recurrences,8:N0} {s.Runs.Count,5}  {s.Description}");
            foreach (var (name, s) in _stats.Where(kv => kv.Value.Distinct > 0).OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                sb.AppendLine();
                sb.AppendLine($"--- {name}: {s.Distinct} violation(s) in {s.Runs.Count} run(s); earliest {s.EarliestDate:yyyy-MM-dd} ---");
                foreach (var x in s.Samples.OrderBy(x => x.Date).ThenBy(x => x.Run, StringComparer.Ordinal).Take(samplesPerInvariant))
                    sb.AppendLine($"  [{x.Run} {x.Date:yyyy-MM-dd}] {x.Detail}");
            }
        }
        return sb.ToString();
    }
}
