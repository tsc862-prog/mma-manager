using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Org;

/// <summary>
/// What a promotion's reach tier unlocks beyond the map. The business of the
/// airwaves lives in Finance.MediaService (networks court a promotion, deals
/// carry terms); this keeps the structural perks — how big a card the stage
/// supports, and how the market prices an org that has outgrown its tier.
/// </summary>
public static class OrgPerks
{
    /// <summary>The biggest card the promotion can stage at its current level.</summary>
    public static EventTier MaxEventTier(Promotion p) => OrgSizing.SizeOf(p) switch
    {
        OrgSize.International => EventTier.SuperCard,
        OrgSize.Continental => EventTier.Ppv,
        _ => EventTier.FightNight,
    };

    /// <summary>
    /// An org pinned at its tier ceiling has outgrown its stage, and the market
    /// prices it that way: partners pay a premium to lock in the next big thing
    /// before it steps up. This premium is what lets a hoarding challenger actually
    /// BANK its way to the promotion cushions — without it, top-tier margins were
    /// too thin to ever save for the leap and the summit froze. The reigning
    /// International crown gets no premium (there's no next stage to sell), so the
    /// chaser closes ground — the crown can be come for.
    /// </summary>
    public static decimal PartnerPremium(Promotion p) => PromotionLadder.SavingForTheLeap(p) ? 1.35m : 1.0m;
}
