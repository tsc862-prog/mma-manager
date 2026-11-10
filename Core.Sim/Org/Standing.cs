using MmaManager.Core.Model;
using MmaManager.Core.Sim.Flavor;

namespace MmaManager.Core.Sim.Org;

/// <summary>
/// The climb engine. Prestige is EARNED show by show: every settled card gets a
/// quality score (fight ratings, the house, star power on top) measured against
/// a bar that rises with the promotion's current prestige — so a promotion climbs
/// on shows that beat its level and stalls once its product just meets it. The
/// climb is a career-long grind, and DELIBERATELY slower the higher you are: each
/// point of prestige is worth more shows at the top than at the bottom, so moving
/// up a tier takes years, and each successive tier takes longer than the last.
/// A brand-new promotion also earns reduced credit until it's built a name. Duds,
/// cancellations and silence all pull the number back down.
/// </summary>
public static class Standing
{
    // The bar a show must clear rises with prestige (equilibrium = where a promo's
    // typical show quality meets its bar). The prestige swing is the margin over the
    // bar divided by a STEP that itself grows with prestige — so the same great show
    // barely nudges a big brand but lifts a small one, making each tier up a longer
    // climb than the one before. Capped at ±2/show. The step scaling changes only the
    // SPEED of the climb, never where an org settles, so the designed tier map holds.
    private const double BarBase = 35.0;
    private const double BarSlope = 0.50;         // re-anchored for the deep-roster era: better fighters everywhere lifted typical show quality
    private const double DeltaStepBase = 15.0;
    // Bigger stage ⇒ slower per show — and STEEPLY so: the climb itself is meant to
    // be the pacing of a promotion's rise. At 0.62 orgs pinned each tier's ceiling
    // in 2-4 years and piled up at the promotion gate, which turned a flood
    // backstop into the de-facto clock. Steeper step scaling staggers arrivals so
    // the gate's slack band almost always has room. (Step scaling changes only the
    // SPEED of the climb, never where an org settles — the tier map holds.)
    private const double DeltaStepSlope = 1.05;
    // Convex bar above prestige 60 — the summit is exclusive. (0.045 with the
    // steeper slope pushed the flagship's own equilibrium against the
    // International cliff and AFC itself died on some seeds — the curve keeps
    // the summit rare, but the giants must still LIVE there.)
    private const double TopTierCurve = 0.03;

