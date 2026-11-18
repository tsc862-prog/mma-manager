using System.Text;
using System.Text.RegularExpressions;
using MmaManager.Core.Model;
using MmaManager.Core.Sim.Ability;
using MmaManager.Core.Sim.Org;
using MmaManager.Core.Sim.Scouting;

namespace MmaManager.Core.Sim.Flavor;

/// <summary>
/// Writes a short scouting-report bio for a fighter or a promotion, built
/// deterministically from what the sim already knows — style, standout skills,
/// weaknesses, career arc and form for fighters; size, prestige, reputation and
/// champions for promotions. Same entity, same words, so bios stay stable while
/// still reading like prose rather than a stat dump.
/// </summary>
public static partial class Biographer
{
    // --- Fighters -----------------------------------------------------------
    /// <summary>
    /// The profile paragraph. Generated prose may only say what the player has SEEN: a strength or
    /// weakness is named only when <paramref name="scout"/> (the player's scouted read of this man)
    /// shows that stat at least Approximate, in the band the profile bar shows; with no report (null)
    /// nothing is revealed and the prose runs on public facts alone (record, how the wins came, style,
    /// camp, age, streak). Authored <see cref="Fighter.Bio"/> text is the writer's voice and is untouched.
    /// Text only: no RNG; word choice is keyed by hash of the fighter's id.
    /// </summary>
    public static string Fighter(World world, Fighter f, ScoutReport? scout = null)
    {
        var sb = new StringBuilder();
        int age = f.AgeOn(world.CurrentDate);

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

            // How he fights: public style first, then only what the scout's read has revealed.
            var strengths = Strengths(f, scout);
            string arena = ArenaPhrase(f);
            sb.Append(strengths.Count > 0
                ? $"They shine {arena}, with {Join(strengths)}. "
                : $"They look most at home {arena}. ");

            // A weakness, only if it has been seen.
            var flaw = Weakness(f, scout);
            if (flaw is not null) sb.Append($"The knock is {flaw}. ");

            // What the record shows: how the wins and losses have come (public).
            if (PublicRead(f) is { Length: > 0 } read) sb.Append(read).Append(' ');
        }

        // Who they are away from the cage — the part a matchmaker asks about
        // second and a promoter asks about first.
        sb.Append(Generation.Personality.Note(f.Disposition)).Append(' ');

        // Crossover origin — a famous outsider's past life IS the story, so it
        // leads into the career line (WorldEvents stamps the accolade at debut).
        if (CrossoverOrigin(f) is { } origin)
            sb.Append($"They didn't come up through MMA at all — {origin} before crossing over to the cage, they arrived with a mainstream spotlight most fighters never see. ");

        // Career arc / form.
        sb.Append(CareerLine(world, f, age));

        // Star power.
        if (f.Bio is null && (f.Intangibles.Popularity.Value >= 70 || f.Intangibles.Charisma.Value >= 78))
            sb.Append(" A genuine draw, they move tickets and turn heads on the mic.");

        // The people around them, read off the world as it stands (authored text first, derived after).
        if (PeopleText(world, f) is { Length: > 0 } people) sb.Append(' ').Append(people);

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

    // The arena is the publicly known style, never the hidden striking/grappling split.
    private static string ArenaPhrase(Fighter f) => f.PrimaryStyle switch
    {
        PrimaryStyle.Striker or PrimaryStyle.Kickboxer or PrimaryStyle.Brawler => "on the feet",
        PrimaryStyle.Wrestler or PrimaryStyle.BrazilianJiuJitsu or PrimaryStyle.Grappler => "once the fight hits the mat",
        PrimaryStyle.ClinchFighter => "up against the fence",
        _ => "wherever the fight goes",
    };

    /// <summary>
    /// Reveal threshold: a stat may be named once its channel reads Approximate or better, i.e. the same
    /// point at which the profile bar shows its band. Words come from that band (Elite = 80+, Weak = under 45),
    /// the value the bar displays; a Hidden channel names nothing.
    /// </summary>
    private static StatBand? Seen(ScoutReport? scout, StatChannel ch, int v) =>
        scout is not null && scout.Precision(ch) != StatReveal.Hidden ? ScoutReport.Band(v) : null;

