using MmaManager.Core.Model;
using MmaManager.Core.Sim.Org;
using MmaManager.Core.Sim.Rng;

namespace MmaManager.Core.Sim.Generation;

/// <summary>
/// One person, one name, for the back office too. <see cref="StaffService"/> pastes generated staff names
/// from two 16-word lists with no check (about five repeated pairs a world, and some match a fighter's).
/// After the staff are seeded, this pass renames any GENERATED staffer whose full name is already taken: by
/// a fighter, an authored person (designed staff, owners, legends, pioneers...), a real notable person, or
/// an earlier staffer. It draws nothing from any shared stream: each rename is keyed off the staffer's place
/// (promotion id or the market, role, position in the list), which is stable because staff have no id of
/// their own. Authored staff keep their names. Staff are only generated at world build (nothing refreshes
/// the market or seeds a startup's desks later), so a day-one pass covers every site; fighters minted later
/// check against live staff names through <see cref="NameRegistry"/>.
/// </summary>
internal static class StaffNaming
{
    private const ulong Salt = 0x57AF_F4A3_E5UL;

    private static string[] W(string names) => names.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Extra first names and surnames for generated staff (the base lists are 16 x 16). Mixed and international
    /// like the base lists; no authored surname, no famous full name (the registry also blocks those).
    /// </summary>
    internal static readonly string[] ReserveFirst = W(
        "Helena Samir Anika Louis Camille Kenji Bianca Joel Tanya Rashid Imogen Pavel Yusuf Clara Hector Amara " +
        "Felix Ingrid Julian Leila Oskar Paloma Quinn Rosa Stefan Tessa Viktor Wendy Zara Aaron Mirela Idris");

    internal static readonly string[] ReserveLast = W(
        "Aldridge Bellamy Castellan Dunmore Ellsworth Fontaine Garibaldi Hartwell Jovanovic Wojtowicz Lindgren " +
        "Marchetti Oyelaran Pellegrini Quintana Rosenberg Salgado Underwood Valdivia Whitcombe Yamada Zielinska " +
        "Abernathy Brasswell Carrillo Devereaux Eastman Fairley Gutierrez Halloway Imbert Keswick Lombardi");

    public static void Resolve(World world)
    {
        var authored = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in DesignedStaff.Staff) authored.Add(s.Name);
        var names = NameRegistry.Build(world, withStaff: false);

        foreach (var promo in world.Promotions.Values.OrderBy(p => p.Id.Value))
        {
            if (!string.IsNullOrWhiteSpace(promo.OwnerName)) names.AddName(promo.OwnerName!);
            Pass(world, promo.Staff, (ulong)promo.Id.Value, authored, names);
        }
        Pass(world, world.StaffMarket, 0UL, authored, names);
    }

    private static void Pass(World world, List<StaffMember> list, ulong place, HashSet<string> authored, NameRegistry names)
    {
        for (int i = 0; i < list.Count; i++)
        {
            var s = list[i];
            if (authored.Contains(s.Name)) { names.AddName(s.Name); continue; }
            var sp = s.Name.Split(' ', 2);
            if (sp.Length == 2 && !names.Taken(sp[0], sp[1])) { names.AddName(s.Name); continue; }

            var rng = new SimRandom(SimRandom.Mix(world.RngSeed, (place * 1_000_003UL + (ulong)s.Role * 8_191UL + (ulong)i) ^ Salt));
            var firsts = StaffService.FirstNames.Concat(ReserveFirst).ToArray();
            var lasts = StaffService.LastNames.Concat(ReserveLast).ToArray();
            string first = sp[0], last = sp.Length == 2 ? sp[1] : "";
            for (int attempt = 0; attempt < 96 && (last.Length == 0 || names.Taken(first, last)); attempt++)
            {
                first = rng.Pick(firsts);
                last = rng.Pick(lasts);
            }
            string stem = last;
            string[] numerals = { "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };
            for (int n = 0; names.Taken(first, last); n++)
                last = n < numerals.Length ? $"{stem} {numerals[n]}" : $"{stem} {n + 2}";

            var renamed = new StaffMember
            {
                Role = s.Role,
                Name = $"{first} {last}",
                Quality = s.Quality,
                MonthlySalary = s.MonthlySalary,
                Background = s.Background,
            };
            list[i] = renamed;
            names.AddName(renamed.Name);
        }
    }
}
