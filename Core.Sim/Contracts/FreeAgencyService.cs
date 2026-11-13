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

    /// <summary>
    /// HARNESS ONLY (tools/longsim's shoestring, "script alone" variant): a
    /// promotion whose roster an outside hand runs, so the monthly pass leaves it
    /// alone the way it leaves the player's. Per thread; null in the game.
    /// </summary>
    [ThreadStatic] public static PromotionId? HarnessHandsOff;

    // How much an org wants to sign a fighter: their ability plus the draw they
    // bring — a popular name is sought after even a notch below the best pure pro.
    // How much an org wants a fighter: its READ of their ability (Fog of War — the
    // org can over- or under-rate an unproven fighter) plus the draw they bring,
    // which is public. Two orgs can rank the same pool differently, so a prospect
    // one overlooks another may pounce on.
    private static double SigningAppeal(World world, Promotion promo, Fighter f) =>
        Scouting.ScoutingService.PerceivedAbility(world, promo, f) + f.Intangibles.Popularity.Value * 0.5
        + HomePull(promo, f);

    // The draw a fighter brings in THIS org's own building: the same hometown-hero
    // (+12) and local-fame (city reputation / 4) terms the box office already reads
    // (Financials.Draw). Only the small stages weigh it — a City or Regional house
    // stages nearly every card at home, while the big leagues tour — and only
    // lightly (a hometown kid of ordinary fame is worth ~4-5 points of ability), so
    // a small hall leans local without turning down a clearly better outsider.
    // Public information (where he's from, how known he is there): no fog leak.
    private const double HomePullWeight = 0.25;

    internal static double HomePull(Promotion promo, Fighter f) =>
        promo.HomeCity is { } hq && Org.OrgSizing.SizeOf(promo) <= OrgSize.Regional
            ? ((f.HomeCity == hq ? 12 : 0) + Geography.Reputation.FighterInCity(f, hq) / 4) * HomePullWeight
            : 0;

    // What a DISTRESSED org shops for: not names, projects. Popularity is a cost
    // (names charge for their draw), youth is upside — the front office wants the
    // cheap prospect it can BUILD UP while the books recover, not the proven
    // loser who's merely cheap (deadweight doesn't rebuild a brand).
    private static double BargainAppeal(World world, Promotion promo, Fighter f) =>
        Scouting.ScoutingService.PerceivedAbility(world, promo, f)
        - f.Intangibles.Popularity.Value * 0.5
        + Math.Max(0, 27 - f.AgeOn(world.CurrentDate)) * 2.0
        - (f.Record.TotalBouts >= 5 && f.Record.Losses > f.Record.Wins ? 15.0 : 0.0)
        + HomePull(promo, f);

    public static int RunMonthly(World world, DateOnly date)
    {
        int signings = 0;
        foreach (var promo in world.Promotions.Values
                     .Where(p => p.Id != world.PlayerPromotion && p.Id != HarnessHandsOff && !p.Defunct)
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
                // (A free agent this org has lodged an offer for — one the player is
                // courting, OfferMarket — holds his seat until the offer resolves. Only a
                // player-rules world ever has one: the all-AI count is untouched.)
                int Have() => world.RosterOf(promo.Id)
                    .Count(f => div.Matches(f) && f.Status != FighterStatus.Retired)
                    + OfferMarket.SeatsHeld(world, promo.Id, div);
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
                    // Likewise a free agent with a rival's offer in his hand (one the
                    // player courts): that offer decides, not a second quiet bid.
                    if (OfferMarket.HasLiveOffer(world, fa.Id)) continue;
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

                    // A free agent the player is courting (open talks): the org puts
                    // this same deal in front of him with a deadline instead of signing
                    // him on the spot — the player gets his chance to beat it. Nobody
                    // else's free agents are touched (no talks, no offer: today's signing).
                    if (OfferMarket.TryLodgeFreeAgent(world, promo, fa, offer, date)) continue;

                    ContractLifecycle.Sign(world, fa, offer, date);
                    signings++;
                }
            }
        }
        return signings;
    }
}
