using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Org;

/// <summary>
/// Who a promotion wants on its books. The giants keep winners, champions, and
/// young prospects — they won't roster a journeyman with more losses than wins.
/// Smaller promotions are far more tolerant: they need bodies to run cards, so
/// they hold on to almost anyone. This one policy drives who the AI re-signs at
/// expiry, who it signs off the market, and who it cuts — so losing fighters
/// naturally slide DOWN the ladder to the scenes that will still book them.
/// Never applied to the player, who runs their own roster.
/// </summary>
public static class RosterPolicy
{
    private const int EnoughBoutsToJudge = 5;

    public static bool Wants(World world, Promotion promo, Fighter f)
    {
        // Always keep the marquee: champions and the top of the contender pile.
        if (world.Titles.Values.Any(t => t.CurrentChampion == f.Id)) return true;
        var div = promo.Divisions.FirstOrDefault(d => d.Matches(f));
        if (div is not null && div.Rankings.Take(3).Contains(f.Id)) return true;

        // Young, high-ceiling projects and fighters too green to judge get a leash.
        // (Retention leans on the PUBLIC record — a proven loser is cut regardless of
        // fog; Fog of War bites when ACQUIRING unproven talent, not when shedding
        // known ones — so this stays on the true ceiling.)
        if (f.AgeOn(world.CurrentDate) <= 25 && f.PotentialAbility.Value >= 70) return true;
        if (f.Record.TotalBouts < EnoughBoutsToJudge) return true;

        // A genuine draw earns their spot — promotions keep (and chase) the names
        // that sell tickets at their level, not just the best fighters.
        if (f.Intangibles.Popularity.Value >= OrgSizing.DrawFloor(OrgSizing.SizeOf(promo))) return true;

        bool losingRecord = f.Record.Losses > f.Record.Wins;
        int skid = f.Record.CurrentStreak;

        return OrgSizing.SizeOf(promo) switch
        {
            // The big stages want winners: a losing record OR even a winning
            // fighter on a real skid is shown the door.
            OrgSize.International or OrgSize.Continental => !(losingRecord || skid <= -3),
            // National orgs still won't carry sub-.500 fighters, but they're
            // patient with a winner going through a rough patch.
            OrgSize.National => !losingRecord,
            // The regional scene keeps almost everyone; only a collapse ends it.
            _ => skid > -4,
        };
    }
}
