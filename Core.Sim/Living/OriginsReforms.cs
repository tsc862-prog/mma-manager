using MmaManager.Core.Model;
using MmaManager.Core.Sim.Booking;
using MmaManager.Core.Sim.Generation.Origins;

namespace MmaManager.Core.Sim.Living;

/// <summary>A change of house rules a promotion can make now (Origins): the chapter it belongs to and what it does, in words.</summary>
public sealed record ReformOption(string Chapter, string Label, string Detail);

/// <summary>
/// The rule reforms of an Origins world: a house trades the single long round for timed rounds and judges,
/// puts every man in gloves, and splits its open division by weight, one line at a time (heavy and light
/// first, the full ladder over the years). Each reform is open once its chapter is (live or fired); the
/// first house to make it IS the chapter for rounds and gloves, and the first champion crowned in a new
/// class is the chapter for a weight line (<see cref="OriginsTimeline"/>). AI houses decide on a keyed
/// monthly roll (pressed harder when the Backlash has closed their home); the player decides through
/// <see cref="Options"/> and <see cref="Adopt"/>, and the AI never decides for him.
///
/// A split keeps every man in his natural class: divisions become BANDS (<see cref="Division.Lightest"/>),
/// the lightest band taking everyone under the first line (the light division is "middleweight" at first).
/// The open-weight belt is retired honestly (its last reign ends vacated and the news says so), every line
/// gets a vacant belt, and a champion a new line leaves outside his belt's band gives it up and starts at
/// the top of the band he now belongs to.
/// </summary>
public static class OriginsReforms
{
    public const string Rounds = "rounds-and-judges";
    public const string Gloves = "gloves-adopted";

    /// <summary>The weight-line chapters, in the order a house adopts them.</summary>
    public static readonly string[] Ladder =
        { "first-weight-split", "weight-ladder-middle", "weight-ladder-lower", "weight-ladder-bantam", "weight-ladder-flyweight" };

    private const ulong AdoptSalt = 0x0219_AD07_0003UL;

    /// <summary>How much likelier a house the Backlash has closed at home is to adopt the rules the critics ask for, a month.</summary>
    public const double BannedPressure = 0.10;

    /// <summary>The men's classes a weight-line chapter draws (strawweight is the women's: never a men's line).</summary>
    public static IReadOnlyList<WeightClass> LinesOf(string chapter) =>
        OriginsChapters.ByKey(chapter).Unlocks
            .Where(u => u.StartsWith("WeightClass:", StringComparison.Ordinal))
            .Select(u => Enum.Parse<WeightClass>(u["WeightClass:".Length..]))
            .Where(wc => wc != WeightClass.Strawweight)
            .OrderBy(wc => wc).ToList();

    /// <summary>Does the house fight timed rounds with judges at the limit (today's rules count)?</summary>
    public static bool HasRounds(Promotion p) =>
        p.Rules.Fight is not { } r || ((r.Rounds is null || r.Rounds > 1) && r.Judges);

    /// <summary>Does the house put every man in gloves (today's rules count)?</summary>
    public static bool HasGloves(Promotion p) =>
        p.Rules.Fight is not { } r || r.Gloves is GloveType.Modern or GloveType.ThinOpenFinger;

    /// <summary>Has the house drawn every line this weight chapter draws?</summary>
    public static bool HasLines(Promotion p, string chapter) =>
        LinesOf(chapter).All(wc => p.Divisions.Any(d => !d.Open && d.Sex == Sex.Male && d.WeightClass == wc));

    /// <summary>
    /// A house that crowns champions: a card of single bouts, or a bracket house (which gives up its
    /// brackets for cards when it splits by weight, as the bracket houses did). A gym challenge never splits.
    /// </summary>
    private static bool BeltHouse(Promotion p) =>
        p.Rules.ShowFormat is ShowFormat.Card or ShowFormat.OneNightTournament
        && p.Divisions.Any(d => d.Sex == Sex.Male && d.Title is not null);

