using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Rng;
using MmaManager.Persistence;

namespace LongSim.Check;

public enum RunKind { Ai, Founded, Takeover }

/// <summary>One world being driven month by month, with its checker and (optionally) a scripted player.</summary>
public sealed class SimRun
{
    public string Label { get; private set; } = "";
    public ulong Seed { get; }
    public RunKind Kind { get; }
    public int TakeoverIndex { get; }
    public World World { get; private set; }
    public WorldTick Tick { get; } = new();
    public PlayerScript? Script { get; private set; }
    public WorldChecker Checker { get; private set; }
    public int Months { get; private set; }
    public long TickMillis { get; private set; }
    public long CheckMillis { get; private set; }
    public volatile int Heartbeat;
    private readonly ViolationLog _log;

    private SimRun(ulong seed, RunKind kind, int takeoverIndex, World world, ViolationLog log, string label, string tag = "")
    {
        Seed = seed; Kind = kind; TakeoverIndex = takeoverIndex; World = world; _log = log; Label = label;
        Checker = new WorldChecker(log, label, tag);
    }

    public static string LabelFor(ulong seed, RunKind kind, int idx) => kind switch
    {
        RunKind.Ai => $"ai#{seed}",
        RunKind.Founded => $"founded#{seed}",
        _ => $"takeover{idx}#{seed}",
    };

    /// <summary>Which player scenario a playcheck seed gets: odd seeds found a promotion, even ones take over a designed one.</summary>
    public static (RunKind Kind, int Index) PlayScenario(ulong seed) =>
        seed % 2 == 1 ? (RunKind.Founded, 0) : (RunKind.Takeover, 2 + (int)((seed / 2) % 9));   // 2..10: IFA .. SCL, the mid-size designed houses

    public static SimRun Create(ulong seed, RunKind kind, int takeoverIndex, ViolationLog log, bool script)
    {
        WorldBuilder.Options o = kind switch
        {
            RunKind.Founded => new WorldBuilder.Options
            {
                Seed = seed,
                Founding = new WorldBuilder.FoundingSpec
                {
                    Name = "Check Fight Co", Abbreviation = "CFC",
                    Region = (Region)((int)(seed % 5)),
                    Prestige = 5 + (int)(seed % 4) * 4,
                    StartingBalance = new Money(250_000m),
                },
            },
            RunKind.Takeover => new WorldBuilder.Options { Seed = seed, PlayerPromotionIndex = takeoverIndex },
            _ => new WorldBuilder.Options { Seed = seed },
        };
        var world = new WorldBuilder().Build(o);
        if (kind == RunKind.Ai)
        {
            // All-AI: the matchmaker skips whoever is flagged as the player's, and the flag also switches on player-only
            // staff costs and effects. Clear both (compare/diagnose only clear PlayerPromotion; see README).
            world.PlayerPromotion = default;
            foreach (var p in world.Promotions.Values) p.IsPlayerControlled = false;
        }
        var label = LabelFor(seed, kind, takeoverIndex);
        var run = new SimRun(seed, kind, takeoverIndex, world, log, label);
        if (script && kind != RunKind.Ai)
        {
            run.Script = new PlayerScript(label, log, reckless: kind == RunKind.Founded && seed % 3 == 0);
            run.Checker.Context = () => run.Script.Recent(6);
        }
        return run;
    }

    /// <summary>Advance one calendar month (week by week, as the UI does) and check the world at the boundary.</summary>
    public void StepMonth(bool check = true)
    {
        int month = World.CurrentDate.Month;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        do
        {
            if (Script is not null) Script.Week(World, Tick); else Tick.AdvanceWeek(World);
            Heartbeat++;
        } while (World.CurrentDate.Month == month);
        TickMillis += sw.ElapsedMilliseconds;
        Months++;
        if (check)
        {
            sw.Restart();
            Checker.Check(World);
            CheckMillis += sw.ElapsedMilliseconds;
        }
    }

    public void CheckNow() => Checker.Check(World);

    /// <summary>Save this world to <paramref name="path"/>, load it back, and return a twin run that continues from the loaded copy.</summary>
    public (SimRun Twin, WorldGraph.DiffResult RoundTrip) ForkViaSave(string path, ViolationLog log)
    {
        SaveGame.Save(World, path);
        var loaded = SaveGame.Load(path);
        try { File.Delete(path); } catch { /* scratch file */ }
        var rt = WorldGraph.Diff(World, loaded);
        var twin = new SimRun(Seed, Kind, TakeoverIndex, loaded, log, Label, tag: "[loaded twin] ");
        if (Script is not null)
        {
            twin.Script = new PlayerScript(twin.Label, log, reckless: Kind == RunKind.Founded && Seed % 3 == 0);
            twin.Checker.Context = () => twin.Script.Recent(6);
        }
        twin.Months = Months;
        return (twin, rt);
    }

    public static ulong HashSeed(ulong seed, string salt)
    {
        ulong h = 1469598103934665603UL;
        foreach (char c in salt) h = (h ^ c) * 1099511628211UL;
        return SimRandom.Mix(seed, h);
    }
}
