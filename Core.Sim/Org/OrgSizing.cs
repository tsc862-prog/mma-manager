using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Org;

/// <summary>
/// Maps a promotion's prestige onto a reach tier — the thing that grows as the
/// brand does. Each tier is a rung of the real geography and gates where the
/// promotion can stage cards (see <see cref="OrgReach"/>): City = the HQ metro
/// only; Regional = the HQ's in-country area; National = anywhere in the HQ
/// country; Continental = the home continent; International = the world.
/// Thresholds are tuned so the established AI orgs sit where their identity
/// says: CK 33 national (Brazil), ICF 45 / VMMA 58 continental, AFC 74 global.
/// </summary>
public static class OrgSizing
{
    public static OrgSize SizeOf(Promotion promo) => promo.Prestige.Value switch
    {
        >= 70 => OrgSize.International,
        >= 45 => OrgSize.Continental,
        >= 32 => OrgSize.National,
        >= 20 => OrgSize.Regional,
        _ => OrgSize.City,
    };

    /// <summary>
    /// The in-cage ability a promotion at this tier looks for when signing talent —
    /// an elite show wants elite fighters; a regional org will take a solid pro.
    /// A fighter below the bar can still be worth signing if they're a big enough
    /// draw (see <see cref="DrawFloor"/>).
    /// </summary>
    public static int TalentFloor(OrgSize size) => size switch
    {
        OrgSize.International => 68,
        OrgSize.Continental => 60,
        OrgSize.National => 52,
        OrgSize.Regional => 44,
        _ => 0,
    };

    /// <summary>
    /// The popularity that makes a fighter worth signing on draw alone — a name
    /// who sells tickets even at modest ability. The bigger the stage, the bigger
    /// the name it takes to move the needle (a regional draw is a national nobody).
    /// </summary>
    public static int DrawFloor(OrgSize size) => size switch
    {
        OrgSize.International => 75,
        OrgSize.Continental => 64,
        OrgSize.National => 54,
        OrgSize.Regional => 44,
        _ => 0,
    };

    /// <summary>
    /// How famous a fighter can get on THIS stage. Fame is bounded by the platform:
    /// you can't become a global name fighting regional cards, so winning only builds
    /// popularity up to this ceiling — to go higher you have to fight for a bigger org.
    /// (It never STRIPS fame a fighter already earned at a bigger stage.)
    /// </summary>
    public static int FameCeiling(OrgSize size) => size switch
    {
        OrgSize.International => 100,
        OrgSize.Continental => 88,
        OrgSize.National => 74,
        OrgSize.Regional => 58,
        _ => 42,   // City
    };

    /// <summary>The prestige needed to reach a tier (the Home screen's "N to go" line).</summary>
    public static int PrestigeFor(OrgSize size) => size switch
    {
        OrgSize.International => 70,
        OrgSize.Continental => 45,
        OrgSize.National => 32,
        OrgSize.Regional => 20,
        _ => 0,
    };

    /// <summary>
    /// How deep a roster the tier can afford PER DIVISION. A small stage runs a lean
    /// roster it can actually pay; the giants stack depth. Keeps a promotion that's
    /// holding at a lower tier from over-hiring for a stage its gate can't fund.
    /// </summary>
    public static int RosterTargetPerDivision(OrgSize size) => size switch
    {
        // Deep enough to feel like a real division — a champion, a ranked
        // contender field, gatekeepers, and a prospect pipeline. The big
        // leagues run a BIWEEKLY card cadence (see WorldTick) so even this
        // depth still gets everyone ~2 fights a year.
        OrgSize.International => 24,
        OrgSize.Continental => 20,
        OrgSize.National => 18,
        OrgSize.Regional => 14,
        _ => 12,   // City
    };

    /// <summary>The next tier up (International is the top — returns itself).</summary>
    public static OrgSize NextTier(OrgSize size) => size switch
    {
        OrgSize.City => OrgSize.Regional,
        OrgSize.Regional => OrgSize.National,
        OrgSize.National => OrgSize.Continental,
        OrgSize.Continental => OrgSize.International,
        _ => OrgSize.International,
    };

    /// <summary>
    /// The top prestige of a tier — one point below the next threshold. A promotion
    /// holding at this tier has its prestige capped here until it accepts promotion,
    /// so its whole economy (gate, purses, overhead) stays at this level.
    /// </summary>
    public static int PrestigeCeiling(OrgSize size) => size switch
    {
        OrgSize.City => PrestigeFor(OrgSize.Regional) - 1,          // 19
        OrgSize.Regional => PrestigeFor(OrgSize.National) - 1,      // 31
        OrgSize.National => PrestigeFor(OrgSize.Continental) - 1,   // 44
        OrgSize.Continental => PrestigeFor(OrgSize.International) - 1,   // 69
        _ => 100,                                                    // International — no ceiling
    };

    public static string Label(OrgSize size) => size switch
    {
        OrgSize.City => "City",
        OrgSize.Regional => "Regional",
        OrgSize.National => "National",
        OrgSize.Continental => "Continental",
        OrgSize.International => "International",
        _ => size.ToString(),
    };

    /// <summary>Where a promotion sits within its current tier and what's next.</summary>
    /// <param name="Tier">Current reach tier.</param>
    /// <param name="Percent">0–100 progress from this tier's floor toward the next (100 = maxed at International).</param>
    /// <param name="Next">The next tier up, or null at the top.</param>
    public readonly record struct TierStanding(OrgSize Tier, int Percent, OrgSize? Next);

    /// <summary>
    /// The within-tier climb, read off the FRACTIONAL prestige so the bar fills
    /// smoothly. A National org at 38.4 prestige sits ~49% of the way to
    /// Continental (32 → 45). At the top the bar tracks 70 → 100.
    /// </summary>
    public static TierStanding Progress(Promotion promo)
    {
        var tier = SizeOf(promo);
        double exact = promo.PrestigeHundredths / 100.0;

        if (tier == OrgSize.International)
        {
            int topPct = (int)Math.Round(Math.Clamp((exact - 70) / 30.0, 0, 1) * 100);
            return new TierStanding(tier, topPct, null);
        }

        var next = tier + 1;
        double floor = PrestigeFor(tier);
        double ceil = PrestigeFor(next);
        int pct = (int)Math.Round(Math.Clamp((exact - floor) / (ceil - floor), 0, 1) * 100);
        return new TierStanding(tier, pct, next);
    }
}
