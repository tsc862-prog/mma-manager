using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using MmaManager.Core.Model;

namespace LongSim.Check;

/// <summary>
/// Reflection over the model graph for the soak and determinism checks. This is a trimmed COPY of the
/// comparer in Tests/PersistenceGraph.cs (an exe cannot reference the test assembly), with two changes:
/// dictionary key-order differences are returned separately from value differences, and there is a
/// canonical digest (keys sorted) next to the strict one (enumeration order matters).
/// If a field is ever added to the not-saved list there, add it here too.
/// </summary>
public static class WorldGraph
{
    private static readonly HashSet<string> NotSaved = new()
    {
        "World.ArtCatalog",            // opaque portrait catalog, null in headless runs
        "FightOutcome.PlayByPlay",     // large prose log, dropped on purpose
    };

    public sealed class DiffResult
    {
        public List<string> Values { get; } = new();
        /// <summary>Dictionary enumeration order differs while the entries match. The sim iterates these when it rolls.</summary>
        public List<string> Order { get; } = new();
        public bool Identical => Values.Count == 0 && Order.Count == 0;
    }

    public static DiffResult Diff(World expected, World actual, int max = 25)
    {
        var r = new DiffResult();
        Compare(expected, actual, "world", r, max);
        return r;
    }

    private static void Compare(object? a, object? b, string path, DiffResult r, int max)
    {
        if (r.Values.Count >= max) return;
        if (a is null || b is null)
        {
            if (!(a is null && b is null)) r.Values.Add($"{path}: {Show(a)} vs {Show(b)}");
            return;
        }
        var t = a.GetType();
        if (t != b.GetType()) { r.Values.Add($"{path}: type {t.Name} vs {b.GetType().Name}"); return; }
        if (IsLeaf(t)) { if (!Equals(a, b)) r.Values.Add($"{path}: {Show(a)} vs {Show(b)}"); return; }

        if (a is IDictionary da)
        {
            var db = (IDictionary)b;
            if (da.Count != db.Count) { r.Values.Add($"{path}: count {da.Count} vs {db.Count}"); return; }
            var ka = da.Keys.Cast<object>().ToList();
            var kb = db.Keys.Cast<object>().ToList();
            for (int i = 0; i < ka.Count; i++)
                if (!Equals(ka[i], kb[i])) { r.Order.Add($"{path}: key order differs at {i} ({ka[i]} vs {kb[i]})"); break; }
            foreach (var k in ka)
            {
                if (!db.Contains(k)) { r.Values.Add($"{path}[{k}]: missing"); continue; }
                Compare(da[k], db[k], $"{path}[{k}]", r, max);
            }
            return;
        }
        if (a is IEnumerable ea && a is not string)
        {
            var la = ea.Cast<object?>().ToList();
            var lb = ((IEnumerable)b).Cast<object?>().ToList();
            if (la.Count != lb.Count) { r.Values.Add($"{path}: count {la.Count} vs {lb.Count}"); return; }
            if (IsSet(t))
            {
                var sb = new HashSet<object?>(lb);
                foreach (var x in la) if (!sb.Contains(x)) r.Values.Add($"{path}: {Show(x)} missing");
                return;
            }
            for (int i = 0; i < la.Count; i++) Compare(la[i], lb[i], $"{path}[{i}]", r, max);
            return;
        }
        foreach (var m in Members(t))
        {
            if (NotSaved.Contains($"{t.Name}.{m.Name}")) continue;
            Compare(m.Get(a), m.Get(b), $"{path}.{m.Name}", r, max);
        }
    }

    // ---- digests ------------------------------------------------------------

    /// <summary>
    /// SHA-256 over every public member of the graph. <paramref name="canonical"/> sorts dictionary entries by key
    /// (state only); the strict form walks them in enumeration order (state plus order).
    /// </summary>
    public static string Digest(World world, bool canonical)
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var sb = new StringBuilder(1 << 16);
        Walk(world, sb, canonical, h);
        Flush(sb, h);
        return Convert.ToHexString(h.GetHashAndReset())[..16];
    }

    private static void Flush(StringBuilder sb, IncrementalHash h)
    {
        if (sb.Length == 0) return;
        h.AppendData(Encoding.UTF8.GetBytes(sb.ToString()));
        sb.Clear();
    }

    private static void Walk(object? o, StringBuilder sb, bool canonical, IncrementalHash h)
    {
        if (sb.Length > 60_000) Flush(sb, h);
        if (o is null) { sb.Append('~'); return; }
        var t = o.GetType();
        if (IsLeaf(t)) { sb.Append(Leaf(o)).Append(';'); return; }
        if (o is IDictionary d)
        {
            var keys = d.Keys.Cast<object>();
            if (canonical) keys = keys.OrderBy(k => k is IComparable ? k.ToString() : k.ToString(), StringComparer.Ordinal);
            sb.Append('{');
            foreach (var k in keys) { sb.Append(Leaf(k)).Append('='); Walk(d[k], sb, canonical, h); }
            sb.Append('}');
            return;
        }
        if (o is IEnumerable e && o is not string)
        {
            sb.Append('[');
            if (IsSet(t))
            {
                foreach (var x in e.Cast<object?>().Select(x => Leaf(x)).OrderBy(x => x, StringComparer.Ordinal))
                    sb.Append(x).Append(',');
            }
            else foreach (var x in e) Walk(x, sb, canonical, h);
            sb.Append(']');
            return;
        }
        sb.Append('(').Append(t.Name).Append(':');
        foreach (var m in Members(t))
        {
            if (NotSaved.Contains($"{t.Name}.{m.Name}")) continue;
            Walk(m.Get(o), sb, canonical, h);
        }
        sb.Append(')');
    }

    private static string Leaf(object? o) => o switch
    {
        null => "~",
        double d => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        float f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        decimal m => m.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Money mo => mo.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture),   // Money.ToString rounds to $0
        IFormattable fm => fm.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => o.ToString() ?? "?",
    };

    // ---- helpers (mirrors Tests/PersistenceGraph.cs) ---------------------------

    private sealed record Member(string Name, Func<object, object?> Get);
    private static readonly Dictionary<Type, Member[]> Cache = new();

    private static Member[] Members(Type t)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(t, out var c)) return c;
            var list = new List<Member>();
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0 || !p.CanRead) continue;
                bool isCollection = typeof(IEnumerable).IsAssignableFrom(p.PropertyType) && p.PropertyType != typeof(string);
                if (p.SetMethod is null && !isCollection) continue;
                var pi = p;
                list.Add(new Member(p.Name, o => pi.GetValue(o)));
            }
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var fi = f;
                list.Add(new Member(f.Name, o => fi.GetValue(o)));
            }
            var arr = list.ToArray();
            Cache[t] = arr;
            return arr;
        }
    }

    private static bool IsLeaf(Type t) =>
        t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal)
        || t == typeof(DateOnly) || t == typeof(DateTime) || t == typeof(TimeSpan)
        || (t.IsValueType && !t.IsGenericType)
        || Nullable.GetUnderlyingType(t) is not null;

    private static bool IsSet(Type t) =>
        t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISet<>));

    private static string Show(object? v) => v switch
    {
        null => "null",
        string s => s.Length > 60 ? $"\"{s[..60]}...\"" : $"\"{s}\"",
        IEnumerable e and not string => "[" + string.Join(",", e.Cast<object?>().Take(5)) + "]",
        _ => v.ToString() ?? "?",
    };
}
