using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Org;

/// <summary>
/// Moving UP the ladder is a choice, not an automatic shove. A promotion's prestige
/// is capped at its cleared tier's ceiling; once it's maxed that tier it becomes
/// ELIGIBLE to step up. The player is told by mail and decides when to take the leap
/// (staying put keeps costs low while they build a roster, stars and a war chest).
/// The AI makes the same call by its bankroll — it only promotes when it can afford
/// the bigger stage, so it doesn't get forced into a tier its gate can't pay for and
/// bleed out (exactly the trap that used to fold over-eager climbers).
/// </summary>
public static class PromotionLadder
{
    /// <summary>Has the org maxed its current tier and earned the right to step up?</summary>
    public static bool IsEligible(Promotion p) =>
        p.TierClearance != OrgSize.International
        && p.Prestige.Value >= OrgSizing.PrestigeCeiling(p.TierClearance);

    /// <summary>
    /// In the leap-chasing band: at or NEAR the tier ceiling. Prestige jitters a few
    /// points around the bar month to month, so anything keyed to exact eligibility
    /// (the war chest, the partner premium) would flicker on and off and never
    /// compound — this sticky band is what those systems key on instead. And once
    /// the war chest is substantially FUNDED, the band widens further: a prestige
    /// wobble must not flip the org back into spend mode for a month and torch a
    /// year of savings (0.42 × surplus in one splurge month did exactly that).
    /// </summary>
    public static bool SavingForTheLeap(Promotion p)
    {
        if (p.TierClearance == OrgSize.International) return false;
        int gap = OrgSizing.PrestigeCeiling(p.TierClearance) - p.Prestige.Value;
        if (gap <= 2) return true;
        return gap <= 6 && p.Balance.Amount >= PromotionCushion(p) * 0.55m;   // deep in the campaign — hold the line
    }

    /// <summary>The tier an eligible org would move up to.</summary>
    public static OrgSize NextTier(Promotion p) => OrgSizing.NextTier(p.TierClearance);

    /// <summary>Take the leap: clear the org for the next tier and step its standing onto that stage.</summary>
    public static void Promote(World world, Promotion promo, DateOnly date)
    {
        if (promo.TierClearance == OrgSize.International) return;
        var next = OrgSizing.NextTier(promo.TierClearance);
        promo.TierClearance = next;
        promo.PromotionOffered = false;
        promo.EligibleSince = null;   // off the queue — the clock starts fresh at the new ceiling
        promo.LastClearedTier = next; // the market remembers who just moved in (rate-based door pricing)
        promo.LastClearedOn = date;
        // Step onto the new stage immediately (prestige floor of the tier), then it
        // climbs from there under the new, higher bar.
        int floor = OrgSizing.PrestigeFor(next) * 100;
        if (promo.PrestigeHundredths < floor) promo.PrestigeHundredths = floor;

        Living.News.Post(world, date, NewsCategory.Milestone,
            $"{promo.Name} steps up to {OrgSizing.Label(next)} level",
            "The promotion has accepted the move to a bigger stage.", promo.Id);
        if (promo.Id == world.PlayerPromotion)
            Living.Mail.Post(world, date, MailCategory.Milestone, "The Board",
                $"We're going {OrgSizing.Label(next)}",
                $"It's official — we're a {OrgSizing.Label(next)} promotion now. Bigger gates and better talent are in reach, "
                + "but so are bigger bills. Make the shows count.");
    }

