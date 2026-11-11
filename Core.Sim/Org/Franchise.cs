using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Org;

/// <summary>
/// Franchise fighters — the rare names that carry companies (the Conor effect).
/// Two layers: STAR POWER is a fighter trait (~2%) that makes fame compound —
/// faster popularity growth, super-linear gates above ~60 pop. THE FACE OF THE
/// COMPANY is an org's bet on one fighter: their fame feeds the promotion's
/// standing and what TV/sponsors pay, they demand face money in return, and
/// losing them — poached, retired — is a genuine company crisis. The player
/// picks their face from the fighter profile; AI orgs pick (and replace) theirs
/// monthly. All flavor/money-side: never competitive stats.
/// </summary>
public static class Franchise
{
    // --- Star power: the it-factor ------------------------------------------

    /// <summary>Extra draw at the gate — above ~60 pop a star's pull compounds.</summary>
    public static int DrawBonus(Fighter f) =>
        f.StarPower ? 6 + (int)(Math.Max(0, f.Intangibles.Popularity.Value - 60) * 0.6) : 0;

    /// <summary>The camera loves them: every win grows the name faster.</summary>
    public static int AmplifyPopGain(Fighter f, int baseGain) =>
        f.StarPower ? (int)Math.Ceiling(baseGain * 1.6) : baseGain;

    // --- The face of the company ---------------------------------------------

    /// <summary>The org's face, if they're still actually on the roster and active.</summary>
    public static Fighter? FaceOf(World world, Promotion p) =>
        p.FaceOfCompany is { } id && world.Get(id) is { Status: FighterStatus.Active } f
        && f.Contract?.Promotion == p.Id ? f : null;

    /// <summary>
    /// The face's fame rubs off on every show — a bankable name IS part of the
    /// product (up to ~+8 show quality at max pop, on par with a funded campaign).
    /// </summary>
    public static double StandingQualityBonus(World world, Promotion p) =>
        FaceOf(world, p) is { } f ? Math.Min(8.0, f.Intangibles.Popularity.Value / 12.0) : 0;

    /// <summary>
    /// Partners price the face in: TV and sponsors pay more for the org that has
    /// the name everyone tunes in for (up to ~+20% at max pop).
    /// </summary>
    public static decimal PartnerFactor(World world, Promotion p) =>
        FaceOf(world, p) is { } f ? 1m + f.Intangibles.Popularity.Value * 0.002m : 1m;

    /// <summary>Is this fighter the face of the org they're signed to?</summary>
    public static bool IsFace(World world, Fighter f) =>
        f.Contract is { } c && world.Get(c.Promotion)?.FaceOfCompany == f.Id;

    /// <summary>Crown the face (player action; the AI does it monthly on its own).</summary>
    public static void SetFace(World world, Promotion promo, Fighter f, DateOnly date)
    {
        // Taking the posters off a lifer who carried them is a decision he takes
        // personally: the bond breaks (Contracts.Lifers — the door it opens is
        // the normal market's, and a retention bonus or the posters back repair it).
        if (FaceOf(world, promo) is { } old && old.Id != f.Id)
            Contracts.Lifers.BreakByDecision(world, old,
                $"{promo.Abbreviation} took the posters down and built the company around {f.FullName}", date);
        promo.FaceOfCompany = f.Id;
        f.Loyalty = Math.Min(100, f.Loyalty + 6);   // being THE guy means something
        Living.News.Post(world, date, NewsCategory.Signing,
            $"{promo.Abbreviation} builds around {f.FullName}",
            "The face of the company — the marketing, the posters, the main events run through them now.", promo.Id, f.Id);
    }

    /// <summary>
    /// Monthly: catch a lost face (retired, poached, walked) and let AI orgs
    /// crown one. Losing the face hurts through what STOPS: the standing-quality
    /// bonus their fame fed every show, and the partner premium at renewal — on
    /// top of losing the fighter themselves. That's the whole bill; there's no
    /// extra prestige fine stacked on it (losing them is already the blow).
    /// </summary>
    public static void RunMonthly(World world, DateOnly date)
    {
        foreach (var promo in world.Promotions.Values.Where(p => !p.Defunct))
        {
            // The face is gone — the fallout is economic, the coverage is loud.
            if (promo.FaceOfCompany is { } id && FaceOf(world, promo) is null)
            {
                promo.FaceOfCompany = null;
                var gone = world.Get(id);
                if (gone is not null)
                {
                    Living.News.Post(world, date, NewsCategory.Signing,
                        $"{promo.Abbreviation} loses its face: {gone.FullName}",
                        "The name the brand was built around is gone — the posters come down.", promo.Id, gone.Id);
                    if (promo.Id == world.PlayerPromotion)
                        Living.Mail.Post(world, date, MailCategory.BoardNote, Living.Mail.Owner(world),
                            $"We built this around {gone.FullName}",
                            "Our face is gone. The shows lose the shine their name gave us, and partners will reprice at renewal. "
                            + "Rebuild around someone: pick a new face from a fighter's profile.",
                            aboutFighter: gone.Id);
                }
            }

            // AI orgs keep a face on the wall whenever a bankable name exists.
            if (promo.Id == world.PlayerPromotion || promo.FaceOfCompany is not null) continue;
            var candidate = world.RosterOf(promo.Id)
                .Where(f => f.Status == FighterStatus.Active && f.Intangibles.Popularity.Value >= 55)
                .OrderByDescending(f => f.Intangibles.Popularity.Value + (f.StarPower ? 15 : 0))
                .FirstOrDefault();
            if (candidate is not null) SetFace(world, promo, candidate, date);
        }
    }
}
