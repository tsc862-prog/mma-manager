using System.Text;
using System.Text.RegularExpressions;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Org;

namespace MmaManager.Core.Sim.Flavor;

/// <summary>
/// Writes a short scouting-report bio for a fighter or a promotion, built
/// deterministically from what the sim already knows — style, standout skills,
/// weaknesses, career arc and form for fighters; size, prestige, reputation and
/// champions for promotions. Same entity, same words, so bios stay stable while
/// still reading like prose rather than a stat dump.
/// </summary>
public static class Biographer
{
    // --- Fighters -----------------------------------------------------------
    public static string Fighter(World world, Fighter f)
    {
        var sb = new StringBuilder();
        int age = f.AgeOn(world.CurrentDate);
        int ability = FighterAbility.CurrentAbility(f);

        if (f.Bio is { Length: > 0 } authored)
        {
            // An authored backstory replaces the derived character sketch; the
            // live career/form line still follows so the story never goes stale.
            sb.Append(authored.TrimEnd());
            if (!authored.TrimEnd().EndsWith('.')) sb.Append('.');
            sb.Append(' ');
        }
        else
        {
            // Opening line: who they are and where they're from.
            string from = world.Get(f.Country)?.Name ?? Home(f.Nationality);
            sb.Append($"{f.FullName} is a {age}-year-old {WeightClassPhrase(f.WeightClass)} out of {from}, ")
              .Append($"{StylePhrase(f.PrimaryStyle, f)}. ");

            // Where they do damage + standout tools.
            var strengths = Strengths(f);
            string arena = ArenaPhrase(f);
            sb.Append(strengths.Count > 0
                ? $"They shine {arena}, with {Join(strengths)}. "
                : $"They operate best {arena}. ");

            // A weakness, if there's a glaring one.
            var flaw = Weakness(f);
            if (flaw is not null) sb.Append($"The knock is {flaw}. ");
        }

        // Who they are away from the cage — the part a matchmaker asks about
        // second and a promoter asks about first.
        sb.Append(Generation.Personality.Note(f.Disposition)).Append(' ');

        // Crossover origin — a famous outsider's past life IS the story, so it
        // leads into the career line (WorldEvents stamps the accolade at debut).
        if (CrossoverOrigin(f) is { } origin)
            sb.Append($"They didn't come up through MMA at all — {origin} before crossing over to the cage, they arrived with a mainstream spotlight most fighters never see. ");

        // Career arc / form.
        sb.Append(CareerLine(world, f, age, ability));

        // Star power.
        if (f.Bio is null && (f.Intangibles.Popularity.Value >= 70 || f.Intangibles.Charisma.Value >= 78))
            sb.Append(" A genuine draw, they move tickets and turn heads on the mic.");

        return sb.ToString().Trim();
    }

    private static string StylePhrase(PrimaryStyle style, Fighter f) => style switch
    {
        PrimaryStyle.Striker => "a sharpshooting striker",
        PrimaryStyle.Kickboxer => "a punishing kickboxer",
        PrimaryStyle.Wrestler => "a relentless wrestler",
        PrimaryStyle.BrazilianJiuJitsu => "a slick jiu-jitsu artist",
        PrimaryStyle.Grappler => "a suffocating grappler",
        PrimaryStyle.Brawler => "a come-forward brawler",
        PrimaryStyle.ClinchFighter => "a grinding clinch fighter",
        _ => "a well-rounded mixed martial artist",
    };

    private static string ArenaPhrase(Fighter f)
    {
        int striking = Avg(f.Striking.PunchPower, f.Striking.PunchTechnique, f.Striking.StrikingDefense, f.Striking.Footwork, f.Striking.KickTechnique);
        int grappling = Avg(f.Grappling.TakedownOffense, f.Grappling.TakedownDefense, f.Grappling.TopControl, f.Grappling.SubmissionOffense, f.Grappling.Scrambling);
        if (striking >= grappling + 6) return "on the feet";
        if (grappling >= striking + 6) return "once the fight hits the mat";
        return "wherever the fight goes";
    }