    private static List<string> Strengths(Fighter f, ScoutReport? scout)
    {
        var pool = new List<(StatChannel ch, int v, string phrase)>
        {
            (StatChannel.Power, f.Striking.PunchPower, "one-punch knockout power"),
            (StatChannel.Chin, f.Physical.Chin, "a granite chin"),
            (StatChannel.Cardio, f.Physical.Cardio, "a bottomless gas tank"),
            (StatChannel.Athletic, f.Physical.Speed, "blinding hand speed"),
            (StatChannel.Submission, f.Grappling.SubmissionOffense, "a dangerous submission game"),
            (StatChannel.Wrestling, f.Grappling.TakedownOffense, "a bruising takedown game"),
            (StatChannel.WrestlingDef, f.Grappling.TakedownDefense, "takedown defense that's tough to crack"),
            (StatChannel.StrikingDef, f.Striking.StrikingDefense, "slippery, hard-to-hit defense"),
            (StatChannel.Mind, f.Mental.FightIQ, "a razor-sharp fight IQ"),
            (StatChannel.Chin, f.Mental.Heart, "a bottomless well of heart"),   // the profile reads Heart off the Chin channel
        };
        return pool.Where(p => Seen(scout, p.ch, p.v) == StatBand.Elite)
            .OrderByDescending(p => p.v).Take(2).Select(p => p.phrase).ToList();
    }

    private static string? Weakness(Fighter f, ScoutReport? scout)
    {
        var pool = new List<(StatChannel ch, int v, string phrase)>
        {
            (StatChannel.Chin, f.Physical.Chin, "a suspect chin"),
            (StatChannel.Cardio, f.Physical.Cardio, "cardio that fades in the championship rounds"),
            (StatChannel.StrikingDef, f.Striking.StrikingDefense, "a leaky guard on the feet"),
            (StatChannel.WrestlingDef, f.Grappling.TakedownDefense, "a real vulnerability to the takedown"),
            (StatChannel.SubmissionDef, f.Grappling.SubmissionDefense, "a shaky submission defense"),
        };
        return pool.Where(p => Seen(scout, p.ch, p.v) == StatBand.Weak)
            .OrderBy(p => p.v).Select(p => p.phrase).FirstOrDefault();
    }