    /// <summary>After a settled card, score the show and move prestige/reputation.</summary>
    public static void AfterEvent(World world, Promotion promo, Event ev)
    {
        if (ev.Financials is not { } fin) return;
        var fought = ev.Card.Where(f => f.Outcome is not null).ToList();
        if (fought.Count == 0) return;

        // The product: how good the fights actually were.
        double avgRating = fought.Average(f => (double)MatchRating.Of(f));

        // The house: filling the room matters, but a 15,000 sellout is a bigger
        // statement than a packed 3,000-seat hall.
        var venue = ev.VenueId is { } vid ? world.Get(vid) : null;
        double fill = venue is { Capacity: > 0 } ? Math.Min(1.0, fin.Attendance / (double)venue.Capacity) : 0.6;
        double house = 60 * fill + 40 * Math.Min(1.0, fin.Attendance / 20_000.0);

        // The marquee: who topped the bill.
        var main = ev.Card[^1];
        int starPower = Math.Max(
            world.Get(main.RedCorner)?.Intangibles.Popularity.Value ?? 0,
            world.Get(main.BlueCorner)?.Intangibles.Popularity.Value ?? 0);

        // A title fight is a statement in proportion to the BELT's own name — a
        // storied strap on the line elevates the whole card; a fresh trinket
        // barely registers. (Was a flat +5; a mid belt still lands about there.)
        double beltStatement = ev.Card.Where(f => f.IsTitleFight && f.Title is not null)
            .Select(f => world.Get(f.Title!.Value)?.Prestige.Value ?? 0)
            .DefaultIfEmpty(0).Max() / 14.0;

        double quality = 0.55 * avgRating + 0.25 * house + 0.20 * starPower
                       + beltStatement
                       + (fin.PayPerViewBuys > 0 ? 3 : 0)
                       + OrgMarketing.StandingQualityBonus(promo)        // a funded brand campaign builds standing faster
                       + Franchise.StandingQualityBonus(world, promo);   // a bankable FACE is part of the product

        // The margin over/under the bar is the prestige swing (points, capped at
        // ±2/show). Kept fractional — a show a hair over the bar still nudges up.
        // Above the Continental range the bar bends UP steeply: reaching and holding a
        // GLOBAL (International) standing demands genuinely elite product, so the summit
        // stays exclusive — a few good shows and a marketing budget can lift you into
        // the top of Continental, but not park you at the very top of the sport.
        int pr = promo.Prestige.Value;
        double bar = BarBase + pr * BarSlope + TopTierCurve * Math.Pow(Math.Max(0, pr - 60), 2);
        double step = DeltaStepBase + promo.Prestige.Value * DeltaStepSlope;
        double delta = (quality - bar) / step;

        // Institutional inertia: a brand is sticky on the way DOWN, the more so
        // the bigger it is — so an established promotion doesn't freefall out of
        // its tier (and toward insolvency) over a rough patch. Smoothly scaled by
        // prestige (no cliff), so a small outfit still declines nearly full-rate
        // while a giant barely budges. Climbing is unchanged.
        if (delta < 0)
            delta *= Math.Clamp(0.6 - promo.Prestige.Value / 250.0, 0.12, 0.6);

        // Building a name takes time. A brand-new promotion's early shows earn only
        // a fraction of the prestige a good card is "worth" — nobody's watching yet —
        // ramping to full credit over its first ~4-5 years of monthly shows. So a hot
        // start can't rocket a nobody up the tiers; the climb to National is a slog.
        // (Only the CLIMB is throttled — a new org that runs duds still slips fully.)
        if (delta > 0)
        {
            int priorShows = world.Events.Values.Count(e => e.Promotion == promo.Id
                && e.Status == EventStatus.Completed && e.Date < ev.Date);
            delta *= Math.Clamp(0.28 + priorShows / 62.0, 0.28, 1.0);

            // A show only builds the brand as far as people can WATCH it: the
            // biggest screen the org is on scales the climb (word-of-mouth alone
            // runs at ~75%). Media deals aren't survival — they're the ceiling.
            // The effect TAPERS with prestige (full below 20, gone by 60): a
            // screen decides whether a SMALL show gets seen at all, but at the
            // top everyone is televised and the quality bar alone must rule —
            // an untapered 1.25× let the whole Continental field pin its
            // ceiling and mushroomed the summit.
            double media = Finance.MediaService.BrandFactor(promo);
            double reach = Math.Clamp((60.0 - promo.Prestige.Value) / 40.0, 0.0, 1.0);
            delta *= 1.0 + (media - 1.0) * reach;
        }

        // A brand doesn't climb twice as fast by running twice as often: past a
        // ~monthly cadence the market normalizes PER-SHOW movement (a biweekly
        // giant's individual card is half the statement — the yearly climb rate
        // stays what the monthly world was tuned to). Symmetric, so the
        // equilibrium point is untouched — only the speed.
        int showsThisYear = world.Events.Values.Count(e => e.Promotion == promo.Id
            && e.Status == EventStatus.Completed && ev.Date.DayNumber - e.Date.DayNumber < 365);
        delta *= Math.Min(1.0, 16.0 / Math.Max(1, showsThisYear));

        delta = Math.Clamp(delta, -2.0, 2.0);

        Apply(world, promo, delta, ev.Date, $"after {ev.Name}");

        // Running a full, profitable house earns goodwill with the industry.
        if (fill >= 0.9 && fin.NetProfit.Amount >= 0)
            promo.Reputation = Math.Min(100, promo.Reputation.Value + 1);

        if (promo.Id == world.PlayerPromotion)
            MailShowReport(world, promo, ev, avgRating, fill, starPower, quality, bar, delta);
    }