    private static List<string> Strengths(Fighter f)
    {
        var pool = new List<(int v, string phrase)>
        {
            (f.Striking.PunchPower, "one-punch knockout power"),
            (f.Physical.Chin, "a granite chin"),
            (f.Physical.Cardio, "a bottomless gas tank"),
            (f.Physical.Speed, "blinding hand speed"),
            (f.Grappling.SubmissionOffense, "a dangerous submission game"),
            (f.Grappling.TakedownOffense, "a bruising takedown game"),
            (f.Grappling.TakedownDefense, "takedown defense that's tough to crack"),
            (f.Striking.StrikingDefense, "slippery, hard-to-hit defense"),
            (f.Mental.FightIQ, "a razor-sharp fight IQ"),
            (f.Mental.Heart, "a bottomless well of heart"),
        };
        return pool.Where(p => p.v >= 80).OrderByDescending(p => p.v).Take(2).Select(p => p.phrase).ToList();
    }

    private static string? Weakness(Fighter f)
    {
        var pool = new List<(int v, string phrase)>
        {
            (f.Physical.Chin, "a suspect chin"),
            (f.Physical.Cardio, "cardio that fades in the championship rounds"),
            (f.Striking.StrikingDefense, "a leaky guard on the feet"),
            (f.Grappling.TakedownDefense, "a real vulnerability to the takedown"),
            (f.Grappling.SubmissionDefense, "a shaky submission defense"),
        };
        var worst = pool.Where(p => p.v <= 45).OrderBy(p => p.v).FirstOrDefault();
        return worst.phrase is { } ph && worst.v <= 45 ? ph : null;
    }

    /// <summary>
    /// The pre-MMA identity of a crossover star ("a former undisputed boxing
    /// world champion"), read from the accolade WorldEvents stamps at debut.
    /// Null for everyone who came up through the sport.
    /// </summary>
    private static string? CrossoverOrigin(Fighter f)
    {
        const string tag = "MMA crossover — ";
        var title = f.Accolades.Select(a => a.Title)
            .FirstOrDefault(t => t.StartsWith(tag, StringComparison.Ordinal));
        if (title is null) return null;

        string origin = title[tag.Length..].Trim();
        return origin.StartsWith("a ") || origin.StartsWith("an ") || origin.StartsWith("the ")
            ? origin
            : $"a {origin}";
    }

    /// <summary>
    /// How a retired fighter's page closes. Retired people used to fall through to
    /// the "free agent, available to the highest bidder" line, which is not only
    /// wrong but invites the player to bid on a legend. Everything here comes
    /// straight off the data: the room they run, the reigns on the lineages, the
    /// final tally.
    /// </summary>
    private static string RetiredLine(World world, Fighter f)
    {
        var sb = new StringBuilder("Retired");
        var room = world.Camps.Values.FirstOrDefault(c => c.HeadCoach == f.Id);
        if (room is not null) sb.Append($", and now head coach of {room.Name}");
        sb.Append('.');

        var reigns = world.Titles.Values
            .SelectMany(t => t.Lineage.Where(r => r.Champion == f.Id).Select(r => (Title: t, Reign: r)))
            .OrderBy(x => x.Reign.WonOn)
            .ToList();
        foreach (var (title, reign) in reigns)
        {
            string span = reign.LostOn is { } lost
                ? $"{reign.WonOn.Year} to {lost.Year}"
                : $"since {reign.WonOn.Year}";
            string defenses = reign.Defenses switch
            {
                0 => "no defenses",
                1 => "1 defense",
                var n => $"{n} defenses",
            };
            sb.Append($" Held the {title.Name} from {span} ({defenses}).");
        }

        if (f.Record.TotalBouts > 0)
            sb.Append($" Finished {f.Record.Wins}-{f.Record.Losses}.");
        return sb.ToString();
    }