    /// <summary>The reforms this house can make today, in the order a house makes them (empty in today's mode).</summary>
    public static IReadOnlyList<ReformOption> Options(World world, Promotion p, DateOnly date)
    {
        var list = new List<ReformOption>();
        if (world.Mode != WorldMode.Origins || p.Defunct) return list;
        if (!HasRounds(p) && OriginsTimeline.IsOpen(world, Rounds, date))
            list.Add(new(Rounds, "Rounds and judges", "Five-minute rounds with a minute's rest; three judges score a fight that goes the distance."));
        if (!HasGloves(p) && OriginsTimeline.IsOpen(world, Gloves, date))
            list.Add(new(Gloves, "Gloves for everyone", "Every man wears the same thin, open-fingered glove."));
        if (BeltHouse(p) && HasRounds(p) && HasGloves(p))
            foreach (var chapter in Ladder)
            {
                if (HasLines(p, chapter)) continue;
                if (OriginsTimeline.IsOpen(world, chapter, date)
                    && OriginsChapters.ByKey(chapter).Requires.Where(r => Ladder.Contains(r)).All(r => HasLines(p, r)))
                    list.Add(new(chapter, LineLabel(chapter), LineDetail(chapter)));
                break;   // one line at a time
            }
        return list;
    }

    private static string LineLabel(string chapter) => chapter == Ladder[0]
        ? "Split heavy and light"
        : "Add " + string.Join(" and ", LinesOf(chapter).Select(Name)) + " divisions";

    private static string LineDetail(string chapter) => chapter == Ladder[0]
        ? "The open-weight belt is retired (and a bracket house runs cards from now on). Men over two hundred pounds fight at heavyweight, everyone else at middleweight; each division gets a vacant belt."
        : "Each new division gets a vacant belt; men move to the division their natural weight puts them in.";

    /// <summary>
    /// Make a reform at this house now. False when it is not one of <see cref="Options"/> today. Rounds and
    /// gloves fire their chapter when it is live (this house was first); a weight line fires its chapter
    /// when a champion is crowned in it.
    /// </summary>
    public static bool Adopt(World world, Promotion p, string chapter, DateOnly date)
    {
        if (!Options(world, p, date).Any(o => o.Chapter == chapter)) return false;
        var rules = p.Rules.Fight ?? FightRules.Modern;
        switch (chapter)
        {
            case Rounds:
            {
                int rounds = p.Rules.ShowFormat == ShowFormat.OneNightTournament ? 2 : 3;   // a bracket's man fights three times a night
                p.Rules.Fight = rules with { Rounds = rounds, RoundMinutes = 5, Judges = true };
                p.Rules.StandardRounds = rounds;
                p.Rules.TitleRounds = rounds;
                p.Rules.RoundMinutes = 5;
                News.Post(world, date, NewsCategory.General, $"{p.Name} adopts rounds and judges",
                    $"From the next night, {p.Abbreviation} fights {rounds} rounds of five minutes with a minute's rest, and three judges score any fight that reaches the limit.", p.Id);
                OriginsTimeline.FireNow(world, Rounds, new ChapterDoer(p.Id, City: p.HomeCity), date);
                break;
            }
            case Gloves:
                p.Rules.Fight = rules with { Gloves = GloveType.ThinOpenFinger };
                News.Post(world, date, NewsCategory.General, $"{p.Name} makes gloves compulsory",
                    $"Every man on a {p.Abbreviation} card will wear the same thin, open-fingered glove.", p.Id);
                OriginsTimeline.FireNow(world, Gloves, new ChapterDoer(p.Id, City: p.HomeCity), date);
                break;
            default:
                if (p.Rules.ShowFormat == ShowFormat.OneNightTournament)
                {
                    // Eight men of every size in one night was the open-weight idea; weight classes end it.
                    // (A bracket already on the books is the house's last: it runs as announced, with no belt.)
                    p.Rules.ShowFormat = ShowFormat.Card;
                    News.Post(world, date, NewsCategory.General, $"{p.Name} ends its one-night brackets",
                        $"No more eight-man nights: {p.Abbreviation} will run cards of single bouts, a champion in each weight class.", p.Id);
                }
                DrawLines(world, p, LinesOf(chapter), date);
                break;
        }
        return true;
    }