    /// <summary>
    /// Monthly: notify the player when they're eligible, and let AI orgs decide for
    /// themselves whether to step up (only when the bank can carry the bigger stage).
    /// </summary>
    public static void RunMonthly(World world, DateOnly date)
    {
        foreach (var promo in world.Promotions.Values.Where(p => !p.Defunct))
        {
            if (!IsEligible(promo))
            {
                promo.PromotionOffered = false;   // dropped back below the ceiling — reset the offer
                // Queue seniority survives a WOBBLE (prestige jitters a point or
                // two around the bar month to month — same stickiness as the
                // savings band); only genuinely falling off the ceiling forfeits
                // the org's place in line. A one-month dip used to torch years
                // of waiting and re-price the door at full crowd markup.
                if (OrgSizing.PrestigeCeiling(promo.TierClearance) - promo.Prestige.Value > 2)
                    promo.EligibleSince = null;
                continue;
            }
            promo.EligibleSince ??= date;         // first month at the ceiling — join the queue

            if (promo.Id == world.PlayerPromotion)
            {
                if (promo.PromotionOffered) continue;                 // already told them; wait for their call
                promo.PromotionOffered = true;
                var next = OrgSizing.NextTier(promo.TierClearance);
                // The door is ALWAYS open for the player — the market never prices
                // them out — but the board reads them the room first.
                int crowding = TierCrowding(world, promo);
                string market = crowding switch
                {
                    0 => "There's room at that level — the market can carry another player.",
                    1 => "Fair warning: that scene is getting crowded. Expect to fight incumbents for every gate.",
                    _ => $"Fair warning: the {OrgSizing.Label(next)} scene is SATURATED ({crowding} over what the market carries). "
                       + "We'd be walking into a knife fight for gates and talent — someone up there has to fold for the air to clear. Or we make them.",
                };
                Living.Mail.Post(world, date, MailCategory.Milestone, "The Board",
                    $"Eligible to promote to {OrgSizing.Label(next)}",
                    $"We've maxed out the {OrgSizing.Label(promo.TierClearance)} scene — the {OrgSizing.Label(next)} door is open. "
                    + "Stepping up unlocks bigger gates and better talent, but the running costs jump too. "
                    + market + " Take the leap when you're ready from the Org screen — or stay here and keep building until you are.");
            }
            else if (AiWillPromote(world, promo, date))
            {
                Promote(world, promo, date);
            }
        }
    }

    /// <summary>
    /// The BASE war chest an org needs banked before stepping up to its next tier —
    /// and it climbs STEEPLY the higher you go, so the ladder narrows: plenty can
    /// afford the step to Regional, far fewer the National jump, and only a rare few
    /// with real financial scale can bankroll a run at the global summit. (Public so
    /// the AI front office knows what it's SAVING toward.)
    /// </summary>
    public static decimal PromotionCushion(Promotion promo) => OrgSizing.NextTier(promo.TierClearance) switch
    {
        OrgSize.Regional => 1_400_000m,
        OrgSize.National => 2_800_000m,     // each rung stickier than the last, so the tiers stay populated top-down
        // The top two rungs are priced for the MEDIA-ERA economy: per-event
        // rights money (v36) multiplied a ceiling-pinned org's savings rate
        // roughly tenfold over the old flat TV stipend, and at the old prices
        // the summit mushroomed (8 Internationals by Y25 in the 30-yr sim).
        OrgSize.Continental => 12_000_000m,
        // Repriced for the biweekly era: a ceiling-pinned Continental org runs
        // ~26 cards a year now, so its media-and-gate surplus roughly doubled —
        // the summit door has to keep pace or the crown stops being scarce.
        OrgSize.International => 60_000_000m,
        _ => 1_400_000m,
    };

