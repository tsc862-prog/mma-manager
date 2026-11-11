using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Contracts;

/// <summary>
/// The back half of the contract system: turning accepted offers into signed
/// deals, renewing or releasing fighters as their deals run out, honouring a
/// champion's clause, and paying out guaranteed money when a fighter is cut.
/// Deterministic; the world tick runs the expiry pass each month.
/// </summary>
public static class ContractLifecycle
{
    private static readonly Negotiator Negotiator = new();
    private static readonly FighterValuation Valuation = new();

    /// <summary>Sign (or re-sign) a fighter to an accepted offer, paying the signing bonus and slotting them into the division.</summary>
    public static void Sign(World world, Fighter f, ContractOffer offer, DateOnly on)
    {
        bool renewal = f.Contract?.Promotion == offer.Promotion;
        // Signing away from a live deal is still leaving the old org (callers
        // normally clear it first; this keeps the old books clean if one doesn't).
        if (f.Contract is { } old && !renewal)
            LeavePromotion(world, f, old.Promotion, on,
                $"{world.Get(offer.Promotion)?.Name ?? "a rival"} signed him away", offer.Promotion, hisChoice: true);
        f.Contract = offer.ToContract(on);
        BiddingWars.OnSigned(world, f, offer.Promotion, on);   // a signature ends any public war over them
        // A move to a new org starts the loyalty clock over; re-upping deepens it a touch.
        f.Loyalty = renewal ? Math.Min(100, f.Loyalty + 5) : 20;
        if (world.Get(offer.Promotion) is not { } promo) return;

        Finance.Ledger.Expense(promo, on, FinanceCategory.SigningBonus, offer.SigningBonus, $"Signing bonus — {f.FullName}");

        bool isChampion = world.Titles.Values.Any(t => t.CurrentChampion == f.Id);
        var div = promo.Divisions.FirstOrDefault(d => d.Matches(f));
        if (div is not null && !isChampion && !div.Rankings.Contains(f.Id))
            div.Rankings.Add(f.Id);

        Living.News.Signed(world, promo, f, offer.PursePerFight, on, renewal);
    }

    /// <summary>
    /// Release a fighter to free agency. If their remaining purses are guaranteed,
    /// the promotion pays them out. Returns the money owed. Cutting a name
    /// mid-deal dents reputation (<paramref name="dentReputation"/>); routine
    /// performance cuts of journeymen don't.
    /// </summary>
    public static Money Release(World world, Fighter f, bool dentReputation = true)
    {
        if (f.Contract is not { } c) return Money.Zero;

        var owed = c.Guaranteed ? c.PursePerFight * c.FightsRemaining : Money.Zero;
        if (world.Get(c.Promotion) is { } promo)
        {
            Finance.Ledger.Expense(promo, world.CurrentDate, FinanceCategory.ContractBuyout, owed, $"Contract buyout — {f.FullName}");
            if (dentReputation) Org.Standing.OnRelease(promo);
        }
        LeavePromotion(world, f, c.Promotion, world.CurrentDate,
            $"{world.Get(c.Promotion)?.Abbreviation ?? "his promotion"} released him");
        f.Contract = null;
        return owed;
    }

    /// <summary>
    /// The one place "this fighter has left this promotion" lands, whatever the
    /// door — a cut, a deal that ran out, a poach, the board's veteran cut, a
    /// signing elsewhere: he comes out of every one of the org's division
    /// rankings (its matchmaker books title fights straight off them, and they
    /// only rebuild at the org's next card), and any belt of that org he holds
    /// goes VACANT (the champion walked; it isn't his to hand on). Does not touch
    /// the contract or the money — callers clear or replace the deal. Retirement
    /// has its own path (<see cref="Living.Lifecycle.Retire"/>: the belt passes to
    /// the top contender); a folding or sold org clears its own books wholesale.
    /// <paramref name="door"/> says HOW he left, in plain words — when a lifer
    /// goes, it's the reason the headline gives (<see cref="Lifers.OnLeaving"/>),
    /// so this must run while his contract still names the org he's leaving.
    /// </summary>
    public static void LeavePromotion(World world, Fighter f, PromotionId from, DateOnly date,
        string? door = null, PromotionId? to = null, bool hisChoice = false)
    {
        if (world.Get(from) is not { } promo) return;
        Lifers.OnLeaving(world, f, promo, door ?? $"he left {promo.Abbreviation}", date, to, hisChoice);
        foreach (var d in promo.Divisions) d.Rankings.Remove(f.Id);

        foreach (var title in world.Titles.Values
                     .Where(t => t.Promotion == from && t.CurrentChampion == f.Id)
                     .OrderBy(t => t.Id.Value)
                     .ToList())
        {
            var reign = title.Lineage.LastOrDefault(r => r.LostOn is null);
            if (reign is not null) reign.LostOn = date;
            title.CurrentChampion = null;
            Living.News.Post(world, date, NewsCategory.TitleChange,
                $"{f.FullName} vacates the {title.Name}",
                $"The champion is leaving {promo.Abbreviation} — the belt is vacant.", promo.Id, f.Id);
        }
    }

    // An expiring deal that isn't renewed: the fighter walks to the open market.
    private static void Walk(World world, Fighter f, Promotion from, DateOnly date, string door, bool hisChoice = false)
    {
        LeavePromotion(world, f, from.Id, date, door, hisChoice: hisChoice);
        f.Contract = null;
    }

    /// <summary>A champion's clause tops a deal back up so a titleholder never runs out mid-reign.</summary>
    public static void ApplyChampionsClause(Fighter champ)
    {
        if (champ.Contract is { HasChampionsClause: true } c && c.FightsRemaining < 3)
            c.FightsRemaining = 3;
    }

