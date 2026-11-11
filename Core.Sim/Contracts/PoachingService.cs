using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Org;

namespace MmaManager.Core.Sim.Contracts;

/// <summary>
/// The talent treadmill: the giants come for a promotion's homegrown stars when
/// their deals run out. A fighter worth having, on an expiring deal, gets courted
/// by any promotion on a genuinely bigger stage that would roster them — and if
/// the current org can't hold them, the star walks (vacating any belt on the way
/// out). This runs BOTH ways: bigger AI orgs skim the best expiring talent off
/// smaller AI orgs, and the giants come for the player's stars too (with a warning
/// first). Develop them, then watch them get taken: the core tension of the climb.
/// </summary>
public static class PoachingService
{
    private static readonly Negotiator Negotiator = new();
    private static readonly FighterValuation Valuation = new();

    /// <summary>
    /// Monthly: warn the owner (once per contract) when a bigger org is circling
    /// a star whose deal is down to its last fight — their cue to re-sign.
    /// </summary>
    public static void WarnExpiring(World world, DateOnly date)
    {
        if (world.Get(world.PlayerPromotion) is not { } player) return;
        foreach (var f in world.RosterOf(player.Id).OrderBy(f => f.Id.Value).ToList())
        {
            if (f.Status != FighterStatus.Active || f.Contract is not { } c) continue;
            if (c.PoachWarned || c.FightsRemaining > 1) continue;   // the fight before free agency
            if (BestSuitor(world, player, f) is not { } suitor) continue;

            // A lifer doesn't need a warning — the call came, and he said no.
            if (Lifers.IsHolding(world, f)) { Lifers.TurnDown(world, f, player, suitor, date); continue; }
            // He said no on this deal and has since soured: his warning comes at
            // expiry with a month's grace (TryPoach), never in the same tick as a poach.
            if (c.BondNoticeOn == Contract.RefusalTold) continue;

            c.PoachWarned = true;
            Living.Mail.Post(world, date, MailCategory.ContractAlert, "Matchmaking",
                $"{suitor.Abbreviation} is circling {f.FullName}",
                $"{f.FullName}'s deal is down to its last fight, and {suitor.Name} — a bigger stage than ours — " +
                "is sniffing around. Re-sign them before free agency or we lose them for nothing.",
                aboutFighter: f.Id);
        }
    }

    /// <summary>
    /// At free agency (deal expired), a bigger org may sign the player's star away.
    /// Thin wrapper over <see cref="TryPoach"/> for the player-facing path.
    /// </summary>
    public static bool TryPoachFromPlayer(World world, Fighter f, DateOnly date) =>
        f.Contract?.Promotion == world.PlayerPromotion && TryPoach(world, f, date);

    /// <summary>
    /// A fighter's deal has expired: if a promotion on a genuinely bigger stage
    /// wants them and can land them, they sign there instead of re-upping where
    /// they are. Works for any org — AI-to-AI and player alike. Returns true if the
    /// fighter was poached; a champion vacates the belt on the way out, and the
    /// player gets a board note when it's their fighter.
    /// </summary>
    public static bool TryPoach(World world, Fighter f, DateOnly date)
    {
        if (f.Contract is not { } cur || world.Get(cur.Promotion) is not { } from) return false;
        if (BestSuitor(world, from, f) is not { } suitor) return false;

        // A holding lifer says no whatever the money — and the approach is public.
        if (Lifers.IsHolding(world, f)) { Lifers.TurnDown(world, f, from, suitor, date); return false; }

        // The player's lifer turned this rival down on this deal and has since
        // become poachable: the owner hears it first and gets a month to act.
        if (from.Id == world.PlayerPromotion && cur.BondNoticeOn == Contract.RefusalTold && !cur.PoachWarned)
        {
            cur.PoachWarned = true;
            var why = Lifers.GrievanceOf(world, f, date);
            Living.Mail.Post(world, date, MailCategory.ContractAlert, "Matchmaking",
                $"{suitor.Abbreviation} is circling {f.FullName}",
                Lifers.Gendered(f, $"{f.FullName} turned {suitor.Name} down once on this deal, but his deal has run out and he's listening now"
                    + (why is not null ? $" — {why.Reason}" : "") + ". Re-sign him this month or we lose him to them."),
                aboutFighter: f.Id);
            return false;
        }

        // The suitor stretches its budget to land them — but a fighter loyal to
        // their current org costs far more to pry loose (up to ~5-10x after years
        // of service). If the suitor can't reach that price, the poach falls through
        // and the fighter re-signs where they are.
        var exp = Valuation.Evaluate(world, f);
        if (Negotiator.PoachOffer(world, suitor, f, exp) is not { } offer) return false;

        // Out of the old org's rankings, and any belt they hold there is vacated as they walk.
        ContractLifecycle.LeavePromotion(world, f, from.Id, date,
            $"{suitor.Name} signed him away when his deal ran out", suitor.Id, hisChoice: true);
        f.Contract = null;
        ContractLifecycle.Sign(world, f, offer, date);   // signs to the bigger org

        Living.News.Post(world, date, NewsCategory.Signing,
            $"{suitor.Abbreviation} signs {f.FullName} away from {from.Abbreviation}",
            $"{f.FullName} leaves for the bigger stage.", suitor.Id, f.Id);
        if (from.Id == world.PlayerPromotion)
            Living.Mail.Post(world, date, MailCategory.BoardNote, Living.Mail.Owner(world),
                $"We lost {f.FullName} to {suitor.Abbreviation}",
                $"Their deal ran out and {suitor.Name} outbid us for a bigger stage. This is the treadmill — " +
                "we build them up, the giants take them. Time to find the next one.",
                aboutFighter: f.Id);
        return true;
    }

