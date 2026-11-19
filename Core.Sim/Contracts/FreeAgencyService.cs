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
        // The free agents as the pass opens, in world order. The pass only ever SHRINKS that set (a signing gives a
        // man a contract; a staged club card a signing cancels retires an unfought local, never one of these): nobody
        // enters it mid-pass, so filtering this list with today's test again is the world's FreeAgents() at that
        // moment, without walking every fighter (retired ones included) for every division of every promotion.
        var market = world.FreeAgents().ToList();
        // One Origins market read for as long as the pass only reads (OriginsMarket): dropped at every signing and every
        // lodged offer, the only things the pass changes, so each man is priced off exactly the numbers he would be alone.
        OriginsMarket? read = null;
        // (Reporting only: longsim origins' staffing counts. Null in the game and in every other run, and never read
        // by the sim; the key strings are built only when a listener is set.)
        var probe = world.Mode == WorldMode.Origins ? Living.TickProbe.Current : null;
        foreach (var promo in world.Promotions.Values
                     .Where(p => p.Id != world.PlayerPromotion && p.Id != HarnessHandsOff && !p.Defunct)
                     .OrderByDescending(p => p.Prestige.Value)
                     .ThenBy(p => p.Id.Value))
        {
            // Roster growth is gated by SOLVENCY: an org in the red stops building
            // depth (card-minimum bodies only) and shops for cheap developable
            // prospects instead of names — nobody signs their way into the grave.
            int distress = Org.Solvency.Distress(promo, world);
            decimal purseCeiling = distress == 0 ? decimal.MaxValue
                : Negotiator.PurseCap(world, promo) * Org.Solvency.DistressedPurseFactor(distress);

            // A promotion only fills its divisions as deep as its TIER can pay for —
            // a lean roster at the small halls, real depth only at the top.
            // (An Origins house runs ONE open division and every night needs its whole field: its floor is
            // what its own night takes. Today's mode keeps the old four.)
            int minBodies = world.Mode == WorldMode.Origins ? Living.OriginsShows.MinRoster(promo) : MinToRunDivision;
            int target = world.Mode == WorldMode.Origins
                ? Living.OriginsShows.DivisionRosterTarget(promo, distress > 0)   // (a split house shares its depth among its divisions)
                : distress > 0
                ? minBodies
                : Math.Max(minBodies, Org.OrgSizing.RosterTargetPerDivision(Org.OrgSizing.SizeOf(promo)));
            foreach (var div in promo.Divisions)
            {
                // (A free agent this org has lodged an offer for — one the player is
                // courting, OfferMarket — holds his seat until the offer resolves. Only a
                // player-rules world ever has one: the all-AI count is untouched.)
                // (Counted once and again only after this pass signs or lodges: nothing else in the loop moves a
                // contract or an offer, and the count scans every fighter in the world.)
                int have = -1;
                int Have() => have >= 0 ? have : have = world.RosterOf(promo.Id)
                    .Count(f => div.Matches(f) && f.Status != FighterStatus.Retired)
                    + OfferMarket.SeatsHeld(world, promo.Id, div);
                if (Have() >= target) continue;
                probe?.Count($"staff|fa|{promo.Id.Value}|div-short");

                // Chase the best SIGNINGS, not just the best fighters — a genuine
                // draw is worth pursuing over a slightly better but anonymous pro.
                // The prestigious orgs bid first (outer loop), so the names tier up.
                // A distressed org ranks the same pool by BARGAIN value instead.
                var poolPhase = Living.TickProbe.Phase("fa/pool");
                var pool = market
                    .Where(f => f.Contract is null && f.Status != FighterStatus.Retired && !f.LocalTalent)   // (World.FreeAgents' test, today)
                    .Where(f => div.Matches(f) && f.Status == FighterStatus.Active)
                    .OrderByDescending(f => distress > 0 ? BargainAppeal(world, promo, f) : SigningAppeal(world, promo, f))
                    .ThenBy(f => f.Id.Value)
                    .ToList();
                poolPhase.Dispose();

                foreach (var fa in pool)
                {
                    if (Have() >= target) break;
                    if (fa.Contract is not null) { probe?.Count($"staff|fa|{promo.Id.Value}|taken"); continue; }      // taken earlier this pass
                    // A star with a public bidding war running is off the quiet
                    // market — the war (or a player swoop) decides where they land.
                    if (world.BiddingWars.Any(w => w.Fighter == fa.Id)) { probe?.Count($"staff|fa|{promo.Id.Value}|war"); continue; }
                    // Likewise a free agent with a rival's offer in his hand (one the
                    // player courts): that offer decides, not a second quiet bid.
                    if (OfferMarket.HasLiveOffer(world, fa.Id)) { probe?.Count($"staff|fa|{promo.Id.Value}|offer-held"); continue; }
                    // A National+ org doesn't stock RAW kids as filler — a debut
                    // belongs on the small-hall circuit until the record says
                    // otherwise (the rare elite/famous prospect still gets
                    // signed through Wants). Keeps the pipeline honest: debut at
                    // the bottom, climb on results.
                    Living.TickProbe.Count("fa/candidate");
                    using var wantsPhase = Living.TickProbe.Phase("fa/gates");
                    if (fa.Record.TotalBouts < 4
                        && Org.OrgSizing.SizeOf(promo) >= OrgSize.National
                        && !Org.RosterPolicy.Wants(world, promo, fa)) { probe?.Count($"staff|fa|{promo.Id.Value}|raw-gate"); continue; }
                    // A big org won't roster a journeyman — unless the division is
                    // so thin it needs bodies just to put on a card.
                    if (Have() >= minBodies && !Org.RosterPolicy.Wants(world, promo, fa)) { probe?.Count($"staff|fa|{promo.Id.Value}|not-wanted"); continue; }

                    var evalPhase = Living.TickProbe.Phase("fa/evaluate");
                    var exp = Valuation.EvaluateWith(world, fa, ref read);   // (and the read goes to his table: nothing changes in between)
                    evalPhase.Dispose();
                    // Out of a distressed budget: measured against what he would ask THIS house (Negotiator.AffordAsk:
                    // his tier price in an Origins world, his open-market ask today).
                    if (distress > 0 && Negotiator.AffordAsk(world, promo, fa, exp, read) > purseCeiling)
                    {
                        probe?.Count($"staff|fa|{promo.Id.Value}|{(purseCeiling < Negotiator.OriginsPurseFloor ? "distress-under-floor" : "distress-budget")}");
                        continue;
                    }

                    var negPhase = Living.TickProbe.Phase("fa/negotiate");
                    var offer = Negotiator.AiOpeningOffer(world, promo, fa, exp, market: read);
                    var verdict = Negotiator.Evaluate(world, fa, offer, exp, read).Verdict;
                    negPhase.Dispose();
                    if (verdict != NegotiationVerdict.Accept)
                    {
                        // (Capped: the offer is under what he asks this house, so the house's cap stopped it; else he
                        // turned the money it could pay down for other reasons: the stage, the length, a signing bonus.)
                        probe?.Count($"staff|fa|{promo.Id.Value}|" + (probe is null ? "" :
                            offer.PursePerFight.Amount < Negotiator.TableAsk(world, promo, fa, exp, offer.Exclusive, read) ? "refused-cap" : "refused-other"));
                        continue;
                    }

                    // A free agent the player is courting (open talks): the org puts
                    // this same deal in front of him with a deadline instead of signing
                    // him on the spot — the player gets his chance to beat it. Nobody
                    // else's free agents are touched (no talks, no offer: today's signing).
                    if (OfferMarket.TryLodgeFreeAgent(world, promo, fa, offer, date)) { probe?.Count($"staff|fa|{promo.Id.Value}|lodged"); have = -1; read = null; continue; }

                    ContractLifecycle.Sign(world, fa, offer, date);
                    probe?.Count($"staff|fa|{promo.Id.Value}|signed");
                    have = -1;
                    read = null;
                    signings++;
                }
                if (probe is not null && Have() < target)
                    for (int k = Have(); k < target; k++)
                        probe.Count($"staff|fa|{promo.Id.Value}|{(pool.Count == 0 ? "missing-no-men" : "missing-pool-tried")}");
            }
        }
        return signings;
    }
}