    private static string CareerLine(World world, Fighter f, int age, int ability)
    {
        if (f.Status == FighterStatus.Retired) return RetiredLine(world, f);

        bool champion = world.Titles.Values.Any(t => t.CurrentChampion == f.Id);
        var promo = f.Contract is { } c ? world.Get(c.Promotion) : null;
        int total = f.Record.TotalBouts;
        int streak = f.Record.CurrentStreak;

        var sb = new StringBuilder();
        if (champion)
        {
            var belt = world.Titles.Values.First(t => t.CurrentChampion == f.Id);
            sb.Append($"They currently reign as {promo?.Abbreviation ?? "the"} {WeightClassPhrase(belt.WeightClass)} champion");
        }
        else if (promo is null)
            sb.Append("They are a free agent, available to the highest bidder");
        else if (age <= 24 && f.Record.Losses <= 2 && ability >= 60)
            sb.Append($"A blue-chip prospect on the rise inside {promo.Abbreviation}");
        else if (age >= 35)
            sb.Append($"A battle-tested veteran of {Math.Max(total, 1)} pro bouts fighting under the {promo.Abbreviation} banner");
        else
            sb.Append($"They ply their trade with {promo.Abbreviation}");

        if (streak >= 3) sb.Append($", riding a {streak}-fight winning streak");
        else if (streak <= -3) sb.Append($", looking to snap a {-streak}-fight skid");
        sb.Append('.');
        return sb.ToString();
    }

    // --- Promotions ---------------------------------------------------------
    public static string Promotion(World world, Promotion promo)
    {
        var sb = new StringBuilder();
        if (promo.Lore is { Length: > 0 } authored)
        {
            // Authored house identity leads; the live facts (roster, champion,
            // reputation) still follow so the page reflects the world as it is.
            sb.Append(authored.TrimEnd());
            if (!authored.TrimEnd().EndsWith('.')) sb.Append('.');
            sb.Append(' ');
            sb.Append("Today ");
        }
        else
        {
            string size = OrgSizing.Label(OrgSizing.SizeOf(promo));
            sb.Append($"{promo.Name} ({promo.Abbreviation}) is a {size.ToLowerInvariant()} promotion based in {Home(promo.BaseRegion)}. ");

            int p = promo.Prestige.Value;
            sb.Append(p switch
            {
                >= 70 => "Widely regarded as one of the sport's elite organizations, ",
                >= 50 => "An established player on the scene, ",
                >= 30 => "A solid regional outfit, ",
                _ => "A scrappy up-and-comer still building its name, ",
            });
        }

        int roster = world.RosterOf(promo.Id).Count(f => f.Status != FighterStatus.Retired);
        sb.Append($"it fields {roster} fighters across {promo.Divisions.Count} weight classes. ");

        // Whose house it actually is. A promotion with nobody's name on it is a
        // logo; the owner is who the player is really working for.
        if (promo.OwnerName is { Length: > 0 } owner)
        {
            sb.Append(promo.FoundedYear > 0
                ? $"Founded in {promo.FoundedYear}, it answers to {owner}. "
                : $"It answers to {owner}. ");
            if (promo.OwnerNote is { Length: > 0 } note)
            {
                sb.Append(note.TrimEnd());
                if (!note.TrimEnd().EndsWith('.')) sb.Append('.');
                sb.Append(' ');
            }
        }

        // Flagship champion.
        var champ = promo.Divisions
            .Where(d => d.Title is { } tid && world.Get(tid)?.CurrentChampion is not null)
            .Select(d => world.Get(world.Get(d.Title!.Value)!.CurrentChampion!.Value))
            .Where(f => f is not null)
            .OrderByDescending(f => FighterAbility.CurrentAbility(f!))
            .FirstOrDefault();
        if (champ is not null)
            sb.Append($"Its marquee attraction is {champ.FullName}. ");

        int rep = promo.Reputation.Value;
        if (rep >= 65) sb.Append("It has a name for treating its fighters right");
        else if (rep <= 38) sb.Append("It has a reputation for playing hardball with its roster");
        else sb.Append($"Its shows draw strongest at home in {Home(promo.BaseRegion)}");
        sb.Append('.');

        return sb.ToString();
    }

    // --- Helpers ------------------------------------------------------------
    private static string Home(Region r) => r switch
    {
        Region.NorthAmerica => "North America",
        Region.SouthAmerica => "South America",
        Region.MiddleEast => "the Middle East",
        _ => r.ToString(),
    };

    private static string WeightClassPhrase(WeightClass wc) => Spaced(wc.ToString()).ToLowerInvariant();

    private static int Avg(params int[] xs) => (int)Math.Round(xs.Average());

    private static string Join(IReadOnlyList<string> items) =>
        items.Count == 1 ? items[0] : $"{items[0]} and {items[1]}";

    private static string Spaced(string pascal) => Regex.Replace(pascal, "(?<=.)([A-Z])", " $1");
}