    /// <summary>The month's AI reforms: each AI house weighs its open reforms on a keyed roll (one reform a month at most).</summary>
    public static void RunMonthly(World world, DateOnly date)
    {
        int month = OriginsTimeline.Month(world, date);
        foreach (var p in world.Promotions.Values.Where(p => !p.Defunct && p.Id != world.PlayerPromotion).OrderBy(p => p.Id.Value).ToList())
        {
            foreach (var option in Options(world, p, date))
            {
                if (OriginsTimeline.Roll(world, AdoptSalt, (ulong)p.Id.Value, (ulong)month, OriginsTimeline.KeyHash(option.Chapter)) >= Chance(world, p, option.Chapter, date))
                    continue;
                Adopt(world, p, option.Chapter, date);
                break;
            }
        }
    }

    /// <summary>
    /// A house's monthly chance of making a reform: a pioneer's 2.5% while nobody has (rising to 5.5% as the
    /// window closes), 6% once the sport has; and the rules the critics ask for are 10% likelier for a house
    /// the Backlash has closed at home.
    /// </summary>
    public static double Chance(World world, Promotion p, string chapter, DateOnly date)
    {
        var c = OriginsChapters.ByKey(chapter);
        int month = OriginsTimeline.Month(world, date);
        double progress = Math.Clamp((month - c.EarliestMonth) / (double)Math.Max(1, c.LatestMonth - c.EarliestMonth), 0, 1);
        double chance = world.Era.HasFired(chapter) ? 0.06 : 0.025 + 0.03 * progress;
        if ((chapter == Rounds || chapter == Gloves) && p.HomeCity is { } hc && Era.Banned(world, hc, date)) chance += BannedPressure;
        return chance;
    }

    // ------------------------------------------------------------------ the weight lines

