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
/// member of the model graph, dictionaries walked in their enumeration order, so a changed ORDER shows too) after the
/// years each key names (mode-kind-seed-yYears: an all-AI or player world today, a player or observer world in 1993),
/// captured from main at f8469e1, built on its own without any tick-perf change (branch world/tick-perf). A pure
/// performance change keeps every one of them; a change that moves a draw, a tie-break or an iteration order that
/// feeds a roll turns this red. When the sim changes ON PURPOSE (anything that moves outcomes), recapture: set
/// TICK_IDENTITY_OUT to a file path and run this class; it writes the new values instead of asserting.
/// Recaptured on world/matchmaker-fog (PR #81: houses decide on their scouted read, not true ability). The two modern
/// pins move with the fog fix. Of the Origins pins, the two player worlds move ONLY through the news gate (with the
/// old true-ability gate they match the earlier pins exactly); observer-5-y10 moves only through Rookie of the Year's
/// tie-break (now the public record), and observer-6-y20 through both. (Measured by putting each back in turn: the
/// pioneer-arrival price and the matchmaker's read move none of the four.)
/// Recaptured on engine/unified-size-term: origins-observer-6-y20 only. A banded or kept open-weight bout now gets its
/// size term under today's rules too (a cage house under the commissions' code that has split); by 2012 such bouts are
/// fought in that world, so their results move. No other key changed: the modern worlds never set the flag.
/// Recaptured on world/perceived-potential (houses and rooms decide on their scouted read of a man's CEILING,
/// ScoutingService.PerceivedPotential, never his hidden potential). Both modern pins move: the promo push, the camp
/// stipend, RosterPolicy.Wants and the protégé each move them alone, and the player's bigger-camp mail moves the player
/// world alone (the mail is in the digest). Of the Origins pins, origins-player-3-y4 never moves; origins-player-8-y10
/// and origins-observer-5-y10 move only through the head coach's protégé choice (with that one site back on the truth
/// they match the pins above exactly); origins-observer-6-y20 moves through the protégé and RosterPolicy.Wants (by 2012
/// its houses keep and sign on the young-prospect leash), and with both back on the truth it matches exactly. The push,
/// the stipend and the mail move no Origins pin. Measured by putting each site back on the truth in turn.
/// Recaptured on world/origins-asks (Origins: a man asks a smaller house less, Negotiator.HouseTier; and the market reads
/// the player's house as an AI's, Negotiator.MarketPurseCap). All four Origins pins move, every one through the house
/// tier alone: with the tier off (HouseTier returning 1) and the market read on, all six keys match the pins above
/// exactly. The modern pins never move (the tier is 1 and the market read is the purse cap in today's mode).
/// </summary>
public class TickIdentityTests
{
    private static readonly Dictionary<string, string> Expected = new(StringComparer.Ordinal)
    {
        ["modern-ai-11-y2"] = "BC76F19D9CD508595457",   // 2027-12-30, 4632 events, 7545 fighters
        ["modern-player-4-y2"] = "ABDC9C08D426744DC856",   // 2027-12-30, 4570 events, 7586 fighters
        ["origins-player-3-y4"] = "1F7F79E93660ED770AA7",   // 1996-12-27, 68 events, 176 fighters
        ["origins-player-8-y10"] = "1F549F5BB1DEA0B9F32B",   // 2002-12-20, 193 events, 304 fighters
        ["origins-observer-5-y10"] = "10EB58C55CDDD08E58A0",   // 2002-12-20, 306 events, 423 fighters
        ["origins-observer-6-y20"] = "3AC8967C71B2A588EB11",   // 2012-12-07, 827 events, 1046 fighters
    };

    public static IEnumerable<object[]> Cases() => Expected.Keys.Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(Cases))]
    public void Ticking_MatchesMainWithoutThePerfWork(string key)
    {
        var world = Build(key);
        var tick = new WorldTick();
        int weeks = 52 * int.Parse(key.Split('-')[^1][1..]);
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
        ulong seed = ulong.Parse(parts[2]);
        if (parts[0] == "origins")
            return new WorldBuilder().Build(parts[1] == "observer" ? WorldBuilder.Options.OriginsObserver(seed) : WorldBuilder.Options.Origins(seed, 0));
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
