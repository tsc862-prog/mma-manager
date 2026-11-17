using MmaManager.Core.Model;
using MmaManager.Core.Sim.Generation.Origins;
using MmaManager.Core.Sim.Living;
using MmaManager.Core.Sim.Org;

namespace MmaManager.Core.Sim.Generation;

/// <summary>
/// Who already has a name. A throwaway set of full names ("First Last", case-blind) and fight names,
/// rebuilt from the world whenever a generated person is minted (never saved, never cached across
/// calls, so a loaded world and a never-saved one agree). The scope is every real fighter ever, active
/// or retired (a retired man becomes a coach, and a coach is a fighter), plus every named staff member
/// and owner, plus the authored people who have not entered the world yet (legends, staff, owners,
/// crossovers, Origins arrivals): a generated person always yields to an authored one. The intentional
/// "Local X" club filler is outside it, as it is outside <see cref="FighterGenerator.EnsureUniqueName"/>.
/// </summary>
internal sealed class NameRegistry
{
    private readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _nicknames = new(StringComparer.OrdinalIgnoreCase);

    public static string Key(string first, string last) => $"{first} {last}";

    public bool Taken(string first, string last) => _names.Contains(Key(first, last));
    public bool NicknameTaken(string nick) => _nicknames.Contains(nick);

    public void Add(Fighter f)
    {
        if (f.LocalTalent) return;
        _names.Add(Key(f.FirstName, f.LastName));
        if (f.Nickname is { } n) _nicknames.Add(n);
    }

    /// <summary>
    /// Everyone currently in <paramref name="world"/> (except <paramref name="except"/>), plus the reserved
    /// authored names and the real notable people nobody here may share a name with.
    /// <paramref name="withStaff"/> false leaves the live staff out (the staff rename pass adds them one by one).
    /// </summary>
    public static NameRegistry Build(World world, FighterId? except = null, bool withStaff = true)
    {
        var r = new NameRegistry();
        foreach (var o in world.Fighters.Values)
            if (except is null || o.Id != except.Value) r.Add(o);
        if (withStaff)
        {
            foreach (var p in world.Promotions.Values)
            {
                if (!string.IsNullOrWhiteSpace(p.OwnerName)) r._names.Add(p.OwnerName!);
                foreach (var s in p.Staff) r._names.Add(s.Name);
            }
            foreach (var s in world.StaffMarket) r._names.Add(s.Name);
        }
        foreach (var n in Reserved(world.Mode)) r._names.Add(n);
        foreach (var n in RealNames.All) r._names.Add(n);
        return r;
    }

    public void AddName(string full) => _names.Add(full);

    private static readonly IReadOnlyList<string> ModernReserved = BuildModern();
    private static readonly IReadOnlyList<string> OriginsReserved = BuildOrigins();

    private static IReadOnlyList<string> Reserved(WorldMode mode) => mode == WorldMode.Origins ? OriginsReserved : ModernReserved;

    private static IReadOnlyList<string> BuildModern()
    {
        var names = new List<string>();
        names.AddRange(DesignedRoster.All.Select(d => Key(d.First, d.Last)));
        names.AddRange(DesignedLegends.All.Select(d => Key(d.First, d.Last)));
        names.AddRange(DesignedStaff.Staff.Select(s => s.Name));
        names.AddRange(DesignedStaff.Owners.Select(o => o.Name));
        names.AddRange(WorldEvents.CrossoverNames);
        // The pioneer portraits are bound by exact "First Last" in every mode (PaintedLibrary.PioneerOf has no
        // mode guard), so a generated man in today's world must not wear a pioneer's name and face.
        names.AddRange(OriginsPioneers.All.Select(p => p.FullName));
        names.AddRange(OriginsArrivals.All.Where(a => a.Pioneer is not null).Select(a => a.Pioneer!.FullName));
        return names;
    }

    private static IReadOnlyList<string> BuildOrigins()
    {
        var names = new List<string>();
        names.AddRange(OriginsPioneers.All.Select(p => p.FullName));
        foreach (var a in OriginsArrivals.All)
        {
            if (a.Pioneer is { } p) names.Add(p.FullName);
            if (a.Lead is { } lead) names.Add(lead);
        }
        names.AddRange(OriginsPromotions.Owners.Select(o => o.Name));
        return names;
    }
}
