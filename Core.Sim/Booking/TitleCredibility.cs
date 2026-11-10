using MmaManager.Core.Model;

namespace MmaManager.Core.Sim.Booking;

/// <summary>
/// How credible a fighter is as a TITLE contestant, in the eyes of the public —
/// built ONLY from public evidence (record, win streak, ranking within the
/// company, fame), never true ability: a belt's name is what the audience can
/// see, and the audience doesn't get past the Fog of War either.
///
/// The same résumé reads differently by STAGE: a 3-0 prospect challenging for
/// a city-hall strap is a hot story that lifts the belt, but the same 3-0 in an
/// international title fight is an insult that tanks it — the bigger the belt,
/// the deeper the résumé it demands. The one exception is the rare CROSSOVER:
/// a famous enough name is its own credential and sells the belt instead of
/// cheapening it.
/// </summary>
public static class TitleCredibility
{
    /// <summary>A fighter's title credibility (0–100) for a belt at this promotion.</summary>
    public static double Of(World world, Promotion promo, Division? division, Title title, Fighter f)
    {
        // What "proven" means at this stage: the deeper résumé a bigger belt demands.
        double provenBouts = Org.OrgSizing.SizeOf(promo) switch
        {
            OrgSize.International => 18,
            OrgSize.Continental => 14,
            OrgSize.National => 10,
            OrgSize.Regional => 7,
            _ => 5,   // City
        };

        int decisive = f.Record.Wins + f.Record.Losses;
        double experience = Math.Min(1.0, decisive / provenBouts);
        double winPct = decisive == 0 ? 0.5 : f.Record.Wins / (double)decisive;
        double streak = Math.Clamp(f.Record.CurrentStreak, 0, 8) / 8.0;

        // Standing in the company: the champion and the top of the contender
        // list are what a title fight is SUPPOSED to look like; an unranked
        // body in a title fight reads as a paper matchup.
        bool champion = title.CurrentChampion == f.Id;
        int rank = division?.Rankings.IndexOf(f.Id) ?? -1;
        double standing = champion ? 1.0 : rank >= 0 ? Math.Max(0.2, 1.0 - rank * 0.1) : 0.0;

        // Results only count once they're PROVEN at this stage's depth — an
        // unbeaten record three fights long impresses a city crowd, not a
        // world audience.
        double cred = 55 * winPct * (0.35 + 0.65 * experience)
                    + 20 * experience
                    + 15 * standing
                    + 10 * streak;

        // The crossover exception: a big enough name is its own credential —
        // fame sells the belt in lieu of a résumé.
        return Math.Clamp(Math.Max(cred, f.Intangibles.Popularity.Value * 0.9), 0, 100);
    }

    /// <summary>The credibility of a whole title matchup — what this booking says about the belt.</summary>
    public static double Contest(World world, Promotion promo, Division? division, Title title, Fighter a, Fighter b) =>
        (Of(world, promo, division, title, a) + Of(world, promo, division, title, b)) / 2.0;
}
