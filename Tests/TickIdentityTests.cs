using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Living;

namespace MmaManager.Tests;

/// <summary>
/// The weekly tick's performance work must not change one outcome. These are digests of whole worlds (every public
/// member of the model graph, dictionaries walked in their enumeration order, so a changed ORDER shows too) after two
/// years of ticking (four for the small Origins world), captured from main at 3bca307 before the tick-perf changes (branch world/tick-perf). A pure
/// performance change keeps every one of them; a change that moves a draw, a tie-break or an iteration order that
/// feeds a roll turns this red. When the sim changes ON PURPOSE (anything that moves outcomes), recapture: set
/// TICK_IDENTITY_OUT to a file path and run this class; it writes the new values instead of asserting.
/// </summary>
public class TickIdentityTests
{
    private static readonly Dictionary<string, string> Expected = new(StringComparer.Ordinal)
    {
        ["modern-ai-11"] = "223615FA4C881CD37696",       // 2027-12-30, 4633 events, 7466 fighters
        ["modern-player-4"] = "9A2E193816E4EB50E233",    // 2027-12-30, 4569 events, 7640 fighters
        ["origins-player-3"] = "C062AC7C8F8F935C5C3F",   // 1996-12-27, 68 events, 174 fighters
    };

    public static IEnumerable<object[]> Cases() => Expected.Keys.Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(Cases))]
    public void TwoYearsOfTicking_MatchMainBeforeThePerfWork(string key)
    {
        var world = Build(key);
        var tick = new WorldTick();
        int weeks = key.StartsWith("origins") ? 208 : 104;   // the young sport is small: give it four years
        for (int w = 0; w < weeks; w++) tick.AdvanceWeek(world);
        string digest = Digest(world);

        string? outPath = Environment.GetEnvironmentVariable("TICK_IDENTITY_OUT");
        if (outPath is not null)
        {
            File.AppendAllText(outPath, $"        [\"{key}\"] = \"{digest}\",   // {world.CurrentDate:yyyy-MM-dd}, {world.Events.Count} events, {world.Fighters.Count} fighters\n");
            return;
        }
        Assert.Equal(Expected[key], digest);
    }

    private static World Build(string key)
    {
        var parts = key.Split('-');
        ulong seed = ulong.Parse(parts[^1]);
        if (parts[0] == "origins") return new WorldBuilder().Build(WorldBuilder.Options.Origins(seed, 0));
        var world = new WorldBuilder().Build(new WorldBuilder.Options { Seed = seed });
        if (parts[1] == "ai")
        {
            world.PlayerPromotion = default;
            foreach (var p in world.Promotions.Values) p.IsPlayerControlled = false;
        }
        return world;
    }

    // ---- the digest (strict: enumeration order counts; mirrors tools/longsim/Check/WorldGraph.Digest) ----

    private static readonly HashSet<string> Skip = new() { "World.ArtCatalog", "FightOutcome.PlayByPlay" };

    internal static string Digest(World world)
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var sb = new StringBuilder(1 << 16);
        Walk(world, sb, h);
        Flush(sb, h);
        return Convert.ToHexString(h.GetHashAndReset())[..20];
    }

    private static void Flush(StringBuilder sb, IncrementalHash h)
    {
        if (sb.Length == 0) return;
        h.AppendData(Encoding.UTF8.GetBytes(sb.ToString()));
        sb.Clear();
    }

    private static void Walk(object? o, StringBuilder sb, IncrementalHash h)
    {
        if (sb.Length > 60_000) Flush(sb, h);
        if (o is null) { sb.Append('~'); return; }
        var t = o.GetType();
        if (IsLeaf(t)) { sb.Append(Leaf(o)).Append(';'); return; }
        if (o is IDictionary d)
        {
            sb.Append('{');
            foreach (var k in d.Keys) { sb.Append(Leaf(k)).Append('='); Walk(d[k], sb, h); }
            sb.Append('}');
            return;
        }
        if (o is IEnumerable e && o is not string)
        {
            sb.Append('[');
            if (IsSet(t))
                foreach (var x in e.Cast<object?>().Select(Leaf).OrderBy(x => x, StringComparer.Ordinal)) sb.Append(x).Append(',');
            else foreach (var x in e) Walk(x, sb, h);
            sb.Append(']');
            return;
        }
        sb.Append('(').Append(t.Name).Append(':');
        foreach (var (name, get) in Members(t))
        {
            if (Skip.Contains($"{t.Name}.{name}")) continue;
            Walk(get(o), sb, h);
        }
        sb.Append(')');
    }

    private static string Leaf(object? o) => o switch
    {
        null => "~",
        double d => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        float f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        decimal m => m.ToString(System.Globalization.CultureInfo.InvariantCulture),
        Money mo => mo.Amount.ToString(System.Globalization.CultureInfo.InvariantCulture),
        IFormattable fm => fm.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => o.ToString() ?? "?",
    };

    private static readonly Dictionary<Type, (string, Func<object, object?>)[]> Cache = new();

    private static (string, Func<object, object?>)[] Members(Type t)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(t, out var c)) return c;
            var list = new List<(string, Func<object, object?>)>();
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0 || !p.CanRead) continue;
                bool isCollection = typeof(IEnumerable).IsAssignableFrom(p.PropertyType) && p.PropertyType != typeof(string);
                if (p.SetMethod is null && !isCollection) continue;
                var pi = p;
                list.Add((p.Name, o => pi.GetValue(o)));
            }
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var fi = f;
                list.Add((f.Name, o => fi.GetValue(o)));
            }
            return Cache[t] = list.ToArray();
        }
    }

    private static bool IsLeaf(Type t) =>
        t.IsPrimitive || t.IsEnum || t == typeof(string) || t == typeof(decimal)
        || t == typeof(DateOnly) || t == typeof(DateTime) || t == typeof(TimeSpan)
        || (t.IsValueType && !t.IsGenericType)
        || Nullable.GetUnderlyingType(t) is not null;

    private static bool IsSet(Type t) =>
        t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISet<>));
}