    /// <summary>
    /// Move prestige by a fractional amount (points), accruing in hundredths so
    /// sub-point swings aren't lost. The one gate that watches for tier changes:
    /// crossing a reach threshold makes news (and, for the player, a board letter).
    /// </summary>
    public static void Apply(World world, Promotion promo, double delta, DateOnly date, string context)
    {
        if (delta == 0) return;
        var before = OrgSizing.SizeOf(promo);
        // Growth is opt-in: prestige can't climb past the ceiling of the tier the org
        // is CLEARED for until it accepts promotion (see PromotionLadder). Held there,
        // its whole economy stays at that tier — cheap and sustainable.
        int cap = OrgSizing.PrestigeCeiling(promo.TierClearance) * 100;
        promo.PrestigeHundredths = Math.Clamp(
            promo.PrestigeHundredths + (int)Math.Round(delta * 100, MidpointRounding.AwayFromZero), 100, Math.Max(100, cap));
        var after = OrgSizing.SizeOf(promo);
        if (after == before) return;

        bool up = after > before;
        Living.News.Post(world, date, NewsCategory.Milestone,
            up ? $"{promo.Name} rises to {OrgSizing.Label(after)} level"
               : $"{promo.Name} slips back to {OrgSizing.Label(after)} level",
            $"The promotion's standing changed {context}.", promo.Id);

        if (promo.Id != world.PlayerPromotion) return;
        string unlock = up switch
        {
            true when after == OrgSize.National => " The networks will come calling.",
            true when after == OrgSize.Continental => " The pay-per-view carriers and streaming giants will talk to us now.",
            true when after == OrgSize.International => " The whole map is ours.",
            _ => "",
        };
        Living.Mail.Post(world, date, MailCategory.Milestone, "The Board",
            up ? $"We're a {OrgSizing.Label(after)} promotion now" : $"We've slipped to {OrgSizing.Label(after)}",
            (up ? "The climb is real. " : "The slide costs us. ") + OrgReach.Describe(world, promo) + unlock);
    }

    /// <summary>
    /// The monthly fade: a promotion that hasn't run a show in four months is
    /// yesterday's news, and its prestige starts slipping until it books again.
    /// </summary>
    public static void MonthlyDrift(World world, DateOnly date)
    {
        foreach (var promo in world.Promotions.Values.Where(p => !p.Defunct))
        {
            var last = world.Events.Values
                .Where(e => e.Promotion == promo.Id && e.Status == EventStatus.Completed)
                .Select(e => (DateOnly?)e.Date)
                .DefaultIfEmpty(null)
                .Max();
            if (last is not { } lastShow) continue;                        // pre-first-show runway
            if (date.DayNumber - lastShow.DayNumber <= 120) continue;
            if (promo.Prestige.Value <= 10) continue;                      // the floor of obscurity

            Apply(world, promo, -1, date, "after months of silence");
            if (promo.Id == world.PlayerPromotion)
                Living.Mail.Post(world, date, MailCategory.BoardNote, "The Board",
                    "Out of sight, out of mind",
                    $"No show in over four months — the brand is fading. Prestige is at {promo.Prestige.Value} and will keep slipping until we run a card.");
        }
    }

    /// <summary>A promoted card that never happens is an embarrassment the brand pays for.</summary>
    public static void OnCancelled(World world, Promotion promo, Event ev)
    {
        Apply(world, promo, -1, ev.Date, $"after {ev.Name} fell apart");
        promo.Reputation = Math.Max(0, promo.Reputation.Value - 2);
        if (promo.Id == world.PlayerPromotion)
            Living.Mail.Post(world, ev.Date, MailCategory.BoardNote, "The Board",
                $"{ev.Name} is off",
                "The card fell apart and we wore the embarrassment — prestige and reputation both took a hit.");
    }

    /// <summary>Cutting a fighter mid-deal dents how the promotion is regarded.</summary>
    public static void OnRelease(Promotion promo) =>
        promo.Reputation = Math.Max(0, promo.Reputation.Value - 3);

    // The player's post-show accounting: what the night did for the brand, and why.
    private static void MailShowReport(World world, Promotion promo, Event ev,
        double avgRating, double fill, int starPower, double quality, double bar, double delta)
    {
        string verdict = delta switch
        {
            >= 1.5 => "A statement show — the brand jumped.",
            >= 0.25 => "The brand grew tonight.",
            > -0.25 => "Held our name where it is — at our level, the bar keeps rising.",
            > -1.5 => "Below what people expect from us now. That one cost us.",
            _ => "An embarrassment at our level. The brand took a real hit.",
        };
        string housing = fill >= 0.95 ? "a sold-out house" : fill >= 0.75 ? "a healthy house" : "a half-empty room";
        string stars = starPower >= 70 ? " with real star power on top" : "";

        Living.Mail.Post(world, ev.Date, MailCategory.BoardNote, "The Board",
            $"Show report: {ev.Name} — prestige {(delta >= 0 ? "+" : "")}{delta:0.0}",
            $"The card averaged {MatchRating.Stars((int)Math.Round(avgRating))} in {housing}{stars}. " +
            $"Show quality {quality:0} against the bar at our level ({bar:0}). {verdict}",
            aboutEvent: ev.Id);
    }
}
