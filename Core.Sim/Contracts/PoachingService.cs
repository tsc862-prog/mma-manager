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
    /// Whether a bigger org would sign the player's run-out star away right now.
    /// Not called by the tick since v45 — the player's deals end into an exclusive
    /// re-sign window (<see cref="DealWindow"/>) and then free agency, with no poach
    /// at expiry; a rival's interest reaches him as a concrete offer on his last
    /// fight (<see cref="OfferMarket"/>, the same gates). Kept because LiferTests and
    /// ProgressionTests use it to probe the poach rules (who takes him, a lifer's refusal).
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

    /// <summary>
    /// Whether a real rival is after this fighter right now: some promotion on a
    /// bigger stage than his own, solvent, with room in his division, that would
    /// pursue him on ITS OWN read (the same gates <see cref="TryPoach"/> uses). A
    /// public fact about the market — who is calling — never his true ability.
    /// A holding lifer doesn't count: he says no to the call.
    /// </summary>
    public static bool HasSuitor(World world, Fighter f) =>
        f.Contract is { } c && world.Get(c.Promotion) is { } holder
        && BestSuitor(world, holder, f) is not null
        && !Lifers.IsHolding(world, f);

    // The most prestigious org on a bigger stage that runs this fighter's division
    // AND would actually want them — talent that fits what a promotion at that level
    // is looking for, not a blanket fame gate.
    internal static Promotion? BestSuitor(World world, Promotion from, Fighter f)
    {
        var fromSize = OrgSizing.SizeOf(from);
        return world.Promotions.Values
            .Where(p => !p.Defunct && p.Id != from.Id)
            .Where(p => p.Id != world.PlayerPromotion)                        // the player signs their own — never auto-acquires
            .Where(p => Solvency.Distress(p) == 0)                            // a bleeding org doesn't go star shopping —
                                                                              // poaching pays the loyalty premium, the most
                                                                              // expensive signing in the sport
            .Where(p => OrgSizing.SizeOf(p) > fromSize)                       // a genuinely bigger stage
            .Where(p => HasRoom(world, p, f))
            .Where(p => WouldPursue(world, p, f))                             // and a fighter they'd roster
            .OrderByDescending(p => p.Prestige.Value)
            .ThenBy(p => p.Id.Value)
            .FirstOrDefault();
    }

    /// <summary>
    /// Whether <paramref name="p"/> runs this fighter's division and has ROOM in it
    /// (a couple of seats of slack — a signing worth poaching is worth squeezing in;
    /// a real DRAW always is). Without this, the giants hoarded every fighter the
    /// deepening talent base produced and simply ate the hoarding retainer: AFC's
    /// roster ran to ~190 on a target of ~110. Public facts only (rosters, popularity).
    /// </summary>
    internal static bool HasRoom(World world, Promotion p, Fighter f) =>
        p.Divisions.Any(d => d.Matches(f))
        && (f.Intangibles.Popularity.Value >= OrgSizing.DrawFloor(OrgSizing.SizeOf(p))
            || world.RosterOf(p.Id).Count(x => x.Status != FighterStatus.Retired
                    && p.Divisions.Any(d => d.Matches(f) && d.Matches(x)))
               < OrgSizing.RosterTargetPerDivision(OrgSizing.SizeOf(p)) + 2);

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
