using MmaManager.Core.Model;
using MmaManager.Core.Sim.Org;

namespace MmaManager.Core.Sim.Contracts;

/// <summary>
/// AI roster upkeep: each month the promotions shed fighters they no longer
/// want (see <see cref="RosterPolicy"/>) — the big stages trimming journeymen
/// on losing skids — so their rosters stay winner-heavy and the cut fighters
/// slide down the ladder to promotions that will still book them. Bounded per
/// org so nobody guts their roster overnight, and a division is never cut below
/// the bodies it needs to run a card. The player's roster is theirs to manage.
/// </summary>
public static class RosterManagement
{
    private const int MaxCutsPerOrg = 3;
    private const int MinPerDivision = 4;

    public static int RunMonthly(World world, DateOnly date)
    {
        int cuts = 0;
        foreach (var promo in world.Promotions.Values
                     .Where(p => p.Id != world.PlayerPromotion && !p.Defunct)
                     .OrderByDescending(p => p.Prestige.Value)
                     .ThenBy(p => p.Id.Value))
        {
            // Worst first: deepest skid, then worst win/loss margin.
            var unwanted = world.RosterOf(promo.Id)
                .Where(f => f.Status == FighterStatus.Active && !RosterPolicy.Wants(world, promo, f))
                .OrderBy(f => f.Record.CurrentStreak)
                .ThenBy(f => f.Record.Wins - f.Record.Losses)
                .ToList();

            int cutThisOrg = 0;
            foreach (var f in unwanted)
            {
                if (cutThisOrg >= MaxCutsPerOrg) break;
                int inDivision = world.RosterOf(promo.Id)
                    .Count(x => x.WeightClass == f.WeightClass && x.Sex == f.Sex && x.Status == FighterStatus.Active);
                if (inDivision <= MinPerDivision) continue;   // keep enough to run the division

                ContractLifecycle.Release(world, f, dentReputation: false);   // routine business, not a betrayal
                cutThisOrg++;
                cuts++;
            }
        }
        return cuts;
    }
}