    /// <summary>
    /// What anyone can read off the card: bouts, how the wins came, the shape of the record.
    /// Wording varies by a hash of the fighter's id (stable, no RNG).
    /// </summary>
    private static string PublicRead(Fighter f)
    {
        var r = f.Record;
        int total = r.TotalBouts, wins = r.Wins;
        uint h = Hash(f.Id.Value, 0x51EE);
        if (total == 0)
            return Variant(h, new[]
            {
                "There is nothing on tape yet: no pro bouts, so the style is all anyone has to go on.",
                "Still waiting on a first pro night, so all that is known is the label and the gym.",
                "Unproven as a pro, a name on a roster sheet until the first bell.",
            });

        string method;
        if (wins < 3)
            method = Variant(h, new[]
            {
                $"At {r} the book is still thin, too few bouts to say much about how they win.",
                $"Only {total} pro {(total == 1 ? "bout" : "bouts")} so far, not enough to pin a pattern on.",
                $"A {r} record is early days, and the way they win is still an open question.",
            });
        else if (r.WinsByKo * 2 >= wins)
            method = Variant(h, new[]
            {
                $"{r.WinsByKo} of their {wins} wins have ended in a knockout, so a night with them rarely goes the distance.",
                $"They finish on the feet: {r.WinsByKo} {Plural(r.WinsByKo, "knockout")} in {wins} wins.",
                $"{r.WinsByKo} {Plural(r.WinsByKo, "knockout")} in {wins} wins tells the room how most of their nights end.",
            });
        else if (r.WinsBySubmission * 2 >= wins)
            method = Variant(h, new[]
            {
                $"{r.WinsBySubmission} of their {wins} wins have come by submission, and opponents know where it is headed.",
                $"They hunt the tap: {r.WinsBySubmission} {Plural(r.WinsBySubmission, "submission")} in {wins} wins.",
                $"{r.WinsBySubmission} submission finishes in {wins} wins makes the ground their office.",
            });
        else if (r.WinsByDecision * 5 >= wins * 3)
            method = Variant(h, new[]
            {
                $"{r.WinsByDecision} of their {wins} wins went to the scorecards: they win the long way round.",
                $"A points fighter by record, with {r.WinsByDecision} {Plural(r.WinsByDecision, "decision")} in {wins} wins.",
                $"{r.WinsByDecision} of {wins} wins came on the judges' cards, a patient streak that shows in the book.",
            });
        else
            method = Variant(h, new[]
            {
                $"The wins come every way: {r.WinsByKo} by knockout, {r.WinsBySubmission} by submission, {r.WinsByDecision} on the cards.",
                $"No single pattern in the {wins} wins, with {r.WinsByKo} {Plural(r.WinsByKo, "knockout")}, {r.WinsBySubmission} {Plural(r.WinsBySubmission, "submission")} and {r.WinsByDecision} {Plural(r.WinsByDecision, "decision")}.",
                $"A mixed book: {r.WinsByKo} {Plural(r.WinsByKo, "knockout")}, {r.WinsBySubmission} {Plural(r.WinsBySubmission, "submission")}, {r.WinsByDecision} {Plural(r.WinsByDecision, "decision")}.",
            });

        string shape = "";
        if (r.Losses == 0 && wins >= 4)
            shape = Variant(h >> 3, new[] { "Nobody has beaten them as a pro.", "They have yet to taste a loss.", "The loss column is still empty." });
        else if (r.Losses >= 4 && r.Losses * 2 >= wins)
            shape = Variant(h >> 3, new[] { "They have been in with plenty of tough opposition, and the losses show it.", "The record has taken its share of hits." });
        return $"{method} {shape}".Trim();
    }

    private static string Plural(int n, string noun) => n == 1 ? noun : noun + "s";

    private static string Variant(uint h, string[] lines) => lines[(int)(h % (uint)lines.Length)];

    private static uint Hash(int id, uint salt)
    {
        unchecked
        {
            uint h = (uint)id * 2246822519u ^ (salt * 0x85EBCA6Bu);
            h ^= h >> 13; h *= 0x27D4EB2Fu;
            h ^= h >> 16;
            return h;
        }
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

    private static string CareerLine(World world, Fighter f, int age)
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
        else if (age <= 24 && f.Record.Wins >= 3 && f.Record.Losses <= 1)
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
        int classes = promo.Divisions.Count(d => !d.Open);
        string spread = promo.Divisions.Count > 0 && classes == 0 ? "at open weight"   // Origins: any man may face any man
            : classes == 1 ? "in 1 weight class"
            : $"across {classes} weight classes";
        sb.Append($"it fields {roster} fighters {spread}. ");

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
            // What the owner is after, in words (authored; text only).
            if (OwnerAgendas.PageLine(promo, world.Mode) is { Length: > 0 } agenda) sb.Append(agenda).Append(' ');
        }

        // Flagship champion.
        var champ = promo.Divisions
            .Where(d => d.Title is { } tid && world.Get(tid)?.CurrentChampion is not null)
            .Select(d => world.Get(world.Get(d.Title!.Value)!.CurrentChampion!.Value))
            .Where(f => f is not null)
            .OrderByDescending(f => FighterAbility.CurrentAbility(f!))
            .FirstOrDefault();
        // The face of the company, when the house has one, IS its marquee attraction.
        var marquee = Franchise.FaceOf(world, promo) ?? champ;
        if (marquee is not null)
            sb.Append($"Its marquee attraction is {marquee.FullName}. ");

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