    /// <summary>
    /// What the step up ACTUALLY costs an AI org: the base cushion — marked up ONLY
    /// against a FLOOD. The gate is a backstop, not the pacing: an org that climbed
    /// at a natural rate finds the door open when it gets there. So the middle tiers
    /// price by the RATE of recent arrivals, never the standing population — the
    /// sport maturing over decades (more big orgs everywhere) is the world's story,
    /// not a violation, and every stock-shaped baseline tried before ended up
    /// fighting that drift and walling natural climbers for years at a time. A
    /// market absorbs a couple of newcomers per tier every few years for free; a
    /// BURST beyond that is what gets priced — brutally — until the arrivals age
    /// out of the window and the door reopens on its own.
    /// THE PLAYER IS NEVER PRICED BY THIS — eligibility alone opens their door; the
    /// board just reads them the room.
    /// </summary>
    public static decimal PromotionCushion(World world, Promotion promo)
    {
        var target = OrgSizing.NextTier(promo.TierClearance);
        int over = PricedCrowding(world, target);
        // The summit is the one deliberate exception — a standing WALL, not a
        // window: it prices on the resident count beyond the ONE org the crown
        // fits, never decays, and never widens as the world grows. Media-era
        // giants bank enormous sums, so this is less a hard cap than a statement
        // that each extra crown must be bought at genuinely summit scale.
        // Middle tiers: 0.15 of the base per overflow entrant, SQUARED — the
        // first org past the market's absorption rate pays a busy-market premium
        // (+15%, clearable by a healthy tier-capped bank: the big-money era's
        // churn keeps the window busy for years at a time, and at +20% the
        // reference shoestring missed a hair-above-plateau door for a DECADE),
        // the second +60%, the third +135%; a land-rush still stops itself
        // within the season while a lone near-miss barely feels the door.
        double markup = target == OrgSize.International ? 1.3 * over : 0.15 * over * over;
        return PromotionCushion(promo) * (1m + (decimal)markup);
    }

    /// <summary>
    /// How hard the market pushes back on entering the target tier RIGHT NOW
    /// (0 = the gate is not in the way — a climber walks through at the base
    /// cushion). Middle tiers: how far past the market's absorption rate this
    /// org's arrival would land, given who else moved in recently. Summit: how
    /// many crowns already exist beyond the one the sport truly carries.
    /// </summary>
    public static int TierCrowding(World world, Promotion promo) =>
        PricedCrowding(world, OrgSizing.NextTier(promo.TierClearance));

    // The market absorbs this many arrivals per tier per window for free; only
    // entrants beyond that are priced.
    private const int AbsorbPerWindow = 2;
    private const int WindowMonths = 36;

    /// <summary>
    /// An org died (folded or was acquired) while operating at <paramref name="band"/> —
    /// its seat frees up, and the market absorbs one recent arrival into it: the
    /// oldest in-window step-up stamp at that band is erased. This is WHY the
    /// sport consolidates — every buyout makes room, so the door behind it
    /// doesn't stay priced against the next natural climber.
    /// </summary>
    public static void OnSeatFreed(World world, OrgSize band)
    {
        var absorbed = world.Promotions.Values
            .Where(p => !p.Defunct && p.LastClearedTier == band && p.LastClearedOn is { } on
                && world.CurrentDate.DayNumber - on.DayNumber <= WindowMonths * 30)
            .OrderBy(p => p.LastClearedOn)
            .FirstOrDefault();
        if (absorbed is null) return;
        absorbed.LastClearedTier = null;
        absorbed.LastClearedOn = null;
    }

    private static int PricedCrowding(World world, OrgSize target)
    {
        if (target == OrgSize.International)
        {
            // The crown: stock-priced against an ABSOLUTE baseline of one — no
            // slack, no decay, no scaling with world size. Counts orgs OPERATING
            // at the summit (their standing, not their paperwork).
            int occupancy = world.Promotions.Values.Count(p => !p.Defunct && OrgSizing.SizeOf(p) >= target);
            return Math.Max(0, occupancy - 1);
        }
        // Everywhere else the market's memory is the recent-arrivals WINDOW: this
        // org would be entrant (recent + 1), and anything past the absorption
        // rate is overflow. Arrivals age out on their own — no seniority decay
        // needed, the door reopens when the dust settles.
        int recent = world.Promotions.Values.Count(p => !p.Defunct
            && p.LastClearedTier == target
            && p.LastClearedOn is { } on
            && world.CurrentDate.DayNumber - on.DayNumber <= WindowMonths * 30);
        return Math.Max(0, recent + 1 - AbsorbPerWindow);
    }

    // A rival steps up only when it can genuinely carry the bigger stage: a cushion
    // in the bank to survive the transition, at the CROWDED market price. A marginal
    // org stays put and survives rather than climbing into a tier it can't pay for
    // and bleeding out (the trap that folded over-eager climbers).
    private static bool AiWillPromote(World world, Promotion promo, DateOnly date) =>
        promo.Balance.Amount >= PromotionCushion(world, promo);
}