    /// <summary>
    /// Monthly: any non-player deal that has run out either re-signs with its
    /// promotion (champions always do; others if a fair renewal is accepted) or
    /// the fighter drops to free agency to be picked up on the open market.
    /// Returns how many expiring deals were resolved.
    /// </summary>
    public static int ProcessExpiries(World world, DateOnly date)
    {
        int resolved = 0;
        foreach (var f in world.Fighters.Values
                     .Where(IsExpired)
                     .OrderBy(f => f.Id.Value)
                     .ToList())
        {
            var promo = world.Get(f.Contract!.Promotion);
            if (promo is null) continue;

            // Some careers end where the contract does: an aging fighter who
            // went the distance on the deal takes the guaranteed money home
            // rather than chase another one — likelier the older and colder
            // they are. (Deterministic hash roll — no RNG stream here.)
            int age = f.AgeOn(date);
            double sunset = age switch { >= 40 => 0.6, >= 37 => 0.35, >= 35 => 0.18, _ => 0.0 };
            if (f.Momentum <= -30) sunset *= 1.5;
            if (sunset > 0)
            {
                var roll = new Rng.SimRandom(Rng.SimRandom.Mix(world.RngSeed,
                    (ulong)f.Id.Value * 977UL ^ (ulong)date.DayNumber ^ 0x50_75E7UL));
                if (roll.Chance(Math.Min(0.9, sunset)))
                {
                    Living.Lifecycle.Retire(world, f, date);
                    Living.Camps.OnRetirement(world, f, date, roll);
                    resolved++;
                    continue;
                }
            }

            // The player re-signs their own — but a bigger org can steal an
            // expiring star they didn't lock down in time (the treadmill).
            if (promo.Id == world.PlayerPromotion)
            {
                if (PoachingService.TryPoachFromPlayer(world, f, date)) resolved++;
                continue;
            }

            // AI-to-AI treadmill: a promotion on a bigger stage skims the best
            // expiring talent off a smaller one before it can re-sign — even a
            // champion, who vacates the belt as they move up.
            if (PoachingService.TryPoach(world, f, date)) { resolved++; continue; }

            bool champion = world.Titles.Values.Any(t => t.CurrentChampion == f.Id);

            // Champions always get kept; otherwise the org only renews fighters
            // it still wants (a big org lets a losing journeyman walk), and only
            // if the fighter accepts a fair deal. A DISTRESSED org tightens
            // further: anyone asking beyond its bargain budget walks, so the
            // payroll actually shrinks while the books recover (champions are
            // exempt — the belt is the gate income).
            if (champion)
            {
                Sign(world, f, Negotiator.AiOpeningOffer(world, promo, f, Valuation.Evaluate(world, f)), date);
            }
            else if (!Org.RosterPolicy.Wants(world, promo, f))
            {
                Walk(world, f, promo, date, $"{promo.Abbreviation} let his deal run out");   // not re-signed — slides down the ladder
            }
            else if (Lifers.KeepsFirstCall(world, f))
            {
                // A LIFER is renewed first — before any trimming of hoarded depth —
                // and the org stretches past its usual purse budget to do it (he,
                // in turn, asks less of his own promotion). Only a solvent org can
                // be holding him at all (being in the red is a grievance), so this
                // is never the bleeding org overreaching. If even the stretch can't
                // reach him, he has outgrown the house.
                var exp = Valuation.Evaluate(world, f);
                var offer = Negotiator.AiOpeningOffer(world, promo, f, exp, Lifers.RenewalStretch);
                if (Negotiator.Evaluate(world, f, offer, exp).Verdict == NegotiationVerdict.Accept)
                    Sign(world, f, offer, date);
                else
                    Walk(world, f, promo, date, $"his deal ran out and {promo.Abbreviation} couldn't pay what it would take to keep him", hisChoice: true);
            }
            else if (DivisionOverTarget(world, promo, f))
            {
                Walk(world, f, promo, date, $"{promo.Abbreviation} let his deal run out to trim the division");   // hoarded depth — over the tier target, let them walk
            }
            else
            {
                var exp = Valuation.Evaluate(world, f);
                int distress = Org.Solvency.Distress(promo);
                if (distress > 0 && exp.AskingPurse.Amount >
                    Negotiator.PurseCap(world, promo) * Org.Solvency.DistressedPurseFactor(distress))
                {
                    Walk(world, f, promo, date, $"{promo.Abbreviation} couldn't afford to keep him");   // too rich for a bleeding org — let them walk
                }
                else
                {
                    var offer = Negotiator.AiOpeningOffer(world, promo, f, exp);
                    if (Negotiator.Evaluate(world, f, offer, exp).Verdict == NegotiationVerdict.Accept)
                        Sign(world, f, offer, date);   // renew in place
                    else
                        Walk(world, f, promo, date, $"his deal ran out and he turned down {promo.Abbreviation}'s offer", hisChoice: true);   // to the open market
                }
            }

            resolved++;
        }
        return resolved;
    }

    // The fighter's division at this promotion already holds more than the tier
    // roster target (counting the fighter themself) — renewing would keep paying
    // the hoarding retainer for depth the org can't book.
    private static bool DivisionOverTarget(World world, Promotion promo, Fighter f)
    {
        var div = promo.Divisions.FirstOrDefault(d => d.Matches(f));
        if (div is null) return false;
        int target = Org.OrgSizing.RosterTargetPerDivision(Org.OrgSizing.SizeOf(promo));
        return world.RosterOf(promo.Id).Count(x => div.Matches(x) && x.Status != FighterStatus.Retired) > target;
    }

    private static bool IsExpired(Fighter f) =>
        f.Contract is { FightsRemaining: 0 } && f.Status == FighterStatus.Active;
}
