namespace MmaManager.Core.Sim.Living;

/// <summary>
/// Something outside the sim that wants to know where a tick spends its time (longsim's
/// <c>tickprofile</c>). The sim never reads a clock itself: it only names the phases it enters and
/// leaves, and counts calls of the helpers worth counting; the listener owns the stopwatch.
/// </summary>
public interface ITickProbe
{
    void Enter(string phase);
    void Exit(string phase);
    void Count(string helper);
}

/// <summary>
/// The probe hook. Off (null) everywhere except a profiling run, where a phase costs one null check.
/// Thread-static, so a parallel test run or a parallel longsim job never sees another thread's listener.
/// It never feeds a roll, a decision or the world: outcomes are the same with it on or off.
/// </summary>
public static class TickProbe
{
    [ThreadStatic] private static ITickProbe? _current;

    public static ITickProbe? Current { get => _current; set => _current = value; }

    /// <summary><c>using (TickProbe.Phase("monthly/expiries")) { ... }</c></summary>
    public static Scope Phase(string name)
    {
        var p = _current;
        p?.Enter(name);
        return new Scope(p, name);
    }

    public static void Count(string helper) => _current?.Count(helper);

    public readonly struct Scope : IDisposable
    {
        private readonly ITickProbe? _probe;
        private readonly string _name;
        internal Scope(ITickProbe? probe, string name) { _probe = probe; _name = name; }
        public void Dispose() => _probe?.Exit(_name);
    }
}
