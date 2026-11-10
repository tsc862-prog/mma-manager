using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Contracts;

namespace MmaManager.Core.Sim.Org;

/// <summary>
/// Rival promotions spending like a promoter would: pouring surplus cash into brand
/// marketing to climb, paying retention bonuses to hang on to the stars the giants
/// keep circling, and bankrolling promo campaigns to build their prospects into
/// draws. Runs monthly for every AI org (the player makes these calls themselves),
/// so the money sinks aren't just the player's edge — the whole field competes with
/// them. All decisions are cash-gated so nobody spends themselves into the grave.
/// </summary>
public static class AiFrontOffice
{
    public static void RunMonthly(World world, DateOnly date)
    {
        foreach (var promo in world.Promotions.Values.Where(p => !p.Defunct && p.Id != world.PlayerPromotion))
        {
            // Everything discretionary is paid out of SURPLUS — cash above a survival
            // buffer of a few months' running costs. No surplus, no spending: an org
            // scraping by during a climb won't market or retain itself into the red.
            decimal buffer = 800_000m + promo.Prestige.Value * 60_000m;
            decimal surplus = promo.Balance.Amount - buffer;
            var roster = world.RosterOf(promo.Id).Where(f => f.Status != FighterStatus.Retired).ToList();

            if (surplus <= 0)
            {
                promo.MarketingBudget = Money.Zero;   // can't afford a campaign right now
                continue;
            }

            // An org that has MAXED its tier is saving for the leap: the war chest IS
            // the plan. It keeps a token campaign running (stay in the conversation)
            // and banks the rest until it can afford the next stage. Without this,
            // the buffer-to-surplus treadmill spent every spare dollar forever and no
            // AI org could ever bankroll the Continental/International jumps — the
            // summit was frozen and #2 could never come for the crown.
            // The leap campaign runs in two phases. NOTE: both keep running once the
            // chest is full — falling through to the big spender the month the bank
            // crossed the cushion caused a sawtooth (splurge → back under → repeat)
            // and the org rode just beneath its own cushion forever.
            if (PromotionLadder.SavingForTheLeap(promo))
            {
                decimal cushion = PromotionLadder.PromotionCushion(promo);
                if (promo.Balance.Amount < cushion)
                {
                    // Phase 1 — FILL THE CHEST: a nominal campaign only. (An early
                    // cut used 8% of surplus, which at a $5M bank quietly burned
                    // ~$1.8M/yr and swallowed the partner premium — orgs "saving"
                    // for a $6M cushion crept at +$0.1M/yr and the summit froze.)
                    decimal maintenance = Math.Min(30_000m, surplus * 0.02m);
                    promo.MarketingBudget = new Money(Math.Round(maintenance / 10_000m) * 10_000m);
                    // Promo pushes started in flusher times pause too — every
                    // recurring spend goes to the chest.
                    foreach (var f in roster.Where(f => f.MarketingPush)) f.MarketingPush = false;
                }
                else
                {
                    // Phase 2 — THE FINAL PUSH: the chest is banked, but eligibility
                    // needs prestige PINNED at the ceiling, and a starved campaign
                    // can't climb the last points (orgs sat at 64-67/69 on $40M+
                    // hoards). Fund a real campaign from the EXCESS above the
                    // cushion — never the cushion itself — until the bar is hit.
                    decimal excess = promo.Balance.Amount - cushion;
                    decimal budget = Math.Min(OrgMarketing.SuggestedMax(promo).Amount, excess * 0.35m);
                    promo.MarketingBudget = new Money(Math.Round(budget / 10_000m) * 10_000m);
                }
                continue;
            }

            SetBrandBudget(promo, surplus);
            RetainAFlightRisk(world, promo, roster, surplus);
            BuildAProspect(promo, roster, surplus);
            SendAProspectToCamp(world, promo, roster, surplus);
        }
    }

    // Pour a chunk of surplus into brand marketing — and pour in HARD when within
    // striking distance of the next tier, an org gunning for promotion drops a big
    // slice of its war chest on the campaign. Capped near saturation so it isn't wasted.
    private static void SetBrandBudget(Promotion p, decimal surplus)
    {
        int prestige = p.Prestige.Value;
        int nextTier = prestige < 20 ? 20 : prestige < 32 ? 32 : prestige < 45 ? 45 : prestige < 70 ? 70 : 100;
        bool pushingForPromotion = nextTier - prestige <= 8;
        decimal fraction = pushingForPromotion ? 0.42m : 0.22m;
        decimal budget = Math.Min(OrgMarketing.SuggestedMax(p).Amount, surplus * fraction);
        p.MarketingBudget = new Money(Math.Round(budget / 10_000m) * 10_000m);
    }

    // Lock down the most valuable fighter who's a genuine flight risk — paid from
    // surplus, and only when the bonus is a modest slice of it.
    private static void RetainAFlightRisk(World world, Promotion promo, List<Fighter> roster, decimal surplus)
    {
        var target = roster
            .Where(f => f.Loyalty < 50)
            .OrderByDescending(f => FighterAbility.CurrentAbility(f) + f.Intangibles.Popularity.Value)
            .FirstOrDefault(f => RetentionService.CanPay(world, promo, f, out _)
                                 && RetentionService.Quote(f).Amount <= surplus * 0.35m);
        if (target is not null) RetentionService.TryPayBonus(world, promo, target, out _);
    }

    // Bankroll a couple of promo campaigns at a time on young, high-ceiling fighters
    // who aren't draws yet — building the next name in-house (only with real surplus).
    private static void BuildAProspect(Promotion promo, List<Fighter> roster, decimal surplus)
    {
        if (surplus < 1_500_000m || roster.Count(f => f.MarketingPush) >= 2) return;
        var target = roster
            .Where(f => !f.MarketingPush && f.Intangibles.Popularity.Value < 65 && f.PotentialAbility.Value >= 62)
            .OrderByDescending(f => f.PotentialAbility.Value)
            .FirstOrDefault();
        if (target is not null) target.MarketingPush = true;
    }

    // Pay a high-ceiling kid's way into a better room — the same camp-stipend sink
    // the player uses. Cheap ($3-9k/mo), compounds through development, and the
    // fighter's loyalty grows with every month the org invests in them.
    private static void SendAProspectToCamp(World world, Promotion promo, List<Fighter> roster, decimal surplus)
    {
        if (surplus < 600_000m || roster.Count(f => f.CampStipend) >= 3) return;
        var target = roster
            .Where(f => !f.CampStipend && f.PotentialAbility.Value >= 62
                        && f.PotentialAbility.Value - Ability.FighterAbility.CurrentAbility(f) >= 8)
            .OrderByDescending(f => f.PotentialAbility.Value)
            .FirstOrDefault();
        if (target is not null) Living.Camps.PayStipend(world, promo, target);
    }
}
