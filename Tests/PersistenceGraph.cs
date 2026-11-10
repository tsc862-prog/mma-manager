using System.Collections;
using System.Reflection;
using System.Text;
using MmaManager.Core.Model;

namespace MmaManager.Tests;

/// <summary>
/// Reflection over the model graph, used by the persistence tests so that a new
/// model field cannot silently escape the save: the comparer looks at every
/// public property, so a field the writer forgets shows up as a difference, and
/// the coverage walk shows which properties a test world never populated (a
/// field that is default on both sides would otherwise pass unnoticed).
/// </summary>
internal static class PersistenceGraph
{
    /// <summary>
    /// Properties that are deliberately NOT saved. Key is "Type.Property". Each entry
    /// says why and how it comes back. An entry that no longer matches a real property
    /// fails the suite, so this list cannot rot either.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> NotSaved = new Dictionary<string, string>
    {
        ["World.ArtCatalog"] = "Opaque portrait catalog attached by the app after load; null in headless runs.",
        ["FightOutcome.PlayByPlay"] = "Large prose log, dropped on purpose; only present on a freshly simulated bout.",
    };

    // ---- deep comparison --------------------------------------------------

    /// <summary>Every difference between two graphs, as readable paths. Empty means identical.</summary>
    public static List<string> Diff(object? expected, object? actual, int max = 40, bool ignoreKeyOrder = false)
    {
        var diffs = new List<string>();
        Compare(expected, actual, "world", diffs, max, ignoreKeyOrder);
        return diffs;
    }

    private static void Compare(object? a, object? b, string path, List<string> diffs, int max, bool ignoreKeyOrder)
    {
        if (diffs.Count >= max) return;
        if (a is null || b is null)
        {
            if (!(a is null && b is null)) diffs.Add($"{path}: {Show(a)} vs {Show(b)}");
            return;
        }

        var t = a.GetType();
        if (t != b.GetType()) { diffs.Add($"{path}: type {t.Name} vs {b.GetType().Name}"); return; }

        if (IsLeaf(t))
        {
            if (!Equals(a, b)) diffs.Add($"{path}: {Show(a)} vs {Show(b)}");
            return;
        }

        if (a is IDictionary da)
        {
            var db = (IDictionary)b;
            if (da.Count != db.Count) { diffs.Add($"{path}: count {da.Count} vs {db.Count}"); return; }
            var ka = da.Keys.Cast<object>().ToList();
            var kb = db.Keys.Cast<object>().ToList();
            // Enumeration order matters: the sim iterates these when it rolls.
            for (int i = 0; i < ka.Count && !ignoreKeyOrder; i++)
                if (!Equals(ka[i], kb[i])) { diffs.Add($"{path}: key order differs at {i} ({ka[i]} vs {kb[i]})"); break; }
            foreach (var k in ka)
            {
                if (!db.Contains(k)) { diffs.Add($"{path}[{k}]: missing after load"); continue; }
                Compare(da[k], db[k], $"{path}[{k}]", diffs, max, ignoreKeyOrder);
            }
            return;
        }

        if (a is IEnumerable ea && a is not string)
        {
            var la = ea.Cast<object?>().ToList();
            var lb = ((IEnumerable)b).Cast<object?>().ToList();
            if (la.Count != lb.Count) { diffs.Add($"{path}: count {la.Count} vs {lb.Count}"); return; }
            if (IsSet(t))
            {
                // Sets compare as sets: membership is the state, enumeration order is not.
                var sb = new HashSet<object?>(lb);
                foreach (var x in la) if (!sb.Contains(x)) diffs.Add($"{path}: {Show(x)} missing after load");
                return;
            }
            for (int i = 0; i < la.Count; i++) Compare(la[i], lb[i], $"{path}[{i}]", diffs, max, ignoreKeyOrder);
            return;
        }

        foreach (var m in Members(t))
        {
            if (NotSaved.ContainsKey(Key(t, m.Name))) continue;
            Compare(m.Get(a), m.Get(b), $"{path}.{m.Name}", diffs, max, ignoreKeyOrder);
        }
    }

    // ---- population coverage ---------------------------------------------

    /// <summary>
    /// Walks a graph and records, per "Type.Property", whether any instance held a
    /// non-default value. A property that is never populated in the test world is
    /// not being tested, whatever the comparer says.
    /// </summary>
    public static Dictionary<string, bool> Populated(object root)
    {
        var map = new Dictionary<string, bool>();
        Walk(root, map, new HashSet<object>(ReferenceEqualityComparer.Instance));
        return map;
    }

    private static void Walk(object? o, Dictionary<string, bool> map, HashSet<object> seen)
    {
        if (o is null || IsLeaf(o.GetType())) return;
        if (!o.GetType().IsValueType && !seen.Add(o)) return;

        if (o is IDictionary d) { foreach (var v in d.Values) Walk(v, map, seen); return; }
        if (o is IEnumerable e && o is not string) { foreach (var v in e) Walk(v, map, seen); return; }

        var t = o.GetType();
        foreach (var m in Members(t))
        {
            string key = Key(t, m.Name);
            object? v = m.Get(o);
            bool populated = !IsDefault(v);
            map[key] = map.GetValueOrDefault(key) || populated;
            Walk(v, map, seen);
        }
    }