    // The most prestigious org on a bigger stage that runs this fighter's division
    // AND would actually want them — talent that fits what a promotion at that level
    // is looking for, not a blanket fame gate.
    private static Promotion? BestSuitor(World world, Promotion from, Fighter f)
    {
        var fromSize = OrgSizing.SizeOf(from);
        return world.Promotions.Values
            .Where(p => !p.Defunct && p.Id != from.Id)
            .Where(p => p.Id != world.PlayerPromotion)                        // the player signs their own — never auto-acquires
            .Where(p => Solvency.Distress(p) == 0)                            // a bleeding org doesn't go star shopping —
                                                                              // poaching pays the loyalty premium, the most
                                                                              // expensive signing in the sport
            .Where(p => OrgSizing.SizeOf(p) > fromSize)                       // a genuinely bigger stage
            .Where(p => p.Divisions.Any(d => d.Matches(f)))
            // ...with ROOM in that division (a couple of seats of slack — a
            // signing worth poaching is worth squeezing in; a real DRAW always
            // is). Without this, the giants hoarded every fighter the deepening
            // talent base produced and simply ate the hoarding retainer: AFC's
            // roster ran to ~190 on a target of ~110.
            .Where(p => f.Intangibles.Popularity.Value >= OrgSizing.DrawFloor(OrgSizing.SizeOf(p))
                || world.RosterOf(p.Id).Count(x => x.Status != FighterStatus.Retired
                        && p.Divisions.Any(d => d.Matches(f) && d.Matches(x)))
                   < OrgSizing.RosterTargetPerDivision(OrgSizing.SizeOf(p)) + 2)
            .Where(p => WouldPursue(world, p, f))                             // and a fighter they'd roster
            .OrderByDescending(p => p.Prestige.Value)
            .ThenBy(p => p.Id.Value)
            .FirstOrDefault();
    }

    // Does a promotion at this level actually want this fighter — the same call it
    // makes signing off the free-agent market? Two independent reasons to pursue:
    // the talent is competitive at their stage, OR they're a big enough draw to
    // sell tickets on their name alone (so a promotion will chase a star even a bit
    // light in the cage). A genuine draw also shaves a little off the talent bar.
    // Nobody chases a fighter in free-fall.
    private static bool WouldPursue(World world, Promotion suitor, Fighter f)
    {
        if (f.Record.CurrentStreak <= -3) return false;

        var size = OrgSizing.SizeOf(suitor);
        int pop = f.Intangibles.Popularity.Value;
        int drawAllowance = Math.Min(6, pop / 12);
        // The suitor pursues on its own READ of the fighter (Fog of War), not the
        // true ability — so it can chase a prospect it overrates or pass on a gem it
        // underrates (a famous camp's tape narrows the guesswork). Popularity is
        // public, so the draw path stays exact.
        return Scouting.ScoutingService.PerceivedAbility(world, suitor, f) >= OrgSizing.TalentFloor(size) - drawAllowance
            || pop >= OrgSizing.DrawFloor(size);
    }
}
