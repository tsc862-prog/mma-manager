using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;

namespace MmaManager.Core.Sim.Contracts;

/// <summary>
/// The AI transfer market: each month, promotions with thin divisions bid for
/// free agents through the <see cref="Negotiator"/>. The richest, most prestigious
/// promotions sign first, so talent tiers naturally and a fighter tends to land at
/// the best home that will meet their price. The player's promotion is left out —
/// they work the market themselves.
/// </summary>
public static class FreeAgencyService
{
    private const int MinToRunDivision = 4;   // below this, sign anyone — bodies to make a card

    private static readonly Negotiator Negotiator = new();
    private static readonly FighterValuation Valuation = new();

    // How much an org wants to sign a fighter: their ability plus the draw they
    // bring — a popular name is sought after even a notch below the best pure pro.
    // How much an org wants a fighter: its READ of their ability (Fog of War — the
    // org can over- or under-rate an unproven fighter) plus the draw they bring,
    // which is public. Two orgs can rank the same pool differently, so a prospect
    // one overlooks another may pounce on.
    private static double SigningAppeal(World world, Promotion promo, Fighter f) =>
        Scouting.ScoutingService.PerceivedAbility(world, promo, f) + f.Intangibles.Popularity.Value * 0.5;

    // What a DISTRESSED org shops for: not names, projects. Popularity is a cost
    // (names charge for their draw), youth is upside — the front office wants the
    // cheap prospect it can BUILD UP while the books recover, not the proven
    // loser who's merely cheap (deadweight doesn't rebuild a brand).
    private static double BargainAppeal(World world, Promotion promo, Fighter f) =>
        Scouting.ScoutingService.PerceivedAbility(world, promo, f)
        - f.Intangibles.Popularity.Value * 0.5
        + Math.Max(0, 27 - f.AgeOn(world.CurrentDate)) * 2.0
        - (f.Record.TotalBouts >= 5 && f.Record.Losses > f.Record.Wins ? 15.0 : 0.0);

    public static int RunMonthly(World world, DateOnly date)
    {
        int signings = 0;
        foreach (var promo in world.Promotions.Values
                     .Where(p => p.Id != world.PlayerPromotion && !p.Defunct)
                     .OrderByDescending(p => p.Prestige.Value)
                     .ThenBy(p => p.Id.Value))
        {
            // Roster growth is gated by SOLVENCY: an org in the red stops building
            // depth (card-minimum bodies only) and shops for cheap developable
            // prospects instead of names — nobody signs their way into the grave.
            int distress = Org.Solvency.Distress(promo);
            decimal purseCeiling = distress == 0 ? decimal.MaxValue
                : Negotiator.PurseCap(world, promo) * Org.Solvency.DistressedPurseFactor(distress);

            // A promotion only fills its divisions as deep as its TIER can pay for —
            // a lean roster at the small halls, real depth only at the top.
            int target = distress > 0
                ? MinToRunDivision
                : Org.OrgSizing.RosterTargetPerDivision(Org.OrgSizing.SizeOf(promo));
            foreach (var div in promo.Divisions)
            {
                int Have() => world.RosterOf(promo.Id)
                    .Count(f => div.Matches(f) && f.Status != FighterStatus.Retired);
                if (Have() >= target) continue;

                // Chase the best SIGNINGS, not just the best fighters — a genuine
                // draw is worth pursuing over a slightly better but anonymous pro.
                // The prestigious orgs bid first (outer loop), so the names tier up.
                // A distressed org ranks the same pool by BARGAIN value instead.
                var pool = world.FreeAgents()
                    .Where(f => div.Matches(f) && f.Status == FighterStatus.Active)
                    .OrderByDescending(f => distress > 0 ? BargainAppeal(world, promo, f) : SigningAppeal(world, promo, f))
                    .ThenBy(f => f.Id.Value)
                    .ToList();

                foreach (var fa in pool)
                {
                    if (Have() >= target) break;
                    if (fa.Contract is not null) continue;      // taken earlier this pass
                    // A star with a public bidding war running is off the quiet
                    // market — the war (or a player swoop) decides where they land.
                    if (world.BiddingWars.Any(w => w.Fighter == fa.Id)) continue;
                    // A National+ org doesn't stock RAW kids as filler — a debut
                    // belongs on the small-hall circuit until the record says
                    // otherwise (the rare elite/famous prospect still gets
                    // signed through Wants). Keeps the pipeline honest: debut at
                    // the bottom, climb on results.
                    if (fa.Record.TotalBouts < 4
                        && Org.OrgSizing.SizeOf(promo) >= OrgSize.National
                        && !Org.RosterPolicy.Wants(world, promo, fa)) continue;
                    // A big org won't roster a journeyman — unless the division is
                    // so thin it needs bodies just to put on a card.
                    if (Have() >= MinToRunDivision && !Org.RosterPolicy.Wants(world, promo, fa)) continue;

                    var exp = Valuation.Evaluate(world, fa);
                    if (exp.AskingPurse.Amount > purseCeiling) continue;   // out of a distressed budget

                    var offer = Negotiator.AiOpeningOffer(world, promo, fa, exp);
                    if (Negotiator.Evaluate(world, fa, offer, exp).Verdict != NegotiationVerdict.Accept) continue;

                    ContractLifecycle.Sign(world, fa, offer, date);
                    signings++;
                }
            }
        }
        return signings;
    }
}