    /// <summary>Every "Type.Property" statically reachable from <paramref name="root"/>, populated or not.</summary>
    public static HashSet<string> Declared(Type root)
    {
        var keys = new HashSet<string>();
        var seen = new HashSet<Type>();
        void Visit(Type t)
        {
            if (t.IsArray) { Visit(t.GetElementType()!); return; }
            if (t.IsGenericType && !IsLeaf(t)) { foreach (var g in t.GetGenericArguments()) Visit(g); return; }
            if (IsLeaf(t) || t == typeof(object) || !seen.Add(t)) return;
            foreach (var m in Members(t)) { keys.Add(Key(t, m.Name)); Visit(m.Type); }
        }
        Visit(root);
        return keys;
    }

    public static bool IsDefault(object? v)
    {
        if (v is null) return true;
        var t = v.GetType();
        if (v is string s) return s.Length == 0;
        if (v is IDictionary d) return d.Count == 0;
        if (v is IEnumerable e) return !e.Cast<object?>().Any();
        if (t.IsValueType) return Equals(v, Activator.CreateInstance(t));
        return false;
    }

    // ---- making the test world exercise every field -------------------------

    /// <summary>
    /// A sim run leaves some fields at their defaults everywhere (a rare flag, a feature
    /// nobody triggered). So that a save bug cannot hide there, give each such scalar
    /// property a non-default value on one existing instance of its type. Properties on
    /// types with no instance at all (the world has no Decision yet) need the caller to
    /// add one; <see cref="Populated"/> then reports whatever is still empty.
    /// Only ever call this on a world that is about to be saved, never one that will run on.
    /// </summary>
    public static void FillUnpopulated(World world)
    {
        var populated = Populated(world);
        var instances = new Dictionary<Type, object>();
        Collect(world, instances, new HashSet<object>(ReferenceEqualityComparer.Instance));

        foreach (var (type, instance) in instances)
        foreach (var m in Members(type))
        {
            if (m.Set is null || populated.GetValueOrDefault(Key(type, m.Name))) continue;
            if (NotSaved.ContainsKey(Key(type, m.Name))) continue;
            if (Sample(m.Type) is { } v) m.Set(instance, v);
            else if (m.Get(instance) is ISet<string> set) set.Add("sample");
        }
    }

    private static void Collect(object? o, Dictionary<Type, object> map, HashSet<object> seen)
    {
        if (o is null || IsLeaf(o.GetType()) || !seen.Add(o)) return;
        if (o is IDictionary d) { foreach (var v in d.Values) Collect(v, map, seen); return; }
        if (o is IEnumerable e && o is not string) { foreach (var v in e) Collect(v, map, seen); return; }
        map.TryAdd(o.GetType(), o);
        foreach (var m in Members(o.GetType())) Collect(m.Get(o), map, seen);
    }

    private static object? Sample(Type t)
    {
        var u = Nullable.GetUnderlyingType(t);
        if (u is not null) return Sample(u);
        if (t == typeof(bool)) return true;
        if (t == typeof(int)) return 7;
        if (t == typeof(long)) return 7L;
        if (t == typeof(double)) return 0.5;
        if (t == typeof(decimal)) return 5m;
        if (t == typeof(string)) return "sample";
        if (t == typeof(DateOnly)) return new DateOnly(2030, 1, 2);
        if (t == typeof(Money)) return new Money(1234m);
        if (t == typeof(Rating)) return new Rating(33);
        if (t.IsEnum) return Enum.GetValues(t).Cast<object>().First(x => !Equals(x, Activator.CreateInstance(t)));
        if (t.IsValueType && t.GetConstructor(new[] { typeof(int) }) is { } ctor) return ctor.Invoke(new object[] { 1 });   // id structs
        return null;
    }

    // ---- shared helpers ---------------------------------------------------

    private sealed record Member(string Name, Type Type, Func<object, object?> Get, Action<object, object?>? Set);

    private static readonly Dictionary<Type, Member[]> MemberCache = new();

    private static Member[] Members(Type t)
    {
        lock (MemberCache)
        {
            if (MemberCache.TryGetValue(t, out var cached)) return cached;
            var list = new List<Member>();
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0 || !p.CanRead) continue;
                // State is what can be set (set or init) plus get-only collections.
                // Plain get-only scalars are computed views (FullName, NetProfit) of state already compared.
                bool isCollection = typeof(IEnumerable).IsAssignableFrom(p.PropertyType) && p.PropertyType != typeof(string);
                if (p.SetMethod is null && !isCollection) continue;
                var pi = p;
                list.Add(new Member(p.Name, p.PropertyType, o => pi.GetValue(o),
                    p.SetMethod is null ? null : (o, v) => pi.SetValue(o, v)));
            }
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var fi = f;
                list.Add(new Member(f.Name, f.FieldType, o => fi.GetValue(o), (o, v) => fi.SetValue(o, v)));
            }
            var arr = list.ToArray();
            MemberCache[t] = arr;
            return arr;
        }
    }

    public static string Key(Type t, string prop) => $"{t.Name}.{prop}";

    /// <summary>Every "Type.Property" key the model declares (for stale allow-list detection).</summary>
    public static bool DeclaresProperty(string key)
    {
        var parts = key.Split('.');
        var type = typeof(World).Assembly.GetTypes().FirstOrDefault(x => x.Name == parts[0]);
        return type?.GetProperty(parts[1], BindingFlags.Public | BindingFlags.Instance) is not null;
    }

    private static bool IsLeaf(Type t) =>
        t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal)
        || t == typeof(DateOnly) || t == typeof(DateTime) || t == typeof(TimeSpan)
        // Every struct in the model (ids, Rating, Money) is a value with its own equality.
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

    public static string Describe(IEnumerable<string> lines)
    {
        var sb = new StringBuilder();
        foreach (var l in lines) sb.AppendLine(l);
        return sb.ToString();
    }
}