    /// <summary>
    /// Draw new weight lines at a house: the open division (if any) gives way, every line gets a division and
    /// a vacant belt, the bands are re-cut, and champions left outside their band give their belts up.
    /// </summary>
    internal static void DrawLines(World world, Promotion p, IReadOnlyList<WeightClass> add, DateOnly date, bool announce = true)
    {
        var enterAtTop = new List<FighterId>();
        if (p.Divisions.FirstOrDefault(d => d.Open && d.Sex == Sex.Male) is { } open)
        {
            if (open.Title is { } tid && world.Get(tid) is { } belt)
            {
                var reign = belt.Lineage.LastOrDefault(r => r.LostOn is null);
                var last = belt.CurrentChampion is { } cid ? world.Get(cid) : null;
                if (reign is not null) { reign.LostOn = date; reign.Vacated = true; }
                belt.CurrentChampion = null;
                if (last is not null) enterAtTop.Add(last.Id);
                News.Post(world, date, NewsCategory.TitleChange, $"{p.Abbreviation} retires its open-weight belt",
                    last is null
                        ? $"{p.Name} splits by weight; the {belt.Name}, never won, is retired with it."
                        : $"{p.Name} splits by weight, and the {belt.Name} is retired with {last.FullName} as its last holder. He will start at the top of his new division.",
                    p.Id, last?.Id);
            }
            p.Divisions.Remove(open);
        }

        foreach (var wc in add.OrderBy(w => w))
        {
            if (p.Divisions.Any(d => !d.Open && d.Sex == Sex.Male && d.WeightClass == wc)) continue;
            var title = new Title
            {
                Id = world.NewTitleId(), Promotion = p.Id, WeightClass = wc,
                Name = $"{p.Abbreviation} {Name(wc)} Championship", Prestige = p.Prestige.Value,
            };
            world.Titles[title.Id] = title;
            p.Titles.Add(title.Id);
            p.Divisions.Add(new Division { WeightClass = wc, Sex = Sex.Male, Title = title.Id });
        }

        // Re-cut the bands: each line takes every class above the line below it.
        var lines = p.Divisions.Where(d => !d.Open && d.Sex == Sex.Male).OrderBy(d => d.WeightClass).ToList();
        WeightClass? below = null;
        foreach (var d in lines)
        {
            var lightest = below is { } b ? b + 1 : WeightClass.Strawweight;
            d.Lightest = lightest == d.WeightClass ? null : lightest;
            below = d.WeightClass;
        }
        // Keep the divisions in weight order (heaviest last), as a card is read.
        var others = p.Divisions.Where(d => d.Open || d.Sex != Sex.Male).ToList();
        p.Divisions.Clear();
        p.Divisions.AddRange(lines);
        p.Divisions.AddRange(others);

        // A champion the new line leaves outside his belt's band gives it up.
        foreach (var d in lines)
        {
            if (d.Title is not { } tid || world.Get(tid) is not { } belt || belt.CurrentChampion is not { } cid || world.Get(cid) is not { } champ) continue;
            if (d.Matches(champ)) continue;
            var reign = belt.Lineage.LastOrDefault(r => r.LostOn is null);
            if (reign is not null) { reign.LostOn = date; reign.Vacated = true; }
            belt.CurrentChampion = null;
            enterAtTop.Add(champ.Id);
            var home = lines.FirstOrDefault(x => x.Matches(champ));
            News.Post(world, date, NewsCategory.TitleChange, $"{champ.FullName} gives up the {belt.Name}",
                $"A natural {Name(champ.WeightClass).ToLowerInvariant()}, {champ.LastName} now belongs in {p.Abbreviation}'s {(home is null ? "lighter" : Name(home.WeightClass).ToLowerInvariant())} division. The belt is vacant; he starts at the top of his new division.",
                p.Id, champ.Id);
        }

        // Bouts already on the books that the new lines put across a division (or on a retired belt) come off
        // the card; the house tops its cards up from the right divisions before the night.
        foreach (var ev in world.Events.Values.Where(e => e.Promotion == p.Id && e.Status == EventStatus.Scheduled && e.Format == EventFormat.Card)
                     .OrderBy(e => e.Date).ThenBy(e => e.Id.Value))
            ev.Card.RemoveAll(f => world.Get(f.RedCorner) is not { } a || world.Get(f.BlueCorner) is not { } b
                || !lines.Any(d => d.Matches(a) && d.Matches(b) && (f.Title is null || d.Title == f.Title)));

        RankingService.RecomputePromotion(world, p, date);
        foreach (var id in enterAtTop)
            if (world.Get(id) is { Status: FighterStatus.Active } f && f.Contract?.Promotion == p.Id
                && p.Divisions.FirstOrDefault(d => d.Matches(f)) is { } div)
                RankingService.EnterAtTop(div, f.Id);

        if (!announce) return;
        News.Post(world, date, NewsCategory.General,
            $"{p.Name} draws {(add.Count == 1 ? "a new weight line" : "new weight lines")}: {string.Join(" and ", add.Select(Name).Select(s => s.ToLowerInvariant()))}",
            $"{p.Abbreviation} now runs {string.Join(", ", lines.Select(d => Name(d.WeightClass).ToLowerInvariant()))}, each with a vacant belt. Every man fights in the division his natural weight puts him in.",
            p.Id);
    }

    /// <summary>A class in words ("Light Heavyweight").</summary>
    public static string Name(WeightClass wc) => wc switch
    {
        WeightClass.LightHeavyweight => "Light Heavyweight",
        _ => wc.ToString(),
    };
}
